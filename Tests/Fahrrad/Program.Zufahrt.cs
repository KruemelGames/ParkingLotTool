using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    /**
     * Zufahrt vorab (Nutzer 03.11.2026): eine als moeglich gemeldete Zufahrt aendert
     * das Raster nicht - dieselben Bausteine an denselben Stellen, dazu ihr Weg.
     * Und es gibt an jeder Form mindestens eine moegliche Stelle.
     */
    private static void Zufahrtsvorpruefung()
    {
        var formen = Stilformen.Where(f => f.Name is "Rechteck" or "Schief" or "L" or "Gross" or "Trapez gross").ToArray();
        int moeglich = 0, gesperrt = 0;
        foreach (var stil in new[] { Fahrradstil.Schlicht, Fahrradstil.Rahmen, Fahrradstil.Platz })
        foreach (var layout in Layouts)
        foreach (var form in formen)
        {
            var s = Mit(LayoutSettings.Fahrrad(layout, stil), 1.5);
            s.AutomaticEntrances = false;
            var ring = form.Ring;
            var ohne = ParkingGeometry.Build(ring, s).Fahrrad;
            var pruefung = FahrradGeometrie.Pruefung(ring, s);
            var irgendeine = false;
            for (var kante = 0; kante < ring.Length; kante++)
            {
                var laenge = math.distance(ring[kante], ring[(kante + 1) % ring.Length]);
                for (var entlang = 2f; entlang < laenge - 2f; entlang += 1f)
                {
                    var e = new Entrance { Edge = kante, Along = entlang };
                    if (!pruefung.Moeglich(e)) { gesperrt++; continue; }
                    moeglich++; irgendeine = true;
                    var mit = s.Clone(); mit.Entrances = new[] { e };
                    var plan = ParkingGeometry.Build(ring, mit).Fahrrad;
                    var name = $"Zufahrt vorab {stil}/{layout}/{form.Name}/{kante}/{entlang}";
                    Pruefe(plan.Wege.Any(w => w.Zufahrt), name + " baut ihren Weg");
                    // Kein Baustein kommt dazu oder wandert.
                    Pruefe(plan.Objekte.All(o => ohne.Objekte.Any(q => math.distance(q.Position, o.Position) < .01f && q.Prefab == o.Prefab)),
                        name + " kein Baustein neu oder verschoben");
                    var weg = ohne.Objekte.Where(q => !plan.Objekte.Any(o => math.distance(q.Position, o.Position) < .01f)).ToList();
                    var ohneWege = !ohne.Wege.Any(w => !w.Zufahrt);
                    if (ohneWege)
                    {
                        // Anlage ohne innere Wege: der Stich nimmt nur, was direkt an der Zufahrt steht.
                        var tor = plan.Wege.Where(w => w.Zufahrt).ToList();
                        Pruefe(weg.All(q => tor.Any(w => Abstand(q.Position, w.A, w.B) < 3f)),
                            name + $" ohne Wege fehlt nur das Tor ({weg.Count} Bausteine)");
                    }
                    else if (stil != Fahrradstil.Platz || ohne.Hinweise.Any(h => h.Contains("Platzring")))
                        Pruefe(weg.Count == 0, name + $" Raster unveraendert ({ohne.Objekte.Count} -> {plan.Objekte.Count} Bausteine)");
                    else
                    {
                        // Platz: am getroffenen Ringstueck faellt die Aussenreihe als Tor weg (so festgelegt),
                        // sonst nichts. Tor-Bausteine liegen nahe am Ende des Zufahrtswegs.
                        var ende = plan.Wege.Where(w => w.Zufahrt).Select(w => w.B).ToList();
                        Pruefe(weg.All(q => ende.Any(b => math.distance(b, q.Position) < 7f)),
                            name + $" am Platz fehlt nur das Tor ({weg.Count} Bausteine)");
                    }
                }
            }
            Pruefe(irgendeine, $"Zufahrt vorab {stil}/{layout}/{form.Name}: mindestens eine moegliche Stelle");
        }
        Console.WriteLine($"Zufahrt vorab: {moeglich} moegliche, {gesperrt} gesperrte Stellen");
        Reihenwinkel();
    }

    /** Reihenwinkel wie beim Auto (Nutzer 03.11.2026): Kante, Quer (+90), Fest (+Regler). */
    private static void Reihenwinkel()
    {
        foreach (var stil in new[] { Fahrradstil.Schlicht, Fahrradstil.Rahmen })
        foreach (var layout in Layouts)
        foreach (var form in Stilformen.Where(f => f.Name is "Rechteck" or "Gross" or "Schief"))
        {
            var kante = Mit(LayoutSettings.Fahrrad(layout, stil), 1.5);
            var basis = ParkingGeometry.Build(form.Ring, kante).Fahrrad;
            foreach (var (modus, grad, soll) in new[] { ("quer", 0.0, 90.0), ("fixed", 30.0, 30.0), ("edge", 0.0, 0.0) })
            {
                var s = Mit(LayoutSettings.Fahrrad(layout, stil), 1.5);
                s.AngleMode = modus; s.Angle = grad;
                var plan = ParkingGeometry.Build(form.Ring, s).Fahrrad;
                var name = $"Reihenwinkel {modus} {grad}/{stil}/{layout}/{form.Name}";
                foreach (var p in Messe(plan, form.Ring)) Pruefe(p.Value, name + "/" + p.Key);
                if (plan.Objekte.Count == 0 || basis.Objekte.Count == 0) continue;
                // Drehung der Bausteine gegen "Kante", modulo 90 Grad (Bausteine stehen beidseitig).
                double Mod90(double rad) { var g = rad * 180 / Math.PI % 90; return g < 0 ? g + 90 : g; }
                var d = Math.Abs(Mod90(plan.Objekte[0].Drehung) - Mod90(basis.Objekte[0].Drehung));
                d = Math.Min(d, 90 - d);
                var sollMod = soll % 90; sollMod = Math.Min(sollMod, 90 - sollMod);
                Pruefe(Math.Abs(d - sollMod) < .5, name + $" dreht um {soll} Grad (gemessen {d:F1})");
            }
        }
    }
}
