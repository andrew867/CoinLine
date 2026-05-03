using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HostPlatform.Tests.Integration;

[Collection("IntegrationApi")]
public sealed class TableDistributionTranche4Tests(ApiFixture factory)
{
    private readonly HttpClient _client = CreateClient(factory);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Publish_confirm_writes_tables_set_audit_event()
    {
        var cust = (await _client.GetFromJsonAsync<JsonElement>("/api/customers"))![0].GetProperty("id").GetGuid();
        var def = await _client.PostAsJsonAsync("/api/tables/definitions", new
        {
            name = "Audit pub table",
            tableNumber = 197,
            description = "audit"
        });
        var defId = JsonSerializer.Deserialize<JsonElement>(await def.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        var setId = JsonSerializer.Deserialize<JsonElement>(
            await (await _client.PostAsJsonAsync("/api/tables/sets", new { name = "audit set", customerId = cust, isDefault = false }))
                .Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        await _client.PostAsJsonAsync("/api/tables/versions", new
        {
            tableSetId = setId,
            tableDefinitionId = defId,
            tableRevision = 1,
            payloadBase64 = Convert.ToBase64String(new byte[] { 0x07 }),
            sortOrder = 0,
            dependsOnTableDefinitionId = (Guid?)null
        });
        Assert.Equal(HttpStatusCode.NoContent,
            (await _client.PostAsync($"/api/tables/sets/{setId}/publish?confirm=true", null)).StatusCode);

        var audits = await _client.GetFromJsonAsync<JsonElement>("/api/audit/events");
        var saw = false;
        foreach (var ev in audits!.EnumerateArray())
        {
            if (string.Equals(ev.GetProperty("category").GetString(), "tables.set", StringComparison.Ordinal)
                && string.Equals(ev.GetProperty("action").GetString(), "publish", StringComparison.Ordinal))
            {
                saw = true;
                break;
            }
        }

        Assert.True(saw);
    }

    [Fact]
    public async Task Seed_has_default_definitions_and_published_set()
    {
        var defs = await _client.GetFromJsonAsync<JsonElement>("/api/tables/definitions");
        Assert.True(defs!.GetArrayLength() >= 3);
        var sets = await _client.GetFromJsonAsync<JsonElement>("/api/tables/sets");
        var row = sets!.EnumerateArray().First(s => s.GetProperty("isDefault").GetBoolean());
        Assert.Equal(1, row.GetProperty("status").GetInt32()); // Published
        var id = row.GetProperty("id").GetGuid();
        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{id}");
        Assert.Equal(3, detail!.GetProperty("versions").GetArrayLength());
    }

    [Fact]
    public async Task Create_definition_version_set_publish_assign_download_cancel_retry_rollback()
    {
        var cust = (await _client.GetFromJsonAsync<JsonElement>("/api/customers"))![0].GetProperty("id").GetGuid();
        var def = await _client.PostAsJsonAsync("/api/tables/definitions", new
        {
            name = "ITest table",
            tableNumber = 199,
            description = "integration"
        });
        Assert.Equal(HttpStatusCode.Created, def.StatusCode);
        var defId = JsonSerializer.Deserialize<JsonElement>(await def.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();

        var set = await _client.PostAsJsonAsync("/api/tables/sets", new
        {
            name = "ITest set",
            customerId = cust,
            isDefault = false
        });
        var setId = JsonSerializer.Deserialize<JsonElement>(await set.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();

        var payload = new byte[] { 0xAB, 0xCD };
        var ver = await _client.PostAsJsonAsync("/api/tables/versions", new
        {
            tableSetId = setId,
            tableDefinitionId = defId,
            tableRevision = 1,
            payloadBase64 = Convert.ToBase64String(payload),
            sortOrder = 0,
            dependsOnTableDefinitionId = (Guid?)null
        });
        Assert.Equal(HttpStatusCode.Created, ver.StatusCode);
        var verBody = JsonSerializer.Deserialize<JsonElement>(await ver.Content.ReadAsStringAsync(), JsonOpts);
        Assert.True(verBody.GetProperty("validationPassed").GetBoolean());

        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsync($"/api/tables/sets/{setId}/publish", null)).StatusCode);
        var pub = await _client.PostAsync($"/api/tables/sets/{setId}/publish?confirm=true", null);
        Assert.Equal(HttpStatusCode.NoContent, pub.StatusCode);

        var term = (await _client.GetFromJsonAsync<JsonElement>("/api/terminals"))![0].GetProperty("id").GetGuid();

        var assign1 = await _client.PostAsJsonAsync($"/api/terminals/{term}/table-assignment", new
        {
            tableSetId = setId,
            customerId = (Guid?)null,
            siteId = (Guid?)null
        });
        Assert.Equal(HttpStatusCode.OK, assign1.StatusCode);

        var set2 = await _client.PostAsJsonAsync("/api/tables/sets", new
        {
            name = "ITest set B",
            customerId = cust,
            isDefault = false
        });
        var set2Id = JsonSerializer.Deserialize<JsonElement>(await set2.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        var ver2 = await _client.PostAsJsonAsync("/api/tables/versions", new
        {
            tableSetId = set2Id,
            tableDefinitionId = defId,
            tableRevision = 2,
            payloadBase64 = Convert.ToBase64String(new byte[] { 0x11 }),
            sortOrder = 0,
            dependsOnTableDefinitionId = (Guid?)null
        });
        Assert.Equal(HttpStatusCode.Created, ver2.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await _client.PostAsync($"/api/tables/sets/{set2Id}/publish?confirm=true", null)).StatusCode);

        var assign2 = await _client.PostAsJsonAsync($"/api/terminals/{term}/table-assignment", new
        {
            tableSetId = set2Id,
            customerId = (Guid?)null,
            siteId = (Guid?)null
        });
        Assert.Equal(HttpStatusCode.OK, assign2.StatusCode);

        var getAssign = await _client.GetFromJsonAsync<JsonElement>($"/api/terminals/{term}/table-assignment");
        Assert.Equal(set2Id, getAssign!.GetProperty("tableSetId").GetGuid());
        Assert.Equal(setId, getAssign.GetProperty("previousTableSetId").GetGuid());

        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsync($"/api/terminals/{term}/table-assignment/rollback", null)).StatusCode);
        var rb = await _client.PostAsync($"/api/terminals/{term}/table-assignment/rollback?confirm=true", null);
        Assert.Equal(HttpStatusCode.OK, rb.StatusCode);
        var afterRb = await _client.GetFromJsonAsync<JsonElement>($"/api/terminals/{term}/table-assignment");
        Assert.Equal(setId, afterRb!.GetProperty("tableSetId").GetGuid());

        var dl = await _client.PostAsJsonAsync($"/api/terminals/{term}/downloads", new { tableSetId = set2Id, scope = "Full" });
        Assert.Equal(HttpStatusCode.Created, dl.StatusCode);
        var batchId = JsonSerializer.Deserialize<JsonElement>(await dl.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        var batch = await _client.GetFromJsonAsync<JsonElement>($"/api/downloads/{batchId}");
        Assert.Equal(1, batch!.GetProperty("status").GetInt32()); // Running

        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsync($"/api/downloads/{batchId}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await _client.PostAsync($"/api/downloads/{batchId}/cancel?confirm=true", null)).StatusCode);
        var cancelled = await _client.GetFromJsonAsync<JsonElement>($"/api/downloads/{batchId}");
        Assert.Equal(4, cancelled!.GetProperty("status").GetInt32());

        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsync($"/api/downloads/{batchId}/retry", null)).StatusCode);
        var retry = await _client.PostAsync($"/api/downloads/{batchId}/retry?confirm=true", null);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        var retryBody = JsonSerializer.Deserialize<JsonElement>(await retry.Content.ReadAsStringAsync(), JsonOpts);
        Assert.Equal(1, retryBody.GetProperty("retryCount").GetInt32());
    }

