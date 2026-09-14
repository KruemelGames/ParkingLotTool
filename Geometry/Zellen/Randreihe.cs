using System;
using System.Collections.Generic;

namespace ParkingLotTool.Geometry.Zellen
{
    internal static class Randreihenplanung
    {
        internal static Randreihenplan Plane(
            IReadOnlyList<Punkt> areal,
            IReadOnlyList<Punkt> aussenkontur,
            IReadOnlyList<Punkt> strassenkante,
            double randabstand,
            double strassenkantentiefe,
            double buchtbreite,
            int ersteBuchtId,
            Linienregister linienregister,
            IReadOnlyList<Zufahrtsvorgabe> zufahrten = null,
            IReadOnlyList<Zoningvorgabe> bauland = null,
            IReadOnlyList<(Punkt A, Punkt B)> randzoning = null,
            double randzoningachstiefe = 0,
            double randzoninghalbebreite = 0)
        {
            zufahrten = zufahrten ?? Array.Empty<Zufahrtsvorgabe>();
            var buchten = new List<Randbuchtplan>();
            var schnitte = new List<Randreihenschnitt>();
            for (var kante = 0; kante < areal.Count; kante++)
            {
                var ursprung = areal[kante];
                var richtung = areal[(kante + 1) % areal.Count] - ursprung;
                var laenge = Geometrie.Laenge(richtung);
                if (laenge == 0)
                    throw new InvalidOperationException(
                        "The perimeter row planner hit a zero-length edge.");
                var tangente = richtung * (1 / laenge);
                var innennormale = new Punkt(-tangente.Y, tangente.X);
                double Entlang(Punkt punkt) =>
                    Geometrie.Skalar(punkt - ursprung, tangente);

                // An einer Gehrung besitzen die beiden parallelen Konturen
                // verschieden lange Kantensegmente. Nur ihre gemeinsame
                // Projektion ergibt echte 3,0 x 5,9-m-Rechtecke; der Rest an
                // beiden Ecken bleibt Kappe.
                var aussenA = Entlang(aussenkontur[kante]);
                var aussenB = Entlang(
                    aussenkontur[(kante + 1) % aussenkontur.Count]);
                var innenA = Entlang(strassenkante[kante]);
                var innenB = Entlang(
                    strassenkante[(kante + 1) % strassenkante.Count]);
                var anfang = Math.Max(
                    Math.Min(aussenA, aussenB), Math.Min(innenA, innenB));
                var ende = Math.Min(
                    Math.Max(aussenA, aussenB), Math.Max(innenA, innenB));
                /**
                 * DIE KANTE ZERFAELLT AN JEDER ZUFAHRT IN EIGENE ABSCHNITTE.
                 *
                 * Vorher lief ueber die ganze Kante EIN Buchtraster, und die
                 * Zufahrt fuhr mittendurch. Was sie traf, wurde spaeter in
                 * `Zufahrtsbauer` zu `Restbelag` erklaert: Asphaltschnipsel
                 * neben der Einfahrt, gemessen 2,95 m2 und 8,56 m2 bei einer
                 * lotrechten Zufahrt auf dem 175-m-Rechteck. Der Nutzer hat
                 * genau das im Spiel gesehen: "es sind irgendwelche Flaechen
                 * neben dem Eingang, aber kein Gras sondern Asphalt."
                 *
                 * Jetzt wird die Zufahrt VORHER freigehalten, mit genau einer
                 * Buchtbreite Luft auf JEDER Seite - Ansage des Nutzers:
                 * "bitte so breit wie eine Parkbucht, und zwar beide". Diese
                 * Luft plant niemand als Flaeche: wo keine Bucht liegt, faellt
                 * das Band in `Layout.cs` in seinen `else`-Zweig und wird
                 * `Zellart.Kappe`, also Gras. Danach schneidet die Zufahrt
                 * ihre eigene Fahrbahn mittig heraus, und was links und rechts
                 * stehen bleibt, ist die Graskappe.
                 *
                 * Damit die Luft WIRKLICH eine Buchtbreite misst, faengt das
                 * Raster jedes Abschnitts an seiner Zufahrtsseite buendig an.
                 * Ein mittig ausgerichtetes Raster haette die Luft um den
                 * halben Rest breiter gemacht - und auf beiden Seiten
                 * verschieden. Der Rest wandert deshalb ans andere Ende, wo er
                 * ohnehin an die Ecke stoesst. Dasselbe tut der Prototyp:
                 * "Nur die aeussere Reihe erhaelt an Einfahrten zusaetzliche
                 * Neustarts."
                 */
                var abschnitte = Abschnitte(
                    anfang, ende, zufahrten, kante, laenge, tangente,
                    buchtbreite);

                var grenzzaehler = 0;
                foreach (var abschnitt in abschnitte)
                {
                    var nutzbar = abschnitt.Bis - abschnitt.Von;
                    if (nutzbar < buchtbreite) continue;

                    /**
                     * HIER WIRD NICHTS MEHR RESERVIERT.
                     *
                     * Bis zum 2026-08-25 stand hier dieselbe Rechnung wie an
                     * einer Querstrasse: zwei volle Buchtbreiten abziehen, den
                     * Rest haelftig auf beide Enden verteilen. Das war aus der
                     * Querstrassenformel uebernommen und an dieser Stelle
                     * falsch.
                     *
                     * Eine Kappe trennt eine Buchtreihe optisch von einer
                     * Fahrbahn, die sie kreuzt. Am Ende einer Kante kreuzt die
                     * Randreihe gar nichts - dort stoesst sie auf die Reihe
                     * der NACHBARKANTE, und die gemeinsame Projektion oben
                     * haelt beide bereits auseinander. Was uebrig bleibt, ist
                     * die Ecke selbst; die gab es auch vorher schon.
                     *
                     * Gemessen am Rechteck 175 x 105 m: die Reserve kostete je
                     * Kante zwei Buchten, zusammen acht, und blies die Ecke
                     * von 1,1 m auf 4,1 m auf. Nutzeransage: "schau nochmal
                     * nach, ob die Kappen-Regel auch fuer Parkbuchten aussen
                     * an der Randstrasse gilt - duerfte naemlich eigentlich
                     * keine sein."
                     */
                    var anzahl = (int)Math.Floor(nutzbar / buchtbreite);
                    if (anzahl <= 0) continue;
                    var rest = nutzbar - anzahl * buchtbreite;
                    var buchtanfang = abschnitt.AnZufahrtVorn
                        ? abschnitt.Von
                        : abschnitt.AnZufahrtHinten
                            ? abschnitt.Von + rest
                            : abschnitt.Von + rest / 2;

                    for (var grenze = 0; grenze <= anzahl; grenze++)
                    {
                        var entlang = buchtanfang + grenze * buchtbreite;
                        var aussen = ursprung + tangente * entlang
                            + innennormale * randabstand;
                        var innen = ursprung + tangente * entlang
                            + innennormale * strassenkantentiefe;
                        schnitte.Add(new Randreihenschnitt
                        {
                            Linie = linienregister.Randbuchtgrenze(
                                aussen, innen, kante, grenzzaehler++),
                            Aussen = aussen,
                            Innen = innen,
                        });
                    }
                    for (var nummer = 0; nummer < anzahl; nummer++)
                    {
                        var von = buchtanfang + nummer * buchtbreite;
                        var bis = von + buchtbreite;
                        /*
                         * KEINE RANDBUCHT AUF BAULAND - dieselbe Regel wie
                         * im Inneren, nur an der anderen Stelle. Die
                         * Randreihe plant ihre Buchten selbst und ging
                         * deshalb an `IstGueltigeBucht` vorbei; gemessen
                         * blieben so bis zu 14 Buchten mitten in der
                         * Baulandflaeche stehen.
                         */
                        var pruefEcken = new[]
                        {
                            ursprung + tangente * von
                                + innennormale * randabstand,
                            ursprung + tangente * bis
                                + innennormale * randabstand,
                            ursprung + tangente * bis
                                + innennormale * strassenkantentiefe,
                            ursprung + tangente * von
                                + innennormale * strassenkantentiefe,
                        };
                        if (Layoutplanung.BeruehrtBauland(pruefEcken, bauland))
                            continue;
                        // Und nicht in die Fahrbahn der Randzoning-Strasse -
                        // siehe `BeruehrtRandzoningstrasse`.
                        if (Layoutplanung.BeruehrtRandzoningstrasse(
                                pruefEcken, randzoning, randzoningachstiefe,
                                randzoninghalbebreite))
                            continue;
                        buchten.Add(new Randbuchtplan
                        {
                            Id = ersteBuchtId + buchten.Count,
                            Randkante = kante,
                            Ecken = new[]
                            {
                                ursprung + tangente * von
                                    + innennormale * randabstand,
                                ursprung + tangente * bis
                                    + innennormale * randabstand,
                                ursprung + tangente * bis
                                    + innennormale * strassenkantentiefe,
                                ursprung + tangente * von
                                    + innennormale * strassenkantentiefe,
                            },
                        });
                    }
                }
            }
            return new Randreihenplan
            {
                Buchten = buchten,
                Schnittlinien = schnitte,
            };
        }

