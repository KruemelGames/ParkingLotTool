using System;
using System.Collections.Generic;
using System.Reflection;
using Game;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /** Startregistrierung; kein Bedarfsschalter, kein Schreiben in fertige Netze. */
    public sealed partial class ParkingLotFahrprefabSystem : GameSystemBase
    {
        private PrefabSystem _prefabs;
        private EntityQuery _wege;
        private EntityQuery _pools;
        private readonly Dictionary<string, (Entity Entity, int Bild)> _angelegt = new();
        private readonly Dictionary<Entity, (PrefabBase Prefab, int Bild)> _waechter = new();
        private bool _fehler;
        private readonly Dictionary<Entity, Entity> _quellen = new();

        internal Entity QuelleFuer(Entity klon)
            => _quellen.TryGetValue(klon, out var quelle) ? quelle : Entity.Null;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _wege = GetEntityQuery(ComponentType.ReadOnly<PathwayData>(),
                ComponentType.ReadOnly<NetData>(), ComponentType.Exclude<PlaceholderObjectElement>());
            _pools = GetEntityQuery(ComponentType.ReadOnly<PlaceholderObjectElement>());
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (_fehler || _angelegt.Count == ParkingLotFahrregeln.Wege.Length) return;
            using var kandidaten = _wege.ToEntityArray(Allocator.Temp);
            foreach (var entity in kandidaten)
            {
                if (!_prefabs.TryGetPrefab<PathwayPrefab>(entity, out var quelle)
                    || quelle == null || !quelle.isBuiltin
                    || !ParkingLotFahrregeln.IstFahrweg(quelle.name)
                    || _angelegt.ContainsKey(quelle.name)) continue;
                try
                {
                    var klon = ScriptableObject.CreateInstance<PathwayPrefab>();
                    klon.name = ParkingLotFahrregeln.Wegname(quelle.name);
                    // Rootkopie ohne Unity-/Prefab-Buchhaltung; Isoliere kopiert
                    // anschliessend alle verweisenden Infoobjekte rekursiv.
                    for (var typ = quelle.GetType(); typ != null && typ != typeof(PrefabBase);
                         typ = typ.BaseType)
                        foreach (var feld in typ.GetFields(BindingFlags.Instance | BindingFlags.Public
                            | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                            if (!feld.IsInitOnly && !feld.IsLiteral
                                && (feld.IsPublic || feld.IsDefined(typeof(SerializeField), false)))
                                feld.SetValue(klon, feld.GetValue(quelle));
                    foreach (var komponente in quelle.components)
                        if (ParkingLotFahrprefabKopie.Erben(komponente)) klon.AddComponentFrom(komponente);
                    Isoliere(klon);
                    Registriere(quelle, klon);
                    _angelegt.Add(quelle.name, (_prefabs.GetEntity(klon), UnityEngine.Time.frameCount));
                }
                catch (Exception e)
                {
                    _fehler = true;
                    Mod.log.Error(e, "PLT-Fahrprefabs: Startregistrierung fehlgeschlagen fuer " + quelle.name);
                    return;
                }
            }
        }

        /** Auch fuer die bestehenden Road- und historischen Flat-Klone, VOR AddPrefab. */
        internal void Isoliere(NetGeometryPrefab netz)
            => new ParkingLotFahrprefabKopie(netz.name, Registriere).Isoliere(netz);

        private void Registriere(PrefabBase quelle, PrefabBase klon)
        {
            if (!_prefabs.AddPrefab(klon))
                throw new InvalidOperationException("AddPrefab abgelehnt: " + klon.name);
            Beobachte(klon);
            // Nur Diagnose: Unterbauteile (Pieces) fuehrt CS2 nicht immer als
            // eigenes Prefab - GetEntity warf dort und brach die ganze Anmeldung ab.
            if (_prefabs.TryGetEntity(quelle, out var quellEntity)) _quellen[_prefabs.GetEntity(klon)] = quellEntity;
            Mod.log.Info("PLT-Fahrprefab Klon: '" + klon.name + "' <- '" + quelle.name
                + "'; Art=" + klon.GetType().Name
                + "; eigene Komponenten/Infoobjekte; UI/Spawn/AssetPack/alte IDs ausgeschlossen"
                + "; FindIt-Ausschluss inaktiv; vor PrefabInitializeSystem angemeldet.");
        }

        internal void Beobachte(PrefabBase klon)
            => _waechter[_prefabs.GetEntity(klon)] = (klon, UnityEngine.Time.frameCount);

        internal void Beobachte(PrefabBase quelle, PrefabBase klon)
        {
            Beobachte(klon);
            // Nur Diagnose: Unterbauteile (Pieces) fuehrt CS2 nicht immer als
            // eigenes Prefab - GetEntity warf dort und brach die ganze Anmeldung ab.
            if (_prefabs.TryGetEntity(quelle, out var quellEntity)) _quellen[_prefabs.GetEntity(klon)] = quellEntity;
            Mod.log.Info("PLT-Fahrprefab Klon: '" + klon.name + "' <- '" + quelle.name
                + "'; Art=" + klon.GetType().Name + "; isolierte Fahrkette, Tempo="
                + ParkingLotFahrregeln.TempoKmh + " km/h; UI/Spawn/AssetPack/alte IDs ausgeschlossen.");
        }

        internal bool TryWeg(string quelle, out Entity entity)
        {
            entity = Entity.Null;
            if (!_angelegt.TryGetValue(quelle, out var eintrag)
                || UnityEngine.Time.frameCount <= eintrag.Bild
                || !EntityManager.HasComponent<PathwayData>(eintrag.Entity)
                || !EntityManager.HasComponent<NetGeometryData>(eintrag.Entity)) return false;
            entity = eintrag.Entity;
            return true;
        }

        /** Nach NetInitialize; UI- und Spawn-Pools lesen, niemals nachtraeglich saeubern. */
        internal void PruefeListen()
        {
            if (_waechter.Count == 0) return;
            using var pools = _pools.ToEntityArray(Allocator.Temp);
            var abgeschlossen = new List<Entity>();
            foreach (var paar in _waechter)
            {
                var entity = paar.Key;
                var prefab = paar.Value.Prefab;
                if (UnityEngine.Time.frameCount <= paar.Value.Bild) continue;
                var inPool = 0;
                foreach (var pool in pools)
                {
                    var elemente = EntityManager.GetBuffer<PlaceholderObjectElement>(pool, true);
                    foreach (var element in elemente) if (element.m_Object == entity) inPool++;
                }
                var ui = EntityManager.HasComponent<UIObjectData>(entity);
                var spawn = EntityManager.HasComponent<SpawnableObjectData>(entity)
                    || EntityManager.HasComponent<SpawnableBuildingData>(entity);
                var editor = EntityManager.HasComponent<EditorAssetCategoryOverrideData>(entity);
                var asset = prefab.asset != null;
                var platzhalter = EntityManager.HasBuffer<PlaceholderObjectElement>(entity);
                var metadaten = prefab.GetComponent<EditorAssetCategoryOverride>();
                var findIt = metadaten != null && !metadaten.active
                    && metadaten.m_IncludeCategories.Length > 0
                    && Array.IndexOf(metadaten.m_ExcludeCategories, "FindIt") >= 0;
                var fehler = ui || spawn || editor || asset || platzhalter || inPool != 0 || !findIt;
                var text = "PLT-Fahrprefab Listenwaechter: '" + prefab.name + "' UI=" + ui
                    + ", Spawn=" + spawn + ", Editor=" + editor + ", Asset=" + asset
                    + ", Placeholder=" + platzhalter + ", Placeholder-Pool=" + inPool
                    + ", FindIt-Ausschluss=" + findIt + ".";
                // Erst abhaken, dann loggen: kann CS2 die Logdatei nicht oeffnen,
                // wirft sein Logger eine NullReferenceException (UnityLogger.Open
                // schluckt den Fehler, Internal_WriteStream nutzt den leeren
                // Schreiber). Ohne Abhaken kaeme sie jedes Bild wieder (Forum, 16.10.).
                abgeschlossen.Add(entity);
                try { if (fehler) Mod.log.Error(text); else Mod.log.Info(text); }
                catch (Exception) { }
            }
            foreach (var entity in abgeschlossen) _waechter.Remove(entity);
        }
    }

    public sealed partial class ParkingLotFahrprefabAbschlussSystem : GameSystemBase
    {
        [Preserve]
        protected override void OnUpdate()
            => World.GetOrCreateSystemManaged<ParkingLotFahrprefabSystem>().PruefeListen();
    }
}
