namespace HostPlatform.Protocols.Dlog;

/// <summary>
/// Known DLOG message types from reference firmware <c>OEM compatibility catalogue</c> and <c>01_dlog_mt_payload_catalogue.md</c>.
/// NPA/NXX wire values depend on <c>COMPRESS_LCD</c> — multiple rows share the same symbolic table index with different type bytes.
/// </summary>
public static class DlogMessageTypeRegistry
{
    private static readonly Dictionary<int, DlogMessageInfo> Map = Build();

    private static Dictionary<int, DlogMessageInfo> Build()
    {
        var d = new Dictionary<int, DlogMessageInfo>();

        void Add(int mt, string name, string ma, bool immediateClear, string? note = null) =>
            d[mt] = new DlogMessageInfo(mt, name, ma, immediateClear, note);

        Add(0, "DLOG_MT_RESERVED", "DLOG_MA_NONE", false, "OPEN QUESTION: wire 0 may be invalid; captures required.");

        Add(1, "DLOG_MT_CARD_AUTH_REQ", "DLOG_MA_OUT_IMMED_CLR", true);
        Add(2, "DLOG_MT_FUNF_CARD_AUTH", "DLOG_MA_OUT_IMMED_CLR", true);
        Add(3, "DLOG_MT_AUTH_RESPONSE", "DLOG_MA_INPUT", false);
        Add(4, "DLOG_MT_CD_CALL_DETAILS", "DLOG_MA_MULT_OUT", false);
        Add(5, "DLOG_MT_CDR_DETAILS_ACK", "DLOG_MA_CDR_ACK", false);
        Add(6, "DLOG_MT_MAINT_REQ", "DLOG_MA_MULT_OUT", false);
        Add(7, "DLOG_MT_ALARM", "DLOG_MA_MULT_OUT", false);
        Add(8, "DLOG_MT_CALL_IN", "DLOG_MA_OUTPUT", false);
        Add(9, "DLOG_MT_CALL_BACK", "DLOG_MA_OUTPUT", false);
        Add(10, "DLOG_MT_TERM_STATUS", "DLOG_MA_TERM_STATUS", false);
        Add(13, "DLOG_MT_END_DATA", "DLOG_MA_END_DATA", false);
        Add(14, "DLOG_MT_TAB_UPD_ACK", "DLOG_MA_MULT_OUT", false);
        Add(15, "DLOG_MT_MAINT_ACK", "DLOG_MA_MAINT_ACK", false);
        Add(16, "DLOG_MT_ALARM_ACK", "DLOG_MA_ALARM_ACK", false);
        Add(17, "DLOG_MT_TRANS_DATA", "DLOG_MA_TRANS_DATA", false);
        Add(18, "DLOG_MT_TABLE_UPD", "DLOG_MA_TAB_UPD_EXP", false);
        Add(19, "DLOG_MT_CALL_BACK_REQ", "DLOG_MA_CALL_BACK_REQ", false);
        Add(20, "DLOG_MT_TIME_SYNC", "DLOG_MA_TIME_SYNC", false);
        Add(21, "DLOG_MT_NCC_TERM_PARMS", "DLOG_MA_TAB_UPD_POTS", false);

        Add(26, "DLOG_MT_FCONFIG_OPTS", "DLOG_MA_TAB_UPD_POTS", false);
        Add(29, "DLOG_MT_ADVERT_PROMPTS", "DLOG_MA_TAB_UPD", false);
        Add(30, "DLOG_MT_USER_IF_PARMS", "DLOG_MA_TAB_UPD", false);
        Add(31, "DLOG_MT_INSTALL_PARMS", "DLOG_MA_TAB_UPD_POTS", false);
        Add(32, "DLOG_MT_COMM_STAT_PARMS", "DLOG_MA_TAB_UPD", false);
        Add(33, "DLOG_MT_MODEM_PARMS", "DLOG_MA_TAB_UPD", false);
        Add(34, "DLOG_MT_CALL_STAT_PARMS", "DLOG_MA_TAB_UPD", false);
        Add(35, "DLOG_MT_CALL_IN_PARMS", "DLOG_MA_TAB_UPD", false);
        Add(36, "DLOG_MT_TIME_SYNC_REQ", "DLOG_MA_OUTPUT", false);
        Add(37, "DLOG_MT_PERF_STATS", "DLOG_MA_MULT_OUT", false);
        Add(38, "DLOG_MT_CASH_BOX_STATUS", "DLOG_MA_CASH_BOX_STATUS", false);
        Add(39, "DLOG_MT_ATN_CALL_BACK", "DLOG_MA_ATN_CALL_BACK", false);

        for (var t = 40; t <= 47; t++)
            Add(t, $"DLOG_MT_ATN_REQ_{t}", "DLOG_MA_ATN_REQUIRED", false, "Attention-request range shares DLOG_ATN_REQ layout.");

        Add(49, "DLOG_MT_ANS_SUP_PARMS", "DLOG_MA_TAB_UPD", false);
        Add(50, "DLOG_MT_COIN_VAL_TABLE", "DLOG_MA_TAB_UPD", false);
        Add(51, "DLOG_MT_CASH_BOX_COLLECTION", "DLOG_MA_OUTPUT", false);
        Add(53, "DLOG_MT_CALL_DETAILS", "DLOG_MA_MULT_OUT", false, "Second DLOG_MT_CALL_DETAILS define in OEM compatibility catalogue (value 53).");
        Add(55, "DLOG_MT_REP_DIAL_LIST", "DLOG_MA_TAB_UPD", false);
        Add(56, "DLOG_MT_CALL_STATS", "DLOG_MA_MULT_OUT", false);
        Add(58, "DLOG_MT_LIMSERV_DATA", "DLOG_MA_TAB_UPD", false);
        Add(60, "DLOG_MT_SW_VERSION", "DLOG_MA_SW_VERSION", false);
        Add(61, "DLOG_MT_CN_CALL_DETAILS", "DLOG_MA_MULT_OUT", false);
        Add(62, "DLOG_MT_NUM_PLAN_TABLE", "DLOG_MA_TAB_UPD_POTS", false);
        Add(63, "DLOG_MT_RATE_REQUEST", "DLOG_MA_OUT_IMMED_CLR", true);
        Add(64, "DLOG_MT_RATE_RESPONSE", "DLOG_MA_INPUT", false);
        Add(65, "DLOG_MT_AUTH_RESP_CODE", "DLOG_MA_INPUT", false);
        Add(70, "DLOG_MT_MDX_DEPOSIT_REC", "DLOG_MA_MULT_OUT", false,
            "OPEN QUESTION: Mondex docs cite immediate-clear scheduling; XSETROM catalogue lists DLOG_MA_MULT_OUT — reconcile with captures.");
        Add(71, "DLOG_MT_CARRIER_STATS", "DLOG_MA_MULT_OUT", false);
        Add(72, "DLOG_MT_SPARE_TABLE", "DLOG_MA_TAB_UPD", false);
        Add(73, "DLOG_MT_RATE_TABLE", "DLOG_MA_TAB_UPD", false);

        for (var i = 0; i < 8; i++)
            Add(74 + i, $"DLOG_MT_NPA_NXX_TABLE_{i + 1}", "DLOG_MA_TAB_UPD (paged)", false, "COMPRESS_LCD==0 wire (OEM compatibility catalogue).");

        Add(90, "DLOG_MT_NPA_NXX_TABLE_9", "DLOG_MA_TAB_UPD (paged)", false, "COMPRESS_LCD==0 wire.");
        Add(91, "DLOG_MT_NPA_NXX_TABLE_10", "DLOG_MA_TAB_UPD (paged)", false, "COMPRESS_LCD==0 wire.");

        for (var i = 0; i < 15; i++)
            Add(101 + i, $"DLOG_MT_NPA_NXX_TABLE_{i + 1}", "DLOG_MA_TAB_UPD (paged)", false, "COMPRESS_LCD==1 wire.");

        for (var i = 0; i < 14; i++)
            Add(136 + i, $"DLOG_MT_NPA_NXX_TABLE_{i + 1}", "DLOG_MA_TAB_UPD (paged)", false, "COMPRESS_LCD==2 wire.");

        Add(154, "DLOG_MT_NPA_NXX_TABLE_15", "DLOG_MA_TAB_UPD (paged)", false, "COMPRESS_LCD==2 wire.");
        Add(155, "DLOG_MT_NPA_NXX_TABLE_16", "DLOG_MA_TAB_UPD (paged)", false, "COMPRESS_LCD==2 wire.");

        Add(82, "DLOG_MT_QUERY_TERM_ERR", "DLOG_MA_QUERY_TERM_ERR", false);
        Add(83, "DLOG_MT_TERM_ERR_REP", "DLOG_MA_OUTPUT", false);
        Add(84, "DLOG_MT_SERIAL_NUM", "DLOG_MA_LOCAL", false);
        Add(85, "DLOG_MT_VIS_PROMPTS_L1", "DLOG_MA_TAB_UPD", false);
        Add(86, "DLOG_MT_VIS_PROMPTS_L2", "DLOG_MA_TAB_UPD", false);
        Add(92, "DLOG_MT_CALL_SCREEN_LIST", "DLOG_MA_TAB_UPD", false);
        Add(93, "DLOG_MT_SCARD_PARM_TABLE", "DLOG_MA_TAB_UPD_POTS", false);
        Add(94, "DLOG_MT_DOWNLOAD_DATA", "DLOG_MA_TAB_UPD", false);
        Add(95, "DLOG_MT_INSTANT_WIN_TABLE", "DLOG_MA_TAB_UPD", false);
        Add(98, "DLOG_MT_VIS_PROMPTS_L3", "DLOG_MA_TAB_UPD", false, "When TWO_LANGUAGE_SUPPORT guards differ (OEM compatibility catalogue).");
        Add(99, "DLOG_MT_VIS_PROMPTS_L4", "DLOG_MA_TAB_UPD", false, "When TWO_LANGUAGE_SUPPORT guards differ (OEM compatibility catalogue).");

        Add(131, "DLOG_MT_GT_TABLE_1", "DLOG_MA_TAB_UPD", false, "GIT_FEATURE (OEM compatibility catalogue).");
        Add(132, "DLOG_MT_GT_TABLE_2", "DLOG_MA_TAB_UPD", false, "GIT_FEATURE (OEM compatibility catalogue).");
        Add(133, "DLOG_MT_GT_TABLE_3", "DLOG_MA_TAB_UPD", false, "GIT_FEATURE (OEM compatibility catalogue).");
        Add(134, "DLOG_MT_CARD_TABLE", "DLOG_MA_TAB_UPD_NOCD", false);
        Add(135, "DLOG_MT_CARRIER_TABLE", "DLOG_MA_TAB_UPD", false);
        Add(150, "DLOG_MT_NPA_SBR_TABLE", "DLOG_MA_TAB_UPD", false);
        Add(151, "DLOG_MT_INTL_SBR_TABLE", "DLOG_MA_TAB_UPD", false);
        Add(152, "DLOG_MT_DISCOUNT_TABLE", "DLOG_MA_TAB_UPD", false);
        Add(153, "DLOG_MT_GIT_USAGE_STATS", "DLOG_MA_MULT_OUT", false, "GIT_FEATURE (OEM compatibility catalogue).");

        return d;
    }

    public static bool TryGet(int messageType, out DlogMessageInfo info) =>
        Map.TryGetValue(messageType, out info!);

    public static string DescribeOrUnknown(int messageType) =>
        Map.TryGetValue(messageType, out var i) ? i.MessageTypeName : $"UNKNOWN_MT_{messageType}";

    /// <summary>
    /// Validates catalogue entries have non-empty symbolic names and action labels (integrity guard for docs/code drift).
    /// </summary>
    public static IReadOnlyList<string> ValidateMetadataCompleteness()
    {
        var issues = new List<string>();
        foreach (var e in Map.Values)
        {
            if (string.IsNullOrWhiteSpace(e.MessageTypeName))
                issues.Add($"Message type {e.MessageType}: MessageTypeName is empty.");
            if (string.IsNullOrWhiteSpace(e.MessageAction))
                issues.Add($"Message type {e.MessageType}: MessageAction is empty.");
        }

        return issues;
    }

    public static IReadOnlyCollection<DlogMessageInfo> AllEntries => Map.Values.OrderBy(x => x.MessageType).ToList();

    public sealed record DlogMessageInfo(
        int MessageType,
        string MessageTypeName,
        string MessageAction,
        bool ImmediateClear,
        string? SourceNote = null);
}
