namespace RatioMaster.Core.Tests;

using RatioMaster.Core.Engine;
using RatioMaster.Core.Util;

using Xunit;

public class FormatTests
{
    [Theory]
    [InlineData(0, "0 bytes")]
    [InlineData(512, "512 bytes")]
    [InlineData(1024, "1.00 KB")]
    [InlineData(1048576, "1.00 MB")]
    [InlineData(1073741824, "1.00 GB")]
    [InlineData(1610612736, "1.50 GB")]
    public void FormatsFileSizes(long bytes, string expected)
    {
        Assert.Equal(expected, Format.FileSize(bytes));
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(59, "00:59")]
    [InlineData(90, "01:30")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "01:00:00")]
    [InlineData(7325, "02:02:05")]
    public void FormatsDurations(int seconds, string expected)
    {
        Assert.Equal(expected, Format.Duration(seconds));
    }

    [Fact]
    public void ReportsRatioAsNaNUntilEnoughHasBeenDownloaded()
    {
        var stats = new SessionStats();
        Assert.Null(stats.Ratio);
        Assert.Equal("NaN", Format.Ratio(stats.Ratio));
    }

    [Fact]
    public void ProgressIsDerivedFromWhatIsLeft()
    {
        var stats = new SessionStats { TotalSize = 1000, Left = 250 };

        Assert.Equal(75, stats.PercentComplete);
    }

    [Fact]
    public void JitterStaysInsideItsRangeAndIsZeroWhenDisabled()
    {
        var disabled = new RandomRange { Enabled = false, MinKiB = 5, MaxKiB = 9 };
        Assert.Equal(0, disabled.NextBytes());

        // Reversed bounds must not throw; the original crashed on min > max.
        var reversed = new RandomRange { Enabled = true, MinKiB = 9, MaxKiB = 5 };
        for (var i = 0; i < 200; i++)
        {
            var value = reversed.NextBytes();
            Assert.InRange(value, 5 * 1024, 9 * 1024);
        }
    }
}
