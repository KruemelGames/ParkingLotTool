using static ParkingLotTool.Tools.ParkingLotTexte;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Colossal.Collections;
using Colossal.Mathematics;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * Fehlerstellen markieren und auswerten lassen.
     *
     * WOFUER: Wer den Mod benutzt und nach dem Bauen etwas Krummes sieht -
     * eine Grasnarbe, eine fehlende Bucht, eine Fläche, die nicht anschliesst
     * - soll das MELDEN koennen, ohne selbst Geometrie lesen zu muessen. Ein
     * Abzug mit 98 Grasflaechen beantwortet die Frage "wo ist die Narbe?"
     * naemlich nicht.
     *
     * Also: Stelle anklicken, Bericht schreiben lassen. Der Bericht sagt in
     * Klartext, WAS an dieser Stelle liegt und WAS daran nicht stimmt - und
     * ist kurz genug, um ihn zu verschicken.
     *
     * DIE PRUEFUNGEN kommen aus dem, was uns bisher wirklich gebissen hat:
     *   - kein Belag an der Stelle          -> die Narbe selbst
     *   - Luecke zwischen zwei Flaechen     -> die Ursache der Narbe
     *   - Flaeche ohne Dreiecke             -> von CS2 verworfen, unsichtbar
     *   - Form unter CS2s Mindestabstand    -> wird demnaechst verworfen
     *   - Teil ohne gemeinsamen Besitzer    -> bleibt beim Abriss stehen
     */
    public sealed partial class ParkingLotToolSystem
    {
        /** Umkreis, den eine Markierung auswertet. */
        private const float MarkerRadius = 12f;

        /**
         * Ab wann zwei Flaechenraender als "Luecke" gelten.
         *
         * Unter 1 cm ist es dieselbe Kante mit Gleitkommarauschen, ueber
         * einem halben Meter ist es Absicht (Gruenstreifen). Dazwischen liegt
         * die Narbe.
         */
        private const float GapMin = 0.01f;
        private const float GapMax = 0.5f;

        /** CS2 verwirft Flaechenformen unterhalb von m_SnapDistance / 2. */
        private const float Cs2MinNodeDistance = 0.375f;

        private readonly List<float3> _markers = new List<float3>();

        internal bool MarkerMode { get; private set; }
        internal IReadOnlyList<float3> Markers => _markers;

        internal void ToggleMarkerMode() => SetMarkerMode(!MarkerMode);

        internal void SetMarkerMode(bool on)
        {
            if (MarkerMode == on) { PublishMarkerState(); return; }
            MarkerMode = on;
            PublishMarkerState();
            Mod.log.Info(MarkerMode
                ? "PLT-Markiermodus AN: Linksklick markiert eine Fehlerstelle, "
                  + "Rechtsklick nimmt die letzte zurück, Alt+P schreibt den "
                  + "Bericht. Alt+M schaltet wieder aus."
                : $"PLT-Markiermodus AUS ({_markers.Count} Markierungen bleiben "
                  + "erhalten).");
        }

        internal void AddMarker(float3 position)
        {
            _markers.Add(position);
            PublishMarkerState();
            Mod.log.Info($"PLT-Markierung {_markers.Count} gesetzt bei "
                + $"{position.x:F1} / {position.z:F1}.");
        }

        internal void RemoveLastMarker()
        {
            if (_markers.Count == 0) return;
            _markers.RemoveAt(_markers.Count - 1);
            PublishMarkerState();
            Mod.log.Info($"PLT-Markierung zurückgenommen, noch {_markers.Count}.");
        }

        internal void ClearMarkers()
        {
            _markers.Clear();
            PublishMarkerState();
        }

        /**
         * Was gerade in der Vorschau steht - vollstaendig und nachrechenbar.
         *
         * Das ist die einzige Quelle, die stimmt, solange nichts gebaut ist:
         * Polygon, Einstellungen samt Rechenweg und das Ergebnis, das der
         * Nutzer vor sich sieht.
         */
        private void BeschreibeVorschau(StringBuilder text)
        {
            if (!_closed || _worldPoints == null || _worldPoints.Count < 3)
            {
                text.AppendLine("No closed preview - the marks refer to something already built.");
                text.AppendLine();
                return;
            }
            text.AppendLine("CURRENT PREVIEW (what is on screen right now)");
            text.AppendLine("  Polygon: " + string.Join(" | ", _worldPoints.Select(
                p => p.x.ToString("R", CultureInfo.InvariantCulture) + " / "
                    + p.z.ToString("R", CultureInfo.InvariantCulture))));
            var einstellungen = _areaPreviewSettings
                ?? (_uiSystem != null ? _uiSystem.CurrentSettings() : null);
            if (einstellungen != null)
                text.AppendLine("  Settings: " + Describe(einstellungen));
            if (_areaPreviewLayout != null)
                text.AppendLine($"  Result: {_areaPreviewLayout.Stalls} stalls, "
                    + $"{_areaPreviewLayout.Aisles} Fahrgassen, Winkel "
                    + $"{_areaPreviewLayout.Angle.ToString("F2", CultureInfo.InvariantCulture)} Grad, "
                    + $"{_entrances.Count} entrance(s)");
            else
                text.AppendLine("  Result: no finished preview yet.");
            text.AppendLine();
        }

        /** Liegt die Markierung im Polygon der laufenden Vorschau? */
        private bool VorschauEnthaelt(float3 marker)
        {
            if (!_closed || _worldPoints == null || _worldPoints.Count < 3) return false;
            var ring = new float2[_worldPoints.Count];
            for (var i = 0; i < _worldPoints.Count; i++)
                ring[i] = new float2(_worldPoints[i].x, _worldPoints[i].z);
            return PointInPolygon(marker.xz, ring);
        }

        private void PublishMarkerState()
            => _uiSystem?.SetMarkerState(MarkerMode, _markers.Count);

        /**
         * Schreibt den Bericht sofort, unabhaengig vom grossen Abzug.
         *
         * Der Knopf im Panel soll genau eine Datei erzeugen, die man
         * verschicken kann - nicht nebenbei ein Megabyte JSON.
         */
        internal void WriteMarkerReportNow()
        {
            try
            {
                var folder = System.IO.Path.Combine(
                    UnityEngine.Application.persistentDataPath, "Logs");
                System.IO.Directory.CreateDirectory(folder);
                var path = System.IO.Path.Combine(folder,
                    "ParkingLotTool-report-"
                    + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
                System.IO.File.WriteAllText(path, BuildMarkerReport(),
                    new UTF8Encoding(false));
                _uiSystem?.SetReportPath(path);
                Mod.log.Info($"PLT-Fehlerbericht geschrieben: {path} "
                    + $"({_markers.Count} Markierung(en)).");
                _debugTooltipSystem?.Show(T(
                    $"Bericht geschrieben ({_markers.Count} Markierungen).",
                    $"Report written ({_markers.Count} marks)."));
            }
            catch (Exception exception)
            {
                Mod.log.Error(exception, "PLT-Fehlerbericht konnte nicht "
                    + "geschrieben werden.");
                _uiSystem?.SetReportPath("ERROR: " + exception.Message);
            }
        }

        /**
         * Der Bericht.
         *
         * Bewusst Klartext und bewusst kurz: er soll verschickt und gelesen
         * werden, nicht ausgewertet. Der grosse JSON-Abzug bleibt daneben
         * bestehen fuer den Fall, dass der Bericht nicht reicht.
         */
        /**
         * WAS STEHT WIRKLICH IN DER STADT - NICHT NUR, WAS AM ZEIGER HAENGT.
         *
         * Ausloeser ist der Testerabzug VZ4A vom 2026-09-23 16:51. Der
         * Tester schrieb *"when i load in it doesnt look the same, the grass
         * disappears and it color is way lighter"*. Sein Bericht sagte dazu
         * genau nichts: "no finished preview yet", "No marks placed".
         *
         * Im JSON daneben stand es die ganze Zeit -
         *
         *     124 Flaechen, Besitzer 1302612
         *      94 -> Prefab 3614670   nicht aufloesbar
         *      30 -> Prefab 3614671   nicht aufloesbar
         *
         * - aber das liest niemand, der einen Fehler meldet. Der Bericht ist
         * das, was verschickt wird; was nur im Abzug steht, existiert fuer
         * den Meldeweg nicht.
         *
         * Eine Flaeche ohne aufloesbares Prefab hat kein Material. Genau das
         * beschreibt der Tester. Diese Zeile beantwortet seine Meldung also
         * in einem Satz, und zwar OHNE Markierung und OHNE Vorschau - denn
         * wer einen kaputt geladenen Parkplatz sieht, hat beides nicht.
         */
        private void BeschreibeGebauteParkplaetze(StringBuilder text)
        {
            EntityQuery abfrage;
            try
            {
                abfrage = GetEntityQuery(
                    ComponentType.ReadOnly<ParkingLotPartRelation>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.Exclude<Deleted>(),
                    ComponentType.Exclude<Temp>());
            }
            catch (Exception ausnahme)
            {
                text.AppendLine("BUILT PARKING LOTS");
                text.AppendLine("  could not be read: " + ausnahme.Message);
                text.AppendLine();
                return;
            }

            using var teile = abfrage.ToEntityArray(Allocator.Temp);
            var lots = new HashSet<Entity>();
            var tot = new Dictionary<int, int>();
            var totGesamt = 0;
            for (var i = 0; i < teile.Length; i++)
            {
                var teil = teile[i];
                var lot = EntityManager
                    .GetComponentData<ParkingLotPartRelation>(teil).Lot;
                if (lot != Entity.Null) lots.Add(lot);
                var prefab = EntityManager
                    .GetComponentData<PrefabRef>(teil).m_Prefab;
                var lebt = false;
                try
                {
                    lebt = _prefabSystem != null
                        && _prefabSystem.TryGetPrefab<PrefabBase>(
                            prefab, out var gefunden)
                        && gefunden != null;
                }
                catch { lebt = false; }
                if (lebt) continue;
                tot.TryGetValue(prefab.Index, out var bisher);
                tot[prefab.Index] = bisher + 1;
                totGesamt++;
            }

            text.AppendLine("BUILT PARKING LOTS (what is actually in the city)");
            if (teile.Length == 0)
            {
                text.AppendLine("  none - nothing built by this mod is in "
                    + "this save.");
                text.AppendLine();
                return;
            }
            text.AppendLine("  " + lots.Count + " lot(s), " + teile.Length
                + " part(s).");
            if (totGesamt == 0)
            {
                text.AppendLine("  All parts resolve to a prefab.");
                text.AppendLine();
                return;
            }

            text.AppendLine("  PROBLEM: " + totGesamt + " of " + teile.Length
                + " part(s) point at a prefab that no longer exists.");
            foreach (var paar in tot.OrderByDescending(p => p.Value))
                text.AppendLine("    prefab index " + paar.Key + ": "
                    + paar.Value + " part(s)");
            text.AppendLine("  Such parts have no material. They look pale "
                + "and lose their surface - grass turns into bare ground.");
            text.AppendLine("  This happens when a surface the lot was built "
                + "with is gone: a surface mod that is no longer loaded, or "
                + "one whose prefab is named differently now. The mod log "
                + "names the missing ones at startup (search for "
                + "'PLT-Vorflaeche').");
            text.AppendLine();
        }

        internal string BuildMarkerReport()
        {
            var text = new StringBuilder();
            text.AppendLine("Parking Lot Tool - problem report");
            text.AppendLine("created " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            text.AppendLine();
            BeschreibeVorschau(text);
            BeschreibeGebauteParkplaetze(text);

            if (_markers.Count == 0)
            {
                text.AppendLine("No marks placed.");
                text.AppendLine();
                text.AppendLine("How to use it: Alt+M turns on marking mode, "
                    + "left click marks the spot that is wrong, Alt+P writes this report.");
                return text.ToString();
            }

            /**
             * ZUERST DIE GESAMTBILANZ DES PARKPLATZES.
             *
             * Der Nutzer meldet "zu viele kleine Flächen" oder "Streifen
             * statt einer Fläche". Das ist keine Aussage über 12 m Umkreis,
             * sondern über den ganzen Parkplatz - und genau diese Zahl fehlte
             * bisher im Bericht.
             *
             * Sie ist zugleich die Gegenprobe zu meiner Rechnung ausserhalb
             * des Spiels: dort wird dasselbe Polygon nachgerechnet. Weichen
             * die Zahlen ab, lügt die Rechnung, nicht das Spiel.
             */
            // Die naechstgelegene Flaeche kann selbst besitzerlos sein - dann
            // fuehrt sie zu keinem Parkplatz. Deshalb der Reihe nach probieren,
            // statt nur die erste zu nehmen; sonst bleibt die Bilanz leer.
            var lotFuerBilanz = Entity.Null;
            foreach (var marker in _markers)
            {
                foreach (var flaeche in CollectNearbyAreas(marker))
                {
                    lotFuerBilanz = LotOwnerOf(flaeche.Entity);
                    if (lotFuerBilanz != Entity.Null) break;
                }
                if (lotFuerBilanz != Entity.Null) break;
            }
            DescribeLotSurfaces(text, lotFuerBilanz);

            for (var i = 0; i < _markers.Count; i++)
            {
                var marker = _markers[i];
                text.AppendLine($"--- Mark {i + 1} at "
                    + marker.x.ToString("F2", CultureInfo.InvariantCulture) + " / "
                    + marker.z.ToString("F2", CultureInfo.InvariantCulture)
                    + $" (radius {MarkerRadius:F0} m)");
                DescribeMarker(text, marker);
                text.AppendLine();
            }
            return text.ToString();
        }

        /**
         * Der Parkplatz zu einer Fläche - NICHT die Besitzerwurzel.
         *
         * `RootOwnerOf` steigt bis ganz nach oben und landet bei der
         * Kartenkachel: der erste Bericht vom 2026-08-17 meldete deshalb
         * "Parkplatz: ... (heißt: Map Tile)" und "kein Flächenbesitzer
         * gefunden". Unsere Lot-Fläche ist der ERSTE Vorfahre, der selbst
         * Unterflächen hält; darüber kommt nur noch das Gelände.
         */
        private Entity LotOwnerOf(Entity area)
        {
            var current = area;
            for (var guard = 0; guard < 8; guard++)
            {
                if (!EntityManager.HasComponent<Owner>(current)) break;
                var next = EntityManager.GetComponentData<Owner>(current).m_Owner;
                if (next == Entity.Null || next == current) break;
                if (EntityManager.HasBuffer<Game.Areas.SubArea>(next)) return next;
                current = next;
            }
            return Entity.Null;
        }

        /**
         * Alle Flächen EINES Parkplatzes auszählen, nicht nur die im Umkreis.
         *
         * Gelesen wird der `SubArea`-Puffer des Besitzers - dieselbe Kette,
         * an der auch der Bulldozer entlangläuft. Wer keinen Puffer hat, hat
         * keine Unterflächen; das ist kein Fehler, sondern ein leerer Bericht.
         */
        private void DescribeLotSurfaces(StringBuilder text, Entity lot)
        {
            if (lot == Entity.Null
                || !EntityManager.HasBuffer<Game.Areas.SubArea>(lot))
            {
                text.AppendLine("Overall: no surface owner found.");
                text.AppendLine();
                return;
            }

            var nachPrefab = new Dictionary<string, List<float>>();
            var ohneDreiecke = 0;
            var unterMindestkante = 0;
            var subAreas = EntityManager.GetBuffer<Game.Areas.SubArea>(lot, true);
            for (var i = 0; i < subAreas.Length; i++)
            {
                var area = subAreas[i].m_Area;
                if (area == Entity.Null
                    || !EntityManager.HasBuffer<Game.Areas.Node>(area)) continue;
                var nodes = EntityManager.GetBuffer<Game.Areas.Node>(area, true);
                if (nodes.Length < 3) continue;

                var doppelt = 0f;
                var kuerzeste = float.MaxValue;
                for (var n = 0; n < nodes.Length; n++)
                {
                    var a = nodes[n].m_Position.xz;
                    var b = nodes[(n + 1) % nodes.Length].m_Position.xz;
                    doppelt += a.x * b.y - b.x * a.y;
                    kuerzeste = math.min(kuerzeste, math.distance(a, b));
                }
                var groesse = math.abs(doppelt) * 0.5f;

                var name = PrefabNameOf(area);
                if (!nachPrefab.TryGetValue(name, out var liste))
                    nachPrefab[name] = liste = new List<float>();
                liste.Add(groesse);

                if (EntityManager.HasBuffer<Game.Areas.Triangle>(area)
                    && EntityManager.GetBuffer<Game.Areas.Triangle>(area, true)
                        .Length == 0) ohneDreiecke++;
                if (kuerzeste < Cs2MinNodeDistance) unterMindestkante++;
            }

            text.AppendLine("=== GESAMTBILANZ DIESES PARKPLATZES ===");
            if (nachPrefab.Count == 0)
            {
                text.AppendLine("  The owner has no sub-surfaces.");
                text.AppendLine();
                return;
            }

            foreach (var paar in nachPrefab)
            {
                var liste = paar.Value;
                liste.Sort();
                var summe = 0f;
                foreach (var f in liste) summe += f;
                var mitte = liste[liste.Count / 2];
                var winzig = 0;
                foreach (var f in liste) if (f < 1f) winzig++;
                text.AppendLine($"  {paar.Key}: {liste.Count} surface(s), "
                    + $"{summe:F0} m2 total, largest {liste[liste.Count - 1]:F0} m2, "
                    + $"median {mitte:F1} m2, "
                    + $"{winzig} of them under 1 m2");
            }
            text.AppendLine($"  {ohneDreiecke} surface(s) without triangles (invisible), "
                + $"{unterMindestkante} with an edge below "
                + $"{Cs2MinNodeDistance:F3} m");
            text.AppendLine();
        }

        private sealed class NearbyArea
        {
            internal Entity Entity;
            internal string Prefab;
            internal float2[] Polygon;
            internal int Triangles;
            internal Entity Owner;
            internal float ShortestEdge;
            internal bool Covers;
            internal float Distance;
        }

        private void DescribeMarker(StringBuilder text, float3 marker)
        {
            var areas = CollectNearbyAreas(marker);
            if (areas.Count == 0)
            {
                text.AppendLine("  There is no surface here at all. Either the mark is beside the "
                    + "parking lot, or nothing was built.");
                return;
            }

            /**
             * ZUERST: WELCHER Parkplatz ist gemeint.
             *
             * Ohne das ist der Bericht wertlos, sobald zwei Parkplaetze
             * nebeneinander liegen. Ermittelt wird die Besitzerwurzel der
             * naechsten Flaeche - dieselbe Kette, an der auch der Bulldozer
             * entlanglaeuft.
             */
            var lot = LotOwnerOf(areas[0].Entity);
            var lotName = OwnerDisplayName(lot);

            // Und DANN: woraus er gerechnet wurde. Zugeordnet ueber das
            // gezogene Polygon, NICHT ueber den Namen - den kann der Nutzer
            // aendern, und zwei Parkplaetze duerfen gleich heissen.
            // DIE LAUFENDE VORSCHAU SCHLAEGT DAS BAUPROTOKOLL.
            //
            // Fehler vom 2026-08-21: der Nutzer markierte eine Vorschau, und
            // der Bericht schrieb ihm vier Parkplaetze vom 18. und 20. August
            // hin - laengst abgerissene Bauten. Das Protokoll weiss nichts vom
            // Abriss, seine Zeilen bleiben ewig stehen, und `FindBuildRecord`
            // nimmt den juengsten Eintrag, der den Punkt ENTHAELT. Auf
            // mehrfach ueberbautem Boden trifft das immer irgendetwas Altes.
            // Ich habe daraufhin am falschen Polygon gemessen.
            if (VorschauEnthaelt(marker))
            {
                text.AppendLine("  From the CURRENT PREVIEW (not built) - polygon and settings are "
                    + "at the top of this report.");
                return;
            }

            var record = FindBuildRecord(marker.xz, lotName);
            text.AppendLine("  Parking lot: "
                + (record != null ? record.Id : "no id")
                + (string.IsNullOrEmpty(lotName) ? "" : $"  (named: \"{lotName}\")"));
            if (record != null)
            {
                text.AppendLine($"  Built on {record.When}");
                text.AppendLine($"  Settings: {record.Settings}");
                text.AppendLine($"  Result back then: {record.Result}");
                text.AppendLine("  Drawn polygon: " + FormatSite(record.Site));
                text.AppendLine("  CAUTION: from the build journal. It knows nothing about demolition - "
                    + "this parking lot may be long gone.");
            }
            else
            {
                text.AppendLine("  Kein Bauprotokoll zu dieser Stelle gefunden - "
                    + "der Parkplatz stammt vermutlich aus einer älteren "
                    + "Version oder von einem anderen Rechner.");
            }

            var covering = areas.FindAll(a => a.Covers);
            text.AppendLine($"  {areas.Count} surface(s) in range, "
                + $"{covering.Count} of them directly under the mark.");

            /**
             * 1. Die Narbe selbst - und WELCHES Material fehlt.
             *
             * Der Bericht vom 2026-08-18 hatte hier eine Luecke: der Nutzer
             * markierte 14 Stellen als "fehlender Asphalt", und der Bericht
             * meldete nichts, weil dort Gras lag. Geprueft wurde nur, OB eine
             * Flaeche den Punkt deckt - nicht welche. Ein Parkplatz, auf dem
             * Gras liegt, wo Belag hingehoert, ist aber genau der gemeldete
             * Fehler.
             */
            var deckendGras = covering.Count(a => a.Prefab == GrassSurfaceName);
            var deckendBelag = covering.Count(a => a.Prefab == PavementSurfaceName);
            if (covering.Count == 0)
            {
                var nearest = areas[0];
                text.AppendLine("  FINDING hole: there is no surface at this spot. "
                    + $"The nearest one ('{nearest.Prefab}') is "
                    + $"{nearest.Distance:F2} m away.");
            }
            else
            {
                text.AppendLine($"  Material here: {deckendBelag} pavement, "
                    + $"{deckendGras} grass"
                    + (covering.Count - deckendBelag - deckendGras > 0
                        ? $", {covering.Count - deckendBelag - deckendGras} foreign"
                        : ""));
                if (deckendBelag == 0 && deckendGras > 0)
                {
                    var naechsterBelag = areas
                        .Where(a => a.Prefab == PavementSurfaceName)
                        .OrderBy(a => a.Distance).FirstOrDefault();
                    text.AppendLine("  FINDING missing pavement: there is only grass here. "
                        + (naechsterBelag != null
                            ? $"The nearest pavement is {naechsterBelag.Distance:F2} m away."
                            : "There is no pavement in range at all."));
                }
                if (deckendGras == 0 && deckendBelag > 1)
                    text.AppendLine($"  FINDING {deckendBelag} pavement surfaces overlap here.");
            }

            // 2. Die uebliche Ursache: zwei Flaechen, die sich fast beruehren.
            var gaps = 0;
            for (var a = 0; a < areas.Count; a++)
                for (var b = a + 1; b < areas.Count; b++)
                {
                    var gap = PolygonGap(areas[a].Polygon, areas[b].Polygon);
                    if (gap <= GapMin || gap >= GapMax) continue;
                    text.AppendLine($"  FINDING gap {gap:F3} m between "
                        + $"'{areas[a].Prefab}' and '{areas[b].Prefab}'. "
                        + "Slits this narrow show up as a scar in game.");
                    if (++gaps >= 5)
                    {
                        text.AppendLine("  (further gaps not listed)");
                        a = areas.Count;
                        break;
                    }
                }

            // 3. Von CS2 verworfen - liegt in der Welt, ist aber unsichtbar.
            foreach (var area in areas)
                if (area.Triangles == 0)
                    text.AppendLine($"  FINDING '{area.Prefab}' has NO triangles. "
                        + "CS2 discarded the shape; it is invisible.");

            // 4. Formen, die CS2 demnaechst verwirft.
            foreach (var area in areas)
                if (area.ShortestEdge > 0 && area.ShortestEdge < Cs2MinNodeDistance)
                    text.AppendLine($"  FINDING '{area.Prefab}' has an edge of only "
                        + $"{area.ShortestEdge:F3} m. CS2 requires at least "
                        + $"{Cs2MinNodeDistance:F3} m for surfaces.");

            /**
             * 5. Herrenlose Teile bleiben beim Abriss stehen.
             *
             * NUR UNSERE. Der erste Anlauf zaehlte jede besitzerlose Flaeche
             * im Umkreis - und meldete deshalb in JEDER Markierung "2 Flächen
             * ohne gemeinsamen Besitzer", obwohl es Bezirks- und
             * Nachbarflaechen des Spiels waren. Eine Falschmeldung in jedem
             * Bericht macht den ganzen Bericht wertlos.
             */
            var ours = areas.FindAll(a => LotOwnerOf(a.Entity) == lot);
            // Die Besitzerflaeche selbst HAT keinen Besitzer, sie IST einer.
            // Ohne diese Zeile meldete jeder Bericht genau eine herrenlose
            // Flaeche - naemlich die Wurzel.
            var ownerless = ours.FindAll(
                a => a.Owner == Entity.Null && a.Entity != lot);
            if (ownerless.Count > 0)
                text.AppendLine($"  FINDING {ownerless.Count} surface(s) of this parking lot have no "
                    + "shared owner. They will stay behind when it is demolished.");

            var objects = CountNearbyObjects(marker);
            text.AppendLine($"  Objects in range: {objects.Total} "
                + $"({objects.Ownerless} of them without an owner).");

            if (gaps == 0 && covering.Count > 0
                && areas.TrueForAll(a => a.Triangles != 0)
                && ownerless.Count == 0)
                text.AppendLine("  Keine der bekannten Regelverletzungen gefunden. "
                    + "Bitte beschreiben, was hier falsch aussieht.");
        }

        /**
         * Das Polygon in einer Zeile. Damit kann ich den Fall nachstellen,
         * ohne den Nutzer nach einem Screenshot zu fragen.
         */
        private static string FormatSite(float2[] site)
        {
            if (site == null || site.Length == 0) return "nicht aufgezeichnet";
            var text = new StringBuilder();
            for (var i = 0; i < site.Length; i++)
            {
                if (i > 0) text.Append(" | ");
                // Volle Genauigkeit: auf eine Stelle gerundet liess sich der
                // Fall nicht nachstellen, und genau dafuer steht er hier.
                text.Append(site[i].x.ToString("R", CultureInfo.InvariantCulture))
                    .Append(" / ")
                    .Append(site[i].y.ToString("R", CultureInfo.InvariantCulture));
            }
            return text.ToString();
        }

        private List<NearbyArea> CollectNearbyAreas(float3 marker)
        {
            var found = new List<NearbyArea>();
            if (_areaSearchSystem == null) return found;

            var tree = _areaSearchSystem.GetSearchTree(readOnly: true, out var deps);
            deps.Complete();
            using var results = new NativeList<Entity>(16, Allocator.Temp);
            var iterator = new AreaItemIterator
            {
                Bounds = new Bounds2(marker.xz - MarkerRadius, marker.xz + MarkerRadius),
                Results = results,
            };
            tree.Iterate(ref iterator);

            var seen = new HashSet<Entity>();
            for (var i = 0; i < results.Length; i++)
            {
                var entity = results[i];
                if (entity == Entity.Null || !seen.Add(entity)) continue;
                if (EntityManager.HasComponent<Temp>(entity)
                    || EntityManager.HasComponent<Deleted>(entity)) continue;
                if (!EntityManager.HasBuffer<Game.Areas.Node>(entity)) continue;

                var nodes = EntityManager.GetBuffer<Game.Areas.Node>(entity, true);
                if (nodes.Length < 3) continue;
                var polygon = new float2[nodes.Length];
                var shortest = float.MaxValue;
                for (var n = 0; n < nodes.Length; n++)
                {
                    polygon[n] = nodes[n].m_Position.xz;
                    var next = nodes[(n + 1) % nodes.Length].m_Position.xz;
                    shortest = math.min(shortest,
                        math.distance(nodes[n].m_Position.xz, next));
                }

                var triangles = EntityManager.HasBuffer<Game.Areas.Triangle>(entity)
                    ? EntityManager.GetBuffer<Game.Areas.Triangle>(entity, true).Length
                    : -1;
                var owner = EntityManager.HasComponent<Owner>(entity)
                    ? EntityManager.GetComponentData<Owner>(entity).m_Owner
                    : Entity.Null;

                found.Add(new NearbyArea
                {
                    Entity = entity,
                    Prefab = PrefabNameOf(entity),
                    Polygon = polygon,
                    Triangles = triangles,
                    Owner = owner,
                    ShortestEdge = shortest,
                    Covers = PointInPolygon(marker.xz, polygon),
                    Distance = DistanceToPolygon(marker.xz, polygon),
                });
            }
            found.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            return found;
        }

        private (int Total, int Ownerless) CountNearbyObjects(float3 marker)
        {
            if (_objectSearchSystem == null) return (0, 0);
            var tree = _objectSearchSystem.GetStaticSearchTree(
                readOnly: true, out var deps);
            deps.Complete();
            using var results = new NativeList<Entity>(16, Allocator.Temp);
            var iterator = new EntityIterator
            {
                Bounds = new Bounds2(marker.xz - MarkerRadius, marker.xz + MarkerRadius),
                Results = results,
            };
            tree.Iterate(ref iterator);

            var seen = new HashSet<Entity>();
            var total = 0;
            var ownerless = 0;
            for (var i = 0; i < results.Length; i++)
            {
                var entity = results[i];
                if (!seen.Add(entity)) continue;
                if (EntityManager.HasComponent<Temp>(entity)
                    || EntityManager.HasComponent<Deleted>(entity)) continue;
                if (!EntityManager.HasComponent<PrefabRef>(entity)) continue;
                var name = PrefabNameOf(entity);
                // Nur unsere Bauteile zaehlen - Baeume und Laternen des
                // Spielers gehen niemanden etwas an.
                if (name == null || !name.StartsWith("ParkingLot",
                        StringComparison.Ordinal)) continue;
                total++;
                if (!EntityManager.HasComponent<Owner>(entity)) ownerless++;
            }
            return (total, ownerless);
        }

        private string PrefabNameOf(Entity entity)
        {
            if (!EntityManager.HasComponent<PrefabRef>(entity)) return null;
            var prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
            return _prefabSystem != null
                && _prefabSystem.TryGetPrefab<PrefabBase>(prefab, out var asset)
                && asset != null ? asset.name : null;
        }

        /** Kleinster Abstand zwischen zwei Vieleckraendern. */
        private static float PolygonGap(float2[] a, float2[] b)
        {
            var best = float.MaxValue;
            for (var i = 0; i < a.Length; i++)
            {
                var segment = new Line2.Segment(a[i], a[(i + 1) % a.Length]);
                for (var k = 0; k < b.Length; k++)
                    best = math.min(best,
                        MathUtils.Distance(segment, b[k], out _));
            }
            for (var k = 0; k < b.Length; k++)
            {
                var segment = new Line2.Segment(b[k], b[(k + 1) % b.Length]);
                for (var i = 0; i < a.Length; i++)
                    best = math.min(best,
                        MathUtils.Distance(segment, a[i], out _));
            }
            return best;
        }

        private static float DistanceToPolygon(float2 point, float2[] polygon)
        {
            if (PointInPolygon(point, polygon)) return 0f;
            var best = float.MaxValue;
            for (var i = 0; i < polygon.Length; i++)
                best = math.min(best, MathUtils.Distance(
                    new Line2.Segment(polygon[i], polygon[(i + 1) % polygon.Length]),
                    point, out _));
            return best;
        }

        private static bool PointInPolygon(float2 point, float2[] polygon)
        {
            var inside = false;
            for (int i = 0, k = polygon.Length - 1; i < polygon.Length; k = i++)
            {
                if (polygon[i].y > point.y == polygon[k].y > point.y) continue;
                var x = (polygon[k].x - polygon[i].x) * (point.y - polygon[i].y)
                    / (polygon[k].y - polygon[i].y) + polygon[i].x;
                if (point.x < x) inside = !inside;
            }
            return inside;
        }
    }
}
