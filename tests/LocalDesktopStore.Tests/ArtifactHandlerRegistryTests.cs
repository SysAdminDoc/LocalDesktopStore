using LocalDesktopStore.Models;
using LocalDesktopStore.Services;
using Xunit;

namespace LocalDesktopStore.Tests;

public sealed class ArtifactHandlerRegistryTests
{
    [Theory]
    [InlineData("Product.msi", ArtifactKind.Msi)]
    [InlineData("Product-InnoSetup.exe", ArtifactKind.Inno)]
    [InlineData("Product-NSIS.exe", ArtifactKind.Nsis)]
    [InlineData("Product-Setup.exe", ArtifactKind.GenericExe)]
    [InlineData("Product.msixbundle", ArtifactKind.Msix)]
    [InlineData("Product.appinstaller", ArtifactKind.AppInstaller)]
    [InlineData("Product.zip", ArtifactKind.PortableZip)]
    [InlineData("Product-1.2.3-full.nupkg", ArtifactKind.Velopack)]
    public void BundledClassifierRoutesSupportedWindowsAssets(string assetName, ArtifactKind expected)
    {
        Assert.Equal(expected, AssetClassifier.ClassifyByName(assetName));
    }

    [Fact]
    public void BundledRegistryHasOneHandlerPerArtifactKind()
    {
        var registry = ArtifactHandlerRegistry.CreateBundled();

        Assert.Equal(registry.Handlers.Count, registry.Handlers.Select(handler => handler.Kind).Distinct().Count());
        Assert.Contains(registry.Handlers, handler => handler.Id == "velopack-full-package");
        Assert.Contains(registry.Handlers, handler => handler.Id == "appimage");
    }

    [Fact]
    public void UnknownFilesStayOutOfTheInstallerPipeline()
    {
        Assert.Equal(ArtifactKind.Unknown, AssetClassifier.ClassifyByName("Product.tar.gz"));
        Assert.Equal(ArtifactKind.Unknown, AssetClassifier.ClassifyByName("README.md"));
    }

    [Fact]
    public void AppImageIsOnlyDiscoverableOnLinux()
    {
        var expected = OperatingSystem.IsLinux() ? ArtifactKind.AppImage : ArtifactKind.Unknown;
        Assert.Equal(expected, AssetClassifier.ClassifyByName("Product.AppImage"));
    }
}
