/** Gemeinsame, ohne Spieloberflaeche pruefbare Bedienregeln. */
export const gebuehrAusText = (text: string): number | null => {
  if (!/^\d+$/.test(text.trim())) return null;
  const zahl = Number(text);
  return Number.isFinite(zahl) ? Math.min(50, Math.max(0, zahl)) : null;
};
export const gebuehrAmBalken = (x: number, links: number, breite: number) =>
  Math.round(Math.min(1, Math.max(0, (x - links) / Math.max(1, breite))) * 50);
export const anteilBelegt = (belegt: number, kapazitaet: number) =>
  kapazitaet > 0 ? Math.max(0, Math.min(1, belegt / kapazitaet)) : 0;
export type Listenwert = { id: string; name: string; kapazitaet: number;
  belegt: number; mangel: { art: number }; waise?: number };
export type Listenfilter = "alle" | "probleme" | "voll" | "frei";
export type Listensortierung = "name" | "belegung" | "frei" | "groesse";
export function waehlePlaetze<T extends Listenwert>(plaetze: T[], suche: string,
  filter: Listenfilter, sortierung: Listensortierung): T[] {
  const text = suche.trim().toLocaleLowerCase();
  const frei = (p: T) => Math.max(0, p.kapazitaet - p.belegt);
  return plaetze.filter(p => p.name.toLocaleLowerCase().includes(text)
    && (filter === "alle" || (filter === "probleme" && (p.mangel.art !== 0 || (p.waise ?? 0) > 0))
      || (filter === "voll" && p.kapazitaet > 0 && anteilBelegt(p.belegt, p.kapazitaet) >= .9)
      || (filter === "frei" && frei(p) > 0)))
    .sort((a, b) => (sortierung === "belegung"
      ? anteilBelegt(b.belegt, b.kapazitaet) - anteilBelegt(a.belegt, a.kapazitaet)
      : sortierung === "frei" ? frei(b) - frei(a)
      : sortierung === "groesse" ? b.kapazitaet - a.kapazitaet : 0)
      || a.name.localeCompare(b.name) || a.id.localeCompare(b.id));
}
