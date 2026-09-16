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
        private ValueBinding<string> _vegetation, _vegetationCatalog;
        private readonly List<VegetationAsset> _vegetationAssets = new List<VegetationAsset>();
        private readonly List<VegetationSet> _vegetationSets = new List<VegetationSet>();
        private string VegetationSetsPath => Path.Combine(Path.GetDirectoryName(SettingsPath()), "vegetation-sets.json");
        internal VegetationOptions Vegetation => JsonConvert.DeserializeObject<VegetationOptions>(_vegetation.value) ?? new VegetationOptions();
        internal string VegetationJson => _vegetation.value;
        internal VegetationAsset[] VegetationAssets => _vegetationAssets.ToArray();
        internal void RestoreVegetation(string json)
            => _vegetation.Update(string.IsNullOrEmpty(json) ? JsonConvert.SerializeObject(new VegetationOptions()) : json);
        private void InitVegetation()
        {
            AddBinding(_vegetation = new ValueBinding<string>(Group, "Vegetation", JsonConvert.SerializeObject(new VegetationOptions())));
            AddBinding(_vegetationCatalog = new ValueBinding<string>(Group, "VegetationCatalog", "{\"Assets\":[],\"Sets\":[]}"));
            AddBinding(new TriggerBinding(Group, "RefreshVegetation", RefreshVegetation));
            AddBinding(new TriggerBinding<string>(Group, "SetVegetation", json => {
                try {
                    var v = JsonConvert.DeserializeObject<VegetationOptions>(json);
                    if (v == null) return;
                    v.Density = Math.Max(0, Math.Min(100, v.Density)); v.Ages &= 63;
                    if (v.Ages == 0) v.Ages = 4;
                    v.Species = (v.Species ?? Array.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id) && id.Length <= 512).Distinct().OrderBy(id => id).Take(512).ToArray();
                    var next = JsonConvert.SerializeObject(v);
                    var tool=Tool(); var before=tool?.CaptureUndoState();
                    if(UpdateValue(_vegetation,next)) {
                        tool?.CommitUndoState(before, ParkingLotTexte.T("Vegetation geändert",
                            "vegetation changed"));
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
        private void SaveVegetationSets()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(VegetationSetsPath));
            var tmp = VegetationSetsPath + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(_vegetationSets.Where(s => s.Custom), Formatting.Indented));
            if (File.Exists(VegetationSetsPath)) File.Replace(tmp, VegetationSetsPath, VegetationSetsPath + ".bak");
            else File.Move(tmp, VegetationSetsPath);
        }
        private void PublishVegetation() => _vegetationCatalog.Update(JsonConvert.SerializeObject(new {
            Assets = _vegetationAssets, Sets = _vegetationSets }));
        private void RefreshVegetation()
        {
            _vegetationAssets.Clear(); _vegetationSets.Clear();
            var prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            using (var query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlantData>(), ComponentType.ReadOnly<ObjectGeometryData>()))
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
                        Tree = tree, Spacing = math.max(0.5f, math.max(size.x, size.z)), Prefab = entity });
                }
            _vegetationAssets.Sort((a,b) => string.Compare(a.Name,b.Name,StringComparison.CurrentCulture));
            AddVegetationSet("wild-deciduous", "Wilde Laubbäume", "Wild deciduous trees", "TreesDeciduous",
                "EU_AlderTree01", "BirchTree01", "NA_LondonPlaneTree01", "NA_LindenTree01", "NA_HickoryTree01", "EU_ChestnutTree01", "OakTree01");
            AddVegetationSet("wild-coniferous", "Wilde Nadelbäume", "Wild coniferous trees", "TreesNeedle", "PineTree01", "SpruceTree01");
            AddVegetationSet("wild-bushes", "Wilde Büsche", "Wild bushes", "Bushes", "GreenBushWild01", "GreenBushWild02", "FlowerBushWild01", "FlowerBushWild02");
            Mod.log.Info("PLT-Vegetationskatalog: " + _vegetationAssets.Count + " echte Pflanzen; "
                + string.Join("; ", _vegetationSets.Select(set=>set.Name+"="+set.Species.Length)));
            try { if (File.Exists(VegetationSetsPath)) _vegetationSets.AddRange((JsonConvert.DeserializeObject<List<VegetationSet>>(File.ReadAllText(VegetationSetsPath)) ?? new List<VegetationSet>()).Where(s => s.Custom && s.Species != null)); }
            catch(Exception e) { Mod.log.Warn("Vegetationssets konnten nicht geladen werden: " + e.Message); }
            PublishVegetation();
        }
        // Kompatible Set-Zusammensetzung nach den auf diesem Rechner installierten
        // Tree-Controller-31-Setdefinitionen (yenyang, MIT). Nur Asset-IDs/Daten,
        // kein fremder Programmcode; Referenz/Lizenz siehe VEGETATION-FIX-20260914.md.
        private void AddVegetationSet(string id,string de,string en,string icon,params string[] names)
        {
            _vegetationSets.Add(new VegetationSet {Id=id,Name=ParkingLotTexte.T(de,en),Icon="coui://uil/Standard/"+icon+".svg",
                Species=_vegetationAssets.Where(a=>names.Any(n=>a.Id=="StaticObjectPrefab:"+n)).Select(a=>a.Id).ToArray()});
        }
        private static string VegetationName(PrefabBase prefab)
        {
            var dictionary = GameManager.instance.localizationManager.activeDictionary;
            return dictionary.TryGetValue("Assets.NAME[" + prefab.name + "]", out var name) ? name : prefab.name;
        }
    }
}
