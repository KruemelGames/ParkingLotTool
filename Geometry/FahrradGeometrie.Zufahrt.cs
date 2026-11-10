using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /**
     * Vorab beim Setzen einer Zufahrt (Nutzer 03.11.2026): "schon vorher, bevor wir
     * die Einfahrt setzen, schauen, ob die ueberhaupt moeglich ist, und wenn nicht,
     * rot anzeigen und nicht setzen lassen".
     *
     * Moeglich heisst: der gerade Zugang von dieser Stelle erreicht einen Weg der
     * Anlage (Sammelweg, Gang, Laufachse, Ring), ohne Bausteine zu queren. Geprueft
     * wird gegen dieselbe Anlage, die der Bau ohne diese Zufahrt rechnet (Plane) -
     * eine erlaubte Zufahrt aendert also nichts am Raster, sie kommt nur dazu.
     */
    public static partial class FahrradGeometrie
    {
        public sealed class Zufahrtspruefung
        {
            internal Func<Entrance, bool> Pruefer;

            /** Erreicht eine Zufahrt an dieser Stelle einen Weg, ohne Bausteine zu queren? */
            public bool Moeglich(Entrance e) => Pruefer == null || Pruefer(e);
        }

        /** Einmal je Umriss und Einstellung; `settings.Entrances` sind die schon gesetzten Zufahrten. */
        public static Zufahrtspruefung Pruefung(float2[] polygon, LayoutSettings settings)
        {
            if (polygon == null || polygon.Length < 3 || settings == null) return null;
            if (Math.Abs(ParkingGeometry.SignedArea(polygon.Select(p => (double2)p).ToArray())) < .01) return null;
            var stil = Enum.IsDefined(typeof(Fahrradstil), settings.Fahrradstil) ? settings.Fahrradstil : Fahrradstil.Schlicht;
            var gesetzt = (settings.Entrances ?? Array.Empty<Entrance>())
                .Where(e => e != null && e.Edge >= 0 && e.Edge < polygon.Length).ToList();
            var (raum, v, rahmen, aussen, _, _) = Plane(polygon, settings, stil, gesetzt);
            var umriss = polygon.ToArray();
            var rand = aussen + rahmen;
            return new Zufahrtspruefung { Pruefer = e => Moeglich(umriss, raum, v, rand, e) };
        }

        private static bool Moeglich(float2[] polygon, Raum raum, Variante v, double rand, Entrance e)
        {
            if (e == null || e.Edge < 0 || e.Edge >= polygon.Length) return false;
            var z = (Raum: raum, V: v, Rand: rand);
            var (p, n) = Zufahrtspunkt(raum, polygon, e);
            var linien = z.V.Wege.Where(w => !w.Zufahrt).Select(w => (w.A, w.B)).ToList();
            // Ohne innere Wege (flache Randreihe, kleiner Buegelplatz) traegt die Gehflaeche:
            // ein kurzer Stich genuegt, er muss nur ins Grundstueck passen. Er nimmt die
            // Bausteine direkt an der Zufahrt weg wie ein Tor - die erste Reihe laeuft dort
            // lueckenlos ueber die ganze Kante, sonst gaebe es gar keine erlaubte Stelle.
            if (linien.Count == 0)
                return z.Raum.Passt(Streifen(p + n * .05, p + n * (z.Rand + Gang / 2), Gang - .01));
            var platz = z.V.Mitte.Count > 0;
            var richtungen = new List<double2> { n };
            if (platz) richtungen.Add(math.normalizesafe(z.V.Mitte[0] - p, n));
            foreach (var richtung in richtungen)
            {
                if (!Treffer(z.Raum, p, richtung, z.Rand, linien, out var welche, out var ziel)) continue;
                var weg = math.normalize(linien[welche].B - linien[welche].A);
                // Platz: streifend an der Ringkante zaehlt nicht (wie beim Bau).
                if (platz && Math.Abs(math.dot(richtung, weg)) >= Math.Cos(Math.PI / 4)) continue;
                var streifen = Streifen(p, ziel, Gang - .01);
                // Platz: die Aussenreihe am getroffenen Ringstueck faellt beim Bau weg (Tor).
                bool Tor(Teil t) => platz && Math.Abs(Kreuz(t.Zugang - linien[welche].A, weg)) < .01;
                if (z.V.Teile.Any(t => !Tor(t) && Ueberlappt(streifen, t.Rect))) continue;
                return true;
            }
            return false;
        }
    }
}
