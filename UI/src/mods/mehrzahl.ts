/** C# liefert die CLDR-Tabelle; hier steht keine zweite Sprachregel. */
export const mehrzahlEndung = (tabelle: string[] | undefined, n: number): string => {
  if (!tabelle || !Number.isSafeInteger(n)) return n === 1 ? ".one" : ".other";
  const zahl = Math.abs(n);
  return zahl === 1 ? ".one" : tabelle[zahl % 100];
};
