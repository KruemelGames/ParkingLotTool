using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using ParkingLotTool.Geometry;
using ParkingLotTool.Geometry.Zellen;
using Unity.Mathematics;

// Folgenmessung: Produktionscode und bestehender roter Solltest bleiben unveraendert.
internal static partial class GeradepunktFolgen
{
    static readonly float2[] Mit = {
        new(-1037.02026f,118.689781f), new(-1170.38806f,123.265007f),
        new(-1173.37708f,36.167f), new(-1239.602f,38.439003f),
        new(-1242.69409f,-51.6540031f), new(-1175.22607f,-53.969f),
        new(-1043.099f,-58.504f) };
    static readonly float2[] Ohne = Mit.Where((_,i) => i != 5).ToArray();
    static Punkt[] Punkte(float2[] f) => f.Select(p => new Punkt(p.x,p.y)).ToArray();
    static double Winkel = ParkingGeometry.LaengsteKante(Ohne.Select(p => new double2(p.x,p.y)).ToArray());
    static LayoutSettings Einstellungen(bool ring, bool fest = true) {
        var e = LayoutSettings.Cs2;
        e.AngleMode="edge"; e.Auto=false; e.Zellen=true; e.Es=1; e.Ai=7;
        e.Cw=3; e.Sl=5.9; e.Sw=3; e.Md=2.5; e.Cr=33; e.Qk=true;
        e.Angle=0; e.Randstrassen=ring; e.AutomaticEntrances=false;
        e.Entrances=Array.Empty<Entrance>(); e.Ausrichtwinkel=fest ? Winkel : null;
        return e;
    }
    static Zelleneinstellungen ZSettings(bool ring) => new() {
        Reihenwinkel=Winkel, Randstrassen=ring, Querstrassenabstand=33 };
    static (Punkt[] Punkte,Zufahrtsvorgabe[] Zufahrten) Eingabe(float2[] f,LayoutSettings e) {
        var o=typeof(ParkingGeometry).GetMethod("ZellenNormalisiereEingabe",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{f,e});
        object Wert(string name)=>o.GetType().GetProperty(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o);
        return ((Punkt[])Wert("Normalisiert"),(Zufahrtsvorgabe[])Wert("Zufahrten"));
    }
    static void Main(string[] args) {
        System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.InvariantCulture;
        if(args.Contains("--mutation-einzelkante")) {Laufentwurf(true);return;}
        if(args.Contains("--entwurf")) {Laufentwurf(false);return;}
        if(args.Contains("--zoning")) {TeilflaechenUndZoning();return;}
        if(args.Contains("--randzoning")) {Randzoning();return;}
        if (args.Contains("--bestand")) {
            Environment.ExitCode=(int)typeof(Program).GetMethod("RunGeradepunkt",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            return;
        }
        Console.WriteLine($"FESTER RAHMEN {Winkel:F9} Grad; Reihenfolge immer OHNE -> MIT");
        var eingang=Eingabe(Mit,Einstellungen(true)).Punkte;
        var kord=eingang[6]-eingang[4];
        Console.WriteLine($"Eingangsgitter: Lot nach Normalisierung={Math.Abs(Geometrie.Kreuz(kord,eingang[5]-eingang[4]))/Geometrie.Laenge(kord):F9} m; max.Punktversatz={eingang.Select((p,i)=>Geometrie.Laenge(p-Punkte(Mit)[i])).Max():F9} m");
        foreach(var ring in new[]{true,false}) {
            foreach(var f in new[]{Ohne,Mit}) {
                try {
                    var l=ParkingGeometry.Build(f,Einstellungen(ring));
                    Console.WriteLine($"Layout ring={ring} Punkte={f.Length}: Buchten={l.Stalls}, Gassen={l.AisleLine.Length}, Netz={l.NetLine.Length}, Quer={l.NetLine.Count(n=>n.Kind=="cross")}, Zoning={l.NetLine.Count(n=>n.Kind=="zoning")}");
                    var b=Layoutbauer.Baue(new Formdefinition("Folgen",Eingabe(f,Einstellungen(ring)).Punkte),ZSettings(ring));
                    Console.WriteLine($"Kern: Teile={b.KonvexeTeile.Count}, Randbuchten={b.Randbuchten.Count}, Innenbuchten={b.Innenbuchten.Count}, Zellen={b.Zellen.Count}, Flaechen={b.Flaechen.Count}");
                    if(b.Ringlos!=null) Console.WriteLine($"Ringlos: Fusswege={b.Ringlos.Fusswege.Count}, Laenge={b.Ringlos.Fusswege.Sum(w=>Geometrie.Laenge(w.B-w.A)):F6}, Kanten="+string.Join(";",b.Ringlos.Gassen.Select(g=>$"{g.KanteA}/{g.KanteB}")));
                } catch(Exception ex) {Console.WriteLine("AUSNAHME "+ex.Message);}
            }
        }
        foreach(var f in new[]{Ohne,Mit}) {
            var p=Punkte(f); var k=new Knotenfabrik(); var r=new Linienregister();
            var teile=new Konvexzerlegung(k,r).Zerlege(Polygonfabrik.Areal(p,k,r));
            Console.WriteLine($"Zerlegung Welt {f.Length}: Teile={teile.Count}, Flaechen="+string.Join(";",teile.Select(t=>Geometrie.Flaeche(t).ToString("F6"))));
            var rand=Randreihenplanung.Plane(p,Layoutplanung.Innenrand(p,1),Layoutplanung.Innenrand(p,6.9),1,6.9,3,0,new Linienregister());
            Console.WriteLine($"Randplanung {f.Length}: {rand.Buchten.Count} Buchten; pro Kante "+string.Join(";",rand.Buchten.GroupBy(b=>b.Randkante).Select(g=>$"{g.Key}:{g.Count()}")));
        }
        foreach(var tiefe in new[]{1.0,6.9,10.4,13.9}) {
            var a=Layoutplanung.Innenrand(Punkte(Ohne),tiefe);
            var b=Layoutplanung.Innenrand(Punkte(Mit),tiefe);
            double Abstand(Punkt p,IReadOnlyList<Punkt> q) => Enumerable.Range(0,q.Count).Min(i=>Geometrie.AbstandPunktStrecke(p,q[i],q[(i+1)%q.Count]));
            var h=Math.Max(a.Max(p=>Abstand(p,b)),b.Max(p=>Abstand(p,a)));
            Console.WriteLine($"Innenrand {tiefe:F1}: Punkte {a.Count}->{b.Count}, Flaechendelta={Geometrie.Vorzeichenflaeche(b)-Geometrie.Vorzeichenflaeche(a):F9}, max.Knotenabstand={h:F9} m");
        }
        Randzoning();
        ZugaengeUndGitter();
        Diagonale();
        TeilflaechenUndZoning();
        Laufentwurf(args.Contains("--mutation-einzelkante"));
    }
    static void Randzoning() {
        foreach(var quelle in new[]{4,5}) {
            foreach(var f in new[]{Ohne,Mit}) {
                foreach(var modus in new[]{"unveraendert","Werkzeug","beide Teilkanten"}) {
                    var rz=new[]{new ParkingGeometry.RandzoningLinie { A=Ohne[quelle],B=Ohne[(quelle+1)%Ohne.Length] }};
                    if(modus=="Werkzeug") {
                        rz=Werkzeugprobe.Rand(f,rz);
                    }
                    if(modus=="beide Teilkanten" && quelle==4 && f.Length==7)
                        rz=new[]{new ParkingGeometry.RandzoningLinie {A=f[4],B=f[5]},new ParkingGeometry.RandzoningLinie {A=f[5],B=f[6]}};
                    var v=ParkingGeometry.RandzoningStrassen(rz,f,10.4);
                    var e=Einstellungen(true); e.Randzoning=rz;
                    var l=ParkingGeometry.Build(f,e);
                    Console.WriteLine($"RZ Quelle={quelle}, Punkte={f.Length}, Modus={modus}: Auswahl={rz.Sum(l=>math.distance(l.A,l.B)):F6} m, Vorschau={v?.Count ?? 0}/{v?.Sum(s=>math.distance(s.A,s.B)) ?? 0:F6} m, Bau={l.NetLine.Count(n=>n.Kind=="zoning")}/{l.NetLine.Where(n=>n.Kind=="zoning").Sum(n=>math.distance(n.A,n.B)):F6} m");
                }
            }
        }
    }
}
