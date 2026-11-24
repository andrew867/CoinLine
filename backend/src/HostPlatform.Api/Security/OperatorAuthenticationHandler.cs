using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using HostPlatform.Api.Authorization;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HostPlatform.Api.Security;

public sealed class OperatorAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IWebHostEnvironment environment,
    OperatorTokenSigningKey signingKey,
    HostPlatformDbContext db) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "CoinLineOperator";
    internal const string NamedAccountClaim = "coinline:named-account";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        const string bearer = "Bearer ";
        var authorization = Request.Headers.Authorization.ToString();
        if (authorization.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
        {
            var token = authorization[bearer.Length..].Trim();
            if (!OperatorToken.TryRead(token, signingKey.Value, out var username, out var role, out var generation))
                return AuthenticateResult.Fail("Invalid operator token.");
            var account = await db.OperatorAccounts.AsNoTracking().Where(user => user.Username == username && user.Enabled)
                .Select(user => new { user.Role, user.SessionGeneration, user.PermissionProfileJson }).SingleOrDefaultAsync(Context.RequestAborted);
            if (account == null || account.Role != role || account.SessionGeneration != generation)
                return AuthenticateResult.Fail("The operator session is no longer current.");
            if (account.Role != OperatorRole.Admin)
                OperatorPermissions.SetRequestProfile(Context, account.PermissionProfileJson);
            return AuthenticateOperator(username, account.Role, namedAccount: true);
        }

        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            var username = Request.Headers["X-Operator-Id"].ToString().Trim();
            if (username.Length > 0)
            {
                var role = Enum.TryParse<OperatorRole>(Request.Headers["X-Operator-Role"].ToString(), true, out var requested)
                    && Enum.IsDefined(requested) ? requested : OperatorRole.Operator;
                return AuthenticateOperator(username, role);
            }
        }

        return AuthenticateResult.NoResult();
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        return base.HandleChallengeAsync(properties);
    }

    private static AuthenticateResult AuthenticateOperator(string username, OperatorRole role, bool namedAccount = false)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, username), new Claim(ClaimTypes.Role, role.ToString())], SchemeName);
        if (namedAccount)
            identity.AddClaim(new Claim(NamedAccountClaim, "true"));
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}

public sealed class OperatorTokenSigningKey(IConfiguration configuration, IWebHostEnvironment environment)
{
    public string Value { get; } = configuration["Security:OperatorTokenKey"] is { } key && !string.IsNullOrWhiteSpace(key)
        ? key
        : environment.IsDevelopment() || environment.IsEnvironment("Testing")
            ? "coinline-lab-token-key"
            : Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
