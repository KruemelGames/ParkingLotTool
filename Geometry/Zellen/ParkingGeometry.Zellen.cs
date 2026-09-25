using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry.Zellen;
using Unity.Mathematics;
using ZellMaterial = ParkingLotTool.Geometry.Zellen.Material;

namespace ParkingLotTool.Geometry
{
    public static partial class ParkingGeometry
    {
        private const double ZellenEingangsgitter = 0.001;

        private sealed class ZellenEingabe
        {
            internal Punkt[] Normalisiert { get; set; }
            internal Zufahrtsvorgabe[] Zufahrten { get; set; }
        }

        private sealed class ZellenStrasse
        {
            internal string Kind { get; set; }
            internal Zufahrtsart Art { get; set; }
            /**
             * VOM NUTZER GESETZT oder vom Plan selbst erzeugt?
             *
             * Ohne Randstrassen legt der ringlose Plan an beiden Enden je
             * einen Fussweg ueber die ganze Breite an. Die sind Infrastruktur,
             * kein Zugang, den jemand gewaehlt hat - und sie duerfen die
             * Sonderplaetze nicht von der gesetzten Zufahrt wegziehen.
             */
            internal bool Gesetzt { get; set; }
            /**
             * AUS EINEM RANDZONING-ABSCHNITT ENTSTANDEN?
             *
             * Nur dann gilt "Innenseite immer aus". Frueher wurde das an der
             * Lage abgelesen; seit die RZ-Strasse eine Fahrgasse ist, geht
             * das nicht mehr - siehe `ParkingLayout.RandzoningRoad`.
             */
            internal bool Randzoning { get; set; }
            /**
             * WO IST INNEN? Einheitsvektor von der Strasse in den Parkplatz,
             * im lokalen Rahmen. Nur am Abschnittsweg gesetzt; Nullvektor
             * heisst "nicht bekannt, entscheide wie frueher ueber die
             * Polygonmitte".
             */
            internal Punkt RandzoningInnen { get; set; }
            internal Punkt A { get; set; }
            internal Punkt B { get; set; }
            internal bool Teilflaechenverbindung { get; set; }
        }

        private sealed class ZellenSchnitt
        {
            internal double T { get; set; }
            internal Punkt Punkt { get; set; }
        }

        private static ParkingLayout BuildZellen(float2[] site, LayoutSettings settings)
        {
            /*
             * MEHR ALS EIN TEIL: JEDES WIRD EIN EIGENER PARKPLATZ.
             *
             * Vorschlag des Nutzers vom 2026-09-01, gemessen besser als der
             * bisherige Weg - Begruendung und Zahlen stehen in
             * `BuildTeilflaechenEinzeln`. Das Teilflaechenraster darunter
             * wird dadurch nicht mehr benutzt; ausgebaut ist es noch nicht,
             * damit dieser Schritt nachvollziehbar bleibt.
             */
            /*
             * NUR BEI GEZOGENEN SCHNITTEN.
             *
             * Der Nutzer hat den Vorschlag fuer SEINE Schnitte gemacht:
             * *"Wir haben eine ordentliche Trennung durch die Strasse
             * zwischen den verbundenen Punkten."* Auf die automatische
             * Zerlegung angewandt faellt der Testfall der L-Form von 369 auf
             * 118 Buchten und laesst zwei Drittel der Flaeche unbedeckt: ihre
             * Teile sind Rechenteile, keine Parkplaetze - schmal, ohne
             * eigenen Zugang, ohne eigene Strassenanbindung.
             *
             * Ein gezogener Schnitt ist etwas anderes: er verbindet zwei
             * Ecken des Umrisses, und beide Seiten sind Stuecke, die der
             * Nutzer selbst als Parkplatz gemeint hat.
             */
            /*
             * EINE RANDSTRASSE UMS GANZE - der Split teilt nur das Innere.
             *
             * Entscheidung des Nutzers vom 2026-09-01, nachdem er den Bau
             * gesehen hatte: *"die normalen Randstrassen sollen weiter laufen
             * wie als haette es keinen Split gegeben. Der Split sorgt nicht
             * ploetzlich dafuer, dass mehr Randstrassen entstehen oder sie
             * anders verlaufen."*
             *
             * Damit faellt der Weg "jedes Teilstueck ein eigener Parkplatz"
             * weg: er gab jedem Teil einen eigenen Ring, und am Schnitt lagen
             * drei Strassen nebeneinander. Er hatte mehr Buchten gebracht
             * (Randbuchten 144 -> 187), aber die kamen genau aus den
             * doppelten Randreihen, die hier nicht sein sollen.
             *
             * `BuildTeilflaechenEinzeln` bleibt vorerst im Baum stehen - der
             * Weg ist gemessen und beschrieben, und die Entscheidung dagegen
             * ist eine Gestaltungsfrage, keine Sackgasse.
             */
            return BuildZellenEinzeln(site, settings);
        }

        private static ParkingLayout BuildZellenEinzeln(
            float2[] site, LayoutSettings settings)
        {
            var eingabe = ZellenNormalisiereEingabe(site, settings);
            var quellpolygon = eingabe.Normalisiert
                .Select(pt => new double2(pt.X, pt.Y)).ToArray();
            /*
             * OHNE GEZOGENEN SCHNITT BLEIBT ES BEIM ALTEN WEG.
             *
             * Hierher kommt nur noch, wer KEINE Handschnitte hat: ein
             * einzelnes Teil, oder eine automatische Zerlegung mit eigenen
             * Winkeln (so bauen die Parkplaetze, die vor dem 2026-09-01
             * entstanden sind). Diese Zeile auf `null` zu setzen hat den
             * Testfall der L-Form still von 369 auf 355 Buchten gedrueckt -
             * die Teilwinkel wirkten nicht mehr, sie wurden nur noch
             * gemeldet.
             */
            var teilplan = HatTeilflaechenVorgaben(settings)
                ? PlaneTeilflaechen(quellpolygon, settings)
                : null;
            var teilnaehte = teilplan == null
                ? null
                : PlaneTeilflaechenNaehte(
                    teilplan, nurVerschiedeneWinkel: true);
            /*
             * DER RASTERWEG LOHNT NUR, WENN EIN TEIL WIRKLICH ANDERS LIEGT.
             *
             * Der Live-Log des Nutzers hat es am 2026-09-01 gezeigt: seine
             * Parkplaetze haben fast immer EINE Teilflaeche, und trotzdem lief
             * der Bau ueber das Teilflaechenraster statt ueber das
             * gewoehnliche Bandraster. Gemessen an derselben Form, bei
             * gleichem Ergebnis:
             *
             *     ohne Ausrichtung  237 ms   s-band   107 ms
             *     mit Ausrichtung   618 ms   s-raster 557 ms
             *
             * und an einer groesseren Form 3328 ms. Der Rasterweg schneidet je
             * Rasterlinie einzeln durch die ganze Fragmentliste - bei 320
             * Linien und 11.000 Fragmenten ist das viel Arbeit fuer ein
             * Ergebnis, das der Bandweg in einem Zug liefert.
             *
             * Liegt jede Teilflaeche im GLOBALEN Bezugswinkel, hat der
             * Rasterweg nichts zu unterscheiden - er baut dasselbe, nur
             * teurer. `PruefeEinheitlicherWinkel` im Prueflauf haelt das fest:
             * 346 Buchten, 7 Gras, 23 Asphalt auf beiden Wegen, 769 gegen
             * 200 ms.
             *
             * Naehte gehen dabei nicht verloren: die werden ohnehin nur
             * zwischen Teilen mit VERSCHIEDENEN Winkeln geplant.
             *
             * Der Plan selbst bleibt bestehen - die Statistik unten meldet
             * weiterhin, welche Teilflaeche in welchem Winkel liegt.
             */
            /*
             * DIE ABKUERZUNG DARF NICHT AM GLOBALEN WINKEL HAENGEN.
             *
             * Hier stand bis zum 2026-09-01 zusaetzlich
             * `settings.Ausrichtwinkel.HasValue`. Die Winkel der Teilflaechen
             * stehen aber in `TeilflaechenAusrichtungen` und nicht zwingend
             * im globalen Rueckfallwert. Lagen zwei verschiedene Teilwinkel
             * vor, waehrend der globale Wert leer war, sprang die Abkuerzung
             * trotzdem an: gebaut wurde EIN Raster, der zweite Winkel fiel
             * still weg - und `PlaneTeilflaechen` meldete beide Winkel
             * weiterhin brav in der Statistik. Es sah also richtig aus.
             *
             * Gefunden von Codex im Auftrag des Nutzers, mit Gegenexperiment:
             * globaler Wert auf null, Teilwinkel 144,6 und 168,7 - mit der
             * Bedingung 350 Buchten, ohne sie 358.
             *
             * Der globale Winkel wird nur noch dort gebraucht, wo er
             * hingehoert: als Vergleichswert. Ohne ihn ist der Bezug die
             * laengste Kante, und `Reihenwinkel` liefert genau das.
             */
            var globalerWinkel = Reihenwinkel(
                settings, LaengsteKante(quellpolygon));
            var rasterNoetig = teilplan != null
                && teilplan.Any(plan =>
                    VerschiedeneAchsen(plan.Winkel, globalerWinkel));
            var zelleneinstellungen = new Zelleneinstellungen
            {
                Randabstand = settings.Es,
                Fahrgassenbreite = settings.Ai,
                Querstrassenbreite = settings.Cw,
                Buchttiefe = settings.Sl,
                Buchtbreite = settings.Sw,
                Gruenstreifenbreite = settings.Md,
                Querstrassenabstand = settings.Cr,
                Querstrassenkappen = settings.Qk,
                Randstrassen = settings.Randstrassen,
                // `edge` ist genau der im Versuch gemessene Rahmen aus der
                // laengsten Kante. Alle anderen Modi benutzen den vom Nutzer
                // gesetzten Winkel; eine 36-Winkel-Suche besitzt der Versuch
                // noch nicht.
                /*
                 * DERSELBE WINKEL WIE IM ALTEN RECHENWEG.
                 *
                 * Hier stand eine eigene Ableitung: `edge` gab `null` (nimm
                 * die laengste Kante), alles andere den ABSOLUTEN Reglerwert.
                 * Damit las dieser Weg die gewaehlte Bezugslinie gar nicht,
                 * "Quer" drehte nicht, und von "Kante" auf "Fest" sprang der
                 * Parkplatz um den Winkel der laengsten Kante. Da der Nutzer
                 * genau diesen Weg faehrt, kam von meinen Winkeländerungen
                 * nichts bei ihm an.
                 *
                 * `null` bleibt der Sonderfall "unveraendert wie bisher":
                 * schlichtes `edge` ohne Bezugslinie. Sonst wird der Winkel
                 * einmal zentral gerechnet und als fertige Zahl uebergeben.
                 */
                Reihenwinkel = string.Equals(
                        settings.AngleMode, "edge", StringComparison.Ordinal)
                    && !settings.Ausrichtwinkel.HasValue
                    ? (double?)null
                    : Reihenwinkel(settings, LaengsteKante(quellpolygon)),
                Zoningflaechen = (settings.Zoningflaechen
                        ?? Array.Empty<ParkingGeometry.Zoningflaeche>())
                    .Where(f => f != null && f.Spalten > 0 && f.Reihen > 0)
                    .Select(f => new Zoningvorgabe
                    {
                        Ecke = new Punkt(f.Ecke.x, f.Ecke.y),
                        Spalten = f.Spalten,
                        Reihen = f.Reihen,
                        Winkel = f.Winkel,
                        Rand = f.Rand,
                        Aussen = f.Aussentiefen,
                    })
                    .ToArray(),
                /*
                 * WELTKOORDINATEN, wie die Zoningflaechen eine Zeile darueber.
                 * `Layout.Baue` dreht beides gemeinsam in seinen Rahmen; hier
                 * schon zu drehen hiesse, es zweimal zu tun.
                 */
                Randzoning = (settings.Randzoning
                        ?? Array.Empty<ParkingGeometry.RandzoningLinie>())
                    .Where(l => l != null)
                    .Select(l => (A: new Punkt(l.A.x, l.A.y),
                        B: new Punkt(l.B.x, l.B.y)))
                    .ToArray(),
                Teilflaechen = !rasterNoetig
                    ? Array.Empty<Teilflaechenrahmenvorgabe>()
                    : teilplan.Select(plan => new Teilflaechenrahmenvorgabe
                    {
                        Index = plan.Index,
                        PunkteWelt = plan.Polygon
                            .Select(p => new Punkt(p.x, p.y)).ToArray(),
                        Winkel = plan.Winkel,
                        EigeneZuweisung = plan.EigeneZuweisung,
                        Trennkanten = Trennkanten(plan.Polygon, quellpolygon),
                    }).ToArray(),
                Teilflaechennaehte = !rasterNoetig || teilnaehte == null
                    ? Array.Empty<Teilflaechennahtvorgabe>()
                    : teilnaehte.Select(naht => new Teilflaechennahtvorgabe
                    {
                        ErstesTeil = naht.ErstesTeil,
                        ZweitesTeil = naht.ZweitesTeil,
                        AnfangWelt = new Punkt(naht.Linie.A.x, naht.Linie.A.y),
                        EndeWelt = new Punkt(naht.Linie.B.x, naht.Linie.B.y),
                    }).ToArray(),
            };
            var form = new Formdefinition("Mod-Polygon", eingabe.Normalisiert);
            var bau = Layoutbauer.Baue(
                form, zelleneinstellungen, eingabe.Zufahrten);
            var layout = ZellenZuLayout(bau, settings);
            SetzeTeilflaechenStatistik(
                layout, quellpolygon, settings, teilplan);
            return layout;
        }

