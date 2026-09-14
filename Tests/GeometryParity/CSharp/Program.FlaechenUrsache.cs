using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using ParkingLotTool.Geometry.Zellen;
using Unity.Mathematics;

/**
 * Rueckverfolgung der von `--flaechen` gemeldeten kurzen Ringkanten.
 *
 * Das Messgeraet baut denselben Zellenlauf ein zweites Mal mit den internen
 * Modelltypen. Dadurch bleiben Linienidentitaet, Zellbesitz und der Stand vor
 * der Lochtrennung sichtbar. Der oeffentliche Lauf wird daneben gerechnet;
 * Ring- und Fehlerzahlen muessen uebereinstimmen, sonst waere die Diagnose
 * nicht mit der vom Mod ausgegebenen Geometrie vergleichbar.
 */
internal static partial class Program
{
    private sealed class KantenfundDiagnose
    {
        internal Zelle Zelle;
        internal GerichteteKante Kante;
    }

    private static int RunFlaechenUrsache(int grenze)
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
        var publicRinge = 0;
        var internRinge = 0;
        var publicKurz = 0;
        var internKurz = 0;
        var kurzVorTrennung = 0;
        var kurzNachTrennung = 0;
        var ringeVorTrennung = 0;
        var ringeNachTrennung = 0;
        var kurzeEndkanten = 0;
        var kurzAmAussenrand = 0;
        var kurzAnTeilungsnaht = 0;
        var kurzOhneKonvexrand = 0;
        var kurzeRohkanten = 0;
        var kurzeRohkantenImEndrand = 0;
        var kurzeRohkantenEntfernt = 0;
        var zellenMitKurzkanteVorZufahrt = 0;
        var zellenMitKurzkanteNachZufahrt = 0;
        var randFahrwegKontakte = 0;
        var flaechenVorTrennung = 0;
        var flaechenNachTrennung = 0;
        var loecherVorTrennung = 0;
        var zellenVorZufahrtGesamt = 0;
        var zellenNachZufahrtGesamt = 0;
        var formenOhneKurzkanteVorher = 0;
        var formenOhneKurzkanteNachher = 0;
        var formenCs2SauberVorher = 0;
        var formenCs2SauberNachher = 0;
        var ringeNurMaterialgrenze = 0;
        var ringeNurLochtrennnaht = 0;
        var ringeMitBeidem = 0;
        var ringeMitAnderemBesitz = 0;

        var nachMaterial = new Dictionary<string, int>();
        var nachTraeger = new Dictionary<string, int>();
        var nachErzeugungsstufe = new Dictionary<string, int>();
        var nachLinientripel = new Dictionary<string, int>();
        var nachBesitz = new Dictionary<string, int>();
        var roheNachBesitz = new Dictionary<string, int>();
        var zellenNachArt = new Dictionary<string, int>();
        var beispiele = new List<string>();

