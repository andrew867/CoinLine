using System.Security.Cryptography;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Dlog;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Infrastructure.Persistence;

public static class SeedData
{
    /// <param name="enableDemoData">When false, only catalog/protocol seeds run — no sample transit customer or lab terminals.</param>
    public static async Task EnsureSeedAsync(HostPlatformDbContext db, bool enableDemoData = true, CancellationToken ct = default)
    {
        await EnsureDlogMessageTypesAsync(db, ct);
        await EnsureCraftCommandTypesSeedAsync(db, ct);

        if (!enableDemoData)
        {
            await EnsureRatingSeedAsync(db, ct);
            await EnsureCardSeedAsync(db, ct);
            return;
        }

        var hadCustomers = await db.Customers.AnyAsync(ct);
        if (hadCustomers)
        {
            await EnsureRatingSeedAsync(db, ct);
            await EnsureCardSeedAsync(db, ct);
            return;
        }

        var cust = new Customer { Name = "Sample Transit Co", Code = "STC" };
        db.Customers.Add(cust);
        await db.SaveChangesAsync(ct);

        var site = new Site { CustomerId = cust.Id, Name = "Downtown Shelter", Code = "DTS" };
        var fw = new FirmwareVersion { Label = "MTR212-sample", BuildId = "seed" };
        var te = new TransportEndpoint { Kind = TransportKind.Tcp, DisplayName = "lab-gateway", ConnectionString = "tcp://127.0.0.1:7000" };
        var grp = new TerminalGroup { CustomerId = cust.Id, Name = "Fleet A" };
        db.AddRange(site, fw, te, grp);
        await db.SaveChangesAsync(ct);

        var term = new Terminal
        {
            SiteId = site.Id,
            TerminalGroupId = grp.Id,
            TransportEndpointId = te.Id,
            FirmwareVersionId = fw.Id,
            TerminalIdHex = "0102030405",
            DisplayName = "Payphone-01",
            Status = TerminalOperationalStatus.Provisioned
        };
        db.Terminals.Add(term);
        await db.SaveChangesAsync(ct);

        db.TerminalEvents.Add(new TerminalEvent
        {
            TerminalId = term.Id,
            EventType = "provisioned",
            OccurredAtUtc = DateTime.UtcNow,
            PayloadJson = """{"source":"seed"}"""
        });

        var tdRate = new TableDefinition
        {
            Name = "Rate table (opaque blob)",
            TableNumber = 10,
            Description = "MVP placeholder — host stores raw bytes only; no firmware layout interpretation."
        };
        var tdLcd = new TableDefinition
        {
            Name = "LCD NPA/NXX (opaque blob)",
            TableNumber = 20,
            Description = "MVP placeholder — least-cost dialing tables as opaque payload."
        };
        var tdIw = new TableDefinition
        {
            Name = "Instant win (opaque blob)",
            TableNumber = 30,
            Description = "MVP placeholder — instant-win configuration as opaque payload."
        };
        var ts = new TableSet
        {
            Name = "Default Host Tables",
            CustomerId = cust.Id,
            IsDefault = true,
            Status = TableSetStatus.Draft
        };
        db.AddRange(tdRate, tdLcd, tdIw, ts);
        await db.SaveChangesAsync(ct);

        foreach (var (def, sortOrder) in new[] { (tdRate, 0), (tdLcd, 1), (tdIw, 2) })
        {
            var raw = new byte[] { 0x01, (byte)(def.TableNumber % 256), 0x02 };
            var sha = TablePayloadHasher.Sha256Hex(raw);
            var tp = new TablePayload
            {
                RawContent = raw,
                Sha256Hex = sha,
                LengthBytes = raw.Length
            };
            db.TablePayloads.Add(tp);
            await db.SaveChangesAsync(ct);
            db.TableVersions.Add(new TableVersion
            {
                TableSetId = ts.Id,
                TableDefinitionId = def.Id,
                TableRevision = 1,
                TablePayloadId = tp.Id,
                PayloadSha256Hex = sha,
                SortOrder = sortOrder,
                ValidationPassed = true,
                Checksum = SHA256.HashData(raw)
            });
        }

        await db.SaveChangesAsync(ct);
        ts.Status = TableSetStatus.Published;
        ts.PublishedAtUtc = DateTime.UtcNow;
        ts.PublishGeneration = 1;
        await db.SaveChangesAsync(ct);

        var nccSession = new NccSession
        {
            TerminalId = term.Id,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            CorrelationId = "ncc-seed",
            LastFrameSample = new byte[] { 0x02, 0x00, 0x05, 0x01, 0x02, 0x03, 0x04, 0x05, 0x03 }
        };
        db.NccSessions.Add(nccSession);
        await db.SaveChangesAsync(ct);

        var seedRaw = new byte[] { 0x3F, 0x00 }; // 63 = DLOG_MT_RATE_REQUEST (wire type in first octet)
        db.DlogTransactions.Add(new DlogTransaction
        {
            TerminalId = term.Id,
            NccSessionId = nccSession.Id,
            Direction = (int)DlogDirection.TerminalToHost,
            MessageType = 63,
            MessageTypeName = "DLOG_MT_RATE_REQUEST",
            RawPayload = seedRaw,
            DecodedJson = "{}",
            IsUnknownMessageType = false,
            ImmediateClear = true,
            ProcessingStatus = (int)DlogProcessingStatus.Decoded,
            IdempotencyKey = DlogIdempotency.ComputeKey(
                seedRaw,
                DlogDirection.TerminalToHost,
                term.Id,
                nccSession.Id,
                "seed-session",
                null),
            CapturedAtUtc = DateTime.UtcNow,
            SessionCorrelationId = "seed-session"
        });

        var rp = new RatePlan { Name = "Local default", CustomerId = cust.Id, Mode = RatingMode.RealTimeRated };
        db.RatePlans.Add(rp);
        await db.SaveChangesAsync(ct);
        var ver = new RatePlanVersion
        {
            RatePlanId = rp.Id,
            VersionNumber = 1,
            Status = RatePlanVersionStatus.Published,
            PublishedAtUtc = DateTime.UtcNow
        };
        db.RatePlanVersions.Add(ver);
        await db.SaveChangesAsync(ct);
        db.RateRules.AddRange(
            new RateRule
            {
                RatePlanVersionId = ver.Id,
                Priority = 900,
                MatchKind = RateRuleMatchKind.Prefix,
                Pattern = "900",
                Outcome = RateRuleOutcome.Block,
                RatePerMinuteUsd = 0,
                Expression = """{"note":"MVP seed — block premium 900"}"""
            },
            new RateRule
            {
                RatePlanVersionId = ver.Id,
                Priority = 850,
                MatchKind = RateRuleMatchKind.Prefix,
                Pattern = "911",
                Outcome = RateRuleOutcome.Emergency,
                RatePerMinuteUsd = 0,
                Expression = "{}"
            },
            new RateRule
            {
                RatePlanVersionId = ver.Id,
                Priority = 700,
                MatchKind = RateRuleMatchKind.Prefix,
                Pattern = "555",
                Outcome = RateRuleOutcome.Rated,
                RatePerMinuteUsd = 0.02m,
                Expression = """{"label":"local-test-prefix"}"""
            },
            new RateRule
            {
                RatePlanVersionId = ver.Id,
                Priority = 600,
                MatchKind = RateRuleMatchKind.Prefix,
                Pattern = "1",
                Outcome = RateRuleOutcome.Rated,
                RatePerMinuteUsd = 0.05m,
                Expression = """{"label":"long-distance-prefix-1"}"""
            });
        rp.PublishedVersionId = ver.Id;
        db.DialedNumberClasses.AddRange(
            new DialedNumberClass
            {
                CustomerId = null,
                ClassName = "Emergency services (seed)",
                Pattern = "911",
                MatchKind = RateRuleMatchKind.Prefix,
                IsEmergency = true,
                SortOrder = 1
            },
            new DialedNumberClass
            {
                CustomerId = null,
                ClassName = "Community information (seed)",
                Pattern = "611",
                MatchKind = RateRuleMatchKind.Prefix,
                IsFree = true,
                SortOrder = 3
            },
            new DialedNumberClass
            {
                CustomerId = null,
                ClassName = "Toll-free (seed)",
                Pattern = "1800",
                MatchKind = RateRuleMatchKind.Prefix,
                IsFree = true,
                SortOrder = 5
            },
            new DialedNumberClass
            {
                CustomerId = null,
                ClassName = "Pay-per-call block (seed)",
                Pattern = "1900",
                MatchKind = RateRuleMatchKind.Prefix,
                IsBlocked = true,
                SortOrder = 10
            },
            new DialedNumberClass
            {
                CustomerId = null,
                ClassName = "Directory assistance (seed)",
                Pattern = "411",
                MatchKind = RateRuleMatchKind.Prefix,
                IsFree = true,
                SortOrder = 15
            });
        db.AuditEvents.Add(new AuditEvent
        {
            Category = "seed",
            Action = "database",
            Actor = "system",
            Resource = "HostPlatformDbContext",
            DetailJson = """{"message":"initial seed"}""",
            CorrelationId = "seed"
        });

        var pkg = new FirmwarePackage
        {
            Name = "Lab bundle",
            VersionLabel = "0.0.0-seed",
            ArtifactChecksum = Convert.FromHexString(
                "0909090909090909090909090909090909090909090909090909090909090909"),
            ArtifactSizeBytes = 1024,
            MetadataJson = """{"simulation":true}"""
        };
        db.FirmwarePackages.Add(pkg);
        await db.SaveChangesAsync(ct);

        db.FirmwareUpdateJobs.Add(new FirmwareUpdateJob
        {
            TerminalId = term.Id,
            FirmwarePackageId = pkg.Id,
            Status = FirmwareUpdateJobStatus.Simulation,
            SimulationMode = true,
            SafetyStateJson = """{"note":"simulation only"}"""
        });

        await db.SaveChangesAsync(ct);

        await EnsureCardSeedAsync(db, ct);
    }

