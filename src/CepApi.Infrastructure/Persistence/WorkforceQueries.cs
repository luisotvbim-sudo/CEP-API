using CepApi.Domain;

namespace CepApi.Infrastructure.Persistence;

public static class WorkforceQueries
{
    public static IQueryable<WorkforcePerson> ActiveNotificationRecipients(this AppDbContext db, Guid organizationId, Guid? userId = null)
        => db.WorkforcePeople.Where(person => person.OrganizationId == organizationId && person.UserId != null &&
            (userId == null || person.UserId == userId) && db.Users.Any(user => user.Id == person.UserId &&
                user.OrganizationId == organizationId && user.Status == UserStatus.Active));
}
