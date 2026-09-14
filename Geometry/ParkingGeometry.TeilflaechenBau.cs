using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static partial class ParkingGeometry
    {
        /**
         * JEDES TEILSTUECK ALS EIGENER PARKPLATZ.
         *
         * Vorschlag des Nutzers vom 2026-09-01, nachdem sich das
         * Teilflaechenraster als die Ursache der fehlenden Buchten erwiesen
         * hatte: *"Wenn wir jedes Teilstueck als einen eigenen Parkplatz
         * berechnen. Wir haben eine ordentliche Trennung durch die Strasse
         * zwischen den verbundenen Punkten."*
         *
         * WARUM ES BESSER IST, und das ist gemessen, nicht vermutet: jedes
         * Teil bekommt seine eigene RANDREIHE rundherum, auch entlang des
         * Schnitts. Die Randreihe folgt der Form genau - sie hat das
         * Rasterproblem gar nicht, an dem der bisherige Weg scheitert (das
         * Bandraster wird ueber den Huellkasten des gedrehten Teils gelegt,
         * und der ist nur zu 30 bis 61 Prozent nutzbar).
         *
         * Gemessen an der gemeldeten Form, ueber alle sieben moeglichen
         * Schnitte, Buchten alt -> neu:
         *
         *     Schnitt 0-3   281 -> 378        Schnitt 1-3   370 -> 375
         *     Schnitt 1-5   298 -> 369        Schnitt 2-4   384 -> 376
         *     Schnitt 0-4   331 -> 369        Schnitt 2-5   368 -> 359
         *     Schnitt 1-4   337 -> 366   (der Schnitt des Nutzers)
         *
         * Es gewinnt am meisten dort, wo der alte Weg am schlechtesten war,
         * und verliert an zwei Schnitten leicht - der Preis fuer die zweite
         * Randstrasse. Die Randbuchten steigen dabei von 144 auf 188 bis 232;
         * daher kommt der Gewinn.
         *
         * ES BLEIBT EIN PARKPLATZ. Nur die Geometrie wird je Teil gerechnet
         * und danach zusammengelegt: ein Besitzer, eine Wirtschaft, ein
         * Bauzettel.
         */
        private static ParkingLayout BuildTeilflaechenEinzeln(
            float2[] site,
            LayoutSettings settings,
            IReadOnlyList<TeilflaechenPlan> teilplan)
        {
            var layouts = new List<ParkingLayout>();
            var warnungen = new List<string>();
            var quellumriss = site
                .Select(p => new double2(p.x, p.y)).ToArray();
            for (var i = 0; i < teilplan.Count; i++)
            {
                var plan = teilplan[i];
                var teilSettings = settings.Clone();
                // Der Winkel steht schon fest; die Teilflaechenangaben duerfen
                // nicht mitwandern, sonst zerlegt sich jedes Teil erneut.
                teilSettings.Ausrichtwinkel = plan.Winkel;
                teilSettings.AngleMode = "edge";
                teilSettings.TeilflaechenAusrichtungen =
                    Array.Empty<TeilflaechenAusrichtung>();
                teilSettings.Teilflaechenschnitte =
                    Array.Empty<Teilflaechenschnitt>();
                teilSettings.Entrances = ZugaengeFuerTeil(
                    settings.Entrances, site, plan.Polygon);
                teilSettings.AutomaticEntrances = false;

                /*
                 * DAS TEILSTUECK ENDET VOR DEM SCHNITT, nicht auf ihm.
                 *
                 * Plan des Nutzers: zwischen den beiden Teilen bleibt ein
                 * Spalt von einer Fahrgassenbreite, und darin liegt EINE
                 * gemeinsame Verbindungsstrasse. Jedes Teil ist damit ein
                 * ganz gewoehnlicher Parkplatz auf einem etwas kleineren
                 * Umriss - der Zellenweg muss nichts Neues koennen.
                 *
                 * Der Versuch davor ging anders herum: die Schnittkante
                 * behielt ihre Lage, und das Randstrassenband wurde auf
                 * Breite null gesetzt. Das brach die Haelfte der Formen (bis
                 * 34,6 Prozent unbebaut). Eine Strasse der Breite null ist
                 * fuer den Rechenweg keine fehlende Strasse.
                 */
                var zurueck = ZurueckgesetztesTeil(
                    plan.Polygon, quellumriss, settings.Ai / 2);
                if (zurueck == null)
                {
                    warnungen.Add("Teilfläche " + plan.Index
                        + " blieb leer: zu schmal für den Trennweg.");
                    continue;
                }
                var polygon = zurueck
                    .Select(p => new float2((float)p.x, (float)p.y)).ToArray();
                try
                {
                    layouts.Add(BuildZellenEinzeln(polygon, teilSettings));
                }
                catch (Exception fehler)
                {
                    /*
                     * EIN ZU KLEINES TEIL DARF DEN BAU NICHT MITREISSEN.
                     *
                     * Gemessen an Schnitt 0-2: ein Zipfel, in den kein
                     * einziges Parkmodul passt, warf "No parking module fits
                     * inside the inner contour" - und damit den ganzen
                     * Parkplatz. Es bleibt einfach leer; der Rest wird
                     * gebaut, und der Nutzer erfaehrt es.
                     */
                    warnungen.Add("Teilfläche " + plan.Index
                        + " blieb leer: " + InnersteMeldung(fehler));
                }
            }

            if (layouts.Count == 0)
                throw new InvalidOperationException(
                    "No sub-area could be built.");

            var vereint = VereineLayouts(layouts, site);
            LegeTrennwege(vereint, settings, site);
            vereint.Teilflaechen = teilplan.Select(plan => new TeilflaechenBauInfo
            {
                Index = plan.Index,
                Winkel = plan.Winkel,
                EigeneZuweisung = plan.EigeneZuweisung,
                Innenbuchten = 0,
            }).ToArray();
            if (warnungen.Count != 0)
                vereint.Warnings = vereint.Warnings.Concat(warnungen).ToArray();
            return vereint;
        }

        /**
         * Das Teilstueck, von seinen Schnittkanten um `abstand` zurueckgesetzt.
         *
         * Nur die SCHNITTKANTEN wandern nach innen; die Kanten, die zum
         * Umriss gehoeren, bleiben, wo sie sind - dort steht der Parkplatz ja
         * weiterhin an seiner eigenen Grenze. Erkannt wird eine Umrisskante
         * an ihrer Mitte: liegt die auf dem Umrissrand, ist es keine
         * Schnittkante.
         *
         * Gibt `null` zurueck, wenn das Teil den Rueckzug nicht ueberlebt -
         * ein schmaler Zipfel kann dabei verschwinden.
         */
        private static double2[] ZurueckgesetztesTeil(
            double2[] teil, double2[] umriss, double abstand)
        {
            if (teil == null || teil.Length < 3) return null;
            var ring = teil.ToList();
            if (SignedArea2(ring) < 0) ring.Reverse();

            var kanten = new (double2 Punkt, double2 Richtung)[ring.Count];
            for (var i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];
                var richtung = b - a;
                var laenge = Len(richtung);
                if (laenge < 1e-9) return null;
                var einheit = richtung / laenge;
                // Links ist innen, weil der Ring gegen den Uhrzeigersinn liegt.
                var normale = new double2(-einheit.y, einheit.x);
                var schnittkante = DistToBoundary((a + b) * 0.5, umriss) > 0.01;
                kanten[i] = (a + normale * (schnittkante ? abstand : 0.0),
                    einheit);
            }

            var neu = new double2[ring.Count];
            for (var i = 0; i < ring.Count; i++)
            {
                var vorher = kanten[(i - 1 + ring.Count) % ring.Count];
                var aktuell = kanten[i];
                var nenner = vorher.Richtung.x * aktuell.Richtung.y
                    - vorher.Richtung.y * aktuell.Richtung.x;
                if (Math.Abs(nenner) < 1e-12)
                {
                    neu[i] = aktuell.Punkt;
                    continue;
                }
                var d = aktuell.Punkt - vorher.Punkt;
                var t = (d.x * aktuell.Richtung.y - d.y * aktuell.Richtung.x)
                    / nenner;
                neu[i] = vorher.Punkt + vorher.Richtung * t;
            }

            // Entartet? Dann lieber gar kein Teil als ein verdrehtes.
            var flaeche = SignedArea2(neu.ToList());
            if (flaeche <= 1e-6) return null;
            if (flaeche > SignedArea2(ring)) return null;
            return neu;
        }

        /**
         * Kuerzt die Strecke auf ihr Stueck innerhalb des Rings.
         *
         * Gesucht sind die beiden Stellen, an denen sie den Ring quert; genau
         * dort sitzt sie danach auf der Randstrasse auf. Findet sie keine
         * zwei, bleibt sie, wie sie war.
         */
        private static bool KuerzeAufRing(
            ref float2 a, ref float2 b, double2[] ring)
        {
            var von = new double2(a.x, a.y);
            var nach = new double2(b.x, b.y);
            var richtung = nach - von;
            var treffer = new List<double>();
            for (var i = 0; i < ring.Length; i++)
            {
                var p = ring[i];
                var q = ring[(i + 1) % ring.Length];
                var kante = q - p;
                var nenner = richtung.x * kante.y - richtung.y * kante.x;
                if (Math.Abs(nenner) < 1e-12) continue;
                var d = p - von;
                var t = (d.x * kante.y - d.y * kante.x) / nenner;
                var u = (d.x * richtung.y - d.y * richtung.x) / nenner;
                if (t < -1e-9 || t > 1 + 1e-9) continue;
                if (u < -1e-9 || u > 1 + 1e-9) continue;
                treffer.Add(t);
            }
            if (treffer.Count < 2) return false;
            treffer.Sort();
            var t0 = treffer[0];
            var t1 = treffer[treffer.Count - 1];
            if (t1 - t0 < 1e-6) return false;
            var neuA = von + richtung * t0;
            var neuB = von + richtung * t1;
            a = new float2((float)neuA.x, (float)neuA.y);
            b = new float2((float)neuB.x, (float)neuB.y);
            return true;
        }

        /** Umriss um `abstand` nach innen, mit Gehrung an den Ecken. */
        private static double2[] Innenversatz(double2[] ring, double abstand)
        {
            var punkte = ring.ToList();
            if (SignedArea2(punkte) < 0) punkte.Reverse();
            var kanten = new (double2 Punkt, double2 Richtung)[punkte.Count];
            for (var i = 0; i < punkte.Count; i++)
            {
                var p = punkte[i];
                var q = punkte[(i + 1) % punkte.Count];
                var r = q - p;
                var laenge = Len(r);
                if (laenge < 1e-9) throw new InvalidOperationException("leer");
                var e = r / laenge;
                kanten[i] = (p + new double2(-e.y, e.x) * abstand, e);
            }
            var aus = new double2[punkte.Count];
            for (var i = 0; i < punkte.Count; i++)
            {
                var vorher = kanten[(i - 1 + punkte.Count) % punkte.Count];
                var aktuell = kanten[i];
                var nenner = vorher.Richtung.x * aktuell.Richtung.y
                    - vorher.Richtung.y * aktuell.Richtung.x;
                if (Math.Abs(nenner) < 1e-12) { aus[i] = aktuell.Punkt; continue; }
                var d = aktuell.Punkt - vorher.Punkt;
                var t = (d.x * aktuell.Richtung.y - d.y * aktuell.Richtung.x)
                    / nenner;
                aus[i] = vorher.Punkt + vorher.Richtung * t;
            }
            return aus;
        }

        private static double SignedArea2(IReadOnlyList<double2> ring)
        {
            var summe = 0.0;
            for (var i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];
                summe += a.x * b.y - b.x * a.y;
            }
            return summe / 2;
        }

        /**
         * DIE GEMEINSAME VERBINDUNGSSTRASSE IM SPALT.
         *
         * Sie liegt mittig auf dem Schnitt, ist so breit wie eine Fahrgasse
         * und gehoert beiden Teilen. Keine zweite Randstrasse - so vom
         * Nutzer vorgegeben.
         */
        private static void LegeTrennwege(
            ParkingLayout vereint, LayoutSettings settings, float2[] site)
        {
            var schnitte = settings.Teilflaechenschnitte
                ?? Array.Empty<Teilflaechenschnitt>();
            if (schnitte.Length == 0) return;

            /*
             * SIE ENDET AUF HOEHE DER RANDSTRASSE, nicht an der Polygonkante.
             *
             * Ansage des Nutzers: *"Die Verbindungsstrasse ist ausserdem zu
             * lang und muesste aussen vom Rand her auf Hoehe der Randstrasse
             * liegen."* Der Schnitt laeuft von Ecke zu Ecke des Umrisses; das
             * aeussere Band gehoert aber der Randstrasse und der Randreihe.
             * Gekuerzt wird deshalb auf das Stueck, das INNERHALB der
             * Randstrassen-Mittellinie liegt - dann sitzen ihre beiden Enden
             * genau auf der Randstrasse, und dort schliesst sie an.
             */
            var mittellinientiefe = settings.Es + settings.Sl
                + settings.Ai / 2;
            double2[] ringmitte = null;
            try
            {
                ringmitte = Innenversatz(
                    site.Select(p => new double2(p.x, p.y)).ToArray(),
                    mittellinientiefe);
            }
            catch
            {
                ringmitte = null;
            }

            var netze = new List<NetSegment>();
            var linien = new List<float2[]>();
            var quads = new List<float2[]>();
            var gruen = new List<float2[]>();
            foreach (var schnitt in schnitte)
            {
                if (schnitt == null) continue;
                var a = schnitt.A;
                var b = schnitt.B;
                /*
                 * DER REST DES SPALTS WIRD GRUEN.
                 *
                 * Die Verbindungsstrasse endet auf Hoehe der Randstrasse; das
                 * Stueck des Spalts davor gehoert keinem der beiden Teile
                 * mehr. Ohne Fuellung bleibt dort blanker Boden - gemessen
                 * 1,0 bis 1,8 Prozent der Flaeche. Es ist ein schmaler
                 * Streifen zwischen zwei Parkplaetzen; Gruen ist dafuer das
                 * Richtige.
                 */
                var ganzA = a;
                var ganzB = b;
                if (ringmitte != null && KuerzeAufRing(ref a, ref b, ringmitte))
                {
                    FuelleSpaltende(gruen, ganzA, a, settings.Ai);
                    FuelleSpaltende(gruen, b, ganzB, settings.Ai);
                }
                var richtung = new double2(b.x - a.x, b.y - a.y);
                var laenge = Len(richtung);
                if (laenge < 1e-6) continue;
                netze.Add(new NetSegment("aisle", a, b));
                linien.Add(new[] { a, b });
                var quer = new double2(-richtung.y, richtung.x) / laenge
                    * (settings.Ai / 2);
                var versatz = new float2((float)quer.x, (float)quer.y);
                quads.Add(new[]
                {
                    a - versatz, b - versatz, b + versatz, a + versatz,
                });
            }
            if (netze.Count == 0) return;
            vereint.NetLine = vereint.NetLine.Concat(netze).ToArray();
            vereint.AisleLine = vereint.AisleLine.Concat(linien).ToArray();
            vereint.AisleQuad = vereint.AisleQuad.Concat(quads).ToArray();
            vereint.AsphaltSurface = vereint.AsphaltSurface
                .Concat(quads).ToArray();
            vereint.Aisles += netze.Count;
            vereint.TeilflaechenVerbindungen = netze.Count;
            if (gruen.Count != 0)
            {
                vereint.GrassSurface = vereint.GrassSurface
                    .Concat(gruen).ToArray();
                vereint.Green = vereint.Green.Concat(gruen).ToArray();
            }
        }

        /** Ein Rechteck der Breite `breite` zwischen zwei Punkten. */
        private static void FuelleSpaltende(
            List<float2[]> ziel, float2 von, float2 nach, double breite)
        {
            var richtung = new double2(nach.x - von.x, nach.y - von.y);
            var laenge = Len(richtung);
            if (laenge < 0.05) return;
            var quer = new double2(-richtung.y, richtung.x) / laenge
                * (breite / 2);
            var versatz = new float2((float)quer.x, (float)quer.y);
            ziel.Add(new[]
            {
                von - versatz, nach - versatz, nach + versatz, von + versatz,
            });
        }

        private static string InnersteMeldung(Exception fehler)
        {
            while (fehler.InnerException != null) fehler = fehler.InnerException;
            return fehler.Message;
        }

        /**
         * Die Zugaenge, die auf DIESES Teil entfallen.
         *
         * Ein Zugang haengt an einer Kantennummer des ganzen Umrisses; das
         * Teil hat eigene Kanten. Gesucht wird deshalb ueber den ORT: liegt
         * der Punkt des Zugangs auf einer Kante des Teils, wird er dorthin
         * umgeschrieben. Teile ohne Zugang bekommen keinen - sie haengen
         * ueber die Naht am Nachbarn.
         */
        private static Entrance[] ZugaengeFuerTeil(
            Entrance[] zugaenge,
            float2[] site,
            double2[] teil)
        {
            if (zugaenge == null || zugaenge.Length == 0 || site == null)
                return Array.Empty<Entrance>();
            var ausgabe = new List<Entrance>();
            foreach (var zugang in zugaenge)
            {
                if (zugang == null) continue;
                if (zugang.Edge < 0 || zugang.Edge >= site.Length) continue;
                var a = new double2(site[zugang.Edge].x, site[zugang.Edge].y);
                var b0 = site[(zugang.Edge + 1) % site.Length];
                var b = new double2(b0.x, b0.y);
                var richtung = b - a;
                var laenge = Len(richtung);
                if (laenge < 1e-9) continue;
                var ort = a + richtung / laenge * zugang.Along;

                for (var k = 0; k < teil.Length; k++)
                {
                    var p = teil[k];
                    var q = teil[(k + 1) % teil.Length];
                    var kante = q - p;
                    var kantenlaenge = Len(kante);
                    if (kantenlaenge < 1e-9) continue;
                    var t = Dot(ort - p, kante) / (kantenlaenge * kantenlaenge);
                    if (t < -1e-6 || t > 1 + 1e-6) continue;
                    var lot = p + kante * Math.Max(0, Math.Min(1, t));
                    if (Len(ort - lot) > 0.05) continue;
                    var kopie = zugang.Clone();
                    kopie.Edge = k;
                    kopie.Along = Math.Max(0, Math.Min(1, t)) * kantenlaenge;
                    ausgabe.Add(kopie);
                    break;
                }
            }
            return ausgabe.ToArray();
        }

        private static double Dot(double2 a, double2 b) => a.x * b.x + a.y * b.y;

        /**
         * Legt die Layouts der Teile zu EINEM zusammen.
         *
         * Reine Aneinanderreihung: jede Liste wird verkettet, jede Zahl
         * addiert. Die Buchtnummern der Elektropaare muessen dabei versetzt
         * werden - sie zeigen auf Buchten, und die stehen nach dem Anhaengen
         * an anderer Stelle.
         */
        private static ParkingLayout VereineLayouts(
            IReadOnlyList<ParkingLayout> teile,
            float2[] site)
        {
            if (teile.Count == 1) return teile[0];

            float2[][] Verkette(Func<ParkingLayout, float2[][]> feld)
                => teile.SelectMany(t => feld(t) ?? Array.Empty<float2[]>())
                    .ToArray();

            var paare = new List<int2>();
            var versatz = 0;
            foreach (var teil in teile)
            {
                foreach (var paar in teil.ElectricPair ?? Array.Empty<int2>())
                    paare.Add(new int2(paar.x + versatz, paar.y + versatz));
                versatz += teil.Bay?.Length ?? 0;
            }

            return new ParkingLayout
            {
                Bay = Verkette(t => t.Bay),
                BayKind = teile.SelectMany(t => t.BayKind
                    ?? Array.Empty<BayKind>()).ToArray(),
                BayRole = teile.SelectMany(t => t.BayRole
                    ?? Array.Empty<BayRole>()).ToArray(),
                ElectricPair = paare.ToArray(),
                Cap = Verkette(t => t.Cap),
                CapKind = teile.SelectMany(t => t.CapKind
                    ?? Array.Empty<string>()).ToArray(),
                Median = Verkette(t => t.Median),
                Green = Verkette(t => t.Green),
                Fill = Verkette(t => t.Fill),
                FillHole = Verkette(t => t.FillHole),
                CrossPavement = Verkette(t => t.CrossPavement),
                GrassSurface = Verkette(t => t.GrassSurface),
                AsphaltSurface = Verkette(t => t.AsphaltSurface),
                PerimeterQuad = Verkette(t => t.PerimeterQuad),
                EntranceQuad = Verkette(t => t.EntranceQuad),
                EntranceQuadArt = teile.SelectMany(t => t.EntranceQuadArt
                    ?? Array.Empty<int>()).ToArray(),
                AisleLine = Verkette(t => t.AisleLine),
                AisleQuad = Verkette(t => t.AisleQuad),
                CrossLine = Verkette(t => t.CrossLine),
                CrossRouteLine = Verkette(t => t.CrossRouteLine),
                CrossQuad = Verkette(t => t.CrossQuad),
                PerimeterLine = Verkette(t => t.PerimeterLine),
                EntranceLine = Verkette(t => t.EntranceLine),
                NetLine = teile.SelectMany(t => t.NetLine
                    ?? Array.Empty<NetSegment>()).ToArray(),
                Entrances = teile.SelectMany(t => t.Entrances
                    ?? Array.Empty<Entrance>()).ToArray(),
                // Der Ring ist der Umriss des GANZEN Parkplatzes, nicht die
                // Summe der Teilringe - er beschreibt, was der Nutzer gezogen
                // hat.
                Ring = site,
                Sections = teile.SelectMany(t => t.Sections
                    ?? Array.Empty<LayoutSection>()).ToArray(),
                PassInfo = teile.SelectMany(t => t.PassInfo
                    ?? Array.Empty<LayoutPassInfo>()).ToArray(),
                CrossRouteInfo = teile.SelectMany(t => t.CrossRouteInfo
                    ?? Array.Empty<LayoutCrossRouteInfo>()).ToArray(),
                Stalls = teile.Sum(t => t.Stalls),
                PerimeterStalls = teile.Sum(t => t.PerimeterStalls),
                InnerPerimeterStalls = teile.Sum(t => t.InnerPerimeterStalls),
                InnerStalls = teile.Sum(t => t.InnerStalls),
                ExtraStalls = teile.Sum(t => t.ExtraStalls),
                SpecialStalls = new SpecialStallCounts
                {
                    Behindert = teile.Sum(t => t.SpecialStalls?.Behindert ?? 0),
                    Elektro = teile.Sum(t => t.SpecialStalls?.Elektro ?? 0),
                },
                // Der gemeldete Winkel ist der des ERSTEN Teils - er ist die
                // Vorgabe, an der sich die uebrigen orientieren.
                Angle = teile[0].Angle,
                Aisles = teile.Sum(t => t.Aisles),
                Parts = teile.Sum(t => t.Parts),
                NotchAisles = teile.Sum(t => t.NotchAisles),
                Crossings = teile.Sum(t => t.Crossings),
                Warnings = teile.SelectMany(t => t.Warnings
                    ?? Array.Empty<string>()).Distinct().ToArray(),
            };
        }
    }
}
