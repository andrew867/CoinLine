using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using HostPlatform.Api.Audit;
using HostPlatform.Api.Authorization;
using HostPlatform.Api.Security;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(HostPlatformDbContext db, OperatorTokenSigningKey signingKey) : ControllerBase
{
    [HttpPost("login")]
    public async Task<ActionResult<object>> Login([FromBody] LoginDto body, CancellationToken ct)
    {
        var username = (body.Username ?? "").Trim();
        var user = await db.OperatorAccounts.FirstOrDefaultAsync(u => u.Username == username && u.Enabled, ct);
        if (user == null || !OperatorPassword.Verify(body.Password ?? "", user.PasswordHash))
            return Unauthorized(new { error = "Unknown username or password." });

        var expires = DateTimeOffset.UtcNow.AddHours(12);
        var token = OperatorToken.Issue(user.Username, user.Role, signingKey.Value, expires, user.SessionGeneration);
        return Ok(new
        {
            token,
            username = user.Username,
            displayName = user.DisplayName,
            role = user.Role.ToString(),
            permissions = EffectiveProfile(user),
            expiresAtUtc = expires
        });
    }

    [HttpGet("me")]
    public async Task<ActionResult<object>> Me(CancellationToken ct)
    {
        var username = OperatorContext.Current?.OperatorId ?? "";
        var user = await db.OperatorAccounts.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username && u.Enabled, ct);
        if (user == null)
            return Unauthorized(new { error = "Sign in again." });
        return Ok(new
        {
            user.Id, user.Username, user.DisplayName, role = user.Role.ToString(), user.Enabled,
            permissions = EffectiveProfile(user)
        });
    }

    [HttpGet("accounts")]
    [Authorize(Policy = Policies.RequireAdmin)]
    public async Task<ActionResult<IEnumerable<object>>> Accounts(CancellationToken ct)
    {
        var rows = await db.OperatorAccounts.AsNoTracking()
            .OrderBy(u => u.Username)
            .Select(u => new { u.Id, u.Username, u.DisplayName, role = u.Role.ToString(), u.Enabled })
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost("accounts")]
    [Authorize(Policy = Policies.RequireAdmin)]
    public async Task<ActionResult<object>> Create([FromBody] CreateAccountDto body, CancellationToken ct)
    {
        var username = (body.Username ?? "").Trim();
        var displayName = (body.DisplayName ?? "").Trim();
        var password = body.Password ?? "";
        if (username.Length < 2 || username.Length > 64 || username.Any(char.IsControl))
            return BadRequest(new { error = "Username must be 2 to 64 printable characters." });
        if (displayName.Length is < 2 or > 120)
            return BadRequest(new { error = "A person needs a display name of 2 to 120 characters." });
        if (password.Length < 8)
            return BadRequest(new { error = "Password must be at least 8 characters." });
        if (!Enum.TryParse<OperatorRole>(body.Role, true, out var role) || !Enum.IsDefined(role))
            return BadRequest(new { error = "Unknown role." });
        if (await db.OperatorAccounts.AnyAsync(u => u.Username == username, ct))
            return Conflict(new { error = "That username is already in use." });

        var user = new OperatorAccount
        {
            Username = username,
            DisplayName = displayName,
            PasswordHash = OperatorPassword.Hash(password),
            Role = role,
            Enabled = true
        };
        db.OperatorAccounts.Add(user);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Conflict(new { error = "That username is already in use." });
        }
        return Created($"/api/auth/accounts/{user.Id}", AccountView(user));
    }

    [HttpGet("permission-catalog")]
    [Authorize(Policy = Policies.RequireAdmin)]
    public ActionResult PermissionCatalog() => Ok(OperatorPermissions.Catalog);

    [HttpGet("accounts/{id:guid}/permissions")]
    [Authorize(Policy = Policies.RequireAdmin)]
    public async Task<ActionResult<object>> GetPermissions(Guid id, CancellationToken ct)
    {
        var user = await db.OperatorAccounts.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, ct);
        if (user == null)
            return NotFound();
        return Ok(ProfileView(user));
    }

    [HttpPut("accounts/{id:guid}/permissions")]
    [Authorize(Policy = Policies.RequireAdmin)]
    public async Task<ActionResult<object>> SetPermissions(Guid id, [FromBody] PermissionProfileDto body, CancellationToken ct)
    {
        if (!OperatorPermissions.TryNormalize(body.Permissions, out var profile))
            return BadRequest(new { error = "Select only supported operations from the permission catalogue." });
        var user = await db.OperatorAccounts.SingleOrDefaultAsync(u => u.Id == id, ct);
        if (user == null)
            return NotFound();
        if (user.Role == OperatorRole.Admin)
            return BadRequest(new { error = "Administrator accounts use their full role privileges." });
        if (user.SessionGeneration != body.Revision)
            return Conflict(new { error = "The profile changed. Reload it before saving." });
        var json = profile == null ? null : JsonSerializer.Serialize(profile);
        var current = EffectiveProfile(user);
        if (json == (current == null ? null : JsonSerializer.Serialize(current)))
            return Ok(ProfileView(user));
        user.PermissionProfileJson = json;
        user.SessionGeneration++;
        ApiAudit.Write(db, HttpContext, "people", "update_profile", $"OperatorAccount:{id}", new { permissions = profile });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { error = "The profile changed. Reload it before saving." });
        }
        return Ok(ProfileView(user));
    }

    [HttpPut("accounts/{id:guid}")]
    [Authorize(Policy = Policies.RequireAdmin)]
    public async Task<ActionResult<object>> Update(Guid id, [FromBody] UpdateAccountDto body, CancellationToken ct)
    {
        try
        {
            if (!db.Database.IsRelational() || (body.Role == null && body.Enabled == null))
                return await UpdateAccountAsync(id, body, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var result = await UpdateAccountAsync(id, body, ct);
            if (result.Result is OkObjectResult)
                await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || ex.GetBaseException() is PostgresException
            { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected })
        {
            return Conflict(new { error = "The account changed. Refresh People and try again." });
        }
    }

    private async Task<ActionResult<object>> UpdateAccountAsync(Guid id, UpdateAccountDto body, CancellationToken ct)
    {
        var user = await db.OperatorAccounts.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user == null)
            return NotFound();
        var previousRole = user.Role;
        var previousEnabled = user.Enabled;
        if (body.DisplayName != null)
        {
            var displayName = body.DisplayName.Trim();
            if (displayName.Length is < 2 or > 120)
                return BadRequest(new { error = "A person needs a display name of 2 to 120 characters." });
            user.DisplayName = displayName;
        }
        if (body.Role != null)
        {
            if (!Enum.TryParse<OperatorRole>(body.Role, true, out var role) || !Enum.IsDefined(role))
                return BadRequest(new { error = "Unknown role." });
            if (user.Role == OperatorRole.Admin && role != OperatorRole.Admin && await EnabledAdminCount(user.Id, ct) == 0)
                return Conflict(new { error = "Keep at least one enabled administrator." });
            user.Role = role;
            if (role == OperatorRole.Admin)
                user.PermissionProfileJson = null;
        }
        if (body.Enabled is { } enabled)
        {
            if (!enabled && user.Enabled && user.Role == OperatorRole.Admin && await EnabledAdminCount(user.Id, ct) == 0)
                return Conflict(new { error = "Keep at least one enabled administrator." });
            user.Enabled = enabled;
        }
        if (user.Role != previousRole || user.Enabled != previousEnabled)
            user.SessionGeneration++;
        await db.SaveChangesAsync(ct);
        return Ok(AccountView(user));
    }

    [HttpPost("password")]
    public async Task<ActionResult> ChangePassword([FromBody] PasswordDto body, CancellationToken ct)
    {
        var username = OperatorContext.Current?.OperatorId ?? "";
        var user = await db.OperatorAccounts.FirstOrDefaultAsync(u => u.Username == username && u.Enabled, ct);
        if (user == null)
            return Unauthorized(new { error = "Sign in again." });
        if (!OperatorPassword.Verify(body.CurrentPassword ?? "", user.PasswordHash))
            return BadRequest(new { error = "Current password is wrong." });
        if ((body.NewPassword ?? "").Length < 8)
            return BadRequest(new { error = "Password must be at least 8 characters." });
        user.PasswordHash = OperatorPassword.Hash(body.NewPassword!);
        user.SessionGeneration++;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { error = "The account changed. Sign in again before changing the password." });
        }
        return NoContent();
    }

    private async Task<int> EnabledAdminCount(Guid exceptId, CancellationToken ct) =>
        await db.OperatorAccounts.CountAsync(
            u => u.Id != exceptId && u.Enabled && u.Role == OperatorRole.Admin, ct);

    private static object AccountView(OperatorAccount user) => new
    {
        user.Id,
        user.Username,
        user.DisplayName,
        role = user.Role.ToString(),
        user.Enabled
    };

    private static Dictionary<string, int>? EffectiveProfile(OperatorAccount user) =>
        user.Role == OperatorRole.Admin ? null : OperatorPermissions.ReadProfile(user.PermissionProfileJson);

    private static object ProfileView(OperatorAccount user) => new
    {
        role = user.Role.ToString(),
        revision = user.SessionGeneration,
        permissions = EffectiveProfile(user)
    };

    public sealed record LoginDto(string? Username, string? Password);
    public sealed record CreateAccountDto(string? Username, string? DisplayName, string? Password, string? Role);
    public sealed record UpdateAccountDto(string? DisplayName, string? Role, bool? Enabled);
    public sealed record PasswordDto(string? CurrentPassword, string? NewPassword);
    public sealed record PermissionProfileDto([property: JsonRequired] int Revision,
        [property: JsonRequired] Dictionary<string, int>? Permissions);
}
