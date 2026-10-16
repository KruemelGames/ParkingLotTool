$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$ablage = Join-Path $repo '.codex-build/speichersicherheit-3-4/mutationen'
New-Item -ItemType Directory -Force -Path $ablage | Out-Null
$projekt = Join-Path $PSScriptRoot 'Speicherformate.csproj'
$faelle = @(
    @('ReceiptLiestZuWeit', 'Tools/ParkingLotBuildReceipt.cs', 'ReceiptQuelle',
      'if (Version >= 2)', 'if (Version >= 1)'),
    @('RueckwegLiestNamenInV1', 'Tools/ParkingLotRueckwegDaten.cs', 'RueckwegQuelle',
      'if (version >= 2)', 'if (version >= 1)'),
    @('AnschlussAltAlsNeu', 'Tools/ParkingLotGespeicherterZwischenbau.cs', 'ZwischenbauQuelle',
      'bool neu = erstes == Format2;', 'bool neu = true;'),
    @('StufeAAltMitVersionsfeld', 'Tools/ParkingLotGespeicherterZwischenbau.cs', 'ZwischenbauQuelle',
      'public void Deserialize<TReader>(TReader r) where TReader : IReader => r.Read(out Prefab);',
      'public void Deserialize<TReader>(TReader r) where TReader : IReader { r.Read(out int version); r.Read(out Prefab); }'),
    @('StufeANeuSpeichertEntity', 'Tools/ParkingLotStufeAPrefabID.cs', 'StufeAQuelle',
      'w.Write(PrefabName.ToString());', 'w.Write(Prefab); w.Write(PrefabName.ToString());'),
    @('StufeAOhneIDVorrang', 'Tools/ParkingLotStufeAPrefabID.cs', 'StufeAQuelle',
      'if (!neu.PrefabName.IsEmpty) return ParkingLotPrefabname.Aufloesen(em, neu.PrefabName);',
      '// Mutation: nur die alte Entity verwenden.'),
    @('StufeACacheBleibt', 'Tools/ParkingLotStufeAPrefabID.cs', 'StufeAQuelle',
      'Prefab = Entity.Null;', '// Mutation: Cache bleibt belegt.'),
    @('StufeAMarkerBleibt', 'Tools/ParkingLotStufeAPrefabID.cs', 'StufeAQuelle',
      'if (em.HasComponent<ParkingLotStufeAPrefabID>(lot)) em.RemoveComponent<ParkingLotStufeAPrefabID>(lot);',
      '// Mutation: neuer Marker bleibt.'),
    @('RueckwegsperreFehlt', 'Geometry/EditFreigabe.cs', 'EditQuelle',
      'if (wiederherstellung) return EditSperre.Wiederherstellung;',
      '// Mutation: Rueckwegsperre fehlt.'),
    @('BauzettelloseFlaecheUnsichtbar', 'Tools/ParkingLotEditSection.Freigabe.cs', 'EditSectionQuelle',
      '&& prefab != null && prefab.name == ParkingLotToolSystem.LotOwnerPrefabName;',
      '&& false;'),
    @('BearbeitenTrotzSperre', 'Tools/ParkingLotEditSection.Freigabe.cs', 'EditSectionQuelle',
      '_bearbeitbar = sperre == EditSperre.Keine;', '_bearbeitbar = true;')
)
# Der erste und der letzte Lauf verwenden wieder die produktiven Quellen.
dotnet run -c Release --project $projekt *> (Join-Path $ablage 'vorher.log')
if ($LASTEXITCODE -ne 0) { throw 'Produktiver Kontrolllauf vor Mutationen fehlgeschlagen.' }
try {
    foreach ($fall in $faelle) {
        $quelle = [IO.File]::ReadAllText((Join-Path $repo $fall[1]))
        if (!$quelle.Contains($fall[3])) { throw "Mutation nicht gefunden: $($fall[0])" }
        $pfad = Join-Path $ablage "$($fall[0]).cs"
        [IO.File]::WriteAllText($pfad, $quelle.Replace($fall[3], $fall[4]))
        $protokoll = Join-Path $ablage "$($fall[0]).log"
        dotnet run -c Release --project $projekt "-p:$($fall[2])=$pfad" *> $protokoll
        $code = $LASTEXITCODE
        $log = [IO.File]::ReadAllText($protokoll)
        if ($code -ne 1 -or $log -match 'error (CS|MSB)[0-9]+' -or !$log.Contains('FEHLER:')) {
            throw "Mutation nicht durch Testlauf erkannt: $($fall[0]), Exit $code"
        }
        Write-Output "$($fall[0]): erwarteter Testfehler, Exit $code"
    }
}
finally {
    dotnet run -c Release --project $projekt *> (Join-Path $ablage 'nachher.log')
    if ($LASTEXITCODE -ne 0) { throw 'Produktiver Kontrolllauf nach Mutationen fehlgeschlagen.' }
}
