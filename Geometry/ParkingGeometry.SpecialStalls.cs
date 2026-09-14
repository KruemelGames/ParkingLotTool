using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static partial class ParkingGeometry
    {
        // Gemessen am Vanilla-Aufkleber: ParkingLotDisabledDecal01 ist 5,0 x
        // 6,2 gross und traegt eine 4,7x5,9-Spur. Drei Plaetze sind 15,00 m und
        // damit exakt fuenf 3,00-m-Rasterfelder. Eine Gruppe kostet zwei Buchten.
        internal const double DisabledWidth = 5.0;
        private const double ElectricShare = 0.04;
        // EIN ELEKTROPLATZ KOMMT NIE ALLEIN. Die Ladesaeule steht ZWISCHEN
        // zwei Buchten und bedient beide - eine einzelne oder eine ungerade
        // Zahl von E-Plaetzen gibt es in Wirklichkeit nicht. Deshalb wird hier
        // in PAAREN gerechnet statt in Plaetzen: so kann gar keine ungerade
        // Zahl entstehen. Vorher stand hier eine Platzzahl, und die L-Form
        // bekam prompt 7.
        private const int ElectricPairsMin = 1;
        private const int ElectricPairsMax = 4;

        private sealed class DisabledGroup
        {
            internal int Fields;
            internal int Stalls;
            internal double Width;
            internal double Error;
        }

        /**
         * Feldzahl GERECHNET. Fest verdrahtete 5 Felder ragten beim zweispurigen
         * Modul ueber ihren Platz und erzeugten sofort eine Buchtueberlappung.
         */
        /**
         * ALLE brauchbaren Blockgroessen, nicht nur die formschoenste.
         *
         * Bis zum 2026-08-27 wurde EINE Groesse gewaehlt - die mit der
         * geringsten Abweichung von der Sollbreite - und danach eine Reihe
         * gesucht, die so viele Buchten am Stueck frei hat. Gemessen an
         * sechs Abzuegen des Nutzers: von 18 Reihen boten nur ZWEI fuenf
         * zusammenhaengende Buchten, und die lagen bis zu 97 m vom Fussweg
         * entfernt. Rund um einen Zugang zerteilen Fahrgassen und
         * Freihaltung die Reihen genau dort, wo der Block hin soll.
         *
         * Naehe schlaegt Formschoenheit: ein Block aus drei Feldern direkt
         * am Fussweg nuetzt mehr als ein perfekt bemessener am anderen Ende
         * des Parkplatzes. Deshalb kommen jetzt alle gueltigen Groessen in
         * die Auswahl, sortiert nach Abweichung - entschieden wird spaeter
         * nach dem Abstand.
         */
        private static List<DisabledGroup> FindDisabledGroups(double stallWidth)
        {
            var alle = new List<DisabledGroup>();
            for (var fields = 3; fields <= 8; fields++)
            {
                var span = fields * stallWidth;
                var stalls = JsRound(span / DisabledWidth);
                if (stalls < 2 || stalls >= fields) continue;
                var width = span / stalls;
                if (width < 4.7 || width > 5.6) continue;
                alle.Add(new DisabledGroup
                {
                    Fields = fields,
                    Stalls = stalls,
                    Width = width,
                    Error = Math.Abs(width - DisabledWidth),
                });
            }
            return alle.OrderBy(g => g.Error).ToList();
        }

        private static DisabledGroup FindDisabledGroup(double stallWidth)
        {
            DisabledGroup best = null;
            for (var fields = 3; fields <= 8; fields++)
            {
                var span = fields * stallWidth;
                var stalls = JsRound(span / DisabledWidth);
                if (stalls < 2 || stalls >= fields) continue;
                var width = span / stalls;
                if (width < 4.7 || width > 5.6) continue;
                var error = Math.Abs(width - DisabledWidth);
                if (best == null || error < best.Error)
                    best = new DisabledGroup
                        { Fields = fields, Stalls = stalls, Width = width, Error = error };
            }
            return best;
        }

        /**
         * Sonderplaetze als letzter Schritt. Behindert liegt als zusammenhaengender
         * Block am naechsten zur Zufahrt, Elektro direkt aussen daneben.
         */
        private static void AssignBayRoles(WorkLayout output, LayoutSettings settings)
        {
            SonderplatzSpur.Clear();
            output.BayRole = Enumerable.Repeat(BayRole.Normal, output.Bay.Count).ToList();
            output.SpecialStalls = new SpecialStallCounts();
            // Je Eintrag die zwei Buchten, die sich eine Ladesaeule teilen.
            // Auch im Abbruchfall gesetzt, damit niemand auf null trifft.
            output.ElectricPair = new List<int2>();
            if (output.Bay.Count == 0 || output.EntranceLine.Count == 0) return;

            /**
             * FUSSWEGE HABEN VORRANG VOR ZUFAHRTEN - und seit dem 2026-09-03
             * gehen die Zoningflaechen noch einmal vor beide, siehe unten.
             *
             * Ansage des Nutzers am 2026-08-27: Behinderten- und E-Plaetze
             * sollen an den Fussweg, auch wenn er spaeter als die Zufahrt
             * gesetzt wurde. Wer schlecht zu Fuss ist, will kurze Wege ZUM
             * GEHWEG, nicht zur Einfahrt - dort steigt niemand aus.
             *
             * Gibt es keinen Fussweg, gelten wie bisher alle Zufahrten. Der
             * Vorrang darf nicht dazu fuehren, dass ein Parkplatz ohne
             * Fussweg gar keine Sonderplaetze bekommt.
             *
             * Die Zuordnung Linie -> Art entsteht beim Anlegen, nicht hier
             * ueber einen Index. Passen die Laengen wider Erwarten nicht
             * zusammen, gilt lieber die alte Regel als eine falsche
             * Zuordnung.
             */
            var linien = output.EntranceLine;
            var arten = output.Entrances;
            var artenPassen = arten != null && arten.Count == linien.Count;
            /**
             * EIN GESETZTER ZUGANG SCHLAEGT EINEN AUTOMATISCHEN.
             *
             * Befund des Nutzers am 2026-09-08: *"Die Parkplatz Prioritaet
             * funktioniert nicht mehr bei Randstrassen aus"* - gemeint waren
             * E- und Behindertenplaetze.
             *
             * Gemessen am Nutzergrundstueck mit einem Fussweg an Kante 0:
             *
             *     mit Randstrasse   1 Zufahrtslinie   Behindert  7,3 - 15,0 m
             *     ohne Randstrasse  3 Zufahrtslinien  Behindert 38,8 - 75,6 m
             *
             * Die zwei zusaetzlichen Linien sind die Randfusswege, die der
             * ringlose Plan selbst anlegt - je 71 m lang, quer ueber das ganze
             * Grundstueck. Sie tragen `Art = Fussweg` und standen damit
             * gleichberechtigt neben dem gesetzten Fussweg. Reihum bekam
             * Gruppe 0 den einen Randweg, Gruppe 1 den anderen - der gesetzte
             * Fussweg kam als dritter nie an die Reihe.
             *
             * Deshalb zaehlen jetzt zuerst die gesetzten Zugaenge. Die
             * automatischen bleiben als Rueckfall, falls gar nichts gesetzt
             * wurde; ohne sie bekaeme so ein Parkplatz sonst gar keine
             * Sonderplaetze.
             */
            double2[] Waehle(Func<int, bool> passt) => artenPassen
                ? Enumerable.Range(0, linien.Count)
                    .Where(i => arten[i] != null && passt(i))
                    .Select(i => linien[i].B)
                    .ToArray()
                : Array.Empty<double2>();
            var gesetzteFusswege = Waehle(i => arten[i].Gesetzt
                && arten[i].Art == Zufahrtsart.Fussweg);
            var gesetzteZugaenge = Waehle(i => arten[i].Gesetzt);
            /*
             * DIE ZONINGFLAECHEN GEHEN VOR - dort stehen die Gebaeude.
             *
             * Ansage des Nutzers am 2026-09-03: *"Die sollten priorisiert in
             * der Naehe von unseren ZF liegen."* Auf Nachfrage die
             * Reihenfolge: **zuerst ZF, danach Fusswege, danach
             * Ein-/Ausfahrt.**
             *
             * Das ist dieselbe Ueberlegung, die den Fussweg schon vor die
             * Zufahrt gestellt hat, nur einen Schritt weiter gedacht: wer
             * schlecht zu Fuss ist, will kurze Wege ZUM ZIEL. Der Fussweg
             * ist nur der Weg dorthin; auf den Parzellen steht, weswegen
             * jemand ueberhaupt kommt.
             *
             * Anker ist die MITTE der Parzellen, nicht ihre Kante. Eine
             * Bucht neben der Mitte einer Seite ist damit naeher als eine an
             * der Ecke - und genau so soll die Reihenfolge sein.
             */
            var zoningziele = (settings.Zoningflaechen
                    ?? Array.Empty<Zoningflaeche>())
                .Where(f => f != null && f.Spalten > 0 && f.Reihen > 0)
                .Select(f =>
                {
                    var m = ZoningMitte(f);
                    return new double2(m.x, m.y);
                })
                .ToArray();

            var targets = zoningziele.Length > 0
                ? zoningziele
                : gesetzteFusswege.Length > 0
                    ? gesetzteFusswege
                    : gesetzteZugaenge.Length > 0
                        ? gesetzteZugaenge
                        : linien.Select(line => line.B).ToArray();
            double2 Center(double2[] q) => q.Aggregate(new double2(0), (sum, p) => sum + p / 4);
            /**
             * AUF MEHRERE FUSSWEGE VERTEILEN, NICHT ALLES AUF EINEN.
             *
             * Ohne das haengt bei zwei Fusswegen alles am ersten - "dass
             * nicht alles auf einem klebt", so der Nutzer. Jede Gruppe
             * bekommt deshalb reihum EINEN Anker zugewiesen und misst nur
             * gegen den. Bei einem einzigen Fussweg aendert das nichts.
             */
            SonderplatzSpur.Add($"Anker: {linien.Count} Linien, davon gesetzt "
                + $"{gesetzteZugaenge.Length} (Fussweg {gesetzteFusswege.Length}), "
                + $"Zoning {zoningziele.Length} -> {targets.Length} Ziel(e)");
            foreach (var t in targets)
                SonderplatzSpur.Add($"    Ziel ({t.x:F1}/{t.y:F1})");
            var ankerIndex = 0;
            /*
             * OFFEN, NICHT BEHOBEN: ohne gesetzten Zugang bleiben nur die
             * automatischen Randfusswege als Ziel. Deren Anker ist der
             * Endpunkt B einer 71 m langen Linie, und welches der beiden
             * Enden das ist, entscheidet der Abstand zur Ringmitte - ohne
             * Randstrasse liegen beide gleich weit weg (64,6 m gegen
             * 64,6 m), die Wahl ist also ein Muenzwurf. Ein Abstand zur
             * Strecke statt zum Endpunkt haette das geloest, aber kein
             * Pruefstand wird davon rot und ein besseres Ergebnis liess
             * sich nicht messen. Deshalb steht es hier statt im Code.
             */
            double NearTo(double2 center, double2 target)
            {
                var delta = center - target;
                return Math.Sqrt(delta.x * delta.x + delta.y * delta.y);
            }
            double Near(double2 center)
            {
                if (targets.Length > 1)
                    return NearTo(center, targets[ankerIndex % targets.Length]);
                var best = double.PositiveInfinity;
                foreach (var target in targets)
                {
                    var delta = center - target;
                    var distance = delta.x * delta.x + delta.y * delta.y;
                    if (distance < best) best = distance;
                }
                return Math.Sqrt(best);
            }
            BayAxis Axis(double2[] q)
            {
                var side0 = Len(q[1] - q[0]);
                return Math.Abs(side0 - settings.Sw) < 0.05
                    ? new BayAxis { U = Norm(q[1] - q[0]), Depth = Len(q[2] - q[1]) }
                    : new BayAxis { U = Norm(q[2] - q[1]), Depth = side0 };
            }
            List<List<int>> Rows()
            {
                // Nicht ueber gerundete absolute Weltkoordinaten gruppieren.
                // Am Nutzergrundstueck zerlegte ein Unterschied von 3 cm im
                // berechneten Querversatz dieselbe Randreihe in 9 und 19
                // Buchten. Relativ zur bereits gefundenen Reihenlinie ist
                // derselbe Vergleich translationsstabil.
                const double parallelCos = 0.999998;
                const double lineTolerance = 0.10;
                var rows = new List<BayRow>();
                for (var i = 0; i < output.Bay.Count; i++)
                {
                    var axis = Axis(output.Bay[i]).U;
                    if (axis.x < -1e-9
                        || (Math.Abs(axis.x) <= 1e-9 && axis.y < 0))
                        axis = -axis;
                    var center = Center(output.Bay[i]);
                    var row = rows.FirstOrDefault(candidate =>
                        candidate.Kind == output.BayKind[i]
                        && math.dot(axis, candidate.Axis) >= parallelCos
                        && Math.Abs(math.dot(center - candidate.Origin,
                            candidate.Normal)) <= lineTolerance);
                    if (row == null)
                    {
                        row = new BayRow
                        {
                            Axis = axis,
                            Normal = new double2(-axis.y, axis.x),
                            Origin = center,
                            Kind = output.BayKind[i],
                        };
                        rows.Add(row);
                    }
                    row.Indices.Add(i);
                }
                return rows.Select(row => row.Indices).ToList();
            }

            var groessen = FindDisabledGroups(settings.Sw);
            var group = groessen.FirstOrDefault();
            var groups = group != null ? (output.Bay.Count >= 150 ? 2 : 1) : 0;
            for (var groupIndex = 0; groupIndex < groups; groupIndex++)
            {
                // Reihum, damit zwei Gruppen an zwei verschiedenen Fusswegen
                // landen statt beide am selben.
                ankerIndex = groupIndex;
                var candidates = Rows().Select(indices =>
                {
                    var axis = Axis(output.Bay[indices[0]]).U;
                    return indices.OrderBy(i => Project(Center(output.Bay[i]), axis)).ToList();
                }).Where(indices => indices.Count >= groessen.Min(g => g.Fields)
                    && indices.All(i => output.BayRole[i] == BayRole.Normal))
                  .ToList();

                /**
                 * DIE NAECHSTE REIHE IST NICHT DER NAECHSTE PLATZ.
                 *
                 * Bis zum 2026-08-27 wurden die Reihen nach ihrer
                 * NAECHSTGELEGENEN Bucht sortiert, und die erste Reihe mit
                 * einem zusammenhaengenden Abschnitt bekam die Gruppe. Beides
                 * zusammen geht schief: eine Reihe kann ganz nah beginnen,
                 * ihren einzigen freien Abschnitt aber am ANDEREN Ende haben.
                 * Eine andere Reihe mit einem viel naeheren Abschnitt kam nie
                 * zum Zug, weil sie in der Sortierung dahinter lag.
                 *
                 * Gemessen an sechs Abzuegen des Nutzers, alle mit einem
                 * Fussweg und identischem Grundstueck - die naechste Bucht lag
                 * jedes Mal 6,9 m entfernt:
                 *
                 *     Fussweg an Kante 0   Behindertenplaetze  7,3 - 20,5 m
                 *     Fussweg an Kante 1                      28,2 - 38,1 m
                 *     Fussweg an Kante 3                      35,2 - 57,5 m
                 *     Fussweg an Kante 2                      52,9 - 97,3 m
                 *
                 * Nur einer von vier Faellen landete wirklich am Fussweg.
                 *
                 * Jetzt wird ZUERST in jeder Reihe der beste Abschnitt
                 * gesucht und danach ueber ALLE Reihen hinweg der naechste
                 * genommen. Die Reihe ist nur noch der Behaelter, entschieden
                 * wird ueber den Abschnitt.
                 */
                (int Start, double Near) BesterAbschnitt(List<int> row,
                                                          DisabledGroup mass)
                {
                    var achse = Axis(output.Bay[row[0]]);
                    double T(int i) => Project(Center(output.Bay[i]), achse.U);
                    var start = -1;
                    var nah = double.PositiveInfinity;
                    for (var k = 0; k + mass.Fields <= row.Count; k++)
                    {
                        var zusammenhaengend = true;
                        for (var m = 1; m < mass.Fields; m++)
                            if (Math.Abs(T(row[k + m]) - T(row[k + m - 1]) - settings.Sw) > 0.05)
                            {
                                zusammenhaengend = false;
                                break;
                            }
                        if (!zusammenhaengend) continue;
                        var abstand = row.Skip(k).Take(mass.Fields)
                            .Min(i => Near(Center(output.Bay[i])));
                        if (abstand < nah) { nah = abstand; start = k; }
                    }
                    return (start, nah);
                }

                /*
                 * NAEHE SCHLAEGT FORMSCHOENHEIT. Erst alle Groessen ueber alle
                 * Reihen durchrechnen, dann den naechsten Abschnitt nehmen.
                 * Bei gleichem Abstand gewinnt die formschoenere Groesse -
                 * `groessen` ist danach sortiert, und `ThenBy` haelt das fest.
                 */
                var bewertet = candidates
                    .SelectMany(row => groessen.Select(mass =>
                    {
                        var (start, nah) = BesterAbschnitt(row, mass);
                        return (Row: row, Start: start, Near: nah, Mass: mass);
                    }))
                    .Where(eintrag => eintrag.Start >= 0)
                    .OrderBy(eintrag => eintrag.Near)
                    .ThenBy(eintrag => eintrag.Mass.Error)
                    .ToList();

                /*
                 * Spur fuer die Fehlersuche: welche Abschnitte standen zur
                 * Wahl, und wie nah war der naechste? Ohne das laesst sich
                 * "zu weit weg" nicht von "naeher ging es nicht" trennen.
                 */
                // Die fuenf naechsten Reihen einzeln: wie viele Buchten, wie
                // nah, und ob ueberhaupt ein Abschnitt hineinpasst. Nur so
                // laesst sich "keine Reihe ist nah" von "die nahen Reihen sind
                // zerstueckelt" unterscheiden.
                foreach (var reihe in candidates
                             .OrderBy(r => r.Min(i => Near(Center(output.Bay[i]))))
                             .Take(5))
                {
                    var nah = reihe.Min(i => Near(Center(output.Bay[i])));
                    var beste = groessen
                        .Select(m => BesterAbschnitt(reihe, m))
                        .Where(t => t.Start >= 0)
                        .Select(t => t.Near)
                        .DefaultIfEmpty(double.NaN)
                        .Min();
                    var achse = Axis(output.Bay[reihe[0]]);
                    var abstaende = Enumerable.Range(1, reihe.Count - 1)
                        .Select(k => Project(Center(output.Bay[reihe[k]]), achse.U)
                                   - Project(Center(output.Bay[reihe[k - 1]]), achse.U))
                        .Select(x => Math.Round(x, 2))
                        .ToList();
                    SonderplatzSpur.Add($"    Reihe {reihe.Count,2} Buchten, "
                        + $"naechste {nah,5:F1} m, bester Abschnitt "
                        + (double.IsNaN(beste) ? "KEINER" : $"{beste,5:F1} m")
                        + $" | Sw {settings.Sw:F2} | Abstaende "
                        + string.Join(" ", abstaende.Take(10)));
                }

                SonderplatzSpur.Add($"Gruppe {groupIndex}: {candidates.Count} Reihen, "
                    + $"{bewertet.Count} mit Abschnitt, naechster "
                    + (bewertet.Count > 0 ? $"{bewertet[0].Near:F1} m" : "-")
                    + $", Felder je Block {group.Fields}, "
                    + $"Reihenlaengen {string.Join("/", candidates.Take(6).Select(r => r.Count))}");

                var placed = false;
                foreach (var eintrag in bewertet)
                {
                    var row = eintrag.Row;
                    var bestStart = eintrag.Start;
                    // Die Groesse gehoert zum Abschnitt, nicht umgekehrt.
                    group = eintrag.Mass;
                    var bayAxis = Axis(output.Bay[row[0]]);

                    var removed = row.Skip(bestStart).Take(group.Fields).ToList();
                    var normal = new double2(-bayAxis.U.y, bayAxis.U.x);
                    var start = Center(output.Bay[removed[0]]) - bayAxis.U * (settings.Sw / 2);
                    var kind = output.BayKind[removed[0]];
                    var newQuads = Enumerable.Range(0, group.Stalls)
                        .Select(k => Rect(start + bayAxis.U * (group.Width * (k + 0.5)),
                                          bayAxis.U, normal, group.Width / 2, bayAxis.Depth / 2))
                        .ToList();
                    foreach (var i in removed.OrderByDescending(i => i))
                    {
                        output.Bay.RemoveAt(i);
                        output.BayKind.RemoveAt(i);
                        output.BayRole.RemoveAt(i);
                    }
                    foreach (var quad in newQuads)
                    {
                        output.Bay.Add(quad);
                        output.BayKind.Add(kind);
                        output.BayRole.Add(BayRole.Disabled);
                        output.SpecialStalls.Behindert++;
                    }
                    placed = true;
                    break;
                }
                if (!placed) break;
            }

            // Elektro ist geometrisch normal (gleiche 2,9x5,9-Spur), nur Grafik.
            var pairs = Math.Max(ElectricPairsMin, Math.Min(ElectricPairsMax,
                JsRound(output.Bay.Count * ElectricShare / 2)));
            // Abstand zum BLAUEN BLOCK. Gegen die Zufahrt gemessen rueckte Elektro
            // naeher als Behindert, weil Blau mehrere freie Felder am Stueck braucht.
            var blue = Enumerable.Range(0, output.Bay.Count)
                .Where(i => output.BayRole[i] == BayRole.Disabled)
                .Select(i => Center(output.Bay[i])).ToList();
            double ReferenceDistance(double2 center)
            {
                if (blue.Count == 0) return Near(center);
                var best = double.PositiveInfinity;
                foreach (var point in blue)
                {
                    var delta = center - point;
                    var distance = delta.x * delta.x + delta.y * delta.y;
                    if (distance < best) best = distance;
                }
                return Math.Sqrt(best);
            }
            /**
             * Alle moeglichen PAARE aus zwei unmittelbar benachbarten Buchten
             * sammeln.
             *
             * "Benachbart" heisst: gleiche Reihe UND genau eine Buchtbreite
             * Abstand. Ueber eine Luecke hinweg zu paaren waere sinnlos - die
             * Ladesaeule stuende dann nicht zwischen zwei Autos, sondern im
             * Nichts. Deshalb wird jede Reihe zuerst in LUECKENLOSE
             * ABSCHNITTE zerlegt und erst darin gepaart.
             *
             * Gepaart wird vom bezugsnahen Ende jedes Abschnitts aus in festen
             * Zweierschritten. Sonst koennte ein Paar mitten in einem
             * Abschnitt liegen und links wie rechts einen einzelnen Platz
             * uebriglassen, der nie ein Paar findet.
             */
            var pairCandidates = new List<int[]>();
            foreach (var row in Rows())
            {
                var axis = Axis(output.Bay[row[0]]).U;
                double T(int i) => Project(Center(output.Bay[i]), axis);
                var ordered = row.OrderBy(T).ToList();
                var runs = new List<List<int>> { new List<int> { ordered[0] } };
                for (var k = 1; k < ordered.Count; k++)
                {
                    if (Math.Abs(T(ordered[k]) - T(ordered[k - 1]) - settings.Sw) > 0.05)
                        runs.Add(new List<int>());
                    runs[runs.Count - 1].Add(ordered[k]);
                }
                foreach (var run in runs)
                {
                    if (run.Count < 2) continue;
                    var front = ReferenceDistance(Center(output.Bay[run[0]]));
                    var back = ReferenceDistance(Center(output.Bay[run[run.Count - 1]]));
                    var walk = back < front
                        ? Enumerable.Reverse(run).ToList() : run;
                    for (var k = 0; k + 1 < walk.Count; k += 2)
                        pairCandidates.Add(new[] { walk[k], walk[k + 1] });
                }
            }
            pairCandidates = pairCandidates
                .OrderBy(pair => Math.Min(
                    ReferenceDistance(Center(output.Bay[pair[0]])),
                    ReferenceDistance(Center(output.Bay[pair[1]])))).ToList();

            output.ElectricPair = new List<int2>();
            var open = pairs;
            foreach (var pair in pairCandidates)
            {
                if (open <= 0) break;
                if (output.BayRole[pair[0]] != BayRole.Normal
                    || output.BayRole[pair[1]] != BayRole.Normal) continue;
                output.BayRole[pair[0]] = BayRole.Electric;
                output.BayRole[pair[1]] = BayRole.Electric;
                output.ElectricPair.Add(new int2(pair[0], pair[1]));
                output.SpecialStalls.Elektro += 2;
                open--;
            }

            output.PerimeterStalls = output.BayKind.Count(
                kind => kind == BayKind.Perimeter || kind == BayKind.InnerPerimeter);
            output.InnerPerimeterStalls = output.BayKind.Count(kind => kind == BayKind.InnerPerimeter);
            output.InnerStalls = output.BayKind.Count(kind => kind == BayKind.Inner);
            output.ExtraStalls = output.BayKind.Count(kind => kind == BayKind.Extra);
            output.Stalls = output.PerimeterStalls + output.InnerStalls + output.ExtraStalls;
        }

        private static double Project(double2 point, double2 axis) =>
            point.x * axis.x + point.y * axis.y;

        private sealed class BayAxis { internal double2 U; internal double Depth; }
        private sealed class BayRow
        {
            internal double2 Axis;
            internal double2 Normal;
            internal double2 Origin;
            internal BayKind Kind;
            internal readonly List<int> Indices = new List<int>();
        }
    }
}
