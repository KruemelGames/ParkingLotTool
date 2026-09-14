using System.Diagnostics;

namespace Zellenversuch;

internal sealed class Rastermessung
{
    internal required long Arealpunkte { get; init; }
    internal required long UngedecktePunkte { get; init; }
    internal required long UeberlapptePunkte { get; init; }
    internal required double Rasterweite { get; init; }
    internal required TimeSpan Dauer { get; init; }

    internal double UngedeckteFlaeche => UngedecktePunkte * Rasterweite * Rasterweite;
    internal double UeberlappteFlaeche => UeberlapptePunkte * Rasterweite * Rasterweite;
    internal double UngedecktProzent =>
        Arealpunkte == 0 ? double.NaN : 100.0 * UngedecktePunkte / Arealpunkte;
}

internal sealed class Flaechenmessung
{
    internal required Flaeche Flaeche { get; init; }
    internal required double KuerzesteKante { get; init; }
    internal required GerichteteKante KuerzesteKanteSegment { get; init; }
    internal required double Engstelle { get; init; }
    internal required Knoten EngstellenKnoten { get; init; }
    internal required GerichteteKante EngstellenKante { get; init; }
    internal required int Selbstkreuzungen { get; init; }
}

internal sealed class Bilanzmessung
{
    internal required double Arealflaeche { get; init; }
    internal required double Teilflaeche { get; init; }
    internal required double ZellflaecheVorZufahrt { get; init; }
    internal required double Zellflaeche { get; init; }
    internal required double AsphaltVorZufahrt { get; init; }
    internal required double AsphaltNachZufahrt { get; init; }
    internal required double GruenVorZufahrt { get; init; }
    internal required double GruenNachZufahrt { get; init; }
    internal required double MaterialflaecheVorTrennung { get; init; }
    internal required double Materialflaeche { get; init; }
    internal required int Quellzellenfehler { get; init; }
    internal required int ZellzuordnungsfehlerVorTrennung { get; init; }
    internal required int ZellzuordnungsfehlerNachTrennung { get; init; }

    internal double Zufahrtsdifferenz => Zellflaeche - ZellflaecheVorZufahrt;
    internal bool ZufahrtsbilanzBitgenau => Zufahrtsdifferenz == 0;
    internal bool ZufahrtsbilanzKombinatorischExakt => Quellzellenfehler == 0;
    internal double Trenndifferenz => Materialflaeche - MaterialflaecheVorTrennung;
    internal bool TrennbilanzBitgenau => Trenndifferenz == 0;
    internal bool TrennbilanzKombinatorischExakt =>
        ZellzuordnungsfehlerVorTrennung == 0 && ZellzuordnungsfehlerNachTrennung == 0;
}

internal sealed class Strukturmessung
{
    internal required int Randseiten { get; init; }
    internal required int RandseitenMitRandband { get; init; }
    internal required int NichtRandzellenAnAussenkante { get; init; }
    internal required int ZufahrtszellenAnAussenkante { get; init; }
    internal required int Randbandkomponenten { get; init; }
    internal required int Randbandinseln { get; init; }
    internal required int Querstrassen { get; init; }
    internal required int QuerstrassenMitBeidenKantenIds { get; init; }
    internal required int QuerstrassenZellfehler { get; init; }
    internal required IReadOnlyList<int> BaenderJeQuerstrasse { get; init; }
}

internal static class Messung
{
    internal const double Rasterweite = 0.1;
    internal const double Cs2Mindestkante = Layoutbauer.Cs2Mindestkante;

