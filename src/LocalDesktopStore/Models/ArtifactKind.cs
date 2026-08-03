namespace LocalDesktopStore.Models;

public enum ArtifactKind
{
    Unknown,
    Msi,
    Nsis,
    Inno,
    GenericExe,
    PortableZip,
    Msix,
    AppInstaller,
    Velopack,
    AppImage
}

public static class ArtifactKindExtensions
{
    public static string DisplayName(this ArtifactKind kind) => kind switch
    {
        ArtifactKind.Msi => "MSI installer",
        ArtifactKind.Nsis => "NSIS installer",
        ArtifactKind.Inno => "Inno Setup installer",
        ArtifactKind.GenericExe => "Setup .exe",
        ArtifactKind.PortableZip => "Portable .zip",
        ArtifactKind.Msix => "MSIX / MSIXBundle",
        ArtifactKind.AppInstaller => "App Installer manifest",
        ArtifactKind.Velopack => "Velopack update package",
        ArtifactKind.AppImage => "AppImage",
        _ => "Unknown"
    };

    public static int Priority(this ArtifactKind kind) => kind switch
    {
        ArtifactKind.Msi => 100,
        ArtifactKind.AppInstaller => 90,
        ArtifactKind.Msix => 85,
        ArtifactKind.Velopack => 82,
        ArtifactKind.Inno => 80,
        ArtifactKind.Nsis => 75,
        ArtifactKind.GenericExe => 60,
        ArtifactKind.PortableZip => 40,
        ArtifactKind.AppImage => 40,
        _ => 0
    };
}
