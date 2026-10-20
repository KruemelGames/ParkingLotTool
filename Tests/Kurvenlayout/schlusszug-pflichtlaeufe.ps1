$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location -LiteralPath $repo
$ablage = Join-Path $repo '.codex-build/kurven-16'
New-Item -ItemType Directory -Force -Path $ablage | Out-Null
$projekt = 'Tests/Kurvenlayout/Kurvenlayout.csproj'
# Veraltete Bandgrenze wieder einsetzen. Die produktive Ausnahme und der
# exakte Stand 54 muessen den urspruenglichen Konstruktionsfehler finden.
$quelle = [IO.File]::ReadAllText((Join-Path $repo 'Geometry/Zellen/Layout.cs'))
$alt = 'erweitertInnen: kurvenrand == null ? null : randstrassenrand)'
$neu = 'erweitertInnen: kurvenrand == null ? null : Innenrand(einstellungen.Zufahrtstiefe))'
if (!$quelle.Contains($alt)) { throw 'Mutationsstelle der Kappengrenze fehlt.' }
$mutation = Join-Path $ablage 'Layout-alter-Kappenrand.cs'
[IO.File]::WriteAllText($mutation, $quelle.Replace($alt, $neu))
$log = Join-Path $ablage 'schlusszug-mutation.log'
dotnet run -c Release --project $projekt "-p:LayoutQuelle=$mutation" -- --schlusszug --stand=54 *> $log
if ($LASTEXITCODE -ne 1 -or !([IO.File]::ReadAllText($log).Contains('Kurvenkeil liegt ausserhalb seines konstruierten Materialbands.'))) {
    throw 'Veraltete Kappengrenze nicht erkannt.'
}
Write-Output 'Veraltete Kappengrenze: erwarteter Testfehler, Exit 1'
dotnet run -c Release --project $projekt -- --schlusszug --stand=54 *> (Join-Path $ablage 'schlusszug-abschluss.log')
if ($LASTEXITCODE -ne 0) { throw 'Produktiver Schlusszug-Abschluss fehlgeschlagen.' }

dotnet run -c Release --project $projekt -- --schlusszug --raster *> (Join-Path $ablage 'schlusszug.log')
if ($LASTEXITCODE -ne 0) { throw 'Schlusszug-Endlagen fehlgeschlagen.' }
Get-Content (Join-Path $ablage 'schlusszug.log') -Tail 2
python Tests/Kurvenlayout/schlusszug_messung.py .codex-build/kurven-16/schlusszug.jsonl *> (Join-Path $ablage 'schlusszug-messung.log')
if ($LASTEXITCODE -ne 0) { throw 'Schlusszug-Materialpruefung fehlgeschlagen.' }
Get-Content (Join-Path $ablage 'schlusszug-messung.log') -Tail 1
