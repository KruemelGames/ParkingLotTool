using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using ParkingLotTool.Geometry.Zellen;
using Unity.Mathematics;

internal static partial class Program
{
    private static int LochtrennungFormen;
    private static int LochtrennungNaehte;

    /**
     * WAS CS2 VON UNSEREN FLAECHEN WIRKLICH ABLEHNT.
     *
     * Der Abdeckungstest misst, ob das Areal rechnerisch bedeckt ist, und
     * meldet fuer die groesste echte Nutzerform des Bauprotokolls
     * `0,00 % ungedeckt`. Im Spiel fehlen trotzdem Flaechen. Das ist kein
     * Widerspruch: CS2 baut eine Flaeche gar nicht erst, wenn zwei ihrer
     * Punkte naeher als 0,375 m beieinanderliegen oder sie sich irgendwo auf
     * fast nichts einschnuert (gemessen: 783,5 m2 grosse Flaeche mit 0,00077 m
     * Hals wurde verworfen - siehe cs2-flaechen-grenzen).
     *
     * Eine rechnerisch perfekte Zerlegung kann also im Spiel loechrig sein.
     * Genau diese Luecke im Messwesen schliesst dieser Lauf: er bewertet die
     * ausgegebenen Ringe nach CS2s Annahmekriterien, nicht nach Abdeckung.
     *
     * Zusaetzlich zaehlt er SPITZE WINKEL. Ansage des Nutzers am 2026-08-24:
     * *"wir muessen spitze winkel-flaechen vermeiden besonders da es durch
     * User-Input dazu kommen kann."* Ein spitzer Keil ist der Vorbote der
     * Einschnuerung - er ueberlebt vielleicht diese Form, aber bei leicht
     * anderem Nutzerpolygon wird daraus ein Hals unter 0,375 m.
     */
    private const double Cs2Mindestkante = 0.375;

    /** Ab hier gilt eine Ecke als spitz. Kein CS2-Wert - eine Warnschwelle. */
    private const double SpitzGrad = 15.0;

    private sealed class Ringmass
    {
        internal double MinKante = double.PositiveInfinity;
        internal double MinWinkel = 180;
        internal double MinHals = double.PositiveInfinity;
        internal double Flaeche;
        internal double Breite;
        internal double Laenge;
        internal float2 Mitte;
        /** Zwei aufeinanderfolgende Punkte sind identisch. */
        internal int Doppelpunkte;
        /** Zwei NICHT benachbarte Punkte sind identisch - der Ring beruehrt sich. */
        internal int Selbstberuehrungen;
        /**
         * WO die duennste Stelle liegt. Das ist die eigentliche Aussage.
         *
         * Liegt sie auf der Nutzerkante, ist sie ein Artefakt des Zuschnitts:
         * das Zellraster steht im Reihenwinkel, die Nutzerkante in einem
         * beliebigen - wo beide sich treffen, bleibt ein Rest beliebiger
         * Duenne. Liegt sie INNEN, kann es das nicht sein; dann entsteht sie
         * am Stoss zweier Wege, also an einer Gehrung.
         *
         * Diese Trennung entscheidet, welcher Loesungsansatz ueberhaupt
         * greifen kann - deshalb wird sie gemessen und nicht vermutet.
         */
        internal float2 DuennsteStelle;
    }

