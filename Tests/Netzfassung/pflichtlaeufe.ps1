$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location -LiteralPath $repo
$ablage = Join-Path $repo '.codex-build/netzfassung'
New-Item -ItemType Directory -Force -Path $ablage | Out-Null
& pwsh -NoProfile -File Tests/Netzfassung/mutationen.ps1 *> (Join-Path $ablage 'mutationen.log')
if ($LASTEXITCODE -ne 0) { throw 'Netzfassungs-Gegenproben fehlgeschlagen.' }
python Tests/Netzfassung/fixturen.py *> (Join-Path $ablage 'fixturen.log')
if ($LASTEXITCODE -ne 0) { throw 'Netz-Fixturenvergleich fehlgeschlagen.' }
Get-Content (Join-Path $ablage 'fixturen.log') -Tail 1
