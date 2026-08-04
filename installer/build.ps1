[CmdletBinding()]
param(
    [string] $Configuration = "Release",
    [string] $Runtime = "win-x64",
    [string] $Version = "0.3.0",
    [string] $OutputRoot = "publish"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\LocalDesktopStore\LocalDesktopStore.csproj"
$sourcePath = Join-Path $PSScriptRoot "LocalDesktopStore.wxs"
$publishPath = [IO.Path]::GetFullPath((Join-Path $repoRoot "$OutputRoot\LocalDesktopStore-v$Version-win-x64"))
$installerPath = [IO.Path]::GetFullPath((Join-Path $repoRoot "$OutputRoot\installers"))

New-Item -ItemType Directory -Force -Path $installerPath | Out-Null

& dotnet restore $projectPath --locked-mode --force-evaluate -r $Runtime
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }

& dotnet publish $projectPath -c $Configuration -r $Runtime --self-contained false --no-restore -o $publishPath
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

$packages = @(
    @{
        Name = "per-user"
        Scope = "perUser"
        RootDirectory = "LocalAppDataFolder"
        UpgradeCode = "{A5D176E0-13C4-4ED4-8E4B-2F0F75BC0F0F}"
        ComponentGuid = "{2F5BBD35-41AA-4A67-9AE2-CA4B8B37859A}"
    },
    @{
        Name = "per-machine"
        Scope = "perMachine"
        RootDirectory = "ProgramFiles64Folder"
        UpgradeCode = "{B7C2ED51-9F71-4E9D-AEA4-1F965E9C6E3C}"
        ComponentGuid = "{DB55B8BF-EA0B-49FC-9D57-A8C0D9A0F2BC}"
    }
)

foreach ($package in $packages) {
    $outputPath = Join-Path $installerPath "LocalDesktopStore-v$Version-$($package.Name)-x64.msi"
    & wix build -arch x64 `
        -d "PublishDir=$publishPath" `
        -d "ProductVersion=$Version" `
        -d "PackageScope=$($package.Scope)" `
        -d "RootDirectory=$($package.RootDirectory)" `
        -d "UpgradeCode=$($package.UpgradeCode)" `
        -d "ComponentGuid=$($package.ComponentGuid)" `
        $sourcePath `
        -out $outputPath
    if ($LASTEXITCODE -ne 0) { throw "WiX failed for $($package.Name) with exit code $LASTEXITCODE." }

    $validationArgs = if ($package.Scope -eq "perUser") {
        @("msi", "validate", "-sice", "ICE91", $outputPath)
    } else {
        @("msi", "validate", $outputPath)
    }
    & wix @validationArgs
    if ($LASTEXITCODE -ne 0) { throw "WiX ICE validation failed for $($package.Name) with exit code $LASTEXITCODE." }
}

Get-ChildItem -LiteralPath $installerPath -Filter *.msi -File
