using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingLotTool.Geometry.Zellen
{
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
            double tiefe,
            Knotenfabrik knotenfabrik,
            Linienregister linienregister,
            Polygonteiler teiler)
        {
            var ersterNeuerKnoten = knotenfabrik.Anzahl;
            var zufahrten = Plane(areal, vorgaben, tiefe, linienregister);
            var zellen = grundzellen.Select(Kopiere).ToList();
            foreach (var zufahrt in zufahrten)
            {
                var links = zufahrt.Start - zufahrt.Querhalbvektor;
                var rechts = zufahrt.Start + zufahrt.Querhalbvektor;
                zellen = TeileAlle(
                    zellen,
                    teiler,
                    zufahrt.LinkeKante,
                    links,
                    links + zufahrt.Innennormale * tiefe);
                VervollstaendigeNachbarkanten(zellen, teiler, zufahrt.LinkeKante);
                zellen = TeileAlle(
                    zellen,
                    teiler,
                    zufahrt.RechteKante,
                    rechts,
                    rechts + zufahrt.Innennormale * tiefe);
                VervollstaendigeNachbarkanten(zellen, teiler, zufahrt.RechteKante);
            }

            var klassifiziert = zellen.Select(zelle =>
            {
                var mitte = Geometrie.Mittelwert(zelle.Polygon);
                var deckungen = 0;
                Zufahrtsart? ersteArt = null;
                for (var i = 0; i < zufahrten.Count; i++)
                {
                    if (!Enthaelt(zufahrten[i], mitte)) continue;
                    deckungen++;
                    ersteArt ??= zufahrten[i].Vorgabe.Art;
                }
                return (Zelle: zelle, Deckungen: deckungen, Zugangsart: ersteArt);
            }).ToList();
            var beruehrteBuchten = new HashSet<int>(klassifiziert
                .Where(wert => wert.Deckungen != 0 && wert.Zelle.BuchtId.HasValue)
                .Select(wert => wert.Zelle.BuchtId.Value));

            var ausgabe = new List<Zelle>(klassifiziert.Count);
            foreach (var wert in klassifiziert)
            {
                var zelle = wert.Zelle;
                var deckungen = wert.Deckungen;
                var istZufahrt = deckungen != 0;
                var istBuchtrest = !istZufahrt
                    && zelle.BuchtId.HasValue
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
                    Flaechenabschnitt = zelle.Flaechenabschnitt,
                    Zufahrtsdeckungen = deckungen,
                    Zugangsart = istZufahrt ? wert.Zugangsart : null,
                });
            }

            return (ausgabe, Messe(
                grundzellen,
                ausgabe,
                zufahrten,
                beruehrteBuchten.Count,
                ersterNeuerKnoten,
                teiler.Schnittbeobachtungen));
        }

        internal static List<Zufahrtsgeometrie> Plane(
            IReadOnlyList<Punkt> areal,
            IReadOnlyList<Zufahrtsvorgabe> vorgaben,
            double tiefe,
            Linienregister linienregister)
        {
            if (Geometrie.Vorzeichenflaeche(areal) <= 0)
                throw new InvalidOperationException(
                    "Zufahrten erwarten einen CCW-Aussenring.");
            var ausgabe = new List<Zufahrtsgeometrie>();
            for (var nummer = 0; nummer < vorgaben.Count; nummer++)
            {
                var vorgabe = vorgaben[nummer];
                if (vorgabe.Kante < 0
                    || vorgabe.Kante >= areal.Count
                    || double.IsNaN(vorgabe.Along)
                    || double.IsInfinity(vorgabe.Along))
                    throw new InvalidOperationException(
                        $"Entrance {nummer}: edge or along is invalid.");
                var a = areal[vorgabe.Kante];
                var b = areal[(vorgabe.Kante + 1) % areal.Count];
                var vektor = b - a;
                var laenge = Geometrie.Laenge(vektor);
                if (laenge == 0 || vorgabe.Along < 0 || vorgabe.Along > laenge)
                    throw new InvalidOperationException(
                        $"Entrance {nummer}: along {vorgabe.Along:R} is not on "
                        + $"edge {vorgabe.Kante} with length {laenge:R}.");
                var tangente = vektor * (1 / laenge);
                var normale = new Punkt(-tangente.Y, tangente.X);
                var start = a + tangente * vorgabe.Along;
                var achse = vorgabe.AchsrichtungLokal ?? normale;
                var achslaenge = Geometrie.Laenge(achse);
                if (achslaenge == 0)
                    throw new InvalidOperationException(
                        $"Entrance {nummer}: corner axis has zero length.");
                achse = achse * (1 / achslaenge);
                var projektion = Geometrie.Skalar(achse, normale);
                if (projektion <= 0.3)
                    throw new InvalidOperationException(
                        $"Entrance {nummer}: corner axis does not point into the polygon.");
                var querhalbvektor = tangente * (vorgabe.Breite / 2 / projektion);
                var tiefeDieserZufahrt = vorgabe.Achslaenge ?? tiefe;
                var links = start - querhalbvektor;
                var rechts = start + querhalbvektor;
                ausgabe.Add(new Zufahrtsgeometrie
                {
                    Nummer = nummer,
                    Vorgabe = vorgabe,
                    Start = start,
                    Tangente = tangente,
                    Querhalbvektor = querhalbvektor,
                    Innennormale = achse,
                    Tiefe = tiefeDieserZufahrt,
                    LinkeKante = linienregister.Zufahrtskante(
                        links, achse, nummer, "links"),
                    RechteKante = linienregister.Zufahrtskante(
                        rechts, achse, nummer, "rechts"),
                });
            }
            return ausgabe;
        }

        private static bool Enthaelt(
            Zufahrtsgeometrie zufahrt,
            Punkt punkt)
        {
            var delta = punkt - zufahrt.Start;
            var achsvektor = zufahrt.Innennormale * zufahrt.Tiefe;
            var nenner = Geometrie.Kreuz(zufahrt.Querhalbvektor, achsvektor);
            if (Math.Abs(nenner) < 1e-12) return false;
            // Das Parallelogramm wird in seinen beiden Vektoren geprüft. Ein
            // Lot-/Skalarprodukttest wäre an der schrägen Ecke ein Rechteck
            // und ließe wieder genau die gemessenen 8,7-m2-Keile übrig.
            var quer = Geometrie.Kreuz(delta, achsvektor) / nenner;
            var hinein = Geometrie.Kreuz(zufahrt.Querhalbvektor, delta) / nenner;
            return quer >= -1 && quer <= 1 && hinein >= 0 && hinein <= 1;
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
                        $"Zufahrtsteilung an {linie.Name} (ID {linie.Id}) scheiterte.",
                        fehler);
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
                        Flaechenabschnitt = zelle.Flaechenabschnitt,
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
                .Select(wert => new
                {
                    wert.Quellkante,
                    wert.Quelllinie,
                    wert.Knoten,
                })
                .GroupBy(wert => new { wert.Quellkante, Knoten = wert.Knoten.Id })
                .Select(gruppe => gruppe.First())
                .ToArray();
            foreach (var einsatz in einsaetze)
                foreach (var zelle in zellen)
                    for (var i = 0; i < zelle.Polygon.Anzahl; i++)
                    {
                        var a = zelle.Polygon.Knoten(i);
                        var b = zelle.Polygon.Knoten(i + 1);
                        if (KantenSchluessel.Von(a, b) != einsatz.Quellkante
                            || a.Id == einsatz.Knoten.Id
                            || b.Id == einsatz.Knoten.Id)
                            continue;
                        // Am 6,9-m-Ende teilt die Zufahrtslinie nur die aeussere
                        // Zelle. Der innere Nachbar erhaelt denselben Knoten als
                        // kollineare Ecke; damit bleibt die Zellkomplexkante exakt.
                        zelle.Polygon.Ecken.Insert(
                            i + 1, new Ecke(einsatz.Knoten, einsatz.Quelllinie));
                        break;
                    }
        }

        private static Zelle Kopiere(Zelle zelle) => new Zelle
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
            Flaechenabschnitt = zelle.Flaechenabschnitt,
            Zufahrtsdeckungen = zelle.Zufahrtsdeckungen,
        };

        private static int WertOderNull(Dictionary<int, int> werte, int schluessel)
        {
            int wert;
            return werte.TryGetValue(schluessel, out wert) ? wert : 0;
        }

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
                .SelectMany(zelle => zelle.Polygon.Ecken.Select(ecke =>
                    (Zelle: zelle.Id, Knoten: ecke.Knoten.Id)))
                .Where(wert => neueKnoten.ContainsKey(wert.Knoten))
                .GroupBy(wert => wert.Knoten)
                .ToDictionary(
                    gruppe => gruppe.Key,
                    gruppe => gruppe.Select(wert => wert.Zelle).Distinct().Count());
            var zufahrtslinien = zufahrten
                .SelectMany(zufahrt => new[]
                    { zufahrt.LinkeKante, zufahrt.RechteKante })
                .ToArray();
            var zufahrtslinienIds = new HashSet<int>(
                zufahrtslinien.Select(linie => linie.Id));
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
                .Sum(gruppe => Math.Max(
                    0, gruppe.Select(wert => wert.Knoten.Id).Distinct().Count() - 1));
            var innenknoten = new HashSet<int>(relevanteBeobachtungen
                .Where(wert => wert.Quelllinie.Art != Linienart.Aussenkante)
                .Select(wert => wert.Knoten.Id)
                .Distinct());
            var randstrassenendpunkte = new HashSet<int>(relevanteBeobachtungen
                .Where(wert => wert.Quelllinie.Art == Linienart.Randstrassenkante)
                .Select(wert => wert.Knoten.Id)
                .Distinct());
            var nutzungsfehler = neueKnoten.Keys.Select(id =>
            {
                var erwartet = innenknoten.Contains(id)
                    ? randstrassenendpunkte.Contains(id) ? 3 : 4
                    : 2;
                var beobachtung = relevanteBeobachtungen
                    .FirstOrDefault(wert => wert.Knoten.Id == id);
                var art = beobachtung == null
                    ? Linienart.Aussenkante
                    : beobachtung.Quelllinie.Art;
                return new Punktnutzungsfehler(
                    neueKnoten[id], WertOderNull(nutzungen, id), erwartet, art);
            }).Where(fehler => fehler.Nutzer < fehler.Erwartet).ToArray();

            var flaechenVorher = vorher
                .Select(zelle => Geometrie.Flaeche(zelle.Polygon))
                .ToArray();
            var flaechen = nachher
                .Select(zelle => Geometrie.Flaeche(zelle.Polygon))
                .ToArray();
            var aussenlinien = new HashSet<int>(vorher
                .SelectMany(zelle => zelle.Polygon.Ecken)
                .Where(ecke => ecke.LinieBisNaechste.Art == Linienart.Aussenkante)
                .Select(ecke => ecke.LinieBisNaechste.Id));
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
                    WertOderNull(nutzungen, id)
                    >= (randstrassenendpunkte.Contains(id) ? 3 : 4)),
                GemeinsamePunktabweichungen = punktabweichungen,
                NeuePunkteMitZuWenigNutzern = nutzungsfehler.Length,
                Punktnutzungsfehler = nutzungsfehler,
                NullflaechenzellenVorher = flaechenVorher.Count(flaeche => flaeche == 0),
                Nullflaechenzellen = flaechen.Count(flaeche => flaeche == 0),
                SplitterzellenVorher = flaechenVorher.Count(flaeche =>
                    flaeche > 0 && flaeche < Splittergrenze),
                Splitterzellen = flaechen.Count(flaeche =>
                    flaeche > 0 && flaeche < Splittergrenze),
                KleinsteZellflaecheVorher = flaechenVorher.Length == 0
                    ? double.NaN
                    : flaechenVorher.Min(),
                KleinsteZellflaeche = flaechen.Length == 0
                    ? double.NaN
                    : flaechen.Min(),
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
}

