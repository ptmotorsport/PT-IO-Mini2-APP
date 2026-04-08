# Factory Verification Checklist

Use this before releasing firmware or hardware revisions.

## USB / Driver Requirements

- DFU interface exposes Microsoft OS descriptors for automatic WinUSB binding.
- DFU descriptors are valid and stable across resets.
- VID/PID values are final and documented.
- USB serial number is unique per unit.

## Validation on Clean PCs

1. Use a clean Windows 10/11 machine with no manual driver tools.
2. Connect board in DFU mode.
3. Confirm device appears without manual INF install.
4. Confirm `dfu-util -l` can enumerate.
5. Flash known-good firmware and verify normal boot.
6. Repeat on multiple USB ports and with hub/no-hub.

## Multi-device Validation

- Connect 2+ units simultaneously.
- Verify each unit can be targeted by USB serial.
- Confirm no cross-flashing occurs.