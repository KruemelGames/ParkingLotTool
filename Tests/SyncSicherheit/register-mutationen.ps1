$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$ablage = Join-Path $repo '.codex-build/register-1a/mutationen'
New-Item -ItemType Directory -Force -Path $ablage | Out-Null
$projekt = Join-Path $PSScriptRoot 'SyncSicherheit.csproj'
$faelle = @(
    @('GenerationFehlt', 'Geometry/Auftragsregister.cs', 'RegisterQuelle',
      'id.Generation == Generation && _offen.TryGetValue(id.Id, out auftrag)',
      '_offen.TryGetValue(id.Id, out auftrag)'),
    @('AbschlussbegrenzungFehlt', 'Geometry/Auftragsregister.cs', 'RegisterQuelle',
      'if (!Lies(id, out var a) || a.Phase == Auftragsphase.Abschluss) return false;',
      'if (!Lies(id, out var a)) return false;'),
    @('FreigabeFehlt', 'Tools/ParkingLotFahrwegTausch.Abschluss.cs', 'FahrwegAbschlussQuelle',
      '_stufe = Stufe.Frei;', '// Mutation: Freigabe fehlt.'),
    @('AnnahmeUnbestaetigt', 'Tools/ParkingLotAuftragsSystem.cs', 'RegisterAdapterQuelle',
      'if (a.Phase != Auftragsphase.Vorgemerkt) return true; // bestaetigte Annahme ist verbindlich', 'if (angenommen) return true;'),
    @('FalscheAuftragsAntwort', 'Tools/ParkingLotSync.Auftraege.cs', 'SyncAuftraegeQuelle',
      ' || id != auftrag.Kennung', ''),
    @('StrassenantwortNimmtAktuelleID', 'Tools/ParkingLotBuildingEconomy.Abschluss.cs', 'StrassenAbschlussQuelle',
      'if (!_anschluesse.TryGetValue(lot, out var a) || a.Auftrag != id || !Auftraege.DarfAnwenden(id)) return;',
      'if (!_anschluesse.TryGetValue(lot, out var a)) return; id = a.Auftrag; if (!Auftraege.DarfAnwenden(id)) return;')
)
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
