param([Parameter(Mandatory=$true)][string]$GameDllQuellen)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$ablage = Join-Path $repo '.codex-build/surface-preview-3-20261017'
New-Item -ItemType Directory -Force -Path $ablage | Out-Null
Set-Location -LiteralPath $repo
python Tests/SurfacePreview/permanent_befund.py $GameDllQuellen --report (Join-Path $ablage 'permanent.json')
if ($LASTEXITCODE -ne 0) { throw 'Permanent-Belege fehlen.' }
$projekt = 'Tests/GeometryParity/CSharp/GeometryParity.csproj'
function Kontrolle {
    dotnet run -c Release --project $projekt -- --surface-preview *> (Join-Path $ablage 'muster.log')
    if ($LASTEXITCODE -ne 0) { throw 'Produktive Musterpruefung fehlgeschlagen.' }
}
Kontrolle
$quelle = [IO.File]::ReadAllText((Join-Path $repo 'Geometry/OberflaechenMuster.cs'))
$faelle = @(
    @('Bauphase-ignoriert', '&& !bauphase', '&& true'),
    @('Bauarbeiter-ignoriert', '&& !bauarbeiter', '&& true'),
    @('Mehrfachmuster-bleibt', 'if (muster > 1) return Aktion.Entfernen;', 'if (muster > 1) return Aktion.Behalten;'),
    @('Offene-Ausgabe-ignoriert', '(muster == 1 || ausgabeOffen)', '(muster == 1)'),
    @('Farbmaske-entfaellt', 'return Array.Empty<float2[]>();', 'return new[] { new[] { a,b,c } };'),
    @('Quadrat-entartet', 'public const float Kantenlaenge = 40f;', 'public const float Kantenlaenge = 0f;'),
    @('NaN-Mitte-erlaubt', 'if (!math.all(math.isfinite(mitte)))', 'if (false)')
)
foreach ($fall in $faelle) {
    if (!$quelle.Contains($fall[1])) { throw "Mutationsstelle fehlt: $($fall[0])" }
    $pfad = Join-Path $ablage "$($fall[0]).cs"
    [IO.File]::WriteAllText($pfad,$quelle.Replace($fall[1],$fall[2]))
    $log = Join-Path $ablage "$($fall[0]).log"
    dotnet run -c Release --project $projekt "-p:MusterQuelle=$pfad" -- --surface-preview *> $log
    if ($LASTEXITCODE -ne 1 -or !([IO.File]::ReadAllText($log).Contains('FEHLER Muster:'))) { throw "Gegenprobe nicht erkannt: $($fall[0])" }
    Write-Output "$($fall[0]): erwarteter Testfehler, Exit 1"
}
$marker = [IO.File]::ReadAllText((Join-Path $repo 'Tools/ParkingLotMusterflaeche.cs'))
foreach ($fall in @(@('Marker-Version-fehlt','w.Write(1)','w.Write(0)'), @('Marker-Leser-fehlt','r.Read(out int version);',''))) {
    if (!$marker.Contains($fall[1])) { throw 'Marker-Mutationsstelle fehlt.' }
    $pfad = Join-Path $ablage "$($fall[0]).cs"
    [IO.File]::WriteAllText($pfad,$marker.Replace($fall[1],$fall[2]))
    $log = Join-Path $ablage "$($fall[0]).log"
    dotnet run -c Release --project Tests/Speicherformate/Speicherformate.csproj "-p:MusterMarkerQuelle=$pfad" *> $log
    if ($LASTEXITCODE -ne 1 -or !([IO.File]::ReadAllText($log).Contains('FEHLER:'))) { throw "Marker-Gegenprobe nicht erkannt: $($fall[0])" }
    Write-Output "$($fall[0]): erwarteter Testfehler, Exit 1"
}
Kontrolle
dotnet run -c Release --project Tests/Speicherformate/Speicherformate.csproj *> (Join-Path $ablage 'formate.log')
if ($LASTEXITCODE -ne 0) { throw 'Produktiver Speicherformat-Abschlusslauf fehlgeschlagen.' }
Write-Output 'Muster/Masken/Entscheidungen/Marker: produktiv gruen, 9 Gegenproben erkannt.'
