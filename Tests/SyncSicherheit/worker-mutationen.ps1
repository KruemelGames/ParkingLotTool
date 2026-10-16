$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$ablage = Join-Path $repo '.codex-build/register-rest/mutationen'
New-Item -ItemType Directory -Force -Path $ablage | Out-Null
$projekt = Join-Path $PSScriptRoot 'SyncSicherheit.csproj'
$faelle = @(
    @('BestandskennungFehlt', 'Tools/ParkingLotBestandsTausch.Auftrag.cs', 'BestandsAuftragQuelle',
      '_kennung != id || ', ''),
    @('AltesTaskErgebnis', 'Tools/ParkingLotBestandsTausch.Auftrag.cs', 'BestandsAuftragQuelle',
      '_kennung != v.Kennung || !Auftraege.DarfAnwenden(v.Kennung)', 'false'),
    @('BestandsfreigabeFehlt', 'Tools/ParkingLotBestandsTausch.Abschluss.cs', 'BestandsAbschlussQuelle',
      '_stufe = Stufe.Frei;', '// Mutation: keine Freigabe.'),
    @('FlaechenfreigabeFehlt', 'Tools/ParkingLotFlaechenSync.Auftrag.cs', 'FlaechenAuftragQuelle',
      '_lauf = null;', '// Mutation: keine Freigabe.'),
    @('FlaechengenerationFehlt', 'Tools/ParkingLotFlaechenSync.Auftrag.cs', 'FlaechenAuftragQuelle',
      '_lauf != null && Auftraege.DarfAnwenden(_lauf.Kennung)', '_lauf != null'),
    @('AltesGateWirkt', 'Tools/ParkingLotHintergrund.Auftrag.cs', 'HintergrundAuftragQuelle',
      'if (!Aktuell(a)) return 0;', '// Mutation: alter Gate-Rueckruf wirkt.'),
    @('WiederholungBeendetAuftrag', 'Tools/ParkingLotHintergrund.Auftrag.cs', 'HintergrundAuftragQuelle',
      'if (!a.Fortsetzen)', 'if (true)'),
    @('HintergrundfreigabeFehlt', 'Tools/ParkingLotHintergrund.Auftrag.cs', 'HintergrundAuftragQuelle',
      '_aktiv = null;', '// Mutation: keine Freigabe.'),
    @('RueckwegBestaetigtMigration', 'Tools/ParkingLotHintergrund.Auftrag.cs', 'HintergrundAuftragQuelle',
      ('bool ok = freigegeben && a.Erfolg && (!a.NurRueckweg' + [Environment]::NewLine +
      '                        || Auftraege.Register.Lies(a.Kennung, out var daten) && daten.Grund != Auftragsgrund.Migration);'),
      'bool ok = freigegeben && a.Erfolg;')
)
dotnet run -c Release --project $projekt *> (Join-Path $ablage 'vorher.log')
if ($LASTEXITCODE -ne 0) { throw 'Produktiver Kontrolllauf vor Mutationen fehlgeschlagen.' }
try {
    foreach ($fall in $faelle) {
        $quelle = [IO.File]::ReadAllText((Join-Path $repo $fall[1])).Replace("`r`n", "`n").Replace("`n", [Environment]::NewLine)
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
