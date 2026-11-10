/** Bedienprobe des Stufenschalters "Sonderplaetze" am echten Panel (Nutzer 2026-11-10).
 * Klickweg Alle -> Aus -> Elektro -> Behindert -> Alle; links aus, rechts alle.
 * Die Attrappe uebernimmt jeden Set-Befehl, so laesst sich die Folge nachlesen. */
module.exports = async (page, t, pruefe, kennung) => {
  const fehler = [];
  const check = (ok, text) => { if (!ok) fehler.push(text); };
  const setzen = async (werte) => {
    await page.evaluate((w) => window.__PLT_SET__(w), werte);
    await page.waitForTimeout(60);
  };
  const kopf = page.getByRole('button', { name: t.tooltipFlaechen, exact: true });
  if (await kopf.count() && await kopf.getAttribute('aria-expanded') === 'false') await kopf.click();
  const schalter = page.getByRole('button', { name: t.tooltipSonderplaetze, exact: true });
  check(await schalter.count() === 1, "Sonderplatz-Schalter genau einmal");
  if (await schalter.count() !== 1) return fehler;
  // Wort, Griffstellung und Schriftgroessen aus dem echten DOM.
  const stand = () => page.evaluate((tipp) => {
    const knopf = document.querySelector(`button[aria-label="${tipp}"]`);
    const bereich = knopf.parentElement;
    const wort = bereich.querySelector('[class*="schalterWort_"]');
    const label = bereich.querySelector('[class*="label_"]');
    const griff = knopf.querySelector('[class*="schalterGriff_"]');
    return {
      wert: window.__PLT_WERTE__.Sonderplaetze ?? 0,
      wort: wort ? wort.innerText : null,
      gleicheSchrift: !!wort && !!label
        && getComputedStyle(wort).fontSize === getComputedStyle(label).fontSize,
      griff: griff ? griff.getBoundingClientRect().left - knopf.getBoundingClientRect().left : -1,
    };
  }, t.tooltipSonderplaetze);

  let vorher = await stand();
  check(vorher.wert === 0 && vorher.wort === t.sonderplaetzeAlle, "Standard ist Alle");
  check(vorher.gleicheSchrift, "Zustandswort so gross wie die Beschriftung");
  const folge = [[1, t.sonderplaetzeAus], [2, t.sonderplaetzeElektro],
    [3, t.sonderplaetzeBehindert], [0, t.sonderplaetzeAlle]];
  const lagen = [];
  for (const [wert, wort] of folge) {
    await schalter.click();
    await page.waitForTimeout(60);
    const jetzt = await stand();
    check(jetzt.wert === wert, `Klick nach ${vorher.wort} sendet ${wert}, gesendet ${jetzt.wert}`);
    check(jetzt.wort === wort, `Wort ${wort}, gezeigt ${jetzt.wort}`);
    check(jetzt.gleicheSchrift, "Zustandswort so gross wie die Beschriftung bei " + wort);
    lagen.push(jetzt.griff);
    await pruefe(kennung + ' / Sonderplätze ' + wort);
    vorher = jetzt;
  }
  // Aus, Elektro, Behindert, Alle: der Griff wandert von links nach rechts.
  check(lagen[0] < lagen[1] && lagen[1] < lagen[2] && lagen[2] < lagen[3],
    "Griff wandert von links (Aus) nach rechts (Alle): " + lagen.map((x) => x.toFixed(1)).join(" "));
  await setzen({ Fahrzeugart: 1 });
  check(await schalter.count() === 0, "Fahrradparkplatz zeigt keinen Sonderplatz-Schalter");
  await setzen({ Fahrzeugart: 0, Sonderplaetze: 0 });
  check(await schalter.count() === 1, "Autoparkplatz zeigt den Schalter wieder");
  return fehler;
};
