using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingLotTool.Geometry.Zellen
{
    internal sealed partial class Teilflaechenlayout
    {
        private sealed class Rasterkandidat
        {
            internal Ringlosplan Ringlos;
            internal double Versatz { get; set; }
            internal double? ErsterModulanfang { get; set; }
            internal double XVersatz { get; set; }
            internal Bandplan Bandplan { get; set; }
            internal IReadOnlyList<Querstrassenplan> Querstrassen { get; set; }
            internal Modulplanung Modulplanung { get; set; }
            internal Dictionary<int, Spaltenplan> Reihenplaene { get; set; }
            internal Dictionary<(int Band, int Spalte), int> Buchten { get; set; }
            internal int BuchtenNachNaht { get; set; }
            internal int OhneFahrgassenkante { get; set; }
            internal int KnappeFahrgassenenden { get; set; }
            internal double Fahrgassenendschiefe { get; set; }
        }

        /**
         * Plant das Bandraster einer Teilflaeche - MITTIG, ohne Phasensuche.
         *
         * HIER STAND EINE SUCHE, UND DER NUTZER HAT SIE ABGEWAEHLT.
         *
         * Sie probierte zwoelf Y-Phasen ueber eine Rasterperiode und zwoelf
         * X-Lagen im seitlichen Rest durch und nahm die buchtenreichste -
         * eine Verschiebung galt ab 8 (Y) beziehungsweise 4 (X) zusaetzlichen
         * Buchten als gewonnen. Sie war dafuer da, dass das mittige Raster
         * bei gedrehten Teilen nicht durch die duennen Ecken laeuft; nur 30
         * bis 61 Prozent des Huellkastens waren dort nutzbar.
         *
         * Der Preis war sichtbar. Nutzerbefund am 2026-09-01:
         *
         *   "Koennen wir noch machen, dass die Strassen mittig platziert
         *    werden, also im gesamten Strassengrid. Manchmal haengt eine
         *    Strasse zu weit rechts und dadurch die andere auch, und es
         *    sieht optisch unstimmig aus."
         *
         * Auf Rueckfrage: es betrifft BEIDE Strassenarten - die langen
         * Fahrgassen und die kurzen Verbindungsstrassen - und auf die Frage,
         * was ihm wichtiger ist, hat er geantwortet: *immer mittig, auch wenn
         * Buchten wegfallen.* Das deckt sich mit seiner aelteren Ansage
         * "Optik vor Buchtenzahl".
         *
         * GEMESSEN, was das kostet (`--protokoll PLT-0F6ED0F3 136.8 75.0`,
         * zehn Schnitte): in acht Faellen null - dort lag das Raster ohnehin
         * mittig. In zwei Faellen 13 und 11 Buchten von rund 340, also
         * 3 bis 4 Prozent.
         *
         * Der ungetrennte Bau ruft diese Klasse gar nicht auf; dort legen
         * `Planung.Baender` und `Planung.Querstrassenmitten` beides von
         * vornherein mittig.
         */
        private static void PlaneRaster(
            Rasterteil teil,
            Zelleneinstellungen einstellungen,
            IReadOnlyList<Nahtplan> naehte,
            double halbeNahtbreite,
            ref int naechsteBuchtId)
        {
            if (teil.Innenkontur.Length < 3
                || teil.Mittellinienkontur.Length < 3)
                return;
            var minX = teil.Innenkontur.Min(punkt => punkt.X);
            var maxX = teil.Innenkontur.Max(punkt => punkt.X);
            var minY = teil.Innenkontur.Min(punkt => punkt.Y);
            var maxY = teil.Innenkontur.Max(punkt => punkt.Y);
            var modulhoehe = 2 * einstellungen.Buchttiefe
                + einstellungen.Fahrgassenbreite;
            if (maxX - minX <= einstellungen.Buchtbreite
                || maxY - minY + 1e-6 < modulhoehe)
                return;

            if (ParkingGeometry.LiveAn)
                ProtokolliereHuellkasten(teil, minX, maxX, minY, maxY);

            var mindestabstand = einstellungen.Querstrassenkappen
                ? einstellungen.Cs2Mindestkante
                : 0.0;
            Rasterkandidat Plane(
                double? ersterModulanfang,
                double versatz,
                double xVersatz = 0)
            {
                var bandplan = Layoutplanung.Baender(
                    minY,
                    maxY,
                    einstellungen.Buchttiefe,
                    einstellungen.Fahrgassenbreite,
                    einstellungen.Gruenstreifenbreite,
                    ersterModulanfang);
                var planregister = new Linienregister();
                var querstrassen = Layoutplanung.GlobaleQuerstrassen(
                    minX,
                    maxX,
                    einstellungen.Buchtbreite,
                    einstellungen.Querstrassenbreite,
                    einstellungen.Querstrassenabstand,
                    einstellungen.Querstrassenkappen,
                    planregister);
                if (Math.Abs(xVersatz) > 1e-9)
                    querstrassen = querstrassen.Select(strasse =>
                    {
                        var anfang = strasse.Anfang + xVersatz;
                        var ende = strasse.Ende + xVersatz;
                        return new Querstrassenplan(
                            strasse.Id,
                            strasse.Mitte + xVersatz,
                            anfang,
                            ende,
                            planregister.Querstrassenkante(
                                anfang, strasse.Id, "links"),
                            planregister.Querstrassenkante(
                                ende, strasse.Id, "rechts"));
                    }).ToArray();
                Ringlosplan ringlos = null;
                if (!einstellungen.Randstrassen)
                    ringlos = Ringlosplan.Plane(bandplan, teil.Innenkontur,
                        teil.PolygonWelt.Select(teil.Rahmen.NachLokal).ToArray(),
                        Array.Empty<Zufahrtsvorgabe>(), einstellungen, planregister, out querstrassen);
                var modulplanung = Layoutplanung.PlaneModule(
                    bandplan,
                    querstrassen,
                    teil.Innenkontur,
                    teil.Mittellinienkontur,
                    einstellungen.Buchtbreite,
                    mindestabstand,
                    einstellungen.Querstrassenkappen, einstellungen.Querstrassenabstand,
                    einstellungen.Randstrassen, ringlos);
                var reihenplaene = modulplanung.Plaene
                    .SelectMany(plan => plan.Reihenplaene)
                    .ToDictionary(paar => paar.Key, paar => paar.Value);
                var buchten = Layoutplanung.GueltigeBuchten(
                    modulplanung.Plaene,
                    teil.Innenkontur,
                    mindestabstand,
                    false);
                var buchtenNachNaht = BuchtenNachNaht(
                    teil, bandplan, reihenplaene, buchten,
                    naehte, halbeNahtbreite);
                var kantenabstaende = buchtenNachNaht.Select(kandidat => (
                    kandidat.Key,
                    Abstand: Fahrgassenkantenabstand(
                        bandplan, reihenplaene, teil.Mittellinienkontur,
                        kandidat.Key))).ToArray();
                return new Rasterkandidat
                {
                    Ringlos = ringlos,
                    Versatz = versatz,
                    ErsterModulanfang = ersterModulanfang,
                    XVersatz = xVersatz,
                    Bandplan = bandplan,
                    Querstrassen = querstrassen,
                    Modulplanung = modulplanung,
                    Reihenplaene = reihenplaene,
                    Buchten = buchten,
                    BuchtenNachNaht = buchtenNachNaht.Count,
                    OhneFahrgassenkante = kantenabstaende.Count(eintrag =>
                        double.IsNegativeInfinity(eintrag.Abstand)),
                    // Am Ende wird die Fahrgasse um ihre halbe Breite mit der
                    // hoeheren Rand- oder Nahtstrasse verschnitten. Nur diese
                    // konstruktive Breite ist hier Reserve, keine neue
                    // Abstandszahl.
                    KnappeFahrgassenenden = kantenabstaende.Count(eintrag =>
                        eintrag.Abstand
                            < einstellungen.Fahrgassenbreite / 2 - 1e-6),
                    Fahrgassenendschiefe = Fahrgassenendschiefe(
                        bandplan, reihenplaene, teil.Innenkontur,
                        teil.Mittellinienkontur, buchtenNachNaht),
                };
            }

            // Die Mitte, und nichts anderes. Siehe den Kopf dieser Datei.
            var bester = Plane(null, double.NaN);

            teil.Ringlos = bester.Ringlos;
            teil.Bandplan = bester.Bandplan;
            teil.Querstrassen = bester.Querstrassen;
            teil.Modulplanung = bester.Modulplanung;
            teil.Reihenplaene = bester.Reihenplaene;
            var kandidaten = ParkingGeometry.LiveAn
                ? Layoutplanung.GueltigeBuchten(
                    teil.Modulplanung.Plaene,
                    teil.Innenkontur,
                    mindestabstand)
                : bester.Buchten;
            if (ParkingGeometry.LiveAn)
                ParkingGeometry.Live("  rasterwahl " + teil.Vorgabe.Index
                    + " | mittig"
                    + " | gueltig " + kandidaten.Count
                    + " | nach Naht " + bester.BuchtenNachNaht
                    + " | ohne Fahrgassenkante "
                    + bester.OhneFahrgassenkante
                    + " | knappe Gassenenden "
                    + bester.KnappeFahrgassenenden
                    + " | Endschiefe " + bester.Fahrgassenendschiefe.ToString(
                        "F2", System.Globalization.CultureInfo.InvariantCulture));

            foreach (var kandidat in kandidaten.OrderBy(paar => paar.Key.Band)
                         .ThenBy(paar => paar.Key.Spalte))
            {
                var band = teil.Bandplan.Baender.First(
                    eintrag => eintrag.Id == kandidat.Key.Band);
                var spalte = teil.Reihenplaene[band.Id].Spalten.First(
                    eintrag => eintrag.Id == kandidat.Key.Spalte);
                if (teil.Ringlos != null)
                {
                    var modul = teil.Bandplan.Module.First(m => m.Reihen.Any(r => r.Id == band.Id));
                    var ecken = new[] { new Punkt(spalte.Anfang, band.Anfang), new Punkt(spalte.Ende, band.Anfang),
                        new Punkt(spalte.Ende, band.Ende), new Punkt(spalte.Anfang, band.Ende) };
                    if (!teil.Ringlos.BuchtFrei(ecken, modul.Fahrgassenmitte)) continue;
                }
                if (BeruehrtNaht(teil, band, spalte, naehte, halbeNahtbreite))
                    continue;
                var id = naechsteBuchtId++;
                teil.Buchten.Add(kandidat.Key, id);
                teil.Buchtgeometrie.Add(id, new[]
                {
                    new Punkt(spalte.Anfang, band.Anfang),
                    new Punkt(spalte.Ende, band.Anfang),
                    new Punkt(spalte.Ende, band.Ende),
                    new Punkt(spalte.Anfang, band.Ende),
                });
            }
        }

        private static IReadOnlyList<
            KeyValuePair<(int Band, int Spalte), int>> BuchtenNachNaht(
            Rasterteil teil,
            Bandplan bandplan,
            IReadOnlyDictionary<int, Spaltenplan> reihenplaene,
            IReadOnlyDictionary<(int Band, int Spalte), int> buchten,
            IReadOnlyList<Nahtplan> naehte,
            double halbeNahtbreite)
        {
            var ausgabe = new List<
                KeyValuePair<(int Band, int Spalte), int>>();
            foreach (var kandidat in buchten)
            {
                var band = bandplan.Baender.First(
                    eintrag => eintrag.Id == kandidat.Key.Band);
                var spalte = reihenplaene[band.Id].Spalten.First(
                    eintrag => eintrag.Id == kandidat.Key.Spalte);
                if (!BeruehrtNaht(teil, band, spalte, naehte, halbeNahtbreite))
                    ausgabe.Add(kandidat);
            }
            return ausgabe;
        }

        private static double Fahrgassenkantenabstand(
            Bandplan bandplan,
            IReadOnlyDictionary<int, Spaltenplan> reihenplaene,
            IReadOnlyList<Punkt> mittellinienkontur,
            (int Band, int Spalte) schluessel)
        {
            var band = bandplan.Baender.First(eintrag =>
                eintrag.Id == schluessel.Band);
            var modul = bandplan.Module.First(eintrag =>
                eintrag.ErsteReihe.Id == band.Id
                || eintrag.ZweiteReihe.Id == band.Id);
            var spalte = reihenplaene[band.Id].Spalten.First(eintrag =>
                eintrag.Id == schluessel.Spalte);
            var abstand = double.NegativeInfinity;
            foreach (var abschnitt in WaagerechteAbschnitte(
                         mittellinienkontur, modul.Fahrgassenmitte))
            {
                if (abschnitt.Anfang > spalte.Anfang + 1e-6
                    || abschnitt.Ende < spalte.Ende - 1e-6) continue;
                abstand = Math.Max(abstand, Math.Min(
                    spalte.Anfang - abschnitt.Anfang,
                    abschnitt.Ende - spalte.Ende));
            }
            return abstand;
        }

        private static double Fahrgassenendschiefe(
            Bandplan bandplan,
            IReadOnlyDictionary<int, Spaltenplan> reihenplaene,
            IReadOnlyList<Punkt> innenkontur,
            IReadOnlyList<Punkt> mittellinienkontur,
            IReadOnlyList<KeyValuePair<(int Band, int Spalte), int>> buchten)
        {
            var maximum = 0.0;
            foreach (var gruppe in buchten.GroupBy(paar => paar.Key.Band))
            {
                var band = bandplan.Baender.First(eintrag =>
                    eintrag.Id == gruppe.Key);
                var modul = bandplan.Module.First(eintrag =>
                    eintrag.ErsteReihe.Id == band.Id
                    || eintrag.ZweiteReihe.Id == band.Id);
                var vorne = modul.ErsteReihe.Id == band.Id
                    ? band.Ende : band.Anfang;
                var spalten = gruppe.Select(paar =>
                    reihenplaene[band.Id].Spalten.First(eintrag =>
                        eintrag.Id == paar.Key.Spalte)).ToArray();
                var min = spalten.Min(spalte => spalte.Anfang);
                var max = spalten.Max(spalte => spalte.Ende);
                var mitte = WaagerechteAbschnitte(
                        mittellinienkontur, modul.Fahrgassenmitte)
                    .FirstOrDefault(abschnitt => abschnitt.Anfang <= min + 1e-6
                        && abschnitt.Ende >= max - 1e-6);
                var rand = WaagerechteAbschnitte(innenkontur, vorne)
                    .FirstOrDefault(abschnitt => abschnitt.Anfang <= min + 1e-6
                        && abschnitt.Ende >= max - 1e-6);
                if (mitte == default || rand == default) continue;
                var links = Math.Max(0, rand.Anfang - mitte.Anfang);
                var rechts = Math.Max(0, mitte.Ende - rand.Ende);
                var schiefe = Math.Abs(links - rechts);
                maximum = Math.Max(maximum, schiefe);
            }
            return maximum;
        }

        private static bool BeruehrtNaht(
            Rasterteil teil,
            Bandabschnitt band,
            Spaltenabschnitt spalte,
            IReadOnlyList<Nahtplan> naehte,
            double halbeNahtbreite) =>
            naehte.Any(naht => naht.GehoertZu(teil.Vorgabe.Index)
                && RechteckBeruehrtNaht(
                    teil.Rahmen, band, spalte, naht, halbeNahtbreite));

        private static void ProtokolliereHuellkasten(
            Rasterteil teil,
            double minX,
            double maxX,
            double minY,
            double maxY)
        {
            double Ring(IReadOnlyList<Punkt> ring)
            {
                var flaeche = 0.0;
                for (var i = 0; i < ring.Count; i++)
                {
                    var a = ring[i];
                    var b = ring[(i + 1) % ring.Count];
                    flaeche += a.X * b.Y - b.X * a.Y;
                }
                return Math.Abs(flaeche) / 2;
            }

            var kasten = (maxX - minX) * (maxY - minY);
            ParkingGeometry.Live("  teilraster " + teil.Vorgabe.Index
                + " | Winkel " + teil.Vorgabe.Winkel.ToString("F1",
                    System.Globalization.CultureInfo.InvariantCulture)
                + " | Teil " + Ring(teil.PolygonWelt).ToString("F0")
                + " m2 | Innenkontur " + Ring(teil.Innenkontur).ToString("F0")
                + " m2 (" + teil.Innenkontur.Length + " Ecken)"
                + " | Huellkasten " + kasten.ToString("F0")
                + " m2 | Fuellgrad "
                + (kasten <= 0 ? 0 : Ring(teil.Innenkontur) / kasten * 100)
                    .ToString("F0") + " %");
        }
    }
}
