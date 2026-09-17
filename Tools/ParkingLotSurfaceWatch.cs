using System;
using Game;
using Game.Prefabs;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * ZAEHLT NACH, OB UNSERE FLAECHEN NOCH DA SIND - VORHER UND NACHHER.
     *
     * Der Nutzer vermutet: *"An sich sollen unsere Flaechen die der
     * Gebaeude ueberlagern. Aber ich glaube, dass unsere verdraengt
     * werden."* Das sind zwei verschiedene Fehler mit zwei verschiedenen
     * Loesungen:
     *
     *   ueberlagert - unsere Flaeche existiert noch, wird nur uebermalt.
     *                 Dann geht es um Darstellung.
     *   verdraengt  - CS2 loescht sie, weil das Gebaeude sein Grundstueck
     *                 freiraeumt. Dann hilft an der Darstellung gar nichts.
     *
     * Ich habe an dieser Frage zweimal die falsche Schraube gedreht -
     * Zeichenprioritaet und Decal-Ebene, beide ohne Wirkung. Deshalb wird
     * hier gezaehlt statt vermutet.
     *
     * EIGENES SYSTEM, NICHT IM WERKZEUG. Der erste Anlauf zaehlte in
     * `ParkingLotToolSystem.OnUpdate` und war damit wertlos, sobald der
     * Nutzer das Werkzeug schloss. Genau das muss er aber tun: *"Stoert,
     * weil ich nach dem Bauen ja auch noch die Haeuser setzen muss
     * beziehungsweise das Zoning malen."*
     */
    public sealed partial class ParkingLotSurfaceWatchSystem : GameSystemBase
    {
        /**
         * Wartezeit bis zur zweiten Zaehlung - FUENF MINUTEN, in ZEIT
         * gerechnet und nicht in Bildern. Die Dauer kommt vom Nutzer: *"So
         * schnell wachsen Gebaeude leider nicht."* Bei 30 statt 60 Bildern
         * haetten gezaehlte Frames doppelt so lange gedauert, und zwei
         * Laeufe waeren nicht vergleichbar.
         */
        private static readonly TimeSpan Wartezeit = TimeSpan.FromMinutes(5);

        private Entity _besitzer = Entity.Null;
        private DateTime _faellig = DateTime.MaxValue;
        private DateTime _aufgeben = DateTime.MaxValue;
        private bool _ausgangswertSteht;
        private Game.Prefabs.PrefabSystem _prefabSystem;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabSystem = World
                .GetOrCreateSystemManaged<Game.Prefabs.PrefabSystem>();
        }
        private int _flaechenVorher;
        private int _knotenVorher;

        /**
         * Vom Werkzeug nach dem Bau aufgerufen.
         *
         * ZAEHLT HIER NOCH NICHT. Der erste Anlauf tat das und mass null:
         * die Flaechen entstehen erst nach dem Traeger. Aus "0 vorher, 73
         * nachher" wurde dann die Meldung "unveraendert" - eine Zahl, die
         * genau das Gegenteil beweisen sollte und stattdessen zufaellig
         * zustimmte. Der Ausgangswert wird deshalb erst genommen, wenn
         * ueberhaupt Flaechen da sind.
         */
        public void Beobachte(Entity besitzer)
        {
            if (besitzer == Entity.Null || !EntityManager.Exists(besitzer))
                return;
            _besitzer = besitzer;
            _ausgangswertSteht = false;
            _aufgeben = DateTime.UtcNow + TimeSpan.FromMinutes(2);
            Mod.log.Info("PLT-Flaechenwache: wartet auf die ersten Flaechen; "
                + "danach laeuft die Uhr.");
        }

        [Preserve]
        protected override void OnUpdate()
        {
            using var uhr = ParkingLotMessung.Miss(
                ParkingLotMessung.Sys.Flaechenwache);
            if (_besitzer == Entity.Null) return;

            if (!_ausgangswertSteht)
            {
                if (!EntityManager.Exists(_besitzer))
                {
                    _besitzer = Entity.Null;
                    return;
                }
                Zaehle(_besitzer, out var da, out var knotenDa);
                if (da == 0)
                {
                    if (DateTime.UtcNow <= _aufgeben) return;
                    _besitzer = Entity.Null;
                    Mod.log.Warn("PLT-Flaechenwache: nach zwei Minuten keine "
                        + "einzige Flaeche am Besitzer - nichts zu zaehlen.");
                    return;
                }
                _flaechenVorher = da;
                _knotenVorher = knotenDa;
                _ausgangswertSteht = true;
                _faellig = DateTime.UtcNow + Wartezeit;
                Mod.log.Info($"PLT-Flaechenwache: Ausgangswert {da} "
                    + $"Flaeche(n) mit {knotenDa} Knoten. Zweite Zaehlung in "
                    + $"{Wartezeit.TotalMinutes:F0} Minuten.");
                return;
            }

            if (DateTime.UtcNow < _faellig) return;

            var besitzer = _besitzer;
            _besitzer = Entity.Null;
            if (!EntityManager.Exists(besitzer))
            {
                Mod.log.Warn("PLT-Flaechenwache: der Besitzer existiert nicht "
                    + "mehr - nichts zu vergleichen.");
                return;
            }

            Zaehle(besitzer, out var jetzt, out var knoten);
            var verloren = _flaechenVorher - jetzt;
            Mod.log.Info($"PLT-Flaechenwache: {jetzt} Flaeche(n) mit {knoten} "
                + $"Knoten nach {Wartezeit.TotalMinutes:F0} Minuten "
                + $"(vorher {_flaechenVorher} / {_knotenVorher}). "
                + (verloren > 0
                    ? $"VERDRAENGT: {verloren} Flaeche(n) fehlen."
                    : knoten < _knotenVorher
                        ? $"GESCHRUMPFT: {_knotenVorher - knoten} Knoten "
                            + "fehlen, die Flaechen selbst sind noch da."
                        : "UNVERAENDERT - was zu sehen ist, wird also "
                            + "ueberlagert und nicht verdraengt."));
            MeldeFremdeFlaechen(besitzer);
        }

        /**
         * WER LIEGT DENN NUN OBEN - und mit welchem Wert?
         *
         * Areas werden ueber `Graphics.RenderMeshIndirect` mit
         * `RenderParams.rendererPriority` gezeichnet
         * (`AreaRenderSystem.cs:209`), und der Wert stammt aus
         * `RenderedArea.m_RendererPriority` des Prefabs
         * (`AreaBatchSystem.cs:2093`). Es ist also ein Sortierschluessel -
         * aber nur zwischen Flaechen, die denselben Weg nehmen.
         *
         * Unsere liegt auf -95, dem hoechsten Wert, den CS2 selbst vergibt.
         * Wenn die Flaeche eines Gebaeudes trotzdem oben liegt, hat sie
         * entweder einen hoeheren Wert oder sie ist gar keine Area. Beides
         * steht in dieser Zeile, und beides fuehrt zu einer anderen Loesung.
         */
        private void MeldeFremdeFlaechen(Entity besitzer)
        {
            var eigene = new System.Collections.Generic.HashSet<Entity>();
            if (EntityManager.HasBuffer<Game.Areas.SubArea>(besitzer))
            {
                var unter = EntityManager.GetBuffer<Game.Areas.SubArea>(
                    besitzer, true);
                for (var i = 0; i < unter.Length; i++)
                    eigene.Add(unter[i].m_Area);
            }
            if (eigene.Count == 0) return;

            // Umgrenzung unserer Flaechen, damit nur Nachbarn geprueft werden.
            var minX = float.MaxValue;
            var maxX = float.MinValue;
            var minZ = float.MaxValue;
            var maxZ = float.MinValue;
            foreach (var flaeche in eigene)
            {
                if (!EntityManager.HasBuffer<Game.Areas.Node>(flaeche)) continue;
                var knoten = EntityManager.GetBuffer<Game.Areas.Node>(
                    flaeche, true);
                for (var i = 0; i < knoten.Length; i++)
                {
                    var pos = knoten[i].m_Position;
                    if (pos.x < minX) minX = pos.x;
                    if (pos.x > maxX) maxX = pos.x;
                    if (pos.z < minZ) minZ = pos.z;
                    if (pos.z > maxZ) maxZ = pos.z;
                }
            }
            if (minX > maxX) return;

            var gefunden = new System.Collections.Generic.List<string>();
            var abfrage = GetEntityQuery(
                ComponentType.ReadOnly<Game.Areas.Area>(),
                ComponentType.ReadOnly<PrefabRef>());
            using var alle = abfrage.ToEntityArray(
                Unity.Collections.Allocator.Temp);
            for (var i = 0; i < alle.Length; i++)
            {
                var fremd = alle[i];
                if (eigene.Contains(fremd)) continue;
                if (!EntityManager.HasBuffer<Game.Areas.Node>(fremd)) continue;
                var knoten = EntityManager.GetBuffer<Game.Areas.Node>(
                    fremd, true);
                var trifft = false;
                for (var n = 0; n < knoten.Length && !trifft; n++)
                {
                    var pos = knoten[n].m_Position;
                    trifft = pos.x >= minX && pos.x <= maxX
                        && pos.z >= minZ && pos.z <= maxZ;
                }
                if (!trifft) continue;

                var prefab = EntityManager
                    .GetComponentData<PrefabRef>(fremd).m_Prefab;
                var name = _prefabSystem != null
                    && _prefabSystem.TryGetPrefab<PrefabBase>(prefab, out var p)
                    && p != null ? p.name : "?";
                var wert = "kein RenderedArea";
                if (_prefabSystem != null
                    && _prefabSystem.TryGetPrefab<PrefabBase>(prefab, out var pb)
                    && pb != null)
                {
                    var gerendert = pb.GetComponent<Game.Prefabs.RenderedArea>();
                    if (gerendert != null)
                        wert = gerendert.m_RendererPriority.ToString();
                }
                /*
                 * WEM GEHOERT SIE? Das ist die eigentliche Frage.
                 *
                 * Der Nutzer: *"Wichtig ist aber, dass die Flaeche ein
                 * Subelement vom Haus ist."* Eine Unterflaeche findet die
                 * Abfrage zwar - sie ist ja auch eine Area -, aber ohne den
                 * Besitzer koennte man sie fuer eine beliebige fremde
                 * Flaeche halten. Erst der Name des Besitzers zeigt, dass
                 * das Haus sie mitbringt.
                 */
                var besitzerName = "ohne Besitzer";
                if (EntityManager.HasComponent<Game.Common.Owner>(fremd))
                {
                    var wem = EntityManager
                        .GetComponentData<Game.Common.Owner>(fremd).m_Owner;
                    besitzerName = !EntityManager.Exists(wem)
                        ? "Besitzer weg"
                        : EntityManager.HasComponent<PrefabRef>(wem)
                            && _prefabSystem != null
                            && _prefabSystem.TryGetPrefab<PrefabBase>(
                                EntityManager.GetComponentData<PrefabRef>(wem)
                                    .m_Prefab, out var bp)
                            && bp != null
                            ? bp.name
                                + (EntityManager
                                    .HasComponent<Game.Buildings.Building>(wem)
                                    ? " [GEBAEUDE]" : string.Empty)
                            : "unbekannt";
                }
                var eintrag = name + " (Prioritaet " + wert
                    + ", gehoert zu " + besitzerName + ")";
                if (!gefunden.Contains(eintrag)) gefunden.Add(eintrag);
                if (gefunden.Count >= 8) break;
            }

            Mod.log.Info("PLT-Flaechenwache FREMDE FLAECHEN im selben Bereich: "
                + (gefunden.Count == 0
                    ? "keine - was oben liegt, ist also KEINE Area."
                    : string.Join(", ", gefunden)));
        }

        /**
         * Flaechen am Besitzer samt ihrer Knoten.
         *
         * Die Knoten zaehlen mit, damit auch eine Flaeche auffaellt, der
         * nur die Geometrie beschnitten wurde - ihre Entity existiert dann
         * weiter, und die blosse Anzahl bliebe gleich.
         */
        private void Zaehle(Entity besitzer, out int flaechen, out int knoten)
        {
            flaechen = 0;
            knoten = 0;
            if (!EntityManager.HasBuffer<Game.Areas.SubArea>(besitzer)) return;
            var unter = EntityManager.GetBuffer<Game.Areas.SubArea>(
                besitzer, true);
            for (var i = 0; i < unter.Length; i++)
            {
                var flaeche = unter[i].m_Area;
                if (!EntityManager.Exists(flaeche)) continue;
                if (!EntityManager.HasComponent<PrefabRef>(flaeche)) continue;
                flaechen++;
                if (EntityManager.HasBuffer<Game.Areas.Node>(flaeche))
                    knoten += EntityManager
                        .GetBuffer<Game.Areas.Node>(flaeche, true).Length;
            }
        }
    }
}
