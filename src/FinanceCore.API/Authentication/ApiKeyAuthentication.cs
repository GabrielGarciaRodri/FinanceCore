using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FinanceCore.API.Authentication;

/// <summary>
/// API Key authentication scheme for service-to-service communication.
/// Header-based API keys only.
/// </summary>
public static class ApiKeyDefaults
{
    public const string AuthenticationScheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";
    public const string AuthErrorItem = "ApiKeyAuthError";
    public const string MissingApiKeyMessage = "API key header missing.";
}

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// Configured API keys with their associated client names and roles.
    /// </summary>
    public Dictionary<string, ApiKeyConfig> ApiKeys { get; set; } = new();
}

public class ApiKeyConfig
{
    public string ClientName { get; set; } = null!;
    public string[] Roles { get; set; } = Array.Empty<string>();
}

public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Header-only API key
        if (!Request.Headers.TryGetValue(ApiKeyDefaults.HeaderName, out var headerValue))
        {
            Context.Items[ApiKeyDefaults.AuthErrorItem] = ApiKeyDefaults.MissingApiKeyMessage;
            return Task.FromResult(AuthenticateResult.Fail(ApiKeyDefaults.MissingApiKeyMessage));
        }

        var apiKey = headerValue.FirstOrDefault();

        if (string.IsNullOrEmpty(apiKey))
        {
            Context.Items[ApiKeyDefaults.AuthErrorItem] = ApiKeyDefaults.MissingApiKeyMessage;
            return Task.FromResult(AuthenticateResult.Fail(ApiKeyDefaults.MissingApiKeyMessage));
        }

        if (!Options.ApiKeys.TryGetValue(apiKey, out var keyConfig))
        {
            Context.Items[ApiKeyDefaults.AuthErrorItem] = "Invalid API key.";
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, keyConfig.ClientName),
            new(ClaimTypes.AuthenticationMethod, ApiKeyDefaults.AuthenticationScheme),
            new("client_id", keyConfig.ClientName)
        };

        foreach (var role in keyConfig.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, ApiKeyDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, ApiKeyDefaults.AuthenticationScheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/json";

        var error = Context.Items.TryGetValue(ApiKeyDefaults.AuthErrorItem, out var reason)
            ? reason?.ToString()
            : "API key is missing or invalid.";

        var problem = new ProblemDetails
        {
            Title = "Unauthorized",
            Status = StatusCodes.Status401Unauthorized,
            Detail = error
        };

        return Response.WriteAsJsonAsync(problem);
    }
}

/// <summary>
/// Extension methods for registering API Key authentication.
/// </summary>
public static class ApiKeyAuthenticationExtensions
{
    public static AuthenticationBuilder AddApiKey(
        this AuthenticationBuilder builder,
        Action<ApiKeyAuthenticationOptions>? configureOptions = null)
    {
        return builder.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
            ApiKeyDefaults.AuthenticationScheme,
            configureOptions ?? (_ => { }));
    }
}
