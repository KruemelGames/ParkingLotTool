$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$projekt = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Push-Location $projekt
try {
    $faelle = @(
        @('flaechen', 'Geometry\Zellen\Layout.cs',
            'if (!durchgehendeEndstreifen)', 'if (true)'),
        @('fusswegsuche', 'Geometry\FusswegAnschluss.cs',
            '? 0u : original', '? original : original')
    )
    foreach ($fall in $faelle) {
        $datei = Join-Path $projekt $fall[1]
        $original = [IO.File]::ReadAllText($datei)
        if (-not $original.Contains($fall[2])) { throw "Mutationsstelle fehlt: $($fall[1])" }
        try {
            [IO.File]::WriteAllText($datei, $original.Replace($fall[2], $fall[3]))
            & dotnet run -c Release --project Tests\GeometryParity\CSharp\GeometryParity.csproj -- --flaechenannahme *> "artifacts\split-mutation-$($fall[0]).txt"
            $code = $LASTEXITCODE
            Write-Output "$($fall[0]): Mutation Exit $code (erwartet 1)"
            if ($code -ne 1) { throw "Mutation $($fall[0]) wurde nicht erkannt" }
        }
        finally { [IO.File]::WriteAllText($datei, $original) }
    }
}
finally {
    # Auch bei unerwartetem Mutationsausgang keine mutierte Test-DLL stehenlassen.
    & dotnet run -c Release --project Tests\GeometryParity\CSharp\GeometryParity.csproj -- --flaechenannahme *> artifacts\split-final-flaechenannahme.txt
    $wiederhergestellt = $LASTEXITCODE
    Pop-Location
    if ($wiederhergestellt -ne 0) { throw "Wiederhergestellter Stand: Exit $wiederhergestellt" }
    Write-Output 'Wiederhergestellter Stand: Exit 0'
}
