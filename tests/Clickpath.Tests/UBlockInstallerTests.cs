using System.IO;
using System.IO.Compression;
using Clickpath.Browser;

namespace Clickpath.Tests;

public class UBlockInstallerTests
{
    [Fact]
    public void The_chromium_zip_is_picked_from_the_release_assets()
    {
        const string json = """
            {"tag_name":"1.99.0","assets":[
              {"name":"uBlock0_1.99.0.firefox.signed.xpi","browser_download_url":"https://example.test/a.xpi"},
              {"name":"uBlock0_1.99.0.chromium.zip","browser_download_url":"https://example.test/c.zip"},
              {"name":"uBlock0_1.99.0.thunderbird.xpi","browser_download_url":"https://example.test/t.xpi"}]}
            """;
        Assert.Equal(("uBlock0_1.99.0.chromium.zip", "https://example.test/c.zip"), UBlockInstaller.PickAsset(json));
    }

    [Theory]
    [InlineData("""{"assets":[{"name":"uBlock0_1.99.0.firefox.signed.xpi","browser_download_url":"https://example.test/a.xpi"}]}""")]
    [InlineData("""{"message":"Not Found"}""")]
    public void A_release_without_a_chromium_zip_gives_nothing(string json) => Assert.Null(UBlockInstaller.PickAsset(json));

    [Fact]
    public void Unpacking_puts_the_folder_with_the_manifest_at_the_target_and_replaces_an_old_one()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lf-ublock-{Guid.NewGuid():N}");
        try
        {
            var source = Path.Combine(root, "src", "uBlock0.chromium");
            Directory.CreateDirectory(Path.Combine(source, "js"));
            File.WriteAllText(Path.Combine(source, "manifest.json"), "{}");
            File.WriteAllText(Path.Combine(source, "js", "a.js"), "//");
            var zip = Path.Combine(root, "work", "u.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
            ZipFile.CreateFromDirectory(Path.Combine(root, "src"), zip);

            var target = Path.Combine(root, "extensions", "ublock-origin");
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "old.txt"), "old");

            UBlockInstaller.Unpack(zip, target);

            Assert.True(File.Exists(Path.Combine(target, "manifest.json")));
            Assert.True(File.Exists(Path.Combine(target, "js", "a.js")));
            Assert.False(File.Exists(Path.Combine(target, "old.txt")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void An_archive_without_a_manifest_is_refused()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lf-ublock-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "src"));
            File.WriteAllText(Path.Combine(root, "src", "readme.txt"), "x");
            var zip = Path.Combine(root, "work", "u.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
            ZipFile.CreateFromDirectory(Path.Combine(root, "src"), zip);

            Assert.Throws<InvalidDataException>(() => UBlockInstaller.Unpack(zip, Path.Combine(root, "extensions", "ublock-origin")));
        }
        finally { Directory.Delete(root, true); }
    }
}
