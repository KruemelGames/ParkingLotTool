/* Das echte Panel in einen bildschirmgrossen Rahmen - wie CS2s "Game"-Ebene. */
import { createRoot } from "react-dom/client";
import { ParkingLotPanel } from "mods/panel";

createRoot(document.getElementById("game")!).render(<ParkingLotPanel />);
