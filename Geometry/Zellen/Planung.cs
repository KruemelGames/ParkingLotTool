using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingLotTool.Geometry.Zellen
{
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
                    throw new InvalidOperationException("The outer ring contains a zero-length edge.");
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
                {
                    /*
                     * EINE GESTRECKTE ECKE IST KEIN FEHLER.
                     *
                     * Bis zum 2026-09-01 flog hier eine Ausnahme, sobald zwei
                     * benachbarte Kanten parallel liefen. Das trifft aber den
                     * voellig harmlosen Fall mit: einen Punkt, der genau auf
                     * der Geraden seiner beiden Nachbarn liegt. Die
                     * Referenzform "Referenz 08s" hat genau so einen - Punkt
                     * 6 bei (68, 83), beide Kanten in Richtung (-12, -9),
                     * Skalarprodukt +225. Der Bau brach dort komplett ab: kein
                     * schlechtes Layout, gar keins. Gesehen hat das niemand,
                     * weil der Paritaetstest bis dahin den alten Rechenweg
                     * mass.
                     *
                     * Fuer eine gestreckte Ecke gibt es keinen Schnittpunkt zu
                     * rechnen und auch keinen noetig: die versetzte Kante
                     * laeuft geradeaus weiter, ihr Anfangspunkt IST der
                     * versetzte Eckpunkt. Das ist exakt, keine Naeherung.
                     *
                     * Eine SPITZE - beide Kanten parallel, aber
                     * gegenlaeufig - bleibt ein Fehler: dort faltet sich der
                     * Rand auf sich selbst, und ein Innenrand ist nicht
                     * definiert.
                     */
                    if (Geometrie.Skalar(vorher.Richtung, aktuell.Richtung) <= 0)
                        throw new InvalidOperationException(
                            "Two adjacent boundary edges fold back on each other.");
                    innen[i] = aktuell.Punkt;
                    continue;
                }
                var parameter = Geometrie.Kreuz(
                    aktuell.Punkt - vorher.Punkt,
                    aktuell.Richtung) / nenner;
                innen[i] = vorher.Punkt + vorher.Richtung * parameter;
            }

            /*
             * EINE KANTE, DIE BEIM VERSETZEN VERSCHWINDET, FAELLT WEG.
             *
             * Bauzettel 2026-09-27 15:47 (AJ54): die erste Kante nach einer
             * Ecke von 57,6 Grad war 24,1 m lang, dahinter ein Knick von 5
             * Grad. Um 13,9 m versetzt (Innenkante der Randstrasse) reicht
             * der Schnitt an der spitzen Ecke 25,3 m weit - weiter, als die
             * Kante lang ist. Die versetzte Kante lief deshalb 0,59 m
             * RUECKWAERTS, und Asphalt wie Gras bekamen einen Zipfel von 5,2
             * Grad. CS2 schiebt jeden Knoten vor dem Zerlegen 0,1 m nach
             * innen; den Zipfel trug das 2,2 m weit, ein Dreieck klappte um
             * und legte 14,6 m2 Gras auf die Fahrgasse.
             *
             * Jeder Linienversatz loest das gleich: eine rueckwaerts laufende
             * Kante hat die Laenge null, ihre beiden Nachbarn schneiden sich
             * direkt. Die Knotenzahl bleibt, damit Kante i weiter zu Kante i
             * des Umrisses gehoert (Randreihe und Ringabschnitte zaehlen
             * darauf) - die weggefallene Kante hat nur Anfang gleich Ende.
             * Das wiederholt sich, bis keine Kante mehr rueckwaerts laeuft;
             * ein Wegfall kann den naechsten ausloesen.
             */
            var weg = new bool[aussenring.Count];
            for (var runde = 0; runde < aussenring.Count; runde++)
            {
                var neu = false;
                for (var i = 0; i < aussenring.Count; i++)
                {
                    if (weg[i]) continue;
                    var ende = innen[(i + 1) % aussenring.Count];
                    if (Geometrie.Skalar(ende - innen[i],
                            verschobeneKanten[i].Richtung) >= 0) continue;
                    weg[i] = true;
                    neu = true;
                }
                if (!neu) break;
                if (weg.All(w => w))
                    throw new InvalidOperationException(
                        $"The boundary inset by {abstand:R} m has no edge left.");
                for (var i = 0; i < aussenring.Count; i++)
                {
                    if (weg[i]) continue;
                    var vorherige = Geometrie.Mod(i - 1, aussenring.Count);
                    while (weg[vorherige])
                        vorherige = Geometrie.Mod(vorherige - 1, aussenring.Count);
                    var vorher = verschobeneKanten[vorherige];
                    var aktuell = verschobeneKanten[i];
                    var nenner = Geometrie.Kreuz(vorher.Richtung, aktuell.Richtung);
                    var schnitt = nenner == 0 ? aktuell.Punkt
                        : vorher.Punkt + vorher.Richtung * (Geometrie.Kreuz(
                            aktuell.Punkt - vorher.Punkt, aktuell.Richtung) / nenner);
                    for (var k = Geometrie.Mod(vorherige + 1, aussenring.Count);
                         ; k = Geometrie.Mod(k + 1, aussenring.Count))
                    {
                        innen[k] = schnitt;
                        if (k == i) break;
                    }
                }
            }

            // Nur wenn der Ring nachweislich unsauber ist - sonst bleibt jede
            // Form bitgleich (siehe ErodierterRing).
            if (RingUnsauber(aussenring, innen, abstand))
            {
                var erodiert = ErodierterRing(aussenring, verschobeneKanten, abstand);
                ParkingGeometry.Live("  innenrand " + abstand.ToString("F2") + " m: Arm zu schmal, erodierter Ring "
                    + (erodiert == null ? "nicht darstellbar (" + _erosionsgrund + ")" : "uebernommen"));
                if (erodiert != null) innen = erodiert;
            }

            if (Geometrie.Vorzeichenflaeche(innen) <= 0)
                throw new InvalidOperationException(
                    $"The boundary inset by {abstand:R} m is not counter-clockwise.");
            if (innen.Any(punkt => !Geometrie.EnthaeltOderRand(aussenring, punkt)))
                throw new InvalidOperationException("The inset boundary leaves the site.");
            return innen;
        }

        /**
         * DIE FAHRBAHN ENDET, WO DIE ACHSE ENDET (Issue #10, Nutzer 2026-10-09).
         *
         * In einem schmalen Arm klappt die Randstrassenachse (10,4 m) frueher
         * zusammen als ihr Aussenrand (6,9 m): die Achse an der Spitze P,
         * wo der Arm 20,8 m breit ist, der Aussenrand erst bei 13,8 m. Dazwischen
         * lag Fahrbahn ohne Weg, und die Randreihen liefen daran entlang bis
         * fast nach oben - 25 Buchten ohne Fahrweg, angebunden an P bis 36 m
         * entfernt. Der Nutzer hat entschieden: oberhalb von P keine Buchten,
         * dort Gruen.
         *
         * Deshalb wird der Aussenrand an jeder zusammengeklappten Stelle der
         * Achse quer gekappt: die Kante vor der Stelle endet eine halbe
         * Fahrbahnbreite neben P, die Kante danach beginnt dort, und eine
         * Kante dazwischen verbindet beide. Die uebrigen Kanten der Stelle
         * haben Laenge null - Kante i bleibt Kante i.
         */
        internal static IReadOnlyList<Punkt> KappeAnSpitzen(IReadOnlyList<Punkt> rand,
            IReadOnlyList<Punkt> achse, double halbbreite)
        {
            var n = achse.Count;
            if (rand == null || rand.Count != n || n < 4) return rand;
            bool Null(int i) => Geometrie.Laenge(achse[(i + 1) % n] - achse[i]) < 1e-9;
            if (Enumerable.Range(0, n).All(Null) || !Enumerable.Range(0, n).Any(Null)) return rand;
            var ergebnis = rand.ToArray();
            // Bei einer Kante mit Laenge beginnen, damit keine Stelle ueber den Ringanfang reicht.
            var start = Enumerable.Range(0, n).First(i => !Null(i));
            for (var k = 0; k < n; k++)
            {
                var a = (start + k) % n;
                if (Null(a) || !Null((a + 1) % n)) continue;
                // Kante a hat Laenge, danach folgt eine Stelle aus Nullkanten bis Kante b.
                var b = (a + 1) % n;
                var anzahl = 0;
                while (Null(b)) { b = (b + 1) % n; anzahl++; }
                var p = achse[(a + 1) % n];
                // Eckiges Strassenende eine halbe Fahrbahnbreite hinter P, wie
                // der Belag um eine Achse, die dort endet. Ein flacher Schnitt
                // durch P liesse dahinter Flaeche naeher als die halbe Breite
                // an der Achse - die Ueberlapp-Regel machte die ganze Armspitze
                // dann zu Belag (gemessen am Bericht von kunred).
                Punkt Aussen(int i, double laengs)
                {
                    var d = achse[(i + 1) % n] - achse[i];
                    var l = Geometrie.Laenge(d);
                    // Gegen den Uhrzeigersinn liegt das Innere links, der Umriss rechts.
                    return p + new Punkt(d.Y / l, -d.X / l) * halbbreite + d * (laengs / l);
                }
                var pa = Aussen(a, halbbreite);
                var pb = Aussen(b, -halbbreite);
                // Reicht der alte Rand gar nicht so weit, ist dort nichts zu kappen.
                Punkt Richtung(int i) => (achse[(i + 1) % n] - achse[i]) * (1 / Geometrie.Laenge(achse[(i + 1) % n] - achse[i]));
                if (Geometrie.Skalar(rand[(a + 1) % n] - pa, Richtung(a)) <= 0
                    || Geometrie.Skalar(rand[b] - pb, Richtung(b)) >= 0) continue;
                ergebnis[(a + 1) % n] = pa;
                for (var j = 2; j <= anzahl; j++) ergebnis[(a + j) % n] = pb;
                ergebnis[b] = pb;
            }
            if (Geometrie.Vorzeichenflaeche(ergebnis) <= 0) return rand;
            return ergebnis;
        }

        /**
         * EIN ARM, SCHMALER ALS ZWEIMAL DER VERSATZ, KLAPPT NICHT ZUSAMMEN.
         *
         * Issue #10 (2026-10-09): ein U-foermiger Parkplatz, dessen linker
         * Arm oben nur 10,5 m breit ist. Der Rueckwaertstest oben strich die
         * Kanten im Innenhof des U und verschnitt danach ihre Nachbarn - die
         * aber lagen auf verschiedenen Seiten des Innenhofs. Beim 13,9-m-Ring
         * lagen Stuecke so naeher am Umriss als erlaubt (Haarriss, verwaiste
         * Bucht). Den Stern quer ueber den Innenhof machte dagegen das Teilen
         * der Wege (siehe `ZellenSchneideStrassen`).
         *
         * Richtig ist der Rand des ERODIERTEN Umrisses: alle Punkte, die
         * mindestens `abstand` von jeder Umrisskante entfernt sind. Er
         * besteht aus Stuecken der versetzten Kanten. Von jeder versetzten
         * Kante bleibt genau der Teil, der zu KEINER Umrisskante naeher
         * liegt als der Versatz (der Abstand zu einer Strecke ist entlang
         * einer Geraden konvex, die zu nahe Stelle also ein Intervall). Die
         * Stuecke ergeben eine oder mehrere Schlaufen; es bleibt die
         * groesste gegen den Uhrzeigersinn. Der Ring geht so weit in einen
         * schmalen Arm, wie er hineinpasst - und kein Stueck verlaesst den
         * Bereich, in den die Strasse passt.
         *
         * Kante i behaelt ihren Platz: eine Kante ohne Stueck hat Laenge
         * null. Laesst sich die Schlaufe so nicht darstellen (eine Kante mit
         * zwei Stuecken, Reihenfolge verdreht), bleibt es beim alten Ring.
         */
        private static bool RingUnsauber(IReadOnlyList<Punkt> aussen, Punkt[] innen, double abstand)
        {
            var n = innen.Length;
            for (var i = 0; i < n; i++)
            {
                var a = innen[i];
                var b = innen[(i + 1) % n];
                if (Geometrie.Laenge(b - a) < 1e-9) continue;
                foreach (var t in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
                {
                    var p = a + (b - a) * t;
                    // Auch ausserhalb: ueber einem Innenhof ist man weit weg
                    // von jeder Kante und trotzdem nicht im Parkplatz.
                    if (!Geometrie.EnthaeltOderRand(aussen, p)
                        || AbstandZumUmriss(aussen, p) < abstand - 0.01) return true;
                }
            }
            return false;
        }

        private static double AbstandZumUmriss(IReadOnlyList<Punkt> aussen, Punkt p)
        {
            var besterAbstand = double.MaxValue;
            for (var j = 0; j < aussen.Count; j++)
                besterAbstand = Math.Min(besterAbstand,
                    Geometrie.AbstandPunktStrecke(p, aussen[j], aussen[(j + 1) % aussen.Count]));
            return besterAbstand;
        }

        [ThreadStatic] private static string _erosionsgrund;

        private sealed class Ringstueck
        {
            internal int Kante;
            internal Punkt Anfang;
            internal Punkt Ende;
        }

        private static Punkt[] ErodierterRing(IReadOnlyList<Punkt> aussen,
            (Punkt Punkt, Punkt Richtung)[] linien, double abstand)
        {
            var n = aussen.Count;
            const double eps = 1e-6;
            // Rohe Gehrungspunkte, ohne jeden Wegfall.
            var roh = new Punkt[n];
            for (var i = 0; i < n; i++)
            {
                var v = linien[Geometrie.Mod(i - 1, n)];
                var w = linien[i];
                var nenner = Geometrie.Kreuz(v.Richtung, w.Richtung);
                roh[i] = Math.Abs(nenner) < 1e-12 ? w.Punkt
                    : v.Punkt + v.Richtung * (Geometrie.Kreuz(w.Punkt - v.Punkt, w.Richtung) / nenner);
            }

            var stuecke = new List<Ringstueck>();
            for (var i = 0; i < n; i++)
            {
                var basis = linien[i].Punkt;
                var laenge = Geometrie.Laenge(linien[i].Richtung);
                if (laenge < 1e-12) continue;
                var u = linien[i].Richtung * (1.0 / laenge);
                Punkt Ort(double t) => basis + u * t;
                var t0 = Geometrie.Skalar(roh[i] - basis, u);
                var t1 = Geometrie.Skalar(roh[(i + 1) % n] - basis, u);
                if (t1 <= t0 + eps) continue;
                var frei = new List<(double Von, double Bis)> { (t0, t1) };
                for (var j = 0; j < n && frei.Count > 0; j++)
                {
                    if (j == i) continue;
                    var a = aussen[j];
                    var b = aussen[(j + 1) % n];
                    double Abst(double t) => Geometrie.AbstandPunktStrecke(Ort(t), a, b);
                    // Konvex in t: Minimum per Dreiteilung, dann beide Raender.
                    double lo = t0, hi = t1;
                    for (var k = 0; k < 80; k++)
                    {
                        var m1 = lo + (hi - lo) / 3;
                        var m2 = hi - (hi - lo) / 3;
                        if (Abst(m1) < Abst(m2)) hi = m2; else lo = m1;
                    }
                    var tm = (lo + hi) / 2;
                    if (Abst(tm) >= abstand - eps) continue;
                    double Rand(double innen, double aussenT)
                    {
                        if (Abst(aussenT) < abstand - eps) return aussenT;
                        for (var k = 0; k < 80; k++)
                        {
                            var m = (innen + aussenT) / 2;
                            if (Abst(m) < abstand - eps) innen = m; else aussenT = m;
                        }
                        return (innen + aussenT) / 2;
                    }
                    var vonZu = Rand(tm, t0);
                    var bisZu = Rand(tm, t1);
                    var neu = new List<(double Von, double Bis)>();
                    foreach (var (von, bis) in frei)
                    {
                        if (bisZu <= von || vonZu >= bis) { neu.Add((von, bis)); continue; }
                        if (vonZu > von + eps) neu.Add((von, vonZu));
                        if (bisZu < bis - eps) neu.Add((bisZu, bis));
                    }
                    frei = neu;
                }
                foreach (var (von, bis) in frei)
                    if (bis - von > 1e-4)
                        stuecke.Add(new Ringstueck { Kante = i, Anfang = Ort(von), Ende = Ort(bis) });
            }
            if (stuecke.Count < 3) { _erosionsgrund = stuecke.Count + " Stuecke"; return null; }

            // Zu Schlaufen verketten: das Ende eines Stuecks ist der Anfang des naechsten.
            var genutzt = new bool[stuecke.Count];
            List<Ringstueck> beste = null;
            var besteFlaeche = 0.0;
            for (var start = 0; start < stuecke.Count; start++)
            {
                if (genutzt[start]) continue;
                var schlaufe = new List<Ringstueck>();
                var aktuell = start;
                var geschlossen = false;
                while (aktuell >= 0 && !genutzt[aktuell])
                {
                    genutzt[aktuell] = true;
                    schlaufe.Add(stuecke[aktuell]);
                    var ende = stuecke[aktuell].Ende;
                    var naechstes = -1;
                    var naechsterAbstand = 1e-3;
                    for (var k = 0; k < stuecke.Count; k++)
                    {
                        var d = Geometrie.Laenge(stuecke[k].Anfang - ende);
                        if (d < naechsterAbstand) { naechsterAbstand = d; naechstes = k; }
                    }
                    if (naechstes == start) { geschlossen = true; break; }
                    aktuell = naechstes;
                }
                if (!geschlossen || schlaufe.Count < 3) continue;
                var flaeche = Geometrie.Vorzeichenflaeche(schlaufe.Select(st => st.Anfang).ToList());
                if (flaeche > besteFlaeche) { besteFlaeche = flaeche; beste = schlaufe; }
            }
            if (beste == null) { _erosionsgrund = "keine geschlossene Schlaufe aus " + stuecke.Count + " Stuecken"; return null; }

            // Kante i behaelt ihren Platz: je Kante hoechstens ein Stueck, und die
            // Kanten folgen einander in Umrissreihenfolge.
            var nachKante = new Ringstueck[n];
            foreach (var st in beste)
            {
                if (nachKante[st.Kante] != null) { _erosionsgrund = "Kante " + st.Kante + " zweimal"; return null; }
                nachKante[st.Kante] = st;
            }
            var erste = Array.FindIndex(nachKante, st => st != null);
            var reihenfolge = beste.Select(st => st.Kante).ToList();
            var versatz = reihenfolge.IndexOf(erste);
            for (var k = 0; k < reihenfolge.Count; k++)
                if (reihenfolge[(versatz + k) % reihenfolge.Count]
                    != nachKante.Select((st, i) => (st, i)).Where(x => x.st != null).ElementAt(k).i)
                { _erosionsgrund = "Reihenfolge"; return null; }

            var ergebnis = new Punkt[n];
            for (var i = 0; i < n; i++)
            {
                var k = i;
                while (nachKante[k] == null) k = (k + 1) % n;
                ergebnis[i] = nachKante[k].Anfang;
            }
            return ergebnis;
        }

        internal static Bandplan Baender(
            double minY,
            double maxY,
            double buchttiefe,
            double fahrgassenbreite,
            double gruenstreifenbreite,
            double? ersterModulanfang = null, bool einseitigErlaubt = false)
        {
            var modulhoehe = 2 * buchttiefe + fahrgassenbreite;
            var hoehe = maxY - minY;
            var module = 0;
            double modulanfang;
            if (ersterModulanfang.HasValue)
            {
                modulanfang = ersterModulanfang.Value;
                if (modulanfang < minY - 1e-6 || modulanfang > maxY)
                    throw new ArgumentOutOfRangeException(
                        nameof(ersterModulanfang));
                while (modulanfang + (module + 1) * modulhoehe
                       + module * gruenstreifenbreite <= maxY + 1e-6)
                    module++;
            }
            else
            {
                while ((module + 1) * modulhoehe
                       + module * gruenstreifenbreite <= hoehe)
                    module++;
                var belegteHoehe = module * modulhoehe
                    + (module - 1) * gruenstreifenbreite;
                modulanfang = minY + (hoehe - belegteHoehe) / 2;
            }
            if (module == 0 && einseitigErlaubt)
                return Ringlosplan.EinseitigesBand(minY, maxY, buchttiefe, fahrgassenbreite);
            if (module == 0)
                throw new InvalidOperationException("No parking module fits inside the inner contour.");
            var cursor = minY;
            var baender = new List<Bandabschnitt>();
            var modulplaene = new List<Parkmodulabschnitt>();
            var bandId = 0;
            var reihenId = 0;

            Bandabschnitt FuegeHinzu(
                double ende,
                Zellart art,
                int? reihe = null)
            {
                if (ende <= cursor) return null;
                var band = new Bandabschnitt(
                    bandId++, cursor, ende, art, reihe);
                baender.Add(band);
                cursor = ende;
                return band;
            }

            FuegeHinzu(modulanfang, Zellart.Restgruen);
            for (var modul = 0; modul < module; modul++)
            {
                var ersteReihe = FuegeHinzu(
                    cursor + buchttiefe, Zellart.Bucht, reihenId++);
                var fahrgasse = FuegeHinzu(
                    cursor + fahrgassenbreite, Zellart.Fahrgasse);
                var zweiteReihe = FuegeHinzu(
                    cursor + buchttiefe, Zellart.Bucht, reihenId++);
                modulplaene.Add(new Parkmodulabschnitt
                {
                    Id = modul,
                    ErsteReihe = ersteReihe,
                    Fahrgasse = fahrgasse,
                    ZweiteReihe = zweiteReihe,
                });
                if (modul + 1 < module)
                    FuegeHinzu(cursor + gruenstreifenbreite, Zellart.Gruenstreifen);
            }
            FuegeHinzu(maxY, Zellart.Restgruen);
            return new Bandplan
            {
                Baender = baender,
                Module = modulplaene,
            };
        }

        /**
         * Wo die Querstrassen liegen - in BUCHTEN gemessen und MITTIG angelegt.
         *
         * Ansage des Nutzers am 2026-08-21:
         *
         *   "Verbindungstrasse nahe an Randstrasse und dadurch auf Fahrtgasse
         *    nur noch Platz fuer 1 oder 2 Fahrtbuchten waehrend die anderen
         *    aber 20 haben. Das wird ungleich. Und besonders sieht das
         *    unschoen aus."
         *
         * Vorher wurde ein Zollstock links angelegt und stur alle `Cr` Meter
         * eine Querstrasse gesetzt, solange noch irgendetwas hineinpasste.
         * Rechts blieb dann ein Rest von wenigen Metern uebrig - eine
         * Querstrasse ohne eine einzige Bucht dahinter. Gemessen an einer
         * 175-m-Reihe: 5 Querstrassen, 40 Buchten, der letzte Abschnitt
         * 3,5 m breit und leer.
         *
         * Jetzt gilt:
         *   1. Der eingestellte Abstand wird in eine BUCHTENZAHL je inneren
         *      Abschnitt umgerechnet. Damit ist die Laenge der inneren
         *      Abschnitte fest und ueberall gleich.
         *   2. Die Querstrassen werden mittig eingehaengt; was uebrig bleibt,
         *      teilen sich die beiden ENDabschnitte zu gleichen Teilen.
         *   3. Es werden mehrere Anzahlen durchgerechnet und die genommen,
         *      deren Endabschnitte am wenigsten von den inneren abweichen.
         *      Eine Querstrasse WEGZULASSEN ist damit ein normales Ergebnis
         *      und kein Notausgang.
         *
         * Dieselbe 175-m-Reihe: 4 Querstrassen, Abschnitte 32/33/33/33/32 m,
         * 43 Buchten, kein Stummel.
         *
         * Der Nutzer hat dazu ausdruecklich gesagt, Symmetrie muesse NICHT
         * erzwungen werden - erzwungen wird deshalb nur die Gleichheit der
         * inneren Abschnitte; die Enden duerfen abweichen, aber beide gleich.
         */
        /**
         * WIEVIELE BUCHTEN EIN ABSCHNITT MINDESTENS TRAGEN MUSS.
         *
         * Sie steht hier als EINE Konstante, weil sie an zwei Stellen
         * gebraucht wird - und weil genau das ihr Problem war: die Auswahl
         * der Querstrassenzahl liess Endabschnitte ab ZWEI Buchten zu, die
         * Pruefung danach verlangte FUENF. Die Auswahl nahm also brav die
         * groesste Anzahl, die "passt", und die Pruefung warf sie danach
         * vollstaendig weg.
         *
         * Nutzerbefund am 2026-08-25: 21 Buchten, N = 9 eingestellt, KEINE
         * Verbindungsstrasse - und erst bei N = 5 erschien eine. Gemessen mit
         * `--quer` ueber acht Werte von N:
         *
         *     N= 9   Abschnitte 3/9/9/3   alle verworfen
         *     N= 8   Abschnitte 3/8, 8/3  alle verworfen
         *     N= 5   Abschnitte 5/5       behalten
         *    N=12   Abschnitte 9/9       behalten
         *
         * Der Regler wirkte damit verkehrt herum: je KLEINER der gewuenschte
         * Abstand, desto wahrscheinlicher fiel alles aus. Seine Einordnung
         * dazu, und sie trifft: *"Es geht im Grunde darum, dass so viele
         * Buchten da sind, dass eine Querstrasse rein kann und N trotzdem
         * erfuellt wird."*
         *
         * Mit einer gemeinsamen Schwelle waehlt die Auswahl jetzt nur noch
         * Anzahlen, die die Pruefung auch ueberlebt.
         */
        internal static int Restmindestbuchten(double abstand, double breite, bool kappen)
            => Math.Min(5, Math.Max(1, (int)Math.Round(
                abstand / breite - (kappen ? 2 : 0), MidpointRounding.AwayFromZero)));

        private static List<double> Querstrassenmitten(
            double minX,
            double maxX,
            double buchtbreite,
            double querstrassenbreite,
            double querstrassenabstand,
            bool kappen)
        {
            var mitten = new List<double>();
            var laenge = maxX - minX;
            if (double.IsNaN(querstrassenabstand)
                || double.IsInfinity(querstrassenabstand)
                || querstrassenabstand <= 0
                || buchtbreite <= 0
                || laenge <= 0)
                return mitten;

            /**
             * DIE RESERVE HAENGT AM KAPPEN-SCHALTER.
             *
             * Der eingestellte Abstand ist Mitte-zu-Mitte gemeint. MIT Kappen
             * besteht ein innerer Abschnitt aus N Buchten und zwei Kappen von
             * je einer Buchtbreite. OHNE Kappen sind es N Buchten und sonst
             * nichts.
             *
             * Stand hier fest die 2, waehrend `FuegeFreienAbschnittHinzu`
             * ohne Kappen nichts mehr reservierte, rechnete die Auswahl mit
             * anderen Zahlen als der Bau danach. Nutzerbefund am 2026-08-24:
             * eingestellt 5 Buchten, im Spiel standen 7.
             */
            var reserveBuchten = kappen ? 2 : 0;
            var buchtenInnen = (int)Math.Round(
                (querstrassenabstand - reserveBuchten * buchtbreite) / buchtbreite,
                MidpointRounding.AwayFromZero);
            if (buchtenInnen < 1) buchtenInnen = 1;
            var innen = (buchtenInnen + reserveBuchten) * buchtbreite;

            /**
             * N IST EIN VERSPRECHEN, KEIN RICHTWERT.
             *
             * Hier stand ein ENDABSCHNITTS-AUSGLEICH: die Auswahl probierte
             * alle Querstrassenzahlen durch und nahm die, bei der End- und
             * Innenabschnitt gleich viele Buchten tragen
             * (`fehler = |buchtenEnde - buchtenInnen|`). Damit sollte die
             * Reihe an beiden Enden nicht mit einem Stummel aufhoeren.
             *
             * Der Preis war, dass der Regler nicht mehr galt. Gemessen am
             * 2026-08-24, Rechteck 300 x 105 m, N = 5 eingestellt:
             *
             *     mit Kappen     8 | 8 | 8 | 8 ...
             *     ohne Kappen    6 | 6 | 6 | 6 ...
             *
             * Der Nutzer hat sich fuer das Versprechen entschieden: **die
             * Innenabschnitte tragen exakt N Buchten**, und die Endabschnitte
             * nehmen, was uebrig bleibt. Gewaehlt wird deshalb die GROESSTE
             * Anzahl, die noch hineinpasst - je mehr Querstrassen, desto
             * kleiner der Rest an den Enden.
             *
             * Die Mindestlaenge bleibt: ein Endabschnitt unter zwei Buchten
             * ist genau der Stummel, um den es geht, und wird nie gewaehlt.
             * Und die 5-Buchten-Regel greift danach unveraendert - traegt ein
             * Abschnitt weniger als fuenf Buchten am Stueck, faellt DORT das
             * Stueck Querstrasse weg (`PlaneModule`, `mindestbuchten`).
             */
            var besteAnzahl = 0;
            for (var anzahl = 1; ; anzahl++)
            {
                var belegt = (anzahl - 1) * innen + anzahl * querstrassenbreite;
                if (belegt > laenge) break;
                var ende = (laenge - belegt) / 2;
                // Wie viele Buchten ein Endabschnitt traegt - wortgleich zu
                // FuegeFreienAbschnittHinzu, sonst rechnet die Auswahl mit
                // anderen Zahlen als der Bau danach.
                var buchtenEnde = (int)Math.Floor(
                    (ende - reserveBuchten * buchtbreite) / buchtbreite);
                /*
                 * DIESELBE SCHWELLE WIE DIE PRUEFUNG DANACH.
                 *
                 * Hier stand `< 2`. Ein Endabschnitt mit zwei Buchten galt
                 * damit als brauchbar, wurde gewaehlt - und fiel danach der
                 * Fuenf-Buchten-Regel zum Opfer, zusammen mit ALLEN
                 * Querstrassen der Reihe. Zwei Regeln, zwei Schwellen, und
                 * die eine hat der anderen die Arbeit verdorben.
                 *
                 * Jetzt gilt beidesmal `Mindestbuchten`. Passt keine Anzahl
                 * mehr, bleibt die Reihe ohne Querstrasse - und das ist
                 * richtig so: wer keine will, stellt N einfach hoch genug.
                 * Ansage des Nutzers: *"Wenn z.B. der User die Querstrassen
                 * nicht will, dann wuerde er einfach N so hoch machen, dass
                 * er keine bekommt."*
                 */
                if (buchtenEnde < Math.Min(5, buchtenInnen)) continue;
                besteAnzahl = anzahl;
            }
            if (besteAnzahl == 0) return mitten;

            var belegtGewaehlt = (besteAnzahl - 1) * innen
                + besteAnzahl * querstrassenbreite;
            var endabschnitt = (laenge - belegtGewaehlt) / 2;
            var cursor = minX + endabschnitt + querstrassenbreite / 2;
            for (var i = 0; i < besteAnzahl; i++)
            {
                mitten.Add(cursor);
                cursor += innen + querstrassenbreite;
            }
            return mitten;
        }

        internal static IReadOnlyList<Querstrassenplan> GlobaleQuerstrassen(
            double minX,
            double maxX,
            double buchtbreite,
            double querstrassenbreite,
            double querstrassenabstand,
            bool kappen,
            Linienregister linienregister)
        {
            var querstrassen = new List<Querstrassenplan>();
            foreach (var mitte in Querstrassenmitten(
                         minX, maxX, buchtbreite,
                         querstrassenbreite, querstrassenabstand, kappen))
            {
                var id = querstrassen.Count;
                var anfang = mitte - querstrassenbreite / 2;
                var ende = mitte + querstrassenbreite / 2;
                var links = linienregister.Querstrassenkante(anfang, id, "links");
                var rechts = linienregister.Querstrassenkante(ende, id, "rechts");
                querstrassen.Add(new Querstrassenplan(
                    id, mitte, anfang, ende, links, rechts));
            }
            return querstrassen;
        }

        private static Spaltenplan Spalten(
            double minX,
            double maxX,
            double buchtbreite,
            IReadOnlyList<Querstrassenplan> kandidaten,
            bool kappen,
            ref int naechsteSpaltenId)
        {
            var querstrassen = kandidaten
                .Where(strasse => strasse.Anfang > minX
                    && strasse.Ende < maxX)
                .OrderBy(strasse => strasse.Mitte)
                .ToList();
            var spalten = new List<Spaltenabschnitt>();
            var spaltenId = naechsteSpaltenId;

            void FuegeFreienAbschnittHinzu(double anfang, double ende)
            {
                var laenge = ende - anfang;
                // Ohne Kappen genuegt eine einzige Bucht, damit der Abschnitt
                // ueberhaupt eine Reihe traegt.
                if (laenge < (kappen ? 2 : 1) * buchtbreite)
                {
                    spalten.Add(new Spaltenabschnitt(
                        spaltenId++, anfang, ende,
                        Spaltenart.Kappenrest));
                    return;
                }

                /**
                  * HIER WIRD DIE KAPPE RESERVIERT - oder eben nicht.
                  *
                  * MIT Kappen endet jede Reihe auf beiden Seiten mit einer
                  * echten Kappe: es werden zwei volle Buchtbreiten abgezogen,
                  * bevor gezaehlt wird, und der Rest haelftig auf beide Enden
                  * verteilt. Bei 3,0 m Buchtbreite ist die Kappe damit 3,0 bis
                  * unter 4,5 m breit. Das ist die optische Trennung zwischen
                  * Buchtreihe und Querstrasse - im echten Leben verhindert sie,
                  * dass jemand von der Querstrasse direkt in eine Bucht
                  * abbiegt, statt erst in die Fahrgasse einzubiegen. In CS2
                  * faehrt niemand quer; dort ist es reine Optik.
                  *
                  * OHNE Kappen faellt der Abzug weg. Es passen so viele Buchten
                  * hinein wie ueberhaupt moeglich, und was uebrig bleibt, ist
                  * weniger als eine Buchtbreite statt einer ganzen. Zusammen
                  * mit der Rollenwahl in `Layout.cs` bekommt dieser Rest die
                  * Rolle seines Bandes: kein Gras, kein eigener Belagstreifen,
                  * sondern Teil der Reihe. Die Buchten liegen dann direkt an
                  * der Querstrasse.
                  *
                  * Nutzeransage am 2026-08-24: *"Das Gruen von den Kappen wird
                  * aus der Rechnung rausgenommen, wodurch diese optische
                  * Trennung wegfaellt und die Parkbuchten direkt an der
                  * Querstrasse liegen, ohne Gras oder Asphalt dazwischen."*
                  *
                  * Gemessen bei L gross: der alte 0,200-m-Rest ergab eine
                  * fertige 0,200-m-Kante; mit zwei Kappen liegt das Minimum
                  * bei 1,000 m. Ohne Kappen kann der Rest also wieder klein
                  * werden - CS2s Annahmekriterium prueft das Ergebnis, siehe
                  * `Cs2Triangulierung`.
                  */
                var reserve = kappen ? 2 * buchtbreite : 0.0;
                var buchten = (int)Math.Floor(
                    (laenge - reserve) / buchtbreite);
                var kappenlaenge = (laenge - buchten * buchtbreite) / 2;
                var cursor = anfang + kappenlaenge;
                spalten.Add(new Spaltenabschnitt(
                    spaltenId++, anfang, cursor,
                    Spaltenart.Kappenrest));
                for (var bucht = 0; bucht < buchten; bucht++)
                {
                    var naechstes = cursor + buchtbreite;
                    spalten.Add(new Spaltenabschnitt(
                        spaltenId++, cursor, naechstes,
                        Spaltenart.Buchtfeld));
                    cursor = naechstes;
                }
                spalten.Add(new Spaltenabschnitt(
                    spaltenId++, cursor, ende,
                    Spaltenart.Kappenrest));
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
            naechsteSpaltenId = spaltenId;

            return new Spaltenplan
            {
                Spalten = spalten,
                Querstrassen = querstrassen,
            };
        }

        internal static Modulplanung PlaneModule(
            Bandplan bandplan,
            IReadOnlyList<Querstrassenplan> globaleQuerstrassen,
            IReadOnlyList<Punkt> innenrand,
            IReadOnlyList<Punkt> randstrassenmittellinie,
            double buchtbreite,
            double mindestkantenabstand,
            bool kappen,
            double querstrassenabstand = 34,
            bool randstrassen = true, Ringlosplan ringlos = null)
        {
            var mindestbuchten = Restmindestbuchten(querstrassenabstand, buchtbreite, kappen);
            var modulplaene = new List<Modulspaltenplan>();
            var pruefungen = new List<Querstrassenpruefung>();
            var vorplanSpaltenId = 0;

            foreach (var modul in bandplan.Module)
            {
                // Die Querstrassenmitten bleiben aus dem globalen Raster. Nur
                // Kappen und Buchtphase kommen aus der Ausdehnung dieses
                // Bucht/Fahrgasse/Bucht-Moduls; so fluchten die Strassen trotz
                // Treppenform, ohne dass die kuerzeren Reihen das lange Raster
                // erben.
                var ausdehnung = HorizontaleAusdehnung(
                    innenrand, modul.Anfang, modul.Ende);
                if (ringlos != null) ausdehnung = (Math.Max(ausdehnung.Min, ringlos.Links), Math.Min(ausdehnung.Max, ringlos.Rechts));
                var vorplan = Spalten(
                    ausdehnung.Min, ausdehnung.Max, buchtbreite,
                    globaleQuerstrassen, kappen,
                    ref vorplanSpaltenId);
                var ueberlebende = new List<int>();
                foreach (var querstrasse in vorplan.Querstrassen)
                {
                    var pruefung = new Querstrassenpruefung
                    {
                        ModulId = modul.Id,
                        QuerstrassenId = querstrasse.Id,
                        ErsteLinks = ZaehleNachbarbuchten(
                            vorplan, modul.ErsteReihe, querstrasse.Id, true,
                            innenrand, mindestkantenabstand),
                        ErsteRechts = ZaehleNachbarbuchten(
                            vorplan, modul.ErsteReihe, querstrasse.Id, false,
                            innenrand, mindestkantenabstand),
                        ZweiteLinks = ZaehleNachbarbuchten(
                            vorplan, modul.ZweiteReihe, querstrasse.Id, true,
                            innenrand, mindestkantenabstand),
                        ZweiteRechts = ZaehleNachbarbuchten(
                            vorplan, modul.ZweiteReihe, querstrasse.Id, false,
                            innenrand, mindestkantenabstand),
                    };
                    /*
                     * EINE REIHE, DIE ES HIER GAR NICHT GIBT, HAT KEIN VETO.
                     *
                     * Bis zum 2026-08-26 stand hier `Minimum >= mindestbuchten`
                     * ueber alle vier Zahlen. Am Nutzerfall gemessen
                     * (`--querfall`, L-Form mit 1048 Buchten):
                     *
                     *   Querstrasse 0/1/2  Modul 4  erste 0/0  zweite 9/9  WEG
                     *
                     * Modul 4 liegt auf der Kante des L-Schenkels: seine zweite
                     * Reihe reicht hinein, die erste liegt dort ausserhalb des
                     * Polygons. Die Pruefung verlangte auch von dieser gar nicht
                     * vorhandenen Reihe fuenf Buchten - und warf die Querstrasse
                     * weg. Von den drei verworfenen scheiterte KEINE knapp; eine
                     * Lockerung der Schwelle haette also nichts geholfen.
                     *
                     * `0/0` heisst: die Reihe traegt hier auf beiden Seiten
                     * nichts, es gibt sie nicht. Nur wenn eine Reihe wirklich da
                     * ist, kann an ihr ein Stummel entstehen - und nur dann ist
                     * die Schwelle sinnvoll. Eine einseitige Null (`0/7`) bleibt
                     * dagegen ein echter Befund und veto-faehig.
                     */
                    var vorhandeneReihen = new List<int>();
                    if (pruefung.ErsteLinks > 0 || pruefung.ErsteRechts > 0)
                    {
                        vorhandeneReihen.Add(pruefung.ErsteLinks);
                        vorhandeneReihen.Add(pruefung.ErsteRechts);
                    }
                    if (pruefung.ZweiteLinks > 0 || pruefung.ZweiteRechts > 0)
                    {
                        vorhandeneReihen.Add(pruefung.ZweiteLinks);
                        vorhandeneReihen.Add(pruefung.ZweiteRechts);
                    }
                    pruefung.Bleibt = querstrasse.Notwendig || (vorhandeneReihen.Count > 0
                        && vorhandeneReihen.Min() >= mindestbuchten);
                    pruefungen.Add(pruefung);
                    if (pruefung.Bleibt)
                        ueberlebende.Add(querstrasse.Id);
                }
                modulplaene.Add(new Modulspaltenplan
                {
                    Modul = modul,
                    MinX = ausdehnung.Min,
                    MaxX = ausdehnung.Max,
                    UeberlebendeQuerstrassen = ueberlebende,
                    Reihenplaene = new Dictionary<int, Spaltenplan>(),
                });
            }

            // Alle Entscheidungen stammen aus demselben Vorplan. Ein Wegfall
            // verlaengert die Nachbarabschnitte nur; eine iterative
            // Schwellenpruefung wuerde deshalb keine weitere Strasse entfernen.
            var stuecke = ringlos != null ? ringlos.Querwege : BaueAnschlusssichereQuerstrassen(
                globaleQuerstrassen, modulplaene,
                randstrassenmittellinie, randstrassen);
            var naechsteSpaltenId = 0;
            foreach (var modulplan in modulplaene)
            {
                var reihenplaene = new Dictionary<int, Spaltenplan>();
                foreach (var reihe in modulplan.Modul.Reihen)
                {
                    var y = (reihe.Anfang + reihe.Ende) / 2;
                    var aktive = globaleQuerstrassen
                        .Where(strasse => stuecke.Any(stueck =>
                            stueck.Querstrasse.Id == strasse.Id
                                && stueck.Enthaelt(strasse.Mitte, y)))
                        .ToArray();
                    reihenplaene.Add(reihe.Id, Spalten(
                        modulplan.MinX, modulplan.MaxX, buchtbreite,
                        aktive, kappen, ref naechsteSpaltenId));
                }
                modulplan.Reihenplaene = reihenplaene;
            }

            return new Modulplanung
            {
                Plaene = modulplaene,
                Pruefungen = pruefungen,
                Querstrassenstuecke = stuecke,
            };
        }

        private static int ZaehleNachbarbuchten(
            Spaltenplan plan,
            Bandabschnitt reihe,
            int querstrassenId,
            bool links,
            IReadOnlyList<Punkt> innenrand,
            double mindestkantenabstand)
        {
            // Einseitige Module haben eine leere Gegenreihe. Deren vier
            // nominelle Ecken bilden nur eine Linie, keine Nachbarbuchten.
            if (reihe.Ende <= reihe.Anfang + 1e-6) return 0;
            var index = plan.Spalten.ToList().FindIndex(spalte =>
                spalte.Art == Spaltenart.Querstrasse
                    && spalte.QuerstrassenId == querstrassenId);
            if (index < 0) return 0;
            var schritt = links ? -1 : 1;
            var anzahl = 0;
            var buchtfelderErreicht = false;
            for (var i = index + schritt;
                 i >= 0 && i < plan.Spalten.Count;
                 i += schritt)
            {
                var spalte = plan.Spalten[i];
                if (spalte.Art == Spaltenart.Querstrasse) break;
                if (spalte.Art != Spaltenart.Buchtfeld)
                {
                    if (buchtfelderErreicht) break;
                    continue;
                }
                buchtfelderErreicht = true;
                // Eine Treppenkante kann denselben geometrischen Abschnitt in
                // mehrere Buchtinseln zerlegen. Gezaehlt wird die Folge DIREKT
                // neben der Querstrasse; Buchten hinter der ersten Luecke
                // duerfen eine einsame Vordergruppe nicht grossrechnen.
                if (!IstGueltigeBucht(
                        reihe, spalte, innenrand, mindestkantenabstand))
                    break;
                anzahl++;
            }
            return anzahl;
        }

        private static IReadOnlyList<Querstrassenstueck>
            BaueAnschlusssichereQuerstrassen(
                IReadOnlyList<Querstrassenplan> querstrassen,
                IReadOnlyList<Modulspaltenplan> modulplaene,
                IReadOnlyList<Punkt> randstrassenmittellinie, bool randstrassen)
        {
            // Ein Netzstueck wird nur zwischen befahrbaren Knoten gebaut:
            // Randstrasse -> Fahrgasse, Fahrgasse -> Fahrgasse oder
            // Fahrgasse -> Randstrasse. Ein einzelnes ueberlebendes Modul
            // zwischen zwei weggefallenen Nachbarn erzeugt daher bewusst kein
            // Stueck, das beidseits im Gruen enden wuerde.
            var ausgabe = new List<Querstrassenstueck>();
            foreach (var querstrasse in querstrassen)
                foreach (var abschnitt in SenkrechteAbschnitte(
                    randstrassenmittellinie, querstrasse.Mitte))
                {
                    var module = modulplaene
                        .Where(plan => plan.Modul.Fahrgassenmitte
                            > abschnitt.Anfang + 1e-6
                            && plan.Modul.Fahrgassenmitte
                            < abschnitt.Ende - 1e-6)
                        .OrderBy(plan => plan.Modul.Fahrgassenmitte)
                        .ToArray();
                    if (module.Length == 0) continue;

                    bool Bleibt(Modulspaltenplan plan) =>
                        plan.UeberlebendeQuerstrassen.Contains(
                            querstrasse.Id);

                    /*
                     * DER KONFLIKTPUNKT SITZT AN DER ECKE, NICHT AN DER ZAHL
                     * DER BUCHTEN.
                     *
                     * Ansage des Nutzers am 2026-08-26, nachdem er sechs
                     * Stellen markiert hatte: *"Es geht darum, wo
                     * Konfliktpunkte entstehen. Markierung 2 ist sehr eng an
                     * der Ecke, wohingegen 4 und 6 einfach an der geraden
                     * Randstrasse liegen."*
                     *
                     * Nachgemessen an seinem Bau, Abstand des Anschlusses zur
                     * naechsten Ecke der Randstrasse:
                     *
                     *     Markierung 2 (richtig weggelassen)   13,0 m
                     *     Markierung 4                         29,5 m
                     *     Markierung 6                         47,9 m
                     *
                     * Dazwischen liegt eine klare Luecke. Als Mass dient EINE
                     * MODULBREITE (Bucht + Fahrgasse + Bucht, hier 18,8 m) -
                     * keine ausgedachte Zahl, sondern die Breite, die eine
                     * Querstrasse zum Einfaedeln braucht, und sie waechst mit
                     * den Reglern mit.
                     *
                     * Betroffen ist nur der Anschluss AN DIE RANDSTRASSE. Ein
                     * Stueck zwischen zwei Fahrgassen hat keinen solchen
                     * Konfliktpunkt.
                     */
                    bool AnschlussFreiVonEcke(double laengs,
                                              Modulspaltenplan plan)
                    {
                        var modulbreite = plan.Modul.Ende - plan.Modul.Anfang;
                        if (modulbreite <= 0) return true;
                        return AbstandZurNaechstenEcke(
                                   randstrassenmittellinie,
                                   new Punkt(querstrasse.Mitte, laengs))
                               >= modulbreite;
                    }

                    if (randstrassen && Bleibt(module[0])
                        && AnschlussFreiVonEcke(abschnitt.Anfang, module[0]))
                        FuegeQuerstrassenstueckHinzu(
                            ausgabe, querstrasse, abschnitt.Anfang,
                            module[0].Modul.Fahrgassenmitte);
                    for (var i = 0; i + 1 < module.Length; i++)
                        if (Bleibt(module[i]) && Bleibt(module[i + 1]))
                            FuegeQuerstrassenstueckHinzu(
                                ausgabe, querstrasse,
                                module[i].Modul.Fahrgassenmitte,
                                module[i + 1].Modul.Fahrgassenmitte);
                    if (randstrassen && Bleibt(module[module.Length - 1])
                        && AnschlussFreiVonEcke(abschnitt.Ende,
                                                module[module.Length - 1]))
                        FuegeQuerstrassenstueckHinzu(
                            ausgabe, querstrasse,
                            module[module.Length - 1].Modul.Fahrgassenmitte,
                            abschnitt.Ende);
                }
            return ausgabe;
        }

        /**
         * Abstand zur naechsten ECHTEN Ecke der Randstrasse.
         *
         * Nicht jeder Punkt der Mittellinie ist eine Ecke - der Ring traegt auf
         * geraden Stuecken zusaetzliche Stuetzpunkte. Als Ecke zaehlt deshalb
         * nur ein Punkt, an dem die Richtung wirklich umspringt. Ohne diese
         * Schranke laege jeder Anschluss "an einer Ecke", und keine
         * Querstrasse erreichte je die Randstrasse.
         *
         * 20 Grad ist bewusst grosszuegig: eine abgerundete oder in vielen
         * kleinen Schritten gezeichnete Kante soll nicht als Ecke gelten, eine
         * echte Richtungsaenderung dagegen schon.
         */
        private static double AbstandZurNaechstenEcke(
            IReadOnlyList<Punkt> ring, Punkt punkt)
        {
            if (ring == null || ring.Count < 3) return double.PositiveInfinity;
            const double mindestwinkelGrad = 20.0;
            var kleinster = double.PositiveInfinity;
            for (var i = 0; i < ring.Count; i++)
            {
                var vor = ring[(i - 1 + ring.Count) % ring.Count];
                var hier = ring[i];
                var nach = ring[(i + 1) % ring.Count];
                var einX = hier.X - vor.X;
                var einY = hier.Y - vor.Y;
                var ausX = nach.X - hier.X;
                var ausY = nach.Y - hier.Y;
                var laengeEin = Math.Sqrt(einX * einX + einY * einY);
                var laengeAus = Math.Sqrt(ausX * ausX + ausY * ausY);
                if (laengeEin <= 1e-9 || laengeAus <= 1e-9) continue;
                var kosinus = (einX * ausX + einY * ausY)
                              / (laengeEin * laengeAus);
                kosinus = Math.Max(-1.0, Math.Min(1.0, kosinus));
                if (Math.Acos(kosinus) * 180.0 / Math.PI < mindestwinkelGrad)
                    continue;
                var dx = punkt.X - hier.X;
                var dy = punkt.Y - hier.Y;
                kleinster = Math.Min(kleinster, Math.Sqrt(dx * dx + dy * dy));
            }
            return kleinster;
        }

        private static void FuegeQuerstrassenstueckHinzu(
            List<Querstrassenstueck> ausgabe,
            Querstrassenplan querstrasse,
            double anfang,
            double ende)
        {
            if (ende <= anfang + 1e-6) return;
            var letztes = ausgabe.LastOrDefault();
            if (letztes != null
                && letztes.Querstrasse.Id == querstrasse.Id
                && Math.Abs(letztes.Ende.Y - anfang) <= 1e-6)
            {
                letztes.Ende = new Punkt(querstrasse.Mitte, ende);
                return;
            }
            ausgabe.Add(new Querstrassenstueck
            {
                Querstrasse = querstrasse,
                Anfang = new Punkt(querstrasse.Mitte, anfang),
                Ende = new Punkt(querstrasse.Mitte, ende),
            });
        }

        private static IReadOnlyList<(double Anfang, double Ende)>
            SenkrechteAbschnitte(IReadOnlyList<Punkt> ring, double x)
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
                    "The module plan found an odd number of vertical boundary crossings.");
            var ausgabe = new List<(double, double)>();
            for (var i = 0; i < schnitte.Count; i += 2)
                if (schnitte[i + 1] > schnitte[i])
                    ausgabe.Add((schnitte[i], schnitte[i + 1]));
            return ausgabe;
        }

        private static (double Min, double Max) HorizontaleAusdehnung(
            IReadOnlyList<Punkt> ring,
            double minY,
            double maxY)
        {
            var x = new List<double>();
            foreach (var punkt in ring)
                if (punkt.Y >= minY - 1e-6 && punkt.Y <= maxY + 1e-6)
                    x.Add(punkt.X);
            for (var i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];
                foreach (var y in new[] { minY, maxY })
                {
                    if ((a.Y < y && b.Y < y) || (a.Y > y && b.Y > y)
                        || a.Y == b.Y) continue;
                    var t = (y - a.Y) / (b.Y - a.Y);
                    if (t >= 0 && t <= 1)
                        x.Add(a.X + (b.X - a.X) * t);
                }
            }
            if (x.Count == 0)
                throw new InvalidOperationException(
                    "A parking module does not intersect the inner contour.");
            return (x.Min(), x.Max());
        }

        internal static Dictionary<(int Band, int Spalte), int> GueltigeBuchten(
            IReadOnlyList<Modulspaltenplan> modulplaene,
            IReadOnlyList<Punkt> innenrand,
            double mindestkantenabstand,
            bool protokollieren = true,
            IReadOnlyList<Zoningvorgabe> bauland = null,
            IReadOnlyList<Punkt> abstandsrand = null)
        {
            var ausgabe = new Dictionary<(int, int), int>();
            /*
             * ZAEHLER AN JEDER VERWERFUNGSSTELLE.
             *
             * Der Nutzer meldete am 2026-09-01 fehlende Buchten an den
             * Fahrgassen und tippte auf "etwas Banales wie einen
             * Rundungsfehler". Statt zu raten, was `IstGueltigeBucht`
             * ablehnt, sagt sie es jetzt selbst - aber nur, wenn jemand
             * mitliest (Live-Log). Im Normalbetrieb kostet das einen
             * Nullvergleich.
             */
            var geprueft = 0;
            var draussen = 0;
            var zuNah = 0;
            var eckeDrin = 0;
            var engste = double.PositiveInfinity;
            var knapp5 = 0;
            var knapp25 = 0;
            var knapp100 = 0;
            var weit = 0;
            foreach (var modulplan in modulplaene)
                foreach (var paar in modulplan.Reihenplaene)
                {
                    var band = modulplan.Modul.Reihen.First(
                        reihe => reihe.Id == paar.Key);
                    foreach (var spalte in paar.Value.Spalten.Where(
                                 spalte => spalte.Art == Spaltenart.Buchtfeld))
                    {
                        geprueft++;
                        if (ParkingGeometry.LiveAn && protokollieren)
                        {
                            var ecken = new[]
                            {
                                new Punkt(spalte.Anfang, band.Anfang),
                                new Punkt(spalte.Ende, band.Anfang),
                                new Punkt(spalte.Ende, band.Ende),
                                new Punkt(spalte.Anfang, band.Ende),
                            };
                            if (!ecken.All(ecke =>
                                Geometrie.EnthaeltOderRand(innenrand, ecke)))
                            {
                                draussen++;
                                // WIE WEIT draussen? Zentimeter waeren ein
                                // Rundungsfehler, Meter echte Geometrie.
                                var raus = ecken
                                    .Where(ecke => !Geometrie.EnthaeltOderRand(
                                        innenrand, ecke))
                                    .Max(ecke => Mindestabstand(ecke, innenrand));
                                if (raus <= 0.05) knapp5++;
                                else if (raus <= 0.25) knapp25++;
                                else if (raus <= 1.0) knapp100++;
                                else weit++;
                            }
                            else
                            {
                                var abstand = ecken.Min(ecke =>
                                    Mindestabstand(ecke, abstandsrand ?? innenrand));
                                if (abstand < mindestkantenabstand - 1e-6)
                                {
                                    zuNah++;
                                    if (abstand < engste) engste = abstand;
                                }
                                else if (innenrand.Any(punkt =>
                                    punkt.X > spalte.Anfang
                                    && punkt.X < spalte.Ende
                                    && punkt.Y > band.Anfang
                                    && punkt.Y < band.Ende))
                                    eckeDrin++;
                            }
                        }
                        if (IstGueltigeBucht(
                            band, spalte, innenrand, mindestkantenabstand,
                            bauland, abstandsrand))
                            ausgabe.Add((band.Id, spalte.Id), ausgabe.Count);
                    }
                }
            if (ParkingGeometry.LiveAn && protokollieren)
                ParkingGeometry.Live("  buchtpruefung " + geprueft
                    + " geprueft, " + ausgabe.Count + " gueltig"
                    + " | draussen " + draussen
                    + " | zu nah am Rand " + zuNah
                    + " (engste " + (double.IsPositiveInfinity(engste)
                        ? "-" : engste.ToString("F3",
                            System.Globalization.CultureInfo.InvariantCulture))
                    + " m, Grenze " + mindestkantenabstand.ToString("F3",
                        System.Globalization.CultureInfo.InvariantCulture)
                    + ") | Randecke in der Bucht " + eckeDrin
                    + " | draussen um bis 5 cm: " + knapp5
                    + ", bis 25 cm: " + knapp25
                    + ", bis 1 m: " + knapp100
                    + ", mehr: " + weit);
            return ausgabe;
        }

        /**
         * Beruehrt dieses Buchtrechteck eine Baulandflaeche?
         *
         * Beide liegen im selben Rahmen, das Bauland darf darin gedreht
         * sein. Geprueft wird in drei Stufen, weil zwei Rechtecke sich auf
         * drei Arten treffen koennen: eine Ecke des einen liegt im anderen,
         * umgekehrt, oder die Kanten kreuzen sich ohne dass eine Ecke drin
         * liegt (Kreuzform).
         */
        /** Kreuzen sich zwei Strecken? Vorzeichen der vier Seitenlagen. */
        private static bool StreckenKreuzen(Punkt a1, Punkt a2, Punkt b1, Punkt b2)
        {
            double Seite(Punkt p, Punkt q, Punkt r)
                => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
            var d1 = Seite(a1, a2, b1);
            var d2 = Seite(a1, a2, b2);
            var d3 = Seite(b1, b2, a1);
            var d4 = Seite(b1, b2, a2);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
                && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        /**
         * LIEGT DIE BUCHT IM WEG DER RANDZONING-STRASSE?
         *
         * Loesungsvorschlag des Nutzers am 2026-09-05, und er ist die
         * richtige Ebene: *"Die Parkbucht dort erst gar nicht so weit kommen
         * lassen, dass sie ueber die Richtung der RZ-Strasse kommt."*
         *
         * Nicht den Aufkleber verschieben, sondern die Bucht nicht anlegen.
         * Ein Aufkleber, der auf der Fahrbahn liegt, ueberlappt sie - und ein
         * ueberlapptes Objekt ist in Vanilla-CS2 nicht mehr zu loeschen. Er
         * hat es selbst nachgewiesen, indem er die Decals abschaltete: die
         * rote Umrandung verschwand.
         *
         * Betroffen ist nicht der RZ-Abschnitt selbst - dort entfallen die
         * Buchten ohnehin -, sondern der UEBERGANG: die letzte Bucht der
         * Nachbarkante ragt in den Fahrbahnkorridor hinein.
         *
         * Der Korridor ist die halbe Fahrbahnbreite beiderseits der Achse,
         * und er wird an beiden Enden um dieselbe halbe Breite verlaengert -
         * sonst bliebe genau an der Ecke ein Zipfel stehen.
         */
        internal static bool BeruehrtRandzoningstrasse(
            Punkt[] ecken,
            IReadOnlyList<(Punkt A, Punkt B)> randzoning,
            double achstiefe,
            double halbebreite)
        {
            if (randzoning == null || randzoning.Count == 0) return false;
            foreach (var linie in randzoning)
            {
                var d = linie.B - linie.A;
                var laenge = Geometrie.Laenge(d);
                if (laenge < 1e-6) continue;
                var r = d * (1.0 / laenge);
                var n = new Punkt(-r.Y, r.X);
                // Nach innen zeigen: die Buchten liegen innen, also muss die
                // Achse in ihre Richtung versetzt werden.
                var mitte = new Punkt(0, 0);
                foreach (var ecke in ecken) mitte += ecke;
                mitte = mitte * (1.0 / ecken.Length);
                if (Geometrie.Skalar(mitte - linie.A, n) < 0) n = new Punkt(-n.X, -n.Y);

                var a = linie.A + n * achstiefe - r * halbebreite;
                var b = linie.B + n * achstiefe + r * halbebreite;
                var korridor = new[]
                {
                    a - n * halbebreite,
                    b - n * halbebreite,
                    b + n * halbebreite,
                    a + n * halbebreite,
                };
                foreach (var ecke in ecken)
                    if (Geometrie.EnthaeltOderRand(korridor, ecke)) return true;
                foreach (var ecke in korridor)
                    if (Geometrie.EnthaeltOderRand(ecken, ecke)) return true;
                for (var i = 0; i < ecken.Length; i++)
                for (var k = 0; k < korridor.Length; k++)
                    if (StreckenKreuzen(
                            ecken[i], ecken[(i + 1) % ecken.Length],
                            korridor[k], korridor[(k + 1) % korridor.Length]))
                        return true;
            }
            return false;
        }

        internal static bool BeruehrtBauland(
            Punkt[] ecken, IReadOnlyList<Zoningvorgabe> bauland)
        {
            if (bauland == null || bauland.Count == 0) return false;
            foreach (var flaeche in bauland)
            {
                foreach (var ecke in ecken)
                    if (flaeche.Enthaelt(ecke) || flaeche.ImAussenband(ecke)) return true;
                var baulandecken = flaeche.Umriss();
                foreach (var ecke in baulandecken)
                    if (Geometrie.EnthaeltOderRand(ecken, ecke)) return true;
                for (var i = 0; i < ecken.Length; i++)
                for (var k = 0; k < baulandecken.Length; k++)
                    if (StreckenKreuzen(
                            ecken[i], ecken[(i + 1) % ecken.Length],
                            baulandecken[k],
                            baulandecken[(k + 1) % baulandecken.Length]))
                        return true;
            }
            return false;
        }

        private static bool IstGueltigeBucht(
            Bandabschnitt band,
            Spaltenabschnitt spalte,
            IReadOnlyList<Punkt> innenrand,
            double mindestkantenabstand,
            IReadOnlyList<Zoningvorgabe> bauland = null,
            IReadOnlyList<Punkt> abstandsrand = null)
        {
            var ecken = new[]
            {
                new Punkt(spalte.Anfang, band.Anfang),
                new Punkt(spalte.Ende, band.Anfang),
                new Punkt(spalte.Ende, band.Ende),
                new Punkt(spalte.Anfang, band.Ende),
            };
            // Mit Kappen reichte vollstaendig innen an der schraegen Kante
            // nicht: Dort blieben gemessen 0,084 m bis zum Rand. Die belegte
            // CS2-Kantengrenze 0,375 m kostet genau eine Bucht; danach misst
            // L schraeg 0,514 m als engste fertige Stelle. Ohne Kappen gibt
            // es an diesem Rest keine eigene Materialzelle; der Aufrufer
            // uebergibt dann deshalb 0,0 m.
            // KEINE BUCHT AUF BAULAND. Nicht hinterher herausfiltern: eine
            // Bucht, die es nie gab, kann auch keinen Aufkleber bekommen und
            // keine Fahrgasse rechtfertigen.
            if (BeruehrtBauland(ecken, bauland)) return false;
            return ecken.All(ecke => Geometrie.EnthaeltOderRand(innenrand, ecke))
                && ecken.All(ecke =>
                    Mindestabstand(ecke, abstandsrand ?? innenrand) >= mindestkantenabstand - 1e-6)
                && !innenrand.Any(punkt =>
                    punkt.X > spalte.Anfang && punkt.X < spalte.Ende
                    && punkt.Y > band.Anfang && punkt.Y < band.Ende);
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
}
