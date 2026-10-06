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
    public void Version_IsWindowsBuild() => Assert.Equal("0.2.0-win", OffloadVersion.String);

    // Spec 2.1/2.5: sizes inside Czech sentences.
    [Theory]
    [InlineData(12L, "12 B")]
    [InlineData(3_200_000_000L, "3,2 GB")]
    [InlineData(2_000_000_000L, "2 GB")]
    [InlineData(412_400_000_000L, "412 GB")]
    [InlineData(850_000_000L, "850 MB")]
    public void BytesCs_CzechDecimalComma(long bytes, string expected) => Assert.Equal(expected, Format.BytesCs(bytes));
}
