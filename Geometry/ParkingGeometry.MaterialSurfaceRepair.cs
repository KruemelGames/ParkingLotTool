using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static partial class ParkingGeometry
    {
        /**
         * Setzt zusaetzliche Knoten auf GENAU eine Kante, nach Lage sortiert.
         * Punkte, die mit einem der beiden Kantenenden zusammenfallen, werden
         * nicht doppelt gesetzt.
         */
        private static double2[] SurfaceInsertOnEdge(
            double2[] ring, int edgeIndex, List<(double T, double2 Point)> additions)
        {
            var output = new List<double2>(ring.Length + additions.Count);
            for (var i = 0; i < ring.Length; i++)
            {
                output.Add(ring[i]);
                if (i != edgeIndex) continue;
                foreach (var item in additions.OrderBy(x => x.T))
                    if (!SurfaceSamePoint(output[output.Count - 1], item.Point)
                        && !SurfaceSamePoint(ring[(i + 1) % ring.Length], item.Point))
                        output.Add(item.Point);
            }
            return output.ToArray();
        }

        /**
         * Bei einem Hals unter 15 cm waere die direkte Schnittkante selbst zu
         * kurz. Zwei 16-cm-Schnitte lassen dazwischen ein Dreieck entstehen.
         * Dieses Reststueck wird ueber seine Basis mit der Nachbarflaeche auf
         * der anderen Seite vereinigt. So bleibt jeder Quadratmillimeter
         * erhalten, ohne eine zu kurze Flaechenkante an CS2 zu schicken.
         *
         * Dieser Schritt fehlte im Port. Ohne ihn fiel die Reparaturschleife
         * auf Teilung und Vereinigung zurueck, die den Hals immer wieder neu
         * erzeugten: bei einem gemessenen Viereck drehte die Schleife 1105 mal
         * statt 5 mal und rechnete 13 Sekunden statt 138 Millisekunden.
         */
        private static bool TryBridgeSurfaceRepair(
            List<double2[]> grass, List<double2[]> asphalt,
            bool sourceIsGrass, SurfaceFeature feature,
            out List<double2[]> resultGrass, out List<double2[]> resultAsphalt)
        {
            resultGrass = null;
            resultAsphalt = null;
            if (!feature.Interior || feature.Distance <= 1e-8
                || feature.Distance >= SurfaceNeckLimit) return false;

            var sourceRings = sourceIsGrass ? grass : asphalt;
            var ring = sourceRings[feature.Ring];
            var n = ring.Length;
            var steps = (feature.Edge - feature.Point + n) % n;
            if (steps < 2 || steps > n - 3) return false;

            var a = ring[feature.Edge];
            var edge = ring[(feature.Edge + 1) % n] - a;
            var edgeLength = Len(edge);
            if (edgeLength < 1e-9) return false;

            const double Target = 0.16;
            var along = Math.Sqrt(Math.Max(0,
                Target * Target - feature.Distance * feature.Distance));
            var dt = along / edgeLength;
            if (feature.T - dt <= 1e-7 || feature.T + dt >= 1 - 1e-7) return false;

            var q0 = a + edge * (feature.T - dt);
            var q1 = a + edge * (feature.T + dt);
            var point = ring[feature.Point];

            var firstPart = new List<double2>();
            for (var i = 0; i <= steps; i++) firstPart.Add(ring[(feature.Point + i) % n]);
            firstPart.Add(q0);
            var restPart = new List<double2> { point, q0, q1 };
            var secondPart = new List<double2> { point, q1 };
            for (var i = steps + 1; i < n; i++) secondPart.Add(ring[(feature.Point + i) % n]);

            var first = SurfaceCcw(firstPart.ToArray());
            var rest = SurfaceCcw(restPart.ToArray());
            var second = SurfaceCcw(secondPart.ToArray());
            foreach (var part in new[] { first, rest, second })
                if (part.Length < 3 || SelfIntersects(part, 2e-6)
                    || Math.Abs(SignedArea(part)) < 1e-10) return false;

            var wanted = Math.Abs(SignedArea(ring));
            var got = Math.Abs(SignedArea(first)) + Math.Abs(SignedArea(rest))
                    + Math.Abs(SignedArea(second));
            if (Math.Abs(got - wanted) > Math.Max(1e-7, wanted * 1e-10)) return false;

            var bestScore = double.NegativeInfinity;
            foreach (var neighborIsGrass in new[] { true, false })
            {
                var neighborRings = neighborIsGrass ? grass : asphalt;
                for (var neighbor = 0; neighbor < neighborRings.Count; neighbor++)
                {
                    if (neighborIsGrass == sourceIsGrass && neighbor == feature.Ring) continue;
                    if (!TrySurfaceUnion(rest, neighborRings[neighbor], out var joined))
                        continue;

                    var candidateGrass = WithoutSurfaceRings(grass,
                        sourceIsGrass ? feature.Ring : -1,
                        neighborIsGrass ? neighbor : -1);
                    var candidateAsphalt = WithoutSurfaceRings(asphalt,
                        sourceIsGrass ? -1 : feature.Ring,
                        neighborIsGrass ? -1 : neighbor);
                    (sourceIsGrass ? candidateGrass : candidateAsphalt).Add(first);
                    (sourceIsGrass ? candidateGrass : candidateAsphalt).Add(second);
                    (neighborIsGrass ? candidateGrass : candidateAsphalt).Add(joined);

                    var sourceFeature = MinimumSurfaceFeature(
                        sourceIsGrass ? candidateGrass : candidateAsphalt);
                    var neighborFeature = MinimumSurfaceFeature(
                        neighborIsGrass ? candidateGrass : candidateAsphalt);
                    var minimum = Math.Min(
                        sourceFeature != null ? sourceFeature.Distance : 1e5,
                        neighborFeature != null ? neighborFeature.Distance : 1e5);
                    var compatible = SurfaceCs2Compatible(first)
                        && SurfaceCs2Compatible(second) && SurfaceCs2Compatible(joined);
                    var score = (compatible ? 1e6 : 0) + Math.Min(minimum, 1e5);
                    if (score <= bestScore) continue;
                    bestScore = score;
                    resultGrass = candidateGrass;
                    resultAsphalt = candidateAsphalt;
                }
            }
            return resultGrass != null;
        }

        /** Kopiert eine Ringliste ohne die angegebenen Indizes (-1 = keiner). */
        private static List<double2[]> WithoutSurfaceRings(
            List<double2[]> rings, int firstDropped, int secondDropped)
        {
            var output = new List<double2[]>(rings.Count);
            for (var i = 0; i < rings.Count; i++)
                if (i != firstDropped && i != secondDropped) output.Add(rings[i]);
            return output;
        }

        /**
         * Liegt ein extrem schmaler Materialkeil an einer anderen Flaeche, wird
         * seine Basis auf BEIDEN Seiten durch dieselben neuen Knoten geteilt.
         * Das Dreieck wechselt damit nur den Besitzer; Gesamtflaeche, gemeinsame
         * Kante und Deckung bleiben exakt erhalten. Kein Punkt wird einseitig
         * bewegt. Fehlte wie die Brueckenreparatur im Port.
         */
        private static bool TryTransferSurfaceNeck(
            List<double2[]> grass, List<double2[]> asphalt,
            bool sourceIsGrass, SurfaceFeature feature,
            out List<double2[]> resultGrass, out List<double2[]> resultAsphalt)
        {
            resultGrass = null;
            resultAsphalt = null;
            var source = (sourceIsGrass ? grass : asphalt)[feature.Ring];
            var sourceA = source[feature.Edge];
            var sourceEdge = source[(feature.Edge + 1) % source.Length] - sourceA;
            var sourceLengthSquared = sourceEdge.x * sourceEdge.x
                                    + sourceEdge.y * sourceEdge.y;
            if (!feature.Interior || sourceLengthSquared < 1e-12) return false;
            var sourceLength = Math.Sqrt(sourceLengthSquared);

            var bestScore = double.NegativeInfinity;
            foreach (var neighborIsGrass in new[] { true, false })
            {
                var neighborRings = neighborIsGrass ? grass : asphalt;
                for (var neighborIndex = 0; neighborIndex < neighborRings.Count;
                     neighborIndex++)
                {
                    if (neighborIsGrass == sourceIsGrass
                        && neighborIndex == feature.Ring) continue;
                    var neighbor = neighborRings[neighborIndex];
                    for (var neighborEdgeIndex = 0; neighborEdgeIndex < neighbor.Length;
                         neighborEdgeIndex++)
                    {
                        var c = neighbor[neighborEdgeIndex];
                        var d = neighbor[(neighborEdgeIndex + 1) % neighbor.Length];
                        var neighborEdge = d - c;
                        // Nur eine exakt gegenlaeufige, auf derselben Geraden
                        // liegende Nachbarkante teilt dieselbe Fuge.
                        if (sourceEdge.x * neighborEdge.x
                            + sourceEdge.y * neighborEdge.y >= 0) continue;
                        if (LineDistance(c) > SurfaceJoinEpsilon
                            || LineDistance(d) > SurfaceJoinEpsilon) continue;

                        var tc = Parameter(c);
                        var td = Parameter(d);
                        var lo = Math.Max(0, Math.Min(tc, td));
                        var hi = Math.Min(1, Math.Max(tc, td));
                        if (hi - lo <= 1e-7 || feature.T <= lo + 1e-7
                            || feature.T >= hi - 1e-7) continue;
                        var u = sourceA + sourceEdge * lo;
                        var v = sourceA + sourceEdge * hi;

                        var splitSource = SurfaceInsertOnEdge(source, feature.Edge,
                            new List<(double, double2)> { (lo, u), (hi, v) });
                        var pointIndex = IndexOfSurfacePoint(
                            splitSource, source[feature.Point]);
                        var splitEdge = IndexOfSurfaceEdge(splitSource, u, v);
                        if (pointIndex < 0 || splitEdge < 0) continue;
                        var steps = (splitEdge - pointIndex + splitSource.Length)
                            % splitSource.Length;
                        if (steps < 2 || steps > splitSource.Length - 3) continue;

                        var point = source[feature.Point];
                        var firstPart = new List<double2>();
                        for (var i = 0; i <= steps; i++)
                            firstPart.Add(splitSource[(pointIndex + i) % splitSource.Length]);
                        var secondPart = new List<double2> { point };
                        for (var i = steps + 1; i < splitSource.Length; i++)
                            secondPart.Add(splitSource[(pointIndex + i) % splitSource.Length]);

                        var first = SurfaceCcw(firstPart.ToArray());
                        var second = SurfaceCcw(secondPart.ToArray());
                        if (first.Length < 3 || second.Length < 3
                            || SelfIntersects(first, 2e-6) || SelfIntersects(second, 2e-6)
                            || Math.Abs(SignedArea(first)) < 1e-10
                            || Math.Abs(SignedArea(second)) < 1e-10) continue;

                        var neighborLengthSquared = neighborEdge.x * neighborEdge.x
                                                  + neighborEdge.y * neighborEdge.y;
                        double NeighborParameter(double2 x) =>
                            ((x.x - c.x) * neighborEdge.x
                           + (x.y - c.y) * neighborEdge.y) / neighborLengthSquared;
                        var inserted = SurfaceInsertOnEdge(neighbor, neighborEdgeIndex,
                            new List<(double, double2)>
                            { (NeighborParameter(v), v), (NeighborParameter(u), u) });
                        var reverseEdge = IndexOfSurfaceEdge(inserted, v, u);
                        if (reverseEdge < 0) continue;
                        var changedList = inserted.ToList();
                        changedList.Insert(reverseEdge + 1, point);
                        var changedNeighbor = SurfaceCcw(changedList.ToArray());
                        if (SelfIntersects(changedNeighbor, 2e-6)) continue;

                        var oldArea = Math.Abs(SignedArea(source))
                                    + Math.Abs(SignedArea(neighbor));
                        var newArea = Math.Abs(SignedArea(first))
                                    + Math.Abs(SignedArea(second))
                                    + Math.Abs(SignedArea(changedNeighbor));
                        if (Math.Abs(newArea - oldArea)
                            > Math.Max(1e-7, oldArea * 1e-10)) continue;

                        var candidateGrass = WithoutSurfaceRings(grass,
                            sourceIsGrass ? feature.Ring : -1,
                            neighborIsGrass ? neighborIndex : -1);
                        var candidateAsphalt = WithoutSurfaceRings(asphalt,
                            sourceIsGrass ? -1 : feature.Ring,
                            neighborIsGrass ? -1 : neighborIndex);
                        (sourceIsGrass ? candidateGrass : candidateAsphalt).Add(first);
                        (sourceIsGrass ? candidateGrass : candidateAsphalt).Add(second);
                        (neighborIsGrass ? candidateGrass : candidateAsphalt)
                            .Add(changedNeighbor);

                        var grassNeck = MinimumSurfaceFeature(candidateGrass);
                        var asphaltNeck = MinimumSurfaceFeature(candidateAsphalt);
                        var compatible = candidateGrass.All(SurfaceCs2Compatible)
                            && candidateAsphalt.All(SurfaceCs2Compatible);
                        var minimum = Math.Min(
                            grassNeck != null ? grassNeck.Distance : 1e5,
                            asphaltNeck != null ? asphaltNeck.Distance : 1e5);
                        var score = (compatible ? 1e6 : 0) + Math.Min(minimum, 1e5);
                        if (score <= bestScore) continue;
                        bestScore = score;
                        resultGrass = candidateGrass;
                        resultAsphalt = candidateAsphalt;
                    }
                }
            }
            return resultGrass != null;

            double LineDistance(double2 p) => Math.Abs(
                sourceEdge.x * (p.y - sourceA.y)
                - sourceEdge.y * (p.x - sourceA.x)) / sourceLength;

            double Parameter(double2 p) => ((p.x - sourceA.x) * sourceEdge.x
                + (p.y - sourceA.y) * sourceEdge.y) / sourceLengthSquared;
        }

        private static int IndexOfSurfacePoint(double2[] ring, double2 point)
        {
            for (var i = 0; i < ring.Length; i++)
                if (SurfaceSamePoint(ring[i], point)) return i;
            return -1;
        }

        private static int IndexOfSurfaceEdge(double2[] ring, double2 from, double2 to)
        {
            for (var i = 0; i < ring.Length; i++)
                if (SurfaceSamePoint(ring[i], from)
                    && SurfaceSamePoint(ring[(i + 1) % ring.Length], to)) return i;
            return -1;
        }

        /**
         * Uebergibt einen konvexen Mikrokeil nur dann, wenn sein Dreieck
         * kantengenau mit einer Gras- oder Asphalt-Nachbarflaeche vereinigt
         * werden kann. Quelle und Ziel erhalten dadurch dieselben Grenzknoten.
         */
        private static bool TryTransferSurfaceVertex(
            List<double2[]> grass, List<double2[]> asphalt,
            bool sourceIsGrass, SurfaceFeature feature,
            out List<double2[]> resultGrass, out List<double2[]> resultAsphalt)
        {
            var candidate = FindSurfaceTransferVertex(
                grass, asphalt, sourceIsGrass, feature);
            resultGrass = candidate?.Grass;
            resultAsphalt = candidate?.Asphalt;
            return candidate != null;
        }

        /**
         * Teilt einen Ring zwischen zwei ZU NAHEN ECKEN.
         *
         * Alle anderen Reparaturwege verlangen einen Lotfuss MITTEN auf einer
         * Kante. Liegt die Enge zwischen zwei Ecken, greift keiner davon und
         * der Hals blieb stehen. Geteilt wird genau zwischen den beiden Ecken:
         * die Schnittkante gehoert beiden Teilen, kein Punkt wandert, die
         * Flaechensumme bleibt exakt. Gegenstueck zu
         * surfaceSplitBetweenCorners im JS-Modell.
         */
        private static bool TrySplitBetweenCorners(
            double2[] ring, SurfaceFeature feature, out double2[][] parts)
        {
            parts = null;
            var n = ring.Length;
            if (n < 4) return false;
            var a = ring[feature.Edge];
            var b = ring[(feature.Edge + 1) % n];
            var point = ring[feature.Point];
            var corner = Len(point - a) <= Len(point - b)
                ? feature.Edge : (feature.Edge + 1) % n;
            var steps = (corner - feature.Point + n) % n;
            if (steps < 2 || steps > n - 2) return false;

            var first = new List<double2>();
            for (var i = 0; i <= steps; i++) first.Add(ring[(feature.Point + i) % n]);
            var second = new List<double2>();
            for (var i = steps; i <= n; i++) second.Add(ring[(feature.Point + i) % n]);

            var candidate = new[] { SurfaceCcw(first.ToArray()), SurfaceCcw(second.ToArray()) };
            foreach (var part in candidate)
                if (part.Length < 3 || SelfIntersects(part, 2e-6)
                    || Math.Abs(SignedArea(part)) < 1e-10) return false;
            var wanted = Math.Abs(SignedArea(ring));
            var got = Math.Abs(SignedArea(candidate[0])) + Math.Abs(SignedArea(candidate[1]));
            if (Math.Abs(got - wanted) > Math.Max(1e-7, wanted * 1e-10)) return false;
            parts = candidate;
            return true;
        }

        private static void AddSurfaceRepairWarning(
            WorkLayout output, bool grassMaterial, List<double2[]> rings,
            SurfaceFeature feature, string reason)
        {
            var ring = feature.Ring >= 0 && feature.Ring < rings.Count
                ? rings[feature.Ring] : Array.Empty<double2>();
            var point = feature.Point >= 0 && feature.Point < ring.Length
                ? ring[feature.Point] : feature.Projection;
            var edgeA = feature.Edge >= 0 && feature.Edge < ring.Length
                ? ring[feature.Edge] : default;
            var edgeB = ring.Length > 0
                ? ring[(feature.Edge + 1 + ring.Length) % ring.Length] : default;
            var material = grassMaterial ? "Gras" : "Asphalt";
            var message = $"{material}: Engstelle {feature.Distance:F6} m bei "
                + $"({point.x:G17}, {point.y:G17}) gegen Kante "
                + $"({edgeA.x:G17}, {edgeA.y:G17})-({edgeB.x:G17}, {edgeB.y:G17}) "
                + $"{reason}; Flaeche bleibt unveraendert.";
            if (!output.Warnings.Contains(message)) output.Warnings.Add(message);
            if (!output.SilentMaterialWarnings) Trace.TraceWarning(message);
        }

        /**
         * ZEITGRENZE FUER DIE MATERIALREPARATUR.
         *
         * Am 2026-08-17 blieb `Build` bei mehreren Formen haengen - gemessen
         * 938 s CPU ohne Ergebnis. Im Spiel ist das eine eingefrorene Vorschau,
         * also der schlimmste denkbare Fehler des Werkzeugs.
         *
         * Die Reparaturschleifen haben zwar ihre 1024er-Grenze, aber sie rufen
         * einander auf und arbeiten auf beliebig vielen Ringen; bei 2231 statt
         * 20 Ringen rechnet das praktisch endlos. Zwei Versuche, die Ursache
         * zu beseitigen, sind gescheitert und stehen als Warnung im Code:
         * Streifengrenzen zusammenfassen (nicht einmal monoton) und den
         * Reihenwinkel neben die Kante legen (verschiebt die Entartung nur auf
         * andere Formen, Form 0 des Schwarms ging danach von 324 ms auf
         * haengend).
         *
         * Deshalb hier die ehrliche Loesung: ein Zeitbudget. Laeuft es ab,
         * fliegt eine Ausnahme, das Werkzeug ANTWORTET aber trotzdem - mit
         * unreparierten Flaechen, die Luecken haben koennen. Genau so macht es
         * der Prototyp seit jeher.
         *
         * BERICHTIGT AM 2026-08-26. Hier stand bis dahin, die Ausnahme werde
         * "vom vorhandenen catch in BuildMaterialSurfaces aufgefangen". Das
         * stimmte nur fuer EINEN der beiden Aufrufwege. `SlabFill` wird auch
         * aus `BuildContext.FillRemainder` gerufen, und dort gab es keinen -
         * die Ausnahme lief bis in `PollCompletedBuild` durch und riss die
         * GANZE Vorschau ab, statt nur die Restfuellung ausfallen zu lassen.
         *
         * Der Nutzer sah davon nur rohen Ausnahmetext und ein leeres Feld.
         * Gemeldet am 2026-08-26 an einer mittelgrossen Flaeche, 1627 Grenzen.
         *
         * Deshalb hat der Abbruch jetzt einen eigenen Typ: `Zeitabbruch` laesst
         * sich gezielt auffangen, ohne echte Fehler mit zu verschlucken.
         */
        internal sealed class Zeitabbruch : InvalidOperationException
        {
            internal Zeitabbruch(string meldung) : base(meldung) { }
        }

        internal static double MaterialBudgetSeconds = 7.0;
        private static System.Diagnostics.Stopwatch _materialClock;

        internal static void StartMaterialBudget()
            => _materialClock = System.Diagnostics.Stopwatch.StartNew();

        /**
         * Waehrend des Rueckfalls darf das Budget nicht nochmal zuschlagen -
         * sonst bleibt genau die Arbeit liegen, die den Rueckfall brauchbar
         * macht. Siehe den Rueckfall in BuildMaterialSurfaces.
         */
        internal static bool BudgetAus;

        private static void CheckMaterialBudget(string wo)
        {
            if (BudgetAus) return;
            if (_materialClock == null) return;
            if (_materialClock.Elapsed.TotalSeconds <= MaterialBudgetSeconds) return;
            throw new Zeitabbruch(
                $"Materialreparatur ueberschreitet {MaterialBudgetSeconds:F1} s in {wo}; "
                + "die Flaechen bleiben unrepariert.");
        }

        private static void RepairMaterialNecks(
            WorkLayout output, ref List<double2[]> grass,
            ref List<double2[]> asphalt, ref double expectedArea)
        {
            var grassBlocked = false;
            var asphaltBlocked = false;
            for (var guard = 0; guard < 1024; guard++)
            {
                CheckMaterialBudget("RepairMaterialNecks");
                var grassFeature = grassBlocked ? null : MinimumSurfaceFeature(grass);
                var asphaltFeature = asphaltBlocked ? null : MinimumSurfaceFeature(asphalt);
                if (grassFeature != null
                    && grassFeature.Distance >= SurfaceNeckLimit - 1e-9) grassFeature = null;
                if (asphaltFeature != null
                    && asphaltFeature.Distance >= SurfaceNeckLimit - 1e-9) asphaltFeature = null;
                if (grassFeature == null && asphaltFeature == null) return;

                var sourceIsGrass = asphaltFeature == null
                    || grassFeature != null && grassFeature.Distance <= asphaltFeature.Distance;
                var feature = sourceIsGrass ? grassFeature : asphaltFeature;
                var rings = sourceIsGrass ? grass : asphalt;

                // Ein kurzer Knoten auf einer geraden Kante ist kein Keil.
                var simplified = MergeCollinear(rings[feature.Ring], 1e-9);
                if (simplified.Length < rings[feature.Ring].Length
                    && !SelfIntersects(simplified, 2e-6)
                    && Math.Abs(Math.Abs(SignedArea(simplified))
                        - Math.Abs(SignedArea(rings[feature.Ring]))) <= 1e-7)
                {
                    rings[feature.Ring] = SurfaceCcw(simplified);
                    continue;
                }

                if (sourceIsGrass && feature.Distance < 0.05
                    && TryTransferCoveredSurfaceVertex(
                        grass, output.Bay, asphalt, feature, out var transferred))
                {
                    expectedArea -= transferred;
                    continue;
                }

                if (feature.Distance < 0.05
                    && TryTransferSurfaceVertex(grass, asphalt, sourceIsGrass, feature,
                        out var transferredGrass, out var transferredAsphalt))
                {
                    grass = transferredGrass;
                    asphalt = transferredAsphalt;
                    continue;
                }

                if (!sourceIsGrass && TryCollapseAreaPreservingSurfaceEdge(
                    ref grass, ref asphalt, false, feature)) continue;

                if (TryBridgeSurfaceRepair(grass, asphalt, sourceIsGrass, feature,
                        out var bridgedGrass, out var bridgedAsphalt))
                {
                    grass = bridgedGrass;
                    asphalt = bridgedAsphalt;
                    continue;
                }

                if (feature.Distance < 0.05
                    && TryTransferSurfaceNeck(grass, asphalt, sourceIsGrass, feature,
                        out var neckGrass, out var neckAsphalt))
                {
                    grass = neckGrass;
                    asphalt = neckAsphalt;
                    continue;
                }

                // Enge zwischen zwei ECKEN: dafuer gibt es keinen anderen
                // Weg, weil alle `Interior` verlangen. Teilen statt stehen
                // lassen.
                if (!feature.Interior && TrySplitBetweenCorners(
                    rings[feature.Ring], feature, out var cornerParts))
                {
                    rings.RemoveAt(feature.Ring);
                    rings.InsertRange(feature.Ring, cornerParts);
                    continue;
                }

                if (TrySplitSurfaceAtNeck(rings[feature.Ring], feature,
                    out var first, out var second))
                {
                    if (feature.Distance < 0.05
                        && (!SurfaceCs2Compatible(first) || !SurfaceCs2Compatible(second))
                        && TrySurfaceDiagonalSplit(rings[feature.Ring],
                            out var diagonalFirst, out var diagonalSecond))
                    {
                        first = diagonalFirst;
                        second = diagonalSecond;
                    }
                    rings.RemoveAt(feature.Ring);
                    rings.Insert(feature.Ring, second);
                    rings.Insert(feature.Ring, first);
                    continue;
                }

                /**
                 * Sehne suchen, wenn gar nichts greift.
                 *
                 * Ist die Engstelle die schmale MUENDUNG eines Einschnitts,
                 * passen alle Stufen darueber: TrySplitBetweenCorners prueft
                 * auf eine Flaechen-SUMME, dort subtrahieren sich die Teile
                 * aber (am Prototyp gemessen 736,23 - 107,16 = 629,07), und
                 * TrySplitSurfaceAtNeck verlangt `Interior`, was bei einer
                 * Enge zwischen zwei Ecken nicht gilt.
                 *
                 * Erst gezielt vom Engstellenpunkt aus - das ist O(n) und
                 * durchtrennt die Engstelle wirklich -, nur bei kleinen Ringen
                 * notfalls ueber alle Paare. Jeder Kandidat wird EINZELN
                 * daraufhin geprueft, ob die Engstelle echt groesser wird;
                 * sonst blockiert ein mittelmaessiges Ergebnis der gezielten
                 * Suche die gruendliche.
                 */
                if (TryChordSplit(rings[feature.Ring], feature,
                    out var chordFirst, out var chordSecond))
                {
                    rings.RemoveAt(feature.Ring);
                    rings.Insert(feature.Ring, chordSecond);
                    rings.Insert(feature.Ring, chordFirst);
                    continue;
                }

                var joined = BestSurfaceUnion(rings, feature.Ring);
                if (joined != null)
                {
                    ReplaceSurfaceUnion(rings, feature.Ring, joined);
                    continue;
                }
                if (TryCollapseAreaPreservingSurfaceEdge(
                    ref grass, ref asphalt, sourceIsGrass, feature)) continue;

                if (sourceIsGrass && output.AllowThinGrassTransfer)
                {
                    var before = MinimumSurfaceFeature(grass)?.Distance
                        ?? double.PositiveInfinity;
                    TransferThinGrassRings(grass, asphalt,
                        out var thinGrass, out var thinAsphalt);
                    var after = MinimumSurfaceFeature(thinGrass)?.Distance
                        ?? double.PositiveInfinity;
                    if (after > before + 1e-9)
                    {
                        grass = thinGrass;
                        asphalt = thinAsphalt;
                        continue;
                    }
                }

                AddSurfaceRepairWarning(output, sourceIsGrass, rings, feature,
                    "has no neighbouring surface that could take it");
                if (sourceIsGrass) grassBlocked = true;
                else asphaltBlocked = true;
            }

            foreach (var sourceIsGrass in new[] { true, false })
            {
                var rings = sourceIsGrass ? grass : asphalt;
                var feature = MinimumSurfaceFeature(rings);
                if (feature != null && feature.Distance < SurfaceNeckLimit - 1e-9)
                    AddSurfaceRepairWarning(output, sourceIsGrass, rings, feature,
                        "blieb nach 1024 Reparaturschritten offen");
            }
        }

        private static void RepairMaterialCompatibility(
            WorkLayout output, List<double2[]> grass, List<double2[]> asphalt)
        {
            var grassBlocked = false;
            var asphaltBlocked = false;
            /**
             * WASSERMARKE statt `FindIndex` von vorn.
             *
             * `FindIndex` prueft bei JEDEM der bis zu 1024 Durchlaeufe die
             * ganze Liste neu - also auch alle Ringe, die schon in der Runde
             * davor vertraeglich waren. Am 2026-08-17 ueber 60 Formen gemessen
             * war das der groesste Einzelposten der gesamten Bauzeit:
             * `RepairMaterialCompatibility` 142,3 s, davon nur 3,7 s in
             * `TrySurfaceUnion` und 26,8 s in `MinimumSurfaceFeature` - der
             * Rest steckte in diesem Rescan.
             *
             * Dass die Marke sicher ist, folgt aus den beiden Reparaturen:
             * `ReplaceSurfaceUnion` entfernt die Ringe an `bad` und
             * `choice.Other` und haengt die Vereinigung HINTEN an, der Split
             * ersetzt nur `bad`. Ringe VOR dem kleineren der beiden Indizes
             * bleiben in beiden Faellen unveraendert - Inhalt wie Position.
             * Deshalb wird die Marke nach jeder Reparatur genau dorthin
             * zurueckgesetzt.
             */
            var geprueftGras = 0;
            var geprueftBelag = 0;
            int ErsterUnvertraeglicher(List<double2[]> ringe, ref int ab)
            {
                while (ab < ringe.Count)
                {
                    if (!SurfaceCs2Compatible(ringe[ab])) return ab;
                    ab++;
                }
                return -1;
            }
            for (var guard = 0; guard < 1024; guard++)
            {
                CheckMaterialBudget("RepairMaterialCompatibility");
                var sourceIsGrass = true;
                var bad = grassBlocked ? -1
                    : ErsterUnvertraeglicher(grass, ref geprueftGras);
                if (bad < 0)
                {
                    sourceIsGrass = false;
                    bad = asphaltBlocked ? -1
                        : ErsterUnvertraeglicher(asphalt, ref geprueftBelag);
                }
                if (bad < 0) return;
                var rings = sourceIsGrass ? grass : asphalt;
                var uhrB = PhaseLog ? System.Diagnostics.Stopwatch.StartNew() : null;
                var joined = BestSurfaceUnion(rings, bad);
                if (uhrB != null)
                {
                    uhrB.Stop();
                    MaterialZeiten.TryGetValue("  BestSurfaceUnion", out var bB2);
                    MaterialZeiten["  BestSurfaceUnion"] = bB2 + uhrB.Elapsed.TotalMilliseconds;
                }
                if (joined != null && SurfaceCs2Compatible(joined.Union))
                {
                    ReplaceSurfaceUnion(rings, bad, joined);
                    var abUnion = Math.Min(bad, joined.Other);
                    if (sourceIsGrass) geprueftGras = Math.Min(geprueftGras, abUnion);
                    else geprueftBelag = Math.Min(geprueftBelag, abUnion);
                    continue;
                }
                /**
                 * ERST GEZIELT, DANN GRUENDLICH - und gruendlich nur bei
                 * kleinen Ringen.
                 *
                 * `TrySurfaceDiagonalSplit` probiert JEDES Punktepaar, kopiert
                 * je zwei Arrays und prueft beide auf Selbstschnitt. Bei einem
                 * Ring mit 300 Punkten sind das zehntausende Sehnen. Am
                 * 2026-08-17 ueber 60 Formen gemessen war das der groesste
                 * Einzelposten ueberhaupt: 143,5 s von 383 s Materialzeit -
                 * praktisch die gesamte Zeit von
                 * `RepairMaterialCompatibility`.
                 *
                 * `TryChordSplit` macht es laengst richtig: erst die gezielte
                 * Suche vom Engstellenpunkt aus (O(n)), die gruendliche nur
                 * `if (ring.Length <= 64)`. Diese Aufrufstelle hatte die
                 * Bremse nie bekommen.
                 */
                var uhrS = PhaseLog ? System.Diagnostics.Stopwatch.StartNew() : null;
                double2[] first = null, second = null;
                var engstelle = MinimumSurfaceFeature(
                    new List<double2[]> { rings[bad] });
                var geteilt = engstelle != null
                    && TrySplitFromNeckPoint(rings[bad], engstelle, out first, out second);
                if (!geteilt && rings[bad].Length <= 64)
                    geteilt = TrySurfaceDiagonalSplit(rings[bad], out first, out second);
                if (uhrS != null)
                {
                    uhrS.Stop();
                    MaterialZeiten.TryGetValue("  Teilen", out var bS);
                    MaterialZeiten["  Teilen"] = bS + uhrS.Elapsed.TotalMilliseconds;
                }
                if (geteilt)
                {
                    rings.RemoveAt(bad);
                    rings.Insert(bad, second);
                    rings.Insert(bad, first);
                    if (sourceIsGrass) geprueftGras = Math.Min(geprueftGras, bad);
                    else geprueftBelag = Math.Min(geprueftBelag, bad);
                    continue;
                }
                var feature = MinimumSurfaceFeature(new List<double2[]> { rings[bad] })
                    ?? new SurfaceFeature
                    {
                        Ring = 0,
                        Point = 0,
                        Edge = rings[bad].Length > 1 ? 1 : 0,
                        Projection = rings[bad].Length > 0 ? rings[bad][0] : default,
                        Distance = 0,
                    };
                feature.Ring = bad;
                AddSurfaceRepairWarning(output, sourceIsGrass, rings, feature,
                    "cannot be decomposed further for CS2");
                if (sourceIsGrass) grassBlocked = true;
                else asphaltBlocked = true;
            }
        }
    }
}
