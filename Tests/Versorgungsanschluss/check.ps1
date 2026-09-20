$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$stage = Join-Path $env:TEMP 'plt-versorgungsanschluss-check'
New-Item -ItemType Directory -Force -Path $stage | Out-Null
$source = [IO.File]::ReadAllText((Join-Path $repo 'Tools/ParkingLotAutoVersorgungValidierung.cs'))
$from = $source.IndexOf('        private bool AvTempAnStrasse(')
$to = $source.IndexOf('        private bool AvKursZusammenhaengend(', $from)
if ($from -lt 0 -or $to -lt 0) { throw 'Produktionsmethode fehlt' }
$method = $source.Substring($from, $to - $from)
$harness = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Harness.txt'))
$managed = [Environment]::GetEnvironmentVariable('CSII_MANAGEDPATH', 'User')
$project = @"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="Program.cs"/><Compile Include="$repo\Geometry\Versorgungsnetz.cs"/><Reference Include="Unity.Mathematics"><HintPath>$managed\Unity.Mathematics.dll</HintPath></Reference></ItemGroup></Project>
"@
[IO.File]::WriteAllText((Join-Path $stage 'Check.csproj'), $project)
foreach ($mutation in @($false, $true)) {
    $code = $method
    if ($mutation) { $code = $code.Replace('foreach (var e in kurs.Anschlussstuecke)', 'foreach (var e in new List<Entity>())') }
    [IO.File]::WriteAllText((Join-Path $stage 'Program.cs'), $harness.Replace('/* PRODUKTION */', $code))
    dotnet run -c Release --project (Join-Path $stage 'Check.csproj')
    $result = $LASTEXITCODE
    if (!$mutation -and $result -ne 0) { throw 'Anschlussregression fehlgeschlagen' }
    if ($mutation -and $result -eq 0) { throw 'Mutation blieb unentdeckt' }
}
Write-Output 'Produktionsmethode gruen; Mutation ohne Zwischenstuecke erkannt.'
