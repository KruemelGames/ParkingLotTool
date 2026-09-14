$ErrorActionPreference = 'Stop'
$projekt = Join-Path $PSScriptRoot '..\GeometryParity\CSharp\GeometryParity.csproj'
$ausgabe = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\artifacts\geradepunkt-pflicht'))
New-Item -ItemType Directory -Force -Path $ausgabe | Out-Null
dotnet build -c Release $projekt *> (Join-Path $ausgabe 'build.txt')
if ($LASTEXITCODE -ne 0) { throw 'Test-Build fehlgeschlagen' }
$modi = @('paritaet','zoningflaeche','zoningnetz','zoningrasten','ueberlappung','versorgung','infokarten','flaechenannahme','zufahrtsverlust','sonderplaetze','zufahrtsquads','lformtoggle','fusswegzugang','gassenabstand','endwegbreite','werkzeugzustand','diagonalwege','winkelmodus','randzoningseite','querstrassengitter')
$ergebnisse = @()
foreach ($modus in $modi) {
    $argumente = @('run','-c','Release','--no-build','--project',$projekt)
    if ($modus -ne 'paritaet') { $argumente += @('--',"--$modus") }
    if ($modus -eq 'sonderplaetze') {
        $argumente += @('-1250.982666,842.118103;-1150.461548,843.283997;-1149.069458,954.494507;-1252.279175,954.003113','0,51.4;1,37.9;2,51.8;3,74.0')
    }
    $uhr = [Diagnostics.Stopwatch]::StartNew()
    & dotnet @argumente *> (Join-Path $ausgabe "$modus.txt")
    $code = $LASTEXITCODE
    $zeile = "$modus exit=$code Sekunden=$([Math]::Round($uhr.Elapsed.TotalSeconds,2))"
    Write-Output $zeile
    $ergebnisse += $zeile
    $ergebnisse | Set-Content (Join-Path $ausgabe 'ergebnisse.txt')
}
foreach ($name in @('Versorgungsneubau','DiagonaleEndwege')) {
    $weiteres = Join-Path $PSScriptRoot "..\$name\$name.csproj"
    & dotnet run -c Release --project $weiteres *> (Join-Path $ausgabe "$name.txt")
    $zeile = "$name exit=$LASTEXITCODE"
    Write-Output $zeile
    $ergebnisse += $zeile
    $ergebnisse | Set-Content (Join-Path $ausgabe 'ergebnisse.txt')
}
