$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location -LiteralPath $repo
$ablage = Join-Path $repo '.codex-build/kurven-14/mutationen'
New-Item -ItemType Directory -Force -Path $ablage | Out-Null
$projekt = 'Tests/Kurvenlayout/Kurvenlayout.csproj'
dotnet run -c Release --project $projekt -- --randlogik *> (Join-Path $ablage 'produktiver-test.log')
if ($LASTEXITCODE -ne 0) { throw 'Produktive Randkurven-Logik fehlgeschlagen.' }
$faelle = @(
    @('clone-flach', 'Geometry/ParkingGeometry.Kurvenrand.cs',
        'Kontrollpunkte = Kontrollpunkte?.ToArray() ??', 'Kontrollpunkte = Kontrollpunkte ??',
        'RanddatenQuelle', '--randlogik', 'Clone trennt die Kurvenformel'),
    @('keine-radialen-buchten', 'Geometry/Zellen/Kurvenrand.cs',
        'if (!s.Randstrassen) return;', 'if (true) return;',
        'RandplanQuelle', '--randlogik', 'Explizite Herkunft erzeugt radiale Buchten'),
    @('lampen-luftlinie', 'Geometry/ParkingGeometry.Kurvenlaternen.cs',
        'double laenge = reihe.Stationen[reihe.Stationen.Length - 1];',
        'double laenge = math.distance(reihe.Standlinie[0], reihe.Standlinie[reihe.Standlinie.Length - 1]);',
        'KurvenlampenQuelle', '--randlogik', 'Laternen messen und verduennen entlang des Bogens'),
    @('lochwinding-ignoriert', 'Geometry/ParkingGeometry.Kurvenflaechen.cs',
        'bool ohneLoecher = schlaufen.All(p => RingflaecheFloat(p) * flaeche > 0)',
        'bool ohneLoecher = true',
        'KurvenflaechenQuelle', '--randlogik', 'Materialexport erhaelt den negativen Lochumlauf'),
    @('kurvenachse-verschoben', 'Geometry/Zellen/ParkingGeometry.Zellen.cs',
        '.OrderByDescending(punkt => festeEcken?.Contains(punkt) == true)',
        '.OrderByDescending(punkt => false)',
        'ZellenAdapterQuelle', '--randlogik', 'Endgueltige Netzachse behaelt die volle Fahrbahnfreiheit'),
    @('kontakt-getrennt', 'Geometry/Zellen/Kurvenrand.cs',
        'Geometrie.Laenge(ecken[0] - vorher[1]) <= ParkingGeometry.ZellenEingangsgitter', 'false',
        'RandplanQuelle', '--randlogik', 'Gemeinsame native Kontaktkante bleibt identisch'),
    @('schmale-graskeile', 'Geometry/Zellen/Kurvenrand.cs',
        'internal const double GraskeilMindestbreite = 1.0;', 'internal const double GraskeilMindestbreite = 0;',
        'RandplanQuelle', '--randlogik', 'Schmale Bordsteinkeile sind Asphalt'),
    @('gras-zur-fahrbahn', 'Geometry/Zellen/Kurvenrand.cs',
        '=> drehung > 0 && breite >= GraskeilMindestbreite;', '=> breite >= GraskeilMindestbreite;',
        'RandplanQuelle', '--randlogik', 'Keil zur Fahrbahn ist immer Asphalt'),
    @('materialstoss-je-bucht', 'Geometry/Zellen/Kurvenrand.Sektoren.cs',
        'var schnitte = geordnet.Where((s, i) =>', 'var schnitte = geordnet.Where((s, i) => true ||',
        'RandsektorenQuelle', '--randlogik', 'Gemeinsamer Belag braucht keine Buchtflaechen'),
    @('materialrollen-getrennt', 'Geometry/Zellen/Vereinigung.cs',
        'erste.GemeinsamerBoden && zweite.GemeinsamerBoden', 'false',
        'VereinigungQuelle', '--randlogik', 'Gemeinsamer Belag braucht keine Buchtflaechen'),
    @('format-version-ignoriert' , 'Geometry/ParkingGeometry.KurvenrandFormat.cs',
        'if (version != Version)', 'if (false)',
        'KurvenrandFormatQuelle', '', 'Unbekannte Kurvenrand-Version abweisen')
)
foreach ($f in $faelle) {
    $quelle = [IO.File]::ReadAllText((Join-Path $repo $f[1]))
    if (!$quelle.Contains($f[2])) { throw "Mutationsstelle fehlt: $($f[0])" }
    $datei = Join-Path $ablage "$($f[0]).cs"
    [IO.File]::WriteAllText($datei, $quelle.Replace($f[2], $f[3]), [Text.UTF8Encoding]::new($false))
    $testprojekt = if ($f[0] -eq 'format-version-ignoriert') { 'Tests/Speicherformate/Speicherformate.csproj' } else { $projekt }
    $log = Join-Path $ablage "$($f[0]).log"
    if ($f[5]) { dotnet run -c Release --project $testprojekt "-p:$($f[4])=$datei" -- $f[5] *> $log }
    else { dotnet run -c Release --project $testprojekt "-p:$($f[4])=$datei" *> $log }
    if ($LASTEXITCODE -ne 1 -or !([IO.File]::ReadAllText($log).Contains($f[6]))) {
        throw "Mutation nicht durch den erwarteten Testfehler erkannt: $($f[0])"
    }
    Write-Output "$($f[0]): erwarteter Testfehler, Exit 1"
}
dotnet run -c Release --project $projekt -- --randlogik *> (Join-Path $ablage 'abschluss.log')
if ($LASTEXITCODE -ne 0) { throw 'Produktiver Abschlusslauf fehlgeschlagen.' }
dotnet run -c Release --project Tests/Speicherformate/Speicherformate.csproj *> (Join-Path $ablage 'speicherformate.log')
if ($LASTEXITCODE -ne 0) { throw 'Produktive Speicherformate fehlgeschlagen.' }
