$ErrorActionPreference = 'Continue'
$ziel = Join-Path $PSScriptRoot '..\artifacts\rzstufen'
$projekt = 'Tests\GeometryParity\CSharp\GeometryParity.csproj'
& dotnet build -c Release $projekt *> (Join-Path $ziel 'build.txt')
if ($LASTEXITCODE -ne 0) { throw 'Build fehlgeschlagen' }
$namen = @('paritaet','zoningflaeche','zoningnetz','zoningrasten','ueberlappung','versorgung','infokarten','flaechenannahme','zufahrtsverlust','sonderplaetze','zufahrtsquads','lformtoggle','fusswegzugang','gassenabstand','endwegbreite','werkzeugzustand','diagonalwege','winkelmodus','randzoningseite','querstrassengitter','geradepunkt','rzstufen')
$zeilen = @()
foreach ($name in $namen) {
 $argumente = @('run','-c','Release','--no-build','--project',$projekt)
 if ($name -ne 'paritaet') { $argumente += @('--',"--$name") }
 if ($name -eq 'sonderplaetze') { $argumente += @('-1250.982666,842.118103;-1150.461548,843.283997;-1149.069458,954.494507;-1252.279175,954.003113','0,51.4;1,37.9;2,51.8;3,74.0') }
 & dotnet @argumente *> (Join-Path $ziel "$name.txt")
 $zeile = "$name Exitcode=$LASTEXITCODE"
 $zeilen += $zeile
 Write-Output $zeile
 $zeilen | Set-Content (Join-Path $ziel 'status.txt')
}
foreach ($name in @('Versorgungsneubau','DiagonaleEndwege')) {
 & dotnet run -c Release --project "Tests\$name\$name.csproj" *> (Join-Path $ziel "$name.txt")
 $zeile = "$name Exitcode=$LASTEXITCODE"
 $zeilen += $zeile
 Write-Output $zeile
 $zeilen | Set-Content (Join-Path $ziel 'status.txt')
}

