/*
 * Attrappe fuer cs2/api: jede Bindung liefert ihren Standardwert, ausser der
 * Pruefstand setzt sie in window.__PLT_WERTE__ (Name der Bindung -> Wert).
 *
 * Ein Ausloeser "SetX" schreibt die Bindung "X" - so wie der Mod es tut
 * (SetTab -> Tab). Damit lassen sich Reiter im Pruefstand wirklich wechseln.
 */
import { useSyncExternalStore } from "react";

const werte = (): Record<string, unknown> => ((window as any).__PLT_WERTE__ ||= {});
const hoerer = new Set<() => void>();
let stand = 0;

export const bindValue = <T,>(group: string, name: string, fallback?: T) => ({ group, name, fallback });

export const useValue = (b: any) => {
  useSyncExternalStore((h) => { hoerer.add(h); return () => hoerer.delete(h); }, () => stand);
  if (!b) return undefined;
  const w = werte();
  return b.name in w ? w[b.name] : b.fallback;
};

export const trigger = (_group: string, name: string, ...args: unknown[]) => {
  if (!name || !name.startsWith("Set") || args.length !== 1) return;
  werte()[name.slice(3)] = args[0];
  stand++;
  hoerer.forEach((h) => h());
};
