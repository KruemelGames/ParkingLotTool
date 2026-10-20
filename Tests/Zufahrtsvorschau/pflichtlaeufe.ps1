$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location -LiteralPath $repo
$ablage = Join-Path $repo '.codex-build/kurven-8'
New-Item -ItemType Directory -Force -Path $ablage | Out-Null
$projekt = 'Tests/Zufahrtsvorschau/Zufahrtsvorschau.csproj'
dotnet run -c Release --project $projekt *> (Join-Path $ablage 'zufahrtsvorschau.log')
if ($LASTEXITCODE -ne 0) { throw 'Produktiver Zufahrtsvergleich fehlgeschlagen.' }
Get-Content (Join-Path $ablage 'zufahrtsvorschau.log') -Tail 2
# Unabhaengiger Vergleich mit den bisherigen drei Teilerschleifen. Die
# Cache-Verdrahtung bleibt, der Vergleich prueft auch den ungekuerzten Vollbau.
$teiler = [IO.File]::ReadAllText((Join-Path $repo 'Geometry/Zellen/Zerlegung.cs')).Replace("`r`n", "`n")
$alt = '            if (!_beobachtungenJeLinie.TryGetValue(schnittlinie.Id, out var beobachtungen)) return;' + "`n" +
    '            var einsaetze = beobachtungen'
$neu = '            var einsaetze = Schnittbeobachtungen' + "`n" +
    '                .Where(wert => wert.Schnittlinie.Id == schnittlinie.Id)'
if (!$teiler.Contains($alt)) { throw 'Referenzstelle Nachbarkanten fehlt.' }
$teiler = $teiler.Replace($alt, $neu)
$alt = '            for (var i = 0; i < polygon.Anzahl; i++)' + "`n" +
    '            {' + "`n" + '                var seite = schnittlinie.Seite(polygon.Ecken[i].Knoten.Punkt);'
$neu = '            foreach (var punkt in polygon.Punkte)' + "`n" +
    '            {' + "`n" + '                var seite = schnittlinie.Seite(punkt);'
if (!$teiler.Contains($alt)) { throw 'Referenzstelle Polygonpunkte fehlt.' }
$teiler = $teiler.Replace($alt, $neu)
$modell = [IO.File]::ReadAllText((Join-Path $repo 'Geometry/Zellen/Modell.cs'))
if (!$modell.Contains('var normalenlaenge = _normalenlaenge;')) { throw 'Referenzstelle Normalenlaenge fehlt.' }
$modell = $modell.Replace('var normalenlaenge = _normalenlaenge;', 'var normalenlaenge = Math.Sqrt(A * A + B * B);')
# Auch die Allokationsaenderung dieser Runde zuruecknehmen. Nur die
# Ressourcenpruefung wird im Referenzlauf ausgelassen, nie die Layoutpruefung.
if (!$teiler.Contains('return polygon.Einzeln();')) { throw 'Referenzstelle Einzelpolygon fehlt.' }
$teiler = $teiler.Replace('return polygon.Einzeln();', 'return new[] { polygon };')
$geometrie = [IO.File]::ReadAllText((Join-Path $repo 'Geometry/Zellen/Geometrie.cs')).Replace("`r`n", "`n")
$alt = @'
            for (var i = 0; i < polygon.Anzahl; i++)
            {
                var punkt = polygon.Ecken[i].Knoten.Punkt;
                x += punkt.X;
                y += punkt.Y;
            }
'@
$neu = @'
            foreach (var punkt in polygon.Punkte)
            {
                x += punkt.X;
                y += punkt.Y;
            }
