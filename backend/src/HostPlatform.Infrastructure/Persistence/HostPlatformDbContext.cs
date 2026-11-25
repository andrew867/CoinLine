using HostPlatform.Domain;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Infrastructure.Persistence;

public class HostPlatformDbContext : DbContext
{
    public HostPlatformDbContext(DbContextOptions<HostPlatformDbContext> options) : base(options) { }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<TerminalGroup> TerminalGroups => Set<TerminalGroup>();
    public DbSet<TerminalGroupMembership> TerminalGroupMemberships => Set<TerminalGroupMembership>();
    public DbSet<FirmwareVersion> FirmwareVersions => Set<FirmwareVersion>();
    public DbSet<InmateFeatureConfiguration> InmateFeatureConfigurations => Set<InmateFeatureConfiguration>();
    public DbSet<CarrierLibraryEntry> CarrierLibraryEntries => Set<CarrierLibraryEntry>();
    public DbSet<CardMasterDefinition> CardMasterDefinitions => Set<CardMasterDefinition>();
    public DbSet<QuickAccessCustomer> QuickAccessCustomers => Set<QuickAccessCustomer>();
    public DbSet<TransportEndpoint> TransportEndpoints => Set<TransportEndpoint>();
    public DbSet<Terminal> Terminals => Set<Terminal>();
    public DbSet<TerminalInventory> TerminalInventories => Set<TerminalInventory>();
    public DbSet<TerminalInmateConfiguration> TerminalInmateConfigurations => Set<TerminalInmateConfiguration>();
    public DbSet<TerminalCallbackRequest> TerminalCallbackRequests => Set<TerminalCallbackRequest>();
    public DbSet<TerminalEvent> TerminalEvents => Set<TerminalEvent>();
    public DbSet<TerminalStatusRecord> TerminalStatusRecords => Set<TerminalStatusRecord>();
    public DbSet<NccSession> NccSessions => Set<NccSession>();
    public DbSet<NccSessionFrame> NccSessionFrames => Set<NccSessionFrame>();
    public DbSet<NccFrameCapture> NccFrameCaptures => Set<NccFrameCapture>();
    public DbSet<CapturedHardwareSession> CapturedHardwareSessions => Set<CapturedHardwareSession>();
    public DbSet<DlogTransaction> DlogTransactions => Set<DlogTransaction>();
    public DbSet<DlogMessageType> DlogMessageTypes => Set<DlogMessageType>();
    public DbSet<DlogParseDiagnostic> DlogParseDiagnostics => Set<DlogParseDiagnostic>();
    public DbSet<DlogCorrelationLink> DlogCorrelationLinks => Set<DlogCorrelationLink>();
    public DbSet<DlogReplayRequest> DlogReplayRequests => Set<DlogReplayRequest>();
    public DbSet<UploadBatch> UploadBatches => Set<UploadBatch>();
    public DbSet<UploadRecord> UploadRecords => Set<UploadRecord>();
    public DbSet<TableDefinition> TableDefinitions => Set<TableDefinition>();
    public DbSet<TablePayload> TablePayloads => Set<TablePayload>();
    public DbSet<TableSet> TableSets => Set<TableSet>();
    public DbSet<TableVersion> TableVersions => Set<TableVersion>();
    public DbSet<CustomerTableOverride> CustomerTableOverrides => Set<CustomerTableOverride>();
    public DbSet<SiteTableOverride> SiteTableOverrides => Set<SiteTableOverride>();
    public DbSet<TerminalTableOverride> TerminalTableOverrides => Set<TerminalTableOverride>();
    public DbSet<TerminalTableAssignment> TerminalTableAssignments => Set<TerminalTableAssignment>();
    public DbSet<ScheduledTableDownload> ScheduledTableDownloads => Set<ScheduledTableDownload>();
    public DbSet<TableRecordUpdateRun> TableRecordUpdateRuns => Set<TableRecordUpdateRun>();
    public DbSet<DownloadBatch> DownloadBatches => Set<DownloadBatch>();
    public DbSet<DownloadBatchItem> DownloadBatchItems => Set<DownloadBatchItem>();
    public DbSet<RatePlan> RatePlans => Set<RatePlan>();
    public DbSet<RatePlanVersion> RatePlanVersions => Set<RatePlanVersion>();
    public DbSet<RateRule> RateRules => Set<RateRule>();
    public DbSet<DestinationPrefix> DestinationPrefixes => Set<DestinationPrefix>();
    public DbSet<TimeBand> TimeBands => Set<TimeBand>();
    public DbSet<Tariff> Tariffs => Set<Tariff>();
    public DbSet<DialedNumberClass> DialedNumberClasses => Set<DialedNumberClass>();
    public DbSet<CallAuthorizationRequest> CallAuthorizationRequests => Set<CallAuthorizationRequest>();
    public DbSet<CallRecord> CallRecords => Set<CallRecord>();
    public DbSet<RatingResult> RatingResults => Set<RatingResult>();
    public DbSet<RatingDiagnostic> RatingDiagnostics => Set<RatingDiagnostic>();
    public DbSet<CallChargeSegment> CallChargeSegments => Set<CallChargeSegment>();
    public DbSet<CardProduct> CardProducts => Set<CardProduct>();
    public DbSet<CardAccount> CardAccounts => Set<CardAccount>();
    public DbSet<SmartcardType> SmartcardTypes => Set<SmartcardType>();
    public DbSet<EPurseAccount> EPurseAccounts => Set<EPurseAccount>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<BalanceAdjustment> BalanceAdjustments => Set<BalanceAdjustment>();
    public DbSet<CardBalance> CardBalances => Set<CardBalance>();
    public DbSet<CardCredential> CardCredentials => Set<CardCredential>();
    public DbSet<CardReadEvent> CardReadEvents => Set<CardReadEvent>();
    public DbSet<CardWriteEvent> CardWriteEvents => Set<CardWriteEvent>();
    public DbSet<CardReconciliationBatch> CardReconciliationBatches => Set<CardReconciliationBatch>();
    public DbSet<SmartcardProfile> SmartcardProfiles => Set<SmartcardProfile>();
    public DbSet<EPurseProfile> EPurseProfiles => Set<EPurseProfile>();
    public DbSet<CraftSession> CraftSessions => Set<CraftSession>();
    public DbSet<CraftCommand> CraftCommands => Set<CraftCommand>();
    public DbSet<CraftAuditEvent> CraftAuditEvents => Set<CraftAuditEvent>();
    public DbSet<CraftCommandType> CraftCommandTypes => Set<CraftCommandType>();
    public DbSet<CraftDiagnostic> CraftDiagnostics => Set<CraftDiagnostic>();
    public DbSet<TerminalDiagnosticSnapshot> TerminalDiagnosticSnapshots => Set<TerminalDiagnosticSnapshot>();
    public DbSet<RemoteTableReloadRequest> RemoteTableReloadRequests => Set<RemoteTableReloadRequest>();
    public DbSet<CdrUploadRequest> CdrUploadRequests => Set<CdrUploadRequest>();
    public DbSet<FirmwarePackage> FirmwarePackages => Set<FirmwarePackage>();
    public DbSet<FirmwareArtifact> FirmwareArtifacts => Set<FirmwareArtifact>();
    public DbSet<FirmwareCompatibilityRule> FirmwareCompatibilityRules => Set<FirmwareCompatibilityRule>();
    public DbSet<FirmwareBlockManifest> FirmwareBlockManifests => Set<FirmwareBlockManifest>();
    public DbSet<FirmwareTarget> FirmwareTargets => Set<FirmwareTarget>();
    public DbSet<FirmwareUpdateJob> FirmwareUpdateJobs => Set<FirmwareUpdateJob>();
    public DbSet<FirmwareRollBackPlan> FirmwareRollBackPlans => Set<FirmwareRollBackPlan>();
    public DbSet<FirmwareUpdateSafetyCheck> FirmwareUpdateSafetyChecks => Set<FirmwareUpdateSafetyCheck>();
    public DbSet<FirmwareUpdateStep> FirmwareUpdateSteps => Set<FirmwareUpdateStep>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<OperatorAccount> OperatorAccounts => Set<OperatorAccount>();
    public DbSet<SubsystemHeartbeat> SubsystemHeartbeats => Set<SubsystemHeartbeat>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Terminal>(b =>
        {
            b.HasOne(t => t.Site).WithMany(s => s.Terminals).HasForeignKey(t => t.SiteId);
            b.HasOne(t => t.TerminalGroup).WithMany(g => g.Terminals).HasForeignKey(t => t.TerminalGroupId);
            b.Property(t => t.Version).IsConcurrencyToken();
            b.Property(t => t.TerminalFamilyId).HasMaxLength(48);
            b.HasOne(t => t.TransportEndpoint).WithMany().HasForeignKey(t => t.TransportEndpointId);
            b.HasOne(t => t.FirmwareVersion).WithMany().HasForeignKey(t => t.FirmwareVersionId);
        });
        modelBuilder.Entity<TerminalInventory>(b =>
        {
            b.HasOne<Terminal>().WithOne().HasForeignKey<TerminalInventory>(inventory => inventory.TerminalId);
            b.Property(inventory => inventory.Version).IsConcurrencyToken();
            b.OwnsOne(inventory => inventory.Details, details =>
            {
                details.Property(profile => profile.EquipmentCost).HasPrecision(12, 2);
                details.Property(profile => profile.LoopMileageCost).HasPrecision(12, 2);
            });
            b.Navigation(inventory => inventory.Details).IsRequired();
        });
        modelBuilder.Entity<TerminalInmateConfiguration>(b =>
        {
            b.HasOne<Terminal>().WithOne().HasForeignKey<TerminalInmateConfiguration>(row => row.TerminalId);
            b.HasOne(row => row.FeatureConfiguration).WithMany().HasForeignKey(row => row.FeatureConfigurationId)
                .OnDelete(DeleteBehavior.Restrict);
            b.Property(row => row.SetupConfigurationId).HasMaxLength(6);
            b.Property(row => row.Version).IsConcurrencyToken();
        });
        modelBuilder.Entity<TerminalGroupMembership>(b =>
        {
            b.HasKey(m => new { m.TerminalId, m.TerminalGroupId });
            b.HasOne<Terminal>().WithMany(t => t.AdditionalGroups).HasForeignKey(m => m.TerminalId);
            b.HasOne<TerminalGroup>().WithMany().HasForeignKey(m => m.TerminalGroupId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Site>(b => b.HasOne(s => s.Customer).WithMany(c => c.Sites).HasForeignKey(s => s.CustomerId));
        modelBuilder.Entity<TerminalGroup>(b =>
        {
            b.HasOne(g => g.Customer).WithMany().HasForeignKey(g => g.CustomerId);
            b.Property(g => g.Code).HasMaxLength(6);
            b.Property(g => g.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<FirmwareVersion>().Property(f => f.TableProfileId).HasMaxLength(48);
        modelBuilder.Entity<InmateFeatureConfiguration>(b =>
        {
            b.Property(c => c.Name).HasMaxLength(6);
            b.Property(c => c.NameKey).HasMaxLength(24);
            b.Property(c => c.Description).HasMaxLength(80);
            b.Property(c => c.SettingsJson).HasColumnType("jsonb");
            b.Property(c => c.Version).IsConcurrencyToken();
            b.HasIndex(c => c.NameKey).IsUnique();
        });
        modelBuilder.Entity<CarrierLibraryEntry>(b =>
        {
            b.Property(c => c.Code).HasMaxLength(6);
            b.Property(c => c.CodeKey).HasMaxLength(6);
            b.Property(c => c.Description).HasMaxLength(80);
            b.Property(c => c.RowPayload).HasColumnType("bytea");
            b.Property(c => c.Version).IsConcurrencyToken();
            b.HasIndex(c => new { c.TableNumber, c.CodeKey }).IsUnique();
        });
        modelBuilder.Entity<CardMasterDefinition>(b =>
        {
            b.Property(c => c.Code).HasMaxLength(4);
            b.Property(c => c.CodeKey).HasMaxLength(4);
            b.Property(c => c.Name).HasMaxLength(80);
            b.Property(c => c.RowPayload).HasColumnType("bytea");
            b.Property(c => c.Version).IsConcurrencyToken();
            b.HasIndex(c => new { c.TableNumber, c.CodeKey }).IsUnique();
        });
        modelBuilder.Entity<QuickAccessCustomer>(b =>
        {
            b.Property(c => c.Reference).HasMaxLength(6);
            b.Property(c => c.Name).HasMaxLength(160);
            b.Property(c => c.BillingComments).HasMaxLength(2000);
            b.Property(c => c.ContactName).HasMaxLength(40);
            b.Property(c => c.Phone).HasMaxLength(64);
            b.Property(c => c.Street).HasMaxLength(256);
            b.Property(c => c.City).HasMaxLength(128);
            b.Property(c => c.Province).HasMaxLength(128);
            b.Property(c => c.PostalCode).HasMaxLength(32);
            b.Property(c => c.Notes).HasMaxLength(2000);
            b.Property(c => c.Version).IsConcurrencyToken();
            b.HasIndex(c => c.Reference).IsUnique();
            b.ToTable(t => t.HasCheckConstraint("CK_QuickAccessCustomers_Reference", "\"Reference\" ~ '^[0-9]{6}$' AND \"Reference\" <> '000000'"));
        });
        modelBuilder.Entity<TableSet>(b =>
        {
            b.HasOne(ts => ts.Customer).WithMany().HasForeignKey(ts => ts.CustomerId);
            b.HasOne(ts => ts.TargetFirmwareVersion).WithMany().HasForeignKey(ts => ts.TargetFirmwareVersionId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<TableSet>().Property(set => set.Version).IsConcurrencyToken();
        modelBuilder.Entity<TableVersion>(b =>
        {
            b.Property(tv => tv.CardMasterLinksJson).HasColumnType("jsonb");
            b.Property(tv => tv.EmbeddedPayload).HasColumnName("Payload");
            b.Property(tv => tv.TableRevision).IsConcurrencyToken();
            b.Property(tv => tv.ConfigurationId).HasMaxLength(6);
            b.HasIndex(tv => new { tv.ConfigurationId, tv.TableDefinitionId });
            b.HasOne(tv => tv.TableSet).WithMany(ts => ts.Versions).HasForeignKey(tv => tv.TableSetId);
            b.HasOne(tv => tv.TableDefinition).WithMany().HasForeignKey(tv => tv.TableDefinitionId);
            b.HasOne(tv => tv.TablePayload).WithMany().HasForeignKey(tv => tv.TablePayloadId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<CustomerTableOverride>(b =>
        {
            b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId);
            b.HasOne(x => x.TableDefinition).WithMany().HasForeignKey(x => x.TableDefinitionId);
            b.HasOne(x => x.TableVersion).WithMany().HasForeignKey(x => x.TableVersionId);
            b.HasIndex(x => new { x.CustomerId, x.TableDefinitionId }).IsUnique();
        });

        modelBuilder.Entity<SiteTableOverride>(b =>
        {
            b.HasOne(x => x.Site).WithMany().HasForeignKey(x => x.SiteId);
            b.HasOne(x => x.TableDefinition).WithMany().HasForeignKey(x => x.TableDefinitionId);
            b.HasOne(x => x.TableVersion).WithMany().HasForeignKey(x => x.TableVersionId);
            b.HasIndex(x => new { x.SiteId, x.TableDefinitionId }).IsUnique();
        });

        modelBuilder.Entity<TerminalTableOverride>(b =>
        {
            b.HasOne(x => x.Terminal).WithMany().HasForeignKey(x => x.TerminalId);
            b.HasOne(x => x.TableDefinition).WithMany().HasForeignKey(x => x.TableDefinitionId);
            b.HasOne(x => x.TableVersion).WithMany().HasForeignKey(x => x.TableVersionId);
            b.HasIndex(x => new { x.TerminalId, x.TableDefinitionId }).IsUnique();
        });

        modelBuilder.Entity<TerminalTableAssignment>(b =>
        {
            b.Property(a => a.Version).IsConcurrencyToken();
            b.HasOne(a => a.Terminal).WithMany().HasForeignKey(a => a.TerminalId);
            b.HasOne(a => a.TableSet).WithMany().HasForeignKey(a => a.TableSetId);
            b.HasOne(a => a.PreviousTableSet).WithMany().HasForeignKey(a => a.PreviousTableSetId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(a => a.Site).WithMany().HasForeignKey(a => a.SiteId);
            b.HasOne(a => a.Customer).WithMany().HasForeignKey(a => a.CustomerId);
            b.HasIndex(a => a.TerminalId).IsUnique();
        });

        modelBuilder.Entity<TerminalCallbackRequest>(b =>
        {
            b.Property(request => request.Version).IsConcurrencyToken();
            b.HasOne(request => request.Terminal).WithMany().HasForeignKey(request => request.TerminalId);
            b.HasIndex(request => request.TerminalId).IsUnique();
        });

        modelBuilder.Entity<TableRecordUpdateRun>(b =>
        {
            b.Property(run => run.Version).IsConcurrencyToken();
            b.HasOne<TableSet>().WithMany().HasForeignKey(run => run.SourceSetId);
            b.HasIndex(run => new { run.SourceSetId, run.EndedAtUtc });
        });

        modelBuilder.Entity<ScheduledTableDownload>(b =>
        {
            b.Property(s => s.Version).IsConcurrencyToken();
            b.HasOne(s => s.Terminal).WithMany().HasForeignKey(s => s.TerminalId);
            b.HasOne(s => s.TableSet).WithMany().HasForeignKey(s => s.TableSetId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(s => s.SourceTableSet).WithMany().HasForeignKey(s => s.SourceTableSetId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(s => s.NccSession).WithMany().HasForeignKey(s => s.NccSessionId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(s => s.LastBatch).WithMany().HasForeignKey(s => s.LastBatchId).OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(s => s.TerminalId).IsUnique()
                .HasFilter("\"CompletedAtUtc\" IS NULL AND \"CancelledAtUtc\" IS NULL");
            b.HasIndex(s => new { s.TableSetId, s.AvailableAtUtc });
            b.HasIndex(s => s.AvailableAtUtc)
                .HasFilter("\"LastBatchId\" IS NULL AND \"NccSessionId\" IS NULL AND \"CompletedAtUtc\" IS NULL AND \"CancelledAtUtc\" IS NULL");
        });

        modelBuilder.Entity<DownloadBatch>(b =>
        {
            b.HasOne(d => d.Terminal).WithMany().HasForeignKey(d => d.TerminalId);
            b.HasOne(d => d.TableSet).WithMany().HasForeignKey(d => d.TableSetId);
            b.HasOne(d => d.NccSession).WithMany().HasForeignKey(d => d.NccSessionId).OnDelete(DeleteBehavior.SetNull);
            b.Property(d => d.Status).IsConcurrencyToken();
            b.HasIndex(d => d.TerminalId);
            b.HasIndex(d => d.TerminalId, "IX_DownloadBatches_TerminalId_ActiveTransfer").IsUnique()
                .HasFilter($"\"NccSessionId\" IS NOT NULL AND \"Status\" = {(int)DownloadBatchStatus.Running}");
        });

        modelBuilder.Entity<DownloadBatchItem>(b =>
        {
            b.HasOne(i => i.DownloadBatch).WithMany(d => d.Items).HasForeignKey(i => i.DownloadBatchId);
            b.HasOne(i => i.TableVersion).WithMany().HasForeignKey(i => i.TableVersionId);
        });

        modelBuilder.Entity<UploadBatch>(b =>
        {
            b.HasOne(u => u.Terminal).WithMany().HasForeignKey(u => u.TerminalId);
            b.HasOne(u => u.RelatedDlogTransaction).WithMany().HasForeignKey(u => u.RelatedDlogTransactionId);
        });

        modelBuilder.Entity<UploadRecord>(b => b.HasOne(r => r.UploadBatch).WithMany(u => u.Records).HasForeignKey(r => r.UploadBatchId));

        modelBuilder.Entity<RatePlan>(b =>
        {
            b.HasOne(r => r.Customer).WithMany().HasForeignKey(r => r.CustomerId);
            b.HasOne(r => r.PublishedVersion)
                .WithMany()
                .HasForeignKey(r => r.PublishedVersionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RatePlanVersion>(b =>
        {
            b.HasOne(v => v.RatePlan).WithMany(p => p.Versions).HasForeignKey(v => v.RatePlanId);
            b.HasIndex(v => new { v.RatePlanId, v.VersionNumber }).IsUnique();
        });

        modelBuilder.Entity<RateRule>(b =>
        {
            b.HasOne(r => r.RatePlanVersion).WithMany(v => v.Rules).HasForeignKey(r => r.RatePlanVersionId);
        });

        modelBuilder.Entity<DestinationPrefix>(b =>
        {
            b.HasOne(d => d.RatePlanVersion).WithMany(v => v.DestinationPrefixes).HasForeignKey(d => d.RatePlanVersionId);
            b.HasOne(d => d.Tariff).WithMany().HasForeignKey(d => d.TariffId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TimeBand>(b =>
        {
            b.HasOne(t => t.RatePlanVersion).WithMany(v => v.TimeBands).HasForeignKey(t => t.RatePlanVersionId);
            b.HasOne(t => t.Tariff).WithMany().HasForeignKey(t => t.TariffId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Tariff>(b =>
            b.HasOne(t => t.RatePlanVersion).WithMany(v => v.Tariffs).HasForeignKey(t => t.RatePlanVersionId));

        modelBuilder.Entity<DialedNumberClass>(b =>
            b.HasOne(c => c.Customer).WithMany().HasForeignKey(c => c.CustomerId));

        modelBuilder.Entity<CallAuthorizationRequest>(b =>
        {
            b.HasOne(a => a.Terminal).WithMany().HasForeignKey(a => a.TerminalId);
            b.HasOne(a => a.RatePlan).WithMany().HasForeignKey(a => a.RatePlanId);
        });

        modelBuilder.Entity<CallRecord>(b =>
        {
            b.HasOne(c => c.Terminal).WithMany().HasForeignKey(c => c.TerminalId);
            b.HasOne(c => c.AppliedRatePlanVersion).WithMany().HasForeignKey(c => c.AppliedRatePlanVersionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RatingResult>(b =>
        {
            b.HasOne(r => r.CallRecord).WithMany(c => c.Results).HasForeignKey(r => r.CallRecordId);
            b.HasOne(r => r.RatePlanVersion).WithMany().HasForeignKey(r => r.RatePlanVersionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RatingDiagnostic>(b =>
            b.HasOne(d => d.RatingResult).WithMany(r => r.Diagnostics).HasForeignKey(d => d.RatingResultId));

        modelBuilder.Entity<CallChargeSegment>(b =>
            b.HasOne(s => s.RatingResult).WithMany(r => r.Segments).HasForeignKey(s => s.RatingResultId));

        modelBuilder.Entity<CardAccount>(b =>
        {
            b.HasOne(a => a.CardProduct).WithMany().HasForeignKey(a => a.CardProductId);
            b.HasOne(a => a.Terminal).WithMany().HasForeignKey(a => a.TerminalId);
            b.HasOne(a => a.CardBalance).WithOne(c => c!.CardAccount).HasForeignKey<CardBalance>(c => c.CardAccountId);
            b.HasMany(a => a.SupplementalCredentials).WithOne(c => c.CardAccount).HasForeignKey(c => c.CardAccountId);
            b.HasOne(a => a.SmartcardProfile).WithOne(p => p!.CardAccount).HasForeignKey<SmartcardProfile>(p => p.CardAccountId);
            b.HasOne(a => a.EPurseProfile).WithOne(p => p!.CardAccount).HasForeignKey<EPurseProfile>(p => p.CardAccountId);
        });

        modelBuilder.Entity<CardBalance>(b => b.HasIndex(x => x.CardAccountId).IsUnique());

        modelBuilder.Entity<CardReadEvent>(b =>
        {
            b.HasOne(e => e.CardAccount).WithMany().HasForeignKey(e => e.CardAccountId);
            b.HasOne(e => e.Terminal).WithMany().HasForeignKey(e => e.TerminalId);
        });

        modelBuilder.Entity<CardWriteEvent>(b => b.HasOne(e => e.CardAccount).WithMany().HasForeignKey(e => e.CardAccountId));

        modelBuilder.Entity<SmartcardProfile>(b =>
            b.HasOne(p => p.SmartcardType).WithMany().HasForeignKey(p => p.SmartcardTypeId));

        modelBuilder.Entity<EPurseAccount>(b => b.HasOne(e => e.CardAccount).WithMany().HasForeignKey(e => e.CardAccountId));
        modelBuilder.Entity<PaymentTransaction>(b => b.HasOne(p => p.CardAccount).WithMany().HasForeignKey(p => p.CardAccountId));
        modelBuilder.Entity<BalanceAdjustment>(b => b.HasOne(a => a.CardAccount).WithMany().HasForeignKey(a => a.CardAccountId));

        modelBuilder.Entity<CraftSession>(b => b.HasOne(s => s.Terminal).WithMany().HasForeignKey(s => s.TerminalId));
        modelBuilder.Entity<CraftCommand>(b =>
        {
            b.HasOne(c => c.CraftSession).WithMany(s => s.Commands).HasForeignKey(c => c.CraftSessionId);
            b.HasOne(c => c.CraftCommandType).WithMany().HasForeignKey(c => c.CraftCommandTypeId)
                .OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity<CraftAuditEvent>(b => b.HasOne(e => e.CraftSession).WithMany(s => s.AuditTrail).HasForeignKey(e => e.CraftSessionId));
        modelBuilder.Entity<CraftCommandType>(b => b.HasIndex(x => x.Code).IsUnique());
        modelBuilder.Entity<CraftDiagnostic>(b =>
        {
            b.HasOne(d => d.Terminal).WithMany().HasForeignKey(d => d.TerminalId);
            b.HasOne(d => d.CraftSession).WithMany().HasForeignKey(d => d.CraftSessionId).IsRequired(false);
        });
        modelBuilder.Entity<TerminalDiagnosticSnapshot>(b =>
            b.HasOne(s => s.Terminal).WithMany().HasForeignKey(s => s.TerminalId));
        modelBuilder.Entity<RemoteTableReloadRequest>(b =>
            b.HasOne(r => r.Terminal).WithMany().HasForeignKey(r => r.TerminalId));
        modelBuilder.Entity<CdrUploadRequest>(b =>
            b.HasOne(r => r.Terminal).WithMany().HasForeignKey(r => r.TerminalId));

        modelBuilder.Entity<FirmwarePackage>(b =>
        {
            b.HasMany(p => p.Artifacts).WithOne(a => a.FirmwarePackage).HasForeignKey(a => a.FirmwarePackageId);
            b.HasOne(p => p.PrimaryArtifact).WithMany().HasForeignKey(p => p.PrimaryArtifactId).OnDelete(DeleteBehavior.SetNull);
            b.HasMany(p => p.CompatibilityRules).WithOne(r => r.FirmwarePackage).HasForeignKey(r => r.FirmwarePackageId);
            b.HasMany(p => p.BlockManifests).WithOne(m => m.FirmwarePackage).HasForeignKey(m => m.FirmwarePackageId);
        });
        modelBuilder.Entity<FirmwareCompatibilityRule>(b =>
            b.HasOne(r => r.RequiredTerminalFirmwareVersion).WithMany().HasForeignKey(r => r.RequiredTerminalFirmwareVersionId)
                .OnDelete(DeleteBehavior.SetNull));
        modelBuilder.Entity<FirmwareBlockManifest>(b =>
            b.HasOne(m => m.FirmwareArtifact).WithMany().HasForeignKey(m => m.FirmwareArtifactId).OnDelete(DeleteBehavior.SetNull));
        modelBuilder.Entity<FirmwareTarget>(b => b.HasOne(t => t.FirmwarePackage).WithMany().HasForeignKey(t => t.FirmwarePackageId));
        modelBuilder.Entity<FirmwareUpdateJob>(b =>
        {
            b.HasOne(j => j.Terminal).WithMany().HasForeignKey(j => j.TerminalId);
            b.HasOne(j => j.FirmwarePackage).WithMany().HasForeignKey(j => j.FirmwarePackageId);
            b.HasOne(j => j.FirmwareArtifact).WithMany().HasForeignKey(j => j.FirmwareArtifactId).OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(j => new { j.TerminalId, j.Status });
        });
        modelBuilder.Entity<FirmwareRollBackPlan>(b =>
            b.HasOne(r => r.FirmwareUpdateJob).WithOne(j => j.RollBackPlan).HasForeignKey<FirmwareRollBackPlan>(r => r.FirmwareUpdateJobId));
        modelBuilder.Entity<FirmwareUpdateSafetyCheck>(b =>
            b.HasOne(c => c.FirmwareUpdateJob).WithMany(j => j.SafetyChecks).HasForeignKey(c => c.FirmwareUpdateJobId));

        modelBuilder.Entity<FirmwareUpdateStep>(b => b.HasOne(s => s.FirmwareUpdateJob).WithMany(j => j.Steps).HasForeignKey(s => s.FirmwareUpdateJobId));

        modelBuilder.Entity<OperatorAccount>(b =>
        {
            b.HasIndex(a => a.Username).IsUnique();
            b.Property(a => a.Username).HasMaxLength(64);
            b.Property(a => a.DisplayName).HasMaxLength(120);
            b.Property(a => a.SessionGeneration).IsConcurrencyToken();
            b.Property(a => a.PermissionProfileJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<SubsystemHeartbeat>(b =>
        {
            b.HasKey(x => x.Subsystem);
            b.Property(x => x.Subsystem).HasMaxLength(64);
        });

        modelBuilder.Entity<AuditEvent>(b =>
        {
            b.HasIndex(e => e.CreatedAtUtc);
            b.HasIndex(e => new { e.TerminalId, e.CreatedAtUtc });
        });

        modelBuilder.Entity<DlogMessageType>(b =>
        {
            b.HasKey(x => x.MtCode);
            b.Property(x => x.MtCode).ValueGeneratedNever();
            b.ToTable("DlogMessageTypes");
        });

        modelBuilder.Entity<DlogTransaction>(b =>
        {
            b.HasOne(d => d.Terminal).WithMany().HasForeignKey(d => d.TerminalId);
            b.HasOne(d => d.NccSession).WithMany().HasForeignKey(d => d.NccSessionId);
            b.HasIndex(d => d.IdempotencyKey).IsUnique()
                .HasFilter("\"IdempotencyKey\" IS NOT NULL AND \"IdempotencyKey\" <> ''");
            b.HasMany(d => d.ParseDiagnostics).WithOne(p => p.DlogTransaction).HasForeignKey(p => p.DlogTransactionId);
        });

        modelBuilder.Entity<DlogCorrelationLink>(b =>
        {
            b.HasOne(l => l.RequestTransaction).WithMany().HasForeignKey(l => l.RequestTransactionId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(l => l.ResponseTransaction).WithMany().HasForeignKey(l => l.ResponseTransactionId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<TerminalEvent>(b => b.HasOne(e => e.Terminal).WithMany(t => t.Events).HasForeignKey(e => e.TerminalId));
        modelBuilder.Entity<TerminalStatusRecord>(b => b.HasOne(s => s.Terminal).WithMany().HasForeignKey(s => s.TerminalId));
        modelBuilder.Entity<NccSession>(b => b.HasOne(s => s.Terminal).WithMany().HasForeignKey(s => s.TerminalId));
        modelBuilder.Entity<NccSessionFrame>(b =>
        {
            b.HasOne(f => f.NccSession).WithMany().HasForeignKey(f => f.NccSessionId);
            b.HasIndex(f => new { f.NccSessionId, f.Sequence });
        });

        modelBuilder.Entity<CapturedHardwareSession>(b =>
        {
            b.HasOne<Terminal>().WithMany().HasForeignKey(x => x.TerminalId).OnDelete(DeleteBehavior.SetNull);
            b.Property(x => x.EnvelopeJson).HasColumnType("text");
            b.HasIndex(x => x.SessionKind);
            b.HasIndex(x => x.CreatedAtUtc);
            b.HasIndex(x => x.EnvelopeChecksumSha256);
        });

        modelBuilder.Entity<UploadBatch>().HasIndex(u => u.IdempotencyKey).IsUnique();

        modelBuilder.Entity<DownloadBatch>(b =>
        {
            b.HasIndex(d => d.ClientIdempotencyKey).IsUnique()
                .HasFilter("\"ClientIdempotencyKey\" IS NOT NULL AND \"ClientIdempotencyKey\" <> ''");
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var utc = DateTime.UtcNow;
        var op = OperatorContext.Current?.OperatorId ?? "system";
        foreach (var e in ChangeTracker.Entries<AuditableEntity>())
        {
            switch (e.State)
            {
                case EntityState.Added:
                    e.Entity.CreatedAtUtc = utc;
                    e.Entity.UpdatedAtUtc = utc;
                    e.Entity.CreatedBy = op;
                    e.Entity.UpdatedBy = op;
                    break;
                case EntityState.Modified:
                    e.Entity.UpdatedAtUtc = utc;
                    e.Entity.UpdatedBy = op;
                    break;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
