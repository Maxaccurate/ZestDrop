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

# A running copy locks its files, so the build would stop halfway.
$taskExe = [IO.Path]::GetFullPath((Join-Path $taskOutput 'ZestDrop.exe'))
if (Get-Process ZestDrop -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $taskExe }) { throw "Quit ZestDrop ($taskExe) before building." }

# Publish into a new empty folder. Publishing straight into outputs/ZestDrop skips files whose old copy is
# newer, which once left .NET 8 framework files beside a .NET 10 build.
$taskOutputs = [IO.Path]::GetFullPath((Join-Path $taskRoot 'outputs'))
$taskPublish = Join-Path $taskOutputs ('publish-' + [Guid]::NewGuid().ToString('N'))
dotnet publish (Join-Path $taskRoot 'work/ZestDrop/ZestDrop.csproj') -c Release -r win-x64 --self-contained true -o $taskPublish --nologo "-p:Store=$($Store.IsPresent)"
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
# /IS /IT: overwrite every file, whatever its date. /R:2 /W:1: fail quickly on a locked file instead of retrying for days.
robocopy $taskPublish $taskOutput /E /IS /IT /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copying the published app failed (robocopy exit $LASTEXITCODE)." }
# Remove application files from earlier builds that this build no longer has (for example the Windows SDK
# after a Store build). Only single files are removed; backend, runtime and the files added below are kept.
$taskKeep = @('LICENSE', 'THIRD-PARTY-NOTICES.txt', '使用说明.md', '依赖清单.json', 'last-cli-result.json', 'last-cli-result.json.progress')
$taskFull = [IO.Path]::GetFullPath($taskOutput)
Get-ChildItem -LiteralPath $taskFull -Recurse -File | ForEach-Object {
    $taskRelative = $_.FullName.Substring($taskFull.Length + 1)
    if ($taskRelative -match '^(backend|runtime)\\' -or $taskRelative -in $taskKeep) { return }
    if (-not (Test-Path -LiteralPath (Join-Path $taskPublish $taskRelative) -PathType Leaf)) {
        Write-Output "Removing stale $taskRelative"
        Remove-Item -LiteralPath $_.FullName -Force
    }
}
# The publish folder is removed only if it is exactly outputs\publish-<guid> and contains no links.
if ((Split-Path -Parent $taskPublish) -ne $taskOutputs -or (Split-Path -Leaf $taskPublish) -notmatch '^publish-[0-9a-f]{32}$') { throw "Unexpected publish folder: $taskPublish" }
if (Get-ChildItem -LiteralPath $taskPublish -Recurse -Force -Attributes ReparsePoint | Select-Object -First 1) { throw "Publish folder contains a link: $taskPublish" }
Remove-Item -LiteralPath $taskPublish -Recurse -Force
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
# robocopy reports copied files with exit code 1; report success to callers such as CI.
exit 0
