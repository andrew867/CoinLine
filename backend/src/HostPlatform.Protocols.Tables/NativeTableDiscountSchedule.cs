namespace HostPlatform.Protocols.Tables;

public static partial class NativeTableLayouts
{
    private const int HolidaySlots = 10;
    private const int DiscountPeriods = 8;
    private const int DiscountPeriodStart = 21;
    private const int DiscountPeriodSize = 8;

    private static NativeTableSection[] TimeDiscountSections()
    {
        string[] days = ["Weekdays", "Saturday", "Sunday", "Holidays"];
        TableFieldChoice[] discounts = Enumerable.Range(0, DiscountCount).Select(index =>
            new TableFieldChoice(index, $"MT93 discount {index + 1}")).ToArray();
        return
        [
            .. Rows("holiday", HolidaySlots, i => $"Holiday {i + 1}", (i, key) =>
                [Field($"{key}.month", "Month", 1 + i * 2, max: 12),
                 Field($"{key}.day", "Day of month", 2 + i * 2, max: 31, help: "Set month and day to zero to clear the holiday.")]),
            .. Rows("period", DiscountPeriods, i => $"{days[i / 2]}, period {i % 2 + 1}", (i, key) =>
            {
                var offset = DiscountPeriodStart + i * DiscountPeriodSize;
                var fields = new List<NativeTableField>
                {
                    Hour($"{key}.startHour", "Starts at hour", offset), Minute($"{key}.startMinute", "Start minute", offset + 1),
                    Field($"{key}.endHour", "Ends at hour", offset + 2, max: 24, help: "An all-day period ends at 24:00."),
                    Minute($"{key}.endMinute", "End minute", offset + 3)
                };
                for (var zone = 0; zone < DiscountZones.Length; zone++)
                {
                    var (zoneKey, zoneLabel) = DiscountZones[zone];
                    var fieldKey = $"{key}.{zoneKey}";
                    var position = offset + 4 + zone;
                    fields.Add(Field(fieldKey, $"{zoneLabel} discount byte", position));
                    fields.Add(new NativeTableField($"{fieldKey}Index", $"{zoneLabel} discount", position,
                        TableFieldEncoding.Bits, Mask: 0x0F, Choices: discounts));
                    fields.Add(Bits($"{fieldKey}Payment", $"{zoneLabel} payment", position, 0x30,
                        (0, "Disabled"), (1, "Coins"), (2, "Cards"), (3, "Coins and cards")));
                }
                return fields.ToArray();
            })
        ];
    }

    internal static void ValidateTimeDiscountChanges(ReadOnlySpan<byte> original, ReadOnlySpan<byte> edited)
    {
        for (var index = 0; index < HolidaySlots; index++)
        {
            var offset = 1 + index * 2;
            var month = edited[offset];
            var day = edited[offset + 1];
            if (original.Slice(offset, 2).SequenceEqual(edited.Slice(offset, 2)) || (month == 0 && day == 0))
                continue;
            if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(2000, month))
                throw new InvalidOperationException($"Enter a valid month and day for holiday {index + 1}.");
        }

        for (var index = 0; index < DiscountPeriods; index++)
        {
            var offset = DiscountPeriodStart + index * DiscountPeriodSize + 2;
            if (!original.Slice(offset, 2).SequenceEqual(edited.Slice(offset, 2)) && edited[offset] == 24 && edited[offset + 1] != 0)
                throw new InvalidOperationException("A discount period that ends at hour 24 must use minute 0.");
        }
    }

    internal static bool UsesTimeDiscountRates(ReadOnlySpan<byte> record)
    {
        for (var index = 0; index < DiscountPeriods; index++)
        {
            var period = record.Slice(DiscountPeriodStart + index * DiscountPeriodSize, DiscountPeriodSize);
            if (period[..2].SequenceEqual(period[2..4]))
                continue;
            foreach (var zone in period[4..])
                if ((zone & 0x30) != 0 && (zone & 0x0F) < DiscountCount)
                    return true;
        }
        return false;
    }
}
