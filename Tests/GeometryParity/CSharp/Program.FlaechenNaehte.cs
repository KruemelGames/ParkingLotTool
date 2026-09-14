using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ParkingLotTool.Geometry;
using ParkingLotTool.Geometry.Zellen;
using Unity.Mathematics;

/**
 * Ortspruefbarer Export fuer ANTWORT-FLAECHEN.md.
 *
 * Die Produktions-Lochtrennung wird unveraendert ein zweites Mal ausgefuehrt.
 * Diese Diagnosekopie merkt sich lediglich die dort lokal private Menge der
 * gesperrten Kanten. Jede Naht und jede kurze fertige Ringkante wird danach in
 * Weltkoordinaten als JSONL ausgegeben. Die Zuordnung benutzt ausschliesslich
 * Knoten- und Linien-IDs; Abstaende oder Fangradien spielen dabei keine Rolle.
 */
internal static partial class Program
{
    private sealed class NahtoperationDiagnose
    {
        internal int Nummer;
        internal Material Material;
        internal Punkt LochSchwerpunktWelt;
        internal int Pfadzellen;
        internal List<KantenSchluessel> Kanten = new List<KantenSchluessel>();
    }

    private sealed class Nahtformmass
    {
        internal string Name;
        internal double Areal;
        internal int Operationen;
        internal int Lochflaechen;
        internal int Loecher;
        internal int Komponenten;
        internal int Nahtkanten;
        internal int KurzeNahtkanten;
        internal double Nahtlaenge;
        internal int Pfadzellen;
        internal int Verworfen;
        internal int KurzeRinge;
        internal int Halsringe;
        internal int KurzeKanten;
        internal int KurzeGanzAufNaht;
        internal int KurzeTeilweiseAufNaht;
        internal int KurzeNichtAufNaht;
    }

