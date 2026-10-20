using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /** Wo eine Laterne steht - bestimmt, ob das Einzel- oder das Doppelmodell kommt. */
    public enum LaternenArt
    {
        /** Gruen-Insel am Reihenende. Doppelmodell. */
        Kappe,
        /** Mittelstreifen zwischen Ruecken-an-Ruecken-Reihen. Doppelmodell. */
        Mittelstreifen,
        /** Kopf-an-Kopf-Linie ohne Mittelstreifen, Ecke von vier Buchten. Doppelmodell. */
        KopfAnKopf,
        /** Hinter einer Randreihe am Grundstuecksrand, zum Platz gerichtet. Einzelmodell. */
        Rand,
        /** Innen im Gruenstreifen hinter einer Einzelreihe. Doppelmodell. */
        Streifen,
    }

    public struct LaternenPlatz
    {
        public float2 Position;
        public LaternenArt Art;
        /**
         * Doppelmodell: die Achse der Arme, quer ueber beide Reihen.
         * Einzelmodell: die Leuchtrichtung, vom Mast in den Parkplatz.
         * Immer Einheitsvektor in XZ.
         */
        public float2 Richtung;
        public bool Doppelt => Art != LaternenArt.Rand;
    }

    /** Wie ein Modell leuchtet - bestimmt die Drehung. */
    public enum LaternenBauart
    {
        /** Ein Arm nach lokal +Z (Einzelstrassenlaterne, Gewerbe/Industrie 01-02). */
        Einseitig,
        /** Zwei Arme nach lokal +-X (Doppelstrassenlaterne). */
        Doppelt,
        /** Rund gebaut, leuchtet nach allen Seiten (Gewerbe/Industrie 03-04). */
        Rundum,
    }

    public sealed class LaternenModell
    {
        public string Prefab;
        public LaternenBauart Bauart;
        public float Hoehe, Reichweite;
    }

    public sealed class LaternenSet
    {
        public string Id, Name;
        public string Einzeln, Doppelt;
        public bool Eigen;
    }

    /**
     * DIE GEMESSENEN MODELLE (Inventur 2026-10-04, ParkingLotLaternenInventurSystem).
     *
     * Hoehe und Lichtreichweite aus den Prefab-Daten; die Bauart aus der Lage
     * der Lichteffekte (Einzel: +Z, Doppel: +-X, 03/04 rund) und fuer
     * Gewerbe/Industrie 01-02 aus der Ansage des Nutzers ("einseitig").
     * Parklichtmaste hat der Nutzer abgelehnt: zu klein, zu wenig Licht.
     */
    public static class LaternenKatalog
    {
        public static readonly LaternenModell[] Modelle =
        {
            new LaternenModell { Prefab = "StreetlightSingle01", Bauart = LaternenBauart.Einseitig, Hoehe = 6.35f, Reichweite = 18f },
            new LaternenModell { Prefab = "StreetlightSingle02", Bauart = LaternenBauart.Einseitig, Hoehe = 7.02f, Reichweite = 18f },
            new LaternenModell { Prefab = "StreetlightSingle03", Bauart = LaternenBauart.Einseitig, Hoehe = 6.70f, Reichweite = 18f },
            new LaternenModell { Prefab = "StreetlightDouble01", Bauart = LaternenBauart.Doppelt, Hoehe = 6.35f, Reichweite = 18f },
            new LaternenModell { Prefab = "StreetlightDouble02", Bauart = LaternenBauart.Doppelt, Hoehe = 7.02f, Reichweite = 18f },
            new LaternenModell { Prefab = "StreetlightDouble03", Bauart = LaternenBauart.Doppelt, Hoehe = 6.70f, Reichweite = 18f },
            new LaternenModell { Prefab = "LightpoleCommercial01", Bauart = LaternenBauart.Einseitig, Hoehe = 10.62f, Reichweite = 20f },
            new LaternenModell { Prefab = "LightpoleCommercial02", Bauart = LaternenBauart.Einseitig, Hoehe = 10.79f, Reichweite = 20f },
            new LaternenModell { Prefab = "LightpoleCommercial03", Bauart = LaternenBauart.Rundum, Hoehe = 10.79f, Reichweite = 20f },
            new LaternenModell { Prefab = "LightpoleCommercial04", Bauart = LaternenBauart.Rundum, Hoehe = 10.82f, Reichweite = 20f },
            new LaternenModell { Prefab = "LightpoleIndustrial01", Bauart = LaternenBauart.Einseitig, Hoehe = 20.42f, Reichweite = 30f },
            new LaternenModell { Prefab = "LightpoleIndustrial02", Bauart = LaternenBauart.Einseitig, Hoehe = 20.75f, Reichweite = 30f },
            new LaternenModell { Prefab = "LightpoleIndustrial03", Bauart = LaternenBauart.Rundum, Hoehe = 22.27f, Reichweite = 30f },
            new LaternenModell { Prefab = "LightpoleIndustrial04", Bauart = LaternenBauart.Rundum, Hoehe = 22.27f, Reichweite = 30f },
        };

        /** Die drei festen Sets; je Feld genau ein Modell (Nutzer 2026-10-04). */
        public static readonly LaternenSet[] Sets =
        {
            new LaternenSet { Id = "strasse", Name = "Straße", Einzeln = "StreetlightSingle02", Doppelt = "StreetlightDouble02" },
            new LaternenSet { Id = "gewerbe", Name = "Gewerbe", Einzeln = "LightpoleCommercial01", Doppelt = "LightpoleCommercial03" },
            new LaternenSet { Id = "industrie", Name = "Industrie", Einzeln = "LightpoleIndustrial01", Doppelt = "LightpoleIndustrial03" },
        };

        public static LaternenModell Modell(string prefab)
            => Modelle.FirstOrDefault(m => m.Prefab == prefab);

        /**
         * Wohin das Modell lokal +Z zeigen muss, als Richtung in XZ.
         * Einseitig: der Arm (+Z) in die Leuchtrichtung. Doppelt und rundum:
         * lokal +X auf die Richtung; bei Unitys LookRotation ist rechts =
         * oben x vorn, also vorn = (-R.y, R.x).
         */
        public static float2 Vorwaerts(LaternenPlatz platz, LaternenBauart bauart)
            => bauart == LaternenBauart.Einseitig ? platz.Richtung : new float2(-platz.Richtung.y, platz.Richtung.x);
    }

    /** Was der Spieler eingestellt hat; steht im Bauzettel. */
    public sealed class LaternenOptionen
    {
        public bool Enabled = true;
        public float Abstand = ParkingLanterns.StandardAbstand;
        public string Set = "strasse";
        public string Einzeln = "StreetlightSingle02";
        public string Doppelt = "StreetlightDouble02";

        public string ModellFuer(LaternenPlatz platz) => platz.Doppelt ? Doppelt : Einzeln;
    }

    public sealed class LaternenPlan
    {
        public readonly List<LaternenPlatz> Laternen = new List<LaternenPlatz>();
        public int Paare, Randlaeufe, Zusammengelegt, Ausgeduennt;
    }

    /**
     * LATERNEN AUF DEM PARKPLATZ (Plan des Nutzers vom 2026-10-04).
     *
     * Abgestimmt an sechs echten Bauzetteln im Design "PLT Laternenplanung":
     * - Kappe: Doppelmodell mittig in jeder Gruen-Insel am Ende einer
     *   Doppelreihe.
     * - Mittelstreifen: weitere Doppelmodelle, wenn die Doppelreihe laenger als
     *   der Abstand ist, auf eine Buchtengrenze gerastet.
     * - Ohne Mittelstreifen: auf der Kopf-an-Kopf-Linie, an der Ecke von vier
     *   Buchten.
     * - Randreihe: Einzelmodell mittig im Gruenstreifen hinter der Reihe,
     *   hoechstens 2 m tief, zum Platz gerichtet.
     * - Nie in Fahrgassen, Querstrassen oder Zufahrten; naeher als 4 m wird
     *   zusammengelegt; Randlaternen halten 0,45 x Abstand zu allen anderen
     *   (an schraegen Kanten bringt jedes Treppenstueck sonst zwei mit).
     *
     * Rechnet nur auf dem Layout: Buchten, Grasflaechen und Netzlinien. Die
     * Vorschau und der Bau nehmen denselben Plan.
     */
    public static partial class ParkingLanterns
    {
        public const float StandardAbstand = 30f;
        public const float MinAbstand = 20f, MaxAbstand = 40f;

        private sealed class Bucht
        {
            internal float SMin, SMax, T, Breite;
        }

        private sealed class Lauf
        {
            internal float T0, T1;
            internal readonly List<Bucht> Buchten = new List<Bucht>();
            /** Ein Partner deckt diese Seite - je Lauf, nicht je Reihe. */
            internal bool PartnerOben, PartnerUnten;
        }

        private sealed class Reihe
        {
            internal float SMin, SMax;
            internal readonly List<Bucht> Buchten = new List<Bucht>();
            internal readonly List<Lauf> Laeufe = new List<Lauf>();
        }

        public static LaternenPlan Plan(ParkingLayout layout, float abstand)
        {
            var plan = new LaternenPlan();
            if (layout == null || layout.Bay.Length == 0) return plan;
            var S = math.clamp(abstand, MinAbstand, MaxAbstand);
            var gras = layout.GrassSurface.Where(r => r != null && r.Length >= 3).ToArray();
            var gassen = new List<(float2 A, float2 B)>();
            foreach (var l in layout.AisleLine) if (l != null && l.Length >= 2) gassen.Add((l[0], l[1]));
            foreach (var l in layout.CrossLine) if (l != null && l.Length >= 2) gassen.Add((l[0], l[1]));
            foreach (var n in layout.NetLine)
                if (n.Kind == "aisle" || n.Kind == "cross" || n.Kind == "perimeter" || n.Kind == "entrance")
                    gassen.Add((n.A, n.B));
            var asphalt = (layout.AsphaltSurface ?? Array.Empty<float2[]>()).Where(r => r != null && r.Length >= 3).ToArray();
            // Gruen = Gras, auf dem kein Asphalt liegt. Aeltere Zettel legten Gras unter
            // den ganzen Platz (ALCU: 43 von 54 Buchten im Gras), dann galt die Fahrgasse
            // als Gruenstreifen und Laternen standen auf dem Asphalt.
            bool IstGruen(float2 p)
            {
                var imGras = false;
                foreach (var r in gras) if (InRing(p, r)) { imGras = true; break; }
                if (!imGras) return false;
                foreach (var r in asphalt) if (InRing(p, r)) return false;
                return true;
            }
            bool NahGasse(float2 p, float tol) { foreach (var g in gassen) if (AbstandSegment(p, g.A, g.B) < tol) return true; return false; }
            // Am Grundstuecksrand: ausserhalb des Umrisses oder naeher als `naehe` an
            // seiner Kante. Der gezogene Umriss, NICHT `layout.Ring`: der liegt im Zellenweg auf
            // der Randstrasse, Randreihen lagen ausserhalb und galten beidseitig als Rand.
            var umriss = layout.Grundstueck ?? Array.Empty<float2>();
            bool AmRand(float2 p, float naehe)
            {
                if (umriss.Length < 3) return false;
                if (!InRing(p, umriss)) return true;
                for (var i = 0; i < umriss.Length; i++)
                    if (AbstandSegment(p, umriss[i], umriss[(i + 1) % umriss.Length]) < naehe) return true;
                return false;
            }

            /*
             * AUSRICHTUNGSGRUPPEN MIT EINER ACHSE UND OERTLICHEM NULLPUNKT.
             *
             * Erst projizierte jede Bucht mit ihrer eigenen Achse auf den
             * Weltursprung. Bei 3500 m Abstand verschob dann schon ein
             * Millimeter Rundung an den Ecken die Hinterkante um einen halben
             * Meter, Reihen zerfielen und Paare wurden falsch erkannt
             * (Testfall C: 15 statt 11 Laternen nur durch Runden). CS2 liefert
             * float - genau diese Unschaerfe.
             */
            var rohBuchten = new List<(double Ang, float2 M01, float2 M23, float Breite)>();
            var kurvenbuchten = new HashSet<int>((layout.KurvenRandreihen ?? Array.Empty<KurvenRandreihe>())
                .SelectMany(r => r.Buchten));
            for (int buchtIndex = 0; buchtIndex < layout.Bay.Length; buchtIndex++)
            {
                if (kurvenbuchten.Contains(buchtIndex)) continue;
                var p = layout.Bay[buchtIndex];
                if (p == null || p.Length < 4) continue;
                var m01 = (p[0] + p[1]) * 0.5f;
                var m23 = (p[2] + p[3]) * 0.5f;
                var d = math.normalizesafe(m23 - m01);
                double ang = Math.Atan2(d.y, d.x);
                if (ang < 0) ang += Math.PI;
                if (ang >= Math.PI - 1e-6) ang -= Math.PI;
                rohBuchten.Add((ang, m01, m23, math.distance(p[0], p[1])));
            }
            const double Toleranz = 0.5 * Math.PI / 180;
            double WinkelDiff(double a, double b) { var d = Math.Abs(a - b) % Math.PI; return Math.Min(d, Math.PI - d); }
            var gruppenListe = new List<(double Ang0, List<(double Ang, float2 M01, float2 M23, float Breite)> Buchten)>();
            foreach (var b in rohBuchten.OrderBy(b => b.Ang))
            {
                var i = gruppenListe.FindIndex(g => WinkelDiff(g.Ang0, b.Ang) < Toleranz);
                if (i >= 0) gruppenListe[i].Buchten.Add(b);
                else gruppenListe.Add((b.Ang, new List<(double, float2, float2, float)> { b }));
            }

            var roh = new List<LaternenPlatz>();
            foreach (var gruppe in gruppenListe)
            {
                double sx = 0, sz = 0, a0 = gruppe.Buchten[0].Ang;
                foreach (var b in gruppe.Buchten)
                {
                    var a = b.Ang;
                    if (a - a0 > Math.PI / 2) a -= Math.PI;
                    if (a0 - a > Math.PI / 2) a += Math.PI;
                    sx += Math.Cos(a);
                    sz += Math.Sin(a);
                }
                var dl = Math.Sqrt(sx * sx + sz * sz);
                var dc = new float2((float)(sx / dl), (float)(sz / dl));
                var wc = new float2(-dc.y, dc.x);
                var o = gruppe.Buchten[0].M01;
                var buchten = gruppe.Buchten.Select(b =>
                {
                    var s1 = math.dot(b.M01 - o, dc);
                    var s2 = math.dot(b.M23 - o, dc);
                    return new Bucht
                    {
                        SMin = math.min(s1, s2), SMax = math.max(s1, s2),
                        T = math.dot((b.M01 + b.M23) * 0.5f - o, wc), Breite = b.Breite,
                    };
                }).ToList();
                float2 W(float s, float t) => o + dc * s + wc * t;

                // Reihen: gleiche Hinterkante
                var reihen = new List<Reihe>();
                foreach (var b in buchten.OrderBy(b => b.SMin))
                {
                    var r = reihen.FirstOrDefault(x => math.abs(x.SMin - b.SMin) < 0.3f);
                    if (r == null) { r = new Reihe { SMin = b.SMin, SMax = b.SMax }; reihen.Add(r); }
                    r.Buchten.Add(b);
                }
                // Laeufe: Unterbrechung ueber 0,5 m (Querstrasse, Zufahrt)
                foreach (var r in reihen)
                {
                    Lauf lauf = null;
                    foreach (var b in r.Buchten.OrderBy(b => b.T))
                    {
                        float t0 = b.T - b.Breite / 2, t1 = b.T + b.Breite / 2;
                        if (lauf != null && t0 - lauf.T1 < 0.5f) { lauf.T1 = t1; lauf.Buchten.Add(b); }
                        else { lauf = new Lauf { T0 = t0, T1 = t1 }; lauf.Buchten.Add(b); r.Laeufe.Add(lauf); }
                    }
                }

                // Ruecken-an-Ruecken-Paare: Abstand bis 7 m, dazwischen keine Fahrgasse
                foreach (var A in reihen)
                foreach (var B in reihen)
                {
                    if (A == B) continue;
                    var gap = B.SMin - A.SMax;
                    if (gap < -0.3f || gap > 7f) continue;
                    var sMitte = (A.SMax + B.SMin) / 2;
                    foreach (var la in A.Laeufe)
                    foreach (var lb in B.Laeufe)
                    {
                        float u0 = math.max(la.T0, lb.T0), u1 = math.min(la.T1, lb.T1);
                        if (u1 - u0 < 2.5f) continue; // mind. eine Bucht; 3,0 lag genau auf der Kante
                        if (gap > 0.5f && NahGasse(W(sMitte, (u0 + u1) / 2), 1.5f)) continue;
                        // Gedeckt ist der ganze Lauf - je Lauf, nicht je Reihe: eine Reihe
                        // auf derselben Linie wie eine Doppelreihe galt sonst als gepaart.
                        // Und der ganze Lauf, nicht nur die Ueberlappung: die Paarlinie
                        // reicht ueber beide Laeufe, sonst stand neben jeder ihrer
                        // Laternen noch eine Randlaterne.
                        la.PartnerOben = true;
                        lb.PartnerUnten = true;
                        plan.Paare++;
                        PaarLaternen(roh, la, lb, sMitte, gap > 0.5f, S, dc, W, IstGruen, NahGasse);
                    }
                }

                // Einzelreihen: die Seite ohne Partner. Hinten liegt direkt hinter der
                // Bucht Gruen oder der Grundstuecksrand, vorne der Asphalt der Fahrgasse.
                // Abstaende zur Gassenlinie taugten nicht: Gassen sind 6-7 m breit, und
                // 3,0 m lag genau auf der Kante (Nutzerbefund 2026-10-04).
                foreach (var r in reihen)
                foreach (var lauf in r.Laeufe)
                foreach (var (sRand, d, gedeckt) in new[] { (r.SMax, +1f, lauf.PartnerOben), (r.SMin, -1f, lauf.PartnerUnten) })
                foreach (var (t0, t1) in gedeckt ? Array.Empty<(float, float)>() : new[] { (lauf.T0, lauf.T1) })
                {
                    var tm = (t0 + t1) / 2;
                    var hinten = 0;
                    foreach (var f in new[] { 0.25f, 0.5f, 0.75f })
                    {
                        var q = W(sRand + d * 0.4f, t0 + (t1 - t0) * f);
                        if (IstGruen(q) || AmRand(q, 3f)) hinten++;
                    }
                    if (hinten < 2) continue;
                    float tiefe = 0;
                    for (var k = 1; k <= 160; k++) { if (!IstGruen(W(sRand + d * (k * 0.25f - 0.125f), tm))) break; tiefe = k * 0.25f; }
                    var s = sRand + d * (tiefe >= 1f ? math.min(tiefe / 2, 2f) : 0.5f);
                    // Aussen nur am Grundstuecksrand: dort einseitig, zum Platz gerichtet.
                    // Innen (Einzelreihe vor einem Gruenstreifen) das Doppelmodell - der
                    // Nutzer: die Einseitigen gelten nur fuer aussen.
                    // Aussen heisst: das Gruen hinter der Reihe reicht bis an den Umriss.
                    // Gefragt wird knapp hinter seinem Ende. Eine feste Probe 3 m hinter
                    // der Reihe lag beim 6 m breiten Randband genau auf der 3-m-Schwelle.
                    var aussen = AmRand(W(sRand + d * (tiefe + 0.5f), tm), 2f);
                    var L = t1 - t0;
                    plan.Randlaeufe++;
                    // Kurz: eine Laterne in der Mitte. Lang: Stuecke von hoechstens dem
                    // Abstand, Enden 1,5 m eingerueckt. Mit nur zwei Endlaternen blieb von
                    // kurzen Randabschnitten nach dem Ausduennen an den Ecken nichts uebrig.
                    var stellen = new List<float>();
                    // +0,1: Laeufe sind Vielfache der Buchtbreite und trafen 0,6 x Abstand
                    // genau (sechs Buchten a 3 m bei 30 m).
                    if (L < S * 0.6f + 0.1f) stellen.Add(t0 + L / 2);
                    else
                    {
                        var m = math.max(1, (int)math.ceil((L - 3f) / S - 0.01f));
                        for (var k = 0; k <= m; k++) stellen.Add(t0 + 1.5f + (L - 3f) * k / m);
                    }
                    foreach (var t in stellen)
                        roh.Add(aussen
                            ? new LaternenPlatz { Position = W(s, t), Art = LaternenArt.Rand, Richtung = -d * dc }
                            : new LaternenPlatz { Position = W(s, t), Art = LaternenArt.Streifen, Richtung = dc });
                }
            }

            // Aussen und innen getrennt (Nutzer 2026-10-04: die Randleuchten duerfen
            // nicht beeinflussen, wie innen gesetzt wird). Innen: naeher als 5 m
            // zusammenlegen, eine Kappe gewinnt. Aussen: zusammenlegen und auf
            // 0,45 x Abstand ausduennen - an schraegen Kanten bringt jedes
            // Treppenstueck zwei mit.
            var innen = new List<LaternenPlatz>();
            foreach (var lt in roh.Where(l => l.Art != LaternenArt.Rand))
            {
                var i = innen.FindIndex(f => math.distance(f.Position, lt.Position) < 5f);
                if (i >= 0) { plan.Zusammengelegt++; if (lt.Art == LaternenArt.Kappe) innen[i] = lt; continue; }
                innen.Add(lt);
            }
            var rand = new List<LaternenPlatz>();
            // -0,1: Randlaternen sitzen 1,5 m eingerueckt auf dem Buchtraster, ihre
            // Abstaende sind oft Vielfache von 1,5 m - und 0,45 x 20/30/40 m trifft
            // genau dieses Raster.
            var randAbstand = math.max(4f, 0.45f * S - 0.1f);
            foreach (var lt in roh.Where(l => l.Art == LaternenArt.Rand))
            {
                if (rand.Any(f => math.distance(f.Position, lt.Position) < randAbstand)) { plan.Ausgeduennt++; continue; }
                rand.Add(lt);
            }
            plan.Laternen.AddRange(innen);
            plan.Laternen.AddRange(rand);
            Kurvenlaternen(plan, layout.KurvenRandreihen, S, IstGruen);
            return plan;
        }

        private static void PaarLaternen(List<LaternenPlatz> roh, Lauf la, Lauf lb, float sMitte, bool mitMittel,
            float S, float2 dc, Func<float, float, float2> W, Func<float2, bool> istGruen, Func<float2, float, bool> nahGasse)
        {
            var artInnen = mitMittel ? LaternenArt.Mittelstreifen : LaternenArt.KopfAnKopf;
            float t0 = math.min(la.T0, lb.T0), t1 = math.max(la.T1, lb.T1);
            var kanten = la.Buchten.Concat(lb.Buchten)
                .SelectMany(b => new[] { b.T - b.Breite / 2, b.T + b.Breite / 2 })
                .OrderBy(t => t).ToList();
            // Grenzen beider Reihen zusammenfuehren: naeher als 5 cm = dieselbe.
            // Nicht auf 10 cm runden - Werte bei x,x5 kippten zwischen float und double.
            for (var i = kanten.Count - 1; i > 0; i--) if (kanten[i] - kanten[i - 1] <= 0.05f) kanten.RemoveAt(i);
            float Kappe(float t, float dir)
            {
                float lg = 0;
                for (var k = 1; k <= 48; k++)
                {
                    var q = W(sMitte, t + dir * (k * 0.25f - 0.125f)); // halber Schritt: Grenzen liegen oft auf 0,25-Vielfachen
                    if (!istGruen(q) || nahGasse(q, 2.5f)) break;
                    lg = k * 0.25f;
                }
                return lg;
            }
            var lgA = Kappe(t0, -1);
            var lgB = Kappe(t1, +1);
            var e0 = lgA >= 1.5f ? (T: t0 - math.min(lgA / 2, 3f), Art: LaternenArt.Kappe)
                : (T: kanten.Count > 1 ? kanten[1] : t0 + 3f, Art: artInnen);
            var e1 = lgB >= 1.5f ? (T: t1 + math.min(lgB / 2, 3f), Art: LaternenArt.Kappe)
                : (T: kanten.Count > 1 ? kanten[kanten.Count - 2] : t1 - 3f, Art: artInnen);
            var L = e1.T - e0.T;
            var n = math.max(0, (int)math.ceil(L / S - 0.01f) - 1); // L genau k*S darf nicht kippen
            var innen = new List<float>();
            for (var k = 1; k <= n; k++)
            {
                var ziel = e0.T + L * k / (n + 1);
                var t = kanten[0];
                foreach (var x in kanten) if (math.abs(x - ziel) < math.abs(t - ziel) - 0.05f) t = x; // Gleichstand: die fruehere Grenze
                if (!innen.Any(x => math.abs(x - t) < 1f) && math.abs(t - e0.T) > 4f && math.abs(t - e1.T) > 4f) innen.Add(t);
            }
            roh.Add(new LaternenPlatz { Position = W(sMitte, e0.T), Art = e0.Art, Richtung = dc });
            foreach (var t in innen) roh.Add(new LaternenPlatz { Position = W(sMitte, t), Art = artInnen, Richtung = dc });
            roh.Add(new LaternenPlatz { Position = W(sMitte, e1.T), Art = e1.Art, Richtung = dc });
        }

        private static float AbstandSegment(float2 p, float2 a, float2 b)
        {
            var ab = b - a;
            var l2 = math.dot(ab, ab);
            var t = l2 > 0 ? math.saturate(math.dot(p - a, ab) / l2) : 0;
            return math.distance(p, a + ab * t);
        }

        private static bool InRing(float2 p, float2[] ring)
        {
            var innen = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                float2 a = ring[i], b = ring[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) innen = !innen;
            }
            return innen;
        }
    }
}
