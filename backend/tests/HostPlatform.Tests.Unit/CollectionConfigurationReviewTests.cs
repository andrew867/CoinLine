using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class CollectionConfigurationReviewTests
{
    [Fact]
    public void Missing_and_unrecognized_records_are_not_disabled_or_zero_thresholds()
    {
        var missing=CollectionConfigurationReview.Build(new Dictionary<int,byte[]>());
        Assert.Equal("missing",missing.StatisticsRecordStatus); Assert.Equal("missing",missing.CallInRecordStatus);
        Assert.Null(missing.OptionalCdrMode); Assert.Null(missing.NormalCdrUploadThreshold);
        Assert.Equal(new[]{34,35},missing.Findings.Select(f=>f.TableNumber));
        var invalid=CollectionConfigurationReview.Build(new Dictionary<int,byte[]>{[34]=[34], [35]=new byte[21]});
        Assert.Equal("unrecognized",invalid.StatisticsRecordStatus); Assert.Equal("unrecognized",invalid.CallInRecordStatus);
        Assert.Null(invalid.StatisticsIntervalDays); Assert.Null(invalid.CarrierStatisticsEnabled);
        Assert.All(invalid.Findings,f=>Assert.Contains("does not match",f.Message));
    }

    [Fact]
    public void Configured_controls_show_independent_statistics_optional_and_normal_thresholds_without_mutation()
    {
        var statistics=Record(34,19); var callIn=Record(35,21);
        statistics[3]=2;statistics[4]=7;statistics[13]=6;statistics[14]=3;
        new byte[]{8,15,1,4}.CopyTo(statistics,15);callIn[12]=11;
        var before=statistics.ToArray();var beforeCallIn=callIn.ToArray();
        var set=NativeTableSetReview.Build(new Dictionary<int,byte[]>{[34]=statistics,[35]=callIn});
        var review=Assert.IsType<CollectionConfigurationReview>(set.CollectionConfiguration);
        Assert.Equal("configured",review.StatisticsRecordStatus);Assert.Equal("configured",review.CallInRecordStatus);
        Assert.Equal(2,review.StatisticsIntervalDays);Assert.Equal(7,review.StatisticsUploadThreshold);Assert.True(review.CarrierStatisticsEnabled);
        Assert.Equal("Completed calls only",review.OptionalCdrMode);Assert.Equal(4,review.OptionalCdrModeMask);
        Assert.Equal(3,review.OptionalCdrUploadThreshold);Assert.Equal("08:15",review.OptionalWindowStart);
        Assert.Equal(1,review.OptionalWindowDays);Assert.Equal(4,review.OptionalWindowHours);Assert.Equal(11,review.NormalCdrUploadThreshold);
        Assert.Empty(review.Findings);Assert.Empty(set.Findings);Assert.Equal(before,statistics);Assert.Equal(beforeCallIn,callIn);
    }

    [Theory]
    [InlineData(8,"Completed calls except local coin")][InlineData(16,"Completed calls except local coin and stored-value")]
    public void Exclusion_and_zero_thresholds_are_explicit_and_not_an_upload_prediction(byte mask,string mode)
    {
        var statistics=Record(34,19);var callIn=Record(35,21);statistics[13]=(byte)(mask|2);
        var review=CollectionConfigurationReview.Build(new Dictionary<int,byte[]>{[34]=statistics,[35]=callIn});
        Assert.Equal(mode,review.OptionalCdrMode);Assert.Equal(0,review.OptionalCdrUploadThreshold);Assert.Equal(0,review.NormalCdrUploadThreshold);
        Assert.Contains(review.Findings,f=>f.TableNumber==34&&f.Message.Contains("excludes local coin"));
        Assert.Contains(review.Findings,f=>f.TableNumber==34&&f.Message.Contains("interpreted as one day"));
        Assert.Contains(review.Findings,f=>f.TableNumber==34&&f.Message.Contains("zero statistics"));
        Assert.Contains(review.Findings,f=>f.TableNumber==34&&f.Message.Contains("No positive optional"));
        Assert.Contains(review.Findings,f=>f.TableNumber==35&&f.Message.Contains("No positive normal"));
    }

    [Fact]
    public void Disabled_optional_policy_retains_stored_controls_and_unknown_combination_is_not_normalized()
    {
        var statistics=Record(34,19);statistics[3]=1;statistics[14]=40;statistics[15]=25;statistics[18]=24;
        var records=new Dictionary<int,byte[]>{[34]=statistics};
        var disabled=CollectionConfigurationReview.Build(records);
        Assert.Equal("Normal calls only (optional logging disabled)",disabled.OptionalCdrMode);
        Assert.Equal(40,disabled.OptionalCdrUploadThreshold);Assert.Null(disabled.OptionalWindowStart);
        Assert.DoesNotContain(disabled.Findings,f=>f.TableNumber==34);
        statistics[13]=5;
        var unknown=CollectionConfigurationReview.Build(records);
        Assert.Equal("Unrecognized combination",unknown.OptionalCdrMode);Assert.Equal(5,unknown.OptionalCdrModeMask);
        Assert.Contains(unknown.Findings,f=>f.Message.Contains("Several optional"));
        Assert.Contains(unknown.Findings,f=>f.Message.Contains("unrecognized start or duration"));
        Assert.Equal((byte)5,statistics[13]);
    }

    private static byte[] Record(int number,int length){var bytes=new byte[length];bytes[0]=(byte)number;return bytes;}
}
