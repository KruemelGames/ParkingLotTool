using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Game;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Rendering;
using ParkingLotTool.Geometry;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /** Registrierung ausschliesslich vor den nativen Prefab-Initialisierern. */
    public sealed partial class ParkingLotMusterobjektPrefabSystem : GameSystemBase
    {
        private sealed class Eintrag
        {
            public string Id, Beschreibung;
            public int Alter;
            public Entity Quelle, Klon;
            public bool Bereit, Fehler;
        }
        private readonly Dictionary<string,Eintrag> _eintraege = new Dictionary<string,Eintrag>();
        private readonly Dictionary<(Entity,int),Eintrag> _schnell = new Dictionary<(Entity,int),Eintrag>();
        private readonly HashSet<string> _merken = new HashSet<string>();
        private readonly HashSet<string> _offen = new HashSet<string>();
        private PrefabSystem _prefabs;
        private string _gelesen, _speicherFehler;
        private int _suchlauf;
        private const SubMeshFlags Altersflags = SubMeshFlags.RequireChild | SubMeshFlags.RequireTeen
            | SubMeshFlags.RequireAdult | SubMeshFlags.RequireElderly | SubMeshFlags.RequireDead | SubMeshFlags.RequireStump;

        [Preserve] protected override void OnCreate()
        { base.OnCreate(); _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>(); }

        [Preserve] protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            _schnell.Clear(); _gelesen=null; _suchlauf=0;
            foreach (var e in _eintraege.Values)
            { e.Quelle=e.Klon=Entity.Null; e.Bereit=e.Fehler=false; }
            base.OnGamePreload(purpose,mode);
        }

        internal Entity FordereAn(Entity quelle, int alter, out bool fehler)
        {
            fehler = false;
            if (_schnell.TryGetValue((quelle,alter),out var cache))
            { fehler=cache.Fehler; return cache.Bereit && EntityManager.Exists(cache.Klon) ? cache.Klon : Entity.Null; }
            string id = ParkingLotPrefabname.Von(EntityManager,quelle).ToString();
            if (string.IsNullOrEmpty(id)) { fehler = true; return Entity.Null; }
            var beschreibung = "1|" + id + "|" + alter;
            if (!_eintraege.TryGetValue(beschreibung,out var e))
            {
                e = new Eintrag { Id=id, Beschreibung=beschreibung, Alter=alter, Quelle=quelle };
                _eintraege.Add(beschreibung,e);
                Merke(beschreibung);
            }
            _schnell[(quelle,alter)] = e;
            fehler = e.Fehler;
            return e.Bereit && EntityManager.Exists(e.Klon) ? e.Klon : Entity.Null;
        }
        private void Merke(string beschreibung) => _merken.Add(beschreibung);
        private void SpeichereGemerkte()
        {
            var optionen = Mod.Optionen;
            if (optionen == null || _merken.Count == 0) return;
            var zeilen = new HashSet<string>((optionen.MusterObjektklone ?? "").Split('\n'),StringComparer.Ordinal);
            zeilen.Remove("");
            foreach (var z in _merken) zeilen.Add(z);
            optionen.MusterObjektklone = string.Join("\n",zeilen);
            // Einmal je Prefabphase, vor der ersten Ausgabe. Kein Spielstand-Save.
            optionen.ApplyAndSave();
            _merken.Clear();
        }
        private void LiesGemerkte()
        {
            var text = Mod.Optionen?.MusterObjektklone ?? "";
            if (text != _gelesen)
            {
                _gelesen = text; _suchlauf=0;
                foreach (var z in text.Split('\n')) if (!string.IsNullOrWhiteSpace(z)) _offen.Add(z.Trim());
            }
            if (_suchlauf++ > 600 && _suchlauf % 120 != 0) return;
            foreach (var z in _offen.ToArray())
            {
                var t = z.Split('|');
                if (t.Length != 3 || t[0] != "1" || !int.TryParse(t[2],out var alter) || alter < -1 || alter > 5)
                { _offen.Remove(z); continue; }
                if (_eintraege.ContainsKey(z)) { _offen.Remove(z); continue; }
                Entity quelle;
                try { quelle=ParkingLotPrefabname.Aufloesen(EntityManager,ParkingLotPrefabname.Fest(t[1])); }
                catch (Exception ex)
                { _offen.Remove(z); Melde("Ungueltiger Kloneintrag ausgelassen: " + ex.Message); continue; }
                if (quelle == Entity.Null) continue;
                _eintraege.Add(z,new Eintrag { Id=t[1],Beschreibung=z,Alter=alter,Quelle=quelle });
                _offen.Remove(z);
            }
        }
        [Preserve] protected override void OnUpdate()
        {
            try { SpeichereGemerkte(); _speicherFehler=null; }
            catch (Exception ex)
            {
                if (_speicherFehler != ex.Message) { _speicherFehler=ex.Message; Melde("Klonnamen nicht gespeichert: " + ex.Message); }
                return;
            }
            if (_merken.Count>0) return; // Ohne gespeicherten Klonnamen noch keine Ausgabe.
            LiesGemerkte();
            foreach (var e in _eintraege.Values)
            {
                if (e.Bereit && (!EntityManager.Exists(e.Klon) || !EntityManager.Exists(e.Quelle)))
                { e.Bereit=false; e.Klon=Entity.Null; }
                if (e.Fehler || e.Bereit) continue;
                try
                {
                    if (!EntityManager.Exists(e.Quelle))
                    { e.Quelle = ParkingLotPrefabname.Aufloesen(EntityManager,ParkingLotPrefabname.Fest(e.Id)); if (e.Quelle == Entity.Null) continue; }
                    if (EntityManager.HasComponent<Created>(e.Quelle)) continue;
                    if (e.Klon == Entity.Null) { Registriere(e); continue; }
                    if (EntityManager.HasComponent<Created>(e.Klon)) continue;
                    var daten = EntityManager.GetComponentData<ObjectData>(e.Klon);
                    if (!daten.m_Archetype.Valid || !EntityManager.HasBuffer<SubMesh>(e.Klon)
                        || EntityManager.GetBuffer<SubMesh>(e.Klon).Length == 0) continue;
                    bool marker=false;
                    using (var typen=daten.m_Archetype.GetComponentTypes())
                        foreach (var typ in typen)
                        {
                            if (typ.TypeIndex==TypeManager.GetTypeIndex<ParkingLotMusterflaeche>()) marker=true;
                            if (typ.TypeIndex==TypeManager.GetTypeIndex<Tree>() || typ.TypeIndex==TypeManager.GetTypeIndex<Plant>()
                                || typ.TypeIndex==TypeManager.GetTypeIndex<StreetLight>() || typ.TypeIndex==TypeManager.GetTypeIndex<Owner>()
                                || typ.TypeIndex==TypeManager.GetTypeIndex<ParkingLotPartRelation>() || typ.TypeIndex==TypeManager.GetTypeIndex<Game.Objects.NetObject>()
                                || typ.TypeIndex==TypeManager.GetTypeIndex<Game.Simulation.UpdateFrame>()
                                || typ.TypeIndex==TypeManager.GetTypeIndex<Game.Buildings.ElectricityConsumer>()
                                || typ.TypeIndex==TypeManager.GetTypeIndex<Game.Effects.EnabledEffect>()
                                || typ.TypeIndex==TypeManager.GetTypeIndex<Emissive>() || typ.TypeIndex==TypeManager.GetTypeIndex<LightState>()
                                || typ.TypeIndex==TypeManager.GetTypeIndex<Game.Objects.Marker>())
                                throw new InvalidOperationException("Unerlaubte Komponente im Musterarchetyp: " + typ);
                        }
                    if (!marker) throw new InvalidOperationException("Vorschaumarker fehlt im Ausgabe-Archetyp.");
                    // Nur eigene Prefabdaten. Marker-Geometrie ohne Marker-Komponente bleibt normal sichtbar,
                    // wird von ObjectUtils.GetCollisionMask(ignoreMarkers:true) aber vollstaendig ignoriert.
                    var geometrie = EntityManager.GetComponentData<ObjectGeometryData>(e.Klon);
                    geometrie.m_Flags = GeometryFlags.Marker | GeometryFlags.WalkThrough;
                    EntityManager.SetComponentData(e.Klon,geometrie);
                    var quelle = EntityManager.GetBuffer<SubMesh>(e.Quelle,true);
                    var kopie = new List<SubMesh>();
                    var flag = e.Alter < 0 ? (SubMeshFlags)0 : Altersflag(e.Alter);
                    for (int i=0;i<quelle.Length;i++)
                    {
                        var m = quelle[i];
                        if (e.Alter >= 0 && (m.m_Flags & Altersflags) != 0 && (m.m_Flags & flag) == 0) continue;
                        if (e.Alter >= 0) m.m_Flags &= ~Altersflags;
                        kopie.Add(m); // Einschliesslich der originalen Varianten-/Farbwuerfel.
                    }
                    if (kopie.Count == 0) throw new InvalidOperationException("Kein sichtbares Altersmesh.");
                    var ziel = EntityManager.GetBuffer<SubMesh>(e.Klon);
                    ziel.Clear(); foreach (var m in kopie) ziel.Add(m);
                    if (EntityManager.HasComponent<TreeData>(e.Klon) || EntityManager.HasComponent<PlantData>(e.Klon)
                        || EntityManager.HasComponent<StreetLightData>(e.Klon) || EntityManager.HasComponent<PlaceableObjectData>(e.Klon))
                        throw new InvalidOperationException("Simulationsdaten am Darstellungsklon.");
                    e.Bereit = true;
                }
                catch (Exception ex) { e.Fehler=true; Melde("Darstellungsklon nicht verfuegbar: " + e.Id + ": " + ex.Message); }
            }
        }
        private void Registriere(Eintrag e)
        {
            if (!_prefabs.TryGetPrefab<StaticObjectPrefab>(e.Quelle,out var original)
                || !EntityManager.HasBuffer<SubMesh>(e.Quelle)) throw new InvalidOperationException("Kein statisches Mesh-Prefab.");
            if (EntityManager.HasBuffer<Game.Prefabs.SubObject>(e.Quelle)
                && EntityManager.GetBuffer<Game.Prefabs.SubObject>(e.Quelle,true).Length>0)
                throw new InvalidOperationException("Unterobjekte werden nicht still weggelassen.");
            if (EntityManager.HasBuffer<SubMeshGroup>(e.Quelle)) throw new InvalidOperationException("Meshgruppen werden nicht angenaehert.");
            var klon = ScriptableObject.CreateInstance<ParkingLotMusterobjektPrefab>();
            using (var sha = SHA256.Create()) klon.name = "PLT Musterobjekt v1 "
                + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(e.Beschreibung))).Replace("-","");
            if (_prefabs.TryGetPrefab(new PrefabID(nameof(ParkingLotMusterobjektPrefab),klon.name),out var vorhanden)
                && vorhanden != null && _prefabs.TryGetEntity(vorhanden,out var entity))
            { UnityEngine.Object.Destroy(klon); e.Klon=entity; return; }
            klon.m_Circular = original.m_Circular;
            klon.m_Meshes = (original.m_Meshes ?? Array.Empty<ObjectMeshInfo>())
                .Where(m=>m != null && EchteVorschau.Altersmesh((int)m.m_RequireState,e.Alter))
                .Select(m=>new ObjectMeshInfo {m_Mesh=m.m_Mesh,m_Position=m.m_Position,m_Rotation=m.m_Rotation,
                    m_RequireState=e.Alter >= 0 && (int)m.m_RequireState>=1 && (int)m.m_RequireState<=6 ? ObjectState.None : m.m_RequireState}).ToArray();
            if (klon.m_Meshes.Length == 0) throw new InvalidOperationException("Kein passendes Mesh.");
            // Keine Effects, SubObjects, TreeObject, PlantObject, PlaceableObject oder StreetLightObject kopieren.
            if (!_prefabs.AddPrefab(klon)) throw new InvalidOperationException("Klonregistrierung abgelehnt.");
            e.Klon = _prefabs.GetEntity(klon);
        }
        private static SubMeshFlags Altersflag(int alter)
        {
            switch (alter)
            {
                case 0: return SubMeshFlags.RequireChild;
                case 1: return SubMeshFlags.RequireTeen;
                case 2: return SubMeshFlags.RequireAdult;
                case 3: return SubMeshFlags.RequireElderly;
                case 4: return SubMeshFlags.RequireDead;
                default: return SubMeshFlags.RequireStump;
            }
        }
        private static void Melde(string text) => ParkingLotHintergrundDiagnose.Sicher(()=>Mod.log.Warn("PLT-EchteVorschau: " + text));
    }
}