    /// <summary>Idempotent card rails catalog + lab account — safe alongside partial upgrades.</summary>
    private static async Task EnsureCardSeedAsync(HostPlatformDbContext db, CancellationToken ct)
    {
        var specs = new (string Name, string Code, CardType Dt, bool AllowNeg, bool TestFx)[]
        {
            ("Magstripe (seed)", "MAG", CardType.Magstripe, false, true),
            ("Generic smartcard (seed)", "SC", CardType.Smartcard, false, false),
            ("EPurse host ledger (seed)", "EP", CardType.EPurse, false, false),
            ("Mondex rail (seed)", "MDX", CardType.Mondex, false, false),
            ("Smart City rail (seed)", "SMC", CardType.SmartCity, false, false),
            ("Proton rail (seed)", "PTN", CardType.Proton, false, false),
        };

        foreach (var s in specs)
        {
            if (await db.CardProducts.AnyAsync(p => p.Code == s.Code, ct))
                continue;
            db.CardProducts.Add(new CardProduct
            {
                Name = s.Name,
                Code = s.Code,
                DefaultCardType = s.Dt,
                AllowNegativeBalance = s.AllowNeg,
                IsTestFixtureCatalogEntry = s.TestFx
            });
        }

        await db.SaveChangesAsync(ct);

        var scTypes = new (string Code, string Name, int Atr, CardType Map, string Notes)[]
        {
            ("SC_GPM416", "Smart City GPM416 (sample)", 1, CardType.SmartCity, "See docs/protocols/host_platform/smart_city"),
            ("PTN_SAMPLE", "Proton sample", 2, CardType.Proton, "See docs/protocols/host_platform/proton"),
            ("EP_PURSE", "EPurse spare-table slice", 0, CardType.EPurse, "See docs/protocols/host_platform/epurse"),
        };

        foreach (var t in scTypes)
        {
            if (await db.SmartcardTypes.AnyAsync(x => x.Code == t.Code, ct))
                continue;
            db.SmartcardTypes.Add(new SmartcardType
            {
                Code = t.Code,
                Name = t.Name,
                AtrProfile = t.Atr,
                MapsToCardType = t.Map,
                Notes = t.Notes
            });
        }

        await db.SaveChangesAsync(ct);

        var orphanBalances = await db.CardAccounts
            .Where(a => !db.CardBalances.Any(cb => cb.CardAccountId == a.Id))
            .ToListAsync(ct);
        foreach (var a in orphanBalances)
        {
            db.CardBalances.Add(new CardBalance
            {
                CardAccountId = a.Id,
                Amount = a.Balance,
                Currency = "USD"
            });
        }

        if (orphanBalances.Count > 0)
            await db.SaveChangesAsync(ct);

        if (await db.CardAccounts.AnyAsync(ct))
            return;

        var mag = await db.CardProducts.FirstOrDefaultAsync(p => p.Code == "MAG", ct);
        var term = await db.Terminals.FirstOrDefaultAsync(ct);
        if (mag == null || term == null)
            return;

        var acct = new CardAccount
        {
            CardProductId = mag.Id,
            TerminalId = term.Id,
            PanLast4 = "1111",
            Balance = 50m,
            ResolvedCardType = CardType.Magstripe,
            CredentialTokenRef = "TEST-FIXTURE-TOKEN-MAG-001",
            CredentialKind = CardCredentialKind.TestFixture
        };
        db.CardAccounts.Add(acct);
        await db.SaveChangesAsync(ct);
        db.CardBalances.Add(new CardBalance { CardAccountId = acct.Id, Amount = acct.Balance, Currency = "USD" });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Idempotent: upgrades legacy DBs that have a rate plan row but no published rules (post-migration).
    /// </summary>
    private static async Task EnsureRatingSeedAsync(HostPlatformDbContext db, CancellationToken ct)
    {
        var plan = await db.RatePlans.Include(p => p.Versions).FirstOrDefaultAsync(p => p.Name == "Local default", ct);
        if (plan == null)
            return;

        var ver = plan.Versions.OrderBy(v => v.VersionNumber).FirstOrDefault();
        if (ver == null)
        {
            ver = new RatePlanVersion
            {
                RatePlanId = plan.Id,
                VersionNumber = 1,
                Status = RatePlanVersionStatus.Draft
            };
            db.RatePlanVersions.Add(ver);
            await db.SaveChangesAsync(ct);
        }

        if (!await db.RateRules.AnyAsync(r => r.RatePlanVersionId == ver.Id, ct))
        {
            db.RateRules.AddRange(
                new RateRule
                {
                    RatePlanVersionId = ver.Id,
                    Priority = 900,
                    MatchKind = RateRuleMatchKind.Prefix,
                    Pattern = "900",
                    Outcome = RateRuleOutcome.Block,
                    RatePerMinuteUsd = 0,
                    Expression = """{"note":"MVP seed — block premium 900"}"""
                },
                new RateRule
                {
                    RatePlanVersionId = ver.Id,
                    Priority = 850,
                    MatchKind = RateRuleMatchKind.Prefix,
                    Pattern = "911",
                    Outcome = RateRuleOutcome.Emergency,
                    RatePerMinuteUsd = 0,
                    Expression = "{}"
                },
                new RateRule
                {
                    RatePlanVersionId = ver.Id,
                    Priority = 700,
                    MatchKind = RateRuleMatchKind.Prefix,
                    Pattern = "555",
                    Outcome = RateRuleOutcome.Rated,
                    RatePerMinuteUsd = 0.02m,
                    Expression = """{"label":"local-test-prefix"}"""
                },
                new RateRule
                {
                    RatePlanVersionId = ver.Id,
                    Priority = 600,
                    MatchKind = RateRuleMatchKind.Prefix,
                    Pattern = "1",
                    Outcome = RateRuleOutcome.Rated,
                    RatePerMinuteUsd = 0.05m,
                    Expression = """{"label":"long-distance-prefix-1"}"""
                });
        }

        if (plan.PublishedVersionId == null && ver.Status != RatePlanVersionStatus.Published)
        {
            ver.Status = RatePlanVersionStatus.Published;
            ver.PublishedAtUtc = DateTime.UtcNow;
            plan.PublishedVersionId = ver.Id;
        }

        if (!await db.DialedNumberClasses.AnyAsync(c => c.Pattern == "911", ct))
        {
            db.DialedNumberClasses.AddRange(
                new DialedNumberClass
                {
                    CustomerId = null,
                    ClassName = "Emergency services (seed)",
                    Pattern = "911",
                    MatchKind = RateRuleMatchKind.Prefix,
                    IsEmergency = true,
                    SortOrder = 1
                },
                new DialedNumberClass
                {
                    CustomerId = null,
                    ClassName = "Community information (seed)",
                    Pattern = "611",
                    MatchKind = RateRuleMatchKind.Prefix,
                    IsFree = true,
                    SortOrder = 3
                },
                new DialedNumberClass
                {
                    CustomerId = null,
                    ClassName = "Toll-free (seed)",
                    Pattern = "1800",
                    MatchKind = RateRuleMatchKind.Prefix,
                    IsFree = true,
                    SortOrder = 5
                },
                new DialedNumberClass
                {
                    CustomerId = null,
                    ClassName = "Pay-per-call block (seed)",
                    Pattern = "1900",
                    MatchKind = RateRuleMatchKind.Prefix,
                    IsBlocked = true,
                    SortOrder = 10
                },
                new DialedNumberClass
                {
                    CustomerId = null,
                    ClassName = "Directory assistance (seed)",
                    Pattern = "411",
                    MatchKind = RateRuleMatchKind.Prefix,
                    IsFree = true,
                    SortOrder = 15
                });
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Idempotent craft opcode registry — referenced by <see cref="CraftController"/> destructive gates.</summary>
    private static async Task EnsureCraftCommandTypesSeedAsync(HostPlatformDbContext db, CancellationToken ct)
    {
        foreach (var (code, display, destructive, simOnly, notes) in new (string Code, string Display, bool Destructive,
                     bool SimOnly, string Notes)[]
                 {
                     ("ping", "Ping / heartbeat", false, true, "Safe probe — aligns with lab ping command name."),
                     ("craft.table_reload", "Remote table reload", true, true,
                         "HOST_VALIDATION — destructive on terminal; requires audit reason + confirm."),
                     ("craft.erase_nv", "Non-volatile erase / factory reset class", true, true,
                         "Never enqueue without explicit technician acknowledgement."),
                     ("craft.cdr_upload", "CDR / maintenance upload trigger", false, true,
                         "Intent-only until NCC/DLOG channel certified."),
                 })
        {
            if (await db.CraftCommandTypes.AnyAsync(t => t.Code == code, ct))
                continue;
            db.CraftCommandTypes.Add(new CraftCommandType
            {
                Code = code,
                DisplayName = display,
                IsDestructive = destructive,
                DefaultSimulationOnly = simOnly,
                Notes = notes
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureDlogMessageTypesAsync(HostPlatformDbContext db, CancellationToken ct)
    {
        if (await db.DlogMessageTypes.AnyAsync(ct))
            return;

        foreach (var e in DlogMessageTypeRegistry.AllEntries)
        {
            db.DlogMessageTypes.Add(new DlogMessageType
            {
                MtCode = e.MessageType,
                SymbolName = e.MessageTypeName,
                MessageAction = e.MessageAction,
                ImmediateClear = e.ImmediateClear,
                Notes = e.SourceNote
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
