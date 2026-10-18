using System.IO;
using Clickpath;

namespace Clickpath.Tests;

public class AdBlockSettingsTests
{
    [Fact]
    public void The_ad_blocker_is_off_by_default_and_the_choice_is_saved_next_to_the_language()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lf-settings-{Guid.NewGuid():N}.json");
        try
        {
            Assert.False(AppSettings.Load(path).BlockAds);

            AppSettings.Save(path, new AppSettings { Language = Localization.Language.German, BlockAds = true });

            var loaded = AppSettings.Load(path);
            Assert.True(loaded.BlockAds);
            Assert.Equal(Localization.Language.German, loaded.Language);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("on", true)]
    [InlineData("off", false)]
    public void The_command_line_switch_overrides_the_saved_choice(string value, bool expected) =>
        Assert.Equal(expected, StartupOptions.Parse(["plan.json", "--adblock", value, "--run"]).BlockAds);

    [Fact]
    public void Without_the_switch_the_saved_choice_decides_and_the_plan_path_is_not_swallowed()
    {
        var options = StartupOptions.Parse(["--adblock", "plan.json"]);

        Assert.Null(options.BlockAds);
        Assert.Equal("plan.json", options.PlanPath);
    }
}
