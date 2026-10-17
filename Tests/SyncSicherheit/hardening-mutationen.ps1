$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$ablage = Join-Path $repo '.codex-build/speichersicherheit-1-2/mutationen'
New-Item -ItemType Directory -Force -Path $ablage | Out-Null
$faelle = @(
    @('AuszuggrenzeFehlt', 'Tools/ParkingLotLogauszug.cs', 'LogQuelle',
      'internal const int MaxBytes = 1024 * 1024;', 'internal const int MaxBytes = 8 * 1024 * 1024;'),
    @('ZeilengrenzeFehlt', 'Tools/ParkingLotLogauszug.cs', 'LogQuelle',
      'internal const int ZeilenBytes = 8 * 1024;', 'internal const int ZeilenBytes = 16 * 1024;'),
    @('FreigabeFehlt', 'Tools/ParkingLotFahrwegTausch.Abschluss.cs', 'FahrwegAbschlussQuelle',
      '_stufe = Stufe.Frei;', '// Mutation: Freigabe fehlt.'),
    @('RueckmeldungImCatch', 'Tools/ParkingLotFahrwegTausch.Abschluss.cs', 'FahrwegAbschlussQuelle',
      'try { ok = Pruefe(out text); }', 'try { ok = Pruefe(out text); Beende(ok, text); return; }'),
    @('StrassenantwortFehlt', 'Tools/ParkingLotBuildingEconomy.Abschluss.cs', 'StrassenAbschlussQuelle',
      'Auftraege.Beende(id, lot, ok);',
      '// Mutation: Rueckmeldung fehlt.'),
    @('IndexAusserhalbCatch', 'Tools/ParkingLotSync.Abarbeiten.cs', 'DispatcherQuelle',
      'var uhr = Stopwatch.StartNew();', 'var uhr = Stopwatch.StartNew(); _index ??= TeileJeLot();')
)
foreach ($fall in $faelle) {
    $quelle = [IO.File]::ReadAllText((Join-Path $repo $fall[1]))
    if (!$quelle.Contains($fall[3])) { throw "Mutation nicht gefunden: $($fall[0])" }
    $pfad = Join-Path $ablage "$($fall[0]).cs"
    [IO.File]::WriteAllText($pfad, $quelle.Replace($fall[3], $fall[4]))
    dotnet run -c Release --project (Join-Path $PSScriptRoot 'SyncSicherheit.csproj') "-p:$($fall[2])=$pfad" *> (Join-Path $ablage "$($fall[0]).log")
    $code = $LASTEXITCODE
    $log = [IO.File]::ReadAllText((Join-Path $ablage "$($fall[0]).log"))
    if ($code -eq 0 -or $log -match 'error (CS|MSB)[0-9]+' -or
        (!$log.Contains('FEHLER:') -and !$log.Contains('Unhandled exception'))) {
        throw "Mutation nicht durch Lauf erkannt: $($fall[0]), Exit $code"
    }
    Write-Output "$($fall[0]): erwarteter Testfehler, Exit $code"
}
