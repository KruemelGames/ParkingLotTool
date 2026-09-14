using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using ParkingLotTool.Geometry.Zellen;
using Unity.Mathematics;

internal static partial class Program
{
    private static int RunRandstrassen(bool mutation = false)
    {
        var fehler = 0;
        void Pruefe(bool ok, string meldung)
        {
            if (ok) return;
            fehler++; Console.WriteLine("FEHLER: " + meldung);
        }
        foreach (var n in new[] { 3, 4, 5, 6, 7, 8, 9, 12 })
        {
            var bau = Layoutbauer.Baue(new Formdefinition("Ringvergleich", QuerPolygon.ToList()),
                new Zelleneinstellungen { Querstrassenabstand = (n + 2) * 3 },
                new[] { new Zufahrtsvorgabe(0, 40) });
            var quer = bau.Zellen.Count(z => z.Art == Zellart.Querstrasse);
            if (n >= 5) Console.WriteLine($"Ring N={n}: {bau.Buchtenzahl}/{quer}");
            if (n >= 5) Pruefe(bau.Buchtenzahl == (n == 5 ? 191 : 209)
                && quer == (n == 5 ? 20 : 10), "Ring N=" + n);
            else Pruefe(quer > 0, "N=" + n + " liefert keine Verbindung");
        }
        var form = new Formdefinition("80 x 64", new[] { new Punkt(0, 0), new Punkt(80, 0), new Punkt(80, 64), new Punkt(0, 64) });
        foreach (var ring in new[] { true, false })
        foreach (var median in new[] { false, true })
        foreach (var kappen in new[] { false, true })
        {
            var e = new Zelleneinstellungen { Reihenwinkel = 0, Randstrassen = ring,
                Gruenstreifenbreite = median ? 2.5 : 0, Querstrassenkappen = kappen };
            var bau = Layoutbauer.Baue(form, e, new[] { new Zufahrtsvorgabe(0, 40) });
            Console.WriteLine($"80x64 Ring={ring} Median={median} Kappen={kappen}: {bau.Buchtenzahl} Buchten");
            Pruefe(bau.Buchtenzahl > 0, "Leeres Layout");
            if (!ring) Pruefe(bau.Buchtenzahl == (kappen ? 112 : 132), "80x64: Buchtenverlust bei Median/Kappen");
            if (!ring) foreach (var band in bau.Bandplan.Baender.Where(b => b.Art == Zellart.Bucht))
                Console.WriteLine($"  Reihe {band.Anfang:F3}..{band.Ende:F3}: {bau.Zellen.Where(z => z.BuchtId.HasValue && z.Polygon.Punkte.All(p => p.Y >= band.Anfang - 1e-6 && p.Y <= band.Ende + 1e-6)).Select(z => z.BuchtId).Distinct().Count()} Buchten");
            if (ring) continue;
            Pruefe(bau.Zellen.All(z => z.Art != Zellart.Randstrasse), "Ringzellen trotz aus");
            Pruefe(bau.Randbuchten.Count == 0, "Randbuchten trotz aus");
            Pruefe(bau.Ringlos != null && bau.Ringlos.Gassen.Count == 3, "Schalter wirkungslos / 3 Gassen fehlen");
            if (bau.Ringlos == null) continue;
            var ausgabeSettings = LayoutSettings.Cs2;
            ausgabeSettings.Randstrassen = false; ausgabeSettings.Md = e.Gruenstreifenbreite;
            ausgabeSettings.Qk = kappen; ausgabeSettings.AngleMode = "fixed";
            ausgabeSettings.Angle = 0; ausgabeSettings.Auto = false;
            ausgabeSettings.Entrances = new[] { new Entrance { Edge = 0, Along = 40 } };
            var ausgabe = ParkingGeometry.Build(form.Punkte.Select(p => new float2((float)p.X, (float)p.Y)).ToArray(), ausgabeSettings);
            var ringe = ausgabe.GrassSurface.Concat(ausgabe.AsphaltSurface).ToArray();
            var unbaubar = ringe.Count(r => r == null || r.Length < 3 || Cs2Triangulierung.Dreiecke(r) == 0);
            Console.WriteLine($"  Export: {ausgabe.Stalls} Buchten, {ringe.Length} Flaechen, {unbaubar} unbaubar; {string.Join(" | ", ausgabe.Warnings)}");
            Pruefe(unbaubar == 0, "80x64: unbaubare Flaechen");
            Pruefe(ausgabe.Stalls == (kappen ? 110 : 130), "80x64: Buchten fehlen im Export");
            if (median && kappen)
            {
                var durchgehend = ausgabe.GrassSurface.Count(r => new[] { 0.5, 1.15 }.All(y =>
                    Geometrie.Enthaelt(r.Select(p => new Punkt(p.x, p.y)).ToArray(), new Punkt(20, y))));
                Console.WriteLine($"  Gruen ueber Rasterrand y=1: {durchgehend} gemeinsame Flaeche(n)");
                Pruefe(durchgehend == 1, "0,30-m-Rest ist nicht mit dem Randband verbunden");
            }
            Pruefe(bau.Ringlos.Fusswege.Count == 2, "2 Fussstreifen fehlen");
            Pruefe(bau.Ringlos.Querwege.Count(q => q.Querstrasse.Notwendig) == 2, "Notwendige Verbindungen fehlen");
            Pruefe(bau.Ringlos.Zufahrten.Count == 1, "Zufahrt fehlt");
            foreach (var w in bau.Ringlos.Fusswege)
            {
                Pruefe(Math.Abs(w.B.X - w.A.X) < 1e-6 && Math.Abs(w.Breite - 2) < 1e-6, "Fussstreifen nicht gerade / 2 m");
                Pruefe(bau.Ringlos.Gassen.All(g => w.A.X <= g.A.X - 1 + 1e-6 || w.A.X >= g.B.X + 1 - 1e-6), "Fussweg auf Gassenende");
                foreach (var z in bau.Zellen.Where(z => z.BuchtId.HasValue))
                    Pruefe(!Ringlosplan.Ueberlappt(z.Polygon.Punkte.ToArray(), w.Ecken), "Bucht unter Fussweg");
            }
        }
        var settings = LayoutSettings.Cs2;
        settings.Randstrassen = false; settings.Qk = false; settings.Md = 0;
        settings.AngleMode = "fixed"; settings.Angle = 0; settings.Auto = false;
        settings.Entrances = new[] { new Entrance { Edge = 0, Along = 40 } };
        var export = ParkingGeometry.Build(form.Punkte.Select(v => new float2((float)v.X, (float)v.Y)).ToArray(), settings);
        Console.WriteLine($"Ausgabe: {export.Stalls} Buchten; Netz {export.NetLine.Length}; Warnungen: {string.Join(" | ", export.Warnings)}");
        Pruefe(export.Stalls > 100 && export.NetLine.All(n => n.Kind != "perimeter"), "Ausgabe ignoriert Schalter");
        Pruefe(export.NetLine.Count(n => n.Art == Zufahrtsart.Fussweg) == 2, "Ausgabe verliert Fusswege");
        var klein = Layoutbauer.Baue(new Formdefinition("Eine Gasse", new[] { new Punkt(0, 0), new Punkt(40, 0), new Punkt(40, 21), new Punkt(0, 21) }),
            new Zelleneinstellungen { Randstrassen = false, Reihenwinkel = 0, Querstrassenkappen = false });
        Pruefe(klein.Buchtenzahl > 0 && klein.Ringlos.Fusswege.Count == 0, "Eine Gasse: Buchten fehlen oder Endweg vorhanden");
        var grossN = Layoutbauer.Baue(form, new Zelleneinstellungen { Randstrassen = false, Reihenwinkel = 0, Querstrassenabstand = 3000 }, new[] { new Zufahrtsvorgabe(0, 40) });
        Pruefe(grossN.Ringlos.Querwege.Count == 2, "Grosses N isoliert Gassen");
        foreach (var variante in new[] { "schraeg", "seitlich", "L", "U", "Bauland", "Randzoning", "Teilwinkel" })
        {
            var s = settings.Clone();
            var site = new[] { new float2(0, 0), new float2(160, 0), new float2(160, 105), new float2(0, 105) };
            if (variante == "schraeg") s.Angle = 25;
            if (variante == "seitlich") s.Entrances = new[] { new Entrance { Edge = 3, Along = 40 } };
            if (variante == "L") site = new[] { new float2(0, 0), new float2(160, 0), new float2(160, 48), new float2(80, 48), new float2(80, 105), new float2(0, 105) };
            if (variante == "U") site = new[] { new float2(0, 0), new float2(160, 0), new float2(160, 105), new float2(110, 105), new float2(110, 45), new float2(50, 45), new float2(50, 105), new float2(0, 105) };
            if (variante == "Bauland") s.Zoningflaechen = new[] { new ParkingGeometry.Zoningflaeche { Ecke = new float2(65, 40), Spalten = 2, Reihen = 2, Rand = 8 } };
            if (variante == "Randzoning") s.Randzoning = new[] { new ParkingGeometry.RandzoningLinie { A = site[0], B = site[1] } };
            if (variante == "Teilwinkel") s.TeilflaechenAusrichtungen = new[] { new TeilflaechenAusrichtung { Anker = new float2(80, 50), Winkel = 25 } };
            try
            {
                var l = ParkingGeometry.Build(site, s);
                if (mutation && variante == "Randzoning")
                {
                    // Nur den inneren Zubringer entfernen; alle Fahrgassen
                    // bleiben als erreichbare Pflichtziele im Export stehen.
                    var innen = Array.FindIndex(l.NetLine, n => n.Kind == "entrance"
                        && n.Art != Zufahrtsart.Fussweg && Math.Min(n.A.y, n.B.y) > 0.001);
                    Pruefe(innen >= 0, "Mutation findet keinen inneren Zubringer");
                    l.NetLine = l.NetLine.Where((_, i) => i != innen).ToArray();
                    Console.WriteLine("MUTATION: inneren Zubringer entfernt");
                }
                var erreichbar = RandstrassenErreichbarkeit.Pruefe(l.NetLine, site, s.Ai, s.Cw);
                Console.WriteLine($"Auto-Erreichbarkeit {variante}: {erreichbar.Erreicht}/{erreichbar.Autowege} Segmente; {erreichbar.Quellen} aeussere Zufahrten, {erreichbar.Randanschluesse} Zoning-Randanschluesse");
                Pruefe(erreichbar.Vollstaendig, variante + ": isolierte Autowege oder keine aeussere Zufahrt");
                var ueber = 0; double maxTiefe = 0; double maxFlaeche = 0;
                foreach (var net in l.NetLine.Where(n => n.Kind != "zoning"))
                {
                    if (math.distance(net.A, net.B) < 1e-5) continue;
                    var w = new Ringlosplan.Weg { A = new Punkt(net.A.x, net.A.y), B = new Punkt(net.B.x, net.B.y),
                        Breite = net.Art == Zufahrtsart.Fussweg ? 2 : net.Kind == "cross" ? s.Cw : s.Ai };
                    foreach (var bay in l.Bay)
                        if (ParkingOverlapGeometry.Measure(bay.Select(v => new double2(v.x, v.y)).ToArray(),
                            w.Ecken.Select(v => new double2(v.X, v.Y)).ToArray(), out var messung))
                        {
                            maxTiefe = Math.Max(maxTiefe, messung.Penetration);
                            maxFlaeche = Math.Max(maxFlaeche, messung.Area);
                            // Exportierte Koordinaten sind float: bei 160 m liegt eine
                            // ULP bei rund 0,000019 m. Gemessen wurden maximal
                            // 0,0000081 m reine Rundung an gemeinsamen Grenzen.
                            var koordinatenmass = Math.Max(1, bay.Max(v => Math.Max(Math.Abs(v.x), Math.Abs(v.y))));
                            var rundungsgrenze = 4 * koordinatenmass * Math.Pow(2, -23);
                            if (messung.Penetration > rundungsgrenze) ueber++;
                        }
                }
                Console.WriteLine($"Kreuzprobe {variante}: {l.Stalls} Buchten, {l.NetLine.Length} Wege, {ueber} Bucht/Weg-Ueberlappungen (max {maxTiefe:R} m / {maxFlaeche:R} m2); {string.Join(" | ", l.Warnings)}");
                Pruefe(l.Stalls > 0 && ueber == 0, variante + ": leer oder Bucht/Weg-Ueberlappung");
            }
            catch (Exception ex) { Pruefe(false, variante + ": " + ex.Message); }
        }
        foreach (var hoehe in new[] { 15.0, 9.0 })
        {
            var b = Layoutbauer.Baue(new Formdefinition("Klein", new[] { new Punkt(0, 0), new Punkt(40, 0), new Punkt(40, hoehe), new Punkt(0, hoehe) }),
                new Zelleneinstellungen { Randstrassen = false, Reihenwinkel = 0, Querstrassenkappen = false });
            Console.WriteLine($"Klein 40x{hoehe}: {b.Buchtenzahl} Buchten");
            Pruefe(hoehe != 15 || b.Buchtenzahl > 0, "Einseitige Reihe nicht gefunden");
        }
        Console.WriteLine($"Randstrassen: {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }
}
