using System.Globalization;
using System.IO;
using Clickpath.Localization;

namespace Clickpath.Tests;

public sealed class LanguageTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"lf-settings-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        Loc.Use(Language.Auto);
        File.Delete(_path);
    }

    [Theory]
    [InlineData(Language.German, "en-US", true)]
    [InlineData(Language.English, "de-DE", false)]
    [InlineData(Language.Auto, "de-DE", true)]
    [InlineData(Language.Auto, "de-AT", true)]
    [InlineData(Language.Auto, "en-GB", false)]
    [InlineData(Language.Auto, "fr-FR", false)]
    public void The_language_choice_decides_between_german_and_english(Language choice, string windowsCulture, bool german)
    {
        Assert.Equal(german, Loc.IsGerman(choice, new CultureInfo(windowsCulture)));
    }

    [Fact]
    public void Strings_follow_the_chosen_language()
    {
        Loc.Use(Language.English);
        var english = Loc.T("Run.All");
        Loc.Use(Language.German);
        var german = Loc.T("Run.All");

        Assert.Equal("Run all", english);
        Assert.Equal("Alle ausführen", german);
        Assert.Equal(Language.German, Loc.Current);
    }

    [Fact]
    public void Both_languages_have_exactly_the_same_keys()
    {
        var (onlyEnglish, onlyGerman) = Loc.Mismatches();

        Assert.Empty(onlyEnglish);
        Assert.Empty(onlyGerman);
    }

    [Fact]
    public void No_german_text_is_a_copy_of_the_english_one_by_accident()
    {
        // identical texts are fine for names and symbols, but long sentences must be translated
        Assert.Empty(Loc.UntranslatedSentences());
    }

    [Fact]
    public void The_choice_survives_a_restart_and_a_missing_or_damaged_file_means_automatic()
    {
        AppSettings.Save(_path, new AppSettings { Language = Language.German });
        Assert.Equal(Language.German, AppSettings.Load(_path).Language);

        File.WriteAllText(_path, "{ not json");
        Assert.Equal(Language.Auto, AppSettings.Load(_path).Language);

        File.Delete(_path);
        Assert.Equal(Language.Auto, AppSettings.Load(_path).Language);
    }
}
