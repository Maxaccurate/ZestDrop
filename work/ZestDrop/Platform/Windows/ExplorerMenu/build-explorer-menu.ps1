[CmdletBinding()]
param(
    [string]$Destination = (Join-Path $PSScriptRoot '..\..\bin\Debug\net8.0-windows10.0.19041.0\win-x64'),
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$source = $PSScriptRoot
$cache = Join-Path $PSScriptRoot '.cache'

New-Item -ItemType Directory -Force -Path $cache, $Destination | Out-Null

$dllPath = Join-Path $Destination 'ZestDrop.dll'
if (Test-Path $dllPath) {
    [System.Reflection.Assembly]::LoadFrom($dllPath) | Out-Null
}

Write-Host "Creating ZestDropExplorerMenu.msix..."
$manifest = [ZestDrop.Platform.Windows.ExplorerMenuPackage]::CreateManifest()
$tempManifest = Join-Path $cache "AppxManifest.xml"
[System.IO.File]::WriteAllText($tempManifest, $manifest, [System.Text.UTF8Encoding]::new($false))

$msixPath = Join-Path $Destination ([ZestDrop.Platform.Windows.ExplorerMenuPackage]::PackageFileName)
$logoPng = Join-Path $cache 'Logo.png'

# Create 150x150 Logo.png if needed
if (-not (Test-Path $logoPng)) {
    Add-Type -AssemblyName System.Drawing
    $bmp = [System.Drawing.Bitmap]::new(150, 150)
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $gfx.Clear([System.Drawing.Color]::FromArgb(255, 255, 140, 0))
    $bmp.Save($logoPng, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $gfx.Dispose()
}

$payload = [System.Collections.Generic.List[System.ValueTuple[string, string, string]]]::new()
$payload.Add([System.ValueTuple[string, string, string]]::new([ZestDrop.Platform.Windows.ExplorerMenuPackage]::LogoPath, $logoPng, "image/png"))

if (Test-Path $msixPath) { Remove-Item $msixPath -Force }
[ZestDrop.Platform.Windows.ExplorerMenuPackage]::PackAppx($msixPath, $tempManifest, $payload)

Write-Host "MSIX package successfully generated at: $msixPath"
