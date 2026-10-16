$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$ablage = Join-Path $repo '.codex-build/speichersicherheit-3-4'
New-Item -ItemType Directory -Force -Path $ablage | Out-Null
Set-Location -LiteralPath $repo
# Nur Testprojekte und Typpruefung; kein Mod-Build, kein UI-Deploy.
& pwsh -NoProfile -File Tests/SyncSicherheit/pflichtlaeufe.ps1 *> (Join-Path $ablage 'pflichtlaeufe-bisher.log')
if ($LASTEXITCODE -ne 0) { throw 'Bisherige Pflichtlaeufe fehlgeschlagen.' }
foreach ($name in @('Speicherformate', 'Baukontext')) {
    dotnet run -c Release --project "Tests/$name/$name.csproj" *> (Join-Path $ablage "$name.log")
    if ($LASTEXITCODE -ne 0) { throw "$name fehlgeschlagen." }
    Write-Output "$name : Exit 0"
}
python Tests/Language/check_cjk.py --report *> (Join-Path $ablage 'cjk.log')
if ($LASTEXITCODE -ne 0) { throw 'CJK-Sprachpruefung fehlgeschlagen.' }
Write-Output 'CJK-Sprachpruefung : Exit 0'
Push-Location UI
try {
    & npx.cmd tsc --noEmit -p tsconfig.json *> (Join-Path $ablage 'typescript.log')
    if ($LASTEXITCODE -ne 0) { throw 'UI-Typpruefung fehlgeschlagen.' }
}
finally { Pop-Location }
Write-Output 'UI-Typpruefung : Exit 0'
& pwsh -NoProfile -File Tests/Speicherformate/mutationen.ps1 *> (Join-Path $ablage 'mutationen.log')
if ($LASTEXITCODE -ne 0) { throw 'Mutationspruefung fehlgeschlagen.' }
Write-Output 'Mutationen : Exit 0'
