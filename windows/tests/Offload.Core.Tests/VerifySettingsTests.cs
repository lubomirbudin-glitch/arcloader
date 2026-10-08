using System.Text.Json;
using Offload.Cli;
using Offload.Core.Desktop;

namespace Offload.Core.Tests;

/// R11 (plan 10): "Ověřit primární disk během proxy" and the primary disk in settings.json; CLI --classic-verify and
/// --primary.
public class VerifySettingsTests
{
    [Fact]
    public void Setting_RoundTrip_AndJobConfigs()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, """{"verifyInProxy":false,"verifyPrimary":"first","projects":[{"name":"X","verifyPrimary":"something-newer"}]}""");
        var s = SettingsStore.Load(path);
        Assert.False(s.VerifyInProxy);
        Assert.Equal("first", s.VerifyPrimary);
        Assert.Equal(0, JobConfigs.Full(s, @"E:\", null, "2026-10-08").PrimaryDestination);
        Assert.False(JobConfigs.Full(s, @"E:\", null, "2026-10-08").VerifyInProxy);
        Assert.Null(JobConfigs.Primary(s.Projects[0]));   // unknown value = the fastest
        Assert.True(new AppSettings().VerifyInProxy);      // R11: on by default
        SettingsStore.Save(s, path);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.False(doc.RootElement.GetProperty("verifyInProxy").GetBoolean());
        Assert.Equal("first", doc.RootElement.GetProperty("verifyPrimary").GetString());
    }

    // R11: the setting is part of a project — "Uložit jako projekt…" takes it with the other values shown.
    [Fact]
    public void SaveAsProject_KeepsTheVerifySetting()
    {
        var s = new AppSettings { VerifyInProxy = false, VerifyPrimary = SettingsProfile.PrimaryFirst };
        var project = Projects.SaveAs(s, "Film X");
        Assert.Equal((false, "first"), (project.VerifyInProxy, project.VerifyPrimary));
        Assert.Equal(0, JobConfigs.Primary(project));
    }

    [Fact]
    public void Cli_ClassicVerifyAndPrimary_OntoTheJob()
    {
        var stage = new StageTwoOptions();
        var cmd = new System.CommandLine.Command("t");
        stage.AddTo(cmd);
        JobConfig Apply(params string[] args)
        {
            var config = new JobConfig(@"E:\", [@"D:\", @"F:\"]);
            stage.Apply(cmd.Parse(args), config, new AppSettings());
            return config;
        }

        Assert.True(Apply().VerifyInProxy);
        Assert.Null(Apply().PrimaryDestination);
        Assert.False(Apply("--classic-verify").VerifyInProxy);
        Assert.Equal(1, Apply("--primary", "2").PrimaryDestination);
        var e = Assert.Throws<ArgumentException>(() => Apply("--primary", "0"));
        Assert.Equal(Msg.PrimaryNotPositive(0), e.Message);
        // Above the number of destinations: refused (the CLI prints it and exits 1), no silent fallback to the fastest.
        var high = Assert.Throws<ArgumentException>(() => Apply("--primary", "3"));
        Assert.Equal(Msg.PrimaryTooHigh(3, 2), high.Message);
        Assert.Contains("jen 2", high.Message);
    }
}
