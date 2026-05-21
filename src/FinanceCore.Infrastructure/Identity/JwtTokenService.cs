using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using FinanceCore.Infrastructure.Persistence.Context;

namespace FinanceCore.Infrastructure.Identity;

public sealed record IssuedTokenPair(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public interface IJwtTokenService
{
    Task<IssuedTokenPair> IssueAsync(
        ApplicationUser user,
        System.Net.IPAddress? ipAddress = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default);

    /// <summary>Rota un refresh token: lo revoca y emite uno nuevo + access.</summary>
    Task<IssuedTokenPair?> RefreshAsync(
        string refreshToken,
        System.Net.IPAddress? ipAddress = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default);

    /// <summary>Revoca un refresh token (logout).</summary>
    Task<bool> RevokeAsync(string refreshToken, CancellationToken cancellationToken = default);
}

public class JwtTokenService : IJwtTokenService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly FinanceCoreDbContext _context;
    private readonly JwtOptions _options;
    private readonly SymmetricSecurityKey _signingKey;
    private readonly JwtSecurityTokenHandler _handler = new();

    public JwtTokenService(
        UserManager<ApplicationUser> userManager,
        FinanceCoreDbContext context,
        IOptions<JwtOptions> options)
    {
        _userManager = userManager;
        _context = context;
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.SigningKey) || _options.SigningKey.Length < 32)
            throw new InvalidOperationException(
                "JWT SigningKey no configurada o demasiado corta (mínimo 32 caracteres).");

        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
    }

    public async Task<IssuedTokenPair> IssueAsync(
        ApplicationUser user,
        System.Net.IPAddress? ipAddress = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default)
    {
        var (accessToken, accessExp) = await BuildAccessTokenAsync(user);
        var (refreshPlain, refreshExp) = await PersistRefreshTokenAsync(user.Id, ipAddress, userAgent, cancellationToken);
        return new IssuedTokenPair(accessToken, accessExp, refreshPlain, refreshExp);
    }

    public async Task<IssuedTokenPair?> RefreshAsync(
        string refreshToken,
        System.Net.IPAddress? ipAddress = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default)
    {
        var hash = HashToken(refreshToken);
        var existing = await _context.Set<RefreshToken>()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (existing is null || !existing.IsActive)
            return null;

        var user = await _userManager.FindByIdAsync(existing.UserId);
        if (user is null || !user.IsActive)
            return null;

        // Rotación: revocar el viejo y emitir uno nuevo enlazado.
        existing.RevokedAt = DateTimeOffset.UtcNow;

        var (newPlain, newExp) = await PersistRefreshTokenAsync(user.Id, ipAddress, userAgent, cancellationToken);

        // Linkear el nuevo refresh al viejo (cadena de rotación auditable).
        var newest = await _context.Set<RefreshToken>()
            .FirstOrDefaultAsync(t => t.TokenHash == HashToken(newPlain), cancellationToken);
        if (newest is not null) existing.ReplacedById = newest.Id;

        var (accessToken, accessExp) = await BuildAccessTokenAsync(user);
        await _context.SaveChangesAsync(cancellationToken);

        return new IssuedTokenPair(accessToken, accessExp, newPlain, newExp);
    }

    public async Task<bool> RevokeAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var hash = HashToken(refreshToken);
        var token = await _context.Set<RefreshToken>()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (token is null || token.RevokedAt is not null)
            return false;

        token.RevokedAt = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<(string token, DateTimeOffset expiresAt)> BuildAccessTokenAsync(ApplicationUser user)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(Math.Max(1, _options.AccessTokenMinutes));

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? user.Email ?? user.Id)
        };

        var roles = await _userManager.GetRolesAsync(user);
        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var creds = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: creds);

        return (_handler.WriteToken(token), expiresAt);
    }

    private async Task<(string plain, DateTimeOffset expiresAt)> PersistRefreshTokenAsync(
        string userId,
        System.Net.IPAddress? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var plain = GenerateSecureToken();
        var hash = HashToken(plain);
        var expiresAt = DateTimeOffset.UtcNow.AddDays(Math.Max(1, _options.RefreshTokenDays));

        var token = new RefreshToken
        {
            UserId = userId,
            TokenHash = hash,
            ExpiresAt = expiresAt,
            IpAddress = ipAddress,
            UserAgent = string.IsNullOrWhiteSpace(userAgent) ? null
                : userAgent.Length > 500 ? userAgent[..500] : userAgent
        };

        _context.Set<RefreshToken>().Add(token);
        await _context.SaveChangesAsync(cancellationToken);

        return (plain, expiresAt);
    }

    private static string GenerateSecureToken()
    {
        // 64 bytes = 512 bits; base64-url -> ~86 chars sin padding.
        var buffer = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(buffer)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
