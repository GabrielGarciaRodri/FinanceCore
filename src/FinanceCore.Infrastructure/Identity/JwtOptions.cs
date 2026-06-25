namespace FinanceCore.Infrastructure.Identity;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Clave HMAC-SHA256. MÍNIMO 32 caracteres ascii (256 bits).</summary>
    public string SigningKey { get; set; } = "";

    public string Issuer { get; set; } = "FinanceCore";
    public string Audience { get; set; } = "FinanceCore.Web";

    /// <summary>Vida del access token. Default 15 min.</summary>
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>Vida del refresh token. Default 14 días.</summary>
    public int RefreshTokenDays { get; set; } = 14;
}

/// <summary>Opciones del bootstrap inicial de usuarios.</summary>
public class IdentitySeedOptions
{
    public const string SectionName = "Identity:Seed";

    public bool Enabled { get; set; } = true;
    public string AdminEmail { get; set; } = "admin@financecore.local";
    public string AdminPassword { get; set; } = "ChangeMe!2026";
    public string AdminDisplayName { get; set; } = "Default Administrator";

    /// <summary>Usuario demo de solo-lectura (rol Reader). Deshabilitado por
    /// defecto; se activa en el entorno de demo público vía
    /// Identity:Seed:DemoUser:Enabled.</summary>
    public DemoUserSeedOptions DemoUser { get; set; } = new();
}

/// <summary>Opciones del usuario demo read-only para el entorno de demo público.</summary>
public class DemoUserSeedOptions
{
    public bool Enabled { get; set; } = false;
    public string Email { get; set; } = "demo@financecore.local";
    public string Password { get; set; } = "Demo!2026";
    public string DisplayName { get; set; } = "Demo (read-only)";
}
