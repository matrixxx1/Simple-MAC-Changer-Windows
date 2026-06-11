param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = ".\artifacts",
    [string]$Version = "1.0.0.0"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "SimpleMacChanger\SimpleMacChanger.csproj"
$publishProfile = Join-Path $repoRoot "SimpleMacChanger\Properties\PublishProfiles\WinX64Folder.pubxml"
$manifestPath = Join-Path $repoRoot "SimpleMacChanger\Package.appxmanifest"
$storeAssets = Join-Path $repoRoot "Store-Assets"
$htmlPath = Join-Path $storeAssets "StoreSubmission.html"
$storeAssetsInPackage = "Assets"

$artifactsRoot = Resolve-Path (Join-Path $repoRoot $OutputRoot)
$publishDir = Join-Path $artifactsRoot ("publish\\$Runtime")
$msixRoot = Join-Path $artifactsRoot "msix"
$msixPackage = Join-Path $msixRoot "m3Coding.SimpleMACChanger.msix"
$msixUploadPath = Join-Path $msixRoot "m3Coding.SimpleMACChanger-8srffngrg4x08.msixupload"
$packageLayout = Join-Path $msixRoot "package-layout"

$makeAppxCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin\x64\makeappx.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin\10.0.19041.0\x64\makeappx.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin\10.0.22000.0\x64\makeappx.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin\10.0.22621.0\x64\makeappx.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin\10.0.26100.0\x64\makeappx.exe")
)

$makeAppx = $null
foreach ($candidate in $makeAppxCandidates) {
    if (Test-Path $candidate) { $makeAppx = $candidate; break }
}
if (-not $makeAppx) {
    throw "MakeAppx.exe was not found under Windows Kits. Install Windows SDK and rerun."
}

$dotnetArgs = @(
    "publish",
    $project,
    "-c", $Configuration,
    "/p:PublishProfile=$publishProfile",
    "/p:PublishDir=$publishDir",
    "/p:PublishSingleFile=true",
    "/p:SelfContained=true",
    "/p:RuntimeIdentifier=$Runtime"
)

& dotnet @dotnetArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

if (Test-Path $packageLayout) { Remove-Item -Recurse -Force $packageLayout }
New-Item -ItemType Directory -Force -Path $msixRoot | Out-Null
New-Item -ItemType Directory -Force -Path $packageLayout | Out-Null

Copy-Item -Path (Join-Path $publishDir "*") -Destination $packageLayout -Recurse -Force

Copy-Item -Path $manifestPath -Destination (Join-Path $packageLayout "AppxManifest.xml") -Force

New-Item -ItemType Directory -Path (Join-Path $packageLayout $storeAssetsInPackage) -Force | Out-Null
Copy-Item -Path (Join-Path $storeAssets "StoreLogo.png") -Destination (Join-Path $packageLayout ($storeAssetsInPackage + "\\StoreLogo.png")) -Force
Copy-Item -Path (Join-Path $storeAssets "Square44x44Logo.png") -Destination (Join-Path $packageLayout ($storeAssetsInPackage + "\\Square44x44Logo.png")) -Force
Copy-Item -Path (Join-Path $storeAssets "Square150x150Logo.png") -Destination (Join-Path $packageLayout ($storeAssetsInPackage + "\\Square150x150Logo.png")) -Force
Copy-Item -Path (Join-Path $storeAssets "Wide310x150Logo.png") -Destination (Join-Path $packageLayout ($storeAssetsInPackage + "\\Wide310x150Logo.png")) -Force
Copy-Item -Path (Join-Path $storeAssets "Square310x310Logo.png") -Destination (Join-Path $packageLayout ($storeAssetsInPackage + "\\Square310x310Logo.png")) -Force

if (Test-Path $msixPackage) { Remove-Item -Force $msixPackage }
& $makeAppx pack /d $packageLayout /p $msixPackage /o
if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed with exit code $LASTEXITCODE" }

if (Test-Path $msixUploadPath) { Remove-Item -Force $msixUploadPath }
$msixUploadZipPath = [System.IO.Path]::ChangeExtension($msixUploadPath, ".zip")
if (Test-Path $msixUploadZipPath) { Remove-Item -Force $msixUploadZipPath }
Compress-Archive -Path $msixPackage -DestinationPath $msixUploadZipPath -Force -CompressionLevel Optimal
Move-Item -Force -Path $msixUploadZipPath -Destination $msixUploadPath

if (-not (Test-Path $msixUploadPath)) { throw "msixupload package was not created." }
$msixUploadPath = (Resolve-Path $msixUploadPath).Path

$logLine = @"
<h2>Upload artifact</h2>
<p><strong>Copy/paste full path:</strong> <code>$msixUploadPath</code></p>
<p><strong>For quick clipboard use:</strong> <code>Set-Clipboard '$msixUploadPath'</code></p>
"@

$htmlContent = Get-Content -Raw -Path $htmlPath
$marker = "<h2>Packaging outputs</h2>"
if ($htmlContent.Contains($marker)) {
    if ($htmlContent.Contains("<h2>Upload artifact</h2>")) {
        $htmlContent = [regex]::Replace($htmlContent, "<h2>Upload artifact</h2>.*?<\\\/body>", "$logLine`r`n</body>", [System.Text.RegularExpressions.RegexOptions]::Singleline)
    } else {
        $htmlContent = $htmlContent -replace [regex]::Escape("</body>"), "$logLine`r`n</body>"
    }
} else {
    $htmlContent = $htmlContent + "`r`n" + $logLine
}

Set-Content -Path $htmlPath -Value $htmlContent -Encoding utf8

Start-Process $htmlPath

Write-Host "MSIX upload artifact: $msixUploadPath"
Write-Host "Store submission handoff: $htmlPath"
