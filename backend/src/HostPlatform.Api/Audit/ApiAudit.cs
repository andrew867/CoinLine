using System.Text.Json;
using HostPlatform.Api.Middleware;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;

namespace HostPlatform.Api.Audit;

/// <summary>Appends <see cref="AuditEvent"/> rows (caller must <see cref="HostPlatformDbContext.SaveChangesAsync"/>).</summary>
public static class ApiAudit
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    /// <param name="db">Database context — audit row is tracked until saved.</param>
    /// <param name="http">When null (e.g. background worker), correlation id is omitted but the audit row is still written.</param>
    /// <param name="category">Audit category (e.g. firmware.job).</param>
    /// <param name="action">Audit action (e.g. cancel).</param>
    /// <param name="resource">Resource identifier or description.</param>
    /// <param name="detail">Serializable detail payload.</param>
    /// <param name="terminalId">Optional terminal scope.</param>
    public static void Write(
        HostPlatformDbContext db,
        HttpContext? http,
        string category,
        string action,
        string resource,
        object detail,
        Guid? terminalId = null)
    {
        string? correlation = null;
        if (http != null)
        {
            correlation = http.Request.Headers[CorrelationIdMiddleware.HeaderName].FirstOrDefault()
                          ?? http.Items["CorrelationId"]?.ToString();
        }

        db.AuditEvents.Add(new AuditEvent
        {
            Category = category,
            Action = action,
            Actor = OperatorContext.Current?.OperatorId ?? "system",
            Resource = resource,
            DetailJson = JsonSerializer.Serialize(detail, JsonOpts),
            CorrelationId = correlation,
            TerminalId = terminalId
        });
    }
}