    [Fact]
    public async Task Empty_table_payload_is_rejected_for_publish()
    {
        var cust = (await _client.GetFromJsonAsync<JsonElement>("/api/customers"))![0].GetProperty("id").GetGuid();
        var def = await _client.PostAsJsonAsync("/api/tables/definitions", new
        {
            name = "Empty payload def",
            tableNumber = 198,
            description = "x"
        });
        var defId = JsonSerializer.Deserialize<JsonElement>(await def.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        var setId = JsonSerializer.Deserialize<JsonElement>(
            await (await _client.PostAsJsonAsync("/api/tables/sets", new { name = "empty set", customerId = cust, isDefault = false }))
                .Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();

        var ver = await _client.PostAsJsonAsync("/api/tables/versions", new
        {
            tableSetId = setId,
            tableDefinitionId = defId,
            tableRevision = 1,
            payloadBase64 = "",
            sortOrder = 0,
            dependsOnTableDefinitionId = (Guid?)null
        });
        Assert.Equal(HttpStatusCode.Created, ver.StatusCode);
        var verBody = JsonSerializer.Deserialize<JsonElement>(await ver.Content.ReadAsStringAsync(), JsonOpts);
        Assert.False(verBody.GetProperty("validationPassed").GetBoolean());

        var pub = await _client.PostAsync($"/api/tables/sets/{setId}/publish?confirm=true", null);
        Assert.Equal(HttpStatusCode.BadRequest, pub.StatusCode);
    }

    [Fact]
    public async Task Partial_download_includes_dependency_closure()
    {
        var cust = (await _client.GetFromJsonAsync<JsonElement>("/api/customers"))![0].GetProperty("id").GetGuid();
        var dA = await _client.PostAsJsonAsync("/api/tables/definitions", new { name = "Dep A", tableNumber = 170, description = "a" });
        var dB = await _client.PostAsJsonAsync("/api/tables/definitions", new { name = "Dep B", tableNumber = 171, description = "b" });
        var idA = JsonSerializer.Deserialize<JsonElement>(await dA.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        var idB = JsonSerializer.Deserialize<JsonElement>(await dB.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();

        var setId = JsonSerializer.Deserialize<JsonElement>(
            await (await _client.PostAsJsonAsync("/api/tables/sets", new { name = "dep set", customerId = cust, isDefault = false }))
                .Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();

        await _client.PostAsJsonAsync("/api/tables/versions", new
        {
            tableSetId = setId,
            tableDefinitionId = idA,
            tableRevision = 1,
            payloadBase64 = Convert.ToBase64String(new byte[] { 0x01 }),
            sortOrder = 0,
            dependsOnTableDefinitionId = (Guid?)null
        });
        await _client.PostAsJsonAsync("/api/tables/versions", new
        {
            tableSetId = setId,
            tableDefinitionId = idB,
            tableRevision = 1,
            payloadBase64 = Convert.ToBase64String(new byte[] { 0x02 }),
            sortOrder = 1,
            dependsOnTableDefinitionId = idA
        });
        await _client.PostAsync($"/api/tables/sets/{setId}/publish?confirm=true", null);

        var term = (await _client.GetFromJsonAsync<JsonElement>("/api/terminals"))![0].GetProperty("id").GetGuid();
        var dl = await _client.PostAsJsonAsync($"/api/terminals/{term}/downloads", new
        {
            tableSetId = setId,
            scope = "Partial",
            partialTableDefinitionIds = new[] { idB }
        });
        Assert.Equal(HttpStatusCode.Created, dl.StatusCode);
        var batchId = JsonSerializer.Deserialize<JsonElement>(await dl.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/downloads/{batchId}");
        Assert.Equal(2, detail!.GetProperty("timeline").GetArrayLength());
    }

    private static HttpClient CreateClient(ApiFixture f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.TryAddWithoutValidation("X-Operator-Id", "itest@local");
        c.DefaultRequestHeaders.TryAddWithoutValidation("X-Operator-Role", "Admin");
        return c;
    }
}
