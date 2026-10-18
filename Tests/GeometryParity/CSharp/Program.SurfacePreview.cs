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
        void Test(bool ok, string text) { pruefungen++; if (!ok) { fehler++; Console.WriteLine("FEHLER Muster: " + text); } }
        PruefeMusterEntscheidung(Test);
        PruefeMustermaske(Test);
        PruefeMusterschnitt(Test);
        var wurzel = Directory.GetParent(Sprachordner()).FullName;
        using var klein = JsonDocument.Parse(File.ReadAllText(Path.Combine(wurzel,"Tests/GeometryParity/Laternen/F-l-form.json")));
        var layout = klein.RootElement.GetProperty("Layout");
        float2[] Ring(JsonElement r) => r.EnumerateArray().Select(p => new float2(p[0].GetSingle(),p[1].GetSingle())).ToArray();
        float2[][] Ringe(string name) => layout.GetProperty(name).EnumerateArray().Select(Ring).ToArray();
        var kleinSite = Ring(layout.GetProperty("Grundstueck"));
        var settings = LayoutSettings.Cs2;
        settings.Zellen = true; settings.Auto = false; settings.AngleMode = "edge"; settings.AutomaticEntrances = false;
        var grossSite = Cases[0].Site.Select(p => p*4).ToArray();
        var gross = ParkingGeometry.Build(grossSite,settings);
        Test(gross.Stalls > 2000,"Grosser Vergleich >2000 Buchten");
        var messungen = new System.Collections.Generic.List<object>();
        foreach (var fall in new[] {
            ("F-l-form",layout.GetProperty("Bay").GetArrayLength(),kleinSite,new[] {Ringe("GrassSurface"),Ringe("AsphaltSurface")}),
            ("Rechteck x4",gross.Stalls,grossSite,new[] {gross.GrassSurface,gross.AsphaltSurface,gross.ZoningSurface,gross.ZoningRoadSurface}) })
        {
            var zeiten = new double[31]; OberflaechenMuster.Plan plan = null;
            for (int lauf = -1; lauf < zeiten.Length; lauf++)
            {
                var uhr = Stopwatch.StartNew();
                plan = OberflaechenMuster.Berechne(fall.Item3,fall.Item3[0],fall.Item4);
                if (lauf >= 0) zeiten[lauf] = uhr.Elapsed.TotalMilliseconds;
            }
            Test(plan.Stuecke.Length > 0,"Gueltiger Vergleich darf nicht leer bleiben");
            Test(plan.Stuecke.All(p => Cs2Triangulierung.Dreiecke(p.Ring)==p.Ring.Length-2),"Jedes ausgegebene Stueck CS2-faehig");
            Test(plan.Stuecke.Length<=OberflaechenMuster.MaxStuecke && plan.Ecken<=OberflaechenMuster.MaxEcken,"Harte Ausgabebudgets");
            double anteil = OberflaechenSchnitt.Schneide(fall.Item3,plan.Ausschnitt)
                .Sum(q=>Math.Abs(OberflaechenSchnitt.Flaeche(q))) / Math.Abs(OberflaechenSchnitt.Flaeche(fall.Item3));
            Array.Sort(zeiten);
            var farb = new System.Collections.Generic.List<float2[]>();
            foreach(var ring in fall.Item4.SelectMany(g=>g ?? Array.Empty<float2[]>()))
            {
                var netz = Cs2Triangulierung.Netz(ring);
                if(netz==null) continue;
                for(int i=0;i<netz.Length;i+=3) farb.Add(new[] {ring[netz[i]],ring[netz[i+1]],ring[netz[i+2]]});
            }
            var maskenzeiten = new double[31];
            for(int lauf=-1;lauf<31;lauf++)
            {
                var uhr=Stopwatch.StartNew();
                foreach(var tri in farb) OberflaechenMuster.Ausserhalb(tri[0],tri[1],tri[2],plan.Ausschnitt);
                if(lauf>=0)maskenzeiten[lauf]=uhr.Elapsed.TotalMilliseconds;
            }
            Array.Sort(maskenzeiten);
            Console.WriteLine($"Ausschnitt {fall.Item1}: {fall.Item2} Buchten, {plan.Stuecke.Length} Stuecke/{plan.Ecken} Ecken, {anteil*100:F2} % Lotflaeche; verworfen {plan.Verworfen}, verkleinert {plan.Verkleinerungen}; Schnitt+Annahme Median {zeiten[15]:F3}/p95 {zeiten[29]:F3} ms; Farbmaske {maskenzeiten[15]:F3}/p95 {maskenzeiten[29]:F3} ms.");
            messungen.Add(new { fall=fall.Item1,buchten=fall.Item2,stuecke=plan.Stuecke.Length,ecken=plan.Ecken,verworfen=plan.Verworfen,verkleinert=plan.Verkleinerungen,anteil,medianMs=zeiten[15],p95Ms=zeiten[29],maskeMedianMs=maskenzeiten[15],maskeP95Ms=maskenzeiten[29] });
        }
        var ablage=Path.Combine(wurzel,".codex-build/surface-preview-4-20261018");Directory.CreateDirectory(ablage);
        File.WriteAllText(Path.Combine(ablage,"schnitt-messung.json"),JsonSerializer.Serialize(messungen,new JsonSerializerOptions {WriteIndented=true}));
        Console.WriteLine($"Surface Preview Ausschnitt: {pruefungen} Pruefungen, {fehler} Fehler.");
        return fehler==0 ? 0 : 1;
    }
}