    private static int RunFlaechenNaehte(string ausgabepfad, int grenze)
    {
        var settings = LayoutSettings.Cs2;
        settings.AngleMode = "edge";
        settings.Auto = false;
        settings.Zellen = true;
        settings.Entrances = new[] { new Entrance { Edge = 0, Along = 20 } };

        var formen = new List<(string Name, float2[] Site)>();
        foreach (var fall in Cases) formen.Add((fall.Name, fall.Site));
        formen.AddRange(FormenAusBauprotokoll());
        if (grenze > 0 && formen.Count > grenze)
            formen = formen.Take(grenze).ToList();

        ausgabepfad = Path.GetFullPath(ausgabepfad);
        var ordner = Path.GetDirectoryName(ausgabepfad);
        if (!string.IsNullOrEmpty(ordner)) Directory.CreateDirectory(ordner);
        var json = new JsonSerializerOptions { WriteIndented = false };
        var masse = new List<Nahtformmass>();
        var abgebrochen = 0;
        var gepruefteOperationen = 0;
        var exakteNahtkanten = 0;
        var ringeNurNaht = 0;
        var ringeNurMaterial = 0;
        var ringeGemischt = 0;
        var ringeAnders = 0;
        var gegenprobe = new List<(int Operationen, int Lochflaechen,
            int Loecher, int Kanten)>();

        using (var writer = new StreamWriter(ausgabepfad, false))
        {
            SchreibeJson(writer, json, new
            {
                record = "method",
                generated_utc = DateTime.UtcNow.ToString("O"),
                cs2_minimum_m = Cs2Mindestkante,
                coordinate_system = "world x/y in metres",
                seam_identity = "exact unordered endpoint node IDs",
                simplified_edge_mapping =
                    "exact cyclic raw-ring traversal between endpoint node IDs; exact line ID",
                distance_threshold_used_for_membership = false,
            });

            var versuch = 0;
            var gebaut = 0;
            foreach (var form in formen)
            {
                versuch++;
                Bauergebnis bau;
                try
                {
                    ParkingGeometry.Build(form.Site, settings);
                    bau = BaueFlaechenDiagnose(form.Site, settings);
                }
                catch (Exception fehler)
                {
                    abgebrochen++;
                    SchreibeJson(writer, json, new
                    {
                        record = "form",
                        attempt_index = versuch,
                        form_name = form.Name,
                        built = false,
                        error_type = fehler.GetType().Name,
                        error = fehler.Message,
                    });
                    continue;
                }
                gebaut++;

                var kantenindex = BaueDiagnoseKantenindex(bau.Zellen);
                var diagnose = DiagnoseLochtrennung(
                    bau.Zellen, bau.FlaechenVorTrennung, bau.Rahmen, kantenindex);
                PruefeNahtaequivalenz(bau, diagnose.Operationen,
                    diagnose.Gesperrt, kantenindex);
                gepruefteOperationen += diagnose.Operationen.Count;

                var operationJeKante = new Dictionary<KantenSchluessel, int>();
                foreach (var operation in diagnose.Operationen)
                    foreach (var schluessel in operation.Kanten)
                        operationJeKante.Add(schluessel, operation.Nummer);

                var mass = new Nahtformmass
                {
                    Name = form.Name,
                    Areal = Math.Abs(FlaechenmassRing(form.Site)),
                    Operationen = diagnose.Operationen.Count,
                    Lochflaechen = bau.Lochtrennung.LochflaechenVorher,
                    Loecher = bau.Lochtrennung.LoecherVorher,
                };
                var kurzeDatensaetze = new List<object>();

                foreach (var operation in diagnose.Operationen)
                {
                    var komponenten = Nahtkomponenten(operation.Kanten);
                    mass.Komponenten += komponenten.Values.Distinct().Count();
                    mass.Nahtkanten += operation.Kanten.Count;
                    mass.Pfadzellen += operation.Pfadzellen;
                    var kanten = new List<object>();
                    foreach (var schluessel in operation.Kanten
                                 .OrderBy(k => k.Klein).ThenBy(k => k.Gross))
                    {
                        var funde = kantenindex[schluessel];
                        var kante = funde[0].Kante;
                        var von = bau.Rahmen.NachWelt(kante.Von.Punkt);
                        var nach = bau.Rahmen.NachWelt(kante.Nach.Punkt);
                        var laenge = Geometrie.Laenge(
                            kante.Nach.Punkt - kante.Von.Punkt);
                        mass.Nahtlaenge += laenge;
                        if (laenge < Cs2Mindestkante) mass.KurzeNahtkanten++;
                        kanten.Add(new
                        {
                            key_node_ids = new[] { schluessel.Klein, schluessel.Gross },
                            component = komponenten[schluessel],
                            from_node_id = kante.Von.Id,
                            to_node_id = kante.Nach.Id,
                            from_world = new[] { von.X, von.Y },
                            to_world = new[] { nach.X, nach.Y },
                            length_m = laenge,
                            short_for_cs2 = laenge < Cs2Mindestkante,
                            line_id = kante.Linie.Id,
                            line_kind = kante.Linie.Art.ToString(),
                            line_name = kante.Linie.Name,
                            cells = funde.Select(fund => new
                            {
                                id = fund.Zelle.Id,
                                kind = fund.Zelle.Art.ToString(),
                                material = fund.Zelle.Material.ToString(),
                            }).ToArray(),
                        });
                    }
                    SchreibeJson(writer, json, new
                    {
                        record = "seam",
                        geometry_variant = "actual_with_entrance",
                        attempt_index = versuch,
                        built_index = gebaut,
                        form_name = form.Name,
                        operation = operation.Nummer,
                        material = operation.Material.ToString(),
                        hole_centroid_world = new[]
                        {
                            operation.LochSchwerpunktWelt.X,
                            operation.LochSchwerpunktWelt.Y,
                        },
                        path_cell_count = operation.Pfadzellen,
                        edge_count = operation.Kanten.Count,
                        component_count = komponenten.Values.Distinct().Count(),
                        total_length_m = operation.Kanten.Sum(schluessel =>
                        {
                            var k = kantenindex[schluessel][0].Kante;
                            return Geometrie.Laenge(k.Nach.Punkt - k.Von.Punkt);
                        }),
                        edges = kanten,
                    });
                }

                foreach (var flaeche in bau.Flaechen)
                {
                    var ringe = new List<(string Art, int Index, Ring Ring)>
                    {
                        ("outer", 0, flaeche.Aussenring),
                    };
                    ringe.AddRange(flaeche.Loecher.Select(
                        (ring, index) => ("hole", index, ring)));
                    foreach (var ringinfo in ringe)
                    {
                        var ringmass = RingmassDiagnose(ringinfo.Ring);
                        var istKurz = ringmass.MinKante < Cs2Mindestkante;
                        var istHals = !istKurz
                            && ringmass.MinHals < Cs2Mindestkante;
                        if (istKurz) mass.KurzeRinge++;
                        if (istHals) mass.Halsringe++;
                        if (istKurz || istHals) mass.Verworfen++;
                        var ringbesitz = new HashSet<string>();

                        for (var kantenindexImRing = 0;
                             kantenindexImRing < ringinfo.Ring.Kanten.Count;
                             kantenindexImRing++)
                        {
                            var kante = ringinfo.Ring.Kanten[kantenindexImRing];
                            var laenge = Geometrie.Laenge(
                                kante.Nach.Punkt - kante.Von.Punkt);
                            if (laenge >= Cs2Mindestkante) continue;
                            mass.KurzeKanten++;
                            var roh = ExakteRohkanten(ringinfo.Ring, kante);
                            var rohDatensaetze = new List<object>();
                            var operationen = new HashSet<int>();
                            var alleNaht = true;
                            var irgendeineNaht = false;
                            foreach (var rohsegment in roh)
                            {
                                var schluessel = rohsegment.Schluessel;
                                int operation;
                                var istNaht = operationJeKante.TryGetValue(
                                    schluessel, out operation);
                                irgendeineNaht |= istNaht;
                                alleNaht &= istNaht;
                                if (istNaht) operationen.Add(operation);
                                var besitz = Besitzart(
                                    kantenindex[schluessel], true);
                                ringbesitz.Add(besitz);
                                var von = bau.Rahmen.NachWelt(
                                    rohsegment.Von.Punkt);
                                var nach = bau.Rahmen.NachWelt(
                                    rohsegment.Nach.Punkt);
                                rohDatensaetze.Add(new
                                {
                                    key_node_ids = new[]
                                    {
                                        schluessel.Klein, schluessel.Gross,
                                    },
                                    from_world = new[] { von.X, von.Y },
                                    to_world = new[] { nach.X, nach.Y },
                                    length_m = Geometrie.Laenge(
                                        rohsegment.Nach.Punkt
                                            - rohsegment.Von.Punkt),
                                    ownership = besitz,
                                    seam_operation = istNaht ? (int?)operation : null,
                                });
                            }
                            if (alleNaht) mass.KurzeGanzAufNaht++;
                            else if (irgendeineNaht)
                                mass.KurzeTeilweiseAufNaht++;
                            else mass.KurzeNichtAufNaht++;

                            var vonWelt = bau.Rahmen.NachWelt(kante.Von.Punkt);
                            var nachWelt = bau.Rahmen.NachWelt(kante.Nach.Punkt);
                            var vonFloat = new float2(
                                (float)vonWelt.X, (float)vonWelt.Y);
                            var nachFloat = new float2(
                                (float)nachWelt.X, (float)nachWelt.Y);
                            kurzeDatensaetze.Add(new
                            {
                                record = "short_edge",
                                geometry_variant = "actual_with_entrance",
                                attempt_index = versuch,
                                built_index = gebaut,
                                form_name = form.Name,
                                surface_id = flaeche.Id,
                                material = flaeche.Material.ToString(),
                                ring_type = ringinfo.Art,
                                ring_index = ringinfo.Index,
                                edge_index = kantenindexImRing,
                                from_node_id = kante.Von.Id,
                                to_node_id = kante.Nach.Id,
                                from_world = new[] { vonWelt.X, vonWelt.Y },
                                to_world = new[] { nachWelt.X, nachWelt.Y },
                                length_local_double_m = laenge,
                                length_world_float_m = math.length(
                                    nachFloat - vonFloat),
                                line_id = kante.Linie.Id,
                                line_kind = kante.Linie.Art.ToString(),
                                line_name = kante.Linie.Name,
                                on_seam = alleNaht,
                                partly_on_seam = irgendeineNaht && !alleNaht,
                                seam_operations = operationen.OrderBy(x => x).ToArray(),
                                membership_rule =
                                    "exact raw edge key; no distance threshold",
                                raw_segments = rohDatensaetze,
                            });
                        }

                        if (istKurz)
                        {
                            var hatMaterial = ringbesitz.Contains("Materialgrenze");
                            var hatNaht = ringbesitz.Contains(
                                "Lochtrennnaht (gleiches Material)");
                            var anderes = ringbesitz.Any(wert =>
                                wert != "Materialgrenze"
                                && wert != "Lochtrennnaht (gleiches Material)");
                            if (anderes) ringeAnders++;
                            else if (hatMaterial && hatNaht) ringeGemischt++;
                            else if (hatMaterial) ringeNurMaterial++;
                            else if (hatNaht) ringeNurNaht++;
                            else ringeAnders++;
                        }
                    }
                }

                masse.Add(mass);
                exakteNahtkanten += mass.Nahtkanten;
                var arealWelt = bau.ArealLokal.Punkte
                    .Select(punkt => bau.Rahmen.NachWelt(punkt))
                    .Select(punkt => new[] { punkt.X, punkt.Y }).ToArray();
                SchreibeJson(writer, json, new
                {
                    record = "form",
                    geometry_variant = "actual_with_entrance",
                    attempt_index = versuch,
                    built_index = gebaut,
                    form_name = form.Name,
                    built = true,
                    site_world = arealWelt,
                    area_m2 = mass.Areal,
                    seam_operation_count = mass.Operationen,
                    hole_surface_count_before = mass.Lochflaechen,
                    hole_count_before = mass.Loecher,
                    seam_component_count = mass.Komponenten,
                    seam_edge_count = mass.Nahtkanten,
                    seam_total_length_m = mass.Nahtlaenge,
                    seam_short_raw_edge_count = mass.KurzeNahtkanten,
                    path_cell_count = mass.Pfadzellen,
                    rejected_ring_count = mass.Verworfen,
                    short_ring_count = mass.KurzeRinge,
                    neck_only_ring_count = mass.Halsringe,
                    short_simplified_edge_count = mass.KurzeKanten,
                    short_edges_entirely_on_seam = mass.KurzeGanzAufNaht,
                    short_edges_partly_on_seam = mass.KurzeTeilweiseAufNaht,
                    short_edges_not_on_seam = mass.KurzeNichtAufNaht,
                    clean = mass.Verworfen == 0,
                    equivalence_verified = true,
                });
                foreach (var datensatz in kurzeDatensaetze)
                    SchreibeJson(writer, json, datensatz);

                // Exakt die vom vorhandenen `--flaechen` verwendete
                // Gegenprobe: anderer Bau ohne Zufahrt. Ihre Nahtzahl darf
                // nicht mit den oben vermessenen Ausgaberings verwechselt
                // werden; sie wird nur exportiert, um 185x2/2x1 nachzuweisen.
                var gegenbau = BaueWieFlaechenNahtzaehler(
                    form.Site, settings);
                var gegenindex = BaueDiagnoseKantenindex(gegenbau.Zellen);
                var gegendiagnose = DiagnoseLochtrennung(
                    gegenbau.Zellen, gegenbau.FlaechenVorTrennung,
                    gegenbau.Rahmen, gegenindex);
                PruefeNahtaequivalenz(gegenbau, gegendiagnose.Operationen,
                    gegendiagnose.Gesperrt, gegenindex);
                gegenprobe.Add((gegendiagnose.Operationen.Count,
                    gegenbau.Lochtrennung.LochflaechenVorher,
                    gegenbau.Lochtrennung.LoecherVorher,
                    gegendiagnose.Gesperrt.Count));
                SchreibeJson(writer, json, new
                {
                    record = "counterprobe_form",
                    geometry_variant = "counterprobe_without_entrance",
                    attempt_index = versuch,
                    built_index = gebaut,
                    form_name = form.Name,
                    seam_operation_count = gegendiagnose.Operationen.Count,
                    hole_surface_count_before =
                        gegenbau.Lochtrennung.LochflaechenVorher,
                    hole_count_before = gegenbau.Lochtrennung.LoecherVorher,
                    seam_edge_count = gegendiagnose.Gesperrt.Count,
                    warning = "not the geometry measured by the short-edge records",
                });
                foreach (var operation in gegendiagnose.Operationen)
                {
                    var komponenten = Nahtkomponenten(operation.Kanten);
                    var kanten = operation.Kanten.OrderBy(k => k.Klein)
                        .ThenBy(k => k.Gross).Select(schluessel =>
                        {
                            var kante = gegenindex[schluessel][0].Kante;
                            var von = gegenbau.Rahmen.NachWelt(
                                kante.Von.Punkt);
                            var nach = gegenbau.Rahmen.NachWelt(
                                kante.Nach.Punkt);
                            return new
                            {
                                key_node_ids = new[]
                                {
                                    schluessel.Klein, schluessel.Gross,
                                },
                                component = komponenten[schluessel],
                                from_world = new[] { von.X, von.Y },
                                to_world = new[] { nach.X, nach.Y },
                                length_m = Geometrie.Laenge(
                                    kante.Nach.Punkt - kante.Von.Punkt),
                            };
                        }).ToArray();
                    SchreibeJson(writer, json, new
                    {
                        record = "counterprobe_seam",
                        geometry_variant = "counterprobe_without_entrance",
                        attempt_index = versuch,
                        built_index = gebaut,
                        form_name = form.Name,
                        operation = operation.Nummer,
                        material = operation.Material.ToString(),
                        hole_centroid_world = new[]
                        {
                            operation.LochSchwerpunktWelt.X,
                            operation.LochSchwerpunktWelt.Y,
                        },
                        path_cell_count = operation.Pfadzellen,
                        edge_count = operation.Kanten.Count,
                        component_count = komponenten.Values.Distinct().Count(),
                        edges = kanten,
                    });
                }
            }
        }

        Console.WriteLine($"FLAECHENNAEHTE ueber {formen.Count} Formen");
        Console.WriteLine($"  gebaut {masse.Count}, abgebrochen {abgebrochen}");
        Console.WriteLine($"  Export {ausgabepfad}");
        Console.WriteLine($"  Diagnosekopie gegen Produktionsbericht und "
            + $"Endrand aequivalent: {masse.Count}/{masse.Count} Formen, "
            + $"{gepruefteOperationen} Operationen, {exakteNahtkanten} Kanten");
        Console.WriteLine("  Trennoperationen je Form:");
        foreach (var gruppe in masse.GroupBy(mass => mass.Operationen)
                     .OrderBy(gruppe => gruppe.Key))
            Console.WriteLine($"    {gruppe.Key}: {gruppe.Count()} Formen, "
                + $"{gruppe.Count(mass => mass.Verworfen == 0)} sauber, "
                + $"verworfen im Mittel {gruppe.Average(mass => mass.Verworfen):F2}");
        Console.WriteLine($"  Davor im tatsaechlichen Bau: "
            + $"{masse.Sum(m => m.Lochflaechen)} Lochflaechen, "
            + $"{masse.Sum(m => m.Loecher)} Loecher");
        Console.WriteLine("  Gegenprobe aus --flaechen, OHNE Zufahrt:");
        foreach (var gruppe in gegenprobe.GroupBy(mass => mass.Operationen)
                     .OrderBy(gruppe => gruppe.Key))
            Console.WriteLine($"    {gruppe.Key}: {gruppe.Count()} Formen");
        Console.WriteLine($"    zusammen {gegenprobe.Sum(m => m.Lochflaechen)} "
            + $"Lochflaechen, {gegenprobe.Sum(m => m.Loecher)} Loecher, "
            + $"{gegenprobe.Sum(m => m.Kanten)} gesperrte Kanten");
        Console.WriteLine("  Der zweite Faktor -- Geometrie der Naht:");
        DruckeNahtvergleich("sauber", masse.Where(mass => mass.Verworfen == 0));
        DruckeNahtvergleich("verworfen", masse.Where(mass => mass.Verworfen != 0));
        Console.WriteLine($"    Spanne Nahtkanten/Form "
            + $"{masse.Min(m => m.Nahtkanten)}..{masse.Max(m => m.Nahtkanten)}, "
            + $"Nahtlaenge {masse.Min(m => m.Nahtlaenge):F2}.."
            + $"{masse.Max(m => m.Nahtlaenge):F2} m, kurze rohe Nahtkanten "
            + $"{masse.Min(m => m.KurzeNahtkanten)}.."
            + $"{masse.Max(m => m.KurzeNahtkanten)}");
        Console.WriteLine($"    Pearson verworfene Ringe ~ Operationen "
            + $"{Pearson(masse, m => m.Verworfen, m => m.Operationen):F3}");
        Console.WriteLine($"    Pearson verworfene Ringe ~ Nahtkanten "
            + $"{Pearson(masse, m => m.Verworfen, m => m.Nahtkanten):F3}");
        Console.WriteLine($"    Pearson verworfene Ringe ~ Nahtkomponenten "
            + $"{Pearson(masse, m => m.Verworfen, m => m.Komponenten):F3}");
        Console.WriteLine($"    Pearson verworfene Ringe ~ Nahtlaenge "
            + $"{Pearson(masse, m => m.Verworfen, m => m.Nahtlaenge):F3}");
        Console.WriteLine($"    Pearson verworfene Ringe ~ kurze rohe Nahtkanten "
            + $"{Pearson(masse, m => m.Verworfen, m => m.KurzeNahtkanten):F3}");
        Console.WriteLine("  Nach vorhandener kurzer Rohkante auf der Naht:");
        foreach (var gruppe in masse.GroupBy(mass => mass.KurzeNahtkanten == 0
                     ? "0" : ">0").OrderBy(gruppe => gruppe.Key))
            Console.WriteLine($"    {gruppe.Key}: {gruppe.Count()} Formen, "
                + $"{gruppe.Count(mass => mass.Verworfen == 0)} sauber, "
                + $"{gruppe.Sum(mass => mass.Verworfen)} verworfene Ringe");
        Console.WriteLine($"  Kurze fertige Kanten {masse.Sum(m => m.KurzeKanten)}: "
            + $"vollstaendig Naht {masse.Sum(m => m.KurzeGanzAufNaht)}, "
            + $"teilweise {masse.Sum(m => m.KurzeTeilweiseAufNaht)}, "
            + $"nicht Naht {masse.Sum(m => m.KurzeNichtAufNaht)}");
        Console.WriteLine($"  Kurze Ringe exakt: nur Naht {ringeNurNaht}, "
            + $"nur Materialgrenze {ringeNurMaterial}, beides {ringeGemischt}, "
            + $"anderes {ringeAnders}");
        return 0;
    }

