using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry.Zellen;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /**
     * Haupt- und Dekoflaeche als lueckenlose Zerlegung ueber den Zellenkern
     * des Autoparkplatzes (Nutzer 03.11.2026: "Ueberlappung, die passen nicht
     * aneinander, Ecken laufen aufeinander zu und brechen ab ... eigentlich ganz
     * gut hat das System von unseren Parkplaetzen funktioniert").
     *
     * Vorher lag Gras ueber dem ganzen Umriss und darauf Pflastervierecke mit
     * 1 cm Ueberstand. CS2 versetzt jede Flaeche vor dem Zeichnen 0,1 m nach
     * innen (cs2-flaechen-annahme): die Ecken liefen sichtbar zusammen, und die
     * Ueberlappungen zeichneten doppelt. Jetzt wie beim Auto: Umriss konvex
     * zerlegen, an den Pflastergrenzen schneiden, jede Zelle Pflaster oder Gras,
     * gleiche Nachbarn vereinigen, Loecher trennen, CS2-Orakel. Keine Flaeche
     * ueberlappt eine andere, gemeinsame Kanten haben dieselben Knoten.
     */
    public static partial class FahrradGeometrie
    {
        // Pflastergrenzen rasten auf 10 cm (nach aussen): sonst schneiden die
        // 5-mm-Fugen der Bausteine Haarzellen, die CS2 nicht zeichnet.
        private const double Raster = .1;

        /**
         * Liefert false, wenn der Zellenkern scheitert; dann baut der Aufrufer
         * eine einzige Pflasterflaeche wie bei Schlicht und meldet es.
         */
        private static bool Flaechenbau(Raum raum, Variante v, Func<float2[], bool> pruefer,
            out List<float2[]> pflaster, out List<float2[]> gras, out Func<double2, bool> aufPflaster,
            List<string> hinweise)
        {
            pflaster = new List<float2[]>(); gras = new List<float2[]>();
            var bereiche = new List<double2[]>();
            var linien = new Linienregister();
            var knoten = new Knotenfabrik();
            // Achsparallele Pflasterkanten schneiden als Gerade durchs ganze Grundstueck
            // (wie das Raster des Autokerns). Schraege Kanten (Ring, schraege Zufahrt)
            // schneiden nur Zellen in ihrer Naehe: verlaengert schnitten sie weit
            // draussen spitze Splitter, die CS2 verwirft.
            var xs = new SortedSet<double>(); var ys = new SortedSet<double>();
            var achsen = new List<(double X0, double Y0, double X1, double Y1)>();
            var schnitte = new List<(Linie Linie, double2 A, double2 B)>();
            foreach (var stueck in Belag(raum, v, zufahrtBisUeberDenRand: true))
            {
                // Was im Platzring liegt, deckt der Ring selbst.
                if (v.RingAussen != null && stueck.All(e => ParkingGeometry.PointIn(e, v.RingAussen)
                    || ParkingGeometry.DistToBoundary(e, v.RingAussen) < .05)) continue;
                Kasten(stueck, out var x0, out var y0, out var x1, out var y1, out var gerade);
                // Beim Platz schneidet alles nur oertlich: eine durchgehende Zufahrtskante lief
                // quer durch den Ring und schnitt dort Splitter (gemessen x = 23,4 an einer Ringecke).
                if (gerade && v.RingAussen == null)
                {
                    x0 = Math.Round(Math.Floor(x0 / Raster + 1e-6) * Raster, 3); x1 = Math.Round(Math.Ceiling(x1 / Raster - 1e-6) * Raster, 3);
                    y0 = Math.Round(Math.Floor(y0 / Raster + 1e-6) * Raster, 3); y1 = Math.Round(Math.Ceiling(y1 / Raster - 1e-6) * Raster, 3);
                    achsen.Add((x0, y0, x1, y1));
                }
                else
                {
                    bereiche.Add(stueck);
                    Kanten(stueck, schnitte, linien);
                }
            }
            // Kanten unter 30 cm Abstand fallen auf die jeweils aeussere: eine 10-cm-Stufe
            // im Pflasterrand ueberlebt CS2s 0,1-m-Innenversatz nicht (gemessen: 0,15-m2-
            // Dreieck bei 8 km). Nach aussen, damit jeder Baustein bedeckt bleibt.
            // Die Umrissecken zaehlen mit: an der Innenecke eines L blieb sonst eine
            // 10-cm-Stufe zwischen Pflaster (27,9) und Umriss (28,0).
            var gx = Gruppen(achsen.SelectMany(a => new[] { a.X0, a.X1 }).Concat(raum.Ring.Select(q => Math.Round(q.x, 3))));
            var gy = Gruppen(achsen.SelectMany(a => new[] { a.Y0, a.Y1 }).Concat(raum.Ring.Select(q => Math.Round(q.y, 3))));
            foreach (var (ax0, ay0, ax1, ay1) in achsen)
            {
                double x0 = gx[ax0].Min, x1 = gx[ax1].Max, y0 = gy[ay0].Min, y1 = gy[ay1].Max;
                xs.Add(x0); xs.Add(x1); ys.Add(y0); ys.Add(y1);
                bereiche.Add(Rechteck(x0, y0, x1 - x0, y1 - y0));
            }
            // Platz: der Pflasterring wird an zwei geraden Naehten links und rechts geteilt,
            // senkrecht zur Ringkante. Der Lochtrenner schnitt sonst an beliebiger Stelle
            // schraeg durch den Ring, mit spitzen Ecken, die CS2s 0,1-m-Versatz abbricht.
            var naehte = new List<(double2 A, double2 B)>();
            if (v.RingAussen != null)
            {
                var ring = new List<(Linie Linie, double2 A, double2 B)>();
                Kanten(v.RingAussen, ring, linien); Kanten(v.RingInnen, ring, linien);
                var n = v.RingInnen.Length;
                foreach (var k in new[] { n / 4, 3 * n / 4 })
                {
                    var innen = (v.RingInnen[k] + v.RingInnen[(k + 1) % n]) / 2;
                    var aussen = (v.RingAussen[k] + v.RingAussen[(k + 1) % n]) / 2;
                    naehte.Add((innen, aussen));
                    ring.Add((linien.FreieGerade(new Punkt(innen.x, innen.y), new Punkt(aussen.x, aussen.y), Linienart.Innenrand, "Ringnaht"), innen, aussen));
                }
                schnitte.InsertRange(0, ring);
            }

            bool Pflaster(double2 p)
                => bereiche.Any(b => ParkingGeometry.PointIn(p, b))
                    || (v.RingAussen != null && ParkingGeometry.PointIn(p, v.RingAussen)
                        && !ParkingGeometry.PointIn(p, v.RingInnen));
            aufPflaster = Pflaster;
            try
            {
                var umriss = raum.Ring.Select(p => new Punkt(p.x, p.y)).ToList();
                if (Geometrie.Vorzeichenflaeche(umriss) < 0) umriss.Reverse();
                var areal = Polygonfabrik.Areal(umriss, knoten, linien);
                var teile = new Konvexzerlegung(knoten, linien).Zerlege(areal);
                var teiler = new Polygonteiler(knoten);
                foreach (var linie in xs.Select(linien.RasterX).Concat(ys.Select(linien.BandY)))
                    teile = teile.SelectMany(t => teiler.Teile(t, linie)).ToList();
                foreach (var (linie, a, b) in schnitte)
                {
                    double minX = Math.Min(a.x, b.x) - .5, maxX = Math.Max(a.x, b.x) + .5;
                    double minY = Math.Min(a.y, b.y) - .5, maxY = Math.Max(a.y, b.y) + .5;
                    bool Nah(Polygon t) => t.Punkte.Any(q => q.X >= minX && q.X <= maxX && q.Y >= minY && q.Y <= maxY)
                        || (t.Punkte.Min(q => q.X) <= maxX && t.Punkte.Max(q => q.X) >= minX
                            && t.Punkte.Min(q => q.Y) <= maxY && t.Punkte.Max(q => q.Y) >= minY);
                    teile = teile.SelectMany(t => Nah(t) ? teiler.Teile(t, linie) : t.Einzeln()).ToList();
                    teiler.VervollstaendigeNachbarkanten(teile, linie);
                }
                var zellen = teile.Select((t, i) =>
                {
                    var m = Geometrie.Mittelwert(t);
                    var belag = Pflaster(new double2(m.X, m.Y));
                    var material = belag ? Material.Asphalt : Material.Gruen;
                    var art = belag ? Zellart.Restbelag : Zellart.Restgruen;
                    return new Zelle { Id = i, QuellzelleId = i, Polygon = t, Material = material, Art = art,
                        Ursprungsmaterial = material, Ursprungsart = art };
                }).ToList();
                var geschuetzt = v.Teile.Select(t => t.Rect).Concat(v.Wege.Select(w => Streifen(w.A, w.B, w.Breite))).ToList();
                SplitterZuschlagen(zellen, geschuetzt);
                var gesperrt = new HashSet<KantenSchluessel>();
                bool AufNaht((double2 A, double2 B) nt, Punkt q)
                {
                    var d = nt.B - nt.A; var l2 = math.lengthsq(d);
                    var t = math.dot(new double2(q.X, q.Y) - nt.A, d) / l2;
                    return t > -1e-6 && t < 1 + 1e-6 && math.lengthsq(nt.A + d * t - new double2(q.X, q.Y)) < 1e-8;
                }
                // Beide Enden auf DERSELBEN Naht: die zwei Naehte liegen gegenueber auf einer
                // Linie, eine Kante quer durch die gruene Mitte verbindet ihre inneren Enden.
                foreach (var z in zellen)
                    for (var i = 0; i < z.Polygon.Anzahl; i++)
                        if (naehte.Any(nt => AufNaht(nt, z.Polygon.Knoten(i).Punkt) && AufNaht(nt, z.Polygon.Knoten(i + 1).Punkt)))
                            gesperrt.Add(KantenSchluessel.Von(z.Polygon.Knoten(i), z.Polygon.Knoten(i + 1)));
                var vereinigt = Vereinigung.Vereinige(zellen, gesperrt);
                var getrennt = Lochtrenner.Trenne(zellen, vereinigt.Flaechen, new Rahmen(new Punkt(1, 0), new Punkt(0, 1)));
                var asphalt = new List<float2[]>(); var gruen = new List<float2[]>();
                foreach (var f in getrennt.Flaechen)
                {
                    if (f.Loecher.Count > 0) throw new InvalidOperationException("Fahrradflaeche mit Loch nach der Lochtrennung.");
                    var ring = f.Aussenring.Knoten.Select(k => raum.Welt(new double2(k.Punkt.X, k.Punkt.Y))).ToArray();
                    (f.Material == Material.Asphalt ? asphalt : gruen).Add(ring);
                }
                var unbaubar = new List<float2[]>();
                pflaster = ParkingGeometry.PlaneZellenCs2Flaechen(asphalt.ToArray(), true, unbaubar, true).Where(pruefer).ToList();
                gras = ParkingGeometry.PlaneZellenCs2Flaechen(gruen.ToArray(), true, unbaubar, true).Where(pruefer).ToList();
                if (unbaubar.Count > 0)
                    hinweise.Add($"Fahrrad: {unbaubar.Count} Flaechenstueck(e) nimmt CS2 nicht an; sie bleiben leer.");
                return pflaster.Count > 0;
            }
            catch (Exception fehler)
            {
                hinweise.Add("Fahrrad: Flaechenzerlegung gescheitert (" + fehler.Message + "); gebaut wird eine einzige Pflasterflaeche.");
                pflaster.Clear(); gras.Clear();
                return false;
            }
        }

        /**
         * Zellen unter 20 cm Dicke bekommen das Material des Nachbarn mit der
         * laengsten gemeinsamen Kante. Sie entstehen, wo eine Pflasterkante fast
         * parallel zu einer Umriss- oder Ringkante laeuft; als eigene Flaeche
         * verwirft CS2 sie (gemessen: 8-10 cm breite, 3-9 m lange Dreiecke).
         * Die Flaechengrenze wandert dabei hoechstens um die Splitterdicke.
         */
        private static void SplitterZuschlagen(List<Zelle> zellen, List<double2[]> geschuetzt)
        {
            // Pflaster unter Bausteinen und Wegen wird nie zu Gras. Gemessen wird mehr als
            // eine Beruehrung: 1 cm Ueberdeckung, sonst schuetzte jede Kante ihren Splitter.
            bool Traegt(Zelle z)
            {
                var ring = z.Polygon.Punkte.Select(q => new double2(q.X, q.Y)).ToArray();
                var mitte = Mitte(ring);
                var innen = ring.Select(q => q + math.normalizesafe(mitte - q) * .01).ToArray();
                return geschuetzt.Any(r => Ueberlappt(innen, r));
            }
            var kanten = new Dictionary<(int, int), List<Zelle>>();
            (int, int) Schluessel(Knoten a, Knoten b) => a.Id < b.Id ? (a.Id, b.Id) : (b.Id, a.Id);
            foreach (var z in zellen)
                for (var i = 0; i < z.Polygon.Anzahl; i++)
                {
                    var k = Schluessel(z.Polygon.Knoten(i), z.Polygon.Knoten(i + 1));
                    if (!kanten.TryGetValue(k, out var liste)) kanten[k] = liste = new List<Zelle>();
                    liste.Add(z);
                }
            for (var runde = 0; runde < 4; runde++)
            {
                var geaendert = false;
                foreach (var z in zellen)
                {
                    var punkte = z.Polygon.Punkte.ToList();
                    var flaeche = Math.Abs(Geometrie.Vorzeichenflaeche(punkte));
                    var laengste = Enumerable.Range(0, punkte.Count)
                        .Max(i => Geometrie.Laenge(punkte[(i + 1) % punkte.Count] - punkte[i]));
                    if (laengste <= 0 || 2 * flaeche / laengste >= .2) continue;
                    Zelle nachbar = null; var beste = 0.0;
                    for (var i = 0; i < z.Polygon.Anzahl; i++)
                    {
                        var a = z.Polygon.Knoten(i); var b = z.Polygon.Knoten(i + 1);
                        var laenge = Geometrie.Laenge(b.Punkt - a.Punkt);
                        foreach (var n in kanten[Schluessel(a, b)])
                            if (n != z && laenge > beste) { beste = laenge; nachbar = n; }
                    }
                    if (nachbar == null || nachbar.Material == z.Material) continue;
                    if (z.Material == Material.Asphalt && Traegt(z)) continue;
                    z.Material = z.Ursprungsmaterial = nachbar.Material;
                    z.Art = z.Ursprungsart = nachbar.Art;
                    geaendert = true;
                }
                if (!geaendert) break;
            }
        }

        private static void Kanten(double2[] ring, List<(Linie Linie, double2 A, double2 B)> schnitte, Linienregister linien)
        {
            for (var i = 0; i < ring.Length; i++)
            {
                var a = ring[i]; var b = ring[(i + 1) % ring.Length];
                if (math.distancesq(a, b) < 1e-8) continue;
                schnitte.Add((linien.FreieGerade(new Punkt(a.x, a.y), new Punkt(b.x, b.y), Linienart.Innenrand, "Fahrrad"), a, b));
            }
        }

        /** Werte in Ketten mit weniger als 30 cm Abstand; je Wert die Grenzen seiner Kette. */
        private static Dictionary<double, (double Min, double Max)> Gruppen(IEnumerable<double> werte)
        {
            var sortiert = werte.Distinct().OrderBy(w => w).ToList();
            var aus = new Dictionary<double, (double Min, double Max)>();
            for (var i = 0; i < sortiert.Count;)
            {
                var j = i;
                while (j + 1 < sortiert.Count && sortiert[j + 1] - sortiert[j] < .3 - 1e-9) j++;
                for (var k = i; k <= j; k++) aus[sortiert[k]] = (sortiert[i], sortiert[j]);
                i = j + 1;
            }
            return aus;
        }

        private static double2 Mitte(double2[] ring)
        {
            var s = new double2(0, 0);
            foreach (var p in ring) s += p;
            return s / ring.Length;
        }
    }
}
