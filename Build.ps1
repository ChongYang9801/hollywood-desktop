$ErrorActionPreference = 'Stop'

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}

$source = Join-Path $PSScriptRoot 'src'
$assets = Join-Path $PSScriptRoot 'assets'
$executable = Join-Path $PSScriptRoot 'HollywoodDesktop.exe'
$files = Get-ChildItem -LiteralPath $source -Filter '*.cs' -File | ForEach-Object FullName
$resources = @(
    foreach ($name in @('map.jpg', 'soundwave.frames.gz', 'upstream-hollywood.sh', 'figlet.txt', 'errno.txt')) {
        "/resource:$assets\$name,Hollywood.$name"
    }
    foreach ($name in @('HollywoodWidget.cs', 'TerminalScene.cs', 'WindowsData.cs', 'DesktopLayer.cs')) {
        "/resource:$source\$name,Hollywood.$name"
    }
    foreach ($name in @('LICENSE', 'THIRD-PARTY-NOTICES.txt')) {
        "/resource:$PSScriptRoot\$name,Hollywood.$name"
    }
)
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /warnaserror+ /codepage:65001 `
    /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
    "/win32manifest:$source\app.manifest" "/win32icon:$source\Hollywood.ico" "/out:$executable" $resources $files
if ($LASTEXITCODE -ne 0) { throw "Compiler exited with code $LASTEXITCODE." }
Get-Item -LiteralPath $executable
