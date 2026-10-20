$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location -LiteralPath $repo
$ablage = Join-Path $repo '.codex-build/kurven-14/zufahrten-mutationen'
New-Item -ItemType Directory -Force -Path $ablage | Out-Null
$projekt = 'Tests/Kurvenlayout/Kurvenlayout.csproj'
$faelle = @(
    @('gerade-tiefe', 'Geometry/Zellen/Kurvenrand.Zufahrten.cs',
        'if (!_versatzKanten[zufahrt.Vorgabe.Kante]) return null;', 'return null; /* Mutation */',
        'KurvenzufahrtenQuelle', $false),
    @('kontur-ignoriert', 'Geometry/Zellen/Zufahrt.cs',
        'if (zufahrt.Kontur != null) return Geometrie.Enthaelt(zufahrt.Kontur, punkt);',
        '', 'ZufahrtsQuelle', $false),
    @('schwerpunkt-statt-zellgrenze', 'Geometry/Zellen/Zufahrt.cs',
        'if (kurvenkontur)', 'if (false)', 'ZufahrtsQuelle', $true),
    @('anschlussbindung-ignoriert', 'Geometry/ParkingGeometry.Kurvenzufahrten.cs',
        'foreach (var ecke in achse)', 'foreach (var ecke in new Punkt[0])', 'KurvenzielQuelle', $true),
    @('kurvenanschluss-zu-kurz', 'Geometry/ParkingGeometry.Kurvenzufahrten.cs',
        'internal const double KurvenzufahrtsMindestkante = NetzMindestlaenge;',
        'internal const double KurvenzufahrtsMindestkante = 0.375;', 'KurvenzielQuelle', $true),
    @('kantenende-ungerastert', 'Geometry/Zellen/ParkingGeometry.Zellen.cs',
        'along = rasterlaenge;', 'along = entrance.Along;', 'ZellenAdapterQuelle', $true),
    @('endpunktfilter-entfernt', 'Geometry/Zellen/ParkingGeometry.Zellen.cs',
        'if (along == Geometrie.Laenge(delta) || along == math.distance(aWelt, bWelt))',
        'if (true)', 'ZellenAdapterQuelle', $true)
)
foreach ($f in $faelle) {
    $quelle = [IO.File]::ReadAllText((Join-Path $repo $f[1]))
    if (!$quelle.Contains($f[2])) { throw "Mutationsstelle fehlt: $($f[0])" }
    $datei = Join-Path $ablage "$($f[0]).cs"
    [IO.File]::WriteAllText($datei, $quelle.Replace($f[2], $f[3]), [Text.UTF8Encoding]::new($false))
    $testargs = @('--kurvenzufahrten')
    if ($f[5]) { $testargs += '--knoten' }
    $log = Join-Path $ablage "$($f[0]).log"
    dotnet run -c Release --project $projekt "-p:$($f[4])=$datei" -- @testargs *> $log
    $erwartet = if ($f[0] -eq 'kantenende-ungerastert') { ' is not on edge ' }
        elseif ($f[0] -eq 'endpunktfilter-entfernt') { 'Nicht-Endpunkt ausserhalb der Kante wurde akzeptiert.' }
        else { 'Kurvenzufahrt hat einen gebrochenen Materialplan.' }
    if ($LASTEXITCODE -ne 1 -or !([IO.File]::ReadAllText($log).Contains($erwartet))) {
        throw "Mutation nicht durch den erwarteten Testfehler erkannt: $($f[0])"
    }
    Write-Output "$($f[0]): erwarteter Testfehler, Exit 1"
    if ($f[0] -eq 'gerade-tiefe') {
        $geoslog = Join-Path $ablage 'geos-gegenprobe.log'
        python Tests/Kurvenlayout/kurvenzufahrten.py .codex-build/kurven-14/zufahrten.jsonl *> $geoslog
        if ($LASTEXITCODE -ne 1 -or !([IO.File]::ReadAllText($geoslog).Contains('Gassenquerschnitt:'))) {
            throw 'Unabhaengige Querschnittsmessung erkennt den alten Materialfehler nicht.'
        }
        Write-Output 'GEOS-Gegenprobe: alter Materialfehler erkannt, Exit 1'
    }
}
# Der Pflichtlauf legt anschliessend alle produktiven Messdaten erneut an.
