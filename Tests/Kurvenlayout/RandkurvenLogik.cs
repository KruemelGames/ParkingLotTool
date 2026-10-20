using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static class RandkurvenLogik
{
    private static int _anzahl;
    private static void Pruefe(bool bedingung, string name)
    {
        _anzahl++;
        if (!bedingung) throw new Exception(name);
    }

    internal static void Laufe()
    {
        var fall = RandkurvenFaelle.Alle().First(f => f.Name == "R30/90/True/False");
        var s = fall.Settings.Clone();
        Pruefe(s.Kurvenlaeufe.Length == 0, "Leere Herkunft aktiviert keine Kurvenreihe");
        s.Kurvenlaeufe = new[] { fall.Lauf.Clone() };
        var kopie = s.Clone();
        Pruefe(kopie.Kurvenlaeufe[0].InhaltGleich(s.Kurvenlaeufe[0]), "Clone erhaelt Kurvenherkunft");
        kopie.Kurvenlaeufe[0].Kontrollpunkte[1] += new float3(1, 0, 0);
        Pruefe(!kopie.Kurvenlaeufe[0].InhaltGleich(s.Kurvenlaeufe[0]), "Clone trennt die Kurvenformel");
        var normal = ParkingGeometry.Build(fall.Polygon, s);
        Pruefe(normal.KurvenRandreihen.Sum(r => r.Buchten.Length) > 0, "Explizite Herkunft erzeugt radiale Buchten");
        int n = fall.Polygon.Length;
        var umgekehrt = fall.Polygon.Reverse().ToArray();
        var r = s.Clone();
        r.Kurvenlaeufe[0].Von = n - 1 - (fall.Lauf.Von + fall.Lauf.Kanten) % n;
        Array.Reverse(r.Kurvenlaeufe[0].Kontrollpunkte);
        var gegen = ParkingGeometry.Build(umgekehrt, r);
        Pruefe(normal.Bay.Length == gegen.Bay.Length, "CW/CCW gleiche Buchtzahl");
        for (int i = 0; i < normal.Bay.Length; i++)
            Pruefe(normal.Bay[i].SequenceEqual(gegen.Bay[i]), "CW/CCW bitgleiche ganze Rechtecke");

        foreach (var variante in new[] {
            new KurvenRandlauf { Von = -1, Kanten = 1, Kontrollpunkte = fall.Lauf.Kontrollpunkte },
            new KurvenRandlauf { Von = 0, Kanten = n, Kontrollpunkte = fall.Lauf.Kontrollpunkte },
            new KurvenRandlauf { Von = 0, Kanten = 1, Kontrollpunkte = new[] { new float3(float.NaN) } } })
        {
            r.Kurvenlaeufe = new[] { variante };
            bool abgelehnt = false;
            try { ParkingGeometry.Build(fall.Polygon, r); }
            catch (ArgumentException) { abgelehnt = true; }
            Pruefe(abgelehnt, "Defekte explizite Kurvenherkunft ablehnen");
        }

        // Ein enger Bogen: 20 m entlang der Linie, weniger als 9 m durch
        // die Luft. Eine Luftlinien-Ausduennung wuerde die zweite entfernen.
        var punkte = new List<float2>(); var stationen = new List<double>();
        var richtungen = new List<float2>();
        const int proben = 1000;
        for (int i = 0; i <= proben; i++)
        {
            double winkel = 330 * Math.PI / 180 * i / proben;
            punkte.Add(new float2((float)(4 * Math.Cos(winkel)), (float)(4 * Math.Sin(winkel))));
            richtungen.Add(math.normalizesafe(-punkte[^1]));
            stationen.Add(4 * winkel);
        }
        var layout = new ParkingLayout
        {
            Bay = new[] { new[] { new float2(0, 0), new float2(3, 0), new float2(3, 5.9f), new float2(0, 5.9f) } },
            GrassSurface = new[] { new[] { new float2(-10, -10), new float2(10, -10), new float2(10, 10), new float2(-10, 10) } },
            KurvenRandreihen = new[] { new KurvenRandreihe { Buchten = new[] { 0 },
                Standlinie = punkte.ToArray(), Innenrichtungen = richtungen.ToArray(), Stationen = stationen.ToArray() } },
        };
        var lampen = ParkingLanterns.Plan(layout, 20).Laternen;
        Pruefe(lampen.Count == 2, "Laternen messen und verduennen entlang des Bogens");
        Pruefe(math.distance(lampen[0].Position, lampen[1].Position) < 9,
            "Laternen-Gegenprobe unterscheidet Bogen und Luftlinie");
        Pruefe(!ParkingLotTool.Geometry.Zellen.Kurvenrandplan.IstGruenkeil(1, 0.999),
            "Schmale Bordsteinkeile sind Asphalt");
        Pruefe(ParkingLotTool.Geometry.Zellen.Kurvenrandplan.IstGruenkeil(1, 1.0),
            "Ein Meter breiter Bordsteinkeil bleibt Gras");
        Pruefe(!ParkingLotTool.Geometry.Zellen.Kurvenrandplan.IstGruenkeil(-1, 2.0),
            "Keil zur Fahrbahn ist immer Asphalt");
        Pruefe(!ParkingLotTool.Geometry.Zellen.Kurvenrandplan.IstGruenkeil(0, 2.0),
            "Gerader Zwischenraum ist Asphalt");
        bool Enthaelt(float2[] ring, float2 p)
        {
            bool innen = false;
            for (int k = 0; k < ring.Length; k++)
            {
                var a = ring[k]; var b = ring[(k + 1) % ring.Length];
                if ((a.y > p.y) != (b.y > p.y)
                    && p.x < a.x + ((double)p.y - a.y) * ((double)b.x - a.x) / ((double)b.y - a.y))
                    innen = !innen;
            }
            return innen;
        }
        var mitten = normal.KurvenRandreihen.SelectMany(r => r.Buchten)
            .Select(i => normal.Bay[i].Aggregate(float2.zero, (a, b) => a + b) / 4).ToArray();
        int belaege = normal.AsphaltSurface.Count(r => mitten.Any(p => Enthaelt(r, p)));
        Pruefe(belaege <= 3, "Gemeinsamer Belag braucht keine Buchtflaechen");
        Materialboden();
        RandexportTests.Laufe(Pruefe);
        Console.WriteLine($"Randkurven-Logik: {_anzahl} Pruefungen, 0 Fehler.");
    }

    private static void Materialboden()
    {
        var f = RandkurvenFaelle.Alle().Single(f => f.Name == "Lot136479");
        var s = f.Settings.Clone(); s.Randstrassen = true;
        s.Entrances = Array.Empty<Entrance>(); s.Kurvenlaeufe = new[] { f.Lauf.Clone() };
        var layout = ParkingGeometry.Build(f.Polygon, s);
        bool Enthaelt(float2[] ring, double x, double y)
        {
            bool innen = false;
            for (int k = 0; k < ring.Length; k++)
            {
                var a = ring[k]; var b = ring[(k + 1) % ring.Length];
                if ((a.y > y) == (b.y > y)) continue;
                if (x < a.x + (y - a.y) * ((double)b.x - a.x) / ((double)b.y - a.y)) innen = !innen;
            }
            return innen;
        }
        int proben = 0;
        foreach (var reihe in layout.KurvenRandreihen)
        for (int k = 1; k < reihe.Buchten.Length; k++)
        {
            var a = layout.Bay[reihe.Buchten[k - 1]]; var b = layout.Bay[reihe.Buchten[k]];
            if (math.distance(a[1], b[0]) <= 0.001f && math.distance(a[2], b[3]) <= 0.001f)
                Pruefe(a[1].Equals(b[0]) && a[2].Equals(b[3]),
                    "Gemeinsame native Kontaktkante bleibt identisch");
            var u = a[3] - a[0]; var v = b[3] - b[0];
            double drehung = (double)u.x * v.y - (double)u.y * v.x;
            if (Math.Abs(drehung) < 1e-3) continue;
            var eng0 = drehung > 0 ? a[2] : a[1];
            var eng1 = drehung > 0 ? b[3] : b[0];
            if (math.distance(eng0, eng1) > 0.01f) continue;
            var w0 = drehung > 0 ? a[1] : a[2]; var w1 = drehung > 0 ? b[0] : b[3];
            double x = ((double)w0.x + w1.x + ((double)eng0.x + eng1.x) / 2) / 3;
            double y = ((double)w0.y + w1.y + ((double)eng0.y + eng1.y) / 2) / 3;
            if (!Enthaelt(f.Polygon, x, y) || layout.Bay.Any(q => Enthaelt(q, x, y))) continue;
            proben++;
            Pruefe((drehung > 0 && math.distance(w0, w1) >= 1.0f ? layout.GrassSurface : layout.AsphaltSurface).Any(q => Enthaelt(q, x, y)),
                "Gemeinsamer Materialboden erhaelt die Randkeile");
        }
        Pruefe(proben > 10, "Materialbodenprobe misst wirkliche Randkeile");
    }
}
