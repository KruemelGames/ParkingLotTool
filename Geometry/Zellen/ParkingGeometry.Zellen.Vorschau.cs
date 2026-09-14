using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry.Zellen;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static partial class ParkingGeometry
    {
        /**
         * Nur rechnerisch identische Grenzen werden zusammengelegt. Die
         * Zellen teilen ihre Knoten; das Epsilon faengt daher nur Rundungsrest
         * in der Flaechenbilanz ab und ueberbrueckt keine sichtbare Fuge.
         */
        private const double ZellenVorschauFlaechenEpsilon = 1e-7;
        private const double ZellenVorschauKantenEpsilon = 1e-8;
        private const double ZellenVorschauStreifenFlaechenEpsilon = 0.01;
        private const double ZellenVorschauStreifenFehleranteil = 0.02;

        private sealed class ZellenVorschauRechteck
        {
            internal Zellart Art { get; set; }
            internal double MinX { get; set; }
            internal double MaxX { get; set; }
            internal double MinY { get; set; }
            internal double MaxY { get; set; }
        }

        /**
         * Macht aus den Zellen die Rechtecke fuer Overlay und Ladesaeulen.
         * Zuerst gewinnt jede vollstaendig rechteckige Zellkomponente, danach
         * werden rechteckige Einzelzellen in beiden Achsen maximal vereinigt.
         * Bei Strassen bleibt die Zellart die Verschmelzungsgrenze. Fuer das
         * eine gemeinsame Feld Green werden die historischen Gruenarten
         * dagegen bewusst wie eine einzige Ausgabeart behandelt.
         *
         * Der schraege Pflichtfall widerlegte am 2026-08-21 die Annahme, dass
         * jede Zelle im Reihenrahmen rechteckig sei: 178/361 Fahrgassen- und
         * 116/265 Randstrassenzellen waren Dreiecke oder Trapeze. Fuer diese
         * Randfragmente wird derselbe Streifenfueller wie im Overlay auf eine
         * lochfreie, artgleiche Zellkomponente angewandt. Seine Ausgabe wird
         * hier in echte Rechtecke umgewandelt; der komplette Materialring mit
         * gemessenen 154 Streifen und bis zu 21 % Flaechenfehler entsteht
         * dadurch nicht.
         */
        private static float2[][] ZellenVorschauQuads(
            Bauergebnis bau,
            Func<Zelle, bool> auswahl,
            bool gegenUhrzeigersinn,
            bool zellartenTrennen = true)
        {
            var zellen = bau.Zellen.Where(auswahl).ToArray();
            if (zellen.Length == 0) return Array.Empty<float2[]>();

            var erledigt = new bool[zellen.Length];
            var rechtecke = new List<ZellenVorschauRechteck>();
            var streifenrechtecke = new List<float2[]>();
            foreach (var komponente in ZellenVorschauKomponenten(
                         zellen))
            {
                ZellenVorschauRechteck rechteck;
                if (ZellenVorschauIstRechteck(
                        zellen, komponente, out rechteck))
                {
                    rechtecke.Add(rechteck);
                    foreach (var index in komponente) erledigt[index] = true;
                }
                else
                {
                    float2[][] komponentenstreifen;
                    if (!ZellenVorschauKomponentenstreifen(
                            bau.Rahmen, zellen, komponente,
                            gegenUhrzeigersinn, out komponentenstreifen))
                        continue;
                    streifenrechtecke.AddRange(komponentenstreifen);
                    foreach (var index in komponente) erledigt[index] = true;
                }
            }

            for (var i = 0; i < zellen.Length; i++)
            {
                if (erledigt[i]) continue;
                ZellenVorschauRechteck rechteck;
                if (!ZellenVorschauIstRechteck(
                        zellen, new[] { i }, out rechteck)) continue;
                rechtecke.Add(rechteck);
                erledigt[i] = true;
            }

            if (zellartenTrennen)
                rechtecke = ZellenVorschauVerschmelze(rechtecke);
            else
            {
                var gemeinsam = rechtecke.Select(ZellenVorschauKopiere).ToList();
                foreach (var rechteck in gemeinsam)
                    rechteck.Art = default(Zellart);
                gemeinsam = ZellenVorschauVerschmelze(gemeinsam);

                var nachArt = ZellenVorschauVerschmelze(rechtecke);
                foreach (var rechteck in nachArt)
                    rechteck.Art = default(Zellart);
                nachArt = ZellenVorschauVerschmelzeNachbarn(nachArt);
                rechtecke = gemeinsam.Count <= nachArt.Count
                    ? gemeinsam : nachArt;
            }
            var ausgabe = rechtecke
                .OrderBy(rechteck => rechteck.Art)
                .ThenBy(rechteck => rechteck.MinY)
                .ThenBy(rechteck => rechteck.MinX)
                .Select(rechteck => ZellenVorschauWeltrechteck(
                    bau.Rahmen, rechteck, gegenUhrzeigersinn))
                .ToList();
            ausgabe.AddRange(streifenrechtecke);

            for (var i = 0; i < zellen.Length; i++)
            {
                if (erledigt[i]) continue;
                ausgabe.AddRange(ZellenVorschauStreifenrechtecke(
                    bau.Rahmen, zellen[i], gegenUhrzeigersinn));
            }
            return ausgabe.Where(ZellenVorschauTraegtFlaeche).ToArray();
        }

        /**
         * EIN RECHTECK OHNE BREITE IST KEIN RECHTECK.
         *
         * Befund des Nutzers am 2026-09-08: *"In der Preview von Randstrasse
         * aus sind die Grafiken der Einfahrten komplett anders farblich und
         * formmaessig."*
         *
         * Gemessen an seinem Fall, beide Betriebsarten:
         *
         *     Randstrassen an    2 Zufahrtsrechtecke, 0 entartet
         *     Randstrassen aus  47 Zufahrtsrechtecke, 25 entartet
         *                      350 Grasrechtecke,     86 entartet
         *
         * Vier davon waren auf einen einzigen Punkt zusammengefallen - vier
         * gleiche Ecken. Sie tragen keine Flaeche, werden aber gefaerbt: statt
         * zweier Streifen liegen 47 Schnipsel uebereinander.
         *
         * Ursache ist nicht der Filter hier, sondern der Streifenfueller: ohne
         * Randstrassen liegen die langen Endfusswege bei 88 Grad schraeg im
         * Zellraster, ihre Zellen sind also Dreiecke und Trapeze statt
         * Rechtecke, und der Fueller zerlegt sie.
         *
         * Die Schranke ist nicht gegriffen: gemessen liegen ALLE Artefakte
         * unter 1 cm und ALLE echten Rechtecke ueber 20 cm. Dazwischen gibt
         * es nichts.
         */
        private const double ZellenVorschauMindestkante = 0.01;

        private static bool ZellenVorschauTraegtFlaeche(float2[] rechteck)
        {
            if (rechteck == null || rechteck.Length < 3) return false;
            for (var i = 0; i < rechteck.Length; i++)
            {
                var b = rechteck[(i + 1) % rechteck.Length];
                if (math.distance(rechteck[i], b) < ZellenVorschauMindestkante)
                    return false;
            }
            return true;
        }

        /**
         * Eine lochfreie, artgleiche Komponente darf gemeinsam gestreift
         * werden, wenn die rohe Streifenflaeche hoechstens 2 % von den Zellen
         * abweicht. Danach wird die Breite auf die exakte Zellflaeche
         * korrigiert. Mehrteilige und ungenaue Ring-Fuellungen fallen auf den
         * zellweisen Weg zurueck.
         */
        private static bool ZellenVorschauKomponentenstreifen(
            Rahmen rahmen,
            IReadOnlyList<Zelle> zellen,
            IReadOnlyList<int> indices,
            bool gegenUhrzeigersinn,
            out float2[][] rechtecke)
        {
            var kopien = indices.Select((index, neueId) => new Zelle
            {
                Id = neueId,
                Polygon = zellen[index].Polygon,
                Art = zellen[index].Art,
                Material = zellen[index].Material,
                Flaechenabschnitt = zellen[index].Flaechenabschnitt,
            }).ToArray();
            var flaechen = Vereinigung.Vereinige(kopien).Flaechen;
            if (flaechen.Count != 1 || flaechen[0].Loecher.Count != 0)
            {
                rechtecke = null;
                return false;
            }
            var ringpunkte = flaechen[0].Aussenring.Knoten
                .Select(knoten => knoten.Punkt).ToArray();
            var roheRechtecke = ZellenVorschauStreifenrechtecke(
                rahmen, ringpunkte, gegenUhrzeigersinn, false).ToArray();
            var zellflaeche = indices.Sum(index =>
                Geometrie.Flaeche(zellen[index].Polygon));
            var roheFlaeche = roheRechtecke.Sum(ZellenVorschauFlaeche);
            var erlaubterFehler = Math.Max(
                ZellenVorschauStreifenFlaechenEpsilon,
                zellflaeche * ZellenVorschauStreifenFehleranteil);
            if (roheFlaeche <= 0
                || Math.Abs(zellflaeche - roheFlaeche) > erlaubterFehler)
            {
                rechtecke = null;
                return false;
            }
            rechtecke = ZellenVorschauStreifenrechtecke(
                rahmen, ringpunkte, gegenUhrzeigersinn, true).ToArray();
            return true;
        }

        private static double ZellenVorschauFlaeche(float2[] polygon)
        {
            var doppelt = 0.0;
            for (var i = 0; i < polygon.Length; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Length];
                doppelt += (double)a.x * b.y - (double)b.x * a.y;
            }
            return Math.Abs(doppelt / 2);
        }

        private static IReadOnlyList<int[]> ZellenVorschauKomponenten(
            IReadOnlyList<Zelle> zellen)
        {
            var eltern = Enumerable.Range(0, zellen.Count).ToArray();
            int Wurzel(int index)
            {
                while (eltern[index] != index)
                {
                    eltern[index] = eltern[eltern[index]];
                    index = eltern[index];
                }
                return index;
            }
            void Vereinige(int a, int b)
            {
                a = Wurzel(a);
                b = Wurzel(b);
                if (a != b) eltern[b] = a;
            }

            var kantenbesitzer = new Dictionary<KantenSchluessel, int>();
            for (var zellindex = 0; zellindex < zellen.Count; zellindex++)
            {
                var zelle = zellen[zellindex];
                for (var i = 0; i < zelle.Polygon.Anzahl; i++)
                {
                    var schluessel = KantenSchluessel.Von(
                        zelle.Polygon.Knoten(i), zelle.Polygon.Knoten(i + 1));
                    int nachbar;
                    if (!kantenbesitzer.TryGetValue(schluessel, out nachbar))
                    {
                        kantenbesitzer[schluessel] = zellindex;
                        continue;
                    }
                    if (zellen[nachbar].Art == zelle.Art)
                        Vereinige(nachbar, zellindex);
                }
            }

            return Enumerable.Range(0, zellen.Count)
                .GroupBy(Wurzel)
                .Select(gruppe => gruppe.ToArray())
                .ToArray();
        }

        private static bool ZellenVorschauIstRechteck(
            IReadOnlyList<Zelle> zellen,
            IReadOnlyList<int> indices,
            out ZellenVorschauRechteck rechteck)
        {
            rechteck = null;
            if (indices.Count == 0) return false;
            var minX = double.PositiveInfinity;
            var maxX = double.NegativeInfinity;
            var minY = double.PositiveInfinity;
            var maxY = double.NegativeInfinity;
            var flaeche = 0.0;
            foreach (var index in indices)
            {
                var zelle = zellen[index];
                flaeche += Geometrie.Flaeche(zelle.Polygon);
                foreach (var punkt in zelle.Polygon.Punkte)
                {
                    minX = Math.Min(minX, punkt.X);
                    maxX = Math.Max(maxX, punkt.X);
                    minY = Math.Min(minY, punkt.Y);
                    maxY = Math.Max(maxY, punkt.Y);
                }
            }

            var breite = maxX - minX;
            var hoehe = maxY - minY;
            if (breite <= ZellenVorschauKantenEpsilon
                || hoehe <= ZellenVorschauKantenEpsilon
                || Math.Abs(breite * hoehe - flaeche)
                    > ZellenVorschauFlaechenEpsilon)
                return false;

            rechteck = new ZellenVorschauRechteck
            {
                Art = zellen[indices[0]].Art,
                MinX = minX,
                MaxX = maxX,
                MinY = minY,
                MaxY = maxY,
            };
            return true;
        }

        private static List<ZellenVorschauRechteck> ZellenVorschauVerschmelze(
            IReadOnlyList<ZellenVorschauRechteck> quelle)
        {
            var ausgabe = new List<ZellenVorschauRechteck>();
            foreach (var artgruppe in quelle.GroupBy(rechteck => rechteck.Art))
            {
                var waagerecht = ZellenVorschauRasterstreifen(
                    artgruppe.ToArray(), true);
                var senkrecht = ZellenVorschauRasterstreifen(
                    artgruppe.ToArray(), false);
                ausgabe.AddRange(waagerecht.Count <= senkrecht.Count
                    ? waagerecht : senkrecht);
            }
            return ausgabe;
        }

        /**
         * Nach der optimaleren Rasterzerlegung je historischer Gruenart
         * werden nur noch Nachbarn mit identischer voller Seitenkante
         * vereinigt. Das kann die Zahl nur senken und niemals eine bereits
         * gute Zerlegung durch ein unguenstigeres Gesamtraster ersetzen.
         */
        private static List<ZellenVorschauRechteck>
            ZellenVorschauVerschmelzeNachbarn(
                IReadOnlyList<ZellenVorschauRechteck> quelle)
        {
            List<ZellenVorschauRechteck> Versuch(bool zuerstWaagerecht)
            {
                var liste = quelle.Select(ZellenVorschauKopiere).ToList();
                bool FuegeEinPaar(bool waagerecht)
                {
                    for (var i = 0; i < liste.Count; i++)
                        for (var j = i + 1; j < liste.Count; j++)
                        {
                            var a = liste[i];
                            var b = liste[j];
                            var passt = waagerecht
                                ? ZellenVorschauGleich(a.MinY, b.MinY)
                                    && ZellenVorschauGleich(a.MaxY, b.MaxY)
                                    && (ZellenVorschauGleich(a.MaxX, b.MinX)
                                        || ZellenVorschauGleich(b.MaxX, a.MinX))
                                : ZellenVorschauGleich(a.MinX, b.MinX)
                                    && ZellenVorschauGleich(a.MaxX, b.MaxX)
                                    && (ZellenVorschauGleich(a.MaxY, b.MinY)
                                        || ZellenVorschauGleich(b.MaxY, a.MinY));
                            if (!passt) continue;
                            a.MinX = Math.Min(a.MinX, b.MinX);
                            a.MaxX = Math.Max(a.MaxX, b.MaxX);
                            a.MinY = Math.Min(a.MinY, b.MinY);
                            a.MaxY = Math.Max(a.MaxY, b.MaxY);
                            liste.RemoveAt(j);
                            return true;
                        }
                    return false;
                }

                while (FuegeEinPaar(zuerstWaagerecht)
                    || FuegeEinPaar(!zuerstWaagerecht)) { }
                return liste;
            }

            var waagerecht = Versuch(true);
            var senkrecht = Versuch(false);
            return waagerecht.Count <= senkrecht.Count
                ? waagerecht : senkrecht;
        }

        private static bool ZellenVorschauGleich(double a, double b) =>
            Math.Abs(a - b) <= ZellenVorschauKantenEpsilon;

        private static ZellenVorschauRechteck ZellenVorschauKopiere(
            ZellenVorschauRechteck rechteck) =>
            new ZellenVorschauRechteck
            {
                Art = rechteck.Art,
                MinX = rechteck.MinX,
                MaxX = rechteck.MaxX,
                MinY = rechteck.MinY,
                MaxY = rechteck.MaxY,
            };

        /**
         * Die gemeinsamen Zellgrenzen bilden ein exaktes orthogonales Raster.
         * Ein Zeilenlauf darf vorhandene Rechtecke an diesen Grenzen teilen;
         * dadurch werden auch T-Teilungen wieder zu einem grossen Rechteck.
         * Beide Achsen werden versucht, die Ausgabe mit weniger Rechtecken
         * gewinnt. Am 120 x 90-Rechteck sank der Randring damit von 26 auf 4.
         */
        private static List<ZellenVorschauRechteck> ZellenVorschauRasterstreifen(
            IReadOnlyList<ZellenVorschauRechteck> quelle,
            bool zeilenweise)
        {
            if (quelle.Count == 0) return new List<ZellenVorschauRechteck>();
            double[] Achse(IEnumerable<double> rohwerte)
            {
                var werte = new List<double>();
                foreach (var wert in rohwerte.OrderBy(wert => wert))
                    if (werte.Count == 0 || Math.Abs(werte[werte.Count - 1] - wert)
                        > ZellenVorschauKantenEpsilon)
                        werte.Add(wert);
                return werte.ToArray();
            }
            int Index(double[] achse, double wert)
            {
                for (var i = 0; i < achse.Length; i++)
                    if (Math.Abs(achse[i] - wert) <= ZellenVorschauKantenEpsilon)
                        return i;
                throw new InvalidOperationException(
                    "Eine Rechteckgrenze fehlt auf der Vorschau-Rasterachse.");
            }
            var xs = Achse(quelle.SelectMany(rechteck =>
                new[] { rechteck.MinX, rechteck.MaxX }));
            var ys = Achse(quelle.SelectMany(rechteck =>
                new[] { rechteck.MinY, rechteck.MaxY }));
            var belegt = new bool[ys.Length - 1, xs.Length - 1];
            foreach (var rechteck in quelle)
            {
                for (var y = Index(ys, rechteck.MinY);
                     y < Index(ys, rechteck.MaxY); y++)
                    for (var x = Index(xs, rechteck.MinX);
                         x < Index(xs, rechteck.MaxX); x++)
                        belegt[y, x] = true;
            }

            var fertig = new List<ZellenVorschauRechteck>();
            var aktiv = new Dictionary<(int Anfang, int Ende),
                ZellenVorschauRechteck>();
            var schichten = zeilenweise ? ys.Length - 1 : xs.Length - 1;
            var felder = zeilenweise ? xs.Length - 1 : ys.Length - 1;
            for (var schicht = 0; schicht < schichten; schicht++)
            {
                var laeufe = new List<(int Anfang, int Ende)>();
                for (var feld = 0; feld < felder;)
                {
                    bool IstBelegt(int stelle) => zeilenweise
                        ? belegt[schicht, stelle]
                        : belegt[stelle, schicht];
                    if (!IstBelegt(feld)) { feld++; continue; }
                    var anfang = feld;
                    while (feld < felder && IstBelegt(feld)) feld++;
                    laeufe.Add((anfang, feld));
                }

                var naechste = new Dictionary<(int Anfang, int Ende),
                    ZellenVorschauRechteck>();
                foreach (var lauf in laeufe)
                {
                    ZellenVorschauRechteck rechteck;
                    if (aktiv.TryGetValue(lauf, out rechteck))
                    {
                        if (zeilenweise) rechteck.MaxY = ys[schicht + 1];
                        else rechteck.MaxX = xs[schicht + 1];
                    }
                    else rechteck = zeilenweise
                        ? new ZellenVorschauRechteck
                        {
                            Art = quelle[0].Art,
                            MinX = xs[lauf.Anfang],
                            MaxX = xs[lauf.Ende],
                            MinY = ys[schicht],
                            MaxY = ys[schicht + 1],
                        }
                        : new ZellenVorschauRechteck
                        {
                            Art = quelle[0].Art,
                            MinX = xs[schicht],
                            MaxX = xs[schicht + 1],
                            MinY = ys[lauf.Anfang],
                            MaxY = ys[lauf.Ende],
                        };
                    naechste[lauf] = rechteck;
                }
                foreach (var paar in aktiv)
                    if (!naechste.ContainsKey(paar.Key)) fertig.Add(paar.Value);
                aktiv = naechste;
            }
            fertig.AddRange(aktiv.Values);
            return fertig;
        }

        private static float2[] ZellenVorschauWeltrechteck(
            Rahmen rahmen,
            ZellenVorschauRechteck rechteck,
            bool gegenUhrzeigersinn)
        {
            var lokal = gegenUhrzeigersinn
                ? new[]
                {
                    new Punkt(rechteck.MinX, rechteck.MinY),
                    new Punkt(rechteck.MaxX, rechteck.MinY),
                    new Punkt(rechteck.MaxX, rechteck.MaxY),
                    new Punkt(rechteck.MinX, rechteck.MaxY),
                }
                : new[]
                {
                    new Punkt(rechteck.MinX, rechteck.MaxY),
                    new Punkt(rechteck.MaxX, rechteck.MaxY),
                    new Punkt(rechteck.MaxX, rechteck.MinY),
                    new Punkt(rechteck.MinX, rechteck.MinY),
                };
            return lokal.Select(punkt => ZellenPunkt(rahmen.NachWelt(punkt)))
                .ToArray();
        }

        private static IEnumerable<float2[]> ZellenVorschauStreifenrechtecke(
            Rahmen rahmen,
            Zelle zelle,
            bool gegenUhrzeigersinn)
            => ZellenVorschauStreifenrechtecke(
                rahmen, zelle.Polygon.Punkte, gegenUhrzeigersinn, true);

        private static IEnumerable<float2[]> ZellenVorschauStreifenrechtecke(
            Rahmen rahmen,
            IEnumerable<Punkt> punkte,
            bool gegenUhrzeigersinn,
            bool flaecheErhalten)
        {
            var punktliste = punkte.ToArray();
            var polygon = punktliste
                .Select(punkt => new float2((float)punkt.X, (float)punkt.Y))
                .ToArray();
            var streifenliste = ParkingSurfaceStrips.Fill(
                new[] { polygon }, ParkingSurfaceStrips.PreferredWidth);
            var streifenflaeche = streifenliste.Sum(streifen =>
                math.length(streifen.To - streifen.From) * streifen.Width);
            var zielFlaeche = Math.Abs(Geometrie.Vorzeichenflaeche(punktliste));
            var breitenfaktor = flaecheErhalten && streifenflaeche > 0
                ? zielFlaeche / streifenflaeche
                : 1;
            foreach (var streifen in streifenliste)
            {
                var richtung = streifen.To - streifen.From;
                var laenge = math.length(richtung);
                var breite = streifen.Width * breitenfaktor;
                if (laenge <= ZellenVorschauKantenEpsilon
                    || breite <= ZellenVorschauKantenEpsilon) continue;
                richtung /= laenge;
                var normal = new double2(-richtung.y, richtung.x)
                    * (breite / 2);
                var lokal = gegenUhrzeigersinn
                    ? new[]
                    {
                        streifen.From - normal,
                        streifen.To - normal,
                        streifen.To + normal,
                        streifen.From + normal,
                    }
                    : new[]
                    {
                        streifen.From + normal,
                        streifen.To + normal,
                        streifen.To - normal,
                        streifen.From - normal,
                    };
                yield return lokal.Select(punkt => ZellenPunkt(
                        rahmen.NachWelt(new Punkt(punkt.x, punkt.y))))
                    .ToArray();
            }
        }

        /**
         * Der schraege Randstrassen-Zellverbund ist ein Ring mit schraegen
         * Grenzen und deshalb nicht exakt als endliches Rechteckmosaik
         * darstellbar. Die bereits aus genau seinen beiden Fahrbahnkanten
         * abgeleitete Mittellinie liefert je Ringseite den einen stumpfen
         * Rechteckstreifen, den auch der bisherige BayOnRoad-Rueckfall benutzt
         * hat. Gemessen am schraegen Pflichtfall bleibt die Pruefung damit bei
         * 0 statt auf 35 zu steigen.
         */
        private static float2[][] ZellenVorschauStrassenlinien(
            Rahmen rahmen,
            IEnumerable<ZellenStrasse> strassen,
            double breite,
            bool gegenUhrzeigersinn)
        {
            /*
             * AN DER ECKE MUSS DAS RECHTECK UEBER DIE LINIE HINAUS.
             *
             * Bis zum 2026-09-01 endete jedes Strassenrechteck genau am Ende
             * seiner Mittellinie. Wo zwei Randstrassen sich treffen, blieb
             * dadurch ein Quadrat von einer halben Breite unbedeckt - obwohl
             * die Mittellinien dort zusammenlaufen und ein Auto im Spiel um
             * die Ecke kommt. Gemessen am Rechteckfall: die Bucht von x 7,5
             * bis 10,5 lag an einer Strasse, die erst bei x 10,4 begann; von
             * ihren 3,0 m Breite lagen 0,1 m auf der Strasse, und sie zaehlte
             * als abgehaengt. Ueber alle Referenzformen erklaerte diese eine
             * Luecke 8 von 8, 12 von 12, 10 von 12 und 10 von 50 der
             * "unerreichbaren" Buchten.
             *
             * Verlaengert wird deshalb - aber NUR an einem Ende, an dem
             * wirklich eine weitere Strasse ansetzt. Ein freies Ende zu
             * verlaengern hiesse Belag dorthin zu schieben, wo keiner
             * hingehoert; das waere derselbe Fehler mit umgekehrtem Vorzeichen.
             *
             * UM WIE VIEL, das war der zweite Anlauf. Eine halbe Breite in
             * Fahrtrichtung stimmt nur im rechten Winkel. An der schraegen
             * Ecke ist sie zu viel: gemessen schob sie bei "Schraeg" 12 und
             * bei "Referenz 08s" 13 Buchten unter den Asphalt - vorher lagen
             * auf allen Formen null. Das war kein geloestes Problem, sondern
             * ein umbenanntes.
             *
             * Richtig ist der GEHRUNGSSCHNITT, wie ihn jeder Linienversatz
             * macht: die versetzte Kante dieser Strasse laeuft genau bis dahin,
             * wo die gleich versetzte Kante der Nachbarstrasse sie schneidet -
             * keinen Meter weiter. Im rechten Winkel ergibt das wieder die
             * halbe Breite, an jeder anderen Ecke von selbst das passende Mass.
             * Das Rechteck wird dadurch zum Trapez; vier Punkte bleiben es.
             *
             * Zwei Ausnahmen fallen auf das stumpfe Ende zurueck: eine
             * GABELUNG (mehr als eine Fortsetzung - welcher Nachbar den
             * Schnitt bestimmt, waere geraten) und eine zu SPITZE Ecke, deren
             * Gehrung als langer Dorn weit ins Gelaende stiese.
             */
            const double GehrungsGrenze = 4.0;
            var liste = strassen.ToList();

            bool Trifft(Punkt a, Punkt b)
                => Geometrie.Laenge(a - b) <= ZellenVorschauKantenEpsilon;

            ZellenStrasse Fortsetzung(Punkt punkt, ZellenStrasse ausser)
            {
                ZellenStrasse gefunden = null;
                foreach (var andere in liste)
                {
                    if (ReferenceEquals(andere, ausser)) continue;
                    if (!Trifft(andere.A, punkt) && !Trifft(andere.B, punkt))
                        continue;
                    if (gefunden != null) return null;   // Gabelung
                    gefunden = andere;
                }
                return gefunden;
            }

            // Richtung, in der die Nachbarstrasse vom gemeinsamen Punkt WEG
            // fuehrt - unabhaengig davon, wie herum sie gespeichert ist.
            Punkt Weg(ZellenStrasse nachbar, Punkt punkt)
            {
                var ziel = Trifft(nachbar.A, punkt) ? nachbar.B : nachbar.A;
                var vektor = ziel - punkt;
                var laenge = Geometrie.Laenge(vektor);
                return laenge <= ZellenVorschauKantenEpsilon
                    ? new Punkt(0, 0) : vektor * (1 / laenge);
            }

            /*
             * Der Gehrungspunkt auf einer Seite: Schnitt der beiden um
             * `seite * breite/2` versetzten Mittellinien. `hinein` zeigt in
             * das Gelenk, `hinaus` aus ihm heraus - beide in derselben
             * Umlaufrichtung, sonst waere "links" nicht dieselbe Seite.
             */
            Punkt Gehrung(Punkt gelenk, Punkt hinein, Punkt hinaus,
                double seite, Punkt rueckfall)
            {
                var kreuz = hinein.X * hinaus.Y - hinein.Y * hinaus.X;
                if (Math.Abs(kreuz) < 1e-9) return rueckfall;   // gerade Fahrt
                var versatz = seite * (breite / 2);
                var n1 = new Punkt(-hinein.Y, hinein.X) * versatz;
                var n2 = new Punkt(-hinaus.Y, hinaus.X) * versatz;
                var d = n2 - n1;
                var t = (d.X * hinaus.Y - d.Y * hinaus.X) / kreuz;
                if (Math.Abs(t) > GehrungsGrenze * (breite / 2)) return rueckfall;
                return gelenk + n1 + hinein * t;
            }

            var ausgabe = new List<float2[]>();
            foreach (var strasse in liste)
            {
                var vektor = strasse.B - strasse.A;
                var laenge = Geometrie.Laenge(vektor);
                if (laenge <= ZellenVorschauKantenEpsilon) continue;
                var richtung = vektor * (1 / laenge);
                var normal = new Punkt(-richtung.Y, richtung.X) * (breite / 2);
                var vorher = Fortsetzung(strasse.A, strasse);
                var nachher = Fortsetzung(strasse.B, strasse);

                // Am Anfang kommt die Fahrt aus dem Nachbarn HEREIN, am Ende
                // geht sie in ihn hinaus - dieselbe Umlaufrichtung fuer beide.
                Punkt EckeA(double seite)
                {
                    var rueckfall = strasse.A + normal * seite;
                    return vorher == null ? rueckfall
                        : Gehrung(strasse.A, Weg(vorher, strasse.A) * -1,
                            richtung, seite, rueckfall);
                }

                Punkt EckeB(double seite)
                {
                    var rueckfall = strasse.B + normal * seite;
                    return nachher == null ? rueckfall
                        : Gehrung(strasse.B, richtung,
                            Weg(nachher, strasse.B), seite, rueckfall);
                }

                var lokal = gegenUhrzeigersinn
                    ? new[]
                    {
                        EckeA(-1), EckeB(-1), EckeB(1), EckeA(1),
                    }
                    : new[]
                    {
                        EckeA(1), EckeB(1), EckeB(-1), EckeA(-1),
                    };
                ausgabe.Add(lokal.Select(punkt =>
                    ZellenPunkt(rahmen.NachWelt(punkt))).ToArray());
            }
            return ausgabe.ToArray();
        }

    }
}
