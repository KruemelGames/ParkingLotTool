using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using ParkingLotTool.Geometry.Zellen;
using Unity.Mathematics;

/**
 * Nur Messgeraet fuer ANTWORT-FLAECHEN.md.
 *
 * Es veraendert weder Planung noch Ausgabe. Die fertigen Ringe werden auf
 * ihre Rohkanten im Zellnetz zurueckgefuehrt. Damit laesst sich unterscheiden,
 * ob eine Haarstelle auf einer echten Materialgrenze liegt oder erst sichtbar
 * wurde, weil Lochtrennung.cs zwei gleichmaterialige Zellen getrennt hat.
 */
internal static partial class Program
{
    private sealed class StudienEngstelle
    {
        internal int Punkt;
        internal int Segment;
        internal double Abstand = double.PositiveInfinity;
    }

    private sealed class StudienWinkelkante
    {
        internal double Winkel;
        internal double Rasterabweichung;
        internal double Laenge;
        internal int KurzeRinge;
        internal int Ringkanten;
        internal int KurzeRingkanten;
    }

    private sealed class StudienKonkavitaet
    {
        internal int Gesamt;
        internal int Verworfen;
        internal double GroessteSaubereFlaeche;
        internal double GroessteVerworfeneFlaeche;
    }

    private static int RunFlaechenStudie(int grenze)
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

        var gebaut = 0;
        var abgebrochen = 0;
        var aktuelleSaubereFormen = 0;
        var proxySaubereFormen = 0;
        var kurzeRinge = 0;
        var kurzeNurMaterial = 0;
        var kurzeNurNaht = 0;
        var kurzeGemischt = 0;
        var kurzeAndere = 0;
        var kurzeAsphalt = 0;
        var kurzeGruen = 0;
        var kurzeKanten = 0;
        var kurzeKantenAussen = 0;
        var kurzeKantenTeilungsnaht = 0;
        var kurzeKantenParallel = 0;
        var halsRinge = 0;
        var halsVorTrennung = 0;
        var halsMitNaht = 0;
        var halsOhneNaht = 0;
        var halsAsphalt = 0;
        var halsGruen = 0;
        var spitzeRinge = 0;
        var spitzeVorTrennung = 0;
        var spitzMitNaht = 0;
        var spitzOhneNaht = 0;
        var spitzAussenknoten = 0;
        var spitzUndVerworfen = 0;
        var groessterKurzerNurNaht = 0.0;
        var groessterKurzerMaterial = 0.0;
        var groessterHals = 0.0;
        var engsteHalsweite = double.PositiveInfinity;
        var nachKurzbesitz = new Dictionary<string, int>();
        var nachHalslinien = new Dictionary<string, int>();
        var nachSpitzlinien = new Dictionary<string, int>();
        var winkelkanten = new List<StudienWinkelkante>();
        var konkav = new Dictionary<string, StudienKonkavitaet>();

