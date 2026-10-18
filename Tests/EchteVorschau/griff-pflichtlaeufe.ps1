$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location -LiteralPath $repo
$ablage=Join-Path $repo '.codex-build/echte-vorschau-7-20261018'
New-Item -ItemType Directory -Force -Path $ablage | Out-Null
$projekt='Tests/EchteVorschau/EchteVorschau.csproj'
$quelle=[IO.File]::ReadAllText((Join-Path $repo 'Geometry/Vorschaugriff.cs'))
$faelle=@(
 @('Unsichtbar-greifbar','=> sichtbar && welt','=> true && welt'),
 @('UI-Klick-uebernommen','sichtbar && welt','sichtbar && true'),
 @('Anderer-Zug-ignoriert','&& !andererZug','&& true'),
 @('Hit-Radius-falsch','<= radius*radius','<= radius*radius*2'),
 @('Ohne-Linksklick','getroffen && links','getroffen && true'),
 @('Bau-blockiert','&& !bauen','&& true'),
 @('Abbruch-startet','&& !abbrechen &&','&& true &&'),
 @('Groesse-springt','GroesseBeimVerschiebenBehalten = true','GroesseBeimVerschiebenBehalten = false'),
 @('Maus-versatz-fehlt','_versatz=Ziel-maus','_versatz=default'),
 @('Zug-aendert-nichts','Ziel=maus+_versatz','Ziel=Ziel'),
 @('Abbruch-ignoriert','if (!abbrechen) Bereich=','if (true) Bereich='),
 @('Beliebiger-Innenpunkt','if (d<abstand)','if (false)'),
 @('Keine-neue-Generation','Stand++;',';'),
 @('Reset-laesst-Mitte','Bereich=null; Zieht=false','Zieht=false'),
 @('Jede-Form-setzt-zurueck','&& !Enthaelt(lot,Bereich.Value.Mitte)','&& true')
)
foreach($f in $faelle) {
 if(!$quelle.Contains($f[1])){throw "Griff-Mutationsstelle fehlt: $($f[0])"}
 $pfad=Join-Path $ablage "$($f[0]).cs"
 [IO.File]::WriteAllText($pfad,$quelle.Replace($f[1],$f[2]))
 $log=Join-Path $ablage "$($f[0]).log"
 dotnet run -c Release --project $projekt "-p:GriffQuelle=$pfad" *> $log
 if($LASTEXITCODE -ne 1 -or !([IO.File]::ReadAllText($log).Contains('FEHLER EchteVorschau:'))){throw "Griff-Gegenprobe nicht erkannt: $($f[0])"}
 Write-Output "$($f[0]): erwarteter Testfehler, Exit 1"
}
# Produktionsintegration: Rohmaus, Vorrang, ESC-Verbrauch und eingefrorene Maske.
$tool=[IO.File]::ReadAllText((Join-Path $repo 'Tools/ParkingLotToolSystem.cs'))
$griff=[IO.File]::ReadAllText((Join-Path $repo 'Tools/ParkingLotMustergriff.cs'))
$preview=[IO.File]::ReadAllText((Join-Path $repo 'Tools/ParkingLotSurfacePreview.cs'))
if($tool.IndexOf('if (HandleMustergriff(') -gt $tool.IndexOf('if (HandleAusrichtWahl(') -or $tool.IndexOf('if (HandleMustergriff(') -gt $tool.IndexOf('UpdateEntranceInteraction();')){throw 'Griffprioritaet verloren.'}
foreach($s in @('Mouse.current?.leftButton.wasPressedThisFrame','!Mouse.current.leftButton.isPressed','_musterAbbruchBild=UnityEngine.Time.frameCount','_panelBuildRequest || BuildRequested()')) {
 if(!$griff.Contains($s)){throw "Griffintegration fehlt: $s"}
}
foreach($s in @('if (_musterGriff.Zieht && !mitte.HasValue) return;','_musterAbbruchBild!=UnityEngine.Time.frameCount','_musterGriff.Stand')) {
 if(!$preview.Contains($s)){throw "Vorschauintegration fehlt: $s"}
}
dotnet run -c Release --project $projekt *> (Join-Path $ablage 'griff-kontrolle.log')
if($LASTEXITCODE -ne 0){throw 'Produktiver Griff-Abschlusslauf fehlgeschlagen.'}
Write-Output 'Verschiebegriff produktiv gruen; 15 Gegenproben erkannt; Integrationsbelege erfuellt.'
