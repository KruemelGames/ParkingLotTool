using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Colossal.UI.Binding;
using Game.Prefabs;
using Game.SceneFlow;
using Newtonsoft.Json;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    internal sealed class VegetationAsset
    {
        public string Id, Name, Icon;
        public bool Tree;
        public float Spacing;
        /** Hoehe der groessten Altersstufe (m_Bounds.max.y): reicht sie an die Leuchte einer Laterne? */
        [JsonIgnore] public float Hoehe;
        [JsonIgnore] public Entity Prefab;
    }
    internal sealed class VegetationSet
    {
        public string Id, Name, Icon;
        public string[] Species = Array.Empty<string>();
        public bool Custom;
    }
    public sealed partial class ParkingLotUISystem
    {
        /** Zuletzt geloggter Vegetationskatalog - gleiche Zeile nicht nochmal. */
        private string _letzterVegetationskatalog;

        private ValueBinding<string> _vegetation, _vegetationCatalog, _vegetationDefault;
        private readonly List<VegetationAsset> _vegetationAssets = new List<VegetationAsset>();
        private readonly List<VegetationSet> _vegetationSets = new List<VegetationSet>();
        private string VegetationSetsPath => Path.Combine(Path.GetDirectoryName(SettingsPath()), "vegetation-sets.json");
        internal VegetationOptions Vegetation => JsonConvert.DeserializeObject<VegetationOptions>(_vegetation.value) ?? new VegetationOptions();
        internal string VegetationJson => _vegetation.value;
        internal VegetationAsset[] VegetationAssets => _vegetationAssets.ToArray();
        // Einmal durch die Klasse: ein alter Zettel ohne neues Feld (NoAging)
        // bekommt so dessen Standard, statt dass die Oberflaeche "fehlt" liest.
        internal void RestoreVegetation(string json)
        {
            VegetationOptions v = null;
            try { if (!string.IsNullOrEmpty(json)) v = JsonConvert.DeserializeObject<VegetationOptions>(json); }
            catch (Exception e) { Mod.log.Warn("Vegetationszettel unlesbar: " + e.Message); }
            _vegetation.Update(JsonConvert.SerializeObject(v ?? new VegetationOptions()));
        }
        private void InitVegetation()
        {
            AddBinding(_vegetation = new ValueBinding<string>(Group, "Vegetation", VegetationStandard()));
            AddBinding(_vegetationDefault = new ValueBinding<string>(Group, "VegetationDefault", VegetationStandard()));
            AddBinding(new TriggerBinding(Group, "SaveVegetationDefault", SpeichereVegetationsstandard));
            AddBinding(new TriggerBinding(Group, "ResetVegetation", SetzeVegetationZurueck));
            AddBinding(_vegetationCatalog = new ValueBinding<string>(Group, "VegetationCatalog", "{\"Assets\":[],\"Sets\":[]}"));
            AddBinding(new TriggerBinding(Group, "RefreshVegetation", RefreshVegetation));
            AddBinding(new TriggerBinding<string>(Group, "SetVegetation", json => {
                try {
                    var v = JsonConvert.DeserializeObject<VegetationOptions>(json);
                    if (v == null) return;
                    if (v.Enabled && v.Seed == 0)
                    {
                        v.Seed = Vegetation.Seed;
                        if (v.Seed == 0) v.Seed = BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0) | 1u;
                    }
                    v.Density = Math.Max(0, Math.Min(100, v.Density)); v.Ages &= 63;
                    if (v.Ages == 0) v.Ages = 4;
                    v.Species = (v.Species ?? Array.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id) && id.Length <= 512).Distinct().OrderBy(id => id).Take(512).ToArray();
                    var next = JsonConvert.SerializeObject(v);
                    var tool=Tool(); var before=tool?.CaptureUndoState();
                    if(UpdateValue(_vegetation,next)) {
                        tool?.CommitUndoState(before, () => ParkingLotTexte.T("vegetationUI.vegetationChanged"));
                        tool?.RefreshVegetationPreview();
                    }
                } catch (Exception e) { Mod.log.Warn("Vegetationseinstellung ungueltig: " + e.Message); }
            }));
            AddBinding(new TriggerBinding<string>(Group, "SaveVegetationSet", json => {
                try {
                    var set = JsonConvert.DeserializeObject<VegetationSet>(json);
                    if (set == null || string.IsNullOrWhiteSpace(set.Name)) return;
                    set.Name = set.Name.Trim().Substring(0, Math.Min(64, set.Name.Trim().Length));
                    set.Species = (set.Species ?? Array.Empty<string>()).Where(id => _vegetationAssets.Any(a => a.Id == id)).Distinct().ToArray();
                    if (set.Species.Length == 0) return;
                    set.Id = "custom:" + Guid.NewGuid().ToString("N"); set.Custom = true;
                    _vegetationSets.Add(set); SaveVegetationSets(); PublishVegetation();
                } catch (Exception e) { Mod.log.Warn("Vegetationsset konnte nicht gespeichert werden: " + e.Message); }
            }));
            AddBinding(new TriggerBinding<string>(Group, "DeleteVegetationSet", id => {
                _vegetationSets.RemoveAll(s => s.Custom && s.Id == id); SaveVegetationSets(); PublishVegetation();
            }));
        }
        // Fuer die Reparatur fehlender Assets: der gespeicherte Standard des Spielers.
        internal string StandardFlaecheStrasse => _defaults?.SurfaceRoad;
        internal string StandardFlaecheDeko => _defaults?.SurfaceDecoration;
        internal VegetationOptions StandardVegetation
            => JsonConvert.DeserializeObject<VegetationOptions>(VegetationStandard()) ?? new VegetationOptions();
        internal void AktualisiereVegetationskatalog() => RefreshVegetation();

        /** Der gespeicherte Standard, sonst die Werkswerte - immer ohne Zufallszahl. */
        private string VegetationStandard()
        {
            VegetationOptions v = null;
            try { if (!string.IsNullOrEmpty(_defaults?.Vegetation)) v = JsonConvert.DeserializeObject<VegetationOptions>(_defaults.Vegetation); }
            catch (Exception e) { Mod.log.Warn("Vegetationsstandard unlesbar, Werkswerte: " + e.Message); }
            v = v ?? new VegetationOptions();
            v.Seed = 0;
            return JsonConvert.SerializeObject(v);
        }

        private void SpeichereVegetationsstandard()
        {
            var v = Vegetation; v.Seed = 0;
            var next = _defaults.Clone();
            next.Vegetation = JsonConvert.SerializeObject(v);
            if (!TryWriteDefaults(next)) return;
            _defaults = next;
            _vegetationDefault.Update(VegetationStandard());
            SetStatus(ParkingLotTexte.T("vegetationUI.vegetationSavedAsDefault"));
        }

        private void SetzeVegetationZurueck()
        {
            var v = JsonConvert.DeserializeObject<VegetationOptions>(VegetationStandard()) ?? new VegetationOptions();
            // Die Zufallszahl bleibt: sonst stuenden die Pflanzen woanders.
            v.Seed = Vegetation.Seed;
            if (v.Enabled && v.Seed == 0) v.Seed = BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0) | 1u;
            var tool = Tool(); var before = tool?.CaptureUndoState();
            if (UpdateValue(_vegetation, JsonConvert.SerializeObject(v)))
            {
                tool?.CommitUndoState(before, () => ParkingLotTexte.T("vegetationUI.vegetationReset"));
                tool?.RefreshVegetationPreview();
            }
            SetStatus(ParkingLotTexte.T("vegetationUI.vegetationResetToYourDefault"));
        }

        private void SaveVegetationSets()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(VegetationSetsPath));
            var tmp = VegetationSetsPath + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(_vegetationSets.Where(s => s.Custom), Formatting.Indented));
            if (File.Exists(VegetationSetsPath)) File.Replace(tmp, VegetationSetsPath, VegetationSetsPath + ".bak");
            else File.Move(tmp, VegetationSetsPath);
        }
        /** Feste Sets nach einem Sprachwechsel neu beschriften; eigene behalten ihren Namen. */
        private void BenenneFesteVegetationSets()
        {
            foreach (var s in _vegetationSets)
                if (!s.Custom) s.Name = ParkingLotTexte.T("vegetationSet." + s.Id);
        }
        private void PublishVegetation() => _vegetationCatalog.Update(JsonConvert.SerializeObject(new {
            Assets = _vegetationAssets, Sets = _vegetationSets }));
        private void RefreshVegetation()
        {
            _vegetationAssets.Clear(); _vegetationSets.Clear();
            var prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            // Ohne Deleted: ein im laufenden Spiel abgewaehltes Asset-Pack hinterlaesst
            // abgemeldete Prefabs, deren Index CS2 schon einem anderen gegeben hat.
            using (var query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlantData>(), ComponentType.ReadOnly<ObjectGeometryData>(),
                       ComponentType.Exclude<Game.Common.Deleted>()))
            using (var entities = query.ToEntityArray(Allocator.Temp))
                foreach (var entity in entities)
                {
                    if (!prefabs.TryGetPrefab<PrefabBase>(entity, out var prefab) || !(prefab is StaticObjectPrefab)) continue;
                    // Platzhalter ohne eigenes Pflanzenmodell niemals als Art anbieten.
                    if (EntityManager.HasComponent<PlaceholderObjectData>(entity)
                        || EntityManager.HasBuffer<PlaceholderObjectElement>(entity)) continue;
                    if (!(prefab is ObjectGeometryPrefab objectPrefab) || objectPrefab.m_Meshes == null || objectPrefab.m_Meshes.Length == 0) continue;
                    bool tree = EntityManager.HasComponent<TreeData>(entity);
                    var geometry = EntityManager.GetComponentData<ObjectGeometryData>(entity);
                    var size = geometry.m_Bounds.max - geometry.m_Bounds.min;
                    _vegetationAssets.Add(new VegetationAsset { Id = prefab.GetPrefabID().ToString(), Name = VegetationName(prefab),
                        Icon = prefab.TryGet<UIObject>(out var ui) && !string.IsNullOrEmpty(ui.m_Icon) ? ui.m_Icon : prefab.thumbnailUrl,
                        // Die ECHTE Groesse, ohne eigene Schranken: `ParkingVegetation.Spacing`
                        // vergleicht damit gegen dieselbe Zahl, die CS2 fuer seine
                        // Kollisionskreise benutzt (`ObjectGeometryData.m_Size`).
                        Tree = tree, Spacing = math.max(0.5f, math.max(size.x, size.z)), Prefab = entity,
                        Hoehe = geometry.m_Bounds.max.y });
                }
            _vegetationAssets.Sort((a,b) => string.Compare(a.Name,b.Name,StringComparison.CurrentCulture));
            // Der ganze Katalog einmal ins Log: wer pflanzt sich wie ein Baum, wer wie ein Busch,
            // und wer hat TreeData (Altersstufen). Die beiden fallen nicht zusammen.
            // Nur, wenn er sich geaendert hat: er wird je Laden mehrfach
            // gebaut, und dreimal dieselbe lange Zeile hilft niemandem.
            var katalog = "PLT-Vegetationskatalog Wuchs (Baum ab " + VegetationSpecies.BaumHoehe.ToString("F1") + " m): "
                + string.Join("; ", _vegetationAssets.Select(a => a.Name + " [" + a.Id.Replace("StaticObjectPrefab:", "") + "] "
                    + (a.Hoehe >= VegetationSpecies.BaumHoehe ? "Baum" : "Busch") + (a.Tree ? "/TreeData" : "")
                    + " " + a.Hoehe.ToString("F1") + " m hoch, " + a.Spacing.ToString("F1") + " m breit"));
            var katalogNeu = katalog != _letzterVegetationskatalog;
            _letzterVegetationskatalog = katalog;
            if (katalogNeu) Mod.log.Info(katalog);
            AddVegetationSet("wild-deciduous", "TreesDeciduous",
                "EU_AlderTree01", "BirchTree01", "NA_LondonPlaneTree01", "NA_LindenTree01", "NA_HickoryTree01", "EU_ChestnutTree01", "OakTree01");
            AddVegetationSet("wild-coniferous", "TreesNeedle", "PineTree01", "SpruceTree01");
            AddVegetationSet("wild-bushes", "Bushes", "GreenBushWild01", "GreenBushWild02", "FlowerBushWild01", "FlowerBushWild02");
            if (katalogNeu)
                Mod.log.Info("PLT-Vegetationskatalog: " + _vegetationAssets.Count + " echte Pflanzen; "
                    + string.Join("; ", _vegetationSets.Select(set=>set.Name+"="+set.Species.Length)));
            try { if (File.Exists(VegetationSetsPath)) _vegetationSets.AddRange((JsonConvert.DeserializeObject<List<VegetationSet>>(File.ReadAllText(VegetationSetsPath)) ?? new List<VegetationSet>()).Where(s => s.Custom && s.Species != null)); }
            catch(Exception e) { Mod.log.Warn("Vegetationssets konnten nicht geladen werden: " + e.Message); }
            PublishVegetation();
        }
        // Kompatible Set-Zusammensetzung nach den auf diesem Rechner installierten
        // Tree-Controller-31-Setdefinitionen (yenyang, MIT). Nur Asset-IDs/Daten,
        // kein fremder Programmcode; Referenz/Lizenz siehe VEGETATION-FIX-20260914.md.
        private void AddVegetationSet(string id,string icon,params string[] names)
        {
            _vegetationSets.Add(new VegetationSet {Id=id,Name=ParkingLotTexte.T("vegetationSet."+id),Icon="coui://uil/Standard/"+icon+".svg",
                Species=_vegetationAssets.Where(a=>names.Any(n=>a.Id=="StaticObjectPrefab:"+n)).Select(a=>a.Id).ToArray()});
        }
        private static string VegetationName(PrefabBase prefab)
        {
            var dictionary = GameManager.instance.localizationManager.activeDictionary;
            return dictionary.TryGetValue("Assets.NAME[" + prefab.name + "]", out var name) ? name : prefab.name;
        }
    }
}
