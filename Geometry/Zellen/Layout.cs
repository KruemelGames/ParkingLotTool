using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ParkingLotTool.Geometry.Zellen
{
    internal static class Layoutbauer
    {
        private sealed class Ringabschnittsplan
        {
            internal IReadOnlyList<Punkt>[] Abschnitte { get; set; }
            internal IReadOnlyList<(Linie Linie, Punkt Aussen, Punkt Innen)> Stoesse
                { get; set; }

            internal int AbschnittBei(Punkt punkt)
            {
                for (var i = 0; i < Abschnitte.Length; i++)
                    if (Geometrie.Enthaelt(Abschnitte[i], punkt)) return i;
                throw new InvalidOperationException(
                    $"Ringzelle bei ({punkt.X:R}, {punkt.Y:R}) "
                    + "liegt in keinem vorab geplanten Abschnitt.");
            }
        }

        internal static Bauergebnis Baue(
            Formdefinition form,
            Zelleneinstellungen einstellungen,
            IReadOnlyList<Zufahrtsvorgabe> zufahrten = null)
        {
            var uhr = Stopwatch.StartNew();
            /*
             * PHASENUHREN FUER DEN LIVE-LOG.
             *
             * Sie laufen NUR, wenn jemand mitliest - sonst kostet der ganze
             * Block einen Nullvergleich je Abschnitt. Gemessen werden die
             * sechs Abschnitte, die es hier ueberhaupt gibt; feiner zu messen
             * hiesse raten, wo es weh tut, und genau das soll der Log ja
             * beantworten.
             */
            var takt = ParkingGeometry.LiveAn ? Stopwatch.StartNew() : null;
            var phasen = takt == null ? null : new List<string>();
            void Phase(string name)
            {
                if (takt == null) return;
                phasen.Add(name + " " + takt.Elapsed.TotalMilliseconds
                    .ToString("F0",
                        System.Globalization.CultureInfo.InvariantCulture));
                takt.Restart();
            }
            zufahrten = zufahrten ?? Array.Empty<Zufahrtsvorgabe>();
            var weltpunkte = form.Punkte.ToList();
            if (Geometrie.Vorzeichenflaeche(weltpunkte) < 0) weltpunkte.Reverse();
            var rahmen = Geometrie.Reihenrahmen(
                weltpunkte, einstellungen.Reihenwinkel);
            var lokalpunkte = weltpunkte.Select(rahmen.NachLokal).ToArray();
            /*
             * DIE BAULANDFLAECHEN IN DEN WURZELRAHMEN - einmal, hier.
             *
             * Der Zellenkern rechnet durchweg im Reihenrahmen; die Flaeche
             * kommt vom Werkzeug in WELTkoordinaten. Der erste Anlauf hat
             * beides direkt verglichen, und `Enthaelt` traf nie zu: die
             * Flaeche kam an (Zaehler sagte 1), wirkte aber nicht - 348
             * Buchten mit und ohne, in allen sechs Faellen gleich.
             *
             * Der Winkel muss dabei mitgedreht werden, nicht nur die Ecke.
             */
            var zoningLokal = (einstellungen.Zoningflaechen
                    ?? Array.Empty<Zoningvorgabe>())
                .Select(vorgabe =>
                {
                    /*
                     * DER INNERE Eckpunkt, nicht der aeussere.
                     *
                     * `Ecken()` liefert seit dem Rand die AUFGEWEITETE
                     * Flaeche. Wer den daraus gewonnenen Punkt wieder als
                     * `Ecke` einsetzt und `Rand` mitgibt, rechnet den Rand
                     * ein zweites Mal dazu - die freigehaltene Flaeche waere
                     * doppelt so weit gewachsen wie gewollt. Die Richtung
                     * darf aus den aeusseren Ecken kommen, sie ist dieselbe.
                     */
                    /*
                     * DER WINKEL WIRD ABGEZOGEN, NICHT ZURUECKGERECHNET.
                     *
                     * Vorher stand hier `Atan2` ueber zwei umgerechnete
                     * Eckpunkte. Das ist rechnerisch dasselbe, aber nur so
                     * genau wie der Abstand dieser beiden Punkte es zulaesst
                     * - und der Fehler waechst mit der Kantenlaenge. Im Test
                     * gemessen: die Ringkante lag bis zu 17 cm neben ihrer
                     * Sollachse (v=56,17 bei Tiefe 56,00; 10x6 unter 40
                     * Grad).
                     *
                     * DAS IST KEINE SCHOENHEITSFRAGE. Der Nutzer beschreibt
                     * genau dieses Verhalten von CS2: *"da wurde angeblich
                     * eine Strasse mit 90 Grad gebaut, aber der war dann
                     * doch 89,999999 Grad, und dann schneidet er Tiles
                     * weg."* Das Zonenraster verzeiht keine Winkelreste.
                     *
                     * Der lokale Winkel ist der Weltwinkel minus dem Winkel
                     * des Rahmens. Eine einzige Subtraktion, unabhaengig von
                     * der Groesse der Flaeche.
                     */
                    var rahmenwinkel = Math.Atan2(
                        rahmen.XAchse.Y, rahmen.XAchse.X) * 180.0 / Math.PI;
                    return new Zoningvorgabe
                    {
                        Ecke = rahmen.NachLokal(vorgabe.Ecke),
                        Spalten = vorgabe.Spalten,
                        Reihen = vorgabe.Reihen,
                        Winkel = vorgabe.Winkel - rahmenwinkel,
                        Rand = vorgabe.Rand,
                        Aussen = vorgabe.Aussen,
                    };
                })
                .ToArray();
            // Die Randzoning-Linien in denselben Rahmen. Sie tragen keinen
            // eigenen Winkel - zwei Punkte drehen sich vollstaendig mit.
            var randzoningLokal = (einstellungen.Randzoning
                    ?? Array.Empty<(Punkt A, Punkt B)>())
                .Select(l => (A: rahmen.NachLokal(l.A), B: rahmen.NachLokal(l.B)))
                .ToArray();
            var lokaleZufahrten = zufahrten
                .Select(zufahrt => zufahrt.NachLokal(rahmen))
                .ToArray();

            var knotenfabrik = new Knotenfabrik();
            var linienregister = new Linienregister();
            var areal = Polygonfabrik.Areal(
                lokalpunkte, knotenfabrik, linienregister);
            var zerlegung = new Konvexzerlegung(knotenfabrik, linienregister);
            var teile = zerlegung.Zerlege(areal);
            if (teile.Any(teil => !Geometrie.IstKonvex(teil)))
                throw new InvalidOperationException(
                    "Not all sub-polygons are convex.");

            var innenrand = Layoutplanung.Innenrand(
                lokalpunkte, einstellungen.Randabstand);
            // Eine geplante Zufahrt schneidet das aeussere Gruenband bereits
            // vor der Vereinigung auf. Ohne Zufahrt braucht auch dieser Ring
            // zwei konstruierte Abschnitte; im Lauf ueber 203 baubare Formen
            // waren sonst alle 203 Gruenringe auf Lochtrennung angewiesen.
            // Ringlos teilen bereits die Gassenachsen die Flaechen. Der alte
            // Eckstoss erzeugte am Nutzerpolygon zusaetzlich einen 0,000008-m-
            // Ring am Innenrand; ohne diesen redundanten Schnitt: 0 Kurzringe.
            var randbandabschnitte = einstellungen.Randstrassen && lokaleZufahrten.Length == 0
                ? PlaneRingabschnitte(
                    lokalpunkte,
                    innenrand,
                    linienregister.Randbandstoss)
                : null;
            // Die Zufahrt endet nach es + sl = 6,9 m bei den gemessenen
            // CS2-Werten an der bereits vorhandenen Aussenkante der
            // Randstrasse. Diese Grenze gehoert zum Grundnetz; eine Zufahrt
            // fuegt deshalb nur ihre zwei Seitenlinien hinzu.
            var randstrassenrand = !einstellungen.Randstrassen
                ? innenrand : Layoutplanung.Innenrand(
                    lokalpunkte, einstellungen.Zufahrtstiefe);
            // Die 7,0-m-Randstrasse liegt zwischen 6,9 und 13,9 m. Ihre
            // Mittellinie bei 10,4 m wird spaeter als Netzachse ausgegeben;
            // nur die beiden Fahrbahnkanten teilen die Materialzellen.
            var randstrassenmittellinie = !einstellungen.Randstrassen && randzoningLokal.Length == 0
                ? innenrand : Layoutplanung.Innenrand(
                    lokalpunkte, einstellungen.Randstrassenmittellinientiefe);
            var randstrasseninnenrand = !einstellungen.Randstrassen
                ? innenrand : Layoutplanung.Innenrand(
                    lokalpunkte, einstellungen.Randstrasseninnentiefe);

            /*
             * DIE RZ-STRASSE IST BREITER ALS DIE RANDSTRASSE.
             *
             * Die Randstrasse ist 7 m breit, die Zoning-Strasse 8 - und beide
             * liegen auf derselben Achse. Wer den Belag im Korridor der
             * Randstrasse einfaerbt, laesst links und rechts je einen halben
             * Meter blank. Genau das hat der Test am 2026-09-04 gemessen:
             * 89,6 % Deckung, also 7,17 von 8 m.
             *
             * Deshalb zwei eigene Ringe, eine halbe Zoning-Strassenbreite
             * beiderseits der Achse. Sie entstehen nur, wenn es Randzoning
             * gibt - ohne kostet die Zeile nichts.
             */
            var rzHalb = ParkingGeometry.ZoningStrassenbreite / 2;
            var rzAchse = einstellungen.Randstrassenmittellinientiefe;
            var randzoningAussen = !einstellungen.Randstrassen || randzoningLokal.Length == 0 ? null
                : Layoutplanung.Innenrand(lokalpunkte, rzAchse - rzHalb);
            var randzoningInnen = !einstellungen.Randstrassen || randzoningLokal.Length == 0 ? null
                : Layoutplanung.Innenrand(lokalpunkte, rzAchse + rzHalb);
            // `rzAbschnitte` entsteht erst NACH dem Bandplan - die Gassen
            // sind die Randzoning-Strasse, also muessen sie zuerst stehen.
            // Siehe PLAN-RZ-an-Fahrgassen.md.
            var randstrassenabschnitte = !einstellungen.Randstrassen ? null : PlaneRingabschnitte(
                randstrassenrand,
                randstrasseninnenrand,
                linienregister.Randstrassenstoss);
            var rasterrand = einstellungen.Randstrassen ? randstrasseninnenrand : innenrand;
            var minX = rasterrand.Min(punkt => punkt.X);
            var maxX = rasterrand.Max(punkt => punkt.X);
            var minY = rasterrand.Min(punkt => punkt.Y);
            var maxY = rasterrand.Max(punkt => punkt.Y);
            /*
             * DIE BAENDER KENNEN DAS RANDZONING NICHT MEHR.
             *
             * Hier stand `Randzoningabschnitt.Plane(...)` als Grenze - sie
             * hielt die Gassen von der erzwungenen RZ-Strasse fern. Seit die
             * Gasse die RZ-Strasse IST, waere das genau verkehrt: die Grenze
             * schoebe die Gasse von der Stelle weg, an der sie gebraucht wird.
             *
             * Mit Randstrassen bleibt die Grenze, dort gibt es weiterhin eine
             * eigene Randstrasse.
             */
            var bandplan = einstellungen.Randstrassen || randzoningLokal.Length > 0
                ? Ringbandplanung.Baender(minY, maxY, einstellungen, einstellungen.Randstrassen ? randstrassenmittellinie : innenrand,
                    einstellungen.Randstrassen
                        ? Randzoningabschnitt.Plane(lokalpunkte, randzoningLokal, rzAchse, randstrassenmittellinie)
                        : Array.Empty<Randzoningabschnitt>(),
                    einstellungen.Randstrassen)
                : Layoutplanung.Baender(
                minY,
                maxY,
                einstellungen.Buchttiefe,
                einstellungen.Fahrgassenbreite,
                einstellungen.Gruenstreifenbreite, null, !einstellungen.Randstrassen);
            /*
             * DIE GASSEN SIND DIE RANDZONING-STRASSE.
             *
             * Entwurf des Nutzers vom 2026-09-10: statt eine Strasse zu
             * erzwingen, wird die naechstliegende Fahrgasse zur RZ-Strasse.
             * Eine schraege Kante bekommt dabei eine Treppe - je Gassenhoehe
             * ein Abschnitt. Die ganze Vorlage steht in
             * PLAN-RZ-an-Fahrgassen.md.
             *
             * Findet sich keine Gasse, bleibt es beim erzwungenen Weg: ein
             * Abbruch, der den Nutzer am Bauen hindert, ist schlimmer als eine
             * zusaetzliche Strasse.
             */
            var rzAbschnitte = Array.Empty<Randzoningabschnitt>();

            var globaleQuerstrassen = Layoutplanung.GlobaleQuerstrassen(
                minX,
                maxX,
                einstellungen.Buchtbreite,
                einstellungen.Querstrassenbreite,
                // Der Abstand allein entscheidet ueber die Querstrassen. Hier
                // stand `Querstrassen ? Abstand : unendlich` - und weil dieser
                // Schalter am Kappen-Regler des Panels hing, loeschte das
                // Abschalten der Kappen die Verbindungsstrassen gleich mit.
                einstellungen.Querstrassenabstand,
                einstellungen.Querstrassenkappen,
                linienregister);
            Ringlosplan ringlos = null;
            if (!einstellungen.Randstrassen)
            {
                ringlos = Ringlosplan.Plane(bandplan, innenrand, lokalpunkte,
                    lokaleZufahrten, einstellungen, linienregister, out globaleQuerstrassen, zoningLokal, randzoningLokal);

                /*
                 * DIE ABSCHNITTE KOMMEN AUS DEN ECHTEN GASSEN.
                 *
                 * Erst hier, nach dem ringlosen Plan - und nicht schon aus den
                 * Baendern. Ein Band bringt nicht immer eine Fahrgasse hervor:
                 * an der Diagonalform lag ein Band bei 123,2 m, die hoechste
                 * WIRKLICHE Gasse aber bei 101,9 m. Aus den Baendern geplant,
                 * sass die RZ-Strasse dort auf einer Hoehe ohne Gasse - also
                 * wieder eine zusaetzliche Strasse, genau das, was der Entwurf
                 * abschaffen soll.
                 *
                 * Findet sich keine Gasse, bleibt es beim erzwungenen Weg: ein
                 * Abbruch, der den Nutzer am Bauen hindert, ist schlimmer als
                 * eine zusaetzliche Strasse.
                 */
                if (randzoningLokal.Length > 0)
                {
                    // Mit ihrer Ausdehnung, nicht nur mit ihrer Hoehe: die
                    // Strasse endet, wo die Gasse endet.
                    var gassenlagen = ringlos.Gassen
                        .Select(g => (Y: g.A.Y,
                            Von: Math.Min(g.A.X, g.B.X),
                            Bis: Math.Max(g.A.X, g.B.X)))
                        .ToArray();
                    var quermitten = ringlos.Querwege
                        .Select(q => (q.Anfang.X + q.Ende.X) / 2)
                        .Distinct().OrderBy(x => x).ToArray();
                    rzAbschnitte = Randzoningabschnitt.PlaneAnGassen(
                        lokalpunkte, randzoningLokal, gassenlagen, minX, maxX,
                        quermitten);
                    if (rzAbschnitte.Length == 0)
                        rzAbschnitte = Randzoningabschnitt.Plane(
                            lokalpunkte, randzoningLokal, rzAchse,
                            randstrassenmittellinie);
                }
            }
            /*
             * OHNE KAPPEN DARF DER REST UNTER 0,375 M BLEIBEN.
             *
             * Der Mindestabstand schuetzt sonst die schmale eigene
             * Materialzelle einer Kappe. Ohne Kappen bekommt der Rest jedoch
             * dieselbe Asphaltrolle wie das Reihenband und verschmilzt mit
             * ihm; er wird nicht als schmaler Ring an CS2 uebergeben.
             *
             * Am Nutzerpolygon vom 2026-08-24 lagen die aeusseren gueltigen
             * Buchtfelder 0,044 bzw. 0,060 m vom Innenrand entfernt. Alle vier
             * Ecken lagen innerhalb, trotzdem kostete die pauschale
             * 0,375-m-Schranke je Endabschnitt eine Bucht. Mit Kappen bleibt
             * die Schranke unveraendert.
             */
            var buchtRandabstand = einstellungen.Querstrassenkappen
                ? einstellungen.Cs2Mindestkante
                : 0.0;
            var modulplanung = Layoutplanung.PlaneModule(
                bandplan,
                globaleQuerstrassen,
                rasterrand,
                randstrassenmittellinie,
                einstellungen.Buchtbreite,
                buchtRandabstand,
                einstellungen.Querstrassenkappen, einstellungen.Querstrassenabstand,
                einstellungen.Randstrassen, ringlos);
            var gueltigeBuchten = Layoutplanung.GueltigeBuchten(
                modulplanung.Plaene,
                rasterrand,
                buchtRandabstand,
                true,
                zoningLokal,
                // Ohne Ring verschmelzen Restgruen und Randband innerhalb
                // desselben Flaechenabschnitts. Die 0,30 m am Rasterrand
                // sind deshalb 1,30 m bis zur echten Gruenkante (80 x 64,
                // Median 2,5 m). Die innere Hilfslinie kostete 40 Buchten.
                ringlos != null ? lokalpunkte : null);
            if (ringlos != null)
            {
                foreach (var modul in modulplanung.Plaene)
                foreach (var reihe in modul.Modul.Reihen)
                foreach (var spalte in modul.Reihenplaene[reihe.Id].Spalten)
                {
                    var ecken = new[] { new Punkt(spalte.Anfang, reihe.Anfang), new Punkt(spalte.Ende, reihe.Anfang),
                        new Punkt(spalte.Ende, reihe.Ende), new Punkt(spalte.Anfang, reihe.Ende) };
                    if (!ringlos.BuchtFrei(ecken, modul.Modul.Fahrgassenmitte)
                        || rzAbschnitte.Any(rz => Ringlosplan.Ueberlappt(ecken, rz.Bauland)))
                        gueltigeBuchten.Remove((reihe.Id, spalte.Id));
                }
            }
            var teilflaechenlayout = Teilflaechenlayout.Plane(
                einstellungen,
                rahmen,
                rasterrand,
                randstrassenmittellinie,
                linienregister);
            if (ringlos != null && teilflaechenlayout != null)
                ringlos = teilflaechenlayout.PlaneRingloseAnschluesse(einstellungen.Querstrassenbreite,
                    lokalpunkte, lokaleZufahrten, zoningLokal, linienregister);
            var randreihenplan = !einstellungen.Randstrassen
                ? new Randreihenplan { Buchten = Array.Empty<Randbuchtplan>(), Schnittlinien = Array.Empty<Randreihenschnitt>() }
                : Randreihenplanung.Plane(
                lokalpunkte,
                innenrand,
                randstrassenrand,
                einstellungen.Randabstand,
                einstellungen.Zufahrtstiefe,
                einstellungen.Buchtbreite,
                teilflaechenlayout?.Buchtenzahl ?? gueltigeBuchten.Count,
                linienregister,
                lokaleZufahrten,
                zoningLokal,
                randzoningLokal.Length == 0
                    ? null
                    : randzoningLokal.Select(l => (l.A, l.B)).ToArray(),
                einstellungen.Randstrassenmittellinientiefe,
                ParkingGeometry.ZoningStrassenbreite / 2);

            // Flaechenabschnitte an den Gassenachsen statt an jeder Material-
            // grenze: Nutzerpolygon 91 -> 18 Grasringe. Jede Achse wird selbst
            // Rasterlinie, bevor eine Zelle ihren Abschnitt erhaelt.
            var ringloseAbschnittsgrenzen = ringlos == null ? Array.Empty<double>()
                : ringlos.Gassen.Select(g => g.A.Y).Distinct().OrderBy(y => y).ToArray();
            /*
             * DURCHGEHENDE ENDSTREIFEN - WOVON SIE WIRKLICH ABHAENGEN.
             *
             * Die einfache Bandkonstruktion gilt fuer parallele Endstreifen.
             * Bei schraegen, verzweigten Streifen erzeugte freies Vereinigen
             * im 25-Grad-Kontrollfall 2152,89 statt 41,95 m2 abgelehnten
             * Belag - dagegen stand hier zusaetzlich `Geometrie.IstKonvex`.
             *
             * Diese Bedingung war zu grob. Befund des Nutzers am 2026-09-09:
             * *"Splitting ist wieder aufgetaucht an Randgruen und
             * Fahrtgassenfussweg."* Seine Form hat zwei Stufen, ist also
             * nicht konvex - und damit wurde JEDES schmale Band an jeder
             * Gassenachse durchgeschnitten. Gemessen am Bauzettel 00:48:
             *
             *     Randgruen  1,0 m breit  ->  12 Stuecke a 21,3 m
             *     Fussweg    2,0 m breit  ->  12 Stuecke a 21,3 m
             *
             * 21,3 m ist der Gassenabstand. In der Vorschau kam derselbe
             * Fussweg als ein Stueck von 156,1 m heraus - deshalb sah die
             * Vorschau anders aus als das Gebaute.
             *
             * Die Konvexitaet ist ausgebaut und nachgemessen: das Splitting
             * verschwindet (12/12 -> 0/0, Belagringe 89 -> 74, Grasringe
             * 38 -> 23, Buchtenzahl unveraendert), und der 25-Grad-Fall
             * bleibt gruen. Was ihn traegt, ist die LETZTE Bedingung - alle
             * Fusswege gerade -, und die stimmt seit dem Diagonalen-Fix vom
             * selben Tag verlaesslich. `--flaechenannahme` haelt fest, dass
             * kein Ring an CS2 geht, den es ablehnen wuerde.
             */
            var durchgehendeEndstreifen = ringlos != null && teilflaechenlayout == null
                && zoningLokal.Length == 0 && randzoningLokal.Length == 0
                // Versuch: schraege Endstreifen zulassen
                ;
            var ringloseBandgrenzen = ringlos == null || teilflaechenlayout != null ? Array.Empty<double>()
                : ringlos.Korridore.SelectMany(w => w.Ecken).Select(v => v.Y)
                    .Concat(ringloseAbschnittsgrenzen)
                    // Die Grenze des Randabstands trennt kein Material:
                    // Restgruen setzt das aeussere Gruen fort. Hier ergaben
                    // sich sonst zwei Ringe mit 1,00 und 0,30 m statt 1,30 m.
                    .Concat(bandplan.Baender.Where(b => b.Art != Zellart.Restgruen)
                        .SelectMany(b => new[] { b.Anfang, b.Ende }))
                    .Concat(zoningLokal.SelectMany(z => z.Umriss()).Select(v => v.Y))
                    .Distinct().OrderBy(y => y).ToArray();
            Phase("vorplanung");
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
                    randstrassenrand[(i + 1) % randstrassenrand.Count],
                    i);
                fragmente = TeileAlle(fragmente, teiler, linie);
            }
            for (var i = 0; i < randstrasseninnenrand.Count; i++)
            {
                var linie = linienregister.Randstrasseninnenkante(
                    randstrasseninnenrand[i],
                    randstrasseninnenrand[(i + 1) % randstrasseninnenrand.Count],
                    i);
                fragmente = TeileAlle(fragmente, teiler, linie);
            }
            Phase("s-ringe");
            if (randbandabschnitte != null)
                foreach (var stoss in randbandabschnitte.Stoesse)
                    fragmente = TeileAlleSegment(
                        fragmente,
                        teiler,
                        stoss.Linie,
                        stoss.Aussen,
                        stoss.Innen);
            foreach (var stoss in randstrassenabschnitte?.Stoesse ?? Array.Empty<(Linie Linie, Punkt Aussen, Punkt Innen)>())
                fragmente = TeileAlleSegment(
                    fragmente,
                    teiler,
                    stoss.Linie,
                    stoss.Aussen,
                    stoss.Innen);
            foreach (var schnitt in randreihenplan.Schnittlinien)
                fragmente = TeileAlleSegment(
                    fragmente,
                    teiler,
                    schnitt.Linie,
                    schnitt.Aussen,
                    schnitt.Innen);
            if (ringlos != null)
                foreach (var weg in ringlos.Korridore)
                {
                    var ecken = weg.Ecken;
                    for (var k = 0; k < 4; k++)
                    {
                        var a = ecken[k]; var b = ecken[(k + 1) % 4];
                        var linie = linienregister.Zufahrtskante(a, b - a, 1000 + k, "ringlos");
                        fragmente = TeileAlle(fragmente, teiler, linie);
                    }
                }
            foreach (var y in ringloseBandgrenzen)
                fragmente = TeileAlle(fragmente, teiler, linienregister.BandY(y));
            Phase("s-stoesse");
            if (teilflaechenlayout == null)
            {
                foreach (var y in bandplan.InnereGrenzen)
                {
                    if (y <= minY || y >= maxY) continue;
                    fragmente = TeileAlle(
                        fragmente, teiler, linienregister.BandY(y));
                }
                fragmente = TeileModulspalten(
                    fragmente,
                    teiler,
                    linienregister,
                    globaleQuerstrassen,
                    modulplanung);
            }
            else
            {
                foreach (var teilung in teilflaechenlayout.Teilungen)
                    fragmente = TeileTeilflaechenraster(
                        fragmente, teiler, teilung, teilflaechenlayout);
                teilflaechenlayout.ProtokolliereRasterzuordnung();
            }

            /*
             * DIE BAULANDKANTEN GEHEN INS RASTER - vor jeder Einordnung.
             *
             * Das ist der ganze Unterschied zwischen "Plan" und
             * "Negativstempel": nicht fertig rechnen und dann ein Loch
             * stanzen, sondern die Grenze anmelden, bevor eine Zelle
             * entsteht. Danach kann keine Zelle halb drinnen und halb
             * draussen liegen, und die Einordnung nach dem Schwerpunkt
             * (weiter unten) kann sich nicht irren.
             *
             * SEGMENTWEISE, nicht als unendliche Gerade: ein gedrehtes
             * Rechteck mitten im Areal wuerde sonst den ganzen Parkplatz in
             * acht Streifen zerschneiden.
             */
            foreach (var zoning in zoningLokal)
            {
                /*
                 * DREI RECHTECKE, NICHT EINES.
                 *
                 * Aussen der freigehaltene Bereich, dazwischen die aeussere
                 * Fahrbahnkante der Zoning-Strasse, innen das gezogene
                 * Parzellenrechteck. Jede dieser Grenzen trennt spaeter zwei
                 * Materialien, und eine Materialgrenze MUSS eine Rasterlinie
                 * sein: sonst entscheidet der Schwerpunkt der Zelle, und der
                 * Rand franst genau dort aus, wo Asphalt auf Bauland trifft.
                 *
                 * Bei gleichem Rand fallen zwei davon zusammen; deshalb wird
                 * die Liste vorher entdoppelt.
                 */
                var aufschlaege = new List<double>
                {
                    0.0, 2 * Zoningvorgabe.Strassenhalbbreite, zoning.Rand,
                };
                aufschlaege.Sort();
                for (var s = 0; s < aufschlaege.Count; s++)
                {
                    if (s > 0 && aufschlaege[s] - aufschlaege[s - 1] <= 1e-6)
                        continue;
                    var ecken = zoning.EckenMitAufschlag(aufschlaege[s]);
                    /*
                     * DIE INNEREN KANTEN WERDEN UEBER DIE ECKEN HINAUS
                     * VERLAENGERT - sonst entsteht ein RING.
                     *
                     * Der Strassenkorridor umschliesst die Parzellen. Als
                     * eine Flaeche mit Loch muesste ihn der Lochtrenner bei
                     * JEDEM Bau aufbrechen, und beim ersten Versuch kam
                     * genau das heraus: 52 bis 69 % Asphalt, der Rest ohne
                     * jedes Material - weder Gruen noch Bauland, schlicht
                     * Loecher.
                     *
                     * Mit dem Ueberstand bis zum aeusseren Rechteck zerfaellt
                     * der Bereich in ein Gitter aus neun Feldern. Kein Ring,
                     * kein Reparaturpass.
                     */
                    var ueberstand = zoning.Rand - aufschlaege[s];
                    for (var k = 0; k < 4; k++)
                    {
                        var a = ecken[k];
                        var b = ecken[(k + 1) % 4];
                        if (Geometrie.Laenge(b - a) <= 1e-6) continue;
                        if (ueberstand > 1e-6)
                        {
                            var laenge = Geometrie.Laenge(b - a);
                            var richtung = (b - a) * (1.0 / laenge);
                            a -= richtung * ueberstand;
                            b += richtung * ueberstand;
                        }
                        fragmente = TeileAlleSegment(
                            fragmente,
                            teiler,
                            linienregister.FreieGerade(
                                a, b, Linienart.Zoningkante,
                                $"Zoningkante {s}/{k}"),
                            a,
                            b);
                    }
                }

                /*
                 * UND DIE KANTEN DES AEUSSEREN BAULANDS DAZU.
                 *
                 * Die Schleife darueber kennt nur Rechtecke, die auf allen
                 * vier Seiten gleich weit wachsen. Das aeussere Bauland ist
                 * aber je Seite verschieden tief und laeuft nicht um die
                 * Ecke - zwei Baender stossen dort aneinander, das Eck
                 * dazwischen bleibt frei. Sein Umriss ist deshalb ein
                 * Treppenzug, und JEDE seiner Kanten trennt Parzellenboden
                 * von dem, was daneben liegt.
                 *
                 * `Umriss` liefert genau diesen Zug; ohne Aussenband sind es
                 * dieselben vier Punkte wie oben, und die Schnitte laufen
                 * doppelt - das kostet nichts und macht den Sonderfall
                 * ueberfluessig.
                 */
                if (zoning.HatAussenband)
                {
                    var umriss = zoning.Umriss();
                    for (var k = 0; k < umriss.Length; k++)
                    {
                        var a = umriss[k];
                        var b = umriss[(k + 1) % umriss.Length];
                        if (Geometrie.Laenge(b - a) <= 1e-6) continue;
                        fragmente = TeileAlleSegment(
                            fragmente,
                            teiler,
                            linienregister.FreieGerade(
                                a, b, Linienart.Zoningkante,
                                $"Aussenbandkante {k}"),
                            a,
                            b);
                    }
                }
            }

            /*
             * DIE FAHRBAHNKANTEN DER RANDZONING-STRASSE SIND MATERIALGRENZEN
             * - ALSO MUESSEN SIE RASTERLINIEN SEIN.
             *
             * Fuer die Zoningflaechen steht diese Regel gleich darueber; fuer
             * das Randzoning fehlte sie. Die beiden Ringe wurden zwar
             * gerechnet, aber nur zur nachtraeglichen Abfrage der Zellenmitte
             * benutzt - genau das, was das Modell verbietet: "eine
             * Materialgrenze muss eine Rasterlinie sein, BEVOR klassifiziert
             * wird". Wo sie es nicht ist, entscheidet der Schwerpunkt, und
             * der Rand franst aus.
             *
             * GEMESSEN am 2026-09-04: die RZ-Fahrbahn bekam nur 91,7 % ihres
             * Belags, der Rest fehlte an den Laengsraendern.
             *
             * Mit Randstrassen wird entlang der gewaehlten Polygonkanten
             * segmentweise geschnitten. Eine unendliche Gerade wuerde den ganzen
             * Parkplatz in Streifen zerlegen, und die uebrigen Kanten des
             * Rings gehoeren zu Polygonseiten ohne Randzoning - dort liegt
             * die gewoehnliche Randstrasse, die ihre eigenen Kanten schon
             * mitbringt.
             */
            // Alle vier Geraden vor der Klassifikation teilen: TeileSegment
            // entscheidet anhand der Zellmitte, setzt also schon begrenzte
            // Zellen voraus. Nach Gassenverschiebung blieben damit 2 Ecken
            // abseits der Sollgeraden (bis 0,375 m). Vollständige Geraden:
            // 0 abweichende Ecken und 0/1600 Deckungslücken in --rzstufen.
            foreach (var rz in rzAbschnitte)
            foreach (var ring in new[] { rz.Strasse, rz.Bauland })
            for (var k = 0; k < ring.Length; k++) {
                var a = ring[k]; var b = ring[(k + 1) % ring.Length];
                fragmente = TeileAlle(fragmente, teiler,
                    linienregister.FreieGerade(a, b, Linienart.Zoningkante, "RZ-Abschnitt"));
            }
            if (randzoningLokal.Length > 0
                && randzoningAussen != null && randzoningInnen != null)
                for (var i = 0; i < lokalpunkte.Length; i++)
                {
                    var ka = lokalpunkte[i];
                    var kb = lokalpunkte[(i + 1) % lokalpunkte.Length];
                    var istRz = false;
                    foreach (var linie in randzoningLokal)
                        if (Geometrie.Laenge(linie.A - ka) < 0.5
                                && Geometrie.Laenge(linie.B - kb) < 0.5
                            || Geometrie.Laenge(linie.A - kb) < 0.5
                                && Geometrie.Laenge(linie.B - ka) < 0.5)
                        {
                            istRz = true;
                            break;
                        }
                    if (!istRz) continue;
                    var ringe = new[] { randzoningAussen, randzoningInnen };
                    for (var r = 0; r < ringe.Length; r++)
                    {
                        var ring = ringe[r];
                        if (ring.Count != lokalpunkte.Length) continue;
                        var a = ring[i];
                        var b = ring[(i + 1) % ring.Count];
                        if (Geometrie.Laenge(b - a) <= 1e-6) continue;
                        fragmente = TeileAlleSegment(
                            fragmente,
                            teiler,
                            linienregister.FreieGerade(
                                a, b, Linienart.Zoningkante,
                                $"Randzoningkante {i}/{r}"),
                            a,
                            b);
                    }
                }

            Phase(teilflaechenlayout == null ? "s-band" : "s-raster");
            var reihenplaene = modulplanung.Plaene
                .SelectMany(plan => plan.Reihenplaene)
                .ToDictionary(paar => paar.Key, paar => paar.Value);

            var zellen = new List<Zelle>();
            foreach (var fragment in fragmente)
            {
                var mitte = Geometrie.Mittelwert(fragment);
                var art = Zellart.Randband;
                int? buchtId = null;
                int? querstrassenId = null;
                int? flaechenabschnitt = null;
                /*
                 * BAULAND GEWINNT GEGEN ALLES - und zwar VOR der Frage, ob
                 * die Zelle innen oder in der Randreihe liegt.
                 *
                 * Der erste Anlauf pruefte es nur im Inneren. Eine Flaeche
                 * nahe am Rand haette dann in der Randreihe weiter Buchten
                 * bekommen, mitten im Bauland. Die Buchten haengen an der
                 * Zellart (`BuchtId` entsteht nur an Buchtzellen) - wer die
                 * Art hier richtig setzt, muss spaeter nichts mehr
                 * herausfiltern.
                 */
                /*
                 * DIE PARZELLE GEHT DEM KORRIDOR VOR - IMMER.
                 *
                 * Hier gewann bisher die ERSTE Flaeche, die den Punkt
                 * ueberhaupt beanspruchte, samt ihrem Strassenkorridor. Das
                 * geht gut, solange die Flaechen auseinanderliegen. Sitzen
                 * zwei buendig aneinander, ragt der Korridor der einen aber
                 * ueber die PARZELLEN der anderen - und stand sie in der
                 * Liste weiter vorne, bekam fremdes Bauland Asphalt.
                 *
                 * Genau das hat der Nutzer am 2026-09-03 gezeigt: *"Eine
                 * Zoningstrassenflaeche, also Asphalt, geht weiterhin in die
                 * Tiles"* - und zwar an derselben Innenecke, an der vorher
                 * schon die Strasse abriss.
                 *
                 * Der Vorrang ist eindeutig: eine Parzelle ist Bauland,
                 * Punkt. Ein Korridor ist nur die Vermutung, dass dort eine
                 * Strasse hinkoennte - und diese Vermutung hat gegen eine
                 * gezogene Parzelle zu weichen, wem auch immer sie gehoert.
                 */
                Zoningvorgabe baulandTreffer = null;
                var baulandIndex = -1;
                var aufParzelle = false;
                for (var zi = 0; zi < zoningLokal.Length; zi++)
                    if (zoningLokal[zi].EnthaeltMitAufschlag(mitte, 0.0))
                    {
                        baulandTreffer = zoningLokal[zi];
                        baulandIndex = zi;
                        aufParzelle = true;
                        break;
                    }
                if (baulandTreffer == null)
                    for (var zi = 0; zi < zoningLokal.Length; zi++)
                        if (zoningLokal[zi].Enthaelt(mitte))
                        {
                            baulandTreffer = zoningLokal[zi];
                            baulandIndex = zi;
                            break;
                        }

                /*
                 * DAS AEUSSERE BAULAND IST PARZELLENBODEN, NICHT FAHRBAHN.
                 *
                 * Es wird ABSICHTLICH wie das gezogene Rechteck behandelt -
                 * `aufParzelle` und damit `Zellart.Zoning`. Der Nutzer hat
                 * es am 2026-09-21 so bestimmt: *"Das funktioniert also
                 * genau wie gruene weil es am ende das gleiche ist."* Dort
                 * liegen keine Buchten, keine Fahrgasse und keine
                 * Querstrasse, nur der Boden, auf dem spaeter etwas steht.
                 *
                 * Dieser Durchgang kommt ZULETZT: `Enthaelt` deckt Parzellen
                 * und Strassenkorridor ab, und die Fahrbahn darf das Band
                 * nicht an sich ziehen.
                 */
                if (baulandTreffer == null)
                    for (var zi = 0; zi < zoningLokal.Length; zi++)
                        if (zoningLokal[zi].ImAussenband(mitte))
                        {
                            baulandTreffer = zoningLokal[zi];
                            baulandIndex = zi;
                            aufParzelle = true;
                            break;
                        }

                /*
                 * DER KORRIDOR ENDET, WO AUCH DIE STRASSE ENDET.
                 *
                 * Der Belag wurde bisher rein geometrisch eingefaerbt -
                 * "Parzellen plus 8 m" - und damit auch dort, wo laengst
                 * keine Strasse mehr gebaut wird, weil sie an der
                 * Randstrassenachse gekappt ist. Der Nutzer sah die
                 * Strassenflaeche deshalb noch im Randgruen liegen, obwohl
                 * die Flaeche korrekt in der Ecke sass.
                 *
                 * Seine Skizze vom 2026-09-03 ist eindeutig: schiebt man die
                 * Flaeche in eine Ecke, liegt die Zoning-Strasse NUR auf den
                 * beiden Innenseiten. Aussen grenzt das gezogene Rechteck
                 * direkt ans Randgruen.
                 *
                 * Faellt ein Korridorstueck also aus der Randstrassenachse
                 * heraus, gilt die Zelle als gewoehnlich und wird ganz
                 * normal eingeordnet - Randband, Gruen, was dort eben
                 * hingehoert.
                 */
                /*
                 * GEGEN EINE RASTERLINIE PRUEFEN, NICHT GEGEN IRGENDEINE.
                 *
                 * Hier stand die Randstrassen-MITTELLINIE. Die ist aber
                 * keine Rasterlinie - laut Aufbau teilen nur die beiden
                 * FAHRBAHNKANTEN die Materialzellen. Eine Zelle, die die
                 * Mittellinie kreuzt, wurde deshalb nach ihrem Schwerpunkt
                 * eingeordnet, und die Grenze franste aus.
                 *
                 * Im Abzug des Nutzers vom 2026-09-03 standen daraufhin zwei
                 * `Zoningstrasse`-Flaechen mit einem Winkel von 1,57 Grad -
                 * Nadeln, die im Spiel als spitzer Zipfel ueber den Gehweg
                 * auf die Strasse liefen.
                 *
                 * `randstrassenrand` ist die aeussere Fahrbahnkante und
                 * damit eine echte Rasterlinie: dort liegt eine Zellgrenze,
                 * und keine Zelle kann sie kreuzen.
                 */
                if (baulandTreffer != null && !aufParzelle
                    && !Geometrie.Enthaelt(randstrassenrand, mitte))
                {
                    baulandTreffer = null;
                    baulandIndex = -1;
                }

                /*
                 * RANDZONING: WAS AUSSEN AN DER RANDSTRASSE LIEGT, WIRD
                 * BAULAND.
                 *
                 * Von der Polygonkante nach innen liegen: 1 m Gruen, dann die
                 * Randreihe (Buchttiefe), dann die Randstrasse. Waechst das
                 * Zoning von der Randstrasse nach AUSSEN, landen die Kacheln
                 * genau auf dieser Buchtreihe. Der Nutzer hat es so erklaert:
                 * *"Dann haetten wir Parkbuchten, die unter Haeusern
                 * stehen."*
                 *
                 * Also gehen dort Buchten UND Randgruen weg, und die Zelle
                 * wird Parzellenboden. Das ist dieselbe Art, die eine innere
                 * Zoningflaeche bekommt - nur ohne eigenes Rechteck.
                 *
                 * `randstrassenrand` ist die AEUSSERE Fahrbahnkante und eine
                 * echte Rasterlinie; ausserhalb davon liegt nichts als
                 * Randreihe und Gruen.
                 */
                /*
                 * DIE GRENZE IST DIE KANTE DER RZ-STRASSE, nicht die der
                 * Randstrasse.
                 *
                 * Hier stand `randstrassenrand`, also die 7-m-Kante. Die
                 * RZ-Strasse ist aber 8 m breit; der halbe Meter dazwischen
                 * wurde damit zu Parzellenboden statt zu Fahrbahn - und die
                 * Strasse blieb an ihrem Rand blank. Gemessen: 89,6 %
                 * Deckung, also 7,17 von 8 m.
                 */
                var imRandzoning = baulandTreffer == null
                    && (rzAbschnitte.Any(rz => Geometrie.EnthaeltOderRand(rz.Bauland, mitte))
                    || einstellungen.Randstrassen && randzoningLokal.Length > 0
                    && !Geometrie.Enthaelt(randzoningAussen, mitte)
                    && ParkingGeometry.RandzoningZelle(
                        lokalpunkte, randzoningLokal, mitte));

                /*
                 * UND DIE FAHRBAHN SELBST BEKOMMT ZONINGSTRASSEN-BELAG.
                 *
                 * Der Streifen AUSSERHALB der Randstrasse wird zu Bauland -
                 * das stand schon. Die Fahrbahn dazwischen behielt aber ihre
                 * alte Einordnung als Randstrasse, und damit fehlte ihr der
                 * Zoningstrassen-Belag. Der Nutzer: *"Es wurde wieder den
                 * echten Strassen keine Asphaltflaeche gegeben."* Im Test
                 * nachgestellt: 123,2 m Zoning-Strasse, 0 m2 Belag.
                 *
                 * Der Unterschied ist nicht nur die Sorte: die Zoning-Strasse
                 * ist 8 m breit statt 7, und ihr Belag liegt bei -94 und
                 * damit ueber den Flaechen der Gebaeude. Die Randstrasse
                 * bringt beides nicht mit.
                 */
                var imRandzoningStrasse = baulandTreffer == null
                    && !imRandzoning
                    && (rzAbschnitte.Any(rz => Geometrie.EnthaeltOderRand(rz.Strasse, mitte))
                    || einstellungen.Randstrassen && randzoningLokal.Length > 0
                    && Geometrie.Enthaelt(randzoningAussen, mitte)
                    && !Geometrie.Enthaelt(randzoningInnen, mitte)
                    && ParkingGeometry.RandzoningZelle(
                        lokalpunkte, randzoningLokal, mitte));

                /*
                 * JEDE ZONING-STRASSE BEKOMMT IHRE EIGENE FLAECHE.
                 *
                 * Ohne diese Trennung verschmolzen ALLE zusammenhaengenden
                 * Zoningstrassen-Zellen zu einem einzigen Ring - der der
                 * Randzoning-Strasse zusammen mit denen aller Zoningflaechen.
                 * Gemessen am 2026-09-04 im Protokoll PLT-A950B8E0: ein Ring
                 * mit 22 Punkten ueber 2.311 m2, darin ein Stachel (42,1 m
                 * hinaus, 10,1 m auf derselben Linie zurueck) mit einem
                 * Doppelpunkt von 7,6 Mikrometern. CS2s Ear-Clipping liefert
                 * dafuer NULL Dreiecke und verwirft den ganzen Ring - also
                 * RZ-Strasse und ZF-Strassen gemeinsam und vollstaendig.
                 * Genau das Bild des Nutzers: *"auf der ganzen RZ-Strasse und
                 * ZF-Strasse gleichermassen fehlte der Asphalt."*
                 *
                 * Das ist derselbe Fehler, der am 2026-08-24 schon beim
                 * Asphalt stand und dort mit `Rollengruppe` behoben wurde -
                 * die Zoning-Strassen kamen spaeter dazu und wurden dabei
                 * vergessen. Die Abhilfe ist dieselbe: nicht hinterher
                 * reparieren, sondern gar nicht erst verschmelzen lassen.
                 *
                 * `Flaechenabschnitt` ist der vorhandene Riegel dafuer; er
                 * geht in `Vereinigung.Vereinige` in die Verschmelzungsregel
                 * ein. Der Zahlenraum ist von den Rand- und Randbandabschnitten
                 * getrennt, damit sich die Bedeutungen nicht ueberlagern.
                 */
                const int zoningAbschnittBasis = 10000;
                const int randzoningAbschnitt = 9999;

                if (imRandzoning)
                {
                    // Parzellenboden statt Bucht und Gruen - siehe oben.
                    art = Zellart.Zoning;
                    flaechenabschnitt = randzoningAbschnitt;
                }
                else if (imRandzoningStrasse)
                {
                    art = Zellart.Zoningstrasse;
                    flaechenabschnitt = randzoningAbschnitt;
                }
                else if (baulandTreffer != null)
                {
                    flaechenabschnitt = zoningAbschnittBasis + baulandIndex;
                    // Der Streifen unter der Zoning-Strasse bekommt Asphalt,
                    // nur der Parzellenboden die Zoning-Flaeche. Die Strasse
                    // selbst ist unsichtbar; ohne Belag saehe man Autos ueber
                    // Gras fahren.
                    art = aufParzelle ? Zellart.Zoning : Zellart.Zoningstrasse;
                }
                else if (ringlos != null && ringlos.Gassen.Any(g => Geometrie.EnthaeltOderRand(g.Ecken, mitte)))
                {
                    // Am Anschluss gehoert der gemeinsame Belag zur Gasse.
                    // Vorrang der Zufahrt hinterliess im Nutzerfall 18:28
                    // eine 3,5 m tiefe Kerbe in einem der sieben Gassenbaender.
                    art = Zellart.Fahrgasse;
                }
                else if (ringlos != null && ringlos.Fusswege.Concat(ringlos.Zufahrten)
                    .Any(w => Geometrie.EnthaeltOderRand(w.Ecken, mitte)))
                {
                    art = Zellart.Zufahrt;
                }
                else if (ringlos != null && ringlos.Querwege.Any(q => Geometrie.EnthaeltOderRand(
                    new Ringlosplan.Weg { A = q.Anfang, B = q.Ende, Breite = q.Querstrasse.Ende - q.Querstrasse.Anfang }.Ecken, mitte)))
                {
                    art = Zellart.Querstrasse;
                }
                else if (ringlos != null && (mitte.X < ringlos.Links || mitte.X >= ringlos.Rechts))
                {
                    art = Zellart.Restgruen;
                }
                else if (Geometrie.Enthaelt(rasterrand, mitte))
                {
                    if (teilflaechenlayout != null)
                    {
                        teilflaechenlayout.Klassifiziere(
                            mitte,
                            einstellungen.Querstrassenkappen,
                            out art,
                            out buchtId,
                            out querstrassenId);
                    }
                    else
                    {
                        var band = bandplan.Bei(mitte.Y);
                        if (band.Art == Zellart.Bucht)
                        {
                            var spalte = reihenplaene[band.Id].Bei(mitte.X);
                            int id;
                            if (spalte.Art == Spaltenart.Querstrasse)
                            {
                                art = Zellart.Querstrasse;
                                querstrassenId = spalte.QuerstrassenId;
                            }
                            else if (spalte.Art == Spaltenart.Buchtfeld
                                && gueltigeBuchten.TryGetValue(
                                    (band.Id, spalte.Id), out id))
                            {
                                art = Zellart.Bucht;
                                buchtId = id;
                            }
                            else
                            {
                                /**
                                 * DAS REIHENENDE: Kappe oder durchgehender Belag?
                                 *
                                 * Wo keine ganze Bucht mehr hinpasst, bleibt ein
                                 * Rest. Mit Kappen wird er Gruen - das ist der
                                 * kleine Grasnubbel am Reihenende, rund eine
                                 * Buchtbreite schmal. Ohne Kappen wird er Belag
                                 * und die Fahrflaeche laeuft glatt bis zur
                                 * Querstrasse durch.
                                 *
                                 * Die Randbuchtreihe weiter unten bleibt davon
                                 * unberuehrt: ihre Eckkeile sind keine
                                 * Reihenenden an einer Querstrasse.
                                 */
                                // Ohne Kappen bekommt der Rest die Rolle SEINES
                                // BANDES, nicht `Restbelag`. Sonst waere er eine
                                // eigene Rolle und damit eine eigene Flaeche:
                                // gemessen an "Gebaut 146" 79 statt 40 Belagringe,
                                // lauter Schnipsel neben der Reihe. Mit der Rolle
                                // des Bandes waechst er in den Reihenstreifen
                                // hinein. Eine Bucht wird er dadurch nicht - die
                                // Buchtenzahl haengt an der BuchtId, und die
                                // bleibt hier leer.
                                art = einstellungen.Querstrassenkappen || (ringlos != null && spalte.Art == Spaltenart.Buchtfeld)
                                    ? Zellart.Kappe
                                    : band.Art;
                            }
                        }
                        else if (band.Art != Zellart.Fahrgasse)
                        {
                            var querstrasse = modulplanung.Querstrassenstuecke
                                .FirstOrDefault(stueck =>
                                    stueck.Enthaelt(mitte.X, mitte.Y));
                            if (querstrasse != null)
                            {
                                art = Zellart.Querstrasse;
                                querstrassenId = querstrasse.Querstrasse.Id;
                            }
                            else
                            {
                                art = band.Art;
                            }
                        }
                        else
                        {
                            art = ringlos != null && !ringlos.Gassen.Any(g => Geometrie.EnthaeltOderRand(g.Ecken, mitte))
                                ? Zellart.Restgruen : band.Art;
                        }
                    }
                }
                else if (einstellungen.Randstrassen && Geometrie.Enthaelt(randstrassenrand, mitte))
                {
                    art = Zellart.Randstrasse;
                    flaechenabschnitt = randstrassenabschnitte.AbschnittBei(mitte);
                }
                else if (Geometrie.Enthaelt(innenrand, mitte))
                {
                    var randbucht = randreihenplan.Buchten.FirstOrDefault(
                        kandidat => Geometrie.Enthaelt(kandidat.Ecken, mitte));
                    if (randbucht != null)
                    {
                        art = Zellart.Bucht;
                        buchtId = randbucht.Id;
                    }
                    else
                    {
                        art = Zellart.Kappe;
                    }
                }
                else if (randbandabschnitte != null)
                {
                    flaechenabschnitt = randbandabschnitte.AbschnittBei(mitte);
                }

                // Keine Abschnittssperre an Gassenachsen: Sie zerlegte im Nutzerfall
                // Fussweg und Grasrand in 28 zusaetzliche Flaechen (132 statt 104).
                // Rollen und echte Materialgrenzen bleiben die Verschmelzungsregeln.
                if (ringlos != null && art != Zellart.Fahrgasse
                    && art != Zellart.Zoning && art != Zellart.Zoningstrasse)
                {
                    if (!durchgehendeEndstreifen)
                        flaechenabschnitt = teilflaechenlayout != null
                            ? teilflaechenlayout.RingloserAbschnitt(mitte)
                            : 20000 + ringloseAbschnittsgrenzen.Count(y => y < mitte.Y);
                    else if (art != Zellart.Zufahrt && lokaleZufahrten.Length == 0
                        && ringloseAbschnittsgrenzen.Length > 0)
                        // Ohne Zufahrt ist das Randgruen geschlossen. Eine
                        // bereits gerasterte Gassenachse konstruiert zwei Boegen,
                        // statt die spaetere Lochtrennung bei jedem Bau zu brauchen.
                        flaechenabschnitt = mitte.Y < ringloseAbschnittsgrenzen[ringloseAbschnittsgrenzen.Length / 2]
                            ? 20000 : 20001;
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
                    Zugangsart = ringlos?.Fusswege.Concat(ringlos.Zufahrten).FirstOrDefault(w => Geometrie.EnthaeltOderRand(w.Ecken, mitte))?.Art,
                    Flaechenabschnitt = flaechenabschnitt,
                });
            }

            Phase("zellen");
            if (ParkingGeometry.LiveAn)
            {
                /*
                 * WELCHE ZELLENART BELEGT WIE VIEL?
                 *
                 * Der Nutzer zeigt auf gepflasterte Flaechen, auf denen
                 * nichts steht. Die Gesamtrechnung sagt, WIE VIEL es ist;
                 * diese Zeile sagt, WAS es ist. Ohne sie raet man, welche
                 * Rolle den Belag erzeugt.
                 */
                var nachArt = new Dictionary<Zellart, double>();
                foreach (var zelle in zellen)
                {
                    var f = Math.Abs(Geometrie.Vorzeichenflaeche(
                        zelle.Polygon.Punkte.ToList()));
                    nachArt.TryGetValue(zelle.Art, out var bisher);
                    nachArt[zelle.Art] = bisher + f;
                }
                ParkingGeometry.Live("  zellenarten "
                    + string.Join(" | ", nachArt
                        .OrderByDescending(paar => paar.Value)
                        .Select(paar => paar.Key + " "
                            + paar.Value.ToString("F0",
                                System.Globalization.CultureInfo
                                    .InvariantCulture))));
            }
            var zellenVorZufahrt = zellen;
            var zufahrtsbau = Zufahrtsbauer.Baue(
                zellenVorZufahrt,
                lokalpunkte,
                ringlos == null ? lokaleZufahrten : Array.Empty<Zufahrtsvorgabe>(),
                einstellungen.Zufahrtstiefe,
                knotenfabrik,
                linienregister,
                teiler);
            zellen = zufahrtsbau.Zellen;
            Phase("zufahrt");
            var vereinigung = Vereinigung.Vereinige(zellen);
            var vorTrennung = vereinigung.Flaechen;
            var topologie = vereinigung.Topologie;
            var trennung = Lochtrenner.Trenne(
                zellen, vorTrennung, rahmen);
            var flaechen = trennung.Flaechen;
            var trennbericht = trennung.Bericht;
            Phase("flaechen");
            uhr.Stop();
            if (phasen != null)
                ParkingGeometry.Live("  zellenbau " + uhr.ElapsedMilliseconds
                    + " ms | teile " + teile.Count
                    + " | fragmente " + fragmente.Count
                    + " | zellen " + zellen.Count
                    + " | flaechen " + flaechen.Count
                    + " | teilungen "
                    + (teilflaechenlayout?.Teilungen.Count ?? 0)
                    + " | " + string.Join(" ", phasen));
            return new Bauergebnis
            {
                Schnittwarnungen = teiler.Warnungen.ToArray(),
                Randstrassen = einstellungen.Randstrassen,
                Ringlos = ringlos,
                Form = form,
                Rahmen = rahmen,
                ArealLokal = areal,
                Umrisslokal = lokalpunkte,
                Randzoningabschnitte = rzAbschnitte,
                Innenrand = innenrand,
                Randstrassenrand = randstrassenrand,
                Randstrassenmittellinie = randstrassenmittellinie,
                Randstrasseninnenrand = randstrasseninnenrand,
                Randbuchten = randreihenplan.Buchten.ToDictionary(
                    bucht => bucht.Id),
                Innenbuchten = teilflaechenlayout?.Buchten
                    ?? new Dictionary<int, Punkt[]>(),
                KonvexeTeile = teile,
                ZellenVorZufahrt = zellenVorZufahrt,
                Zellen = zellen,
                FlaechenVorTrennung = vorTrennung,
                Flaechen = flaechen,
                Bandplan = teilflaechenlayout?.ErsterBandplan ?? bandplan,
                Spaltenplan = new Spaltenplan
                {
                    Spalten = Array.Empty<Spaltenabschnitt>(),
                    Querstrassen = globaleQuerstrassen,
                },
                Modulspaltenplaene = teilflaechenlayout?.Modulplaene
                    ?? modulplanung.Plaene,
                Querstrassenpruefungen =
                    teilflaechenlayout?.Querstrassenpruefungen
                    ?? modulplanung.Pruefungen,
                Querstrassenstuecke = teilflaechenlayout == null
                    ? modulplanung.Querstrassenstuecke
                    : Array.Empty<Querstrassenstueck>(),
                Zufahrtsbericht = zufahrtsbau.Bericht,
                Lochtrennung = trennbericht,
                Topologie = topologie,
                InnereStrassen = teilflaechenlayout?.Strassen
                    ?? Array.Empty<Rasterstrassenplan>(),
                EinzelneBauzeit = uhr.Elapsed,
            };
        }

        /**
         * Plant die minimale lochfreie Zerlegung eines geschlossenen Randbands.
         *
         * Der Spielfall vom 2026-08-25 lieferte vor der Lochtrennung einen
         * 14.955,51-m2-Aussenring mit 11.583,02-m2-Loch. Die spaetere
         * Zellpfadsuche machte daraus eine einzelne 7,000 x 5,900-m-Zelle.
         * Zwei Stosslinien genuegen dagegen konstruktiv: Sie teilen Asphalt
         * oder Gruen vor jeder Zellrolle in zwei Umfangsboegen. Mehr
         * Abschnitte waeren geometrisch moeglich, aber nicht noetig und
         * wuerden mehr CS2-Areas erzeugen. Die moeglichst gleich langen
         * Boegen verhindern, dass eine der beiden geplanten Flaechen selbst
         * nur ein kurzer Rest wird.
         */
        private static Ringabschnittsplan PlaneRingabschnitte(
            IReadOnlyList<Punkt> aussen,
            IReadOnlyList<Punkt> innen,
            Func<Punkt, Punkt, int, Linie> stosslinie)
        {
            if (aussen.Count != innen.Count || aussen.Count < 3)
                throw new InvalidOperationException(
                    "Randstrassenraender haben keine gemeinsame Eckstruktur.");

            var umfang = 0.0;
            var prefix = new double[aussen.Count + 1];
            for (var i = 0; i < aussen.Count; i++)
            {
                umfang += Geometrie.Laenge(
                    aussen[(i + 1) % aussen.Count] - aussen[i]);
                prefix[i + 1] = umfang;
            }

            var ersterStoss = 0;
            var zweiterStoss = 1;
            var besteAbweichung = double.PositiveInfinity;
            for (var i = 0; i < aussen.Count; i++)
                for (var j = i + 1; j < aussen.Count; j++)
                {
                    var bogen = prefix[j] - prefix[i];
                    var abweichung = Math.Abs(umfang - 2 * bogen);
                    if (abweichung >= besteAbweichung) continue;
                    besteAbweichung = abweichung;
                    ersterStoss = i;
                    zweiterStoss = j;
                }

            IReadOnlyList<Punkt> Abschnitt(int anfang, int ende)
            {
                var punkte = new List<Punkt>();
                for (var i = anfang;; i = (i + 1) % aussen.Count)
                {
                    punkte.Add(aussen[i]);
                    if (i == ende) break;
                }
                for (var i = ende;; i = Geometrie.Mod(i - 1, innen.Count))
                {
                    punkte.Add(innen[i]);
                    if (i == anfang) break;
                }
                if (Geometrie.Vorzeichenflaeche(punkte) <= 0)
                    throw new InvalidOperationException(
                        "Ein geplanter Randstrassenabschnitt ist nicht gegen den Uhrzeigersinn.");
                return punkte;
            }

            var stoesse = new[] { ersterStoss, zweiterStoss }
                .Select((ecke, nummer) =>
                    (Linie: stosslinie(aussen[ecke], innen[ecke], nummer),
                     Aussen: aussen[ecke],
                     Innen: innen[ecke]))
                .ToArray();
            return new Ringabschnittsplan
            {
                Abschnitte = new[]
                {
                    Abschnitt(ersterStoss, zweiterStoss),
                    Abschnitt(zweiterStoss, ersterStoss),
                },
                Stoesse = stoesse,
            };
        }

        private static List<Polygon> TeileModulspalten(
            IEnumerable<Polygon> fragmente,
            Polygonteiler teiler,
            Linienregister linienregister,
            IReadOnlyList<Querstrassenplan> globaleQuerstrassen,
            Modulplanung modulplanung)
        {
            const double rasterlinienEpsilon = 0.002;
            var linienNachX = new Dictionary<double, Linie>();
            foreach (var querstrasse in globaleQuerstrassen)
            {
                linienNachX[querstrasse.Anfang] = querstrasse.LinkeKante;
                linienNachX[querstrasse.Ende] = querstrasse.RechteKante;
            }
            var schnittlinien = new HashSet<Linie>();

            Linie LinieBei(double x)
            {
                Linie linie;
                if (linienNachX.TryGetValue(x, out linie)) return linie;
                linie = linienregister.RasterX(x);
                linienNachX.Add(x, linie);
                return linie;
            }

            foreach (var modulplan in modulplanung.Plaene)
                foreach (var paar in modulplan.Reihenplaene)
                {
                    var grenzen = paar.Value.Spalten
                        .SelectMany(spalte => new[]
                            { spalte.Anfang, spalte.Ende })
                        .Where(x => x > modulplan.MinX + 1e-6
                            && x < modulplan.MaxX - 1e-6)
                        .Distinct();
                    foreach (var x in grenzen)
                        schnittlinien.Add(LinieBei(x));
                }
            foreach (var stueck in modulplanung.Querstrassenstuecke)
            {
                schnittlinien.Add(stueck.Querstrasse.LinkeKante);
                schnittlinien.Add(stueck.Querstrasse.RechteKante);
            }

            var ausgabe = fragmente.ToList();
            // Modulraster duerfen verschiedene Phasen haben. Ihre Grenzen
            // werden trotzdem als ganze konstruktive Geraden geschnitten:
            // endliche Schnitte erzeugten auf zwei echten Acht-Ecken-Formen
            // je einen T-Stoss ("Materialrand ... 2 Fortsetzungen"). Die
            // zusaetzlichen gleichmaterialigen Zellkanten verschwinden bei der
            // Vereinigung wieder; die gemeinsame Knotentopologie bleibt exakt.
            var geordneteLinien = new List<Linie>();
            double? letzteRohachse = null;
            foreach (var linie in schnittlinien.OrderBy(
                         linie => linie.Achsenwert))
            {
                var gehoertZumLetztenCluster = letzteRohachse.HasValue
                    && linie.Achsenwert - letzteRohachse.Value
                        <= rasterlinienEpsilon;
                letzteRohachse = linie.Achsenwert;
                if (gehoertZumLetztenCluster)
                {
                    // Beim fernen T lagen nominell gleiche Modulraster nach
                    // 1-mm-Eingangsrundung 5,020738e-7 bzw. 0,000222789 m
                    // auseinander. 2 mm sind zweimal dieses Eingangsgitter
                    // und noch weit unter CS2s 0,375-m-Mindestkante; ein
                    // zweiter Schnitt erzeugte orientierungslose Splitter.
                    // Die konstruktiv wichtigere Querstrassenkante gewinnt.
                    if (linie.Art == Linienart.Querstrassenkante
                        && geordneteLinien[geordneteLinien.Count - 1].Art
                            != Linienart.Querstrassenkante)
                        geordneteLinien[geordneteLinien.Count - 1] = linie;
                    continue;
                }
                geordneteLinien.Add(linie);
            }
            for (var i = 0; i < geordneteLinien.Count; i++)
            {
                try
                {
                    ausgabe = TeileAlle(
                        ausgabe, teiler, geordneteLinien[i]);
                }
                catch (Exception fehler)
                {
                    var vorher = i == 0
                        ? double.PositiveInfinity
                        : geordneteLinien[i].Achsenwert
                            - geordneteLinien[i - 1].Achsenwert;
                    var nachher = i + 1 == geordneteLinien.Count
                        ? double.PositiveInfinity
                        : geordneteLinien[i + 1].Achsenwert
                            - geordneteLinien[i].Achsenwert;
                    throw new InvalidOperationException(
                        $"Modulraster x={geordneteLinien[i].Achsenwert:R}, "
                            + $"Abstand vorher {vorher:R} m, "
                            + $"nachher {nachher:R} m.",
                        fehler);
                }
            }
            return ausgabe;
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
                        $"Teilung an {linie.Name} (ID {linie.Id}) scheiterte.",
                        fehler);
                }
            }
            return ausgabe;
        }

        /**
         * Ein Teilraster darf an seiner gemeinsamen Kante enden. Der neue
         * Knoten wird dort zugleich in die Nachbarzelle eingetragen; damit
         * bleibt die Naht mannigfaltig, obwohl die andere Seite einen anderen
         * Rahmen besitzt.
         */
        private static List<Polygon> TeileTeilflaechenraster(
            IEnumerable<Polygon> fragmente,
            Polygonteiler teiler,
            Teilflaechenlayout.Rasterteilung teilung,
            Teilflaechenlayout layout)
        {
            var ausgabe = new List<Polygon>();
            foreach (var fragment in fragmente)
            {
                if (!layout.GehoertTeilungZuPolygon(teilung, fragment))
                {
                    ausgabe.Add(fragment);
                    continue;
                }
                try
                {
                    ausgabe.AddRange(teiler.Teile(fragment, teilung.Linie));
                }
                catch (Exception fehler)
                {
                    throw new InvalidOperationException(
                        $"Teilflaechenraster an {teilung.Linie.Name} "
                            + $"(ID {teilung.Linie.Id}) scheiterte.",
                        fehler);
                }
            }
            teiler.VervollstaendigeNachbarkanten(ausgabe, teilung.Linie);
            return ausgabe;
        }

        private static List<Polygon> TeileAlleSegment(
            IEnumerable<Polygon> fragmente,
            Polygonteiler teiler,
            Linie linie,
            Punkt segmentanfang,
            Punkt segmentende)
        {
            var ausgabe = new List<Polygon>();
            foreach (var fragment in fragmente)
            {
                try
                {
                    ausgabe.AddRange(teiler.TeileSegment(
                        fragment, linie, segmentanfang, segmentende));
                }
                catch (Exception fehler)
                {
                    throw new InvalidOperationException(
                        $"Segmentteilung an {linie.Name} (ID {linie.Id}) scheiterte.",
                        fehler);
                }
            }
            teiler.VervollstaendigeNachbarkanten(ausgabe, linie);
            return ausgabe;
        }
    }
}
