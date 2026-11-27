using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HostPlatform.Api.Authorization;

[Flags]
public enum PermissionOperation
{
    None = 0,
    Read = 1,
    Update = 2,
    Add = 4,
    Delete = 8
}

public sealed record PermissionScreen(string Key, string Label, PermissionOperation Operations);

public static class OperatorPermissions
{
    private sealed record Rule(string Screen, PermissionOperation Operations, bool Any);
    private static readonly Dictionary<(string Controller, string Action), List<Rule>> Rules = new();
    private static readonly List<PermissionScreen> Screens = new();
    private static readonly object ContextKey = new();

    static OperatorPermissions()
    {
        const PermissionOperation R = PermissionOperation.Read;
        const PermissionOperation U = PermissionOperation.Update;
        const PermissionOperation A = PermissionOperation.Add;
        const PermissionOperation D = PermissionOperation.Delete;

        Screen("overview", "Home and search", ("Operator", R, "Dashboard Search"));
        Screen("phones", "Phones",
            ("Terminals", R, "GetAll GetById Families Regions TimeAreas Identity FirmwareOptions"), ("TerminalIdCutover", R, "Preview Jobs Job Recovery"), ("TerminalIdCutover", U, "Schedule Cancel Reconcile"),
            ("Terminals", A, "Create CreateOptions"), ("Terminals", U, "SetFamily SetFirmware Update SetWireId"));
        Screen("inventory", "Phone inventory", ("TerminalInventory", R, "Get"), ("TerminalInventory", U, "Save"));
        Screen("memberships", "Phone group membership",
            ("Terminals", R, "Groups"), ("TerminalGroups", R, "Phones MembershipOptions"),
            ("Terminals", A | D, "AssignGroup AssignGroups"), ("TerminalGroups", A | D, "EditPhones"));
        Screen("phone-status", "Terminal status",
            ("Terminals", R, "Events"), ("Terminals", U, "PostStatus"), ("FieldEvents", R, "List Reference PhoneOptions CurrentStatus"),
            ("Operator", R, "TerminalTimeline"));
        Screen("field-event-catalogue", "Alarm and maintenance catalogue",
            ("FieldEvents", U, "SaveReference ImportMaintenanceExamples"));
        Screen("technicians", "Technician directory and activity", ("CraftTechnicians", R, "Directory Activity NearbyAlarms"),
            ("CraftTechnicians", A, "Create"), ("CraftTechnicians", U, "Update"));
        Screen("field-event-destinations", "Alarm and maintenance destinations",
            ("FieldEventDestinations", R, "List Options Preview Queue"),
            ("FieldEventDestinations", A, "Create"), ("FieldEventDestinations", U, "Update"),
            ("FieldEventDestinations", D, "Delete"));
        Screen("phone-lifecycle", "Terminal deactivation and reactivation", ("TerminalLifecycle", R, "Get"),
            ("TerminalLifecycle", U, "Save"));
        Screen("diagnostics", "Phone diagnostics",
            ("Terminals", R, "GetDiagnostics"), ("Terminals", A, "PostDiagnosticSnapshot"));
        Screen("customers", "Customers", ("Customers", R, "GetAll GetById"), ("Customers", A, "Create"),
            ("Customers", U, "Update"), ("Operator", R, "CustomerConsole CustomerTimeline"));
        Screen("sites", "Sites", ("Sites", R, "GetAll"), ("Sites", A, "Create"));
        Screen("groups", "Phone groups", ("TerminalGroups", R, "GetAll"), ("TerminalGroups", A, "Create"),
            ("TerminalGroups", U, "Rename UpdateDetails"), ("TerminalGroups", D, "Delete"));
        Screen("table-sets", "Table sets",
            ("Tables", R, "Sets SetById FamilyReview PreviewRecordCopy"),
            ("FirmwareLocalDialingStaging", A, "Create"),
            ("FirmwareNumberingStaging", A, "Create"),
            ("ConfigurationSetup", R, "Preview"), ("ConfigurationSetup", A, "Create"),
            ("FirmwareTariffStaging", A, "Create"),
            ("FirmwareCarrierStaging", A, "Create"),
            ("Terminals", R, "Families"),
            ("Tables", A, "CreateSet CloneSet"), ("Firmware", A, "StageAccessConfiguration StageScreeningConfiguration"), ("Tables", U, "UpdateSet SetFirmwareTarget SetConfigurationId PublishSet ApplyRecordCopy"),
            ("Tables", A | U, "FirmwareOptions"), ("FirmwareSetupStaging", A, "Create"));
        Screen("table-definitions", "Table definitions", ("Tables", R, "Definitions DefinitionById ReferenceRecords"), ("Tables", A, "CreateDefinition"));
        Screen("table-records", "Table records and advertising",
            ("Tables", R, "Versions Advertising"), ("Tables", A | U, "CreateVersion SaveAdvertising"),
            ("TerminalTariffReferences", R, "Get"),
            ("DialedNumberReview", R, "Review"),
            ("FirmwareTariffStaging", R, "Preview"),
            ("FirmwareCarrierStaging", R, "Preview"),
            ("Tables", D, "RemoveDraftRecord"),
            ("FirmwareNumberingStaging", R, "Preview"),
            ("ConfigurationLibrary", R, "List Options"),
            ("FirmwareSetupStaging", R, "Preview"),
            ("FirmwareLocalDialingStaging", R, "Preview"),
            ("NativeTableConfiguration", R, "Get"), ("NativeTableConfiguration", A, "Initialize AuthorPrompts"), ("NativeTableConfiguration", U, "Save"),
            ("NativeTableConfiguration", R, "PreviewNumberingOverlay"), ("NativeTableConfiguration", U, "ApplyNumberingOverlay"),
            ("CraftAccessConfiguration", R, "Reveal"),
            ("ScreeningBulk", R, "Preview"), ("ScreeningBulk", U, "Preview Apply"),
            ("CardSequence", R, "Get"), ("CardSequence", U, "Move Add Remove Refresh Link"),
            ("GraphicalStarter", A, "Create"), ("OperationalRecordAuthoring", R, "Get"), ("OperationalRecordAuthoring", A, "Create"),
            ("TableRecordUpdates", R, "Preview PausedRuns"), ("TableRecordUpdates", U, "Preview PausedRuns Apply Continue"),
            ("Tables", R, "ScopeOptions"), ("Tables", U, "ScopeOptions"));
        Screen("carrier-library", "Carrier library", ("CarrierLibrary", R, "List Get Schema Apply"),
            ("CarrierLibrary", A, "Create Schema"), ("CarrierLibrary", U, "Update"), ("CarrierLibrary", D, "Delete"));
        Screen("card-master", "Card master list", ("CardMaster", R, "List Get Schema Usage"),
            ("CardMaster", A, "Create Schema"), ("CardMaster", U, "Update Restore"), ("CardMaster", D, "Archive"),
            ("CardMasterRollout", R, "Preview"), ("CardMasterRollout", U, "Apply"));
        Screen("quick-access-customers", "Quick Access Key customer directory",
            ("QuickAccessCustomers", R, "Search Get Lookup"), ("QuickAccessCustomers", A, "Create"),
            ("QuickAccessCustomers", U, "Update Restore"), ("QuickAccessCustomers", D, "Archive"));
        Screen("quick-access-customer-lookup", "Quick Access Key customer reference lookup",
            ("QuickAccessCustomers", R, "Lookup"));
        Screen("table-assignments", "Table assignments",
            ("Tables", R, "PreviewAssignments ScopeOptions"), ("Tables", U, "AssignToScope PreviewAssignments ScopeOptions"),
            ("TerminalConfigurationWorksheet", R, "Get Options Preview"), ("TerminalConfigurationWorksheet", U, "Apply"),
            ("TerminalDeliveryReview", R, "Get"),
            ("Terminals", R, "GetTableAssignment TableAssignmentOptions RoutineCallIn"), ("Terminals", U, "SetTableAssignment RollbackTableAssignment"));
        Screen("downloads", "Downloads",
            ("Downloads", R, "List Get"), ("Downloads", U, "Cancel Retry"),
            ("DownloadPreparation", A, "Options Preview Prepare"),
            ("Tables", R, "ListSchedules"), ("Tables", A, "ScheduleForScope PreviewAssignments ScopeOptions"), ("Tables", U, "CancelSchedule"),
            ("Terminals", A, "StartDownload RequestTableReload"));
        Screen("callbacks", "Callbacks", ("TerminalCallbacks", R, "Get"), ("TerminalCallbacks", U, "Save Cancel"));
        Screen("calls", "Call records", ("CallRecords", R, "List Get"), ("CallRecords", A, "Create"),
            ("CallRecords", U, "Reconcile"));
        Screen("uploads", "Uploads", ("Uploads", R, "List Get"), ("Uploads", A, "Create Ingest"),
            ("Uploads", U, "Reprocess OperatorReview"), ("Terminals", A, "RequestCdrUpload"));
        Screen("cashbox", "Cashbox accounting", ("TerminalCashbox", R, "Get Options"), ("TerminalCashbox", A, "InitializeEmpty InitializeDefault"));
        Screen("ncc-sessions", "Phone sessions", ("NccSessions", R, "List Get Capture UploadObservations"), ("NccSessions", U, "Close Archive"));
        Screen("message-log", "Message log", ("Dlog", R, "MessageTypes CorrelationPairs MessagesLegacy Transactions TransactionById TransactionPayload Decode"),
            ("Dlog", A, "Ingest"), ("Dlog", U, "Replay"), ("Terminals", R, "DlogForTerminal"));
        Screen("frame-captures", "Frame captures", ("NccFrameCaptures", R, "List Get"),
            ("NccFrameCaptures", A, "Upload"), ("NccFrameCaptures", D, "Delete"));
        Screen("audit", "Audit", ("Audit", R, "Events"));
        Screen("rate-plans", "Rate plans", ("RatePlans", R, "List Get GetVersion"),
            ("RatePlans", A, "Create CreateVersion AddRule AddTariff AddDestinationPrefix AddTimeBand"),
            ("RatePlans", U, "Publish"), ("RateRules", U, "Update"), ("Tariffs", U, "Update"),
            ("DestinationPrefixes", U, "Update"), ("TimeBands", U, "Update"),
            ("Tariffs", D, "Delete"), ("DestinationPrefixes", D, "Delete"), ("TimeBands", D, "Delete"));
        Screen("number-classes", "Number classes", ("NumberClasses", R, "List"), ("NumberClasses", A, "Create"));
        Screen("rating", "Rating", ("RatingOperations", R, "Quote"), ("RatingOperations", A, "Authorize"));
        Screen("card-products", "Card products", ("Cards", R, "SimulationState Products Product"), ("Cards", A, "CreateProduct"));
        Screen("card-accounts", "Card accounts", ("Cards", R, "Accounts Account AccountTimeline"),
            ("Cards", A, "CreateAccount"), ("Cards", U, "Adjust"));
        Screen("card-transactions", "Card payments", ("Cards", R, "Transactions AuthorizationAttempts"), ("Cards", A, "ReadEvent PostTransaction WriteEvent"));
        Screen("reconciliation", "Reconciliation", ("Cards", R, "ReconciliationPending ListReconciliationBatches GetReconciliationBatch"),
            ("Cards", A, "PostReconciliationBatch"), ("Cards", U, "PostReconciliationPosted PostReconciliationClose PostReconciliationException"));
        Screen("craft", "Technician craft", ("Craft", R, "ExecutionPolicy CommandTypes Sessions SessionDetail CommandDetail"),
            ("Craft", A, "StartSession EnqueueCommand"), ("Craft", U, "EndSession SimulateCommand ExecuteLiveCommand CancelCommand"));
        Screen("firmware-registry", "Firmware registry",
            ("Firmware", R, "Packages TableProfiles FirmwareVersions PackageDetail Targets"),
            ("Firmware", A, "CreateFirmwareVersion CreatePackage AddArtifact AddCompatibilityRule CreateTarget"),
            ("Firmware", U, "PreviewFirmwareTableProfile SetFirmwareTableProfile"));
        Screen("firmware-jobs", "Firmware jobs",
            ("Firmware", R, "ExecutionPolicy ListJobs Job TransitionReview PreviewAccessStaging PreviewScreeningStaging"), ("Terminals", A, "StartFirmwareJob"),
            ("Firmware", U, "SimulateJob ExecuteLiveJob ApproveJob CancelJob"), ("Tables", U, "QueueFirmwareCancellation"),
            ("FirmwareConfigurationTransitions", R, "Options List Preview"), ("FirmwareConfigurationTransitions", U, "Stage Activate Cancel Recover"));
        Screen("hardware-validation", "Hardware validation",
            ("HardwareValidation", R, "Checklists EvidenceGuide ListCapturedSessions GetCapturedSession"),
            ("HardwareValidation", A, "ImportCapturedSession"), ("HardwareValidation", U, "ReplayCapturedSession"));
        Screen("transport", "Modem transport", ("SerialModem", R, "Get"), ("SipModem", R, "Get"), ("SipModem", U, "SelfTest Hangup"));
        Screen("inmate-features", "Inmate features", ("InmateFeatures", R, "Schema List Get"),
            ("InmateFeatures", A, "Create"), ("InmateFeatures", U, "Update"), ("InmateFeatures", D, "Delete"));
        Screen("inmate-assignments", "Inmate terminal configuration", ("TerminalInmateConfiguration", R, "Get"),
            ("TerminalInmateConfiguration", U, "Save"));
        Screen("smartcards", "Smartcard types", ("Smartcards", R, "Types Applications"));
        Screen("protocol-tools", "Protocol tools", ("NccProtocol", R, "Decode Encode"), ("NccProtocol", U, "Replay"));

        void Screen(string key, string label, params (string Controller, PermissionOperation Operations, string Actions)[] entries)
        {
            var supported = PermissionOperation.None;
            foreach (var (controller, operations, actions) in entries)
            {
                supported |= operations;
                foreach (var action in actions.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!Rules.TryGetValue((controller, action), out var rules))
                        Rules.Add((controller, action), rules = []);
                    rules.Add(new Rule(key, operations,
                        (key == "memberships" && operations == (A | D)) ||
                        ((key == "table-sets" || key == "table-records") && operations == (A | U))));
                }
            }
            Screens.Add(new PermissionScreen(key, label, supported));
        }
    }

    public static IReadOnlyList<PermissionScreen> Catalog => Screens;

    public static bool IsMapped(string controller, string action) =>
        controller == "Auth" || Rules.ContainsKey((controller, action));

    public static bool TryNormalize(IReadOnlyDictionary<string, int>? input, out Dictionary<string, int>? normalized)
    {
        normalized = null;
        if (input == null)
            return true;

        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (key, value) in input.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var screen = Screens.Find(screen => screen.Key == key);
            if (screen == null || value < 0 || (value & ~(int)screen.Operations) != 0)
                return false;
            if (value != 0)
                result.Add(key, value);
        }
        normalized = result;
        return true;
    }

    public static Dictionary<string, int>? ReadProfile(string? json)
    {
        if (json == null)
            return null;
        try
        {
            if (TryNormalize(JsonSerializer.Deserialize<Dictionary<string, int>>(json), out var profile) && profile != null)
                return profile;
        }
        catch (JsonException) { }
        return new Dictionary<string, int>();
    }

    public static void SetRequestProfile(HttpContext context, string? json) =>
        context.Items[ContextKey] = ReadProfile(json);

    public static bool Allows(HttpContext context, string screen, PermissionOperation operation)
    {
        var profile = context.Items[ContextKey] as Dictionary<string, int>;
        return profile == null || (profile.GetValueOrDefault(screen) & (int)operation) == (int)operation;
    }

    internal static bool AllowsAction(HttpContext context, string controller, string action)
    {
        if (context.Items[ContextKey] is not Dictionary<string, int> profile || controller == "Auth")
            return true;
        if (!Rules.TryGetValue((controller, action), out var rules))
            return false;
        return rules.Any(rule =>
        {
            var available = profile.GetValueOrDefault(rule.Screen) & (int)rule.Operations;
            return rule.Any ? available != 0 : available == (int)rule.Operations;
        });
    }
}

public sealed class OperatorPermissionFilter : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor is ControllerActionDescriptor action &&
            !OperatorPermissions.AllowsAction(context.HttpContext, action.ControllerName, action.ActionName))
            context.Result = new ForbidResult();
    }
}
