namespace Offload.Core.Tests;

public class FormatTests
{
    [Fact]
    public void Hex64_IsSixteenLowercaseDigits() => Assert.Equal("00000000000000ff", Format.Hex64(255));

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(999L, "999 B")]
    [InlineData(1000L, "1.00 KB")]
    [InlineData(1_500_000L, "1.50 MB")]
    [InlineData(2_000_000_000_000L, "2.00 TB")]
    public void Bytes_UsesDecimalUnits(long bytes, string expected) => Assert.Equal(expected, Format.Bytes(bytes));

    [Fact]
    public void Rate_IsMegabytesPerSecond()
    {
        Assert.Equal("250 MB/s", Format.Rate(500_000_000, 2));
        Assert.Equal("-", Format.Rate(1, 0));
    }

    [Fact]
    public void Version_IsWindowsBuild() => Assert.Equal("0.1.0-win", OffloadVersion.String);
}
