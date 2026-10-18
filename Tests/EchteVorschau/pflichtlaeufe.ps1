param([Parameter(Mandatory=$true)][string]$GameDllQuellen,[switch]$NurGezielt)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location -LiteralPath $repo
$ablage=Join-Path $repo '.codex-build/echte-vorschau-5-20261018'
New-Item -ItemType Directory -Force -Path $ablage | Out-Null
python Tests/EchteVorschau/spielcode_befund.py $GameDllQuellen
if($LASTEXITCODE -ne 0){throw 'Spielcodebelege fehlen.'}
$projekt='Tests/EchteVorschau/EchteVorschau.csproj'
function Kontrolle {
 dotnet run -c Release --project $projekt *> (Join-Path $ablage 'test.log')
 if($LASTEXITCODE -ne 0){throw 'Produktiver Vorschautest fehlgeschlagen.'}
}
Kontrolle
$quelle=[IO.File]::ReadAllText((Join-Path $repo 'Geometry/EchteVorschau.cs'))
$faelle=@(
 @('An-ignoriert','an ? art :','an ? aktuell :'),
 @('Aus-fremder-Art-loescht','aktuell == art ? EchteVorschauArt.Aus : aktuell','true ? EchteVorschauArt.Aus : aktuell'),
 @('Altmigration-fehlt',': alterSchalter ? EchteVorschauArt.Flaechen',': false ? EchteVorschauArt.Flaechen'),
 @('Neuer-Wert-ignoriert','wert.HasValue ?','false ?'),
 @('Drehung-ignoriert','b.Lokal(punkt) - b.Mitte','punkt - b.Mitte'),
 @('Rand-ausgeschlossen','<= b.Halbseite + .001f','< b.Halbseite - .01f'),
 @('Budget-verdoppelt','MaxObjekte = 256','MaxObjekte = 512'),
 @('Teilsatz-ausgegeben','if (indizes.Count <= MaxObjekte)','if (true)'),
 @('Verkleinerung-fehlt','b.Halbseite*.7f','b.Halbseite'),
 @('Alle-Altersmeshes','zustand == alter+1','true')
)
function Mutation($name,$text,$alt,$neu,$eigenschaft) {
 if(!$text.Contains($alt)){throw "Mutationsstelle fehlt: $name"}
 $pfad=Join-Path $ablage "$name.cs"
 [IO.File]::WriteAllText($pfad,$text.Replace($alt,$neu))
 $log=Join-Path $ablage "$name.log"
 dotnet run -c Release --project $projekt "-p:${eigenschaft}=$pfad" *> $log
 if($LASTEXITCODE -ne 1 -or !([IO.File]::ReadAllText($log).Contains('FEHLER EchteVorschau:'))){throw "Gegenprobe nicht erkannt: $name"}
 Write-Output "$name : erwarteter Testfehler, Exit 1"
}
foreach($f in $faelle){Mutation $f[0] $quelle $f[1] $f[2] 'EchteQuelle'}
$quelle=[IO.File]::ReadAllText((Join-Path $repo 'Tools/ParkingLotMusterobjektArchetyp.cs'))
foreach($f in @(
 @('Marker-fehlt','c.Add(ComponentType.ReadWrite<ParkingLotMusterflaeche>());',';'),
 @('Emissive-bleibt','c.Remove(ComponentType.ReadWrite<Emissive>());',';'),
 @('LightState-bleibt','c.Remove(ComponentType.ReadWrite<LightState>());',';'),
 @('Darstellung-fehlt','base.GetArchetypeComponents(c);',';')
)){Mutation $f[0] $quelle $f[1] $f[2] 'MusterArchetypQuelle'}
$quelle=[IO.File]::ReadAllText((Join-Path $repo 'Tools/ParkingLotEchteVorschauEinstellung.cs'))
foreach($f in @(
 @('Alter-Jsonname-verloren','JsonProperty("SurfacePreview")','JsonProperty("Verloren")'),
 @('Neuer-Jsonname-verloren','JsonProperty("EchteVorschau",','JsonProperty("Verloren",'),
 @('Zweiter-Speicherwert','[JsonIgnore]','[JsonProperty]')
)){Mutation $f[0] $quelle $f[1] $f[2] 'VorschauEinstellungQuelle'}
Kontrolle
Write-Output 'Echte Vorschau produktiv gruen; 17 Mutationen erkannt.'
& pwsh -NoProfile -File Tests/EchteVorschau/griff-pflichtlaeufe.ps1
if($LASTEXITCODE -ne 0){throw 'Verschiebegriff-Gegenproben fehlgeschlagen.'}
if(!$NurGezielt) {
 & pwsh -NoProfile -File Tests/SurfacePreview/pflichtlaeufe.ps1 -GameDllQuellen $GameDllQuellen
 if($LASTEXITCODE -ne 0){throw 'Bestehende Oberflaechenpruefung fehlgeschlagen.'}
 & pwsh -NoProfile -File Tests/SyncSicherheit/register-pflichtlaeufe.ps1
 if($LASTEXITCODE -ne 0){throw 'Bestehende Pflichtlaeufe fehlgeschlagen.'}
}
