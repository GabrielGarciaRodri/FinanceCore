using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinanceCore.Infrastructure.Identity;

public interface IIdentitySeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}

public class IdentitySeeder : IIdentitySeeder
{
    private static readonly string[] DefaultRoles = ["Admin", "FinanceAdmin", "Reader"];

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IdentitySeedOptions _options;
    private readonly ILogger<IdentitySeeder> _logger;

    public IdentitySeeder(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IOptions<IdentitySeedOptions> options,
        ILogger<IdentitySeeder> logger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Identity seed deshabilitado por configuración.");
            return;
        }

        // 1. Asegurar que los roles existen (V003 ya los inserta, pero por idempotencia)
        foreach (var role in DefaultRoles)
        {
            if (!await _roleManager.RoleExistsAsync(role))
            {
                await _roleManager.CreateAsync(new IdentityRole(role));
                _logger.LogInformation("Rol {Role} creado por seeder.", role);
            }
        }

        // 2. Crear admin por defecto si no existe ningún usuario
        if (_userManager.Users.Any())
        {
            _logger.LogDebug("Usuarios existentes detectados; saltando creación de admin por defecto.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.AdminEmail) || string.IsNullOrWhiteSpace(_options.AdminPassword))
        {
            _logger.LogWarning("Identity seed habilitado pero AdminEmail/AdminPassword vacíos.");
            return;
        }

        var admin = new ApplicationUser
        {
            UserName = _options.AdminEmail,
            Email = _options.AdminEmail,
            EmailConfirmed = true,
            DisplayName = _options.AdminDisplayName,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var result = await _userManager.CreateAsync(admin, _options.AdminPassword);
        if (!result.Succeeded)
        {
            _logger.LogError(
                "No se pudo crear el usuario admin por defecto: {Errors}",
                string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}")));
            return;
        }

        await _userManager.AddToRoleAsync(admin, "Admin");
        await _userManager.AddToRoleAsync(admin, "FinanceAdmin");

        _logger.LogWarning(
            "Usuario admin por defecto creado: {Email}. CAMBIAR LA CONTRASEÑA EN EL PRIMER LOGIN.",
            _options.AdminEmail);
    }
}
