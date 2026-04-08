# Portable Release Checklist

## Publish Command

```powershell
dotnet publish .\src\IOMini2Tool.App\IOMini2Tool.App.csproj -c Release -r win-x64 --self-contained true
```

Expected root layout in `publish\`:
- `IO-Mini2-Tool.exe`
- `tools\`
- `legal\`
- `docs\`

## Package Content

- App executable and required runtime files
- `tools\dfu-util\dfu-util.exe`
- Any required `libusb` DLL files used by your dfu-util build
- `customer-troubleshooting.md`
- `legal\THIRD-PARTY-NOTICES.txt`
- `legal\SOURCE-OFFER.txt`

## One-Time Release Setup

- Edit `legal\SOURCE-OFFER.txt` and replace `[REPLACE_WITH_YOUR_SUPPORT_EMAIL]` with your real support/compliance email before distributing.

## QA Before Shipment

- Run from an arbitrary folder path with spaces.
- Verify no installer is required.
- Verify upload works on clean Windows 10 and Windows 11 machines.
- Verify behavior when device is disconnected during upload.

## Compliance

- Review obligations for distributing `dfu-util` and its dependencies.