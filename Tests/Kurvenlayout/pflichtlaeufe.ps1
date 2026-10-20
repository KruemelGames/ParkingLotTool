$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location -LiteralPath $repo
$ablage = Join-Path $repo '.codex-build/kurven-9/prueflauf'
New-Item -ItemType Directory -Force -Path $ablage | Out-Null
dotnet run -c Release --project Tests/Kurvenlayout/Kurvenlayout.csproj -- $ablage *> (Join-Path $ablage 'matrix.log')
if ($LASTEXITCODE -ne 0) { throw 'Kurven-Messmatrix fehlgeschlagen.' }
python Tests/Kurvenlayout/messung.py (Join-Path $ablage 'faelle.jsonl') *> (Join-Path $ablage 'messung.log')
if ($LASTEXITCODE -ne 0) { throw 'Unabhaengige Flaechenmessung fehlgeschlagen.' }
python Tests/Kurvenlayout/pruefung.py
if ($LASTEXITCODE -ne 0) { throw 'Messgeraet-Selbsttest fehlgeschlagen.' }
$messung = [IO.File]::ReadAllText((Join-Path $repo 'Tests/Kurvenlayout/messung.py'))
$pruefung = [IO.File]::ReadAllText((Join-Path $repo 'Tests/Kurvenlayout/pruefung.py'))
$faelle = @(
    @('loch-ignoriert', 'fehlt = umriss.difference(belegt)', 'fehlt = Polygon()', 'Loch wird gemessen'),
    @('ueberlapp-ignoriert', 'Ueberlappung=sum(p.area for p in ringe) - belegt.area', 'Ueberlappung=0', 'Doppelflaeche wird gemessen'),
    @('selbstschnitt-ignoriert', 'UngueltigeRinge=invalid', 'UngueltigeRinge=0', 'Selbstschnitt wird nicht repariert'),
    @('breitenzeuge-ignoriert', 'LueckeBreiter5cm=fehlt.buffer(-.025).area', 'LueckeBreiter5cm=0', 'Loch wird gemessen')
)
foreach ($f in $faelle) {
    if (!$messung.Contains($f[1])) { throw "Mutationsstelle fehlt: $($f[0])" }
    $ordner = Join-Path $ablage $f[0]
    New-Item -ItemType Directory -Force -Path $ordner | Out-Null
    [IO.File]::WriteAllText((Join-Path $ordner 'messung.py'), $messung.Replace($f[1], $f[2]), [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $ordner 'pruefung.py'), $pruefung, [Text.UTF8Encoding]::new($false))
    $log = Join-Path $ordner 'mutation.log'
    python (Join-Path $ordner 'pruefung.py') *> $log
    if ($LASTEXITCODE -ne 1 -or !([IO.File]::ReadAllText($log).Contains($f[3]))) {
        throw "Mutation nicht erkannt: $($f[0])"
    }
    Write-Output "$($f[0]): erwarteter Testfehler, Exit 1"
}
# Kein Gruenwaschen: die Abnahme liefert bei offenen Layoutbefunden Exit 1.
python Tests/Kurvenlayout/pruefung.py --abnahme (Join-Path $ablage 'faelle.jsonl') *> (Join-Path $ablage 'abnahme.log')
$abnahme = $LASTEXITCODE
Get-Content (Join-Path $ablage 'abnahme.log')
exit $abnahme
