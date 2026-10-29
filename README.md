# Tempest

A small Windows utility for Spicetify. Windows Fluent controls, a dark blue colour scheme, and a slime app icon. No background artwork, account, or telemetry.

## Run

Download **[Tempest.exe](https://github.com/SxGoldenxS/Tempest/releases/latest/download/Tempest.exe)** and double-click it. No ZIP extraction, source files, setup scripts, or separate .NET installation are required. Run as a normal user, not as administrator. This is a portable Windows x64 application; the executable includes .NET and extracts native runtime components automatically on first launch.

To try the interface without touching Spotify:

```powershell
.\Tempest.exe --demo
```

Demo mode uses disposable sample files under the system temporary folder. It never launches or modifies your real Spotify installation. Activity includes a switch to simulate failure and exercise recovery.

Tempest checks the latest public GitHub release when launched. If a newer version is available, a banner shows both versions and opens the release page with **Get update**. Replace your portable executable with the new download. Offline or failed checks do not block the app; you can also open the latest release from **Install & update**. Demo mode skips the network check.

## Tools

- **Overview:** installation status, install/update, reapply, recovery.
- **Install & update:** Spicetify and Marketplace releases, the signed Spotify installer, and a separate auto-apply shortcut.
- **Repair:** reapply, standard desktop location, customisation reset, PATH, developer tools, update controls.
- **Recovery:** saved checkpoints, restore vanilla Spotify, remove the CLI while retaining configuration, and Spotify version selection.
- **Activity:** local operation output and a manually exported redacted log.

Operations show a review dialog. File-changing operations save recovery checkpoints and attempt rollback on failure or cancellation. Downloaded Spicetify/Marketplace archives must match the SHA-256 metadata supplied by their GitHub releases. Spotify installers must have a valid Spotify AB signature. Archive paths are checked before extraction.

## Scope and limits

This version manages the standard per-user Windows desktop Spotify installation. Custom paths, Store installations, macOS, and Linux are not managed. Sign in to desktop Spotify and leave it open for at least 60 seconds before using Spicetify.

- Spotify's external installer is verified before launch, but has its own UI and is not covered by Tempest's file rollback or cancellation once launched. The Store version is not removed.
- Checkpoints can use significant disk space, especially before a Spotify version change. They are retained locally until you remove them manually.
- A checkpoint is a copy of files, not a system restore point. It cannot reverse upstream service changes or guarantee recovery after power loss, disk failure, or permission changes.
- Update blocking uses the installed CLI's `spotify-updates` command. Legacy ACL blocks from other installers may need separate cleanup.
- Removal keeps configuration and recovery files. Auto-apply shortcuts should be removed or replaced after removing the CLI.
- Downgrades use LoaderSpot's catalogue, permit only the supported HTTPS hosts, verify the installer and extracted executable signatures, and verify the selected version. Some catalogue mirrors are intentionally unsupported. Compatibility is not guaranteed.
- Local installation detection is a filesystem heuristic, not a Spotify health check.

## Build and test

Requires Windows and the .NET 10 SDK. No external NuGet packages are required.

```powershell
dotnet run --project tests/Tempest.Tests -c Release
dotnet run --project src/Tempest -- --demo
powershell -NoProfile -File build.ps1
# Optional native ARM64 package:
powershell -NoProfile -File build.ps1 -Runtime win-arm64
```

The test executable covers INI edits, unsafe archives, checkpoint validation, rollback, cancellation, failed uninstallation, subprocess exit codes, and recovery undo checkpoints. Tests use isolated fixtures; they do not install or modify Spotify. Real install/downgrade workflows have not been validated end-to-end against a live Spotify installation.

The icon is an original geometric slime mark; its editable SVG and ICO are included. `scripts/make_icon.py` regenerates it with Pillow (optional; not required for builds).

## Privacy

No telemetry or automated uploads. Local logs replace the Windows profile path and username; review any manually exported log before sharing, because third-party command output may contain other identifiers. Recovery files can contain personal Spotify data and must not be committed or uploaded. Source packages exclude logs, checkpoints, test fixtures, developer paths, and personal artwork. Release symbols are disabled and compilation paths are mapped to a generic path.

Independent community utility; not affiliated with Spotify, Spicetify, or the creators of Rimuru Tempest.

## 0.3 maintenance update

Recovery listing skips malformed or incomplete checkpoints, and restore rejects duplicate or overlapping destinations before changing files. ZIP extraction rejects Windows alternate data streams and existing linked paths. Spotify version selection shows only supported download hosts and explains when none are available. App and release versions now match.
