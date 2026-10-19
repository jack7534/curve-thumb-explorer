# Quick Start

## Install and view thumbnails

1. Download `CurveThumbExplorer-x64.zip` from GitHub Releases and extract it to a regular folder.
2. Right-click `Install.ps1` and select **Run with PowerShell**. Administrator rights are not required.
3. Close and reopen the folder in File Explorer.
4. Choose **View → Large icons** or **Extra large icons**.

Supported files: `.stl`, `.obj`, `.3mf`, ASCII `.ply`, and ASCII `.fbx`. A small format label appears on each supported model thumbnail.

## If you still see a generic icon

- The provider skips files larger than 128 MiB to keep File Explorer responsive.
- PLY and FBX must use the ASCII variant. Binary PLY and binary FBX are not supported yet.
- File Explorer may still be showing an older cached thumbnail. Clear **Thumbnails** in Windows Disk Cleanup, then reopen the folder.
- Another thumbnail extension may already handle the same format. Uninstalling CurveThumb restores the current-user handler saved during installation.

## Uninstall

Run `Uninstall.ps1` from the extracted folder, then reopen File Explorer. The uninstaller removes the registry entries created by CurveThumb. The provider DLL is stored in `%LOCALAPPDATA%\Programs\CurveThumb Explorer`; remove that folder if you want to delete the installed files too.

## Privacy

Parsing and rendering happen on your computer. CurveThumb does not connect to the internet, upload model files, or collect usage data.