$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$ablage = Join-Path $repo '.codex-build/register-1a'
New-Item -ItemType Directory -Force -Path $ablage | Out-Null
Set-Location -LiteralPath $repo
# Bestehende Geometrie-, Sprach-, Speicherformat-, UI- und Mutationspruefungen.
& pwsh -NoProfile -File Tests/Speicherformate/pflichtlaeufe.ps1 *> (Join-Path $ablage 'pflichtlaeufe.log')
if ($LASTEXITCODE -ne 0) { throw 'Bisherige Pflichtlaeufe fehlgeschlagen.' }
Write-Output 'Bisherige Pflichtlaeufe : Exit 0'
foreach ($name in @('register-mutationen', 'worker-mutationen', 'nebenarbeit-mutationen', 'hardening-mutationen', 'mutationen')) {
    & pwsh -NoProfile -File "Tests/SyncSicherheit/$name.ps1" *> (Join-Path $ablage "$name.log")
    if ($LASTEXITCODE -ne 0) { throw "$name fehlgeschlagen." }
    Write-Output "$name : Exit 0"
}
# Der letzte Lauf muss wieder ohne mutierte Quelldatei erfolgen.
dotnet run -c Release --project Tests/SyncSicherheit/SyncSicherheit.csproj *> (Join-Path $ablage 'sync.log')
if ($LASTEXITCODE -ne 0) { throw 'Produktiver Abschlusslauf fehlgeschlagen.' }
Write-Output 'SyncSicherheit produktiv : Exit 0'

foreach ($name in @('Versorgungsabsturz','Versorgungsuebernahme')) {
    dotnet run -c Release --project "Tests/$name/$name.csproj" *> (Join-Path $ablage "$name.log")
    if ($LASTEXITCODE -ne 0) { throw "$name fehlgeschlagen." }
    Write-Output "$name : Exit 0"
}

# Neue Sprachen und ihre produktive Mehrzahlregel, einschliesslich UI-Gegenproben.
& pwsh -NoProfile -File Tests/Language/pflichtlaeufe.ps1 -GrundlaeufeBereitsGeprueft
if ($LASTEXITCODE -ne 0) { throw 'Sprachgegenproben fehlgeschlagen.' }
