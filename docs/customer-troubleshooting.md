# Customer Troubleshooting

## Device not shown in DFU list

- Re-enter board boot/DFU mode.
- Try a known-good USB data cable.
- Try another USB port.
- Press Refresh in app.

## Upload fails immediately

- Confirm firmware file is `.bin` or `.elf`.
- Confirm the selected device is the intended board.
- Disconnect other similar boards and retry.

## dfu-util missing

- Ensure `tools\dfu-util\dfu-util.exe` is present next to app output.
- Re-copy the full portable app folder.

## Corporate policy blocks operation

- Ask IT to allow the app executable and bundled `dfu-util`.
- Ask IT to allow standard WinUSB device binding for the product VID/PID.