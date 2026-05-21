using Microsoft.AspNetCore.Identity;

namespace FinanceCore.Infrastructure.Identity;

/// <summary>
/// Usuario del sistema. Extiende IdentityUser con metadatos propios
/// (display name, flag de activo, fecha de creación).
/// </summary>
public class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
