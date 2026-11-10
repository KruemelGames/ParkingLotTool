/** Mutiert nur den Pruefstands-Build; die Produktionsdateien bleiben unveraendert. */
module.exports = function (quelle) {
  const name = process.env.PLT_UI_MUTATION;
  const faelle = {
    edit: ['fahrradwahl.tsx', 'if (!edit && !busy)', 'if (!busy)'],
    layout: ['fahrradwahl.tsx', 'setFahrradlayout(Number(v))', 'setFahrradlayout(0)'],
    schrift: ['fahrradwahl.tsx', 'className={styles.wechsel}',
      'style={{ fontSize: "1rem" }} className={styles.wechsel}'],
    randstrassen: ['panel.tsx', 'titleTooltip={t.tooltipGruen}>\n          {!fahrrad ? <>',
      'titleTooltip={t.tooltipGruen}>\n          {true ? <>'],
  };
  if (!name) return quelle;
  const fall = faelle[name];
  if (!fall) throw new Error('Unbekannte UI-Mutation: ' + name);
  if (!this.resourcePath.endsWith(fall[0])) return quelle;
  // Produktionsdateien koennen CRLF enthalten.
  quelle = quelle.replace(/\r\n/g, '\n');
  if (quelle.split(fall[1]).length !== 2) throw new Error('Mutationsstelle nicht eindeutig: ' + name);
  return quelle.replace(fall[1], fall[2]);
};
