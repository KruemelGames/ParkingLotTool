using System;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static partial class Strassenkurve
    {
        // Gleiche Startgrenze wie die Abtastung. Keine Aussage ueber den Strassennamen.
        public const double Fortsetzungswinkel = Math.PI / 12;

        public static Grund PruefeFortsetzung(Kante a, Kante b, long node)
        {
            if (!a.HatMittellinie || !b.HatMittellinie || !a.Mittellinie.Endlich || !b.Mittellinie.Endlich)
                return Grund.Ungueltig;
            var ca = a.Mittellinie;
            var cb = b.Mittellinie;
            var an = a.Ende == node ? ca.D - ca.C : a.Start == node ? ca.A - ca.B : default;
            var ab = b.Start == node ? cb.B - cb.A : b.Ende == node ? cb.C - cb.D : default;
            if (math.lengthsq(an.xz) < 1e-12 || math.lengthsq(ab.xz) < 1e-12) return Grund.Ungueltig;
            if (Winkel(an, ab) > Fortsetzungswinkel + 1e-6) return Grund.EckeAndereStrasse;
            return Grund.Keiner;
        }

        /**
         * An einer Kreuzung fuehrt ein Bordstein zu der Strasse, die - von der
         * eigenen aus zur Bordsteinseite hin gedreht - als naechste kommt. An
         * der Gegenseite einer T-Kreuzung ist das die Fortsetzung, an der
         * Einmuendung die einmuendende Strasse. 0 = unbekannt oder nicht eindeutig.
         */
        public static long BordsteinNachbar(Knoten n, long kante, float3 bordstein)
        {
            int eigen = Array.IndexOf(n.Kanten, kante);
            if (eigen < 0 || n.Richtungen == null || n.Richtungen.Length != n.Kanten.Length) return 0;
            var d = n.Richtungen[eigen];
            var s = bordstein.xz - n.Lage.xz;
            double lage = (double)d.x * s.y - (double)d.y * s.x;
            if (math.lengthsq(d) < 1e-12 || Math.Abs(lage) < 1e-6) return 0;
            long beste = 0;
            double kleinste = double.PositiveInfinity;
            bool doppelt = false;
            for (int i = 0; i < n.Kanten.Length; i++)
            {
                var r = n.Richtungen[i];
                if (i == eigen || n.Kanten[i] == kante || math.lengthsq(r) < 1e-12) continue;
                double w = Math.Atan2((double)d.x * r.y - (double)d.y * r.x, (double)d.x * r.x + (double)d.y * r.y);
                if (lage < 0) w = -w;
                if (w <= 0) w += 2 * Math.PI;
                if (Math.Abs(w - kleinste) < 1e-9) { doppelt = true; continue; }
                if (w < kleinste) { kleinste = w; beste = n.Kanten[i]; doppelt = false; }
            }
            return doppelt ? 0 : beste;
        }

        /** Die eine Strasse, die an einer Kreuzung ohne Knick weiterfuehrt; 0 = keine oder mehrere. */
        public static long Weiterfuehrung(Knoten n, long kante)
        {
            int eigen = Array.IndexOf(n.Kanten, kante);
            if (eigen < 0 || n.Richtungen == null || n.Richtungen.Length != n.Kanten.Length) return 0;
            var d = n.Richtungen[eigen];
            if (math.lengthsq(d) < 1e-12) return 0;
            long fund = 0;
            for (int i = 0; i < n.Kanten.Length; i++)
            {
                var r = n.Richtungen[i];
                if (i == eigen || n.Kanten[i] == kante || math.lengthsq(r) < 1e-12) continue;
                if (math.dot(math.normalize(d), math.normalize(r)) > -Math.Cos(Fortsetzungswinkel)) continue;
                if (fund != 0) return 0;
                fund = n.Kanten[i];
            }
            return fund;
        }

        /**
         * AEUSSERE ECKE (Nutzer, 19.10.): Knickt der Strassenzug an einem
         * Grad-2-Knoten, rundet CS2 die Aussenseite mit einem Bogen ab - dem
         * folgt man gern. Innen laufen die Bordsteine spitz zusammen; dort
         * bleibt es eine Ecke. Aussen heisst: der verfolgte Bordstein liegt
         * auf der Gegenseite der Drehrichtung.
         */
        public static bool AussenEcke(Kante a, Kante b, long node, float3 bordstein)
        {
            if (!a.HatMittellinie || !b.HatMittellinie || !a.Mittellinie.Endlich || !b.Mittellinie.Endlich)
                return false;
            var ca = a.Mittellinie;
            var cb = b.Mittellinie;
            var hinein = a.Ende == node ? ca.D - ca.C : a.Start == node ? ca.A - ca.B : default;
            var hinaus = b.Start == node ? cb.B - cb.A : b.Ende == node ? cb.C - cb.D : default;
            var knoten = a.Ende == node ? ca.D : ca.A;
            if (math.lengthsq(hinein.xz) < 1e-12 || math.lengthsq(hinaus.xz) < 1e-12) return false;
            double drehung = (double)hinein.x * hinaus.z - (double)hinein.z * hinaus.x;
            var seite = bordstein - knoten;
            double lage = (double)hinein.x * seite.z - (double)hinein.z * seite.x;
            if (Math.Abs(drehung) < 1e-9 || Math.Abs(lage) < 1e-6) return false;
            return drehung * lage < 0;
        }
    }
}
