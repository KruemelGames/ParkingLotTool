namespace Zellenversuch;

/// <summary>
/// Plant nur konstruktive Linien. Die Randkontur, Bandgrenzen, Querstrassen
/// und Buchtgrenzen werden danach vom vorhandenen Halbebenenteiler benutzt;
/// keine dieser Grenzen entsteht durch Nachbearbeiten fertiger Polygone.
/// </summary>
internal static class Layoutplanung
{
    internal static IReadOnlyList<Punkt> Innenrand(
        IReadOnlyList<Punkt> aussenring,
        double abstand)
    {
        var verschobeneKanten = new (Punkt Punkt, Punkt Richtung)[aussenring.Count];
        for (var i = 0; i < aussenring.Count; i++)
        {
            var a = aussenring[i];
            var richtung = aussenring[(i + 1) % aussenring.Count] - a;
            var laenge = Geometrie.Laenge(richtung);
            if (laenge == 0)
                throw new InvalidOperationException("Der Aussenring enthaelt eine Nullkante.");
            var linkeNormale = new Punkt(-richtung.Y / laenge, richtung.X / laenge);
            verschobeneKanten[i] = (a + linkeNormale * abstand, richtung);
        }

        var innen = new Punkt[aussenring.Count];
        for (var i = 0; i < aussenring.Count; i++)
        {
            var vorher = verschobeneKanten[Geometrie.Mod(i - 1, aussenring.Count)];
            var aktuell = verschobeneKanten[i];
            var nenner = Geometrie.Kreuz(vorher.Richtung, aktuell.Richtung);
            if (nenner == 0)
                throw new InvalidOperationException("Zwei benachbarte Randkanten sind parallel.");
            var parameter = Geometrie.Kreuz(
                aktuell.Punkt - vorher.Punkt,
                aktuell.Richtung) / nenner;
            innen[i] = vorher.Punkt + vorher.Richtung * parameter;
        }

        if (Geometrie.Vorzeichenflaeche(innen) <= 0)
            throw new InvalidOperationException("Der um 1,0 m eingerueckte Rand ist nicht CCW.");
        if (innen.Any(punkt => !Geometrie.EnthaeltOderRand(aussenring, punkt)))
            throw new InvalidOperationException("Der eingerueckte Rand verlaesst das Areal.");
        return innen;
    }

    internal static Bandplan Baender(
        double minY,
        double maxY,
        double buchttiefe,
        double fahrgassenbreite,
        double gruenstreifenbreite)
    {
        var modulhoehe = 2 * buchttiefe + fahrgassenbreite;
        var hoehe = maxY - minY;
        var module = 0;
        while ((module + 1) * modulhoehe + module * gruenstreifenbreite <= hoehe)
            module++;
        if (module == 0)
            throw new InvalidOperationException("In die Innenkontur passt kein Parkmodul.");

        var belegteHoehe = module * modulhoehe + (module - 1) * gruenstreifenbreite;
        var cursor = minY;
        var modulanfang = minY + (hoehe - belegteHoehe) / 2;
        var baender = new List<Bandabschnitt>();
        var bandId = 0;
        var reihenId = 0;

        void FuegeHinzu(double ende, Zellart art, int? reihe = null)
        {
            if (ende <= cursor) return;
            baender.Add(new Bandabschnitt(bandId++, cursor, ende, art, reihe));
            cursor = ende;
        }

        FuegeHinzu(modulanfang, Zellart.Restgruen);
        for (var modul = 0; modul < module; modul++)
        {
            FuegeHinzu(cursor + buchttiefe, Zellart.Bucht, reihenId++);
            FuegeHinzu(cursor + fahrgassenbreite, Zellart.Fahrgasse);
            FuegeHinzu(cursor + buchttiefe, Zellart.Bucht, reihenId++);
            if (modul + 1 < module)
                FuegeHinzu(cursor + gruenstreifenbreite, Zellart.Gruenstreifen);
        }
        FuegeHinzu(maxY, Zellart.Restgruen);
        return new Bandplan { Baender = baender };
    }

