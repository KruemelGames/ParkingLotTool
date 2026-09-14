namespace Zellenversuch;

/// <summary>
/// Verfeinert das fertige Zellnetz an zwei durchgehenden Geraden je Zufahrt
/// und widmet erst danach die Fragmente zwischen Aussenkante und der bereits
/// vorhandenen Randstrassenkante um. Materialpolygone existieren zu diesem
/// Zeitpunkt noch nicht; an ihnen wird deshalb nichts ausgeschnitten.
/// </summary>
internal static class Zufahrtsbauer
{
    internal const double Splittergrenze = 0.01;

    internal static (List<Zelle> Zellen, Zufahrtsbericht Bericht) Baue(
        IReadOnlyList<Zelle> grundzellen,
        IReadOnlyList<Punkt> areal,
        IReadOnlyList<Zufahrtsvorgabe> vorgaben,
        double breite,
        double tiefe,
        Knotenfabrik knotenfabrik,
        Linienregister linienregister,
        Polygonteiler teiler)
    {
        var ersterNeuerKnoten = knotenfabrik.Anzahl;
        var zufahrten = Plane(areal, vorgaben, breite, tiefe, linienregister);
        var zellen = grundzellen.Select(Kopiere).ToList();
        foreach (var zufahrt in zufahrten)
        {
            var links = zufahrt.Start - zufahrt.Tangente * (breite / 2);
            var rechts = zufahrt.Start + zufahrt.Tangente * (breite / 2);
            zellen = TeileAlle(zellen, teiler, zufahrt.LinkeKante,
                links, links + zufahrt.Innennormale * tiefe);
            VervollstaendigeNachbarkanten(zellen, teiler, zufahrt.LinkeKante);
            zellen = TeileAlle(zellen, teiler, zufahrt.RechteKante,
                rechts, rechts + zufahrt.Innennormale * tiefe);
            VervollstaendigeNachbarkanten(zellen, teiler, zufahrt.RechteKante);
        }

        var klassifiziert = zellen.Select(zelle =>
        {
            var mitte = Geometrie.Mittelwert(zelle.Polygon);
            var deckungen = zufahrten.Count(zufahrt => Enthaelt(zufahrt, mitte, breite));
            return (Zelle: zelle, Deckungen: deckungen);
        }).ToList();
        var beruehrteBuchten = klassifiziert
            .Where(wert => wert.Deckungen != 0 && wert.Zelle.BuchtId.HasValue)
            .Select(wert => wert.Zelle.BuchtId!.Value)
            .ToHashSet();

        var ausgabe = new List<Zelle>(klassifiziert.Count);
        foreach (var (zelle, deckungen) in klassifiziert)
        {
            var istZufahrt = deckungen != 0;
            var istBuchtrest = !istZufahrt && zelle.BuchtId.HasValue
                && beruehrteBuchten.Contains(zelle.BuchtId.Value);
            var art = istZufahrt
                ? Zellart.Zufahrt
                : istBuchtrest ? Zellart.Restbelag : zelle.Art;
            ausgabe.Add(new Zelle
            {
                Id = ausgabe.Count,
                Polygon = zelle.Polygon,
                Material = istZufahrt ? Material.Asphalt : zelle.Material,
                Art = art,
                QuellzelleId = zelle.QuellzelleId,
                Ursprungsmaterial = zelle.Ursprungsmaterial,
                Ursprungsart = zelle.Ursprungsart,
                BuchtId = istZufahrt || istBuchtrest ? null : zelle.BuchtId,
                QuerstrassenId = istZufahrt ? null : zelle.QuerstrassenId,
                Zufahrtsdeckungen = deckungen,
            });
        }

        return (ausgabe, Messe(
            grundzellen, ausgabe, zufahrten, beruehrteBuchten.Count,
            ersterNeuerKnoten, teiler.Schnittbeobachtungen));
    }

