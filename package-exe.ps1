# Package a complete bundle as a per-user Windows installer. No application rebuild is performed.
param(
    [string]$BundlePath = (Join-Path $PSScriptRoot 'outputs/ZestDrop'),
    [string]$CompilerPath,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'outputs/installers')
)
$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$bundle = (Resolve-Path -LiteralPath $BundlePath).Path
$outDir = [IO.Path]::GetFullPath($OutputDirectory)

if (-not $CompilerPath) {
    $candidates = @(
        (Join-Path $repoRoot 'outputs/tools/inno-7.1.0/ISCC.exe'),
        'C:\Program Files\Inno Setup 7\ISCC.exe',
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
    )
    $CompilerPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $CompilerPath) {
        $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($command) { $CompilerPath = $command.Source }
    }
}
if (-not $CompilerPath -or -not (Test-Path -LiteralPath $CompilerPath)) {
    throw 'Install Inno Setup 7 (https://jrsoftware.org/isdl.php), or pass -CompilerPath to ISCC.exe.'
}
foreach ($required in @('ZestDrop.exe', 'ZestDrop.dll', 'ZestDrop.runtimeconfig.json', 'backend/worker.py',
    'runtime/python/python.exe', 'runtime/ffmpeg/ffmpeg.exe', 'runtime/ffmpeg/ffprobe.exe',
    'runtime/7zip/7z.exe', 'runtime/7zip/7z.dll', 'LICENSE', 'THIRD-PARTY-NOTICES.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $bundle $required) -PathType Leaf)) {
        throw "Incomplete app bundle: missing $required. Run build.ps1 with the complete portable runtimes first."
    }
}
$exeInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $bundle 'ZestDrop.exe'))
$version = '{0}.{1}.{2}' -f $exeInfo.FileMajorPart, $exeInfo.FileMinorPart, $exeInfo.FileBuildPart
if ($version -notmatch '^\d+\.\d+\.\d+$' -or $version -eq '0.0.0') { throw 'Cannot read application version.' }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$installer = Join-Path $outDir "ZestDrop-v$version-windows-x64-setup.exe"
if (Test-Path -LiteralPath $installer) { throw "Output already exists: $installer. Use another output directory." }

# A fresh staging folder avoids stale binaries and requires no recursive deletion.
$stage = Join-Path $outDir ('stage-' + [Guid]::NewGuid().ToString('N'))
if ($stage.StartsWith($bundle.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The installer output directory must be outside the source bundle.'
}
New-Item -ItemType Directory -Path $stage | Out-Null
robocopy $bundle $stage /E /NFL /NDL /NJH /NJS /NP /XD source logs __pycache__ /XF *.pdb *.pyc last-cli-result.json last-cli-result.json.progress events.jsonl | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Staging failed (robocopy exit $LASTEXITCODE)." }
if (Get-ChildItem -LiteralPath $stage -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint } | Select-Object -First 1) {
    throw 'Staging contains a reparse point; the installer must contain real files.'
}
$files = @(Get-ChildItem -LiteralPath $stage -Recurse -File | Sort-Object FullName | ForEach-Object {
    [ordered]@{ path = $_.FullName.Substring($stage.Length + 1).Replace('\', '/'); bytes = $_.Length;
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$manifest = [ordered]@{ version = $version; productVersion = $exeInfo.ProductVersion; files = $files }
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outDir 'installer-payload.json') -Encoding utf8

# Python creates bytecode beside the installed modules. Remove only those cache
# files, then empty directories, so reinstalling after uninstall works as expected.
$cleanupFile = Join-Path $outDir 'uninstall-cache.iss'
$cleanup = [Collections.Generic.List[string]]::new()
$cleanup.Add('[UninstallDelete]')
$pythonDirs = @($files | Where-Object { $_.path.EndsWith('.py') } | ForEach-Object {
    [IO.Path]::GetDirectoryName($_.path.Replace('/', '\'))
} | Sort-Object -Unique)
foreach ($dir in $pythonDirs) {
    $cache = if ($dir) { $dir + '\__pycache__' } else { '__pycache__' }
    $cleanup.Add('Type: files; Name: "{app}\' + $cache + '\*.pyc"')
    $cleanup.Add('Type: dirifempty; Name: "{app}\' + $cache + '"')
}
Get-ChildItem -LiteralPath $stage -Recurse -Directory | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object {
    $relative = $_.FullName.Substring($stage.Length + 1)
    $cleanup.Add('Type: dirifempty; Name: "{app}\' + $relative + '"')
}
$cleanup.Add('Type: dirifempty; Name: "{app}"')
[IO.File]::WriteAllLines($cleanupFile, $cleanup, [Text.UTF8Encoding]::new($false))

& $CompilerPath ('/DBundlePath=' + $stage) ('/DAppVersion=' + $version) ('/DOutputPath=' + $outDir) `
    ('/DAppIcon=' + (Join-Path $repoRoot 'work/ZestDrop/assets/zestdrop.ico')) ('/DCleanupFile=' + $cleanupFile) `
    '/Qp' (Join-Path $repoRoot 'packaging/ZestDrop.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$digest = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($installer + '.sha256', $digest + '  ' + [IO.Path]::GetFileName($installer) + "`n", [Text.Encoding]::ASCII)
Write-Output "Built: $installer"
Write-Output "SHA-256: $digest"
