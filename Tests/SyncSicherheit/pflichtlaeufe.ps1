$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$ablage = Join-Path $repo '.codex-build/issue12'
New-Item -ItemType Directory -Force -Path $ablage | Out-Null
Set-Location -LiteralPath $repo
$projekt = 'Tests/GeometryParity/CSharp/GeometryParity.csproj'
dotnet build -c Release $projekt *> (Join-Path $ablage 'geometry-build.log')
if ($LASTEXITCODE -ne 0) { throw 'Geometrietest kompiliert nicht.' }
$namen = @('', 'migrationen', 'statusmeldungen', 'sprache', 'laternen', 'zoningflaeche', 'zoningnetz',
    'zoningrasten', 'ueberlappung', 'versorgung', 'infokarten', 'flaechenannahme',
    'zufahrtsverlust', 'sonderplaetze', 'zufahrtsquads', 'lformtoggle', 'fusswegzugang',
    'gassenabstand', 'endwegbreite', 'werkzeugzustand', 'diagonalwege', 'winkelmodus',
    'randzoningseite', 'querstrassengitter', 'geradepunkt', 'rzstufen', 'ortsunabhaengig',
    'rzanschluss', 'entarteteringe')
$ergebnisse = [Collections.Generic.List[object]]::new()
foreach ($name in $namen) {
    $argumente = @('run', '-c', 'Release', '--no-build', '--project', $projekt)
    if ($name -ne '') { $argumente += @('--', "--$name") }
    if ($name -eq 'sonderplaetze') {
        $argumente += @('-1250.982666,842.118103;-1150.461548,843.283997;-1149.069458,954.494507;-1252.279175,954.003113',
            '0,51.4;1,37.9;2,51.8;3,74.0')
    }
    $label = if ($name -eq '') { 'standard' } else { $name }
    dotnet @argumente *> (Join-Path $ablage "$label.log")
    $code = $LASTEXITCODE
    $ergebnisse.Add([pscustomobject]@{ Lauf = $label; Exit = $code })
    $ergebnisse | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ablage 'ergebnisse.json')
    Write-Output "$label : Exit $code"
}
foreach ($name in @('Versorgungsneubau', 'DiagonaleEndwege', 'SyncSicherheit', 'Fahrprefabs')) {
    dotnet run -c Release --project "Tests/$name/$name.csproj" *> (Join-Path $ablage "$name.log")
    $code = $LASTEXITCODE
    $ergebnisse.Add([pscustomobject]@{ Lauf = $name; Exit = $code })
    $ergebnisse | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ablage 'ergebnisse.json')
    Write-Output "$name : Exit $code"
}
if (@($ergebnisse | Where-Object Exit -ne 0).Count -ne 0) { exit 1 }