        foreach (var form in formen)
        {
            Bauergebnis bau;
            try
            {
                // Derselbe Filter wie `--flaechen`: Ein interner Bau, den der
                // oeffentliche Adapter nicht ausgeben kann, gehoert nicht in
                // den Vergleich 77/187 und 350/39/49.
                ParkingGeometry.Build(form.Site, settings);
                bau = BaueFlaechenDiagnose(form.Site, settings);
            }
            catch
            {
                abgebrochen++;
                continue;
            }
            gebaut++;

            foreach (var flaeche in bau.FlaechenVorTrennung)
                foreach (var ring in flaeche.AlleRinge)
                {
                    var mass = RingmassDiagnose(ring);
                    if (mass.MinWinkel < SpitzGrad) spitzeVorTrennung++;
                    if (mass.MinKante >= Cs2Mindestkante
                        && mass.MinHals < Cs2Mindestkante)
                        halsVorTrennung++;
            }

            var arealPunkte = bau.ArealLokal.Punkte.ToArray();
            var quellkanten = StudienQuellkanten(bau);
            winkelkanten.AddRange(quellkanten);
            var kantenindex = BaueDiagnoseKantenindex(bau.Zellen);
            var endrand = new HashSet<KantenSchluessel>(bau.Flaechen
                .SelectMany(flaeche => flaeche.AlleRinge)
                .SelectMany(ring => ring.Rohkanten)
                .Select(kante => kante.Schluessel));
            var formIstSauber = true;
            var formProxySauber = true;

            foreach (var flaeche in bau.Flaechen)
            {
                var ring = flaeche.Aussenring;
                var punkte = StudienRingpunkte(ring);
                var mass = Vermessen(punkte);
                var istKurz = mass.MinKante < Cs2Mindestkante;
                var istNurHals = !istKurz && mass.MinHals < Cs2Mindestkante;
                var istVerworfen = istKurz || istNurHals;
                if (istVerworfen) formIstSauber = false;

                var reflexe = StudienReflexe(punkte);
                var reflexEimer = reflexe == 0 ? "0 (konvex)"
                    : reflexe <= 4 ? "1-4"
                    : reflexe <= 9 ? "5-9" : "10 und mehr";
                StudienKonkavitaet konkavitaet;
                if (!konkav.TryGetValue(reflexEimer, out konkavitaet))
                {
                    konkavitaet = new StudienKonkavitaet();
                    konkav.Add(reflexEimer, konkavitaet);
                }
                konkavitaet.Gesamt++;
                if (istVerworfen)
                {
                    konkavitaet.Verworfen++;
                    konkavitaet.GroessteVerworfeneFlaeche = Math.Max(
                        konkavitaet.GroessteVerworfeneFlaeche, mass.Flaeche);
                }
                else
                {
                    konkavitaet.GroessteSaubereFlaeche = Math.Max(
                        konkavitaet.GroessteSaubereFlaeche, mass.Flaeche);
                }

                var kurzDurchNurNaht = false;
                if (istKurz)
                {
                    kurzeRinge++;
                    if (flaeche.Material == Material.Asphalt) kurzeAsphalt++;
                    else kurzeGruen++;
                    var kurzindices = Enumerable.Range(0, ring.Kanten.Count)
                        .Where(i => StudienKantenlaenge(ring.Kanten[i])
                            < Cs2Mindestkante)
                        .ToArray();
                    var ringbesitz = new HashSet<string>();
                    var alleKurzenNurNaht = kurzindices.Length != 0;
                    foreach (var i in kurzindices)
                    {
                        var kante = ring.Kanten[i];
                        kurzeKanten++;
                        if (kante.Linie.Art == Linienart.Aussenkante)
                            kurzeKantenAussen++;
                        if (kante.Linie.Art == Linienart.Teilungsnaht)
                            kurzeKantenTeilungsnaht++;
                        var besitz = StudienBesitz(
                            ring, kante, kantenindex, endrand);
                        ringbesitz.UnionWith(besitz);
                        alleKurzenNurNaht &= StudienNurNaht(besitz);

                        var quellindex = StudienNaechsteQuellkante(
                            (kante.Von.Punkt + kante.Nach.Punkt) * 0.5,
                            arealPunkte);
                        if (quellindex >= 0)
                        {
                            var quelle = quellkanten[quellindex];
                            quelle.KurzeRingkanten++;
                            if (StudienWinkeldifferenz(
                                    StudienKantenwinkel(kante), quelle.Winkel)
                                <= 0.1)
                                kurzeKantenParallel++;
                        }
                    }
                    var besitzname = StudienBesitzname(ringbesitz);
                    Zaehle(nachKurzbesitz, besitzname);
                    if (besitzname == "nur Materialgrenze")
                    {
                        kurzeNurMaterial++;
                        groessterKurzerMaterial = Math.Max(
                            groessterKurzerMaterial, mass.Flaeche);
                    }
                    else if (besitzname == "nur Lochtrennnaht")
                    {
                        kurzeNurNaht++;
                        groessterKurzerNurNaht = Math.Max(
                            groessterKurzerNurNaht, mass.Flaeche);
                    }
                    else if (besitzname == "Materialgrenze und Lochtrennnaht")
                    {
                        kurzeGemischt++;
                        groessterKurzerMaterial = Math.Max(
                            groessterKurzerMaterial, mass.Flaeche);
                    }
                    else kurzeAndere++;

                    var kuerzeste = kurzindices.OrderBy(i =>
                        StudienKantenlaenge(ring.Kanten[i])).First();
                    var mitte = (ring.Kanten[kuerzeste].Von.Punkt
                        + ring.Kanten[kuerzeste].Nach.Punkt) * 0.5;
                    var naechste = StudienNaechsteQuellkante(
                        mitte, arealPunkte);
                    if (naechste >= 0) quellkanten[naechste].KurzeRinge++;

                    kurzDurchNurNaht = flaeche.Material == Material.Asphalt
                        && alleKurzenNurNaht;
                    if (!kurzDurchNurNaht) formProxySauber = false;
                }

                if (istNurHals)
                {
                    halsRinge++;
                    groessterHals = Math.Max(groessterHals, mass.Flaeche);
                    engsteHalsweite = Math.Min(engsteHalsweite, mass.MinHals);
                    if (flaeche.Material == Material.Asphalt) halsAsphalt++;
                    else halsGruen++;
                    var engstelle = StudienEngsteStelle(punkte);
                    var relevante = new[]
                    {
                        Geometrie.Mod(engstelle.Punkt - 1, ring.Kanten.Count),
                        engstelle.Punkt,
                        engstelle.Segment,
                    }.Distinct().ToArray();
                    var besitz = new HashSet<string>();
                    foreach (var index in relevante)
                        besitz.UnionWith(StudienBesitz(
                            ring, ring.Kanten[index], kantenindex, endrand));
                    var mitNaht = besitz.Any(StudienIstNaht);
                    if (mitNaht) halsMitNaht++;
                    else halsOhneNaht++;
                    Zaehle(nachHalslinien, string.Join(" + ", relevante
                        .Select(index => ring.Kanten[index].Linie.Art.ToString())
                        .OrderBy(wert => wert)));
                    var durchNaht = flaeche.Material == Material.Asphalt && mitNaht;
                    if (!durchNaht) formProxySauber = false;
                }

                if (mass.MinWinkel < SpitzGrad)
                {
                    spitzeRinge++;
                    if (istVerworfen) spitzUndVerworfen++;
                    var ecke = StudienSpitzesteEcke(punkte);
                    var vorher = Geometrie.Mod(ecke - 1, ring.Kanten.Count);
                    var nachher = ecke;
                    var besitz = new HashSet<string>();
                    besitz.UnionWith(StudienBesitz(
                        ring, ring.Kanten[vorher], kantenindex, endrand));
                    besitz.UnionWith(StudienBesitz(
                        ring, ring.Kanten[nachher], kantenindex, endrand));
                    if (besitz.Any(StudienIstNaht)) spitzMitNaht++;
                    else spitzOhneNaht++;
                    if (ring.Kanten[vorher].Linie.Art == Linienart.Aussenkante
                        && ring.Kanten[nachher].Linie.Art == Linienart.Aussenkante)
                        spitzAussenknoten++;
                    Zaehle(nachSpitzlinien,
                        $"{ring.Kanten[vorher].Linie.Art} -> "
                            + ring.Kanten[nachher].Linie.Art);
                }

                for (var i = 0; i < ring.Kanten.Count; i++)
                {
                    var kante = ring.Kanten[i];
                    var naechste = StudienNaechsteQuellkante(
                        (kante.Von.Punkt + kante.Nach.Punkt) * 0.5,
                        arealPunkte);
                    if (naechste < 0) continue;
                    quellkanten[naechste].Ringkanten++;
                }
            }

            if (formIstSauber) aktuelleSaubereFormen++;
            if (formProxySauber) proxySaubereFormen++;
        }