        foreach (var form in formen)
        {
            ParkingLayout oeffentlich;
            Bauergebnis bau;
            try
            {
                oeffentlich = ParkingGeometry.Build(form.Site, settings);
                bau = BaueFlaechenDiagnose(form.Site, settings);
            }
            catch
            {
                abgebrochen++;
                continue;
            }
            gebaut++;

            var oeffentlicheRinge = oeffentlich.GrassSurface
                .Concat(oeffentlich.AsphaltSurface).ToArray();
            publicRinge += oeffentlicheRinge.Length;
            publicKurz += oeffentlicheRinge.Count(ring =>
                Vermessen(ring)?.MinKante < Cs2Mindestkante);

            internRinge += bau.Flaechen.Count;
            internKurz += bau.Flaechen.Count(flaeche =>
                RingMindestkante(flaeche.Aussenring) < Cs2Mindestkante);
            ringeVorTrennung += bau.FlaechenVorTrennung.Sum(flaeche =>
                1 + flaeche.Loecher.Count);
            ringeNachTrennung += bau.Flaechen.Sum(flaeche =>
                1 + flaeche.Loecher.Count);
            kurzVorTrennung += bau.FlaechenVorTrennung.Sum(flaeche =>
                flaeche.AlleRinge.Count(ring =>
                    RingMindestkante(ring) < Cs2Mindestkante));
            kurzNachTrennung += bau.Flaechen.Sum(flaeche =>
                flaeche.AlleRinge.Count(ring =>
                    RingMindestkante(ring) < Cs2Mindestkante));
            flaechenVorTrennung += bau.FlaechenVorTrennung.Count;
            flaechenNachTrennung += bau.Flaechen.Count;
            loecherVorTrennung += bau.FlaechenVorTrennung.Sum(
                flaeche => flaeche.Loecher.Count);

            var vorherMasse = bau.FlaechenVorTrennung
                .SelectMany(flaeche => flaeche.AlleRinge)
                .Select(RingmassDiagnose).ToArray();
            var nachherMasse = bau.Flaechen
                .SelectMany(flaeche => flaeche.AlleRinge)
                .Select(RingmassDiagnose).ToArray();
            if (vorherMasse.All(mass => mass.MinKante >= Cs2Mindestkante))
                formenOhneKurzkanteVorher++;
            if (nachherMasse.All(mass => mass.MinKante >= Cs2Mindestkante))
                formenOhneKurzkanteNachher++;
            if (vorherMasse.All(mass => mass.MinKante >= Cs2Mindestkante
                    && mass.MinHals >= Cs2Mindestkante))
                formenCs2SauberVorher++;
            if (nachherMasse.All(mass => mass.MinKante >= Cs2Mindestkante
                    && mass.MinHals >= Cs2Mindestkante))
                formenCs2SauberNachher++;

            zellenVorZufahrtGesamt += bau.ZellenVorZufahrt.Count;
            zellenNachZufahrtGesamt += bau.Zellen.Count;
            zellenMitKurzkanteVorZufahrt += bau.ZellenVorZufahrt.Count(
                zelle => ZellmindestkanteDiagnose(zelle) < Cs2Mindestkante);
            foreach (var zelle in bau.Zellen.Where(zelle =>
                         ZellmindestkanteDiagnose(zelle) < Cs2Mindestkante))
            {
                zellenMitKurzkanteNachZufahrt++;
                Zaehle(zellenNachArt, zelle.Art.ToString());
            }

            var kantenindex = BaueDiagnoseKantenindex(bau.Zellen);
            var endrand = new HashSet<KantenSchluessel>(bau.Flaechen
                .SelectMany(flaeche => flaeche.AlleRinge)
                .SelectMany(ring => ring.Rohkanten)
                .Select(kante => kante.Schluessel));

            foreach (var paar in kantenindex)
            {
                var laenge = Geometrie.Laenge(
                    paar.Value[0].Kante.Nach.Punkt
                    - paar.Value[0].Kante.Von.Punkt);
                if (laenge >= Cs2Mindestkante) continue;
                kurzeRohkanten++;
                var besitz = Besitzart(paar.Value, endrand.Contains(paar.Key));
                Zaehle(roheNachBesitz, besitz);
                if (endrand.Contains(paar.Key)) kurzeRohkantenImEndrand++;
                else kurzeRohkantenEntfernt++;
            }

            foreach (var paar in kantenindex.Values)
            {
                if (paar.Count != 2) continue;
                var arten = new[] { paar[0].Zelle.Art, paar[1].Zelle.Art };
                if (arten.Contains(Zellart.Randstrasse)
                    && arten.Any(art => art == Zellart.Fahrgasse
                        || art == Zellart.Querstrasse
                        || art == Zellart.Zufahrt)
                    && paar[0].Zelle.Material == paar[1].Zelle.Material)
                    randFahrwegKontakte++;
            }

            foreach (var flaeche in bau.Flaechen)
            {
                var ring = flaeche.Aussenring;
                var ringbesitz = new HashSet<string>();
                for (var i = 0; i < ring.Kanten.Count; i++)
                {
                    var kurz = ring.Kanten[i];
                    var laenge = Geometrie.Laenge(
                        kurz.Nach.Punkt - kurz.Von.Punkt);
                    if (laenge >= Cs2Mindestkante) continue;
                    kurzeEndkanten++;
                    Zaehle(nachMaterial, flaeche.Material.ToString());
                    Zaehle(nachTraeger, kurz.Linie.Art.ToString());

                    var vorher = ring.Kanten[Geometrie.Mod(
                        i - 1, ring.Kanten.Count)].Linie;
                    var nachher = ring.Kanten[Geometrie.Mod(
                        i + 1, ring.Kanten.Count)].Linie;
                    var stufe = new[] { vorher, kurz.Linie, nachher }
                        .OrderByDescending(linie => Diagnosestufe(linie.Art))
                        .First();
                    Zaehle(nachErzeugungsstufe,
                        Diagnosestufenname(Diagnosestufe(stufe.Art)));
                    Zaehle(nachLinientripel,
                        $"{vorher.Art} -> {kurz.Linie.Art} -> {nachher.Art}");

                    var arten = new[] { vorher.Art, kurz.Linie.Art, nachher.Art };
                    if (arten.Contains(Linienart.Aussenkante)) kurzAmAussenrand++;
                    else if (arten.Contains(Linienart.Teilungsnaht))
                        kurzAnTeilungsnaht++;
                    else kurzOhneKonvexrand++;

                    var rohe = ring.Rohkanten.Where(kante =>
                        kante.Linie.Id == kurz.Linie.Id
                        && PunktAufDiagnosesegment(
                            kante.Von.Punkt, kurz.Von.Punkt, kurz.Nach.Punkt)
                        && PunktAufDiagnosesegment(
                            kante.Nach.Punkt, kurz.Von.Punkt, kurz.Nach.Punkt))
                        .ToArray();
                    var besitzarten = rohe.Select(kante =>
                    {
                        List<KantenfundDiagnose> funde;
                        return kantenindex.TryGetValue(kante.Schluessel, out funde)
                            ? Besitzart(funde, true)
                            : "nicht zugeordnet";
                    }).Distinct().OrderBy(wert => wert).ToArray();
                    var besitz = string.Join(" + ", besitzarten);
                    Zaehle(nachBesitz, besitz);
                    ringbesitz.UnionWith(besitzarten);

                    if (beispiele.Count < 20)
                        beispiele.Add($"{form.Name}: {flaeche.Material}, "
                            + $"{laenge:R} m, {vorher.Name} -> "
                            + $"{kurz.Linie.Name} -> {nachher.Name}, {besitz}");
                }
                if (ringbesitz.Count == 0) continue;
                var material = ringbesitz.Contains("Materialgrenze");
                var naht = ringbesitz.Contains("Lochtrennnaht (gleiches Material)");
                var anderes = ringbesitz.Any(wert => wert != "Materialgrenze"
                    && wert != "Lochtrennnaht (gleiches Material)");
                if (anderes) ringeMitAnderemBesitz++;
                else if (material && naht) ringeMitBeidem++;
                else if (material) ringeNurMaterialgrenze++;
                else if (naht) ringeNurLochtrennnaht++;
            }
        }

