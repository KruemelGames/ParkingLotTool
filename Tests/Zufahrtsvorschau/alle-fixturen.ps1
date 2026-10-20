$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location -LiteralPath $repo
$ablage = Join-Path $repo '.codex-build/kurven-8/alle-fixturen'
New-Item -ItemType Directory -Force -Path $ablage | Out-Null
$quelle = [IO.File]::ReadAllText((Join-Path $repo 'Geometry/ParkingGeometry.Types.cs')).Replace("`r`n", "`n")
$alt = '        public static ParkingLayout Build(float2[] site, LayoutSettings settings)' + "`n" +
    '            => Build(site, settings, null);'
$neu = @'
        private static readonly VorschauVorbereitung _vergleichsspeicher = new VorschauVorbereitung();
        public static int VerglicheneVorschauen;
        public static ParkingLayout Build(float2[] site, LayoutSettings settings)
        {
            var kopie = settings?.Clone();
            var voll = Build(site, settings, null);
            string soll = global::Vergleich.Abdruck(voll);
            Console.WriteLine("BAUABDRUCK:" + soll);
            // Jeder vorhandene Fall prueft auch einen warmen Cachetreffer,
            // nicht nur zwei identische Erstberechnungen.
            for (int i = 0; i < 2; i++)
            {
                var ist = Build(site, kopie.Clone(), _vergleichsspeicher);
                if (global::Vergleich.Abdruck(ist) != soll)
                    throw new InvalidOperationException("Vorbereiteter Bau weicht vom Vollbau ab.");
            }
            VerglicheneVorschauen++;
            return voll;
        }
'@
if (!$quelle.Contains($alt)) { throw 'Build-Vergleichsstelle fehlt.' }
$pfad = Join-Path $ablage 'Types-Vergleich.cs'
[IO.File]::WriteAllText($pfad, $quelle.Replace($alt, $neu), [Text.UTF8Encoding]::new($false))
$projekt = 'Tests/Zufahrtsvorschau/Zufahrtsvorschau.csproj'
dotnet build -c Release $projekt "-p:TypesQuelle=$pfad" *> (Join-Path $ablage 'build.log')
if ($LASTEXITCODE -ne 0) { throw 'Fixturenvergleich kompiliert nicht.' }
$namen = @('', 'migrationen', 'statusmeldungen', 'sprache', 'laternen', 'zoningflaeche', 'zoningnetz',
    'zoningrasten', 'ueberlappung', 'versorgung', 'infokarten', 'flaechenannahme',
    'zufahrtsverlust', 'sonderplaetze', 'zufahrtsquads', 'lformtoggle', 'fusswegzugang',
    'gassenabstand', 'endwegbreite', 'werkzeugzustand', 'diagonalwege', 'winkelmodus',
    'randzoningseite', 'querstrassengitter', 'geradepunkt', 'rzstufen', 'ortsunabhaengig',
    'rzanschluss', 'entarteteringe')
function PruefeModus($name, $suffix) {
    $argumente = @('run', '-c', 'Release', '--no-build', '--project', $projekt, '--', '--paritaet')
    if ($name -ne '') { $argumente += "--$name" }
    if ($name -eq 'sonderplaetze') {
        $argumente += @('-1250.982666,842.118103;-1150.461548,843.283997;-1149.069458,954.494507;-1252.279175,954.003113',
            '0,51.4;1,37.9;2,51.8;3,74.0')
    }
    $label = if ($name -eq '') { 'standard' } else { $name }
    dotnet @argumente *> (Join-Path $ablage "$label$suffix.log")
    if ($LASTEXITCODE -ne 0) { throw "Fixturenvergleich fehlgeschlagen: $label$suffix" }
}
foreach ($name in $namen) {
    PruefeModus $name ''
    $label = if ($name -eq '') { 'standard' } else { $name }
    Write-Output "$label mit warmem Cache: Exit 0"
}
# Zweite Kompilierung gegen den Stand vor den Allokationsaenderungen:
# alle erfolgreichen Pflichtfixturen muessen denselben Fingerabdruck liefern.
$referenz = $quelle.Replace($alt, $neu).Replace('i < 2; i++', 'i < 0; i++')
$referenzPfad = Join-Path $ablage 'Types-Referenz.cs'
[IO.File]::WriteAllText($referenzPfad, $referenz, [Text.UTF8Encoding]::new($false))
$teiler = [IO.File]::ReadAllText((Join-Path $repo 'Geometry/Zellen/Zerlegung.cs'))
if (!$teiler.Contains('return polygon.Einzeln();')) { throw 'Referenzstelle Einzelarray fehlt.' }
$teiler = $teiler.Replace('return polygon.Einzeln();', 'return new[] { polygon };')
$teilerPfad = Join-Path $ablage 'Teiler-Referenz.cs'
[IO.File]::WriteAllText($teilerPfad, $teiler, [Text.UTF8Encoding]::new($false))
$geometrie = [IO.File]::ReadAllText((Join-Path $repo 'Geometry/Zellen/Geometrie.cs')).Replace("`r`n", "`n")
$direkt = @'
            for (var i = 0; i < polygon.Anzahl; i++)
            {
                var punkt = polygon.Ecken[i].Knoten.Punkt;
'@
$iterator = @'
            foreach (var punkt in polygon.Punkte)
            {
'@
$direkt = $direkt.Replace("`r`n", "`n"); $iterator = $iterator.Replace("`r`n", "`n")
if (!$geometrie.Contains($direkt)) { throw 'Referenzstelle Mittelpunkt fehlt.' }
$geometriePfad = Join-Path $ablage 'Geometrie-Referenz.cs'
[IO.File]::WriteAllText($geometriePfad, $geometrie.Replace($direkt, $iterator), [Text.UTF8Encoding]::new($false))
dotnet build -c Release $projekt "-p:TypesQuelle=$referenzPfad" "-p:TeilerQuelle=$teilerPfad" "-p:GeometrieQuelle=$geometriePfad" *> (Join-Path $ablage 'referenz-build.log')
if ($LASTEXITCODE -ne 0) { throw 'Referenzbau kompiliert nicht.' }
$gesamt = 0
foreach ($name in $namen) {
    PruefeModus $name '-referenz'
    $label = if ($name -eq '') { 'standard' } else { $name }
    $ist = @([IO.File]::ReadAllLines((Join-Path $ablage "$label.log")) | Where-Object { $_.StartsWith('BAUABDRUCK:') })
    $soll = @([IO.File]::ReadAllLines((Join-Path $ablage "$label-referenz.log")) | Where-Object { $_.StartsWith('BAUABDRUCK:') })
    if ($ist.Count -ne $soll.Count -or ($ist -join "`n") -cne ($soll -join "`n")) { throw "Referenzabweichung: $label" }
    $gesamt += $ist.Count
}
if ($gesamt -lt 145) { throw "Zu wenige Bauvergleiche: $gesamt" }
Write-Output "$gesamt Layouts auch bitgleich gegen den bisherigen Schnittpfad."
# Wieder die echten Quellen kompilieren; nur der Prueflauf oben benutzt die Kopie.
dotnet build -c Release $projekt *> (Join-Path $ablage 'abschluss-build.log')
if ($LASTEXITCODE -ne 0) { throw 'Produktiver Abschlussbuild fehlgeschlagen.' }
