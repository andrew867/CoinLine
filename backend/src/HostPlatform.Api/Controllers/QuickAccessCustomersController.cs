using HostPlatform.Api.Audit;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/quick-access-customers")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class QuickAccessCustomersController(HostPlatformDbContext db) : ControllerBase
{
    private const int PageSize = 50;
    public sealed record QuickAccessCustomerInput(string? Reference, string? Name, string? BillingComments = null,
        string? ContactName = null, string? Phone = null, string? Street = null, string? City = null,
        string? Province = null, string? PostalCode = null, string? Notes = null, int? Revision = null);
    public sealed record QuickAccessCustomerSearch(string? Query = null, int Page = 1, string Sort = "reference",
        bool IncludeArchived = false);

    [HttpPost("search")]
    public async Task<IActionResult> Search(QuickAccessCustomerSearch body, CancellationToken ct)
    {
        if (!ValidSearch(body)) return InvalidSearch();
        var query = Filter(body, body.IncludeArchived);
        var total = await query.CountAsync(ct);
        var rows = await query.Skip((body.Page - 1) * PageSize).Take(PageSize)
            .Select(c => new { c.Id, c.Reference, c.Name, revision = c.Version, c.Archived }).ToListAsync(ct);
        return Ok(new { rows, total, page = body.Page, pageSize = PageSize });
    }

    [HttpPost("lookup")]
    public async Task<IActionResult> Lookup(QuickAccessCustomerSearch body, CancellationToken ct)
    {
        if (!ValidSearch(body)) return InvalidSearch();
        var query = Filter(body, false);
        var total = await query.CountAsync(ct);
        var rows = await query.Skip((body.Page - 1) * PageSize).Take(PageSize)
            .Select(c => new { c.Reference, c.Name }).ToListAsync(ct);
        return Ok(new { rows, total, page = body.Page, pageSize = PageSize });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var row = await db.QuickAccessCustomers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);
        return row is null ? NotFound() : Ok(Details(row));
    }

    [HttpPost]
    public async Task<IActionResult> Create(QuickAccessCustomerInput body, CancellationToken ct)
    {
        if (!ValidReference(body.Reference)) return BadRequest(new { error = "Enter a nonzero six-digit customer reference." });
        if (await db.QuickAccessCustomers.AnyAsync(c => c.Reference == body.Reference, ct))
            return Duplicate();
        var row = new QuickAccessCustomer { Reference = body.Reference!, Version = 1 };
        try { Apply(row, Normalize(body)); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        db.QuickAccessCustomers.Add(row);
        Audit("create", row);
        return await Save(ct) ?? CreatedAtAction(nameof(Get), new { id = row.Id }, Details(row));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, QuickAccessCustomerInput body, CancellationToken ct)
    {
        var row = await db.QuickAccessCustomers.FindAsync([id], ct);
        if (row is null) return NotFound();
        if (row.Version != body.Revision || row.Archived) return Changed();
        if (row.Reference != body.Reference)
            return BadRequest(new { error = "A customer reference cannot be changed because existing quick access keys may use it." });
        QuickAccessCustomerInput normalized;
        try { normalized = Normalize(body); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        if (normalized == Input(row)) return Ok(Details(row));
        Apply(row, normalized);
        row.Version++;
        Audit("update", row);
        return await Save(ct) ?? Ok(Details(row));
    }

    [HttpDelete("{id:guid}")]
    public Task<IActionResult> Archive(Guid id, [FromQuery] int revision, [FromQuery] bool confirm, CancellationToken ct) =>
        confirm ? SetArchive(id, revision, true, ct)
            : Task.FromResult<IActionResult>(BadRequest(new { error = DestructiveOperationMessages.RepeatWithConfirmTrue }));

    [HttpPost("{id:guid}/restore")]
    public Task<IActionResult> Restore(Guid id, [FromQuery] int revision, CancellationToken ct) => SetArchive(id, revision, false, ct);

    private async Task<IActionResult> SetArchive(Guid id, int revision, bool archived, CancellationToken ct)
    {
        var row = await db.QuickAccessCustomers.FindAsync([id], ct);
        if (row is null) return NotFound();
        if (row.Version != revision) return Changed();
        if (row.Archived == archived) return NoContent();
        row.Archived = archived;
        row.Version++;
        Audit(archived ? "archive" : "restore", row);
        return await Save(ct) ?? NoContent();
    }

    private IQueryable<QuickAccessCustomer> Filter(QuickAccessCustomerSearch body, bool includeArchived)
    {
        var query = db.QuickAccessCustomers.AsNoTracking();
        if (!includeArchived) query = query.Where(c => !c.Archived);
        var term = (body.Query ?? "").Trim().ToUpperInvariant();
        if (term.Length != 0) query = query.Where(c => c.Reference.Contains(term) || c.Name.ToUpper().Contains(term));
        return body.Sort == "name" ? query.OrderBy(c => c.Name).ThenBy(c => c.Reference) : query.OrderBy(c => c.Reference);
    }

    private static bool ValidSearch(QuickAccessCustomerSearch body) =>
        body.Page is >= 1 and <= 1_000_000 && body.Sort is "reference" or "name"
        && (body.Query?.Length ?? 0) <= 160 && !(body.Query?.Any(char.IsControl) ?? false);
    private BadRequestObjectResult InvalidSearch() => BadRequest(new { error = "Enter a valid search, sort order and page." });
    private static bool ValidReference(string? reference) =>
        reference is { Length: 6 } && reference != "000000" && reference.All(char.IsAsciiDigit);

    private static QuickAccessCustomerInput Normalize(QuickAccessCustomerInput body)
    {
        var name = Clean(body.Name, 160, "Customer name");
        if (name.Length == 0) throw new InvalidOperationException("Customer name is required.");
        return body with { Name = name, BillingComments = Clean(body.BillingComments, 2000, "Billing comments", true),
            ContactName = Clean(body.ContactName, 40, "Contact name"), Phone = Clean(body.Phone, 64, "Phone"),
            Street = Clean(body.Street, 256, "Street"), City = Clean(body.City, 128, "City"),
            Province = Clean(body.Province, 128, "Province or state"), PostalCode = Clean(body.PostalCode, 32, "Postal or ZIP code"),
            Notes = Clean(body.Notes, 2000, "Notes", true) };
    }

    private static string Clean(string? value, int limit, string label, bool multiline = false)
    {
        var cleaned = (value ?? "").Trim();
        if (cleaned.Length > limit || cleaned.Any(ch => char.IsControl(ch) && !(multiline && ch is '\r' or '\n' or '\t')))
            throw new InvalidOperationException($"{label} must contain at most {limit} valid characters.");
        return cleaned;
    }

    private static QuickAccessCustomerInput Input(QuickAccessCustomer row) => new(row.Reference, row.Name, row.BillingComments,
        row.ContactName, row.Phone, row.Street, row.City, row.Province, row.PostalCode, row.Notes, row.Version);

    private static void Apply(QuickAccessCustomer row, QuickAccessCustomerInput body)
    {
        row.Name = body.Name!; row.BillingComments = body.BillingComments!; row.ContactName = body.ContactName!; row.Phone = body.Phone!;
        row.Street = body.Street!; row.City = body.City!; row.Province = body.Province!; row.PostalCode = body.PostalCode!; row.Notes = body.Notes!;
    }

    private static object Details(QuickAccessCustomer row) => new { row.Id, row.Reference, row.Name, row.BillingComments,
        row.ContactName, row.Phone, row.Street, row.City, row.Province, row.PostalCode, row.Notes, row.Archived, revision = row.Version };
    private void Audit(string action, QuickAccessCustomer row) =>
        ApiAudit.Write(db, HttpContext, "quick_access_customer", action, row.Id.ToString(), new { revision = row.Version, row.Archived });

    private async Task<IActionResult?> Save(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Changed(); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) { return Duplicate(); }
        return null;
    }
    private ConflictObjectResult Duplicate() => Conflict(new { error = "This reference is already in use, including archived directory entries." });
    private ConflictObjectResult Changed() => Conflict(new { error = "This directory entry changed or is archived. Reload it before editing." });
}
