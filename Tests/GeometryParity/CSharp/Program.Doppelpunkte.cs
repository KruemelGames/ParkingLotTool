using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ParkingLotTool.Geometry;
using ParkingLotTool.Geometry.Zellen;
using Unity.Mathematics;
using ZellMaterial = ParkingLotTool.Geometry.Zellen.Material;

/**
 * Misst die Fast-Doppelpunkte der von CS2 verworfenen Materialringe.
 *
 * Der oeffentliche Ring ist die tatsaechliche float-Ausgabe des Mods. Parallel
 * wird derselbe Bau ueber die internen Zellentypen gelesen. Beide Ringfolgen
 * muessen bis auf die zyklische Startposition bitgleich sein; erst dann werden
 * double-Abstand, Knotenidentitaet, Linien und Zellbesitz ausgewertet.
 */
internal static partial class Program
{
    private sealed class DoppelKnoteninfo
    {
        internal bool VorZufahrt;
        internal readonly HashSet<int> Zellen = new HashSet<int>();
        internal readonly HashSet<string> Zellarten = new HashSet<string>();
        internal readonly HashSet<string> Materialien = new HashSet<string>();
        internal readonly Dictionary<int, Linie> Linien = new Dictionary<int, Linie>();
    }

    private sealed class DoppelRingfund
    {
        internal string Form;
        internal string Material;
        internal int Ringindex;
        internal float2[] FloatRing;
        internal Punkt[] DoubleWelt;
        internal Ring Modellring;
        internal int Modellstart;
        internal int KuerzesteKante;
        internal double Floatlaenge;
        internal double Doublelaenge;
        internal ulong Floatschritte;
        internal double UlpMass;
        internal double Flaeche;
        internal bool EntferneVorpunktAngenommen;
        internal bool EntferneNachpunktAngenommen;
        internal bool EntferneBeideAngenommen;
        internal int EntfernteUlpKappen;
        internal bool AlleUlpKappenEntferntAngenommen;
        internal bool DoubleVersatzSchneidetSich;
        internal double DoubleVersatzflaeche;
        internal double DoubleHals;
        internal string Besitz;
        internal bool RohkanteImGruen;
        internal bool EndkanteImGruen;
        internal string GrueneRohlinien;
        internal DoppelKnoteninfo VonInfo;
        internal DoppelKnoteninfo NachInfo;
    }

    private static int RunDoppelpunkte(int grenze, string detailfilter)
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

        var funde = new List<DoppelRingfund>();
        var gebaut = 0;
        var abgebrochen = 0;
        var ringeGesamt = 0;
        var formenSchlecht = new HashSet<string>();
        var formenMitUlpKante = new HashSet<string>();
        var formenNachEinfacherGegenprobeSchlecht = new HashSet<string>();

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
            var knoteninfos = DoppelKnoteninfos(bau);
            var formHatSchlechtenRest = false;

