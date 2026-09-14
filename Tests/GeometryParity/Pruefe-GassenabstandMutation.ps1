$ErrorActionPreference = 'Stop'
$projekt = Join-Path $PSScriptRoot 'CSharp/GeometryParity.csproj'
$quelle = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../Geometry/Zellen/Layout.cs'))
$logordner = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../artifacts/gassenabstand'))
[IO.Directory]::CreateDirectory($logordner) | Out-Null
$original = [IO.File]::ReadAllBytes($quelle)
$text = [IO.File]::ReadAllText($quelle)
$neu = 'Ringbandplanung.Baender(minY, maxY, einstellungen, randstrassenmittellinie)'
$alt = 'Layoutplanung.Baender(minY, maxY, einstellungen.Buchttiefe, einstellungen.Fahrgassenbreite, einstellungen.Gruenstreifenbreite)'
if (-not $text.Contains($neu)) { throw 'Mutationsstelle nicht gefunden.' }
try {
    # Windows PowerShell 5 behandelt auch erwartete native stderr-Meldungen
    # als Fehlerobjekte; unten entscheiden deshalb Exitcode UND Befundtext.
    $ErrorActionPreference = 'Continue'
    # Echte Quellmutation: den alten, nur global zentrierten Plan wieder einbauen.
    [IO.File]::WriteAllText($quelle, $text.Replace($neu, $alt))
    & dotnet run -c Release --project $projekt -- --gassenabstand *> (Join-Path $logordner 'mutation.log')
    if ($LASTEXITCODE -ne 1 -or -not (Select-String -LiteralPath (Join-Path $logordner 'mutation.log') -Pattern 'Gassenabstand: [1-9][0-9]* Fehler' -Quiet)) {
        throw 'Mutation nicht durch den Abstandsfehler erkannt (oder Build fehlgeschlagen).'
    }
    & dotnet run --no-build -c Release --project $projekt *> (Join-Path $logordner 'paritaet-vorher.log')
    Write-Output "Paritaet mit alter Konstruktion: Exit=$LASTEXITCODE"
    Get-Content (Join-Path $logordner 'paritaet-vorher.log') -Tail 2
}
finally {
    [IO.File]::WriteAllBytes($quelle, $original)
    & dotnet run -c Release --project $projekt -- --gassenabstand *> (Join-Path $logordner 'gassenabstand.log')
    if ($LASTEXITCODE -ne 0) { throw 'Wiederhergestellte Konstruktion besteht den Lauf nicht.' }
}
Write-Output 'Mutation rot, wiederhergestellte Konstruktion gruen.'
