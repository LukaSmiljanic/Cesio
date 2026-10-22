using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modules.Audit.Contracts;
using Modules.Identity.Contracts;
using Modules.Organizations.Contracts;
using Modules.Organizations.Persistence;
using Shared.Infrastructure;
using Shared.Infrastructure.Email;
using Shared.Infrastructure.Outbox;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Persistence.Platform;
using Shared.Infrastructure.Requests;
using Shared.Kernel;

namespace Modules.Organizations;

public sealed record CreateOrganizationRequest(string Pib);

public sealed record CreateFactorOrganizationRequest(string Pib, string LegalName);

public sealed record UpdateOrganizationRequest(string? LegalName, string? Mb, string? LegalForm, string? RegisteredAddress);

public sealed record InviteMemberRequest(string Email);

public sealed record AcceptInvitationRequest(string Token);

public sealed record UpdateMemberRequest(bool IsSignatory, Guid? LegalRepresentativeId, Guid? PowerOfAttorneyDocumentId);

public sealed record LegalRepresentativeRequest(string Name, string Role, DateOnly? ValidFrom, DateOnly? ValidTo, Guid? LinkedUserId);

public sealed record AcceptTermsRequest(string TermsVersion);

public sealed class OrganizationOptions
{
    /// <summary>Base URL of the SPA for invitation links (shared with the Auth section).</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:5173";
}

/// <summary>The Terms of Use version users accept (texts come from task 000; a placeholder until then).</summary>
public static class TermsVersions
{
    public const string Current = "placeholder-2026-10";
}

