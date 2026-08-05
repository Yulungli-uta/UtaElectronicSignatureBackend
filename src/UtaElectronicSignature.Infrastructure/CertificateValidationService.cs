using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using UtaElectronicSignature.Application;
using UtaElectronicSignature.Contracts;

namespace UtaElectronicSignature.Infrastructure;

// Lee los datos PUBLICOS de un certificado personal .p12/.pfx (titular, vigencia, emisor).
// La contraseña solo se usa en memoria, dentro de esta llamada, para abrir el archivo — no
// se persiste, no se registra en logs, y el objeto que da acceso a la llave privada se
// descarta (Dispose) apenas se leen los campos publicos que se necesitan.
public sealed class CertificateValidationService : ICertificateValidator
{
    public CertificateValidationResult Validate(byte[] pkcs12, string password)
    {
        using var cert = X509CertificateLoader.LoadPkcs12(pkcs12, password, X509KeyStorageFlags.EphemeralKeySet);
        var now = DateTimeOffset.UtcNow;
        var notBefore = new DateTimeOffset(cert.NotBefore.ToUniversalTime());
        var notAfter = new DateTimeOffset(cert.NotAfter.ToUniversalTime());
        return new CertificateValidationResult(
            cert.GetNameInfo(X509NameType.SimpleName, false),
            CertificateIdentity.ExtractCedula(cert),
            cert.GetNameInfo(X509NameType.SimpleName, true),
            notBefore,
            notAfter,
            now >= notBefore && now <= notAfter,
            cert.SerialNumber);
    }
}
