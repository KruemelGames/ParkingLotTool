using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    // Platz braucht Raum fuer Mitte (3 m), Baustein, Ring und Gegenreihe: grosse Formen dazu.
    private static readonly (string Name, float2[] Ring)[] Stilformen = Formen.Concat(new[]
    {
        ("Gross", Form(0,0,44,0,44,36,0,36)),
        ("Trapez gross", Form(0,0,40,0,52,34,0,34)),
        ("L gross", Form(0,0,50,0,50,30,28,30,28,46,0,46)),
    }).ToArray();

    /**
     * Gestaltung (Nutzer 03.11.2026): Rahmen und Platz liegen auf Gras; Pflaster
     * deckt jedes Objekt und jeden Weg, Pflanzen stehen nie auf Pflaster oder
     * Objekten, der Rand bleibt frei von Bausteinen, im Gruen gibt es nur den
     * Hauptbaustein. Gemessen unabhaengig von den Produktionshilfen.
     */
    private static void Stile(bool ausgabe)
    {
        foreach (var stil in new[] { Fahrradstil.Rahmen, Fahrradstil.Platz })
        foreach (var layout in Layouts)
        foreach (var form in Stilformen)
        foreach (var grad in new[] { 0, 17, 203 })
        foreach (var ort in new[] { new float2(0,0), new float2(8000,-8000) })
        foreach (var umgekehrt in new[] { false, true })
        {
            var winkel = grad * Math.PI / 180;
            var ring = form.Ring.Select(p => new float2((float)(p.x*Math.Cos(winkel)-p.y*Math.Sin(winkel)),
                (float)(p.x*Math.Sin(winkel)+p.y*Math.Cos(winkel))) + ort).ToArray();
            if (umgekehrt) Array.Reverse(ring);
            var settings = LayoutSettings.Fahrrad(layout, stil);
            settings.Es = FahrradGeometrie.Rahmenbreite; // Randabstand = Rahmenbreite
            var gesamt = ParkingGeometry.Build(ring, settings);
            var plan = gesamt.Fahrrad;
            var name = $"{stil}/{layout}/{form.Name}/{grad}/{ort}/{umgekehrt}";
            Pruefe(plan != null && plan.Stil == stil, name + " Plan mit Stil");
            if (plan == null) continue;
            var platzGebaut = stil == Fahrradstil.Platz && !plan.Hinweise.Any(h => h.Contains("Platzring"));
            // Gewaehlte Abstellart steht immer (Nutzer 03.11.): notfalls wird der Rahmen schmaler.
            Pruefe(plan.Objekte.Count > 0, name + " die gewaehlte Abstellart steht");
            Pruefe(plan.Rahmen == FahrradGeometrie.Rahmenbreite
                || plan.Hinweise.Any(h => h.Contains("Rahmen auf")), name + " schmalerer Rahmen nur mit Hinweis");
            // Eingestellt ist eingestellt: schmaler nur, wenn sonst gar nichts steht.
            if (form.Name != "Schmal" && !(stil == Fahrradstil.Platz && form.Name == "Kurvenpolygon"))
                Pruefe(plan.Rahmen == FahrradGeometrie.Rahmenbreite, name + " Rahmen wie eingestellt");
            foreach (var p in Messe(plan, ring))
                // Ein Platzring hat keine durchgehende Gerade, er ist aber an jedem Stueck erreichbar (Messe prueft das).
                Pruefe(p.Value, name + "/" + p.Key);
            // Wie beim Autoparkplatz: Flaechen decken den Umriss genau einmal, nichts ueberlappt.
            Pruefe(plan.Gras.Count > 0 && gesamt.GrassSurface.Length == plan.Gras.Count
                && gesamt.AsphaltSurface.Length == plan.Flaechen.Count, name + " Gras und Pflaster als getrennte Flaechen");
            Pruefe(Deckung(plan, ring, out var befund), name + " Flaechen lueckenlos ohne Ueberlappung (" + befund + ")");
            Pruefe(plan.Gras.All(r => Cs2Triangulierung.Dreiecke(r) == r.Length - 2 && ImRing(r, ring)), name + " Grasflaechen gueltig");
            Pruefe(!plan.Hinweise.Any(h => h.Contains("Flaechenzerlegung") || h.Contains("nimmt CS2 nicht an")), name + " Zellenbau ohne Rueckfall: "
                + string.Join(" | ", plan.Hinweise.Where(h => h.Contains("Flaechenzerlegung") || h.Contains("nimmt CS2 nicht an"))));
            Pruefe(plan.Flaechen.Count > 0 && plan.Flaechen.All(r => Cs2Triangulierung.Dreiecke(r) == r.Length - 2 && ImRing(r, ring)),
                name + " Pflasterstuecke gueltig und im Umriss");
            Pruefe(plan.Objekte.All(o => o.Grundriss.All(e => Belegt(plan, Einwaerts(e, o.Position)))),
                name + " jedes Objekt steht auf Pflaster");
            Pruefe(plan.Wege.All(w => Enumerable.Range(0, 11).All(i => Belegt(plan, math.lerp(w.A, w.B, i / 10f))
                || (w.Zufahrt && Rand(math.lerp(w.A, w.B, i / 10f), ring) < .05))), name + " jeder Weg liegt auf Pflaster");
            Pruefe(plan.Baeume.Concat(plan.Buesche).All(p => !Belegt(plan, p) && Innen(p, ring)
                && !plan.Objekte.Any(o => Innen(p, o.Grundriss))), name + " Pflanzen nie auf Pflaster oder Objekt");
            Pruefe(plan.Objekte.All(o => o.Grundriss.All(e => Rand(e, ring) >= plan.Rahmen - .01)),
                name + " Bausteine hinter dem Rahmen");
            var haupt = layout == Fahrradlayout.C ? Fahrradobjektart.Unterstand
                : layout == Fahrradlayout.D ? Fahrradobjektart.Buegel : Fahrradobjektart.Staender;
            Pruefe(plan.Objekte.All(o => o.Art == haupt), name + " nur der Hauptbaustein");
            if ((form.Name == "Rechteck" || form.Name.StartsWith("Gross")) && plan.Rahmen >= .5)
                Pruefe(plan.Baeume.Count > 0 && plan.Buesche.Count > 0, name + " Baeume und Buesche im Rahmen");
            if (stil == Fahrradstil.Platz && form.Name.StartsWith("Gross"))
                Pruefe(platzGebaut, name + " Platzring passt in 44 x 36 m");
            if (platzGebaut)
                Pruefe(plan.Wege.Count(w => !w.Zufahrt) >= 8, name + " Ring aus mindestens 8 Stuecken");
            if (ausgabe && grad == 0 && ort.x == 0 && !umgekehrt)
                Console.WriteLine($"{stil}/{layout}/{form.Name}: {plan.Kapazitaet} Plaetze, {plan.Objekte.Count} Objekte, "
                    + $"{plan.Flaechen.Count} Pflasterstuecke, {plan.Baeume.Count} Baeume, {plan.Buesche.Count} Buesche, Rahmen {plan.Rahmen} m"
                    + (stil == Fahrradstil.Platz ? platzGebaut ? ", Ring" : ", Rahmen statt Ring" : ""));
        }
        // Schlicht bleibt eine Flaeche ohne Gruen.
        foreach (var layout in Layouts)
        {
            var plan = ParkingGeometry.Build(Formen[0].Ring, LayoutSettings.Fahrrad(layout)).Fahrrad;
            Pruefe(plan.Stil == Fahrradstil.Schlicht && plan.Flaechen.Count == 1 && plan.Gras.Count == 0
                && plan.Baeume.Count == 0 && plan.Buesche.Count == 0, "Schlicht/" + layout + " eine Flaeche, kein Gruen");
        }
        // Der Randabstand stellt die Rahmenbreite ein; 0 heisst ohne Rahmen und ohne Randbepflanzung.
        foreach (var breite in new[] { 0.0, 1.0, 3.0 })
        foreach (var layout in Layouts)
        {
            var plan = ParkingGeometry.Build(Stilformen.First(f => f.Name == "Gross").Ring,
                Mit(LayoutSettings.Fahrrad(layout, Fahrradstil.Rahmen), breite)).Fahrrad;
            var ring = Stilformen.First(f => f.Name == "Gross").Ring;
            Pruefe(plan.Rahmen == breite && plan.Objekte.Count > 0
                && plan.Objekte.All(o => o.Grundriss.All(e => Rand(e, ring) >= breite - .01)),
                $"Randabstand {breite} m ist der Rahmen ({layout})");
            Pruefe(breite > 0 || plan.Buesche.Count == 0, $"ohne Rahmen keine Randbuesche ({layout})");
        }
        var kaputt = LayoutSettings.Fahrrad(Fahrradlayout.A);
        kaputt.Fahrradstil = (Fahrradstil)7;
        Pruefe(FahrradGeometrie.Baue(Formen[0].Ring, kaputt).Stil == Fahrradstil.Schlicht, "unbekannter Stil baut Schlicht");
        Pruefe(LayoutSettings.Fahrrad(Fahrradlayout.C, Fahrradstil.Platz).Clone().Fahrradstil == Fahrradstil.Platz, "Clone behaelt Stil");
    }

    private static bool Belegt(FahrradPlan plan, float2 p) => plan.Flaechen.Any(r => Innen(p, r));

    /** Rasterproben (0,37 m, gegen das Bauraster versetzt): jede Probe im Umriss liegt in genau einer Flaeche. */
    private static bool Deckung(FahrradPlan plan, float2[] ring, out string befund)
    {
        var alle = plan.Flaechen.Concat(plan.Gras).ToList();
        float x0 = ring.Min(p => p.x), x1 = ring.Max(p => p.x), y0 = ring.Min(p => p.y), y1 = ring.Max(p => p.y);
        int luecke = 0, doppelt = 0, proben = 0;
        for (var y = y0 + .123f; y < y1; y += .37f)
            for (var x = x0 + .071f; x < x1; x += .37f)
            {
                var p = new float2(x, y);
                if (!Innen(p, ring) || Rand(p, ring) < .05) continue;
                // Proben direkt auf einer Flaechenkante zaehlen nicht (Innen() hat 3 mm Toleranz).
                if (alle.Any(r => Rand(p, r) < .01)) continue;
                proben++;
                var n = alle.Count(r => Innen(p, r));
                if (n == 0) luecke++; else if (n > 1) doppelt++;
            }
        befund = $"{proben} Proben, {luecke} Luecken, {doppelt} doppelt";
        return luecke == 0 && doppelt == 0;
    }

    private static float2 Einwaerts(float2 p, float2 mitte) => p + math.normalizesafe(mitte - p) * .02f;

    private static double Rand(float2 p, float2[] ring)
        => Enumerable.Range(0, ring.Length).Min(i => Abstand(p, ring[i], ring[(i + 1) % ring.Length]));
}
