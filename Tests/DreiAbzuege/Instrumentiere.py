import pathlib, shutil
p=pathlib.Path('Tests/DreiAbzuege/Kopie'); p.mkdir(exist_ok=True); (p/'Zellen').mkdir(exist_ok=True)
for f in pathlib.Path('Geometry').glob('*.cs'): shutil.copyfile(f,p/f.name)
for f in pathlib.Path('Geometry/Zellen').glob('*.cs'): shutil.copyfile(f,p/'Zellen'/f.name)
f=p/'Zellen/Ringlos.cs'; s=f.read_text(encoding='utf-8-sig'); s=s.replace('p.PlaneGemeinsameEnden(e.Buchtbreite);','''Console.WriteLine("INNEN "+string.Join("; ", innen.Select((v,i)=>$"{i}: {v.X:F6},{v.Y:F6}")));
            foreach(var g in p.Gassen) Console.WriteLine($"ROH y={g.A.Y:F6} x={g.A.X:F6}..{g.B.X:F6} K={g.KanteA}/{g.KanteB}");
            p.PlaneGemeinsameEnden(e.Buchtbreite);
            foreach(var g in p.Gassen) Console.WriteLine($"ANGEGLICHEN y={g.A.Y:F6} x={g.A.X:F6}..{g.B.X:F6} K={g.KanteA}/{g.KanteB}");'''); f.write_text(s,encoding='utf-8')
f=p/'Zellen/RinglosEndwege.cs'; s=f.read_text(encoding='utf-8-sig'); s=s.replace('if (!Frei(streifen, innen, zoning))','''Console.WriteLine($"SCHRAEG K={gruppe.Key} rechts={rechtsSeite} m={m:F9} c={c:F9} frei={Frei(streifen,innen,zoning)}");
                    foreach(var v in streifen.Ecken) Console.WriteLine($" ECK {v.X:F6},{v.Y:F6} innen={Geometrie.EnthaeltOderRand(innen,v)} Abstand={Enumerable.Range(0,innen.Count).Min(i=>Geometrie.AbstandPunktStrecke(v,innen[i],innen[(i+1)%innen.Count])):F9}");
                    if (!Frei(streifen, innen, zoning))'''); s=s.replace('if (!Frei(uNeu, innen, zoning) || !Frei(vNeu, innen, zoning)) continue;','''Console.WriteLine($"GEHRUNG {i}/{j} Schnitt={schnitt.X:F6},{schnitt.Y:F6} frei={Frei(uNeu,innen,zoning)}/{Frei(vNeu,innen,zoning)} Gassen="+string.Join(",",Gassen.Select((g,k)=>(g,k)).Where(z=>Ueberlappt(uNeu.Ecken,z.g.Ecken)||Ueberlappt(vNeu.Ecken,z.g.Ecken)).Select(z=>z.k)));
                if (!Frei(uNeu, innen, zoning) || !Frei(vNeu, innen, zoning)) continue;'''); f.write_text(s,encoding='utf-8')

f=p/'Zellen/Ringlos.cs'; s=f.read_text(encoding='utf-8'); s=s.replace('p.PlaneSchraegeEndwege(innen, zoning);', 'if(Environment.GetEnvironmentVariable("PLT_DREI_ENTWURF")=="1") p.PlaneDreiEntwurf(innen,zoning); else p.PlaneSchraegeEndwege(innen, zoning);'); f.write_text(s,encoding='utf-8')
shutil.copyfile('Tests/DreiAbzuege/Entwurf.cs',p/'Zellen/DreiEntwurf.cs')
