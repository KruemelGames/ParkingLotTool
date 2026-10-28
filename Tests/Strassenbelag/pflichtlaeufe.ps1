$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location -LiteralPath $repo
$ablage = Join-Path $repo '.codex-build/issue22'
New-Item -ItemType Directory -Force -Path $ablage | Out-Null
$projekt = 'Tests/Strassenbelag/Strassenbelag.csproj'
dotnet run -c Release --project $projekt *> (Join-Path $ablage 'strassenbelag.log')
if ($LASTEXITCODE -ne 0) { throw 'Produktiver Strassenbelag-Lauf fehlgeschlagen.' }
python Tests/Strassenbelag/messung.py (Join-Path $ablage 'messungen.json') *> (Join-Path $ablage 'materialmessung.log')
$materialCode = $LASTEXITCODE
$quelle = [IO.File]::ReadAllText((Join-Path $repo 'Geometry/Zellen/Strassenbelag.cs'))
$adapter = [IO.File]::ReadAllText((Join-Path $repo 'Geometry/Zellen/ParkingGeometry.Strassenbelag.cs'))
$mutationen = @(
    @{ Name='zu-schmal'; Quelle=$adapter; Alt='Zufahrtsgassenbreite = 8.0'; Neu='Zufahrtsgassenbreite = 5.5'; Prop='StrassenadapterQuelle' },
    @{ Name='kein-eigenes-material'; Quelle=$quelle; Alt='z.Material = Material.Strassenbelag;'; Neu='z.Material = Material.Asphalt;'; Prop='StrassenplanQuelle' },
    @{ Name='gassen-ignoriert'; Quelle=$adapter; Alt='s.Kind == "entrance"'; Neu='s.Kind == "nie"'; Prop='StrassenadapterQuelle' },
    @{ Name='strasse-ohne-fahrbahn'; Quelle=$quelle; Alt='if (arten.Count == 0) return ringe;'; Neu='return ringe;'; Prop='StrassenplanQuelle' },
    @{ Name='bruecke-ignoriert'; Quelle=$quelle; Alt='return !bruecken.Any(b => Geometrie.Enthaelt(b, mitte));'; Neu='return true;'; Prop='StrassenplanQuelle' },
    @{ Name='buchten-nicht-ausgenommen'; Quelle=$quelle; Alt='IstBucht(Zelle z) => z.BuchtId.HasValue || z.Art == Zellart.Bucht;'; Neu='IstBucht(Zelle z) => false;'; Prop='StrassenplanQuelle' }
)
foreach ($m in $mutationen) {
    if (!$m.Quelle.Contains($m.Alt)) { throw "Mutationsstelle fehlt: $($m.Name)" }
    $datei = Join-Path $ablage ($m.Name + '.cs')
    [IO.File]::WriteAllText($datei, $m.Quelle.Replace($m.Alt, $m.Neu), [Text.UTF8Encoding]::new($false))
    dotnet run -c Release --project $projekt "-p:$($m.Prop)=$datei" *> (Join-Path $ablage ($m.Name + '.log'))
    if ($LASTEXITCODE -eq 0) { throw "Gegenprobe faelschlich gruen: $($m.Name)" }
    if (!(Select-String -LiteralPath (Join-Path $ablage ($m.Name + '.log')) -Pattern 'eigener Belag vorhanden|8-m-Alley abgedeckt|Lot201905 erzeugt den Strassenbelag|unsichtbare Strasse|einer Bucht|Bruecke:' -Quiet)) {
        throw "Gegenprobe ohne erwarteten geometrischen Befund: $($m.Name)"
    }
    Write-Output "Gegenprobe $($m.Name): erkannt"
}
# Die historische XOR-Randbildung muss an den echten Zellknoten scheitern.
$vereinigung = [IO.File]::ReadAllText((Join-Path $repo 'Geometry/Zellen/Vereinigung.cs')).Replace("`r`n", "`n")
$altRand = @'
        private static List<GerichteteKante> Randkanten(IReadOnlyList<Zelle> zellen)
        {
            var offen = new Dictionary<KantenSchluessel, GerichteteKante>();
            foreach (var zelle in zellen)
                for (var i = 0; i < zelle.Polygon.Anzahl; i++)
                {
                    var kante = new GerichteteKante(zelle.Polygon.Knoten(i),
                        zelle.Polygon.Knoten(i + 1), zelle.Polygon.Linie(i));
                    if (!offen.Remove(kante.Schluessel)) offen.Add(kante.Schluessel, kante);
                }
            return offen.Values.ToList();
        }
