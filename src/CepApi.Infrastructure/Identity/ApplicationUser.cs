using CepApi.Domain;
using Microsoft.AspNetCore.Identity;

namespace CepApi.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public required string DisplayName { get; set; }
    public Guid? OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public UserRole Role { get; set; } = UserRole.User;
    public UserStatus Status { get; set; } = UserStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ICollection<ProductAccess> ProductAccesses { get; set; } = [];
    public ICollection<RefreshSession> RefreshSessions { get; set; } = [];
}
