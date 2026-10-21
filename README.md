<div align="center">
<img src="docs/icon.png" width="96" alt="">

# ChangeLedger

**Skeleton for small Windows tray apps: one exe, RU/EN, light and dark, releases from CI.**

**English** · [Русский](README.ru.md)

[![Release](https://img.shields.io/github/v/release/lojekq/ChangeLedger?sort=semver)](https://github.com/lojekq/ChangeLedger/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/lojekq/ChangeLedger/total)](https://github.com/lojekq/ChangeLedger/releases)
[![CI](https://github.com/lojekq/ChangeLedger/actions/workflows/ci.yml/badge.svg)](https://github.com/lojekq/ChangeLedger/actions/workflows/ci.yml)
![Windows 11](https://img.shields.io/badge/Windows-11-0078D4)
[![License](https://img.shields.io/github/license/lojekq/ChangeLedger)](LICENSE)

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/main-dark-en.png">
  <img src="docs/main-light-en.png" width="820" alt="ChangeLedger main window">
</picture>
</div>


## Why

Every small Windows utility needs the same boring parts: a tray icon, settings, autostart, two languages, a crash log and a release with checksums.
ChangeLedger has them done once, so an app only adds its own pages.

## Features

- **Tray first** — left click opens the window, the cross hides it, Exit is in the tray menu.
- **One exe** — no installer and no admin rights; a Start menu shortcut and autostart are switches in Settings.
- **English and Russian, light and dark** — follows Windows or your choice.
- **Honest updates** — once a day it asks GitHub about a new release, shows a note and downloads nothing.
- **Crash log** — if it fails, it stops and shows where the log is; nothing is sent anywhere.

## See it in action

![ChangeLedger demo](docs/demo-en.gif)

## Screenshots

| | Light | Dark |
|---|---|---|
| Settings | ![Settings, light](docs/settings-light-en.png) | ![Settings, dark](docs/settings-dark-en.png) |
| About | ![About, light](docs/about-light-en.png) | ![About, dark](docs/about-dark-en.png) |

## Download

1. Get **`ChangeLedger-x.y.z-win-x64.exe`** from [Releases](https://github.com/lojekq/ChangeLedger/releases/latest). Single file, no install, no admin.
2. Smaller build: `ChangeLedger-x.y.z-win-x64-fd.zip` (needs [.NET Desktop Runtime 10](https://dotnet.microsoft.com/download/dotnet/10.0)).
3. Optional check: `gh attestation verify <file> -R lojekq/ChangeLedger` or compare with `SHA256SUMS.txt`.

Requirements: Windows 11 x64. Windows 10 22H2 should work but is not tested.

## First run

Put the exe into a permanent folder first, for example `%LOCALAPPDATA%\Programs\ChangeLedger\`: autostart and the shortcut point to it.
On the first start Windows may show a SmartScreen warning (see FAQ). The app then lives in the tray; turn on **Start with Windows** in Settings.

## Settings

| Setting | Default | What it does |
|---|---|---|
| Language | Windows language | English or Russian, applied after a restart |
| Theme | Windows theme | Light or dark |
| Start with Windows | off | Starts minimized to the tray |
| Start menu shortcut | on when the exe is in `%LOCALAPPDATA%\Programs\ChangeLedger\` | A shortcut in the Start menu |
| Check for updates | on | Once a day asks GitHub about a new release |

Settings and logs live in `%APPDATA%\ChangeLedger`.

## FAQ

<details><summary>Windows protected your PC / SmartScreen warning</summary>

The exe is not code-signed yet, so Windows does not know the publisher. Click **More info → Run anyway**.
Every file is built by GitHub Actions from this repository; you can check it with `gh attestation verify` or `SHA256SUMS.txt`.
Smart App Control, if it is on, blocks unsigned files with no way to allow one; the only option is to turn it off.
</details>

<details><summary>My antivirus flags it</summary>

It is a false positive: the app has no installer, no packer and no obfuscation. Please report it to your antivirus vendor
and open an issue with the detection name and the SHA256 of the file.
</details>

<details><summary>How do I uninstall?</summary>

About → **Remove data and autostart**, then delete the exe.
</details>

## Privacy

No telemetry. The only network request is the update check: `GET https://api.github.com/repos/lojekq/ChangeLedger/releases/latest` once a day; turn it off in Settings.
Logs contain no window titles, paths or other personal data.

## Build from source

```powershell
winget install Microsoft.DotNet.SDK.10
git clone https://github.com/lojekq/ChangeLedger
cd ChangeLedger
dotnet run --project src/ChangeLedger
dotnet test
```

## License

MIT © 2026 lojekq
