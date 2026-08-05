using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using UtaElectronicSignature.Application;
using UtaElectronicSignature.Contracts;

namespace UtaElectronicSignature.Infrastructure;

// Validacion local de PDFs firmados: lee directamente la(s) firma(s) PKCS#7/CMS incrustadas
// (estandar PDF /Type/Sig, /SubFilter adbe.pkcs7.detached) para verificar integridad
// criptografica y leer del certificado del firmante su fecha de vigencia (NotBefore/NotAfter).
// Funciona para CUALQUIER PDF firmado (no solo los generados por nuestros propios procesos) —
// deliberadamente SIN depender de IFirmaEcClient/FirmaEcOptions, para poder reutilizarse desde
// cualquier otro flujo de este backend sin arrastrar la configuracion de FirmaEc.
// OJO: esto NO verifica revocacion en linea (OCSP/CRL) contra la PKI de Security Data/BCE —
// requeriria alcance de red adicional y esta fuera de este alcance; se marca como advertencia
// explicita en el resultado en vez de aparentar una validacion completa.
public sealed class PdfSignatureValidationService : IPdfSignatureValidator
{
    private const int MaxDocumentBytes = 25_000_000;

    public async Task<DocumentValidationResult> ValidateDocumentAsync(Stream document, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await document.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        EnsurePdf(bytes);
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var signers = new List<ValidatedSigner>();
        var warnings = new List<string> { "No se verificó el estado de revocación en línea (OCSP/CRL) del certificado." };
        var allValid = true;

        foreach (var (byteRange, contentsDer) in FindSignatures(bytes))
        {
            try
            {
                var signedContent = new byte[byteRange[1] + byteRange[3]];
                Buffer.BlockCopy(bytes, byteRange[0], signedContent, 0, byteRange[1]);
                Buffer.BlockCopy(bytes, byteRange[2], signedContent, byteRange[1], byteRange[3]);

                var cms = new SignedCms(new ContentInfo(signedContent), detached: true);
                cms.Decode(contentsDer);
                bool cryptoValid;
                try
                {
                    cms.CheckSignature(verifySignatureOnly: true);
                    cryptoValid = true;
                }
                catch (CryptographicException)
                {
                    cryptoValid = false;
                }

                var signerInfo = cms.SignerInfos.Count > 0 ? cms.SignerInfos[0] : null;
                var cert = signerInfo?.Certificate;
                if (cert is null)
                {
                    warnings.Add("Una de las firmas no incluye el certificado del firmante embebido.");
                    allValid = false;
                    continue;
                }

                var signedAt = TryGetSigningTime(signerInfo!) ?? TryGetPdfSignDate(bytes, byteRange[2]);
                var now = DateTimeOffset.UtcNow;
                var notBefore = new DateTimeOffset(cert.NotBefore.ToUniversalTime());
                var notAfter = new DateTimeOffset(cert.NotAfter.ToUniversalTime());
                var revocationStatus = now < notBefore ? "AUN_NO_VIGENTE" : now > notAfter ? "EXPIRADO" : "VIGENTE";

                signers.Add(new ValidatedSigner(
                    cert.GetNameInfo(X509NameType.SimpleName, false),
                    ExtractIdentification(cert) ?? "",
                    signedAt,
                    cryptoValid ? "FIRMA_VALIDA" : "FIRMA_INVALIDA",
                    cert.GetNameInfo(X509NameType.SimpleName, true),
                    cert.SerialNumber,
                    notBefore,
                    notAfter,
                    revocationStatus));

                allValid = allValid && cryptoValid;
            }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException)
            {
                warnings.Add("No se pudo interpretar una de las firmas del documento.");
                allValid = false;
            }
        }

