namespace CepApi.Domain;

public sealed class WorkforceTeam
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public required string Name { get; set; }
    public required string NormalizedName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ICollection<TeamAssignment> Assignments { get; set; } = [];
}

public sealed class TeamAssignment
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TeamId { get; set; }
    public WorkforceTeam Team { get; set; } = null!;
    public Guid UserId { get; set; }
    public TeamAssignmentRole Role { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
}