    internal static Spaltenplan Spalten(
        double minX,
        double maxX,
        double buchtbreite,
        double querstrassenbreite,
        double querstrassenabstand,
        Linienregister linienregister)
    {
        var querstrassen = new List<Querstrassenplan>();
        var grenzen = new Dictionary<double, Linie>();
        var mitte = minX + querstrassenabstand;
        while (mitte + querstrassenbreite / 2 < maxX)
        {
            var id = querstrassen.Count;
            var anfang = mitte - querstrassenbreite / 2;
            var ende = mitte + querstrassenbreite / 2;
            var links = linienregister.Querstrassenkante(anfang, id, "links");
            var rechts = linienregister.Querstrassenkante(ende, id, "rechts");
            grenzen.Add(anfang, links);
            grenzen.Add(ende, rechts);
            querstrassen.Add(new Querstrassenplan(id, mitte, anfang, ende, links, rechts));
            mitte += querstrassenabstand;
        }

        var spalten = new List<Spaltenabschnitt>();
        var spaltenId = 0;

        void MerkeRastergrenze(double x)
        {
            if (x <= minX || x >= maxX || grenzen.ContainsKey(x)) return;
            grenzen.Add(x, linienregister.RasterX(x));
        }

        void FuegeFreienAbschnittHinzu(double anfang, double ende)
        {
            var laenge = ende - anfang;
            if (laenge < 2 * buchtbreite)
            {
                spalten.Add(new Spaltenabschnitt(
                    spaltenId++, anfang, ende, Spaltenart.Kappenrest));
                return;
            }

            // Jede Reihe endet auf beiden Seiten mit einer echten Kappe.
            // Bei 3,0 m Buchtbreite ist sie damit 3,0 bis unter 4,5 m breit.
            // Gemessen bei L gross: Der alte 0,200-m-Rest ergab eine fertige
            // 0,200-m-Kante; mit zwei Kappen liegt das Minimum bei 1,000 m.
            var buchten = (int)Math.Floor((laenge - 2 * buchtbreite) / buchtbreite);
            var kappenlaenge = (laenge - buchten * buchtbreite) / 2;
            var cursor = anfang + kappenlaenge;
            spalten.Add(new Spaltenabschnitt(
                spaltenId++, anfang, cursor, Spaltenart.Kappenrest));
            MerkeRastergrenze(cursor);
            for (var bucht = 0; bucht < buchten; bucht++)
            {
                var naechstes = cursor + buchtbreite;
                spalten.Add(new Spaltenabschnitt(
                    spaltenId++, cursor, naechstes, Spaltenart.Buchtfeld));
                MerkeRastergrenze(naechstes);
                cursor = naechstes;
            }
            spalten.Add(new Spaltenabschnitt(
                spaltenId++, cursor, ende, Spaltenart.Kappenrest));
        }

        var freierAnfang = minX;
        foreach (var querstrasse in querstrassen)
        {
            FuegeFreienAbschnittHinzu(freierAnfang, querstrasse.Anfang);
            spalten.Add(new Spaltenabschnitt(
                spaltenId++, querstrasse.Anfang, querstrasse.Ende,
                Spaltenart.Querstrasse, querstrasse.Id));
            freierAnfang = querstrasse.Ende;
        }
        FuegeFreienAbschnittHinzu(freierAnfang, maxX);

        return new Spaltenplan
        {
            Spalten = spalten,
            Querstrassen = querstrassen,
            Schnittlinien = grenzen.OrderBy(paar => paar.Key).Select(paar => paar.Value).ToList(),
        };
    }

    internal static Dictionary<(int Band, int Spalte), int> GueltigeBuchten(
        Bandplan bandplan,
        Spaltenplan spaltenplan,
        IReadOnlyList<Punkt> innenrand,
        double mindestkantenabstand)
    {
        var ausgabe = new Dictionary<(int, int), int>();
        foreach (var band in bandplan.Baender.Where(band => band.Art == Zellart.Bucht))
            foreach (var spalte in spaltenplan.Spalten.Where(
                         spalte => spalte.Art == Spaltenart.Buchtfeld))
            {
                var ecken = new[]
                {
                    new Punkt(spalte.Anfang, band.Anfang),
                    new Punkt(spalte.Ende, band.Anfang),
                    new Punkt(spalte.Ende, band.Ende),
                    new Punkt(spalte.Anfang, band.Ende),
                };
                // Vollstaendig innen reichte an der schraegen Kante nicht:
                // Dort blieben gemessen 0,084 m bis zum Rand. Die belegte
                // CS2-Kantengrenze 0,375 m kostet genau eine Bucht; danach
                // misst L schraeg 0,514 m als engste fertige Stelle.
                if (ecken.All(ecke => Geometrie.EnthaeltOderRand(innenrand, ecke))
                    && ecken.All(ecke => Mindestabstand(ecke, innenrand) >= mindestkantenabstand)
                    && !innenrand.Any(punkt =>
                        punkt.X > spalte.Anfang && punkt.X < spalte.Ende
                        && punkt.Y > band.Anfang && punkt.Y < band.Ende))
                    ausgabe.Add((band.Id, spalte.Id), ausgabe.Count);
            }
        return ausgabe;
    }

    private static double Mindestabstand(Punkt punkt, IReadOnlyList<Punkt> ring)
    {
        var minimum = double.PositiveInfinity;
        for (var i = 0; i < ring.Count; i++)
            minimum = Math.Min(minimum, Geometrie.AbstandPunktStrecke(
                punkt, ring[i], ring[(i + 1) % ring.Count]));
        return minimum;
    }
}