    private static void SchreibeJson(
        TextWriter writer, JsonSerializerOptions optionen, object wert) =>
        writer.WriteLine(JsonSerializer.Serialize(wert, optionen));

    private static void DruckeNahtvergleich(
        string name, IEnumerable<Nahtformmass> quelle)
    {
        var werte = quelle.ToArray();
        Console.WriteLine($"    {name,-9} {werte.Length,3} Formen: Operationen "
            + $"{werte.Average(m => m.Operationen):F2}, Pfadzellen "
            + $"{werte.Average(m => m.Pfadzellen):F2}, Nahtkanten "
            + $"{werte.Average(m => m.Nahtkanten):F2}, Nahtlaenge "
            + $"{werte.Average(m => m.Nahtlaenge):F2} m, kurze rohe "
            + $"Nahtkanten {werte.Average(m => m.KurzeNahtkanten):F2}, "
            + $"Komponenten {werte.Average(m => m.Komponenten):F2}");
    }

    /** Exakte Bauvariante des Nahtzaehlers in `LoecherDerForm`. */
    private static Bauergebnis BaueWieFlaechenNahtzaehler(
        float2[] site, LayoutSettings settings)
    {
        var punkte = site.Select(p => new Punkt(p.x, p.y)).ToList();
        return Layoutbauer.Baue(
            new Formdefinition("Nahtgegenprobe", punkte),
            new Zelleneinstellungen
            {
                Randabstand = settings.Es,
                Fahrgassenbreite = settings.Ai,
                Querstrassenbreite = settings.Cw,
                Buchttiefe = settings.Sl,
                Buchtbreite = settings.Sw,
                Gruenstreifenbreite = settings.Md,
                Querstrassenabstand = settings.Cr,
                Querstrassenkappen = settings.Qk,
                Reihenwinkel = null,
            },
            Array.Empty<Zufahrtsvorgabe>());
    }

    private static double Pearson(
        IReadOnlyList<Nahtformmass> werte,
        Func<Nahtformmass, double> x,
        Func<Nahtformmass, double> y)
    {
        var mittelX = werte.Average(x);
        var mittelY = werte.Average(y);
        var zaehler = 0.0;
        var nennerX = 0.0;
        var nennerY = 0.0;
        foreach (var wert in werte)
        {
            var dx = x(wert) - mittelX;
            var dy = y(wert) - mittelY;
            zaehler += dx * dy;
            nennerX += dx * dx;
            nennerY += dy * dy;
        }
        return zaehler / Math.Sqrt(nennerX * nennerY);
    }

}
