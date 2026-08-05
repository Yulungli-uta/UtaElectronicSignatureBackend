using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using UtaElectronicSignature.Application;
using UtaElectronicSignature.Contracts;

namespace UtaElectronicSignature.FirmaEc;

public sealed class FirmaEcOptions
{
    public const string SectionName = "FirmaEc";
    public bool Enabled { get; set; }
    public string Mode { get; set; } = "DECENTRALIZED";
    public string ServiceBaseUrl { get; set; } = "";
    public string PublicApiBaseUrl { get; set; } = "";
    public string ProtocolScheme { get; set; } = "firmaec";
    public string SystemCode { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string CallbackApiKey { get; set; } = "";
    public int CertificateType { get; set; } = 2;
    public int TokenLifetimeMinutes { get; set; } = 5;
    public int TimeoutSeconds { get; set; } = 60;
    public int MaxDocumentBytes { get; set; } = 15_728_640;
    // Sello visual (QR) en el documento: opcional, apagado por defecto para no alterar
    // el flujo de firma invisible ya validado. FirmaEC no ofrece un selector propio de
    // posicion, asi que HrFrontend construye uno (ver SignaturePositionPicker) y manda
    // Llx/Lly elegidos por el usuario en StartSigningRequest; estos valores de aqui son
    // solo el fallback cuando no se manda ninguno. El tamaño del cuadro es siempre el
    // mismo (Width/Height) para que no varie firmante a firmante.
    public bool StampEnabled { get; set; }
    public string StampType { get; set; } = "QR";
    public int StampLlx { get; set; }
    public int StampLly { get; set; }
    public int StampWidth { get; set; } = 100;
    public int StampHeight { get; set; } = 100;
}

public sealed class FirmaEcClient(
    HttpClient http,
    Microsoft.Extensions.Options.IOptions<FirmaEcOptions> options) : IFirmaEcClient
{
    private readonly FirmaEcOptions _options = options.Value;

    public async Task<FirmaEcCreateResult> CreateSigningRequestAsync(
        FirmaEcCreateRequest request,
        CancellationToken ct)
    {
        EnsureConfigured();
        ValidateDocument(request.Document);

        var endpoint = new Uri(
            EnsureTrailingSlash(_options.ServiceBaseUrl),
            "documentos");
        var payload = new
        {
            cedula = request.Identification,
            sistema = _options.SystemCode,
            documentos = new[]
            {
                new
                {
                    nombre = request.FileName,
                    documento = Convert.ToBase64String(request.Document)
                }
            }
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload)
        };
        message.Headers.Add("X-API-KEY", _options.ApiKey);

        using var response = await http.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            ct);
        var token = (await response.Content.ReadAsStringAsync(ct)).Trim();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"FIRMAEC_REQUEST_FAILED: FirmaEC respondió HTTP {(int)response.StatusCode}.");
        }
        if (!LooksLikeJwt(token))
        {
            throw new InvalidOperationException(
                "FIRMAEC_INVALID_RESPONSE: FirmaEC no devolvió un token de firma válido.");
        }

        var launchUrl = BuildLaunchUrl(token, request.Reason, request.Page, request.Llx, request.Lly, request.Width, request.Height);
        var transactionId = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        return new(
            transactionId,
            launchUrl,
            DateTimeOffset.UtcNow.AddMinutes(_options.TokenLifetimeMinutes));
    }

    private void EnsureConfigured()
    {
        if (!_options.Enabled)
        {
            throw new InvalidOperationException(
                "FIRMAEC_NOT_ENABLED: la integración con FirmaEC está deshabilitada.");
        }
        if (!string.Equals(
                _options.Mode,
                "DECENTRALIZED",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "FIRMAEC_MODE_INVALID: esta instalación requiere el modo DECENTRALIZED.");
        }
        if (!Uri.TryCreate(
                _options.ServiceBaseUrl,
                UriKind.Absolute,
                out var serviceUri)
            || serviceUri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                "FIRMAEC_CONTRACT_NOT_CONFIGURED: ServiceBaseUrl no es válido.");
        }
        if (!Uri.TryCreate(
                _options.PublicApiBaseUrl,
                UriKind.Absolute,
                out var publicUri)
            || publicUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "FIRMAEC_CONTRACT_NOT_CONFIGURED: PublicApiBaseUrl debe utilizar HTTPS.");
        }
        if (string.IsNullOrWhiteSpace(_options.SystemCode)
            || string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                "FIRMAEC_CONTRACT_NOT_CONFIGURED: faltan SystemCode o ApiKey.");
        }
        if (_options.TokenLifetimeMinutes is < 1 or > 30)
        {
            throw new InvalidOperationException(
                "FIRMAEC_CONTRACT_NOT_CONFIGURED: TokenLifetimeMinutes debe estar entre 1 y 30.");
        }
    }

    private void ValidateDocument(byte[] document)
    {
        if (document.Length == 0 || document.Length > _options.MaxDocumentBytes)
        {
            throw new InvalidOperationException("DOCUMENT_SIZE_LIMIT_EXCEEDED");
        }
        ReadOnlySpan<byte> pdfMagic = "%PDF-"u8;
        if (document.Length < pdfMagic.Length
            || !document.AsSpan(0, pdfMagic.Length).SequenceEqual(pdfMagic))
        {
            throw new ArgumentException("El documento enviado a FirmaEC no es un PDF válido.");
        }
    }

    private string BuildLaunchUrl(string token, string? reason, int? page, int? llx, int? lly, int? width, int? height)
    {
        var query = new List<string>
        {
            $"token={Uri.EscapeDataString(token)}",
            $"tipo_certificado={_options.CertificateType}",
            $"url={Uri.EscapeDataString(_options.PublicApiBaseUrl.TrimEnd('/'))}"
        };
        if (_options.StampEnabled)
        {
            var resolvedLlx = llx ?? _options.StampLlx;
            var resolvedLly = lly ?? _options.StampLly;
            query.Add($"estampado={Uri.EscapeDataString(_options.StampType)}");
            query.Add($"llx={resolvedLlx}");
            query.Add($"lly={resolvedLly}");
            // OJO: en este WildFly, urx/ury NO son "llx+ancho"/"lly-alto" (coordenadas
            // relativas): el motor de estampado los usa como el tamaño absoluto del
            // sello en puntos (confirmado viendo la matriz de transformacion real del
            // QR en el PDF resultante, igual al valor de urx enviado). Sumarle llx/lly
            // haría que el sello creciera segun donde se hiciera clic. Width/Height
            // llegan del recuadro que el firmante dibuja en el visor (frontend); si no
            // se manda ninguno se usa el tamaño fijo de configuracion.
            query.Add($"urx={width ?? _options.StampWidth}");
            query.Add($"ury={height ?? _options.StampHeight}");
        }
        if (page is > 0)
        {
            query.Add($"pagina={page}");
        }
        if (!string.IsNullOrWhiteSpace(reason))
        {
            query.Add($"razon={Uri.EscapeDataString(reason.Trim())}");
        }

        return $"{_options.ProtocolScheme}://{_options.SystemCode}/firmar?{string.Join('&', query)}";
    }

    private static bool LooksLikeJwt(string value) =>
        value.Length is > 40 and < 8192
        && value.Count(character => character == '.') == 2
        && !value.Any(char.IsWhiteSpace);

    private static Uri EnsureTrailingSlash(string value) =>
        new(value.EndsWith('/') ? value : $"{value}/", UriKind.Absolute);
}
