using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Domain;

namespace HostPlatform.Tests.Integration;

[Collection("IntegrationApi")]
public sealed class RatingTranche5Tests(ApiFixture factory)
{
    private readonly HttpClient _client = CreateClient(factory);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Quote_local_and_ld_and_blocked_and_emergency()
    {
        var planId = await GetLocalDefaultPlanIdAsync();
        var cust = await GetCustomerIdForPlanAsync(planId);

        var local = await _client.PostAsJsonAsync("/api/rating/quote", new
        {
            dialedDigits = "5551234",
            mode = RatingMode.RealTimeRated,
            ratePlanId = planId,
            customerId = cust,
            assumedDurationMinutes = 1m
        });
        Assert.Equal(HttpStatusCode.OK, local.StatusCode);
        var lj = JsonSerializer.Deserialize<JsonElement>(await local.Content.ReadAsStringAsync(), JsonOpts);
        Assert.Equal((int)RatingDecisionKind.Allowed, lj.GetProperty("decisionKind").GetInt32());
        Assert.Equal(0.02m, lj.GetProperty("amountUsd").GetDecimal());

        var ld = await _client.PostAsJsonAsync("/api/rating/quote", new
        {
            dialedDigits = "12125551212",
            mode = RatingMode.RealTimeRated,
            ratePlanId = planId,
            assumedDurationMinutes = 1m
        });
        var ldj = JsonSerializer.Deserialize<JsonElement>(await ld.Content.ReadAsStringAsync(), JsonOpts);
        Assert.Equal(0.05m, ldj.GetProperty("amountUsd").GetDecimal());

        var blk = await _client.PostAsJsonAsync("/api/rating/quote", new
        {
            dialedDigits = "19005551212",
            mode = RatingMode.RealTimeRated,
            ratePlanId = planId,
            assumedDurationMinutes = 1m
        });
        var bj = JsonSerializer.Deserialize<JsonElement>(await blk.Content.ReadAsStringAsync(), JsonOpts);
        Assert.Equal((int)RatingDecisionKind.Blocked, bj.GetProperty("decisionKind").GetInt32());

        var em = await _client.PostAsJsonAsync("/api/rating/quote", new
        {
            dialedDigits = "911",
            mode = RatingMode.RealTimeRated,
            ratePlanId = planId,
            assumedDurationMinutes = 1m
        });
        var ej = JsonSerializer.Deserialize<JsonElement>(await em.Content.ReadAsStringAsync(), JsonOpts);
        Assert.Equal((int)RatingDecisionKind.Emergency, ej.GetProperty("decisionKind").GetInt32());
        Assert.Equal(0m, ej.GetProperty("amountUsd").GetDecimal());
    }

    [Fact]
    public async Task Authorize_insufficient_balance_overrides_allowed()
    {
        var planId = await GetLocalDefaultPlanIdAsync();
        var auth = await _client.PostAsJsonAsync("/api/rating/authorize", new
        {
            dialedDigits = "5559999",
            mode = RatingMode.RealTimeRated,
            ratePlanId = planId,
            assumedDurationMinutes = 1m,
            availableBalanceUsd = 0.01m
        });
        Assert.Equal(HttpStatusCode.OK, auth.StatusCode);
        var j = JsonSerializer.Deserialize<JsonElement>(await auth.Content.ReadAsStringAsync(), JsonOpts);
        Assert.True(j.GetProperty("insufficientBalance").GetBoolean());
        Assert.Equal((int)RatingDecisionKind.InsufficientBalance, j.GetProperty("effectiveDecisionKind").GetInt32());
    }