        Console.WriteLine($"FLAECHENURSACHE ueber {formen.Count} Formen");
        Console.WriteLine($"  gebaut {gebaut}, abgebrochen {abgebrochen}");
        Console.WriteLine($"  Aequivalenz: oeffentlich {publicRinge} Ringe / "
            + $"{publicKurz} mit kurzer Kante; intern {internRinge} / {internKurz}");
        Console.WriteLine($"  vor Lochtrennung {ringeVorTrennung} Ringe, "
            + $"{kurzVorTrennung} kurz; danach {ringeNachTrennung}, "
            + $"{kurzNachTrennung} kurz");
        Console.WriteLine($"  Flaechen vor/nach Lochtrennung "
            + $"{flaechenVorTrennung}/{flaechenNachTrennung}; "
            + $"Loecher vorher {loecherVorTrennung}");
        Console.WriteLine($"  Formen ohne Kurzkante vor/nach Lochtrennung "
            + $"{formenOhneKurzkanteVorher}/{formenOhneKurzkanteNachher}; "
            + $"ohne Kurzkante oder Hals {formenCs2SauberVorher}/"
            + $"{formenCs2SauberNachher}");
        Console.WriteLine($"  Zellen mit kurzer Kante vor/nach Zufahrt "
            + $"{zellenMitKurzkanteVorZufahrt}/{zellenMitKurzkanteNachZufahrt} "
            + $"von {zellenVorZufahrtGesamt}/{zellenNachZufahrtGesamt}");
        DruckeZaehler("  nach Zellart", zellenNachArt);
        Console.WriteLine($"  eindeutige rohe Zellkanten unter 0,375 m: "
            + $"{kurzeRohkanten}; im Endrand {kurzeRohkantenImEndrand}, "
            + $"durch Vereinigung entfernt {kurzeRohkantenEntfernt}");
        DruckeZaehler("  rohe Kurzkanten nach Besitz", roheNachBesitz);
        Console.WriteLine($"  fertige Ringkanten unter 0,375 m: {kurzeEndkanten}");
        DruckeZaehler("  nach Material", nachMaterial);
        DruckeZaehler("  nach Besitz", nachBesitz);
        Console.WriteLine($"  kurze Ringe nach Besitz: nur Materialgrenze "
            + $"{ringeNurMaterialgrenze}, nur Lochtrennnaht "
            + $"{ringeNurLochtrennnaht}, beides {ringeMitBeidem}, "
            + $"anderes {ringeMitAnderemBesitz}");
        DruckeZaehler("  nach traegender Linie", nachTraeger);
        DruckeZaehler("  nach letzter Schnittstufe", nachErzeugungsstufe);
        Console.WriteLine($"  mit Aussenkante {kurzAmAussenrand}, mit "
            + $"Teilungsnaht {kurzAnTeilungsnaht}, ohne beide "
            + $"{kurzOhneKonvexrand}");
        Console.WriteLine($"  bereits gleichmaterialig vereinigte Kontakte "
            + $"Randstrasse <-> Fahrgasse/Querstrasse/Zufahrt: "
            + $"{randFahrwegKontakte}");
        DruckeZaehler("  haeufigste Linientripel", nachLinientripel, 20);
        Console.WriteLine("  erste 20 Beispiele:");
        foreach (var beispiel in beispiele) Console.WriteLine("    " + beispiel);
        return 0;
    }

    /** Exakte Kopie der Eingangsnormalisierung von BuildZellen, nur lesend. */
    private static Bauergebnis BaueFlaechenDiagnose(
        float2[] site, LayoutSettings settings)
    {
        const double gitter = 0.001;
        var original = site.Select(punkt => new Punkt(
            Math.Round(punkt.x / gitter) * gitter,
            Math.Round(punkt.y / gitter) * gitter)).ToList();
        if (original.Count > 3
            && original[0].X == original[original.Count - 1].X
            && original[0].Y == original[original.Count - 1].Y)
            original.RemoveAt(original.Count - 1);
        var normalisiert = original.ToList();
        var umgedreht = Geometrie.Vorzeichenflaeche(normalisiert) < 0;
        if (umgedreht) normalisiert.Reverse();

        var zufahrten = new List<Zufahrtsvorgabe>();
        foreach (var entrance in settings.Entrances ?? Array.Empty<Entrance>())
        {
            if (!umgedreht)
            {
                zufahrten.Add(new Zufahrtsvorgabe(entrance.Edge, entrance.Along));
                continue;
            }
            var a = original[entrance.Edge];
            var b = original[(entrance.Edge + 1) % original.Count];
            zufahrten.Add(new Zufahrtsvorgabe(
                Geometrie.Mod(original.Count - 2 - entrance.Edge, original.Count),
                Geometrie.Laenge(b - a) - entrance.Along));
        }

        return Layoutbauer.Baue(
            new Formdefinition("Flaechenursache", normalisiert),
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
                Reihenwinkel = string.Equals(
                    settings.AngleMode, "edge", StringComparison.Ordinal)
                    ? (double?)null : settings.Angle,
            },
            zufahrten);
    }

    private static Dictionary<KantenSchluessel, List<KantenfundDiagnose>>
        BaueDiagnoseKantenindex(IReadOnlyList<Zelle> zellen)
    {
        var raus = new Dictionary<KantenSchluessel, List<KantenfundDiagnose>>();
        foreach (var zelle in zellen)
            for (var i = 0; i < zelle.Polygon.Anzahl; i++)
            {
                var kante = new GerichteteKante(
                    zelle.Polygon.Knoten(i), zelle.Polygon.Knoten(i + 1),
                    zelle.Polygon.Linie(i));
                List<KantenfundDiagnose> funde;
                if (!raus.TryGetValue(kante.Schluessel, out funde))
                {
                    funde = new List<KantenfundDiagnose>();
                    raus.Add(kante.Schluessel, funde);
                }
                funde.Add(new KantenfundDiagnose { Zelle = zelle, Kante = kante });
            }
        return raus;
    }

    private static string Besitzart(
        IReadOnlyList<KantenfundDiagnose> funde, bool imEndrand)
    {
        if (funde.Count == 1) return imEndrand ? "Aussenrand" : "offen entfernt";
        if (funde.Count != 2) return $"nichtmannigfaltig {funde.Count}";
        if (funde[0].Zelle.Material != funde[1].Zelle.Material)
            return imEndrand ? "Materialgrenze" : "Materialgrenze entfernt";
        if (!imEndrand) return "Innenkante gleiches Material";
        if (DiagnoseRollengruppe(funde[0].Zelle.Art)
                != DiagnoseRollengruppe(funde[1].Zelle.Art))
            return "Rollengrenze (gleiches Material)";
        if (funde[0].Zelle.Flaechenabschnitt
                != funde[1].Zelle.Flaechenabschnitt
            || funde.Any(fund =>
                fund.Kante.Linie.Art == Linienart.Randstrassenstoss
                || fund.Kante.Linie.Art == Linienart.Randbandstoss))
            return "Geplanter Flaechenstoss (gleiches Material)";
        return "Lochtrennnaht (gleiches Material)";
    }

    private static int DiagnoseRollengruppe(Zellart art)
    {
        switch (art)
        {
            case Zellart.Randstrasse: return 1;
            case Zellart.Fahrgasse: return 2;
            case Zellart.Querstrasse: return 3;
            case Zellart.Bucht: return 4;
            case Zellart.Zufahrt: return 5;
            default: return 0;
        }
    }

    private static double RingMindestkante(Ring ring) => ring.Kanten.Min(kante =>
        Geometrie.Laenge(kante.Nach.Punkt - kante.Von.Punkt));

    private static Ringmass RingmassDiagnose(Ring ring) => Vermessen(ring.Kanten
        .Select(kante => new float2(
            (float)kante.Von.Punkt.X, (float)kante.Von.Punkt.Y)).ToArray());

    private static double ZellmindestkanteDiagnose(Zelle zelle)
    {
        var minimum = double.PositiveInfinity;
        for (var i = 0; i < zelle.Polygon.Anzahl; i++)
            minimum = Math.Min(minimum, Geometrie.Laenge(
                zelle.Polygon.Knoten(i + 1).Punkt
                - zelle.Polygon.Knoten(i).Punkt));
        return minimum;
    }

    private static bool PunktAufDiagnosesegment(Punkt p, Punkt a, Punkt b)
    {
        if (Geometrie.Laenge(b - a) < 1e-12)
            return Geometrie.Laenge(p - a) < 1e-9;
        return Math.Abs(Geometrie.Kreuz(b - a, p - a))
                <= 1e-8 * Geometrie.Laenge(b - a)
            && Geometrie.Skalar(p - a, p - b) <= 1e-8;
    }

    private static int Diagnosestufe(Linienart art)
    {
        if (art == Linienart.Aussenkante || art == Linienart.Teilungsnaht) return 0;
        if (art == Linienart.Innenrand) return 1;
        if (art == Linienart.Randstrassenkante) return 2;
        if (art == Linienart.Randstrasseninnenkante) return 3;
        if (art == Linienart.Randstrassenstoss
            || art == Linienart.Randbandstoss) return 4;
        if (art == Linienart.Randbuchtgrenze) return 4;
        if (art == Linienart.BandY) return 5;
        if (art == Linienart.RasterX || art == Linienart.Querstrassenkante) return 6;
        if (art == Linienart.Zufahrtskante) return 7;
        return -1;
    }

    private static string Diagnosestufenname(int stufe)
    {
        switch (stufe)
        {
            case 0: return "Konvexteil/Aussenring";
            case 1: return "Innenrand-Schnitt";
            case 2: return "Randstrassen-Aussenkante";
            case 3: return "Randstrassen-Innenkante";
            case 4: return "Randbucht-Segmentschnitt";
            case 5: return "BandY-Schnitt";
            case 6: return "Modulspalten-/Querstrassen-Schnitt";
            case 7: return "Zufahrts-Segmentschnitt";
            default: return "unbekannt";
        }
    }

    private static void Zaehle(Dictionary<string, int> zaehler, string schluessel)
    {
        int vorher;
        zaehler[schluessel] = zaehler.TryGetValue(schluessel, out vorher)
            ? vorher + 1 : 1;
    }

    private static void DruckeZaehler(
        string titel, IReadOnlyDictionary<string, int> zaehler, int maximal = 50)
    {
        Console.WriteLine(titel + ":");
        foreach (var paar in zaehler.OrderByDescending(paar => paar.Value)
                     .ThenBy(paar => paar.Key).Take(maximal))
            Console.WriteLine($"    {paar.Value,6}  {paar.Key}");
    }
}
