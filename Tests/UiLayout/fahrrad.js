/** Bedienprobe am echten Panel. Die Attrappe zeichnet gesendete Befehle auf;
 * die C#-Gegenprobe prueft die unabhaengigen Serversperren. */
module.exports = async (page, t, pruefe, kennung) => {
  const fehler = [];
  const check = (ok, text) => { if (!ok) fehler.push(text); };
  const setzen = async (werte) => {
    await page.evaluate((w) => window.__PLT_SET__(w), werte);
    await page.waitForTimeout(60);
  };
  const abschnitt = async (tooltip) => {
    const kopf = page.getByRole('button', { name: tooltip, exact: true });
    if (await kopf.count() && await kopf.getAttribute('aria-expanded') === 'false')
      await kopf.click();
  };
  const wechsel = page.locator('[data-bauartwechsel="true"]');
  check(await wechsel.innerText() === t.fahrradZuRad, "Auto bietet Wechsel zu Fahrrad");
  check(await page.evaluate(() => {
    const w = document.querySelector('[data-bauartwechsel="true"]');
    const label = w.parentElement.parentElement.querySelector('[class*="label_"]');
    return label && getComputedStyle(w).fontSize === getComputedStyle(label).fontSize;
  }), "Umschalter beachtet dieselbe Spiel-Textgroesse wie die Regler");
  check(await page.evaluate(() => {
    const w = document.querySelector('[data-fahrradwahl="true"]');
    const spalte = w.parentElement;
    return spalte.children[1] === w && /control_/.test(spalte.children[2].className);
  }), "Bauartwahl steht als erstes Bedienelement vor Randabstand");
  const auto = await page.evaluate(() => ({ ...window.__PLT_WERTE__ }));
  await wechsel.click({ force: true });
  await page.waitForTimeout(80);
  check(await wechsel.innerText() === t.fahrradZuAuto, "Umschalter zeigt Fahrradmodus");
  // Im Hochkantstil wird der Inhalt geschlossener Abschnitte ausgebaut.
  // Die Abwesenheit eines Reglers wird deshalb am OFFENEN Abschnitt gemessen.
  await abschnitt(t.tooltipGruen);
  check(await page.getByText(t.randstrassen, { exact: true }).count() === 0, "keine Randstrassen");
  await abschnitt(t.tooltipFlaechen);
  check(await page.getByText(t.buchtsymbole, { exact: true }).count() === 0, "keine Spezialbuchtsymbole");
  await abschnitt(t.tooltipZuschnitt);
  check(await page.getByText(t.bushaltestelle, { exact: true }).count() === 0, "keine Bushaltestelle");
  check(await page.locator('[class*="artKnopf_"]').count() === 1, "genau ein beidseitiger Fahrradzugang");
  const layout = page.locator('[data-fahrradwahl="true"]');
  // Umschalter, Abstellart A/C/D (kein B) und Gestaltung Schlicht/Rahmen/Platz (03.11.2026).
  check(await layout.getByRole('button').count() === 7, "genau Umschalter, A/C/D und drei Stile, kein B");
  for (const tipp of [t.fahrradLayoutATipp, t.fahrradLayoutCTipp, t.fahrradLayoutDTipp,
    t.fahrradStilSchlichtTipp, t.fahrradStilRahmenTipp, t.fahrradStilPlatzTipp])
    check(await layout.getByRole('button', { name: tipp, exact: true }).count() === 1, "Knopf einmal: " + tipp);
  await setzen({ PolygonClosed: true });
  const zoning = page.getByRole('button', { name: t.fahrradZoningAus, exact: true });
  // Hochkant bei 150 % Text liegt auch der gesperrte Reiter im Aufklapper.
  const reiterwahl = page.locator("[class*='reiterWahl_']").first();
  const aufgeklappt = await zoning.count() === 0 && await reiterwahl.count() > 0;
  if (aufgeklappt) { await reiterwahl.click(); await page.waitForTimeout(100); }
  check(await zoning.count() === 1 && await zoning.getAttribute('aria-disabled') === 'true', "Zoning mit Grund gesperrt");
  await zoning.click({ force: true });
  if (aufgeklappt) await reiterwahl.click();
  check(await page.evaluate(() => window.__PLT_WERTE__.Tab !== 'zoning'), "Zoning-Klick bleibt ohne Befehl");
  for (const [id, name, tipp] of [[0, t.fahrradLayoutA, t.fahrradLayoutATipp],
    [2, t.fahrradLayoutC, t.fahrradLayoutCTipp], [3, t.fahrradLayoutD, t.fahrradLayoutDTipp]]) {
    const knopf = layout.getByRole('button', { name: tipp, exact: true });
    check(await knopf.innerText() === name, "Layoutname " + id);
    await knopf.click();
    check(await page.evaluate(() => window.__PLT_WERTE__.Fahrradlayout) === id, "Layoutwahl sendet " + id);
    check((await knopf.getAttribute('class')).includes('modeAktiv'), "Layoutwahl markiert " + id);
    await pruefe(kennung + ' / Fahrrad ' + id);
  }
  for (const [id, name, tipp] of [[0, t.fahrradStilSchlicht, t.fahrradStilSchlichtTipp],
    [1, t.fahrradStilRahmen, t.fahrradStilRahmenTipp], [2, t.fahrradStilPlatz, t.fahrradStilPlatzTipp]]) {
    const knopf = layout.getByRole('button', { name: tipp, exact: true });
    check(await knopf.innerText() === name, "Stilname " + id);
    await knopf.click();
    check(await page.evaluate(() => window.__PLT_WERTE__.Fahrradstil) === id, "Stilwahl sendet " + id);
    check((await knopf.getAttribute('class')).includes('modeAktiv'), "Stilwahl markiert " + id);
    await pruefe(kennung + ' / Fahrradstil ' + id);
  }
  await setzen({ BauartEdit: true });
  check(await wechsel.getAttribute('aria-disabled') === 'true'
    && await wechsel.getAttribute('title') === t.fahrradArtFest, "Edit-Sperre mit Grund");
  const anzahl = () => page.evaluate(() => window.__PLT_BEFEHLE__.length);
  let vorher = await anzahl();
  await wechsel.click({ force: true });
  check(await anzahl() === vorher, "Edit sendet keinen Artwechsel");
  // Der echte Server weist einen unerlaubten Wechsel zurueck (C#-Gegenprobe).
  // Die einfache Bindungsattrappe nimmt jeden Set-Befehl an; fuer nachfolgende
  // unabhaengige Pruefungen stellen wir den bestaetigten Edit-Typ wieder her.
  await setzen({ Fahrzeugart: 1 });
  await layout.getByRole('button', { name: t.fahrradLayoutCTipp, exact: true }).click();
  check(await page.evaluate(() => window.__PLT_WERTE__.Fahrradlayout) === 2, "Edit erlaubt anderes Fahrradlayout");
  await layout.getByRole('button', { name: t.fahrradStilRahmenTipp, exact: true }).click();
  check(await page.evaluate(() => window.__PLT_WERTE__.Fahrradstil) === 1, "Edit erlaubt anderen Stil");
  await pruefe(kennung + ' / Fahrrad Edit');
  await setzen({ BauartEdit: false, BauartBeschaeftigt: true });
  vorher = await anzahl();
  await wechsel.click({ force: true });
  await layout.getByRole('button', { name: t.fahrradLayoutATipp, exact: true }).click({ force: true });
  await layout.getByRole('button', { name: t.fahrradStilPlatzTipp, exact: true }).click({ force: true });
  check(await anzahl() === vorher, "Laufender Bau sperrt Art, Layout und Stil");
  check(await wechsel.getAttribute('title') === t.fahrradArbeit, "Bausperre mit Grund");
  await setzen({ BauartBeschaeftigt: false });
  await wechsel.click({ force: true });
  check(await wechsel.innerText() === t.fahrradZuRad, "Rueckwechsel zu Auto");
  await abschnitt(t.tooltipGruen);
  check(await page.getByText(t.randstrassen, { exact: true }).count() === 1, "Auto zeigt Randstrassen wieder");
  await abschnitt(t.tooltipZuschnitt);
  check(await page.evaluate((alt) => Object.keys(alt).every((k) =>
    window.__PLT_WERTE__[k] === alt[k]), auto), "Autobindungen bleiben erhalten");
  await setzen({ PolygonClosed: false });
  return fehler;
};
