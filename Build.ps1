$ErrorActionPreference = 'Stop'

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}

$source = Join-Path $PSScriptRoot 'src'
$executable = Join-Path $PSScriptRoot 'HollywoodDesktop.exe'
$files = Get-ChildItem -LiteralPath $source -Filter '*.cs' -File | ForEach-Object FullName
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /warnaserror+ /codepage:65001 `
    /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
    "/win32manifest:$source\app.manifest" "/win32icon:$source\Hollywood.ico" "/out:$executable" $files
if ($LASTEXITCODE -ne 0) { throw "Compiler exited with code $LASTEXITCODE." }
Get-Item -LiteralPath $executable
