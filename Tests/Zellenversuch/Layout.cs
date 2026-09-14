using System.Diagnostics;

namespace Zellenversuch;

internal static class Layoutbauer
{
    internal const double Randabstand = 1.0;
    internal const double Fahrgassenbreite = 7.0;
    internal const double Buchtbreite = 3.0;
    internal const double Buchttiefe = 5.9;
    internal const double Gruenstreifenbreite = 2.5;
    internal const double Querstrassenbreite = 3.0;
    internal const double Querstrassenabstand = 34.0;
    internal const double Cs2Mindestkante = 0.375;
    internal const double Zufahrtstiefe = Randabstand + Buchttiefe;

    internal static Bauergebnis Baue(
        Formdefinition form,
        IReadOnlyList<Zufahrtsvorgabe>? zufahrten = null)
    {
        var uhr = Stopwatch.StartNew();
        zufahrten ??= Array.Empty<Zufahrtsvorgabe>();
        var weltpunkte = form.Punkte.ToList();
        if (Geometrie.Vorzeichenflaeche(weltpunkte) < 0) weltpunkte.Reverse();
        var rahmen = Geometrie.Reihenrahmen(weltpunkte);
        var lokalpunkte = weltpunkte.Select(rahmen.NachLokal).ToArray();

        var knotenfabrik = new Knotenfabrik();
        var linienregister = new Linienregister();
        var areal = Polygonfabrik.Areal(lokalpunkte, knotenfabrik, linienregister);
        var zerlegung = new Konvexzerlegung(knotenfabrik, linienregister);
        var teile = zerlegung.Zerlege(areal);
        if (teile.Any(teil => !Geometrie.IstKonvex(teil)))
            throw new InvalidOperationException("Nicht alle Teilpolygone sind konvex.");

        var innenrand = Layoutplanung.Innenrand(lokalpunkte, Randabstand);
        // Die Zufahrt endet nach 1,0 + 5,9 = 6,9 m an der bereits vorhandenen
        // Aussenkante der Randstrasse. Diese Grenze gehoert zum Grundnetz;
        // eine Zufahrt fuegt deshalb nur ihre zwei Seitenlinien hinzu.
        var randstrassenrand = Layoutplanung.Innenrand(lokalpunkte, Zufahrtstiefe);
        var minX = innenrand.Min(punkt => punkt.X);
        var maxX = innenrand.Max(punkt => punkt.X);
        var minY = innenrand.Min(punkt => punkt.Y);
        var maxY = innenrand.Max(punkt => punkt.Y);
        var bandplan = Layoutplanung.Baender(
            minY, maxY, Buchttiefe, Fahrgassenbreite, Gruenstreifenbreite);
        var spaltenplan = Layoutplanung.Spalten(
            minX, maxX, Buchtbreite, Querstrassenbreite,
            Querstrassenabstand, linienregister);
        var gueltigeBuchten = Layoutplanung.GueltigeBuchten(
            bandplan, spaltenplan, innenrand, Cs2Mindestkante);

        var teiler = new Polygonteiler(knotenfabrik);
        var fragmente = teile.ToList();
        for (var i = 0; i < innenrand.Count; i++)
        {
            var linie = linienregister.Innenrand(
                innenrand[i], innenrand[(i + 1) % innenrand.Count], i);
            fragmente = TeileAlle(fragmente, teiler, linie);
        }
        for (var i = 0; i < randstrassenrand.Count; i++)
        {
            var linie = linienregister.Randstrassenkante(
                randstrassenrand[i],
                randstrassenrand[(i + 1) % randstrassenrand.Count], i);
            fragmente = TeileAlle(fragmente, teiler, linie);
        }
        foreach (var linie in spaltenplan.Schnittlinien)
            fragmente = TeileAlle(fragmente, teiler, linie);
        foreach (var y in bandplan.InnereGrenzen)
        {
            if (y <= minY || y >= maxY) continue;
            fragmente = TeileAlle(fragmente, teiler, linienregister.BandY(y));
        }

        var zellen = new List<Zelle>();
        foreach (var fragment in fragmente)
        {
            var mitte = Geometrie.Mittelwert(fragment);
            var art = Zellart.Randband;
            int? buchtId = null;
            int? querstrassenId = null;
            if (Geometrie.Enthaelt(innenrand, mitte))
            {
                var band = bandplan.Bei(mitte.Y);
                var spalte = spaltenplan.Bei(mitte.X);
                if (spalte.Art == Spaltenart.Querstrasse)
                {
                    art = Zellart.Querstrasse;
                    querstrassenId = spalte.QuerstrassenId;
                }
                else if (band.Art == Zellart.Bucht)
                {
                    if (spalte.Art == Spaltenart.Buchtfeld
                        && gueltigeBuchten.TryGetValue((band.Id, spalte.Id), out var id))
                    {
                        art = Zellart.Bucht;
                        buchtId = id;
                    }
                    else
                    {
                        art = Zellart.Kappe;
                    }
                }
                else
                {
                    art = band.Art;
                }
            }

            var zellId = zellen.Count;
            zellen.Add(new Zelle
            {
                Id = zellId,
                Polygon = fragment,
                Art = art,
                Material = Bandplan.MaterialVon(art),
                QuellzelleId = zellId,
                Ursprungsmaterial = Bandplan.MaterialVon(art),
                Ursprungsart = art,
                BuchtId = buchtId,
                QuerstrassenId = querstrassenId,
            });
        }

        var zellenVorZufahrt = zellen;
        var zufahrtsbau = Zufahrtsbauer.Baue(
            zellenVorZufahrt, lokalpunkte, zufahrten,
            Fahrgassenbreite, Zufahrtstiefe,
            knotenfabrik, linienregister, teiler);
        zellen = zufahrtsbau.Zellen;
        var (vorTrennung, topologie) = Vereinigung.Vereinige(zellen);
        var (flaechen, trennbericht) = Lochtrenner.Trenne(
            zellen, vorTrennung, rahmen);
        uhr.Stop();
        return new Bauergebnis
        {
            Form = form,
            Rahmen = rahmen,
            ArealLokal = areal,
            Innenrand = innenrand,
            Randstrassenrand = randstrassenrand,
            KonvexeTeile = teile,
            ZellenVorZufahrt = zellenVorZufahrt,
            Zellen = zellen,
            FlaechenVorTrennung = vorTrennung,
            Flaechen = flaechen,
            Bandplan = bandplan,
            Spaltenplan = spaltenplan,
            Zufahrtsbericht = zufahrtsbau.Bericht,
            Lochtrennung = trennbericht,
            Topologie = topologie,
            EinzelneBauzeit = uhr.Elapsed,
        };
    }

    private static List<Polygon> TeileAlle(
        IEnumerable<Polygon> fragmente,
        Polygonteiler teiler,
        Linie linie)
    {
        var ausgabe = new List<Polygon>();
        foreach (var fragment in fragmente)
        {
            try
            {
                ausgabe.AddRange(teiler.Teile(fragment, linie));
            }
            catch (Exception fehler)
            {
                throw new InvalidOperationException(
                    $"Teilung an {linie.Name} (ID {linie.Id}) scheiterte.", fehler);
            }
        }
        return ausgabe;
    }
}
