$ErrorActionPreference = 'Stop'
$ziel = Join-Path $PSScriptRoot '..\artifacts\zoningkanten-20260921'
$projekt = 'Tests\GeometryParity\CSharp\GeometryParity.csproj'
$namen = @('paritaet','zoningflaeche','zoningnetz','zoningrasten','ueberlappung','versorgung','infokarten','flaechenannahme','zufahrtsverlust','sonderplaetze','zufahrtsquads','lformtoggle','fusswegzugang','gassenabstand','endwegbreite','werkzeugzustand','diagonalwege','winkelmodus','randzoningseite','querstrassengitter','geradepunkt','rzstufen','ortsunabhaengig','rzanschluss','entarteteringe')
$status = @()
foreach ($name in $namen) {
 $argumente = @('run','-c','Release','--no-build','--project',$projekt)
 if ($name -ne 'paritaet') { $argumente += @('--',"--$name") }
 if ($name -eq 'sonderplaetze') { $argumente += @('-1250.982666,842.118103;-1150.461548,843.283997;-1149.069458,954.494507;-1252.279175,954.003113','0,51.4;1,37.9;2,51.8;3,74.0') }
 & dotnet @argumente *> (Join-Path $ziel "$name.txt")
 $status += "$name Exitcode=$LASTEXITCODE"
 $status | Set-Content (Join-Path $ziel 'status.txt')
 Write-Output $status[-1]
}
foreach ($name in @('Versorgungsneubau','DiagonaleEndwege')) {
 & dotnet run -c Release --project "Tests\$name\$name.csproj" *> (Join-Path $ziel "$name.txt")
 $status += "$name Exitcode=$LASTEXITCODE"
 $status | Set-Content (Join-Path $ziel 'status.txt')
 Write-Output $status[-1]
}

if (@($status | Where-Object { $_ -notmatch "Exitcode=0$" }).Count -ne 0) { throw "Pflichtlauf fehlgeschlagen; siehe status.txt" }
