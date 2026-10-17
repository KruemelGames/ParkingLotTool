param([switch]$GrundlaeufeBereitsGeprueft)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$ablage = Join-Path $repo '.codex-build/sprachpruefung'
New-Item -ItemType Directory -Force -Path $ablage | Out-Null
Set-Location -LiteralPath $repo
# Der Registerlauf hat Sprache, Breiten und tsc schon geprueft.
if (!$GrundlaeufeBereitsGeprueft) {
    dotnet run -c Release --project Tests/GeometryParity/CSharp/GeometryParity.csproj -- --sprache *> (Join-Path $ablage 'sprache.log')
    if ($LASTEXITCODE -ne 0) { throw 'Sprachpruefung fehlgeschlagen.' }
    python Tests/Language/check_cjk.py --report *> (Join-Path $ablage 'breiten.log')
    if ($LASTEXITCODE -ne 0) { throw 'Breitenpruefung fehlgeschlagen.' }
    Push-Location UI
    try {
        & npx.cmd tsc --noEmit -p tsconfig.json *> (Join-Path $ablage 'typescript.log')
        if ($LASTEXITCODE -ne 0) { throw 'UI-Typpruefung fehlgeschlagen.' }
    }
    finally { Pop-Location }
}
node Tests/Language/check_plural.cjs *> (Join-Path $ablage 'ui-mehrzahl.log')
if ($LASTEXITCODE -ne 0) { throw 'UI-Mehrzahlpruefung fehlgeschlagen.' }
python Tests/Language/gegenproben.py *> (Join-Path $ablage 'gegenproben.log')
if ($LASTEXITCODE -ne 0) { throw 'Sprachgegenproben fehlgeschlagen.' }
Write-Output 'Sprachen: C#/UI-Mehrzahl, Breiten und 18 Gegenproben : Exit 0'
