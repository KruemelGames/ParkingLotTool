using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    private static int PruefeSurfacePreview()
    {
        try { return PruefeSurfacePreviewKern(); }
        catch (Exception e) { Console.Error.WriteLine("FEHLER Muster: " + e); return 1; }
    }

    private static int PruefeSurfacePreviewKern()
    {
        int fehler = 0, pruefungen = 0;
        void Test(bool ok, string text)
        {
            pruefungen++;
            if (!ok) { fehler++; Console.WriteLine("FEHLER Muster: " + text); }
        }
        // Erste Koordinate der vorhandenen kleinen Testform, nicht deren Schwerpunkt.
        var wurzel = Directory.GetParent(Sprachordner()).FullName;
        using var klein = JsonDocument.Parse(File.ReadAllText(Path.Combine(wurzel,
            "Tests/GeometryParity/Laternen/F-l-form.json")));
        var layout = klein.RootElement.GetProperty("Layout");
        var anfang = layout.GetProperty("Grundstueck")[0];
        var kleinMitte = new float2(anfang[0].GetSingle(), anfang[1].GetSingle());
        var settings = LayoutSettings.Cs2;
        settings.Zellen = true; settings.Auto = false; settings.AngleMode = "edge";
        settings.AutomaticEntrances = false;
        var grossSite = Cases[0].Site.Select(p => p * 4).ToArray();
        var gross = ParkingGeometry.Build(grossSite, settings);
        Test(gross.Stalls > 2000, "Grosser Vergleich muss >2000 Buchten haben");
        Test(layout.GetProperty("Bay").GetArrayLength() < 100, "Kleiner Vergleich muss <100 Buchten haben");
        foreach (var mitte in new[] { kleinMitte, grossSite[0], new float2(-12000, 8000),
            new float2(12000, -12000), new float2(-1250.982666f, 842.118103f) })
        foreach (bool umgekehrt in new[] { false, true })
        foreach (bool geschlossen in new[] { false, true })
        {
            var q = OberflaechenMuster.Quadrat(mitte);
            Test(q.Length == 4, "Genau vier offene Knoten");
            Test(math.distance((q[0] + q[2]) * .5f, mitte) < .002f, "Mitte ist Punkt 0");
            for (int i = 0; i < 4; i++)
                Test(Math.Abs(math.distance(q[i], q[(i + 1) % 4]) - 40) < .002f, "Kante 40 m");
            if (umgekehrt) q = q.Reverse().ToArray();
            if (geschlossen) q = q.Concat(new[] { q[0] }).ToArray();
            Test(Cs2Triangulierung.Dreiecke(q, !umgekehrt) == 2,
                "CS2-Nachbau muss Quadrat einschliesslich 10-cm-Einzug annehmen");
        }
        PruefeMusterEntscheidung(Test);
        PruefeMustermaske(Test);
        float2[][] Ringe(string name) => layout.GetProperty(name).EnumerateArray()
            .Select(r => r.EnumerateArray().Select(p => new float2(p[0].GetSingle(),p[1].GetSingle())).ToArray()).ToArray();
        var kleinRinge = Ringe("AsphaltSurface").Concat(Ringe("GrassSurface")).ToArray();
        var grossRinge = gross.AsphaltSurface.Concat(gross.GrassSurface).ToArray();
        var messungen = new System.Collections.Generic.List<object>();
        foreach (var fall in new[] { ("F-l-form", layout.GetProperty("Bay").GetArrayLength(), kleinMitte, kleinRinge),
            ("Rechteck x4", gross.Stalls, grossSite[0], grossRinge) })
        {
            // Nur vier Knoten und Annahme-Nachbau. Keine ECS-, Shader- oder Framezeitmessung.
            for (int i = 0; i < 1000; i++) Cs2Triangulierung.Dreiecke(OberflaechenMuster.Quadrat(fall.Item3));
            var zeiten = new double[31];
            for (int lauf = 0; lauf < zeiten.Length; lauf++)
            {
                var uhr = Stopwatch.StartNew();
                int dreiecke = 0;
                for (int i = 0; i < 2000; i++) dreiecke += Cs2Triangulierung.Dreiecke(OberflaechenMuster.Quadrat(fall.Item3));
                uhr.Stop();
                Test(dreiecke == 4000, "Kein verworfenes Quadrat im Messlauf");
                zeiten[lauf] = uhr.Elapsed.TotalMilliseconds / 2000;
            }
            Array.Sort(zeiten);
            Console.WriteLine($"Muster {fall.Item1}: {fall.Item2} Buchten; 1 Quadrat/4 Knoten/2 Dreiecke; "
                + $"Erzeugen+CS2-Nachbau Median {zeiten[15]:F6} ms, p95 {zeiten[29]:F6} ms.");
            var farbDreiecke = new System.Collections.Generic.List<float2[]>();
            foreach (var ring in fall.Item4)
            {
                var netz = Cs2Triangulierung.Netz(ring);
                if (netz == null && ring.Length == 4) netz = new[] {0,1,2,0,2,3};
                if (netz == null) continue;
                for (int i=0; i+2<netz.Length; i+=3)
                    farbDreiecke.Add(new[] {ring[netz[i]],ring[netz[i+1]],ring[netz[i+2]]});
            }
            var maskenZeiten = new double[31];
            int restZahl = 0;
            for (int lauf=0; lauf<32; lauf++)
            {
                var uhr = Stopwatch.StartNew(); restZahl = 0;
                foreach (var q in farbDreiecke)
                    restZahl += OberflaechenMuster.Ausserhalb(q[0],q[1],q[2],fall.Item3).Length;
                if (lauf>0) maskenZeiten[lauf-1] = uhr.Elapsed.TotalMilliseconds;
            }
            Array.Sort(maskenZeiten);
            Console.WriteLine($"Farbmaske {fall.Item1}: {farbDreiecke.Count} Quelldreiecke, {restZahl} Restdreiecke; "
                + $"Schnitt Median {maskenZeiten[15]:F4} ms, p95 {maskenZeiten[29]:F4} ms; kein Terrain/GPU-Upload.");
            messungen.Add(new { fall = fall.Item1, buchten = fall.Item2, knoten = 4, dreiecke = 2,
                medianMs = zeiten[15], p95Ms = zeiten[29], farbQuelle = farbDreiecke.Count, farbRest = restZahl,
                maskeMedianMs = maskenZeiten[15], maskeP95Ms = maskenZeiten[29] });
        }
        var ablage = Path.Combine(wurzel, ".codex-build", "surface-preview-3-20261017");
        Directory.CreateDirectory(ablage);
        File.WriteAllText(Path.Combine(ablage, "muster-messung.json"),
            JsonSerializer.Serialize(messungen, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Surface Preview Muster: {pruefungen} Pruefungen, {fehler} Fehler.");

        return fehler == 0 ? 0 : 1;
    }
}
