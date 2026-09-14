using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingLotTool.Geometry.Zellen
{
    internal sealed class Knotenfabrik
    {
        private int _naechsteId;

        internal Knoten Neu(Punkt punkt) => new Knoten(_naechsteId++, punkt);
        internal int Anzahl => _naechsteId;
    }

    internal sealed class Linienregister
    {
        private int _naechsteId;

        internal Linie Aussenkante(int index) =>
            new Linie(_naechsteId++, Linienart.Aussenkante, $"Aussenkante {index}");

        internal Linie Teilungsnaht(int index, Linie fortgesetzteLinie) =>
            new Linie(
                _naechsteId++,
                Linienart.Teilungsnaht,
                $"Teilungsnaht {index}",
                tragendeGeradenId: fortgesetzteLinie.TragendeGeradenId);

        internal Linie RasterX(double x) =>
            new Linie(_naechsteId++, Linienart.RasterX, $"x={x:R}",
                1, 0, x, Schnittachse.X, x);

        internal Linie BandY(double y) =>
            new Linie(_naechsteId++, Linienart.BandY, $"y={y:R}",
                0, 1, y, Schnittachse.Y, y);

        internal Linie Querstrassenkante(double x, int strasse, string seite) =>
            new Linie(_naechsteId++, Linienart.Querstrassenkante,
                $"Querstrasse {strasse} {seite} x={x:R}",
                1, 0, x, Schnittachse.X, x);

        internal Linie Innenrand(Punkt a, Punkt b, int index) =>
            GeradeDurch(a, b, Linienart.Innenrand, $"Innenrand {index}");

        internal Linie Randstrassenkante(Punkt a, Punkt b, int index) =>
            GeradeDurch(a, b, Linienart.Randstrassenkante,
                $"Randstrassenkante {index}");

        internal Linie Randstrasseninnenkante(Punkt a, Punkt b, int index) =>
            GeradeDurch(a, b, Linienart.Randstrasseninnenkante,
                $"Randstrasseninnenkante {index}");

        internal Linie Randstrassenstoss(Punkt a, Punkt b, int index) =>
            GeradeDurch(a, b, Linienart.Randstrassenstoss,
                $"Randstrassenstoss {index}");

        internal Linie Randbandstoss(Punkt a, Punkt b, int index) =>
            GeradeDurch(a, b, Linienart.Randbandstoss,
                $"Randbandstoss {index}");

        internal Linie Randbuchtgrenze(
            Punkt a,
            Punkt b,
            int randkante,
            int grenze) =>
            GeradeDurch(a, b, Linienart.Randbuchtgrenze,
                $"Randbucht Kante {randkante} Grenze {grenze}");

        internal Linie Zufahrtskante(
            Punkt punkt,
            Punkt richtung,
            int zufahrt,
            string seite) =>
            GeradeDurch(punkt, punkt + richtung, Linienart.Zufahrtskante,
                $"Zufahrt {zufahrt} {seite}");

        /** Beliebig gedrehte, vor dem Zellbau geplante Raster- oder Nahtlinie. */
        internal Linie FreieGerade(
            Punkt a,
            Punkt b,
            Linienart art,
            string name) => GeradeDurch(a, b, art, name);

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
            var achsenwert = achse == Schnittachse.X
                ? a.X
                : achse == Schnittachse.Y ? a.Y : 0;
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
                throw new InvalidOperationException("The normalised site is not counter-clockwise.");
            return polygon;
        }

        internal static Polygon AusKnotenUndLinien(
            IReadOnlyList<Knoten> knoten,
            IReadOnlyList<Linie> linien)
        {
            if (knoten.Count != linien.Count)
                throw new InvalidOperationException(
                    "Node count and line count do not match.");
            var polygon = new Polygon(knoten.Select((knotenwert, index) =>
                new Ecke(knotenwert, linien[index])));
            if (Geometrie.Vorzeichenflaeche(polygon.Punkte) <= 0)
                throw new InvalidOperationException("A sub-surface is not counter-clockwise.");
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

        internal Konvexzerlegung(
            Knotenfabrik knotenfabrik,
            Linienregister linienregister)
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
                if (!treffer.HasValue)
                    throw new InvalidOperationException(
                        $"Reflex corner {reflex} could not be split.");

                var kantenindex = treffer.Value.Kantenindex;
                var parameter = treffer.Value.Parameter;
                var punkt = treffer.Value.Punkt;
                var naht = _linienregister.Teilungsnaht(
                    _nahtindex++, polygon.Linie(reflex - 1));
                Polygon erstes;
                Polygon zweites;
                if (parameter == 0 || parameter == 1)
                {
                    var zielindex = parameter == 0
                        ? kantenindex
                        : Geometrie.Mod(kantenindex + 1, polygon.Anzahl);
                    // Bei allen 100 T-Abbruechen traf die zweite Reflexecke
                    // bitgleich den Knoten der ersten Naht (76x Parameter 1,
                    // 24x Parameter 0). Der Knoten wird deshalb als Ringindex
                    // benutzt; ihn erneut als Kanteninneres einzufuegen wuerde
                    // eine Nullkante erzeugen.
                    erstes = TeilZwischenKnoten(
                        polygon, reflex, zielindex, naht);
                    zweites = TeilZwischenKnoten(
                        polygon, zielindex, reflex, naht);
                }
                else
                {
                    var schnittknoten = _knotenfabrik.Neu(punkt);
                    erstes = ErstesTeil(
                        polygon, reflex, kantenindex, schnittknoten, naht);
                    zweites = ZweitesTeil(
                        polygon, reflex, kantenindex, schnittknoten, naht);
                }
                var ergebnis = Zerlege(erstes);
                ergebnis.AddRange(Zerlege(zweites));
                return ergebnis;
            }

            if (!Geometrie.IstKonvex(polygon))
                throw new InvalidOperationException(
                    "The decomposition ended with a non-convex part.");
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
            (int Kantenindex, double Parameter, Punkt Punkt)? bester = null;

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

        private static Polygon TeilZwischenKnoten(
            Polygon polygon,
            int anfang,
            int ende,
            Linie naht)
        {
            var knoten = new List<Knoten> { polygon.Knoten(anfang) };
            var linien = new List<Linie>();
            var index = Geometrie.Mod(anfang, polygon.Anzahl);
            var ziel = Geometrie.Mod(ende, polygon.Anzahl);
            while (index != ziel)
            {
                linien.Add(polygon.Linie(index));
                index = Geometrie.Mod(index + 1, polygon.Anzahl);
                knoten.Add(polygon.Knoten(index));
            }
            linien.Add(naht);
            return Polygonfabrik.AusKnotenUndLinien(knoten, linien);
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
        internal List<string> Warnungen { get; } = new List<string>();
        private readonly Knotenfabrik _knotenfabrik;
        private readonly Dictionary<SchnittSchluessel, Knoten> _schnittpunkte =
            new Dictionary<SchnittSchluessel, Knoten>();
        private readonly Dictionary<
            (int Quelllinie, int Schnittlinie, int Knoten, int KanteA, int KanteB),
            Beobachtung> _beobachtungen =
                new Dictionary<
                    (int Quelllinie, int Schnittlinie, int Knoten, int KanteA, int KanteB),
                    Beobachtung>();

        private sealed class Beobachtung
        {
            internal Linie Quelllinie { get; set; }
            internal Linie Schnittlinie { get; set; }
            internal Knoten Knoten { get; set; }
            internal KantenSchluessel Quellkante { get; set; }
            internal int Anfragen { get; set; }
        }

        internal Polygonteiler(Knotenfabrik knotenfabrik)
        {
            _knotenfabrik = knotenfabrik;
        }

        internal IReadOnlyList<Schnittbeobachtung> Schnittbeobachtungen =>
            _beobachtungen.Values
                .Select(wert => new Schnittbeobachtung(
                    wert.Quelllinie,
                    wert.Schnittlinie,
                    wert.Knoten,
                    wert.Quellkante,
                    wert.Anfragen))
                .ToArray();

        internal void VervollstaendigeNachbarkanten(
            IReadOnlyList<Polygon> polygone,
            Linie schnittlinie)
        {
            var einsaetze = Schnittbeobachtungen
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
            /*
             * ERST FRAGEN, OB DIESES POLYGON UEBERHAUPT GEMEINT SEIN KANN.
             *
             * Die Schleife lief bis zum 2026-09-01 als Einsatz x Polygon x
             * Kante - fuer JEDEN Einsatz ueber ALLE Polygone. Beim Ausrichten
             * sind das gemessen 5630 Einsaetze und 5383 Polygone, also rund
             * 121 Millionen Kantenvergleiche; 283 der 313 ms dieser Funktion
             * gingen dorthin, und sie war der groesste Einzelposten des
             * gesamten Ausrichtbaus.
             *
             * Ein Polygon kann nur getroffen sein, wenn eine seiner Kanten
             * ueberhaupt als Quellkante vorkommt. Diese Frage kostet vier
             * Nachschlagevorgaenge; sie wirft praktisch alle Polygone hinaus,
             * bevor die Einsatzschleife ueberhaupt beginnt.
             *
             * DAS ERGEBNIS BLEIBT GLEICH, und zwar nachweisbar: eingefuegt
             * wird nur in ein Polygon, das eine passende Kante HAT. Ein
             * Polygon ohne solche Kante konnte also auch vorher nie etwas
             * bekommen - und da Einfuegungen nur eigene Kanten aufteilen,
             * kann es auch spaeter keine bekommen. Die beiden Schleifen sind
             * ausserdem vertauscht: Einfuegungen in verschiedene Polygone
             * beruehren sich nicht, die Reihenfolge der Einsaetze INNERHALB
             * eines Polygons bleibt erhalten.
             */
            var quellkanten = new HashSet<KantenSchluessel>();
            foreach (var einsatz in einsaetze) quellkanten.Add(einsatz.Quellkante);

            foreach (var polygon in polygone)
            {
                var betroffen = false;
                for (var i = 0; i < polygon.Anzahl && !betroffen; i++)
                    betroffen = quellkanten.Contains(KantenSchluessel.Von(
                        polygon.Knoten(i), polygon.Knoten(i + 1)));
                if (!betroffen) continue;

                foreach (var einsatz in einsaetze)
                    for (var i = 0; i < polygon.Anzahl; i++)
                    {
                        var a = polygon.Knoten(i);
                        var b = polygon.Knoten(i + 1);
                        if (KantenSchluessel.Von(a, b) != einsatz.Quellkante
                            || a.Id == einsatz.Knoten.Id
                            || b.Id == einsatz.Knoten.Id)
                            continue;
                        // Ein begrenzter Schnitt teilt nur die Randreihenzelle.
                        // Der Nachbar jenseits der Kontur braucht denselben
                        // Knoten als kollineare Ecke, sonst entsteht ein T-Stoss.
                        polygon.Ecken.Insert(
                            i + 1, new Ecke(einsatz.Knoten, einsatz.Quelllinie));
                        break;
                    }
            }
        }

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
                SchneideHalbebene(polygon, schnittlinie, true),
                SchneideHalbebene(polygon, schnittlinie, false),
            }.Where(teil => teil != null).ToArray();
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
                Geometrie.Mittelwert(polygon) - segmentanfang,
                richtung) / laengenquadrat;
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
                    parameter.Add(Geometrie.Skalar(
                        a - segmentanfang, richtung) / laengenquadrat);
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

        private readonly struct Eingang
        {
            internal Eingang(Knoten knoten, Linie eingangsLinie)
            {
                Knoten = knoten;
                EingangsLinie = eingangsLinie;
            }

            internal Knoten Knoten { get; }
            internal Linie EingangsLinie { get; }
        }

        private Polygon SchneideHalbebene(
            Polygon polygon,
            Linie schnittlinie,
            bool minusBehalten)
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

            var letzter = ausgabe.Count - 1;
            if (ausgabe.Count > 1
                && ausgabe[0].Knoten.Id == ausgabe[letzter].Knoten.Id)
            {
                ausgabe[0] = new Eingang(
                    ausgabe[0].Knoten, ausgabe[letzter].EingangsLinie);
                ausgabe.RemoveAt(letzter);
            }
            if (ausgabe.Count < 3)
            {
                Warnungen.Add($"Halbebenenschnitt {schnittlinie.Name} (ID {schnittlinie.Id}): "
                    + $"entartete Scherbe mit {ausgabe.Count} Ecken verworfen, Flaeche 0 m2.");
                return null;
            }

            var ecken = new List<Ecke>();
            for (var i = 0; i < ausgabe.Count; i++)
            {
                var linieZumNaechsten =
                    ausgabe[(i + 1) % ausgabe.Count].EingangsLinie;
                ecken.Add(new Ecke(ausgabe[i].Knoten, linieZumNaechsten));
            }
            var ergebnis = new Polygon(ecken);
            var punkte = ergebnis.Punkte.ToArray();
            // Flaeche UND Fehlerschranke brauchen denselben lokalen Ursprung.
            // Im RZ-Absturzfall vom 10.09.2026 verwarf die globale Schranke
            // (1,23e-9 m2) zwei gueltige Zellen von je 1,00e-9 m2. Ihre Loecher
            // liess die Lochtrennung mit 22,6-Mikrometer-Streifen aufgehen,
            // die als float zu Nullringen wurden. Lokal gerechnet: 0 Loecher.
            var ursprung = punkte[0];
            var flaeche = Geometrie.Vorzeichenflaeche(punkte.Select(p => p - ursprung));
            var produktbetrag = 0.0;
            for (var i = 0; i < punkte.Length; i++)
            {
                var a = punkte[i] - ursprung; var b = punkte[(i + 1) % punkte.Length] - ursprung;
                produktbetrag += Math.Abs(a.X * b.Y) + Math.Abs(a.Y * b.X);
            }
            var rundungsgrenze = (punkte.Length + 2) * 2.2204460492503131e-16 * produktbetrag;
            if (flaeche < -rundungsgrenze)
                throw new InvalidOperationException(
                    $"The half-plane cut lost its orientation. Flaeche {flaeche:R} m2, Rundungsgrenze {rundungsgrenze:R} m2.");
            if (Math.Abs(flaeche) <= rundungsgrenze)
            {
                /*
                 * DIE MELDUNG BLEIBT fuer numerisch unbestimmte Scherben.
                 * Die alte Warnungsprobe in --zufahrtsschnitt verlangt zwei
                 * Meldungen fuer gueltige Rechteckhaelften von je 0,00002 m2
                 * bei (1e6, 1e6). Seit der lokalen Schranke bleiben beide
                 * erhalten: 0 Meldungen. Das ist keine Rundungsentartung.
                 *
                 * Dass eine Scherbe von 0,00 m2 dem Nutzer trotzdem nicht als
                 * roter Fehler in der Statusleiste begegnen soll, ist eine
                 * Frage der ANZEIGE und wird in `ParkingLotToolSystem`
                 * entschieden, nicht hier.
                 */
                Warnungen.Add($"Halbebenenschnitt {schnittlinie.Name} (ID {schnittlinie.Id}): "
                    + $"entartete Scherbe verworfen, Flaeche {flaeche:R} m2, Rundungsgrenze {rundungsgrenze:R} m2.");
                return null;
            }
            return ergebnis;
        }

        private static void FuegeHinzu(List<Eingang> ausgabe, Eingang eingang)
        {
            if (ausgabe.Count != 0
                && ausgabe[ausgabe.Count - 1].Knoten.Id == eingang.Knoten.Id)
                return;
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
            Knoten vorhanden;
            if (_schnittpunkte.TryGetValue(schluessel, out vorhanden))
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
            switch (schnittlinie.Achse)
            {
                case Schnittachse.X:
                    punkt = new Punkt(schnittlinie.Achsenwert, punkt.Y);
                    break;
                case Schnittachse.Y:
                    punkt = new Punkt(punkt.X, schnittlinie.Achsenwert);
                    break;
            }
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
            var schluessel = (
                quelllinie.Id,
                schnittlinie.Id,
                knoten.Id,
                quellkante.Klein,
                quellkante.Gross);
            Beobachtung beobachtung;
            if (!_beobachtungen.TryGetValue(schluessel, out beobachtung))
            {
                beobachtung = new Beobachtung
                {
                    Quelllinie = quelllinie,
                    Schnittlinie = schnittlinie,
                    Knoten = knoten,
                    Quellkante = quellkante,
                };
                _beobachtungen.Add(schluessel, beobachtung);
            }
            beobachtung.Anfragen++;
        }
    }
}
