import { useEffect, useRef, useState } from "react";
import { MitTooltip } from "./controls";
import { useTexte } from "./texte";
import { parkplatzGebuehr } from "./bindings";
import { gebuehrAusText, gebuehrAmBalken } from "./liste-logik";
import styles from "./panel.module.scss";

/** Kurzer Cohtml-Regler. Erst beim Loslassen buchen, lokale Anzeige sofort. */
export const Listengebuehr = ({ id, wert }: { id: string; wert: number }) => {
  const t = useTexte();
  const [lokal, setLokal] = useState<number | null>(null);
  const [text, setText] = useState<string | null>(null);
  const [fehler, setFehler] = useState(false);
  const schiene = useRef<HTMLDivElement>(null);
  const zug = useRef<(() => void) | null>(null);
  const aktuell = useRef(wert);
  const entwurf = useRef<string | null>(null);
  const quittung = useRef<ReturnType<typeof setTimeout> | null>(null);
  aktuell.current = wert;
  useEffect(() => {
    if (lokal === wert) { setLokal(null); setFehler(false); }
  }, [wert]);
  useEffect(() => () => {
    zug.current?.();
    if (quittung.current !== null) clearTimeout(quittung.current);
  }, []);
  const setzen = (neu: number) => {
    setLokal(neu); setFehler(false);
    if (quittung.current !== null) clearTimeout(quittung.current);
    if (neu === aktuell.current) { setLokal(null); return; }
    parkplatzGebuehr(id, neu);
    quittung.current = setTimeout(() => {
      setLokal(null);
      setFehler(aktuell.current !== neu);
    }, 3000);
  };
  const speichern = () => {
    if (entwurf.current === null) return;
    const neu = gebuehrAusText(entwurf.current);
    entwurf.current = null; setText(null);
    if (neu === null) { setFehler(true); return; }
    setzen(neu);
  };
  const anzeige = lokal ?? wert;
  return (
    <div className={styles.listeGebuehr}>
      <MitTooltip text={t.listeGebuehrHilfe}>
        <div ref={schiene} className={styles.listeGebuehrSchiene}
          role="slider" tabIndex={0} aria-label={t.spalteGebuehr}
          aria-valuemin={0} aria-valuemax={50} aria-valuenow={anzeige}
          aria-valuetext={anzeige === 0 ? t.gebuehrAus : `${anzeige} ${t.waehrung}`}
          onKeyDown={e => {
            const neu = e.key === "Home" ? 0 : e.key === "End" ? 50
              : e.key === "ArrowLeft" || e.key === "ArrowDown" ? Math.max(0, anzeige - 1)
              : e.key === "ArrowRight" || e.key === "ArrowUp" ? Math.min(50, anzeige + 1) : null;
            if (neu !== null) { e.preventDefault(); e.stopPropagation(); setzen(neu); }
          }}
          onMouseDown={e => {
            if (e.button !== 0) return;
            e.preventDefault(); e.stopPropagation(); zug.current?.();
            // Der Regler ersetzt einen offenen Texteingabe-Entwurf.
            // preventDefault allein liesse dessen alten Text vor der Zuganzeige stehen.
            entwurf.current = null; setText(null); schiene.current?.focus();
            const rect = schiene.current?.getBoundingClientRect();
            if (!rect) return;
            let neu = gebuehrAmBalken(e.clientX, rect.left, rect.width);
            setLokal(neu);
            const move = (event: MouseEvent) => {
              neu = gebuehrAmBalken(event.clientX, rect.left, rect.width);
              setLokal(neu);
            };
            const cleanup = () => {
              window.removeEventListener("mousemove", move);
              window.removeEventListener("mouseup", up);
              window.removeEventListener("blur", cancel);
              zug.current = null;
            };
            const up = () => { cleanup(); setzen(neu); };
            const cancel = () => { cleanup(); setLokal(null); };
            zug.current = cleanup;
            window.addEventListener("mousemove", move);
            window.addEventListener("mouseup", up);
            window.addEventListener("blur", cancel);
          }}>
          <div className={styles.listeGebuehrBett}>
            <div className={styles.listeGebuehrFuellung} style={{ width: `${anzeige * 2}%` }} />
          </div>
          <div className={styles.listeGebuehrGriff} style={{ left: `${anzeige * 2}%` }} />
        </div>
      </MitTooltip>
      <MitTooltip text={fehler ? t.listeGebuehrFehler : t.listeGebuehrHilfe}>
        <input className={`${styles.listeGebuehrEingabe} ${fehler ? styles.listeEingabeFehler : ""}`}
          aria-label={t.listeGebuehrHilfe} aria-invalid={fehler}
          value={text ?? String(anzeige)}
          onFocus={e => { entwurf.current = String(anzeige); setText(entwurf.current); e.target.select(); }}
          onChange={e => { entwurf.current = e.target.value; setText(e.target.value); setFehler(false); }}
          onBlur={speichern}
          onKeyDown={e => {
            e.stopPropagation();
            if (e.key === "Enter") { e.preventDefault(); speichern(); e.currentTarget.blur(); }
            if (e.key === "Escape") {
              e.preventDefault(); entwurf.current = null; setText(null); setFehler(false); e.currentTarget.blur();
            }
          }} />
      </MitTooltip>
      <span className={styles.listeGebuehrEinheit}>{anzeige === 0 ? t.gebuehrAus : t.waehrung}</span>
    </div>
  );
};