        Console.WriteLine($"FLAECHENSTUDIE ueber {formen.Count} Formen");
        Console.WriteLine($"  gebaut {gebaut}, abgebrochen {abgebrochen}");
        Console.WriteLine($"  aktuell sauber {aktuelleSaubereFormen}/{gebaut}");
        Console.WriteLine();
        Console.WriteLine($"KURZE RINGE: {kurzeRinge}, davon Asphalt "
            + $"{kurzeAsphalt}, Gruen {kurzeGruen}");
        Console.WriteLine($"  nur Materialgrenze {kurzeNurMaterial}, nur "
            + $"Lochtrennnaht {kurzeNurNaht}, beides {kurzeGemischt}, "
            + $"anderes {kurzeAndere}");
        DruckeZaehler("  Besitz aller kurzen Ringkanten", nachKurzbesitz);
        Console.WriteLine($"  kurze fertige Kanten {kurzeKanten}; auf echter "
            + $"Aussenkante {kurzeKantenAussen}, auf Konvex-Teilungsnaht "
            + $"{kurzeKantenTeilungsnaht}");
        Console.WriteLine($"  davon parallel (<=0,1 Grad) zur naechsten "
            + $"Polygonkante {kurzeKantenParallel}");
        Console.WriteLine($"  groesster Ring nur Lochtrennnaht "
            + $"{groessterKurzerNurNaht:F2} m2; mit Materialgrenze "
            + $"{groessterKurzerMaterial:F2} m2");
        Console.WriteLine();
        Console.WriteLine($"EINSCHNUERUNGEN ohne Kurzkante: nach Lochtrennung "
            + $"{halsRinge}, davor {halsVorTrennung}");
        Console.WriteLine($"  Asphalt {halsAsphalt}, Gruen {halsGruen}; "
            + $"lokal mit Lochtrennnaht {halsMitNaht}, ohne "
            + $"Lochtrennnaht {halsOhneNaht}");
        Console.WriteLine($"  groesste {groessterHals:F2} m2; engster Hals "
            + $"{engsteHalsweite:R} m");
        DruckeZaehler("  haeufigste Linien an der Engstelle", nachHalslinien, 15);
        Console.WriteLine();
        Console.WriteLine($"SPITZE RINGE unter {SpitzGrad:F0} Grad: "
            + $"nach Lochtrennung {spitzeRinge}, davor {spitzeVorTrennung}; "
            + $"davon danach zugleich verworfen {spitzUndVerworfen}");
        Console.WriteLine($"  lokal mit Lochtrennnaht {spitzMitNaht}, ohne "
            + $"Lochtrennnaht {spitzOhneNaht}; echte Polygon-Ecke "
            + $"{spitzAussenknoten}");
        DruckeZaehler("  haeufigste Linien an der Spitze", nachSpitzlinien, 15);
        Console.WriteLine();

