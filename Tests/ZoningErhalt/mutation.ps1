$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$stage = Join-Path $env:TEMP 'plt-zoningerhalt-mutation'
New-Item -ItemType Directory -Force -Path $stage | Out-Null
$project = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'ZoningErhalt.csproj')).Replace('<Compile Include="../../Geometry/ZoningErhalt.cs" Link="ZoningErhalt.cs" />', '')
[IO.File]::WriteAllText((Join-Path $stage 'Test.csproj'), $project)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Program.cs') -Destination $stage -Force
$source = [IO.File]::ReadAllText((Join-Path $repo 'Geometry/ZoningErhalt.cs'))
foreach ($mutation in @($false, $true)) {
    $code = $source
    if ($mutation) { $code = $code.Replace('math.distancesq(a[0], b[0]) > .000001f', 'false').Replace('math.distancesq(a[1], b[1]) > .000001f', 'false') }
    [IO.File]::WriteAllText((Join-Path $stage 'ZoningErhalt.cs'), $code)
    dotnet run -c Release --project (Join-Path $stage 'Test.csproj')
    if (($LASTEXITCODE -eq 0) -eq $mutation) { throw "Unerwartetes Ergebnis Mutation=$mutation" }
}
Write-Output 'Original bestanden; fehlende Geometriepruefung erkannt.'
