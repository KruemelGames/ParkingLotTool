using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    // Eingaben aus ParkingLotTool-builds.jsonl, 2026-09-05.
    // Beide Abnahmewerte werden am SELBEN Build-Ergebnis gemessen.
    private static int ZoningTAnschlussProtokolle()
    {
        var fehler = 0;
        foreach (var zweiter in new[] { false, true })
        foreach (var exakt in new[] { false, true })
        {
            var site = zweiter ? new[]
            {
                new float2(-1037.020263671875f,118.68978118896484f),
                new float2(-1173.262939453125f,123.36361694335938f),
                new float2(-1180.0333251953125f,-73.897567749023438f),
                new float2(-1043.787353515625f,-78.573898315429688f),
            } : new[]
            {
                new float2(-1037.0201416015625f,118.68978118896484f),
                new float2(-1170.156005859375f,123.25703430175781f),
                new float2(-1176.7835693359375f,-69.84210205078125f),
                new float2(-1043.64453125f,-74.409759521484375f),
            };
            var s = LayoutSettings.Cs2;
            s.Zellen = true;
            s.AngleMode = "edge";
            s.Auto = false;
            s.AutomaticEntrances = false;
            // Ungerundete Zufahrt aus dem jeweiligen Debug-Abzug.
            s.Entrances = new[] { new Entrance { Edge = 0,
                Along = zweiter ? 68.16278076171875 : 66.60845947265625 } };
            s.Zoningflaechen = new[]
            {
                new ParkingGeometry.Zoningflaeche
                {
                    Ecke = zweiter
                        ? new float2(-1044.2919921875f,-64.147224426269531f)
                        : new float2(-1044.0601806640625f,-57.386402130126953f),
                    Spalten = 6, Reihen = 6, Winkel = 88.04, Rand = 8,
                },
                new ParkingGeometry.Zoningflaeche
                {
                    Ecke = zweiter
                        ? new float2(-1042.6463623046875f,-16.176445007324219f)
                        : new float2(-1042.4144287109375f,-9.4146194458007813f),
                    Spalten = 7, Reihen = 2, Winkel = 88.04, Rand = 8,
                },
            };
            s.Randzoning = new[]
            {
                new ParkingGeometry.RandzoningLinie { A = site[2], B = site[3] },
            };
            // Der Bauzettel rundet Winkel auf zwei Nachkommastellen.
            // Zusaetzlich die ungerundete Ableitung des Werkzeugs pruefen.
            if (exakt)
                foreach (var f in s.Zoningflaechen)
                    f.Winkel = ParkingGeometry.Reihenwinkel(s,
                        ParkingGeometry.LaengsteKante(site.Select(p => new double2(p.x, p.y)).ToArray()));
            var bau = ParkingGeometry.Build(site, s);
            var zoning = bau.NetLine.Where(n => n.Kind == "zoning")
                .Select(n => (n.A, n.B)).ToArray();
            var netze = EndpunktZusammenhangsteile(zoning);
            var paare = ZoningFahrbahnueberlappungen(bau.NetLine);
            // Keine gerundeten Schluessel: GenerateNodes braucht exakt gleiche
            // Kursenden. Unter 1 m lehnt CreateCourseDefinition eine Kante ab.
            var grade = new Dictionary<float2, int>();
            foreach (var stueck in zoning)
            foreach (var punkt in new[] { stueck.A, stueck.B })
                grade[punkt] = grade.TryGetValue(punkt, out var grad) ? grad + 1 : 1;
            var kreuzungen = grade.Count(p => p.Value >= 3);
            var kuerzeste = zoning.Length == 0 ? 0
                : zoning.Min(p => math.distance(p.A, p.B));
            var schlecht = netze != 1 || paare != 0 || zoning.Length < 5
                || kreuzungen == 0 || kuerzeste < 1f;
            Console.WriteLine($"  Bau {(zweiter ? "01:52 / PLT-3F5291F9" : "01:51 / PLT-FAB3DE01")}: "
                + $"Winkel {s.Zoningflaechen[0].Winkel:F8}, {zoning.Length} Kanten, {netze} Netz, {paare} Strasse-auf-Strasse, "
                + $"{kreuzungen} exakte Kreuzung(en), kuerzeste Kante {kuerzeste:F3} m"
                + (schlecht ? " <-- FEHLER" : ""));
            if (schlecht) fehler++;
        }
        return fehler;
    }
}
