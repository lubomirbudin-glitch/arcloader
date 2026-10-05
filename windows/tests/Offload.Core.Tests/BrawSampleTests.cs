using System.Diagnostics;
using System.Globalization;
using Offload.BrawDecode;

namespace Offload.Core.Tests;

/// Need a real clip: OFFLOAD_BRAW_SAMPLE=<path to .braw> (skipped otherwise; see windows\README.md).
public class BrawSampleTests
{
    static string Sample => BrawSampleFactAttribute.Sample!;

    static (int Code, byte[] Stdout, string Stderr) Run(params string[] args) => BrawDecodeTests.Run(args);

    static BrawInfo Info(string scale)
    {
        var r = Run("--info", "--scale", scale, Sample);
        Assert.True(r.Code == 0, r.Stderr);
        return BrawBackend.ParseInfo(System.Text.Encoding.UTF8.GetString(r.Stdout));
    }

    static string N(long n) => n.ToString(CultureInfo.InvariantCulture);

    [BrawSampleFact]
    public void Info_DescribesTheClip()
    {
        var i = Info("eighth");
        Assert.True(i.SourceWidth >= 1920 && i.SourceHeight >= 1080, $"{i.SourceWidth}x{i.SourceHeight}");
        // The processed size at 1/8 is about a eighth of the sensor (the SDK rounds; never bigger than a quarter).
        Assert.InRange(i.Width, i.SourceWidth / 9, i.SourceWidth / 4);
        Assert.True(i.Fps > 0 && i.Frames > 0, $"{i.Fps} fps, {i.Frames} frames");
        Assert.Matches(@"^\d\d:\d\d:\d\d[:;]\d\d$", i.Timecode);
        Assert.False(string.IsNullOrEmpty(i.Camera));
        // Spike risk: gamma/gamut come through the OLE VARIANT — a sample proves the layout.
        Assert.False(string.IsNullOrEmpty(i.Gamma), "gamma missing");
        Assert.False(string.IsNullOrEmpty(i.Gamut), "gamut missing");
        Assert.InRange(i.ColorScienceGen ?? 0, 4, 9);
        // T3 review: the LUT rule (Luts.Braw) compares the gamma with exactly this SDK string.
        if (i.Gamma!.Contains("Film", StringComparison.OrdinalIgnoreCase)) Assert.Equal("Blackmagic Design Film", i.Gamma);
        var full = Info("full");
        Assert.Equal((i.SourceWidth, i.SourceHeight), (full.Width, full.Height));
    }

    [BrawSampleFact]
    public void Frames_ExactRgb48Bytes_InOrder()
    {
        var i = Info("eighth");
        long frame = (long)i.Width * i.Height * 6;
        var two = Run("--scale", "eighth", "--start", "0", "--count", "2", Sample);
        Assert.True(two.Code == 0, two.Stderr);
        Assert.Equal(2 * frame, two.Stdout.LongLength);
        var second = Run("--scale", "eighth", "--start", "1", "--count", "1", Sample);
        Assert.Equal(two.Stdout[(int)frame..], second.Stdout);   // the same frame decodes to the same bytes
        Assert.Contains(two.Stdout, b => b != 0);                // not a black buffer
    }

    /// More than one batch of 8: batches are written in frame order.
    [BrawSampleFact]
    public void Frames_AcrossBatches_InOrder()
    {
        var i = Info("eighth");
        Assert.True(i.Frames >= 10, "the sample needs at least 10 frames");
        long frame = (long)i.Width * i.Height * 6;
        var ten = Run("--scale", "eighth", "--count", "10", Sample);
        Assert.True(ten.Code == 0, ten.Stderr);
        Assert.Equal(10 * frame, ten.Stdout.LongLength);
        var ninth = Run("--scale", "eighth", "--start", "9", "--count", "1", Sample);
        Assert.Equal(ten.Stdout[(int)(9 * frame)..], ninth.Stdout);
    }

    [BrawSampleFact]
    public void Frames_ClampedAtTheEnd()
    {
        var i = Info("eighth");
        long frame = (long)i.Width * i.Height * 6;
        var last = Run("--scale", "eighth", "--start", N(i.Frames - 1), "--count", "5", Sample);
        Assert.True(last.Code == 0, last.Stderr);
        Assert.Equal(frame, last.Stdout.LongLength);
        var past = Run("--scale", "eighth", "--start", N(i.Frames), Sample);
        Assert.Equal((0, 0), (past.Code, past.Stdout.Length));
    }

    /// The real exe: stdout is binary (no text translation, nothing but frames).
    [BrawSampleFact]
    public void Exe_StdoutIsBinary()
    {
        var i = Info("eighth");
        var r = BrawDecodeTests.RunExe("--scale", "eighth", "--count", "3", Sample);
        Assert.True(r.Code == 0, r.Stderr);
        Assert.Equal(3L * i.Width * i.Height * 6, r.Stdout.LongLength);
        Assert.Equal(Run("--scale", "eighth", "--count", "3", Sample).Stdout, r.Stdout);
    }

    /// Ruling M1: when the reader closes the pipe (ffmpeg has what it wanted) the exe stops promptly instead of
    /// decoding the rest of the clip for nobody — and exits 0 like the Mac (a closed pipe is not a failure).
    [BrawSampleFact]
    public void Exe_ReaderClosesThePipe_StopsWithExit0()
    {
        var i = Info("eighth");
        Assert.True(i.Frames >= 2, "the sample needs at least 2 frames");
        var psi = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "braw-decode.exe"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in new[] { "--scale", "eighth", Sample }) psi.ArgumentList.Add(a);   // the whole clip
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        try
        {
            var first = new byte[1024];
            p.StandardOutput.BaseStream.ReadExactly(first);   // decoding has started
            p.StandardOutput.Close();                          // ffmpeg exited
            Assert.True(p.WaitForExit(60_000), "braw-decode kept running after its reader went away");
            Assert.Equal(0, p.ExitCode);
            Assert.Contains("braw-decode: stdout closed", err.Result);
        }
        finally
        {
            if (!p.HasExited) p.Kill();
        }
    }
}
