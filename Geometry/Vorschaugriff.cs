using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /** Nur Sitzungszustand. Ziehen aendert weder Layout noch gespeicherte Einstellungen. */
    public sealed class Vorschaugriff
    {
        public const bool GroesseBeimVerschiebenBehalten = true;
        public OberflaechenMuster.Bereich? Bereich { get; private set; }
        public bool Zieht { get; private set; }
        public float2 Ziel { get; private set; }
        public long Stand { get; private set; }
        private float2 _versatz;

        public static bool Trifft(bool sichtbar, bool welt, bool andererZug, float2 maus,
            float2 mitte, float radius)
            => sichtbar && welt && !andererZug && math.all(math.isfinite(maus))
                && math.distancesq(maus,mitte) <= radius*radius;

        public static bool Beginnt(bool getroffen,bool links,bool abbrechen,bool bauen)
            => getroffen && links && !abbrechen && !bauen;

        public OberflaechenMuster.Bereich Startbereich(float2[] lot, float2 richtung)
        {
            if (!Bereich.HasValue) Bereich=OberflaechenMuster.Ausschnitt(lot,lot[0],richtung);
            var b=Bereich.Value;
            return GroesseBeimVerschiebenBehalten ? b : OberflaechenMuster.Ausschnitt(lot,b.Mitte,b.Richtung);
        }
        public void MerkeAusgabe(OberflaechenMuster.Bereich b)
        { if (!Zieht && b.Gueltig) Bereich=b; }
        public bool Beginne(float2 maus)
        {
            if (Zieht || !Bereich.HasValue || !math.all(math.isfinite(maus))) return false;
            Ziel=Bereich.Value.Mitte; _versatz=Ziel-maus; Zieht=true; return true;
        }
        public void Bewege(float2 maus)
        { if (Zieht && math.all(math.isfinite(maus))) Ziel=maus+_versatz; }
        public void Ende(IReadOnlyList<float2> lot, bool abbrechen)
        {
            if (!Zieht) return;
            var b=Bereich.Value;
            if (!abbrechen) Bereich=new OberflaechenMuster.Bereich(Klemme(lot,Ziel),b.Halbseite,b.Richtung);
            Zieht=false; Stand++;
        }
        public void PruefeForm(IReadOnlyList<float2> lot)
        { if (Bereich.HasValue && !Enthaelt(lot,Bereich.Value.Mitte)) Vergiss(); }
        public void Vergiss() { Bereich=null; Zieht=false; Stand++; }

        /** Rand gehoert zum Lot; Weltkoordinaten werden vor der Projektion differenziert. */
        public static bool Enthaelt(IReadOnlyList<float2> lot, float2 p)
        {
            if (lot==null || lot.Count<3 || !math.all(math.isfinite(p))) return false;
            bool innen=false;
            for (int i=0,j=lot.Count-1;i<lot.Count;j=i++)
            {
                var a=lot[j]; var b=lot[i];
                if (!math.all(math.isfinite(a)) || !math.all(math.isfinite(b))) return false;
                var q=Naechster(a,b,p);
                if (math.distancesq(p,q)<=.000001f) return true;
                if ((a.y>p.y)!=(b.y>p.y)
                    && p.x < a.x+((double)p.y-a.y)*(b.x-(double)a.x)/(b.y-(double)a.y)) innen=!innen;
            }
            return innen;
        }
        public static float2 Klemme(IReadOnlyList<float2> lot, float2 p)
        {
            if (lot==null || lot.Count<3) throw new ArgumentException("Lot fuer Vorschaugriff fehlt.");
            if (!math.all(math.isfinite(p))) return lot[0];
            if (Enthaelt(lot,p)) return p;
            float2 beste=lot[0]; double abstand=double.PositiveInfinity;
            for (int i=0;i<lot.Count;i++)
            {
                var q=Naechster(lot[i],lot[(i+1)%lot.Count],p);
                double dx=q.x-(double)p.x,dy=q.y-(double)p.y, d=dx*dx+dy*dy;
                if (d<abstand) {abstand=d;beste=q;}
            }
            return beste;
        }
        private static float2 Naechster(float2 a,float2 b,float2 p)
        {
            double x=b.x-(double)a.x,y=b.y-(double)a.y,l=x*x+y*y;
            double t=l>1e-16 ? Math.Max(0,Math.Min(1,((p.x-(double)a.x)*x+(p.y-(double)a.y)*y)/l)) : 0;
            return new float2((float)(a.x+x*t),(float)(a.y+y*t));
        }
    }
}
