"""Read-only-Belege fuer Permanent-Ausgabe, Archetyp-Markierung und Kostenpfad."""
from pathlib import Path
import argparse
import json
import re
import sys


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("gamedll", type=Path)
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    def spiel(p): return (args.gamedll / p).read_text(encoding="utf-8-sig")
    def mod(p): return (root / p).read_text(encoding="utf-8-sig")
    generator = spiel("Game.Tools/GenerateAreasSystem.cs")
    prefab = spiel("Game.Prefabs/AreaPrefab.cs")
    apply = spiel("Game.Tools/ToolApplySystem.cs")
    render = spiel("Game.Prefabs/RenderedArea.cs")
    klon = mod("Tools/ParkingLotApronPrefab.cs")
    system = mod("Tools/ParkingLotMusterSystem.cs")
    kennung = mod("Tools/ParkingLotMusterkennung.cs")
    laden = mod("Tools/ParkingLotBayObjects.cs")
    belege = {
        "Native_Area_aus_Prefab_Archetyp": "m_CommandBuffer.CreateEntity(areaData.m_Archetype)" in generator,
        "Permanent_ohne_Temp": bool(re.search(r"if \(\(creationDefinition.m_Flags & CreationFlags.Permanent\) == 0\)\s*\{\s*m_CommandBuffer.AddComponent\(item, new Temp", generator)),
        "Archetyp_aus_allen_Komponenten": "list[i].GetArchetypeComponents(hashSet)" in prefab,
        "Marker_im_Archetyp": "components.Add(ComponentType.ReadWrite<ParkingLotMusterflaeche>())" in kennung,
        "Klon_haengt_Kennung_an": "klon.AddComponent<ParkingLotMusterkennung>()" in klon,
        "Muster_erbt_nur_RenderedArea": "&& !eintrag.Raeumt && !(bauteil is RenderedArea)) continue;" in klon,
        "RenderedArea_nur_Batch": bool(re.search(r"GetArchetypeComponents.*?components.Add\(ComponentType.ReadWrite<Batch>\(\)\)",render,re.S)),
        "Kosten_nur_Temp": "NativeArray<Temp> nativeArray2" in apply and "num += temp.m_Cost;" in apply,
        "Eigene_Permanent_Definition": "m_Prefab = klon, m_Flags = CreationFlags.Permanent" in system,
        "Definition_nur_ein_Bild": "ParkingLotToolSystem.NurDiesesBild(EntityManager,definition);" in system,
        "Kein_Apply": "ApplyMode.Apply" not in system,
        "Kein_Lotbezug": "ParkingLotPartRelation" not in system and "new Owner" not in system,
        "Markerquery_gegen_Verlust_des_Werkzeugfelds": "ComponentType.ReadOnly<ParkingLotMusterflaeche>()" in system and "_flaechen.CalculateEntityCount()" in system,
        "Laden_nach_Puffern": laden.index("PufferWiederhergestellt();") < laden.index("ParkingLotMusterSystem>().NachLaden();"),
    }
    for name, ok in belege.items():
        if not ok: raise AssertionError("Beleg fehlt: " + name)
    print(f"Permanent-Ausgabe: {len(belege)} Quellcodebelege bestaetigt; kein nativer Spiel-/Renderlauf.")
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(belege, indent=2), encoding="utf-8")
    return 0


if __name__ == "__main__":
    try: sys.exit(main())
    except Exception as error:
        print("FEHLER Permanent-Beleg: " + str(error), file=sys.stderr)
        sys.exit(1)
