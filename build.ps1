$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The 64-bit .NET Framework C# compiler was not found.' }
$outDir = Join-Path $root 'dist'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
Get-ChildItem -LiteralPath $outDir -File | Remove-Item -Force
$buildDir = Join-Path $env:TEMP 'CurveThumbBuild'
New-Item -ItemType Directory -Path $buildDir -Force | Out-Null
$source = Join-Path $buildDir 'CurveThumbProvider.cs'
$output = Join-Path $buildDir 'CurveThumb.Provider.dll'
Copy-Item -LiteralPath (Join-Path $root 'src\CurveThumbProvider.cs') -Destination $source -Force
$rasterSource = Join-Path $root 'src\StlRasterizer.cs'
$compilerLine = '/nologo /target:library /platform:x64 /optimize+ /out:"' + $output + '" ' +
    '/reference:System.Drawing.dll /reference:System.IO.Compression.dll ' +
    '/reference:System.Xml.Linq.dll "' + $source + '" "' + $rasterSource + '"'
$process = Start-Process -FilePath $compiler -ArgumentList $compilerLine -Wait -PassThru -NoNewWindow
if ($process.ExitCode -ne 0) { throw "Compilation failed with exit code $($process.ExitCode)." }
Copy-Item -LiteralPath $output -Destination (Join-Path $outDir 'CurveThumb.Provider.dll') -Force
$setupSource = Join-Path $root 'src\CurveThumbSetup.cs'
$manifest = Join-Path $root 'src\CurveThumbSetup.manifest'
$setupOutput = Join-Path $outDir 'CurveThumb.Setup.exe'
$setupLine = '/nologo /target:winexe /platform:x64 /optimize+ /out:"' + $setupOutput + '" ' +
    '/reference:System.Windows.Forms.dll /reference:System.Management.dll /resource:"' + $output + '",CurveThumb.Provider.dll ' +
    '/win32manifest:"' + $manifest + '" "' + $setupSource + '"'
$setupProcess = Start-Process -FilePath $compiler -ArgumentList $setupLine -Wait -PassThru -NoNewWindow
if ($setupProcess.ExitCode -ne 0) { throw "Setup compilation failed with exit code $($setupProcess.ExitCode)." }
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $outDir -Force
Copy-Item -LiteralPath (Join-Path $root 'QUICKSTART.md') -Destination $outDir -Force
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $outDir -Force
Copy-Item -LiteralPath (Join-Path $root 'assets\logo.png') -Destination $outDir -Force
Copy-Item -LiteralPath (Join-Path $root 'samples\Sample-Tetrahedron.stl') -Destination $outDir -Force
$package = Join-Path $outDir 'CurveThumbExplorer-x64.zip'
Compress-Archive -Path (Join-Path $outDir '*') -DestinationPath $package -Force
$checksums = @(
    "SHA256  $((Get-FileHash -LiteralPath $setupOutput -Algorithm SHA256).Hash)  CurveThumb.Setup.exe"
    "SHA256  $((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash)  CurveThumbExplorer-x64.zip"
)
Set-Content -LiteralPath (Join-Path $outDir 'SHA256SUMS.txt') -Value $checksums -Encoding ascii
Write-Host "Build complete: $package"
