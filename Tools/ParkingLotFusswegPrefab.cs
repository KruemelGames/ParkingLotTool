using System;
using System.Reflection;
using Game;
using Game.Net;
using Game.Prefabs;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    // Vor dem Laden anmelden, damit gespeicherte Wege ihren Prefabverweis
    // wiederfinden. Nur der eigene Klon wird nach NetInitialize angepasst.
    public sealed partial class ParkingLotFusswegPrefabSystem : GameSystemBase
    {
        private const string Originalname = "Invisible Pedestrian Path";
        private const string Klonname = "PLT Invisible Pedestrian Path";
        private PrefabSystem _prefabs;
        private EntityQuery _wege;
        private Entity _klon;
        private Entity _zugang;
        private bool _fehler;
        public Entity Bereit { get; private set; }
        public Entity ZugangBereit { get; private set; }

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
            if (_klon != Entity.Null || _fehler) return;
            using var kandidaten = _wege.ToEntityArray(Allocator.Temp);
            foreach (var entity in kandidaten)
            {
                if (!_prefabs.TryGetPrefab<PathwayPrefab>(entity, out var original)
                    || original == null || !original.isBuiltin || original.name != Originalname) continue;
                PathwayPrefab klon = null;
                try
                {
                    klon = ScriptableObject.CreateInstance<PathwayPrefab>();
                    klon.name = Klonname;
                    // PrefabBase enthaelt Unity-Identitaet und darf nicht kopiert
                    // werden. Querschnittsreferenzen werden nur gelesen.
                    for (var typ = original.GetType(); typ != typeof(PrefabBase) && typ != null; typ = typ.BaseType)
                    foreach (var feld in typ.GetFields(BindingFlags.Instance | BindingFlags.Public
                        | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        if (!feld.IsInitOnly && !feld.IsLiteral) feld.SetValue(klon, feld.GetValue(original));
                    foreach (var komponente in original.components)
                        if (komponente != null && !(komponente is UIObject)) klon.AddComponentFrom(komponente);
                    if (!_prefabs.AddPrefab(klon)) throw new InvalidOperationException("AddPrefab gab false zurueck");
                    _klon = _prefabs.GetEntity(klon);
                    var zugang = ScriptableObject.CreateInstance<PathwayPrefab>();
                    zugang.name = "PLT Pedestrian Entrance Path";
                    for (var typ = original.GetType(); typ != typeof(PrefabBase) && typ != null; typ = typ.BaseType)
                    foreach (var feld in typ.GetFields(BindingFlags.Instance | BindingFlags.Public
                        | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        if (!feld.IsInitOnly && !feld.IsLiteral) feld.SetValue(zugang, feld.GetValue(original));
                    foreach (var komponente in original.components)
                        if (komponente != null && !(komponente is UIObject)) zugang.AddComponentFrom(komponente);
                    if (!_prefabs.AddPrefab(zugang))
                    {
                        UnityEngine.Object.Destroy(zugang);
                        throw new InvalidOperationException("Zugangs-AddPrefab gab false zurueck");
                    }
                    _zugang = _prefabs.GetEntity(zugang);
                }
                catch (Exception e)
                {
                    _fehler = true;
                    if (_klon == Entity.Null && klon != null) UnityEngine.Object.Destroy(klon);
                    Mod.log.Error(e, "PLT-Fusswegklon: Anmeldung fehlgeschlagen.");
                }
                return;
            }
        }

        internal void SperreAutomatischeVerlaengerung()
        {
            if (_zugang != Entity.Null && EntityManager.HasComponent<LocalConnectData>(_zugang))
            {
                var zugang = EntityManager.GetComponentData<LocalConnectData>(_zugang);
                var erlaubt = (Layer)FusswegAnschluss.Suchmaske(Zufahrtsart.Fussweg,
                    (uint)zugang.m_Layers, gesetzterZugang: true);
                if ((zugang.m_Flags & LocalConnectFlags.RequireDeadend) != 0
                    && erlaubt != Layer.None && ZugangBereit == Entity.Null)
                    ZugangBereit = _zugang;
            }
            if (_klon == Entity.Null || !EntityManager.Exists(_klon)
                || !EntityManager.HasComponent<LocalConnectData>(_klon)) return;
            var daten = EntityManager.GetComponentData<LocalConnectData>(_klon);
            // NetInitialize setzt RequireDeadend und Road erst bei fertiger
            // Initialisierung. Davor darf kein Kurs diesen Klon verwenden.
            if ((daten.m_Flags & LocalConnectFlags.RequireDeadend) == 0) return;
            var maske = (Layer)FusswegAnschluss.Suchmaske(Zufahrtsart.Fussweg, (uint)daten.m_Layers);
            if (daten.m_Layers != maske || Bereit == Entity.Null)
            {
                var vorher = daten.m_Layers;
                daten.m_Layers = maske;
                EntityManager.SetComponentData(_klon, daten);
                Bereit = _klon;
                Mod.log.Info($"PLT-Bauzettel: Fusswegklon LocalConnect-Suchmaske {vorher} -> {maske}; "
                    + $"Suchweite {daten.m_SearchDistance:F1} m, Auto-Prefabs unveraendert.");
            }
        }
    }

    // Eine Phase zum Anmelden vor PrefabInitialize, eine zum Konfigurieren
    // nach NetInitialize. Auch erneutes Initialisieren setzt so keine Suche frei.
    public sealed partial class ParkingLotFusswegPrefabAbschlussSystem : GameSystemBase
    {
        [Preserve]
        protected override void OnUpdate()
            => World.GetOrCreateSystemManaged<ParkingLotFusswegPrefabSystem>()
                .SperreAutomatischeVerlaengerung();
    }
}
