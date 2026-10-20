$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location -LiteralPath $repo
$ablage=Join-Path $repo '.codex-build/kurven-7'
New-Item -ItemType Directory -Force -Path $ablage | Out-Null
python Tests/Kurven/fangvergleich.py
if($LASTEXITCODE -ne 0){throw 'Fangvergleich fehlgeschlagen.'}
$projekt='Tests/Kurven/Kurven.csproj'
dotnet run -c Release --project $projekt *> (Join-Path $ablage 'kurven.log')
if($LASTEXITCODE -ne 0){throw 'Produktive Kurvenpruefung fehlgeschlagen.'}
Get-Content (Join-Path $ablage 'kurven.log')
$quelle=[IO.File]::ReadAllText((Join-Path $repo 'Geometry/Strassenkurve.cs')).Replace("`r`n","`n")
$huelleAlt='Math.Max(Abstand(c.A,a,d),Math.Max(Abstand(c.B,a,d),'+"`n"+'                Math.Max(Abstand(c.C,a,d),Abstand(c.D,a,d))))'
$faelle=@(
 @('nur-mittelpunkt',$huelleAlt,'Abstand(c.Punkt(.5),a,d)','S braucht Kontrollhuelle'),
 @('sehnenabweichung-nur-mittelpunkt','double t = i / (double)proben;','double t = .5;','Unabhaengige Kurvenabdeckung'),
 @('seitenfilter-weg','if(seite!=b.Seite)return Grund.Seitenwechsel;','if(false)return Grund.Seitenwechsel;','Seitenwechsel'),
 @('hoehenfilter-weg','if(k.Hoehe)return Grund.Hoehe;','if(false)return Grund.Hoehe;','Rampenkante'),
 @('grenze-ignoriert','if (n > budget) { e.Grund = Grund.Budget; return e; }','if (false) { e.Grund = Grund.Budget; return e; }','Budget: nie Teilbogen'),
 @('radiusquelle-weg',('if(c.HatQuelle)'+"`n"+'            {'+"`n"+'                von='), ('if(false)'+"`n"+'            {'+"`n"+'                von='), 'Weltverschiebung')
)
foreach($fall in $faelle){
 if(!$quelle.Contains($fall[1])){throw "Mutationsstelle fehlt: $($fall[0])"}
 $pfad=Join-Path $ablage "$($fall[0]).cs"
 [IO.File]::WriteAllText($pfad,$quelle.Replace($fall[1],$fall[2]),[Text.UTF8Encoding]::new($false))
 $log=Join-Path $ablage "$($fall[0]).log"
 dotnet run -c Release --project $projekt "-p:KurvenQuelle=$pfad" *> $log
 if($LASTEXITCODE -eq 0){throw "Mutation blieb gruen: $($fall[0])"}
 if(!([IO.File]::ReadAllText($log).Contains($fall[3]))){throw "Mutation scheiterte am falschen Grund: $($fall[0])"}
 Write-Output "$($fall[0]) erkannt"
}
$netz=[IO.File]::ReadAllText((Join-Path $repo 'Geometry/StrassenkurvenNetz.cs')).Replace("`r`n","`n")
$logquelle=[IO.File]::ReadAllText((Join-Path $repo 'Geometry/StrassenkurvenLog.cs')).Replace("`r`n","`n")
$netzAlt='if(!kanten.TryGetValue(jetzt,out var k))break;'+"`n"+'                    if(jetzt==b)return true;'
$weitere=@(
 @('ziel-nur-id',$netz,'NetzQuelle',$netzAlt,('if(jetzt==b)return true;'+"`n"+'                    if(!kanten.TryGetValue(jetzt,out var k))break;'),'Zielgeometrie muss vor Verbindungsfreigabe gelesen sein'),
 @('zweite-knotenhaelfte-fehlt',$netz,'NetzQuelle','ergaenze(k);','if(id!=b.Kante)ergaenze(k);','Zweite native Knotenhaelfte muss im Angebot sein'),
 @('gemeinsames-loggedaechtnis',$logquelle,'LogQuelle','var stand=(e.Von.Kante','quelle=0;var stand=(e.Von.Kante','Wechselnde feste/Mausquelle'),
 @('kurz-ist-eng',$quelle,'KurvenQuelle','e.Grund = Grund.ZuKurz;','e.Grund = Grund.ZuEng;','R148-Kurzabschnitt ist ZuKurz'),
 @('lueckenmessung-fehlt',$quelle,'KurvenQuelle','if(bruecke==Grund.Luecke)messung=MesseLuecke(n,von,ziel);','if(false)messung=MesseLuecke(n,von,ziel);','Fehlende native Haelfte liefert messbare Luecke')
)
foreach($fall in $weitere){
 if(!$fall[1].Contains($fall[3])){throw "Mutationsstelle fehlt: $($fall[0])"}
 $pfad=Join-Path $ablage "$($fall[0]).cs"
 [IO.File]::WriteAllText($pfad,$fall[1].Replace($fall[3],$fall[4]),[Text.UTF8Encoding]::new($false))
 $log=Join-Path $ablage "$($fall[0]).log"
 dotnet run -c Release --project $projekt "-p:$($fall[2])=$pfad" *> $log
 if($LASTEXITCODE -eq 0){throw "Mutation blieb gruen: $($fall[0])"}
 if(!([IO.File]::ReadAllText($log).Contains($fall[5]))){throw "Mutation scheiterte am falschen Grund: $($fall[0])"}
 Write-Output "$($fall[0]) erkannt"
}
$gruppe=[IO.File]::ReadAllText((Join-Path $repo 'Geometry/Kurvengruppe.cs')).Replace("`r`n","`n")
$gruppentests=@(
 @('gruppenhulle-nur-mitte',('return Math.Max(Abstand(A, a, b), Math.Max(Abstand(B, a, b),'+"`n"+'                    Math.Max(Abstand(C, a, b), Abstand(D, a, b))));'), 'return Abstand(Punkt(.5), a, b);', 'Kontrollhuelle des symmetrischen S'),
 @('bogenlaengen-vertauscht','s[i] = grenzen[ort.Index] + abschnitte[ort.Index].Bogenlaenge(ort.T);','s[i] = grenzen[grenzen.Length - 1] - grenzen[ort.Index] - abschnitte[ort.Index].Bogenlaenge(ort.T);','Zettel: Stationen steigen'),
 @('gegenrichtung-falsch','p[i] = Punkte[p.Length - 1 - i];','p[i] = Punkte[i];','Orientierte Abschnitte passen zu Endpunkten'),
 @('bogenrueckweg-falsch','bogen = Bogenlaengen[i] + lage.Along / sehne * (Bogenlaengen[i + 1] - Bogenlaengen[i]);','bogen = Laenge - (Bogenlaengen[i] + lage.Along / sehne * (Bogenlaengen[i + 1] - Bogenlaengen[i]));','Polygonsegment und Along zu unabhaengiger Bogenposition')
)
foreach($fall in $gruppentests){
 if(!$gruppe.Contains($fall[1])){throw "Mutationsstelle fehlt: $($fall[0])"}
 $pfad=Join-Path $ablage "$($fall[0]).cs"
 [IO.File]::WriteAllText($pfad,$gruppe.Replace($fall[1],$fall[2]),[Text.UTF8Encoding]::new($false))
 $log=Join-Path $ablage "$($fall[0]).log"
 dotnet run -c Release --project $projekt "-p:GruppenQuelle=$pfad" *> $log
 if($LASTEXITCODE -eq 0){throw "Mutation blieb gruen: $($fall[0])"}
 if(!([IO.File]::ReadAllText($log).Contains($fall[3]))){throw "Mutation scheiterte am falschen Grund: $($fall[0])"}
 Write-Output "$($fall[0]) erkannt"
}
dotnet run -c Release --project $projekt *> (Join-Path $ablage 'kurven.log')
if($LASTEXITCODE -ne 0){throw 'Produktiver Abschlusslauf fehlgeschlagen.'}
Write-Output 'Kurven produktiv gruen; 15 Mutationen und Fanggegenprobe erkannt.'
