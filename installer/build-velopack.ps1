[CmdletBinding()]
param(
    [string] $Version = "0.3.0",
    [string] $Configuration = "Release",
    [string] $Runtime = "win-x64",
    [string] $OutputRoot = "publish\velopack"
)

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$projectPath = Join-Path $repoRoot "src\LocalDesktopStore\LocalDesktopStore.csproj"
$publishPath = [IO.Path]::GetFullPath((Join-Path $repoRoot "publish\LocalDesktopStore-v$Version-win-x64"))
$outputPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot))
$velopackVersion = "1.2.0"
$packId = "LocalDesktopStore"

Write-Host "Restoring locked release graph..."
& dotnet restore $projectPath --locked-mode --force-evaluate -r $Runtime
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }

Write-Host "Publishing framework-dependent $Runtime application..."
& dotnet publish $projectPath -c $Configuration -r $Runtime --self-contained false --no-restore -o $publishPath
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$toolSpec = "vpk@$velopackVersion"
Write-Host "Packing unsigned Velopack release with $toolSpec..."
& dnx $toolSpec -- --yes pack `
    --packId $packId `
    --packVersion $Version `
    --packDir $publishPath `
    --outputDir $outputPath `
    --mainExe "LocalDesktopStore.exe" `
    --packTitle "LocalDesktopStore" `
    --packAuthors "SysAdminDoc" `
    --channel "win" `
    --runtime $Runtime `
    --delta none `
    --shortcuts "StartMenuRoot"
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed with exit code $LASTEXITCODE." }

$required = @(
    "$packId-$Version-full.nupkg",
    "$packId-win-Setup.exe",
    "$packId-win-Portable.zip",
    "releases.win.json",
    "assets.win.json"
)
foreach ($name in $required) {
    $path = Join-Path $outputPath $name
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing Velopack output: $name" }
}

$setupPath = Join-Path $outputPath "$packId-win-Setup.exe"
$signature = Get-AuthenticodeSignature -LiteralPath $setupPath
if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::NotSigned) {
    throw "Unexpected Authenticode signature status '$($signature.Status)' on unsigned Velopack Setup.exe."
}

Write-Host "Velopack release ready: $outputPath"