    private static List<Zufahrtsgeometrie> Plane(
        IReadOnlyList<Punkt> areal,
        IReadOnlyList<Zufahrtsvorgabe> vorgaben,
        double breite,
        double tiefe,
        Linienregister linienregister)
    {
        if (Geometrie.Vorzeichenflaeche(areal) <= 0)
            throw new InvalidOperationException("Zufahrten erwarten einen CCW-Aussenring.");
        var ausgabe = new List<Zufahrtsgeometrie>();
        for (var nummer = 0; nummer < vorgaben.Count; nummer++)
        {
            var vorgabe = vorgaben[nummer];
            if (vorgabe.Kante < 0 || vorgabe.Kante >= areal.Count
                || !double.IsFinite(vorgabe.Along))
                throw new InvalidOperationException(
                    $"Zufahrt {nummer}: Kante oder along ist ungueltig.");
            var a = areal[vorgabe.Kante];
            var b = areal[(vorgabe.Kante + 1) % areal.Count];
            var vektor = b - a;
            var laenge = Geometrie.Laenge(vektor);
            if (laenge == 0 || vorgabe.Along < 0 || vorgabe.Along > laenge)
                throw new InvalidOperationException(
                    $"Zufahrt {nummer}: along {vorgabe.Along:R} liegt nicht auf Kante "
                    + $"{vorgabe.Kante} mit Laenge {laenge:R}.");
            var tangente = vektor * (1 / laenge);
            var normale = new Punkt(-tangente.Y, tangente.X);
            var start = a + tangente * vorgabe.Along;
            var links = start - tangente * (breite / 2);
            var rechts = start + tangente * (breite / 2);
            ausgabe.Add(new Zufahrtsgeometrie
            {
                Nummer = nummer,
                Vorgabe = vorgabe,
                Start = start,
                Tangente = tangente,
                Innennormale = normale,
                Tiefe = tiefe,
                LinkeKante = linienregister.Zufahrtskante(
                    links, normale, nummer, "links"),
                RechteKante = linienregister.Zufahrtskante(
                    rechts, normale, nummer, "rechts"),
            });
        }
        return ausgabe;
    }

    private static bool Enthaelt(Zufahrtsgeometrie zufahrt, Punkt punkt, double breite)
    {
        var delta = punkt - zufahrt.Start;
        var entlang = Geometrie.Skalar(delta, zufahrt.Tangente);
        var hinein = Geometrie.Skalar(delta, zufahrt.Innennormale);
        return entlang >= -breite / 2 && entlang <= breite / 2
            && hinein >= 0 && hinein <= zufahrt.Tiefe;
    }

    private static List<Zelle> TeileAlle(
        IReadOnlyList<Zelle> zellen,
        Polygonteiler teiler,
        Linie linie,
        Punkt segmentanfang,
        Punkt segmentende)
    {
        var ausgabe = new List<Zelle>();
        foreach (var zelle in zellen)
        {
            IReadOnlyList<Polygon> teile;
            try
            {
                teile = teiler.TeileSegment(
                    zelle.Polygon, linie, segmentanfang, segmentende);
            }
            catch (Exception fehler)
            {
                throw new InvalidOperationException(
                    $"Zufahrtsteilung an {linie.Name} (ID {linie.Id}) scheiterte.", fehler);
            }
            foreach (var teil in teile)
                ausgabe.Add(new Zelle
                {
                    Id = ausgabe.Count,
                    Polygon = teil,
                    Material = zelle.Material,
                    Art = zelle.Art,
                    QuellzelleId = zelle.QuellzelleId,
                    Ursprungsmaterial = zelle.Ursprungsmaterial,
                    Ursprungsart = zelle.Ursprungsart,
                    BuchtId = zelle.BuchtId,
                    QuerstrassenId = zelle.QuerstrassenId,
                    Zufahrtsdeckungen = zelle.Zufahrtsdeckungen,
                });
        }
        return ausgabe;
    }

