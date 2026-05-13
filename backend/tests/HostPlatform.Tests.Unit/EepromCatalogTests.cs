using HostPlatform.Firmware;

namespace HostPlatform.Tests.Unit;

public sealed class EepromCatalogTests
{
    [Fact]
    public void Catalog_has_dl_constants()
    {
        Assert.Equal(0x1000u, EepromLayoutCatalog.DlBlockSize);
        Assert.True(EepromLayoutCatalog.DlaSectorStart < EepromLayoutCatalog.DlaSectorEnd);
    }

    [Fact]
    public void Validator_accepts_catalog_record()
    {
        var r = EepromLayoutCatalog.Records[0];
        Assert.True(EepromLayoutValidator.TryValidateRecord(r.Id, r.StartAddress, r.LengthBytes, out var err));
        Assert.Null(err);
    }

    [Fact]
    public void Uart_catalog_contains_three_profiles()
    {
        Assert.Equal(3, UartCompatibilityCatalog.Profiles.Count);
        _ = UartCompatibilityCatalog.GetRequired("dla_code_channel");
    }
}
