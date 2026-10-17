$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$ablage = Join-Path $repo '.codex-build/speicher9-11/mutationen'
New-Item -ItemType Directory -Force -Path $ablage | Out-Null
$faelle = @(
 @('SeedFehlt','Geometry/Erzeugungsherkunft.cs','ErzeugungsregelQuelle','seed == erwartet && ','','SyncSicherheit'),
 @('AlteKanteErlaubt','Geometry/Erzeugungsherkunft.cs','ErzeugungsregelQuelle',' && !alt','', 'SyncSicherheit'),
 @('BusNurNachLage','Tools/ParkingLotErzeugungsSystem.Herkunft.cs','HerkunftQuelle',
   'treffer.Count != 1 || !Passt(treffer[0], q, prefab, lot, carrier)','treffer.Count != 1','SyncSicherheit'),
 @('LeitungOhneQuelle','Tools/ParkingLotErzeugungsSystem.Herkunft.cs','HerkunftQuelle',
   'if (!Passt(e, q, prefab, lot, carrier)) return false;','','SyncSicherheit'),
 @('ApplyOhneZuordnung','Tools/ParkingLotVersorgungsleitung.Apply.cs','VersorgungsApplyQuelle',
   'return t.m_Original == Entity.Null && ','return ','SyncSicherheit'),
 @('AlleTeilpflichtenQuittiert','Tools/ParkingLotNebenarbeitDaten.cs','NebenMarkerQuelle',
   'm.Offen &= ~pflicht;','m.Offen = NebenarbeitPflicht.Keine;','SyncSicherheit'),
 @('AlteQuittungWirkt','Tools/ParkingLotNebenarbeitDaten.cs','NebenMarkerQuelle',
   '=> bereit && aktuell == gespeichert;','=> bereit;','SyncSicherheit'),
 @('NebenarbeitNimmtRueckweg','Tools/ParkingLotHintergrund.Abschluss.cs','HintergrundAbschlussAdapterQuelle',
   'if (a.NurNebenarbeit)','if (false)','SyncSicherheit'),
 @('TeilnachweisGiltAlsVoll','Tools/ParkingLotVersorgungsnachweis.cs','VersorgungsnachweisQuelle',
   'if (voll)','if (true)','SyncSicherheit'),
 @('AlteVersorgungBleibt','Tools/ParkingLotAutoVersorgung.Generation.cs','VersorgungsGenerationQuelle',
   '_avGeneration != register.Generation','false','SyncSicherheit'),
 @('BushaltNurAnzahl','Geometry/Erzeugungsherkunft.cs','ErzeugungsregelQuelle',
   'var zuordnung = new int[vorhanden.Count];','return geplant.Count <= vorhanden.Count; var zuordnung = new int[vorhanden.Count];','SyncSicherheit'),
 @('EditNachweisFehlt','Tools/ParkingLotVersorgungsnachweis.cs','VersorgungsnachweisQuelle',
   'if (ganz) uebernehmen.AddRange(g);','if (false) uebernehmen.AddRange(g);','SyncSicherheit'),
 @('DiagnoseWirft','Tools/ParkingLotNebenarbeitDaten.cs','NebenMarkerQuelle',
   'try { aktion(); } catch (Exception) { }','aktion();','SyncSicherheit'),
 @('V1LiestV2Felder','Tools/ParkingLotNebenarbeitDaten.cs','NebenMarkerQuelle',
   'if (version >= 2)','if (version >= 1)','Speicherformate'),
 @('PrefabIDFehlt','Tools/ParkingLotErzeugungsauftrag.cs','ErzeugungsdatenQuelle',
   'w.Write(Prefabname.ToString());','w.Write(Seed);','Speicherformate'),
 @('NachweiskanteFehlt','Tools/ParkingLotVersorgungsnachweis.Daten.cs','VersorgungsdatenQuelle',
   'w.Write(Kante);','w.Write(Entity.Null);','Speicherformate')
)
foreach ($name in @('SyncSicherheit','Speicherformate')) {
 dotnet run -c Release --project (Join-Path $repo "Tests/$name/$name.csproj") *> (Join-Path $ablage "$name-vorher.log")
 if ($LASTEXITCODE -ne 0) { throw "Kontrolllauf $name fehlgeschlagen." }
}
try {
 foreach ($f in $faelle) {
  $quelle=[IO.File]::ReadAllText((Join-Path $repo $f[1]))
  if (!$quelle.Contains($f[3])) { throw "Mutationsziel fehlt: $($f[0])" }
  $pfad=Join-Path $ablage "$($f[0]).cs"
  [IO.File]::WriteAllText($pfad,$quelle.Replace($f[3],$f[4]))
  $logpfad=Join-Path $ablage "$($f[0]).log"
  dotnet run -c Release --project (Join-Path $repo "Tests/$($f[5])/$($f[5]).csproj") "-p:$($f[2])=$pfad" *> $logpfad
  $code=$LASTEXITCODE; $log=[IO.File]::ReadAllText($logpfad)
  if ($code -ne 1 -or $log -match 'error (CS|MSB)[0-9]+' -or !$log.Contains('FEHLER:')) {
   throw "Mutation nicht durch Testaussage erkannt: $($f[0]), Exit $code"
  }
  Write-Output "$($f[0]): erwarteter Testfehler, Exit $code"
 }
}
finally {
 foreach ($name in @('SyncSicherheit','Speicherformate')) {
  dotnet run -c Release --project (Join-Path $repo "Tests/$name/$name.csproj") *> (Join-Path $ablage "$name-nachher.log")
  if ($LASTEXITCODE -ne 0) { throw "Produktiver Abschluss $name fehlgeschlagen." }
 }
}
