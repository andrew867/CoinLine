using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using HostPlatform.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace HostPlatform.Api.Security;

public sealed class OperatorAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IWebHostEnvironment environment,
    OperatorTokenSigningKey signingKey) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "CoinLineOperator";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        const string bearer = "Bearer ";
        var authorization = Request.Headers.Authorization.ToString();
        if (authorization.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
        {
            var token = authorization[bearer.Length..].Trim();
            return Task.FromResult(OperatorToken.TryRead(token, signingKey.Value, out var username, out var role)
                ? AuthenticateOperator(username, role)
                : AuthenticateResult.Fail("Invalid operator token."));
        }

        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            var username = Request.Headers["X-Operator-Id"].ToString().Trim();
            if (username.Length > 0)
            {
                var role = Enum.TryParse<OperatorRole>(Request.Headers["X-Operator-Role"].ToString(), true, out var requested)
                    && Enum.IsDefined(requested) ? requested : OperatorRole.Operator;
                return Task.FromResult(AuthenticateOperator(username, role));
            }
        }

        return Task.FromResult(AuthenticateResult.NoResult());
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

public sealed class OperatorTokenSigningKey(IConfiguration configuration, IWebHostEnvironment environment)
{
    public string Value { get; } = configuration["Security:OperatorTokenKey"] is { } key && !string.IsNullOrWhiteSpace(key)
        ? key
        : environment.IsDevelopment() || environment.IsEnvironment("Testing")
            ? "coinline-lab-token-key"
            : Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
