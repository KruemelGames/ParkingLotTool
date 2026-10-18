using System;
using System.Diagnostics;
using System.Collections.Generic;
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
        private string _wahl, _fehler, _bereinigungsfehler;
        private object _pflegeLayout;
        private string[] _pflegeMaterialien;
        private string _pflegeSchalter;
        private readonly Dictionary<string, Entity> _quellen = new Dictionary<string, Entity>();
        private readonly OberflaechenMuster.NeuesterStand _neuester = new OberflaechenMuster.NeuesterStand();
        private long _ausgabeStand, _wunschStand;
        private int _soll;
        private OberflaechenMuster.Plan _plan;
        private double _schnittMs;
        private int _bild;
        private long _beginn, _anforderungBeginn;
        private int _anforderungBild;

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
            _objekte = GetEntityQuery(ComponentType.ReadOnly<ParkingLotMusterflaeche>(),
                ComponentType.ReadOnly<Game.Objects.Object>(), ComponentType.Exclude<Deleted>());
            _flaechen = GetEntityQuery(ComponentType.ReadOnly<ParkingLotMusterflaeche>(),
                ComponentType.ReadOnly<Game.Areas.Area>(), ComponentType.Exclude<Deleted>());
            _eingaben = GetEntityQuery(ComponentType.ReadOnly<ParkingLotMusterflaeche>(),
                ComponentType.ReadOnly<CreationDefinition>());
            _vorbilder = GetEntityQuery(ComponentType.ReadOnly<SurfaceData>(), ComponentType.ReadOnly<AreaData>(),
                ComponentType.ReadOnly<AreaGeometryData>(), ComponentType.Exclude<Deleted>());
        }

        internal void Pflege(object layout, string[] materialien, string schalter, Func<OberflaechenMuster.Plan> berechnen, bool abbrechen)
        {
            if (!_ladebereit) { Warte("Ladebereinigung steht aus"); return; }
            if (!abbrechen && _wunsch && _art==EchteVorschauArt.Flaechen
                && ReferenceEquals(layout,_pflegeLayout) && ReferenceEquals(materialien,_pflegeMaterialien)
                && schalter==_pflegeSchalter && (_fehler!=null || _ausgabeStand==_wunschStand || !_neuester.IstAktuell(_wunschStand))) return;
            _pflegeLayout=layout; _pflegeMaterialien=materialien; _pflegeSchalter=schalter;
            var wahl = schalter + "|" + string.Join("\u001f", materialien);
            long stand = _neuester.Merke(layout, wahl);
            if (abbrechen) _neuester.Stoppe();
            if (!_neuester.IstAktuell(stand))
            {
                // Auch vor der ersten Ausgabe bleibt der Wunsch bis Toggle AUS abbrechbar.
                _wunsch = true;
                // ESC kann nur einen Untermodus schliessen; denselben Satz dann nicht erneut ausgeben.
                EntferneAbgebrochenenSatz();
                return;
            }
            bool gleich = _wunsch && _wahl == wahl && _ausgabeStand == stand;
            if (gleich) return;
            try
            {
                if (!_wunsch || _wunschStand != stand)
                {
                    if (_wunsch) Bereinige();
                    _plan = null; _objektplan = null; _fehler = null;
                    _anforderungBeginn=Stopwatch.GetTimestamp(); _anforderungBild=UnityEngine.Time.frameCount;
                }
                _wunsch = true; _wahl = wahl; _wunschStand = stand; _art=EchteVorschauArt.Flaechen;
                if (_fehler != null) return;
                // Rechnen nur einmal je Wunsch, auch wenn die Prefabphase noch aussteht.
                if (_plan == null)
                {
                    var schnitt = Stopwatch.StartNew();
                    _plan = berechnen(); _schnittMs = schnitt.Elapsed.TotalMilliseconds;
                }
                var benutzt = new bool[materialien.Length];
                foreach (var teil in _plan.Stuecke) benutzt[teil.Gruppe] = true;
                var klone = new Entity[materialien.Length];
                bool wartet = false;
                for (int g = 0; g < materialien.Length; g++)
                {
                    if (!benutzt[g]) continue;
                    string name = materialien[g];
                    if (string.IsNullOrEmpty(name)) { Fehler("Material des geplanten Stuecks fehlt"); return; }
                    if (!_quellen.TryGetValue(name, out var quelle) || !EntityManager.Exists(quelle))
                    {
                        quelle = Entity.Null;
                        using var vorbilder = _vorbilder.ToEntityArray(Allocator.Temp);
                        foreach (var e in vorbilder)
                            if (_prefabs.TryGetPrefab<SurfacePrefab>(e, out var p)
                                && string.Equals(p.name, name, StringComparison.OrdinalIgnoreCase))
                            { quelle = e; break; }
                        if (quelle == Entity.Null) { Fehler("Material nicht gefunden: " + name); return; }
                        _quellen[name] = quelle;
                    }
                    int prioritaet = g == 1 || g >= 4 ? ParkingLotPreviewStyle.MusterPrioritaet
                        : ParkingLotPreviewStyle.MusterBodenPrioritaet;
                    var klon = _klone.FordereAn(quelle, prioritaet, out bool fehlt, out bool aufgegeben, raeumt: false);
                    if (fehlt || aufgegeben) { Fehler("Musterklon nicht verfuegbar: " + name); return; }
                    if (klon == Entity.Null) { wartet = true; continue; }
                    var daten = EntityManager.GetComponentData<AreaData>(klon);
                    var flags = EntityManager.GetComponentData<AreaGeometryData>(klon).m_Flags;
                    if (flags != 0 || !daten.m_Archetype.Valid
                        || !_prefabs.TryGetPrefab<SurfacePrefab>(klon, out var muster)
                        || muster.GetComponent<ParkingLotMusterkennung>() == null)
                    { Fehler("Unsicherer Musterarchetyp oder Geometrieflags " + flags); return; }
                    klone[g] = klon;
                }
                if (wartet)
                {
                    World.GetExistingSystemManaged<ParkingLotToolSystem>()?.MusterMaske(null);
                    Warte("Musterklone noch nicht fertig"); return;
                }
                // Keine Warteschlange: nur der letzte Wunsch wird an den nativen Generator uebergeben.
                var uhr = Stopwatch.StartNew();
                _beginn = Stopwatch.GetTimestamp();
                if (!_neuester.IstAktuell(stand)) return;
                Bereinige();
                _soll = _plan.Stuecke.Length;
                var hoehen = _terrain.GetHeightData(waitForPending: true);
                foreach (var teil in _plan.Stuecke)
                {
                    var klon = klone[teil.Gruppe];
                    if (klon == Entity.Null) throw new InvalidOperationException("Material des geplanten Stuecks fehlt.");
                    var definition = EntityManager.CreateEntity();
                    // Archetyp traegt denselben Marker schon bei der nativen Erzeugung.
                    EntityManager.AddComponent<ParkingLotMusterflaeche>(definition);
                    EntityManager.AddComponentData(definition, new CreationDefinition {
                        m_Prefab = klon, m_Flags = CreationFlags.Permanent,
                    });
                    var ring = teil.Ring;
                    var nodes = EntityManager.AddBuffer<Game.Areas.Node>(definition);
                    nodes.ResizeUninitialized(ring.Length + 1);
                    for (int i = 0; i < ring.Length; i++)
                    {
                        var p = new float3(ring[i].x, 0, ring[i].y);
                        p.y = TerrainUtils.SampleHeight(ref hoehen,p);
                        nodes[i] = new Game.Areas.Node(p,float.MinValue);
                    }
                    nodes[ring.Length] = nodes[0];
                    EntityManager.AddComponent<Updated>(definition);
                    ParkingLotToolSystem.NurDiesesBild(EntityManager,definition);
                }
                _ausgabeStand = stand;
                _warteGeometrie = _warteSichtbar = _soll > 0; _bild = UnityEngine.Time.frameCount;
                _bildzeitVorher = UnityEngine.Time.unscaledDeltaTime * 1000f;
                World.GetExistingSystemManaged<ParkingLotToolSystem>()?.MusterMaske(_soll > 0 ? _plan.Ausschnitt : (OberflaechenMuster.Bereich?)null);
                _wartet = null;
                Melde($"Satz {stand} angefordert: {_soll} Permanent-Definitionen/{_plan.Ecken} Ecken; "
                    + $"verworfen {_plan.Verworfen}, verkleinert {_plan.Verkleinerungen}; "
                    + $"Schnitt+Annahme {_schnittMs:F3} ms, Definition+Maskenwechsel {uhr.Elapsed.TotalMilliseconds:F3} ms; "
                    + "Materialien " + string.Join(" / ", materialien));
            }
            catch (Exception e) { Fehler(e.Message); }
        }

        private void EntferneAbgebrochenenSatz()
        {
            try
            {
                if (_ausgabeStand != 0 || !_marker.IsEmptyIgnoreFilter || !_eingaben.IsEmptyIgnoreFilter)
                {
                    Bereinige(); _ausgabeStand = 0;
                    World.GetExistingSystemManaged<ParkingLotToolSystem>()?.MusterMaske(null);
                }
                Warte("ESC: bis zum naechsten Layout-/Flaechenwechsel ausgeblendet");
            }
            catch (Exception e) { Fehler(e.Message); }
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (_marker.IsEmptyIgnoreFilter) return;
            if (!_ladebereit || !_wunsch || !_neuester.IstAktuell(_ausgabeStand)) { Entferne("ohne aktuellen Wunsch"); return; }
            // GenerateAreas/Barrieren laufen vor ModificationEnd. Eingaben nur einmal verbrauchen.
            int zahl = _art == EchteVorschauArt.Flaechen ? _flaechen.CalculateEntityCount() : _objekte.CalculateEntityCount();
            if (zahl > _soll || _marker.CalculateEntityCount() != zahl) { Entferne("Mehrfachsatz " + zahl + "/" + _soll); return; }
            if (zahl == 0) return;
            if (!_eingaben.IsEmptyIgnoreFilter)
                using (var eingaben = _eingaben.ToEntityArray(Allocator.Temp))
                    foreach (var e in eingaben) EntityManager.DestroyEntity(e);
            if (!_warteGeometrie && !_warteSichtbar) return;
            int bilder = UnityEngine.Time.frameCount-_bild;
            if (bilder > 60 && bilder % 15 != 0) return;
            using var flaechen = (_art == EchteVorschauArt.Flaechen ? _flaechen : _objekte).ToEntityArray(Allocator.Temp);
            int bereit = 0, sichtbar = 0, dreiecke = 0;
            foreach (var flaeche in flaechen)
            {
                if (_art != EchteVorschauArt.Flaechen)
                {
                    if (EntityManager.HasComponent<Game.Objects.Transform>(flaeche)) bereit++;
                    if (EntityManager.HasBuffer<Game.Rendering.MeshBatch>(flaeche)
                        && EntityManager.GetBuffer<Game.Rendering.MeshBatch>(flaeche).Length > 0
                        && EntityManager.HasComponent<Game.Rendering.CullingInfo>(flaeche)
                        && EntityManager.GetComponentData<Game.Rendering.CullingInfo>(flaeche).m_PassedCulling != 0) sichtbar++;
                    continue;
                }
                if (EntityManager.HasBuffer<Game.Areas.Triangle>(flaeche)
                    && EntityManager.GetBuffer<Game.Areas.Triangle>(flaeche).Length > 0) bereit++;
                if (!EntityManager.HasComponent<Game.Areas.Batch>(flaeche)) continue;
                var batch = EntityManager.GetComponentData<Game.Areas.Batch>(flaeche);
                if (batch.m_AllocatedSize > 0 && batch.m_VisibleCount > 0)
                { sichtbar++; dreiecke += batch.m_VisibleCount; }
            }
            double ms = (Stopwatch.GetTimestamp()-_beginn)*1000.0/Stopwatch.Frequency;
            if (_warteGeometrie && bereit == _soll)
            {
                _warteGeometrie = false;
                Melde($"Geometrie bereit: Satz {_ausgabeStand}; Markerzahl {zahl}/{_soll}, {bilder} Bilder/{ms:F2} ms Wandzeit");
            }
            if (!_warteSichtbar || sichtbar == 0) return;
            _warteSichtbar = false;
            Melde($"Renderbatch erstmals sichtbar gemeldet: Satz {_ausgabeStand}; {sichtbar}/{_soll} Musterelemente, "
                + $"{bilder} Bilder/{ms:F2} ms ab Definition; ab Anforderung {UnityEngine.Time.frameCount-_anforderungBild} Bilder/"
                + $"{(Stopwatch.GetTimestamp()-_anforderungBeginn)*1000.0/Stopwatch.Frequency:F2} ms (einschl. Klonwartezeit); "
                + $"sichtbare Flaechendreiecke {dreiecke}, Bildzeit vorher {_bildzeitVorher:F2}/"
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
            _wunsch = false; _wahl = null; _fehler = null; _plan = null; _objektplan = null; _ausgabeStand = 0; _wartet = null; _neuester.Verwirf();
            World.GetExistingSystemManaged<ParkingLotToolSystem>()?.MusterMaske(null);
            if (offen) Melde($"entfernt ({grund}): {anzahl} Musterelemente; eigene Eingaben beendet");
        }

        internal void NachLaden()
        {
            int anzahl = 0;
            try { anzahl = Bereinige(); }
            catch (Exception e) { Melde("Ladebereinigung wird im Folgebild wiederholt: " + e.Message); }
            _wunsch = false; _wahl = _fehler = null; _quellen.Clear(); _plan = null; _objektplan = null; _ausgabeStand = 0; _wartet = null; _neuester.Verwirf();
            _ladebereit = true;
            World.GetExistingSystemManaged<ParkingLotToolSystem>()?.MusterMaske(null);
            Melde($"Ladebereinigung: {anzahl} Musterelemente entfernt; eigene Eingaben beendet");
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
            try { Bereinige(); }
            catch (Exception e) { Melde("Bereinigung nach Vorschaufehler: " + e.Message); }
            World.GetExistingSystemManaged<ParkingLotToolSystem>()?.MusterMaske(null);
            _fehler = text;
            Melde("nicht angelegt: " + text);
        }

        // Stille Wartegruende einmal je Wechsel melden, nie je Bild.
        private string _wartet;
        private void Warte(string grund)
        {
            if (_wartet == grund) return;
            _wartet = grund;
            Melde("wartet: " + grund);
        }

        private static void Melde(string text)
            => ParkingLotHintergrundDiagnose.Sicher(() => Mod.log.Info("PLT-SurfacePreview: " + text));
    }
}
