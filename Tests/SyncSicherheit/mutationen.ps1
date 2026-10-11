$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$ablage = Join-Path $repo '.codex-build/issue12/mutationen'
New-Item -ItemType Directory -Force -Path $ablage | Out-Null
$faelle = @(
    @('Strassenachse', 'Geometry/BegleiterAnbindung.cs', 'BegleiterQuelle',
      'mitte = rand + aussen * (tiefe * 4f + 0.5f);', 'mitte = achse;'),
    @('FlaechenNichtMitgezaehlt', 'Geometry/SyncWache.cs', 'WacheQuelle',
      'fahrwege + bestand + flaechen + begleiter', 'fahrwege + bestand + begleiter'),
    @('SpaeteAntwort', 'Geometry/SyncWache.cs', 'WacheQuelle',
      '=> geladen && wartet;', '=> geladen;'),
    @('FehlenderKlonGiltAlsFertig', 'Tools/ParkingLotFahrwegTauschSystem.cs', 'TauschQuelle',
      '=> AlteFahrwege(lot).Count > 0;', '=> Plan(lot)?.Count > 0;')
)
foreach ($fall in $faelle) {
    $quelle = [IO.File]::ReadAllText((Join-Path $repo $fall[1]))
    if (!$quelle.Contains($fall[3])) { throw "Mutation nicht gefunden: $($fall[0])" }
    $pfad = Join-Path $ablage "$($fall[0]).cs"
    [IO.File]::WriteAllText($pfad, $quelle.Replace($fall[3], $fall[4]))
    dotnet run -c Release --project (Join-Path $PSScriptRoot 'SyncSicherheit.csproj') "-p:$($fall[2])=$pfad" *> (Join-Path $ablage "$($fall[0]).log")
    $code = $LASTEXITCODE
    $log = [IO.File]::ReadAllText((Join-Path $ablage "$($fall[0]).log"))
    if ($code -eq 0 -or !$log.Contains('SYNC-SICHERHEIT:')) { throw "Mutation wurde nicht durch einen Test erkannt: $($fall[0])" }
    Write-Output "$($fall[0]) : erwarteter Testfehler, Exit $code"
}
