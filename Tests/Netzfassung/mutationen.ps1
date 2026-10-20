$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location -LiteralPath $repo
$ablage = Join-Path $repo '.codex-build/netzfassung/mutationen'
New-Item -ItemType Directory -Force -Path $ablage | Out-Null
$projekt = 'Tests/Netzfassung/Netzfassung.csproj'
$faelle = @(
    @('alte-kursgrenze', 'Geometry/ParkingGeometry.Netzfassung.cs',
        'public const float NetzMindestlaenge = 1f;', 'public const float NetzMindestlaenge = 0.375f;',
        'NetzfassungQuelle', 'Gemeinsamer Vertrag ist ein Meter'),
    @('einfach-weglassen', 'Geometry/ParkingGeometry.Netzfassung.cs',
        'var arten = new Dictionary<Punkt, HashSet<string>>();',
        'return stuecke.Where(s => Laenge(s.A, s.B) >= NetzMindestlaenge).ToList(); var arten = new Dictionary<Punkt, HashSet<string>>();',
        'NetzfassungQuelle', 'Alle Anschlussenden teilen exakt die feste Kurvenecke'),
    @('kurvenecke-ignoriert', 'Geometry/ParkingGeometry.Netzfassung.cs',
        '.OrderByDescending(p => festeEcken?.Contains(p) == true)', '.OrderByDescending(p => false)',
        'NetzfassungQuelle', 'Alle Anschlussenden teilen exakt die feste Kurvenecke'),
    @('neue-kurzstrecke-ignoriert', 'Geometry/ParkingGeometry.Netzfassung.cs',
        'Laenge(bindung[s.A], bindung[s.B]) < NetzMindestlaenge', 'Laenge(s.A, s.B) < NetzMindestlaenge',
        'NetzfassungQuelle', 'Jeder Welt-float-Kurs ist mindestens 1 m lang'),
    @('anschlussende-vergessen', 'Geometry/ParkingGeometry.Netzfassung.cs',
        'A = a, B = b,', 'A = stueck.A, B = b,',
        'NetzfassungQuelle', 'Alle Anschlussenden teilen exakt die feste Kurvenecke'),
    @('weltmass-ignoriert', 'Geometry/ParkingGeometry.Netzfassung.cs',
        'return Math.Sqrt(x * x + y * y);', 'return Geometrie.Laenge(b - a);',
        'NetzfassungQuelle', 'Jeder Welt-float-Kurs ist mindestens 1 m lang'),
    @('welt-rundung-ignoriert', 'Geometry/ParkingGeometry.Netzfassung.cs',
        'if (Geometrie.Laenge(b - a) >= NetzMindestlaenge)', 'if (false)',
        'NetzfassungQuelle', 'Float-Rundung darf einen baubaren Zugang nicht verschlucken'),
    @('produktionspfad-umgangen', 'Geometry/Zellen/ParkingGeometry.Zellen.cs',
        'return ZellenBindeNetzknoten(knoten, rahmen, festeEcken);', 'return knoten;',
        'ZellenAdapterQuelle', 'Produktive Netzfassung bindet die historischen Kurzstuecke')
)
foreach ($f in $faelle) {
    $quelle = [IO.File]::ReadAllText((Join-Path $repo $f[1]))
    if (!$quelle.Contains($f[2])) { throw "Mutationsstelle fehlt: $($f[0])" }
    $datei = Join-Path $ablage "$($f[0]).cs"
    [IO.File]::WriteAllText($datei, $quelle.Replace($f[2], $f[3]), [Text.UTF8Encoding]::new($false))
    $log = Join-Path $ablage "$($f[0]).log"
    dotnet run -c Release --project $projekt "-p:$($f[4])=$datei" *> $log
    if ($LASTEXITCODE -ne 1 -or !([IO.File]::ReadAllText($log).Contains($f[5]))) {
        throw "Mutation nicht durch den erwarteten Testfehler erkannt: $($f[0])"
    }
    Write-Output "$($f[0]): erwarteter Testfehler, Exit 1"
}
dotnet run -c Release --project $projekt *> (Join-Path $ablage 'abschluss.log')
if ($LASTEXITCODE -ne 0) { throw 'Produktive Netzfassung fehlgeschlagen.' }
