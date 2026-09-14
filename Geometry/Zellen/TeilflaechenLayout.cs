using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingLotTool.Geometry.Zellen
{
    /**
     * Plant mehrere innere Raster in ihren eigenen Rahmen. Aussenring,
     * Randreihe und Zufahrten bleiben Eigentum des einmaligen Gesamtbaus in
     * <see cref="Layoutbauer"/>.
     */
    internal sealed partial class Teilflaechenlayout
    {
        internal sealed class Rasterteilung
        {
            internal Linie Linie { get; set; }
            internal Punkt Anfang { get; set; }
            internal Punkt Ende { get; set; }
            internal IReadOnlyList<int> Teilindizes { get; set; }
        }

        private sealed class Nahtplan
        {
            internal int Nummer { get; set; }
            internal int ErstesTeil { get; set; }
            internal int ZweitesTeil { get; set; }
            internal Punkt AnfangWelt { get; set; }
            internal Punkt EndeWelt { get; set; }

            internal bool GehoertZu(int index) =>
                ErstesTeil == index || ZweitesTeil == index;

            internal bool Enthaelt(Punkt welt, double halbeBreite) =>
                Geometrie.AbstandPunktStrecke(
                    welt, AnfangWelt, EndeWelt) <= halbeBreite + 1e-6;
        }

        private sealed class Rasterteil
        {
            internal Ringlosplan Ringlos;
            internal Teilflaechenrahmenvorgabe Vorgabe { get; set; }
            internal Rahmen Rahmen { get; set; }
            internal Punkt[] PolygonWelt { get; set; }
            internal Punkt[] PolygonWurzel { get; set; }
            internal Punkt[] Innenkontur { get; set; }
            internal Punkt[] Mittellinienkontur { get; set; }
            internal Bandplan Bandplan { get; set; }
            internal IReadOnlyList<Querstrassenplan> Querstrassen { get; set; }
            internal Modulplanung Modulplanung { get; set; }
            internal Dictionary<(int Band, int Spalte), int> Buchten { get; set; }
            internal Dictionary<int, Punkt[]> Buchtgeometrie { get; set; }
            internal Dictionary<int, Spaltenplan> Reihenplaene { get; set; }
            internal int QuerstrassenIdBasis { get; set; }

            /**
             * Der Huellkasten von `PolygonWurzel`, einmal gerechnet. Der
             * Live-Vergleich mit der alten Schwerpunktzuordnung braucht ihn;
             * die wirksame Zuordnung benutzt die exakten Linienintervalle.
             */
            internal double MinX { get; set; }
            internal double MinY { get; set; }
            internal double MaxX { get; set; }
            internal double MaxY { get; set; }

            internal bool Enthaelt(Punkt welt) =>
                Geometrie.EnthaeltOderRand(PolygonWelt, welt);
        }

        private readonly Rahmen _wurzelrahmen;
        private readonly double _halbeNahtbreite;
        private readonly IReadOnlyList<Rasterteil> _teile;
        private readonly Dictionary<int, Rasterteil> _teileNachIndex;
        private readonly IReadOnlyList<Nahtplan> _naehte;
        private readonly Dictionary<int,
            IReadOnlyList<(double Anfang, double Ende)>> _teilungsintervalle;
        private int _durchLinienlageErgaenzt;
        private int _durchLinienlageVerworfen;

        private Teilflaechenlayout(
            Rahmen wurzelrahmen,
            double halbeNahtbreite,
            IReadOnlyList<Rasterteil> teile,
            IReadOnlyList<Nahtplan> naehte,
            IReadOnlyList<Rasterteilung> teilungen,
            IReadOnlyList<Rasterstrassenplan> strassen,
            IReadOnlyDictionary<int, Punkt[]> buchten,
            int buchtenzahl)
        {
            _wurzelrahmen = wurzelrahmen;
            _halbeNahtbreite = halbeNahtbreite;
            _teile = teile;
            _teileNachIndex = new Dictionary<int, Rasterteil>();
            foreach (var teil in teile)
            {
                var minX = double.MaxValue;
                var minY = double.MaxValue;
                var maxX = double.MinValue;
                var maxY = double.MinValue;
                foreach (var punkt in teil.PolygonWurzel)
                {
                    if (punkt.X < minX) minX = punkt.X;
                    if (punkt.X > maxX) maxX = punkt.X;
                    if (punkt.Y < minY) minY = punkt.Y;
                    if (punkt.Y > maxY) maxY = punkt.Y;
                }
                teil.MinX = minX;
                teil.MinY = minY;
                teil.MaxX = maxX;
                teil.MaxY = maxY;
                _teileNachIndex[teil.Vorgabe.Index] = teil;
            }
            _naehte = naehte;
            _teilungsintervalle = new Dictionary<int,
                IReadOnlyList<(double Anfang, double Ende)>>();
            foreach (var teilung in teilungen)
            {
                var intervalle = new List<(double Anfang, double Ende)>();
                foreach (var teilindex in teilung.Teilindizes)
                    if (_teileNachIndex.TryGetValue(teilindex, out var teil))
                        intervalle.AddRange(SegmentparameterImPolygon(
                            teilung.Anfang,
                            teilung.Ende,
                            teil.PolygonWurzel));
                _teilungsintervalle[teilung.Linie.Id] = intervalle;
            }
            Teilungen = teilungen;
            Strassen = strassen;
            Buchten = buchten;
            Buchtenzahl = buchtenzahl;
        }

        internal IReadOnlyList<Rasterteilung> Teilungen { get; }
        internal IReadOnlyList<Rasterstrassenplan> Strassen { get; }
        internal IReadOnlyDictionary<int, Punkt[]> Buchten { get; }
        internal int Buchtenzahl { get; }
        internal Bandplan ErsterBandplan => _teile
            .Select(teil => teil.Bandplan)
            .FirstOrDefault(plan => plan != null);
        internal IReadOnlyList<Modulspaltenplan> Modulplaene => _teile
            .Where(teil => teil.Modulplanung != null)
            .SelectMany(teil => teil.Modulplanung.Plaene)
            .ToArray();
        internal IReadOnlyList<Querstrassenpruefung> Querstrassenpruefungen =>
            _teile.Where(teil => teil.Modulplanung != null)
                .SelectMany(teil => teil.Modulplanung.Pruefungen)
                .ToArray();

        internal static Teilflaechenlayout Plane(
            Zelleneinstellungen einstellungen,
            Rahmen wurzelrahmen,
            IReadOnlyList<Punkt> randstrasseninnenrand,
            IReadOnlyList<Punkt> randstrassenmittellinie,
            Linienregister linienregister)
        {
            var vorgaben = (einstellungen.Teilflaechen
                    ?? Array.Empty<Teilflaechenrahmenvorgabe>())
                .Where(vorgabe => vorgabe?.PunkteWelt != null
                    && vorgabe.PunkteWelt.Length >= 3)
                .OrderBy(vorgabe => vorgabe.Index)
                .ToArray();
            if (vorgaben.Length == 0) return null;

            var halbeNahtbreite = Math.Max(0, einstellungen.Querstrassenbreite / 2);
            var naehte = (einstellungen.Teilflaechennaehte
                    ?? Array.Empty<Teilflaechennahtvorgabe>())
                .Where(naht => naht != null
                    && Geometrie.Laenge(naht.EndeWelt - naht.AnfangWelt) > 1e-6
                    && halbeNahtbreite > 0)
                .Select((naht, nummer) => new Nahtplan
                {
                    Nummer = nummer,
                    ErstesTeil = naht.ErstesTeil,
                    ZweitesTeil = naht.ZweitesTeil,
                    AnfangWelt = naht.AnfangWelt,
                    EndeWelt = naht.EndeWelt,
                }).ToArray();

            var innenWelt = randstrasseninnenrand
                .Select(wurzelrahmen.NachWelt).ToArray();
            var mitteWelt = randstrassenmittellinie
                .Select(wurzelrahmen.NachWelt).ToArray();
            var teile = new List<Rasterteil>();
            var naechsteBuchtId = 0;
            foreach (var vorgabe in vorgaben)
            {
                var polygonWelt = GegenUhrzeigersinn(vorgabe.PunkteWelt);
                /*
                 * NUR AN DEN TRENNKANTEN BESCHNEIDEN, nicht am ganzen
                 * Polygon - siehe `Teilflaechenrahmenvorgabe.Trennkanten`.
                 * Ohne Trennkanten (ein einziges Teil, oder eine aeltere
                 * Vorgabe) bleibt es beim alten Weg; bei einem konvexen Teil
                 * liefern beide dasselbe.
                 */
                var innenSchnittWelt = vorgabe.Trennkanten != null
                    && vorgabe.Trennkanten.Length != 0
                    ? SchneideMitHalbebenen(innenWelt, vorgabe.Trennkanten)
                    : SchneideMitKonvexemPolygon(innenWelt, polygonWelt);
                var mitteSchnittWelt = vorgabe.Trennkanten != null
                    && vorgabe.Trennkanten.Length != 0
                    ? SchneideMitHalbebenen(mitteWelt, vorgabe.Trennkanten)
                    : SchneideMitKonvexemPolygon(mitteWelt, polygonWelt);
                var teilrahmen = Geometrie.Reihenrahmen(
                    polygonWelt, vorgabe.Winkel);
                var teil = new Rasterteil
                {
                    Vorgabe = vorgabe,
                    Rahmen = teilrahmen,
                    PolygonWelt = polygonWelt,
                    PolygonWurzel = polygonWelt
                        .Select(wurzelrahmen.NachLokal).ToArray(),
                    Innenkontur = innenSchnittWelt
                        .Select(teilrahmen.NachLokal).ToArray(),
                    Mittellinienkontur = mitteSchnittWelt
                        .Select(teilrahmen.NachLokal).ToArray(),
                    Buchten = new Dictionary<(int, int), int>(),
                    Buchtgeometrie = new Dictionary<int, Punkt[]>(),
                    Reihenplaene = new Dictionary<int, Spaltenplan>(),
                    QuerstrassenIdBasis = 100000 * (teile.Count + 1),
                };
                PlaneRaster(
                    teil, einstellungen, naehte, halbeNahtbreite,
                    ref naechsteBuchtId);
                teile.Add(teil);
            }

            var teilungen = PlaneTeilungen(
                teile, naehte, halbeNahtbreite, wurzelrahmen, linienregister);
            var strassen = PlaneStrassen(
                teile, naehte, mitteWelt, wurzelrahmen);
            var buchten = teile.SelectMany(teil => teil.Buchtgeometrie)
                .ToDictionary(
                    paar => paar.Key,
                    paar => teile.First(teil =>
                            teil.Buchtgeometrie.ContainsKey(paar.Key))
                        .Buchtgeometrie[paar.Key]
                        .Select(punkt => Wurzelpunkt(
                            teile.First(kandidat => kandidat.Buchtgeometrie
                                .ContainsKey(paar.Key)),
                            wurzelrahmen,
                            punkt))
                        .ToArray());
            return new Teilflaechenlayout(
                wurzelrahmen,
                halbeNahtbreite,
                teile,
                naehte,
                teilungen,
                strassen,
                buchten,
                naechsteBuchtId);
        }

        private static IReadOnlyList<Rasterteilung> PlaneTeilungen(
            IReadOnlyList<Rasterteil> teile,
            IReadOnlyList<Nahtplan> naehte,
            double halbeNahtbreite,
            Rahmen wurzelrahmen,
            Linienregister linienregister)
        {
            var ausgabe = new List<Rasterteilung>();
            foreach (var teil in teile.Where(kandidat => kandidat.Bandplan != null))
            {
                var minX = teil.Innenkontur.Min(punkt => punkt.X) - 1;
                var maxX = teil.Innenkontur.Max(punkt => punkt.X) + 1;
                var minY = teil.Innenkontur.Min(punkt => punkt.Y) - 1;
                var maxY = teil.Innenkontur.Max(punkt => punkt.Y) + 1;
                foreach (var y in teil.Bandplan.InnereGrenzen
                             .Concat(teil.Ringlos?.Korridore.SelectMany(w => w.Ecken).Select(v => v.Y) ?? Array.Empty<double>())
                             .Distinct().Where(y => y > minY + 1 && y < maxY - 1))
                    FuegeTeilungHinzu(
                        ausgabe,
                        linienregister,
                        Wurzelpunkt(teil, wurzelrahmen, new Punkt(minX, y)),
                        Wurzelpunkt(teil, wurzelrahmen, new Punkt(maxX, y)),
                        Linienart.BandY,
                        $"Teil {teil.Vorgabe.Index} Band y={y:R}",
                        new[] { teil.Vorgabe.Index });

                var xGrenzen = teil.Reihenplaene.Values
                    .SelectMany(plan => plan.Spalten)
                    .SelectMany(spalte => new[] { spalte.Anfang, spalte.Ende })
                    .Concat(teil.Modulplanung.Querstrassenstuecke.SelectMany(
                        stueck => new[]
                        {
                            stueck.Querstrasse.Anfang,
                            stueck.Querstrasse.Ende,
                        }))
                    .Where(x => x > minX + 1 && x < maxX - 1)
                    .Distinct()
                    .OrderBy(x => x);
                foreach (var x in xGrenzen)
                    FuegeTeilungHinzu(
                        ausgabe,
                        linienregister,
                        Wurzelpunkt(teil, wurzelrahmen, new Punkt(x, minY)),
                        Wurzelpunkt(teil, wurzelrahmen, new Punkt(x, maxY)),
                        Linienart.RasterX,
                        $"Teil {teil.Vorgabe.Index} Raster x={x:R}",
                        new[] { teil.Vorgabe.Index });
            }

            foreach (var naht in naehte)
            {
                var richtung = naht.EndeWelt - naht.AnfangWelt;
                var laenge = Geometrie.Laenge(richtung);
                if (laenge <= 1e-6) continue;
                var normale = new Punkt(-richtung.Y / laenge, richtung.X / laenge);
                foreach (var vorzeichen in new[] { -1.0, 1.0 })
                {
                    var versatz = normale * (vorzeichen * halbeNahtbreite);
                    FuegeTeilungHinzu(
                        ausgabe,
                        linienregister,
                        wurzelrahmen.NachLokal(naht.AnfangWelt + versatz),
                        wurzelrahmen.NachLokal(naht.EndeWelt + versatz),
                        Linienart.Querstrassenkante,
                        $"Teilflaechennaht {naht.Nummer} Rand",
                        new[] { naht.ErstesTeil, naht.ZweitesTeil });
                }
            }
            return ausgabe;
        }

        private static void FuegeTeilungHinzu(
            List<Rasterteilung> ausgabe,
            Linienregister register,
            Punkt a,
            Punkt b,
            Linienart art,
            string name,
            IReadOnlyList<int> teile)
        {
            if (Geometrie.Laenge(b - a) <= 1e-6) return;
            ausgabe.Add(new Rasterteilung
            {
                Linie = register.FreieGerade(a, b, art, name),
                Anfang = a,
                Ende = b,
                Teilindizes = teile,
            });
        }

        private static IReadOnlyList<Rasterstrassenplan> PlaneStrassen(
            IReadOnlyList<Rasterteil> teile,
            IReadOnlyList<Nahtplan> naehte,
            IReadOnlyList<Punkt> mittellinienringWelt,
            Rahmen wurzelrahmen)
        {
            var ausgabe = new List<Rasterstrassenplan>();
            foreach (var teil in teile.Where(kandidat => kandidat.Modulplanung != null))
            {
                foreach (var modul in teil.Bandplan.Module)
                    foreach (var abschnitt in WaagerechteAbschnitte(
                                 teil.Mittellinienkontur,
                                 modul.Fahrgassenmitte))
                        ausgabe.Add(Strasse(
                            teil,
                            wurzelrahmen,
                            Zellart.Fahrgasse,
                            new Punkt(abschnitt.Anfang, modul.Fahrgassenmitte),
                            new Punkt(abschnitt.Ende, modul.Fahrgassenmitte),
                            false));
                foreach (var stueck in teil.Modulplanung.Querstrassenstuecke)
                    ausgabe.Add(Strasse(
                        teil,
                        wurzelrahmen,
                        Zellart.Querstrasse,
                        stueck.Anfang,
                        stueck.Ende,
                        false));
            }

            foreach (var naht in naehte)
            {
                var abschnitte = SegmentabschnitteImPolygon(
                    naht.AnfangWelt, naht.EndeWelt, mittellinienringWelt);
                if (abschnitte.Count == 0) continue;
                var laengster = abschnitte.OrderByDescending(abschnitt =>
                    Geometrie.Laenge(abschnitt.Ende - abschnitt.Anfang)).First();
                ausgabe.Add(new Rasterstrassenplan
                {
                    Art = Zellart.Querstrasse,
                    Anfang = wurzelrahmen.NachLokal(laengster.Anfang),
                    Ende = wurzelrahmen.NachLokal(laengster.Ende),
                    TeilIndex = naht.ErstesTeil,
                    Teilflaechenverbindung = true,
                });
            }
            return ausgabe;
        }

        private static Rasterstrassenplan Strasse(
            Rasterteil teil,
            Rahmen wurzelrahmen,
            Zellart art,
            Punkt anfang,
            Punkt ende,
            bool verbindung) => new Rasterstrassenplan
            {
                Art = art,
                Anfang = Wurzelpunkt(teil, wurzelrahmen, anfang),
                Ende = Wurzelpunkt(teil, wurzelrahmen, ende),
                TeilIndex = teil.Vorgabe.Index,
                Teilflaechenverbindung = verbindung,
            };

        internal bool Klassifiziere(
            Punkt wurzelpunkt,
            bool kappen,
            out Zellart art,
            out int? buchtId,
            out int? querstrassenId)
        {
            art = Zellart.Restgruen;
            buchtId = null;
            querstrassenId = null;
            var welt = _wurzelrahmen.NachWelt(wurzelpunkt);
            var naht = _naehte.FirstOrDefault(kandidat =>
                kandidat.Enthaelt(welt, _halbeNahtbreite));
            if (naht != null)
            {
                art = Zellart.Querstrasse;
                querstrassenId = 1000000 + naht.Nummer;
                return true;
            }

            var teil = _teile.FirstOrDefault(kandidat => kandidat.Enthaelt(welt));
            if (teil?.Bandplan == null) return teil != null;
            var lokal = teil.Rahmen.NachLokal(welt);
            var band = teil.Bandplan.Baender.FirstOrDefault(kandidat =>
                lokal.Y >= kandidat.Anfang - 1e-6
                    && lokal.Y < kandidat.Ende - 1e-6);
            if (band == null) return true;
            if (band.Art == Zellart.Bucht)
            {
                Spaltenplan plan;
                if (!teil.Reihenplaene.TryGetValue(band.Id, out plan)) return true;
                var spalte = plan.Spalten.FirstOrDefault(kandidat =>
                    lokal.X >= kandidat.Anfang - 1e-6
                        && lokal.X < kandidat.Ende - 1e-6);
                if (spalte == null) return true;
                if (spalte.Art == Spaltenart.Querstrasse)
                {
                    art = Zellart.Querstrasse;
                    querstrassenId = teil.QuerstrassenIdBasis
                        + spalte.QuerstrassenId;
                }
                else
                {
                    int id;
                    if (spalte.Art == Spaltenart.Buchtfeld
                        && teil.Buchten.TryGetValue((band.Id, spalte.Id), out id))
                    {
                        art = Zellart.Bucht;
                        buchtId = id;
                    }
                    /*
                     * KEIN BELAG OHNE BUCHT.
                     *
                     * Hier stand `kappen ? Zellart.Kappe : band.Art`. Ohne
                     * Kappen behielt eine Zelle also die Rolle ihres BANDES -
                     * und ein Buchtband ist Belag. Wo keine gueltige Bucht
                     * stand, blieb damit gepflasterte Flaeche liegen, auf der
                     * nichts ist: gemessen 455 m2 an einem Bau des Nutzers,
                     * die Flaeche von rund 26 Buchten. Er hat genau darauf
                     * gezeigt - breite Streifen ohne eine einzige weisse
                     * Linie, daneben gleich breite markierte Reihen.
                     *
                     * Der Belag folgte dem RASTER statt dem, was daraufsteht.
                     * Jetzt folgt er dem, was daraufsteht: keine Bucht, kein
                     * Belag.
                     *
                     * `Kappe` ist dabei die richtige Rolle - es ist genau
                     * das, was am Reihenende ohnehin entsteht, nur eben auch
                     * mitten in der Reihe, wenn dort nichts hinpasst.
                     */
                    else art = Zellart.Kappe;
                }
                return true;
            }

            if (band.Art != Zellart.Fahrgasse)
            {
                var quer = teil.Modulplanung.Querstrassenstuecke.FirstOrDefault(
                    stueck => stueck.Enthaelt(lokal.X, lokal.Y));
                if (quer != null)
                {
                    art = Zellart.Querstrasse;
                    querstrassenId = teil.QuerstrassenIdBasis
                        + quer.Querstrasse.Id;
                    return true;
                }
            }
            /*
             * DIE GASSE BEKOMMT IHREN BELAG - AUF GANZER LAENGE.
             *
             * Hier stand vom 2026-09-01 bis zum selben Tag eine Regel
             * `TraegtDieGasse`: eine Fahrgassenzelle wurde gruen, wenn
             * links und rechts von IHR keine Bucht stand. Gedacht war sie
             * gegen breite Streifen ohne Markierung.
             *
             * Sie hat den Fehler am falschen Ende bekaempft. Ihr Mass war
             * die einzelne Zelle, und am ENDE einer Gasse - genau dort, wo
             * sie auf die Randstrasse oder die Verbindungsstrasse trifft -
             * steht neben ihr planmaessig keine Bucht mehr. Also faerbte
             * die Regel ausgerechnet das Anschlussstueck gruen. Der Nutzer
             * hat es fotografiert: Fahrgassen, die kurz vor der Strasse
             * aufhoeren und ins Gruen laufen, aussen wie innen am Schnitt.
             *
             * GEMESSEN (`--protokoll PLT-BB119CEB 36.4 75.0`, neue Zeile
             * "Gras unter Gassen"): ohne Ausrichtung 0 m2, mit Ausrichtung
             * 106 bis 193 m2 - je nach Schnitt 15 bis 27 laufende Meter
             * Gasse auf Gras. Ohne die Regel: 0 m2 bei JEDEM Schnitt, bei
             * unveraenderter Buchtenzahl.
             *
             * Die Streifen, gegen die sie half, gibt es seit dem Wurzelfix
             * an `GehoertTeilungZuPolygon` nicht mehr: ungenutzter Belag
             * liegt auch ohne sie bei 0 m2. Eine Bandvariante der Regel
             * ("ein Band ganz ohne Bucht wird gruen") wurde gemessen und
             * ist Zeile fuer Zeile identisch mit gar keiner Regel - kein
             * Band ist ohne Bucht. Sie war ein Reparaturpass fuer einen
             * behobenen Fehler.
             */
            art = band.Art;
            return true;
        }

        private static Punkt Wurzelpunkt(
            Rasterteil teil,
            Rahmen wurzelrahmen,
            Punkt lokal) => wurzelrahmen.NachLokal(teil.Rahmen.NachWelt(lokal));

        private static Punkt[] GegenUhrzeigersinn(IReadOnlyList<Punkt> polygon)
        {
            var ausgabe = polygon.ToArray();
            if (Geometrie.Vorzeichenflaeche(ausgabe) < 0) Array.Reverse(ausgabe);
            return ausgabe;
        }

        /** Sutherland-Hodgman: jede Grenze steht vor dem Zellbau fest. */
        /**
         * Schneidet gegen eine Liste von Halbebenen (A->B, innen ist LINKS).
         *
         * Dasselbe Verfahren wie beim konvexen Polygon - nur bestimmt hier
         * der Aufrufer, WELCHE Kanten schneiden duerfen. Genau darin lag der
         * Fehler: an einem konkaven Teil schnitten auch die Umrisskanten mit,
         * und ihre Halbebenen reichen weit ins Teil hinein.
         */
        private static Punkt[] SchneideMitHalbebenen(
            IReadOnlyList<Punkt> quelle,
            IReadOnlyList<(Punkt A, Punkt B)> kanten)
        {
            var ausgabe = quelle.ToList();
            foreach (var kante in kanten)
            {
                if (ausgabe.Count < 3) break;
                var richtung = kante.B - kante.A;
                if (Geometrie.Laenge(richtung) < 1e-9) continue;
                var eingabe = ausgabe;
                ausgabe = new List<Punkt>();
                var vorher = eingabe[eingabe.Count - 1];
                var vorherSeite = Geometrie.Kreuz(richtung, vorher - kante.A);
                foreach (var aktuell in eingabe)
                {
                    var aktuellSeite = Geometrie.Kreuz(
                        richtung, aktuell - kante.A);
                    var vorherInnen = vorherSeite >= -1e-6;
                    var aktuellInnen = aktuellSeite >= -1e-6;
                    if (vorherInnen != aktuellInnen)
                    {
                        var nenner = vorherSeite - aktuellSeite;
                        var t = nenner == 0 ? 0 : vorherSeite / nenner;
                        FuegeEindeutigHinzu(
                            ausgabe, vorher + (aktuell - vorher) * t);
                    }
                    if (aktuellInnen) FuegeEindeutigHinzu(ausgabe, aktuell);
                    vorher = aktuell;
                    vorherSeite = aktuellSeite;
                }
                EntferneDoppeltenSchlusspunkt(ausgabe);
            }
            return ausgabe.ToArray();
        }

        private static Punkt[] SchneideMitKonvexemPolygon(
            IReadOnlyList<Punkt> quelle,
            IReadOnlyList<Punkt> klammer)
        {
            var ausgabe = quelle.ToList();
            for (var k = 0; k < klammer.Count && ausgabe.Count >= 3; k++)
            {
                var a = klammer[k];
                var b = klammer[(k + 1) % klammer.Count];
                var richtung = b - a;
                var eingabe = ausgabe;
                ausgabe = new List<Punkt>();
                var vorher = eingabe[eingabe.Count - 1];
                var vorherSeite = Geometrie.Kreuz(richtung, vorher - a);
                foreach (var aktuell in eingabe)
                {
                    var aktuellSeite = Geometrie.Kreuz(richtung, aktuell - a);
                    var vorherInnen = vorherSeite >= -1e-6;
                    var aktuellInnen = aktuellSeite >= -1e-6;
                    if (vorherInnen != aktuellInnen)
                    {
                        var nenner = vorherSeite - aktuellSeite;
                        var t = nenner == 0 ? 0 : vorherSeite / nenner;
                        FuegeEindeutigHinzu(
                            ausgabe, vorher + (aktuell - vorher) * t);
                    }
                    if (aktuellInnen) FuegeEindeutigHinzu(ausgabe, aktuell);
                    vorher = aktuell;
                    vorherSeite = aktuellSeite;
                }
                EntferneDoppeltenSchlusspunkt(ausgabe);
            }
            return ausgabe.Count >= 3 ? ausgabe.ToArray() : Array.Empty<Punkt>();
        }

        private static void FuegeEindeutigHinzu(List<Punkt> punkte, Punkt punkt)
        {
            if (punkte.Count == 0
                || Geometrie.Laenge(punkte[punkte.Count - 1] - punkt) > 1e-6)
                punkte.Add(punkt);
        }

        private static void EntferneDoppeltenSchlusspunkt(List<Punkt> punkte)
        {
            if (punkte.Count > 1
                && Geometrie.Laenge(punkte[0] - punkte[punkte.Count - 1]) <= 1e-6)
                punkte.RemoveAt(punkte.Count - 1);
        }

        private static bool RechteckBeruehrtNaht(
            Rahmen rahmen,
            Bandabschnitt band,
            Spaltenabschnitt spalte,
            Nahtplan naht,
            double halbeBreite)
        {
            var ecken = new[]
            {
                rahmen.NachWelt(new Punkt(spalte.Anfang, band.Anfang)),
                rahmen.NachWelt(new Punkt(spalte.Ende, band.Anfang)),
                rahmen.NachWelt(new Punkt(spalte.Ende, band.Ende)),
                rahmen.NachWelt(new Punkt(spalte.Anfang, band.Ende)),
            };
            if (ecken.Any(ecke => naht.Enthaelt(ecke, halbeBreite))) return true;
            if (Geometrie.EnthaeltOderRand(ecken, naht.AnfangWelt)
                || Geometrie.EnthaeltOderRand(ecken, naht.EndeWelt)) return true;
            for (var i = 0; i < ecken.Length; i++)
                if (StreckenSchneiden(
                    ecken[i], ecken[(i + 1) % ecken.Length],
                    naht.AnfangWelt, naht.EndeWelt)) return true;
            return false;
        }

        private static bool StreckenSchneiden(Punkt a, Punkt b, Punkt c, Punkt d)
        {
            var ab = b - a;
            var cd = d - c;
            var nenner = Geometrie.Kreuz(ab, cd);
            if (Math.Abs(nenner) <= 1e-9) return false;
            var t = Geometrie.Kreuz(c - a, cd) / nenner;
            var u = Geometrie.Kreuz(c - a, ab) / nenner;
            return t >= -1e-6 && t <= 1 + 1e-6
                && u >= -1e-6 && u <= 1 + 1e-6;
        }

        private static IReadOnlyList<(double Anfang, double Ende)>
            WaagerechteAbschnitte(IReadOnlyList<Punkt> ring, double y)
        {
            var schnitte = new List<double>();
            for (var i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];
                if ((a.Y > y) == (b.Y > y)) continue;
                schnitte.Add(a.X + (y - a.Y) * (b.X - a.X) / (b.Y - a.Y));
            }
            schnitte.Sort();
            var ausgabe = new List<(double, double)>();
            for (var i = 0; i + 1 < schnitte.Count; i += 2)
                if (schnitte[i + 1] > schnitte[i] + 1e-6)
                    ausgabe.Add((schnitte[i], schnitte[i + 1]));
            return ausgabe;
        }

        private static IReadOnlyList<(Punkt Anfang, Punkt Ende)>
            SegmentabschnitteImPolygon(
                Punkt anfang,
                Punkt ende,
                IReadOnlyList<Punkt> polygon)
        {
            var richtung = ende - anfang;
            return SegmentparameterImPolygon(anfang, ende, polygon)
                .Select(abschnitt => (
                    anfang + richtung * abschnitt.Anfang,
                    anfang + richtung * abschnitt.Ende))
                .ToArray();
        }

        private static IReadOnlyList<(double Anfang, double Ende)>
            SegmentparameterImPolygon(
                Punkt anfang,
                Punkt ende,
                IReadOnlyList<Punkt> polygon)
        {
            var richtung = ende - anfang;
            var parameter = new List<double> { 0, 1 };
            for (var i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                var kante = b - a;
                var nenner = Geometrie.Kreuz(richtung, kante);
                if (Math.Abs(nenner) <= 1e-9) continue;
                var t = Geometrie.Kreuz(a - anfang, kante) / nenner;
                var u = Geometrie.Kreuz(a - anfang, richtung) / nenner;
                if (t >= -1e-6 && t <= 1 + 1e-6
                    && u >= -1e-6 && u <= 1 + 1e-6)
                    parameter.Add(Math.Max(0, Math.Min(1, t)));
            }
            var sortiert = parameter.Distinct().OrderBy(t => t).ToArray();
            var ausgabe = new List<(double, double)>();
            for (var i = 0; i + 1 < sortiert.Length; i++)
            {
                var von = sortiert[i];
                var bis = sortiert[i + 1];
                if (bis <= von + 1e-9) continue;
                var mitte = anfang + richtung * ((von + bis) / 2);
                if (!Geometrie.EnthaeltOderRand(polygon, mitte)) continue;
                ausgabe.Add((von, bis));
            }
            return ausgabe;
        }
    }
}
