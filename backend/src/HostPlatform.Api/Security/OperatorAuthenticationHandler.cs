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
    OperatorTokenSigningKey signingKey,
    HostPlatformDbContext db) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "CoinLineOperator";

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
            return AuthenticateOperator(username, account.Role);
        }

        return AuthenticateResult.NoResult();
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        return base.HandleChallengeAsync(properties);
    }

    private static AuthenticateResult AuthenticateOperator(string username, OperatorRole role)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, username), new Claim(ClaimTypes.Role, role.ToString())], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}

public sealed class OperatorTokenSigningKey
{
    public string Value { get; }

    public OperatorTokenSigningKey(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var key = configuration["COINLINE_OPERATOR_TOKEN_KEY"];
        if (string.IsNullOrWhiteSpace(key)) key = configuration["Security:OperatorTokenKey"];
        if (string.IsNullOrWhiteSpace(key))
        {
            if (!environment.IsDevelopment())
                throw new InvalidOperationException("Configure an operator token signing key before starting outside Development.");
            Value = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            return;
        }
        // The retired public default is rejected, never used to issue a token.
        if (key == "coinline-lab-token-key" || System.Text.Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException("Configure a new operator token signing key of at least 32 bytes.");
        Value = key;
    }
}
