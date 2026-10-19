# Quick Start

![Download the setup EXE, approve the Windows prompt, and reopen File Explorer](docs/install-guide.svg)

## Install and view thumbnails

1. Download [CurveThumb.Setup.exe](https://github.com/jack7534/curve-thumb-explorer/releases/download/v1.1.0/CurveThumb.Setup.exe) from [GitHub Releases](https://github.com/jack7534/curve-thumb-explorer/releases/tag/v1.1.0) and double-click it.
2. Approve the Windows administrator prompt.
3. Close and reopen the folder in File Explorer.
4. Choose **View → Large icons** or **Extra large icons**.

Supported files: `.stl`, `.obj`, `.3mf`, ASCII `.ply`, and ASCII `.fbx`. A small format label appears on each supported model thumbnail.

## If you still see a generic icon

- Binary STL files larger than 128 MiB use a sampled thumbnail. Other formats are limited to 128 MiB to keep File Explorer responsive.
- PLY and FBX must use the ASCII variant. Binary PLY and binary FBX are not supported yet.
- File Explorer may still be showing an older cached thumbnail. Clear **Thumbnails** in Windows Disk Cleanup, then reopen the folder.
- Another thumbnail extension may already handle the same format. Uninstalling CurveThumb restores a machine-wide handler saved during installation.
- Files marked with a cloud icon must be downloaded or made available offline first.

## Uninstall

Use **Settings → Apps → Installed apps → CurveThumb Explorer → Uninstall**, then reopen File Explorer. The uninstaller removes the registry entries created by CurveThumb and restores any saved machine-wide thumbnail handler.

## Privacy

Parsing and rendering happen on your computer. CurveThumb does not connect to the internet, upload model files, or collect usage data.
