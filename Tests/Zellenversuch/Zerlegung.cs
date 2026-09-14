namespace Zellenversuch;

internal sealed class Knotenfabrik
{
    private int _naechsteId;

    internal Knoten Neu(Punkt punkt) => new(_naechsteId++, punkt);
    internal int Anzahl => _naechsteId;
}

internal sealed class Linienregister
{
    private int _naechsteId;

    internal Linie Aussenkante(int index) =>
        new(_naechsteId++, Linienart.Aussenkante, $"Aussenkante {index}");

    internal Linie Teilungsnaht(int index) =>
        new(_naechsteId++, Linienart.Teilungsnaht, $"Teilungsnaht {index}");

    internal Linie RasterX(double x) =>
        new(_naechsteId++, Linienart.RasterX, $"x={x:R}", 1, 0, x, Schnittachse.X, x);

    internal Linie BandY(double y) =>
        new(_naechsteId++, Linienart.BandY, $"y={y:R}", 0, 1, y, Schnittachse.Y, y);

    internal Linie Querstrassenkante(double x, int strasse, string seite) =>
        new(_naechsteId++, Linienart.Querstrassenkante,
            $"Querstrasse {strasse} {seite} x={x:R}", 1, 0, x, Schnittachse.X, x);

    internal Linie Innenrand(Punkt a, Punkt b, int index)
        => GeradeDurch(a, b, Linienart.Innenrand, $"Innenrand {index}");

    internal Linie Randstrassenkante(Punkt a, Punkt b, int index)
        => GeradeDurch(a, b, Linienart.Randstrassenkante,
            $"Randstrassenkante {index}");

    internal Linie Zufahrtskante(
        Punkt punkt,
        Punkt richtung,
        int zufahrt,
        string seite)
        => GeradeDurch(punkt, punkt + richtung, Linienart.Zufahrtskante,
            $"Zufahrt {zufahrt} {seite}");

    private Linie GeradeDurch(Punkt a, Punkt b, Linienart art, string name)
    {
        var richtung = b - a;
        if (Geometrie.Skalar(richtung, richtung) == 0)
            throw new InvalidOperationException("Eine Teilungsgerade braucht eine Richtung.");
        var koeffizientA = -richtung.Y;
        var koeffizientB = richtung.X;
        var koeffizientC = koeffizientA * a.X + koeffizientB * a.Y;
        var achse = richtung.X == 0
            ? Schnittachse.X
            : richtung.Y == 0
                ? Schnittachse.Y
                : Schnittachse.Keine;
        var achsenwert = achse == Schnittachse.X ? a.X : achse == Schnittachse.Y ? a.Y : 0;
        return new Linie(_naechsteId++, art, name,
            koeffizientA, koeffizientB, koeffizientC, achse, achsenwert);
    }
}

internal static class Polygonfabrik
{
    internal static Polygon Areal(
        IReadOnlyList<Punkt> punkte,
        Knotenfabrik knotenfabrik,
        Linienregister linienregister)
    {
        var knoten = punkte.Select(knotenfabrik.Neu).ToArray();
        var ecken = new List<Ecke>();
        for (var i = 0; i < knoten.Length; i++)
            ecken.Add(new Ecke(knoten[i], linienregister.Aussenkante(i)));
        var polygon = new Polygon(ecken);
        if (Geometrie.Vorzeichenflaeche(polygon.Punkte) <= 0)
            throw new InvalidOperationException("Das normalisierte Areal ist nicht CCW.");
        return polygon;
    }

    internal static Polygon AusKnotenUndLinien(
        IReadOnlyList<Knoten> knoten,
        IReadOnlyList<Linie> linien)
    {
        if (knoten.Count != linien.Count)
            throw new InvalidOperationException("Knoten- und Linienzahl stimmen nicht ueberein.");
        var polygon = new Polygon(knoten.Select((knotenwert, index) =>
            new Ecke(knotenwert, linien[index])));
        if (Geometrie.Vorzeichenflaeche(polygon.Punkte) <= 0)
            throw new InvalidOperationException("Eine Teilflaeche ist nicht CCW.");
        return polygon;
    }
}

/// <summary>
/// Teilt den ersten gefundenen Reflexwinkel, indem die ankommende Kante als
/// Strahl bis zur naechsten nicht benachbarten Kante verlaengert wird. Das ist
/// dieselbe geometrische Idee wie im Mod, aber eine eigenstaendige Umsetzung
/// mit gemeinsam benutzten Knoten an der neuen Naht.
/// </summary>
internal sealed class Konvexzerlegung
{
    private readonly Knotenfabrik _knotenfabrik;
    private readonly Linienregister _linienregister;
    private int _nahtindex;

