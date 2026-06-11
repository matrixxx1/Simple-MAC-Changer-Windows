param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = ".\artifacts"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

$project = Join-Path $repoRoot "SimpleMacChanger\SimpleMacChanger.csproj"
$publishProfile = Join-Path $repoRoot "SimpleMacChanger\Properties\PublishProfiles\WinX64Folder.pubxml"
$publishDir = Join-Path -Path (Join-Path -Path $repoRoot -ChildPath $OutputRoot) -ChildPath ("publish\\$Runtime")
$packageDir = Join-Path -Path (Join-Path -Path $repoRoot -ChildPath $OutputRoot) -ChildPath "package"
$archive = Join-Path -Path $packageDir -ChildPath "m3Coding.SimpleMACChanger-Windows-$Runtime.zip"

if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
New-Item -ItemType Directory -Force -Path $packageDir | Out-Null

$dotnetArgs = @(
    "publish",
    $project,
    "-c", $Configuration,
    "/p:PublishProfile=$publishProfile",
    "/p:PublishDir=$publishDir"
)

& dotnet @dotnetArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

if (Test-Path $archive) { Remove-Item -Force $archive }
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $archive -CompressionLevel Fastest

Write-Host "Published to: $publishDir"
Write-Host "Package: $archive"
