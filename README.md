<p align="center">
  <strong>English</strong> | <a href="README.zh-CN.md">简体中文</a>
</p>

# EspansoGo

<p align="center">
  <a href="https://github.com/mofanx/EspansoGo/issues">Report Bug</a>
  ·
  <a href="https://github.com/mofanx/EspansoGo/issues">Request Feature</a>
  ·
  <a href="https://github.com/mofanx/EspansoGo/releases">Releases</a>
  ·
  <a href="https://espanso.org/docs/get-started/">Espanso docs</a>
</p>

A configuration-based text expansion app for Android, compatible with [espanso](https://espanso.org) config files. Define triggers and replacements in YAML, and let EspansoGo do the typing for you.

> This project is inspired by and rewrites [Expandroid](https://github.com/lochidev/Expandroid) by [lochidev](https://github.com/lochidev), reimplemented with .NET MAUI + Blazor Hybrid. Licensed under the same open source license.

<p align="center">
  <a href="https://github.com/mofanx/EspansoGo/releases/latest"><img src="https://raw.githubusercontent.com/andOTP/andOTP/master/assets/badges/get-it-on-github.png" height="80"></a>
</p>

## Features

- **Free and open source** — forever, no ads, no tracking
- **Unlimited expansions** — no caps on matches or triggers
- **Espanso compatible** — reuse your existing YAML config files
- **Variable types** — `echo`, `date`, `random`, `clipboard`, `choice`, `form`, `shell`, `script`, `http`, `javascript`, `match`
- **Forms support** — multi-line, choice & list forms
- **Cross-device sync** — WebDAV & Git sync to keep configs in sync with desktop espanso
- **No internet permission** — your data stays on your device
- **Multi-language UI** — English & Chinese, with more to come

## Getting Started

1. Download the latest APK from [Releases](https://github.com/mofanx/EspansoGo/releases/latest)
2. Install and grant Accessibility Service permission
3. Import your espanso YAML config or create new matches in the editor
4. Type a trigger anywhere and watch it expand!

> **Which APK should I download?**
> - `arm64-v8a` — Recommended, covers 95% of modern devices
> - `armeabi-v7a` — For older 32-bit devices
> - `x86_64` — For emulators / Chromebooks
> - `fat` — Universal, works on all devices (larger file)

## Supported Date Formats

The following chrono date time formats are supported:

```
%Y, %m, %b, %B, %h, %d, %e, %a, %A, %j, %w, %u, %D, %F, %H, %I, %p, %M, %S, %R, %T, %r
```

You can also use C# `DateTime.ToString()` format strings for further customization.

## Notes

- Espanso YAML files may need a few tries to parse correctly. Ensure compliance with YAML specs.
- Clipboard extension does not work on Android 10+ due to Google's security restrictions. A workaround is available since v1.0.
- Forms support includes multi-line, choice & list. "Using Forms with Script and Shell extensions" from the espanso docs is not supported.
- Not tested on Android versions below 12.

## Build

This is a .NET MAUI + Blazor Hybrid project using MudBlazor.

```bash
# Install MAUI workload
dotnet workload install maui-android

# Restore dependencies
dotnet restore src/EspansoGo.csproj

# Build Debug
dotnet build src/EspansoGo.csproj -c Debug -f net9.0-android35.0

# Build Release (requires keystore env vars)
dotnet build src/EspansoGo.csproj -c Release -f net9.0-android35.0
```

For detailed CLI publishing instructions, see [Microsoft's MAUI deployment guide](https://learn.microsoft.com/en-us/dotnet/maui/android/deployment/publish-cli).

### CI/CD

- **Push to main** → automatic build, artifacts available for download
- **Tag `v*`** → automatic GitHub Release with signed APKs (per-architecture)

## Tech Stack

- [.NET 9 MAUI](https://learn.microsoft.com/dotnet/maui/) — Cross-platform framework
- [Blazor Hybrid](https://learn.microsoft.com/aspnet/core/blazor/hybrid/) — UI rendering
- [MudBlazor](https://mudblazor.com/) — Material Design component library
- [YamlDotNet](https://github.com/aaubry/YamlDotNet) — YAML parsing

## License

Apache License 2.0. See [LICENSE](LICENSE) for details.
