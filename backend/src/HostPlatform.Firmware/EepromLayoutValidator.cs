namespace HostPlatform.Firmware;

/// <summary>Validates host-side metadata against the configuration storage profile catalog.</summary>
public static class EepromLayoutValidator
{
    public static bool TryValidateRecord(string recordId, ulong claimedStart, ulong claimedLength, out string? error)
    {
        error = null;
        var rec = EepromLayoutCatalog.Records.FirstOrDefault(r => r.Id == recordId);
        if (rec == null)
        {
            error = $"Unknown EEPROM record id '{recordId}'.";
            return false;
        }
        if (claimedStart != rec.StartAddress || claimedLength != rec.LengthBytes)
        {
            error = "Claimed layout range does not match catalog entry.";
            return false;
        }
        return true;
    }

    public static void ValidatePackageSectorHint(byte dlaSectorField)
    {
        if (dlaSectorField < EepromLayoutCatalog.DlaSectorStart || dlaSectorField > EepromLayoutCatalog.DlaSectorEnd)
            throw new InvalidOperationException("DLA sector hint outside OEM DLA window — compatibility validation failed.");
    }
}
