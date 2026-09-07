namespace CPCREDO.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "CPCREDO";
    public string Audience { get; set; } = "CPCREDO.Staff";
    public string Secret { get; set; } = string.Empty;
    public int ExpiryMinutes { get; set; } = 480;
}
