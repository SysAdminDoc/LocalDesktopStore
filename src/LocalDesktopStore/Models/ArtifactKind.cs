namespace LocalDesktopStore.Models;

public enum ArtifactKind
{
    Unknown,
    Msi,
    Nsis,
    Inno,
    GenericExe,
    PortableZip
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
        _ => "Unknown"
    };

    public static int Priority(this ArtifactKind kind) => kind switch
    {
        ArtifactKind.Msi => 100,
        ArtifactKind.Inno => 80,
        ArtifactKind.Nsis => 75,
        ArtifactKind.GenericExe => 60,
        ArtifactKind.PortableZip => 40,
        _ => 0
    };
}
