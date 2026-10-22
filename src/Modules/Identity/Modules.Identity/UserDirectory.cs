using Microsoft.AspNetCore.Identity;
using Modules.Identity.Contracts;
using Modules.Identity.Persistence;

namespace Modules.Identity;

internal sealed class UserDirectory(UserManager<AuthUser> users) : IUserDirectory
{
    public async Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken) =>
        (await users.FindByIdAsync(userId.ToString()))?.Email;
}