    internal Konvexzerlegung(Knotenfabrik knotenfabrik, Linienregister linienregister)
    {
        _knotenfabrik = knotenfabrik;
        _linienregister = linienregister;
    }

    internal List<Polygon> Zerlege(Polygon polygon)
    {
        for (var reflex = 0; reflex < polygon.Anzahl; reflex++)
        {
            if (!Geometrie.IstReflex(polygon, reflex)) continue;
            var treffer = NaechsterTreffer(polygon, reflex);
            if (treffer is null)
                throw new InvalidOperationException($"Reflexecke {reflex} konnte nicht geteilt werden.");

            var (kantenindex, parameter, punkt) = treffer.Value;
            var q = polygon.Knoten(kantenindex);
            var r = polygon.Knoten(kantenindex + 1);
            // Ein Treffer auf einem vorhandenen Knoten bleibt genau dieser
            // Knoten. Fuer die fuenf Messformen liegen alle Treffer im
            // Kanteninneren; der Zweig kostet daher keine Sondertoleranz.
            var schnittknoten = parameter == 0
                ? q
                : parameter == 1
                    ? r
                    : _knotenfabrik.Neu(punkt);
            if (schnittknoten.Id == q.Id || schnittknoten.Id == r.Id)
                throw new InvalidOperationException(
                    "Der Zellenversuch unterstuetzt fuer diese Studie keine Teilung durch einen Alt-Knoten.");

            var naht = _linienregister.Teilungsnaht(_nahtindex++);
            var erstes = ErstesTeil(polygon, reflex, kantenindex, schnittknoten, naht);
            var zweites = ZweitesTeil(polygon, reflex, kantenindex, schnittknoten, naht);
            var ergebnis = Zerlege(erstes);
            ergebnis.AddRange(Zerlege(zweites));
            return ergebnis;
        }

        if (!Geometrie.IstKonvex(polygon))
            throw new InvalidOperationException("Die Zerlegung endete mit einem nicht konvexen Teil.");
        return new List<Polygon> { polygon };
    }

    private static (int Kantenindex, double Parameter, Punkt Punkt)? NaechsterTreffer(
        Polygon polygon,
        int reflex)
    {
        var a = polygon.Knoten(reflex - 1).Punkt;
        var b = polygon.Knoten(reflex).Punkt;
        var strahl = b - a;
        var besterStrahlparameter = double.PositiveInfinity;
        (int, double, Punkt)? bester = null;

        for (var kante = 0; kante < polygon.Anzahl; kante++)
        {
            if (kante == reflex || Geometrie.Mod(kante + 1, polygon.Anzahl) == reflex)
                continue;
            var q = polygon.Knoten(kante).Punkt;
            var kantenvektor = polygon.Knoten(kante + 1).Punkt - q;
            var nenner = Geometrie.Kreuz(strahl, kantenvektor);
            if (nenner == 0) continue;
            var delta = q - b;
            var strahlparameter = Geometrie.Kreuz(delta, kantenvektor) / nenner;
            var kantenparameter = Geometrie.Kreuz(delta, strahl) / nenner;
            if (strahlparameter <= 0 || kantenparameter < 0 || kantenparameter > 1)
                continue;
            if (strahlparameter >= besterStrahlparameter) continue;
            besterStrahlparameter = strahlparameter;
            bester = (kante, kantenparameter, q + kantenvektor * kantenparameter);
        }
        return bester;
    }

    private static Polygon ErstesTeil(
        Polygon polygon,
        int reflex,
        int trefferkante,
        Knoten treffer,
        Linie naht)
    {
        var knoten = new List<Knoten> { polygon.Knoten(reflex) };
        var linien = new List<Linie>();
        var index = reflex;
        while (index != trefferkante)
        {
            linien.Add(polygon.Linie(index));
            index = Geometrie.Mod(index + 1, polygon.Anzahl);
            knoten.Add(polygon.Knoten(index));
        }
        linien.Add(polygon.Linie(trefferkante));
        knoten.Add(treffer);
        linien.Add(naht);
        return Polygonfabrik.AusKnotenUndLinien(knoten, linien);
    }

    private static Polygon ZweitesTeil(
        Polygon polygon,
        int reflex,
        int trefferkante,
        Knoten treffer,
        Linie naht)
    {
        var knoten = new List<Knoten> { treffer };
        var linien = new List<Linie> { polygon.Linie(trefferkante) };
        var index = Geometrie.Mod(trefferkante + 1, polygon.Anzahl);
        knoten.Add(polygon.Knoten(index));
        while (index != reflex)
        {
            linien.Add(polygon.Linie(index));
            index = Geometrie.Mod(index + 1, polygon.Anzahl);
            knoten.Add(polygon.Knoten(index));
        }
        linien.Add(naht);
        return Polygonfabrik.AusKnotenUndLinien(knoten, linien);
    }
}