    private static void VervollstaendigeNachbarkanten(
        IReadOnlyList<Zelle> zellen,
        Polygonteiler teiler,
        Linie schnittlinie)
    {
        var einsaetze = teiler.Schnittbeobachtungen
            .Where(wert => wert.Schnittlinie.Id == schnittlinie.Id)
            .Select(wert => (wert.Quellkante, wert.Quelllinie, wert.Knoten))
            .DistinctBy(wert => (wert.Quellkante, wert.Knoten.Id))
            .ToArray();
        foreach (var (quellkante, quelllinie, knoten) in einsaetze)
            foreach (var zelle in zellen)
                for (var i = 0; i < zelle.Polygon.Anzahl; i++)
                {
                    var a = zelle.Polygon.Knoten(i);
                    var b = zelle.Polygon.Knoten(i + 1);
                    if (KantenSchluessel.Von(a, b) != quellkante
                        || a.Id == knoten.Id || b.Id == knoten.Id)
                        continue;
                    // Am 6,9-m-Ende teilt die Zufahrtslinie nur die aeussere
                    // Zelle. Der innere Nachbar erhaelt denselben Knoten als
                    // kollineare Ecke; damit bleibt die Zellkomplexkante exakt.
                    zelle.Polygon.Ecken.Insert(i + 1, new Ecke(knoten, quelllinie));
                    break;
                }
    }

    private static Zelle Kopiere(Zelle zelle) => new()
    {
        Id = zelle.Id,
        Polygon = new Polygon(zelle.Polygon.Ecken),
        Material = zelle.Material,
        Art = zelle.Art,
        QuellzelleId = zelle.QuellzelleId,
        Ursprungsmaterial = zelle.Ursprungsmaterial,
        Ursprungsart = zelle.Ursprungsart,
        BuchtId = zelle.BuchtId,
        QuerstrassenId = zelle.QuerstrassenId,
        Zufahrtsdeckungen = zelle.Zufahrtsdeckungen,
    };

