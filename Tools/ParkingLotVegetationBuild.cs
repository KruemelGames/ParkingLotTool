using System;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using Colossal.Serialization.Entities;
using Game.Common;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Game.Tools;
using Newtonsoft.Json;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    public struct ParkingLotVegetationReceipt : IBufferElementData, ISerializable
    {
        public byte Value;
        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter => writer.Write(Value);
        public void Deserialize<TReader>(TReader reader) where TReader : IReader => reader.Read(out Value);
    }
    internal sealed class VegetationReceipt
    {
        public int Version = 1;
        public string Options, Signature;
    }
    public sealed partial class ParkingLotToolSystem
    {
        internal void RefreshVegetationPreview()
        {
            if (_areaPreviewLayout != null) SetVegetationPreview(_areaPreviewLayout);
        }
        private void SetVegetationPreview(ParkingLayout layout)
        {
            layout.SurfacesForPlacement(_uiSystem?.FlaecheStrasseAn ?? true, _uiSystem?.FlaecheDekoAn ?? true, out var grass, out _);
            var options=_uiSystem?.Vegetation ?? new VegetationOptions();
            var assets=(_uiSystem?.VegetationAssets ?? Array.Empty<VegetationAsset>()).Where(a=>options.Species.Contains(a.Id)).ToArray();
            var species=assets.Select(a=>new VegetationSpecies {Id=a.Id,Tree=a.Tree,Spacing=a.Spacing}).ToArray();
            var plan=ParkingVegetation.Plan(grass,options,species);
            _overlay.SetVegetation(plan,species,_terrainSystem);
        }
        // Nur eigene neu erzeugte Temp-Baeume: Alter unmittelbar vor Apply festlegen.
        private readonly System.Collections.Generic.Dictionary<(Entity, float2), Game.Objects.Tree> _vegetationTreeStates = new();
        private readonly System.Collections.Generic.HashSet<(Entity, float2)> _vegetationPlanned = new();
        private bool ApplyVegetationAge(Entity entity, Entity prefab)
        {
            if (!EntityManager.HasComponent<Game.Objects.Transform>(entity)) return false;
            var position = EntityManager.GetComponentData<Game.Objects.Transform>(entity).m_Position;
            if (!_vegetationPlanned.Contains((prefab, position.xz))) return false;
            if (EntityManager.HasComponent<Game.Objects.Tree>(entity) && _vegetationTreeStates.TryGetValue((prefab, position.xz), out var tree))
            {
                // Nicht jede Baumart hat ein sechstes (Stumpf-)Mesh, wie auch Tree Controller prueft.
                if (tree.m_State == Game.Objects.TreeState.Stump && (!EntityManager.HasBuffer<SubMesh>(prefab) || EntityManager.GetBuffer<SubMesh>(prefab,true).Length <= 5))
                    tree.m_State = Game.Objects.TreeState.Dead;
                EntityManager.SetComponentData(entity, tree);
                // Der Mesh-Zustand muss nach dem Temp-Alterswechsel neu gebuendelt werden.
                if (!EntityManager.HasComponent<BatchesUpdated>(entity)) EntityManager.AddComponent<BatchesUpdated>(entity);
            }
            return true;
        }
        // OverrideSystem folgt Owner, nicht Attached/PLT-Relation. Der nackte
        // Traeger besitzt weder Object noch Transform/Area: keine Streu-Umverteilung.
        // Baum -> Traeger -> Lot laesst den nativen AreaIterator das eigene Lot ausnehmen.
        private void SetVegetationOwner(Entity plant, Entity carrier, Entity lot)
        {
            var carrierOwner = new Owner { m_Owner=lot };
            if(EntityManager.HasComponent<Owner>(carrier)) EntityManager.SetComponentData(carrier,carrierOwner);
            else EntityManager.AddComponentData(carrier,carrierOwner);
            var owner = new Owner { m_Owner=carrier };
            if(EntityManager.HasComponent<Owner>(plant)) EntityManager.SetComponentData(plant,owner);
            else EntityManager.AddComponentData(plant,owner);
        }
        private void LogVegetationAges(Entity lot)
        {
            using var parts=_editRelatedParts.ToEntityArray(Allocator.Temp);
            var counts=new System.Collections.Generic.Dictionary<string,int>();
            int hidden=0;
            foreach(var part in parts)
            {
                if(EntityManager.GetComponentData<ParkingLotPartRelation>(part).Lot!=lot || !EntityManager.HasComponent<Game.Objects.Tree>(part)) continue;
                string age=EntityManager.GetComponentData<Game.Objects.Tree>(part).m_State.ToString();
                counts[age]=counts.TryGetValue(age,out var n)?n+1:1;
                if(EntityManager.HasComponent<Overridden>(part)) hidden++;
            }
            Mod.log.Info("PLT-Vegetation IST Lot " + lot.Index + ": " + string.Join("; ",counts.Select(p=>p.Key+"="+p.Value)) + "; Overridden="+hidden);
        }
        private bool _vegetationPreserve;
        private VegetationReceipt _vegetationReceipt;
        private int _vegetationCount;
        private VegetationReceipt ReadVegetation(Entity lot)
        {
            if (lot == Entity.Null || !EntityManager.Exists(lot)) return null;
            if (EntityManager.HasBuffer<ParkingLotBuildText>(lot) && TryReadBuildText(EntityManager.GetBuffer<ParkingLotBuildText>(lot,true),4,out var saved) && !string.IsNullOrEmpty(saved))
            {
                try { return JsonConvert.DeserializeObject<VegetationReceipt>(saved); }
                catch(Exception e) { Mod.log.Warn("Vegetation im Hauptbauzettel unlesbar: " + e.Message); }
            }
            if (!EntityManager.HasBuffer<ParkingLotVegetationReceipt>(lot)) return null;
            var buffer = EntityManager.GetBuffer<ParkingLotVegetationReceipt>(lot,true);
            var bytes = new byte[buffer.Length];
            for (int i=0;i<bytes.Length;i++) bytes[i]=buffer[i].Value;
            try { return JsonConvert.DeserializeObject<VegetationReceipt>(Encoding.UTF8.GetString(bytes)); }
            catch(Exception e) { Mod.log.Warn("Vegetationsbauzettel unlesbar: " + e.Message); return null; }
        }
        private void LoadVegetation(Entity lot)
        {
            var receipt=ReadVegetation(lot);
            _uiSystem?.RestoreVegetation(receipt?.Options);
            LogVegetationAges(lot);
            Mod.log.Info("PLT-Vegetation laden Lot " + lot.Index + ": " + (receipt?.Options ?? "kein Vegetationsbauzettel; Standard aus"));
        }
        private void WriteVegetation(Entity lot)
        {
            if (_vegetationReceipt == null) throw new InvalidOperationException("Vegetationsbauplan fehlt vor dem Speichern.");
            string saved=JsonConvert.SerializeObject(_vegetationReceipt);
            AddBuildText(EntityManager.GetBuffer<ParkingLotBuildText>(lot),4,saved);
            var buffer = EntityManager.HasBuffer<ParkingLotVegetationReceipt>(lot)
                ? EntityManager.GetBuffer<ParkingLotVegetationReceipt>(lot) : EntityManager.AddBuffer<ParkingLotVegetationReceipt>(lot);
            buffer.Clear();
            foreach (byte b in Encoding.UTF8.GetBytes(saved))
                buffer.Add(new ParkingLotVegetationReceipt {Value=b});
            var read=ReadVegetation(lot);
            if(read?.Options!=_vegetationReceipt.Options || read.Signature!=_vegetationReceipt.Signature)
                throw new InvalidOperationException("Vegetationsbauzettel Ruecklesepruefung fehlgeschlagen.");
            Mod.log.Info("PLT-Vegetation gespeichert/geprueft Lot " + lot.Index + ": " + read.Options);
        }
        private string VegetationSignature(float2[][] grass, string options)
        {
            var s = new StringBuilder(options);
            foreach(var ring in grass) {s.Append('|');foreach(var p in ring) s.Append(Math.Round(p.x*1000)).Append(',').Append(Math.Round(p.y*1000)).Append(';');}
            using(var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(s.ToString())));
        }
        private int CreateVegetationDefinitions(float2[][] grass, ref TerrainHeightData heightData)
        {
            _vegetationPreserve=false; _vegetationCount=0; _vegetationTreeStates.Clear(); _vegetationPlanned.Clear();
            var options = _uiSystem?.Vegetation ?? new VegetationOptions();
            string json=JsonConvert.SerializeObject(options);
            _vegetationReceipt=new VegetationReceipt {Options=json, Signature=VegetationSignature(grass,json)};
            var old=ReadVegetation(_editLot);
            if (old != null && old.Signature == _vegetationReceipt.Signature)
            {
                _vegetationPreserve=true;
                Mod.log.Info("PLT-Vorbauzettel Vegetation: unveraendert, bestehende Pflanzen werden uebernommen. " + json);
                return 0;
            }
            var assets=(_uiSystem?.VegetationAssets ?? Array.Empty<VegetationAsset>()).Where(a=>options.Species.Contains(a.Id)).ToArray();
            var species=assets.Select(a=>new VegetationSpecies {Id=a.Id,Tree=a.Tree,Spacing=a.Spacing}).ToArray();
            var plan=ParkingVegetation.Plan(grass,options,species);
            Mod.log.Info("PLT-Vorbauzettel Vegetation: " + json + "; Kandidaten="+plan.Candidates+"; Pflanzen="+plan.Plants.Count+"; Grenze="+plan.Limited);
            if(options.Enabled && assets.Length==0) _uiSystem?.SetStatus(ParkingLotTexte.T("Vegetation: keine verfügbaren Pflanzen ausgewählt.","Vegetation: no available plants selected."));
            if(plan.Limited) Mod.log.Warn("PLT-Vegetation: Kandidatengrenze erreicht; Teilbepflanzung im Vorbauzettel.");
            foreach(var plant in plan.Plants)
            {
                var asset=assets[plant.Species];
                var position=new float3(plant.Position.x,0,plant.Position.y);
                position.y=TerrainUtils.SampleHeight(ref heightData,position);
                if(!math.all(math.isfinite(position))) continue;
                var random=new Unity.Mathematics.Random(plant.Seed == 0 ? 1u : plant.Seed);
                _vegetationPlanned.Add((asset.Prefab,position.xz));
                int ageIndex=ParkingVegetation.SelectAge(options.Ages, plant.Seed);
                float age=ParkingVegetation.AgeValue(ageIndex);
                if(asset.Tree) _vegetationTreeStates[(asset.Prefab,position.xz)]=new Game.Objects.Tree {
                    m_State=(Game.Objects.TreeState)(ageIndex==0?0:1<<(ageIndex-1)), m_Growth=128 };
                var definition=EntityManager.CreateEntity();
                EntityManager.AddComponentData(definition,new CreationDefinition {m_Prefab=asset.Prefab,m_RandomSeed=random.NextInt()});
                EntityManager.AddComponent<Updated>(definition);
                EntityManager.AddComponentData(definition,new ObjectDefinition {
                    m_Position=position,m_Rotation=quaternion.RotateY(random.NextFloat(0,math.PI*2)),
                    m_Probability=100,m_PrefabSubIndex=-1,m_Scale=new float3(1),m_Intensity=1,m_ParentMesh=-1,
                    m_Age=age,m_IsDecoration=false });
                RecordObjectDefinition("Vegetation",_vegetationCount++,asset.Prefab,definition,position);
            }
            return _vegetationCount;
        }
        private void TransferVegetation(Entity old, Entity next, Entity carrier)
        {
            if(!_vegetationPreserve) return;
            using var parts=_editRelatedParts.ToEntityArray(Allocator.Temp);
            int count=0;
            foreach(var part in parts)
            {
                var relation=EntityManager.GetComponentData<ParkingLotPartRelation>(part);
                if(relation.Lot!=old || !EntityManager.HasComponent<PrefabRef>(part)) continue;
                var prefab=EntityManager.GetComponentData<PrefabRef>(part).m_Prefab;
                if(!EntityManager.HasComponent<PlantData>(prefab)) continue;
                if(EntityManager.Exists(relation.Carrier) && EntityManager.HasBuffer<Game.Objects.SubObject>(relation.Carrier))
                {
                    var previous=EntityManager.GetBuffer<Game.Objects.SubObject>(relation.Carrier);
                    for(int i=previous.Length-1;i>=0;i--) if(previous[i].m_SubObject==part) previous.RemoveAt(i);
                }
                SetVegetationOwner(part,carrier,next);
                EntityManager.SetComponentData(part,new ParkingLotPartRelation {Lot=next,Carrier=carrier});
                EntityManager.SetComponentData(part,new Game.Objects.Attached(carrier,Entity.Null,0));
                if(EntityManager.HasComponent<Hidden>(part)) EntityManager.RemoveComponent<Hidden>(part);
                if(!EntityManager.HasComponent<BatchesUpdated>(part)) EntityManager.AddComponent<BatchesUpdated>(part);
                _hiddenByEdit.Remove(part);
                // Der neue Traeger ist nackt: keine Vanilla-Umverteilung von SubObjects.
                var children=EntityManager.GetBuffer<Game.Objects.SubObject>(carrier);
                bool found=false;foreach(var child in children) if(child.m_SubObject==part) found=true;
                if(!found) children.Add(new Game.Objects.SubObject {m_SubObject=part});
                count++;
            }
            Mod.log.Info("PLT-Bauzettel Vegetation: "+count+" bestehende Entities samt Alter uebernommen.");
        }
    }
}
