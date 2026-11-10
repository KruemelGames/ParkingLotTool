import { useValue } from "cs2/api";
import { bauartBeschaeftigt$, bauartEdit$, fahrzeugart$, fahrradlayout$, fahrradstil$, setFahrzeugart, setFahrradlayout, setFahrradstil } from "./bindings";
import { ModeChooser, TooltipKnopf } from "./controls";
import { useTexte } from "./texte";
import styles from "./fahrradwahl.module.scss";

/** Die Auswahl gilt fuer neue Anlagen; Edit behaelt seinen gespeicherten Typ. */
export const Fahrradwahl = () => {
  const t = useTexte();
  const bike = useValue(fahrzeugart$) === 1;
  const edit = useValue(bauartEdit$);
  const busy = useValue(bauartBeschaeftigt$);
  const layout = useValue(fahrradlayout$);
  const stil = useValue(fahrradstil$);
  return <div data-fahrradwahl="true">
    <TooltipKnopf text={edit ? t.fahrradArtFest : busy ? t.fahrradArbeit : t.fahrradWechselHinweis}
      className={styles.wechsel} aria-disabled={edit || busy} data-bauartwechsel="true"
      onClick={() => { if (!edit && !busy) setFahrzeugart(bike ? 0 : 1); }}>
      {bike ? t.fahrradZuAuto : t.fahrradZuRad}
    </TooltipKnopf>
    {bike ? <ModeChooser label={t.fahrradLayout} tooltip={t.fahrradLayoutHinweis}
      value={String(layout)} ton="Zuschnitt" raster disabled={busy}
      options={[
        { id: "0", text: t.fahrradLayoutA, tooltip: t.fahrradLayoutATipp },
        { id: "2", text: t.fahrradLayoutC, tooltip: t.fahrradLayoutCTipp },
        { id: "3", text: t.fahrradLayoutD, tooltip: t.fahrradLayoutDTipp },
      ]} onChange={(v) => setFahrradlayout(Number(v))} /> : null}
    {bike ? <ModeChooser label={t.fahrradStil} tooltip={t.fahrradStilHinweis}
      value={String(stil)} ton="Zuschnitt" raster disabled={busy}
      options={[
        { id: "0", text: t.fahrradStilSchlicht, tooltip: t.fahrradStilSchlichtTipp },
        { id: "1", text: t.fahrradStilRahmen, tooltip: t.fahrradStilRahmenTipp },
        { id: "2", text: t.fahrradStilPlatz, tooltip: t.fahrradStilPlatzTipp },
      ]} onChange={(v) => setFahrradstil(Number(v))} /> : null}
  </div>;
};
