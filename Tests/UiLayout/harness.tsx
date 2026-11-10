/* Das echte Panel in einen bildschirmgrossen Rahmen - wie CS2s "Game"-Ebene. */
import { createRoot } from "react-dom/client";
import { ParkingLotPanel } from "mods/panel";
import { PanelFarben } from "mods/panel-farben";

createRoot(document.getElementById("game")!).render(<><PanelFarben /><ParkingLotPanel /></>);
