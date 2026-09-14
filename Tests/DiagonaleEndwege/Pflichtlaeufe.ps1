$ErrorActionPreference = 'Stop'
$wurzel = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Set-Location -LiteralPath $wurzel
$ziel = Join-Path $wurzel 'artifacts\diagonale-fusswege'
$projekt = 'Tests\GeometryParity\CSharp\GeometryParity.csproj'
$namen = @('paritaet', 'zoningflaeche', 'zoningnetz', 'zoningrasten',
    'ueberlappung', 'versorgung', 'infokarten', 'flaechenannahme',
    'zufahrtsverlust', 'sonderplaetze', 'zufahrtsquads',
    'fusswegzugang', 'gassenabstand', 'winkelmodus')
foreach ($name in $namen) {
    $argumente = @('run', '-c', 'Release', '--project', $projekt)
    if ($name -ne 'paritaet') { $argumente += @('--', "--$name") }
    if ($name -eq 'sonderplaetze') {
        $argumente += @('-1250.982666,842.118103;-1150.461548,843.283997;-1149.069458,954.494507;-1252.279175,954.003113',
            '0,51.4;1,37.9;2,51.8;3,74.0')
    }
    & dotnet @argumente *> (Join-Path $ziel "$name.txt")
    "$name Exitcode=$LASTEXITCODE" | Tee-Object -FilePath (Join-Path $ziel 'status.txt') -Append
}
& dotnet run -c Release --project Tests\Versorgungsneubau\Versorgungsneubau.csproj *> (Join-Path $ziel 'versorgungsneubau.txt')
"versorgungsneubau Exitcode=$LASTEXITCODE" | Tee-Object -FilePath (Join-Path $ziel 'status.txt') -Append
