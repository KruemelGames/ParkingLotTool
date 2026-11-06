import { useEffect } from "react";
import { useValue } from "cs2/api";
import { panelFarben$ } from "./bindings";

/**
 * Schaltet die Farben aller PLT-Fenster an einer Stelle um (FRQ-34). Die Klasse
 * sitzt am `body`, genau wie CS2s `style--...`; die Werte stehen in
 * panel.module.scss. Das Spiel raeumt dort nur Klassen mit `style--` weg.
 */
const KLASSE = "plt-farben-spiel";

export const PanelFarben = () => {
  const farben = useValue(panelFarben$);
  useEffect(() => {
    if (farben === "spiel") document.body.classList.add(KLASSE);
    else document.body.classList.remove(KLASSE);
  }, [farben]);
  return null;
};
