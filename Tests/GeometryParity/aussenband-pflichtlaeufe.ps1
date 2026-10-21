$ErrorActionPreference = 'Stop'
$projekt = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Push-Location $projekt
try {
    $ausgabe = Join-Path $projekt ('.codex-build/aussenband-pflicht/' + (Get-Date -Format 'yyyyMMdd-HHmmssfff'))
    New-Item -ItemType Directory -Path $ausgabe -Force | Out-Null
    $original = Join-Path $projekt 'Tests/GeometryParity/CSharp/GeometryParity.csproj'
    & dotnet run -c Release --project $original -- --aussenband *> (Join-Path $ausgabe 'normal.log')
    if ($LASTEXITCODE -ne 0) { throw "Aussenband-Pflichtlauf fehlgeschlagen: $ausgabe/normal.log" }

    # Mutationen laufen in einer Kopie, niemals im produktiven Arbeitsbaum.
    $kopie = Join-Path $ausgabe 'kopie'
    New-Item -ItemType Directory -Path $kopie -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projekt 'Geometry') -Destination $kopie -Recurse
    $tests = Join-Path $kopie 'Tests/GeometryParity/CSharp'
    New-Item -ItemType Directory -Path $tests -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $projekt 'Tests/GeometryParity/CSharp') -File |
        Where-Object { $_.Extension -in '.cs', '.csproj' } |
        ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $tests }
    $vergleich = Join-Path $kopie 'Tests/Zufahrtsvorschau'
    New-Item -ItemType Directory -Path $vergleich -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projekt 'Tests/Zufahrtsvorschau/Vergleich.cs') -Destination $vergleich
    $mutationen = @(
        @('Netzkappung fehlt', 'Geometry/Zellen/ParkingGeometry.Zellen.cs',
            'reste = ZoningOhneRechteck(reste, band);', ';'),
        @('Buchtzugang fehlt', 'Geometry/Zellen/Layout.cs',
            'if (buchtId.HasValue && buchtzugang.TryGetValue', 'if (false && buchtId.HasValue && buchtzugang.TryGetValue'),
        @('Ringlose Wegpruefung fehlt', 'Geometry/Zellen/RinglosSchnitte.cs',
            'z.Aussenbauland().Any(band => Ueberlappt(w.Ecken, band))', 'false'),
        @('Zufahrtspruefung fehlt', 'Geometry/Zellen/RinglosSchnitte.cs',
            'z.Aussenbauland().Any(band => Ueberlappt(ecken, band))', 'false')
    )
    foreach ($m in $mutationen) {
        $datei = Join-Path $kopie $m[1]
        $bytes = [IO.File]::ReadAllBytes($datei)
        $text = [IO.File]::ReadAllText($datei)
        if (($text.Split(@($m[2]), [StringSplitOptions]::None).Count - 1) -ne 1) {
            throw ('Mutationsstelle nicht eindeutig: ' + $m[0])
        }
        $log = Join-Path $ausgabe ($m[0].Replace(' ', '-') + '.log')
        try {
            [IO.File]::WriteAllText($datei, $text.Replace($m[2], $m[3]), [Text.UTF8Encoding]::new($false))
            & dotnet run -c Release --project (Join-Path $tests 'GeometryParity.csproj') -- --aussenband *> $log
            if ($LASTEXITCODE -ne 1 -or (Get-Content -LiteralPath $log -Raw) -notmatch 'AUSSENBAND: .* [1-9][0-9]* Fehler') {
                throw ('Mutation nicht fachlich erkannt: ' + $m[0] + ' (' + $log + ')')
            }
            Write-Output ('Mutation erkannt: ' + $m[0])
        }
        finally { [IO.File]::WriteAllBytes($datei, $bytes) }
    }
    Write-Output ('Aussenband: Pflichtlauf und 4 Mutationen bestanden. Logs: ' + $ausgabe)
}
finally { Pop-Location }
