using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /**
     * Gestaltung nach niederlaendischem Vorbild (Naarden-Bussum, Nutzer 03.11.2026).
     *
     *  - Schlicht: eine Flaeche ueber den ganzen Umriss, kein Gruen.
     *  - Gruener Rahmen: 1,5 m Rand ringsum, Baeume alle 9 m und Buesche im
     *    Rand, offen nur an den Zufahrten. Im Feld steht nur der Hauptbaustein
     *    im Raster; was nicht passt, wird gruen statt mit kleineren Bausteinen
     *    gestopft.
     *  - Platz: derselbe Rand, im Feld ein Ring aus geraden Wegstuecken um eine
     *    bepflanzte Mitte, Bausteine beidseitig am Ring.
     *
     * Nur zwei Flaechen (Nutzer 03.11.): Die Dekoflaeche (Gras) liegt ueber
     * dem ganzen Umriss, die Hauptflaeche (Pflaster) nur unter Wegen und
     * Bausteinen. Pflaster zeichnet ueber Gras (PLT-Klone -90 gegen -94), also
     * braucht das Gras keine Loecher.
     */
    public static partial class FahrradGeometrie
    {
        /** Rahmen der Planungsbilder und Tests; im Spiel kommt die Breite vom Randabstand. */
        internal const double Rahmenbreite = 1.5;
        private const double BaumabstandRahmen = 9;
        private const double BuschabstandRahmen = 1.5;
        // Bepflanzte Mitte des Platzes: Halbmesser mindestens 3 m (Baumkrone),
        // hoechstens 8 m, sonst wird die Mitte zur Wiese (Vanilla-Ring 10,5 m).
        private const double MitteMin = 3, MitteMax = 8;

        private static bool Gruen(Fahrradstil stil) => stil != Fahrradstil.Schlicht;

        // ---------------------------------------------------------- Platz

        /**
         * Ring als regelmaessiges Vieleck aus geraden Wegstuecken, eine Seite
         * zur Zufahrt. Die Mitte liegt am Ort mit dem groessten Randabstand.
         * Liefert null, wenn der Ring nicht passt oder die Zufahrt ihn nicht
         * erreicht; dann baut der Aufrufer den gruenen Rahmen.
         */
        private static Variante Ring(Raum raum, double2 zugang, double rand, bool hatZufahrt,
            Baustein haupt, List<(double2 P, double2 N)> weitere)
        {
            var t = haupt.Tiefe;
            if (!Mittelpunkt(raum, zugang, rand, out var c, out var frei)) return null;
            var n = 12; var a = 0.0;
            for (var i = 0; i < 3; i++)
            {
                a = Math.Min(MitteMax + Gang / 2 + Luft + t, frei * Math.Cos(Math.PI / n) - Gang / 2 - Luft - t);
                n = Math.Max(8, Math.Min(16, (int)Math.Round(2 * Math.PI * a / 6)));
            }
            if (a - Gang / 2 - Luft - t < MitteMin) return null;
            var rv = a / Math.Cos(Math.PI / n);
            var ecken = new double2[n];
            for (var i = 0; i < n; i++)
            {
                // Seite 0 liegt mittig vor der Zufahrt (lokal -Y).
                var w = -Math.PI / 2 - Math.PI / n + 2 * Math.PI * i / n;
                ecken[i] = c + rv * new double2(Math.Cos(w), Math.Sin(w));
            }
            var v = new Variante();
            v.Mitte.Add(c);
            // Pflasterring: innen bis hinter die Innenreihe, aussen bis hinter die Aussenreihe.
            v.RingAussen = Vieleck(c, n, a + Gang / 2 + Luft + t);
            v.RingInnen = Vieleck(c, n, a - Gang / 2 - Luft - t);
            var linien = new List<(double2 A, double2 B)>();
            for (var i = 0; i < n; i++)
            {
                var p = ecken[i]; var q = ecken[(i + 1) % n];
                if (!raum.Passt(Streifen(p, q, Gang), rand)) return null;
                linien.Add((p, q));
                v.Sperren.Add(Streifen(p, q, Gang));
                v.Wege.Add(new Weg { A = p, B = q, Breite = Gang, Art = Fahrradwegart.Fahrradweg, Kappe = true });
            }
            // Erst gerade ins Grundstueck wie bei den Reihen, sonst auf die Mitte zu.
            // Am getroffenen Stueck bleibt die Aussenseite frei: sonst stehen links und
            // rechts vom Zugang einzelne Reste (Nutzer: keine zufaelligen Einzelstaender).
            var tor = new HashSet<int>();
            // Streifend (unter 45 Grad zur Ringkante) zaehlt nicht: die Zufahrt laege fast
            // parallel neben der Kante und schnitte spitze Splitter.
            bool Steil(double2 richtung, int welche)
                => welche >= 0 && Math.Abs(math.dot(richtung, math.normalize(linien[welche].B - linien[welche].A))) < Math.Cos(Math.PI / 4);
            bool Anschluss(double2 p, double2 senkrecht, out double2 ziel)
            {
                var zurMitte = math.normalizesafe(c - p, senkrecht);
                var ok = (Treffer(raum, p, senkrecht, rand, linien, out var welche, out ziel) && Steil(senkrecht, welche))
                    || (Treffer(raum, p, zurMitte, rand, linien, out welche, out ziel) && Steil(zurMitte, welche));
                if (ok) tor.Add(welche);
                return ok;
            }
            if (hatZufahrt)
            {
                if (!Anschluss(zugang, new double2(0, 1), out var ziel)) return null;
                v.Sperren.Add(Streifen(zugang, ziel, Gang));
                v.Wege.Add(new Weg { A = zugang, B = ziel, Breite = Gang, Art = Fahrradwegart.Fahrradweg, Zufahrt = true });
            }
            foreach (var (p, senkrecht) in weitere)
            {
                if (!Anschluss(p, senkrecht, out var ziel))
                { v.Hinweise.Add("Fahrrad: eine weitere Zufahrt erreicht keinen Weg."); continue; }
                v.Sperren.Add(Streifen(p, ziel, Gang));
                v.Wege.Add(new Weg { A = p, B = ziel, Breite = Gang, Art = Fahrradwegart.Fahrradweg, Zufahrt = true });
            }
            // Bausteine beidseitig an jedem Ringstueck, mittig verteilt.
            var schritt = haupt.Laenge + Luft;
            for (var nr = 0; nr < linien.Count; nr++)
            {
                var (p, q) = linien[nr];
                var l = math.distance(p, q); var u = (q - p) / l;
                var innen = new double2(-u.y, u.x);
                if (math.dot(c - p, innen) < 0) innen = -innen;
                var k = (int)Math.Floor((l + Luft) / schritt);
                var start = (l - k * schritt + Luft) / 2;
                foreach (var seite in tor.Contains(nr) ? new[] { innen } : new[] { innen, -innen })
                    for (var j = 0; j < k; j++)
                    {
                        var s = start + j * schritt + Rest(haupt);
                        var laenge = haupt.Laenge - 2 * Rest(haupt);
                        var p0 = p + u * s + seite * (Gang / 2 + Luft);
                        var rect = new[] { p0, p0 + u * laenge, p0 + u * laenge + seite * t, p0 + seite * t };
                        if (!Frei(raum, v, rect, rand)) continue;
                        v.Teile.Add(new Teil { B = haupt, Rect = rect, Zugang = p + u * (s + laenge / 2), ZumWeg = -seite });
                    }
            }
            return v.Teile.Count == 0 ? null : v;
        }

        /** Regelmaessiges Vieleck mit Inkreisradius `apothem`, Seite 0 zur Zufahrt (wie die Ringecken). */
        private static double2[] Vieleck(double2 c, int n, double apothem)
        {
            var r = apothem / Math.Cos(Math.PI / n);
            return Enumerable.Range(0, n).Select(i =>
            {
                var w = -Math.PI / 2 - Math.PI / n + 2 * Math.PI * i / n;
                return c + r * new double2(Math.Cos(w), Math.Sin(w));
            }).ToArray();
        }

        /** Punkt mit dem groessten Abstand zum Rand; bei Gleichstand naeher an der Zufahrt. */
        private static bool Mittelpunkt(Raum raum, double2 zugang, double rand, out double2 mitte, out double frei)
        {
            mitte = default; frei = 0;
            var schritt = Math.Max(.5, Math.Max(raum.MaxX - raum.MinX, raum.MaxY - raum.MinY) / 120);
            var kandidaten = new List<(double2 P, double D)>();
            for (var y = raum.MinY + schritt / 2; y < raum.MaxY; y += schritt)
                for (var x = raum.MinX + schritt / 2; x < raum.MaxX; x += schritt)
                {
                    var p = new double2(x, y);
                    if (ParkingGeometry.PointIn(p, raum.Ring))
                        kandidaten.Add((p, ParkingGeometry.DistToBoundary(p, raum.Ring) - rand));
                }
            if (kandidaten.Count == 0) return false;
            var best = kandidaten.Max(k => k.D);
            if (best <= 0) return false;
            // Gleich gut heisst: hoechstens eine Rasterweite schlechter.
            var wahl = kandidaten.Where(k => k.D >= best - schritt)
                .OrderBy(k => math.distance(k.P, zugang)).First();
            mitte = wahl.P; frei = wahl.D;
            return true;
        }

        // --------------------------------------------------- Hauptflaeche

        /**
         * Pflasterstuecke unter Wegen und Bausteinen, als konvexe Vierecke.
         * Wege reichen bis ans Ende der letzten Bausteinreihe; Bausteine
         * werden zu Reihen zusammengefasst und bis zu ihrem Zugang verlaengert,
         * damit zwischen Reihe und Weg kein Grasstreifen bleibt. 1 cm Ueberstand
         * schliesst die 5-mm-Fugen.
         */
        private static List<double2[]> Belag(Raum raum, Variante v, bool zufahrtBisUeberDenRand = false)
        {
            var stuecke = new List<double2[]>();
            void Dazu(double2[] r, double2[] ersatz = null)
            {
                // Gleicher Umlaufsinn wie der Belag (positive Flaeche), sonst verwirft CS2 das Stueck.
                if (ParkingGeometry.SignedArea(r) < 0) Array.Reverse(r);
                if (ersatz != null && ParkingGeometry.SignedArea(ersatz) < 0) Array.Reverse(ersatz);
                if (raum.Passt(r)) stuecke.Add(r);
                else if (ersatz != null && raum.Passt(ersatz)) stuecke.Add(ersatz);
            }
            foreach (var w in v.Wege)
            {
                // Platz: die Ringwege liegen im Pflasterring; ihre ueber die Ecken
                // verlaengerten Stuecke machten aussen Zacken.
                if (w.Kappe && v.RingAussen != null) continue;
                var d = w.B - w.A; var l = math.length(d);
                if (l < Epsilon) continue;
                d /= l;
                double vor = 0, nach = 0;
                // Bausteine, die seitlich direkt am Weg stehen (Sammelweg: die erste Reihe
                // jeder Spalte), bekommen Pflaster bis unter sich - sonst blieb an den
                // aeusseren Spalten eine Grasstufe (Nutzerbericht 03.11., "Stufenecke").
                if (!w.Zufahrt)
                {
                    var quer = new double2(-d.y, d.x);
                    foreach (var t in v.Teile)
                    {
                        var q = Bereich(t.Rect, quer); var a = math.dot(w.A, quer);
                        var oben = q.Von - a; var unten = a - q.Bis;
                        if (Math.Abs(oben - w.Breite / 2) > .05 && Math.Abs(unten - w.Breite / 2) > .05) continue;
                        var s = Bereich(t.Rect, d); var a0 = math.dot(w.A, d);
                        if (s.Von - a0 < 0 && s.Von - a0 > -6) vor = Math.Max(vor, a0 - s.Von);
                        if (s.Bis - a0 > l && s.Bis - a0 < l + 6) nach = Math.Max(nach, s.Bis - a0 - l);
                    }
                }
                if (!w.Zufahrt)
                    foreach (var t in v.Teile)
                    {
                        // Nur Bausteine, deren Zugang AUF diesem Weg liegt.
                        var auf = math.dot(t.Zugang - w.A, d);
                        if (Abstand(t.Zugang, w.A, d) > .01 || auf < -.01 || auf > l + .01
                            || Math.Abs(math.dot(t.ZumWeg, d)) > .01) continue;
                        foreach (var e in t.Rect)
                        {
                            var s = math.dot(e - w.A, d);
                            if (s < 0 && s > -4) vor = Math.Max(vor, -s);
                            if (s > l && s < l + 4) nach = Math.Max(nach, s - l);
                        }
                    }
                if (w.Kappe) { vor = Math.Max(vor, w.Breite / 2); nach = Math.Max(nach, w.Breite / 2); }
                var roh = Streifen(w.A, w.B, w.Breite + .02);
                if (w.Zufahrt && zufahrtBisUeberDenRand)
                {
                    // Zellenbau: der Umriss schneidet die Zufahrt selbst ab, genau an der
                    // Kante. Ein kuerzerer Streifen liess dort einen spitzen Grassplitter.
                    stuecke.Add(Streifen(w.A - d * 2, w.B, w.Breite + .02));
                    continue;
                }
                if (w.Zufahrt)
                {
                    // An einer schraegen oder gebogenen Kante ragt der Streifen hinaus:
                    // dann beginnt das Pflaster ein Stueck weiter innen.
                    for (var ab = 0.0; ab < Math.Min(3, l); ab += .1)
                    {
                        var r = Streifen(w.A + d * ab, w.B, w.Breite + .02);
                        if (!raum.Passt(r)) continue;
                        Dazu(r); break;
                    }
                    continue;
                }
                Dazu(Streifen(w.A - d * vor, w.B + d * nach, w.Breite + .02), roh);
            }
            // Reihen: gleiche Zugangsrichtung, gleiche Tiefe, Abstand hoechstens 0,9 m.
            var gruppen = v.Teile.GroupBy(t => (Math.Round(t.ZumWeg.x * 1000), Math.Round(t.ZumWeg.y * 1000),
                Math.Round(Bereich(t.Rect, t.ZumWeg).Von * 100), Math.Round(Bereich(t.Rect, t.ZumWeg).Bis * 100)));
            foreach (var g in gruppen)
            {
                var z = g.First().ZumWeg; var quer = new double2(-z.y, z.x);
                var reihe = g.OrderBy(t => Bereich(t.Rect, quer).Von).ToList();
                var i = 0;
                while (i < reihe.Count)
                {
                    var j = i; var bis = Bereich(reihe[i].Rect, quer).Bis;
                    while (j + 1 < reihe.Count && Bereich(reihe[j + 1].Rect, quer).Von - bis <= .9)
                        bis = Math.Max(bis, Bereich(reihe[++j].Rect, quer).Bis);
                    var von = Bereich(reihe[i].Rect, quer).Von;
                    var tief = Bereich(reihe[i].Rect, z);
                    // Bis zum Zugang, mindestens 0,4 m (Reihenabstand bei D).
                    var vorn = Math.Max(.4, math.dot(reihe[i].Zugang, z) - tief.Bis);
                    Dazu(Viereck(quer, z, von - .01, bis + .01, tief.Von - .01, tief.Bis + vorn + .01),
                        Viereck(quer, z, von - .01, bis + .01, tief.Von - .01, tief.Bis + .01));
                    i = j + 1;
                }
            }
            return stuecke;
        }

        private static double Abstand(double2 p, double2 a, double2 d)
            => Math.Abs(Kreuz(p - a, d));

        private static (double Von, double Bis) Bereich(double2[] r, double2 achse)
        {
            var w = r.Select(p => math.dot(p, achse)).ToArray();
            return (w.Min(), w.Max());
        }

        private static double2[] Viereck(double2 x, double2 y, double x0, double x1, double y0, double y1)
            => new[] { x * x0 + y * y0, x * x1 + y * y0, x * x1 + y * y1, x * x0 + y * y1 };

        // ------------------------------------------------------- Bepflanzung

        /**
         * Baeume alle 9 m und Buesche alle 1,5 m auf der Mittellinie des
         * Rahmens. Lage aus dem Umfang abgetragen, damit auch Bogenkanten aus
         * vielen kurzen Stuecken ihren Takt behalten.
         */
        private static List<double2> Rahmenpunkte(Raum raum, double abstandVomRand, double takt)
        {
            var ring = raum.Ring; var n = ring.Length;
            var laengen = new double[n]; var gesamt = 0.0;
            for (var i = 0; i < n; i++) { laengen[i] = math.distance(ring[i], ring[(i + 1) % n]); gesamt += laengen[i]; }
            var punkte = new List<double2>();
            if (gesamt < takt) return punkte;
            var kante = 0; var bisher = 0.0;
            for (var s = takt / 2; s < gesamt; s += takt)
            {
                while (kante < n - 1 && s > bisher + laengen[kante]) bisher += laengen[kante++];
                if (laengen[kante] < Epsilon) continue;
                var a = ring[kante]; var d = (ring[(kante + 1) % n] - a) / laengen[kante];
                var q = a + d * (s - bisher);
                var normal = new double2(-d.y, d.x);
                if (!raum.Innen(q + normal * .05)) normal = -normal;
                var p = q + normal * abstandVomRand;
                if (!ParkingGeometry.PointIn(p, ring)
                    || ParkingGeometry.DistToBoundary(p, ring) < abstandVomRand - .05) continue;
                punkte.Add(p);
            }
            return punkte;
        }
    }
}