    internal static Rastermessung Raster(Bauergebnis bau)
    {
        var uhr = Stopwatch.StartNew();
        var minX = bau.Form.Punkte.Min(punkt => punkt.X);
        var maxX = bau.Form.Punkte.Max(punkt => punkt.X);
        var minY = bau.Form.Punkte.Min(punkt => punkt.Y);
        var maxY = bau.Form.Punkte.Max(punkt => punkt.Y);
        var ersteSpalte = (long)Math.Floor(minX / Rasterweite);
        var letzteSpalte = (long)Math.Ceiling(maxX / Rasterweite) - 1;
        var ersteZeile = (long)Math.Floor(minY / Rasterweite);
        var letzteZeile = (long)Math.Ceiling(maxY / Rasterweite) - 1;
        var arealring = bau.ArealLokal.Punkte.ToArray();
        long arealpunkte = 0;
        long ungedeckt = 0;
        long ueberlappt = 0;
        var flaechenMitRahmen = bau.Flaechen.Select(flaeche =>
        {
            var punkte = flaeche.Aussenring.Knoten.Select(knoten => knoten.Punkt).ToArray();
            return (Flaeche: flaeche,
                MinX: punkte.Min(punkt => punkt.X),
                MaxX: punkte.Max(punkt => punkt.X),
                MinY: punkte.Min(punkt => punkt.Y),
                MaxY: punkte.Max(punkt => punkt.Y));
        }).ToArray();

        for (var zeile = ersteZeile; zeile <= letzteZeile; zeile++)
        {
            var weltY = (zeile + 0.5) * Rasterweite;
            for (var spalte = ersteSpalte; spalte <= letzteSpalte; spalte++)
            {
                var welt = new Punkt((spalte + 0.5) * Rasterweite, weltY);
                var lokal = bau.Rahmen.NachLokal(welt);
                if (!Geometrie.Enthaelt(arealring, lokal)) continue;
                arealpunkte++;
                var treffer = 0;
                foreach (var kandidat in flaechenMitRahmen)
                {
                    if (lokal.X < kandidat.MinX || lokal.X > kandidat.MaxX
                        || lokal.Y < kandidat.MinY || lokal.Y > kandidat.MaxY)
                        continue;
                    if (Geometrie.Enthaelt(kandidat.Flaeche, lokal)) treffer++;
                }
                if (treffer == 0) ungedeckt++;
                if (treffer > 1) ueberlappt++;
            }
        }

        uhr.Stop();
        return new Rastermessung
        {
            Arealpunkte = arealpunkte,
            UngedecktePunkte = ungedeckt,
            UeberlapptePunkte = ueberlappt,
            Rasterweite = Rasterweite,
            Dauer = uhr.Elapsed,
        };
    }

    internal static Bilanzmessung Bilanz(Bauergebnis bau) => new()
    {
        Arealflaeche = Geometrie.Flaeche(bau.ArealLokal),
        Teilflaeche = bau.KonvexeTeile.Sum(Geometrie.Flaeche),
        ZellflaecheVorZufahrt = bau.ZellenVorZufahrt.Sum(
            zelle => Geometrie.Flaeche(zelle.Polygon)),
        Zellflaeche = bau.Zellen.Sum(zelle => Geometrie.Flaeche(zelle.Polygon)),
        AsphaltVorZufahrt = Materialzellenflaeche(
            bau.ZellenVorZufahrt, Material.Asphalt),
        AsphaltNachZufahrt = Materialzellenflaeche(bau.Zellen, Material.Asphalt),
        GruenVorZufahrt = Materialzellenflaeche(
            bau.ZellenVorZufahrt, Material.Gruen),
        GruenNachZufahrt = Materialzellenflaeche(bau.Zellen, Material.Gruen),
        MaterialflaecheVorTrennung = bau.FlaechenVorTrennung.Sum(
            flaeche => flaeche.Flaecheninhalt),
        Materialflaeche = bau.Flaechen.Sum(flaeche => flaeche.Flaecheninhalt),
        Quellzellenfehler = Quellzellenfehler(bau.ZellenVorZufahrt, bau.Zellen),
        ZellzuordnungsfehlerVorTrennung = Zellzuordnungsfehler(
            bau.Zellen, bau.FlaechenVorTrennung),
        ZellzuordnungsfehlerNachTrennung = Zellzuordnungsfehler(
            bau.Zellen, bau.Flaechen),
    };

