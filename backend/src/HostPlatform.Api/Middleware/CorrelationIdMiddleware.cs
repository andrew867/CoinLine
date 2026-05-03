namespace HostPlatform.Api.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task Invoke(HttpContext ctx)
    {
        if (!ctx.Request.Headers.TryGetValue(HeaderName, out var cid) || string.IsNullOrWhiteSpace(cid))
            cid = Guid.NewGuid().ToString("N");
        var id = cid.ToString();
        ctx.Response.Headers[HeaderName] = id;
        ctx.Items["CorrelationId"] = id;
        using (logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = id }))
            await next(ctx);
    }
}
