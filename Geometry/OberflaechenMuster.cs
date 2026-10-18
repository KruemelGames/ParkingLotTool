using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /** Begrenzter Ausschnitt, Zulassung und Farbnetzdifferenz; ohne ECS. */
    public static class OberflaechenMuster
    {
        public const double Anteil = .25;
        public const int MaxStuecke = 128, MaxEcken = 2048, MaxRingEcken = 256;
        public const float Mindestkante = .2f;
        public readonly struct Bereich : IEquatable<Bereich>
        {
            public readonly float2 Mitte;
            public readonly float Halbseite;
            /** Einheitsvektor der Quadratkante; (1,0) = weltachsenparallel. */
            public readonly float2 Richtung;
            public Bereich(float2 mitte, float halbseite) : this(mitte, halbseite, new float2(1, 0)) { }
            public Bereich(float2 mitte, float halbseite, float2 richtung)
            {
                Mitte = mitte; Halbseite = halbseite;
                Richtung = math.all(math.isfinite(richtung)) && math.lengthsq(richtung) > 1e-12f
                    ? math.normalize(richtung) : new float2(1, 0);
            }
            public bool Gueltig => math.all(math.isfinite(Mitte)) && math.isfinite(Halbseite) && Halbseite > 0;
            public bool Equals(Bereich b) => Mitte.Equals(b.Mitte) && Halbseite == b.Halbseite && Richtung.Equals(b.Richtung);
            internal bool Achsparallel => Richtung.Equals(new float2(1, 0));
            internal Bereich Achsen => new Bereich(Mitte, Halbseite);
            /** Weltpunkt in den Rahmen des Quadrats drehen (um die Mitte). */
            internal float2 Lokal(float2 p)
            {
                double dx = p.x - (double)Mitte.x, dy = p.y - (double)Mitte.y;
                return new float2((float)(Mitte.x + dx*Richtung.x + dy*Richtung.y),
                    (float)(Mitte.y - dx*Richtung.y + dy*Richtung.x));
            }
            internal float2 Welt(float2 p)
            {
                double dx = p.x - (double)Mitte.x, dy = p.y - (double)Mitte.y;
                return new float2((float)(Mitte.x + dx*Richtung.x - dy*Richtung.y),
                    (float)(Mitte.y + dx*Richtung.y + dy*Richtung.x));
            }
        }

        /**
         * Rasterrichtung am ersten Punkt: die naechste Fahrgasse, sonst die
         * erste Shape-Kante. Auf 0..90 Grad gefaltet - ein Quadrat ist um 90
         * Grad symmetrisch, so bleibt der Wert stabil.
         */
        public static float2 Richtung(float2[][] gassen, float2[] lot, float2 punkt)
        {
            float2 beste = default; float abstand = float.PositiveInfinity;
            foreach (var linie in gassen ?? Array.Empty<float2[]>())
                for (int i = 0; linie != null && i + 1 < linie.Length; i++)
                {
                    var a = linie[i]; var b = linie[i + 1]; var d = b - a;
                    if (!math.all(math.isfinite(d)) || math.lengthsq(d) < 1f) continue;
                    float t = math.saturate(math.dot(punkt - a, d) / math.lengthsq(d));
                    float q = math.distancesq(punkt, a + d * t);
                    if (q < abstand) { abstand = q; beste = d; }
                }
            if (math.lengthsq(beste) < 1f && lot != null && lot.Length >= 2) beste = lot[1] - lot[0];
            if (!math.all(math.isfinite(beste)) || math.lengthsq(beste) < 1e-6f) return new float2(1, 0);
            beste = math.normalize(beste);
            if (beste.x < 0) beste = -beste;
            if (beste.y < 0) beste = new float2(-beste.y, beste.x);
            return beste;
        }
        public sealed class Stueck
        {
            public int Gruppe;
            public float2[] Ring;
        }
        public sealed class Plan
        {
            public Bereich Ausschnitt;
            public Stueck[] Stuecke = Array.Empty<Stueck>();
            public int Verworfen, Ecken, Verkleinerungen;
        }
        /** Jede neue Anforderung ersetzt die vorherige; kein Auftragsstapel. */
        public sealed class NeuesterStand
        {
            public long Stand { get; private set; }
            private object _layout;
            private string _wahl;
            private bool _abgebrochen;
            public long Merke(object layout, string wahl)
            {
                if (!ReferenceEquals(layout, _layout) || wahl != _wahl)
                { _layout = layout; _wahl = wahl; _abgebrochen = false; Stand++; }
                return Stand;
            }
            public void Stoppe() { _abgebrochen = true; }
            public void Verwirf() { _layout = null; _wahl = null; _abgebrochen = false; Stand++; }
            public bool IstAktuell(long stand) => stand == Stand && !_abgebrochen;
        }
        public static bool DarfZeigen(bool an, bool geschlossen, bool layoutAktuell, bool ziehen,
            bool werkzeug, bool bauphase, bool bauarbeiter, bool speichern, bool andereArbeit)
            => an && geschlossen && layoutAktuell && !ziehen && werkzeug && !bauphase
                && !bauarbeiter && !speichern && !andereArbeit;

        public static Bereich Ausschnitt(float2[] lot, float2 mitte) => Ausschnitt(lot, mitte, new float2(1, 0));
        public static Bereich Ausschnitt(float2[] lot, float2 mitte, float2 richtung)
        {
            var rahmen = new Bereich(mitte, 1, richtung);
            if (lot != null && !rahmen.Achsparallel) lot = lot.Select(rahmen.Lokal).ToArray();
            if (lot == null || lot.Length < 3 || !math.all(math.isfinite(mitte))
                || lot.Any(p => !math.all(math.isfinite(p)))) throw new ArgumentException("Ungueltiger Lotumriss.");
            double gesamt = Math.Abs(OberflaechenSchnitt.Flaeche(lot));
            if (gesamt < .001) throw new ArgumentException("Entarteter Lotumriss.");
            float oben = lot.Max(p => math.cmax(math.abs(p - mitte)));
            float unten = 0;
            // Nur zur Flaechenmessung genuegt die Halbebenenkette, auch bei getrennten Inseln.
            for (int i = 0; i < 32; i++)
            {
                float h = (oben + unten) * .5f;
                var ring = new List<float2>(lot);
                for (int seite = 0; seite < 4 && ring.Count >= 3; seite++)
                    ring = Halbebene(ring, seite/2, mitte[seite/2] + (seite%2==0 ? -h : h), seite%2==0 ? 1 : -1);
                if (Math.Abs(OberflaechenSchnitt.Flaeche(ring.ToArray())) < gesamt * Anteil) unten = h;
                else oben = h;
            }
            return new Bereich(mitte, oben, richtung);
        }
        public static Plan Berechne(float2[] lot, float2 mitte, float2[][][] gruppen)
            => Berechne(lot, mitte, gruppen, new float2(1, 0));
        public static Plan Berechne(float2[] lot, float2 mitte, float2[][][] gruppen, float2 richtung)
            => Berechne(Ausschnitt(lot, mitte, richtung), gruppen);
        public static Plan Berechne(Bereich bereich, float2[][][] gruppen)
        {
            var mitte=bereich.Mitte;
            // Bei zu vielen Bauteilen den Ausschnitt verkleinern, nie einen grossen Satz teilweise ausgeben.
            for (int versuch = 0; versuch < 16; versuch++)
            {
                var plan = new Plan { Ausschnitt = bereich, Verkleinerungen = versuch };
                var stuecke = new List<Stueck>();
                bool voll = false;
                for (int g = 0; g < gruppen.Length && !voll; g++)
                foreach (var ring in gruppen[g] ?? Array.Empty<float2[]>())
                {
                    if (ring == null || ring.Length < 3) { plan.Verworfen++; continue; }
                    if (ring.Any(p => !math.all(math.isfinite(p)))) { plan.Verworfen++; continue; }
                    // Gedreht wird nur der Rahmen: im Quadratrahmen schneiden, zurueck in die Welt.
                    var lokal = bereich.Achsparallel ? ring : ring.Select(bereich.Lokal).ToArray();
                    var min = lokal.Aggregate(new float2(float.PositiveInfinity), (a,b) => math.min(a,b));
                    var max = lokal.Aggregate(new float2(float.NegativeInfinity), (a,b) => math.max(a,b));
                    if (math.any(max < bereich.Mitte-bereich.Halbseite) || math.any(min > bereich.Mitte+bereich.Halbseite)) continue;
                    float2[][] teile;
                    try
                    {
                        teile = OberflaechenSchnitt.Schneide(lokal, bereich.Achsen);
                        if (!bereich.Achsparallel)
                            teile = teile.Select(t => t.Select(bereich.Welt).ToArray()).ToArray();
                    }
                    catch (ArgumentException) { plan.Verworfen++; continue; }
                    catch (InvalidOperationException) { plan.Verworfen++; continue; }
                    foreach (var teil in teile)
                    {
                        var sauber = Bereinige(teil);
                        if (sauber.Length > MaxRingEcken) { voll = true; break; }
                        // CS2 rueckt um -0,1 m ein. Keine Mindestkante ersetzt diesen Annahmetest.
                        if (sauber.Length < 3 || Math.Abs(OberflaechenSchnitt.Flaeche(sauber)) < .04
                            || Cs2Triangulierung.Dreiecke(sauber) != sauber.Length-2)
                        { plan.Verworfen++; continue; }
                        if (stuecke.Count >= MaxStuecke || plan.Ecken + sauber.Length > MaxEcken)
                        { voll = true; break; }
                        stuecke.Add(new Stueck { Gruppe = g, Ring = sauber }); plan.Ecken += sauber.Length;
                    }
                    if (voll) break;
                }
                if (!voll) { plan.Stuecke = stuecke.ToArray(); return plan; }
                bereich = new Bereich(mitte, bereich.Halbseite * .7f, bereich.Richtung);
            }
            // Auch nach Verkleinern nie ein unvollstaendig ausgegebenes Teilbudget.
            return new Plan { Ausschnitt = bereich, Verkleinerungen = 16, Verworfen = 1 };
        }
        public static float2[] Bereinige(float2[] ring)
        {
            var q = new List<float2>(ring);
            if (q.Any(p => !math.all(math.isfinite(p)))) return Array.Empty<float2>();
            bool aenderung = true;
            while (aenderung && q.Count >= 3)
            {
                aenderung = false;
                for (int i = 0; i < q.Count; i++)
                {
                    int j = (i+1)%q.Count;
                    if (math.distancesq(q[i],q[j]) < Mindestkante*Mindestkante)
                    { q.RemoveAt(j); aenderung = true; break; }
                }
            }
            if (OberflaechenSchnitt.Flaeche(q.ToArray()) < 0) q.Reverse();
            return q.ToArray();
        }

        /** Konvexe Dreiecke einzeln schneiden: keine Lochringe oder konkaven Restpolygone. */
        public static float2[][] Ausserhalb(float2 a, float2 b, float2 c, Bereich bereich)
        {
            if (!math.all(math.isfinite(a)) || !math.all(math.isfinite(b))
                || !math.all(math.isfinite(c)) || !bereich.Gueltig)
                throw new ArgumentException("Nicht endliche Musterkoordinate.");
            if (!bereich.Achsparallel)
                return Ausserhalb(bereich.Lokal(a), bereich.Lokal(b), bereich.Lokal(c), bereich.Achsen)
                    .Select(t => t.Select(bereich.Welt).ToArray()).ToArray();
            var mitte = bereich.Mitte; float h = bereich.Halbseite;
            float2 min = math.min(a,math.min(b,c)), max = math.max(a,math.max(b,c));
            if (max.x <= mitte.x-h || min.x >= mitte.x+h || max.y <= mitte.y-h || min.y >= mitte.y+h)
                return new[] { new[] { a,b,c } };
            if (min.x >= mitte.x-h && max.x <= mitte.x+h && min.y >= mitte.y-h && max.y <= mitte.y+h)
                return Array.Empty<float2[]>();
            var rest = new List<float2> { a,b,c };
            var result = new List<float2[]>();
            // Vier disjunkte Reststreifen; das immer kleinere Innenpolygon geht weiter.
            for (int seite = 0; seite < 4 && rest.Count >= 3; seite++)
            {
                int achse = seite / 2;
                float grenze = mitte[achse] + (seite % 2 == 0 ? -h : h);
                int vorzeichen = seite % 2 == 0 ? 1 : -1;
                var draussen = Halbebene(rest,achse,grenze,-vorzeichen);
                for (int i = 1; i+1 < draussen.Count; i++)
                {
                    var p=draussen[0]; var q=draussen[i]; var r=draussen[i+1];
                    double f = (q.x-(double)p.x)*(r.y-(double)p.y) - (q.y-(double)p.y)*(r.x-(double)p.x);
                    if (Math.Abs(f)>1e-12) result.Add(new[] { p,q,r });
                }
                rest = Halbebene(rest,achse,grenze,vorzeichen);
            }
            return result.ToArray();
        }

        private static List<float2> Halbebene(List<float2> ring, int achse, float grenze, int vorzeichen)
        {
            var result = new List<float2>();
            var a = ring[ring.Count-1];
            double da = (a[achse]-(double)grenze)*vorzeichen;
            foreach (var b in ring)
            {
                double db = (b[achse]-(double)grenze)*vorzeichen;
                if ((da < 0) != (db < 0))
                {
                    double t = da/(da-db);
                    var p = new float2((float)(a.x+(b.x-(double)a.x)*t), (float)(a.y+(b.y-(double)a.y)*t));
                    p[achse] = grenze;
                    result.Add(p);
                }
                if (db >= 0) result.Add(b);
                a=b; da=db;
            }
            return result;
        }
    }
}