    internal static Strukturmessung Struktur(Bauergebnis bau)
    {
        var aussenlinien = bau.ArealLokal.Ecken
            .Select(ecke => ecke.LinieBisNaechste.Id)
            .ToHashSet();
        var randseiten = new HashSet<int>();
        var falscheRandzellen = 0;
        var zufahrtsrandzellen = 0;
        foreach (var zelle in bau.Zellen)
            for (var i = 0; i < zelle.Polygon.Anzahl; i++)
            {
                var linie = zelle.Polygon.Linie(i);
                if (!aussenlinien.Contains(linie.Id)) continue;
                if (zelle.Art == Zellart.Randband)
                    randseiten.Add(linie.Id);
                else if (zelle.Art == Zellart.Zufahrt)
                    zufahrtsrandzellen++;
                else
                    falscheRandzellen++;
            }

        var benutzteLinien = bau.Zellen
            .SelectMany(zelle => zelle.Polygon.Ecken)
            .Select(ecke => ecke.LinieBisNaechste.Id)
            .ToHashSet();
        var vollstaendigeQuerstrassen = bau.Spaltenplan.Querstrassen.Count(strasse =>
            benutzteLinien.Contains(strasse.LinkeKante.Id)
            && benutzteLinien.Contains(strasse.RechteKante.Id));
        var querfehler = 0;
        foreach (var zelle in bau.Zellen.Where(zelle => zelle.Art == Zellart.Querstrasse))
        {
            if (!zelle.QuerstrassenId.HasValue
                || zelle.QuerstrassenId.Value < 0
                || zelle.QuerstrassenId.Value >= bau.Spaltenplan.Querstrassen.Count)
            {
                querfehler++;
                continue;
            }
            var strasse = bau.Spaltenplan.Querstrassen[zelle.QuerstrassenId.Value];
            if (zelle.Polygon.Punkte.Any(punkt =>
                    punkt.X < strasse.Anfang || punkt.X > strasse.Ende))
                querfehler++;
            if (zelle.Polygon.Ecken.Any(ecke =>
                    ecke.LinieBisNaechste.Art == Linienart.RasterX
                    && ecke.Knoten.Punkt.X > strasse.Anfang
                    && ecke.Knoten.Punkt.X < strasse.Ende))
                querfehler++;
        }
        querfehler += bau.Zellen.Count(zelle =>
            zelle.Art != Zellart.Querstrasse && zelle.QuerstrassenId.HasValue);
        foreach (var zelle in bau.Zellen.Where(zelle =>
                     zelle.Art is not Zellart.Randband and not Zellart.Zufahrt))
        {
            var x = Geometrie.Mittelwert(zelle.Polygon).X;
            if (bau.Spaltenplan.Querstrassen.Any(strasse =>
                    x >= strasse.Anfang && x < strasse.Ende)
                && zelle.Art != Zellart.Querstrasse)
                querfehler++;
        }

        var baenderJeQuerstrasse = bau.Spaltenplan.Querstrassen
            .Select(strasse => bau.Zellen
                .Where(zelle => zelle.QuerstrassenId == strasse.Id)
                .Select(zelle => bau.Bandplan.Bei(Geometrie.Mittelwert(zelle.Polygon).Y).Id)
                .Distinct()
                .Count())
            .ToArray();

        var randband = Randbandkomponenten(bau.Zellen);
        return new Strukturmessung
        {
            Randseiten = aussenlinien.Count,
            RandseitenMitRandband = randseiten.Count,
            NichtRandzellenAnAussenkante = falscheRandzellen,
            ZufahrtszellenAnAussenkante = zufahrtsrandzellen,
            Randbandkomponenten = randband.Komponenten,
            Randbandinseln = randband.Inseln,
            Querstrassen = bau.Spaltenplan.Querstrassen.Count,
            QuerstrassenMitBeidenKantenIds = vollstaendigeQuerstrassen,
            QuerstrassenZellfehler = querfehler,
            BaenderJeQuerstrasse = baenderJeQuerstrasse,
        };
    }

    internal static List<Lochlage> Lochlagen(Bauergebnis bau)
    {
        var ausgabe = new List<Lochlage>();
        foreach (var flaeche in bau.FlaechenVorTrennung)
            foreach (var loch in flaeche.Loecher)
            {
                var weltpunkte = loch.Knoten
                    .Select(knoten => bau.Rahmen.NachWelt(knoten.Punkt))
                    .ToArray();
                ausgabe.Add(new Lochlage(
                    flaeche.Material,
                    bau.Rahmen.NachWelt(Geometrie.Schwerpunkt(loch)),
                    weltpunkte.Min(punkt => punkt.X),
                    weltpunkte.Max(punkt => punkt.X),
                    weltpunkte.Min(punkt => punkt.Y),
                    weltpunkte.Max(punkt => punkt.Y)));
            }
        return ausgabe;
    }

