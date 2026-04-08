# TSD RA4M1 Firmware Uploader

Portable Windows WPF app for flashing RA4M1 firmware via `dfu-util`.

## MVP Features

- Shows available COM ports
- Shows DFU-like USB devices
- Auto-refreshes device lists
- Lets users select `.bin` or `.elf` firmware file
- Runs upload with live status console and progress parsing
- Supports cancel during upload

## Build Prerequisites (developer machine)

- Windows 10/11
- .NET 8 SDK

## Build and Run

```powershell
dotnet restore .\TSDApp.sln
dotnet run --project .\src\TsdFlasher.App\TsdFlasher.App.csproj
```

## Portable Publish

```powershell
dotnet publish .\src\TsdFlasher.App\TsdFlasher.App.csproj -c Release -r win-x64 --self-contained true
```

The output folder is your portable distribution. Keep `tools\dfu-util\` with the app executable.

Before sending to customers:

- Include `docs\customer-troubleshooting.md`
- Include `legal\THIRD-PARTY-NOTICES.txt`
- Include `legal\SOURCE-OFFER.txt` (replace placeholder support email first)

## Important Note

True zero-touch customer flashing on Windows depends on device-side USB descriptor support for automatic WinUSB binding in DFU mode. See docs:

- `docs/factory-verification.md`
- `docs/customer-troubleshooting.md`
- `docs/portable-release.md`