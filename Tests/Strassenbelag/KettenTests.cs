using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ParkingLotTool.Geometry;
using ParkingLotTool.Geometry.Zellen;
using Unity.Mathematics;

internal static partial class StrassenbelagTests
{
    private static void SaumkontaktTest()
    {
        Polygon Form(Punkt[] punkte, int start)
        {
            return new Polygon(punkte.Select((p, i) => new Ecke(new Knoten(start + i, p),
                new Linie(start + i, Linienart.Zufahrtskante, "Kontaktfixtur"))));
        }
        var road = new Zelle
        {
            Id = 0,
            Material = (Material)Enum.Parse(typeof(Material), "Strassenbelag"),
            Polygon = Form(new[] { new Punkt(0, 0), new Punkt(8, 0), new Punkt(8, 4), new Punkt(0, 4) }, 0),
        };
        var a = new Punkt(8, 4);
        var mitte = new Punkt(8, 2);
        var fern = new Punkt(100, 100);
        var gruen = new Zelle
        {
            Id = 1, Material = Material.Gruen,
            Polygon = Form(new[] { a, new Punkt(8.04, 4), new Punkt(8.04, 6), a,
                mitte, new Punkt(8.04, 2), new Punkt(8.04, 3), mitte, a,
                fern, new Punkt(100.04, 100), new Punkt(100.04, 102), fern, a }, 10),
        };
        var kanten = Enumerable.Range(0, gruen.Polygon.Anzahl).Select(i => new GerichteteKante(
            gruen.Polygon.Knoten(i), gruen.Polygon.Knoten(i + 1), gruen.Polygon.Linie(i))).ToList();
        var f = new Flaeche { Material = Material.Gruen,
            Aussenring = new Ring { Kanten = kanten, Rohkanten = kanten }, Loecher = new List<Ring>() };
        var methode = typeof(ParkingGeometry).GetMethod("GruenRastervorgaben",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var saume = (Punkt[][])methode.Invoke(null, new object[] {
            new Rahmen(new Punkt(1, 0), new Punkt(0, 1)), new[] { f }, new[] { road, gruen } });
        Pruefe(saume.Length == 2, "Nur Gruensaume an Road-Knoten oder Road-Kante planen");
        Pruefe(saume.All(s => s.All(p => p.X < 20)), "Fremde Gruenaltlast bleibt unangetastet");
    }

    private static void ArchivTest()
    {
        var daten = Archivdaten();
        var layout = ParkingGeometry.Build(daten.Polygon, daten.Settings);
        var road = (float2[][])typeof(ParkingLayout).GetProperty("StrassenbelagSurface").GetValue(layout);
        Pruefe(road.Length > 0, "Lot201905 erzeugt den Strassenbelag trotz degenerierter Zellnaht");
        foreach (var ring in layout.AsphaltSurface.Concat(layout.GrassSurface).Concat(road))
            Pruefe(Cs2Triangulierung.Dreiecke(ring) > 0, "Lot201905: alle ausgegebenen Ringe baubar");
    }

    /*
     * Nutzerbau 27.10.: zwei Alleys, eine davon endet genau auf der Ecke der
     * Randstrasse. Dort blieb der Halbkreis des Alley-Knotens offen (ein
     * aufgeklebtes Kreuzungsstueck ragte ueber die Strasse und fiel weg).
     * Die Randstrasse ist jetzt selbst Fahrbahn: rund um JEDEN Alley-Knoten
     * liegt sie, auch an der Ecke, baubar und mit Texturlage ihres Rings.
     */
    private static void EckalleyTest()
    {
        var daten = Archivdaten("LotEckalley.json", alleZufahrten: true);
        var layout = ParkingGeometry.Build(daten.Polygon, daten.Settings);
        var gassen = layout.NetLine.Where(n => n.Kind == "entrance" && Zufahrtsarten.IstGasse(n.Art)).ToArray();
        Pruefe(gassen.Length == 2, "Eckalley: beide Alleys gebaut");
        foreach (var n in gassen)
        {
            int proben = 0;
            for (float x = -4f; x <= 4f; x += .25f)
            for (float y = -4f; y <= 4f; y += .25f)
            {
                var p = n.B + new float2(x, y);
                if (math.length(new float2(x, y)) > 3.9f) continue;
                if (!layout.PerimeterQuad.Any(r => Innen(r, p)) || layout.PerimeterQuad.Any(r => Abstand(r, p) < .1f)) continue;
                proben++;
                Pruefe(layout.FahrbahnSurface.Any(r => Innen(r, p) || Abstand(r, p) < .02f),
                    "Eckalley: Randstrasse liegt am Alley-Knoten " + n.B + " ueber der Alley " + p);
            }
            Pruefe(proben > 0, "Eckalley: Proben am Alley-Knoten " + n.B);
        }
        foreach (var ring in layout.FahrbahnSurface)
        {
            Pruefe(Cs2Triangulierung.Dreiecke(ring) > 0, "Eckalley: Fahrbahn baubar");
            Pruefe(layout.AsphaltSurface.Any(a => Enumerable.Range(0, a.Length).Any(i =>
                Enumerable.Range(0, ring.Length).Any(j => GemeinsameLinie(
                    a[i], a[(i + 1) % a.Length], ring[j], ring[(j + 1) % ring.Length])))),
                "Eckalley: Fahrbahn findet die Texturlage ihres Asphaltrings");
        }
    }

    /*
     * Eine Bruecke quer ueber den Eckalley-Parkplatz (Issue #22): unter
     * ihrem Grundriss keine Fahrbahn mit Strassen-Ebene, daneben laeuft
     * sie weiter; der Asphalt darunter bleibt.
     */
    private static void BrueckenTest()
    {
        var daten = Archivdaten("LotEckalley.json", alleZufahrten: true);
        var ohne = ParkingGeometry.Build(daten.Polygon, daten.Settings);
        var mitte = daten.Polygon.Aggregate(float2.zero, (x, y) => x + y) / daten.Polygon.Length;
        var laengs = math.normalize(daten.Polygon[1] - daten.Polygon[0]);
        var quer = new float2(-laengs.y, laengs.x);
        // 10 m breites Band durch die Mitte, quer zur ersten Kante, 300 m lang.
        var band = new[] { mitte - laengs * 5 - quer * 150, mitte + laengs * 5 - quer * 150,
            mitte + laengs * 5 + quer * 150, mitte - laengs * 5 + quer * 150 };
        daten.Settings.Brueckengrundrisse = new[] { band };
        var mit = ParkingGeometry.Build(daten.Polygon, daten.Settings);
        int unter = 0, daneben = 0;
        foreach (var r in ohne.FahrbahnSurface)
        {
            var lo = r.Aggregate(new float2(float.MaxValue), math.min);
            var hi = r.Aggregate(new float2(float.MinValue), math.max);
            for (float x = lo.x + .05f; x < hi.x; x += .41f)
            for (float y = lo.y + .05f; y < hi.y; y += .41f)
            {
                var p = new float2(x, y);
                if (!Innen(r, p) || Abstand(r, p) < .1f || Abstand(band, p) < .1f) continue;
                var belegt = mit.FahrbahnSurface.Any(f => Innen(f, p));
                if (Innen(band, p)) { unter++; Pruefe(!belegt, "Bruecke: keine Fahrbahn unter der Bruecke " + p); }
                else { daneben++; Pruefe(belegt, "Bruecke: Fahrbahn neben der Bruecke bleibt " + p); }
                Pruefe(mit.AsphaltSurface.Any(a => Innen(a, p) || Abstand(a, p) < .02f), "Bruecke: Asphalt darunter bleibt " + p);
            }
        }
        Pruefe(unter > 0 && daneben > 0, $"Bruecke: Proben unter {unter} / daneben {daneben}");
        foreach (var ring in mit.FahrbahnSurface)
            Pruefe(Cs2Triangulierung.Dreiecke(ring) > 0, "Bruecke: Fahrbahn baubar");
    }

    private static (float2[] Polygon, LayoutSettings Settings) Archivdaten(
        string datei = "Lot201905.json", bool alleZufahrten = false)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, datei)));
        var d = doc.RootElement;
        var polygon = d.GetProperty("Polygon").EnumerateArray()
            .Select(p => new float2(p.GetProperty("X").GetSingle(), p.GetProperty("Z").GetSingle())).ToArray();
        var optionen = new JsonSerializerOptions { IncludeFields = true };
        optionen.Converters.Add(new JsonStringEnumConverter());
        optionen.Converters.Add(new VektorLeser());
        var s = d.GetProperty("Settings").Deserialize<LayoutSettings>(optionen);
        if (!alleZufahrten) s.Entrances = s.Entrances.Take(1).ToArray();
        return (polygon, s);
    }

    private sealed class VektorLeser : JsonConverter<float2>
    {
        public override float2 Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            using var d = JsonDocument.ParseValue(ref reader);
            return new float2(d.RootElement[0].GetSingle(), d.RootElement[1].GetSingle());
        }

        public override void Write(Utf8JsonWriter writer, float2 value, JsonSerializerOptions options)
            => throw new NotSupportedException();
    }

    private static void KettenTest()
    {
        // Zwei echte Nachbarzellen von Lot201905, nicht eine erfundene Naht.
        // Ihre gemeinsame Kante hat dieselbe Richtung; eine Nullstrecke
        // verbindet zwei verschiedene Knoten an derselben Koordinate.
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Ketten.json")));
        var knoten = new Dictionary<int, Knoten>();
        var zellen = new List<Zelle>();
        foreach (var roh in doc.RootElement.EnumerateArray())
        {
            var ecken = new List<Ecke>();
            foreach (var e in roh.GetProperty("Ecken").EnumerateArray())
            {
                int id = e.GetProperty("Id").GetInt32();
                if (!knoten.TryGetValue(id, out var k))
                    knoten.Add(id, k = new Knoten(id, new Punkt(e.GetProperty("X").GetDouble(), e.GetProperty("Y").GetDouble())));
                ecken.Add(new Ecke(k, new Linie(e.GetProperty("Linie").GetInt32(), Linienart.Zufahrtskante, "Fixtur")));
            }
            zellen.Add(new Zelle { Id = zellen.Count, Polygon = new Polygon(ecken), Material = Material.Asphalt, Art = Zellart.Zufahrt });
        }
        var vorher = zellen.Sum(z => Geometrie.Flaeche(z.Polygon));
        var ergebnis = Vereinigung.Vereinige(zellen);
        Pruefe(ergebnis.Flaechen.Count > 0, "Gleichgerichtete Zellnaht bleibt baubar");
        Pruefe(Math.Abs(ergebnis.Flaechen.Sum(f => f.Flaecheninhalt) - vorher) <= 1e-8,
            "Gerichtete Randkette verliert keine Flaeche");
    }
}