        /**
         * Welche Kanten dieses Teils sind TRENNKANTEN, welche Umriss?
         *
         * Eine Kante gehoert zum Umriss, wenn ihre Mitte auf dem Umrissrand
         * liegt - das faengt auch Teilstuecke einer Umrisskante, wie sie die
         * automatische Zerlegung erzeugt. Alles andere ist ein Schnitt.
         *
         * Die Ausgabe ist so gedreht, dass das Teil LINKS der Kante liegt:
         * das Teilpolygon wird dafuer gegen den Uhrzeigersinn gelesen.
         */
        private static (Punkt A, Punkt B)[] Trennkanten(
            double2[] teil, double2[] umriss)
        {
            var ring = teil.ToList();
            var flaeche = 0.0;
            for (var i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];
                flaeche += a.x * b.y - b.x * a.y;
            }
            if (flaeche < 0) ring.Reverse();

            var ausgabe = new List<(Punkt, Punkt)>();
            for (var i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];
                var mitte = (a + b) * 0.5;
                if (DistToBoundary(mitte, umriss) <= 0.01) continue;
                ausgabe.Add((new Punkt(a.x, a.y), new Punkt(b.x, b.y)));
            }
            return ausgabe.ToArray();
        }

        private static ZellenEingabe ZellenNormalisiereEingabe(
            float2[] site,
            LayoutSettings settings)
        {
            if (site == null || site.Length < 3)
                throw new ArgumentException(
                    "The cell engine needs a polygon with at least three corners.",
                    nameof(site));

            var original = site.Select(punkt => new Punkt(
                Math.Round(punkt.x / ZellenEingangsgitter) * ZellenEingangsgitter,
                Math.Round(punkt.y / ZellenEingangsgitter) * ZellenEingangsgitter))
                .ToList();
            if (original.Count > 3
                && ZellenGleich(original[0], original[original.Count - 1]))
                original.RemoveAt(original.Count - 1);
            if (original.Count < 3 || Geometrie.Vorzeichenflaeche(original) == 0)
                throw new ArgumentException(
                    "The cell engine needs a polygon with positive area.",
                    nameof(site));

            var normalisiert = original.ToList();
            var umgedreht = Geometrie.Vorzeichenflaeche(normalisiert) < 0;
            if (umgedreht) normalisiert.Reverse();

            var vorgaben = new List<Zufahrtsvorgabe>();
            foreach (var entrance in settings.Entrances ?? Array.Empty<Entrance>())
            {
                if (entrance == null) continue;
                if (entrance.Edge < 0 || entrance.Edge >= original.Count)
                    throw new ArgumentException(
                        $"Entrance: edge {entrance.Edge} is not part of the polygon.",
                        nameof(settings));
                var kante = entrance.Edge;
                var along = entrance.Along;
                var eckenanfang = string.Equals(
                    entrance.Corner, "start", StringComparison.Ordinal)
                    ? (bool?)true
                    : string.Equals(entrance.Corner, "end", StringComparison.Ordinal)
                        ? false : (bool?)null;
                if (umgedreht)
                {
                    var a = original[entrance.Edge];
                    var b = original[(entrance.Edge + 1) % original.Count];
                    var laenge = Geometrie.Laenge(b - a);
                    kante = Geometrie.Mod(
                        original.Count - 2 - entrance.Edge, original.Count);
                    along = laenge - entrance.Along;
                    if (eckenanfang.HasValue) eckenanfang = !eckenanfang.Value;
                }

                if (eckenanfang.HasValue)
                {
                    var quelle = normalisiert.Select(punkt =>
                        new double2(punkt.X, punkt.Y)).ToArray();
                    var fit = EntranceCornerFit(
                        quelle, settings, kante, eckenanfang.Value);
                    if (fit != null)
                    {
                        vorgaben.Add(new Zufahrtsvorgabe(
                            kante,
                            fit.Along,
                            new Punkt(fit.Direction.x, fit.Direction.y),
                            fit.Length,
                            entrance.Breite(settings.Ai, settings.Gassenbreite),
                            entrance.Art));
                        continue;
                    }
                }

                var achse = entrance.AxisDirection;
                vorgaben.Add(new Zufahrtsvorgabe(
                    kante, along,
                    achse.HasValue ? new Punkt(achse.Value.x, achse.Value.y) : (Punkt?)null,
                    entrance.AxisLength,
                    entrance.Breite(settings.Ai, settings.Gassenbreite), entrance.Art));
            }

            return new ZellenEingabe
            {
                Normalisiert = normalisiert.ToArray(),
                Zufahrten = vorgaben.ToArray(),
            };
        }

        private static ParkingLayout ZellenZuLayout(
            Bauergebnis bau,
            LayoutSettings settings)
        {
            if (bau.Flaechen.Any(flaeche => flaeche.Loecher.Count != 0))
                throw new InvalidOperationException(
                    "The cell adapter cannot output material surfaces with holes.");

            var buchtgruppen = bau.Zellen
                .Where(zelle => zelle.BuchtId.HasValue)
                .GroupBy(zelle => zelle.BuchtId.Value)
                .OrderBy(gruppe => gruppe.Key)
                .ToArray();
            var buchten = buchtgruppen
                .Select(gruppe => ZellenBucht(bau, gruppe))
                .ToArray();
            var buchtarten = buchtgruppen
                .Select(gruppe => bau.Randbuchten.ContainsKey(gruppe.Key)
                    ? BayKind.Perimeter
                    : BayKind.Inner)
                .ToArray();
            /**
             * EINE GROSSE FLAECHE STATT VIELER KLEINER.
             *
             * Sind beide Kategorien dasselbe Prefab, kaeme ueberall dasselbe
             * Material heraus - nur in 18 bis 41 Einzelstuecken. Gemessen am
             * 2026-08-22: Rechteck 18, L schraeg 41.
             *
             * An der WURZEL, nicht hinterher verschmolzen: das gezeichnete
             * Areal IST der Umriss des Belags (die Zellen decken es
             * vollstaendig ab, bis hinaus zum Randband). Es gibt also nichts
             * zu vereinigen - und damit auch keine Naht, die entstehen
             * koennte. Genau die Fehlerquelle, um die es den ganzen Tag ging.
             *
             * Alles landet in `AsphaltSurface`; welches der beiden Prefabs der
             * Bau daraus macht, ist gleich, sie sind ja dasselbe.
             */
            var gras = bau.Flaechen
                .Where(flaeche => flaeche.Material == ZellMaterial.Gruen)
                .Select(flaeche => ZellenRing(bau.Rahmen, flaeche.Aussenring))
                .ToArray();
            var asphalt = bau.Flaechen
                .Where(flaeche => flaeche.Material == ZellMaterial.Asphalt)
                .Select(flaeche => ZellenRing(bau.Rahmen, flaeche.Aussenring))
                .ToArray();
            var zoningflaechen = bau.Flaechen
                .Where(flaeche => flaeche.Material == ZellMaterial.Zoning)
                .Select(flaeche => ZellenRing(bau.Rahmen, flaeche.Aussenring))
                .ToArray();
            // Eigene Liste, weil der Korridor ein anderes Prefab braucht:
            // eine Flaeche mit Ebenenmaske Terrain ist auf einer Strasse
            // unsichtbar, auch auf einer unsichtbaren.
            var zoningstrassen = bau.Flaechen
                .Where(flaeche => flaeche.Material == ZellMaterial.Zoningstrasse)
                .Select(flaeche => ZellenRing(bau.Rahmen, flaeche.Aussenring))
                .ToArray();

            /*
             * DURCH DIE CS2-ANNAHMEPRUEFUNG - beide Zoning-Listen.
             *
             * Gras und Asphalt laufen hier seit jeher hindurch; die beiden
             * Zoning-Listen sind spaeter dazugekommen und wurden vergessen.
             * Folge im Spiel: der Bauzettel meldete "Fahrbahn der
             * Zoning-Strasse: 2 Ring(e) (Prefab da)", und der Nutzer sah
             * trotzdem nichts - CS2 hatte die Ringe still verworfen.
             *
             * Der Korridor ist besonders anfaellig: er ist nur 8 m breit
             * und laeuft um die Ecke, und `Game.Areas.GeometrySystem`
             * versetzt jeden Knoten erst 0,1 m nach innen, bevor es
             * dreieckt. Genau solche Ringe fallen dabei durch.
             */
            // Sammelt, was CS2 nachweislich ablehnt und auch der
            // Sehnenschnitt nicht rettet. Siehe PlaneZellenCs2Flaechen.
            var unbaubareRinge = new List<float2[]>();
            // Ringe, die CS2 NEHMEN wuerde und die wir trotzdem weglassen,
            // weil sie Haarrisse sind. Getrennt gezaehlt - siehe die Meldung.
            var haarrisse = new List<float2[]>();
            zoningflaechen = PlaneZellenCs2Flaechen(
                zoningflaechen, true, unbaubareRinge);
            zoningstrassen = PlaneZellenCs2Flaechen(
                zoningstrassen, true, unbaubareRinge);
            /*
             * OHNE SCHRANKE, UND ZWAR ABSICHTLICH.
             *
             * Codex hatte diesen Aufruf an den gezogenen Teilflaechenschnitt
             * gebunden - dort war der Fehler aufgefallen. GEMESSEN am
             * 2026-09-01: auch ZWEI Formen des Waechters, ganz ohne Schnitt,
             * verloren je eine Flaeche an CS2. Eine Absicherung, die nur in
             * einem von drei Betriebsarten greift, ist eine Falle.
             *
             * Kosten: --teilflaechenwinkel 221/228 ms gegen 221/222 ms mit
             * Schranke und 217/215 ms davor. Das ist Rauschen.
             */
            /*
             * ENTWIRREN NUR OHNE RANDSTRASSE - und zwar gemessen.
             *
             * Der Kommentar an `PlaneZellenCs2Flaechen` hielt das Zerlegen
             * selbstberuehrender Ringe von Gras und Belag fern, "bis derselbe
             * Befund auch dort mit Zahlen belegt ist". Die Zahlen liegen jetzt
             * vor, aus dem Problembericht des Nutzers vom 2026-09-08, 18:28:
             * drei Ringe mit zusammen 1.009,16 m2 fielen bei CS2 durch. Es
             * war nicht bloss eine Luecke - der ganze Bau brach ab, weil
             * `AreaTransferMaterialized` alle Flaechen als Entities verlangt
             * ("Nur 137 von 138 Flaechen").
             *
             * ABER: global zugeschaltet bricht es die Paritaet. Gemessen am
             * 2026-09-08, dieselben Abweichungen wie am 2026-09-04 - "Referenz
             * 08s AUS" und "Nutzerpolygon" melden neue Werte. Mit Randstrasse
             * sitzen die Ringe seit Monaten anders zusammen; ihre
             * Beruehrpunkte sind echte Engstellen, keine Haarkanten.
             *
             * Also genau dort, wo es gebraucht wird und nichts Altes gefaehrdet:
             * im ringlosen Fall. Der ist neu und hat keinen Bestand, den eine
             * Zerlegung verschieben koennte. Mit Randstrasse bleibt alles, wie
             * es war - der Paritaetslauf belegt das.
             */
            var entwirreBelag = !settings.Randstrassen;
            var grasNachRolle = PlaneZellenCs2Flaechen(
                gras, entwirreBelag, unbaubareRinge, entwirreBelag, haarrisse);
            var asphaltNachRolle = PlaneZellenCs2Flaechen(
                asphalt, entwirreBelag, unbaubareRinge, entwirreBelag, haarrisse);
            gras = grasNachRolle;
            asphalt = asphaltNachRolle;

            /**
             * SONDERPLAETZE: dieselbe Rechnung wie im alten Weg, aufgerufen
             * statt nachgebaut.
             *
             * `AssignBayRoles` ist KEIN reines Etikett - pro Behindertengruppe
             * ersetzt es fuenf normale Buchten durch drei breitere. Deshalb
             * laeuft es hier, bevor die Listen eingefroren werden, und die
             * Buchtenzahl sinkt dabei (gemessen Rechteck 253 -> 249). Genau so
             * verhaelt sich der alte Weg auch; die Zahlen stimmen ueberein:
             * 6 Behinderte, 8 Elektro, 4 Ladesaeulen-Paare.
             *
             * Gelesen werden nur Bay, BayKind, EntranceLine und settings.Sw -
             * alle uebrigen WorkLayout-Felder bleiben leer.
             */
            if (settings.EineFlaeche)
            {
                // Das gezeichnete Areal IST der Umriss des Belags - die Zellen
                // decken es vollstaendig ab, bis hinaus zum Randband. Es gibt
                // also nichts zu vereinigen und damit auch keine Naht, die
                // entstehen koennte.
                asphalt = new[]
                {
                    ZellenAusrichten(bau.Rahmen, bau.ArealLokal.Punkte
                        .Select(punkt => ZellenPunkt(bau.Rahmen.NachWelt(punkt)))
                        .ToArray()),
                };
                gras = Array.Empty<float2[]>();
                asphalt = PlaneZellenCs2Flaechen(
                    asphalt, entwirreBelag, unbaubareRinge, entwirreBelag);
            }

            // Das Bauland liegt in Weltkoordinaten; die Wege rechnen im
            // Wurzelrahmen. Einmal umlegen, wie im Layout auch.
            var baulandLokal = (settings.Zoningflaechen
                    ?? Array.Empty<ParkingGeometry.Zoningflaeche>())
                .Where(f => f != null && f.Spalten > 0 && f.Reihen > 0)
                .Select(f =>
                {
                    // Winkel abziehen statt zurueckrechnen - dieselbe Regel
                    // wie im Layout. Ueber zwei umgerechnete Eckpunkte
                    // waechst der Fehler mit der Kantenlaenge, und das
                    // Zonenraster von CS2 verzeiht keine Winkelreste.
                    var rahmenwinkel = Math.Atan2(
                        bau.Rahmen.XAchse.Y, bau.Rahmen.XAchse.X)
                        * 180.0 / Math.PI;
                    return new Zoningvorgabe
                    {
                        Ecke = bau.Rahmen.NachLokal(
                            new Punkt(f.Ecke.x, f.Ecke.y)),
                        Spalten = f.Spalten,
                        Reihen = f.Reihen,
                        Winkel = f.Winkel - rahmenwinkel,
                        Rand = f.Rand,
                        Aussen = f.Aussentiefen,
                    };
                })
                .ToArray();
            // Die Randzoning-Linien einmal in den Rahmen des Kerns, wie die
            // Baulandflaechen eine Zeile darueber.
            var randzoningLokal = (settings.Randzoning
                    ?? Array.Empty<RandzoningLinie>())
                .Where(l => l != null)
                .Select(l => (
                    A: bau.Rahmen.NachLokal(new Punkt(l.A.x, l.A.y)),
                    B: bau.Rahmen.NachLokal(new Punkt(l.B.x, l.B.y))))
                .ToArray();
            var strassen = ZellenStrassen(bau, baulandLokal, randzoningLokal,
                settings.Ai, settings.Cw);
            var rollen = new WorkLayout();
            rollen.Bay.AddRange(buchten.Select(bucht =>
                bucht.Select(punkt => new double2(punkt.x, punkt.y)).ToArray()));
            rollen.BayKind.AddRange(buchtarten);
            foreach (var zufahrt in strassen.Where(s2 => s2.Kind == "entrance"))
            {
                var linie = ZellenLinie(bau.Rahmen, zufahrt);
                if (linie == null || linie.Length < 2) continue;
                // `AssignBayRoles` nimmt das Ende B als Anker fuer "nah an der
                // Zufahrt". Gemeint ist das INNERE Ende - dort kommen die Autos
                // an. Welches der beiden das ist, entscheidet der Abstand zur
                // Mitte des Rings.
                var a = linie[0];
                var b = linie[linie.Length - 1];
                var mitte = float2.zero;
                var ringpunkte = bau.Randstrassenmittellinie.Count;
                foreach (var punkt in bau.Randstrassenmittellinie)
                    mitte += ZellenPunkt(bau.Rahmen.NachWelt(punkt));
                if (ringpunkte > 0) mitte /= ringpunkte;
                var innen = math.distancesq(a, mitte) < math.distancesq(b, mitte) ? a : b;
                var aussen = innen.Equals(a) ? b : a;
                rollen.EntranceLine.Add(new Line2(
                    new double2(aussen.x, aussen.y), new double2(innen.x, innen.y)));
                /*
                 * DIE ART REIST MIT DER LINIE, nicht ueber einen Index.
                 *
                 * `AssignBayRoles` bevorzugt Fusswege als Anker fuer die
                 * Sonderplaetze und muss dafuer wissen, welche Linie zu
                 * welcher Art gehoert. Beide hier gemeinsam anzuhaengen ist
                 * die einzige Zuordnung, die jede spaetere Umsortierung
                 * ueberlebt - eine parallele Liste, die woanders gefuellt
                 * wird, waere genau die bruechige Variante.
                 *
                 * Der Zellenkern fuellte `Entrances` bisher gar nicht; im
                 * klassischen Kern passiert dasselbe in Build.Setup.
                 */
                rollen.Entrances.Add(new Entrance
                {
                    Edge = -1,
                    Along = 0,
                    Art = zufahrt.Art,
                    Gesetzt = zufahrt.Gesetzt,
                });
            }
            AssignBayRoles(rollen, settings);
            buchten = rollen.Bay
                .Select(bucht => bucht
                    .Select(punkt => new float2((float)punkt.x, (float)punkt.y))
                    .ToArray())
                .ToArray();
            buchtarten = rollen.BayKind.ToArray();
            var randstrasse = strassen.Where(
                strasse => strasse.Kind == "perimeter").ToList();
            var gassen = strassen.Where(strasse => strasse.Kind == "aisle").ToList();
            var quer = strassen.Where(strasse => strasse.Kind == "cross").ToList();
            var zufahrten = strassen.Where(strasse => strasse.Kind == "entrance").ToList();
            var netz = ZellenNetzfassung(strassen);
            var winkel = Math.Atan2(bau.Rahmen.XAchse.Y, bau.Rahmen.XAchse.X)
                * 180 / Math.PI;
            winkel = ((winkel % 180) + 180) % 180;

            // ENGLISCH, NICHT ZWEISPRACHIG. Die Geometrie wird auch vom
            // Testprojekt uebersetzt, und dort gibt es `Mod` und die
            // Einstellungen gar nicht - ein Sprachhelfer wuerde den Testbau
            // brechen. Englisch ist ohnehin der Standard des Mods.
            var warnungen = new List<string>
            {
            };
            if (bau.Ringlos != null) warnungen.AddRange(bau.Ringlos.Warnungen);
            warnungen.AddRange(bau.Schnittwarnungen);
            /*
             * Weggelassene Ringe gehoeren in den Bauzettel, nicht ins
             * Schweigen. Die Flaechensumme sagt sofort, ob es kosmetisch ist
             * oder ein echtes Loch.
             */
            if (unbaubareRinge.Count > 0)
            {
                var summe = 0.0;
                foreach (var ring in unbaubareRinge)
                    summe += Math.Abs(RingFlaeche(ring));
                // Die weggelassenen Ringe selbst ins Live-Log: ohne ihre
                // Form laesst sich nicht sagen, WARUM CS2 sie verworfen haette.
                if (LiveAn)
                    foreach (var ring in unbaubareRinge)
                        Live("  verworfener ring | " + ring.Length + " ecken | "
                            + Math.Abs(RingFlaeche(ring)).ToString("F2") + " m2 | "
                            + string.Join(" ", ring.Select(q => q.x.ToString("F3",
                                System.Globalization.CultureInfo.InvariantCulture) + ","
                                + q.y.ToString("F3", System.Globalization.CultureInfo.InvariantCulture))));
                // Die Meldung bleibt; ob sie dem Nutzer rot begegnet,
                // entscheidet die Anzeige (siehe ParkingLotToolSystem).
                warnungen.Add(
                    $"Cell engine: {unbaubareRinge.Count} surface ring(s) with "
                    + $"{summe:F2} m2 left out - CS2 would have refused them.");
            }
            if (haarrisse.Count > 0)
            {
                var summe = 0.0;
                foreach (var ring in haarrisse) summe += Math.Abs(RingFlaeche(ring));
                warnungen.Add(
                    $"Cell engine: {haarrisse.Count} hairline ring(s) with "
                    + $"{summe:F2} m2 left out - CS2 would have taken them, but "
                    + "they are thinner than 5 cm.");
            }
            if ((settings.Entrances == null || settings.Entrances.Length == 0)
                && settings.AutomaticEntrances)
                warnungen.Add(
                    "Cell engine: automatic entrances are not implemented; place them by hand.");
            if (settings.Auto
                && !string.Equals(settings.AngleMode, "edge", StringComparison.Ordinal))
                warnungen.Add(
                    "Cell engine: the automatic angle search is not implemented; "
                    + $"calculated with Angle={settings.Angle:R}.");
            /**
              * DIE LOCHTRENNUNG IST EIN WAECHTER, KEIN WERKZEUG.
              *
              * Sie schneidet eine Flaeche mit Loch auf, indem sie eine
              * Zellkette zur zweiten Flaeche macht. Das ist Reparatur nach dem
              * Bau - genau die Bauweise, die AGENTS.md verbietet.
              *
              * Bis zum 2026-08-25 lief sie bei JEDEM Parkplatz: die Randstrasse
              * verschmolz zu einem Ring, ein Ring hat ein Loch, also gab es
              * immer genau einen Schnitt. Die abgeschnittene Kette war eine
              * Zelle ueber die volle Strassenbreite - das Rechteck, das der
              * Nutzer auf der Strasse gemeldet hat. Dass sie immer ansprang,
              * stand die ganze Zeit im Bericht und wurde als Normalzustand
              * gelesen.
              *
              * Seit die Randstrasse als zwei Boegen gebaut wird, findet sie
              * nichts mehr (`Loecher 0 -> 0, Naehte 0`). Sie bleibt trotzdem
              * stehen - aber laut. Springt sie noch einmal an, verschmilzt
              * irgendwo eine neue Flaeche zum Ring, und DAS gehoert gefunden,
              * nicht klaglos weggeschnitten.
              *
              * Nutzerentscheidung am 2026-08-25: drinlassen, aber melden.
              */
            var trennung = bau.Lochtrennung;
            if (trennung != null && trennung.Trennnaehte != null
                && trennung.Trennnaehte.Count != 0)
                warnungen.Add(
                    "Cell engine: hole separation had to cut "
                    + $"{trennung.Trennnaehte.Count} seam(s) - "
                    + $"{trennung.LoecherVorher} hole(s) in "
                    + $"{trennung.LochflaechenVorher} surface(s). A surface "
                    + "merged into a ring; that is a construction fault "
                    + "upstream, not a repair job.");

            var logischeGassen = bau.Bandplan.Baender.Count(
                band => band.Art == Zellart.Fahrgasse);
            if (bau.InnereStrassen != null && bau.InnereStrassen.Count != 0)
                logischeGassen = bau.InnereStrassen.Count(strasse =>
                    strasse.Art == Zellart.Fahrgasse);
            /*
             * DIE RANDSTRASSE WIRD IN DER VORSCHAU AM BAULAND GEKAPPT.
             *
             * Als NETZ laeuft sie bewusst durch - sie erschliesst den
             * ganzen Parkplatz. Ihr BELAG wird an dieser Stelle aber
             * ohnehin durch das Bauland ersetzt, denn die Zoningflaeche
             * gewinnt gegen alles. Die Vorschau zeigte trotzdem ein
             * durchgehendes Band und log damit ueber das Ergebnis.
             *
             * Befund des Nutzers am 2026-09-03: *"Sowie das Abschneiden der
             * Randstrasse in der Preview, was derzeit nicht geschieht."*
             */
            var randstrasseVorschau = baulandLokal.Length == 0
                ? randstrasse
                : randstrasse
                    .SelectMany(strasse =>
                    {
                        var reste = new List<(Punkt A, Punkt B)>();
                        ZoningSchneidetWeg(strasse.A, strasse.B, baulandLokal,
                            reste);
                        return reste.Select(rest => new ZellenStrasse
                        {
                            Kind = strasse.Kind,
                            Art = strasse.Art,
                            A = rest.A,
                            B = rest.B,
                        });
                    })
                    .ToList();
            var perimeterQuads = ZellenVorschauStrassenlinien(
                bau.Rahmen, randstrasseVorschau, settings.Ai, true);
            float2[][] aisleQuads;
            float2[][] crossQuads;
            if (bau.InnereStrassen != null && bau.InnereStrassen.Count != 0)
                ZellenTeilflaechenStrassenquads(
                    bau.Rahmen,
                    perimeterQuads,
                    gassen,
                    quer,
                    settings.Ai,
                    settings.Cw,
                    out aisleQuads,
                    out crossQuads);
            else
            {
                aisleQuads = ZellenVorschauQuads(
                    bau, zelle => zelle.Art == Zellart.Fahrgasse, false);
                crossQuads = ZellenVorschauQuads(
                    bau, zelle => zelle.Art == Zellart.Querstrasse, false);
            }
            /*
             * JE ART EIN EIGENER LAUF - aus zwei Gruenden.
             *
             * Erstens braucht die Vorschau die Farbe je Rechteck, und die
             * haengt an der Art. Zweitens verschmilzt der Quaderbauer
             * benachbarte Zellen: eine Einfahrt neben einer Ausfahrt wuerde
             * sonst zu EINEM Rechteck, und die Unterscheidung waere schon vor
             * dem Faerben verloren.
             *
             * Zellen ohne Art (klassischer Rechenkern, alte Spielstaende)
             * zaehlen als Zufahrt - das ist der Standardwert der Aufzaehlung
             * und die Sorte, die es vorher als einzige gab.
             */
            var entranceQuadListe = new List<float2[]>();
            var entranceArtListe = new List<int>();
            foreach (Zufahrtsart art in Enum.GetValues(typeof(Zufahrtsart)))
            {
                var lokal = art;
                var quads = ZellenVorschauQuads(
                    bau,
                    zelle => zelle.Art == Zellart.Zufahrt
                        && (zelle.Zugangsart ?? Zufahrtsart.Zufahrt) == lokal,
                    false);
                entranceQuadListe.AddRange(quads);
                for (var i = 0; i < quads.Length; i++)
                    entranceArtListe.Add((int)lokal);
            }
            var entranceQuads = entranceQuadListe.ToArray();
            var greenQuads = ZellenVorschauQuads(
                bau, zelle => zelle.Material == ZellMaterial.Gruen, true, false);
            return new ParkingLayout
            {
                Bay = buchten,
                BayKind = buchtarten,
                BayRole = rollen.BayRole.ToArray(),
                ElectricPair = rollen.ElectricPair.ToArray(),
                SpecialStalls = new SpecialStallCounts
                {
                    Behindert = rollen.SpecialStalls.Behindert,
                    Elektro = rollen.SpecialStalls.Elektro,
                },
                Green = greenQuads,
                GrassSurface = gras,
                AsphaltSurface = asphalt,
                ZoningSurface = zoningflaechen,
                ZoningRoadSurface = zoningstrassen,
                GrassSurfaceByRole = settings.EineFlaeche
                    ? grasNachRolle : null,
                AsphaltSurfaceByRole = settings.EineFlaeche
                    ? asphaltNachRolle : null,
                PerimeterQuad = perimeterQuads,
                AisleQuad = aisleQuads,
                CrossQuad = crossQuads,
                EntranceQuad = entranceQuads,
                EntranceQuadArt = entranceArtListe.ToArray(),
                PerimeterLine = randstrasse.Select(strasse =>
                    ZellenLinie(bau.Rahmen, strasse)).ToArray(),
                AisleLine = gassen.Select(strasse =>
                    ZellenLinie(bau.Rahmen, strasse)).ToArray(),
                CrossLine = quer.Select(strasse =>
                    ZellenLinie(bau.Rahmen, strasse)).ToArray(),
                CrossRouteLine = quer.Select(strasse =>
                    ZellenLinie(bau.Rahmen, strasse)).ToArray(),
                EntranceLine = zufahrten.Select(strasse =>
                    ZellenLinie(bau.Rahmen, strasse)).ToArray(),
                NetLine = netz.Select(strasse => new NetSegment(
                    strasse.Kind,
                    ZellenPunkt(bau.Rahmen.NachWelt(strasse.A)),
                    ZellenPunkt(bau.Rahmen.NachWelt(strasse.B)),
                    strasse.Art))
                    .ToArray(),
                /*
                 * DIE ACHSEN DER RZ-STRASSEN, WIE SIE WIRKLICH LIEGEN.
                 *
                 * Damit muss weder die Vorschau noch die Seitenwahl sie an
                 * ihrer Lage erraten. Siehe `ParkingLayout.RandzoningRoad`.
                 */
                RandzoningRoad = netz
                    .Where(strasse => strasse.Randzoning)
                    .Select(strasse =>
                    {
                        var a = ZellenPunkt(bau.Rahmen.NachWelt(strasse.A));
                        var b = ZellenPunkt(bau.Rahmen.NachWelt(strasse.B));
                        /*
                         * EINE RICHTUNG WIRD GEDREHT, NICHT VERSCHOBEN.
                         * Deshalb die Differenz zweier verwandelter Punkte -
                         * `NachWelt` auf den Vektor allein losgelassen zeigte
                         * vom Weltnullpunkt aus irgendwohin.
                         */
                        var innen = ZellenPunkt(bau.Rahmen.NachWelt(
                                strasse.A + strasse.RandzoningInnen)) - a;
                        var laenge = math.length(innen);
                        return (A: a, B: b,
                            Innen: laenge < 1e-6f ? float2.zero : innen / laenge);
                    })
                    .ToArray(),
                Entrances = (settings.Entrances ?? Array.Empty<Entrance>())
                    .Where(entrance => entrance != null)
                    .Select(entrance => entrance.Clone())
                    .ToArray(),
                Ring = bau.Randstrassenmittellinie
                    .Select(punkt => ZellenPunkt(bau.Rahmen.NachWelt(punkt)))
                    .ToArray(),
                Stalls = buchten.Length,
                PerimeterStalls = buchtarten.Count(art => art == BayKind.Perimeter),
                InnerStalls = buchtarten.Count(art => art == BayKind.Inner),
                Angle = winkel,
                Aisles = logischeGassen,
                Parts = bau.KonvexeTeile.Count,
                TeilflaechenVerbindungen = bau.InnereStrassen?.Count(strasse =>
                    strasse.Teilflaechenverbindung) ?? 0,
                Warnings = warnungen.ToArray(),
            };
        }

        private static float2[] ZellenBucht(
            Bauergebnis bau,
            IGrouping<int, Zelle> fragmente)
        {
            Randbuchtplan randbucht;
            if (bau.Randbuchten.TryGetValue(fragmente.Key, out randbucht))
                return randbucht.Ecken
                    .Select(punkt => ZellenPunkt(bau.Rahmen.NachWelt(punkt)))
                    .ToArray();
            Punkt[] innenbucht;
            if (bau.Innenbuchten != null
                && bau.Innenbuchten.TryGetValue(fragmente.Key, out innenbucht))
                return innenbucht
                    .Select(punkt => ZellenPunkt(bau.Rahmen.NachWelt(punkt)))
                    .ToArray();
            var punkte = fragmente
                .SelectMany(zelle => zelle.Polygon.Punkte)
                .ToArray();
            var minX = punkte.Min(punkt => punkt.X);
            var maxX = punkte.Max(punkt => punkt.X);
            var minY = punkte.Min(punkt => punkt.Y);
            var maxY = punkte.Max(punkt => punkt.Y);
            return new[]
            {
                ZellenPunkt(bau.Rahmen.NachWelt(new Punkt(minX, minY))),
                ZellenPunkt(bau.Rahmen.NachWelt(new Punkt(maxX, minY))),
                ZellenPunkt(bau.Rahmen.NachWelt(new Punkt(maxX, maxY))),
                ZellenPunkt(bau.Rahmen.NachWelt(new Punkt(minX, maxY))),
            };
        }

        /**
         * DIE ERSTE KANTE GIBT DIE MUSTERRICHTUNG VOR.
         *
         * Nutzerbeweis vom 2026-08-21: zweimal dieselbe Grasflaeche gezogen,
         * nur mit anderer Startecke - die Grashalme liefen einmal waagerecht,
         * einmal senkrecht. Die Ausrichtung haengt also an der
         * KNOTENREIHENFOLGE.
         *
         * Ich hatte vorher das Gegenteil behauptet, weil `Areas.Node` und
         * `Areas.Triangle` im Dekompilat keine UV tragen. Das stimmt - die UV
         * entsteht erst beim Vernetzen, und zwar aus dem Ring. Meine
         * Schlussfolgerung war trotzdem falsch.
         *
         * Also wird der Ring so gedreht, dass er immer an der Kante beginnt,
         * die am ehesten laengs der REIHENRICHTUNG laeuft. Damit zeigt ein
         * gerichtetes Muster in dieselbe Richtung wie Buchten und Fahrgassen,
         * bei jeder Form und bei jeder Zeichenreihenfolge des Nutzers.
         */
        private static float2[] ZellenRing(Rahmen rahmen, Ring ring)
            => ZellenAusrichten(rahmen, ring.Kanten
                .Select(kante => ZellenPunkt(rahmen.NachWelt(kante.Von.Punkt)))
                .ToArray());

        private static float2[] ZellenAusrichten(Rahmen rahmen, float2[] punkte)
        {
            if (punkte.Length < 3) return punkte;

            // Die Reihenrichtung in Weltkoordinaten.
            var laengs = ZellenPunkt(rahmen.NachWelt(new Punkt(1, 0)))
                - ZellenPunkt(rahmen.NachWelt(new Punkt(0, 0)));
            var laenge = math.length(laengs);
            if (laenge < 1e-6f) return punkte;
            laengs /= laenge;

            var bester = 0;
            var bestesMass = float.NegativeInfinity;
            for (var i = 0; i < punkte.Length; i++)
            {
                var kante = punkte[(i + 1) % punkte.Length] - punkte[i];
                var kantenlaenge = math.length(kante);
                if (kantenlaenge < 1e-6f) continue;
                // Nicht der Betrag: die Kante soll IN die Reihenrichtung
                // zeigen, nicht nur parallel dazu liegen. Sonst kippt das
                // Muster je nach Umlaufsinn um 180 Grad.
                var mass = math.dot(kante / kantenlaenge, laengs);
                if (mass <= bestesMass) continue;
                bestesMass = mass;
                bester = i;
            }
            if (bester == 0) return punkte;

            var gedreht = new float2[punkte.Length];
            for (var i = 0; i < punkte.Length; i++)
                gedreht[i] = punkte[(bester + i) % punkte.Length];
            return gedreht;
        }

        /**
         * SCHNEIDET EINEN FAHRWEG AN DER BAULANDKANTE AB.
         *
         * Ein Fahrweg spannt sich ueber die ganze Reihe; das Stueck neben
         * dem Bauland ist voellig in Ordnung, nur das Stueck DARIN nicht.
         * Deshalb wird hier nicht verworfen, sondern zerlegt: was
         * ausserhalb liegt, bleibt.
         *
         * Das ist kein nachtraegliches Stanzen im Sinne der Zellen - eine
         * Linie hat keinen Schwerpunkt, der sich irren koennte. Sie wird an
         * zwei Parametern geteilt, und die beiden Reste sind exakt.
         *
         * Gerechnet wird im Rahmen der Baulandflaeche: die Strecke wird in
         * "laengs" und "quer" gelegt, dann bleiben zwei Intervallschnitte.
         */
        /**
         * `aufschlag` vergroessert das Kapprechteck nach aussen.
         *
         * Mit 0 wird am Parzellenrand gekappt - das ist richtig fuer die
         * MATERIALZELLEN. Die NETZKURSE bekommen dagegen die halbe
         * Strassenbreite mitgegeben und enden damit genau auf der Achse der
         * Zoning-Strasse. Nur so entsteht ein Knoten; 4 m daneben ist keine
         * Kreuzung, sondern zwei Segmente, die sich verfehlen.
         */
        private static void ZoningSchneidetWeg(
            Punkt a, Punkt b, IReadOnlyList<Zoningvorgabe> bauland,
            List<(Punkt A, Punkt B)> ausgabe, double aufschlag = 0.0)
        {
            var stuecke = new List<(double Von, double Bis)> { (0.0, 1.0) };
            foreach (var flaeche in bauland)
            {
                var bogen = flaeche.Winkel * Math.PI / 180.0;
                var laengs = new Punkt(Math.Cos(bogen), Math.Sin(bogen));
                var quer = new Punkt(-laengs.Y, laengs.X);
                var d = b - a;
                var ursprung = flaeche.Ecke
                    - laengs * aufschlag - quer * aufschlag;
                var start = a - ursprung;

                // Zwei Streifen, je einer in laengs und quer. Der Schnitt
                // beider ist das Rechteck.
                var innen = new Bereich(0, 1);
                foreach (var (achse, laenge) in new[]
                         {
                             (laengs, flaeche.Spalten * 8.0 + 2 * aufschlag),
                             (quer, flaeche.Reihen * 8.0 + 2 * aufschlag),
                         })
                {
                    var richtung = d.X * achse.X + d.Y * achse.Y;
                    var versatz = start.X * achse.X + start.Y * achse.Y;
                    if (Math.Abs(richtung) < 1e-9)
                    {
                        // Parallel zum Streifen: entweder ganz drin oder
                        // ganz draussen.
                        if (versatz < 0 || versatz > laenge)
                        {
                            innen = Bereich.Leer;
                            break;
                        }
                        continue;
                    }
                    var t1 = (0 - versatz) / richtung;
                    var t2 = (laenge - versatz) / richtung;
                    innen = innen.Geschnitten(
                        new Bereich(Math.Min(t1, t2), Math.Max(t1, t2)));
                    if (innen.IstLeer) break;
                }
                if (innen.IstLeer) continue;

                var neu = new List<(double Von, double Bis)>();
                foreach (var stueck in stuecke)
                {
                    if (innen.Bis <= stueck.Von || innen.Von >= stueck.Bis)
                    {
                        neu.Add(stueck);
                        continue;
                    }
                    if (innen.Von > stueck.Von)
                        neu.Add((stueck.Von, Math.Min(innen.Von, stueck.Bis)));
                    if (innen.Bis < stueck.Bis)
                        neu.Add((Math.Max(innen.Bis, stueck.Von), stueck.Bis));
                }
                stuecke = neu;
                if (stuecke.Count == 0) return;
            }

            foreach (var stueck in stuecke)
            {
                if (stueck.Bis - stueck.Von < 1e-6) continue;
                ausgabe.Add((
                    a + (b - a) * stueck.Von,
                    a + (b - a) * stueck.Bis));
            }
        }

        private readonly struct Bereich
        {
            internal Bereich(double von, double bis) { Von = von; Bis = bis; }
            internal double Von { get; }
            internal double Bis { get; }
            internal bool IstLeer => Bis <= Von;
            internal static Bereich Leer => new Bereich(0, 0);
            internal Bereich Geschnitten(Bereich anderer) => new Bereich(
                Math.Max(Von, anderer.Von), Math.Min(Bis, anderer.Bis));
        }

        /**
         * DIE RANDZONING-STRASSE WIRD AN IHREN ANSCHLUESSEN GETEILT.
         *
         * CS2 verbindet zwei Kurse NUR ueber einen identischen Endpunkt, nie
         * ueber Beruehrung (siehe [[cs2-knoten-statt-localconnect]]). Eine
         * Querstrasse, die MITTEN auf die RZ-Strasse stoesst, erzeugt dort
         * also keinen Knoten - beide liegen uebereinander und sind trotzdem
         * getrennt.
         *
         * Genau so sah es beim Nutzer aus. Sein Befund am 2026-09-09: *"Sehr
         * auffaellig war, dass die RZ niemals ueber Querstrassen verbunden
         * wurde."* Nach dem ersten Teil der Reparatur - die RZ-Achse ist
         * Partner in der Querstrassen-Paarschleife - wurde die Querstrasse
         * zwar geplant (14 -> 15 Kurse), der Anschluss zaehlte aber weiter
         * nicht.
         *
         * Die Fahrgassen haben dasselbe Problem und loesen es laengst: `Fuege`
         * legt sie stueckweise an, ein Stueck je Abschnitt zwischen zwei
         * Kreuzungen. Die Zoning-Strasse ging an `Fuege` vorbei. Sie bekommt
         * die Teilung hier - beim Anlegen, nicht hinterher.
         */
        private static IEnumerable<(Punkt A, Punkt B)> RandzoningGeteilt(
            (Punkt A, Punkt B) achse, Ringlosplan ringlos)
        {
            var richtung = achse.B - achse.A;
            var laenge = Geometrie.Laenge(richtung);
            if (laenge < 1e-6 || ringlos == null)
            {
                yield return achse;
                yield break;
            }
            var einheit = richtung * (1 / laenge);

            var marken = new List<double> { 0.0, laenge };
            foreach (var q in ringlos.Querwege)
            foreach (var ende in new[] { q.Anfang, q.Ende })
            {
                var w = ende - achse.A;
                var laengs = Geometrie.Skalar(w, einheit);
                var lot = Math.Abs(Geometrie.Kreuz(einheit, w));
                // Nur Enden, die wirklich auf der Achse liegen. Ein halber
                // Zentimeter faengt die float-Rechnung des Bauwegs ab, ohne
                // eine danebenliegende Querstrasse mitzunehmen.
                if (lot > 0.005) continue;
                if (laengs <= 0.005 || laengs >= laenge - 0.005) continue;
                marken.Add(laengs);
            }
            marken.Sort();

            for (var i = 0; i + 1 < marken.Count; i++)
            {
                if (marken[i + 1] - marken[i] < 0.005) continue;
                yield return (achse.A + einheit * marken[i],
                    achse.A + einheit * marken[i + 1]);
            }
        }

        private static List<ZellenStrasse> ZellenStrassen(
            Bauergebnis bau, IReadOnlyList<Zoningvorgabe> bauland = null,
            IReadOnlyList<(Punkt A, Punkt B)> randzoningLokal = null,
            double fahrwegbreite = 7.0, double querwegbreite = 3.0)
        {
            var ausgabe = new List<ZellenStrasse>();
            var zoningstrassen = new List<ZellenStrasse>();
            var randstrassenteile = new List<(Punkt A, Punkt B)>();
            var randzoningAchsen = new List<(Punkt A, Punkt B)>();

            /*
             * DIE HOEHERWERTIGEN STRASSEN STEHEN VORHER FEST.
             *
             * `Fuege` muss die Endpunkte eines niedrigeren Weges bereits beim
             * Erzeugen an deren Fahrbahnrand setzen. Deshalb werden RZ- und
             * ZF-Achsen zuerst geplant; ein nachtraegliches Kuerzen fertiger
             * Wege waere genau die verbotene Reparaturbauweise.
             */
            /*
             * KANTE GEGEN KANTE, NICHT ABSTAND GEGEN GUERTEL.
             *
             * Die Randstrassenmittellinie kommt aus `Layoutplanung.Innenrand`
             * und behaelt die Nummerierung des Umrisses. Wo beide gleich viele
             * Kanten haben, ist die Zuordnung deshalb exakt: Kante i gehoert
             * zu Kante i. Der Abstandstest darunter bleibt als Rueckfallweg,
             * falls je ein Ring mit anderer Kantenzahl hier ankommt.
             *
             * Der Abstandstest allein hatte einen Fehler, den der Nutzer am
             * 2026-09-09 im Spiel gesehen hat: *"Bei einer L-Form ging eine
             * Seite beim Rand-Toggle nach innen."* Bei schmalem Arm liegt die
             * gegenueberliegende Armseite im 15-m-Guertel und lief nur
             * entgegengesetzt - was der Betrag des Kreuzprodukts nicht sieht.
             */
            /*
             * DIE RANDZONING-STRASSE KOMMT AUS DEM ABSCHNITT.
             *
             * Seit dem 2026-09-10 ist die naechstliegende Fahrgasse die
             * RZ-Strasse (PLAN-RZ-an-Fahrgassen.md). Der Abschnitt weiss, wo
             * sie liegt - der Perimeterring nicht. Wer sie hier noch einmal
             * aus dem Ring ableitete, baute sie an der alten Stelle, waehrend
             * das Bauland schon an der neuen lag: Strasse mitten im Bauland.
             *
             * Mit eingeschalteten Randstrassen bleibt der alte Weg; dort gibt
             * es eine echte Randstrasse, die den Abschnitt traegt.
             */
            var ausAbschnitten = !bau.Randstrassen
                && (bau.Randzoningabschnitte?.Count ?? 0) > 0;
            if (ausAbschnitten)
                foreach (var rz in bau.Randzoningabschnitte)
                foreach (var stueck in RandzoningGeteilt((rz.A, rz.B), bau.Ringlos))
                {
                    zoningstrassen.Add(new ZellenStrasse
                    {
                        Kind = "zoning", A = stueck.A, B = stueck.B,
                        Randzoning = true, RandzoningInnen = rz.Innen,
                    });
                    randzoningAchsen.Add((stueck.A, stueck.B));
                }

            var umriss = bau.Umrisslokal;
            var nachNummer = umriss != null
                && umriss.Count == bau.Randstrassenmittellinie.Count;
            for (var i = 0; i < bau.Randstrassenmittellinie.Count; i++)
            {
                var a = bau.Randstrassenmittellinie[i];
                var b = bau.Randstrassenmittellinie[
                    (i + 1) % bau.Randstrassenmittellinie.Count];
                if (ausAbschnitten || randzoningLokal == null
                    || randzoningLokal.Count == 0)
                {
                    // Die RZ-Strasse steht schon; der Ring traegt hier nur
                    // noch die gewoehnliche Randstrasse - und die gibt es
                    // ohne Randstrassen gar nicht.
                    if (bau.Randstrassen) randstrassenteile.Add((a, b));
                    continue;
                }
                var imRand = new List<(Punkt A, Punkt B)>();
                var ausserhalb = new List<(Punkt A, Punkt B)>();
                if (nachNummer)
                {
                    var gewaehlt = RandzoningIstKante(randzoningLokal,
                        umriss[i], umriss[(i + 1) % umriss.Count]);
                    (gewaehlt ? imRand : ausserhalb).Add((a, b));
                }
                else
                {
                    RandzoningTeileLokal(
                        randzoningLokal, a, b, imRand, ausserhalb);
                }
                if (bau.Randstrassen) randstrassenteile.AddRange(ausserhalb);
                foreach (var teil in imRand)
                foreach (var stueck in RandzoningGeteilt(teil, bau.Ringlos))
                {
                    zoningstrassen.Add(new ZellenStrasse
                    {
                        Kind = "zoning", A = stueck.A, B = stueck.B,
                        Randzoning = true,
                    });
                    randzoningAchsen.Add((stueck.A, stueck.B));
                }
            }
            if (bauland != null && bauland.Count != 0)
                ZoningStrassennetz(zoningstrassen, bauland, bau.Innenrand,
                    randzoningAchsen);

            /*
             * KEIN WEG LAEUFT DURCHS BAULAND - auch die Randstrasse nicht.
             *
             * Hier stand bis zum 2026-09-03, die Randstrasse duerfe
             * hindurch, weil sie den Parkplatz erschliesst. Der Nutzer hat
             * es im Spiel gesehen und widersprochen: *"Mir ist aufgefallen,
             * dass die Invisible Paths weiterhin durch die Zoningflaeche
             * fuehren."* Durch bebautes Land faehrt niemand.
             *
             * Nur die ZUFAHRTEN bleiben ungekappt: sie kommen von aussen,
             * enden am Ring und beruehren das Bauland gar nicht.
             */
            void Fuege(string kind, Punkt a, Punkt b, bool verbindung = false, double? breite = null)
            {
                var rzQueranschluss = bau.Ringlos != null && kind == "cross" && randzoningAchsen.Count > 0;
                if (rzQueranschluss)
                {
                    a = RandzoningQuerEnde(a, b, randzoningAchsen);
                    b = RandzoningQuerEnde(b, a, randzoningAchsen);
                }
                var reste = new List<(Punkt A, Punkt B)> { (a, b) };

                /*
                 * KEIN WEG LAEUFT IN DIE KACHELN.
                 *
                 * Regel 3 des Nutzers: an einer Randzoning-Kante entsteht kein
                 * Endfussweg, und die Gassen werden ausserhalb der
                 * Kachelflaeche verbunden. Statt jede Wegart einzeln zu
                 * behandeln, wird hier EINMAL geschnitten - was im Bauland
                 * liegt, entsteht gar nicht erst.
                 *
                 * Die Zoning-Strasse selbst ist ausgenommen: sie IST der
                 * Anschluss und liegt am Rand des Bandes.
                 */
                if (!string.Equals(kind, "zoning", StringComparison.Ordinal)
                    && (bau.Randzoningabschnitte?.Count ?? 0) > 0)
                    foreach (var rz in bau.Randzoningabschnitte)
                    {
                        var behalten = new List<(Punkt A, Punkt B)>();
                        foreach (var rest in reste)
                            rz.SchneideHeraus(rest.A, rest.B, behalten);
                        reste = behalten;
                    }

                if (bauland != null && bauland.Count != 0)
                {
                    var ausserhalb = new List<(Punkt A, Punkt B)>();
                    foreach (var rest in reste)
                        ZoningSchneidetWeg(rest.A, rest.B, bauland,
                            ausserhalb, 0.0);
                    reste = ausserhalb;
                }

                /*
                 * ACHSE AUF ACHSE WAR DER FEHLER.
                 *
                 * Bis 2026-09-04 endete der niedrigere Weg auf der Achse der
                 * 8-m-Zoningstrasse. Der Ingame-Scan mass dadurch 13 Paare,
                 * mehrfach mit 0,00 m Achsabstand. Zwei Fahrbahnen beruehren
                 * einander erst bei der halben SUMME ihrer Breiten. Dieser
                 * Abstand wird hier vor dem Anlegen der Segmente exakt gegen
                 * die bereits geplanten Zoningachsen geschnitten.
                 */
                var eigeneBreite = breite ?? (string.Equals(kind, "cross",
                        StringComparison.Ordinal)
                    ? querwegbreite : fahrwegbreite);
                var achsabstand = ZoningStrassenbreite * 0.5
                    + eigeneBreite * 0.5;
                foreach (var zoning in zoningstrassen)
                {
                    if (bau.Ringlos != null && !zoning.Randzoning
                        && GeplanterZoningknoten(kind, a, b, zoning)) continue;
                    // Ein geplanter T-Knoten gehoert beiden Kursen. Der
                    // Abstandsschutz fuer getrennte Objekte kappte ihn im
                    // Fall 22:22 von 8,411699 auf 2,911774 m: exakt 5,50 m
                    // Luecke (4 + 1,5). Nur dieser gemeinsame RZ-Knoten ist
                    // ausgenommen; parallele Wege und fremde Strassen nicht.
                    if (rzQueranschluss
                        && randzoningAchsen.Any(r => r.A.Equals(zoning.A) && r.B.Equals(zoning.B))
                        && RandzoningGemeinsamerKnoten(a, b, zoning.A, zoning.B)) continue;

                    /*
                     * WER AUF DERSELBEN GERADEN LIEGT, STOESST AN.
                     *
                     * Seit die RZ-Strasse eine Fahrgasse IST, teilen sich
                     * beide eine Linie: ein Stueck davon ist Strasse, der
                     * Rest bleibt Gasse. Die Kapsel des Abstandsschutzes
                     * riss dazwischen 7,5 m auf, und ohne identischen
                     * Endpunkt verbindet CS2 innen nicht.
                     */
                    var achse = (zoning.A, zoning.B);
                    var laengsRichtung = zoning.B - zoning.A;
                    var laengsLaenge = Geometrie.Laenge(laengsRichtung);
                    if (laengsLaenge < 1e-9)
                    {
                        reste = ZoningOhneStrassenband(reste, achse, achsabstand);
                        continue;
                    }
                    var laengsEinheit = laengsRichtung * (1.0 / laengsLaenge);
                    var aufDerLinie = new List<(Punkt A, Punkt B)>();
                    var daneben = new List<(Punkt A, Punkt B)>();
                    foreach (var rest in reste)
                        (ZoningAufDerselbenGeraden(zoning.A, laengsEinheit, rest)
                            ? aufDerLinie : daneben).Add(rest);
                    reste = ZoningOhneStrassenband(daneben, achse, achsabstand);
                    reste.AddRange(ZoningLaengsGestossen(aufDerLinie, achse));
                }

                foreach (var rest in reste)
                {
                    ausgabe.Add(new ZellenStrasse
                    {
                        Kind = kind, A = rest.A, B = rest.B,
                        Teilflaechenverbindung = verbindung,
                    });
                }
            }
            foreach (var teil in randstrassenteile)
                Fuege("perimeter", teil.A, teil.B);
            if (bau.Ringlos != null)
            {
                foreach (var g in bau.Ringlos.Gassen) Fuege("aisle", g.A, g.B);
                foreach (var q in bau.Ringlos.Querwege) Fuege("cross", q.Anfang, q.Ende);
                foreach (var w in bau.Ringlos.Fusswege.Concat(bau.Ringlos.Zufahrten))
                {
                    // Ein Fusswegrest traegt nur Belag, kein Wegenetz.
                    if (w.OhneNetz) continue;
                    var vorher = ausgabe.Count;
                    Fuege("entrance", w.A, w.B, false, w.Breite);
                    for (var k = vorher; k < ausgabe.Count; k++)
                    {
                        ausgabe[k].Art = w.Fuss ? Zufahrtsart.Fussweg : w.Art;
                        // `Ringlos.Zufahrten` stammt aus einer Vorgabe des
                        // Nutzers, `Ringlos.Fusswege` legt der Plan selbst an.
                        ausgabe[k].Gesetzt = w.Zufahrt;
                    }
                }
                // Nur Netz: die Anschluesse geschnittener Fusswege an ihre
                // Gasse (RinglosZufahrtsgassen). Kein Belag, siehe dort.
                foreach (var w in bau.Ringlos.Fussanschluesse)
                {
                    var vorher = ausgabe.Count;
                    Fuege("entrance", w.A, w.B, false, w.Breite);
                    for (var k = vorher; k < ausgabe.Count; k++)
                    {
                        ausgabe[k].Art = Zufahrtsart.Fussweg;
                        ausgabe[k].Gesetzt = false;
                    }
                }
            }
            else if (bau.InnereStrassen != null && bau.InnereStrassen.Count != 0)
            {
                foreach (var strasse in bau.InnereStrassen)
                    Fuege(
                        strasse.Art == Zellart.Fahrgasse ? "aisle" : "cross",
                        strasse.Anfang,
                        strasse.Ende,
                        strasse.Teilflaechenverbindung);
            }
            else
            {
                foreach (var band in bau.Bandplan.Baender.Where(
                             band => band.Art == Zellart.Fahrgasse))
                {
                    var y = (band.Anfang + band.Ende) / 2;
                    foreach (var gasse in ZellenWaagerecht(
                                 bau.Randstrassenmittellinie, y, "aisle"))
                        Fuege("aisle", gasse.A, gasse.B);
                }
                foreach (var stueck in bau.Querstrassenstuecke)
                    Fuege("cross", stueck.Anfang, stueck.Ende);
            }
            foreach (var zufahrt in bau.Zufahrtsbericht.Zufahrten)
            {
                var vorher = ausgabe.Count;
                Fuege("entrance", zufahrt.Start,
                    ZellenStrahlTrifftRing(zufahrt.Start,
                        zufahrt.Innennormale, bau.Randstrassenmittellinie));
                for (var i = vorher; i < ausgabe.Count; i++)
                {
                    ausgabe[i].Art = zufahrt.Vorgabe.Art;
                    ausgabe[i].Gesetzt = true;
                }
            }
            ausgabe.AddRange(bau.Ringlos == null ? zoningstrassen
                : ZoningAnAnschluessenGeteilt(zoningstrassen, ausgabe).ToList());
            return ausgabe;
        }

        /** Zwei Punkte gelten als derselbe, wenn sie unter 1 mm auseinander liegen. */
        private static bool ZoningNahBei(Punkt a, Punkt b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return dx * dx + dy * dy < 1e-6;
        }

        /**
         * Liegt der Punkt auf dem Rand des Achsenrechtecks dieser Flaeche?
         *
         * Gerechnet wird im Rahmen der Flaeche: zwei Skalarprodukte statt
         * einer Kantenschleife, damit der Umlaufsinn keine Rolle spielt.
         */
        private static bool ZoningAufStrassenachse(Punkt p, Zoningvorgabe f)
        {
            const double toleranz = 1e-3;
            var bogen = f.Winkel * Math.PI / 180.0;
            var laengs = new Punkt(Math.Cos(bogen), Math.Sin(bogen));
            var quer = new Punkt(-laengs.Y, laengs.X);
            var h = Zoningvorgabe.Strassenhalbbreite;
            var ursprung = f.Ecke - laengs * h - quer * h;
            var d = p - ursprung;
            var u = d.X * laengs.X + d.Y * laengs.Y;
            var v = d.X * quer.X + d.Y * quer.Y;
            var breite = f.Spalten * 8.0 + 2 * h;
            var tiefe = f.Reihen * 8.0 + 2 * h;
            if (u < -toleranz || u > breite + toleranz) return false;
            if (v < -toleranz || v > tiefe + toleranz) return false;
            return Math.Abs(u) < toleranz || Math.Abs(u - breite) < toleranz
                || Math.Abs(v) < toleranz || Math.Abs(v - tiefe) < toleranz;
        }

        /**
         * Die Teile der Strecke, die INNERHALB des Rings liegen.
         *
         * Allgemein gehalten, weil der Zoning-Ring gedreht sein kann -
         * `ZellenWaagerecht` und `ZellenSenkrecht` helfen dort nicht.
         */
        private static IEnumerable<(Punkt A, Punkt B)> ZoningInnenstuecke(
            IReadOnlyList<Punkt> ring, Punkt a, Punkt b)
        {
            if (ring == null || ring.Count < 3)
            {
                yield return (a, b);
                yield break;
            }

            var d = b - a;
            var teiler = new List<double> { 0.0, 1.0 };
            for (var i = 0; i < ring.Count; i++)
            {
                var p = ring[i];
                var q = ring[(i + 1) % ring.Count];
                var e = q - p;
                var nenner = d.X * e.Y - d.Y * e.X;
                if (Math.Abs(nenner) < 1e-12) continue;
                var diff = p - a;
                var t = (diff.X * e.Y - diff.Y * e.X) / nenner;
                var u = (diff.X * d.Y - diff.Y * d.X) / nenner;
                if (t <= 1e-9 || t >= 1 - 1e-9) continue;
                if (u < -1e-9 || u > 1 + 1e-9) continue;
                teiler.Add(t);
            }
            teiler.Sort();

            for (var i = 0; i + 1 < teiler.Count; i++)
            {
                var von = teiler[i];
                var bis = teiler[i + 1];
                if (bis - von < 1e-6) continue;
                var mitte = a + d * ((von + bis) / 2);
                if (!Geometrie.Enthaelt(ring, mitte)) continue;
                yield return (a + d * von, a + d * bis);
            }
        }

        /**
         * WO DER ZUFAHRTSSTRAHL DIE RINGACHSE TRIFFT.
         *
         * ECKEN SIND DER SCHWERE FALL. Schnappt der Nutzer eine Zufahrt an
         * eine Ecke der Randstrasse, landet der Schnittpunkt GENAU auf einem
         * Ringpunkt. Durch Rundung liegt der Kantenparameter dann auf BEIDEN
         * angrenzenden Kanten knapp ausserhalb von [0,1] - keine nimmt ihn,
         * und die alte Fassung warf:
         *
         *     The entrance axis does not meet the perimeter road centreline.
         *
         * Der Nutzer konnte deshalb am 2026-08-27 nicht bauen (Debug-Abzug
         * 16:06:07). Eine Toleranz von einem Millionstel des Kantenanteils
         * faengt das ab; danach wird auf die Kante geklemmt, damit der Punkt
         * garantiert AUF ihr liegt - spaeter wird dieselbe Kante an dieser
         * Stelle geteilt, und beide Seiten brauchen denselben Rechenwert.
         */
        private const double RingtrefferToleranz = 1e-6;

        private static Punkt ZellenStrahlTrifftRing(
            Punkt start,
            Punkt richtung,
            IReadOnlyList<Punkt> ring)
        {
            var besterParameter = double.PositiveInfinity;
            var besterPunkt = new Punkt();
            var gefunden = false;
            for (var i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var kante = ring[(i + 1) % ring.Count] - a;
                var nenner = Geometrie.Kreuz(richtung, kante);
                if (nenner == 0) continue;
                var delta = a - start;
                var strahlparameter = Geometrie.Kreuz(delta, kante) / nenner;
                var kantenparameter = Geometrie.Kreuz(delta, richtung) / nenner;
                if (strahlparameter < -RingtrefferToleranz
                    || kantenparameter < -RingtrefferToleranz
                    || kantenparameter > 1 + RingtrefferToleranz
                    || strahlparameter >= besterParameter) continue;
                besterParameter = strahlparameter;
                // Auf die Kante klemmen: der Punkt muss AUF ihr liegen, auch
                // wenn die Toleranz ihn um ein Millionstel danebenliess.
                var geklemmt = kantenparameter < 0 ? 0
                    : kantenparameter > 1 ? 1 : kantenparameter;
                // Von der Ringkante ableiten: derselbe Rechenwert wird spaeter
                // beim Teilen dieser Kante als gemeinsamer Netzknoten benutzt.
                besterPunkt = a + kante * geklemmt;
                gefunden = true;
            }
            if (gefunden) return besterPunkt;

            /*
             * IMMER NOCH KEIN TREFFER - DANN DEN NAECHSTEN RINGPUNKT NEHMEN,
             * ABER LAUT.
             *
             * Ein Bau darf daran nicht scheitern; der Nutzer sieht sonst nur
             * "Vorschau konnte nicht berechnet werden" ohne jeden Anhalt.
             * Gleichzeitig ist das ein BEFUND: wenn diese Stelle regelmaessig
             * anspringt, stimmt die Konstruktion davor nicht, und die Meldung
             * nennt den gemessenen Abstand, damit man das entscheiden kann.
             */
            var naechsterAbstand = double.PositiveInfinity;
            var ersatz = ring.Count > 0 ? ring[0] : start;
            for (var i = 0; i < ring.Count; i++)
            {
                var abstand = Geometrie.Laenge(ring[i] - start);
                if (abstand >= naechsterAbstand) continue;
                naechsterAbstand = abstand;
                ersatz = ring[i];
            }
            /*
             * Gezaehlt statt geloggt: die Geometrie teilt sich diese Datei
             * mit dem Testprojekt, und dort gibt es kein `Mod`. Denselben Weg
             * gehen die Kappenzaehler daneben. Der Mod meldet den Zuwachs
             * nach dem Bau, der Qualitaetslauf druckt ihn mit.
             */
            RingtrefferErsatz++;
            RingtrefferAbstand = naechsterAbstand;
            return ersatz;
        }

        private static IEnumerable<ZellenStrasse> ZellenWaagerecht(
            IReadOnlyList<Punkt> ring,
            double y,
            string kind)
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
            if (schnitte.Count % 2 != 0)
                throw new InvalidOperationException(
                    "The cell engine found an odd number of horizontal boundary crossings.");
            for (var i = 0; i < schnitte.Count; i += 2)
            {
                // Das Materialband endet an der inneren Fahrbahnkante. Fuer
                // das Netz wird dieselbe Achse um Ai/2 bis zu den hier
                // berechneten Schnittpunkten der Ringmittellinie verlaengert.
                var anfang = schnitte[i];
                var ende = schnitte[i + 1];
                if (ende <= anfang) continue;
                yield return new ZellenStrasse
                {
                    Kind = kind,
                    A = new Punkt(anfang, y),
                    B = new Punkt(ende, y),
                };
            }
        }

        private static IEnumerable<ZellenStrasse> ZellenSenkrecht(
            IReadOnlyList<Punkt> ring,
            double x,
            string kind)
        {
            var schnitte = new List<double>();
            for (var i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];
                if ((a.X > x) == (b.X > x)) continue;
                schnitte.Add(a.Y + (x - a.X) * (b.Y - a.Y) / (b.X - a.X));
            }
            schnitte.Sort();
            if (schnitte.Count % 2 != 0)
                throw new InvalidOperationException(
                    "The cell engine found an odd number of vertical boundary crossings.");
            for (var i = 0; i < schnitte.Count; i += 2)
            {
                var anfang = schnitte[i];
                var ende = schnitte[i + 1];
                if (ende <= anfang) continue;
                yield return new ZellenStrasse
                {
                    Kind = kind,
                    A = new Punkt(x, anfang),
                    B = new Punkt(x, ende),
                };
            }
        }

        private static List<ZellenStrasse> ZellenNetzfassung(
            IReadOnlyList<ZellenStrasse> strassen)
        {
            var schnitte = strassen.Select(strasse => new List<ZellenSchnitt>
            {
                new ZellenSchnitt { T = 0, Punkt = strasse.A },
                new ZellenSchnitt { T = 1, Punkt = strasse.B },
            }).ToArray();

            /*
             * ZONING GEGEN ZONING WIRD AN DER T-EINMUENDUNG GETEILT.
             *
             * CS2 verschmilzt gleichzeitig erzeugte Kurse an identischen
             * Endpunkten. Ein Ende mitten auf einer anderen Kante erzeugt
             * dagegen keinen Knoten: im Bau PLT-6E2B88E8 blieb die ZF-Strasse
             * deshalb als L an nur einem RZ-Stueck haengen; Strom, Wasser und
             * Abwasser wechselten nicht auf den anderen Strassenzug.
             *
             * Der Schnitt wird hier VOR dem Bau aus den bekannten Linien
             * konstruiert. Damit entstehen zwei RZ-Kurse und der ZF-Kurs von
             * Anfang an mit genau demselben Knoten. `CourseSplitSystem` muss
             * nichts nachtraeglich finden oder reparieren.
             *
             * Der Preis ist real: CS2 gibt jeder fertigen Kante einen eigenen
             * Zonenblock. Darum wird nur Zoning gegen Zoning geteilt. Bei Weg
             * gegen Zoning bleibt der gemessene LocalConnect-Weg bestehen;
             * dort wuerde ein Split Kacheln kosten, ohne die Versorgung zu
             * verbessern.
             */
            bool IstZoning(ZellenStrasse strasse) =>
                string.Equals(strasse.Kind, "zoning", StringComparison.Ordinal);

            for (var i = 0; i < strassen.Count; i++)
                for (var j = i + 1; j < strassen.Count; j++)
                {
                    var erstesZoning = IstZoning(strassen[i]);
                    var zweitesZoning = IstZoning(strassen[j]);
                    if (erstesZoning != zweitesZoning)
                        continue;
                    ZellenSchneideStrassen(
                        strassen[i], schnitte[i], strassen[j], schnitte[j]);
                }

            var ausgabe = new List<ZellenStrasse>();
            for (var i = 0; i < strassen.Count; i++)
            {
                var geordnet = schnitte[i]
                    .OrderBy(schnitt => schnitt.T)
                    .GroupBy(schnitt => schnitt.T)
                    .Select(gruppe => gruppe.First())
                    .ToArray();
                for (var j = 0; j + 1 < geordnet.Length; j++)
                {
                    if (ZellenGleich(geordnet[j].Punkt, geordnet[j + 1].Punkt))
                        continue;
                    ZellenFuegeNetzstueckHinzu(ausgabe, new ZellenStrasse
                    {
                        Kind = strassen[i].Kind,
                        Art = strassen[i].Art,
                        // Die Herkunft reist mit. Ein Teilstueck einer
                        // RZ-Strasse ist immer noch eine RZ-Strasse - ohne
                        // das faende die Seitenwahl sie nach dem Teilen
                        // nicht mehr wieder.
                        Randzoning = strassen[i].Randzoning,
                        RandzoningInnen = strassen[i].RandzoningInnen,
                        A = geordnet[j].Punkt,
                        B = geordnet[j + 1].Punkt,
                    });
                }
            }
            return ZellenVerschmelzeNaheKnoten(ausgabe);
        }

        /**
         * KNOTEN, DIE ZU DICHT BEIEINANDER LIEGEN, ZU EINEM MACHEN.
         *
         * Nutzerbefund vom 2026-08-21 an der Spurenansicht: an der Einfahrt
         * ging es nur in eine Richtung. Der Abzug zeigte warum - die
         * Randstrasse bestand aus 18,5 / 29,8 / ... Meter langen Stuecken UND
         * einem von 0,039 m. CS2 verwirft alles unter 0,375 m; genau an dieser
         * Stelle war der Ring danach durchtrennt.
         *
         * Die Ursache liegt im Teilen: die Zufahrt trifft den Ring 3,9 cm
         * neben einer Ecke, die das Zellenraster dort ohnehin schon hatte.
         * Beide Knoten sind gewollt, nur eben zu dicht.
         *
         * Also werden sie zu einem verschmolzen - und zwar auf den WICHTIGEREN:
         * ein Punkt, an dem sich mehrere Wegarten treffen, ist eine echte
         * Kreuzung und darf sich nicht bewegen; eine blosse Ringecke schon.
         * Weil alle Stuecke ueber dieselbe Zuordnung umgeschrieben werden,
         * bleiben die exakt gemeinsamen Endpunkte erhalten - daran haengt in
         * CS2 die ganze Verbindung.
         */
        private static List<ZellenStrasse> ZellenVerschmelzeNaheKnoten(
            List<ZellenStrasse> stuecke)
        {
            // Dasselbe Mass, mit dem CS2 selbst aussortiert.
            const double Mindestlaenge = 0.375;

            var wichtigkeit = new Dictionary<Punkt, HashSet<string>>();
            void Merke(Punkt punkt, string art)
            {
                if (!wichtigkeit.TryGetValue(punkt, out var arten))
                    wichtigkeit[punkt] = arten = new HashSet<string>();
                arten.Add(art);
            }
            foreach (var stueck in stuecke)
            {
                Merke(stueck.A, stueck.Kind);
                Merke(stueck.B, stueck.Kind);
            }

            // Wichtigste zuerst: sie werden Vertreter, die anderen wandern.
            var geordnet = wichtigkeit.Keys
                .OrderByDescending(punkt => wichtigkeit[punkt].Count)
                .ToList();
            var vertreter = new List<Punkt>();
            var zuordnung = new Dictionary<Punkt, Punkt>();
            foreach (var punkt in geordnet)
            {
                var ziel = punkt;
                foreach (var kandidat in vertreter)
                {
                    if (Geometrie.Laenge(punkt - kandidat) >= Mindestlaenge) continue;
                    ziel = kandidat;
                    break;
                }
                if (ziel.Equals(punkt)) vertreter.Add(punkt);
                zuordnung[punkt] = ziel;
            }

            var raus = new List<ZellenStrasse>();
            foreach (var stueck in stuecke)
            {
                var a = zuordnung[stueck.A];
                var b = zuordnung[stueck.B];
                // Ein Stueck, dessen beide Enden auf denselben Knoten fallen,
                // WAR der Splitter. Es faellt weg statt zu verschwinden.
                if (Geometrie.Laenge(b - a) < 1e-9) continue;
                ZellenFuegeNetzstueckHinzu(raus, new ZellenStrasse
                {
                    Kind = stueck.Kind,
                    Art = stueck.Art,
                    Randzoning = stueck.Randzoning,
                    RandzoningInnen = stueck.RandzoningInnen,
                    A = a,
                    B = b,
                });
            }
            return raus;
        }

        private static void ZellenSchneideStrassen(
            ZellenStrasse erste,
            List<ZellenSchnitt> ersteSchnitte,
            ZellenStrasse zweite,
            List<ZellenSchnitt> zweiteSchnitte)
        {
            var r = erste.B - erste.A;
            var s = zweite.B - zweite.A;
            var delta = zweite.A - erste.A;
            var nenner = Geometrie.Kreuz(r, s);
            if (nenner != 0)
            {
                var t = Geometrie.Kreuz(delta, s) / nenner;
                var u = Geometrie.Kreuz(delta, r) / nenner;
                if (t < -FitEps || t > 1 + FitEps
                    || u < -FitEps || u > 1 + FitEps) return;
                t = ZellenKlemmeStreckenparameter(t);
                u = ZellenKlemmeStreckenparameter(u);
                var punkt = t == 0
                    ? erste.A
                    : t == 1
                        ? erste.B
                        : u == 0
                            ? zweite.A
                            : u == 1
                                ? zweite.B
                                : erste.A + r * t;
                ZellenFuegeSchnittHinzu(ersteSchnitte, t, punkt);
                ZellenFuegeSchnittHinzu(zweiteSchnitte, u, punkt);
                return;
            }
            if (Geometrie.Kreuz(delta, r) != 0) return;

            ZellenFuegeKollinearenEndpunktHinzu(
                erste.A, erste, ersteSchnitte, zweite, zweiteSchnitte);
            ZellenFuegeKollinearenEndpunktHinzu(
                erste.B, erste, ersteSchnitte, zweite, zweiteSchnitte);
            ZellenFuegeKollinearenEndpunktHinzu(
                zweite.A, zweite, zweiteSchnitte, erste, ersteSchnitte);
            ZellenFuegeKollinearenEndpunktHinzu(
                zweite.B, zweite, zweiteSchnitte, erste, ersteSchnitte);
        }

        private static void ZellenFuegeKollinearenEndpunktHinzu(
            Punkt punkt,
            ZellenStrasse eigene,
            List<ZellenSchnitt> eigeneSchnitte,
            ZellenStrasse andere,
            List<ZellenSchnitt> andereSchnitte)
        {
            var richtung = andere.B - andere.A;
            var quadrat = Geometrie.Skalar(richtung, richtung);
            if (quadrat == 0) return;
            var u = Geometrie.Skalar(punkt - andere.A, richtung) / quadrat;
            if (u < -FitEps || u > 1 + FitEps) return;
            u = ZellenKlemmeStreckenparameter(u);
            var eigeneRichtung = eigene.B - eigene.A;
            var eigenesQuadrat = Geometrie.Skalar(eigeneRichtung, eigeneRichtung);
            var t = eigenesQuadrat == 0
                ? 0
                : Geometrie.Skalar(punkt - eigene.A, eigeneRichtung)
                    / eigenesQuadrat;
            t = ZellenKlemmeStreckenparameter(t);
            ZellenFuegeSchnittHinzu(eigeneSchnitte, t, punkt);
            ZellenFuegeSchnittHinzu(andereSchnitte, u, punkt);
        }

        private static double ZellenKlemmeStreckenparameter(double parameter)
        {
            // Ein rechnerisch gemeinsamer Endpunkt darf nicht als zweiter,
            // fast gleicher Knoten in NetLine landen. Bei Rechteck waren vor
            // dieser Klemme 1/6 Gassen- und 3/4 Querstrassenenden unverbunden,
            // obwohl ihr Abstand zur Ringlinie jeweils 0 m betrug.
            if (Math.Abs(parameter) <= FitEps) return 0;
            if (Math.Abs(parameter - 1) <= FitEps) return 1;
            return parameter;
        }

        private static void ZellenFuegeSchnittHinzu(
            List<ZellenSchnitt> schnitte,
            double t,
            Punkt punkt)
        {
            if (schnitte.Any(schnitt => schnitt.T == t)) return;
            schnitte.Add(new ZellenSchnitt { T = t, Punkt = punkt });
        }

        private static void ZellenFuegeNetzstueckHinzu(
            List<ZellenStrasse> ausgabe,
            ZellenStrasse kandidat)
        {
            var vorhanden = ausgabe.FindIndex(strasse =>
                ZellenGleich(strasse.A, kandidat.A)
                    && ZellenGleich(strasse.B, kandidat.B)
                || ZellenGleich(strasse.A, kandidat.B)
                    && ZellenGleich(strasse.B, kandidat.A));
            if (vorhanden < 0)
            {
                ausgabe.Add(kandidat);
                return;
            }
            if (ZellenStrassenrang(kandidat.Kind)
                > ZellenStrassenrang(ausgabe[vorhanden].Kind))
                ausgabe[vorhanden] = kandidat;
        }

        private static int ZellenStrassenrang(string kind)
        {
            if (kind == "perimeter") return 4;
            if (kind == "aisle") return 3;
            if (kind == "cross") return 2;
            return 1;
        }

        private static bool ZellenGleich(Punkt a, Punkt b) =>
            a.X == b.X && a.Y == b.Y;

        private static float2 ZellenPunkt(Punkt punkt) =>
            new float2((float)punkt.X, (float)punkt.Y);

        private static float2[] ZellenLinie(Rahmen rahmen, ZellenStrasse strasse) =>
            new[]
            {
                ZellenPunkt(rahmen.NachWelt(strasse.A)),
                ZellenPunkt(rahmen.NachWelt(strasse.B)),
            };
    }
}
