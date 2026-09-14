import * as CS2UI from "cs2/ui";

/*
 * DIE TYPDATEI IST KEIN BELEG DAFUER, DASS ES EINEN EXPORT GIBT.
 *
 * Am 2026-08-26 riss ein Import die GANZE Spieloberflaeche mit:
 *
 *     JS Error: Minified React error #130 (args[]=undefined)
 *
 * Nummer 130 heisst: erwartet wurde ein Element, geliefert wurde `undefined`.
 * Importiert waren `InfoRow` und `InfoSectionFoldout` aus `cs2/ui`, weil
 * `UI/types/ui.d.ts` sie in Zeile 531 und 546 auffuehrt. Das Spiel exportiert
 * sie aber nur UMBENANNT - ganz am Ende derselben Typdatei:
 *
 *     export {
 *         InfoRow            as PanelSectionRow,
 *         InfoSection        as PanelSection,
 *         InfoSectionFoldout as PanelFoldout,
 *     };
 *
 * Im Spielbundle steht dieselbe Gleichung als Variablenidentitaet:
 * `PanelSectionRow:()=>lj` neben `get InfoRow(){return lj}`. Es sind also
 * wirklich die Vanilla-Bausteine, nur unter anderem Namen.
 *
 * TypeScript sieht den Fehler nicht: in einem `declare module`-Block gilt
 * jedes `export const` als Export. Ein gruener Build sagt hier nichts.
 *
 * Deshalb steht der Zugriff NUR hier, ueber den Namensraum und mit Pruefung.
 * Fehlt ein Baustein doch einmal, kostet das den eigenen Abschnitt und nicht
 * die Oberflaeche des Spiels.
 */
export const brauchbar = (x: unknown) =>
  typeof x === "function"
  // React.memo und forwardRef liefern OBJEKTE, keine Funktionen. `Tooltip`
  // ist so eines - eine reine `typeof === "function"`-Pruefung haette ihn
  // stumm verworfen, und der Tooltip waere wieder ausgeblieben, diesmal
  // ohne erkennbaren Grund.
  || (typeof x === "object" && x !== null && "$$typeof" in (x as object));

/** Aufklappbarer Abschnittsrahmen (Vanilla: `InfoSectionFoldout`). */
export const Foldout: any = (CS2UI as any).PanelFoldout;

/** Zeile mit Symbol links, Wert rechts (Vanilla: `InfoRow`). */
export const Zeile: any = (CS2UI as any).PanelSectionRow;

/**
 * DER TOOLTIP DES SPIELS - `title` tut in Cohtml NICHTS.
 *
 * Am 2026-08-27 hatten die Flaechenschalter ein `title`-Attribut und zeigten
 * beim Ueberfahren nichts. Der Grund ist grundsaetzlich: `title` ist ein
 * Merkmal des BROWSERS, und Cohtml hat keine Browseroberflaeche, die es
 * zeichnen koennte. Es passiert einfach gar nichts - kein Fehler, keine
 * Meldung.
 *
 * CS2 bringt eine eigene Komponente mit, und Move It benutzt genau die:
 *
 *     <Tooltip tooltip={text}><Button ... /></Tooltip>
 *
 * `tooltip` nimmt einen ReactNode, `children` MUSS ein einzelnes Element
 * sein, das eine Ref annehmen kann - ein nacktes Textstueck genuegt nicht.
 */
export const Tooltip: any = (CS2UI as any).Tooltip;
