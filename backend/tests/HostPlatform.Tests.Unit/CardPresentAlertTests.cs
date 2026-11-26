using System.Buffers.Binary;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class CardPresentAlertTests
{
    [Theory]
    [InlineData(NativeTableLayouts.MultiPay)]
    [InlineData(NativeTableLayouts.Later)]
    public void Alert_timers_round_trip_independently_of_fraud_control(string profile)
    {
        var record = Enumerable.Repeat((byte)0x5A, 72).ToArray();
        record[0] = 26;
        var timings = new (string Key, int Offset, ushort Value)[]
        {
            ("onHookAlarmDelay", 55, 500), ("postOnHookAlarmDelay", 57, 200),
            ("cardAlarmDuration", 59, 1000), ("cardAlarmOn", 61, 50),
            ("cardAlarmOff", 63, 50), ("blockedReaderAlarm", 65, 300)
        };
        foreach (var timing in timings)
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(timing.Offset), timing.Value);

        var layout = NativeTableLayouts.Find(26, record, profile)!;
        var alert = layout.Sections.Single(section => section.Key == "cardAlert");
        var reporting = layout.Sections.Single(section => section.Key == "reporting");
        Assert.Contains(reporting.Fields, field => field.Key == "zeroValueCardAlarm");
        Assert.Equal(timings.Select(t => t.Key), alert.Fields.Select(f => f.Key));
        var unchanged = timings.ToDictionary(t => t.Key, t => t.Value.ToString());
        foreach (var timing in timings)
        {
            var field = alert.Fields.Single(f => f.Key == timing.Key);
            Assert.Equal(timing.Offset, field.Offset);
            Assert.Equal(unchanged[timing.Key], field.Read(record));
            Assert.DoesNotContain(reporting.Fields, f => f.Key == timing.Key);
            foreach (ushort value in new ushort[] { 0, 65535 })
            {
                var expected = record.ToArray();
                BinaryPrimitives.WriteUInt16LittleEndian(expected.AsSpan(timing.Offset), value);
                Assert.Equal(expected, layout.Apply(record, new Dictionary<string, string> { [timing.Key] = value.ToString() }));
            }
            foreach (var invalid in new[] { "-1", "65536" })
                Assert.Throws<InvalidOperationException>(() => layout.Apply(record, new Dictionary<string, string> { [timing.Key] = invalid }));
        }
        Assert.Equal(record, layout.Apply(record, unchanged));
    }
}
