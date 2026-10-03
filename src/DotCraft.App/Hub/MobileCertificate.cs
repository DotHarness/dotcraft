using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;

namespace DotCraft.Hub;

internal static class MobileCertificate
{
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";

    public static X509Certificate2 LoadOrCreate(string path)
    {
        if (File.Exists(path))
            return X509CertificateLoader.LoadPkcs12FromFile(path, password: null);

        var pfx = Create(Environment.MachineName);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        WriteOwnerOnly(path, pfx);
        return X509CertificateLoader.LoadPkcs12(pfx, password: null);
    }

    public static string Fingerprint(X509Certificate2 certificate) =>
        Convert.ToHexStringLower(SHA256.HashData(certificate.RawData));

    private static byte[] Create(string computerName)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var subject = new X500DistinguishedNameBuilder();
        subject.AddCommonName(computerName);
        var request = new CertificateRequest(subject.Build(), key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(computerName);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(ServerAuthenticationOid)], false));
        var now = DateTimeOffset.UtcNow;
        using var certificate = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(20));
        return certificate.Export(X509ContentType.Pkcs12);
    }

    private static void WriteOwnerOnly(string path, byte[] contents)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var stream = new FileStream(path, options))
            stream.Write(contents);
        if (OperatingSystem.IsWindows())
            RestrictToCurrentUser(path);
    }

    [SupportedOSPlatform("windows")]
    private static void RestrictToCurrentUser(string path)
    {
        if (WindowsIdentity.GetCurrent().User is not { } user)
            return;
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
        try
        {
            new FileInfo(path).SetAccessControl(security);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
        }
    }
}
