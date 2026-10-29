# Factory Verification Checklist

Use this before releasing firmware or hardware revisions.

## USB / Driver Requirements

- DFU interface exposes Microsoft OS descriptors for automatic WinUSB binding.
- DFU descriptors are valid and stable across resets.
- VID/PID values are final and documented.
- USB serial number is unique per unit.
- The product's actual VID/PID and hardware IDs are not assumed to be covered by Arduino's Renesas INF.
- The DFU interface binds to the vendor-approved WinUSB driver and reports a healthy Windows PnP status.

## Validation on Clean PCs

1. Use a clean Windows 10/11 machine with no manual driver tools.
2. Connect board in DFU mode.
3. Confirm device appears without manual INF install.
4. Confirm `dfu-util -l` can enumerate.
5. In the app, run **Check Prerequisites** and confirm the selected VID/PID matches `dfu-util` output and the driver reports WinUSB with no PnP error.
6. Flash known-good firmware and verify normal boot.
7. Repeat on multiple USB ports and with hub/no-hub.

## Multi-device Validation

- Connect 2+ units simultaneously.
- Verify each unit is targeted by its unique USB serial or enumerated USB path.
- Confirm no cross-flashing occurs.
- Confirm preflight blocks devices that cannot be uniquely selected.

## Failure and Diagnostics Validation

- Disconnect the device during enumeration and during transfer; neither case may report a successful upload.
- Verify enumeration and upload timeouts stop the `dfu-util` process and preserve stdout, stderr, arguments, and exit status in the support log.
- Verify a missing or unhealthy WinUSB binding blocks upload.
- Verify driver installation only accepts a complete signed Arduino Renesas INF/catalog pair for an explicitly supported hardware ID; unsupported/custom VID/PID devices must be refused.
- Verify administrator cancellation, an invalid catalog signature, and a failed `pnputil` installation are reported as failures.