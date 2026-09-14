using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static partial class ParkingGeometry
    {
        private sealed class MaterialRingSides
        {
            internal readonly List<double2[]> Inside = new List<double2[]>();
            internal readonly List<double2[]> Outside = new List<double2[]>();
        }

        private sealed class MaterialSurfaceGroups
        {
            internal List<double2[]> Grass;
            internal List<double2[]> Asphalt;
            internal bool MaterialChanged;
        }

        private static MaterialRingSides SplitMaterialRingSides(
            IEnumerable<double2[]> list, double2[] ring)
        {
            var result = new MaterialRingSides();
            foreach (var polygon in list)
            {
                var middle = polygon.Length == 0
                    ? new double2(double.NaN, double.NaN)
                    : polygon.Aggregate(new double2(0), (sum, point) => sum + point)
                        / polygon.Length;
                (ring.Length >= 3 && PointIn(middle, ring)
                    ? result.Inside : result.Outside).Add(polygon);
            }
            return result;
        }

        /** Einen unbaubaren Rest verlustfrei in eine baubare Grasflaeche aufnehmen. */
        private static bool TryAbsorbThinRestIntoGrass(
            double2[] rest, List<double2[]> grass)
        {
            var bestIndex = -1;
            var bestNeck = double.NegativeInfinity;
            double2[] bestUnion = null;
            for (var index = 0; index < grass.Count; index++)
            {
                if (!TrySurfaceUnion(rest, grass[index], out var union)
                    || !SurfaceCs2Compatible(union)) continue;
                var feature = MinimumSurfaceFeature(new List<double2[]> { union });
                var neck = feature?.Distance ?? double.PositiveInfinity;
                if (neck < SurfaceNeckLimit - 1e-9 || neck <= bestNeck) continue;
                bestIndex = index;
                bestNeck = neck;
                bestUnion = union;
            }
            if (bestIndex < 0) return false;
            grass[bestIndex] = bestUnion;
            return true;
        }

        /**
         * Der Materialschalter wirkt ausschliesslich innerhalb der Randstrasse.
         *
         * Die Mittelstreifen bleiben IMMER gruen. Es gab dafuer einmal einen
         * zweiten Schalter; der beruhte auf einer Annahme und wurde auf Ansage
         * des Nutzers wieder entfernt.
         */
        private static MaterialSurfaceGroups GroupMaterialSurfaces(
            WorkLayout output, LayoutSettings settings)
        {
            var capsGreen = settings.Qk;
            var cap = SplitMaterialRingSides(output.Cap, output.Ring);
            var fill = SplitMaterialRingSides(output.Fill, output.Ring);
            var crossPavement = SplitMaterialRingSides(output.CrossPavement, output.Ring);

            var grassCaps = capsGreen ? output.Cap : cap.Outside;
            var grassFill = capsGreen ? output.Fill : fill.Outside;
            var grass = capsGreen
                ? output.Cap.Concat(output.Median).Concat(output.Green)
                    .Concat(output.Fill).ToList()
                : grassCaps.Concat(output.Median).Concat(output.Green)
                    .Concat(grassFill).Concat(crossPavement.Outside).ToList();
            var asphalt = new List<double2[]>();

            /**
             * EIN DUENNER REST GEHT ZUERST IN EINEN BAUBAREN GRASNACHBARN.
             *
             * Bis 2026-08-18 wanderte JEDER Rest der Restfuellung unter
             * 0,375 m Breite zum Belag. Wo er neben der Fahrbahn lag, war das
             * richtig. Wo er MITTEN IM GRUEN lag, malte er dort eine duenne
             * sandfarbene Linie in den Rasen - der Nutzer schickte am selben
             * Tag ein Bild davon: spitz zulaufende Streifen quer durch den
             * Gruenstreifen.
             *
             * Das blosse Beruehren einer Fahrbahn reicht aber ebenfalls nicht:
             * Gebaut 18 hinterliess nach dem Entfernen einer Verbindung einen
             * Rest von 7,742 m2 (0,220 x 35,067 m). Er beruehrte Fahrbahnen nur
             * an den Enden, liess sich aber mit der seitlichen Restfuellung zu
             * einer CS2-tauglichen Grasflaeche mit 1,403 m Engstelle vereinigen.
             * Als Belag blieb davon ein nackter Fleck von 6,78 m2 und
             * 0,40 x 31,50 m. Deshalb gewinnt zuerst nur eine verlustfreie,
             * baubare Grasvereinigung; erst der uebrige Rest darf an Belag.
             */
            var fahrbahn = output.PerimeterQuad.Concat(output.AisleQuad)
                .Concat(output.CrossQuad).Concat(output.EntranceQuad)
                .Concat(output.Bay).ToList();
            var unresolved = output.FillThin.ToList();
            /*
             * HIER STAND EINE SCHLEIFE, DIE NIE LIEF.
             *
             * Sie nahm duenne Reste nachtraeglich in die Grasflaeche auf und
             * haing an `output.CrossPolicyRebuilt`. Dieses Feld hat
             * ausschliesslich der ALTE Rechenweg gesetzt - fuer den
             * Zellenweg war es immer `false`, die Schleife also schon vor
             * ihrem Ausbau (2026-09-01) wirkungslos.
             *
             * Entfernt, weil toter Code, der so tut als ob, schlimmer ist
             * als keiner: beim naechsten Rest haette jemand hier gesucht und
             * eine Reparatur vermutet, die es nie gab. Am Verhalten aendert
             * sich nichts - `unresolved` geht unveraendert in die Zeile
             * darunter.
             */
            foreach (var rest in unresolved)
                if (fahrbahn.Any(weg =>
                    rest.Any(q => DistToBoundary(q, weg) <= 0.05)))
                    asphalt.Add(rest);
            asphalt.AddRange(crossPavement.Inside);
            if (!capsGreen)
            {
                asphalt.AddRange(cap.Inside);
                asphalt.AddRange(fill.Inside);
            }
            // Welche Quelle explodiert? Am 2026-08-17 lieferte ein Fuenfeck mit
            // 16.430 m2 2390 Grasteile, waehrend ein groesseres Areal mit
            // 17.388 m2 nur 81 brauchte - ein 30-facher Ausreisser, der die
            // Materialreparatur zum Stehen brachte.
            if (ParkingGeometry.PhaseLog)
            {
                Console.Error.WriteLine(
                    $"      [Gras] Kappen {output.Cap.Count,5} | Mittelstreifen "
                    + $"{output.Median.Count,5} | Gruen {output.Green.Count,5} | "
                    + $"Restfuellung {output.Fill.Count,5} | Summe {grass.Count,6}");
                Console.Error.Flush();
            }
            return new MaterialSurfaceGroups
            {
                Grass = grass,
                Asphalt = asphalt,
                MaterialChanged = !capsGreen,
            };
        }

        private static List<double2[]> SubtractSurfaceRegion(
            List<double2[]> source, List<double2[]> blockers) =>
            SubtractAsphaltAsRegion(source, blockers)
            ?? SubtractAsphaltPerPiece(source, blockers);

        /**
         * Unbaubar duenne BELAGringe in ihren Nachbarn aufloesen.
         *
         * Die Reste der Restfuellung kommen als eigene Ringe an den Belag.
         * Bleiben sie einzeln stehen, hat der Belag Engstellen unter CS2s
         * Grenze - am Qualitaetslauf ueber 64 Formen 21 statt 6. Sie sollen
         * aber keine eigene Flaeche sein, sondern in der grossen aufgehen.
         *
         * Das Gegenstueck fuer Gras ist TransferThinGrassRings; hier bleibt
         * das Material gleich, es verschmilzt nur.
         */
        private static List<double2[]> AbsorbThinAsphaltRings(List<double2[]> rings)
        {
            var offen = rings.Select(r => r.ToArray()).ToList();
            if (ParkingGeometry.PhaseLog)
            {
                var duenn = offen.Count(r =>
                {
                    var n = MinimumSurfaceFeature(new List<double2[]> { r });
                    return (n != null && n.Distance < SurfaceNeckLimit)
                        || !SurfaceCs2Compatible(r);
                });
                Console.Error.WriteLine($"      [BelagDuenn] {offen.Count} Ringe, "
                    + $"{duenn} davon zu duenn");
                Console.Error.Flush();
            }
            var geaendert = true;
            while (geaendert)
            {
                geaendert = false;
                for (var i = 0; i < offen.Count; i++)
                {
                    var neck = MinimumSurfaceFeature(new List<double2[]> { offen[i] });
                    if ((neck == null || neck.Distance >= SurfaceNeckLimit - 1e-9)
                        && SurfaceCs2Compatible(offen[i])) continue;
                    for (var j = 0; j < offen.Count; j++)
                    {
                        if (i == j) continue;
                        if (!TrySurfaceUnion(offen[i], offen[j], out var union)) continue;
                        var vereint = MinimumSurfaceFeature(
                            new List<double2[]> { union });
                        if (!SurfaceCs2Compatible(union)
                            || (vereint != null
                                && vereint.Distance < SurfaceNeckLimit - 1e-9)) continue;
                        offen[j] = union;
                        offen.RemoveAt(i);
                        geaendert = true;
                        break;
                    }
                    if (geaendert) break;
                }
            }
            return offen;
        }

        /** Kleine, unbaubare Grasringe duerfen flaechentreu an Asphalt wechseln. */
        private static void TransferThinGrassRings(
            List<double2[]> inputGrass, List<double2[]> inputAsphalt,
            out List<double2[]> resultGrass, out List<double2[]> resultAsphalt)
        {
            var grass = inputGrass.Select(ring => ring.ToArray()).ToList();
            var asphalt = inputAsphalt.Select(ring => ring.ToArray()).ToList();
            var changed = true;
            while (changed)
            {
                changed = false;
                for (var grassIndex = 0; grassIndex < grass.Count; grassIndex++)
                {
                    var ring = grass[grassIndex];
                    var neck = MinimumSurfaceFeature(new List<double2[]> { ring });
                    if ((neck == null || neck.Distance >= SurfaceNeckLimit - 1e-9)
                        && SurfaceCs2Compatible(ring))
                    {
                        if (PhaseLog && Math.Abs(SignedArea(ring)) < 10)
                            Console.Error.WriteLine($"      [Band] uebersprungen: "
                                + $"{Math.Abs(SignedArea(ring)):F2} m2, Hals "
                                + $"{neck?.Distance ?? -1:F3} m, cs2ok "
                                + $"{SurfaceCs2Compatible(ring)}");
                        continue;
                    }
                    if (Math.Abs(SignedArea(ring)) > SurfaceCs2MaxTransferArea)
                    {
                        if (PhaseLog)
                            Console.Error.WriteLine($"      [Band] zu gross: "
                                + $"{Math.Abs(SignedArea(ring)):F2} m2 > "
                                + $"{SurfaceCs2MaxTransferArea} m2");
                        continue;
                    }

                    var bestAsphalt = -1;
                    double2[] bestUnion = null;
                    var bestNeck = double.NegativeInfinity;
                    for (var asphaltIndex = 0; asphaltIndex < asphalt.Count; asphaltIndex++)
                    {
                        if (!TrySurfaceUnion(ring, asphalt[asphaltIndex], out var union)
                            || !SurfaceCs2Compatible(union)) continue;
                        var joined = MinimumSurfaceFeature(new List<double2[]> { union });
                        var joinedNeck = joined?.Distance ?? double.PositiveInfinity;
                        if (joinedNeck < SurfaceNeckLimit - 1e-9
                            || joinedNeck <= bestNeck) continue;
                        bestAsphalt = asphaltIndex;
                        bestUnion = union;
                        bestNeck = joinedNeck;
                    }
                    if (bestAsphalt < 0)
                    {
                        if (PhaseLog)
                            Console.Error.WriteLine($"      [Band] kein Nachbar: "
                                + $"{Math.Abs(SignedArea(ring)):F2} m2, Hals "
                                + $"{neck?.Distance ?? -1:F3} m, "
                                + $"{asphalt.Count} Asphaltflaechen geprueft");
                        continue;
                    }
                    grass.RemoveAt(grassIndex);
                    asphalt[bestAsphalt] = bestUnion;
                    changed = true;
                    break;
                }
            }
            resultGrass = MergeAdjacentSurfaces(grass);
            resultAsphalt = MergeAdjacentSurfaces(asphalt);
        }

        /** Ringe desselben Materials disjunkt machen, ohne ihre Vereinigung zu aendern. */
        private static List<double2[]> MakeSurfacesDisjoint(List<double2[]> rings)
        {
            var ordered = rings.Select(ring => ring.ToArray())
                .OrderByDescending(ring => Math.Abs(SignedArea(ring))).ToList();
            var output = new List<double2[]>();
            foreach (var ring in ordered)
            {
                /**
                 * SCHEITERT DAS ENTDOPPELN, BLEIBT DER RING STEHEN.
                 *
                 * Hier wird Belag von Belag abgezogen, nur um doppelte Deckung
                 * zu vermeiden. Liegt ein Ring dabei ganz oder teilweise IN
                 * einem anderen, entsteht ein Ring mit Loch - und
                 * SubtractAsphaltPerPiece wirft dann mit der Meldung
                 * "Pavement cut produced a grass hole that cannot be represented".
                 *
                 * Am 2026-08-18 brachte genau das die ganze Materialstufe zu
                 * Fall: ein Ring von 8002 m2 gegen einen einzigen anderen
                 * ergab fuenf Loecher von je 153,30 m2, die Reparatur brach ab,
                 * und aus 3 Belagflaechen wurden 44.
                 *
                 * Dabei ist doppelte Deckung bei GLEICHEM Material voellig
                 * harmlos: es liegt Asphalt auf Asphalt, sichtbar aendert das
                 * nichts. Der Abbruch kostet dagegen alles.
                 */
                List<double2[]> rest;
                try
                {
                    rest = output.Count == 0
                        ? new List<double2[]> { ring }
                        : SubtractSurfaceRegion(new List<double2[]> { ring }, output);
                }
                catch (Exception)
                {
                    rest = new List<double2[]> { ring };
                }
                output.AddRange(rest);
            }
            return MergeAdjacentSurfaces(output);
        }
    }
}