'@
$pattern = '(?s)        private static List<GerichteteKante> Randkanten\(.*?\n        }'
if ([regex]::Matches($vereinigung, $pattern).Count -ne 1) { throw 'Randketten-Mutationsstelle fehlt.' }
$datei = Join-Path $ablage 'xor-rand.cs'
[IO.File]::WriteAllText($datei, [regex]::Replace($vereinigung, $pattern, $altRand.Replace("`r`n", "`n")), [Text.UTF8Encoding]::new($false))
dotnet run -c Release --project $projekt "-p:VereinigungQuelle=$datei" *> (Join-Path $ablage 'xor-rand.log')
if ($LASTEXITCODE -eq 0 -or !(Select-String -LiteralPath (Join-Path $ablage 'xor-rand.log') -Pattern 'Ein Materialrand ist offen' -Quiet)) {
    throw 'XOR-Randgegenprobe ohne erwarteten Topologiefehler.'
}
Write-Output 'Gegenprobe XOR-Randkette: erkannt'
$filter = 'ZieheRinge(randkanten).Where(r => r.Vorzeichenflaeche != 0).ToList()'
if (!$vereinigung.Contains($filter)) { throw 'Nullring-Mutationsstelle fehlt.' }
$datei = Join-Path $ablage 'nullring-mut.cs'
[IO.File]::WriteAllText($datei, $vereinigung.Replace($filter, 'ZieheRinge(randkanten)'), [Text.UTF8Encoding]::new($false))
dotnet run -c Release --project $projekt "-p:VereinigungQuelle=$datei" *> (Join-Path $ablage 'nullring-mut.log')
if ($LASTEXITCODE -eq 0 -or !(Select-String -LiteralPath (Join-Path $ablage 'nullring-mut.log') -Pattern 'A material component has no valid outer ring' -Quiet)) {
    throw 'Nullring-Gegenprobe ohne erwarteten Topologiefehler.'
}
Write-Output 'Gegenprobe exakter Nullring: erkannt'
$plan = [IO.File]::ReadAllText((Join-Path $repo 'Geometry/Zellen/Strassenbelag.cs'))
$materialMutationen = @(
    @('ohne-rastervorgaben', 'var rastersaeume = ParkingGeometry.GruenRastervorgaben(bau.Rahmen, offen.Flaechen, vorplan);', 'var rastersaeume = Array.Empty<Punkt[]>();'),
    @('saumgrenze-fehlt', 'zellen = Teile(zellen, rastersaeume, linien, teiler);', '')
)
foreach ($m in $materialMutationen) {
    if (!$plan.Contains($m[1])) { throw "Material-Mutationsstelle fehlt: $($m[0])" }
    $datei = Join-Path $ablage ($m[0] + '.cs')
    [IO.File]::WriteAllText($datei, $plan.Replace($m[1], $m[2]), [Text.UTF8Encoding]::new($false))
    dotnet run -c Release --project $projekt "-p:StrassenplanQuelle=$datei" *> (Join-Path $ablage ($m[0] + '.log'))
    if ($LASTEXITCODE -ne 0) {
        # Der C#-Lauf misst selbst Flaechensumme, CS2-Annahme und Splitter.
        if (!(Select-String -LiteralPath (Join-Path $ablage ($m[0] + '.log')) -Pattern 'Flaechensumme unveraendert|CS2 nimmt Ring an|Splitterring' -Quiet)) {
            throw "Material-Gegenprobe ohne auswertbare Geometrie: $($m[0])"
        }
        Write-Output "Gegenprobe $($m[0]): erkannt (C#-Flaechenpruefung)"
        continue
    }
    python Tests/Strassenbelag/messung.py (Join-Path $ablage 'messungen.json') *> (Join-Path $ablage ($m[0] + '-messung.log'))
    if ($LASTEXITCODE -eq 0 -or !(Select-String -LiteralPath (Join-Path $ablage ($m[0] + '-messung.log')) -Pattern '; FEHLER' -Quiet)) {
        throw "Material-Gegenprobe nicht erkannt: $($m[0])"
    }
    Write-Output "Gegenprobe $($m[0]): erkannt"
}
# Produktive Messdatei nach den Gegenproben wiederherstellen.
dotnet run -c Release --project $projekt *> (Join-Path $ablage 'strassenbelag.log')
if ($LASTEXITCODE -ne 0) { throw 'Produktiver Abschlusslauf fehlgeschlagen.' }
python Tests/Strassenbelag/messung.py (Join-Path $ablage 'messungen.json') *> (Join-Path $ablage 'materialmessung.log')
if ($LASTEXITCODE -ne 0 -or $materialCode -ne 0) { throw 'Materialdeckung fehlgeschlagen; siehe materialmessung.log.' }
Write-Output 'Strassenbelag einschliesslich unabhaengiger Materialmessung: Exit 0'
