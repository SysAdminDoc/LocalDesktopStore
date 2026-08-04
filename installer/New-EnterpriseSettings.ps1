[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $OutputPath,
    [string] $GitHubUser = "SysAdminDoc",
    [string] $TopicFilter = "windows-app"
)

$ErrorActionPreference = "Stop"
$token = [Environment]::GetEnvironmentVariable("LDS_GITHUB_TOKEN", "Process")
if ([string]::IsNullOrWhiteSpace($token)) {
    throw "Set LDS_GITHUB_TOKEN in the deployment process; the token is intentionally not accepted as a command-line argument."
}

Add-Type -AssemblyName System.Security
$protectedBytes = [System.Security.Cryptography.ProtectedData]::Protect(
    [Text.Encoding]::UTF8.GetBytes($token),
    $null,
    [System.Security.Cryptography.DataProtectionScope]::LocalMachine)

$settings = [ordered]@{
    GitHubUser = $GitHubUser.Trim()
    GitHubTokenProtected = [Convert]::ToBase64String($protectedBytes)
    UseTopicFilter = $false
    TopicFilter = $TopicFilter.Trim()
    VerifyHashSidecar = $true
    UiLanguage = "en"
}

$parent = Split-Path -Parent ([IO.Path]::GetFullPath($OutputPath))
if (-not [string]::IsNullOrWhiteSpace($parent)) {
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
}
$settings | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
Write-Output "Wrote DPAPI machine-scope settings seed to $OutputPath"
