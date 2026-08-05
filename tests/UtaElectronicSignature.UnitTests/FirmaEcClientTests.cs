using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using UtaElectronicSignature.Contracts;
using UtaElectronicSignature.FirmaEc;

namespace UtaElectronicSignature.UnitTests;

public sealed class FirmaEcClientTests
{
    [Fact]
    public async Task Create_request_uses_verified_decentralized_contract()
    {
        const string token =
            "eyJhbGciOiJIUzI1NiJ9.eyJleHAiOjE5OTk5OTk5OTl9.signature-value";
        var handler = new CapturingHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(token)
            });
        var client = CreateClient(handler);
        var sessionId = Guid.NewGuid();

        var result = await client.CreateSigningRequestAsync(
            new FirmaEcCreateRequest(
                sessionId,
                "1800000000",
                $"firmaec-{sessionId:N}.pdf",
                "%PDF-test"u8.ToArray(),
                "AprobaciÃ³n institucional"),
            CancellationToken.None);

        Assert.Equal(
            "http://firmaec-wildfly:8080/servicio/documentos",
            handler.Request!.RequestUri!.ToString());
        Assert.Equal("test-api-key", handler.Request.Headers.GetValues("X-API-KEY").Single());
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal("1800000000", json.RootElement.GetProperty("cedula").GetString());
        Assert.Equal("UTA-SIGNATURE", json.RootElement.GetProperty("sistema").GetString());
        Assert.Equal(
            Convert.ToBase64String("%PDF-test"u8),
            json.RootElement.GetProperty("documentos")[0].GetProperty("documento").GetString());
        Assert.StartsWith("firmaec://UTA-SIGNATURE/firmar?", result.LaunchUrl);
        Assert.Contains(
            "url=https%3A%2F%2Fportal.uta.edu.ec%2Ffirmaec",
            result.LaunchUrl);
        Assert.DoesNotContain(token, result.TransactionId);
    }

    [Fact]
    public async Task Create_request_rejects_non_pdf_content()
    {
        var client = CreateClient(new CapturingHandler(
            new HttpResponseMessage(HttpStatusCode.OK)));

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => client.CreateSigningRequestAsync(
                new FirmaEcCreateRequest(
                    Guid.NewGuid(),
                    "1800000000",
                    "document.pdf",
                    "not-a-pdf"u8.ToArray(),
                    null),
                CancellationToken.None));

        Assert.Contains("PDF", exception.Message);
    }

    private static FirmaEcClient CreateClient(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler),
            Options.Create(new FirmaEcOptions
            {
                Enabled = true,
                Mode = "DECENTRALIZED",
                ServiceBaseUrl = "http://firmaec-wildfly:8080/servicio/",
                PublicApiBaseUrl = "https://portal.uta.edu.ec/firmaec",
                SystemCode = "UTA-SIGNATURE",
                ApiKey = "test-api-key"
            }));

    private sealed class CapturingHandler(HttpResponseMessage response)
        : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }
}
