import { ModRegistrar } from "cs2/modding";
import { useValue } from "cs2/api";
import { forwardRef } from "react";
import { ParkingLotLauncher } from "mods/launcher";
import { ParkingLotPanel } from "mods/panel";
import { SyncFortschritt } from "mods/sync-fortschritt";
import { parkingFeeVisible$ } from "mods/bindings";
import { ParkingFeeSection } from "mods/fee-section";
import { ParkingEmployeeSection } from "mods/employee-section";
import { ParkingEditSection } from "mods/edit-section";

const SELECTED_INFO_PANEL =
  "game-ui/game/components/selected-info-panel/selected-info-panel.tsx";
const SELECTED_INFO_SECTIONS =
  "game-ui/game/components/selected-info-panel/selected-info-sections/"
  + "selected-info-sections.tsx";
/*
 * DER SCHLUESSEL IST DER VOLLE .NET-KLASSENNAME, nicht `group`.
 *
 * `InfoSectionBase.Write` (Zeile 123-129) schreibt
 *
 *     writer.TypeBegin(GetType().FullName);   // <- das wird __Type
 *     writer.PropertyName("group"); ...       // group ist nur Beiwerk
 *
 * Vorher stand hier "ParkingLotTool.ParkingFeeSection", die Klasse heisst
 * aber `ParkingLotTool.Tools.ParkingLotFeeSection`. Zwei verschiedene Namen -
 * das Panel fand keine Komponente und zeigte kommentarlos nichts. Kein
 * Fehler im Log, keine leere Zeile, einfach nichts.
 *
 * Aendert sich Namensraum oder Klassenname in
 * `Tools/ParkingLotFeeSection.cs`, muss diese Zeile mitwandern.
 */
const PARKING_FEE_SECTION = "ParkingLotTool.Tools.ParkingLotFeeSection";
const EMPLOYEE_SECTION = "ParkingLotTool.Tools.ParkingLotEmployeeSection";
const EDIT_SECTION = "ParkingLotTool.Tools.ParkingLotEditSection";

/**
 * Zwei Einhaengepunkte, mehr braucht es nicht:
 *   GameTopLeft - der Knopf in der oberen Leiste
 *   Game        - das Panel selbst, frei ueber der Spielflaeche
 */
const register: ModRegistrar = (moduleRegistry) => {
  moduleRegistry.append("GameTopLeft", ParkingLotLauncher);
  moduleRegistry.append("Game", ParkingLotPanel);
  moduleRegistry.append("Game", SyncFortschritt);

  /*
   * DER GEBUEHRENABSCHNITT DARF DEN REST NICHT MITREISSEN.
   *
   * Befund vom 2026-08-26, aus der UI.log des Nutzers:
   *
   *     JS Error: TypeError: Assignment to constant variable.
   *         at mt (coui://ui-mods/ParkingLotTool.mjs:9:37046)
   *
   * Die Registrierung warf beim Laden, und der Abschnitt erschien nie. Knopf
   * und Panel liefen weiter, weil sie DAVOR angemeldet werden - reine
   * Reihenfolge, kein Schutz. Ein Wurf an dieser Stelle haette bei einer
   * kleinen Umstellung genauso gut das ganze Modul-UI gekostet.
   *
   * Jeder der beiden Eingriffe steht deshalb in seinem eigenen try/catch und
   * sagt im Log, WELCHER gescheitert ist. Ohne diese Trennung sucht man beim
   * naechsten Mal wieder in beiden.
   */
  try {
    const sectionComponents = moduleRegistry.get(
      SELECTED_INFO_SECTIONS, "selectedInfoSectionComponents",
    );
    if (sectionComponents && typeof sectionComponents === "object"
        && Object.keys(sectionComponents).length > 0) {
      moduleRegistry.override(
        SELECTED_INFO_SECTIONS,
        "selectedInfoSectionComponents",
        {
          ...sectionComponents,
          [PARKING_FEE_SECTION]: ParkingFeeSection,
          [EMPLOYEE_SECTION]: ParkingEmployeeSection,
          [EDIT_SECTION]: ParkingEditSection,
        },
      );
    } else {
      console.warn("ParkingLotTool: selectedInfoSectionComponents nicht "
        + "gefunden - der Gebuehrenabschnitt bleibt aus.");
    }
  } catch (fehler) {
    console.error("ParkingLotTool: der Sektionstyp liess sich nicht "
      + "anmelden (override auf selectedInfoSectionComponents). Der "
      + "Gebuehrenabschnitt bleibt aus, alles andere laeuft weiter. "
      + "Ursache: " + fehler);
  }

  /*
   * KEIN `extend` AUF `SelectedInfoPanel` MEHR.
   *
   * Genau das warf beim Laden - das Spiel fuehrt den Export als `const`.
   * Unser Abschnitt meldet sich jetzt auf dem vorgesehenen Weg selbst an,
   * in C#: `SelectedInfoUISystem.AddMiddleSection` (Zeile 250), siehe
   * `Tools/ParkingLotFeeSection.cs`. Hier bleibt nur die Zuordnung
   * Sektionstyp -> React-Komponente, und die hat von Anfang an funktioniert.
   */
};

export default register;
