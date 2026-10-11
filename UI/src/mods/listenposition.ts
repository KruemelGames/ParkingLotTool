import { useCallback, useLayoutEffect, useRef } from "react";

// Nutzerwunsch 2026-10-09: Listen in der Sitzung am letzten Ort oeffnen;
// Suchtreffer haben andere Hoehen und duerfen diesen Ort nicht ersetzen.
const positionen = new Map<string, number>();

export const useListenposition = (schluessel: string, offen: boolean,
                                  gefiltert: boolean, anzahl: number) => {
  const element = useRef<HTMLDivElement | null>(null);
  const zustand = useRef({ offen, gefiltert });
  zustand.current = { offen, gefiltert };
  const stelltWiederHer = useRef(false);
  const merken = useCallback(() => {
    if (element.current && !zustand.current.gefiltert && !stelltWiederHer.current)
      positionen.set(schluessel, element.current.scrollTop);
  }, [schluessel]);
  const ref = useCallback((node: HTMLDivElement | null) => {
    // Beim Schliessen auch vor dem Entfernen des Containers messen.
    if (!node) merken();
    element.current = node;
  }, [merken]);

  useLayoutEffect(() => {
    if (!offen || gefiltert || !element.current || anzahl === 0) return;
    const node = element.current;
    const position = positionen.get(schluessel) ?? 0;
    stelltWiederHer.current = true;
    node.scrollTop = position;
    // Einmal nach dem Cohtml-Layout nachsetzen, ohne weitere Frameschleife.
    const frame = requestAnimationFrame(() => {
      node.scrollTop = position;
      stelltWiederHer.current = false;
    });
    return () => {
      cancelAnimationFrame(frame);
      stelltWiederHer.current = false;
    };
  }, [schluessel, offen, gefiltert, anzahl]);

  const onScroll = useCallback(() => {
    if (zustand.current.offen) merken();
  }, [merken]);
  return { ref, onScroll };
};
