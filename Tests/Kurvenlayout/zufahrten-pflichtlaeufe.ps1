$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location -LiteralPath $repo
$ablage = Join-Path $repo '.codex-build/kurven-14'
New-Item -ItemType Directory -Force -Path $ablage | Out-Null
& pwsh -NoProfile -File Tests/Kurvenlayout/zufahrten-mutationen.ps1 *> (Join-Path $ablage 'zufahrten-mutationen.log')
if ($LASTEXITCODE -ne 0) { throw 'Kurvenzufahrts-Gegenproben fehlgeschlagen.' }
dotnet run -c Release --project Tests/Kurvenlayout/Kurvenlayout.csproj -- --kurvenzufahrten *> (Join-Path $ablage 'zufahrten.log')
if ($LASTEXITCODE -ne 0) { throw 'Spielbefund Kurvenzufahrten fehlgeschlagen.' }
# 5,5 m Belag sind Absicht: die 6-m-Gasse bleibt ueber dem Belag sichtbar.
# 6 m sind hier nur ein weiterer Eingabefall, kein Korrekturvorschlag.
dotnet run -c Release --no-build --project Tests/Kurvenlayout/Kurvenlayout.csproj -- --kurvenzufahrten --breite=6 --ausgabe=.codex-build/kurven-14/zufahrten-6m.jsonl *> (Join-Path $ablage 'zufahrten-6m.log')
if ($LASTEXITCODE -ne 0) { throw 'Kurvenzufahrten mit breiterem Belagparameter fehlgeschlagen.' }
dotnet run -c Release --no-build --project Tests/Kurvenlayout/Kurvenlayout.csproj -- --kurvenzufahrten --knoten *> (Join-Path $ablage 'knoten.log')
if ($LASTEXITCODE -ne 0) { throw 'Kurvenpunkt-Zufahrten fehlgeschlagen.' }
python Tests/Kurvenlayout/kurvenzufahrten.py .codex-build/kurven-14/zufahrten.jsonl .codex-build/kurven-14/zufahrten-6m.jsonl .codex-build/kurven-14/knoten.jsonl *> (Join-Path $ablage 'zufahrten-geos.log')
if ($LASTEXITCODE -ne 0) { throw 'Unabhaengige Gassenquerschnitt-Messung fehlgeschlagen.' }
Get-Content (Join-Path $ablage 'zufahrten-geos.log') -Tail 1