    internal static List<Flaechenmessung> Flaechen(Bauergebnis bau) =>
        bau.Flaechen.Select(flaeche =>
        {
            var kuerzeste = KuerzesteKante(flaeche);
            var engste = EngsteStelle(flaeche);
            return new Flaechenmessung
            {
                Flaeche = flaeche,
                KuerzesteKante = kuerzeste.Laenge,
                KuerzesteKanteSegment = kuerzeste.Kante,
                Engstelle = engste.Abstand,
                EngstellenKnoten = engste.Knoten,
                EngstellenKante = engste.Kante,
                Selbstkreuzungen = ZaehleSelbstkreuzungen(flaeche),
            };
        }).ToList();

    internal static double MedianBauzeitMillisekunden(
        Formdefinition form,
        IReadOnlyList<Zufahrtsvorgabe> zufahrten,
        int wiederholungen)
    {
        var werte = new double[wiederholungen];
        for (var i = 0; i < wiederholungen; i++)
        {
            var uhr = Stopwatch.StartNew();
            var gebaut = Layoutbauer.Baue(form, zufahrten);
            uhr.Stop();
            GC.KeepAlive(gebaut);
            werte[i] = uhr.Elapsed.TotalMilliseconds;
        }
        Array.Sort(werte);
        return wiederholungen % 2 == 1
            ? werte[wiederholungen / 2]
            : (werte[wiederholungen / 2 - 1] + werte[wiederholungen / 2]) / 2;
    }

    internal static void WaermeZeitmessung(
        IReadOnlyList<(Formdefinition Form, IReadOnlyList<Zufahrtsvorgabe> Zufahrten)> faelle,
        int runden)
    {
        // Alle Formen waermen gemeinsam auf. Sonst lief im ersten Messlauf
        // noch Tier-0-Code: gemessen 3,340 ms fuer das Rechteck, waehrend die
        // spaeter kompilierten Formen schon unter 1 ms lagen.
        for (var runde = 0; runde < runden; runde++)
            foreach (var fall in faelle)
                try
                {
                    GC.KeepAlive(Layoutbauer.Baue(fall.Form, fall.Zufahrten));
                }
                catch (Exception fehler)
                {
                    throw new InvalidOperationException(
                        $"Aufwaermlauf fuer {fall.Form.Name} scheiterte.", fehler);
                }
    }

    private static (double Laenge, GerichteteKante Kante) KuerzesteKante(Flaeche flaeche)
    {
        var minimum = double.PositiveInfinity;
        var kuerzeste = default(GerichteteKante);
        foreach (var ring in flaeche.AlleRinge)
            foreach (var kante in ring.Kanten)
            {
                var laenge = Geometrie.Laenge(kante.Nach.Punkt - kante.Von.Punkt);
                if (laenge >= minimum) continue;
                minimum = laenge;
                kuerzeste = kante;
            }
        return (minimum, kuerzeste);
    }

    /// <summary>
    /// Dieselbe Art Engstelle, die die Reparaturfrage motiviert: kleinster
    /// Abstand eines Ringknotens zu einer nicht an ihm haengenden Ringkante.
    /// Bei Loechern werden auch Aussen-/Innenring gegeneinander gemessen.
    /// </summary>
    private static (double Abstand, Knoten Knoten, GerichteteKante Kante) EngsteStelle(
        Flaeche flaeche)
    {
        var ringe = flaeche.AlleRinge.ToList();
        var minimum = double.PositiveInfinity;
        var engknoten = default(Knoten)!;
        var engkante = default(GerichteteKante);
        foreach (var punktring in ringe)
            foreach (var knoten in punktring.Knoten)
                foreach (var kantenring in ringe)
                    foreach (var kante in kantenring.Kanten)
                    {
                        if (kante.Von.Id == knoten.Id || kante.Nach.Id == knoten.Id) continue;
                        var abstand = Geometrie.AbstandPunktStrecke(
                            knoten.Punkt, kante.Von.Punkt, kante.Nach.Punkt);
                        if (abstand >= minimum) continue;
                        minimum = abstand;
                        engknoten = knoten;
                        engkante = kante;
                    }
        return (minimum, engknoten, engkante);
    }

