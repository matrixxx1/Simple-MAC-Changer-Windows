# Simple MAC Changer for Windows

Review network adapters and prepare MAC address changes.

This is a practical Windows desktop app scaffold for $(System.Collections.Hashtable.Title). It uses .NET 8 and WPF, keeps adapter review and reversible change-planning workflows in-app, and now includes a packaging script for Windows distribution.

## Initial scope

- Adapter review surface
- MAC generation and validation
- Reversible change planning
- Actionable admin handoff script output

## Build

```powershell
dotnet build .\SimpleMacChanger\SimpleMacChanger.csproj -c Release
```

## Package for Windows

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-package.ps1
```

This produces:
- `artifacts\publish\win-x64\` (published WPF executable)
- `artifacts\package\SimpleMacChanger-Windows-win-x64.zip` (distribution zip)

## Store notes

Before final packaging, reserve the exact Microsoft Store product name in Partner Center and update package identity values to match that reservation.
