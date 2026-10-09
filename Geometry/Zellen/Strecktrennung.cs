using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingLotTool.Geometry.Zellen
{
    /**
     * DIE KONTUR EINER TEILFLAECHE - geteilt an der STRECKE, nicht an der
     * Geraden.
     *
     * Vorher wurde die Innenkontur des ganzen Parkplatzes gegen die
     * Halbebenen der Trennkanten beschnitten (Sutherland-Hodgman). Eine
     * Halbebene reicht aber unendlich weit: an einer U-Form mit Schnitt von
     * der Aussenecke in die Innenecke schnitt sie den ganzen anderen Arm
     * weg, obwohl der gar nicht hinter dem Schnitt liegt. Gemessen am
     * Prueflauf `--teilen`: rechter Arm 46 -> 4 Buchten, der Parkplatz von
     * 165 auf rund 80 Buchten, sobald die beiden Teile verschiedene Winkel
     * hatten. Gefunden von Codex (2026-10-08).
     *
     * Jetzt wird die Kontur nur entlang der Schnittstrecken geteilt, und es
     * bleiben die Stuecke, die im Teil liegen. Die Schnittstrecke laeuft von
     * Umrisspunkt zu Umrisspunkt; ihre Enden liegen also ausserhalb der
     * (eingerueckten) Kontur. Damit wechseln die Treffer entlang der Strecke
     * sauber zwischen "hinein" und "hinaus", und jedes Paar ist eine Sehne
     * im Inneren der Kontur - egal ob die Enden konkave, konvexe oder
     * gestreckte Ecken des Umrisses sind.
     */
    internal static class Strecktrennung
    {
        private const double Eps = 1e-9;

        private struct Ringpunkt
        {
            internal Punkt P;
            internal int Id;
        }

        /**
         * Das groesste Stueck der Kontur, das im Teil liegt; leer, wenn kein
         * Stueck im Teil liegt. `null`, wenn eine Schnittstrecke mit einem
         * Ende in der Kontur liegt - dann gilt die Voraussetzung nicht, und
         * der Aufrufer bleibt beim alten Weg.
         */
        internal static Punkt[] Teilkontur(
            IReadOnlyList<Punkt> kontur,
            IReadOnlyList<(Punkt A, Punkt B)> trennkanten,
            IReadOnlyList<Punkt> teil)
        {
            if (kontur == null || kontur.Count < 3) return Array.Empty<Punkt>();
            var naechsteId = 0;
            var stuecke = new List<List<Ringpunkt>>
            {
                kontur.Select(p => new Ringpunkt { P = p, Id = naechsteId++ }).ToList(),
            };
            foreach (var kante in trennkanten)
            {
                if (Geometrie.Laenge(kante.B - kante.A) < 1e-6) continue;
                if (PunktIn(kante.A, kontur) || PunktIn(kante.B, kontur))
                    return null;
                var neu = new List<List<Ringpunkt>>();
                foreach (var stueck in stuecke)
                    neu.AddRange(Teile(stueck, kante.A, kante.B, ref naechsteId));
                stuecke = neu;
            }

            Punkt[] bestes = null;
            var besteFlaeche = 0.0;
            foreach (var stueck in stuecke)
            {
                var ring = Bereinige(stueck.Select(r => r.P).ToList());
                if (ring.Count < 3) continue;
                var flaeche = Geometrie.Vorzeichenflaeche(ring);
                if (Math.Abs(flaeche) < 1e-6) continue;
                if (flaeche < 0) ring.Reverse();
                if (!PunktIn(InnererPunkt(ring), teil)) continue;
                if (Math.Abs(flaeche) <= besteFlaeche) continue;
                besteFlaeche = Math.Abs(flaeche);
                bestes = ring.ToArray();
            }
            return bestes ?? Array.Empty<Punkt>();
        }

        /**
         * DIE NAHT ZUERST - die Trennkanten des Teils, um `abstand` ins Teil
         * zurueckgesetzt (2026-10-08, Befund von Codex).
         *
         * Das Raster zentriert seine Module in der Teilkontur. Endete die
         * Kontur an der Schnittstrecke, blieb nach dem Zentrieren je Seite
         * ein beliebiger Rest - am Spielfall 3,5185 m - und die Naht nahm
         * davon 3,5 m. Uebrig blieb ein 18,5 mm breiter Grasstreifen, der
         * zwei Rasenstuecke zu EINEM Ring verband; CS2 verwarf den ganzen
         * Ring (94,74 m2 nackter Boden). Endet die Kontur dagegen am
         * Nahtrand, zentriert das Raster im tatsaechlich freien Raum.
         *
         * Jede Trennkante wandert parallel nach innen; ihre neuen Enden sind
         * die Schnittpunkte mit den Geraden der Nachbarkanten (Umrisskanten
         * bleiben, wo sie sind). So liegen die Enden weiter auf dem Umriss,
         * also ausserhalb der Kontur - die Voraussetzung von `Teilkontur`.
         * `null`, wenn das zurueckgesetzte Teil entartet.
         */
        internal static (Punkt[] Teil, (Punkt A, Punkt B)[] Kanten)? Zurueckgesetzt(
            IReadOnlyList<Punkt> teil,
            IReadOnlyList<(Punkt A, Punkt B)> trennkanten,
            double abstand)
        {
            var n = teil.Count;
            if (n < 3 || abstand <= 0) return null;
            bool Gleich(Punkt p, Punkt q) => Geometrie.Laenge(p - q) < 1e-4;
            var istSchnitt = new bool[n];
            for (var i = 0; i < n; i++)
            {
                var a = teil[i];
                var b = teil[(i + 1) % n];
                istSchnitt[i] = trennkanten.Any(k =>
                    (Gleich(k.A, a) && Gleich(k.B, b)) || (Gleich(k.A, b) && Gleich(k.B, a)));
            }
            if (!istSchnitt.Any(x => x)) return null;

            var linien = new (Punkt P, Punkt R)[n];
            for (var i = 0; i < n; i++)
            {
                var a = teil[i];
                var b = teil[(i + 1) % n];
                var r = b - a;
                var l = Geometrie.Laenge(r);
                if (l < 1e-9) return null;
                r = r * (1.0 / l);
                // Gegen den Uhrzeigersinn: links ist innen.
                var normale = new Punkt(-r.Y, r.X);
                linien[i] = (istSchnitt[i] ? a + normale * abstand : a, r);
            }
            var neu = new Punkt[n];
            for (var i = 0; i < n; i++)
            {
                var v = linien[(i - 1 + n) % n];
                var w = linien[i];
                var nenner = Geometrie.Kreuz(v.R, w.R);
                if (Math.Abs(nenner) < 1e-12)
                {
                    neu[i] = w.P;
                    continue;
                }
                var t = Geometrie.Kreuz(w.P - v.P, w.R) / nenner;
                neu[i] = v.P + v.R * t;
            }
            var vorher = Geometrie.Vorzeichenflaeche(teil.ToList());
            var nachher = Geometrie.Vorzeichenflaeche(neu.ToList());
            if (nachher <= 1e-6 || nachher >= vorher) return null;
            var kanten = new List<(Punkt A, Punkt B)>();
            for (var i = 0; i < n; i++)
                if (istSchnitt[i]) kanten.Add((neu[i], neu[(i + 1) % n]));
            return (neu, kanten.ToArray());
        }

        /** Teilt einen Ring entlang der Strecke A-B an allen inneren Sehnen. */
        private static List<List<Ringpunkt>> Teile(
            List<Ringpunkt> ring, Punkt a, Punkt b, ref int naechsteId)
        {
            var richtung = b - a;
            var laenge2 = richtung.X * richtung.X + richtung.Y * richtung.Y;
            // Ein Punkt genau auf der Geraden zaehlt als "links" - so wird
            // jeder Seitenwechsel genau einmal gezaehlt.
            bool Links(Punkt p) => Geometrie.Kreuz(richtung, p - a) >= 0;

            var mitTreffern = new List<Ringpunkt>();
            var treffer = new List<(double S, int Id)>();
            for (var i = 0; i < ring.Count; i++)
            {
                var p = ring[i];
                var q = ring[(i + 1) % ring.Count];
                mitTreffern.Add(p);
                if (Links(p.P) == Links(q.P)) continue;
                var dp = Geometrie.Kreuz(richtung, p.P - a);
                var dq = Geometrie.Kreuz(richtung, q.P - a);
                var t = dp / (dp - dq);
                var x = p.P + (q.P - p.P) * t;
                var s = ((x.X - a.X) * richtung.X + (x.Y - a.Y) * richtung.Y) / laenge2;
                if (s <= Eps || s >= 1 - Eps) continue;
                var id = naechsteId++;
                mitTreffern.Add(new Ringpunkt { P = x, Id = id });
                treffer.Add((s, id));
            }
            if (treffer.Count < 2) return new List<List<Ringpunkt>> { ring };

            // Entlang der Strecke sortiert bilden je zwei Treffer eine Sehne
            // im Inneren - die Enden der Strecke liegen ausserhalb.
            treffer.Sort((x, y) => x.S.CompareTo(y.S));
            var ringe = new List<List<Ringpunkt>> { mitTreffern };
            for (var k = 0; k + 1 < treffer.Count; k += 2)
            {
                var von = treffer[k].Id;
                var nach = treffer[k + 1].Id;
                for (var r = 0; r < ringe.Count; r++)
                {
                    var i = ringe[r].FindIndex(x => x.Id == von);
                    var j = ringe[r].FindIndex(x => x.Id == nach);
                    if (i < 0 || j < 0) continue;
                    var erster = new List<Ringpunkt>();
                    for (var n = i; ; n = (n + 1) % ringe[r].Count)
                    {
                        erster.Add(ringe[r][n]);
                        if (n == j) break;
                    }
                    var zweiter = new List<Ringpunkt>();
                    for (var n = j; ; n = (n + 1) % ringe[r].Count)
                    {
                        zweiter.Add(ringe[r][n]);
                        if (n == i) break;
                    }
                    ringe.RemoveAt(r);
                    ringe.Add(erster);
                    ringe.Add(zweiter);
                    break;
                }
            }
            return ringe;
        }

        /** Doppelte Nachbarpunkte und Punkte ohne Knick entfernen. */
        private static List<Punkt> Bereinige(List<Punkt> ring)
        {
            var aus = new List<Punkt>();
            foreach (var p in ring)
                if (aus.Count == 0 || Geometrie.Laenge(p - aus[aus.Count - 1]) > 1e-7)
                    aus.Add(p);
            while (aus.Count > 1 && Geometrie.Laenge(aus[0] - aus[aus.Count - 1]) <= 1e-7)
                aus.RemoveAt(aus.Count - 1);
            var geaendert = true;
            while (geaendert && aus.Count >= 3)
            {
                geaendert = false;
                for (var i = 0; i < aus.Count; i++)
                {
                    var p = aus[(i + aus.Count - 1) % aus.Count];
                    var q = aus[i];
                    var r = aus[(i + 1) % aus.Count];
                    var u = q - p;
                    var v = r - q;
                    var lu = Geometrie.Laenge(u);
                    var lv = Geometrie.Laenge(v);
                    if (lu < 1e-7 || lv < 1e-7
                        || (Math.Abs(Geometrie.Kreuz(u, v)) <= 1e-9 * lu * lv
                            && u.X * v.X + u.Y * v.Y > 0))
                    {
                        aus.RemoveAt(i);
                        geaendert = true;
                        break;
                    }
                }
            }
            return aus;
        }

        private static Punkt InnererPunkt(IReadOnlyList<Punkt> ring)
        {
            var summe = new Punkt(0, 0);
            foreach (var p in ring) summe = summe + p;
            var mitte = summe * (1.0 / ring.Count);
            if (PunktIn(mitte, ring)) return mitte;
            // Ohr-Spitze: Mitte des Dreiecks an einer konvexen Ecke, wenn
            // keine andere Ecke darin liegt.
            for (var i = 0; i < ring.Count; i++)
            {
                var p = ring[(i + ring.Count - 1) % ring.Count];
                var q = ring[i];
                var r = ring[(i + 1) % ring.Count];
                if (Geometrie.Kreuz(q - p, r - q) <= 0) continue;
                var kandidat = (p + q + r) * (1.0 / 3);
                if (PunktIn(kandidat, ring)) return kandidat;
            }
            return mitte;
        }

        private static bool PunktIn(Punkt p, IReadOnlyList<Punkt> ring)
        {
            var innen = false;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                var a = ring[i];
                var b = ring[j];
                if ((a.Y > p.Y) != (b.Y > p.Y)
                    && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                    innen = !innen;
            }
            return innen;
        }
    }
}
