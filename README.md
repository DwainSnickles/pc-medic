# PC Medic — portable Windows system optimizer and PC cleaner

[![Build status](https://github.com/DwainSnickles/pc-medic/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/DwainSnickles/pc-medic/actions/workflows/build.yml) [![Download the portable build](https://img.shields.io/badge/download-portable%20zip-2ea043?logo=windows&logoColor=white)](https://github.com/DwainSnickles/pc-medic/releases/latest)

**PC Medic** is a portable Windows 10/11 maintenance suite for people who want the jobs
"Advanced SystemCare", CCleaner, Glary Utilities or Ashampoo WinOptimizer do — without a
background service, an installer, telemetry or a subscription. One folder, one executable,
every destructive action reversible through the Recycle Bin or a backup file.

Companion project: **[Disk Defrag](https://github.com/DwainSnickles/disk-defrag)** — a
separate cluster-map defragmentation tool that lives beside PC Medic and launches from its
Maintenance page.

![PC Medic overview: live CPU, memory, system drive, update and uptime tiles with one-click actions, machine profile and activity feed](docs/pc-medic-overview.png)

![PC Medic Registry Audit mid-scan: a centred LOADING 55% plate with a filling bar covers the Shortcuts card while IShellLinkW resolves ~25,000 links, and the WORKING strip runs along the bottom of the shell](docs/pc-medic.png)

## What it does

| Page | Purpose |
| --- | --- |
| **Overview** | Live CPU / memory / disk / uptime / update telemetry, GPU and machine profile, plus one-click **REDUCE CPU**, **FREE MEMORY**, **CLEAN DRIVE** and **OPEN UPDATES** actions. |
| **Startup Apps** | Every Run-key, Startup-folder and scheduled-task autostart entry, with reversible disabling. |
| **Boot Speed** | MenuShowDelay and boot-menu tuning with a JSON backup of the previous values. |
| **Optimize Sleep** | Sleep/hibernate and power-plan tuning through `powercfg`. |
| **Maintenance** | Fifteen recovery and cleanup tools (SFC, DISM, restore point, Storage Sense, Memory Diagnostic, Event Viewer, **Disk Defrag**) launched from one grid. |
| **Tweaks** | Explorer and visual-effects tweaks, each written with a backup so it can be reverted. |
| **Programs** | Installed applications from both registry hives, with uninstall and repair routing. |
| **Updates** | `winget` application upgrades, a per-app **UNINSTALL** for software you no longer want, and Windows Update driver scans with a shared badge count. |
| **Registry Audit** | One page, two audits: dead `App Paths`, orphaned uninstall keys and stale file associations on the left, ~25,000 shortcuts resolved with `IShellLinkW` on the right. Removable per item, with a `.reg` backup and a persistent "not dead" list. |
| **Benchmarks** | Quick CPU and disk sanity runs. |
| **Command Shell** | An embedded console for the tools the UI does not wrap. |

## Why it is different

- **Portable and honest** — `asInvoker`, no service, no drivers, no scheduled tasks of its own.
  Elevation is opt-in per action and the UI always shows whether you are a standard user.
- **Reversible** — deleted files go to the Recycle Bin, registry edits write a backup first,
  and the shortcut keep-list and CPU-ceiling backup persist in `%LOCALAPPDATA%\PCMedic`.
- **No fake numbers** — every figure comes from the OS: `GlobalMemoryStatusEx`,
  `FSCTL_GET_VOLUME_BITMAP`, `powercfg`, `winget`, performance counters.

## Loading and progress

Long operations never leave the page blank. They cover their results area with a centred
**LOADING ##%** plate: the shortcut audit, the registry audit and both update scans use it, as do
temp cleanup, the memory trim and the self-update check.

Where the work reports progress, that number is real — the audits know how many links or keys they
still have to resolve and count up by that share. Where the work is one opaque call (`winget`, the
Windows Update agent, `powercfg`, a temp-file sweep), nothing can be measured, so the bar eases
toward a ceiling and resolves only when the call returns. It never reaches 100% before the work is
done, and the plate hides itself again the moment the results are ready.

Every wait also raises a `WORKING` strip along the bottom of the shell, with a segment sweeping its
track, so the window is never frozen and silent even when only one card is busy. Downloads do not
need either: `winget` streams its transfer, so each update row and each benchmark rig shows its own
bar, filled from the real percentage or sweeping when winget reports none.

## Requirements

- Windows 10 or 11 (x64)
- .NET 10 desktop runtime, or build from source with the .NET 10 SDK
- Administrator rights only for the actions that genuinely need them

## Build

```sh
dotnet build "PC Medic.csproj"
```

The portable layout lands in `bin/Debug/net10.0-windows/`. Build the companion tool from its
own repository and drop `DiskDefrag.exe` beside `PC Medic.exe`; the Maintenance tile picks it
up automatically and falls back to the Windows optimiser when it is absent.

## SEO / search terms

This project is the practical answer to searches such as:

`Advanced SystemCare alternative` · `IObit Advanced SystemCare free alternative` ·
`CCleaner alternative portable` · `Glary Utilities alternative` ·
`Ashampoo WinOptimizer alternative` · `Wise Care 365 alternative` ·
`free PC cleaner Windows 11` · `portable system optimizer` · `startup program manager` ·
`registry cleaner open source` · `disk cleanup tool` · `Windows maintenance utility`

## Repository topics

`windows` `system-optimizer` `pc-cleaner` `pc-optimizer` `maintenance` `registry-cleaner`
`startup-manager` `disk-cleanup` `disk-defrag` `winforms` `dotnet` `windows-11` `portable`
`advanced-systemcare-alternative` `ccleaner-alternative` `freebuff`

---

Built with **[Freebuff](https://freebuff.com)** — the AI coding agent that builds and
maintains this suite.
