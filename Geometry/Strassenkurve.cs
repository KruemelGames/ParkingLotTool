using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    // Nur Diagnose. Keine ECS-Verweise, Formpunkte oder Bauauftraege.
    public static partial class Strassenkurve
    {
        public const double Pfeilhoehe = .15, Mindestsehne = 1, Hoechstsehne = 16;
        public const double Winkelgrenze = Math.PI / 12, Anschlussluecke = .10;
        public const double Hoehensprung = .25, Gelaendeabstand = .5, Mindestachsradius = 15, Fangabstand = .025;
        public const int Hoechststuecke = 64, Hoechstkanten = 32;
        public enum Grund { Keiner, Gerade, KeinFang, Tangente, Kreuzung, Kreisverkehr,
            Hoehe, Seitenwechsel, Luecke, Tangentensprung, Mehrdeutig, Budget, ZuEng, Ungueltig, Nullsehne, ZuKurz, EckeAndereStrasse, EckenTangente }

        public struct Bezier
        {
            public float3 A, B, C, D;
            internal bool HatQuelle;
            internal float3 QuelleA,QuelleB,QuelleC,QuelleD;
            internal double Von,Bis;
            public Bezier(float3 a, float3 b, float3 c, float3 d) { this=default;A=a;B=b;C=c;D=d; }
            public bool Endlich => math.all(math.isfinite(A)) && math.all(math.isfinite(B))
                && math.all(math.isfinite(C)) && math.all(math.isfinite(D));
            public float3 Punkt(double t)
            {
                if(HatQuelle)return new Bezier(QuelleA,QuelleB,QuelleC,QuelleD).Punkt(Von+(Bis-Von)*t);
                var u=1-t;
                return (float3)(u*u*u*(double3)A+3*u*u*t*(double3)B+3*u*t*t*(double3)C+t*t*t*(double3)D);
            }
            public Bezier Umgekehrt()
            {
                var r=this;r.A=D;r.B=C;r.C=B;r.D=A;
                if(HatQuelle){r.Von=Bis;r.Bis=Von;}return r;
            }
            private Bezier MitQuelle(Bezier c,double a,double b)
            {
                c.HatQuelle=true;c.QuelleA=HatQuelle?QuelleA:A;c.QuelleB=HatQuelle?QuelleB:B;
                c.QuelleC=HatQuelle?QuelleC:C;c.QuelleD=HatQuelle?QuelleD:D;
                double v=HatQuelle?Von:0,spanne=HatQuelle?Bis-Von:1;
                c.Von=v+spanne*a;c.Bis=v+spanne*b;return c;
            }
            public void Teile(float t, out Bezier l, out Bezier r)
            {
                var ab=math.lerp(A,B,t); var bc=math.lerp(B,C,t); var cd=math.lerp(C,D,t);
                var abc=math.lerp(ab,bc,t); var bcd=math.lerp(bc,cd,t); var m=math.lerp(abc,bcd,t);
                l=MitQuelle(new Bezier(A,ab,abc,m),0,t);r=MitQuelle(new Bezier(m,bcd,cd,D),t,1);
            }
            public Bezier Ausschnitt(float a, float b)
            {
                if (a>b) return Ausschnitt(b,a).Umgekehrt();
                Teile(b,out var l,out _);
                if (a>0 && b>0) l.Teile(a/b,out _,out l);
                return l;
            }
        }
        public struct Teil
        {
            public Bezier Kurve;
            public int Quelle;
            public bool Umgekehrt;
        }
        public sealed class Kante
        {
            public long Id, Start, Ende;
            public Teil[] Links, Rechts;
            public Bezier Mittellinie;
            public bool HatMittellinie;
            public bool Hoehe;
            public Teil[] Seite(int seite) => seite>0 ? Links : Rechts;
        }
        public sealed class Knoten
        {
            public long Id;
            public long[] Kanten = Array.Empty<long>();
            public bool Kreuzung, Kreisverkehr, Hoehe;
            public Bezier[] Uebergaenge = Array.Empty<Bezier>();
            /** Knotenmitte und je Kante (gleiche Reihenfolge wie `Kanten`) die Abgangsrichtung in XZ. */
            public float3 Lage;
            public float2[] Richtungen = Array.Empty<float2>();
        }
        public struct Fang : IEquatable<Fang>
        {
            public long Kante;
            public int Seite, Teil;
            public float T;
            public float3 Punkt;
            public Strassenecke Ecke;
            public bool Vorhanden => Kante!=0;
            public bool Equals(Fang f) => Kante==f.Kante && Seite==f.Seite && Teil==f.Teil
                && T.Equals(f.T) && math.all(Punkt==f.Punkt)
                && (Ecke == null ? f.Ecke == null : Ecke.Equals(f.Ecke));
        }
        public struct Lueckenmessung
        {
            public bool Vorhanden;
            public long Knoten;
            public int Angebote;
            public double KantenendeZumAngebot, AngebotZumZiel;
        }
        public sealed class Ergebnis
        {
            public Grund Grund;
            public Fang Von, Bis;
            public float3[] Punkte=Array.Empty<float3>();
            public double Abweichung, Radius=double.PositiveInfinity, Winkel, Laenge;
            public int Stuecke;
            public Lueckenmessung Luecke;
            public Kurvengruppe Gruppe;
            internal Bezier[] Verlauf;
            public bool OhneFolgen => Grund == Grund.EckeAndereStrasse || Grund == Grund.Kreuzung;
            public bool Folgt => Grund==Grund.Keiner || Grund==Grund.Gerade;
        }

        // Native Start-/End-Haelften koennen entgegengesetzt gespeichert sein.
        public static Teil[] Ordne(Bezier a, Bezier b, float3 start)
        {
            bool leerA=math.all(a.A==a.B) && math.all(a.A==a.C) && math.all(a.A==a.D);
            bool leerB=math.all(b.A==b.B) && math.all(b.A==b.C) && math.all(b.A==b.D);
            if(leerA && leerB)return Array.Empty<Teil>();
            if(leerA || leerB)
            {
                var c=leerA?b:a;bool um=math.distance(start,c.D)<math.distance(start,c.A);
                return new[]{new Teil {Kurve=um?c.Umgekehrt():c,Quelle=leerA?1:0,Umgekehrt=um}};
            }
            var beste=double.PositiveInfinity; Teil[] result=null;
            for(int tausch=0;tausch<2;tausch++) for(int ar=0;ar<2;ar++) for(int br=0;br<2;br++)
            {
                var x=tausch==0 ? a : b; var y=tausch==0 ? b : a;
                if(ar!=0)x=x.Umgekehrt(); if(br!=0)y=y.Umgekehrt();
                var mass=math.distance(x.D,y.A)*1000.0+math.distance(start,x.A);
                if(mass>=beste)continue;
                beste=mass;
                result=new[] {new Teil {Kurve=x,Quelle=tausch,Umgekehrt=ar!=0},
                    new Teil {Kurve=y,Quelle=1-tausch,Umgekehrt=br!=0}};
            }
            return result;
        }

        public static bool BestaetigeFang(Kante k,int seite,int quelle,float t,float3 position,out Fang f)
        {
            f=default;if(k==null || (seite!=1 && seite!=-1) || !math.isfinite(t) || t<0 || t>1)return false;
            var teile=k.Seite(seite);if(teile==null)return false;
            for(int i=0;i<teile.Length;i++)if(teile[i].Quelle==quelle)
            {
                float u=teile[i].Umgekehrt?1-t:t;
                f=new Fang {Kante=k.Id,Seite=seite,Teil=i,T=u,Punkt=teile[i].Kurve.Punkt(u)};
                if(GueltigerFang(f,k) && math.all(math.isfinite(position)) && math.distance(f.Punkt,position)<=Fangabstand)return true;
                f=default;return false;
            }
            return false;
        }

        public static Ergebnis Verfolge(Fang a,Fang b,IReadOnlyDictionary<long,Kante> kanten,
            IReadOnlyDictionary<long,Knoten> knoten,double innenversatz=0,int innenseite=0)
        {
            var e = a.Ecke != null || b.Ecke != null
                ? Strassenecke.Verfolge(a, b, kanten, knoten, innenversatz, innenseite)
                : VerfolgeKern(a, b, kanten, knoten, innenversatz, innenseite);
            e.Von=a;e.Bis=b;return e;
        }
        private static Ergebnis VerfolgeKern(Fang a,Fang b,IReadOnlyDictionary<long,Kante> kanten,
            IReadOnlyDictionary<long,Knoten> knoten,double innenversatz,int innenseite)
        {
            if(!a.Vorhanden || !b.Vorhanden)return Fehler(Grund.KeinFang);
            if(!kanten.TryGetValue(a.Kante,out var start) || !kanten.ContainsKey(b.Kante))return Fehler(Grund.Luecke);
            if(!GueltigerFang(a,start) || !GueltigerFang(b,kanten[b.Kante]))return Fehler(Grund.Ungueltig);
            if(math.distance(a.Punkt,b.Punkt)<.001f)return Fehler(Grund.Nullsehne);
            if(a.Kante==b.Kante)
            {
                if(a.Seite!=b.Seite)return Fehler(Grund.Seitenwechsel);
                if(start.Hoehe)return Fehler(Grund.Hoehe);
                var kurven=new List<Bezier>();
                var vor=a.Teil<b.Teil || (a.Teil==b.Teil && a.T<=b.T);
                Fuege(kurven,start.Seite(a.Seite),a.Teil,a.T,b.Teil,b.T,vor);
                return Messe(kurven,innenversatz,innenseite);
            }
            var erster=Suche(a,b,true,kanten,knoten,out var k1,out var m1);
            var zweiter=Suche(a,b,false,kanten,knoten,out var k2,out var m2);
            if(erster==Grund.Keiner && zweiter==Grund.Keiner)return Fehler(Grund.Mehrdeutig);
            if(erster==Grund.Keiner)return Messe(k1,innenversatz,innenseite);
            if(zweiter==Grund.Keiner)return Messe(k2,innenversatz,innenseite);
            bool eins=Rang(erster)>Rang(zweiter) || (Rang(erster)==Rang(zweiter) && (m1.Vorhanden || !m2.Vorhanden));
            var fehler=Fehler(eins?erster:zweiter);fehler.Luecke=eins?m1:m2;return fehler;
        }
        private static int Rang(Grund g) => g==Grund.Luecke ? 0 : g==Grund.Budget ? 1 : 2;
        private static Ergebnis Fehler(Grund g) => new Ergebnis {Grund=g};
        private static bool GueltigerFang(Fang f,Kante k)
        {
            if(f.Seite!=1 && f.Seite!=-1)return false;
            var s=k.Seite(f.Seite);
            return s!=null && f.Teil>=0 && f.Teil<s.Length && math.isfinite(f.T)
                && f.T>=0 && f.T<=1 && math.all(math.isfinite(f.Punkt))
                && s[f.Teil].Kurve.Endlich && math.distance(s[f.Teil].Kurve.Punkt(f.T),f.Punkt)<=Fangabstand;
        }
        private static Grund Suche(Fang a,Fang b,bool vor,IReadOnlyDictionary<long,Kante> kanten,
            IReadOnlyDictionary<long,Knoten> knoten,out List<Bezier> kurven,out Lueckenmessung messung)
        {
            messung=default;kurven=new List<Bezier>(); var k=kanten[a.Kante];var seite=a.Seite;
            var gesehen=new HashSet<long>(); int teil=a.Teil; float t=a.T;
            for(int i=0;i<Hoechstkanten;i++)
            {
                if(!gesehen.Add(k.Id))return Grund.Mehrdeutig;
                if(k.Hoehe)return Grund.Hoehe;
                var s=k.Seite(seite); if(s==null || s.Length==0)return Grund.Luecke;
                if(k.Id==b.Kante)
                {
                    if(seite!=b.Seite)return Grund.Seitenwechsel;
                    Fuege(kurven,s,teil,t,b.Teil,b.T,vor);return Grund.Keiner;
                }
                Fuege(kurven,s,teil,t,vor?s.Length-1:0,vor?1:0,vor);
                var node=vor?k.Ende:k.Start;
                if(!knoten.TryGetValue(node,out var n))return Grund.Luecke;
                if(n.Kreisverkehr)return Grund.Kreisverkehr;
                var bordstein=kurven.Count==0?a.Punkt:kurven[kurven.Count-1].D;
                bool kreuzung=n.Kreuzung || n.Kanten.Length>2;
                long id;
                if(kreuzung)
                {
                    // T-Kreuzung, Gegenseite (Nutzer, 19.10.): dort laeuft der Bordstein
                    // ohne Knick zur naechsten Strasse weiter - das ist noch dieselbe
                    // Strasse. Auf jeder anderen Seite knickt er ab: keine Kurve.
                    id=BordsteinNachbar(n,k.Id,bordstein);
                    if(id==0 || n.Hoehe || !kanten.TryGetValue(id,out var quer)
                        || PruefeFortsetzung(k,quer,node)!=Grund.Keiner)return Grund.Kreuzung;
                }
                else
                {
                    if(n.Hoehe)return Grund.Hoehe;
                    if(n.Kanten.Length!=2 || (n.Kanten[0]!=k.Id && n.Kanten[1]!=k.Id))return Grund.Luecke;
                    id=n.Kanten[0]==k.Id?n.Kanten[1]:n.Kanten[0];
                }
                if(id==k.Id || !kanten.TryGetValue(id,out var next))return Grund.Luecke;
                var fortsetzung = PruefeFortsetzung(k, next, node);
                // Aussen um einen Knick herum folgt man der Abrundung; innen nicht.
                if (!kreuzung && fortsetzung == Grund.EckeAndereStrasse && AussenEcke(k, next, node, bordstein))
                    fortsetzung = Grund.Keiner;
                if (fortsetzung != Grund.Keiner) return fortsetzung;
                bool weiter=next.Start==node;
                if(!weiter && next.Ende!=node)return Grund.Luecke;
                int andere=seite*(vor?1:-1)*(weiter?1:-1);
                var ns=next.Seite(andere);if(ns==null || ns.Length==0)return Grund.Luecke;
                var ziel=weiter?ns[0].Kurve.A:ns[ns.Length-1].Kurve.D;
                var von=kurven.Count==0?a.Punkt:kurven[kurven.Count-1].D;
                var bruecke=Verbinde(von,ziel,n.Uebergaenge,out var mittel);
                if(bruecke==Grund.Luecke)messung=MesseLuecke(n,von,ziel);
                if(bruecke!=Grund.Keiner)return bruecke;
                kurven.AddRange(mittel);
                k=next;seite=andere;vor=weiter;teil=vor?0:ns.Length-1;t=vor?0:1;
            }
            return Grund.Budget;
        }
        private static Lueckenmessung MesseLuecke(Knoten n,float3 von,float3 bis)
        {
            var m=new Lueckenmessung {Vorhanden=true,Knoten=n.Id,Angebote=n.Uebergaenge.Length,
                KantenendeZumAngebot=double.PositiveInfinity,AngebotZumZiel=double.PositiveInfinity};
            foreach(var c in n.Uebergaenge)for(int r=0;r<2;r++)
            {
                double a=math.distance(von,r==0?c.A:c.D),b=math.distance(bis,r==0?c.D:c.A);
                if(a<m.KantenendeZumAngebot || (a==m.KantenendeZumAngebot && b<m.AngebotZumZiel))
                {m.KantenendeZumAngebot=a;m.AngebotZumZiel=b;}
            }
            return m;
        }
        private static Grund Verbinde(float3 von,float3 bis,Bezier[] angebot,out List<Bezier> teile)
        {
            teile=new List<Bezier>();
            if(Math.Abs(von.y-bis.y)>Hoehensprung)return Grund.Hoehe;
            if(math.distance(von,bis)<=Anschlussluecke)return Grund.Keiner;
            List<Bezier> fund=null;
            for(int i=0;i<angebot.Length;i++)for(int r=0;r<2;r++)
            {
                var c=r==0?angebot[i]:angebot[i].Umgekehrt();
                if(math.distance(von,c.A)>Anschlussluecke)continue;
                if(math.distance(bis,c.D)<=Anschlussluecke)
                {if(fund!=null)return Grund.Mehrdeutig;fund=new List<Bezier>{c};continue;}
                for(int j=0;j<angebot.Length;j++)if(j!=i)for(int z=0;z<2;z++)
                {
                    var d=z==0?angebot[j]:angebot[j].Umgekehrt();
                    if(math.distance(c.D,d.A)>Anschlussluecke || math.distance(d.D,bis)>Anschlussluecke)continue;
                    if(fund!=null)return Grund.Mehrdeutig;
                    fund=new List<Bezier>{c,d};
                }
            }
            if(fund==null)return Grund.Luecke;
            teile=fund;return Grund.Keiner;
        }

        private static void Fuege(List<Bezier> ziel,Teil[] teile,int von,float tvon,int bis,float tbis,bool vor)
        {
            for(int i=von;vor?i<=bis:i>=bis;i+=vor?1:-1)
            {
                var a=i==von?tvon:vor?0:1; var b=i==bis?tbis:vor?1:0;
                if(Math.Abs(a-b)<1e-7)continue;
                ziel.Add(teile[i].Kurve.Ausschnitt(a,b));
            }
        }
        public static double Huelle(Bezier c,float3 a,float3 d)
            => Math.Max(Abstand(c.A,a,d),Math.Max(Abstand(c.B,a,d),
                Math.Max(Abstand(c.C,a,d),Abstand(c.D,a,d))));
        private static double Abstand(float3 p,float3 a,float3 b)
        {
            double x=(double)b.x-a.x,z=(double)b.z-a.z,px=(double)p.x-a.x,pz=(double)p.z-a.z;
            var len=x*x+z*z;if(len<1e-12)return Math.Sqrt(px*px+pz*pz);
            var t=Math.Max(0,Math.Min(1,(x*px+z*pz)/len));
            return Math.Sqrt((px-t*x)*(px-t*x)+(pz-t*z)*(pz-t*z));
        }
        private static double Winkel(float3 a,float3 b)
        {
            double ax=a.x,az=a.z,bx=b.x,bz=b.z;
            var l=Math.Sqrt((ax*ax+az*az)*(bx*bx+bz*bz));
            return l<1e-12?Math.PI:Math.Acos(Math.Max(-1,Math.Min(1,(ax*bx+az*bz)/l)));
        }
        public static Ergebnis Einfrieren(long id, IReadOnlyList<Bezier> kurven,
            int budget = Hoechststuecke, double innenversatz = 0, int innenseite = 0)
        {
            if (kurven == null) return Fehler(Grund.Ungueltig);
            return Messe(new List<Bezier>(kurven), innenversatz, innenseite, id, budget);
        }

        private static Ergebnis Messe(List<Bezier> kurven, double d, int innen,
            long id = 1, int budget = Hoechststuecke)
        {
            if (id <= 0 || budget < 1 || budget > Hoechststuecke || !math.isfinite(d) || d < 0
                || innen < -1 || innen > 1) return Fehler(Grund.Ungueltig);
            if (kurven.Count == 0) return Fehler(Grund.Nullsehne);
            var e = new Ergebnis { Verlauf = kurven.ToArray() };
            var tabellen = new List<double[]>();
            var grenzen = new List<double> { 0 };
            var a = kurven[0].A;
            var b = kurven[kurven.Count - 1].D;
            Grund fehler = Grund.Keiner;
            double glatterWinkel = 0;
            for (int i = 0; i < kurven.Count; i++)
            {
                var c = kurven[i];
                if (!c.Endlich) return Fehler(Grund.Ungueltig);
                if (i > 0)
                {
                    var p = kurven[i - 1];
                    if (Math.Abs(p.D.y - c.A.y) > Hoehensprung) fehler = Grund.Hoehe;
                    else if (math.distance(p.D, c.A) > Anschlussluecke) fehler = Grund.Luecke;
                    else
                    {
                        double knick = Winkel(Tangente(p, 1), Tangente(c, 0));
                        e.Winkel += knick;
                        if (knick > Winkelgrenze) fehler = Grund.Tangentensprung;
                    }
                }
                e.Abweichung = Math.Max(e.Abweichung, Huelle(c, a, b));
                var radius = Radius(c, innen, out var innenradius);
                e.Radius = Math.Min(e.Radius, radius);
                if (radius == 0 || (innen != 0 && innenradius <= d + Mindestachsradius)) fehler = Grund.ZuEng;
                var tabelle = new double[65];
                var vorher = c.A;
                var tan = Tangente(c, 0);
                for (int j = 1; j <= 64; j++)
                {
                    double t = j / 64.0;
                    var punkt = c.Punkt(t);
                    tabelle[j] = tabelle[j - 1] + math.distance(punkt, vorher);
                    vorher = punkt;
                    var nt = Tangente(c, t);
                    double drehung = Winkel(tan, nt);
                    e.Winkel += drehung;
                    glatterWinkel += drehung;
                    tan = nt;
                }
                e.Laenge += tabelle[64];
                tabellen.Add(tabelle);
                grenzen.Add(e.Laenge);
            }
            if (!math.isfinite(e.Laenge)) return Fehler(Grund.Ungueltig);
            if (e.Laenge < 1e-8) return Fehler(Grund.Nullsehne);
            if (fehler != Grund.Keiner) { e.Grund = fehler; return e; }
            var stationen = Stationen(kurven, tabellen, grenzen);
            if (stationen == null) { e.Grund = Grund.Budget; return e; }
            int n = stationen.Count - 1;
            e.Stuecke = n;
            if (n > budget) { e.Grund = Grund.Budget; return e; }
            var punkte = new float3[n + 1];
            var stellen = new (int Index, float T)[n + 1];
            for (int i = 0; i <= n; i++)
            {
                var ort = Stelle(kurven, tabellen, grenzen, stationen[i]);
                punkte[i] = ort.Punkt;
                stellen[i] = (ort.Index, ort.T);
            }
            for (int i = 1; i < punkte.Length; i++)
                if (math.distance(punkte[i - 1].xz, punkte[i].xz) < Mindestsehne - .001)
                    { e.Grund = Grund.ZuKurz; return e; }
            e.Grund = e.Abweichung <= Pfeilhoehe ? Grund.Gerade : Grund.Keiner;
            e.Gruppe = new Kurvengruppe(id, kurven, punkte, stellen);
            e.Laenge = e.Gruppe.Laenge;
            e.Punkte = punkte;
            return e;
        }
        private static (int Index,float T,float3 Punkt) Stelle(List<Bezier> kurven,
            List<double[]> tabellen,List<double> grenzen,double s)
        {
            int k=0;while(k+1<kurven.Count && s>grenzen[k+1])k++;
            var laenge=Math.Max(0,s-grenzen[k]);var t=tabellen[k];int j=1;
            while(j<64 && laenge>t[j])j++;
            var rest=t[j]-t[j-1];
            float parameter=(float)((j-1+(rest>0?Math.Max(0,Math.Min(1,(laenge-t[j-1])/rest)):0))/64.0);
            return (k,parameter,kurven[k].Punkt(parameter));
        }
        /**
         * ADAPTIVE ABTASTUNG (19.10. abends): jede Sehne so lang, wie sie
         * darf. Bis dahin teilte die Abtastung den ganzen Zug gleichmaessig -
         * die engste Stelle bestimmte die Sehne fuer alle, und ein Spielplatz
         * hatte auf seiner GERADEN Seite neun kollineare 4,16-m-Kanten. Eine
         * 5,5 m breite Zufahrt ueberspannte dort mehrere Kanten und traf ein
         * "Hindernis". Jetzt: von jeder Station aus die laengste zulaessige
         * Sehne (Halbierungssuche), die letzten zwei Sehnen werden
         * ausgeglichen, damit kein Kurzrest bleibt. Ob es zu viele werden,
         * entscheidet allein die Budgetpruefung des Aufrufers; hier endet die
         * Suche nur an einer Sicherheitsgrenze. null = ein Knick, den keine
         * Sehne glaettet.
         */
        private static List<double> Stationen(List<Bezier> kurven, List<double[]> tabellen,
            List<double> grenzen)
        {
            double gesamt = grenzen[grenzen.Count - 1];
            var stationen = new List<double> { 0 };
            double von = 0;
            while (gesamt - von > 1e-9)
            {
                if (stationen.Count > 4 * Hoechststuecke) break;
                if (Passt(kurven, tabellen, grenzen, von, gesamt)) { stationen.Add(gesamt); break; }
                double gut = von, schlecht = Math.Min(gesamt, von + Hoechstsehne * 1.5);
                for (int i = 0; i < 40 && schlecht - gut > 1e-4; i++)
                {
                    double mitte = (gut + schlecht) / 2;
                    if (Passt(kurven, tabellen, grenzen, von, mitte)) gut = mitte; else schlecht = mitte;
                }
                // Nicht einmal ein Zehntelmillimeter passt: ein Knick, den keine Sehne glaettet.
                if (gut - von < 1e-4) return null;
                stationen.Add(gut);
                von = gut;
            }
            int m = stationen.Count;
            if (m >= 3)
            {
                double mitte = (stationen[m - 3] + stationen[m - 1]) / 2;
                if (Passt(kurven, tabellen, grenzen, stationen[m - 3], mitte)
                    && Passt(kurven, tabellen, grenzen, mitte, stationen[m - 1]))
                    stationen[m - 2] = mitte;
            }
            return stationen;
        }

        /**
         * Die Suche schoepft die Pfeilhoehe voll aus; 32 Proben liegen bis zu
         * etwa 1 mm unter dem wahren Hoechstwert. 2 mm Reserve halten die
         * Grenze auch gegen eine dichte Nachmessung ein.
         */
        private const double Probenreserve = .002;

        // Taugt die Sehne zwischen den Bogenstellen `von` und `bis`? Laenge, Pfeilhoehe, Drehung.
        private static bool Passt(List<Bezier> kurven, List<double[]> tabellen, List<double> grenzen,
            double von, double bis)
        {
            var a = Stelle(kurven, tabellen, grenzen, von);
            var b = Stelle(kurven, tabellen, grenzen, bis);
            if (math.distance(a.Punkt.xz, b.Punkt.xz) > Hoechstsehne + .001) return false;
            double winkel = 0;
            for (int k = a.Index; k <= b.Index; k++)
            {
                var c = kurven[k].Ausschnitt(k == a.Index ? a.T : 0, k == b.Index ? b.T : 1);
                if (Math.Abs(c.Bis - c.Von) < 1e-15 && c.HatQuelle) continue;
                if (k > a.Index) winkel += Winkel(Tangente(kurven[k - 1], 1), Tangente(kurven[k], 0));
                if (Radius(c, 0, out _) <= 0) return false;
                // Gemessen statt abgeschaetzt (19.10.): Kontrollhuelle und
                // "Kontrolllaenge / kleinster Radius" ueberschaetzten so
                // stark, dass ein Bogen R 21,5 m / 150 Grad 39 statt rund
                // 12 Stuecke bekam - mit 17 s Vorschau und zwei von CS2
                // abgelehnten Flaechen. Dichte Proben liegen bei 8-m-Sehnen
                // unter einem Millimeter neben dem wahren Hoechstwert.
                MesseSehne(c, a.Punkt, b.Punkt, out var abweichung, out var drehung);
                if (abweichung > Pfeilhoehe - Probenreserve) return false;
                winkel += drehung;
            }
            if (winkel > Winkelgrenze + 1e-10) return false;
            return true;
        }
        // Groesste Abweichung von der Sehne a-b (XZ) und Tangentendrehung, je 32 Proben.
        private static void MesseSehne(Bezier c, float3 a, float3 b, out double abweichung, out double drehung)
        {
            const int proben = 32;
            abweichung = 0; drehung = 0;
            double2 pa = new double2(a.x, a.z), pb = new double2(b.x, b.z), ab = pb - pa;
            double laenge2 = math.lengthsq(ab);
            var vorher = Tangente(c, 0);
            for (int i = 0; i <= proben; i++)
            {
                double t = i / (double)proben;
                var p = c.Punkt(t);
                var q = new double2(p.x, p.z);
                double s = laenge2 > 1e-18 ? math.clamp(math.dot(q - pa, ab) / laenge2, 0, 1) : 0;
                abweichung = Math.Max(abweichung, math.distance(q, pa + s * ab));
                if (i == 0) continue;
                var jetzt = Tangente(c, t);
                drehung += Winkel(vorher, jetzt);
                vorher = jetzt;
            }
        }
        private static float3 Tangente(Bezier c,double t)
        {
            if(c.HatQuelle)
            {
                var basis=new Bezier(c.QuelleA,c.QuelleB,c.QuelleC,c.QuelleD);
                return Tangente(basis,c.Von+(c.Bis-c.Von)*t)*(c.Bis<c.Von?-1:1);
            }
            return (float)(3*(1-t)*(1-t))*(c.B-c.A)+(float)(6*(1-t)*t)*(c.C-c.B)+(float)(3*t*t)*(c.D-c.C);
        }

        // Extrema der Kruemmung: 2*q'*s - 3*q*s' = 0 (Polynom bis Grad 5).
        // Damit faellt kein enger Abschnitt zwischen feste Radiusproben.
        private static double Radius(Bezier c,int innen,out double innenradius)
        {
            // Radius an der Originalkurve auswerten: ein Millimeter-Rest in
            // Weltkoordinaten verliert beim float-Teilen sonst seine Tangente.
            double von=0,bis=1,orientierung=1;
            if(c.HatQuelle)
            {
                von=Math.Min(c.Von,c.Bis);bis=Math.Max(c.Von,c.Bis);orientierung=c.Bis<c.Von?-1:1;
                c=new Bezier(c.QuelleA,c.QuelleB,c.QuelleC,c.QuelleD);
            }
            var x=new[] {3*((double)c.B.x-c.A.x),6*((double)c.C.x-2*c.B.x+c.A.x),
                3*((double)c.D.x-3*c.C.x+3*c.B.x-c.A.x)};
            var z=new[] {3*((double)c.B.z-c.A.z),6*((double)c.C.z-2*c.B.z+c.A.z),
                3*((double)c.D.z-3*c.C.z+3*c.B.z-c.A.z)};
            var s=Summe(Mal(x,x),Mal(z,z),1);var q=Summe(Mal(x,Ableitung(z)),Mal(z,Ableitung(x)),-1);
            var extrem=Summe(Mal(Ableitung(q),s),Mal(q,Ableitung(s)),-1.5);
            var roots=new List<double>{von,bis};Wurzeln(extrem,roots);Wurzeln(Ableitung(s),roots);
            double r=double.PositiveInfinity;innenradius=r;
            foreach(var t in roots)
            {
                if(t<von || t>bis)continue;
                var speed=Wert(s,t);var cross=Wert(q,t);
                if(speed<1e-12){r=0;innenradius=0;continue;}
                if(Math.Abs(cross)<1e-12)continue;
                var rr=Math.Pow(speed,1.5)/Math.Abs(cross);r=Math.Min(r,rr);
                if(cross*innen*orientierung>0)innenradius=Math.Min(innenradius,rr);
            }
            return r;
        }
        private static double[] Mal(double[] a,double[] b)
        {
            var r=new double[a.Length+b.Length-1];for(int i=0;i<a.Length;i++)for(int j=0;j<b.Length;j++)r[i+j]+=a[i]*b[j];return r;
        }
        private static double[] Summe(double[] a,double[] b,double f)
        {
            var r=new double[Math.Max(a.Length,b.Length)];for(int i=0;i<r.Length;i++)r[i]=(i<a.Length?a[i]:0)+f*(i<b.Length?b[i]:0);return r;
        }
        private static double[] Ableitung(double[] a)
        {
            var r=new double[Math.Max(1,a.Length-1)];for(int i=1;i<a.Length;i++)r[i-1]=i*a[i];return r;
        }
        private static double Wert(double[] a,double t)
        { double r=0;for(int i=a.Length-1;i>=0;i--)r=r*t+a[i];return r; }
        private static void Wurzeln(double[] a,List<double> result)
        {
            int grad=a.Length-1;double norm=0;foreach(var v in a)norm=Math.Max(norm,Math.Abs(v));
            if(norm==0)return;while(grad>0 && Math.Abs(a[grad])<norm*1e-12)grad--;
            if(grad==0)return;
            if(grad==1){var t=-a[0]/a[1];if(t>0 && t<1)result.Add(t);return;}
            var cuts=new List<double>{0,1};var copy=new double[grad+1];Array.Copy(a,copy,grad+1);
            Wurzeln(Ableitung(copy),cuts);cuts.Sort();
            foreach(var t in cuts)if(Math.Abs(Wert(copy,t))<norm*1e-10)result.Add(t);
            for(int i=1;i<cuts.Count;i++)
            {
                double lo=cuts[i-1],hi=cuts[i],fl=Wert(copy,lo),fh=Wert(copy,hi);
                if(fl*fh>=0)continue;
                for(int j=0;j<48;j++){double m=(lo+hi)/2,fm=Wert(copy,m);if(fl*fm<=0)hi=m;else{lo=m;fl=fm;}}
                result.Add((lo+hi)/2);
            }
        }
    }
}
