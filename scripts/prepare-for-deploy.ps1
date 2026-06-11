param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

& (Join-Path $PSScriptRoot "prepare-store-assets.ps1")
& (Join-Path $PSScriptRoot "prepare-msixupload.ps1") -Configuration $Configuration -Runtime $Runtime
