using System;
using System.Collections.Generic;
using System.Reflection;
using Game;
using Game.Net;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * EINEBNENDE VARIANTEN UNSERER UNSICHTBAREN WEGE - fuer die Stuecke, die
     * am inneren Ende einer Zufahrtsgasse ankommen.
     *
     * Befund 2026-09-26 (Tester-Bericht UFS7, Codex + Dekompilat): fuenf von
     * sechs Gassen kippten nach dem Bau in den Boden, das innere Ende bis
     * 5,5 m tief. Die Gassen selbst waren richtig gebaut (Kurvenende 53,25 m),
     * nur der gemeinsame KNOTEN sank. Ablauf:
     *
     *   1. `GroundHeightSystem.UpdateHeightsJob` legt nach jeder
     *      Gelaendeaktualisierung Knoten und Kurven NICHT einebnender Netze
     *      neu aufs Gelaende (`NetUtils.AdjustPosition`). Einebnende
     *      (`FlattenTerrain`) laesst es in Ruhe (`BoundsFindJob.Iterate`),
     *      ausser sie gehoeren zu einem Gebaeude-Grundstueck - unser Lot ist
     *      keins.
     *   2. Welches Prefab ein gemeinsamer Knoten traegt, entscheidet
     *      `GenerateNodesSystem` bei gleicher Position nach der Reihenfolge:
     *      der zuletzt verarbeitete Kurs gewinnt. Also zufaellig mal die
     *      Gasse (einebnend), mal ein unsichtbarer Weg (nicht einebnend).
     *   3. Traegt der Knoten den Weg, sinkt er auf das Gelaende - und das hat
     *      die Gasse mit ihrem `ClipTerrain` gerade weggeschnitten. Die Gasse
     *      folgt dem Knoten, schneidet tiefer, der Knoten sinkt nach.
     *
     * Die Wurzel ist Punkt 2 im Zusammenspiel mit 1: jedes Netz, das am
     * Gassenende ankommt, muss einebnen. Dann ist der Knoten immer
     * einebnend, egal welcher Kurs zuletzt kommt. `ClipTerrain` an der Gasse
     * bleibt, wie beschlossen.
     *
     * Die Klone entstehen BEDINGUNGSLOS beim Start (sonst laedt ein
     * Spielstand ihre Kanten als totes Prefab) und ihre Namen sind ab dem
     * ersten veroeffentlichten Stand Spielstandformat - nie umbenennen.
     */
    public sealed partial class ParkingLotEbenerWegPrefabSystem : GameSystemBase
    {
        /** Quelle -> Klon. Die Quellen sind die Vanilla-Fahrwege und unsere Fusswegklone. */
        private static readonly (string Quelle, string Klon)[] Paare =
        {
            ("Invisible Car Path - 1xTwoway", "PLT Flat Invisible Car Path - 1xTwoway"),
            ("Invisible Road Path - 1xTwoway", "PLT Flat Invisible Road Path - 1xTwoway"),
            ("Invisible Car Path - 2xTwoway", "PLT Flat Invisible Car Path - 2xTwoway"),
            ("Invisible Road Path - 2xTwoway", "PLT Flat Invisible Road Path - 2xTwoway"),
            ("PLT Invisible Pedestrian Path", "PLT Flat Invisible Pedestrian Path"),
            ("PLT Pedestrian Entrance Path", "PLT Flat Pedestrian Entrance Path"),
        };

        private PrefabSystem _prefabs;
        private EntityQuery _wege;
        private readonly Dictionary<Entity, Entity> _quelleZuKlon = new();
        private readonly HashSet<string> _angelegt = new();
        private bool _fehler;

        /** Quelle -> fertiger Klon (FlattenTerrain gesetzt, LocalConnect wie die Quelle). */
        private readonly Dictionary<Entity, Entity> _bereit = new();

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _wege = GetEntityQuery(ComponentType.ReadOnly<PathwayData>(),
                ComponentType.ReadOnly<NetData>(),
                ComponentType.Exclude<PlaceholderObjectElement>());
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (_fehler || _angelegt.Count == Paare.Length) return;
            using var kandidaten = _wege.ToEntityArray(Allocator.Temp);
            foreach (var entity in kandidaten)
            {
                if (!_prefabs.TryGetPrefab<PathwayPrefab>(entity, out var quelle)
                    || quelle == null) continue;
                foreach (var (quellname, klonname) in Paare)
                {
                    if (quelle.name != quellname || _angelegt.Contains(klonname)) continue;
                    // Vanilla-Quellen nur als eingebautes Prefab, unsere
                    // Fusswegklone sind naturgemaess nicht eingebaut.
                    if (!quellname.StartsWith("PLT ", StringComparison.Ordinal)
                        && !quelle.isBuiltin) continue;
                    try
                    {
                        var klon = ScriptableObject.CreateInstance<PathwayPrefab>();
                        klon.name = klonname;
                        for (var typ = quelle.GetType(); typ != typeof(PrefabBase) && typ != null;
                             typ = typ.BaseType)
                            foreach (var feld in typ.GetFields(BindingFlags.Instance
                                | BindingFlags.Public | BindingFlags.NonPublic
                                | BindingFlags.DeclaredOnly))
                                if (!feld.IsInitOnly && !feld.IsLiteral)
                                    feld.SetValue(klon, feld.GetValue(quelle));
                        /*
                         * WELCHE KOMPONENTEN ERBT DER KLON? Einmal ins Log.
                         *
                         * Zweimal ist ein Klon schon in einem Vanilla-Topf
                         * gelandet (SpawnableArea an der Vorflaeche, Mesh der
                         * Gasse). Welche Komponenten die Vanilla-Wege tragen,
                         * steht nicht in der Game.dll, sondern in den
                         * Spieldaten - Codex konnte es am 2026-09-26 deshalb
                         * nicht ausschliessen. Diese Zeile beantwortet es beim
                         * ersten Start.
                         */
                        var namen = new List<string>();
                        foreach (var komponente in quelle.components)
                        {
                            if (komponente == null) continue;
                            namen.Add(komponente.GetType().Name
                                + (komponente is UIObject ? " (ausgelassen)" : ""));
                            if (!(komponente is UIObject))
                                klon.AddComponentFrom(komponente);
                        }
                        Mod.log.Info($"PLT-Ebener Weg: '{klonname}' erbt von '{quellname}': "
                            + string.Join(", ", namen) + ".");
                        if (!_prefabs.AddPrefab(klon))
                        {
                            UnityEngine.Object.Destroy(klon);
                            throw new InvalidOperationException(
                                "AddPrefab gab false zurueck fuer " + klonname);
                        }
                        _quelleZuKlon[entity] = _prefabs.GetEntity(klon);
                        _angelegt.Add(klonname);
                    }
                    catch (Exception e)
                    {
                        _fehler = true;
                        Mod.log.Error(e, "PLT-Ebener Weg: Klon " + klonname
                            + " konnte nicht angelegt werden.");
                        return;
                    }
                }
            }
        }

        /**
         * Nach `NetInitializeSystem`, in jedem Zyklus: `FlattenTerrain` an die
         * Klone und den LocalConnect der Quelle uebernehmen. Die Nachschau je
         * Zyklus, weil CS2 die Prefabdaten beim erneuten Initialisieren neu
         * schreibt (dieselbe Erfahrung wie beim Terraineingriff der
         * Zoningstrasse).
         */
        internal void Vollende()
        {
            foreach (var paar in _quelleZuKlon)
            {
                var quelle = paar.Key;
                var klon = paar.Value;
                if (!EntityManager.Exists(klon)
                    || !EntityManager.HasComponent<NetGeometryData>(klon)) continue;
                var geometrie = EntityManager.GetComponentData<NetGeometryData>(klon);
                if ((geometrie.m_Flags & GeometryFlags.FlattenTerrain) == 0)
                {
                    geometrie.m_Flags |= GeometryFlags.FlattenTerrain;
                    EntityManager.SetComponentData(klon, geometrie);
                }
                /*
                 * DER LOCALCONNECT KOMMT VON DER QUELLE, nicht aus einer
                 * eigenen Regel. Unsere Fusswegklone bekommen ihre Suchmaske
                 * erst nach dem Initialisieren (`ParkingLotFusswegPrefab
                 * AbschlussSystem`); wer sie hier neu rechnete, haette die
                 * Regel zweimal.
                 */
                if (EntityManager.HasComponent<LocalConnectData>(quelle)
                    && EntityManager.HasComponent<LocalConnectData>(klon))
                {
                    var soll = EntityManager.GetComponentData<LocalConnectData>(quelle);
                    var ist = EntityManager.GetComponentData<LocalConnectData>(klon);
                    if (ist.m_Layers != soll.m_Layers || ist.m_Flags != soll.m_Flags
                        || ist.m_SearchDistance != soll.m_SearchDistance)
                        EntityManager.SetComponentData(klon, soll);
                }
                if (!_bereit.ContainsKey(quelle))
                {
                    _bereit[quelle] = klon;
                    Mod.log.Info($"PLT-Ebener Weg: '{Name(klon)}' bereit (FlattenTerrain "
                        + $"gesetzt, LocalConnect wie '{Name(quelle)}').");
                }
            }
        }

        /** Die einebnende Variante eines Wegs - oder `Entity.Null`, wenn es keine gibt. */
        internal Entity EbeneVariante(Entity quelle)
            => _bereit.TryGetValue(quelle, out var klon) ? klon : Entity.Null;

        private string Name(Entity prefab)
        {
            try { return _prefabs.GetPrefabName(prefab); }
            catch { return prefab.ToString(); }
        }
    }

    public sealed partial class ParkingLotEbenerWegAbschlussSystem : GameSystemBase
    {
        [Preserve]
        protected override void OnUpdate()
            => World.GetOrCreateSystemManaged<ParkingLotEbenerWegPrefabSystem>().Vollende();
    }
}