            foreach (var materialpaar in new[]
                     {
                         (Name: "Gras", Material: ZellMaterial.Gruen,
                             Ringe: oeffentlich.GrassSurface),
                         (Name: "Belag", Material: ZellMaterial.Asphalt,
                             Ringe: oeffentlich.AsphaltSurface),
                     })
            {
                var modellflaechen = bau.Flaechen
                    .Where(flaeche => flaeche.Material == materialpaar.Material)
                    .ToArray();
                if (modellflaechen.Length != materialpaar.Ringe.Length)
                    throw new InvalidOperationException(
                        $"{form.Name}: Ringzahl public/intern weicht ab: "
                        + $"{materialpaar.Ringe.Length}/{modellflaechen.Length}.");

                for (var ringindex = 0; ringindex < materialpaar.Ringe.Length;
                     ringindex++)
                {
                    var floatring = materialpaar.Ringe[ringindex];
                    ringeGesamt++;
                    if (Cs2Triangulierung.Dreiecke(floatring) != 0) continue;

                    formenSchlecht.Add(form.Name);
                    var modellring = modellflaechen[ringindex].Aussenring;
                    var zuordnung = DoppelOrdneZu(floatring, modellring, bau.Rahmen);
                    var doublewelt = Enumerable.Range(0, floatring.Length)
                        .Select(index => bau.Rahmen.NachWelt(modellring.Kanten[
                            (zuordnung + index) % modellring.Kanten.Count].Von.Punkt))
                        .ToArray();
                    var kurz = DoppelKuerzesteKante(floatring);
                    var modellindex = (zuordnung + kurz.Index) % modellring.Kanten.Count;
                    var von = modellring.Kanten[modellindex].Von;
                    var nach = modellring.Kanten[modellindex].Nach;
                    var schritte = Math.Max(
                        DoppelFloatschritte(floatring[kurz.Index].x,
                            floatring[(kurz.Index + 1) % floatring.Length].x),
                        DoppelFloatschritte(floatring[kurz.Index].y,
                            floatring[(kurz.Index + 1) % floatring.Length].y));
                    var ulp = Math.Max(
                        DoppelUlpMass(floatring[kurz.Index]),
                        DoppelUlpMass(floatring[(kurz.Index + 1) % floatring.Length]));
                    var istUlp = schritte <= 8 && kurz.Laenge <= 8 * ulp;
                    if (istUlp) formenMitUlpKante.Add(form.Name);

                    var ohneVor = DoppelEntferne(floatring, kurz.Index);
                    var ohneNach = DoppelEntferne(
                        floatring, (kurz.Index + 1) % floatring.Length);
                    var ohneBeide = DoppelEntferneZweiNachbarn(floatring, kurz.Index);
                    var vorOk = ohneVor.Length >= 3
                        && Cs2Triangulierung.Dreiecke(ohneVor) != 0;
                    var nachOk = ohneNach.Length >= 3
                        && Cs2Triangulierung.Dreiecke(ohneNach) != 0;
                    var beideOk = ohneBeide.Length >= 3
                        && Cs2Triangulierung.Dreiecke(ohneBeide) != 0;
                    var bereinigt = DoppelEntferneUlpKappen(floatring, 8);
                    if (!istUlp || !(vorOk || nachOk || beideOk))
                        formHatSchlechtenRest = true;

                    List<KantenfundDiagnose> besitzer;
                    var kantenindex = BaueDiagnoseKantenindex(bau.Zellen);
                    var besitz = kantenindex.TryGetValue(
                            KantenSchluessel.Von(von, nach), out besitzer)
                        ? string.Join(" + ", besitzer.Select(wert =>
                            $"{wert.Zelle.Art}/{wert.Zelle.Material}"
                                + $"#{wert.Zelle.Id}"))
                        : "vereinfachte Ringkante";
                    var gruenfund = DoppelGrueneGegenkante(
                        bau, KantenSchluessel.Von(von, nach));

                    var versatzmass = DoppelVersatzmassVon(doublewelt);
                    funde.Add(new DoppelRingfund
                    {
                        Form = form.Name,
                        Material = materialpaar.Name,
                        Ringindex = ringindex,
                        FloatRing = floatring,
                        DoubleWelt = doublewelt,
                        Modellring = modellring,
                        Modellstart = zuordnung,
                        KuerzesteKante = kurz.Index,
                        Floatlaenge = kurz.Laenge,
                        Doublelaenge = Geometrie.Laenge(
                            doublewelt[(kurz.Index + 1) % doublewelt.Length]
                                - doublewelt[kurz.Index]),
                        Floatschritte = schritte,
                        UlpMass = ulp,
                        Flaeche = Math.Abs(FlaechenmassRing(floatring)),
                        EntferneVorpunktAngenommen = vorOk,
                        EntferneNachpunktAngenommen = nachOk,
                        EntferneBeideAngenommen = beideOk,
                        EntfernteUlpKappen = bereinigt.EntfernteKappen,
                        AlleUlpKappenEntferntAngenommen =
                            bereinigt.Ring.Length >= 3
                            && Cs2Triangulierung.Dreiecke(bereinigt.Ring) != 0,
                        DoubleVersatzSchneidetSich = versatzmass.SchneidetSich,
                        DoubleVersatzflaeche = versatzmass.Vorzeichenflaeche,
                        DoubleHals = DoppelHals(doublewelt),
                        Besitz = besitz,
                        RohkanteImGruen = gruenfund.Roh,
                        EndkanteImGruen = gruenfund.Ende,
                        GrueneRohlinien = gruenfund.Linien,
                        VonInfo = knoteninfos[von.Id],
                        NachInfo = knoteninfos[nach.Id],
                    });
                }
            }
            if (formHatSchlechtenRest)
                formenNachEinfacherGegenprobeSchlecht.Add(form.Name);
        }

        Console.WriteLine($"DOPPELPUNKTE ueber {formen.Count} Versuche / "
            + $"{gebaut} gebaute Formen");
        Console.WriteLine($"  abgebrochen {abgebrochen}, Ringe gesamt {ringeGesamt}");
        Console.WriteLine($"  CS2 verwirft {funde.Count} Ringe in "
            + $"{formenSchlecht.Count} Formen, zusammen "
            + $"{funde.Sum(fund => fund.Flaeche):F6} m2");

        var ulpFunde = funde.Where(DoppelIstUlpFund).ToArray();
        Console.WriteLine($"  kuerzeste Kante <= 8 Float-Schritte und <= 8 ULP: "
            + $"{ulpFunde.Length} Ringe / {ulpFunde.Select(f => f.Form).Distinct().Count()} "
            + $"Formen / {ulpFunde.Sum(f => f.Flaeche):F6} m2");
        Console.WriteLine($"    davon nach Entfernen eines Endpunkts angenommen: "
            + $"{ulpFunde.Count(f => f.EntferneVorpunktAngenommen
                || f.EntferneNachpunktAngenommen)}");
        Console.WriteLine($"    davon nach Entfernen beider Kappenpunkte angenommen: "
            + $"{ulpFunde.Count(f => f.EntferneBeideAngenommen)}");
        Console.WriteLine($"  Formen mit weiterem Fehler nach einfacher Ring-Gegenprobe: "
            + $"{formenNachEinfacherGegenprobeSchlecht.Count}");
        Console.WriteLine();

        var ulpKappen = funde.Where(fund => fund.EntfernteUlpKappen != 0).ToArray();
        Console.WriteLine("  Vollstaendige Gegenprobe mit allen <= 8-ULP-Haarkappen:");
        Console.WriteLine($"    betroffen {ulpKappen.Length} Ringe / "
            + $"{ulpKappen.Select(fund => fund.Form).Distinct().Count()} Formen / "
            + $"{ulpKappen.Sum(fund => fund.Flaeche):F6} m2");
        Console.WriteLine($"    danach angenommen "
            + $"{ulpKappen.Count(fund => fund.AlleUlpKappenEntferntAngenommen)} "
            + $"Ringe / {ulpKappen.Where(fund =>
                fund.AlleUlpKappenEntferntAngenommen)
                .Select(fund => fund.Form).Distinct().Count()} Formen");
        var schmutzigNachUlp = funde.GroupBy(fund => fund.Form).Count(gruppe =>
            gruppe.Any(fund => !fund.AlleUlpKappenEntferntAngenommen));
        var saubereNachUlp = formenSchlecht.Except(funde.GroupBy(fund => fund.Form)
            .Where(gruppe => gruppe.Any(fund =>
                !fund.AlleUlpKappenEntferntAngenommen))
            .Select(gruppe => gruppe.Key)).OrderBy(wert => wert).ToArray();
        Console.WriteLine($"    von {formenSchlecht.Count} unsauberen Formen wuerden "
            + $"{formenSchlecht.Count - schmutzigNachUlp} sauber; "
            + $"{schmutzigNachUlp} blieben unsauber");
        Console.WriteLine($"    sauber wuerden: {string.Join(", ", saubereNachUlp)}");
        Console.WriteLine($"    entfernte Haarkappen: "
            + $"{ulpKappen.Sum(fund => fund.EntfernteUlpKappen)}");
        Console.WriteLine($"    kuerzeste Kante: in double exakt null "
            + $"{ulpKappen.Count(fund => fund.Doublelaenge == 0)}, nach float "
            + $"exakt null {ulpKappen.Count(fund => fund.Floatlaenge == 0)}");
        Console.WriteLine($"    dieselbe Kante im Gruen: roh "
            + $"{ulpKappen.Count(fund => fund.RohkanteImGruen)}, fertig "
            + $"{ulpKappen.Count(fund => fund.EndkanteImGruen)}");
        Console.WriteLine($"    beide Knoten schon vor der Zufahrt: "
            + $"{ulpKappen.Count(fund => fund.VonInfo.VorZufahrt
                && fund.NachInfo.VorZufahrt)}");
        Console.WriteLine("    Zellnutzer der beiden getrennten Knoten:");
        foreach (var gruppe in ulpKappen.GroupBy(fund =>
                     $"{fund.VonInfo.Zellen.Count}/{fund.NachInfo.Zellen.Count}")
                     .OrderByDescending(gruppe => gruppe.Count()))
            Console.WriteLine($"      {gruppe.Count(),2} Ringe: "
                + $"{gruppe.Key} Zellen");
        Console.WriteLine("    Linien der rohen gruenen Gegenkante:");
        foreach (var gruppe in ulpKappen.GroupBy(fund =>
                     (fund.GrueneRohlinien ?? "-").Split(';')[0])
                     .OrderByDescending(gruppe => gruppe.Count()))
            Console.WriteLine($"      {gruppe.Count(),2}  {gruppe.Key}");
        Console.WriteLine("    nach Form:");
        foreach (var gruppe in ulpKappen.GroupBy(fund => fund.Form)
                     .OrderBy(gruppe => gruppe.Key))
            Console.WriteLine($"      {gruppe.Key,-11} {gruppe.Count(),2} Ringe | "
                + $"{gruppe.Sum(fund => fund.Flaeche),12:F6} m2 | "
                + $"angenommen {gruppe.Count(fund =>
                    fund.AlleUlpKappenEntferntAngenommen),2}");
        Console.WriteLine("    nach Linienfolge:");
        foreach (var gruppe in ulpKappen.GroupBy(DoppelLinientripel)
                     .OrderByDescending(gruppe => gruppe.Count()))
            Console.WriteLine($"      {gruppe.Count(),2}  {gruppe.Key}");
        Console.WriteLine();

        Console.WriteLine("  Empfindlichkeit der Haarkappen-Gegenprobe:");
        foreach (var schwelle in new[] { 4d, 8d, 16d, 32d })
        {
            var ergebnisse = funde.Select(fund =>
            {
                var sauber = DoppelEntferneUlpKappen(fund.FloatRing, schwelle);
                return (Fund: fund, sauber.EntfernteKappen,
                    Angenommen: sauber.Ring.Length >= 3
                        && Cs2Triangulierung.Dreiecke(sauber.Ring) != 0);
            }).ToArray();
            var betroffen = ergebnisse.Where(wert =>
                wert.EntfernteKappen != 0).ToArray();
            var schmutzigeFormen = ergebnisse.GroupBy(wert => wert.Fund.Form)
                .Count(gruppe => gruppe.Any(wert => !wert.Angenommen));
            Console.WriteLine($"    {schwelle,2:F0} ULP: "
                + $"{betroffen.Length,2} Ringe mit "
                + $"{betroffen.Sum(wert => wert.EntfernteKappen),2} Kappen | "
                + $"{betroffen.Count(wert => wert.Angenommen),2} angenommen | "
                + $"{formenSchlecht.Count - schmutzigeFormen} Formen sauber");
        }
        Console.WriteLine();

        var nurAnhaengsel = funde.Where(fund => fund.EntfernteUlpKappen != 0
            && fund.AlleUlpKappenEntferntAngenommen).ToArray();
        var mischfaelle = funde.Where(fund => fund.EntfernteUlpKappen != 0
            && !fund.AlleUlpKappenEntferntAngenommen).ToArray();
        var echteSplitter = funde.Where(fund => fund.EntfernteUlpKappen == 0
            && (fund.DoubleVersatzSchneidetSich
                || fund.DoubleVersatzflaeche <= 0)).ToArray();
        var anderes = funde.Except(nurAnhaengsel)
            .Except(mischfaelle).Except(echteSplitter).ToArray();
        Console.WriteLine("  Klassifikation (8 ULP; naechster Ring liegt bei 9,109 ULP):");
        DoppelDruckeKlasse("Float-Anhaengsel", nurAnhaengsel);
        DoppelDruckeKlasse("Echter Splitter", echteSplitter);
        DoppelDruckeKlasse("Mischfall", mischfaelle);
        DoppelDruckeKlasse("Sonstiges", anderes);
        Console.WriteLine();

        Console.WriteLine("  Schwellenlauf Kantenlaenge / lokales Float-ULP:");
        foreach (var schwelle in new[] { 1d, 2d, 4d, 8d, 16d, 32d, 64d, 128d })
        {
            var gruppe = funde.Where(fund =>
                fund.Floatlaenge <= schwelle * fund.UlpMass).ToArray();
            Console.WriteLine($"    <= {schwelle,3:F0} ULP: {gruppe.Length,3} Ringe | "
                + $"{gruppe.Select(fund => fund.Form).Distinct().Count(),3} Formen | "
                + $"{gruppe.Sum(fund => fund.Flaeche),14:F6} m2 | "
                + $"Kappe weg -> {gruppe.Count(fund =>
                    fund.EntferneBeideAngenommen),3} angenommen");
        }
        Console.WriteLine();

        DoppelDruckeVerteilung("Float-Schritte der kuerzesten Kante", funde,
            fund => fund.Floatschritte == 0 ? "0"
                : fund.Floatschritte <= 2 ? "1-2"
                : fund.Floatschritte <= 8 ? "3-8"
                : fund.Floatschritte <= 64 ? "9-64"
                : fund.Floatschritte <= 1024 ? "65-1024" : ">1024");
        DoppelDruckeVerteilung("double-Laenge der kuerzesten Kante", funde,
            fund => fund.Doublelaenge < 0.0005 ? "<0,5 mm"
                : fund.Doublelaenge < 0.002 ? "0,5-2 mm"
                : fund.Doublelaenge < 0.01 ? "2-10 mm"
                : fund.Doublelaenge < 0.2 ? "1-20 cm" : ">=20 cm");
        DoppelDruckeVerteilung("Material", funde, fund => fund.Material);
        DoppelDruckeVerteilung("double-Innenversatz", funde, fund =>
            fund.DoubleVersatzSchneidetSich ? "Selbstschnitt"
                : fund.DoubleVersatzflaeche <= 0 ? "Umlauf gekippt"
                : "einfach + CCW");
        Console.WriteLine();

        Console.WriteLine("  Die 34 kleinsten ULP-Verhaeltnisse:");
        foreach (var fund in funde.OrderBy(fund => fund.Floatlaenge / fund.UlpMass)
                     .Take(34))
            Console.WriteLine($"    {fund.Floatlaenge / fund.UlpMass,9:F3} ULP | "
                + $"{fund.Form,-11} | {fund.Material,-5} | "
                + $"{fund.FloatRing.Length,3} P | {fund.Flaeche,12:F6} m2 | "
                + $"Kappen {fund.EntfernteUlpKappen} | "
                + $"danach {fund.AlleUlpKappenEntferntAngenommen}");
        Console.WriteLine();

        var details = string.IsNullOrWhiteSpace(detailfilter)
            ? funde
            : funde.Where(fund => fund.Form.IndexOf(
                detailfilter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        Console.WriteLine($"DETAILS {details.Count} Ring(e)"
            + (detailfilter == null ? string.Empty : $" fuer '{detailfilter}'"));
        foreach (var fund in details)
            DoppelDruckeFund(fund);
        return 0;
    }

    private static bool DoppelIstUlpFund(DoppelRingfund fund) =>
        fund.Floatschritte <= 8 && fund.Floatlaenge <= 8 * fund.UlpMass;

    private static string DoppelLinientripel(DoppelRingfund fund)
    {
        var modell = (fund.Modellstart + fund.KuerzesteKante)
            % fund.Modellring.Kanten.Count;
        var vorher = fund.Modellring.Kanten[Geometrie.Mod(
            modell - 1, fund.Modellring.Kanten.Count)].Linie.Art;
        var kante = fund.Modellring.Kanten[modell].Linie.Art;
        var nachher = fund.Modellring.Kanten[(modell + 1)
            % fund.Modellring.Kanten.Count].Linie.Art;
        return $"{vorher} -> {kante} -> {nachher}";
    }

    private static void DoppelDruckeFund(DoppelRingfund fund)
    {
        var i = fund.KuerzesteKante;
        var modell = (fund.Modellstart + i) % fund.Modellring.Kanten.Count;
        var vorher = fund.Modellring.Kanten[Geometrie.Mod(
            modell - 1, fund.Modellring.Kanten.Count)].Linie;
        var kante = fund.Modellring.Kanten[modell].Linie;
        var nachher = fund.Modellring.Kanten[(modell + 1)
            % fund.Modellring.Kanten.Count].Linie;
        Console.WriteLine($"  {fund.Form} | {fund.Material} #{fund.Ringindex} | "
            + $"{fund.FloatRing.Length} Punkte | {fund.Flaeche:F6} m2 | "
            + $"float {fund.Floatlaenge:R} m / double {fund.Doublelaenge:R} m | "
            + $"Schritte {fund.Floatschritte} | ULP {fund.UlpMass:R} m | "
            + $"Hals(double) {fund.DoubleHals:R} m | "
            + $"Versatzschnitt {fund.DoubleVersatzSchneidetSich} | "
            + $"Versatzflaeche {fund.DoubleVersatzflaeche:R} m2");
        Console.WriteLine($"    [{i}] "
            + $"{DoppelPunkt(fund.DoubleWelt[i])} -> "
            + $"{DoppelPunkt(fund.DoubleWelt[(i + 1) % fund.DoubleWelt.Length])}");
        Console.WriteLine($"    Linien: {vorher.Name} -> {kante.Name} -> "
            + $"{nachher.Name}; Besitz: {fund.Besitz}");
        Console.WriteLine($"    Gegenprobe angenommen: nur Punkt {i} weg "
            + $"{fund.EntferneVorpunktAngenommen}, nur Punkt "
            + $"{(i + 1) % fund.FloatRing.Length} weg "
            + $"{fund.EntferneNachpunktAngenommen}, beide weg "
            + $"{fund.EntferneBeideAngenommen}");
        Console.WriteLine($"    Von-Knoten: " + DoppelKnoten(fund.VonInfo));
        Console.WriteLine($"    Nach-Knoten: " + DoppelKnoten(fund.NachInfo));
        Console.WriteLine($"    Gruene Gegenseite: roh {fund.RohkanteImGruen}, "
            + $"fertig {fund.EndkanteImGruen}, Rohlinien "
            + $"{fund.GrueneRohlinien ?? "-"}");
    }

    private static string DoppelPunkt(Punkt punkt) =>
        $"{punkt.X.ToString("R", CultureInfo.InvariantCulture)}/"
        + punkt.Y.ToString("R", CultureInfo.InvariantCulture);

    private static string DoppelKnoten(DoppelKnoteninfo info) =>
        $"vor Zufahrt {info.VorZufahrt}, {info.Zellen.Count} Zellen "
        + $"[{string.Join(",", info.Zellarten.OrderBy(wert => wert))}], "
        + $"Linien [{string.Join(" | ", info.Linien.Values
            .OrderBy(linie => linie.Id).Select(linie =>
                $"{linie.Id}:{linie.Name}"))}]";

    private static Dictionary<int, DoppelKnoteninfo> DoppelKnoteninfos(
        Bauergebnis bau)
    {
        var vor = new HashSet<int>(bau.ZellenVorZufahrt.SelectMany(zelle =>
            zelle.Polygon.Ecken.Select(ecke => ecke.Knoten.Id)));
        var ausgabe = new Dictionary<int, DoppelKnoteninfo>();
        foreach (var zelle in bau.Zellen)
            for (var i = 0; i < zelle.Polygon.Anzahl; i++)
            {
                var knoten = zelle.Polygon.Knoten(i);
                DoppelKnoteninfo info;
                if (!ausgabe.TryGetValue(knoten.Id, out info))
                {
                    info = new DoppelKnoteninfo { VorZufahrt = vor.Contains(knoten.Id) };
                    ausgabe.Add(knoten.Id, info);
                }
                info.Zellen.Add(zelle.Id);
                info.Zellarten.Add(zelle.Art.ToString());
                info.Materialien.Add(zelle.Material.ToString());
                var an = zelle.Polygon.Linie(i);
                var ein = zelle.Polygon.Linie(i - 1);
                info.Linien[an.Id] = an;
                info.Linien[ein.Id] = ein;
            }
        return ausgabe;
    }

    private static (bool Roh, bool Ende, string Linien) DoppelGrueneGegenkante(
        Bauergebnis bau, KantenSchluessel schluessel)
    {
        var roh = false;
        var ende = false;
        string linien = null;
        foreach (var flaeche in bau.Flaechen.Where(flaeche =>
                     flaeche.Material == ZellMaterial.Gruen))
            foreach (var ring in flaeche.AlleRinge)
            {
                if (ring.Kanten.Any(kante => kante.Schluessel == schluessel))
                    ende = true;
                for (var i = 0; i < ring.Rohkanten.Count; i++)
                {
                    if (ring.Rohkanten[i].Schluessel != schluessel) continue;
                    roh = true;
                    var vorher = ring.Rohkanten[Geometrie.Mod(
                        i - 1, ring.Rohkanten.Count)].Linie;
                    var aktuell = ring.Rohkanten[i].Linie;
                    var nachher = ring.Rohkanten[(i + 1)
                        % ring.Rohkanten.Count].Linie;
                    linien = $"{vorher.Art} -> {aktuell.Art} -> {nachher.Art}"
                        + $"; IDs {vorher.Id}/{aktuell.Id}/{nachher.Id}";
                }
            }
        return (roh, ende, linien);
    }

    private static int DoppelOrdneZu(float2[] oeffentlich, Ring modell,
                                      Rahmen rahmen)
    {
        if (oeffentlich.Length != modell.Kanten.Count)
            throw new InvalidOperationException(
                $"Ring hat public/intern {oeffentlich.Length}/{modell.Kanten.Count} Punkte.");
        var intern = modell.Kanten.Select(kante =>
        {
            var welt = rahmen.NachWelt(kante.Von.Punkt);
            return new float2((float)welt.X, (float)welt.Y);
        }).ToArray();
        for (var start = 0; start < intern.Length; start++)
        {
            var passt = true;
            for (var i = 0; i < intern.Length; i++)
                if (!math.all(oeffentlich[i] == intern[(start + i) % intern.Length]))
                {
                    passt = false;
                    break;
                }
            if (passt) return start;
        }
        throw new InvalidOperationException(
            "Der public-Ring ist keine zyklische float-Kopie des Modellrings.");
    }

    private static (int Index, double Laenge) DoppelKuerzesteKante(float2[] ring)
    {
        var index = -1;
        var laenge = double.PositiveInfinity;
        for (var i = 0; i < ring.Length; i++)
        {
            var wert = math.distance(ring[i], ring[(i + 1) % ring.Length]);
            if (wert >= laenge) continue;
            index = i;
            laenge = wert;
        }
        return (index, laenge);
    }

    private static ulong DoppelFloatschritte(float a, float b)
    {
        var oa = DoppelGeordnet(a);
        var ob = DoppelGeordnet(b);
        return oa >= ob ? oa - ob : ob - oa;
    }

    private static ulong DoppelGeordnet(float wert)
    {
        var bits = unchecked((uint)BitConverter.SingleToInt32Bits(wert));
        return (bits & 0x80000000u) != 0 ? ~bits : bits | 0x80000000u;
    }

    private static double DoppelUlpMass(float2 punkt)
    {
        double U(float wert)
        {
            if (float.IsNaN(wert) || float.IsInfinity(wert))
                return double.PositiveInfinity;
            return Math.Max(
                Math.Abs((double)MathF.BitIncrement(wert) - wert),
                Math.Abs((double)MathF.BitDecrement(wert) - wert));
        }
        return Math.Sqrt(U(punkt.x) * U(punkt.x) + U(punkt.y) * U(punkt.y));
    }

    private static float2[] DoppelEntferne(float2[] ring, int index) =>
        ring.Where((_, i) => i != index).ToArray();

    private static float2[] DoppelEntferneZweiNachbarn(float2[] ring, int index)
    {
        var zweiter = (index + 1) % ring.Length;
        return ring.Where((_, i) => i != index && i != zweiter).ToArray();
    }

    private static (float2[] Ring, int EntfernteKappen) DoppelEntferneUlpKappen(
        float2[] eingabe, double maximaleUlps)
    {
        var ring = eingabe.ToList();
        var entfernt = 0;
        while (ring.Count > 4)
        {
            var kandidat = -1;
            var bestesVerhaeltnis = double.PositiveInfinity;
            for (var i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];
                var laenge = math.distance(a, b);
                var ulp = Math.Max(DoppelUlpMass(a), DoppelUlpMass(b));
                var verhaeltnis = laenge / ulp;
                if (verhaeltnis > maximaleUlps || verhaeltnis >= bestesVerhaeltnis)
                    continue;
                var vorher = a - ring[Geometrie.Mod(i - 1, ring.Count)];
                var nachher = ring[(i + 2) % ring.Count] - b;
                var produkt = math.length(vorher) * math.length(nachher);
                if (produkt == 0) continue;
                // Eine Haarkappe verbindet zwei fast gegenlaeufige Seiten
                // desselben Fortsatzes. Eine kurze Seite eines normalen
                // Rechtecks wird dadurch nicht versehentlich entfernt.
                var cos = math.dot(vorher, nachher) / produkt;
                if (cos > -0.999f) continue;
                kandidat = i;
                bestesVerhaeltnis = verhaeltnis;
            }
            if (kandidat < 0) break;
            var zweiter = (kandidat + 1) % ring.Count;
            if (zweiter > kandidat)
            {
                ring.RemoveAt(zweiter);
                ring.RemoveAt(kandidat);
            }
            else
            {
                ring.RemoveAt(kandidat);
                ring.RemoveAt(zweiter);
            }
            entfernt++;
        }
        return (ring.ToArray(), entfernt);
    }

    private static double DoppelHals(Punkt[] ring)
    {
        var minimum = double.PositiveInfinity;
        for (var p = 0; p < ring.Length; p++)
            for (var k = 0; k < ring.Length; k++)
            {
                if (k == p || (k + 1) % ring.Length == p) continue;
                minimum = Math.Min(minimum, DoppelAbstandPunktStrecke(
                    ring[p], ring[k], ring[(k + 1) % ring.Length]));
            }
        return minimum;
    }

    private static double DoppelAbstandPunktStrecke(Punkt p, Punkt a, Punkt b)
    {
        var ab = b - a;
        var quadrat = Geometrie.Skalar(ab, ab);
        if (quadrat == 0) return Geometrie.Laenge(p - a);
        var t = Math.Max(0, Math.Min(1, Geometrie.Skalar(p - a, ab) / quadrat));
        return Geometrie.Laenge(p - (a + ab * t));
    }

    private readonly struct DoppelVersatzmass
    {
        internal DoppelVersatzmass(bool schneidetSich, double vorzeichenflaeche)
        {
            SchneidetSich = schneidetSich;
            Vorzeichenflaeche = vorzeichenflaeche;
        }

        internal bool SchneidetSich { get; }
        internal double Vorzeichenflaeche { get; }
    }

    private static DoppelVersatzmass DoppelVersatzmassVon(Punkt[] ring)
    {
        var versetzt = new Punkt[ring.Length];
        for (var i = 0; i < ring.Length; i++)
            versetzt[i] = DoppelVersetzterKnoten(ring, i, -0.1);
        var schneidet = false;
        for (var a = 0; a < versetzt.Length; a++)
            for (var b = a + 1; b < versetzt.Length; b++)
            {
                if (b == a || b == (a + 1) % versetzt.Length
                    || a == (b + 1) % versetzt.Length)
                    continue;
                if (DoppelStreckenSchneiden(
                    versetzt[a], versetzt[(a + 1) % versetzt.Length],
                    versetzt[b], versetzt[(b + 1) % versetzt.Length]))
                    schneidet = true;
            }
        return new DoppelVersatzmass(
            schneidet, Geometrie.Vorzeichenflaeche(versetzt));
    }

    private static Punkt DoppelVersetzterKnoten(Punkt[] ring, int i, double betrag)
    {
        var hier = ring[i];
        var a = DoppelNormalisiere(ring[Geometrie.Mod(i - 1, ring.Length)] - hier);
        var b = DoppelNormalisiere(ring[(i + 1) % ring.Length] - hier);
        var senkrecht = new Punkt(-a.Y, a.X);
        var winkel = Math.Acos(Math.Max(-1, Math.Min(1, Geometrie.Skalar(a, b))));
        var zeichen = Math.Sign(Geometrie.Skalar(senkrecht, b));
        var tan = Math.Tan(winkel * 0.5);
        senkrecht += a * (tan < 0.001 ? 0 : zeichen / tan);
        return hier + senkrecht * betrag;
    }

    private static Punkt DoppelNormalisiere(Punkt punkt)
    {
        var laenge = Geometrie.Laenge(punkt);
        return laenge == 0 ? new Punkt(0, 0) : punkt * (1 / laenge);
    }

    private static bool DoppelStreckenSchneiden(Punkt a, Punkt b, Punkt c, Punkt d)
    {
        const double epsilon = 1e-10;
        var ab = b - a;
        var cd = d - c;
        var o1 = Geometrie.Kreuz(ab, c - a);
        var o2 = Geometrie.Kreuz(ab, d - a);
        var o3 = Geometrie.Kreuz(cd, a - c);
        var o4 = Geometrie.Kreuz(cd, b - c);
        if (((o1 > epsilon && o2 < -epsilon) || (o1 < -epsilon && o2 > epsilon))
            && ((o3 > epsilon && o4 < -epsilon)
                || (o3 < -epsilon && o4 > epsilon)))
            return true;
        return Math.Abs(o1) <= epsilon && DoppelAufStrecke(c, a, b)
            || Math.Abs(o2) <= epsilon && DoppelAufStrecke(d, a, b)
            || Math.Abs(o3) <= epsilon && DoppelAufStrecke(a, c, d)
            || Math.Abs(o4) <= epsilon && DoppelAufStrecke(b, c, d);
    }

    private static bool DoppelAufStrecke(Punkt p, Punkt a, Punkt b) =>
        p.X >= Math.Min(a.X, b.X) - 1e-10
        && p.X <= Math.Max(a.X, b.X) + 1e-10
        && p.Y >= Math.Min(a.Y, b.Y) - 1e-10
        && p.Y <= Math.Max(a.Y, b.Y) + 1e-10;

    private static void DoppelDruckeVerteilung(
        string titel,
        IEnumerable<DoppelRingfund> funde,
        Func<DoppelRingfund, string> schluessel)
    {
        Console.WriteLine("  " + titel + ":");
        foreach (var gruppe in funde.GroupBy(schluessel)
                     .OrderBy(gruppe => gruppe.Key))
            Console.WriteLine($"    {gruppe.Key,-12} {gruppe.Count(),3} Ringe | "
                + $"{gruppe.Select(fund => fund.Form).Distinct().Count(),3} Formen | "
                + $"{gruppe.Sum(fund => fund.Flaeche),14:F6} m2");
    }

    private static void DoppelDruckeKlasse(
        string name, IReadOnlyCollection<DoppelRingfund> funde)
    {
        Console.WriteLine($"    {name,-18} {funde.Count,3} Ringe | "
            + $"{funde.Select(fund => fund.Form).Distinct().Count(),3} Formen | "
            + $"{funde.Sum(fund => fund.Flaeche),14:F6} m2");
        if (funde.Count != 0)
        {
            Console.WriteLine($"      double-Kante "
                + $"{funde.Min(fund => fund.Doublelaenge):R} .. "
                + $"{funde.Max(fund => fund.Doublelaenge):R} m | "
                + $"ULP-Verhaeltnis "
                + $"{funde.Min(fund => fund.Floatlaenge / fund.UlpMass):F3} .. "
                + $"{funde.Max(fund => fund.Floatlaenge / fund.UlpMass):F3}");
            Console.WriteLine($"      double-Innenversatz: Selbstschnitt "
                + $"{funde.Count(fund => fund.DoubleVersatzSchneidetSich)}, "
                + $"Umlauf gekippt {funde.Count(fund =>
                    !fund.DoubleVersatzSchneidetSich
                    && fund.DoubleVersatzflaeche <= 0)}");
        }
    }
}
