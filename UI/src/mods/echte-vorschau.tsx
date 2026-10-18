import { useValue } from "cs2/api";
import { echteVorschau$, setEchteVorschau } from "./bindings";
import { Toggle } from "./controls";
import { useTexte } from "./texte";

/** Ein gespeicherter Wert: Einschalten beendet jede andere echte Vorschau. */
export const EchteVorschauSchalter = ({ art }: { art: 2 | 3 }) => {
  const wert = useValue(echteVorschau$);
  const t = useTexte();
  return <Toggle label={art === 2 ? t.lanternPreview : t.vegetationPreview}
    tooltip={art === 2 ? t.tooltipLanternPreview : t.tooltipVegetationPreview}
    value={wert === art} onChange={an => setEchteVorschau(an ? art : 0)} />;
};
