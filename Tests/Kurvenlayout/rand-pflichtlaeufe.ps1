$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location -LiteralPath $repo
$ablage = Join-Path $repo '.codex-build/kurven-14'
New-Item -ItemType Directory -Force -Path $ablage | Out-Null
python Tests/Kurvenlayout/randmessung_test.py
if ($LASTEXITCODE -ne 0) { throw 'Unabhaengiges Randmessgeraet fehlgeschlagen.' }
& pwsh -NoProfile -File Tests/Kurvenlayout/rand-mutationen.ps1 *> (Join-Path $ablage 'rand-mutationen.log')
if ($LASTEXITCODE -ne 0) { throw 'Randkurven-Gegenproben fehlgeschlagen.' }
& pwsh -NoProfile -File Tests/Kurvenlayout/zufahrten-pflichtlaeufe.ps1
if ($LASTEXITCODE -ne 0) { throw 'Kurvenzufahrts-Pflichtlauf fehlgeschlagen.' }

dotnet run -c Release --project Tests/Kurvenlayout/Kurvenlayout.csproj -- --randbuchten --ausgabe=.codex-build/kurven-14/randbuchten.jsonl *> (Join-Path $ablage 'matrix.log')
$matrix = $LASTEXITCODE
# Auch nach einem Abbruch alle schon ausgewerteten Varianten unabhaengig
# messen. Der C#-Lauf protokolliert jede Ausnahme und liefert am Ende Exit 1.
python Tests/Kurvenlayout/randmessung.py .codex-build/kurven-14/randbuchten.jsonl *> (Join-Path $ablage 'randmessung.log')
$messung = $LASTEXITCODE
Get-Content (Join-Path $ablage 'matrix.log') -Tail 2
Get-Content (Join-Path $ablage 'randmessung.log') -Tail 2
if ($matrix -ne 0 -or $messung -ne 0) { exit 1 }
dotnet run -c Release --no-build --project Tests/Kurvenlayout/Kurvenlayout.csproj -- --randgeraden --ausgabe=.codex-build/kurven-14/geraden.jsonl *> (Join-Path $ablage 'geraden.log')
if ($LASTEXITCODE -ne 0) { throw 'Gerade Vergleichsreihen fehlgeschlagen.' }
Get-Content (Join-Path $ablage 'geraden.log') -Tail 1
