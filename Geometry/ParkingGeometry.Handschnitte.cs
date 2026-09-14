using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /**
     * Ein vom Nutzer gezogener Trennschnitt zwischen zwei Polygonecken.
     *
     * Gemerkt werden die WELTLAGEN der beiden Ecken, nicht ihre Nummern. Eine
     * Nummer ueberlebt das Einfuegen oder Loeschen eines Punktes nicht - genau
     * daran sind in diesem Projekt schon die Zugaenge gescheitert.
     */
    public sealed class Teilflaechenschnitt
    {
        public float2 A { get; set; }
        public float2 B { get; set; }

        internal Teilflaechenschnitt Clone()
            => new Teilflaechenschnitt { A = A, B = B };
    }

    public static partial class ParkingGeometry
    {
        /**
         * DIE TEILFLAECHEN AUS DEN SCHNITTEN DES NUTZERS.
         *
         * Entscheidung des Nutzers vom 2026-09-01, nachdem die automatische
         * Zerlegung an seiner schiefen T-Form vorbeigeschnitten hatte: *"Es
         * waere gut, wenn der User am besten selbst bestimmen kann, wo
         * geschnitten wird - rein nur fuer das Align der Teilflaechen."*
         *
         * Liegt auch nur EIN Schnitt vor, gilt allein er: die automatische
         * Zerlegung wird nicht mehr befragt. Ohne Schnitt bleibt der ganze
         * Umriss eine Flaeche. Das ist der Vertrag - was gezeichnet ist, gilt.
         *
         * DAS VERFAHREN ist bewusst das einfachste, das traegt: jeder Schnitt
         * verbindet zwei Ecken DESSELBEN Rings und zerteilt ihn in zwei. Weil
         * sich Schnitte nicht kreuzen duerfen (das prueft die Werkzeugseite,
         * bevor ein Schnitt entsteht), liegt jeder Schnitt nach dem Teilen
         * vollstaendig in genau einem der Teilringe - die Reihenfolge der
         * Schnitte spielt fuer das Ergebnis also keine Rolle.
         */
        internal static double2[][] TeilflaechenAusSchnitten(
            double2[] site,
            IReadOnlyList<Teilflaechenschnitt> schnitte)
        {
            if (site == null || site.Length < 3)
                return new[] { site ?? Array.Empty<double2>() };
            if (schnitte == null || schnitte.Count == 0)
                return new[] { site };

            var ringe = new List<List<double2>> { new List<double2>(site) };
            foreach (var schnitt in schnitte)
            {
                if (schnitt == null) continue;
                var a = new double2(schnitt.A.x, schnitt.A.y);
                var b = new double2(schnitt.B.x, schnitt.B.y);
                for (var r = 0; r < ringe.Count; r++)
                {
                    var ring = ringe[r];
                    var i = NaechsteEcke(ring, a);
                    var j = NaechsteEcke(ring, b);
                    if (i < 0 || j < 0 || i == j) continue;
                    // Nur benachbart? Dann trennt der Schnitt nichts ab - er
                    // liegt auf einer vorhandenen Kante.
                    if ((i + 1) % ring.Count == j || (j + 1) % ring.Count == i)
                        continue;

                    var erster = new List<double2>();
                    for (var k = i; ; k = (k + 1) % ring.Count)
                    {
                        erster.Add(ring[k]);
                        if (k == j) break;
                    }
                    var zweiter = new List<double2>();
                    for (var k = j; ; k = (k + 1) % ring.Count)
                    {
                        zweiter.Add(ring[k]);
                        if (k == i) break;
                    }
                    if (erster.Count < 3 || zweiter.Count < 3) continue;
                    ringe[r] = erster;
                    ringe.Insert(r + 1, zweiter);
                    break;
                }
            }

            var ausgabe = new List<double2[]>(ringe.Count);
            foreach (var ring in ringe)
                if (ring.Count >= 3) ausgabe.Add(ring.ToArray());
            return ausgabe.Count == 0 ? new[] { site } : ausgabe.ToArray();
        }

        /**
         * Die Ecke dieses Rings, die dem gemerkten Ort am naechsten liegt -
         * oder -1, wenn keine nah genug ist.
         *
         * Eine halbe Rasterweite Toleranz: der Punkt kann sich seit dem
         * Zeichnen bewegt haben, aber er soll nicht auf einen ANDEREN Punkt
         * umspringen. Liegt er zu weit weg, gilt der Schnitt in diesem Ring
         * nicht.
         */
        private static int NaechsteEcke(IReadOnlyList<double2> ring, double2 ort)
        {
            var beste = -1;
            var abstand = 0.25;
            for (var i = 0; i < ring.Count; i++)
            {
                var d = Len(ring[i] - ort);
                if (d > abstand) continue;
                abstand = d;
                beste = i;
            }
            return beste;
        }

        /**
         * Liegt die Strecke zwischen zwei Ecken ganz IM Umriss?
         *
         * Bei einem L verbindet die Strecke zwischen den beiden Enden zwei
         * echte Ecken - und laeuft trotzdem durch die Luft. Ein solcher
         * Schnitt trennt nichts, er erzeugt Unsinn.
         *
         * Geprueft wird zweifach: die Strecke darf keine Umrisskante kreuzen,
         * und ihre Mitte muss innen liegen. Die erste Pruefung allein liesse
         * eine Strecke durch, die im Aussenbereich einer Einbuchtung verlaeuft
         * ohne eine Kante zu schneiden; die zweite allein liesse eine
         * durchgehen, die aussen herum wieder hereinkommt.
         */
        internal static bool SchnittLiegtInnen(double2[] site, int i, int j)
        {
            if (site == null || site.Length < 4) return false;
            if (i < 0 || j < 0 || i >= site.Length || j >= site.Length) return false;
            if (i == j) return false;
            if ((i + 1) % site.Length == j || (j + 1) % site.Length == i)
                return false;

            var a = site[i];
            var b = site[j];
            for (var k = 0; k < site.Length; k++)
            {
                // Die vier Kanten an den beiden Enden teilen einen Punkt mit
                // der Strecke - sie koennen sie nicht "kreuzen".
                var l = (k + 1) % site.Length;
                if (k == i || l == i || k == j || l == j) continue;
                if (StreckenKreuzen(a, b, site[k], site[l])) return false;
            }
            return PointIn((a + b) * 0.5, site);
        }

        private static bool StreckenKreuzen(
            double2 a1, double2 a2, double2 b1, double2 b2)
        {
            double Seite(double2 p, double2 q, double2 r)
                => (q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x);

            var d1 = Seite(a1, a2, b1);
            var d2 = Seite(a1, a2, b2);
            var d3 = Seite(b1, b2, a1);
            var d4 = Seite(b1, b2, a2);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
                && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }
    }
}
