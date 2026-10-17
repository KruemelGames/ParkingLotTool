using System;
using System.Diagnostics;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /** Eigenstaendiger Erzeuger: keine Lot-ID, kein Besitzer, kein globales Apply. */
    internal sealed partial class ParkingLotMusterSystem : GameSystemBase
    {
        private EntityQuery _marker, _flaechen, _eingaben, _vorbilder;
        private PrefabSystem _prefabs;
        private ParkingLotApronPrefabSystem _klone;
        private TerrainSystem _terrain;
        private bool _ladebereit, _wunsch, _warteGeometrie, _warteSichtbar;
        private float _bildzeitVorher;
        private string _wahl, _quellname, _fehler, _bereinigungsfehler;
        private Entity _quelle;
        private float2 _mitte;
        private int _bild;
        private long _beginn;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _klone = World.GetOrCreateSystemManaged<ParkingLotApronPrefabSystem>();
            _terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            _marker = GetEntityQuery(new EntityQueryDesc {
                All = new[] { ComponentType.ReadOnly<ParkingLotMusterflaeche>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
                Options = EntityQueryOptions.IncludeDisabledEntities,
            });
            _flaechen = GetEntityQuery(ComponentType.ReadOnly<ParkingLotMusterflaeche>(),
                ComponentType.ReadOnly<Game.Areas.Area>(), ComponentType.Exclude<Deleted>());
            _eingaben = GetEntityQuery(ComponentType.ReadOnly<ParkingLotMusterflaeche>(),
                ComponentType.ReadOnly<CreationDefinition>());
            _vorbilder = GetEntityQuery(ComponentType.ReadOnly<SurfaceData>(), ComponentType.ReadOnly<AreaData>(),
                ComponentType.ReadOnly<AreaGeometryData>(), ComponentType.Exclude<Deleted>());
        }

        internal void Pflege(string wahl, float2 mitte)
        {
            if (!_ladebereit) return;
            bool gleich = _wunsch && _wahl == wahl && _mitte.Equals(mitte);
            var aktion = OberflaechenMuster.Entscheide(true, _flaechen.CalculateEntityCount(),
                !_eingaben.IsEmptyIgnoreFilter, gleich);
            if (aktion == OberflaechenMuster.Aktion.Behalten) return;
            if (aktion == OberflaechenMuster.Aktion.Entfernen) { Entferne("Mehrfachmuster"); return; }
            try
            {
                if (!gleich)
                {
                    Bereinige(); // Nur Marker-Entities; kein fremder Definitions-/Temp-Clear.
                    _fehler = null;
                }
                _wunsch = true; _wahl = wahl; _mitte = mitte;
                if (_fehler != null) return;
                if (_quellname != wahl || !EntityManager.Exists(_quelle))
                {
                    _quelle = Entity.Null; _quellname = wahl;
                    using var vorbilder = _vorbilder.ToEntityArray(Allocator.Temp);
                    foreach (var e in vorbilder)
                        if (_prefabs.TryGetPrefab<SurfacePrefab>(e, out var p)
                            && string.Equals(p.name, wahl, StringComparison.OrdinalIgnoreCase))
                        { _quelle = e; break; }
                }
                if (_quelle == Entity.Null) { Fehler("Material nicht gefunden"); return; }
                // Gemerkter Klon wird vor dem naechsten Laden wieder registriert.
                // ApplyAndSave des Mod-Settings speichert keinen Spielstand.
                var klon = _klone.FordereAn(_quelle, ParkingLotPreviewStyle.MusterPrioritaet,
                    out bool fehlgeschlagen, out bool aufgegeben, raeumt: false);
                if (fehlgeschlagen || aufgegeben) { Fehler("Musterklon nicht verfuegbar"); return; }
                if (klon == Entity.Null) return; // Naechste Prefabphase, niemals Bau blockieren.
                var daten = EntityManager.GetComponentData<AreaData>(klon);
                var flags = EntityManager.GetComponentData<AreaGeometryData>(klon).m_Flags;
                if (flags != 0 || !daten.m_Archetype.Valid
                    || !_prefabs.TryGetPrefab<SurfacePrefab>(klon, out var muster)
                    || muster.GetComponent<ParkingLotMusterkennung>() == null)
                { Fehler("Unsicherer Musterarchetyp oder Geometrieflags " + flags); return; }
                var uhr = Stopwatch.StartNew();
                Bereinige();
                var definition = EntityManager.CreateEntity();
                // Auch ein im Save verbliebener Eingaberest wird beim Laden erkannt.
                EntityManager.AddComponent<ParkingLotMusterflaeche>(definition);
                EntityManager.AddComponentData(definition, new CreationDefinition {
                    m_Prefab = klon, m_Flags = CreationFlags.Permanent,
                });
                var hoehen = _terrain.GetHeightData(waitForPending: true);
                var ring = OberflaechenMuster.Quadrat(mitte);
                var nodes = EntityManager.AddBuffer<Game.Areas.Node>(definition);
                nodes.ResizeUninitialized(5);
                for (int i=0; i<4; i++)
                {
                    var p = new float3(ring[i].x,0,ring[i].y);
                    p.y = TerrainUtils.SampleHeight(ref hoehen,p);
                    nodes[i] = new Game.Areas.Node(p,float.MinValue);
                }
                nodes[4] = nodes[0];
                EntityManager.AddComponent<Updated>(definition);
                ParkingLotToolSystem.NurDiesesBild(EntityManager,definition);
                _warteGeometrie = _warteSichtbar = true; _bild = UnityEngine.Time.frameCount;
                _bildzeitVorher = UnityEngine.Time.unscaledDeltaTime * 1000f;
                _beginn = Stopwatch.GetTimestamp();
                World.GetExistingSystemManaged<ParkingLotToolSystem>()?.MusterMaske(mitte);
                Melde($"angefordert: {wahl}; 1 Permanent-Definition, 4 Ecken; "
                    + $"Definition+Maskenwechsel {uhr.Elapsed.TotalMilliseconds:F3} ms");
            }
            catch (Exception e) { Fehler(e.Message); }
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (_marker.IsEmptyIgnoreFilter) return;
            if (!_ladebereit || !_wunsch) { Entferne("ohne aktiven Wunsch"); return; }
            // Nach Generator/Barrieren: native Ausgabe existiert schon mit Marker.
            // Die Permanent-Eingabe genau einmal verbrauchen, sonst entstuende im Folgeframe ein Duplikat.
            int zahl = _flaechen.CalculateEntityCount();
            if (zahl > 1) { Entferne("Mehrfachmuster " + zahl); return; }
            if (zahl == 0) return;
            if (!_eingaben.IsEmptyIgnoreFilter)
                using (var eingaben = _eingaben.ToEntityArray(Allocator.Temp))
                    foreach (var e in eingaben) EntityManager.DestroyEntity(e);
            if (!_warteGeometrie && !_warteSichtbar) return;
            using var flaechen = _flaechen.ToEntityArray(Allocator.Temp);
            var flaeche = flaechen[0];
            if (!EntityManager.HasBuffer<Game.Areas.Triangle>(flaeche)
                || EntityManager.GetBuffer<Game.Areas.Triangle>(flaeche).Length == 0) return;
            double ms = (Stopwatch.GetTimestamp()-_beginn)*1000.0/Stopwatch.Frequency;
            int bilder = UnityEngine.Time.frameCount-_bild;
            if (_warteGeometrie)
            {
                _warteGeometrie = false;
                Melde($"Geometrie bereit: {_wahl}; Markerzahl {zahl}, {bilder} Bilder/{ms:F2} ms Wandzeit");
            }
            // Ausserhalb der Kamera kann das Muster unsichtbar bleiben; dann seltener pruefen.
            if (bilder > 60 && bilder % 15 != 0) return;
            if (!EntityManager.HasComponent<Game.Areas.Batch>(flaeche)) return;
            var batch = EntityManager.GetComponentData<Game.Areas.Batch>(flaeche);
            if (batch.m_AllocatedSize <= 0 || batch.m_VisibleCount <= 0) return;
            _warteSichtbar = false;
            Melde($"Renderbatch erstmals sichtbar gemeldet: {_wahl}; {bilder} Bilder/{ms:F2} ms Wandzeit; "
                + $"sichtbare Dreiecke {batch.m_VisibleCount}, Bildzeit vorher {_bildzeitVorher:F2}/"
                + $"jetzt {UnityEngine.Time.unscaledDeltaTime*1000:F2} ms (Einzelbilder; keine GPU-Messung)");
        }

        private int Bereinige()
        {
            int anzahl = 0;
            // Auch bereits fuer das Bildende geloeschte Definitionen sofort zurueckziehen.
            using (var eingaben = _eingaben.ToEntityArray(Allocator.Temp))
                foreach (var e in eingaben) EntityManager.DestroyEntity(e);
            using var alle = _marker.ToEntityArray(Allocator.Temp);
            foreach (var e in alle)
            {
                // Definitionen sind keine nativen Bauteile und duerfen nicht nochmals generieren.
                if (EntityManager.HasComponent<CreationDefinition>(e)) EntityManager.DestroyEntity(e);
                else { EntityManager.AddComponent<Deleted>(e); anzahl++; }
            }
            _warteGeometrie = _warteSichtbar = false;
            return anzahl;
        }

        internal void Entferne(string grund)
        {
            try { EntferneKern(grund); _bereinigungsfehler = null; }
            catch (Exception e)
            {
                _wunsch = false;
                if (_bereinigungsfehler == e.Message) return;
                _bereinigungsfehler = e.Message;
                Melde("Bereinigung wird erneut versucht: " + e.Message);
            }
        }

        private void EntferneKern(string grund)
        {
            bool offen = !_marker.IsEmptyIgnoreFilter || !_eingaben.IsEmptyIgnoreFilter;
            if (!offen && !_wunsch) return;
            int anzahl = offen ? Bereinige() : 0;
            _wunsch = false; _wahl = null; _fehler = null;
            World.GetExistingSystemManaged<ParkingLotToolSystem>()?.MusterMaske(null);
            if (offen) Melde($"entfernt ({grund}): {anzahl} Musterflaechen; eigene Eingaben beendet");
        }

        internal void NachLaden()
        {
            int anzahl = 0;
            try { anzahl = Bereinige(); }
            catch (Exception e) { Melde("Ladebereinigung wird im Folgebild wiederholt: " + e.Message); }
            _wunsch = false; _wahl = _quellname = _fehler = null; _quelle = Entity.Null;
            _ladebereit = true;
            World.GetExistingSystemManaged<ParkingLotUISystem>()?.VergissSurfacePreviewFenster();
            World.GetExistingSystemManaged<ParkingLotToolSystem>()?.MusterMaske(null);
            Melde($"Ladebereinigung: {anzahl} Musterflaechen entfernt; eigene Eingaben beendet");
        }

        [Preserve]
        protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            Entferne("Weltwechsel");
            _ladebereit = false;
            base.OnGamePreload(purpose,mode);
        }

        private void Fehler(string text)
        {
            var wahl = _wahl;
            Entferne("Vorschaufehler");
            _wunsch = true; _wahl = wahl;
            _fehler = text;
            Melde("nicht angelegt: " + text);
        }

        private static void Melde(string text)
            => ParkingLotHintergrundDiagnose.Sicher(() => Mod.log.Info("PLT-SurfacePreview: " + text));
    }
}
