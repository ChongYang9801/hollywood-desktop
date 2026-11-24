# Hollywood Desktop

[简体中文](README.md) · [English](README.en.md)

Hollywood Desktop is an unofficial Windows port of [Dustin Kirkland’s Hollywood](https://github.com/dustinkirkland/hollywood). It keeps the random terminal splits and the roles of the 20 upstream widgets. Run it in a Windows terminal or as animated wallpaper behind your desktop icons.

![Preview of the Hollywood Desktop animated terminal wallpaper](Preview.png)

## Download and run

1. Download `HollywoodDesktop.exe` from [GitHub Releases](https://github.com/ChongYang9801/hollywood-desktop/releases/latest) and double-click it to start the wallpaper. It uses the .NET Framework 4.x included with Windows and requires no WSL, Docker, `tmux`, or separate runtime installation.
2. Right-click the Hollywood icon in the taskbar notification area. The menu lets you **Pause animation** (`暂停动画`), **Resume animation** (`继续动画`), or **Shuffle panels** (`重排终端面板`). If the icon is hidden, open the `^` overflow menu.
3. Choose **Exit and restore wallpaper** (`退出并恢复壁纸`) to remove the animation and reveal your existing wallpaper. You can also exit by running `./HollywoodDesktop.exe --stop` in PowerShell.

The app was tested on Windows 11 with two monitors. Windows handles desktop icon clicks, dragging, and context menus. If Windows animation effects are disabled, the wallpaper starts paused. You can resume it from the tray menu. The app tries to reattach if Explorer rebuilds the desktop layer.

## Terminal mode and options

Run this from PowerShell in Windows Terminal. Press `Ctrl+C` to exit.

```powershell
Start-Process -FilePath .\HollywoodDesktop.exe -ArgumentList '--terminal', '-s', '8', '-d', '10' -NoNewWindow -Wait
```

`-s` or `--splits` sets the maximum pane count. The default is twice the logical processor count, capped at 20. `-d` or `--delay` sets the shuffle interval and defaults to 10 seconds. Omit `--terminal` to use the same options for the wallpaper.

## Upstream widgets on Windows

| Upstream widgets | Windows data and output |
| --- | --- |
| `map`, `mplayer`, `figlet` | The original world map and ASCII frames from the original soundwave video, plus FIGlet Standard output for the upstream word list |
| `cmatrix`, `apg`, `pv`, `sshart` | Digital rain, pronounceable random strings, a 10 MiB random byte transfer, and SHA-256 fingerprint art. When Windows OpenSSH is available, the art uses a disposable Ed25519 key |
| `atop`, `htop`, `bmon`, `speedometer` | Live Windows CPU, memory, process, and network counters. In small panes, `bmon` falls back to `speedometer` |
| `bat`, `code`, `errno`, `hexdump` | Highlighted port source and the upstream script, Linux error names and codes, and hex dumps of Windows system binaries |
| `jp2a`, `logs`, `man`, `stat`, `tree` | Windows stock images, System and Application event logs, Windows command help, system file metadata, and directory trees |

The port preserves the terminal panes and the kinds of content they display. System widgets read the local Windows machine, so log entries, processes, paths, and help text differ from those on Linux.

## Build from source

Open PowerShell in the repository directory and run the following command. The script uses the C# compiler included with Windows and writes `HollywoodDesktop.exe` to the same directory.

```powershell
powershell -NoProfile -File .\Build.ps1
```

## Credits

The port follows [Hollywood upstream commit `b715325`](https://github.com/dustinkirkland/hollywood/tree/b715325107cb3f54f77831c3a9d23adcdfe8de1d). See the [license](LICENSE) and [third-party notices](THIRD-PARTY-NOTICES.txt) for asset credits. The executable also includes these notices in the tray menu.
