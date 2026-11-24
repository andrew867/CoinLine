using HostPlatform.Api.Authorization;
using HostPlatform.Api.Security;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
        var token = OperatorToken.Issue(user.Username, user.Role, signingKey.Value, expires);
        return Ok(new
        {
            token,
            username = user.Username,
            displayName = user.DisplayName,
            role = user.Role.ToString(),
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
        return Ok(AccountView(user));
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
        if (username.Length < 2 || username.Length > 64)
            return BadRequest(new { error = "Username must be 2 to 64 characters." });
        if (displayName.Length < 2)
            return BadRequest(new { error = "A person needs a display name." });
        if (password.Length < 8)
            return BadRequest(new { error = "Password must be at least 8 characters." });
        if (!Enum.TryParse<OperatorRole>(body.Role, true, out var role))
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
        await db.SaveChangesAsync(ct);
        return Created($"/api/auth/accounts/{user.Id}", AccountView(user));
    }

    [HttpPut("accounts/{id:guid}")]
    [Authorize(Policy = Policies.RequireAdmin)]
    public async Task<ActionResult<object>> Update(Guid id, [FromBody] UpdateAccountDto body, CancellationToken ct)
    {
        var user = await db.OperatorAccounts.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user == null)
            return NotFound();
        if (body.DisplayName != null)
        {
            var displayName = body.DisplayName.Trim();
            if (displayName.Length < 2)
                return BadRequest(new { error = "A person needs a display name." });
            user.DisplayName = displayName;
        }
        if (body.Role != null)
        {
            if (!Enum.TryParse<OperatorRole>(body.Role, true, out var role))
                return BadRequest(new { error = "Unknown role." });
            if (user.Role == OperatorRole.Admin && role != OperatorRole.Admin && await EnabledAdminCount(user.Id, ct) == 0)
                return Conflict(new { error = "Keep at least one enabled administrator." });
            user.Role = role;
        }
        if (body.Enabled is { } enabled)
        {
            if (!enabled && user.Enabled && user.Role == OperatorRole.Admin && await EnabledAdminCount(user.Id, ct) == 0)
                return Conflict(new { error = "Keep at least one enabled administrator." });
            user.Enabled = enabled;
        }
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
        await db.SaveChangesAsync(ct);
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

    public sealed record LoginDto(string? Username, string? Password);
    public sealed record CreateAccountDto(string? Username, string? DisplayName, string? Password, string? Role);
    public sealed record UpdateAccountDto(string? DisplayName, string? Role, bool? Enabled);
    public sealed record PasswordDto(string? CurrentPassword, string? NewPassword);
}
