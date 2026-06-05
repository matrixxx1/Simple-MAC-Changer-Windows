# Simple MAC Changer for Windows

Review network adapters and prepare MAC address changes.

This is the initial Microsoft Store-oriented Windows desktop app scaffold for $(System.Collections.Hashtable.Title). It uses .NET 8 and WPF, keeps the first implementation local-first, and includes a repo-root Store-Assets folder for listing and privacy handoff material.

## Initial scope

- Adapter review surface
- MAC generation workflow
- Restore-original reminder
- Admin-action handoff notes

## Build

``powershell
dotnet build .\SimpleMacChanger\SimpleMacChanger.csproj -c Release
``

## Store notes

Before final packaging, reserve the exact Microsoft Store product name in Partner Center and update package identity values to match that reservation.