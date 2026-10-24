using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Modules.Identity.Contracts;
using Modules.Identity.Persistence;

namespace Modules.Identity;

/// <summary>Platform roles as Identity claims of type <see cref="ClaimType"/> (interim until task 007's staff realm).</summary>
internal sealed class PlatformRoleStore(UserManager<AuthUser> users) : IPlatformRoles
{
    public const string ClaimType = "platform_role";

    public async Task<bool> HasRoleAsync(Guid userId, string role, CancellationToken cancellationToken) =>
        await users.FindByIdAsync(userId.ToString()) is { } user
        && (await users.GetClaimsAsync(user)).Any(c => c.Type == ClaimType && c.Value == role);

    public async Task<bool> IsStaffAsync(Guid userId, CancellationToken cancellationToken) =>
        await users.FindByIdAsync(userId.ToString()) is { } user
        && (await users.GetClaimsAsync(user)).Any(c => c.Type == ClaimType);

    /// <summary>Development only (dev endpoint): gives the signed-in user a platform role.</summary>
    public async Task GrantAsync(Guid userId, string role)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new InvalidOperationException("Unknown user.");
        if (!await HasRoleAsync(userId, role, CancellationToken.None))
        {
            await users.AddClaimAsync(user, new Claim(ClaimType, role));
        }
    }
}