    private static int RunFlaechen(int grenze)
    {
        var einstellungen = LayoutSettings.Cs2;
        einstellungen.AngleMode = "edge";
        einstellungen.Auto = false;
        einstellungen.Zellen = true;
        einstellungen.Entrances = new[] { new Entrance { Edge = 0, Along = 20 } };

        var formen = new List<(string Name, float2[] Site)>();
        foreach (var fall in Cases) formen.Add((fall.Name, fall.Site));
        formen.AddRange(FormenAusBauprotokoll());
        if (grenze > 0 && formen.Count > grenze)
            formen = formen.Take(grenze).ToList();

        Console.WriteLine($"FLAECHENANNAHME ueber {formen.Count} Formen, Zellenweg, "
            + "Modus edge, mit Zufahrt");
        Console.WriteLine($"  Ein Ring gilt als von CS2 verworfen, wenn seine "
            + $"kuerzeste Kante unter {Cs2Mindestkante:F3} m liegt oder sein "
            + "engster Hals darunter.");
        Console.WriteLine();

        var formenGesamt = 0;
        var formenSauber = 0;
        var ringeGesamt = 0;
        var ringeVerworfen = 0;
        var ringeSpitz = 0;
        var flaecheVerworfen = 0.0;
        var abbrueche = 0;
        // DREI VOELLIG VERSCHIEDENE URSACHEN, die dieselbe Kennzahl erzeugen.
        // Ein Doppelpunkt ist ein Ausgabefehler und waere trivial zu beheben;
        // eine Selbstberuehrung ist strukturell (der Ring laeuft an sich selbst
        // vorbei); ein Splitter ist ein echtes zu duennes Stueck Geometrie.
        // Sie zusammen zu zaehlen haette den Auftrag an Codex in die falsche
        // Richtung geschickt.
        // DAS ECHTE KRITERIUM, seit dem 2026-08-24 aus dem Dekompilat.
        // Die 0,375-m-Regel daneben bleibt stehen, damit der Unterschied
        // sichtbar ist - sie war die Schaetzung, mit der wir monatelang
        // gerechnet haben.
        var cs2Verworfen = 0;
        var cs2Kappe = 0;    // durch Entfernen der Haarkappe geheilt
        var cs2Stachel = 0;  // laeuft auf sich selbst zurueck (Hals ~ 0)
        var cs2Keil = 0;     // echter duenner Keil
        var cs2FormenSauber = 0;
        var cs2FlaecheVerworfen = 0.0;
        var artDoppel = 0;
        var artBeruehrt = 0;
        var artSplitter = 0;
        // Nach ORT: auf der Nutzerkante oder im Inneren?
        // Die Bauteile liegen in BEKANNTEN Abstaenden von der Nutzerkante:
        //   0,0 - 1,0 m   Randstreifen (Es)
        //   1,0 - 6,9 m   Randbuchtreihe (Es + Buchttiefe)
        //   6,9 - 13,9 m  Randstrasse (bis Es + Sl + Ai)
        //   ueber 13,9 m  Inneres: Buchtreihen, Fahrgassen, Querstrassen
        // Der Abstand der duennsten Stelle sagt damit direkt, WELCHES Bauteil
        // sie erzeugt hat. "Innen" allein waere zu grob gewesen.
        var band = new int[4];
        var bandSpitz = new int[4];
        // GEGENPROBE ZU CODEX' BEFUND: er misst die Ursache in
        // `Lochtrennung.cs`. Wenn das stimmt, muessen Formen OHNE Loch sauber
        // sein und Formen MIT Loch schmutzig - und zwar umso mehr, je mehr
        // Loecher aufgetrennt wurden. Das ist unabhaengig von seinem Werkzeug
        // pruefbar, indem der Zellenbau direkt befragt wird.
        var ohneLochSauber = 0; var ohneLochGesamt = 0;
        var mitLochSauber = 0; var mitLochGesamt = 0;
        var naehteGesamt = 0;
        // Alle Formen haben Loecher - eine Kontrollgruppe gibt es also nicht.
        // Dann muss die ANZAHL entscheiden: laufen Naehte und verworfene Ringe
        // miteinander, ist die Naht die Ursache; laufen sie auseinander, nicht.
        var paare = new List<(int Naehte, int Schlecht)>();
        // Nach Groesse getrennt: der Nutzer meldet ausdruecklich "groessere
        // Formen sowie mehrere kleine Formen". Ob das stimmt, entscheidet
        // diese Aufteilung und nicht mein Eindruck.
        var nachGroesse = new SortedDictionary<string, (int Formen, int Sauber)>();
        var schlimmste = new List<(string Form, Ringmass Mass, string Art)>();

        foreach (var form in formen)
        {
            ParkingLayout layout;
            try { layout = ParkingGeometry.Build(form.Site, einstellungen); }
            catch { abbrueche++; continue; }

            formenGesamt++;
            var arealFlaeche = Math.Abs(FlaechenmassRing(form.Site));
            var eimer = arealFlaeche < 5000 ? "  bis  5.000 m2"
                : arealFlaeche < 15000 ? "  bis 15.000 m2"
                : arealFlaeche < 30000 ? "  bis 30.000 m2"
                : "  ueber 30.000 m2";

            var schlecht = 0;
            var cs2Schlecht = 0;
            var spitz = 0;
            foreach (var (ringe, art) in new[]
                     {
                         (layout.GrassSurface, "Gras"),
                         (layout.AsphaltSurface, "Belag"),
                     })
            {
                if (ringe == null) continue;
                foreach (var ring in ringe)
                {
                    var mass = Vermessen(ring);
                    if (mass == null) continue;
                    ringeGesamt++;
                    // Genau das, was das Spiel rechnet: 0 Dreiecke = unsichtbar.
                    if (Cs2Triangulierung.Dreiecke(ring) == 0)
                    {
                        cs2Schlecht++;
                        cs2Verworfen++;
                        cs2FlaecheVerworfen += mass.Flaeche;
                        /*
                         * WELCHE ART VON FEHLER IST ES?
                         *
                         * Der Befund an der 251.518-m2-Form des Nutzers: der
                         * Ring ist ein sauberes 3x7-Rechteck mit einem 5,9 m
                         * langen STACHEL VON NULL BREITE - der Weg laeuft
                         * hinaus und auf derselben Linie zurueck. Die
                         * Haarkappe an der Spitze ist nur das Ende davon.
                         *
                         * Ein solcher Ruecklauf ist genau das, was ein
                         * Trennkorridor der Lochtrennung hinterlaesst. Das
                         * unterscheidet ihn von einem echten duennen Keil,
                         * und die beiden brauchen verschiedene Antworten.
                         */
                        var ohne = OhneHaarkappen(ring);
                        var m2 = Vermessen(ohne) ?? mass;
                        if (Cs2Triangulierung.Dreiecke(ohne) != 0) cs2Kappe++;
                        else if (m2.MinHals < 0.01) cs2Stachel++;
                        else cs2Keil++;
                    }
                    if (mass.MinKante < Cs2Mindestkante
                        || mass.MinHals < Cs2Mindestkante)
                    {
                        schlecht++;
                        flaecheVerworfen += mass.Flaeche;
                        // DIE TRENNUNG, AUF DIE ES ANKOMMT.
                        // "Kurze Kante" heisst: der Ring hat irgendwo ein
                        // winziges Stueck Rand - ein Splitter am Zuschnitt.
                        // "Einschnuerung" heisst: alle Kanten sind lang, aber
                        // zwei nicht benachbarte laufen aneinander vorbei -
                        // der Ring ist ein fast geschlossenes C oder haengt
                        // nur an einem Haar zusammen. Das sind zwei voellig
                        // verschiedene Fehler mit zwei verschiedenen Fixes.
                        if (mass.MinKante < Cs2Mindestkante) artSplitter++;
                        else artBeruehrt++;
                        band[Band(mass.DuennsteStelle, form.Site)]++;
                        artDoppel += mass.Doppelpunkte;
                        if (schlimmste.Count < 400)
                            schlimmste.Add((form.Name, mass, art));
                    }
                    if (mass.MinWinkel < SpitzGrad)
                    {
                        spitz++;
                        bandSpitz[Band(mass.DuennsteStelle, form.Site)]++;
                    }
                }
            }

            var loecher = LoecherDerForm(form.Site, einstellungen);
            if (loecher.Naehte >= 0)
            {
                naehteGesamt += loecher.Naehte;
                paare.Add((loecher.Naehte, schlecht));
                if (loecher.Loecher == 0)
                {
                    ohneLochGesamt++;
                    if (schlecht == 0) ohneLochSauber++;
                }
                else
                {
                    mitLochGesamt++;
                    if (schlecht == 0) mitLochSauber++;
                }
            }

            ringeVerworfen += schlecht;
            ringeSpitz += spitz;
            var sauber = schlecht == 0;
            if (sauber) formenSauber++;
            if (cs2Schlecht == 0) cs2FormenSauber++;
            var stand = nachGroesse.TryGetValue(eimer, out var alt)
                ? alt : (Formen: 0, Sauber: 0);
            nachGroesse[eimer] = (stand.Formen + 1, stand.Sauber + (sauber ? 1 : 0));
        }

        Console.WriteLine($"  Formen gebaut {formenGesamt}, abgebrochen {abbrueche}");
        Console.WriteLine(LochtrennungFormen == 0
            ? "  Lochtrennung: bei KEINER Form noetig - so soll es sein."
            : $"  ACHTUNG Lochtrennung sprang bei {LochtrennungFormen} Formen an, {LochtrennungNaehte} Naehte. Irgendwo verschmilzt eine Flaeche zum Ring.");
        Console.WriteLine($"  Formen OHNE einen einzigen verworfenen Ring: "
            + $"{formenSauber} von {formenGesamt} "
            + $"({100d * formenSauber / Math.Max(1, formenGesamt):F1} %)");
        Console.WriteLine($"  Ringe gesamt {ringeGesamt}, davon von CS2 "
            + $"voraussichtlich verworfen {ringeVerworfen} "
            + $"({100d * ringeVerworfen / Math.Max(1, ringeGesamt):F1} %), "
            + $"zusammen {flaecheVerworfen:F1} m2");
        Console.WriteLine($"    davon kurze KANTE (Splitter am Zuschnitt): "
            + $"{artSplitter}");
        Console.WriteLine($"    davon nur EINSCHNUERUNG (lange Kanten, aber der "
            + $"Ring laeuft an sich selbst vorbei): {artBeruehrt}");
        Console.WriteLine($"    Doppelpunkte insgesamt: {artDoppel}");
        Console.WriteLine("    Abstand der duennsten Stelle von der Nutzerkante:");
        Console.WriteLine($"      bis  1,0 m (Randstreifen)      {band[0],4}");
        Console.WriteLine($"      bis  6,9 m (Randbuchtreihe)    {band[1],4}");
        Console.WriteLine($"      bis 13,9 m (Randstrasse)       {band[2],4}");
        Console.WriteLine($"      ueber 13,9 m (Inneres)         {band[3],4}");
        Console.WriteLine();
        Console.WriteLine("  NACH CS2s EIGENER TRIANGULIERUNG (Ear-Clipping "
            + "nachgebaut, float, Weltkoordinaten):");
        Console.WriteLine($"    Formen ohne einen verworfenen Ring: "
            + $"{cs2FormenSauber} von {formenGesamt} "
            + $"({100d * cs2FormenSauber / Math.Max(1, formenGesamt):F1} %)");
        Console.WriteLine($"    davon Haarkappe (Fast-Doppelpunkt): {cs2Kappe}");
        Console.WriteLine($"    davon STACHEL (Ring laeuft auf sich selbst "
            + $"zurueck, Hals ~ 0): {cs2Stachel}");
        Console.WriteLine($"    davon echter duenner Keil: {cs2Keil}");
        Console.WriteLine($"    Ringe, die CS2 verwirft: {cs2Verworfen} von "
            + $"{ringeGesamt} "
            + $"({100d * cs2Verworfen / Math.Max(1, ringeGesamt):F1} %), "
            + $"zusammen {cs2FlaecheVerworfen:F1} m2");
        Console.WriteLine();
        Console.WriteLine($"  Ringe mit einer Ecke unter {SpitzGrad:F0} Grad: "
            + $"{ringeSpitz} ({100d * ringeSpitz / Math.Max(1, ringeGesamt):F1} %)");
        Console.WriteLine($"    Abstand: Randstreifen {bandSpitz[0]}, "
            + $"Randbuchtreihe {bandSpitz[1]}, Randstrasse {bandSpitz[2]}, "
            + $"Inneres {bandSpitz[3]}");
        Console.WriteLine();

        Console.WriteLine("  GEGENPROBE Lochtrennung:");
        Console.WriteLine($"    Formen OHNE Loch: {ohneLochSauber} von "
            + $"{ohneLochGesamt} sauber "
            + $"({100d * ohneLochSauber / Math.Max(1, ohneLochGesamt):F0} %)");
        Console.WriteLine($"    Formen MIT  Loch: {mitLochSauber} von "
            + $"{mitLochGesamt} sauber "
            + $"({100d * mitLochSauber / Math.Max(1, mitLochGesamt):F0} %)");
        Console.WriteLine($"    Trennnaehte insgesamt: {naehteGesamt}");
        Console.WriteLine("    Wessen Flaeche hat ein Loch (Material -> Anzahl):");
        foreach (var m in LochNachMaterial)
            Console.WriteLine($"      {m.Key,-14} {m.Value,5}");
        foreach (var gruppe in paare.GroupBy(x => Math.Min(x.Naehte, 5))
                     .OrderBy(g => g.Key))
            Console.WriteLine($"      {gruppe.Key}{(gruppe.Key == 5 ? "+" : " ")} Naehte: "
                + $"{gruppe.Count(),4} Formen, davon sauber "
                + $"{gruppe.Count(x => x.Schlecht == 0),4}, verworfene Ringe im "
                + $"Mittel {gruppe.Average(x => x.Schlecht):F2}");
        Console.WriteLine();

        Console.WriteLine("  Sauberkeit nach Arealgroesse:");
        foreach (var eintrag in nachGroesse)
            Console.WriteLine($"  {eintrag.Key}  {eintrag.Value.Sauber,4} von "
                + $"{eintrag.Value.Formen,4} sauber "
                + $"({100d * eintrag.Value.Sauber / Math.Max(1, eintrag.Value.Formen):F0} %)");
        Console.WriteLine();

        // Die AUSDEHNUNG ist die eigentliche Aussage: `0,30 x 28,40 m` sagt
        // sofort "langer duenner Streifen an einer Kante", `1,2 x 1,4 m` sagt
        // "verlorenes Eck". Das trennt zwei voellig verschiedene Ursachen.
        Console.WriteLine("  Die 15 duennsten verworfenen Ringe:");
        foreach (var fall in schlimmste
                     .OrderBy(f => Math.Min(f.Mass.MinKante, f.Mass.MinHals))
                     .Take(15))
            Console.WriteLine($"    {fall.Art} {fall.Mass.Breite,7:F2} x "
                + $"{fall.Mass.Laenge,7:F2} m | {fall.Mass.Flaeche,8:F2} m2 | "
                + $"Kante {fall.Mass.MinKante:F4} | Hals {fall.Mass.MinHals:F4} | "
                + $"Winkel {fall.Mass.MinWinkel,5:F1} Grad | {fall.Form}");

        // Dieser Lauf ist ein MESSGERAET, kein Test mit Sollwert - er meldet
        // immer 0. Ein Sollwert waere hier eine Zahl, die ich mir ausdenke;
        // der Massstab ist der Vergleich vorher/nachher.
        return 0;
    }