    private static int ZaehleSelbstkreuzungen(Flaeche flaeche)
    {
        var kanten = flaeche.AlleRinge.SelectMany(ring => ring.Kanten).ToList();
        var anzahl = 0;
        for (var i = 0; i < kanten.Count; i++)
            for (var j = i + 1; j < kanten.Count; j++)
            {
                var a = kanten[i];
                var b = kanten[j];
                if (a.Von.Id == b.Von.Id || a.Von.Id == b.Nach.Id
                    || a.Nach.Id == b.Von.Id || a.Nach.Id == b.Nach.Id)
                    continue;
                if (KreuzenSichEcht(a.Von.Punkt, a.Nach.Punkt, b.Von.Punkt, b.Nach.Punkt))
                    anzahl++;
            }
        return anzahl;
    }

    private static bool KreuzenSichEcht(Punkt a, Punkt b, Punkt c, Punkt d)
    {
        var erste = b - a;
        var zweite = d - c;
        var nenner = Geometrie.Kreuz(erste, zweite);
        if (nenner == 0) return false;
        var delta = c - a;
        var t = Geometrie.Kreuz(delta, zweite) / nenner;
        var u = Geometrie.Kreuz(delta, erste) / nenner;
        return t > 0 && t < 1 && u > 0 && u < 1;
    }

    private static int Zellzuordnungsfehler(
        IReadOnlyList<Zelle> zellen,
        IReadOnlyList<Flaeche> flaechen)
    {
        var haeufigkeit = new int[zellen.Count];
        foreach (var id in flaechen.SelectMany(flaeche => flaeche.ZellIds))
        {
            if (id < 0 || id >= haeufigkeit.Length) return 1;
            haeufigkeit[id]++;
        }
        return haeufigkeit.Count(wert => wert != 1);
    }

    private static double Materialzellenflaeche(
        IEnumerable<Zelle> zellen,
        Material material) => zellen
        .Where(zelle => zelle.Material == material)
        .Sum(zelle => Geometrie.Flaeche(zelle.Polygon));

    private static int Quellzellenfehler(
        IReadOnlyList<Zelle> vorher,
        IReadOnlyList<Zelle> nachher)
    {
        var vorhandene = vorher.Select(zelle => zelle.Id).ToHashSet();
        var vertretene = nachher.Select(zelle => zelle.QuellzelleId).ToHashSet();
        return vorhandene.Count(id => !vertretene.Contains(id))
            + vertretene.Count(id => !vorhandene.Contains(id));
    }

    private static (int Komponenten, int Inseln) Randbandkomponenten(
        IReadOnlyList<Zelle> zellen)
    {
        var ids = zellen
            .Where(zelle => zelle.Ursprungsart == Zellart.Randband
                && zelle.Material == Material.Gruen)
            .Select(zelle => zelle.Id)
            .ToHashSet();
        if (ids.Count == 0) return (0, 0);

        var besitz = new Dictionary<KantenSchluessel, List<int>>();
        foreach (var id in ids)
            for (var i = 0; i < zellen[id].Polygon.Anzahl; i++)
            {
                var schluessel = KantenSchluessel.Von(
                    zellen[id].Polygon.Knoten(i), zellen[id].Polygon.Knoten(i + 1));
                if (!besitz.TryGetValue(schluessel, out var liste))
                    besitz.Add(schluessel, liste = new List<int>());
                liste.Add(id);
            }
        var nachbarn = ids.ToDictionary(id => id, _ => new HashSet<int>());
        foreach (var liste in besitz.Values.Where(liste => liste.Count == 2))
        {
            nachbarn[liste[0]].Add(liste[1]);
            nachbarn[liste[1]].Add(liste[0]);
        }

        var offen = ids.ToHashSet();
        var komponenten = 0;
        var inseln = 0;
        while (offen.Count != 0)
        {
            komponenten++;
            var start = offen.First();
            var warteschlange = new Queue<int>();
            warteschlange.Enqueue(start);
            offen.Remove(start);
            var beruehrtAussen = false;
            while (warteschlange.Count != 0)
            {
                var id = warteschlange.Dequeue();
                beruehrtAussen |= zellen[id].Polygon.Ecken.Any(ecke =>
                    ecke.LinieBisNaechste.Art == Linienart.Aussenkante);
                foreach (var nachbar in nachbarn[id])
                    if (offen.Remove(nachbar)) warteschlange.Enqueue(nachbar);
            }
            if (!beruehrtAussen) inseln++;
        }
        return (komponenten, inseln);
    }
}
