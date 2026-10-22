# -Store builds for the Microsoft Store package (package-msix.ps1); other builds leave out the Windows SDK.
param([switch]$SkipRuntimeCheck, [switch]$Store)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskOutput = Join-Path $taskRoot 'outputs/ZestDrop'

if (-not $SkipRuntimeCheck) {
    foreach ($taskFile in @('runtime/python/python.exe', 'runtime/ffmpeg/ffmpeg.exe', 'runtime/ffmpeg/ffprobe.exe', 'runtime/7zip/7z.exe', 'runtime/7zip/7z.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $taskOutput $taskFile))) {
            throw "Missing $taskFile. Run python work/prepare-runtime.py first, or copy runtime from a complete build."
        }
    }
}

dotnet publish (Join-Path $taskRoot 'work/ZestDrop/ZestDrop.csproj') -c Release -r win-x64 --self-contained true -o $taskOutput --nologo "-p:Store=$($Store.IsPresent)"
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
# A previous Store build leaves the Windows SDK files behind; the other builds do not use them.
if (-not $Store) {
    foreach ($taskName in @('Microsoft.Windows.SDK.NET.dll', 'WinRT.Runtime.dll')) {
        $taskStale = Join-Path $taskOutput $taskName
        if (Test-Path -LiteralPath $taskStale -PathType Leaf) { Remove-Item -LiteralPath $taskStale -Force }
    }
}
Copy-Item -LiteralPath (Join-Path $taskRoot 'work/ZestDrop/backend') -Destination $taskOutput -Recurse -Force
Get-ChildItem -LiteralPath (Join-Path $taskOutput 'backend') -Recurse -Directory -Filter '__pycache__' | Remove-Item -Recurse -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/usage.md') -Destination (Join-Path $taskOutput '使用说明.md') -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/dependencies.json') -Destination (Join-Path $taskOutput '依赖清单.json') -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'LICENSE') -Destination (Join-Path $taskOutput 'LICENSE') -Force
# Third-party notices, opened from the tray menu; generated from docs/dependencies.json.
$taskPython = Join-Path $taskOutput 'runtime/python/python.exe'
if (Test-Path -LiteralPath $taskPython) {
    & $taskPython (Join-Path $taskRoot 'work/make-notices.py') (Join-Path $taskOutput 'THIRD-PARTY-NOTICES.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Generating third-party notices failed.' }
} elseif (-not $SkipRuntimeCheck) { throw 'Missing runtime/python; cannot generate third-party notices.' }
Write-Output "Built: $taskOutput/ZestDrop.exe"
