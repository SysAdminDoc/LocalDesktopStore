using Velopack;
using Velopack.Locators;
using Velopack.Sources;
using LocalDesktopStore.Services;
using Xunit;

namespace LocalDesktopStore.Tests;

public sealed class VelopackUpdateServiceTests
{
    [Fact]
    public async Task EmptyLocalFeedReportsNoUpdateForVelopackInstall()
    {
        var root = Path.Combine(Path.GetTempPath(), "LocalDesktopStore-VelopackTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "releases.win.json"), "{\"Assets\":[]}");
            var locator = new TestVelopackLocator("LocalDesktopStore", "0.3.0", root);
            var source = new SimpleFileSource(new DirectoryInfo(root));
            var manager = new UpdateManager(source, null, locator);
            var service = new VelopackUpdateService(() => manager);

            var result = await service.CheckAndApplyAsync();

            Assert.Equal(SelfUpdateStatus.NoUpdate, result.Status);
            Assert.Equal("LocalDesktopStore is already up to date.", result.Message);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