    /**
     * Liegt der Punkt auf dem Umriss des Nutzerpolygons?
     *
     * 1,0 m ist grosszuegig gewaehlt: die Randstrasse liegt 1,0 m innerhalb
     * der Kante, ein Zuschnittrest an der Kante also allemal darunter. Eine
     * Gehrung zweier innerer Wege liegt dagegen mindestens 6,9 m innen.
     */
    /**
     * Fragt den Zellenbau direkt, wie viele Loecher aufgetrennt wurden.
     *
     * Ohne Zufahrt gebaut - fuer die Frage "hat diese Form ueberhaupt ein
     * Loch" reicht das, und es spart die private Eingabenormalisierung des
     * Adapters nachzubauen. Der Unterschied ist im Bericht genannt.
     */
    private static (int Loecher, int Naehte) LoecherDerForm(
        float2[] site, LayoutSettings settings)
    {
        try
        {
            var punkte = site.Select(p => new Punkt(p.x, p.y)).ToList();
            var form = new Formdefinition("Messung", punkte);
            var e = new Zelleneinstellungen
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
            };
            var bau = Layoutbauer.Baue(form, e);
            /*
             * WAECHTER, nicht Statistik. Die Lochtrennung darf nach dem
             * Umbau vom 2026-08-25 nirgends mehr anspringen. Tut sie es
             * doch, verschmilzt irgendwo eine Flaeche wieder zum Ring -
             * und das ist ein Konstruktionsfehler davor, keine Aufgabe
             * fuer eine Reparatur. Deshalb steht die Zahl im
             * Standardlauf und nicht in einem Sonderbefehl.
             */
            if (bau.Lochtrennung?.Trennnaehte != null
                && bau.Lochtrennung.Trennnaehte.Count != 0)
            {
                LochtrennungFormen++;
                LochtrennungNaehte += bau.Lochtrennung.Trennnaehte.Count;
            }
            var bericht = bau.Lochtrennung;
            if (bericht == null) return (0, -1);
            // WAS genau ein Loch ist, wird hier zaehlbar: welche Flaeche
            // (welches Material) umschliesst wen? Ohne das bleibt "Loch" ein
            // Wort fuer zwei verschiedene Dinge.
            foreach (var f in bau.FlaechenVorTrennung)
            {
                if (f.Loecher == null || f.Loecher.Count == 0) continue;
                var schluessel = f.Material.ToString();
                LochNachMaterial[schluessel] =
                    (LochNachMaterial.TryGetValue(schluessel, out var v) ? v : 0)
                    + f.Loecher.Count;
            }
            return (bericht.LoecherVorher,
                bericht.Trennnaehte?.Count ?? 0);
        }
        catch { return (0, -1); }
    }

    private static readonly SortedDictionary<string, int> LochNachMaterial =
        new SortedDictionary<string, int>();

    private static int Band(float2 punkt, float2[] site)
    {
        if (site == null || site.Length < 3) return 3;
        var min = double.PositiveInfinity;
        for (var i = 0; i < site.Length; i++)
            min = Math.Min(min, AbstandPunktStrecke(
                punkt, site[i], site[(i + 1) % site.Length]));
        if (min <= 1.0) return 0;
        if (min <= 6.9) return 1;
        if (min <= 13.9) return 2;
        return 3;
    }

    /**
     * EINE EINZELNE FORM, und was CS2 an ihr verwirft.
     *
     * Die Sammelzahl aus `--flaechen` sagt "24 Stueck" - fuer den Umbau muss
     * man wissen, WAS fuer Gebilde das sind.
     */
    internal static int VerbindungAlle;

    private static int RunVerworfen(string polygon, bool kappen = true)
    {
        var site = Punkte(polygon);
        var einstellungen = LayoutSettings.Cs2;
        einstellungen.Qk = kappen;
        // "Verbindung alle N Buchten" wird im UI so umgerechnet:
        // Cr = (N + 2) * Buchtbreite.
        // Wie im UI: die +2 sind die beiden Kappen und entfallen ohne sie.
        if (VerbindungAlle > 0)
            einstellungen.Cr = (VerbindungAlle + (kappen ? 2 : 0)) * 3.0;
        einstellungen.AngleMode = "edge";
        einstellungen.Auto = false;
        einstellungen.Zellen = true;
        einstellungen.Entrances = new[] { new Entrance { Edge = 0, Along = 20 } };

        var layout = ParkingGeometry.Build(site, einstellungen);
        Console.WriteLine($"FORM {Math.Abs(FlaechenmassRing(site)):F0} m2, "
            + $"{site.Length} Ecken, {layout.Stalls} Buchten | Kappen "
            + (kappen ? "AN" : "AUS")
            + $" | Querstrassen {layout.CrossRouteLine?.Length ?? 0}"
            + $" | Fahrgassen {layout.AisleLine?.Length ?? 0}");
        Console.WriteLine();

        BuchtenJeAbschnitt(layout, site);

        foreach (var paar in new[]
                 {
                     (Ringe: layout.AsphaltSurface, Art: "Belag"),
                     (Ringe: layout.GrassSurface, Art: "Gras"),
                 })
        {
            if (paar.Ringe == null) continue;
            var schlecht = 0;
            var geheilt = 0;
            var echterSplitter = 0;
            Console.WriteLine($"  {paar.Art}: {paar.Ringe.Length} Ringe");
            for (var i = 0; i < paar.Ringe.Length; i++)
            {
                if (Cs2Triangulierung.Dreiecke(paar.Ringe[i]) != 0) continue;
                schlecht++;
                var m = Vermessen(paar.Ringe[i]);
                if (m == null) continue;
                // Beim ERSTEN verworfenen Ring die Punkte selbst zeigen -
                // Kennzahlen sagen "Kante 0,000", aber erst die Koordinaten
                // sagen, ob das ein Doppelpunkt ist oder ein echter Splitter.
                if (schlecht == 1)
                {
                    Console.WriteLine($"    Punkte des ersten verworfenen Rings:");
                    var r = paar.Ringe[i];
                    for (var k = 0; k < r.Length; k++)
                    {
                        var vor = r[(k + r.Length - 1) % r.Length];
                        Console.WriteLine($"      [{k}] {r[k].x:F4} / {r[k].y:F4}"
                            + $"   Abstand zum vorigen "
                            + $"{math.distance(vor, r[k]):F6} m");
                    }
                }
                // GEGENPROBE ZU CODEX: wenn die Haarkappe der Grund ist,
                // muss der Ring nach ihrem Entfernen angenommen werden.
                // Entfernt wird NUR in einer Kopie - das ist eine Messung,
                // kein Fix.
                var ohne = OhneHaarkappen(paar.Ringe[i]);
                var heiltDurchKappen = ohne.Length != paar.Ringe[i].Length
                    && Cs2Triangulierung.Dreiecke(ohne) != 0;
                if (heiltDurchKappen) geheilt++; else echterSplitter++;
                if (schlecht <= 12)
                    Console.WriteLine($"    VERWORFEN #{i,-4} "
                        + $"{paar.Ringe[i].Length,3} Punkte | "
                        + $"{m.Breite,8:F2} x {m.Laenge,8:F2} m | "
                        + $"{m.Flaeche,10:F1} m2 | Kante {m.MinKante,7:F3} | "
                        + $"Hals {m.MinHals,7:F3} | Winkel {m.MinWinkel,5:F1} | "
                        + BandName(Band(m.DuennsteStelle, site)));
            }
            Console.WriteLine($"    -> {schlecht} von {paar.Ringe.Length} verworfen"
                + $" | davon durch Entfernen der Haarkappe geheilt {geheilt}, "
                + $"echter Splitter {echterSplitter}");
            Console.WriteLine();
        }
        return 0;
    }

    /**
     * Entfernt Punkte, die weniger als 8 lokale float-Schritte von ihrem
     * Vorgaenger entfernt sind. NUR fuer die Messung - der Ring des Modells
     * bleibt unangetastet.
     */
    private static float2[] OhneHaarkappen(float2[] ring)
    {
        var raus = new System.Collections.Generic.List<float2>(ring.Length);
        for (var i = 0; i < ring.Length; i++)
        {
            var vor = ring[(i + ring.Length - 1) % ring.Length];
            // Lokales ULP an dieser Weltkoordinate.
            var betrag = Math.Max(Math.Abs(ring[i].x), Math.Abs(ring[i].y));
            var ulp = Math.Max(betrag * 1.1920929e-7f, float.Epsilon);
            if (math.distance(vor, ring[i]) > 8 * ulp) raus.Add(ring[i]);
        }
        return raus.Count >= 3 ? raus.ToArray() : ring;
    }

    /**
     * WIEVIELE BUCHTEN STEHEN ZWISCHEN ZWEI QUERSTRASSEN?
     *
     * Das ist die Zahl, die der Regler "Verbindung alle N Buchten" verspricht.
     * Der Nutzer hat am 2026-08-24 nachgezaehlt und 7 statt 5 gefunden; seither
     * wird sie gemessen statt aus der Abstandsrechnung geschlossen.
     *
     * Genommen wird die am dichtesten besetzte Buchtreihe - die liegt im
     * Inneren und ist von Randeffekten am wenigsten gestoert.
     */
    private static void BuchtenJeAbschnitt(ParkingLayout layout, float2[] site)
    {
        if (layout.Bay == null || layout.Bay.Length == 0) return;
        /*
         * NUR INNERE REIHEN.
         *
         * Der erste Versuch nahm die dichteste Reihe ueberhaupt - und das war
         * die RANDBUCHTREIHE. Die legt `Randreihe.cs`, nicht der Spaltenplan;
         * sie kennt weder Kappen noch Abschnitte und hat bei 24 m
         * Querstrassenabstand schlicht 8 Buchten. Daraus habe ich am
         * 2026-08-24 geschlossen, der Regler werde nicht eingehalten - er
         * wurde, nur an einer Reihe gemessen, fuer die er gar nicht gilt.
         */
        /*
         * IM REIHENRAHMEN MESSEN, NICHT IN WELTKOORDINATEN.
         *
         * Die Reihen laufen im Reihenwinkel, nicht waagerecht. Bei der Form
         * des Nutzers (93,32 Grad) fand die Gruppierung nach Welt-y deshalb
         * Reihen aus zwei Buchten und Querstrassen, deren x alle
         * uebereinanderlagen. Erst zurueckdrehen, dann gruppieren.
         */
        var winkel = -layout.Angle * Math.PI / 180.0;
        var cos = (float)Math.Cos(winkel);
        var sin = (float)Math.Sin(winkel);
        float2 Dreh(float2 v) => new float2(v.x * cos - v.y * sin,
                                            v.x * sin + v.y * cos);

        var mitten = layout.Bay
            .Where(b => b != null && b.Length >= 3)
            .Select(b => new float2(b.Average(p => p.x), b.Average(p => p.y)))
            .Where(m => Band(m, site) == 3)
            .Select(Dreh)
            .ToArray();
        if (mitten.Length == 0) return;

        // Reihen ueber die y-Mitte gruppieren, halbmetergenau.
        var reihe = mitten.GroupBy(m => Math.Round(m.y * 2) / 2)
            .OrderByDescending(g => g.Count())
            .First()
            .OrderBy(m => m.x)
            .Select(m => m.x)
            .ToArray();

        var strassen = (layout.CrossRouteLine ?? Array.Empty<float2[]>())
            .Where(l => l != null && l.Length > 0)
            .Select(l => Dreh(new float2(l.Average(p => p.x),
                                         l.Average(p => p.y))).x)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        var abschnitte = new List<int>();
        var index = 0;
        foreach (var grenze in strassen)
        {
            var zahl = 0;
            while (index < reihe.Length && reihe[index] < grenze) { zahl++; index++; }
            abschnitte.Add(zahl);
        }
        abschnitte.Add(reihe.Length - index);

        Console.WriteLine($"  [Pruefung] Reihe bei y={mitten.GroupBy(m => Math.Round(m.y * 2) / 2).OrderByDescending(g => g.Count()).First().Key:F1}, "
            + $"{reihe.Length} Buchten, Spanne x {reihe.First():F1} bis {reihe.Last():F1}; "
            + $"Querstrassen-x: {string.Join(", ", strassen.Select(x => x.ToString("F1")))}");
        // WIE LANG IST DER ABSCHNITT WIRKLICH? Die Fahrgasse laeuft ueber die
        // ganze nutzbare Laenge der Reihe - ihre Ausdehnung im Reihenrahmen
        // ist also das Mass, gegen das die gesetzten Buchten zu halten sind.
        var gassen = (layout.AisleLine ?? Array.Empty<float2[]>())
            .Where(l => l != null && l.Length > 0)
            .SelectMany(l => l.Select(Dreh))
            .ToArray();
        if (gassen.Length > 0)
            Console.WriteLine($"  Fahrgassen im Reihenrahmen: x {gassen.Min(g => g.x):F1} "
                + $"bis {gassen.Max(g => g.x):F1} "
                + $"(Laenge {gassen.Max(g => g.x) - gassen.Min(g => g.x):F1} m)");
        Console.WriteLine($"  Buchten belegen: x {reihe.First() - 1.5:F1} bis "
            + $"{reihe.Last() + 1.5:F1} (Laenge {reihe.Last() - reihe.First() + 3:F1} m)");

        // Die Rasterlage je Bucht: liegen alle auf demselben 3-m-Raster?
        Console.WriteLine("  Buchtmitten im Reihenrahmen (x, und x mod 3):");
        Console.WriteLine("    " + string.Join("  ", reihe.Take(16)
            .Select(x => $"{x:F1}({(((x % 3) + 3) % 3):F2})")));
        Console.WriteLine($"  Buchten je Abschnitt in der dichtesten Reihe "
            + $"({strassen.Length} Querstrassen): "
            + string.Join(" | ", abschnitte));
        var innen = abschnitte.Skip(1).Take(Math.Max(0, abschnitte.Count - 2)).ToArray();
        if (innen.Length > 0)
            Console.WriteLine($"    innere Abschnitte: min {innen.Min()}, "
                + $"max {innen.Max()}, Mittel {innen.Average():F1}");
        Console.WriteLine();
    }

    private static string BandName(int band)
    {
        switch (band)
        {
            case 0: return "Randstreifen";
            case 1: return "Randbuchtreihe";
            case 2: return "Randstrasse";
            default: return "Inneres";
        }
    }

    private static double FlaechenmassRing(float2[] polygon)
    {
        if (polygon == null || polygon.Length < 3) return 0;
        var summe = 0.0;
        for (var i = 0; i < polygon.Length; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Length];
            summe += (double)a.x * b.y - (double)b.x * a.y;
        }
        return summe / 2;
    }

    /**
     * Kuerzeste Kante, spitzester Winkel und engster Hals eines Rings.
     *
     * Der HALS ist das eigentliche Ausschlusskriterium von CS2 und lässt sich
     * nicht aus den Kantenlaengen ablesen: eine Flaeche kann lauter lange
     * Kanten haben und sich trotzdem irgendwo auf 0,001 m einschnueren, weil
     * zwei nicht benachbarte Kanten aneinander vorbeilaufen. Gemessen wird er
     * deshalb als kleinster Abstand jedes Punktes zu jeder Kante, die ihn
     * nicht selbst beruehrt.
     */
    private static Ringmass Vermessen(float2[] ring)
    {
        if (ring == null) return null;
        // Ein doppelter Schlusspunkt gehoert zur Uebergabe an CS2, nicht zur
        // Form - er wuerde sonst als Kante der Laenge 0 gezaehlt.
        var n = ring.Length;
        while (n > 1 && math.all(ring[n - 1] == ring[0])) n--;
        if (n < 3) return null;

        var mass = new Ringmass();
        var min = new float2(float.MaxValue, float.MaxValue);
        var max = new float2(float.MinValue, float.MinValue);
        for (var i = 0; i < n; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % n];
            var c = ring[(i + 2) % n];
            min = math.min(min, a);
            max = math.max(max, a);
            var kante = math.distance(a, b);
            if (kante < mass.MinKante)
            {
                mass.MinKante = kante;
                mass.DuennsteStelle = (a + b) * 0.5f;
            }
            if (kante == 0) mass.Doppelpunkte++;
            for (var k = i + 1; k < n; k++)
                if (k != (i + 1) % n && math.all(ring[k] == a))
                    mass.Selbstberuehrungen++;

            var ba = math.normalizesafe(a - b);
            var bc = math.normalizesafe(c - b);
            var grad = math.degrees(math.acos(
                math.clamp(math.dot(ba, bc), -1f, 1f)));
            if (!double.IsNaN(grad)) mass.MinWinkel = Math.Min(mass.MinWinkel, grad);
        }
        mass.Breite = Math.Min(max.x - min.x, max.y - min.y);
        mass.Laenge = Math.Max(max.x - min.x, max.y - min.y);
        mass.Mitte = (min + max) * 0.5f;
        mass.Flaeche = Math.Abs(FlaechenmassRing(ring.Take(n).ToArray()));

        for (var p = 0; p < n; p++)
        for (var k = 0; k < n; k++)
        {
            // Die beiden Kanten am Punkt selbst haben Abstand 0 - sie sagen
            // nichts ueber eine Einschnuerung aus.
            if (k == p || (k + 1) % n == p) continue;
            var hals = AbstandPunktStrecke(ring[p], ring[k], ring[(k + 1) % n]);
            if (hals >= mass.MinHals) continue;
            mass.MinHals = hals;
            if (hals < mass.MinKante) mass.DuennsteStelle = ring[p];
        }
        return mass;
    }

}