'@
$alt = $alt.Replace("`r`n", "`n"); $neu = $neu.Replace("`r`n", "`n")
if (!$geometrie.Contains($alt)) { throw 'Referenzstelle Mittelwert fehlt.' }
$geometrie = $geometrie.Replace($alt, $neu)
$geometriePfad = Join-Path $ablage 'referenz-geometrie.cs'
[IO.File]::WriteAllText($geometriePfad, $geometrie, [Text.UTF8Encoding]::new($false))
$teilerPfad = Join-Path $ablage 'referenz-teiler.cs'
$modellPfad = Join-Path $ablage 'referenz-modell.cs'
[IO.File]::WriteAllText($teilerPfad, $teiler, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($modellPfad, $modell, [Text.UTF8Encoding]::new($false))
dotnet run -c Release --project $projekt "-p:TeilerQuelle=$teilerPfad" "-p:ModellQuelle=$modellPfad" "-p:GeometrieQuelle=$geometriePfad" -- --referenz *> (Join-Path $ablage 'referenz.log')
if ($LASTEXITCODE -ne 0) { throw 'Referenzlauf fehlgeschlagen.' }
function Fingerabdruecke($pfad) {
    $ergebnis = @{}
    foreach ($zeile in [IO.File]::ReadAllLines($pfad)) {
        if ($zeile -match '^(.+?): voll .+? ms, vorbereitet .+? ms, (.+)$') { $ergebnis[$Matches[1]] = $Matches[2] }
    }
    return $ergebnis
}
$ist = Fingerabdruecke (Join-Path $ablage 'zufahrtsvorschau.log')
$soll = Fingerabdruecke (Join-Path $ablage 'referenz.log')
if ($ist.Count -ne 57 -or $soll.Count -ne 57) { throw 'Referenzvergleich unvollstaendig.' }
foreach ($name in @('Lot201905/False/0', 'Lot201905/False/1', 'Lot201905/False/2')) {
    if (!$ist.ContainsKey($name) -or !$soll.ContainsKey($name)) { throw "Neue Kurvenzufahrts-Fixtur fehlt: $name" }
}
foreach ($name in $ist.Keys) { if ($ist[$name] -ne $soll[$name]) { throw "Referenzabweichung: $name" } }
Write-Output '57 Layouts bitgleich mit bisherigem Teiler und mit/ohne Vorbereitung.'
$cs2 = [IO.File]::ReadAllText((Join-Path $repo 'Geometry/Cs2Triangulierung.cs'))
$cache = [IO.File]::ReadAllText((Join-Path $repo 'Geometry/VorschauVorbereitung.cs'))
$teilerAktuell = [IO.File]::ReadAllText((Join-Path $repo 'Geometry/Zellen/Zerlegung.cs'))
$mutationen = @(
    @('einzelarrays-je-anfrage', $teilerAktuell, 'return polygon.Einzeln();', 'return new[] { polygon };', 'TeilerQuelle', '--cache', 'Warme Schnitt-Nichttreffer ohne Allokationen'),
    @('mittelwert-iterator', [IO.File]::ReadAllText((Join-Path $repo 'Geometry/Zellen/Geometrie.cs')).Replace("`r`n", "`n"), $alt, $neu, 'GeometrieQuelle', '--cache', 'Warme Schnitt-Nichttreffer ohne Allokationen'),
    @('alte-knoten-ignoriert', $cs2, 'if (knoten.Length > 20)', 'if (false)', 'Cs2Quelle', '--ringe', 'Echte CS2-Annahme Grass/3'),
    @('koordinaten-ignoriert', $cache, 'gleich && i < punkte.Length', 'false && i < punkte.Length', 'VorbereitungQuelle', '--cache', 'Cache prueft Koordinaten'),
    @('zaehler-nicht-fortgesetzt', $cache, 'knoten.Fortsetzen(_knoten);', '', 'VorbereitungQuelle', '--cache', 'Cache erhaelt Knoten- und Liniennummern'),
    @('versatzabstand-ignoriert', $cache, 'TryGetValue(schluessel, out var innen)', 'TryGetValue(4607182418800017408L, out var innen)', 'VorbereitungQuelle', '--cache', 'Cache unterscheidet Versatzabstaende'),
    @('innenrand-nicht-kopiert', $cache, 'return innen.ToArray();', 'return innen;', 'VorbereitungQuelle', '--cache', 'Cache gibt keine veraenderbaren Innenrandlisten heraus')
)
foreach ($m in $mutationen) {
    if (!$m[1].Contains($m[2])) { throw "Mutationsstelle fehlt: $($m[0])" }
    $pfad = Join-Path $ablage "$($m[0]).cs"
    [IO.File]::WriteAllText($pfad, $m[1].Replace($m[2], $m[3]), [Text.UTF8Encoding]::new($false))
    $log = Join-Path $ablage "$($m[0]).log"
    dotnet run -c Release --project $projekt "-p:$($m[4])=$pfad" -- $m[5] *> $log
    if ($LASTEXITCODE -eq 0) { throw "Mutation blieb gruen: $($m[0])" }
    if (!([IO.File]::ReadAllText($log).Contains($m[6]))) { throw "Falscher Fehler bei Mutation: $($m[0])" }
    Write-Output "$($m[0]) erkannt"
}
dotnet run -c Release --project $projekt -- --ringe *> (Join-Path $ablage 'ringe-abschluss.log')
if ($LASTEXITCODE -ne 0) { throw 'Produktiver Abschlusslauf fehlgeschlagen.' }
Write-Output 'Zufahrtsvergleich, echte CS2-Ringe und 7 Mutationen gruen.'
& pwsh -NoProfile -File Tests/Zufahrtsvorschau/alle-fixturen.ps1
if ($LASTEXITCODE -ne 0) { throw 'Vergleich aller Pflichtfixturen fehlgeschlagen.' }