internal sealed class Polygonteiler
{
    private readonly Knotenfabrik _knotenfabrik;
    private readonly Dictionary<SchnittSchluessel, Knoten> _schnittpunkte = new();
    private readonly Dictionary<
        (int Quelllinie, int Schnittlinie, int Knoten, int KanteA, int KanteB),
        Beobachtung> _beobachtungen = new();

    private sealed class Beobachtung
    {
        internal required Linie Quelllinie { get; init; }
        internal required Linie Schnittlinie { get; init; }
        internal required Knoten Knoten { get; init; }
        internal required KantenSchluessel Quellkante { get; init; }
        internal int Anfragen { get; set; }
    }

    internal Polygonteiler(Knotenfabrik knotenfabrik)
    {
        _knotenfabrik = knotenfabrik;
    }

    internal IReadOnlyList<Schnittbeobachtung> Schnittbeobachtungen => _beobachtungen.Values
        .Select(wert => new Schnittbeobachtung(
            wert.Quelllinie, wert.Schnittlinie, wert.Knoten,
            wert.Quellkante, wert.Anfragen))
        .ToArray();

    internal IReadOnlyList<Polygon> Teile(Polygon polygon, Linie schnittlinie)
    {
        var hatMinus = false;
        var hatPlus = false;
        foreach (var punkt in polygon.Punkte)
        {
            var seite = schnittlinie.Seite(punkt);
            if (seite < 0) hatMinus = true;
            if (seite > 0) hatPlus = true;
        }
        if (!hatMinus || !hatPlus) return new[] { polygon };
        return new[]
        {
            SchneideHalbebene(polygon, schnittlinie, minusBehalten: true),
            SchneideHalbebene(polygon, schnittlinie, minusBehalten: false),
        };
    }

    internal IReadOnlyList<Polygon> TeileSegment(
        Polygon polygon,
        Linie schnittlinie,
        Punkt segmentanfang,
        Punkt segmentende)
    {
        var richtung = segmentende - segmentanfang;
        var laengenquadrat = Geometrie.Skalar(richtung, richtung);
        if (laengenquadrat == 0)
            throw new InvalidOperationException("Eine Teilungsstrecke braucht Laenge.");
        // Die 6,9-m-Endlinie hat das Grundnetz bereits geteilt. Der Schwerpunkt
        // entscheidet daher rein kombinatorisch, auf welcher Seite dieser
        // vorhandenen Grenze die ganze konvexe Zelle liegt. Eine erneute
        // Gleitkomma-Pruefung des Endpunkts erzeugte im Rechteck gemessen einen
        // ungewollten Schnitt bis y=9,55 statt nur bis y=6,90.
        var mittenparameter = Geometrie.Skalar(
            Geometrie.Mittelwert(polygon) - segmentanfang, richtung) / laengenquadrat;
        if (mittenparameter < 0 || mittenparameter > 1)
            return new[] { polygon };

        var parameter = new List<double>();
        for (var i = 0; i < polygon.Anzahl; i++)
        {
            var a = polygon.Knoten(i).Punkt;
            var b = polygon.Knoten(i + 1).Punkt;
            var seiteA = schnittlinie.Seite(a);
            var seiteB = schnittlinie.Seite(b);
            if (seiteA == 0)
                parameter.Add(Geometrie.Skalar(a - segmentanfang, richtung) / laengenquadrat);
            if ((seiteA < 0 && seiteB > 0) || (seiteA > 0 && seiteB < 0))
            {
                var anteil = seiteA / (seiteA - seiteB);
                var punkt = a + (b - a) * anteil;
                parameter.Add(Geometrie.Skalar(
                    punkt - segmentanfang, richtung) / laengenquadrat);
            }
        }
        if (parameter.Count < 2 || parameter.Max() <= 0 || parameter.Min() >= 1)
            return new[] { polygon };
        return Teile(polygon, schnittlinie);
    }

    private readonly record struct Eingang(Knoten Knoten, Linie EingangsLinie);

