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

        // 2. Admin por defecto: solo en un sistema sin usuarios (intención original).
        await EnsureDefaultAdminAsync();

        // 3. Usuario demo read-only (rol Reader): idempotente por email e
        //    independiente del admin. Apto para Production (a diferencia del
        //    DevController). Se activa con Identity:Seed:DemoUser:Enabled.
        await EnsureDemoReaderUserAsync();
    }

    private async Task EnsureDefaultAdminAsync()
    {
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

    private async Task EnsureDemoReaderUserAsync()
    {
        var demo = _options.DemoUser;
        if (demo is null || !demo.Enabled)
            return;

        if (string.IsNullOrWhiteSpace(demo.Email) || string.IsNullOrWhiteSpace(demo.Password))
        {
            _logger.LogWarning("DemoUser habilitado pero Email/Password vacíos; se omite.");
            return;
        }

        if (await _userManager.FindByEmailAsync(demo.Email) is not null)
        {
            _logger.LogDebug("Usuario demo {Email} ya existe; nada que hacer.", demo.Email);
            return;
        }

        var user = new ApplicationUser
        {
            UserName = demo.Email,
            Email = demo.Email,
            EmailConfirmed = true,
            DisplayName = demo.DisplayName,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var result = await _userManager.CreateAsync(user, demo.Password);
        if (!result.Succeeded)
        {
            _logger.LogError(
                "No se pudo crear el usuario demo read-only: {Errors}",
                string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}")));
            return;
        }

        await _userManager.AddToRoleAsync(user, "Reader");
        _logger.LogInformation("Usuario demo read-only creado: {Email} (rol Reader).", demo.Email);
    }
}
