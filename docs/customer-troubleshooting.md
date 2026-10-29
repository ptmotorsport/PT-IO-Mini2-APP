# Customer Troubleshooting

## Device not shown in the DFU list

- Re-enter board boot/DFU mode, reconnect it with a known-good USB data cable, and press **Refresh**.
- Open **Device Manager** and check whether Windows reports a problem on the USB device.
- Select the device and press **Check Prerequisites**. The app runs the bundled `dfu-util.exe -l` and verifies that Windows and `dfu-util` identify the same VID/PID.
- If another program is using the device, close it and retry.

## Preflight reports a WinUSB or Windows device error

- Flashing is blocked unless the selected device is present, reports a valid VID/PID, is enumerated by `dfu-util`, and has a healthy WinUSB binding in Windows.
- Use **Install / Repair Driver** only for hardware IDs supported by the Arduino Renesas package. Select `renesas.inf` from a complete, official Arduino Renesas board-package installation; the matching `renesas.cat` must be beside it and have a valid Windows signature. The compatibility list is based on the [Arduino Renesas driver INF](https://github.com/arduino/ArduinoCore-renesas/blob/main/drivers/renesas.inf).
- Windows will request administrator approval. Declining the prompt makes no changes. If Windows rejects the package, check the exported diagnostic log and Device Manager.
- The package supports only the specific Arduino Renesas hardware IDs present in that INF. It does **not** support arbitrary product/custom VID/PID values. The app will refuse to install a driver for an unsupported ID; do not force the Arduino INF onto a custom device or use a generic driver-assignment utility. Contact the device vendor for its driver package.

## Upload fails, times out, or the device disconnects

- Do not assume firmware was updated after a failed or interrupted transfer.
- Reconnect the board, re-enter DFU mode, press **Refresh**, and run **Check Prerequisites** again before retrying.
- Check the firmware file is correct for the hardware. Mini2 and DTM2 firmware are not interchangeable.
- Use a direct USB port and a known-good data cable. Close other DFU tools before retrying.
- The app targets the enumerated device by its USB serial number or USB path. If multiple devices cannot be distinguished safely, disconnect the other matching devices and refresh.
- After a successful transfer the board may reset and disappear from the DFU list; this is expected.

## Exporting a support log

- Press **Export Support Log** to save the complete DFU and driver diagnostics. It includes executed commands and arguments, stdout/stderr, exit codes, device USB IDs, driver state, and actionable error details.
- The log can contain USB serial numbers and local firmware paths. Review and redact it before sharing.

## `dfu-util` missing

- Ensure `tools\dfu-util\dfu-util.exe` and its required DLLs are present next to the app output.
- Re-copy the complete portable app folder. Do not download replacement executables from an untrusted source.

## Corporate policy blocks operation

- Ask IT to allow the app executable and bundled `dfu-util`.
- Ask IT to review the product's actual VID/PID and vendor-approved WinUSB binding. Do not request installation of the Arduino package for a custom VID/PID device.
