using System;
using System.Linq;
using ParkingLotTool.Geometry;
using ParkingLotTool.Geometry.Zellen;
using Unity.Mathematics;

internal static partial class Program
{
    private static int RunZufahrtsschnitt()
    {
        var polygon = new[] {
            new float2(-1037.02026f, 118.689781f),
            new float2(-1183.354f, 123.710007f),
            new float2(-1188.24707f, -18.8730011f),
            new float2(-1041.911f, -23.8930016f),
        };
        var s = LayoutSettings.Cs2;
        s.Randstrassen = false; s.Qk = true; s.Md = 2.5;
        s.AngleMode = "edge"; s.Angle = 0;
        s.Es = 1; s.Ai = 7; s.Cw = 3; s.Cr = 33; s.Sw = 3; s.Sl = 5.9;
        s.Entrances = Array.Empty<Entrance>();
        var fehler = 0;
        void Pruefe(bool ok, string meldung)
        {
            if (ok) return;
            fehler++; Console.WriteLine("FEHLER: " + meldung);
        }
        for (var kante = -1; kante < 4; kante++)
        {
            s.Entrances = kante < 0 ? Array.Empty<Entrance>() : new[] {
                new Entrance { Edge = kante, Along = math.distance(polygon[kante], polygon[(kante + 1) % 4]) / 2 }
            };
            try
            {
                var l = ParkingGeometry.Build(polygon, s);
                var warnungen = l.Warnings.Where(w => w.StartsWith("Halbebenenschnitt ")).ToArray();
                Console.WriteLine($"Zufahrtsschnitt Kante {kante}: {l.Stalls} Buchten, {warnungen.Length} Schnittwarnungen");
                if (kante < 0) foreach (var w in warnungen) Console.WriteLine(w);
                Pruefe(l.Stalls > 0 && l.Bay.Length > 0 && l.NetLine.Length > 0, "Leerer Export");
                // Vorher 153/158/504/153/326: die Konstruktion darf nicht
                // mehr von Hunderten verworfenen Scherben abhaengen.
                Pruefe(warnungen.Length <= 10, "Zu viele entartete Scherben");
            }
            catch (Exception e) { Pruefe(false, $"Kante {kante}: {e}"); }
        }
        // Ein duennes Dreieck muss auch bei Weltkoordinaten seine Flaeche
        // und seinen Schwerpunkt behalten. Binaere Masse sind exakt:
        // 2^-10 * 2^-32 / 2 = 2^-43 m2; die alte globale Summe lieferte 0.
        foreach (var ursprung in new[] { new Punkt(0, 0), new Punkt(-1113, 65) })
        {
            var fabrik = new Knotenfabrik();
            var register = new Linienregister();
            var punkte = new[] { ursprung, ursprung + new Punkt(0, Math.Pow(2, -32)),
                ursprung + new Punkt(Math.Pow(2, -10), 0) };
            var knoten = punkte.Select(fabrik.Neu).ToArray();
            var ring = new Ring { Kanten = Enumerable.Range(0, 3).Select(i =>
                new GerichteteKante(knoten[i], knoten[(i + 1) % 3], register.Aussenkante(i))).ToList() };
            Pruefe(Math.Abs(ring.Vorzeichenflaeche / -Math.Pow(2, -43) - 1) < 1e-12,
                "Duennes Dreieck verliert Flaeche/Vorzeichen bei " + ursprung.X);
            try
            {
                var erwartet = ursprung + new Punkt(Math.Pow(2, -10) / 3, Math.Pow(2, -32) / 3);
                Pruefe(Geometrie.Laenge(Geometrie.Schwerpunkt(ring) - erwartet) < 1e-12,
                    "Duennes Dreieck verliert Schwerpunkt bei " + ursprung.X);
            }
            catch (InvalidOperationException e) { Pruefe(false, "Duennes Dreieck: " + e.Message); }
        }
        // Den Warnungskanal an einer kontrollierten Rundungsentartung pruefen,
        // statt den Produktionsfehler (eine zwingende Scherbe) zu verlangen.
        {
            var fabrik = new Knotenfabrik();
            var register = new Linienregister();
            var punkte = new[] { new Punkt(1e6, 1e6), new Punkt(1e6 + 1e-5, 1e6),
                new Punkt(1e6 + 1e-5, 1e6 + 4), new Punkt(1e6, 1e6 + 4) };
            var p = new Polygon(punkte.Select((v, i) => new Ecke(fabrik.Neu(v), register.Aussenkante(i))));
            var teiler = new Polygonteiler(fabrik);
            teiler.Teile(p, register.RasterX(1e6 + 5e-6));
            Pruefe(teiler.Warnungen.Count == 2 && teiler.Warnungen.All(w => w.StartsWith("Halbebenenschnitt ")),
                "Kontrollierte Scherbe fehlt im Warnungskanal");
        }
        foreach (var umkehr in new[] { false, true })
        {
            var fabrik = new Knotenfabrik();
            var register = new Linienregister();
            var punkte = new[] { new Punkt(0, 0), new Punkt(4, 0), new Punkt(4, 4), new Punkt(0, 4) };
            // Mutation: denselben 16-m2-Ring im Uhrzeigersinn in den echten
            // Halbebenenschnitt schicken. Beide Haelften muessen -8 m2 haben.
            if (umkehr) Array.Reverse(punkte);
            var p = new Polygon(punkte.Select((v, i) => new Ecke(fabrik.Neu(v), register.Aussenkante(i))));
            var teiler = new Polygonteiler(fabrik);
            try
            {
                var teile = teiler.Teile(p, register.RasterX(2));
                Pruefe(!umkehr, "Orientierungsmutation wurde verschluckt");
                Pruefe(teile.Count == 2 && Math.Abs(teile.Sum(Geometrie.Flaeche) - 16) < 1e-12,
                    "Gueltiger Schnitt verliert Flaeche");
            }
            catch (InvalidOperationException e)
            {
                Pruefe(umkehr && e.Message.Contains("lost its orientation"), "Unerwarteter Schnittfehler: " + e);
                Console.WriteLine("Orientierungsmutation: " + e.Message);
            }
        }
        Console.WriteLine($"Zufahrtsschnitt: {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }
}

