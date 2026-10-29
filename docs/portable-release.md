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
- Do not bundle an unsigned or modified USB driver package. The app accepts a user-selected, complete Arduino Renesas package only when the INF matches a supported Arduino hardware ID and its `renesas.cat` signature verifies.

## DFU Driver Workflow

- The app does not silently install drivers. The user must choose **Install / Repair Driver**, select `renesas.inf`, confirm the operation, and approve the Windows administrator prompt.
- The INF and its matching `renesas.cat` must come from the official Arduino Renesas board-package installation. The compatibility list is based on the [Arduino Renesas driver INF](https://github.com/arduino/ArduinoCore-renesas/blob/main/drivers/renesas.inf). The app validates the Arduino provider, WinUSB install sections, supported hardware ID list, and Windows catalog signature; Windows `pnputil` then validates and installs the package.
- The supported USB hardware IDs are deliberately limited to those in the checked-in Arduino Renesas INF compatibility list. Custom VID/PID products require their own vendor driver and must not be assigned the Arduino INF.
- No DFU upload may proceed unless a fresh Windows query reports a healthy WinUSB binding and `dfu-util -l` uniquely identifies the selected device by VID/PID plus serial or USB path.

## One-Time Release Setup

- Edit `legal\SOURCE-OFFER.txt` and replace `[REPLACE_WITH_YOUR_SUPPORT_EMAIL]` with your real support/compliance email before distributing.

## QA Before Shipment

- Run from an arbitrary folder path with spaces.
- Verify no installer is required.
- Verify upload works on clean Windows 10 and Windows 11 machines.
- Verify preflight with a correct device, an incorrect VID/PID, a missing WinUSB binding, and a missing `dfu-util`.
- Verify multiple identical devices are targeted by serial/path, and ambiguous devices block flashing.
- Verify device disconnection and the five-minute upload timeout do not report success.
- Verify driver installation rejects an unsigned catalog, mismatched INF, unsupported/custom VID/PID, UAC cancellation, and a failed `pnputil` operation.
- Run `dotnet test .\IOMini2App.sln` on Windows and build/publish the Release solution.
- Verify **Export Support Log** includes complete process output and clearly warns that serial numbers and local paths may need redaction.

## Compliance

- Review obligations for distributing `dfu-util` and its dependencies.