    private Polygon SchneideHalbebene(Polygon polygon, Linie schnittlinie, bool minusBehalten)
    {
        var ausgabe = new List<Eingang>();
        for (var i = 0; i < polygon.Anzahl; i++)
        {
            var start = polygon.Knoten(i);
            var ende = polygon.Knoten(i + 1);
            var kantenlinie = polygon.Linie(i);
            var startseite = schnittlinie.Seite(start.Punkt);
            var endseite = schnittlinie.Seite(ende.Punkt);
            var startInnen = minusBehalten ? startseite <= 0 : startseite >= 0;
            var endeInnen = minusBehalten ? endseite <= 0 : endseite >= 0;

            if (startInnen && endeInnen)
            {
                FuegeHinzu(ausgabe, new Eingang(ende, kantenlinie));
            }
            else if (startInnen)
            {
                var schnitt = Schnittpunkt(
                    start, ende, kantenlinie, schnittlinie, startseite, endseite);
                FuegeHinzu(ausgabe, new Eingang(schnitt, kantenlinie));
            }
            else if (endeInnen)
            {
                var schnitt = Schnittpunkt(
                    start, ende, kantenlinie, schnittlinie, startseite, endseite);
                FuegeHinzu(ausgabe, new Eingang(schnitt, schnittlinie));
                FuegeHinzu(ausgabe, new Eingang(ende, kantenlinie));
            }
        }

        if (ausgabe.Count > 1 && ausgabe[0].Knoten.Id == ausgabe[^1].Knoten.Id)
        {
            ausgabe[0] = ausgabe[0] with { EingangsLinie = ausgabe[^1].EingangsLinie };
            ausgabe.RemoveAt(ausgabe.Count - 1);
        }
        if (ausgabe.Count < 3)
            throw new InvalidOperationException("Ein Halbebenenschnitt erzeugte weniger als drei Ecken.");

        var ecken = new List<Ecke>();
        for (var i = 0; i < ausgabe.Count; i++)
        {
            var linieZumNaechsten = ausgabe[(i + 1) % ausgabe.Count].EingangsLinie;
            ecken.Add(new Ecke(ausgabe[i].Knoten, linieZumNaechsten));
        }
        var ergebnis = new Polygon(ecken);
        if (Geometrie.Vorzeichenflaeche(ergebnis.Punkte) <= 0)
            throw new InvalidOperationException("Der Halbebenenschnitt hat die Orientierung verloren.");
        return ergebnis;
    }

    private static void FuegeHinzu(List<Eingang> ausgabe, Eingang eingang)
    {
        if (ausgabe.Count != 0 && ausgabe[^1].Knoten.Id == eingang.Knoten.Id) return;
        ausgabe.Add(eingang);
    }

    private Knoten Schnittpunkt(
        Knoten start,
        Knoten ende,
        Linie quelllinie,
        Linie schnittlinie,
        double startseite,
        double endseite)
    {
        if (startseite == 0) return start;
        if (endseite == 0) return ende;
        var schluessel = SchnittSchluessel.Von(start, ende, schnittlinie);
        if (_schnittpunkte.TryGetValue(schluessel, out var vorhanden))
        {
            Beobachte(quelllinie, schnittlinie, vorhanden, schluessel);
            return vorhanden;
        }

        var t = startseite / (startseite - endseite);
        var punkt = start.Punkt + (ende.Punkt - start.Punkt) * t;
        // Rasterkoordinaten werden gesetzt, nicht zurueckgerechnet. Zwei
        // Nachbarzellen tragen damit bitgleich denselben Achsenwert. Im
        // Pflichtlauf 2026-08-20 blieben so auch bei L schraeg alle 475
        // Zellen mannigfaltig und 0 Teilungsnaehte offen.
        punkt = schnittlinie.Achse switch
        {
            Schnittachse.X => new Punkt(schnittlinie.Achsenwert, punkt.Y),
            Schnittachse.Y => new Punkt(punkt.X, schnittlinie.Achsenwert),
            _ => punkt,
        };
        var knoten = _knotenfabrik.Neu(punkt);
        _schnittpunkte.Add(schluessel, knoten);
        Beobachte(quelllinie, schnittlinie, knoten, schluessel);
        return knoten;
    }

    private void Beobachte(
        Linie quelllinie,
        Linie schnittlinie,
        Knoten knoten,
        SchnittSchluessel schnittschluessel)
    {
        var quellkante = new KantenSchluessel(
            schnittschluessel.Klein, schnittschluessel.Gross);
        var schluessel = (quelllinie.Id, schnittlinie.Id, knoten.Id,
            quellkante.Klein, quellkante.Gross);
        if (!_beobachtungen.TryGetValue(schluessel, out var beobachtung))
        {
            _beobachtungen.Add(schluessel, beobachtung = new Beobachtung
            {
                Quelllinie = quelllinie,
                Schnittlinie = schnittlinie,
                Knoten = knoten,
                Quellkante = quellkante,
            });
        }
        beobachtung.Anfragen++;
    }
}
