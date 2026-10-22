namespace Modules.Identity.Contracts;

/// <summary>Read access to users for other modules (e.g. matching an invitation to the signed-in user).</summary>
public interface IUserDirectory
{
    /// <summary>The user's email, or null when the user does not exist.</summary>
    Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken);
}
