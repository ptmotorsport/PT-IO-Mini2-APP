# PT-IO-Mini Management App

Portable Windows WPF app for managing PT-IO-Mini2 Configurations and flashing RA4M1 firmware via `dfu-util`.

## MVP Features

- Monitor Input statuses and information
- Monitor Output statuses and information
- Configure pullup resisitors on digital inputs
- Configure Drive frequency for outputs
- Change CAN modes
- Change CAN speeds
- Shows available COM ports
- Shows DFU-like USB devices
- Auto-refreshes device lists
- Lets users select `.bin` or `.elf` firmware file
- Runs upload with live status console and progress parsing
- Supports cancel during upload

## Build Prerequisites (developer machine)

- Windows 10/11
- .NET 8 SDK
