import { useValue } from "cs2/api";
import { FloatingButton } from "cs2/ui";
import { panelOpen$, toggleTool, toolActive$ } from "./bindings";
import toolbarIcon from "../assets/plt-toolbar-a.svg";
import { useTexte } from "./texte";

/**
 * Der Knopf oben links - der sichtbare Weg in den Mod.
 *
 * SPIELEIGENES BAUTEIL, kein rohes <button>. Der erste Anlauf war eins mit
 * eigener Klasse und blieb im Spiel unsichtbar; die Leiste bringt ihre
 * eigene Gestaltung mit, gegen die ein nackter Knopf nicht ankommt.
 * `FloatingButton` ist das, was andere Mods dort verwenden (17 von ihnen),
 * und sieht damit aus wie alles andere in der Leiste.
 *
 * Er ist IMMER da. Ihn nur bei laufendem Werkzeug zu zeigen war zirkulaer:
 * zum Einschalten taugte er dann nicht, und wer die Tastenkombination
 * nicht kennt, findet den Mod gar nicht.
 *
 * Ein Klick schaltet Werkzeug und Panel gemeinsam. Nur das Fenster zu
 * verstecken liess bisher das Draft-Werkzeug unsichtbar weiterzeichnen.
 */
export const ParkingLotLauncher = () => {
  const t = useTexte();
  const toolActive = useValue(toolActive$);
  const open = useValue(panelOpen$);

  return (
    <FloatingButton
      src={toolbarIcon}
      selected={toolActive && open}
      onSelect={toggleTool}
      tooltipLabel={toolActive
        ? (open ? t.einstellungenOffen : t.einstellungenZu)
        : t.werkzeugTitel}
    />
  );
};
