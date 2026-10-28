using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class StrassenbelagTests
{
    public sealed class Materialstand
    {
        public float[][][] Asphalt { get; set; }
        public float[][][] Gras { get; set; }
    }

    private static Materialstand Merke(ParkingLayout l) => new Materialstand
    {
        Asphalt = l.AsphaltSurface.Select(r => r.Select(p => new[] { p.x, p.y }).ToArray()).ToArray(),
        Gras = l.GrassSurface.Select(r => r.Select(p => new[] { p.x, p.y }).ToArray()).ToArray(),
    };

    private static double Flaeche(float2[] ring)
    {
        double a = 0;
        var o = (double2)ring[0];
        for (int i = 1; i + 1 < ring.Length; i++)
            a += Kreuz((double2)ring[i] - o, (double2)ring[i + 1] - o) / 2;
        return Math.Abs(a);
    }

    // Zwei Kanten gleich gerichtet auf einer Linie, mit mindestens 5 cm Ueberlappung.
    private static bool GemeinsameLinie(float2 s0, float2 s1, float2 p0, float2 p1)
    {
        var r = math.normalizesafe(s1 - s0);
        if (math.dot(r, math.normalizesafe(p1 - p0)) < .9999f) return false;
        if (math.abs(math.dot(p0 - s0, new float2(-r.y, r.x))) > .02f) return false;
        var ta = math.dot(p0 - s0, r);
        var tb = math.dot(p1 - s0, r);
        return math.min(math.distance(s0, s1), tb) - math.max(0f, ta) >= .05f;
    }

    private static float2[][] AmEnde(float2 b, float2[][] strassen)
        => strassen.Where(s => Innen(s, b) || Abstand(s, b) < .05f).ToArray();

    // Liegt die Strecke von a nach p ganz auf Strassenflaeche (10-cm-Schritte)?
    private static bool Durchgehend(float2 a, float2 p, float2[][] strassen)
    {
        var schritte = (int)math.ceil(math.distance(a, p) / .1f);
        for (int i = 0; i <= schritte; i++)
        {
            var x = math.lerp(a, p, schritte == 0 ? 0 : (float)i / schritte);
            if (!strassen.Any(s => Innen(s, x) || Abstand(s, x) < .05f)) return false;
        }
        return true;
    }

    private static float Abstand(float2[] ring, float2 p)
    {
        var beste = float.MaxValue;
        for (int i = 0; i < ring.Length; i++)
        {
            var a = ring[i];
            var ab = ring[(i + 1) % ring.Length] - a;
            var t = math.lengthsq(ab) < 1e-12f ? 0f : math.clamp(math.dot(p - a, ab) / math.lengthsq(ab), 0f, 1f);
            beste = math.min(beste, math.distance(p, a + ab * t));
        }
        return beste;
    }

    private static double Kreuz(double2 a, double2 b) => a.x * b.y - a.y * b.x;

    private static bool Innen(float2[] ring, float2 p)
    {
        bool drin = false;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            if ((ring[i].y > p.y) != (ring[j].y > p.y)
                && p.x < ((double)ring[j].x - ring[i].x) * (p.y - ring[i].y)
                    / ((double)ring[j].y - ring[i].y) + ring[i].x) drin = !drin;
        return drin;
    }

    private static void Messe(string name, float2[] polygon, ParkingLayout l, Materialstand alt)
    {
        var eigenschaft = typeof(ParkingLayout).GetProperty("StrassenbelagSurface");
        var road = (float2[][])eigenschaft.GetValue(l);
        var gasse = name.Contains("/gasse/");
        Pruefe(gasse ? road.Length > 0 : road.Length == 0, name + " eigener Belag vorhanden");
        /*
         * AUTOZUFAHRTEN (Nutzer 28.10.: "Surface to road" reicht nicht mehr bis
         * zur Strasse): ihr Gang im Lot gehoert zur Fahrbahn mit Strassen-Ebene,
         * in die Tools die Vorflaeche einsetzt - und liegt nur auf Asphalt.
         */
        var fahrbahnAlle = l.FahrbahnSurface ?? Array.Empty<float2[]>();
        // Bezug ist die Achse jeder Autozufahrt im Netz, 1,5 m zu jeder Seite,
        // vom Umriss bis 4 m vor ihren inneren Knoten (dort beginnt die Strasse).
        // Die Vierecke aus EntranceQuad taugen nicht: sie zaehlen Fusswegzellen
        // auf Gras ohne Zugangsart als "Zufahrt" (gemessen 1/False/ohne/Zufahrt).
        var quads = l.EntranceQuad ?? Array.Empty<float2[]>();
        var autos = l.NetLine.Where(n => n.Kind == "entrance" && !Zufahrtsarten.IstGasse(n.Art)
            && n.Art != Zufahrtsart.Fussweg).ToArray();
        var autoproben = 0;
        foreach (var n in autos)
        {
            var u = math.normalize(n.B - n.A);
            var q = new float2(-u.y, u.x);
            var laenge = math.distance(n.A, n.B);
            for (float t = .3f; t < laenge - 4f; t += .37f)
            for (float quer = -1.5f; quer <= 1.5f; quer += .25f)
            {
                var p = n.A + u * t + q * quer;
                if (!Innen(polygon, p) || Abstand(polygon, p) < .1f) continue;
                autoproben++;
                Pruefe(fahrbahnAlle.Any(f => Innen(f, p)), name + " Autozufahrt in der Fahrbahn " + p);
                Pruefe(l.AsphaltSurface.Any(a => Innen(a, p)), name + " Autozufahrt: Asphalt darunter bleibt " + p);
            }
        }
        if (autos.Any(n => math.distance(n.A, n.B) > 5f)) Pruefe(autoproben > 0, name + " Autozufahrt: Proben vorhanden");
        foreach (var r in fahrbahnAlle)
            Pruefe(Cs2Triangulierung.Dreiecke(r) > 0, name + " Autozufahrt: CS2 nimmt Fahrbahn an");
        if (!gasse) return;
        if (name.EndsWith("/einheitlich", StringComparison.Ordinal))
        {
            Pruefe(l.Green.Length > 0, name + " Gruenplan fuer Pflanzen erhalten");
            Pruefe(l.GrassSurfaceByRole.Length > 0, name + " Gruenrollen fuer Pflanzen erhalten");
        }
        var asphaltAlt = alt.Asphalt.Select(r => r.Select(p => new float2(p[0], p[1])).ToArray()).ToArray();
        var grasAlt = alt.Gras.Select(r => r.Select(p => new float2(p[0], p[1])).ToArray()).ToArray();
        var alle = l.AsphaltSurface.Concat(l.GrassSurface).Concat(road).ToArray();
        foreach (var r in alle)
            Pruefe(Cs2Triangulierung.Dreiecke(r) > 0, name + " CS2 nimmt Ring an");
        var differenz = alle.Sum(Flaeche) - asphaltAlt.Concat(grasAlt).Sum(Flaeche);
        // Die unabhaengige GEOS-Pruefung misst Welt-float-Grenzen mit
        // 2 mm Randtoleranz. Eine Summe gerundeter Ringflaechen allein
        // zaehlte die schon im Altpfad vorhandenen Submillimeter-T-Stoesse.
        /*
         * VORGABE (Nutzer 27.10.): der Belag ist genau die gemessene
         * 8-m-Alley im Umriss, ohne Buchten und ohne die unsichtbare
         * Strasse am inneren Ende. Diese Strasse ist eine eigene,
         * DURCHGAENGIGE Fahrbahn, die ueber der Alley liegt ("die
         * Randstrasse soll durchgaengig sein aber jede alley abdecken") -
         * die ganze Strasse, nicht ein Stueck an der Kreuzung. Sie liegt nur
         * ueber Strassenasphalt, der darunter ganz bleibt.
         */
        var fahrbahn = l.FahrbahnSurface ?? Array.Empty<float2[]>();
        var strassen = l.AisleQuad.Concat(l.PerimeterQuad).Concat(l.CrossQuad).ToArray();
        var ausgenommen = l.Bay.Concat(strassen).ToArray();
        bool Rand(float2 p) => ausgenommen.Append(polygon).Any(r => Abstand(r, p) < .1f);
        var gassen = l.NetLine.Where(n => n.Kind == "entrance" && Zufahrtsarten.IstGasse(n.Art)).ToArray();
        int proben = 0, strassenproben = 0;
        foreach (var netz in gassen)
        {
            var u = math.normalize(netz.B - netz.A);
            var q = new float2(-u.y, u.x);
            var laenge = math.distance(netz.A, netz.B);
            for (float t = -3.89f; t < laenge + 9f; t += .37f)
            for (float quer = -3.89f; quer < 3.9f; quer += .19f)
            {
                var p = netz.A + u * t + q * quer;
                if (!Innen(polygon, p) || Rand(p)) continue;
                var belegt = road.Any(r => Innen(r, p));
                if (l.Bay.Any(r => Innen(r, p)))
                {
                    Pruefe(!belegt, name + " kein Alley-Belag auf einer Bucht " + p);
                    continue;
                }
                if (strassen.Any(r => Innen(r, p)))
                {
                    // Nur die Strasse, in der der innere Knoten B liegt -
                    // nicht eine, die dahinter weiterlaeuft.
                    var amEnde = AmEnde(netz.B, strassen);
                    if (!amEnde.Any(r => Innen(r, p)) || !Durchgehend(netz.B, p, amEnde)) continue;
                    strassenproben++;
                    Pruefe(!belegt, name + " kein Alley-Belag auf der unsichtbaren Strasse " + p);
                    Pruefe(fahrbahn.Any(r => Innen(r, p)), name + " Fahrbahn der unsichtbare Strasse liegt ueber der Alley " + p);
                    Pruefe(l.AsphaltSurface.Any(r => Innen(r, p)), name + " Strassenasphalt unter der Fahrbahn bleibt " + p);
                    continue;
                }
                if (t >= laenge - .05f) continue;
                proben++;
                Pruefe(belegt, name + " 8-m-Alley abgedeckt " + p);
                Pruefe(!l.AsphaltSurface.Concat(l.GrassSurface).Any(r => Innen(r, p)),
                    name + " Alley aus Asphalt und Gras ausgespart " + p);
            }
        }
        Pruefe(proben > 0, name + " unabhaengige Gassenproben");
        // Die GANZE Strasse, an der eine Alley endet, ist Fahrbahn - jede
        // Strassenart, deren Streifen den inneren Knoten einer Alley tragen.
        // 1 m Abstand zum Viereckrand: ohne Randstrasse kommen die Vierecke
        // aus den Strassenlinien und stehen an den Enden bis 0,3 m ueber die
        // Fahrgassenzellen hinaus (gemessen 1/False). Am Alley-Knoten prueft
        // die Probe oben und der Eckalley-Test ohne diesen Abstand.
        foreach (var art in new[] { l.PerimeterQuad, l.AisleQuad, l.CrossQuad })
        {
            if (!gassen.Any(n => art.Any(r => Innen(r, n.B) || Abstand(r, n.B) < .05f))) continue;
            foreach (var r in art)
            {
                var lo = r.Aggregate(new float2(float.MaxValue), math.min);
                var hi = r.Aggregate(new float2(float.MinValue), math.max);
                for (float x = lo.x + .05f; x < hi.x; x += .53f)
                for (float y = lo.y + .05f; y < hi.y; y += .53f)
                {
                    var p = new float2(x, y);
                    if (!Innen(r, p) || Abstand(r, p) < 1f || Rand(p) || !Innen(polygon, p)) continue;
                    Pruefe(fahrbahn.Any(f => Innen(f, p)), name + " unsichtbare Strasse durchgaengig als Fahrbahn " + p);
                }
            }
        }
        // Durchgaengig UEBER die Alley hinweg: links und rechts der Einmuendung
        // dasselbe Fahrbahnstueck, solange es derselbe Asphaltring ist.
        foreach (var netz in gassen)
        {
            var amEnde = AmEnde(netz.B, strassen);
            if (amEnde.Length == 0) continue;
            var u = math.normalize(netz.B - netz.A);
            var q = new float2(-u.y, u.x);
            foreach (var seite in new[] { q, -q })
            {
                var p = netz.B + seite * 5.5f;
                if (!amEnde.Any(r => Innen(r, p)) || Rand(p) || Rand(netz.B)) continue;
                var ring = l.AsphaltSurface.FirstOrDefault(a => Innen(a, netz.B));
                if (ring == null || !Innen(ring, p)) continue;
                Pruefe(fahrbahn.Any(f => Innen(f, netz.B) && Innen(f, p)),
                    name + " Fahrbahn ueber die Alley hinweg durchgaengig (unsichtbare Strasse) " + p);
            }
        }
        // Kein Belag ausserhalb der Alley: jede Probe in einem Belagring
        // liegt im 8-m-Streifen einer Gasse.
        foreach (var r in road)
        {
            var lo = r.Aggregate(new float2(float.MaxValue), math.min);
            var hi = r.Aggregate(new float2(float.MinValue), math.max);
            for (float x = lo.x + .05f; x < hi.x; x += .29f)
            for (float y = lo.y + .05f; y < hi.y; y += .29f)
            {
                var p = new float2(x, y);
                if (!Innen(r, p) || Abstand(r, p) < .05f) continue;
                Pruefe(gassen.Any(n =>
                {
                    var u = math.normalize(n.B - n.A);
                    var d = p - n.A;
                    var t = math.dot(d, u);
                    return math.abs(u.x * d.y - u.y * d.x) <= 4.05f && t >= -4.05f
                        && t <= math.distance(n.A, n.B) + .05f;
                }), name + " Belag nur im 8-m-Streifen der Alley " + p);
                Pruefe(!strassen.Any(s => Innen(s, p)), name + " Belag nicht auf unsichtbarer Strasse " + p);
            }
        }
        // Tools gibt der Fahrbahn die Texturlage ihres Asphaltrings: dafuer
        // muss eine ihrer Kanten mit einer Kante des Rings auf derselben
        // Linie liegen, gleich gerichtet und ueberlappend. Ein Ring hat nur
        // einen ersten Knoten, also geht das je Asphaltring fuer EIN Stueck;
        // mehrere Stuecke auf einem Ring gibt es nur, wo eine umlaufende
        // Strasse ihres Lochs wegen geteilt ist ("einheitlich"). Gefordert:
        // auf jedem Asphaltring, der Fahrbahn traegt, passt mindestens eines.
        foreach (var a in l.AsphaltSurface)
        {
            var darauf = fahrbahn.Where(r => r.Any(p => Innen(a, p) || Abstand(a, p) < .02f)
                && Innen(a, r.Aggregate(float2.zero, (x, y) => x + y) / r.Length)).ToArray();
            if (darauf.Length == 0) continue;
            Pruefe(darauf.Any(r => Enumerable.Range(0, a.Length).Any(i =>
                Enumerable.Range(0, r.Length).Any(j => GemeinsameLinie(
                    a[i], a[(i + 1) % a.Length], r[j], r[(j + 1) % r.Length])))),
                name + " Fahrbahn findet die Texturlage ihres Asphaltrings");
        }
        // Die Fahrbahn liegt ganz auf Strassenasphalt.
        foreach (var r in fahrbahn)
        {
            // Der eingesetzte Zwischenknoten darf den Ring fuer CS2 nicht verderben.
            foreach (var a in l.AsphaltSurface)
            for (int i = 0; i < a.Length; i++)
            for (int j = 0; j < r.Length; j++)
            {
                var s0 = a[i]; var s1 = a[(i + 1) % a.Length];
                if (!GemeinsameLinie(s0, s1, r[j], r[(j + 1) % r.Length])) continue;
                var rs = math.normalizesafe(s1 - s0);
                var punkt = s0 + rs * math.max(0f, math.dot(r[j] - s0, rs));
                if (math.distance(punkt, s0) <= .02f || math.distance(punkt, s1) <= .02f) continue;
                var eingesetzt = a.Take(i + 1).Append(punkt).Concat(a.Skip(i + 1)).ToArray();
                Pruefe(Cs2Triangulierung.Dreiecke(eingesetzt) > 0, name + " CS2 nimmt Strassenring mit eingesetztem Knoten an");
            }
            Pruefe(Cs2Triangulierung.Dreiecke(r) > 0, name + " CS2 nimmt Fahrbahn an");
            var lo = r.Aggregate(new float2(float.MaxValue), math.min);
            var hi = r.Aggregate(new float2(float.MinValue), math.max);
            for (float x = lo.x + .05f; x < hi.x; x += .29f)
            for (float y = lo.y + .05f; y < hi.y; y += .29f)
            {
                var p = new float2(x, y);
                if (!Innen(r, p) || Abstand(r, p) < .05f) continue;
                // Asphalt streng; die Strassenvierecke mit 0,5 m, weil sie ohne
                // Randstrasse aus den Linien kommen und an den Enden bis 0,31 m
                // zu kurz sind (gemessen 1/False am Parkplatzrand).
                Pruefe(l.AsphaltSurface.Any(a => Innen(a, p) || Abstand(a, p) < .02f)
                    && strassen.Concat(quads).Any(a => Innen(a, p) || Abstand(a, p) < .5f),
                    name + " Fahrbahn nur ueber Strassenasphalt " + p);
                Pruefe(!l.Bay.Any(a => Innen(a, p) && Abstand(a, p) > .05f)
                    && !l.GrassSurface.Any(a => Innen(a, p) && Abstand(a, p) > .05f)
                    && !road.Any(a => Innen(a, p) && Abstand(a, p) > .05f),
                    name + " Fahrbahn nicht ueber Bucht, Gras oder Alley-Belag " + p);
            }
        }
        // Keine neuen Splitter: jeder kleine Ring (< 2 m2) muss es schon vorher gegeben haben.
        var altFlaechen = asphaltAlt.Concat(grasAlt).Select(Flaeche).ToList();
        foreach (var r in l.AsphaltSurface.Concat(l.GrassSurface).Concat(road))
        {
            var f = Flaeche(r);
            if (f >= 2.0) continue;
            Pruefe(altFlaechen.Any(a => Math.Abs(a - f) < 0.01), name + $" neuer Splitterring {f:F3} m2");
        }
        // Keine Luecke, keine Ueberlappung: Gesamtflaeche wie vorher.
        Pruefe(Math.Abs(differenz) < 0.05, name + $" Flaechensumme unveraendert (Differenz {differenz:F4} m2)");
        Console.WriteLine($"{name}: Ringe Asphalt/Gras/Strasse {asphaltAlt.Length}/{grasAlt.Length}/0 -> "
            + $"{l.AsphaltSurface.Length}/{l.GrassSurface.Length}/{road.Length}; Deckung {differenz:F6} m2, {proben} Gassenproben, {strassenproben} auf Strasse/Bucht");
    }

}
