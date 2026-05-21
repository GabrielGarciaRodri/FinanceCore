using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using FinanceCore.Infrastructure.Identity;

namespace FinanceCore.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IJwtTokenService _jwt;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IJwtTokenService jwt,
        ILogger<AuthController> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _jwt = jwt;
        _logger = logger;
    }

    /// <summary>Autenticación con email + password. Emite access + refresh token.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return Unauthorized(new ProblemDetails
            {
                Title = "Credenciales inválidas",
                Status = StatusCodes.Status401Unauthorized
            });

        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive)
            return Unauthorized(new ProblemDetails { Title = "Credenciales inválidas", Status = 401 });

        // CheckPasswordSignInAsync respeta lockout configurable de Identity.
        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            _logger.LogWarning("Login fallido para {Email}. Locked: {Locked}", user.Email, result.IsLockedOut);
            return Unauthorized(new ProblemDetails
            {
                Title = result.IsLockedOut ? "Usuario bloqueado temporalmente" : "Credenciales inválidas",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        var pair = await _jwt.IssueAsync(user, GetClientIp(), GetUserAgent(), cancellationToken);
        _logger.LogInformation("Login OK para {UserId}", user.Id);

        return Ok(BuildResponse(user, pair, await _userManager.GetRolesAsync(user)));
    }

    /// <summary>Rota un refresh token y emite nuevo access + refresh.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return Unauthorized(new ProblemDetails { Title = "Refresh token requerido", Status = 401 });

        var pair = await _jwt.RefreshAsync(request.RefreshToken, GetClientIp(), GetUserAgent(), cancellationToken);
        if (pair is null)
            return Unauthorized(new ProblemDetails { Title = "Refresh token inválido o expirado", Status = 401 });

        // Recuperamos el usuario para el body (RefreshAsync ya validó que existe).
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(pair.RefreshToken));
        // No volvemos a buscar el user por hash — pero podemos extraer del access token, o simplemente devolver sin user.
        // Para mantenerlo simple devolvemos sin el bloque user; el frontend ya tiene el user del login.
        return Ok(new AuthTokenResponse(
            AccessToken: pair.AccessToken,
            ExpiresAt: pair.AccessTokenExpiresAt,
            RefreshToken: pair.RefreshToken,
            RefreshTokenExpiresAt: pair.RefreshTokenExpiresAt,
            User: null));
    }

    /// <summary>Revoca el refresh token (logout).</summary>
    [HttpPost("logout")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
            await _jwt.RevokeAsync(request.RefreshToken, cancellationToken);

        return NoContent();
    }

    /// <summary>Devuelve la identidad del usuario asociada al access token actual.</summary>
    [HttpGet("me")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(AuthenticatedUserResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null || !user.IsActive)
            return Unauthorized();

        var roles = await _userManager.GetRolesAsync(user);
        return Ok(new AuthenticatedUserResponse(
            Id: user.Id,
            Email: user.Email ?? string.Empty,
            DisplayName: user.DisplayName,
            Roles: roles.ToArray()));
    }

    // ----- helpers -----

    private IPAddress? GetClientIp() => HttpContext.Connection.RemoteIpAddress;

    private string? GetUserAgent()
    {
        var ua = HttpContext.Request.Headers.UserAgent.ToString();
        return string.IsNullOrWhiteSpace(ua) ? null : ua;
    }

    private static AuthTokenResponse BuildResponse(ApplicationUser user, IssuedTokenPair pair, IList<string> roles)
        => new(
            AccessToken: pair.AccessToken,
            ExpiresAt: pair.AccessTokenExpiresAt,
            RefreshToken: pair.RefreshToken,
            RefreshTokenExpiresAt: pair.RefreshTokenExpiresAt,
            User: new AuthenticatedUserResponse(
                Id: user.Id,
                Email: user.Email ?? string.Empty,
                DisplayName: user.DisplayName,
                Roles: roles.ToArray()));
}

public record LoginRequest(string Email, string Password);
public record RefreshTokenRequest(string RefreshToken);

public record AuthTokenResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    AuthenticatedUserResponse? User);

public record AuthenticatedUserResponse(
    string Id,
    string Email,
    string? DisplayName,
    string[] Roles);
