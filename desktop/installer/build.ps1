# Baut die Windows-App (eigenständig, x64) und daraus das Installationsprogramm.
#   pwsh desktop/installer/build.ps1 -Version 0.1.0 -Output out
param(
    [string]$Version = "0.1.0",
    [string]$Output = "out"
)
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$publish = Join-Path ([IO.Path]::GetTempPath()) "EstrichONE-publish"
New-Item -ItemType Directory -Force -Path $Output | Out-Null
$Output = Resolve-Path $Output

# Versionsnummer für Windows: nur Ziffern (0.1.0-preview.2 → 0.1.0).
$numeric = ($Version -split '-')[0]
dotnet publish (Join-Path $root "src/EstrichOne.Desktop/EstrichOne.Desktop.csproj") -c Release -r win-x64 -p:Platform=x64 `
    -p:Version=$numeric -p:InformationalVersion=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fehlgeschlagen" }

$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    choco install innosetup -y --no-progress | Out-Null
    $iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
}
& $iscc /Q "/DAppVersion=$Version" "/DNumericVersion=$numeric" "/DSourceDir=$publish" "/DOutputDir=$Output" (Join-Path $PSScriptRoot "EstrichONE.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup fehlgeschlagen" }

Compress-Archive -Path "$publish\*" -DestinationPath (Join-Path $Output "EstrichONE-Windows-x64-$Version.zip") -Force
Get-ChildItem $Output