    [Fact]
    public async Task Rate_plan_version_publish_and_quote_end_to_end()
    {
        var cust = (await _client.GetFromJsonAsync<JsonElement>("/api/customers"))![0].GetProperty("id").GetGuid();
        var create = await _client.PostAsJsonAsync("/api/rate-plans", new
        {
            name = $"E2E plan {Guid.NewGuid():N}",
            customerId = cust,
            mode = RatingMode.RealTimeRated
        });
        var planId = JsonSerializer.Deserialize<JsonElement>(await create.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();

        var ver = await _client.PostAsJsonAsync($"/api/rate-plans/{planId}/versions", new { cloneFromVersionId = (Guid?)null });
        var verId = JsonSerializer.Deserialize<JsonElement>(await ver.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();

        await _client.PostAsJsonAsync($"/api/rate-plans/{planId}/versions/{verId}/rules", new
        {
            priority = 100,
            matchKind = RateRuleMatchKind.Prefix,
            pattern = "888",
            outcome = RateRuleOutcome.Rated,
            ratePerMinuteUsd = 0.99m,
            expression = "{}"
        });

        var pub = await _client.PostAsJsonAsync($"/api/rate-plans/{planId}/publish", new { ratePlanVersionId = verId, confirm = true });
        Assert.Equal(HttpStatusCode.OK, pub.StatusCode);

        var q = await _client.PostAsJsonAsync("/api/rating/quote", new
        {
            dialedDigits = "8881234",
            mode = RatingMode.RealTimeRated,
            ratePlanId = planId,
            assumedDurationMinutes = 1m
        });
        var qj = JsonSerializer.Deserialize<JsonElement>(await q.Content.ReadAsStringAsync(), JsonOpts);
        Assert.Equal(0.99m, qj.GetProperty("amountUsd").GetDecimal());
    }

    [Fact]
    public async Task Call_record_post_and_get_with_diagnostics()
    {
        var planId = await GetLocalDefaultPlanIdAsync();
        var post = await _client.PostAsJsonAsync("/api/call-records", new
        {
            terminalId = (Guid?)null,
            dialedDigits = "5550000",
            mode = RatingMode.RealTimeRated,
            startedAtUtc = DateTime.UtcNow,
            endedAtUtc = (DateTime?)null,
            ratePlanId = planId,
            assumedDurationMinutes = 1m
        });
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        var id = JsonSerializer.Deserialize<JsonElement>(await post.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        var get = await _client.GetAsync($"/api/call-records/{id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var doc = JsonSerializer.Deserialize<JsonElement>(await get.Content.ReadAsStringAsync(), JsonOpts);
        Assert.True(doc.GetProperty("results").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Number_classes_list_ok()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/number-classes")).StatusCode);
    }

    [Fact]
    public async Task Quote_tariff_catalog_uses_destination_or_peak_time_band()
    {
        var planId = await GetLocalDefaultPlanIdAsync();
        var inPeak = new DateTime(2026, 1, 6, 12, 0, 0, DateTimeKind.Utc);
        var rPeak = await _client.PostAsJsonAsync("/api/rating/quote", new
        {
            dialedDigits = "3331000",
            mode = RatingMode.RealTimeRated,
            ratePlanId = planId,
            assumedDurationMinutes = 1m,
            asOfUtc = inPeak
        });
        var jPeak = JsonSerializer.Deserialize<JsonElement>(await rPeak.Content.ReadAsStringAsync(), JsonOpts);
        Assert.Equal(0.15m, jPeak.GetProperty("amountUsd").GetDecimal());
        Assert.Equal("TimeBand", jPeak.GetProperty("airtimeSource").GetString());

        var offPeak = new DateTime(2026, 1, 6, 14, 0, 0, DateTimeKind.Utc);
        var rOff = await _client.PostAsJsonAsync("/api/rating/quote", new
        {
            dialedDigits = "3331000",
            mode = RatingMode.RealTimeRated,
            ratePlanId = planId,
            assumedDurationMinutes = 1m,
            asOfUtc = offPeak
        });
        var jOff = JsonSerializer.Deserialize<JsonElement>(await rOff.Content.ReadAsStringAsync(), JsonOpts);
        Assert.Equal(0.03m, jOff.GetProperty("amountUsd").GetDecimal());
        Assert.Equal("DestinationPrefix", jOff.GetProperty("airtimeSource").GetString());
    }

    [Fact]
    public async Task Get_rate_plan_detail_and_reconcile_call_record()
    {
        var planId = await GetLocalDefaultPlanIdAsync();
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/rate-plans/{planId}")).StatusCode);

        var post = await _client.PostAsJsonAsync("/api/call-records", new
        {
            terminalId = (Guid?)null,
            dialedDigits = "5551111",
            mode = RatingMode.RealTimeRated,
            startedAtUtc = DateTime.UtcNow,
            ratePlanId = planId,
            assumedDurationMinutes = 1m
        });
        var recId = JsonSerializer.Deserialize<JsonElement>(await post.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        var rec = await _client.PostAsJsonAsync($"/api/call-records/{recId}/reconcile",
            new { status = ReconciliationStatus.Matched, note = "itest", confirm = true });
        Assert.Equal(HttpStatusCode.OK, rec.StatusCode);
    }

    [Fact]
    public async Task Put_rate_rule_on_draft_version()
    {
        var cust = (await _client.GetFromJsonAsync<JsonElement>("/api/customers"))![0].GetProperty("id").GetGuid();
        var create = await _client.PostAsJsonAsync("/api/rate-plans", new
        {
            name = $"Put-rule plan {Guid.NewGuid():N}",
            customerId = cust,
            mode = RatingMode.RealTimeRated
        });
        var planId = JsonSerializer.Deserialize<JsonElement>(await create.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        var ver = await _client.PostAsJsonAsync($"/api/rate-plans/{planId}/versions", new { cloneFromVersionId = (Guid?)null });
        var verId = JsonSerializer.Deserialize<JsonElement>(await ver.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        await _client.PostAsJsonAsync($"/api/rate-plans/{planId}/versions/{verId}/rules", new
        {
            priority = 50,
            matchKind = RateRuleMatchKind.Prefix,
            pattern = "777",
            outcome = RateRuleOutcome.Rated,
            ratePerMinuteUsd = 0.01m,
            expression = "{}"
        });
        var verDoc = await _client.GetFromJsonAsync<JsonElement>($"/api/rate-plans/{planId}/versions/{verId}");
        var ruleId = verDoc!.GetProperty("rules")[0].GetProperty("id").GetGuid();
        var put = await _client.PutAsJsonAsync($"/api/rate-rules/{ruleId}", new
        {
            priority = 99,
            matchKind = RateRuleMatchKind.Prefix,
            pattern = "777",
            outcome = RateRuleOutcome.Rated,
            ratePerMinuteUsd = 0.02m,
            expression = "{}"
        });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
    }

    [Fact]
    public async Task Number_class_blocked_requires_confirm()
    {
        var u = (Random.Shared.Next(1_000_000, 9_999_999)).ToString();
        var denied = await _client.PostAsJsonAsync("/api/number-classes", new
        {
            customerId = (Guid?)null,
            className = $"blocked-test-{u}",
            pattern = "8" + u,
            matchKind = RateRuleMatchKind.Prefix,
            isBlocked = true,
            isFree = false,
            isEmergency = false,
            sortOrder = 999,
            confirm = false
        });
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);

        var ok = await _client.PostAsJsonAsync("/api/number-classes", new
        {
            customerId = (Guid?)null,
            className = $"blocked-test-ok-{u}",
            pattern = "7" + u,
            matchKind = RateRuleMatchKind.Prefix,
            isBlocked = true,
            isFree = false,
            isEmergency = false,
            sortOrder = 998,
            confirm = true
        });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }

    private static HttpClient CreateClient(ApiFixture f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.TryAddWithoutValidation("X-Operator-Id", "itest@local");
        c.DefaultRequestHeaders.TryAddWithoutValidation("X-Operator-Role", "Admin");
        return c;
    }

    private async Task<Guid> GetLocalDefaultPlanIdAsync()
    {
        var plans = await _client.GetFromJsonAsync<JsonElement>("/api/rate-plans");
        foreach (var p in plans!.EnumerateArray())
        {
            if (p.GetProperty("name").GetString() == "Local default")
                return p.GetProperty("id").GetGuid();
        }

        throw new InvalidOperationException("Seed rate plan 'Local default' not found.");
    }

    private async Task<Guid?> GetCustomerIdForPlanAsync(Guid planId)
    {
        var plans = await _client.GetFromJsonAsync<JsonElement>("/api/rate-plans");
        foreach (var p in plans!.EnumerateArray())
        {
            if (p.GetProperty("id").GetGuid() != planId)
                continue;
            if (p.TryGetProperty("customerId", out var cid) && cid.ValueKind != JsonValueKind.Null)
                return cid.GetGuid();
        }

        return null;
    }
}
