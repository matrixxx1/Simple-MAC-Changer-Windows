param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$repoRoot = Resolve-Path $ProjectRoot
$assetsRoot = Join-Path $repoRoot 'SimpleMacChanger\Assets'
$storeAssets = Join-Path $repoRoot 'Store-Assets'
$screenshotRoot = Join-Path $repoRoot 'screenshots'

New-Item -ItemType Directory -Force -Path $assetsRoot, $storeAssets, $screenshotRoot | Out-Null

function New-BrandBitmap {
    param([int]$Width, [int]$Height, [string]$Label, [string]$SubLabel)

    $bmp = New-Object System.Drawing.Bitmap $Width, $Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

    $rect = New-Object System.Drawing.RectangleF([float]0, [float]0, [float]$Width, [float]$Height)
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect,
        [System.Drawing.Color]::FromArgb(255, 14, 116, 110),
        [System.Drawing.Color]::FromArgb(255, 8, 51, 76),
        45)
    $g.FillRectangle($bg, 0, 0, $Width, $Height)

    $cardRect = New-Object System.Drawing.RectangleF
    $cardRect.X = [float](0.08 * $Width)
    $cardRect.Y = [float](0.12 * $Height)
    $cardRect.Width = [float](0.84 * $Width)
    $cardRect.Height = [float](0.76 * $Height)
    $cardBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(30, 255, 255, 255))
    [void]$g.FillRectangle($cardBrush, $cardRect)

    $titleFont = New-Object System.Drawing.Font('Segoe UI Semibold', [Math]::Max(14, $Width/28), [System.Drawing.FontStyle]::Bold)
    $subFont = New-Object System.Drawing.Font('Segoe UI', [Math]::Max(9, $Width/42), [System.Drawing.FontStyle]::Regular)
    $labelBrush = [System.Drawing.Brushes]::White
    $dimBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(220, 255, 255, 255))

    $titleSize = $g.MeasureString($Label, $titleFont)
    $subSize = $g.MeasureString($SubLabel, $subFont)
    [void]$g.DrawString($Label, $titleFont, $labelBrush, [float](($Width - $titleSize.Width) / 2), [float](($Height - $titleSize.Height - $subSize.Height) / 2 - 10))
    [void]$g.DrawString($SubLabel, $subFont, $dimBrush, [float](($Width - $subSize.Width) / 2), [float](($Height - $subSize.Height) / 2 + 20))

    $g.Dispose()
    $bmp
}

function Write-Asset {
    param([int]$Width, [int]$Height, [string]$Path, [string]$Label, [string]$SubLabel, [switch]$AsIco)

    $bmp = New-BrandBitmap -Width $Width -Height $Height -Label $Label -SubLabel $SubLabel
    try {
        if ($AsIco) {
            $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Icon)
        } else {
            $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
        }
    }
    finally {
        $bmp.Dispose()
    }
}

Write-Asset -Width 512 -Height 512 -Path (Join-Path $assetsRoot 'appicon.png') -Label 'Simple MAC Changer' -SubLabel 'Network MAC Utility'
Write-Asset -Width 50 -Height 50 -Path (Join-Path $storeAssets 'StoreLogo.png') -Label 'SMC' -SubLabel '$1.99'
Write-Asset -Width 150 -Height 150 -Path (Join-Path $storeAssets 'Square150x150Logo.png') -Label 'Simple MAC Changer' -SubLabel 'Network Utility'
Write-Asset -Width 44 -Height 44 -Path (Join-Path $storeAssets 'Square44x44Logo.png') -Label 'MAC' -SubLabel '$1.99'
Write-Asset -Width 310 -Height 150 -Path (Join-Path $storeAssets 'Wide310x150Logo.png') -Label 'Simple MAC Changer' -SubLabel 'MAC randomizer and planner'
Write-Asset -Width 310 -Height 310 -Path (Join-Path $storeAssets 'Square310x310Logo.png') -Label 'Simple MAC Changer' -SubLabel 'MAC Utility'

Write-Asset -Width 1366 -Height 768 -Path (Join-Path $screenshotRoot '01-main-workflow.png') -Label 'Main Workflow' -SubLabel 'List adapters, generate, validate, and build plans'
Write-Asset -Width 1366 -Height 768 -Path (Join-Path $screenshotRoot '02-licensing-usage.png') -Label 'Store Licensing' -SubLabel '15-day trial + 15 min/day after trial'
Write-Asset -Width 1366 -Height 768 -Path (Join-Path $screenshotRoot '03-logging-help.png') -Label 'Logging and About' -SubLabel 'View logs, restore scripts, and app details'

$listing = '# Store Listing Copy`r`n`r`n## Short name`r`nSimple MAC Changer`r`n`r`n## Price`r`n$1.99 USD`r`n`r`n## Short description`r`nReview adapters and generate reversible MAC change plans with a 15-day full trial.`r`n`r`n## Full description`r`n### Dependencies`r`n- Microsoft Windows desktop (Windows 10 2004 / version 19041.0+ or later, x64)`r`n- .NET 8 runtime is packaged via self-contained publish (no separate .NET install required)`r`n- Windows Store license API for trial/purchase state and entitlement detection (`Windows.Services.Store`)`r`n- Elevation in PowerShell may be required to apply MAC change scripts immediately`r`n`r`nSimple MAC Changer is a practical Windows utility for local network adapter review and MAC address preparation workflows.`r`n`r`n## Licensing behavior`r`n- 15-day fully functional trial.`r`n- After trial window, features are limited to 15 minutes per day.`r`n- Store API checks determine paid/trial status in the app and updates the about/license panel.`r`n- Store identity: m3Coding.SimpleMACChanger (Publisher: CN=AFF85DD5-3D92-42A5-BA39-3AF6D41B1837).`r`n- Package Family Name (PFN): m3Coding.SimpleMACChanger_8srffngrg4x08.`r`n- Store ID: 9NZD3XQ9HCPQ.`r`n`r`n## Features`r`n- Network adapter review (name, status, interface ID, MAC)`r`n- Generate and validate MAC addresses`r`n- Build reversible PowerShell change plans`r`n- Restore command generation`r`n- Export plan to file`r`n- Activity logs and local usage audit trail'
Set-Content -Path (Join-Path $storeAssets 'StoreListing.md') -Value $listing -Encoding utf8
