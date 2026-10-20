using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static class StrassenkurvenNetz
    {
        // Eine bekannte Nachbar-ID reicht nicht: beide nativen Knotenhaelften lesen.
        public static void Lade(Strassenkurve.Fang a,Strassenkurve.Fang b,
            Dictionary<long,Strassenkurve.Kante> kanten,Dictionary<long,Strassenkurve.Knoten> knoten,
            List<long> suche,Func<long,Strassenkurve.Kante> lese,Action<Strassenkurve.Kante> ergaenze)
        {
            kanten.Clear();knoten.Clear();suche.Clear();suche.Add(a.Kante);
            if(a.Kante!=b.Kante)suche.Add(b.Kante);
            if (a.Ecke != null && a.Ecke.Zwei.Vorhanden) suche.Add(a.Ecke.Zwei.Kante);
            if (b.Ecke != null && b.Ecke.Zwei.Vorhanden) suche.Add(b.Ecke.Zwei.Kante);
            for(int i=0;i<suche.Count && kanten.Count<Strassenkurve.Hoechstkanten;i++)
            {
                long id=suche[i];if(kanten.ContainsKey(id))continue;
                var k=lese(id);if(k==null)continue;
                kanten.Add(id,k);
                if(a.Kante==b.Kante && a.Ecke == null && b.Ecke == null)return;
                ergaenze(k);
                foreach(var node in new[]{k.Start,k.Ende})
                {
                    if(!knoten.TryGetValue(node,out var n) || n.Kreisverkehr || n.Hoehe)continue;
                    if(n.Kreuzung || n.Kanten.Length>2)
                    {
                        // An einer Kreuzung geht es nur geradeaus weiter (T-Kreuzung, Gegenseite).
                        long weiter=Strassenkurve.Weiterfuehrung(n,k.Id);
                        if(weiter!=0 && !kanten.ContainsKey(weiter))suche.Add(weiter);
                        continue;
                    }
                    if(n.Kanten.Length!=2)continue;
                    foreach(var next in n.Kanten)if(!kanten.ContainsKey(next))suche.Add(next);
                }
                if ((a.Ecke == null || !a.Ecke.Zwei.Vorhanden || kanten.ContainsKey(a.Ecke.Zwei.Kante))
                    && (b.Ecke == null || !b.Ecke.Zwei.Vorhanden || kanten.ContainsKey(b.Ecke.Zwei.Kante))
                    && Verbunden(a.Kante,b.Kante,kanten,knoten)) return;
            }
        }
        public static bool Verbunden(long a,long b,IReadOnlyDictionary<long,Strassenkurve.Kante> kanten,
            IReadOnlyDictionary<long,Strassenkurve.Knoten> knoten)
        {
            for(int r=0;r<2;r++)
            {
                long jetzt=a,node=0;
                for(int i=0;i<Strassenkurve.Hoechstkanten;i++)
                {
                    if(!kanten.TryGetValue(jetzt,out var k))break;
                    if(jetzt==b)return true;
                    node=node==0?(r==0?k.Start:k.Ende):node==k.Start?k.Ende:k.Start;
                    if(!knoten.TryGetValue(node,out var n) || n.Kanten.Length<2)break;
                    jetzt=n.Kanten.Length==2?(n.Kanten[0]==jetzt?n.Kanten[1]:n.Kanten[0])
                        :Strassenkurve.Weiterfuehrung(n,jetzt);
                    if(jetzt==0)break;
                    if(jetzt==a)break;
                }
            }
            return false;
        }
        public static void ErgaenzeAngebot(Strassenkurve.Knoten n,params Strassenkurve.Bezier[] neu)
        {
            var liste=new List<Strassenkurve.Bezier>(n.Uebergaenge);
            foreach(var c in neu)
            {
                if(!c.Endlich || math.distance(c.A,c.D)<.001f)continue;
                bool gleich=false;
                foreach(var p in liste)
                    if((math.distance(p.A,c.A)<.001 && math.distance(p.B,c.B)<.001 && math.distance(p.C,c.C)<.001 && math.distance(p.D,c.D)<.001)
                        || (math.distance(p.D,c.A)<.001 && math.distance(p.C,c.B)<.001 && math.distance(p.B,c.C)<.001 && math.distance(p.A,c.D)<.001))
                    {gleich=true;break;}
                if(!gleich)liste.Add(c);
            }
            n.Uebergaenge=liste.ToArray();
        }
    }
}
