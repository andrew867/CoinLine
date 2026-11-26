using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Integration;

[Collection("IntegrationApi")]
public sealed class TableDistributionTranche4Tests(ApiFixture factory)
{
    private readonly HttpClient _client = CreateClient(factory);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Imported_mt59_requires_a_layout_and_keeps_it_after_an_edit()
    {
        using var set = await _client.PostAsJsonAsync("/api/tables/sets", new { name = "Compact screening editor test" });
        set.EnsureSuccessStatusCode();
        var id = (await set.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var definition = await _client.PostAsJsonAsync("/api/tables/definitions", new { name = "Screening test record", tableNumber = 59 });
        definition.EnsureSuccessStatusCode();
        var definitionId = (await definition.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var layout = NativeTableLayouts.DraftTemplates(59).Single(item => item.Profile == CallScreeningWire.CompactProfile);
        var payload = NativeTableLayouts.CreateDraftTemplate(layout);
        using var imported = await _client.PostAsJsonAsync("/api/tables/versions", new
        {
            tableSetId = id, tableDefinitionId = definitionId, tableRevision = 1, payloadBase64 = Convert.ToBase64String(payload), sortOrder = 0
        });
        imported.EnsureSuccessStatusCode();

        var path = $"/api/tables/sets/{id}/records/59/fields";
        var ambiguous = await _client.GetFromJsonAsync<JsonElement>(path);
        Assert.False(ambiguous.GetProperty("supported").GetBoolean());
        Assert.Equal(2, ambiguous.GetProperty("profiles").GetArrayLength());
        var selected = await _client.GetFromJsonAsync<JsonElement>($"{path}?profile={Uri.EscapeDataString(layout.Profile)}");
        Assert.Equal(85, selected.GetProperty("sections").GetArrayLength());
        var changes = new Dictionary<string, string>();
        var edit = new { profile = layout.Profile, revision = 1, sha256 = selected.GetProperty("sha256"), changes };
        using var unselected = await _client.PutAsJsonAsync(path, edit with { profile = null! });
        Assert.Equal(HttpStatusCode.BadRequest, unselected.StatusCode);
        using var noChange = await _client.PutAsJsonAsync(path, edit);
        Assert.Equal(HttpStatusCode.NoContent, noChange.StatusCode);
        var afterNoChange = await _client.GetFromJsonAsync<JsonElement>(path);
        Assert.False(afterNoChange.GetProperty("supported").GetBoolean());

        changes["rule-84.pattern"] = "90?";
        using var changed = await _client.PutAsJsonAsync(path, edit);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        var reopened = await _client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(layout.Profile, reopened.GetProperty("profile").GetString());
        Assert.Single(reopened.GetProperty("profiles").EnumerateArray());
        Assert.Equal("90?", reopened.GetProperty("sections")[84].GetProperty("fields")[0].GetProperty("value").GetString());
        using var wrongLayout = await _client.PutAsJsonAsync(path, new
        {
            profile = CallScreeningWire.ExpandedProfile, revision = 2, sha256 = reopened.GetProperty("sha256"), changes
        });
        Assert.Equal(HttpStatusCode.BadRequest, wrongLayout.StatusCode);
    }


    [Fact]
    public async Task Spare_table_template_saves_named_fields_and_byte_edits_without_exposing_credentials()
    {
        using var created = await _client.PostAsJsonAsync("/api/tables/sets", new { name = "Spare table editor test" });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var path = $"/api/tables/sets/{id}/records/72/fields";
        var missing = await _client.GetFromJsonAsync<JsonElement>(path);
        using var initialized = await _client.PostAsJsonAsync(path, missing.GetProperty("templates")[0]);
        Assert.Equal(HttpStatusCode.NoContent, initialized.StatusCode);
        var original = await _client.GetFromJsonAsync<JsonElement>(path);
        var request = new
        {
            profile = original.GetProperty("profile"), revision = original.GetProperty("revision"), sha256 = original.GetProperty("sha256"),
            changes = new Dictionary<string, string> { ["dailyCash"] = "4000000000", ["operatorPin"] = "258", ["screenReference"] = "180",
                ["operatorAccessTone"] = "13", ["operatorCallTone"] = "15", ["sessionTimeout"] = "30" },
            spareBytes = new[] { new SpareBytePatch(1000, 0xDE) }
        };
        using var saved = await _client.PutAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);

        var result = await _client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(2, result.GetProperty("revision").GetInt32());
        var bytes = result.GetProperty("spareBytes").GetString()!;
        Assert.EndsWith("DE", bytes);
        Assert.Equal("B40000DF1E", bytes.Substring((345 - 1) * 2, 10));
        Assert.Equal("????????", bytes.Substring(698, 8));
        Assert.Contains(result.GetProperty("sections").EnumerateArray(), section => section.GetProperty("label").GetString() == "IOPR configuration");
        var fields = result.GetProperty("sections").EnumerateArray().SelectMany(section => section.GetProperty("fields").EnumerateArray())
            .ToDictionary(field => field.GetProperty("key").GetString()!);
        Assert.Equal("4000000000", fields["dailyCash"].GetProperty("value").GetString());
        Assert.Equal(JsonValueKind.Null, fields["operatorPin"].GetProperty("value").ValueKind);
        Assert.Equal(180, fields["screenReference"].GetProperty("max").GetInt32());

        using var blocked = await _client.PutAsJsonAsync(path, new
        {
            profile = result.GetProperty("profile"), revision = result.GetProperty("revision"), sha256 = result.GetProperty("sha256"),
            changes = new Dictionary<string, string>(), spareBytes = new[] { new SpareBytePatch(350, 0) }
        });
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        using var stale = await _client.PutAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task Stored_value_editor_masks_validation_keys_and_loads_the_selected_discount_method()
    {
        using var created = await _client.PostAsJsonAsync("/api/tables/sets", new { name = "Stored-value parameter editor test" });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var path = $"/api/tables/sets/{id}/records/93/fields";
        var missing = await _client.GetFromJsonAsync<JsonElement>(path);
        var template = missing.GetProperty("templates").EnumerateArray().Single(item => item.GetProperty("profile").GetString() != NativeTableLayouts.MultiPay);
        using var initialized = await _client.PostAsJsonAsync(path, template);
        Assert.Equal(HttpStatusCode.NoContent, initialized.StatusCode);
        var ambiguous = await _client.GetFromJsonAsync<JsonElement>(path);
        Assert.False(ambiguous.GetProperty("supported").GetBoolean());
        var profileName = template.GetProperty("profile").GetString()!;
        var withProfile = $"{path}?profile={Uri.EscapeDataString(profileName)}";
        var original = await _client.GetFromJsonAsync<JsonElement>(withProfile);
        var syntheticSlotBytes = Convert.ToHexString(Enumerable.Range(0, 8).Select(number => (byte)(number + 80)).ToArray());
        using var saved = await _client.PutAsJsonAsync(path, new
        {
            profile = profileName, revision = original.GetProperty("revision"), sha256 = original.GetProperty("sha256"),
            changes = new Dictionary<string, string>
            {
                ["validation-key-0"] = syntheticSlotBytes, ["discount-0.localMode"] = "0", ["discount-0.localInitial"] = "-5"
            }
        });
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        using var after = await _client.GetAsync(withProfile);
        after.EnsureSuccessStatusCode();
        var serialized = await after.Content.ReadAsStringAsync();
        Assert.DoesNotContain(syntheticSlotBytes, serialized, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(serialized);
        var result = document.RootElement;
        var fields = result.GetProperty("sections").EnumerateArray().SelectMany(section => section.GetProperty("fields").EnumerateArray())
            .ToDictionary(field => field.GetProperty("key").GetString()!);
        Assert.Equal(JsonValueKind.Null, fields["validation-key-0"].GetProperty("value").ValueKind);
        Assert.Equal("password-hex", fields["validation-key-0"].GetProperty("editor").GetString());
        Assert.Equal("-5", fields["discount-0.localInitial"].GetProperty("value").GetString());
        Assert.Equal("3", fields["discount-0.internationalMode"].GetProperty("value").GetString());
    }

    [Fact]
    public async Task Card_template_saves_family_specific_fields_and_refreshes_the_editor()
    {
        using var created = await _client.PostAsJsonAsync("/api/tables/sets", new { name = "Card family editor test" });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var path = $"/api/tables/sets/{id}/records/134/fields";
        var before = await Read();
        using var initialized = await _client.PostAsJsonAsync(path, before.GetProperty("templates")[0]);
        Assert.Equal(HttpStatusCode.NoContent, initialized.StatusCode);
        var initial = await Read();
        Assert.Equal(1153, initial.GetProperty("length").GetInt32());
        Assert.DoesNotContain(initial.GetProperty("sections").EnumerateArray(), section => section.GetProperty("key").GetString() == "card-0-service");

        await Save(initial, new() { ["card-0.format"] = "1", ["card-0.serviceCode-0"] = "123", ["card-0.spillStep-0.type"] = "2" });
        var magnetic = await Read();
        var firstCode = magnetic.GetProperty("sections").EnumerateArray()
            .Single(section => section.GetProperty("key").GetString() == "card-0-service").GetProperty("fields")[0];
        Assert.Equal("123", firstCode.GetProperty("value").GetString());

        await Save(magnetic, new() { ["card-0.format"] = "10", ["card-0.discount"] = "2" });
        var storedValue = await Read();
        Assert.Equal(3, storedValue.GetProperty("revision").GetInt32());
        var sections = storedValue.GetProperty("sections").EnumerateArray().ToDictionary(section => section.GetProperty("key").GetString()!);
        Assert.False(sections.ContainsKey("card-0-service"));
        Assert.False(sections.ContainsKey("card-0-spill"));
        Assert.Equal("2", sections["card-0-stored-value"].GetProperty("fields")[0].GetProperty("value").GetString());

        async Task<JsonElement> Read() => await _client.GetFromJsonAsync<JsonElement>(path);

        async Task Save(JsonElement fields, Dictionary<string, string> changes)
        {
            using var saved = await _client.PutAsJsonAsync(path, new
            {
                profile = fields.GetProperty("profile"), revision = fields.GetProperty("revision"), sha256 = fields.GetProperty("sha256"), changes
            });
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        }
    }

    [Fact]
    public async Task Numbering_plan_editor_saves_both_rule_sets_and_currency_in_one_revision()
    {
        using var definition = await _client.PostAsJsonAsync("/api/tables/definitions", new { name = "Numbering editor test", tableNumber = 62 });
        definition.EnsureSuccessStatusCode();
        using var created = await _client.PostAsJsonAsync("/api/tables/sets", new { name = "Numbering editor test" });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var data = new byte[NumberingPlanWire.RecordLength];
        data[0] = NumberingPlanWire.MessageType;
        var root = new NumberingRule("first", null, null, 0, 48, 3, [18]);
        NumberingPlanWire.Apply(data, new([root], null));
        using var imported = await _client.PostAsJsonAsync("/api/tables/versions", new
        {
            tableSetId = id, tableDefinitionId = (await definition.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(),
            tableRevision = 1, payloadBase64 = Convert.ToBase64String(data), sortOrder = 0
        });
        imported.EnsureSuccessStatusCode();

        var path = $"/api/tables/sets/{id}/records/62/fields";
        var original = await _client.GetFromJsonAsync<JsonElement>(path);
        var originalPlan = original.GetProperty("numberingPlan").Deserialize<NumberingPlanChanges>(JsonOpts)!;
        var added = new NumberingRule("second", null, null, 0, 48, 5, [10, 17]);
        var formatting = new NumberingRule("format", null, null, 4, 0, 0, [0x51, 0x91]);
        var edits = new
        {
            profile = original.GetProperty("profile"), revision = original.GetProperty("revision"), sha256 = original.GetProperty("sha256"),
            changes = new Dictionary<string, string> { ["currencySymbol"] = "¢", ["formatCurrency"] = "true" },
            numberingPlan = new NumberingPlanChanges([originalPlan.Dial![0] with { NoMatch = added.Id }, added], [formatting])
        };

        using var saved = await _client.PutAsJsonAsync(path, edits);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var result = await _client.GetFromJsonAsync<JsonElement>(path);
        var updated = result.GetProperty("numberingPlan").Deserialize<NumberingPlanChanges>(JsonOpts)!;
        Assert.Equal(2, result.GetProperty("revision").GetInt32());
        Assert.Equal(updated.Dial![1].Id, updated.Dial[0].NoMatch);
        Assert.Equal(added.Digits, updated.Dial[1].Digits);
        Assert.Equal(formatting.Digits, Assert.Single(updated.Format!).Digits);
        var fields = result.GetProperty("sections").EnumerateArray().Single(section => section.GetProperty("key").GetString() == "number-display")
            .GetProperty("fields").EnumerateArray().ToDictionary(field => field.GetProperty("key").GetString()!);
        Assert.Equal("¢", fields["currencySymbol"].GetProperty("value").GetString());
        Assert.Equal("true", fields["formatCurrency"].GetProperty("value").GetString());

        using var stale = await _client.PutAsJsonAsync(path, edits);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task Targeted_screening_review_blocks_invalid_rates_and_warns_about_omitted_tariffs()
    {
        using var device = await _client.PostAsJsonAsync("/api/firmware/versions", new
        {
            label = "Screening reference test", tableProfileId = FirmwareTableProfiles.MultiPay200
        });
        device.EnsureSuccessStatusCode();
        var targetFirmwareVersionId = (await device.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var created = await _client.PostAsJsonAsync("/api/tables/sets", new { name = "Screening reference test", targetFirmwareVersionId });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var path = $"/api/tables/sets/{id}";
        var screening = new byte[3401];
        screening[0] = 92;
        byte[] free = [1, 0x29, 0, 0, 0x70];
        byte[] paid = [0, 0x35, 128, 0, 0x80];
        byte[] denied = [0, 0x2B, 255, 0, 0x90];
        free.CopyTo(screening, 1);
        paid.CopyTo(screening, 18);
        denied.CopyTo(screening, 35);

        await ImportRecord(92, screening, 0);
        var findings = await Findings();
        Assert.Equal(2, findings.GetArrayLength());
        Assert.All(findings.EnumerateArray(), finding => Assert.True(finding.GetProperty("blocking").GetBoolean()));
        Assert.Contains("255", findings[0].GetProperty("message").GetString());
        Assert.Contains("tariff 128", findings[1].GetProperty("message").GetString());
        using var blocked = await _client.PostAsync($"{path}/publish?confirm=true", null);
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);

        var fieldsPath = $"{path}/records/92/fields";
        var original = await _client.GetFromJsonAsync<JsonElement>(fieldsPath);
        using var saved = await _client.PutAsJsonAsync(fieldsPath, new
        {
            profile = original.GetProperty("profile"), revision = original.GetProperty("revision"), sha256 = original.GetProperty("sha256"),
            changes = new Dictionary<string, string> { ["rule-0.rate"] = "255", ["rule-1.rate"] = "2", ["rule-0.wildcards"] = "15",
                ["rule-0.pattern"] = "*8#", ["rule-0.denyDataPort"] = "true" }
        });
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var updated = await _client.GetFromJsonAsync<JsonElement>(fieldsPath);
        var firstRule = updated.GetProperty("sections")[0].GetProperty("fields").EnumerateArray()
            .ToDictionary(field => field.GetProperty("key").GetString()!);
        Assert.Equal("*8#", firstRule["rule-0.pattern"].GetProperty("value").GetString());
        Assert.Equal("15", firstRule["rule-0.wildcards"].GetProperty("value").GetString());
        Assert.Equal("true", firstRule["rule-0.denyDataPort"].GetProperty("value").GetString());
        var warning = Assert.Single((await Findings()).EnumerateArray());
        Assert.Equal(73, warning.GetProperty("tableNumber").GetInt32());
        Assert.False(warning.GetProperty("blocking").GetBoolean());

        var tariff = new byte[1192];
        tariff[0] = 73;
        await ImportRecord(73, tariff, 1);
        Assert.Empty((await Findings()).EnumerateArray());
        using var published = await _client.PostAsync($"{path}/publish?confirm=true", null);
        Assert.Equal(HttpStatusCode.NoContent, published.StatusCode);

        async Task<JsonElement> Findings() => (await _client.GetFromJsonAsync<JsonElement>(path)).GetProperty("firmwareFindings");

        async Task ImportRecord(int number, byte[] bytes, int sortOrder)
        {
            using var definition = await _client.PostAsJsonAsync("/api/tables/definitions", new { name = $"Screening review MT{number}", tableNumber = number });
            definition.EnsureSuccessStatusCode();
            using var imported = await _client.PostAsJsonAsync("/api/tables/versions", new
            {
                tableSetId = id, tableDefinitionId = (await definition.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(),
                tableRevision = 1, payloadBase64 = Convert.ToBase64String(bytes), sortOrder
            });
            imported.EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Imported_numbering_and_operator_routes_are_reviewed_before_publication_and_can_be_corrected()
    {
        using var firmware = await _client.PostAsJsonAsync("/api/firmware/versions", new
        {
            label = "Routing review fixture", tableProfileId = FirmwareTableProfiles.LaterMultiPay180
        });
        firmware.EnsureSuccessStatusCode();
        var targetFirmwareVersionId = (await firmware.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var created = await _client.PostAsJsonAsync("/api/tables/sets", new { name = "Routing review fixture", targetFirmwareVersionId });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var path = $"/api/tables/sets/{id}";
        var numbering = new byte[353];
        numbering[0] = 62;
        new byte[] { 7, 0, 0, 48, 3, 1, 2 }.CopyTo(numbering, 33);
        new byte[] { 7, 0, 0, 48, 4, 1, 3 }.CopyTo(numbering, 40);
        numbering[60] = 0x5A;
        // This is a later profile: the former currency tail can contain a linked formatting rule.
        new byte[] { 132, 0, 4, 0, 0x51, 0 }.CopyTo(numbering, 213);
        new byte[] { 0, 0, 4, 0, 0x91, 0x51, 0x91, 0 }.CopyTo(numbering, 345);
        var features = new byte[72];
        features[0] = 26;
        features[15] = 180; // An enabled reroute points to the last, currently blank slot.
        var screening = new byte[3061];
        screening[0] = 92;
        await Import(numbering);
        await Import(features);
        await Import(screening);

        var findings = await Findings();
        Assert.Equal(2, findings.Length);
        Assert.All(findings, finding => Assert.True(finding.GetProperty("blocking").GetBoolean()));
        Assert.Contains(findings, finding => finding.GetProperty("tableNumber").GetInt32() == 62 && finding.GetProperty("message").GetString()!.Contains("circular"));
        Assert.Contains(findings, finding => finding.GetProperty("tableNumber").GetInt32() == 26 && finding.GetProperty("message").GetString()!.Contains("blank MT92 entry 180"));
        using (var rejected = await _client.PostAsync(path + "/publish?confirm=true", null))
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        var numberingPath = path + "/records/62/fields";
        var original = await _client.GetFromJsonAsync<JsonElement>(numberingPath);
        var plan = original.GetProperty("numberingPlan").Deserialize<NumberingPlanChanges>(JsonOpts)!;
        plan.Dial![1] = plan.Dial[1] with { NoMatch = null };
        using (var saved = await _client.PutAsJsonAsync(numberingPath, new
        {
            profile = original.GetProperty("profile"), revision = original.GetProperty("revision"), sha256 = original.GetProperty("sha256"),
            changes = new Dictionary<string, string>(), numberingPlan = plan
        })) Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        numbering[40] = 0;
        var corrected = await _client.GetFromJsonAsync<JsonElement>(numberingPath);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(numbering)).ToLowerInvariant(), corrected.GetProperty("sha256").GetString());
        Assert.Equal(26, Assert.Single(await Findings()).GetProperty("tableNumber").GetInt32());

        var screeningPath = path + "/records/92/fields";
        var screeningFields = await _client.GetFromJsonAsync<JsonElement>(screeningPath);
        using (var saved = await _client.PutAsJsonAsync(screeningPath, new
        {
            profile = screeningFields.GetProperty("profile"), revision = screeningFields.GetProperty("revision"), sha256 = screeningFields.GetProperty("sha256"),
            changes = new Dictionary<string, string> { ["rule-179.pattern"] = "1234" }
        })) Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        Assert.Empty(await Findings());
        using var published = await _client.PostAsync(path + "/publish?confirm=true", null);
        Assert.Equal(HttpStatusCode.NoContent, published.StatusCode);

        async Task<JsonElement[]> Findings() => (await _client.GetFromJsonAsync<JsonElement>(path)).GetProperty("firmwareFindings").EnumerateArray().ToArray();
        async Task Import(byte[] bytes)
        {
            using var definition = await _client.PostAsJsonAsync("/api/tables/definitions", new { name = $"Routing fixture MT{bytes[0]}", tableNumber = bytes[0] });
            definition.EnsureSuccessStatusCode();
            using var version = await _client.PostAsJsonAsync("/api/tables/versions", new
            {
                tableSetId = id, tableDefinitionId = (await definition.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(),
                tableRevision = 1, payloadBase64 = Convert.ToBase64String(bytes), sortOrder = bytes[0]
            });
            version.EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Later_rating_review_blocks_invalid_international_default_and_accepts_corrected_partial_set()
    {
        using var firmware = await _client.PostAsJsonAsync("/api/firmware/versions", new
        {
            label = "International review fixture", tableProfileId = FirmwareTableProfiles.LaterMultiPay180
        });
        firmware.EnsureSuccessStatusCode();
        var targetFirmwareVersionId = (await firmware.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var created = await _client.PostAsJsonAsync("/api/tables/sets", new { name = "International review fixture", targetFirmwareVersionId });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var path = $"/api/tables/sets/{id}";
        using var definition = await _client.PostAsJsonAsync("/api/tables/definitions", new { name = "International review MT151", tableNumber = 151 });
        definition.EnsureSuccessStatusCode();
        var bytes = new byte[604];
        bytes[0] = 151;
        bytes[1] = 0xA1;
        bytes[2] = 100;
        bytes[3] = 0xB7;
        bytes[4] = 1;
        bytes[6] = 255;
        using var imported = await _client.PostAsJsonAsync("/api/tables/versions", new
        {
            tableSetId = id, tableDefinitionId = (await definition.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(),
            tableRevision = 1, payloadBase64 = Convert.ToBase64String(bytes), sortOrder = 0
        });
        imported.EnsureSuccessStatusCode();
        var invalid = Assert.Single((await _client.GetFromJsonAsync<JsonElement>(path)).GetProperty("firmwareFindings").EnumerateArray());
        Assert.True(invalid.GetProperty("blocking").GetBoolean());
        Assert.Equal(151, invalid.GetProperty("tableNumber").GetInt32());
        using (var blocked = await _client.PostAsync(path + "/publish?confirm=true", null))
            Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);

        var fieldsPath = path + "/records/151/fields";
        var original = await _client.GetFromJsonAsync<JsonElement>(fieldsPath);
        var destination = original.GetProperty("sections")[1].GetProperty("fields")[1];
        Assert.Equal(100, destination.GetProperty("tariffFallbackCode").GetInt32());
        Assert.Equal("255", destination.GetProperty("value").GetString());
        using var saved = await _client.PutAsJsonAsync(fieldsPath, new
        {
            profile = original.GetProperty("profile"), revision = original.GetProperty("revision"), sha256 = original.GetProperty("sha256"),
            changes = new Dictionary<string, string> { ["fallback"] = "99", ["country-0.rate"] = "100" }
        });
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        bytes[2] = 99;
        bytes[6] = 100;
        var updated = await _client.GetFromJsonAsync<JsonElement>(fieldsPath);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(), updated.GetProperty("sha256").GetString());
        var warning = Assert.Single((await _client.GetFromJsonAsync<JsonElement>(path)).GetProperty("firmwareFindings").EnumerateArray());
        Assert.False(warning.GetProperty("blocking").GetBoolean());
        Assert.Equal(73, warning.GetProperty("tableNumber").GetInt32());
        using var published = await _client.PostAsync(path + "/publish?confirm=true", null);
        Assert.Equal(HttpStatusCode.NoContent, published.StatusCode);
    }

    [Fact]
    public async Task Later_compiled_target_selects_its_native_editor_and_rejects_unlisted_or_wrong_length_records()
    {
        using var firmware = await _client.PostAsJsonAsync("/api/firmware/versions", new
        {
            label = "Later compiled profile fixture", tableProfileId = FirmwareTableProfiles.LaterMultiPay180
        });
        firmware.EnsureSuccessStatusCode();
        var targetFirmwareVersionId = (await firmware.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var created = await _client.PostAsJsonAsync("/api/tables/sets", new { name = "Later target fixture", targetFirmwareVersionId });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var path = $"/api/tables/sets/{id}";

        var screening = await _client.GetFromJsonAsync<JsonElement>(path + "/records/92/fields");
        var layout = Assert.Single(screening.GetProperty("templates").EnumerateArray());
        Assert.Equal(3061, layout.GetProperty("length").GetInt32());
        Assert.Equal(180, layout.GetProperty("screeningSlots").GetInt32());
        var shared = await _client.GetFromJsonAsync<JsonElement>(path + "/records/35/fields");
        Assert.Equal(NativeTableLayouts.MultiPay, Assert.Single(shared.GetProperty("templates").EnumerateArray()).GetProperty("profile").GetString());
        var absent = await _client.GetFromJsonAsync<JsonElement>(path + "/records/131/fields");
        Assert.False(absent.GetProperty("supported").GetBoolean());
        Assert.Contains("absent", absent.GetProperty("reason").GetString());

        await Import(33, 35);
        await Import(131, 9224);
        var review = await _client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(42, review.GetProperty("tableProfileEvidence").GetProperty("recordCount").GetInt32());
        Assert.Equal(12, review.GetProperty("tableProfileEvidence").GetProperty("terminalAcknowledgedCount").GetInt32());
        var findings = review.GetProperty("firmwareFindings").EnumerateArray().ToArray();
        Assert.Equal(2, findings.Length);
        Assert.All(findings, finding => Assert.True(finding.GetProperty("blocking").GetBoolean()));
        Assert.Contains("36 bytes", findings[0].GetProperty("message").GetString());
        Assert.Contains("absent", findings[1].GetProperty("message").GetString());
        using var rejected = await _client.PostAsync(path + "/publish?confirm=true", null);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        async Task Import(int number, int length)
        {
            var existing = (await _client.GetFromJsonAsync<JsonElement>("/api/tables/definitions"))
                .EnumerateArray().FirstOrDefault(row => row.GetProperty("tableNumber").GetInt32() == number);
            Guid definitionId;
            if (existing.ValueKind == JsonValueKind.Object)
                definitionId = existing.GetProperty("id").GetGuid();
            else
            {
                using var definition = await _client.PostAsJsonAsync("/api/tables/definitions",
                    new { name = $"Later profile MT{number}", tableNumber = number });
                definition.EnsureSuccessStatusCode();
                definitionId = (await definition.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            }
            var payload = new byte[length];
            payload[0] = (byte)number;
            using var version = await _client.PostAsJsonAsync("/api/tables/versions", new
            {
                tableSetId = id, tableDefinitionId = definitionId,
                tableRevision = 1, payloadBase64 = Convert.ToBase64String(payload), sortOrder = number
            });
            version.EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Family_checklist_reports_present_and_missing_records_without_blocking_partial_publication()
    {
        using var created = await _client.PostAsJsonAsync("/api/tables/sets", new { name = "Family checklist fixture" });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var path = $"/api/tables/sets/{id}";
        var endpoint = path + "/family-review";

        var empty = await _client.GetFromJsonAsync<TerminalFamilyTableReview>(endpoint + "?familyId=mtr19-multipay");
        Assert.True(empty!.Documented);
        Assert.Equal(200, empty.ScreeningEntries);
        Assert.Equal("missing", Assert.Single(empty.Tables, table => table.Name == "Call screening").Status);

        var fieldsPath = path + "/records/92/fields";
        var templates = await _client.GetFromJsonAsync<JsonElement>(fieldsPath);
        var template = Assert.Single(templates.GetProperty("templates").EnumerateArray(),
            item => item.GetProperty("length").GetInt32() == 3401);
        using var imported = await _client.PostAsJsonAsync(fieldsPath, template);
        imported.EnsureSuccessStatusCode();
        var review = await _client.GetFromJsonAsync<TerminalFamilyTableReview>(endpoint + "?familyId=mtr19-multipay");
        var screening = Assert.Single(review!.Tables, table => table.Name == "Call screening");
        Assert.True(screening.Required);
        Assert.Equal("present", screening.Status);
        Assert.Equal(new[] { 200 }, Assert.Single(screening.Records).ScreeningSlots);

        using var unknown = await _client.GetAsync(endpoint + "?familyId=unknown");
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        var desk = await _client.GetFromJsonAsync<TerminalFamilyTableReview>(endpoint + "?familyId=mtr20-desk");
        Assert.False(desk!.Documented);
        using var published = await _client.PostAsync(path + "/publish?confirm=true", null);
        Assert.Equal(HttpStatusCode.NoContent, published.StatusCode);
    }

    [Fact]
    public async Task Service_restriction_template_creates_one_native_draft_record()
    {
        using var set = await _client.PostAsJsonAsync("/api/tables/sets", new { name = "Native service restriction template" });
        set.EnsureSuccessStatusCode();
        var id = (await set.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var path = $"/api/tables/sets/{id}/records/58/fields";
        var initial = await _client.GetFromJsonAsync<JsonElement>(path);
        Assert.False(initial.GetProperty("supported").GetBoolean());
        var template = initial.GetProperty("templates")[0];
        using var created = await _client.PostAsJsonAsync(path, template);
        Assert.Equal(HttpStatusCode.NoContent, created.StatusCode);
        var details = await _client.GetFromJsonAsync<JsonElement>(path);
        Assert.True(details.GetProperty("editable").GetBoolean());
        Assert.Equal(26, details.GetProperty("length").GetInt32());
        Assert.Equal("0", details.GetProperty("sections")[0].GetProperty("fields")[0].GetProperty("value").GetString());
        using var repeated = await _client.PostAsJsonAsync(path, template);
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
    }

    [Fact]
    public async Task Later_call_collection_policy_edits_independent_carrier_and_cdr_bits_without_changing_the_record_tail()
    {
        using var firmware = await _client.PostAsJsonAsync("/api/firmware/versions", new
        {
            label = "CDR policy fixture", tableProfileId = FirmwareTableProfiles.LaterMultiPay180
        });
        firmware.EnsureSuccessStatusCode();
        var targetFirmwareVersionId = (await firmware.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var created = await _client.PostAsJsonAsync("/api/tables/sets", new { name = "CDR collection fixture", targetFirmwareVersionId });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var path = $"/api/tables/sets/{id}";
        var fieldsPath = path + "/records/34/fields";
        var templates = await _client.GetFromJsonAsync<JsonElement>(fieldsPath);
        Assert.Equal(NativeTableLayouts.Later, Assert.Single(templates.GetProperty("templates").EnumerateArray()).GetProperty("profile").GetString());

        byte[] record = [34, 2, 15, 1, 2, 6, 0, 12, 0, 0, 0, 0, 0, 0xC2, 40, 20, 0, 0, 6];
        using var definition = await _client.PostAsJsonAsync("/api/tables/definitions", new { name = "CDR collection fixture", tableNumber = 34 });
        definition.EnsureSuccessStatusCode();
        using var imported = await _client.PostAsJsonAsync("/api/tables/versions", new
        {
            tableSetId = id, tableDefinitionId = (await definition.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(),
            tableRevision = 1, payloadBase64 = Convert.ToBase64String(record), sortOrder = 34
        });
        imported.EnsureSuccessStatusCode();
        var original = await _client.GetFromJsonAsync<JsonElement>(fieldsPath);
        using (var untouched = await Save(original, new Dictionary<string, string>())) Assert.Equal(HttpStatusCode.NoContent, untouched.StatusCode);
        using (var edited = await Save(original, new Dictionary<string, string> { ["enabled"] = "16" })) Assert.Equal(HttpStatusCode.NoContent, edited.StatusCode);
        record[13] = 0xD2;
        var updated = await _client.GetFromJsonAsync<JsonElement>(fieldsPath);
        Assert.Equal(Hash(record), updated.GetProperty("sha256").GetString());
        using (var summarized = await Save(updated, new Dictionary<string, string> { ["carrierStatistics"] = "17" })) Assert.Equal(HttpStatusCode.NoContent, summarized.StatusCode);
        record[13] = 0xF2;
        var final = await _client.GetFromJsonAsync<JsonElement>(fieldsPath);
        Assert.Equal(Hash(record), final.GetProperty("sha256").GetString());
        var fields = final.GetProperty("sections").EnumerateArray().SelectMany(section => section.GetProperty("fields").EnumerateArray())
            .ToDictionary(field => field.GetProperty("key").GetString()!);
        Assert.Equal("16", fields["enabled"].GetProperty("value").GetString());
        Assert.Equal("17", fields["carrierStatistics"].GetProperty("value").GetString());
        Assert.Empty((await _client.GetFromJsonAsync<JsonElement>(path)).GetProperty("firmwareFindings").EnumerateArray());
        using var published = await _client.PostAsync(path + "/publish?confirm=true", null);
        Assert.Equal(HttpStatusCode.NoContent, published.StatusCode);

        static string Hash(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        Task<HttpResponseMessage> Save(JsonElement details, Dictionary<string, string> changes) => _client.PutAsJsonAsync(fieldsPath, new
        {
            profile = details.GetProperty("profile"), revision = details.GetProperty("revision"), sha256 = details.GetProperty("sha256"), changes
        });
    }

    [Fact]
    public async Task Later_service_restriction_editor_reviews_imported_windows_and_keeps_the_unedited_tail()
    {
        using var firmware = await _client.PostAsJsonAsync("/api/firmware/versions", new
        {
            label = "Service restriction fixture", tableProfileId = FirmwareTableProfiles.LaterMultiPay180
        });
        firmware.EnsureSuccessStatusCode();
        var targetFirmwareVersionId = (await firmware.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var created = await _client.PostAsJsonAsync("/api/tables/sets", new { name = "Service restriction window", targetFirmwareVersionId });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var path = $"/api/tables/sets/{id}";
        var fieldsPath = path + "/records/58/fields";
        var templates = await _client.GetFromJsonAsync<JsonElement>(fieldsPath);
        Assert.Equal(NativeTableLayouts.Later, Assert.Single(templates.GetProperty("templates").EnumerateArray()).GetProperty("profile").GetString());

        byte[] record = [58, 4, 20, 30, 24, 15, .. Enumerable.Repeat((byte)0xA5, 20)];
        using var definition = await _client.PostAsJsonAsync("/api/tables/definitions", new { name = "Service restriction fixture", tableNumber = 58 });
        definition.EnsureSuccessStatusCode();
        using var imported = await _client.PostAsJsonAsync("/api/tables/versions", new
        {
            tableSetId = id, tableDefinitionId = (await definition.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(),
            tableRevision = 1, payloadBase64 = Convert.ToBase64String(record), sortOrder = 58
        });
        imported.EnsureSuccessStatusCode();

        var original = await _client.GetFromJsonAsync<JsonElement>(fieldsPath);
        Assert.Equal(NativeTableLayouts.Later, original.GetProperty("profile").GetString());
        Assert.Contains(original.GetProperty("sections")[0].GetProperty("fields")[0].GetProperty("choices").EnumerateArray(),
            choice => choice.GetProperty("value").GetInt32() == 4);
        using (var untouched = await Save(original, new Dictionary<string, string>())) Assert.Equal(HttpStatusCode.NoContent, untouched.StatusCode);
        var unchanged = await _client.GetFromJsonAsync<JsonElement>(fieldsPath);
        Assert.Equal(original.GetProperty("sha256").GetString(), unchanged.GetProperty("sha256").GetString());
        Assert.True(Assert.Single(await Findings()).GetProperty("blocking").GetBoolean());
        using (var rejected = await _client.PostAsync(path + "/publish?confirm=true", null)) Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        using (var corrected = await Save(unchanged, new Dictionary<string, string> { ["durationHours"] = "6" }))
            Assert.Equal(HttpStatusCode.NoContent, corrected.StatusCode);
        record[4] = 6;
        var edited = await _client.GetFromJsonAsync<JsonElement>(fieldsPath);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(record)).ToLowerInvariant(), edited.GetProperty("sha256").GetString());
        var dependency = Assert.Single(await Findings());
        Assert.False(dependency.GetProperty("blocking").GetBoolean());
        Assert.Contains("MT55 and MT92", dependency.GetProperty("message").GetString());
        using var published = await _client.PostAsync(path + "/publish?confirm=true", null);
        Assert.Equal(HttpStatusCode.NoContent, published.StatusCode);

        Task<HttpResponseMessage> Save(JsonElement details, Dictionary<string, string> changes) => _client.PutAsJsonAsync(fieldsPath, new
        {
            profile = details.GetProperty("profile"), revision = details.GetProperty("revision"), sha256 = details.GetProperty("sha256"), changes
        });
        async Task<JsonElement[]> Findings() => (await _client.GetFromJsonAsync<JsonElement>(path)).GetProperty("firmwareFindings").EnumerateArray().ToArray();
    }

    [Fact]
    public async Task Messaging_template_and_updates_keep_the_key_out_of_editor_reads()
    {
        using var created = await _client.PostAsJsonAsync("/api/tables/sets", new { name = "Messaging template test" });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var path = $"/api/tables/sets/{id}/records/68/fields";
        var missing = await _client.GetFromJsonAsync<JsonElement>(path);
        using var initialized = await _client.PostAsJsonAsync(path, missing.GetProperty("templates")[0]);
        Assert.Equal(HttpStatusCode.NoContent, initialized.StatusCode);

        var initial = await _client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(72, initial.GetProperty("length").GetInt32());
        var changes = new Dictionary<string, string> { ["enabled"] = "1" };
        var request = new { profile = initial.GetProperty("profile"), revision = initial.GetProperty("revision"), sha256 = initial.GetProperty("sha256"), changes };
        using var rejected = await _client.PutAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        changes["serviceKey"] = string.Concat(Enumerable.Range(1, 8));
        changes["serviceNumber"] = "120";
        changes["allowCoin"] = "true";
        using var saved = await _client.PutAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var result = await _client.GetFromJsonAsync<JsonElement>(path);
        var fields = result.GetProperty("sections").EnumerateArray().SelectMany(section => section.GetProperty("fields").EnumerateArray())
            .ToDictionary(field => field.GetProperty("key").GetString()!);
        Assert.Equal("1", fields["enabled"].GetProperty("value").GetString());
        Assert.Equal("120", fields["serviceNumber"].GetProperty("value").GetString());
        Assert.Equal("true", fields["allowCoin"].GetProperty("value").GetString());
        Assert.Equal("password", fields["serviceKey"].GetProperty("editor").GetString());
        Assert.Equal(JsonValueKind.Null, fields["serviceKey"].GetProperty("value").ValueKind);
    }

    [Fact]
    public async Task Imported_audio_gram_record_preserves_spare_flags_and_requires_valid_enabled_configuration()
    {
        using var firmware = await _client.PostAsJsonAsync("/api/firmware/versions", new
        {
            label = "Audio-Gram fixture", tableProfileId = FirmwareTableProfiles.MultiPay200
        });
        firmware.EnsureSuccessStatusCode();
        var targetFirmwareVersionId = (await firmware.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var created = await _client.PostAsJsonAsync("/api/tables/sets", new { name = "Audio-Gram review fixture", targetFirmwareVersionId });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var path = $"/api/tables/sets/{id}";
        var fieldsPath = path + "/records/68/fields";
        var record = new byte[72];
        record[0] = 68;
        record[19] = 1;
        record[21] = 0xA4;
        record[34] = 0xA1;
        record[35] = 0xB1;
        record[36] = 5;
        record[37] = 2;
        record[39] = 0x10;
        record[40] = 0x20;
        new Random(68).NextBytes(record.AsSpan(56));
        using var definition = await _client.PostAsJsonAsync("/api/tables/definitions", new { name = "Audio-Gram review fixture", tableNumber = 68 });
        definition.EnsureSuccessStatusCode();
        using var imported = await _client.PostAsJsonAsync("/api/tables/versions", new
        {
            tableSetId = id, tableDefinitionId = (await definition.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(),
            tableRevision = 1, payloadBase64 = Convert.ToBase64String(record), sortOrder = 68
        });
        imported.EnsureSuccessStatusCode();
        var initial = await _client.GetFromJsonAsync<JsonElement>(fieldsPath);
        using (var untouched = await Save(initial, new Dictionary<string, string>())) Assert.Equal(HttpStatusCode.NoContent, untouched.StatusCode);
        using (var rejected = await _client.PostAsync(path + "/publish?confirm=true", null)) Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        var replacement = string.Concat(Enumerable.Range(1, 8));
        using (var corrected = await Save(initial, new Dictionary<string, string>
        {
            ["serviceKey"] = replacement, ["serviceNumber"] = "120", ["protocolVersion"] = "1", ["statistics"] = "1", ["lineReleaseDelay"] = "300"
        })) Assert.Equal(HttpStatusCode.NoContent, corrected.StatusCode);
        System.Text.Encoding.ASCII.GetBytes(replacement).CopyTo(record, 1);
        record[9] = 0x12;
        record[10] = 0xA0;
        record[21] |= 1;
        record[42] = 1;
        record[56] = 44;
        record[57] = 1;
        var edited = await _client.GetFromJsonAsync<JsonElement>(fieldsPath);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(record)).ToLowerInvariant(), edited.GetProperty("sha256").GetString());
        var fields = edited.GetProperty("sections").EnumerateArray().SelectMany(section => section.GetProperty("fields").EnumerateArray())
            .ToDictionary(field => field.GetProperty("key").GetString()!);
        Assert.Equal(JsonValueKind.Null, fields["serviceKey"].GetProperty("value").ValueKind);
        Assert.Equal("1", fields["statistics"].GetProperty("value").GetString());
        Assert.Equal("true", fields["statisticsSpare7"].GetProperty("value").GetString());
        var findings = (await _client.GetFromJsonAsync<JsonElement>(path)).GetProperty("firmwareFindings").EnumerateArray();
        var advisory = Assert.Single(findings);
        Assert.False(advisory.GetProperty("blocking").GetBoolean());
        Assert.Contains("conditional firmware feature", advisory.GetProperty("message").GetString());
        using var published = await _client.PostAsync(path + "/publish?confirm=true", null);
        Assert.Equal(HttpStatusCode.NoContent, published.StatusCode);

        Task<HttpResponseMessage> Save(JsonElement details, Dictionary<string, string> changes) => _client.PutAsJsonAsync(fieldsPath, new
        {
            profile = details.GetProperty("profile"), revision = details.GetProperty("revision"), sha256 = details.GetProperty("sha256"), changes
        });
    }

    [Fact]
    public async Task Visual_prompt_record_exposes_paired_preview_and_saves_accents_with_existing_field_keys()
    {
        using var definition = await _client.PostAsJsonAsync("/api/tables/definitions", new { name = "Language record test", tableNumber = 86 });
        definition.EnsureSuccessStatusCode();
        using var created = await _client.PostAsJsonAsync("/api/tables/sets", new { name = "Language editor test" });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var data = new byte[8001];
        data[0] = 86;
        data[1 + 20 * 33] = 0xD6;
        using var imported = await _client.PostAsJsonAsync("/api/tables/versions", new
        {
            tableSetId = id, tableDefinitionId = (await definition.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(),
            tableRevision = 1, payloadBase64 = Convert.ToBase64String(data), sortOrder = 0
        });
        imported.EnsureSuccessStatusCode();
        var path = $"/api/tables/sets/{id}/records/86/fields";
        var initial = await _client.GetFromJsonAsync<JsonElement>(path);
        var calling = initial.GetProperty("sections").EnumerateArray().Single(section => section.GetProperty("key").GetString() == "message-16");
        Assert.True(calling.GetProperty("showDisplayPreview").GetBoolean());
        Assert.Equal("prompt-32.text", calling.GetProperty("fields")[0].GetProperty("key").GetString());
        Assert.Equal(JsonValueKind.Null, calling.GetProperty("fields")[1].GetProperty("value").ValueKind);
        using var saved = await _client.PutAsJsonAsync(path, new
        {
            profile = initial.GetProperty("profile"), revision = initial.GetProperty("revision"), sha256 = initial.GetProperty("sha256"),
            changes = new Dictionary<string, string> { ["prompt-32.text"] = "Appel préparé" }
        });
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var result = await _client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(2, result.GetProperty("revision").GetInt32());
        var fields = result.GetProperty("sections").EnumerateArray().Single(section => section.GetProperty("key").GetString() == "message-16").GetProperty("fields");
        Assert.Equal("Appel préparé", fields[0].GetProperty("value").GetString());
        Assert.Equal(JsonValueKind.Null, fields[1].GetProperty("value").ValueKind);
    }

    [Fact]
    public async Task Graphical_draft_can_author_a_mixed_screen_and_its_key_link_in_one_revision()
    {
        using var created = await _client.PostAsJsonAsync("/api/tables/sets", new { name = "Graphical editor test" });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var path = $"/api/tables/sets/{id}/records/133/fields";
        var initial = await _client.GetFromJsonAsync<JsonElement>(path);
        using var initialized = await _client.PostAsJsonAsync(path, initial.GetProperty("templates")[0]);
        Assert.Equal(HttpStatusCode.NoContent, initialized.StatusCode);

        var original = await _client.GetFromJsonAsync<JsonElement>(path);
        using var saved = await _client.PutAsJsonAsync(path, new
        {
            profile = original.GetProperty("profile"), revision = original.GetProperty("revision"), sha256 = original.GetProperty("sha256"),
            changes = new Dictionary<string, string>
            {
                ["screen-0.panel-0.line-0"] = "Main menu", ["screen-0.panel-0.label-0"] = "Details",
                ["screen-0.panel-0.button-0.next"] = "80", ["screen-0.kind"] = "77"
            }
        });
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var result = await _client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(2, result.GetProperty("revision").GetInt32());
        var allSections = result.GetProperty("sections").EnumerateArray().ToDictionary(section => section.GetProperty("key").GetString()!);
        var panel = allSections["screen-0.panel-0"];
        Assert.Equal("Screen 17 · Panel 1", panel.GetProperty("label").GetString());
        Assert.True(panel.GetProperty("showPanelPreview").GetBoolean());
        Assert.Equal("Main menu", panel.GetProperty("fields")[0].GetProperty("value").GetString());
        var key = allSections["screen-0.panel-0.button-0"].GetProperty("fields").EnumerateArray()
            .Single(field => field.GetProperty("key").GetString()!.EndsWith(".next"));
        Assert.Equal("screen-link", key.GetProperty("editor").GetString());
        Assert.Equal("80", key.GetProperty("value").GetString());
    }

    [Fact]
    public async Task Installation_editor_lists_both_profiles_and_does_not_return_craft_credentials()
    {
        using var definition = await _client.PostAsJsonAsync("/api/tables/definitions", new { name = "Servicing record test", tableNumber = 31 });
        definition.EnsureSuccessStatusCode();
        using var created = await _client.PostAsJsonAsync("/api/tables/sets", new { name = "Servicing editor test" });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var bytes = new byte[37];
        bytes[0] = 31;
        using var imported = await _client.PostAsJsonAsync("/api/tables/versions", new
        {
            tableSetId = id, tableDefinitionId = (await definition.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(),
            tableRevision = 1, payloadBase64 = Convert.ToBase64String(bytes), sortOrder = 0
        });
        imported.EnsureSuccessStatusCode();

        var path = $"/api/tables/sets/{id}/records/31/fields";
        var initial = await _client.GetFromJsonAsync<JsonElement>(path);
        var profiles = initial.GetProperty("profiles").EnumerateArray().Select(profile => profile.GetString()).ToArray();
        Assert.Equal(2, profiles.Length);
        Assert.Equal("MTR 1.9 packed", initial.GetProperty("profile").GetString());
        var later = profiles.Single(profile => profile != "MTR 1.9 packed")!;
        var laterPath = $"{path}?profile={Uri.EscapeDataString(later)}";
        Assert.Equal(later, (await _client.GetFromJsonAsync<JsonElement>(laterPath)).GetProperty("profile").GetString());
        using var saved = await _client.PutAsJsonAsync(path, new
        {
            profile = later, revision = initial.GetProperty("revision"), sha256 = initial.GetProperty("sha256"),
            changes = new Dictionary<string, string> { ["craftAccessCode"] = string.Concat(Enumerable.Range(1, 7)), ["analogPrefix"] = "*#" }
        });
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var result = await _client.GetFromJsonAsync<JsonElement>(laterPath);
        var fields = result.GetProperty("sections").EnumerateArray().SelectMany(section => section.GetProperty("fields").EnumerateArray())
            .ToDictionary(field => field.GetProperty("key").GetString()!);
        Assert.Equal("*#", fields["analogPrefix"].GetProperty("value").GetString());
        Assert.Equal("password", fields["craftAccessCode"].GetProperty("editor").GetString());
        Assert.Equal(JsonValueKind.Null, fields["craftAccessCode"].GetProperty("value").ValueKind);
        Assert.Equal(JsonValueKind.Null, fields["keyCardNumber"].GetProperty("value").ValueKind);
    }

    [Fact]
    public async Task Draft_clone_retains_original_records_and_can_publish_advertising()
    {
        var sets = (await _client.GetFromJsonAsync<JsonElement>("/api/tables/sets"))!;
        var source = sets.EnumerateArray().First(s => s.GetProperty("isDefault").GetBoolean());
        var sourceId = source.GetProperty("id").GetGuid();
        var original = (await _client.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{sourceId}"))!;

        using var cloned = await _client.PostAsJsonAsync($"/api/tables/sets/{sourceId}/clone", new { name = "Advertising test" });
        Assert.Equal(HttpStatusCode.Created, cloned.StatusCode);
        var id = (await cloned.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var slots = Enumerable.Range(0, 20).Select(i => new
        {
            text = i == 0 ? "TEST MESSAGE" : "", durationTicks = i == 0 ? 300 : 0, mode = 0, blankBefore = false,
            endSequence = i > 1
        }).ToArray();
        var body = new { slots };

        using var saved = await _client.PutAsJsonAsync($"/api/tables/sets/{id}/advertising", body);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var messages = (await _client.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{id}/advertising"))!;
        Assert.Equal("TEST MESSAGE", messages.GetProperty("slots")[0].GetProperty("text").GetString());
        Assert.False(messages.GetProperty("slots")[1].GetProperty("endSequence").GetBoolean());
        Assert.True(messages.GetProperty("slots")[2].GetProperty("endSequence").GetBoolean());
        Assert.Equal(1, messages.GetProperty("revision").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PutAsJsonAsync($"/api/tables/sets/{id}/advertising",
            new { slots, revision = 0, sha256 = (string?)null })).StatusCode);
        var detail = (await _client.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{id}"))!;
        var versions = detail.GetProperty("versions").EnumerateArray().ToArray();
        Assert.Equal(481, versions.Single(v => v.GetProperty("tableNumber").GetInt32() == 29).GetProperty("payloadLengthBytes").GetInt32());
        foreach (var inherited in original.GetProperty("versions").EnumerateArray())
        {
            var inheritedId = inherited.GetProperty("tableDefinitionId").GetGuid();
            Assert.Equal(inherited.GetProperty("payloadSha256Hex").GetString(),
                versions.Single(v => v.GetProperty("tableDefinitionId").GetGuid() == inheritedId).GetProperty("payloadSha256Hex").GetString());
        }

        Assert.Equal(HttpStatusCode.NoContent,
            (await _client.PostAsync($"/api/tables/sets/{id}/publish?confirm=true", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PutAsJsonAsync($"/api/tables/sets/{id}/advertising", body)).StatusCode);
    }

    [Fact]
    public async Task Scope_assignment_requires_current_preview_and_is_idempotent()
    {
        var sets = (await _client.GetFromJsonAsync<JsonElement>("/api/tables/sets"))!;
        var setId = sets.EnumerateArray().First(s => s.GetProperty("isDefault").GetBoolean()).GetProperty("id").GetGuid();
        var terminals = (await _client.GetFromJsonAsync<JsonElement>("/api/terminals"))!;
        var terminal = terminals[0];
        var siteId = terminal.GetProperty("siteId").GetGuid();
        var terminalId = terminal.GetProperty("id").GetGuid();
        var endpoint = $"/api/tables/sets/{setId}";
        var preview = (await _client.GetFromJsonAsync<JsonElement>($"{endpoint}/assignment-preview?targetType=site&targetId={siteId}"))!;
        var targetIds = preview.GetProperty("targets").EnumerateArray().Where(t => t.GetProperty("eligible").GetBoolean())
            .Select(t => t.GetProperty("id").GetGuid()).ToArray();
        Assert.Contains(terminalId, targetIds);

        using var stale = await _client.PostAsJsonAsync($"{endpoint}/assignments", new { targetType = "site", targetId = siteId, terminalIds = Array.Empty<Guid>() });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var current = new { targetType = "site", targetId = siteId, terminalIds = targetIds };
        using var assigned = await _client.PostAsJsonAsync($"{endpoint}/assignments", current);
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        using var repeated = await _client.PostAsJsonAsync($"{endpoint}/assignments", current);
        Assert.Equal(0, (await repeated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("changed").GetInt32());
    }

    [Fact]
    public async Task Wildcard_group_targets_include_secondary_members_once_and_respect_region()
    {
        var site = (await _client.GetFromJsonAsync<JsonElement>("/api/sites"))[0].GetProperty("id").GetGuid();
        var narrow = await CreateGroup("Pattern secondary", "rxa1b0");
        var broad = await CreateGroup("Pattern primary", "RXA1A0");
        var different = await CreateGroup("Pattern other suffix", "RXA2C0");
        var first = await CreatePhone("0000000621", broad, "LAB-Q");
        var west = await CreatePhone("0000000622", broad, "LAB-W");
        var outside = await CreatePhone("0000000623", different, "LAB-Q");
        var groups = await _client.GetFromJsonAsync<JsonElement>($"/api/terminals/{first}/groups");
        using var members = await _client.PutAsJsonAsync($"/api/terminals/{first}/groups",
            new { groupIds = new[] { broad, narrow }, revision = groups.GetProperty("revision") });
        members.EnsureSuccessStatusCode();
        using var rename = await _client.PutAsJsonAsync($"/api/terminal-groups/{narrow}", new { name = "Renamed secondary" });
        rename.EnsureSuccessStatusCode();
        var groupRow = (await _client.GetFromJsonAsync<JsonElement>("/api/terminal-groups")).EnumerateArray()
            .Single(row => row.GetProperty("id").GetGuid() == narrow);
        Assert.Equal("RXA1B0", groupRow.GetProperty("code").GetString());
        using var staleDetails = await _client.PutAsJsonAsync($"/api/terminal-groups/{narrow}/details",
            new { name = "Stale details", code = "ZZZ001", revision = 0 });
        Assert.Equal(HttpStatusCode.Conflict, staleDetails.StatusCode);

        var set = (await _client.GetFromJsonAsync<JsonElement>("/api/tables/sets")).EnumerateArray()
            .First(row => row.GetProperty("isDefault").GetBoolean()).GetProperty("id").GetGuid();
        var endpoint = $"/api/tables/sets/{set}";
        var preview = $"{endpoint}/assignment-preview?targetType=group-pattern&regionCode=lab-q";
        Assert.Equal(first, Assert.Single(await Targets($"{preview}&groupPattern=rxa1%3F0")));
        Assert.Equal(new[] { first, outside }.Order(), (await Targets($"{preview}&groupPattern=RXA*")).Order());
        Assert.Equal(first, Assert.Single(await Targets($"{preview}&groupPattern=RXA1B0")));
        using var invalid = await _client.GetAsync($"{preview}&groupPattern=RX%3FA*");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var embeddedStar = await _client.GetAsync($"{preview}&groupPattern=RXA*0");
        Assert.Equal(HttpStatusCode.BadRequest, embeddedStar.StatusCode);
        using var forged = await _client.PostAsJsonAsync($"{endpoint}/assignments",
            new { targetType = "group-pattern", groupPattern = "RXA1?0", regionCode = "lab-q", terminalIds = new[] { first, west } });
        Assert.Equal(HttpStatusCode.Conflict, forged.StatusCode);
        using var assigned = await _client.PostAsJsonAsync($"{endpoint}/assignments",
            new { targetType = "group-pattern", groupPattern = "RXA1?0", regionCode = "lab-q", terminalIds = new[] { first } });
        assigned.EnsureSuccessStatusCode();

        async Task<Guid> CreateGroup(string name, string code)
        {
            using var response = await _client.PostAsJsonAsync("/api/terminal-groups", new { name, code, description = "Synthetic fixture" });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }

        async Task<Guid> CreatePhone(string terminalIdHex, Guid terminalGroupId, string regionCode)
        {
            using var response = await _client.PostAsJsonAsync("/api/terminals", new
            {
                displayName = $"Pattern fixture {terminalIdHex[^2..]}", terminalIdHex, terminalGroupId, siteId = site, status = 0
            });
            response.EnsureSuccessStatusCode();
            var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            using var inventory = await _client.PutAsJsonAsync($"/api/terminals/{id}/inventory", new { profile = new { regionCode } });
            inventory.EnsureSuccessStatusCode();
            return id;
        }

        async Task<Guid[]> Targets(string path) =>
            (await _client.GetFromJsonAsync<JsonElement>(path)).GetProperty("targets").EnumerateArray()
                .Select(row => row.GetProperty("id").GetGuid()).ToArray();
    }

    [Fact]
    public async Task Phone_group_membership_adds_the_phone_to_each_assignment_preview()
    {
        var terminal = (await _client.GetFromJsonAsync<JsonElement>("/api/terminals"))[0].GetProperty("id").GetGuid();
        var path = $"/api/terminals/{terminal}/groups";
        var existing = await _client.GetFromJsonAsync<JsonElement>(path);
        var original = existing.GetProperty("groupIds").Deserialize<Guid[]>()!;
        var revision = existing.GetProperty("revision").GetInt32();
        var set = (await _client.GetFromJsonAsync<JsonElement>("/api/tables/sets")).EnumerateArray()
            .First(row => row.GetProperty("isDefault").GetBoolean()).GetProperty("id").GetGuid();

        using var first = await _client.PostAsJsonAsync("/api/terminal-groups", new { name = "Membership preview A" });
        using var second = await _client.PostAsJsonAsync("/api/terminal-groups", new { name = "Membership preview B" });
        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Guid[] groupIds = [firstId, secondId];
        using var saved = await _client.PutAsJsonAsync(path, new { groupIds, revision });
        saved.EnsureSuccessStatusCode();
        try
        {
            var read = await _client.GetFromJsonAsync<JsonElement>(path);
            Assert.Equal(groupIds, read.GetProperty("groupIds").Deserialize<Guid[]>());
            foreach (var groupId in groupIds)
            {
                var preview = await _client.GetFromJsonAsync<JsonElement>(
                    $"/api/tables/sets/{set}/assignment-preview?targetType=group&targetId={groupId}");
                var target = Assert.Single(preview.GetProperty("targets").EnumerateArray());
                Assert.Equal(terminal, target.GetProperty("id").GetGuid());
                Assert.True(target.GetProperty("eligible").GetBoolean());
            }

            using var stale = await _client.PutAsJsonAsync(path, new { groupIds = original, revision });
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            using var deletion = await _client.DeleteAsync($"/api/terminal-groups/{secondId}");
            Assert.Equal(HttpStatusCode.Conflict, deletion.StatusCode);
        }
        finally
        {
            var state = await saved.Content.ReadFromJsonAsync<JsonElement>();
            using var reverted = await _client.PutAsJsonAsync(path, new { groupIds = original, revision = state.GetProperty("revision").GetInt32() });
            reverted.EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Group_phone_edits_respect_membership_limits_and_target_the_saved_members()
    {
        var site = (await _client.GetFromJsonAsync<JsonElement>("/api/sites"))[0].GetProperty("id").GetGuid();
        var groups = new List<Guid>();
        for (var index = 0; index <= 40; index++)
            groups.Add(await Create("/api/terminal-groups", new { name = $"Group membership fixture {index}" }));
        var source = groups[0];
        var destination = groups[40];
        var path = $"/api/terminal-groups/{destination}/phones";
        var full = await CreatePhone("0000000624", source);
        var movable = await CreatePhone("0000000625", source);
        var candidate = await CreatePhone("0000000626", null);
        var fullPath = $"/api/terminals/{full}/groups";
        var limit = await _client.GetFromJsonAsync<JsonElement>(fullPath);
        Assert.Equal(40, limit.GetProperty("limit").GetInt32());
        using var forty = await _client.PutAsJsonAsync(fullPath,
            new { groupIds = groups.Take(40).ToArray(), revision = limit.GetProperty("revision") });
        forty.EnsureSuccessStatusCode();
        var fullState = await forty.Content.ReadFromJsonAsync<JsonElement>();
        using var fortyOne = await _client.PutAsJsonAsync(fullPath,
            new { groupIds = groups, revision = fullState.GetProperty("revision") });
        Assert.Equal(HttpStatusCode.BadRequest, fortyOne.StatusCode);

        var sourceMembers = await _client.GetFromJsonAsync<JsonElement>($"/api/terminal-groups/{source}/phones?membersOnly=true");
        Assert.Equal(new[] { full, movable }, sourceMembers.EnumerateArray().Select(row => row.GetProperty("id").GetGuid()));
        Assert.All(sourceMembers.EnumerateArray(), row => Assert.True(row.GetProperty("primary").GetBoolean()));
        var choices = await _client.GetFromJsonAsync<JsonElement>(path);
        var fullRow = Find(choices, full);
        var movableRow = Find(choices, movable);
        var candidateRow = Find(choices, candidate);
        Assert.True(fullRow.GetProperty("atLimit").GetBoolean());
        using var blocked = await _client.PutAsJsonAsync(path, new[] { Edit(movableRow, true), Edit(fullRow, true) });
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Equal(0, (await _client.GetFromJsonAsync<JsonElement>(path + "?membersOnly=true")).GetArrayLength());
        using var missingChoice = await _client.PutAsJsonAsync(path,
            new[] { new { terminalId = candidate, revision = candidateRow.GetProperty("revision") } });
        Assert.Equal(HttpStatusCode.BadRequest, missingChoice.StatusCode);

        using var added = await _client.PutAsJsonAsync(path, new[] { Edit(movableRow, true), Edit(candidateRow, true) });
        added.EnsureSuccessStatusCode();
        var members = await _client.GetFromJsonAsync<JsonElement>(path + "?membersOnly=true");
        Assert.False(Find(members, movable).GetProperty("primary").GetBoolean());
        Assert.True(Find(members, candidate).GetProperty("primary").GetBoolean());
        var set = (await _client.GetFromJsonAsync<JsonElement>("/api/tables/sets")).EnumerateArray()
            .First(row => row.GetProperty("isDefault").GetBoolean()).GetProperty("id").GetGuid();
        var preview = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/tables/sets/{set}/assignment-preview?targetType=group&targetId={destination}");
        Assert.Equal(new[] { movable, candidate }.Order(),
            preview.GetProperty("targets").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()).Order());

        using var stale = await _client.PutAsJsonAsync(path, new[] { Edit(Find(members, candidate), false), Edit(movableRow, false) });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(2, (await _client.GetFromJsonAsync<JsonElement>(path + "?membersOnly=true")).GetArrayLength());
        using var removedPrimary = await _client.PutAsJsonAsync($"/api/terminal-groups/{source}/phones",
            new[] { Edit(Find(sourceMembers, movable), false, Find(members, movable).GetProperty("revision").GetInt32()) });
        removedPrimary.EnsureSuccessStatusCode();
        var retained = await _client.GetFromJsonAsync<JsonElement>(path + "?membersOnly=true");
        Assert.True(Find(retained, movable).GetProperty("primary").GetBoolean());
        using var removed = await _client.PutAsJsonAsync(path, new[] { Edit(Find(retained, candidate), false) });
        removed.EnsureSuccessStatusCode();
        Assert.Equal(movable, Assert.Single((await _client.GetFromJsonAsync<JsonElement>(path + "?membersOnly=true"))
            .EnumerateArray()).GetProperty("id").GetGuid());

        Task<Guid> CreatePhone(string terminalIdHex, Guid? terminalGroupId) => Create("/api/terminals",
            new { displayName = $"Group membership phone {terminalIdHex[^2..]}", terminalIdHex, terminalGroupId, siteId = site, status = 0 });

        async Task<Guid> Create(string route, object body)
        {
            using var response = await _client.PostAsJsonAsync(route, body);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }

        static JsonElement Find(JsonElement rows, Guid id) => rows.EnumerateArray().Single(row => row.GetProperty("id").GetGuid() == id);
        static object Edit(JsonElement row, bool member, int? revision = null) => new
        {
            terminalId = row.GetProperty("id").GetGuid(), revision = revision ?? row.GetProperty("revision").GetInt32(), member
        };
    }

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
        Assert.Equal(0, batch!.GetProperty("status").GetInt32()); // Pending until the phone calls

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
        Assert.Equal(0, retryBody.GetProperty("status").GetInt32());
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
        var endpoint = $"/api/terminals/{term}/downloads";
        using var ambiguous = await _client.PostAsJsonAsync(endpoint, new { tableSetId = setId, partialTableDefinitionIds = new[] { idB } });
        Assert.Equal(HttpStatusCode.BadRequest, ambiguous.StatusCode);
        using var unknown = await _client.PostAsJsonAsync(endpoint, new { tableSetId = setId, scope = "Selected" });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        var dl = await _client.PostAsJsonAsync(endpoint, new
        {
            tableSetId = setId,
            scope = "Partial",
            partialTableDefinitionIds = new[] { idB }
        });
        Assert.Equal(HttpStatusCode.Created, dl.StatusCode);
        var batchId = JsonSerializer.Deserialize<JsonElement>(await dl.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/downloads/{batchId}");
        Assert.Equal(2, detail!.GetProperty("timeline").GetArrayLength());
        foreach (var step in detail.GetProperty("timeline").EnumerateArray())
        {
            Assert.Equal(3, step.GetProperty("hostDownloadPhase").GetInt32());
            Assert.Equal("Queued", step.GetProperty("hostDownloadPhaseName").GetString());
        }
    }

    private static HttpClient CreateClient(ApiFixture f)
    {
        var c = TestOperatorAuthentication.CreateAuthenticatedClient(f, "itest@local", "Admin");
        return c;
    }
}
