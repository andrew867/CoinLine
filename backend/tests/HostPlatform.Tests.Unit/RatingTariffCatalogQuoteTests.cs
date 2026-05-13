using HostPlatform.Domain;
using HostPlatform.Rating;

namespace HostPlatform.Tests.Unit;

public sealed class RatingTariffCatalogQuoteTests
{
    private static RatePlanVersion EmptyRulesVersion()
    {
        var ver = new RatePlanVersion { Id = Guid.NewGuid(), VersionNumber = 1 };
        return ver;
    }

    [Fact]
    public void Destination_prefix_tariff_when_no_rule_matches()
    {
        var t = new Tariff { Id = Guid.NewGuid(), Name = "dest", RatePerMinuteUsd = 0.03m };
        var ver = EmptyRulesVersion();
        ver.DestinationPrefixes.Add(new DestinationPrefix
        {
            PrefixDigits = "333",
            TariffId = t.Id,
            Tariff = t
        });

        var q = RatingEngine.Quote(new RatingEngine.QuoteRequest(
            "3339876",
            RatingMode.RealTimeRated,
            Guid.NewGuid(),
            2m,
            DateTime.UtcNow,
            Array.Empty<DialedNumberClass>(),
            ver));

        Assert.Equal(RatingDecisionKind.Allowed, q.DecisionKind);
        Assert.Equal(0.06m, q.AmountUsd);
        Assert.Equal(RatingAirtimeSource.DestinationPrefix, q.AirtimeSource);
    }

    [Fact]
    public void Time_band_tariff_overrides_destination_in_peak_window()
    {
        var tLow = new Tariff { Id = Guid.NewGuid(), Name = "low", RatePerMinuteUsd = 0.03m };
        var tPeak = new Tariff { Id = Guid.NewGuid(), Name = "peak", RatePerMinuteUsd = 0.15m };
        var ver = EmptyRulesVersion();
        ver.DestinationPrefixes.Add(new DestinationPrefix
        {
            PrefixDigits = "333",
            TariffId = tLow.Id,
            Tariff = tLow
        });
        ver.TimeBands.Add(new TimeBand
        {
            DayOfWeekMask = 127,
            StartMinuteOfDay = 600,
            EndMinuteOfDay = 780,
            TariffId = tPeak.Id,
            Tariff = tPeak
        });

        var peakUtc = new DateTime(2026, 1, 6, 12, 0, 0, DateTimeKind.Utc);
        var qPeak = RatingEngine.Quote(new RatingEngine.QuoteRequest(
            "3334000",
            RatingMode.RealTimeRated,
            Guid.NewGuid(),
            1m,
            peakUtc,
            Array.Empty<DialedNumberClass>(),
            ver));
        Assert.Equal(0.15m, qPeak.AmountUsd);
        Assert.Equal(RatingAirtimeSource.TimeBand, qPeak.AirtimeSource);

        var offUtc = new DateTime(2026, 1, 6, 14, 0, 0, DateTimeKind.Utc);
        var qOff = RatingEngine.Quote(new RatingEngine.QuoteRequest(
            "3334000",
            RatingMode.RealTimeRated,
            Guid.NewGuid(),
            1m,
            offUtc,
            Array.Empty<DialedNumberClass>(),
            ver));
        Assert.Equal(0.03m, qOff.AmountUsd);
        Assert.Equal(RatingAirtimeSource.DestinationPrefix, qOff.AirtimeSource);
    }

    [Fact]
    public void Day_mask_excludes_wrong_weekday()
    {
        var tSat = new Tariff { Id = Guid.NewGuid(), Name = "sat", RatePerMinuteUsd = 0.99m };
        var ver = EmptyRulesVersion();
        ver.TimeBands.Add(new TimeBand
        {
            DayOfWeekMask = 1 << (int)DayOfWeek.Saturday,
            StartMinuteOfDay = 0,
            EndMinuteOfDay = 1440,
            TariffId = tSat.Id,
            Tariff = tSat
        });

        var saturday = new DateTime(2026, 1, 3, 15, 0, 0, DateTimeKind.Utc);
        Assert.Equal(DayOfWeek.Saturday, saturday.DayOfWeek);
        var qSat = RatingEngine.Quote(new RatingEngine.QuoteRequest(
            "9991111",
            RatingMode.RealTimeRated,
            Guid.NewGuid(),
            1m,
            saturday,
            Array.Empty<DialedNumberClass>(),
            ver));
        Assert.Equal(RatingDecisionKind.Allowed, qSat.DecisionKind);
        Assert.Equal(0.99m, qSat.AmountUsd);

        var sunday = new DateTime(2026, 1, 4, 15, 0, 0, DateTimeKind.Utc);
        var qSun = RatingEngine.Quote(new RatingEngine.QuoteRequest(
            "9991111",
            RatingMode.RealTimeRated,
            Guid.NewGuid(),
            1m,
            sunday,
            Array.Empty<DialedNumberClass>(),
            ver));
        Assert.Equal(RatingDecisionKind.DeniedUnknownPrefix, qSun.DecisionKind);
    }
}