/// <summary>
/// Organization commands (task 005). Each runs in the unit-of-work transaction (opened by the idempotency
/// middleware for these endpoints) together with its audit event and, for submit, its outbox event.
/// </summary>
internal sealed class OrganizationService(
    OrgsDbContext orgs,
    UnitOfWork unitOfWork,
    IAuditWriter audit,
    IOutbox outbox,
    IRequestContext request,
    IBusinessParameters parameters,
    IEmailSender email,
    IUserDirectory userDirectory,
    IOptions<OrganizationOptions> options,
    TimeProvider timeProvider)
{
    public const string MaxSellerMembersParameter = "orgs.maxSellerMembers";

    private static readonly TimeSpan InvitationValidity = TimeSpan.FromDays(7);

    private Guid UserId => request.UserId ?? throw new InvalidOperationException("An authenticated user is required.");

    private Guid OrgId => request.OrganizationId ?? throw new InvalidOperationException("An organization context is required.");

    public async Task<IResult> ListMineAsync(CancellationToken cancellationToken)
    {
        var userId = UserId;
        var mine = await (
                from m in orgs.Memberships.IgnoreQueryFilters()
                join o in orgs.Organizations.IgnoreQueryFilters() on m.OrgId equals o.Id
                where m.UserId == userId && m.Status == MembershipStatus.Active
                orderby o.CreatedAt
                select new { id = o.Id, type = o.OrgType, status = o.OnboardingStatus, pib = o.Pib, legalName = o.LegalName, isSignatory = m.IsSignatory })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return Results.Ok(mine);
    }

    /// <summary>A seller's private Draft claim (ADR-020): nobody else sees it, and the PIB is reserved only by a valid SEF key (task 007a).</summary>
    public async Task<IResult> CreateSellerAsync(CreateOrganizationRequest body, CancellationToken cancellationToken)
    {
        if (!Pib.TryParse(body.Pib, out var pib))
        {
            return Problems.Validation("pib", "invalid_pib", "A PIB has 9 digits and a valid check digit.");
        }

        var organization = await CreateAsync(OrganizationType.Seller, pib, legalName: null, claimedAt: null, "Organization.ClaimCreated", cancellationToken);
        return Results.Created($"/api/v1/organizations/{organization.Id}", Details(organization));
    }

    /// <summary>Development only: factor organizations are created by staff in task 008.</summary>
    public async Task<IResult> CreateFactorAsync(CreateFactorOrganizationRequest body, CancellationToken cancellationToken)
    {
        if (!Pib.TryParse(body.Pib, out var pib) || string.IsNullOrWhiteSpace(body.LegalName))
        {
            return Problems.Validation("pib", "invalid_pib", "A valid PIB and a legal name are required.");
        }

        if (await orgs.Organizations.IgnoreQueryFilters().AnyAsync(o => o.Pib == pib.Value && o.ClaimedAt != null, cancellationToken))
        {
            return Problems.Error(StatusCodes.Status409Conflict, "pib_already_claimed", "An organization with this PIB is already registered.");
        }

        var organization = await CreateAsync(OrganizationType.Factor, pib, body.LegalName.Trim(), timeProvider.GetUtcNow(), "Organization.Created", cancellationToken);
        return Results.Created($"/api/v1/organizations/{organization.Id}", Details(organization));
    }

    public async Task<IResult> GetAsync(CancellationToken cancellationToken) =>
        await orgs.Organizations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == OrgId, cancellationToken) is { } organization
            ? WithETag(Details(organization), organization.Version)
            : Results.NotFound();

    public async Task<IResult> UpdateAsync(UpdateOrganizationRequest body, int? ifMatch, CancellationToken cancellationToken)
    {
        var organization = await orgs.Organizations.SingleAsync(o => o.Id == OrgId, cancellationToken);
        if (organization.OnboardingStatus != OnboardingStatus.Draft)
        {
            return Problems.ConflictState("Only a Draft organization can be edited.");
        }

        if (ifMatch is not { } expected)
        {
            return Problems.PreconditionRequired();
        }

        if (expected != organization.Version)
        {
            return Problems.ConcurrencyConflict();
        }

        string? mb = null;
        if (body.Mb is not null)
        {
            if (!MaticniBroj.TryParse(body.Mb, out var parsedMb))
            {
                return Problems.Validation("mb", "invalid_mb", "A matični broj has 8 digits and a valid check digit.");
            }

            mb = parsedMb.Value;
        }

        var changes = new Dictionary<string, string?>();
        Set(organization.LegalName, body.LegalName?.Trim(), v => organization.LegalName = v, "legalName", changes);
        Set(organization.Mb, mb, v => organization.Mb = v, "mb", changes);
        Set(organization.LegalForm, body.LegalForm?.Trim(), v => organization.LegalForm = v, "legalForm", changes);
        Set(organization.RegisteredAddress, body.RegisteredAddress?.Trim(), v => organization.RegisteredAddress = v, "registeredAddress", changes);

        return await SaveAsync(
            async ct => await audit.WriteAsync(new AuditEntry("Organization.Updated", "Organization", organization.Id, organization.Id, Changes: changes), ct),
            () => WithETag(Details(organization), organization.Version),
            cancellationToken);
    }

    /// <summary>
    /// Draft → Submitted. The minimum data is the simplest reading of task 005: legal name, MB, at least one legal
    /// representative and the current Terms accepted. KYB review starts from the event in task 007.
    /// </summary>
    public async Task<IResult> SubmitAsync(CancellationToken cancellationToken)
    {
        var organization = await orgs.Organizations.SingleAsync(o => o.Id == OrgId, cancellationToken);
        if (organization.OnboardingStatus != OnboardingStatus.Draft)
        {
            return Problems.ConflictState("Only a Draft organization can be submitted.");
        }

        var missing = new List<(string Field, string Code)>();
        if (string.IsNullOrWhiteSpace(organization.LegalName))
        {
            missing.Add(("legalName", "required"));
        }

        if (organization.Mb is null)
        {
            missing.Add(("mb", "required"));
        }

        if (!await orgs.LegalRepresentatives.AnyAsync(cancellationToken))
        {
            missing.Add(("legalRepresentatives", "required"));
        }

        if (!await orgs.TermsAcceptances.AnyAsync(t => t.TermsVersion == TermsVersions.Current, cancellationToken))
        {
            missing.Add(("termsAcceptance", "required"));
        }

        if (missing.Count > 0)
        {
            return Problems.Error(StatusCodes.Status422UnprocessableEntity, "organization_incomplete", "Required organization data is missing.",
                missing.Select(m => new { field = m.Field, code = m.Code }).ToList());
        }

        organization.OnboardingStatus = OnboardingStatus.Submitted;
        organization.SubmittedAt = timeProvider.GetUtcNow();
        return await SaveAsync(
            async ct =>
            {
                await audit.WriteAsync(new AuditEntry("Organization.Submitted", "Organization", organization.Id, organization.Id,
                    PreviousState: nameof(OnboardingStatus.Draft), NewState: nameof(OnboardingStatus.Submitted)), ct);
                await outbox.AddAsync(new OrganizationSubmitted(organization.Id, organization.OrgType), ct);
            },
            () => WithETag(Details(organization), organization.Version),
            cancellationToken);
    }

    public async Task<IResult> ListMembersAsync(CancellationToken cancellationToken) =>
        Results.Ok(await orgs.Memberships.AsNoTracking()
            .Where(m => m.Status == MembershipStatus.Active)
            .OrderBy(m => m.JoinedAt)
            .Select(m => new { id = m.Id, userId = m.UserId, role = m.Role, isSignatory = m.IsSignatory, legalRepresentativeId = m.LegalRepresentativeId, version = m.Version })
            .ToListAsync(cancellationToken));

    public async Task<IResult> InviteAsync(InviteMemberRequest body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Email) || body.Email.Length > 256 || !body.Email.Contains('@', StringComparison.Ordinal))
        {
            return Problems.Validation("email", "invalid_email", "A valid email address is required.");
        }

        var organization = await orgs.Organizations.SingleAsync(o => o.Id == OrgId, cancellationToken);
        if (organization.OrgType == OrganizationType.Seller && await AtSellerLimitAsync(cancellationToken))
        {
            return Problems.Error(StatusCodes.Status422UnprocessableEntity, "member_limit_reached", "This organization cannot have more members.");
        }

        SerializeMembershipChanges(organization);
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var now = timeProvider.GetUtcNow();
        var invitation = new Invitation
        {
            Id = SequentialGuid.NewGuid(),
            OrgId = organization.Id,
            Email = body.Email.Trim(),
            TokenHash = Hash(token),
            CreatedAt = now,
            ExpiresAt = now + InvitationValidity,
            InvitedBy = UserId,
        };
        orgs.Invitations.Add(invitation);
        var result = await SaveAsync(
            async ct => await audit.WriteAsync(new AuditEntry("Member.Invited", "Organization", organization.Id, organization.Id,
                Changes: new Dictionary<string, string?> { ["email"] = Masking.Email(invitation.Email) },
                Related: [new AuditReference("Invitation", invitation.Id)]), ct),
            () => Results.Accepted(value: new { invitationId = invitation.Id, expiresAt = invitation.ExpiresAt }),
            cancellationToken);
        await email.SendAsync(
            new EmailMessage(invitation.Email, "Poziv u organizaciju na platformi Cesio",
                $"Pozvani ste da se pridružite organizaciji {organization.LegalName ?? organization.Pib} na platformi Cesio: {options.Value.PublicBaseUrl.TrimEnd('/')}/invitations/accept?token={token}"),
            cancellationToken);
        return result;
    }

    /// <summary>The invited user accepts with the emailed token; the token only works for the invited email address.</summary>
    public async Task<IResult> AcceptInvitationAsync(AcceptInvitationRequest body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Token) || body.Token.Length > 100)
        {
            return Results.NotFound();
        }

        var hash = Hash(body.Token);
        var now = timeProvider.GetUtcNow();
        var invitation = await orgs.Invitations.IgnoreQueryFilters()
            .SingleOrDefaultAsync(i => i.TokenHash == hash && i.AcceptedAt == null && i.ExpiresAt > now, cancellationToken);
        var userEmail = await userDirectory.GetEmailAsync(UserId, cancellationToken);
        if (invitation is null || !string.Equals(invitation.Email, userEmail, StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound();
        }

        var organization = await orgs.Organizations.IgnoreQueryFilters().SingleAsync(o => o.Id == invitation.OrgId, cancellationToken);
        var membership = await orgs.Memberships.IgnoreQueryFilters().SingleOrDefaultAsync(m => m.OrgId == organization.Id && m.UserId == UserId, cancellationToken);
        if (membership is { Status: MembershipStatus.Active })
        {
            return Problems.Error(StatusCodes.Status409Conflict, "already_member", "You are already a member of this organization.");
        }

        if (organization.OrgType == OrganizationType.Seller
            && await orgs.Memberships.IgnoreQueryFilters().CountAsync(m => m.OrgId == organization.Id && m.Status == MembershipStatus.Active, cancellationToken)
                >= await parameters.GetAsync(MaxSellerMembersParameter, 1, cancellationToken))
        {
            return Problems.Error(StatusCodes.Status422UnprocessableEntity, "member_limit_reached", "This organization cannot have more members.");
        }

        invitation.AcceptedAt = now;
        invitation.AcceptedBy = UserId;
        SerializeMembershipChanges(organization);
        if (membership is null)
        {
            membership = new Membership { Id = SequentialGuid.NewGuid(), OrgId = organization.Id, UserId = UserId, Status = MembershipStatus.Active, JoinedAt = now };
            orgs.Memberships.Add(membership);
        }
        else
        {
            membership.Status = MembershipStatus.Active;
            membership.IsSignatory = false;
            membership.JoinedAt = now;
        }

        return await SaveAsync(
            async ct => await audit.WriteAsync(new AuditEntry("Member.Added", "Organization", organization.Id, organization.Id,
                Related: [new AuditReference("Membership", membership.Id), new AuditReference("Invitation", invitation.Id)]), ct),
            () => Results.Ok(new { organizationId = organization.Id }),
            cancellationToken);
    }

    /// <summary>
    /// Sets or clears the Signatory flag. It needs a link to a legal representative or a power-of-attorney document;
    /// a seller's single account becomes Signatory only through its accepted pristupnica (task 007a).
    /// </summary>
    public async Task<IResult> UpdateMemberAsync(Guid memberId, UpdateMemberRequest body, int? ifMatch, CancellationToken cancellationToken)
    {
        var organization = await orgs.Organizations.SingleAsync(o => o.Id == OrgId, cancellationToken);
        if (await orgs.Memberships.SingleOrDefaultAsync(m => m.Id == memberId && m.Status == MembershipStatus.Active, cancellationToken) is not { } membership)
        {
            return Results.NotFound();
        }

        if (organization.OrgType == OrganizationType.Seller)
        {
            return Problems.Error(StatusCodes.Status422UnprocessableEntity, "signatory_via_pristupnica", "A seller's account becomes Signatory when its pristupnica is accepted.");
        }

        if (ifMatch is not { } expected)
        {
            return Problems.PreconditionRequired();
        }

        if (expected != membership.Version)
        {
            return Problems.ConcurrencyConflict();
        }

        if (body.LegalRepresentativeId is { } representativeId && !await orgs.LegalRepresentatives.AnyAsync(r => r.Id == representativeId, cancellationToken))
        {
            return Problems.Validation("legalRepresentativeId", "not_found", "The legal representative does not exist.");
        }

        // A power of attorney needs a stored, verified document (tasks 006 and 007); until then it cannot qualify a
        // Signatory. Legal representatives entered here are verified by staff in task 007 before the organization is
        // Approved, and only Approved organizations can transact (INV-A1).
        if (body.PowerOfAttorneyDocumentId is not null)
        {
            return Problems.Error(StatusCodes.Status422UnprocessableEntity, "power_of_attorney_unverified",
                "Powers of attorney can be used once document verification is available.");
        }

        if (body.IsSignatory && body.LegalRepresentativeId is null)
        {
            return Problems.Error(StatusCodes.Status422UnprocessableEntity, "signatory_requires_representative",
                "A Signatory must be linked to a legal representative or a power-of-attorney document.");
        }

        var previous = membership.IsSignatory;
        membership.IsSignatory = body.IsSignatory;
        membership.LegalRepresentativeId = body.LegalRepresentativeId;
        membership.PowerOfAttorneyDocumentId = body.PowerOfAttorneyDocumentId;
        return await SaveAsync(
            async ct => await audit.WriteAsync(new AuditEntry("Member.SignatoryChanged", "Organization", organization.Id, organization.Id,
                Changes: new Dictionary<string, string?> { ["isSignatory"] = $"{previous} → {body.IsSignatory}" },
                Related: [new AuditReference("Membership", membership.Id)]), ct),
            () => Results.Ok(new { id = membership.Id, isSignatory = membership.IsSignatory, version = membership.Version }),
            cancellationToken);
    }

    public async Task<IResult> RemoveMemberAsync(Guid memberId, CancellationToken cancellationToken)
    {
        if (await orgs.Memberships.SingleOrDefaultAsync(m => m.Id == memberId && m.Status == MembershipStatus.Active, cancellationToken) is not { } membership)
        {
            return Results.NotFound();
        }

        // Every member is an Owner, so the last active member is the last Owner.
        if (!await orgs.Memberships.AnyAsync(m => m.Id != memberId && m.Status == MembershipStatus.Active, cancellationToken))
        {
            return Problems.Error(StatusCodes.Status409Conflict, "last_owner", "The last Owner cannot be removed.");
        }

        membership.Status = MembershipStatus.Removed;
        membership.IsSignatory = false;
        membership.LegalRepresentativeId = null;
        membership.PowerOfAttorneyDocumentId = null;
        SerializeMembershipChanges(await orgs.Organizations.SingleAsync(o => o.Id == membership.OrgId, cancellationToken));
        return await SaveAsync(
            async ct => await audit.WriteAsync(new AuditEntry("Member.Removed", "Organization", membership.OrgId, membership.OrgId,
                Related: [new AuditReference("Membership", membership.Id)]), ct),
            Results.NoContent,
            cancellationToken);
    }

    public async Task<IResult> ListRepresentativesAsync(CancellationToken cancellationToken) =>
        Results.Ok(await orgs.LegalRepresentatives.AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new { id = r.Id, name = r.Name, role = r.Role, source = r.Source, validFrom = r.ValidFrom, validTo = r.ValidTo, linkedUserId = r.LinkedUserId, version = r.Version })
            .ToListAsync(cancellationToken));

    public async Task<IResult> AddRepresentativeAsync(LegalRepresentativeRequest body, CancellationToken cancellationToken)
    {
        if (await DraftOrConflictAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        if (Invalid(body) is { } invalid)
        {
            return invalid;
        }

        var representative = new LegalRepresentative
        {
            Id = SequentialGuid.NewGuid(),
            OrgId = OrgId,
            Name = body.Name.Trim(),
            Role = body.Role.Trim(),
            Source = "Document",
            ValidFrom = body.ValidFrom,
            ValidTo = body.ValidTo,
            LinkedUserId = body.LinkedUserId,
        };
        orgs.LegalRepresentatives.Add(representative);
        return await SaveAsync(
            async ct => await audit.WriteAsync(new AuditEntry("LegalRepresentative.Added", "Organization", OrgId, OrgId,
                Related: [new AuditReference("LegalRepresentative", representative.Id)]), ct),
            () => Results.Created($"/api/v1/organizations/{OrgId}/legal-representatives/{representative.Id}", new { id = representative.Id, version = representative.Version }),
            cancellationToken);
    }

    public async Task<IResult> UpdateRepresentativeAsync(Guid representativeId, LegalRepresentativeRequest body, CancellationToken cancellationToken)
    {
        if (await orgs.LegalRepresentatives.SingleOrDefaultAsync(r => r.Id == representativeId, cancellationToken) is not { } representative)
        {
            return Results.NotFound();
        }

        if (await DraftOrConflictAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        if (Invalid(body) is { } invalid)
        {
            return invalid;
        }

        representative.Name = body.Name.Trim();
        representative.Role = body.Role.Trim();
        representative.ValidFrom = body.ValidFrom;
        representative.ValidTo = body.ValidTo;
        representative.LinkedUserId = body.LinkedUserId;
        return await SaveAsync(
            async ct => await audit.WriteAsync(new AuditEntry("LegalRepresentative.Updated", "Organization", OrgId, OrgId,
                Related: [new AuditReference("LegalRepresentative", representative.Id)]), ct),
            () => Results.Ok(new { id = representative.Id, version = representative.Version }),
            cancellationToken);
    }

    public async Task<IResult> RemoveRepresentativeAsync(Guid representativeId, CancellationToken cancellationToken)
    {
        if (await orgs.LegalRepresentatives.SingleOrDefaultAsync(r => r.Id == representativeId, cancellationToken) is not { } representative)
        {
            return Results.NotFound();
        }

        if (await DraftOrConflictAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        if (await orgs.Memberships.AnyAsync(m => m.LegalRepresentativeId == representativeId && m.Status == MembershipStatus.Active, cancellationToken))
        {
            return Problems.ConflictState("The legal representative is linked to a member.");
        }

        orgs.LegalRepresentatives.Remove(representative);
        return await SaveAsync(
            async ct => await audit.WriteAsync(new AuditEntry("LegalRepresentative.Removed", "Organization", OrgId, OrgId,
                Related: [new AuditReference("LegalRepresentative", representative.Id)]), ct),
            Results.NoContent,
            cancellationToken);
    }

    public async Task<IResult> AcceptTermsAsync(AcceptTermsRequest body, CancellationToken cancellationToken)
    {
        if (body.TermsVersion != TermsVersions.Current)
        {
            return Problems.Error(StatusCodes.Status422UnprocessableEntity, "terms_version_mismatch", "Accept the current version of the Terms of Use.");
        }

        // Who may accept for a factor organization, and in what form, is open (LQ-73): no acceptor rule is enforced.
        var acceptance = new TermsAcceptance
        {
            Id = SequentialGuid.NewGuid(),
            OrgId = OrgId,
            TermsVersion = body.TermsVersion,
            UserId = UserId,
            AcceptedAt = timeProvider.GetUtcNow(),
            IpAddress = request.IpAddress,
        };
        orgs.TermsAcceptances.Add(acceptance);
        return await SaveAsync(
            async ct => await audit.WriteAsync(new AuditEntry("Organization.TermsAccepted", "Organization", OrgId, OrgId,
                Changes: new Dictionary<string, string?> { ["termsVersion"] = acceptance.TermsVersion }), ct),
            () => Results.Created($"/api/v1/organizations/{OrgId}/terms-acceptances/{acceptance.Id}", new { id = acceptance.Id, termsVersion = acceptance.TermsVersion, acceptedAt = acceptance.AcceptedAt }),
            cancellationToken);
    }

    public async Task<IResult> ListTermsAcceptancesAsync(CancellationToken cancellationToken) =>
        Results.Ok(await orgs.TermsAcceptances.AsNoTracking()
            .OrderByDescending(t => t.AcceptedAt)
            .Select(t => new { id = t.Id, termsVersion = t.TermsVersion, userId = t.UserId, acceptedAt = t.AcceptedAt })
            .ToListAsync(cancellationToken));

    private async Task<Organization> CreateAsync(
        OrganizationType type, Pib pib, string? legalName, DateTimeOffset? claimedAt, string action, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var organization = new Organization
        {
            Id = SequentialGuid.NewGuid(),
            OrgType = type,
            Pib = pib.Value,
            LegalName = legalName,
            ClaimedAt = claimedAt,
            OnboardingStatus = OnboardingStatus.Draft,
            CreatedAt = now,
            CreatedBy = UserId,
        };
        orgs.Organizations.Add(organization);
        orgs.Memberships.Add(new Membership { Id = SequentialGuid.NewGuid(), OrgId = organization.Id, UserId = UserId, Status = MembershipStatus.Active, JoinedAt = now });
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await orgs.SaveChangesAsync(ct);
                await audit.WriteAsync(new AuditEntry(action, "Organization", organization.Id, organization.Id), ct);
            },
            cancellationToken);
        return organization;
    }

    /// <summary>Saves the tracked changes and writes <paramref name="audited"/> in one transaction; a lost update is a 412.</summary>
    private async Task<IResult> SaveAsync(Func<CancellationToken, Task> audited, Func<IResult> result, CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    await orgs.SaveChangesAsync(ct);
                    await audited(ct);
                },
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problems.ConcurrencyConflict();
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 1205 })
        {
            // Deadlock victim between two concurrent membership changes: the other one won.
            return Problems.ConcurrencyConflict();
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // A unique index: an MB already claimed by another organization, or a duplicate membership.
            return Problems.Error(StatusCodes.Status409Conflict, "already_exists", "This organization or membership already exists.");
        }

        return result();
    }

    /// <summary>
    /// Marks the organization row as changed, so its version serialises concurrent membership changes: of two
    /// Owners removing each other, or two acceptances over the seller limit, one gets 412 (last-Owner and limit checks).
    /// </summary>
    private void SerializeMembershipChanges(Organization organization) =>
        orgs.Entry(organization).Property(o => o.Version).IsModified = true;

    private async Task<bool> AtSellerLimitAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var members = await orgs.Memberships.CountAsync(m => m.Status == MembershipStatus.Active, cancellationToken);
        var pending = await orgs.Invitations.CountAsync(i => i.AcceptedAt == null && i.ExpiresAt > now, cancellationToken);
        return members + pending >= await parameters.GetAsync(MaxSellerMembersParameter, 1, cancellationToken);
    }

    private async Task<IResult?> DraftOrConflictAsync(CancellationToken cancellationToken) =>
        await orgs.Organizations.AnyAsync(o => o.OnboardingStatus == OnboardingStatus.Draft, cancellationToken)
            ? null
            : Problems.ConflictState("Legal representatives can be changed only while the organization is a Draft.");

    private static IResult? Invalid(LegalRepresentativeRequest body) =>
        string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 200 || string.IsNullOrWhiteSpace(body.Role) || body.Role.Length > 200
            ? Problems.Validation("name", "required", "Name and role are required (at most 200 characters).")
            : null;

    private static void Set(string? current, string? value, Action<string?> assign, string field, Dictionary<string, string?> changes)
    {
        if (value is not null && value != current)
        {
            assign(value.Length == 0 ? null : value);
            changes[field] = value;
        }
    }

    private static object Details(Organization o) => new
    {
        id = o.Id,
        type = o.OrgType,
        status = o.OnboardingStatus,
        pib = o.Pib,
        mb = o.Mb,
        legalName = o.LegalName,
        legalForm = o.LegalForm,
        registeredAddress = o.RegisteredAddress,
        claimed = o.ClaimedAt is not null,
        version = o.Version,
    };

    private static ETagResult WithETag(object value, int version) => new(Results.Ok(value), version);

    private static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>Adds <c>ETag: "version"</c> (api.md §1 concurrency).</summary>
    private sealed class ETagResult(IResult inner, int version) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.ETag = $"\"{version}\"";
            return inner.ExecuteAsync(httpContext);
        }
    }
}

internal static class Problems
{
    public static IResult Validation(string field, string code, string message) =>
        Error(StatusCodes.Status400BadRequest, "validation_failed", message, new[] { new { field, code, message } });

    public static IResult ConflictState(string detail) => Error(StatusCodes.Status409Conflict, "conflict_state", detail);

    public static IResult PreconditionRequired() =>
        Error(StatusCodes.Status428PreconditionRequired, "precondition_required", "Send If-Match with the version you last read.");

    public static IResult ConcurrencyConflict() =>
        Error(StatusCodes.Status412PreconditionFailed, "concurrency_conflict", "The resource was changed by someone else. Reload it and try again.");

    public static IResult Error(int status, string errorCode, string detail, object? errors = null)
    {
        var extensions = new Dictionary<string, object?> { ["errorCode"] = errorCode };
        if (errors is not null)
        {
            extensions["errors"] = errors;
        }

        return Results.Problem(detail: detail, statusCode: status, extensions: extensions);
    }
}
