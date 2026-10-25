# Build a tiny online installer pinned to the exact existing offline installer.
param(
    [Parameter(Mandatory=$true)][string]$InstallerPath,
    [string]$CompilerPath,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'outputs/online-installers')
)
$ErrorActionPreference = 'Stop'
$offline = (Resolve-Path -LiteralPath $InstallerPath).Path
$info = [Diagnostics.FileVersionInfo]::GetVersionInfo($offline)
$version = '{0}.{1}.{2}' -f $info.FileMajorPart, $info.FileMinorPart, $info.FileBuildPart
if ([IO.Path]::GetFileName($offline) -ne "ZestDrop-v$version-windows-x64-setup.exe") {
    throw 'InstallerPath must be the versioned ZestDrop offline installer.'
}
if (-not $CompilerPath) {
    $CompilerPath = @(
        (Join-Path $PSScriptRoot 'outputs/tools/inno-7.1.0/ISCC.exe'),
        'C:\Program Files\Inno Setup 7\ISCC.exe',
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $CompilerPath) {
        $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($command) { $CompilerPath = $command.Source }
    }
}
if (-not $CompilerPath -or -not (Test-Path -LiteralPath $CompilerPath)) {
    throw 'Install Inno Setup 7, or pass -CompilerPath to ISCC.exe.'
}
$outDir = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$online = Join-Path $outDir "ZestDrop-v$version-windows-x64-online-setup.exe"
if (Test-Path -LiteralPath $online) { throw "Output already exists: $online" }
$digest = (Get-FileHash -LiteralPath $offline -Algorithm SHA256).Hash.ToLowerInvariant()
$url = "https://github.com/Maxaccurate/ZestDrop/releases/download/v$version/ZestDrop-v$version-windows-x64-setup.exe"
$downloadMB = [math]::Round((Get-Item -LiteralPath $offline).Length / 1e6, 1).ToString([Globalization.CultureInfo]::InvariantCulture)
& $CompilerPath ('/DAppVersion=' + $version) ('/DInstallerURL=' + $url) ('/DInstallerSHA256=' + $digest) `
    ('/DDownloadSizeMB=' + $downloadMB) `
    ('/DOutputPath=' + $outDir) ('/DAppIcon=' + (Join-Path $PSScriptRoot 'work/ZestDrop/assets/zestdrop.ico')) `
    '/Q' (Join-Path $PSScriptRoot 'packaging/ZestDrop-online.iss')
if ($LASTEXITCODE -ne 0) { throw 'Online installer compilation failed.' }
[ordered]@{
    version = $version
    downloadUrl = $url
    downloadBytes = (Get-Item -LiteralPath $offline).Length
    downloadSha256 = $digest
    bootstrapBytes = (Get-Item -LiteralPath $online).Length
    bootstrapSha256 = (Get-FileHash -LiteralPath $online -Algorithm SHA256).Hash.ToLowerInvariant()
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outDir 'online-build.json') -Encoding utf8
Write-Output "Built: $online"