        /**
         * Die nutzbare Strecke einer Kante, zerlegt an den Zufahrten.
         *
         * Je Zufahrt faellt ihre eigene Breite heraus PLUS eine Buchtbreite
         * auf jeder Seite - das ist die Graskappe. Was zwischen zwei solchen
         * Sperren uebrig bleibt, ist ein eigener Abschnitt mit eigenem
         * Buchtraster.
         *
         * Eine schraege Zufahrt - der Eckfang gibt eine eigene Achse vor -
         * belegt entlang der Kante mehr als ihre Breite. Deshalb wird durch
         * die Projektion der Achse auf die Kantennormale geteilt, genau wie in
         * `Zufahrtsbauer.Plane`. Bei einer lotrechten Zufahrt ist dieser
         * Faktor 1.
         */
        private static List<Abschnitt> Abschnitte(
            double anfang,
            double ende,
            IReadOnlyList<Zufahrtsvorgabe> zufahrten,
            int kante,
            double kantenlaenge,
            Punkt tangente,
            double buchtbreite)
        {
            var sperren = new List<Abschnitt>();
                var normale = new Punkt(-tangente.Y, tangente.X);
                foreach (var zufahrt in zufahrten)
                {
                    if (zufahrt.Kante != kante) continue;
                    if (zufahrt.Along < 0 || zufahrt.Along > kantenlaenge)
                        continue;
                    var achse = zufahrt.AchsrichtungLokal ?? normale;
                    var achslaenge = Geometrie.Laenge(achse);
                    if (achslaenge == 0) continue;
                    var projektion = Geometrie.Skalar(
                        achse * (1 / achslaenge), normale);
                    if (projektion <= 0.3) continue;
                    /*
                     * DER FUSSWEG BEKOMMT KEINE GRASKAPPE.
                     *
                     * Die Buchtbreite Luft auf jeder Seite ist genau das, was
                     * spaeter zur Graskappe wird (siehe die Begruendung
                     * oben). Fuer eine Zufahrt ist sie gewollt: sie trennt die
                     * Fahrbahn optisch von den Buchten daneben.
                     *
                     * Ein 2-m-Fussweg trennt nichts - dort standen links und
                     * rechts nur zwei Grasnasen, die breiter waren als der Weg
                     * selbst. Ansage des Nutzers am 2026-08-27: "Fuer den
                     * Fussgaengerzugang braucht es keine Gruenflaeche
                     * daneben."
                     *
                     * Ohne Luft bleibt exakt der Korridor freigehalten, und
                     * die Randbuchten laufen bis an den Weg heran.
                     */
                    var luft = zufahrt.Art == Zufahrtsart.Fussweg
                        ? 0.0
                        : buchtbreite;
                    var halb = zufahrt.Breite / 2 / projektion + luft;
                    sperren.Add(new Abschnitt
                    {
                        Von = zufahrt.Along - halb,
                        Bis = zufahrt.Along + halb,
                    });
                }
            sperren.Sort((a, b) => a.Von.CompareTo(b.Von));

            var abschnitte = new List<Abschnitt>();
            var laufend = anfang;
            var vorherZufahrt = false;
            foreach (var sperre in sperren)
            {
                if (sperre.Bis <= laufend)
                {
                    // Zwei Zufahrten so dicht beieinander, dass sich ihre
                    // Kappen ueberlappen: die zweite verschiebt nur die Grenze.
                    if (sperre.Bis > laufend) laufend = sperre.Bis;
                    vorherZufahrt = true;
                    continue;
                }
                if (sperre.Von > laufend)
                    abschnitte.Add(new Abschnitt
                    {
                        Von = laufend,
                        Bis = Math.Min(sperre.Von, ende),
                        AnZufahrtVorn = vorherZufahrt,
                        AnZufahrtHinten = sperre.Von <= ende,
                    });
                laufend = Math.Max(laufend, sperre.Bis);
                vorherZufahrt = true;
                if (laufend >= ende) break;
            }
            if (laufend < ende)
                abschnitte.Add(new Abschnitt
                {
                    Von = laufend,
                    Bis = ende,
                    AnZufahrtVorn = vorherZufahrt,
                    AnZufahrtHinten = false,
                });
            return abschnitte;
        }

        /**
         * Ein Stueck Kante, auf dem Buchten liegen duerfen.
         *
         * `AnZufahrtVorn` / `AnZufahrtHinten` sagen, an welchem Ende eine
         * Zufahrtskappe anschliesst. Nur dort muss das Raster buendig sitzen,
         * damit die Kappe genau eine Buchtbreite misst; der Rest wandert ans
         * andere Ende.
         */
        private struct Abschnitt
        {
            internal double Von;
            internal double Bis;
            internal bool AnZufahrtVorn;
            internal bool AnZufahrtHinten;
        }
    }
}