        var status = signers.Count == 0 ? "SIN_FIRMAS" : allValid ? "VALIDO" : "INVALIDO";
        return new DocumentValidationResult(status, signers.Count > 0, signers.Count > 0 && allValid, sha256, signers.Count, signers, warnings);
    }

    private static void EnsurePdf(byte[] document)
    {
        if (document.Length == 0 || document.Length > MaxDocumentBytes)
        {
            throw new InvalidOperationException("DOCUMENT_SIZE_LIMIT_EXCEEDED");
        }
        ReadOnlySpan<byte> pdfMagic = "%PDF-"u8;
        if (document.Length < pdfMagic.Length || !document.AsSpan(0, pdfMagic.Length).SequenceEqual(pdfMagic))
        {
            throw new ArgumentException("El documento enviado no es un PDF válido.");
        }
    }

    // Ubica cada diccionario de firma (/Type/Sig) por su /ByteRange, que es siempre texto
    // plano en el PDF (nunca dentro de un stream comprimido: la especificacion PDF lo exige
    // asi para que /Contents pueda sobrescribirse in-place al firmar). El propio /ByteRange
    // ya indica donde esta /Contents (el hueco entre el primer y segundo rango firmado), asi
    // que no hace falta parsear el diccionario completo.
    private static IEnumerable<(int[] ByteRange, byte[] ContentsDer)> FindSignatures(byte[] bytes)
    {
        var text = Encoding.Latin1.GetString(bytes);
        foreach (Match brMatch in Regex.Matches(text, @"/ByteRange\s*\[\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s*\]"))
        {
            var byteRange = new[]
            {
                int.Parse(brMatch.Groups[1].Value), int.Parse(brMatch.Groups[2].Value),
                int.Parse(brMatch.Groups[3].Value), int.Parse(brMatch.Groups[4].Value),
            };
            if (byteRange[2] <= byteRange[0] + byteRange[1] || byteRange[2] + byteRange[3] > bytes.Length) continue;

            // El hueco entre el primer y segundo rango firmado NO es "/Contents<hex>": la
            // clave "/Contents" ya quedo dentro del primer rango firmado. El hueco es
            // exactamente el valor "<hex>" (con los signos < > incluidos), como confirma
            // el /ByteRange real de un PDF firmado por FirmaEC.
            var gapStart = byteRange[0] + byteRange[1];
            var gapLength = byteRange[2] - gapStart;
            if (gapLength <= 0 || gapStart + gapLength > bytes.Length) continue;
            var gap = Encoding.Latin1.GetString(bytes, gapStart, gapLength).Trim();
            var contentsMatch = Regex.Match(gap, "^<([0-9A-Fa-f]+)>$");
            if (!contentsMatch.Success) continue;

            var hex = contentsMatch.Groups[1].Value;
            if (hex.Length % 2 != 0) hex = hex[..^1];
            var der = Convert.FromHexString(hex);
            // El campo /Contents se reserva con un tamaño fijo y suele quedar mas grande
            // que el DER real, relleno con ceros al final (confirmado con un documento
            // firmado real): hay que recortarlos o SignedCms.Decode falla.
            var derLength = der.Length;
            while (derLength > 0 && der[derLength - 1] == 0) derLength--;
            if (derLength != der.Length) der = der[..derLength];
            yield return (byteRange, der);
        }
    }

    private static DateTimeOffset? TryGetSigningTime(SignerInfo signerInfo)
    {
        foreach (var attr in signerInfo.SignedAttributes)
        {
            if (attr.Oid?.Value != "1.2.840.113549.1.9.5") continue;
            foreach (var value in attr.Values)
            {
                if (value is Pkcs9SigningTime signingTime) return new DateTimeOffset(signingTime.SigningTime.ToUniversalTime());
            }
        }
        return null;
    }

    // Respaldo si la firma no trae el atributo CMS de fecha (confirmado con documentos
    // reales de FirmaEC: no lo traen). El campo /M del propio diccionario de firma PDF
    // (formato "D:AAAAMMDDHHmmSS+HH'mm'") aparece DESPUES del valor de /Contents (que puede
    // pesar decenas de KB), asi que la busqueda arranca justo donde termina el segundo rango
    // firmado (byteRange[2]), no en el propio /ByteRange — de lo contrario la ventana
    // acotada nunca alcanzaria a cubrir /M.
    private static DateTimeOffset? TryGetPdfSignDate(byte[] bytes, int searchFrom)
    {
        var window = Math.Min(4000, bytes.Length - searchFrom);
        if (window <= 0) return null;
        var text = Encoding.Latin1.GetString(bytes, searchFrom, window);
        var match = Regex.Match(text, @"/M\s*\(D:(\d{4})(\d{2})(\d{2})(\d{2})(\d{2})(\d{2})([+-]\d{2})'?(\d{2})'?\)");
        if (!match.Success) return null;
        try
        {
            var offset = new TimeSpan(int.Parse(match.Groups[7].Value), int.Parse(match.Groups[8].Value), 0);
            return new DateTimeOffset(
                int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value),
                int.Parse(match.Groups[4].Value), int.Parse(match.Groups[5].Value), int.Parse(match.Groups[6].Value), offset);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or FormatException)
        {
            return null;
        }
    }

    private static string? ExtractIdentification(X509Certificate2 cert) => CertificateIdentity.ExtractCedula(cert);
}

// Los certificados personales ecuatorianos (Security Data/BCE/ANF) codifican la cedula en
// el atributo SERIALNUMBER (OID 2.5.4.5) del Subject como "{cedula}-{ddMMyyHHmmss}"
// (confirmado inspeccionando un certificado real emitido por Security Data). .NET SI
// reconoce este OID y lo renderiza como "SERIALNUMBER=..." (a diferencia de otras
// librerias que lo dejan en forma numerica "2.5.4.5=..."); se aceptan ambas formas.
// Compartido entre PdfSignatureValidationService y CertificateValidationService.
public static class CertificateIdentity
{
    public static string? ExtractCedula(X509Certificate2 cert)
    {
        var match = Regex.Match(cert.Subject, @"(?:SERIALNUMBER|2\.5\.4\.5)=(\d{10})");
        return match.Success ? match.Groups[1].Value : null;
    }
}
