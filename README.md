# Simple MAC Changer for Windows

Review network adapters and prepare MAC address changes.

This is a practical WPF utility that helps you review adapters, generate/review reversible MAC change plans, and track local activity while enforcing the configured trial model.

## Features

- Adapter review surface
- MAC generation and validation
- Reversible change planning
- Restore-plan output
- Persistent usage and action logging
- Microsoft Store-aware licensing check (`Windows.Services.Store`)
- 15-day fully functional trial with post-trial daily cap behavior

## Trial and licensing model

- Price: **$1.99 USD**
- Trial: **15-day fully functional period**
- After trial: **15 minutes/day** in app actions until purchase
- Trial/purchase state shown in the in-app licensing panel and About dialog.
- In development/unpackaged builds, state uses a local 15-day fallback trial and local usage metering.

## Build

```powershell
dotnet build .\SimpleMacChanger\SimpleMacChanger.csproj -c Release
```

## Package for Windows

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-package.ps1
```

This produces:
- `artifacts\publish\win-x64\`
- `artifacts\package\m3Coding.SimpleMACChanger-Windows-win-x64.zip`

## Store prep

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\prepare-store-assets.ps1
```

Outputs produced:
- App icon and store icons in `SimpleMacChanger\Assets\appicon.ico`
- Store listing and legal copy in `Store-Assets\`
- Screenshot images in `screenshots\` (`01-main-workflow.png`, `02-licensing-usage.png`, `03-logging-help.png`)

## Prepare Store upload package

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\prepare-msixupload.ps1
```

This now:
- builds a self-contained publish
- creates `artifacts\msix\m3Coding.SimpleMACChanger.msix`
- creates `artifacts\msix\m3Coding.SimpleMACChanger-8srffngrg4x08.msixupload`
- opens `Store-Assets\\StoreSubmission.html`
- writes the full `.msixupload` path into the HTML for copy/paste.

You can run this as the single deploy-prep command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\prepare-for-deploy.ps1
```

## Store submission notes

Before final store upload, reserve the exact Microsoft Store product name and complete these checklist items:
- confirm app reserved name and identity match your Store account entry:
  - Package/Identity/Name: `m3Coding.SimpleMACChanger`
  - Package/Identity/Publisher: `CN=AFF85DD5-3D92-42A5-BA39-3AF6D41B1837`
  - Package/Properties/PublisherDisplayName: `m3 Coding`
  - Package Family Name: `m3Coding.SimpleMACChanger_8srffngrg4x08`
  - Store ID: `9NZD3XQ9HCPQ`
- ensure package identity/version and Store listing price ($1.99 USD) match the reserved app
- include Store icons: `StoreLogo.png`, `Square44x44Logo.png`, `Square150x150Logo.png`, `Square310x310Logo.png`, `Wide310x150Logo.png`
- upload screenshots and listing text from `Store-Assets\StoreListing.md`
- include Privacy Policy in `Store-Assets\PrivacyPolicy.txt`