        Console.WriteLine("WINKEL zur Reihenrichtung, bezogen auf die jeweils "
            + "naechste Polygonkante:");
        StudienDruckeWinkel(winkelkanten, false);
        Console.WriteLine("  Abstand zum naechsten Rasterwinkel (0/90 Grad):");
        StudienDruckeWinkel(winkelkanten, true);
        Console.WriteLine();
        Console.WriteLine("KONKAVITAET der fertigen Ringe:");
        foreach (var paar in konkav.OrderBy(paar => paar.Key))
            Console.WriteLine($"  Reflexe {paar.Key,-11}: {paar.Value.Verworfen,4}/"
                + $"{paar.Value.Gesamt,4} verworfen; groesste saubere Flaeche "
                + $"{paar.Value.GroessteSaubereFlaeche:F2} m2, groesste "
                + $"verworfene {paar.Value.GroessteVerworfeneFlaeche:F2} m2");
        Console.WriteLine();
        Console.WriteLine("NUTZERVORSCHLAG, optimistische Naht-Projektion:");
        Console.WriteLine($"  {kurzeNurNaht} der {kurzeRinge} kurzen Ringe "
            + "haben lokal ausschliesslich eine Lochtrennnaht.");
        Console.WriteLine($"  Wenn ein konstruiertes Asphaltmodell alle heutigen "
            + "Lochtrennnaehte beseitigt und keine neue Haarstelle erzeugt: "
            + $"{proxySaubereFormen}/{gebaut} Formen sauber.");
        Console.WriteLine("  Das ist eine optimistische Projektion, kein Lauf "
            + "eines noch nicht definierten Flaechenkonstrukteurs.");
        return 0;
    }

    private static List<StudienWinkelkante> StudienQuellkanten(Bauergebnis bau)
    {
        var punkte = bau.ArealLokal.Punkte.ToArray();
        var raus = new List<StudienWinkelkante>();
        for (var i = 0; i < punkte.Length; i++)
        {
            var d = punkte[(i + 1) % punkte.Length] - punkte[i];
            var winkel = StudienRichtungswinkel(d);
            raus.Add(new StudienWinkelkante
            {
                Winkel = winkel,
                Rasterabweichung = Math.Min(winkel, 90 - winkel),
                Laenge = Geometrie.Laenge(d),
            });
        }
        return raus;
    }

    private static float2[] StudienRingpunkte(Ring ring) => ring.Kanten
        .Select(kante => new float2(
            (float)kante.Von.Punkt.X, (float)kante.Von.Punkt.Y))
        .ToArray();

    private static double StudienKantenlaenge(GerichteteKante kante) =>
        Geometrie.Laenge(kante.Nach.Punkt - kante.Von.Punkt);

    private static HashSet<string> StudienBesitz(
        Ring ring,
        GerichteteKante kante,
        IReadOnlyDictionary<KantenSchluessel, List<KantenfundDiagnose>> kantenindex,
        ISet<KantenSchluessel> endrand)
    {
        var raus = new HashSet<string>();
        foreach (var roh in ExakteRohkanten(ring, kante))
        {
            List<KantenfundDiagnose> funde;
            raus.Add(kantenindex.TryGetValue(roh.Schluessel, out funde)
                ? Besitzart(funde, endrand.Contains(roh.Schluessel))
                : "nicht zugeordnet");
        }
        if (raus.Count == 0) raus.Add("nicht zugeordnet");
        return raus;
    }

    private static bool StudienIstNaht(string wert) =>
        wert.StartsWith("Lochtrennnaht", StringComparison.Ordinal);

    private static bool StudienNurNaht(IEnumerable<string> besitz)
    {
        var werte = besitz.ToArray();
        return werte.Length != 0 && werte.All(StudienIstNaht);
    }

    private static string StudienBesitzname(ISet<string> besitz)
    {
        var material = besitz.Contains("Materialgrenze");
        var naht = besitz.Any(StudienIstNaht);
        var anderes = besitz.Any(wert => wert != "Materialgrenze"
            && !StudienIstNaht(wert));
        if (anderes) return "anderer Besitz: "
            + string.Join(" + ", besitz.OrderBy(wert => wert));
        if (material && naht) return "Materialgrenze und Lochtrennnaht";
        if (material) return "nur Materialgrenze";
        if (naht) return "nur Lochtrennnaht";
        return "ohne Besitz";
    }

    private static StudienEngstelle StudienEngsteStelle(float2[] ring)
    {
        var raus = new StudienEngstelle();
        for (var p = 0; p < ring.Length; p++)
        for (var k = 0; k < ring.Length; k++)
        {
            if (k == p || (k + 1) % ring.Length == p) continue;
            var abstand = AbstandPunktStrecke(
                ring[p], ring[k], ring[(k + 1) % ring.Length]);
            if (abstand >= raus.Abstand) continue;
            raus.Abstand = abstand;
            raus.Punkt = p;
            raus.Segment = k;
        }
        return raus;
    }

    private static int StudienSpitzesteEcke(float2[] ring)
    {
        var bester = 0;
        var winkel = double.PositiveInfinity;
        for (var b = 0; b < ring.Length; b++)
        {
            var a = ring[Geometrie.Mod(b - 1, ring.Length)];
            var c = ring[(b + 1) % ring.Length];
            var ba = math.normalizesafe(a - ring[b]);
            var bc = math.normalizesafe(c - ring[b]);
            var grad = math.degrees(math.acos(
                math.clamp(math.dot(ba, bc), -1f, 1f)));
            if (double.IsNaN(grad) || grad >= winkel) continue;
            winkel = grad;
            bester = b;
        }
        return bester;
    }

    private static int StudienReflexe(float2[] ring)
    {
        var anzahl = 0;
        for (var b = 0; b < ring.Length; b++)
        {
            var a = ring[Geometrie.Mod(b - 1, ring.Length)];
            var c = ring[(b + 1) % ring.Length];
            var ab = ring[b] - a;
            var bc = c - ring[b];
            if ((double)ab.x * bc.y - (double)ab.y * bc.x < 0) anzahl++;
        }
        return anzahl;
    }

    private static int StudienNaechsteQuellkante(
        Punkt punkt, IReadOnlyList<Punkt> ring)
    {
        var bester = -1;
        var abstand = double.PositiveInfinity;
        for (var i = 0; i < ring.Count; i++)
        {
            var wert = Geometrie.AbstandPunktStrecke(
                punkt, ring[i], ring[(i + 1) % ring.Count]);
            if (wert >= abstand) continue;
            abstand = wert;
            bester = i;
        }
        return bester;
    }

    private static double StudienKantenwinkel(GerichteteKante kante) =>
        StudienRichtungswinkel(kante.Nach.Punkt - kante.Von.Punkt);

    private static double StudienRichtungswinkel(Punkt richtung)
    {
        var grad = Math.Atan2(richtung.Y, richtung.X) * 180 / Math.PI;
        grad = ((grad % 180) + 180) % 180;
        return grad > 90 ? 180 - grad : grad;
    }

    private static double StudienWinkeldifferenz(double a, double b)
    {
        var d = Math.Abs(a - b);
        return Math.Min(d, 180 - d);
    }

    private static void StudienDruckeWinkel(
        IReadOnlyList<StudienWinkelkante> kanten, bool rasterabweichung)
    {
        var grenzen = rasterabweichung
            ? new[] { 0d, 2, 5, 15, 30, 45.000001 }
            : new[] { 0d, 15, 30, 45, 60, 75, 90.000001 };
        for (var i = 0; i + 1 < grenzen.Length; i++)
        {
            var min = grenzen[i];
            var max = grenzen[i + 1];
            var eimer = kanten.Where(kante =>
            {
                var wert = rasterabweichung
                    ? kante.Rasterabweichung : kante.Winkel;
                return wert >= min && wert < max;
            }).ToArray();
            var laenge = eimer.Sum(kante => kante.Laenge);
            var kurzringe = eimer.Sum(kante => kante.KurzeRinge);
            var ringkanten = eimer.Sum(kante => kante.Ringkanten);
            var kurzeRingkanten = eimer.Sum(kante => kante.KurzeRingkanten);
            Console.WriteLine($"    {min,5:F0} bis {Math.Min(max, 90),5:F0} Grad: "
                + $"{eimer.Length,4} Polygonkanten / {laenge,9:F1} m; "
                + $"{kurzringe,3} kurze Ringe ({1000 * kurzringe / Math.Max(1, laenge),5:F2}/km); "
                + $"{kurzeRingkanten,3}/{ringkanten,6} fertige Ringkanten kurz");
        }
    }
}