    private static Zufahrtsbericht Messe(
        IReadOnlyList<Zelle> vorher,
        IReadOnlyList<Zelle> nachher,
        IReadOnlyList<Zufahrtsgeometrie> zufahrten,
        int entfalleneBuchten,
        int ersterNeuerKnoten,
        IReadOnlyList<Schnittbeobachtung> beobachtungen)
    {
        var neueKnoten = nachher
            .SelectMany(zelle => zelle.Polygon.Ecken)
            .Select(ecke => ecke.Knoten)
            .Where(knoten => knoten.Id >= ersterNeuerKnoten)
            .GroupBy(knoten => knoten.Id)
            .ToDictionary(gruppe => gruppe.Key, gruppe => gruppe.First());
        var nutzungen = nachher
            .SelectMany(zelle => zelle.Polygon.Ecken
                .Select(ecke => (Zelle: zelle.Id, Knoten: ecke.Knoten.Id)))
            .Where(wert => neueKnoten.ContainsKey(wert.Knoten))
            .GroupBy(wert => wert.Knoten)
            .ToDictionary(gruppe => gruppe.Key,
                gruppe => gruppe.Select(wert => wert.Zelle).Distinct().Count());
        var zufahrtslinien = zufahrten
            .SelectMany(zufahrt => new[] { zufahrt.LinkeKante, zufahrt.RechteKante })
            .ToArray();
        var zufahrtslinienIds = zufahrtslinien.Select(linie => linie.Id).ToHashSet();
        var wirksameLinien = nachher
            .SelectMany(zelle => zelle.Polygon.Ecken)
            .Select(ecke => ecke.LinieBisNaechste.Id)
            .Where(zufahrtslinienIds.Contains)
            .Distinct()
            .Count();
        var relevanteBeobachtungen = beobachtungen
            .Where(wert => zufahrtslinienIds.Contains(wert.Schnittlinie.Id)
                && wert.Knoten.Id >= ersterNeuerKnoten)
            .ToList();
        var punktabweichungen = relevanteBeobachtungen
            .GroupBy(wert => (wert.Quelllinie.Id, wert.Schnittlinie.Id))
            .Sum(gruppe => Math.Max(0,
                gruppe.Select(wert => wert.Knoten.Id).Distinct().Count() - 1));
        var innenknoten = relevanteBeobachtungen
            .Where(wert => wert.Quelllinie.Art != Linienart.Aussenkante)
            .Select(wert => wert.Knoten.Id)
            .Distinct()
            .ToHashSet();
        var randstrassenendpunkte = relevanteBeobachtungen
            .Where(wert => wert.Quelllinie.Art == Linienart.Randstrassenkante)
            .Select(wert => wert.Knoten.Id)
            .Distinct()
            .ToHashSet();
        var nutzungsfehler = neueKnoten.Keys.Select(id =>
        {
            var erwartet = innenknoten.Contains(id)
                ? randstrassenendpunkte.Contains(id) ? 3 : 4
                : 2;
            var art = relevanteBeobachtungen
                .FirstOrDefault(wert => wert.Knoten.Id == id)?.Quelllinie.Art
                ?? Linienart.Aussenkante;
            return new Punktnutzungsfehler(
                neueKnoten[id], nutzungen.GetValueOrDefault(id), erwartet, art);
        }).Where(fehler => fehler.Nutzer < fehler.Erwartet).ToArray();

        var flaechenVorher = vorher.Select(
            zelle => Geometrie.Flaeche(zelle.Polygon)).ToArray();
        var flaechen = nachher.Select(zelle => Geometrie.Flaeche(zelle.Polygon)).ToArray();
        var aussenlinien = vorher
            .SelectMany(zelle => zelle.Polygon.Ecken)
            .Where(ecke => ecke.LinieBisNaechste.Art == Linienart.Aussenkante)
            .Select(ecke => ecke.LinieBisNaechste.Id)
            .ToHashSet();
        var getroffeneRandseiten = nachher
            .Where(zelle => zelle.Art == Zellart.Zufahrt)
            .SelectMany(zelle => zelle.Polygon.Ecken)
            .Select(ecke => ecke.LinieBisNaechste.Id)
            .Where(aussenlinien.Contains)
            .Distinct()
            .Count();

        return new Zufahrtsbericht
        {
            Vorgaben = zufahrten.Count,
            Teilungslinien = zufahrtslinien.Length,
            WirksameTeilungslinien = wirksameLinien,
            NeueGeometriepunkte = neueKnoten.Count,
            GemeinsamGenutzteNeuePunkte = innenknoten.Count(id =>
                nutzungen.GetValueOrDefault(id)
                >= (randstrassenendpunkte.Contains(id) ? 3 : 4)),
            GemeinsamePunktabweichungen = punktabweichungen,
            NeuePunkteMitZuWenigNutzern = nutzungsfehler.Length,
            Punktnutzungsfehler = nutzungsfehler,
            NullflaechenzellenVorher = flaechenVorher.Count(flaeche => flaeche == 0),
            Nullflaechenzellen = flaechen.Count(flaeche => flaeche == 0),
            SplitterzellenVorher = flaechenVorher.Count(flaeche => flaeche > 0
                && flaeche < Splittergrenze),
            Splitterzellen = flaechen.Count(flaeche => flaeche > 0
                && flaeche < Splittergrenze),
            KleinsteZellflaecheVorher = flaechenVorher.Length == 0
                ? double.NaN : flaechenVorher.Min(),
            KleinsteZellflaeche = flaechen.Length == 0 ? double.NaN : flaechen.Min(),
            Zufahrtsflaeche = nachher
                .Where(zelle => zelle.Art == Zellart.Zufahrt)
                .Sum(zelle => Geometrie.Flaeche(zelle.Polygon)),
            MehrfachUeberdeckteFlaeche = nachher
                .Where(zelle => zelle.Zufahrtsdeckungen > 1)
                .Sum(zelle => Geometrie.Flaeche(zelle.Polygon)),
            UmgewidmetesGruen = nachher
                .Where(zelle => zelle.Art == Zellart.Zufahrt
                    && zelle.Ursprungsmaterial == Material.Gruen)
                .Sum(zelle => Geometrie.Flaeche(zelle.Polygon)),
            EntfalleneBuchten = entfalleneBuchten,
            GetroffeneRandseiten = getroffeneRandseiten,
            Zufahrten = zufahrten,
        };
    }
}
