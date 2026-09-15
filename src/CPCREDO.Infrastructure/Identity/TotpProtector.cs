using CPCREDO.Application.Identity;
using Microsoft.AspNetCore.DataProtection;
using OtpNet;
using QRCoder;

namespace CPCREDO.Infrastructure.Identity;

public sealed class TotpProtector
{
    public const string ProtectorPurpose = "CPCREDO.Totp.v1";
    private readonly IDataProtector _protector;

    public TotpProtector(IDataProtectionProvider dataProtection)
    {
        _protector = dataProtection.CreateProtector(ProtectorPurpose);
    }

    public static byte[] NewSecret() => KeyGeneration.GenerateRandomKey(20);

    public static string ToManualKey(byte[] secret) => Base32Encoding.ToString(secret).TrimEnd('=');

    public string Protect(byte[] secret) => _protector.Protect(Convert.ToBase64String(secret));

    public byte[] Unprotect(string protectedSecret)
    {
        var raw = _protector.Unprotect(protectedSecret);
        return Convert.FromBase64String(raw);
    }

    public static bool Verify(byte[] secret, string code, long? lastTimestep, out long timestep)
    {
        timestep = 0;
        var digits = (code ?? string.Empty).Trim().Replace(" ", "");
        if (digits.Length != 6 || !digits.All(char.IsDigit))
            return false;
        var totp = new Totp(secret, step: 30, totpSize: 6);
        if (!totp.VerifyTotp(digits, out timestep, VerificationWindow.RfcSpecifiedNetworkDelay))
            return false;
        if (lastTimestep is long used && timestep <= used)
            return false;
        return true;
    }

    public static string OtpauthUrl(string username, string manualKey) =>
        $"otpauth://totp/CPCREDO:{Uri.EscapeDataString(username)}?secret={manualKey}&issuer=CPCREDO&digits=6&period=30";

    public static string QrPngDataUrl(string otpauth)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(otpauth, QRCodeGenerator.ECCLevel.M);
        var qr = new PngByteQRCode(data);
        var png = qr.GetGraphic(6);
        return "data:image/png;base64," + Convert.ToBase64String(png);
    }

    public static MfaSetupDto BuildSetup(byte[] secret, string username)
    {
        var manual = ToManualKey(secret);
        var url = OtpauthUrl(username, manual);
        return new MfaSetupDto(manual, url, QrPngDataUrl(url));
    }
}
