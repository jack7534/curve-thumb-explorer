# CurveThumb Explorer

**Preview 3D models directly in Windows File Explorer.** Browse your model folders in Large or Extra large icons view to see a thumbnail and a small format label beside each supported file. No separate viewer is required.

<img src="assets/logo.png" alt="CurveThumb Explorer logo" width="160">

[View the visual overview and before/after gallery](OVERVIEW.md).

## Supported formats

The built-in thumbnail provider currently supports binary and ASCII STL, OBJ, 3MF, ASCII PLY, and ASCII FBX. Binary FBX, binary PLY, GLB/glTF, STEP/STP, and IGES/IGS are not supported yet. File Explorer will continue to show its regular icon or another installed thumbnail provider for unsupported files. STEP and IGES surface models require a dedicated CAD geometry engine for reliable tessellation.

## Install

1. Download `CurveThumb.Setup.exe` from **Releases** and double-click it. The ZIP package is also available for offline sharing.
2. Approve the Windows administrator prompt. This registers the thumbnail provider for File Explorer.
3. Close and reopen File Explorer. Choose **View → Large icons** or **Extra large icons**.

For cloud files that show a cloud icon, download the model locally or choose **Available offline** before expecting a preview. Windows cannot render a thumbnail while the model data is only online.

Windows SmartScreen may show an unknown publisher notice for a new free and open-source utility. Download only from this project's GitHub Releases page and verify the published SHA-256 checksum.

## Uninstall

Open **Windows Settings → Apps → Installed apps**, find **CurveThumb Explorer**, and choose **Uninstall**. If another machine-wide thumbnail handler was registered for the same format before installation, setup restores it.

## Privacy and compatibility

- Model files are processed locally. The tool does not connect to the internet, upload models, or collect usage data.
- Setup registers the thumbnail handler for this computer. The default app associated with `.stl`, `.obj`, and other model files is not changed.
- Requires 64-bit Windows and administrator approval during installation. Thumbnail rendering runs in Windows' isolated Shell process.
- Only the formats listed above are registered. Unsupported formats remain under Windows or other installed thumbnail providers.
- Large models can take longer to preview. Windows caches thumbnails; clear the Windows thumbnail cache if a changed model still shows an older preview.

## Build from source

Run `build.ps1` on Windows with the .NET Framework 4.x compiler available. The script builds the x64 Shell provider, a single-file setup EXE, and the distributable ZIP in `dist/`.

## License

MIT. See [`LICENSE`](LICENSE).
