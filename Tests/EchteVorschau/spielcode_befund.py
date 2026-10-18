"""Lokale Quellcodebelege, keine behauptete Ausfuehrung im Spiel."""
from pathlib import Path
import json,sys,re

def main():
    game=Path(sys.argv[1]); root=Path(__file__).resolve().parents[2]
    def g(p):return (game/p).read_text(encoding="utf-8-sig")
    def m(p):return (root/p).read_text(encoding="utf-8-sig")
    obj=g("Game.Tools/GenerateObjectsSystem.cs"); geo=g("Game.Prefabs/ObjectGeometryPrefab.cs")
    register=m("Tools/ParkingLotMusterobjektPrefab.cs"); archetyp=m("Tools/ParkingLotMusterobjektArchetyp.cs")
    emitter=m("Tools/ParkingLotMusterSystem.Objekte.cs"); system=m("Tools/ParkingLotMusterSystem.cs")
    objutils=g("Game.Objects/ObjectUtils.cs"); search=g("Game.Objects/SearchSystem.cs")
    override=g("Game.Objects/OverrideSystem.cs"); batch=g("Game.Rendering/BatchInstanceSystem.cs")
    belege={
      "Ausgabe_aus_ObjectData_Archetyp":"m_ObjectData[component.m_Prefab].m_Archetype" in obj,
      "Permanent_ohne_Temp":"if ((definitionData.m_Flags & CreationFlags.Permanent) == 0)" in obj,
      "Owner_nur_bei_Vorgabe":"else if (definitionData.m_Owner != Entity.Null)" in obj,
      "Keine_Fremdbezuege_in_eigener_Ausgabe":"daten.m_Owner != Entity.Null" in emitter and "HasComponent<ParkingLotPartRelation>" in emitter,
      "Keine_Quellkomponenten_kopiert":"AddComponentFrom" not in register and "original.components" not in register,
      "Keine_Tree_Plant_im_natuerlichen_Geometriearchetyp":"ReadWrite<Tree>" not in geo and "ReadWrite<Plant>" not in geo,
      "Marker_vor_CreateEntity":"c.Add(ComponentType.ReadWrite<ParkingLotMusterflaeche>())" in archetyp,
      "Keine_Emissive_LightState":"c.Remove(ComponentType.ReadWrite<Emissive>())" in archetyp and "c.Remove(ComponentType.ReadWrite<LightState>())" in archetyp,
      "Kollisionsmaske_ignoriert_Marker":bool(re.search(r"GeometryFlags.Marker.*?ignoreMarkers.*?return \(CollisionMask\)0",objutils,re.S)),
      "Override_ignoriert_Marker_im_Spiel":"bool ignoreMarkers = !m_EditorMode ||" in override and "GetCollisionMask(objectGeometryData, ignoreMarkers)" in override,
      "Darstellung_braucht_nicht_Geometrie_Physical":"m_ObjectMarkerData.HasComponent(cullingData.m_Entity)" in batch,
      "Suchbaum_IsTree_nur_mit_Tree":"bool flag3 = chunk.Has(ref m_TreeType)" in search,
      "Eigene_flags_vor_Bereit":"geometrie.m_Flags = GeometryFlags.Marker | GeometryFlags.WalkThrough" in register,
      "Unerlaubte_Native_Komponenten_abgewiesen":"GetTypeIndex<Tree>()" in register and "GetTypeIndex<Game.Buildings.ElectricityConsumer>()" in register,
      "Native_Originalwuerfel_erhalten":"kopie.Add(m)" in register,
      "Keine_Unterobjekte_unbemerkt_weggelassen":"Unterobjekte werden nicht still weggelassen" in register,
      "Wachstum_nur_Decoration_aus":"WithDisabled<Decoration>()" in g("Game.Simulation/TreeGrowthSystem.cs"),
      "Holz_im_Original_TreeObject":"m_WoodAmount" in g("Game.Prefabs/TreeObject.cs"),
      "Strassenlichtsystem_nur_Road_Building_Watercraft":all(s in g("Game.Simulation/StreetLightSystem.cs") for s in ("ReadOnly<Road>()","ReadOnly<Building>()","ReadOnly<Watercraft>()")),
      "Kosten_aus_Temp":"num += temp.m_Cost" in g("Game.Tools/ToolApplySystem.cs"),
      "Alterszustand_Wuchs128_Skalierung1":"tree.m_Growth - 128" in g("Game.Rendering/BatchDataHelpers.cs"),
      "Gemeinsame_Markerquery_ueber_Arten":"_marker.CalculateEntityCount() != zahl" in system,
      "Wiederanmeldung_vor_Prefabinitialisierung":"UpdateBefore<ParkingLotMusterobjektPrefabSystem, Game.Prefabs.PrefabInitializeSystem>" in m("Mod.cs"),
      "Ladeloeschung_nach_Puffern":m("Tools/ParkingLotBayObjects.cs").index("PufferWiederhergestellt();") < m("Tools/ParkingLotBayObjects.cs").index("ParkingLotMusterSystem>().NachLaden();"),
      "Echte_Definitionsfunktionen_wiederverwendet":all(s in m("Tools/ParkingLotEchteObjektvorschau.cs") for s in ("LaternenDefinition(","PflanzenDefinition(")),
    }
    for k,v in belege.items():
        if not v:raise AssertionError("Beleg fehlt: "+k)
    ziel=root/".codex-build/echte-vorschau-5-20261018/spielcode.json"
    ziel.write_text(json.dumps(belege,indent=2),encoding="utf-8")
    print(f"Echte Vorschau: {len(belege)} Quellcodebelege, kein nativer Spieltest.")

if __name__=="__main__":
    try: main()
    except Exception as e: print("FEHLER EchteVorschau: "+str(e));sys.exit(1)
