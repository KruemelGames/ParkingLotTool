// Linienmodell - Geometrie und Aufbau, ohne DOM.
// Wird von parking-linien.html UND von den Tests benutzt, damit Seite und
// Messung garantiert denselben Code ausfuehren.

const sub = (a, b) => [a[0] - b[0], a[1] - b[1]];
const add = (a, b) => [a[0] + b[0], a[1] + b[1]];
const mul = (a, k) => [a[0] * k, a[1] * k];
const len = (a) => Math.sqrt(a[0] * a[0] + a[1] * a[1]);
const norm = (a) => { const l = len(a) || 1; return [a[0] / l, a[1] / l]; };

function signedArea(p) {
  let s = 0;
  for (let i = 0; i < p.length; i++) {
    const a = p[i], b = p[(i + 1) % p.length];
    s += a[0] * b[1] - b[0] * a[1];
  }
  return s / 2;
}

function pointIn(p, ring) {
  let inside = false;
  for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
    const a = ring[i], b = ring[j];
    if ((a[1] > p[1]) !== (b[1] > p[1]) &&
        p[0] < (b[0] - a[0]) * (p[1] - a[1]) / (b[1] - a[1]) + a[0]) inside = !inside;
  }
  return inside;
}

const rect = (c, d, n, hd, hn) => [
  add(add(c, mul(d, -hd)), mul(n, -hn)), add(add(c, mul(d, hd)), mul(n, -hn)),
  add(add(c, mul(d, hd)), mul(n, hn)), add(add(c, mul(d, -hd)), mul(n, hn)),
];

/**
 * Abstand eines Punktes zur naechsten Polygonkante.
 *
 * Rechnet in QUADRATEN und zieht die Wurzel nur einmal am Ende statt einmal
 * je Kante. Ausserdem ohne `sub`/`add`/`mul`: die legen je Aufruf kleine
 * Arrays an, und diese Funktion ist mit `rectClear` zusammen der heisseste
 * Pfad im ganzen Modell. Das Ergebnis ist bitgleich.
 */
function distToBoundary(p, ring) {
  const px = p[0], py = p[1];
  let best = Infinity;
  for (let i = 0; i < ring.length; i++) {
    const a = ring[i], b = ring[(i + 1) % ring.length];
    const abx = b[0] - a[0], aby = b[1] - a[1];
    const l2 = abx * abx + aby * aby || 1;
    let t = ((px - a[0]) * abx + (py - a[1]) * aby) / l2;
    t = t < 0 ? 0 : t > 1 ? 1 : t;
    const dx = px - (a[0] + abx * t), dy = py - (a[1] + aby * t);
    const d2 = dx * dx + dy * dy;
    if (d2 < best) best = d2;
  }
  return Math.sqrt(best);
}

/**
 * Umschliessendes Rechteck eines Polygons, einmal je Polygon gerechnet und
 * gemerkt. Damit kann `insideBy` die allermeisten Punkte mit vier Vergleichen
 * abweisen, statt zweimal alle Kanten durchzugehen.
 */
const BBOX = new WeakMap();
function bboxOf(poly) {
  let b = BBOX.get(poly);
  if (b) return b;
  let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
  for (const q of poly) {
    if (q[0] < x0) x0 = q[0];
    if (q[0] > x1) x1 = q[0];
    if (q[1] < y0) y0 = q[1];
    if (q[1] > y1) y1 = q[1];
  }
  b = { x0, y0, x1, y1 };
  BBOX.set(poly, b);
  return b;
}

/**
 * Im Areal UND mindestens d von jeder Kante entfernt.
 *
 * Das Mikrometer Toleranz ist nicht kosmetisch: die Randbuchten werden so
 * gesetzt, dass ihre Aussenkante EXAKT auf der Setback-Linie liegt. Ohne
 * Toleranz entscheidet dort Gleitkomma-Rauschen in der 14. Stelle. Gemessen am
 * Parallelogramm: eine Diagonale verfehlte um 1,15e-14 m und verlor alle 24
 * Buchten, die Gegendiagonale lag zufaellig auf der anderen Seite und behielt
 * ihre 17.
 */
const FIT_EPS = 1e-6;
// Einrastziele gelten bis fuenf Grad als kollinear; Modell und Browser teilen
// exakt dieselbe Schranke.
const ENTRANCE_SNAP_COS = Math.cos(5 * Math.PI / 180);
// Wie spitz darf eine konvexe Ecke sein, damit die Einfahrt dort einrasten
// darf? Der Wert ist der Sinus des Eckwinkels; bei 30 Grad wird die schraege
// Einfahrt 1/sin = 2x so lang wie die senkrechte.
const MIN_CORNER_SNAP_SIN = Math.sin(30 * Math.PI / 180);
// Ab wann gelten zwei Fahrbahnen als parallel? cos(20 Grad).
const PARALLEL_COS = Math.cos(20 * Math.PI / 180);
// WIDERLEGT: hier stand einmal 1,0 m mit der Begruendung "Abstand null heisst
// EINE Strasse statt zwei". Falsch - eine wirklich fluchtende Verlaengerung
// liegt HINTER dem Segment, und dort liefert `sideGap` ohnehin Unendlich.
// Abstand null bei Lotpunkt AUF dem Segment heisst: sie liegen aufeinander.
// Gemessen lag die Fahrgasse der L-Form dadurch 9,3 m in der Randstrasse.
const MERGE_TOL = 0;
// Flachster erlaubter Einmuendungswinkel einer Fahrgasse in eine Strasse.
// Nur noch eine Plausibilitaetsschranke (15 Grad). Die Ueberlappung loest
// nicht dieser Winkel, sondern das Enden am Fahrbahnrand - siehe `clipSegment`
// beim Bau der Gassenlinie. Mit 30 Grad kostete die Regel den Browser-Fall
// 30 Buchten (219 -> 189) und liess trotzdem 6,3 m Ueberlappung stehen.
// Wie lang darf eine Fahrgasse durch ein zu enges Stueck hindurch, um eine
// Strasse zu ERREICHEN? Zwei Fahrbahnbreiten. Ohne Riegel blieb die ganze Gasse
// ungekuerzt und lief lang neben der Randstrasse her (L-Form 9 -> 281 m2).
const MAX_PASS = 2;
const MIN_JUNCTION_SIN = Math.sin(15 * Math.PI / 180);
function insideBy(p, site, d) {
  // Schnelle Abweisung ueber das umschliessende Rechteck: liegt der Punkt
  // schon dort naeher als `d` am Rand, kann er es im Polygon erst recht nicht
  // weiter weg sein.
  const b = bboxOf(site), e = d - FIT_EPS;
  if (p[0] < b.x0 + e || p[0] > b.x1 - e
      || p[1] < b.y0 + e || p[1] > b.y1 - e) return false;
  return pointIn(p, site) && distToBoundary(p, site) >= e;
}

function rectClear(c, d, n, hd, hn, site, clearance) {
  for (let i = -1; i <= 1; i++)
    for (let j = -1; j <= 1; j++)
      if (!insideBy(add(add(c, mul(d, hd * i)), mul(n, hn * j)), site, clearance))
        return false;
  return true;
}

/** Ueberlappen sich zwei konvexe Vierecke? Trennachsen-Test mit Toleranz. */
function quadsOverlap(A, B, tol = 0.02) {
  for (const poly of [A, B])
    for (let i = 0; i < poly.length; i++) {
      const e = sub(poly[(i + 1) % poly.length], poly[i]);
      const ax = norm([-e[1], e[0]]);
      let aLo = Infinity, aHi = -Infinity, bLo = Infinity, bHi = -Infinity;
      for (const p of A) {
        const d = p[0] * ax[0] + p[1] * ax[1];
        aLo = Math.min(aLo, d); aHi = Math.max(aHi, d);
      }
      for (const p of B) {
        const d = p[0] * ax[0] + p[1] * ax[1];
        bLo = Math.min(bLo, d); bHi = Math.max(bHi, d);
      }
      if (aHi <= bLo + tol || bHi <= aLo + tol) return false;
    }
  return true;
}

/**
 * Belegungsliste mit grobem Gitter. Ohne sie ueberlagern sich die Buchten -
 * besonders an den Ringecken, wo zwei Kantenlaeufe denselben Platz belegen.
 */
function makeIndex(cell) {
  const map = new Map();
  const keys = (q) => {
    const out = new Set();
    for (const p of q) out.add(`${Math.floor(p[0] / cell)}:${Math.floor(p[1] / cell)}`);
    return out;
  };
  return {
    add(q) {
      for (const k of keys(q)) {
        if (!map.has(k)) map.set(k, []);
        map.get(k).push(q);
      }
    },
    hits(q) {
      const seen = new Set();
      for (const p of q)
        for (let dx = -1; dx <= 1; dx++)
          for (let dy = -1; dy <= 1; dy++) {
            const k = `${Math.floor(p[0] / cell) + dx}:${Math.floor(p[1] / cell) + dy}`;
            for (const other of map.get(k) || []) {
              if (seen.has(other)) continue;
              seen.add(other);
              if (quadsOverlap(q, other)) return true;
            }
          }
      return false;
    },
  };
}

/**
 * Punkt-in-irgendeinem-Viereck, mit Gitterindex. Ohne den kostet das Abtasten
 * der Restflaeche `Zellen x Vierecke` Tests - bei 0,5 m Raster und ~500
 * Vierecken sind das Millionen.
 */
function makeCoverIndex(quads, cell) {
  const map = new Map();
  for (const q of quads) {
    const xs = q.map((p) => p[0]), ys = q.map((p) => p[1]);
    const i0 = Math.floor(Math.min(...xs) / cell), i1 = Math.floor(Math.max(...xs) / cell);
    const j0 = Math.floor(Math.min(...ys) / cell), j1 = Math.floor(Math.max(...ys) / cell);
    for (let i = i0; i <= i1; i++)
      for (let j = j0; j <= j1; j++) {
        const k = `${i}:${j}`;
        if (!map.has(k)) map.set(k, []);
        map.get(k).push(q);
      }
  }
  return (p) => {
    for (const q of map.get(`${Math.floor(p[0] / cell)}:${Math.floor(p[1] / cell)}`) || [])
      if (pointIn(p, q)) return true;
    return false;
  };
}

/**
 * EXAKTE Restflaeche durch Slab-Zerlegung.
 *
 * Das Raster konnte prinzipbedingt nie punktgenau sein: eine Zelle ist ganz
 * frei oder gar nicht, also blieb an jeder schraegen Kante ein Saum. Hier
 * stattdessen: senkrechte Streifen an allen Eckpunkten und Schnittpunkten.
 * Innerhalb eines Streifens kreuzt sich keine Kante, die Grenzen sind also
 * ueber die ganze Streifenbreite dieselben zwei Segmente - der freie Bereich
 * ist dort exakt ein Trapez.
 *
 * `blocked(p)` sagt, ob ein Punkt belegt ist. Rueckgabe: Trapeze, die die
 * freie Flaeche exakt und ueberlappungsfrei kacheln.
 */
function slabFill(site, quads, blocked, minArea) {
  const segs = [], xs = new Set();
  const addPoly = (poly) => {
    for (let i = 0; i < poly.length; i++) {
      const a = poly[i], b = poly[(i + 1) % poly.length];
      xs.add(a[0]);
      if (Math.abs(a[0] - b[0]) < 1e-9) continue;   // senkrecht: nur Ereignis
      segs.push(a[0] < b[0] ? { a, b } : { a: b, b: a });
    }
  };
  addPoly(site);
  for (const q of quads) addPoly(q);
  // Schnittpunkte: Buchten und Gruen ueberlappen sich nie, Fahrbahnen an den
  // Einmuendungen schon. Ohne diese Ereignisse tauschen zwei Kanten INNERHALB
  // eines Streifens die Reihenfolge und das Trapez waere falsch.
  for (let i = 0; i < segs.length; i++)
    for (let j = i + 1; j < segs.length; j++) {
      const A = segs[i], B = segs[j];
      if (A.b[0] <= B.a[0] || B.b[0] <= A.a[0]) continue;
      const r = sub(A.b, A.a), s2 = sub(B.b, B.a);
      const den = r[0] * s2[1] - r[1] * s2[0];
      if (Math.abs(den) < 1e-12) continue;
      const d0 = sub(B.a, A.a);
      const t = (d0[0] * s2[1] - d0[1] * s2[0]) / den;
      const u = (d0[0] * r[1] - d0[1] * r[0]) / den;
      if (t <= 0 || t >= 1 || u <= 0 || u >= 1) continue;
      xs.add(A.a[0] + r[0] * t);
    }
  const cut = [...xs].sort((a, b) => a - b);
  const yAt = (sg, x) => sg.a[1] + (sg.b[1] - sg.a[1]) * (x - sg.a[0]) / (sg.b[0] - sg.a[0]);
  const out = [];
  for (let i = 0; i + 1 < cut.length; i++) {
    const x0 = cut[i], x1 = cut[i + 1];
    if (x1 - x0 < 1e-7) continue;
    const xm = (x0 + x1) / 2;
    const akt = segs.filter((sg) => sg.a[0] <= xm && sg.b[0] >= xm);
    if (akt.length < 2) continue;
    akt.sort((p, q) => yAt(p, xm) - yAt(q, xm));
    for (let k = 0; k + 1 < akt.length; k++) {
      const lo = akt[k], hi = akt[k + 1];
      const yl = yAt(lo, xm), yh = yAt(hi, xm);
      if (yh - yl < 1e-7) continue;
      if (blocked([xm, (yl + yh) / 2])) continue;
      out.push({ x0, x1, lo, hi });
    }
  }
  // Trapeze verschmelzen, solange Streifen an Streifen dieselben zwei Kanten
  // begrenzen - sonst zerfaellt jede Flaeche in Dutzende Schnipsel.
  const fertig = [];
  for (const t of out) {
    const last = fertig[fertig.length - 1];
    if (last && last.hi === t.hi && last.lo === t.lo
        && Math.abs(last.x1 - t.x0) < 1e-7) { last.x1 = t.x1; continue; }
    fertig.push({ ...t });
  }
  const traps = fertig.map((t) => ({
    x0: t.x0, x1: t.x1,
    lo0: yAt(t.lo, t.x0), lo1: yAt(t.lo, t.x1),
    hi0: yAt(t.hi, t.x0), hi1: yAt(t.hi, t.x1),
  }));

  // Die Trapeze kacheln die freie Flaeche exakt. Also die Umrisse gewinnen,
  // indem sich GETEILTE Kanten gegeneinander aufheben - was uebrig bleibt, ist
  // der Rand. Ohne diesen Schritt blieben 790 Schnipsel statt einer Handvoll
  // Flaechen. Senkrechte Kanten muessen dafuer an allen Nachbargrenzen
  // aufgeteilt werden, sonst passen Kante und Gegenkante nicht exakt.
  const K = (v) => Math.round(v * 1e6) / 1e6;
  const trenner = new Map();   // x -> Set der y-Werte
  const merke = (x, y) => {
    const k = K(x);
    if (!trenner.has(k)) trenner.set(k, new Set());
    trenner.get(k).add(K(y));
  };
  for (const t of traps) {
    merke(t.x0, t.lo0); merke(t.x0, t.hi0);
    merke(t.x1, t.lo1); merke(t.x1, t.hi1);
  }
  const edges = new Map();
  const addEdge = (a, b) => {
    if (Math.abs(a[0] - b[0]) < 1e-9 && Math.abs(a[1] - b[1]) < 1e-9) return;
    const ka = `${K(a[0])},${K(a[1])}`, kb = `${K(b[0])},${K(b[1])}`;
    if (edges.has(`${kb}|${ka}`)) { edges.delete(`${kb}|${ka}`); return; }
    edges.set(`${ka}|${kb}`, [a, b]);
  };
  const senkrecht = (x, ya, yb) => {
    const alle = [...(trenner.get(K(x)) || [])]
      .filter((y) => y > Math.min(ya, yb) + 1e-9 && y < Math.max(ya, yb) - 1e-9)
      .sort((p, q) => (ya < yb ? p - q : q - p));
    let cur = ya;
    for (const y of [...alle, yb]) { addEdge([x, cur], [x, y]); cur = y; }
  };
  for (const t of traps) {
    addEdge([t.x0, t.lo0], [t.x1, t.lo1]);          // unten, nach rechts
    senkrecht(t.x1, t.lo1, t.hi1);                  // rechts, nach oben
    addEdge([t.x1, t.hi1], [t.x0, t.hi0]);          // oben, nach links
    senkrecht(t.x0, t.hi0, t.lo0);                  // links, nach unten
  }
  const nachStart = new Map();
  for (const [, e] of edges) {
    const k = `${K(e[0][0])},${K(e[0][1])}`;
    if (!nachStart.has(k)) nachStart.set(k, []);
    nachStart.get(k).push(e);
  }
  const ringe = [];
  for (const [, liste] of nachStart) {
    while (liste.length) {
      const first = liste.pop();
      const ring = [first[0]];
      let cur = first[1];
      for (let guard = 0; guard < 100000; guard++) {
        if (Math.abs(cur[0] - first[0][0]) < 1e-6
            && Math.abs(cur[1] - first[0][1]) < 1e-6) break;
        const l = nachStart.get(`${K(cur[0])},${K(cur[1])}`);
        if (!l || !l.length) break;
        const e = l.pop();
        ring.push(e[0]);
        cur = e[1];
      }
      if (ring.length >= 3) ringe.push(ring);
    }
  }
  // Umlaufsinn trennt Aussenring von LOCH. Alle Trapeze laufen gleich herum;
  // ein Ring, der ein Hindernis umschliesst, laeuft dadurch andersherum. Ohne
  // diese Trennung wird das Loch mitgefuellt - gemessen lagen bei der L-Form
  // 30 % der Restflaeche auf Buchten und Fahrbahn.
  const aussen = [], loecher = [];
  for (const r of ringe) {
    const q = mergeCollinear(r, 1e-6);
    if (q.length < 3) continue;
    const A = signedArea(q);
    if (Math.abs(A) < minArea) continue;
    (A > 0 ? aussen : loecher).push(q);
  }

  /**
   * GEGENPROBE, und bei Abweichung zurueck zu den Trapezen.
   *
   * Die Trapeze oben kacheln die freie Flaeche exakt - das ist der verlaessliche
   * Teil. Danach werden sie zu Umrissen verschmolzen, indem sich geteilte Kanten
   * aufheben. Dieser Schritt kann brechen: bei einem frei gezogenen Zehneck
   * lieferte er einen 12-Ecken-"Ring", der sich selbst ueberschlug (Flaeche
   * -126 m2, abgetastet aber 258 m2). Ergebnis waren 503 m2 unbelegte Flaeche,
   * 4,8 % des Areals - im Bild grosse leere Rechtecke mitten im Parkplatz.
   *
   * Statt die Rekonstruktion zu erraten wird sie GEMESSEN: die Summe der
   * Ringflaechen muss der Summe der Trapezflaechen entsprechen. Weicht sie ab,
   * sind die Umrisse kaputt, und die Trapeze gehen unveraendert hinaus. Das
   * gibt mehr Einzelstuecke, aber keine verlorene Flaeche - und im Mod ist eine
   * Flaeche zu viel harmlos, eine fehlende nicht.
   */
  const trapezFlaeche = traps.reduce((sum, t) => {
    const q = [[t.x0, t.lo0], [t.x1, t.lo1], [t.x1, t.hi1], [t.x0, t.hi0]];
    return sum + Math.abs(signedArea(q));
  }, 0);
  const ringFlaeche = aussen.reduce((sum, q) => sum + Math.abs(signedArea(q)), 0)
                    - loecher.reduce((sum, q) => sum + Math.abs(signedArea(q)), 0);
  if (Math.abs(ringFlaeche - trapezFlaeche) > Math.max(0.5, trapezFlaeche * 0.001)) {
    const roh = [];
    for (const t of traps) {
      const q = [[t.x0, t.lo0], [t.x1, t.lo1], [t.x1, t.hi1], [t.x0, t.hi0]];
      if (Math.abs(signedArea(q)) >= minArea)
        roh.push(signedArea(q) > 0 ? q : q.slice().reverse());
    }
    return { aussen: roh, loecher: [], fallback: true,
             fehler: ringFlaeche - trapezFlaeche };
  }
  return { aussen, loecher };
}


/**
 * Kollineare Punkte entfernen. VERLUSTFREI - die Flaeche aendert sich nicht,
 * deshalb braucht dieser Schritt keine Ueberdeckungspruefung. Genau er fehlte:
 * die Konturverfolgung liefert eine Kante je Zellseite, und ohne das Verschmelzen
 * blieben ALLE 830 Kanten des Browser-Falls exakt 0,5 m lang.
 */
function mergeCollinear(ring, eps) {
  const out = [];
  for (let i = 0; i < ring.length; i++) {
    const a = out.length ? out[out.length - 1] : ring[(i - 1 + ring.length) % ring.length];
    const b = ring[i], c = ring[(i + 1) % ring.length];
    const ab = sub(b, a), bc = sub(c, b);
    if (Math.abs(ab[0] * bc[1] - ab[1] * bc[0]) <= eps
        && ab[0] * bc[0] + ab[1] * bc[1] > 0) continue;
    out.push(b);
  }
  return out.length >= 3 ? out : ring;
}

/** Abstand eines Punktes zur Strecke a-b. */
function distToSeg(p, a, b) {
  const ab = sub(b, a), l2 = ab[0] * ab[0] + ab[1] * ab[1];
  if (l2 < 1e-12) return len(sub(p, a));
  let t = ((p[0] - a[0]) * ab[0] + (p[1] - a[1]) * ab[1]) / l2;
  t = Math.max(0, Math.min(1, t));
  return len(sub(p, add(a, mul(ab, t))));
}

/**
 * Seitlicher Abstand zu einem Segment - aber nur, wenn der Lotpunkt WIRKLICH
 * auf dem Segment liegt. Sonst laufen die beiden nicht nebeneinander, sondern
 * hintereinander, und der Abstand sagt nichts ueber Parallelitaet aus.
 * Genau daran scheiterte die Verlaengerung der Randstrasse an der konkaven
 * Ecke: sie ist die FORTSETZUNG ihres Ringsegments, und der Abstand zu dessen
 * Endpunkt liess sie wie eine danebenliegende Strasse aussehen.
 */
function sideGap(p, a, b) {
  const ab = sub(b, a), l2 = ab[0] * ab[0] + ab[1] * ab[1];
  if (l2 < 1e-12) return Infinity;
  const t = ((p[0] - a[0]) * ab[0] + (p[1] - a[1]) * ab[1]) / l2;
  if (t < 0 || t > 1) return Infinity;
  return Math.abs((p[0] - a[0]) * ab[1] - (p[1] - a[1]) * ab[0]) / Math.sqrt(l2);
}



function lineIntersect(p1, d1, p2, d2) {
  const den = d1[0] * d2[1] - d1[1] * d2[0];
  if (Math.abs(den) < 1e-9) return null;
  const dp = sub(p2, p1);
  const t = (dp[0] * d2[1] - dp[1] * d2[0]) / den;
  return add(p1, mul(d1, t));
}

/**
 * Echter Offset-Ring mit Gehrung: die nach innen geschobenen Kantengeraden
 * werden paarweise geschnitten. Frueher bekam jede Kante ein eigenes Rechteck -
 * an den Ecken schossen die uebereinander hinaus, der Ring kreuzte sich selbst
 * und lief an konkaven Ecken aus dem Areal.
 */
/**
 * Wie `offsetRing`, liefert aber zusaetzlich die Zuordnung: welche Punkte
 * gehoeren zu welcher Areal-Ecke? Bei Gehrung ist das genau einer, beim
 * Bogen an einer spitzen konkaven Ecke mehrere. Wird fuer das Randgruen
 * gebraucht, das exakt zwischen Kante und Versatz liegen muss.
 */
function offsetRingParts(site, d) {
  const ccw = signedArea(site) > 0;
  const n = site.length;
  const lines = [];
  for (let i = 0; i < n; i++) {
    const a = site[i], b = site[(i + 1) % n];
    const e = norm(sub(b, a));
    const nn = ccw ? [-e[1], e[0]] : [e[1], -e[0]];
    lines.push({ p: add(a, mul(nn, d)), dir: e, nn });
  }
  const out = [], first = [], last = [];
  for (let i = 0; i < n; i++) {
    first[i] = out.length;
    const L1 = lines[(i - 1 + n) % n], L2 = lines[i];
    const hit = lineIntersect(L1.p, L1.dir, L2.p, L2.dir);
    // Die Gehrung gilt nur, wenn sie den Abstand auch WIRKLICH einhaelt und im
    // Areal liegt. An einer konkaven Ecke waechst sie mit `d / sin(Winkel/2)`
    // ueber alle Grenzen: gemessen an einer spitzen Kerbe lag die Ringecke
    // 9,8 m AUSSERHALB des Grundstuecks und 32 m Ringlaenge liefen daneben -
    // die Randstrasse selbst verliess damit das Areal. Der alte Deckel
    // (6 * d, also 68 m) griff viel zu spaet.
    const gut = hit && pointIn(hit, site)
      && distToBoundary(hit, site) >= d - 0.05 && len(sub(hit, site[i])) < d * 3;
    if (gut) { out.push(hit); last[i] = out.length - 1; continue; }
    // Sonst um die Ecke HERUMFUEHREN. Der korrekte Versatz einer konkaven Ecke
    // ist ein Kreisbogen mit Radius `d` um die Ecke. Eine gerade Abschraegung
    // waere die Sehne davon und kaeme der Kante zu nahe - gemessen 6,6 m statt
    // 11,4. Schrittweite 20 Grad haelt den Stich unter 0,2 m.
    const c = site[i];
    const a1 = Math.atan2(L1.nn[1], L1.nn[0]);
    const a2 = Math.atan2(L2.nn[1], L2.nn[0]);
    let dA = a2 - a1;
    while (dA <= -Math.PI) dA += 2 * Math.PI;
    while (dA > Math.PI) dA -= 2 * Math.PI;
    const mid = (delta) => [c[0] + d * Math.cos(a1 + delta / 2),
                            c[1] + d * Math.sin(a1 + delta / 2)];
    // Die kurze Drehrichtung fuehrt bei einer konkaven Ecke aus dem Areal.
    if (!pointIn(mid(dA), site)) dA += dA > 0 ? -2 * Math.PI : 2 * Math.PI;
    const steps = Math.max(1, Math.ceil(Math.abs(dA) / (20 * Math.PI / 180)));
    for (let s = 0; s <= steps; s++) {
      const ang = a1 + dA * s / steps;
      out.push([c[0] + d * Math.cos(ang), c[1] + d * Math.sin(ang)]);
    }
    last[i] = out.length - 1;
  }
  return { pts: out, first, last };
}

/** Nur der Versatzring, ohne Zuordnung. */
function offsetRing(site, d) { return offsetRingParts(site, d).pts; }

/**
 * Alle Schnittparameter der Geraden `base + t*d` mit den Kanten von `poly`,
 * aufsteigend und ohne Dubletten. Damit werden die Stellen gefunden, an denen
 * eine Fahrgasse die Randstrasse trifft - der Anfang und das Ende eines
 * Abschnitts. Frueher endeten die Reihen dort, wo zufaellig die letzte Bucht
 * passte, und zur Strasse klaffte eine Luecke.
 */
/**
 * Der Parameterbereich, in dem das Segment `a + t*u`, 0 <= t <= L, INNERHALB
 * des konvexen Vierecks `q` liegt - exakt, ohne Abtasten.
 *
 * Halbebenenschnitt: fuer jede Kante ist das Innere die eine Seite. Das
 * Segment wird an jeder Kante beschnitten; bleibt am Ende t0 <= t1, gibt es
 * einen Schnitt. Wird gebraucht, um Strassen an hoeherrangigen Strassen
 * ABZUSCHNEIDEN - dort zaehlt der Zentimeter, weil in CS2 sonst entweder eine
 * Fuge bleibt oder zwei Fahrbahnen uebereinanderliegen.
 */
function segInConvex(a, u, L, q) {
  const s = signedArea(q) > 0 ? 1 : -1;   // Umlaufsinn -> Innennormale
  let t0 = 0, t1 = L;
  for (let i = 0; i < q.length; i++) {
    const p0 = q[i], e = sub(q[(i + 1) % q.length], p0);
    const nx = -e[1] * s, ny = e[0] * s;
    const den = u[0] * nx + u[1] * ny;
    const num = (a[0] - p0[0]) * nx + (a[1] - p0[1]) * ny;
    if (Math.abs(den) < 1e-12) { if (num < 0) return null; continue; }
    const t = -num / den;
    if (den > 0) t0 = Math.max(t0, t); else t1 = Math.min(t1, t);
    if (t0 > t1) return null;
  }
  return [Math.max(0, t0), Math.min(L, t1)];
}

function lineCrossings(base, d, poly) {
  const out = [];
  for (let i = 0; i < poly.length; i++) {
    const a = poly[i], b = poly[(i + 1) % poly.length];
    const e = sub(b, a);
    const den = d[0] * e[1] - d[1] * e[0];
    if (Math.abs(den) < 1e-9) continue;
    const dp = sub(a, base);
    const u = (dp[0] * d[1] - dp[1] * d[0]) / den;
    if (u < -1e-9 || u > 1 + 1e-9) continue;
    // `dn` = Anteil von d senkrecht zur Kante. Eine halbe Fahrbahnbreite quer
    // zur Strasse entspricht LAENGS der Reihe `h/dn` - ohne diesen Faktor endet
    // alles zu frueh und ragt in die Fahrbahn. Sehr flache Schnitte werden
    // gedeckelt, dort laeuft die Reihe ohnehin neben der Strasse her.
    const ne = norm([-e[1], e[0]]);
    const dn = Math.abs(d[0] * ne[0] + d[1] * ne[1]);
    // `dn` ist der SINUS des Einmuendungswinkels. Ungeklemmt mitgeben, damit
    // die Mindestwinkel-Regel ihn auswerten kann.
    out.push({ t: (dp[0] * e[1] - dp[1] * e[0]) / den,
               dn: Math.max(dn, 0.25), dnRaw: dn });
  }
  out.sort((x, y) => x.t - y.t);
  return out.filter((c, i) => i === 0 || c.t - out[i - 1].t > 1e-6);
}

/**
 * Schneidet ein Segment auf den Bereich zu, der im Areal liegt und den
 * Randabstand einhaelt. Ohne das ragten Fahrgassen und Verbindungsstrassen
 * ueber das Polygon hinaus: sie wurden bis zu den Verbindungspunkten gezogen,
 * die selbst schon eine halbe Fahrgasse hinter der letzten Bucht liegen - und
 * die letzte Bucht steht bereits an der Setback-Linie.
 */
function clipSegment(a, b, site, clear) {
  const L = len(sub(b, a));
  if (L < 0.5) return null;
  const d = mul(sub(b, a), 1 / L);
  // Das LAENGSTE zusammenhaengende Stueck, nicht "erster bis letzter Treffer".
  // Bei einer konkaven Form schneidet die Linie den Ring mehr als zweimal;
  // erster-bis-letzter zieht die Fahrgasse dann ueber den Bereich DAZWISCHEN
  // hinweg - gemessen an der L-Form 6,8 m ausserhalb des Rings und 28,8 m auf
  // der Randstrasse.
  let lo = null, hi = null, curLo = null, curHi = null;
  for (let t = 0; t <= L + 1e-9; t += 0.25) {
    if (insideBy(add(a, mul(d, t)), site, clear)) {
      if (curLo === null) curLo = t;
      curHi = t;
      continue;
    }
    if (curLo !== null && (lo === null || curHi - curLo > hi - lo)) { lo = curLo; hi = curHi; }
    curLo = null;
  }
  if (curLo !== null && (lo === null || curHi - curLo > hi - lo)) { lo = curLo; hi = curHi; }
  if (lo === null || hi - lo < 0.5) return null;
  // Die Abtastung findet das laengste Stueck, ihre ENDEN liegen aber bis zu
  // einer Schrittweite daneben - und die Gasse endet dann bis zu 0,25 m vor
  // dem Fahrbahnrand. Genau diese Fuge war an drei Fahrgassen sichtbar
  // (gemessen 0,03 / 0,09 / 0,23 m). Dieselbe Fehlerart hatten schon die
  // Verbindungsstrassen; dort half exaktes Schneiden statt Abtasten.
  //
  // Hier bleibt die Abtastung, weil sie das laengste zusammenhaengende Stueck
  // findet - bei konkaven Formen schneidet die Linie den Ring mehrfach. Nur
  // die beiden Enden werden nachgeschaerft: zwischen dem letzten Punkt
  // draussen und dem ersten drinnen liegt die Kante, und die findet eine
  // Intervallhalbierung auf ein Zehntelmillimeter genau.
  const drin = (t) => insideBy(add(a, mul(d, t)), site, clear);
  const schaerfen = (innen, aussen) => {
    if (aussen < 0 || aussen > L) return innen;
    for (let k = 0; k < 40; k++) {
      const mid = (innen + aussen) / 2;
      if (drin(mid)) innen = mid; else aussen = mid;
    }
    return innen;
  };
  lo = schaerfen(lo, lo - 0.25);
  hi = schaerfen(hi, Math.min(hi + 0.25, L));
  if (hi - lo < 0.5) return null;
  return [add(a, mul(d, lo)), add(a, mul(d, hi))];
}

/** Springt die Ecke nach innen? Dann ist das Polygon dort konkav. */
function isReflex(poly, i) {
  const n = poly.length;
  const a = poly[(i - 1 + n) % n], b = poly[i], c = poly[(i + 1) % n];
  const cr = (b[0] - a[0]) * (c[1] - b[1]) - (b[1] - a[1]) * (c[0] - b[0]);
  return signedArea(poly) > 0 ? cr < 0 : cr > 0;
}

/**
 * Nur eine konvexe Ecke mit parallel zur Einfahrt laufender Anschlusskante
 * darf das normale Reihenende ersetzen. An einer konkaven Ecke laeuft die
 * Randstrasse ins Areal und bleibt deshalb gesperrt.
 */
function entranceCornerCanSnap(poly, edge, atStart) {
  const n = poly.length, corner = atStart ? edge : (edge + 1) % n;
  if (isReflex(poly, corner)) return false;
  const u = norm(sub(poly[(edge + 1) % n], poly[edge]));
  const adjacent = atStart
    ? norm(sub(poly[edge], poly[(edge - 1 + n) % n]))
    : norm(sub(poly[(edge + 2) % n], poly[(edge + 1) % n]));
  const normal = [-u[1], u[0]];
  // Nur die Schaerfe der Ecke begrenzen, NICHT den rechten Winkel verlangen.
  // Der alte Test liess die Ecke nur zu, wenn die Nachbarkante auf 5 Grad
  // genau senkrecht stand - Schraeg hat 71,6 Grad und fiel deshalb durch,
  // obwohl die Ecke konvex ist.
  return Math.abs(adjacent[0] * normal[0] + adjacent[1] * normal[1])
    >= MIN_CORNER_SNAP_SIN;
}

/**
 * Die FLAECHE einer Einfahrt.
 *
 * Senkrecht auf ihrer Kante ist sie ein Rechteck. Sitzt sie schraeg an einer
 * Ecke, waeren senkrecht abgeschnittene Enden falsch: die Polygonkante laeuft
 * dann im Winkel dazu, und an beiden Enden bleibt ein Keil unbelegt -
 * gemessen 8,7 m2 bei Schraeg, also 8,2 % des markierten Bereichs.
 *
 * Richtig ist ein PARALLELOGRAMM: gleiche Breite quer zur Achse, aber beide
 * Enden parallel zur Polygonkante. Der Versatz entlang der Kante ist dafuer
 * `ai/2 / proj` - damit ergibt er quer genau `ai/2`.
 */
function entranceQuadOf(start, dir, length, edgeU, proj, S) {
  const end = add(start, mul(dir, length));
  const e = proj && proj < 1 - 1e-9
    ? mul(edgeU, S.ai / 2 / proj)
    : mul([-dir[1], dir[0]], S.ai / 2);
  return [add(start, e), add(end, e), sub(end, e), sub(start, e)];
}

/**
 * Lage und Richtung einer an einer KONVEXEN Ecke eingerasteten Einfahrt.
 *
 * Dort uebernimmt die Einfahrt den Winkel der anschliessenden Randstrasse und
 * bildet deren Verlaengerung nach aussen. Beim Rechteck faellt das nicht auf,
 * weil die Nachbarkante zufaellig senkrecht steht; bei Schraeg sind es
 * 71,6 Grad, und die kollineare Lage liegt 3,3 m VOR dem Anfang des eigenen
 * Ringstuecks.
 *
 * EINE Funktion fuer Modell und Seite. Vorher rechnete die Seite ihre
 * Fangziele selbst - zwei Kopien derselben Geometrie sind genau die Quelle,
 * aus der die 5-Grad-Sperre und der Vorzeichenfehler an der Innennormale
 * kamen.
 */
function entranceCornerFit(site, S, edgeIndex, atStart) {
  if (!site || site.length < 3) return null;
  if (!entranceCornerCanSnap(site, edgeIndex, atStart)) return null;
  const N = site.length;
  const a = site[edgeIndex], b = site[(edgeIndex + 1) % N];
  const v = sub(b, a), L = len(v);
  if (L < FIT_EPS) return null;
  const u = mul(v, 1 / L);
  const winding = signedArea(site) >= 0 ? 1 : -1;
  const ringDist = S.es + S.sl + S.ai / 2;
  const corner = atStart ? a : b;
  // Laufrichtung der Nachbarkante wie im Polygon, fuer die Innennormale ...
  const ua = atStart
    ? norm(sub(site[edgeIndex], site[(edgeIndex - 1 + N) % N]))
    : norm(sub(site[(edgeIndex + 2) % N], site[(edgeIndex + 1) % N]));
  // ... und dieselbe Kante nach INNEN zeigend, als Einfahrtsrichtung.
  const dir = atStart ? mul(ua, -1) : ua;
  const na = mul([-ua[1], ua[0]], winding);
  const n = mul([-u[1], u[0]], winding);
  const proj = Math.abs(dir[0] * n[0] + dir[1] * n[1]);
  if (proj <= 0.3) return null;
  const hit = lineIntersect(add(corner, mul(na, ringDist)), dir, a, u);
  if (!hit) return null;
  const along = (hit[0] - a[0]) * u[0] + (hit[1] - a[1]) * u[1];
  if (!Number.isFinite(along) || along < -S.ai || along > L + S.ai) return null;
  return { along, dir, proj, length: (ringDist - S.ai / 2) / proj };
}

/** Gemeinsamer Endanschlag fuer Vollauf und schnellen Einfahrts-Neuaufbau. */
function entrancePlacementBounds(site, edge, run, ringLength, alongOffset, S) {
  // Am Eckziel beginnt der Rasterbruch eine halbe Fahrbahnbreite vor dem
  // Ringsegment. Dort gibt es auf der Eckseite weder Reihe noch Kappe.
  const startMargin = run.from <= FIT_EPS
    && entranceCornerCanSnap(site, edge.edge, true) ? -S.ai / 2 : 2 * S.sw;
  const endMargin = ringLength - run.to <= FIT_EPS
    && entranceCornerCanSnap(site, edge.edge, false) ? -S.ai / 2 : 2 * S.sw;
  return [
    Math.max(run.from + startMargin, -alongOffset),
    Math.min(run.to - S.ai - endMargin, edge.L - alongOffset - S.ai),
  ];
}

/**
 * Zerlegt das Areal an konkaven Ecken in Teilbereiche. Ohne das wird EIN
 * Raster ueber die Bounding Box gelegt - bei einer L-Form bekommt der zweite
 * Schenkel dann gar keine Fahrgasse, obwohl er gross genug waere. Das Vorbild
 * loest jeden Bereich einzeln, mit eigener Ausrichtung.
 */
function decompose(poly, depth) {
  depth = depth || 0;
  if (depth > 4 || poly.length < 4) return [poly];
  const n = poly.length;
  for (let i = 0; i < n; i++) {
    if (!isReflex(poly, i)) continue;
    const a = poly[(i - 1 + n) % n], b = poly[i];
    const dir = norm(sub(b, a));
    let bestT = Infinity, bestJ = -1, bestP = null;
    for (let j = 0; j < n; j++) {
      if (j === i || (j + 1) % n === i) continue;
      const q1 = poly[j], q2 = poly[(j + 1) % n];
      const L = len(sub(q2, q1));
      if (L < 0.001) continue;
      const hit = lineIntersect(b, dir, q1, mul(sub(q2, q1), 1 / L));
      if (!hit) continue;
      const t = (hit[0] - b[0]) * dir[0] + (hit[1] - b[1]) * dir[1];
      if (t < 0.5 || t >= bestT) continue;
      const u = ((hit[0] - q1[0]) * (q2[0] - q1[0]) +
                 (hit[1] - q1[1]) * (q2[1] - q1[1])) / (L * L);
      if (u < 0 || u > 1) continue;
      bestT = t; bestJ = j; bestP = hit;
    }
    if (bestJ < 0) continue;
    const A = [], B = [];
    for (let k = i; ; k = (k + 1) % n) { A.push(poly[k]); if (k === bestJ) break; }
    A.push(bestP);
    B.push(bestP);
    for (let k = (bestJ + 1) % n; ; k = (k + 1) % n) { B.push(poly[k]); if (k === i) break; }
    if (A.length < 3 || B.length < 3) continue;
    return decompose(A, depth + 1).concat(decompose(B, depth + 1));
  }
  return [poly];
}

/** Richtung der laengsten Kante, in Grad, 0..180. */
function longestEdgeAngle(site) {
  let bestLen = -1, bestDeg = 0;
  for (let i = 0; i < site.length; i++) {
    const a = site[i], b = site[(i + 1) % site.length];
    const L = len(sub(b, a));
    if (L <= bestLen) continue;
    bestLen = L;
    const deg = Math.atan2(b[1] - a[1], b[0] - a[0]) * 180 / Math.PI;
    bestDeg = Math.round(((deg % 180) + 180) % 180);
  }
  return bestDeg;
}

/** Randreihen nach derselben Kapselregel abschliessen - im Vollauf und
 * beim schnellen Neuaufbau nach einer verschobenen Einfahrt. */
function finishPerimeterRows(out, site, S, crossWidth, ringJunctionLine,
                             corridors, rowDepth) {
  const perimeterKind = (kind) => kind === "rand" || kind === "rand-innen";
  const center = (q) => mul(q.reduce((s, p) => add(s, p), [0, 0]), 1 / q.length);
  const dot = (a, b) => a[0] * b[0] + a[1] * b[1];
  const roads = [];
  const addRoads = (segments, width, supportsRow = false) => {
    for (const segment of segments) {
      const [a, b] = segment, v = sub(b, a), L = len(v);
      if (L < 0.2) continue;
      const u = mul(v, 1 / L), nn = [-u[1] * width / 2, u[0] * width / 2];
      roads.push({ segment, u, supportsRow,
        q: [add(a, nn), add(b, nn), sub(b, nn), sub(a, nn)] });
    }
  };
  // Nur die eigentlichen Ringsegmente tragen die Randreihen. Eckstummel,
  // Fahrgassen und Verbindungen sind dagegen moegliche Strassen am Laufende.
  addRoads(out.perimeterLine, S.ai, true);
  addRoads(ringJunctionLine, S.ai);
  addRoads(out.aisleLine, S.ai);
  addRoads(out.crossLine, crossWidth);
  addRoads(out.entranceLine, S.ai);

  const rowData = (q) => {
    const c = center(q), u = norm(sub(q[1], q[0])), n = norm(sub(q[3], q[0]));
    let support = null, supportDist = Infinity;
    for (const road of roads) {
      if (!road.supportsRow || Math.abs(dot(u, road.u)) < 1 - 1e-8) continue;
      const d = distToSeg(c, road.segment[0], road.segment[1]);
      if (d < supportDist) { support = road; supportDist = d; }
    }
    if (!support) return null;
    // Eine Probe unmittelbar hinter der Bucht zeigt, auf welcher Querseite
    // die tragende Randstrasse liegt; die zusaetzlichen md Meter liegen
    // auf der abgewandten Seite.
    const plusIsRoad = pointIn(add(c, mul(n, rowDepth / 2 + 0.1)), support.q);
    return { c, u, n, support, away: plusIsRoad ? mul(n, -1) : n };
  };
  const endEdge = (d, sign) => add(d.c, mul(d.u, sign * S.sw / 2));
  const extension = (base, d, sign, L, depth) =>
    rect(add(base, mul(d.u, sign * L / 2)), d.u, d.n, L / 2, depth / 2);
  const touchingBay = (i, d, sign, removed) => {
    const probe = extension(endEdge(d, sign), d, sign, 0.1, rowDepth - 0.1);
    return out.bay.some((q, j) => j !== i && !removed.has(j)
      && quadsOverlap(probe, q, 0));
  };
  const roadAhead = (base, d, sign, depth) => {
    const max = 2 * S.sw + 0.1;
    const probe = extension(base, d, sign, max, depth);
    let best = null;
    for (const road of roads) {
      // Ein paralleles Stueck ist die tragende Strasse oder ihre kollineare
      // Eckverlaengerung, keine quer vor dem Reihenende liegende Fahrbahn.
      const angle = Math.acos(Math.min(1, Math.abs(dot(d.u, road.u))))
                  * 180 / Math.PI;
      // Unter 45 Grad laeuft die Strasse von der Reihe weg. Dort bleibt die
      // bereits erfolgte Einzelpruefung jeder Bucht massgeblich.
      if (road === d.support || angle < MIN_TRANSVERSE_CAP_ANGLE
          || !quadsOverlap(probe, road.q, 0)) continue;
      let lo = 0, hi = max;
      for (let k = 0; k < 45; k++) {
        const mid = (lo + hi) / 2;
        if (quadsOverlap(extension(base, d, sign, mid, depth), road.q, 0)) hi = mid;
        else lo = mid;
      }
      if (!best || hi < best.gap) best = { gap: hi, road };
    }
    return best;
  };

  const removed = new Set();
  for (let changed = true; changed; ) {
    changed = false;
    for (let i = 0; i < out.bay.length; i++) {
      if (removed.has(i) || !perimeterKind(out.bayKind[i])) continue;
      const d = rowData(out.bay[i]);
      if (!d) continue;
      for (const sign of [-1, 1]) {
        if (touchingBay(i, d, sign, removed)) continue;
        const hit = roadAhead(endEdge(d, sign), d, sign, rowDepth);
        if (!hit || hit.gap >= S.sw - FIT_EPS) continue;
        removed.add(i); changed = true; break;
      }
    }
  }

  if (removed.size) {
    const bay = [], kind = [];
    for (let i = 0; i < out.bay.length; i++) {
      if (removed.has(i)) continue;
      bay.push(out.bay[i]); kind.push(out.bayKind[i]);
    }
    out.bay = bay; out.bayKind = kind;
    out.perimeterStalls = kind.filter(perimeterKind).length;
    out.innerPerimeterStalls = kind.filter((x) => x === "rand-innen").length;
    out.innerStalls = kind.filter((x) => x === "innen").length;
    out.extraStalls = kind.filter((x) => x === "extra").length;
  }

  const noneRemoved = new Set();
  for (let i = 0; i < out.bay.length; i++) {
    if (!perimeterKind(out.bayKind[i])) continue;
    const d = rowData(out.bay[i]);
    if (!d) continue;
    for (const sign of [-1, 1]) {
      if (touchingBay(i, d, sign, noneRemoved)) continue;
      // BEIDE Randreihen haben keinen Mittelstreifen hinter sich: aussen liegt
      // die Grundstueckskante, innen die Randstrasse. Also gilt fuer beide die
      // Randreihen-Tiefe `sl`.
      // Vorher galt das nur fuer "rand"; "rand-innen" bekam `sl + md` = 8,0 m
      // und ragte damit 2,5 m tiefer als die eigene Reihe. Die Kappe schob die
      // Buchten weg - gemessen 7 statt 8 Buchten je Randreihe, waehrend die
      // Fahrgassenreihe daneben 8 hatte.
      const outer = out.bayKind[i] === "rand" || out.bayKind[i] === "rand-innen";
      const capDepth = outer ? rowDepth : rowDepth + S.md;
      const base = outer ? endEdge(d, sign)
        : add(endEdge(d, sign), mul(d.away, S.md / 2));
      const hit = roadAhead(base, d, sign, capDepth);
      if (!hit || hit.gap < S.sw - FIT_EPS || hit.gap >= 2 * S.sw - FIT_EPS)
        continue;
      const cap = extension(base, d, sign, hit.gap, capDepth);
      if (!cap.every((p) => insideBy(p, site, S.es))) continue;
      if (corridors.some((q) => quadsOverlap(cap, q, 0.05))) continue;
      if (out.bay.some((q) => quadsOverlap(cap, q, 0.05))) continue;
      if ([...out.green, ...out.median, ...out.cap]
          .some((q) => quadsOverlap(cap, q, 0.05))) continue;
      out.cap.push(cap);
    }
  }
}

/**
 * Modulraster quer durchs ganze Areal. Aus dem Video (parking solver) und dem
 * hochaufgeloesten 08s-Frame uebernommen:
 *  - KEINE eigene Randstrasse. Die aeusserste Fahrgasse bedient die Randreihe;
 *    die Buchten stehen direkt an der Setback-Linie. Bei uns verbrauchte der
 *    zusaetzliche Ring gemessene 19 % der Arealflaeche.
 *  - Verbindungsstrassen an den REIHENENDEN (und mittig bei langen Reihen),
 *    nicht in festem Abstand. Fester Abstand kostete im Prototyp 12-19 % der
 *    Buchten und drehte den Vorteil beim Rechteck ins Minus.
 *  - Reihenrichtung aus der dominanten Kante, bei jeder Aenderung neu.
 */
function buildImpl(site, S) {
  const out = { bay: [], median: [], cap: [], green: [], fill: [], ring: [],
                perimeterLine: [], perimeterQuad: [], entrances: [], entranceLine: [],
                entranceQuad: [],
                aisleLine: [], crossLine: [],
                stalls: 0, perimeterStalls: 0, innerPerimeterStalls: 0,
                innerStalls: 0, extraStalls: 0, notchAisles: 0,
                shifted: 0, dropped: 0,
                angle: 0, aisles: 0, sections: [], fillHole: [], bayKind: [], warnings: [] };
  if (!site || site.length < 3) { out.warnings.push("Weniger als 3 Ecken."); return out; }

  const rowDepth = S.sl;
  // Querstrassen benutzen das gemessene einspurige Vanilla-Prefab. Ihre
  // Fahrbahnbreite ist unabhaengig vom 7-m-Kern der Park-Fahrgassen.
  const crossWidth = Number.isFinite(S.cw) ? S.cw : 3;
  const module = S.ai + 2 * rowDepth + S.md;
  const clear = S.es;
  const axisCoord = (p, u) => p[0] * u[0] + p[1] * u[1];
  const makeLongGrid = (u, edgeOffset) => ({
    u, edgeOffset,
    center: (i) => edgeOffset + (i + 0.5) * S.sw,
    firstEdgeAtOrAfter: (t) => Math.ceil((t - edgeOffset) / S.sw - 1e-9),
  });
  // Randstrasse: sie traegt Buchten auf BEIDEN Seiten - aussen zur
  // Grundstueckskante, innen zum ersten Modul. Im Video ist sie keine Linie,
  // sondern die durchgehende Fahrflaeche zwischen Randreihe und Modulen; genau
  // deshalb hatte ich sie uebersehen und faelschlich geloescht.
  const ringDist = S.es + rowDepth + S.ai / 2;
  // Das Fahrgassenraster bleibt samt Gruenstreifen von der Randstrasse frei.
  // Niedriger priorisierte Buchten an ihrer Innenseite kommen erst nach dem
  // vollstaendigen Fahrgassenlayout hinzu; fuer dessen Suche bleibt deshalb
  // dieses strengere Mass bestehen.
  const interiorClear = ringDist + S.ai / 2 + S.md;
  // Bis hierher darf eine Bucht in JEDE Richtung heran: der innere Fahrbahnrand
  // der Randstrasse.
  const roadClear = ringDist + S.ai / 2;
  // Die Fahrgasse muss den Ring ERREICHEN - sie endet auf seiner Mittellinie.
  // Sie auf Modulabstand zur Kante zuzuschneiden war falsch: dann kann sie den
  // Ring nicht mehr treffen und das Netz zerfaellt. Den Abstand halten die
  // BUCHTEN ueber interiorClear, nicht die Fahrbahn.
  const occupied = makeIndex(Math.max(4, rowDepth * 2));
  // Je Fahrgassenseite das bereits gewaehlte Laengsraster. Spaetere
  // Nachrueckbuchten duerfen kein neues Raster am Segmentanfang eroeffnen.
  const aisleGrids = [];

  /**
   * Randgruen als echtes OFFSET-BAND zwischen Arealkante und dem um `es` nach
   * innen versetzten Ring. Vorher bekam jede Kante ein eigenes Rechteck; an
   * konvexen Ecken blieb dadurch ein Keil frei (der als Restfuellung endete),
   * an konkaven ueberlappten sie sich und ragten aus dem Areal heraus -
   * gemessen 3,3 m2 draussen und bis 36 m2 Ueberlappung.
   *
   * Die Ecke wird VERSETZT, nicht gerundet: der Gehrungspunkt ist genau ein
   * Punkt. Nur wenn die Gehrung entgleist (sehr spitze konkave Ecke), fuegt
   * `offsetRingParts` dort einen Bogen ein - dann bekommt die Ecke ein eigenes
   * Faecherstueck.
   */
  if (S.es > 0.05) {
    const band = offsetRingParts(site, S.es);
    const N = site.length;
    for (let i = 0; i < N; i++) {
      const a = site[i], b = site[(i + 1) % N];
      if (len(sub(b, a)) < 0.5) continue;
      const innenB = band.pts[band.first[(i + 1) % N]];
      const innenA = band.pts[band.last[i]];
      const q = [a, b, innenB, innenA];
      if (Math.abs(signedArea(q)) > 0.01) out.green.push(q);
    }
    for (let i = 0; i < N; i++) {
      if (band.last[i] === band.first[i]) continue;
      const faecher = [site[i]];
      for (let k = band.first[i]; k <= band.last[i]; k++) faecher.push(band.pts[k]);
      if (Math.abs(signedArea(faecher)) > 0.01) out.green.push(faecher);
    }
  }

  const entranceGreenBase = out.green.slice();

  // Ring plus Randreihen aussen und innen.
  const ring = offsetRing(site, ringDist);
  out.ring = ring;
  // Ringsegmente mit Laufrichtung - fuer den Parallelitaetstest der Fahrgassen.
  const ringSegs = [];
  for (let i = 0; i < ring.length; i++) {
    const a = ring[i], b = ring[(i + 1) % ring.length];
    const L = len(sub(b, a));
    if (L < 0.5) continue;
    ringSegs.push({ a, b, u: mul(sub(b, a), 1 / L) });
  }
  for (let i = 0; i < ring.length; i++)
    out.perimeterLine.push([ring[i], ring[(i + 1) % ring.length]]);

  /**
   * Die FAHRBAHNFLAECHE der Randstrasse, ein Vieleck JE SEGMENT.
   *
   * Bisher war das je ein Rechteck mit stumpfen Enden. An jeder Ringecke blieb
   * dadurch eine Kerbe, die die Restfuellung mit Gruen auffuellte statt mit
   * Asphalt - gemessen 0,76 und 0,68 m2 bei Schraeg. Die Seite zeichnete
   * dieselbe Ecke ausserdem RUND (`lineJoin: round`), also nochmal anders.
   *
   * Jetzt liegt die Endkante jedes Segments auf der GEHRUNG. Zwei Nachbarn
   * teilen sich damit exakt dieselben zwei Punkte: keine Kerbe, keine
   * Ueberlappung, keine Fuge.
   *
   * Bewusst NICHT zu einem Polygon verschmolzen - in CS2 wird daraus je
   * Segment eine eigene Flaeche. Die Mittellinie in `perimeterLine` bleibt
   * unangetastet; daraus entsteht dort die eigentliche Strasse.
   */
  {
    // Gehrung direkt am RING rechnen, nicht ueber die Arealecken: bei
    // Referenz 08s hat der Ring 12 Punkte fuer 11 Arealecken (offsetRing
    // setzt an spitzen Stellen Boegen ein). Ueber die Arealecken indiziert
    // verrutscht die Zuordnung, und es blieben 0,2 % ungedeckt.
    const N = ring.length;
    const kante = (i) => {
      const a = ring[i], b = ring[(i + 1) % N], v = sub(b, a), L = len(v);
      return L < FIT_EPS ? null : { a, b, u: mul(v, 1 / L), L };
    };
    // Schnittpunkt der um `d` versetzten Geraden zweier aufeinanderfolgender
    // Kanten. Faellt er aus (fast parallel oder zu weit), bleibt es stumpf.
    const gehrung = (e0, e1, d) => {
      const n0 = [-e0.u[1] * d, e0.u[0] * d], n1 = [-e1.u[1] * d, e1.u[0] * d];
      const hit = lineIntersect(add(e0.a, n0), e0.u, add(e1.a, n1), e1.u);
      if (!hit) return add(e1.a, n1);
      return len(sub(hit, e1.a)) > 3 * S.ai ? add(e1.a, n1) : hit;
    };
    for (let i = 0; i < N; i++) {
      const e = kante(i), vor = kante((i - 1 + N) % N), nach = kante((i + 1) % N);
      if (!e) { out.perimeterQuad.push(null); continue; }
      const halb = S.ai / 2;
      const a0 = vor ? gehrung(vor, e, -halb) : add(e.a, [-e.u[1] * -halb, e.u[0] * -halb]);
      const i0 = vor ? gehrung(vor, e, halb) : add(e.a, [-e.u[1] * halb, e.u[0] * halb]);
      const a1 = nach ? gehrung(e, nach, -halb) : add(e.b, [-e.u[1] * -halb, e.u[0] * -halb]);
      const i1 = nach ? gehrung(e, nach, halb) : add(e.b, [-e.u[1] * halb, e.u[0] * halb]);
      const q = [a0, a1, i1, i0];
      out.perimeterQuad.push(Math.abs(signedArea(q)) > 0.01 ? q : null);
    }
    for (let i = out.perimeterQuad.length - 1; i >= 0; i--)
      if (!out.perimeterQuad[i]) out.perimeterQuad.splice(i, 1);
  }
  // Rechteckige Fahrbahnkorridore enden stumpf. An jedem Knick verlaengert
  // deshalb ein kurzes, gleich breites Strassenstueck den ankommenden Korridor
  // bis zur Aussenkante des abgehenden. Die bislang freie Eckflaeche ist damit
  // Teil der Strassenkreuzung und nicht Restgruen.
  const ringJunctionLine = [];
  for (let i = 0; i < ring.length; i++) {
    const a = ring[(i - 1 + ring.length) % ring.length];
    const c = ring[i], b = ring[(i + 1) % ring.length];
    const va = sub(c, a), vb = sub(b, c);
    if (len(va) < 0.2 || len(vb) < 0.2) continue;
    const incoming = norm(va), outgoing = norm(vb);
    const cosine = Math.max(-1, Math.min(1,
      incoming[0] * outgoing[0] + incoming[1] * outgoing[1]));
    const reach = S.ai / 2 * Math.tan(Math.acos(cosine) / 2);
    if (Number.isFinite(reach) && reach > FIT_EPS)
      ringJunctionLine.push([c, add(c, mul(incoming, reach))]);
  }
  // Ring-Aussen- und Ring-Innenseite starten mit derselben einmal berechneten
  // Phase. Nur die aeussere Reihe erhaelt an Einfahrten zusaetzliche Neustarts.
  // Kurze, uebersprungene Segmente drehen die Grundphase wie bisher nicht weiter.
  let ringCarry = 0;
  const perimeterGrids = out.perimeterLine.map(([a, b]) => {
    const grid = { carry: ringCarry, breaks: [] };
    const L = len(sub(b, a));
    if (L >= 0.2) ringCarry = (ringCarry + L) % S.sw;
    return grid;
  });
  const perimeterGridRuns = (grid, L) => {
    const runs = [];
    let from = 0, edgeOffset = grid.carry;
    const modulo = (x, m) => ((x % m) + m) % m;
    for (const brk of grid.breaks.slice().sort((a, b) => a.start - b.start)) {
      // Auch vor der Einfahrt kommt der Rasterursprung von ihrem Fahrbahnrand.
      // edgeOffset ist die erste dazu kongruente Buchtgrenze im Restlauf.
      edgeOffset = from + modulo(brk.start - from, S.sw);
      runs.push({ from, to: brk.start, edgeOffset });
      from = brk.end;
      edgeOffset = brk.end;
    }
    runs.push({ from, to: L, edgeOffset });
    return runs;
  };

  /**
   * Ein-/Ausfahrten liegen automatisch auf jeder gleich langen laengsten
   * Arealkante. Eine Vorgabe in S.entrances behaelt stattdessen ihre Kante und
   * ihren stufenlosen Abstand vom Kantenanfang. Die Einfahrt rastet NICHT auf
   * das Buchtenraster: Beide Fahrbahnraender werden stattdessen je zum Ursprung
   * der von dort nach aussen laufenden Randbuchten.
   *
   * Der zulaessige Bereich braucht beidseits neben der Gruenbreite noch den
   * ersten Randplatz. Nur an diesen stetigen Bereichsgrenzen wird eine Vorgabe
   * begrenzt; damit koennen weder Fahrbahn noch Kappe ueber ein Kantenende
   * hinausragen. Die Mittellinie endet stumpf an der AUSSENKANTE der hoeher
   * priorisierten Randstrasse, also nach es + sl Metern statt auf ihrer
   * Mittellinie.
   */
  {
    const edges = site.map((a, edge) => {
      const b = site[(edge + 1) % site.length], v = sub(b, a), L = len(v);
      return { edge, a, b, L, u: L > FIT_EPS ? mul(v, 1 / L) : [1, 0] };
    });
    const longest = Math.max(...edges.map((e) => e.L));
    const tieEps = Math.max(FIT_EPS, longest * 1e-9);
    const winding = signedArea(site) >= 0 ? 1 : -1;
    const placementsFor = (edge, wantedAlong) => {
      const placements = [];
      out.perimeterLine.forEach(([a, b], ringIndex) => {
        const v = sub(b, a), L = len(v);
        if (L < 0.2) return;
        const u = mul(v, 1 / L);
        if (axisCoord(u, edge.u) < 1 - 1e-8) return;
        const across = axisCoord(sub(a, edge.a),
          mul([-edge.u[1], edge.u[0]], winding));
        if (Math.abs(across - ringDist) > 0.05) return;
        const alongOffset = axisCoord(sub(a, edge.a), edge.u);
        for (const run of perimeterGridRuns(perimeterGrids[ringIndex], L)) {
          const [lo, hi] = entrancePlacementBounds(
            site, edge, run, L, alongOffset, S);
          if (lo > hi + FIT_EPS) continue;
          const wantedStart = wantedAlong - alongOffset - S.ai / 2;
          const start = Math.max(lo, Math.min(hi, wantedStart));
          placements.push({
            along: alongOffset + start + S.ai / 2,
            ringIndex, start, end: start + S.ai,
          });
        }
      });
      return placements.sort((a, b) =>
        Math.abs(a.along - wantedAlong) - Math.abs(b.along - wantedAlong)
        || a.along - b.along);
    };
    const specified = Array.isArray(S.entrances) && S.entrances.length;
    const requests = specified ? S.entrances : edges
      .filter((edge) => longest - edge.L <= tieEps)
      .map((edge) => ({ edge: edge.edge, along: edge.L / 2 }));
    const queued = [], chosenEntrances = [];
    requests.forEach((request, order) => {
      const edgeIndex = request && Number.isInteger(request.edge) ? request.edge : -1;
      const edge = edges[edgeIndex];
      if (!edge || !Number.isFinite(request.along)) {
        out.warnings.push("Ungueltige Einfahrtsvorgabe ignoriert.");
        return;
      }
      queued.push({ request, order, edge });
    });
    queued.sort((a, b) => a.edge.edge - b.edge.edge
                            || a.request.along - b.request.along);

    for (const { request, order, edge } of queued) {
      const n = mul([-edge.u[1], edge.u[0]], winding);

      /**
       * ECKFANG MIT EIGENEM WINKEL. Normalerweise steht die Einfahrt senkrecht
       * auf ihrer Kante. An einer konvexen Ecke soll sie stattdessen die
       * Richtung der dort anschliessenden Randstrasse uebernehmen und deren
       * Verlaengerung nach aussen bilden.
       *
       * Beim Rechteck faellt der Unterschied nicht auf, weil die
       * Nachbarkante dort zufaellig senkrecht steht. Bei Schraeg sind es
       * 71,6 Grad: die senkrechte Einfahrt weicht um 18,4 Grad von der
       * Randstrasse ab, und die kollineare Lage liegt 3,3 m VOR dem Anfang
       * des eigenen Ringstuecks - deshalb reicht es nicht, nur den Anschlag
       * zu lockern.
       */
      const cornerAt = request.corner === "start" ? true
        : request.corner === "end" ? false : null;
      const eckig = cornerAt !== null
        && entranceCornerCanSnap(site, edge.edge, cornerAt);
      if (eckig) {
        const fit = entranceCornerFit(site, S, edge.edge, cornerAt);
        if (fit) {
          const { along, dir, proj } = fit;
          const halb = S.ai / 2 / proj;
          const ziel = placementsFor(edge, along)
            .find((q) => Math.abs(q.along - along) < S.ai) || null;
          if (ziel) {
            perimeterGrids[ziel.ringIndex].breaks.push({
              start: along - (ziel.along - ziel.start) - halb + S.ai / 2,
              end: along - (ziel.along - ziel.start) + halb + S.ai / 2,
            });
            const start = add(edge.a, mul(edge.u, along));
            const end = add(start, mul(dir, fit.length));
            chosenEntrances.push({ order,
              entrance: { edge: edge.edge, along, corner: request.corner },
              line: [start, end],
              quad: entranceQuadOf(start, dir, fit.length, edge.u, proj, S) });
            continue;
          }
        }
      }

      const chosen = placementsFor(edge, request.along)[0];
      if (!chosen) {
        out.warnings.push("Kein Randabschnitt mit beidseitiger Kappe fuer Einfahrt gefunden.");
        continue;
      }
      perimeterGrids[chosen.ringIndex].breaks.push(
        { start: chosen.start, end: chosen.end });
      const start = add(edge.a, mul(edge.u, chosen.along));
      const end = add(start, mul(n, ringDist - S.ai / 2));
      chosenEntrances.push({ order, entrance: { edge: edge.edge, along: chosen.along },
                             line: [start, end],
                             quad: entranceQuadOf(start, n, ringDist - S.ai / 2,
                                                  edge.u, 1, S) });
    }
    chosenEntrances.sort((a, b) => a.order - b.order);
    out.entrances.push(...chosenEntrances.map((x) => x.entrance));
    out.entranceLine.push(...chosenEntrances.map((x) => x.line));
    out.entranceQuad.push(...chosenEntrances.map((x) => x.quad));
  }

  const entranceRoad = out.entranceQuad.map((q) => q.slice());
  if (entranceRoad.length) {
    const green = [];
    for (const q0 of out.green) {
      const cuts = entranceRoad.filter((z) => quadsOverlap(q0, z, 0));
      if (!cuts.length) { green.push(q0); continue; }
      const q = signedArea(q0) >= 0 ? q0 : q0.slice().reverse();
      const teile = slabFill(q, cuts,
        (p) => cuts.some((z) => pointIn(p, z)), 0);
      green.push(...teile.aussen);
    }
    out.green = green;
  }

  // Fahrbahnflaeche der Randstrasse, VOR dem Setzen der Randreihe. An einer
  // Kerbe laeuft der Ring um die Ecke, und eine Bucht der einen Kante landet auf
  // der Fahrbahn der anderen - in der L-Form drei Stueck, seit langem gemeldet.
  // Gehrungsflaechen fuer den Ring, stumpfe Rechtecke nur fuer die Eckstummel.
  // Die Eckstummel gehoeren NICHT mehr zur Fahrbahnflaeche. Sie ueberbrueckten
  // die Kerbe, die zwei stumpf endende Rechtecke an einer Ecke liessen; seit
  // die Randstrasse GEGEHRTE Flaechen liefert, gibt es die Kerbe nicht mehr
  // (1,44 -> 0,00 m2). Sichtbar blieb nur ihr Ueberstand schraeg ins Gruen -
  // und der nahm den Randbuchten Platz weg.
  // Als LOGISCHE Strasse bleiben sie der Kappenregel erhalten (addRoads).
  const ringRoad = out.perimeterQuad.map((q) => q.slice());
  for (const [a, b] of out.perimeterLine.slice(out.perimeterQuad.length)) {
    const v = sub(b, a), L = len(v);
    if (L < 0.2) continue;
    const u = mul(v, 1 / L), nn = [-u[1] * S.ai / 2, u[0] * S.ai / 2];
    ringRoad.push([add(a, nn), add(b, nn), sub(b, nn), sub(a, nn)]);
  }
  ringRoad.push(...entranceRoad);
  /**
   * AUSNAHME AN DER KONKAVEN ECKE (Nutzerregel).
   *
   * Dort endet ein Stueck Randstrasse. Ab der Ecke laeuft eine FAHRGASSE
   * geradeaus weiter ins Innere. Sie beginnt exakt AN der Ecke und liegt
   * nirgends auf der Randstrasse - in CS2 duerfen sich zwei Strassen nicht
   * ueberlagern.
   *
   * Weil Randstrasse und Fahrgasse fluchten, darf die Buchtenreihe der
   * Fahrgasse an der Randstrasse WEITERLAUFEN, dort auf der Innenseite; die
   * Aussenseite gehoert der Randreihe. Genau das macht die Verlaengerung
   * lohnend - ohne die durchlaufende Reihe kostete sie nur Flaeche (gemessen
   * L-Form 155 -> 137 Buchten).
   */
  const notchGreens = [], notchCaps = [], protectedNotchBays = [], notchLines = [],
        notchRows = [];
  for (let k = 0; k < site.length && !S.__noNotch; k++) {
    if (!isReflex(site, k)) continue;
    // NUR EINE Verlaengerung je Ecke - die laengere. Beide zu nehmen zerschnitt
    // die L-Form so, dass fuers Modulraster nichts blieb (innen 41 -> 1) und
    // unterm Strich 7 Buchten fehlten.
    const kand = [(k - 1 + ring.length) % ring.length, (k + 1) % ring.length]
      .map((nb) => {
        const v = sub(ring[k], ring[nb]);
        if (len(v) < S.sw) return null;
        const u = norm(v);
        const h = lineCrossings(ring[k], u, ring)
          .filter((c) => c.t > S.sw).sort((a, b) => a.t - b.t)[0];
        return h ? { nb, t: h.t } : null;
      }).filter(Boolean).sort((a, b) => b.t - a.t);
    for (const { nb } of kand.slice(0, 1)) {
      const v = sub(ring[k], ring[nb]);
      const back = len(v);
      if (back < S.sw) continue;
      const u = norm(v), nn = [-u[1], u[0]];
      const hit = lineCrossings(ring[k], u, ring)
        .filter((c) => c.t > S.sw).sort((a, b) => a.t - b.t)[0];
      if (!hit || hit.t < S.ai + 3 * S.sw) continue;
      const end = add(ring[k], mul(u, hit.t));
      if (!pointIn(mul(add(ring[k], end), 0.5), ring)) continue;
      let eng = false;
      for (let t = S.ai; t <= hit.t - S.ai && !eng; t += S.sw) {
        const q = add(ring[k], mul(u, t));
        for (const sg of ringSegs) {
          // NUR parallele Segmente. Ohne diesen Test schlaegt auch die
          // rechtwinklig gekreuzte Randstrasse an und toetet jede Verlaengerung.
          if (Math.abs(u[0] * sg.u[0] + u[1] * sg.u[1]) < PARALLEL_COS) continue;
          const dc = sideGap(q, sg.a, sg.b);
          if (dc > MERGE_TOL && dc < S.ai + rowDepth + S.md - 0.05) { eng = true; break; }
        }
      }
      if (eng) continue;
      // Auch die Eckgasse gehorcht der Rangfolge: sie endet am FAHRBAHNRAND der
      // Randstrasse, nicht auf deren Mittellinie. Vorher steckte sie an jedem
      // Ende eine halbe Fahrbahnbreite darin - gemessen 9,3 m der L-Form.
      const gasse = clipSegment(ring[k], end, ring, S.ai / 2);
      if (!gasse || len(sub(gasse[1], gasse[0])) < S.ai + 2 * S.sw) continue;
      const aisleIndex = out.aisleLine.length;
      out.aisleLine.push(gasse);
      aisleGrids.push({});
      out.notchAisles++;
      // Fuer die Buchten zaehlt die fluchtende Verbindung als EIN Lauf.
      // clipSegment kuerzt nur die rangniedrigere Strassenmittellinie am
      // Fahrbahnrand; daraus darf keine Luecke im seitlichen Raster entstehen.
      const proj = (q) => (q[0] - ring[k][0]) * u[0] + (q[1] - ring[k][1]) * u[1];
      const reach = Math.max(proj(gasse[0]), proj(gasse[1]));
      // Wie ein Randstrassensegment behandeln: dann haelt auch das Modulraster
      // den Mindestabstand ein und legt keine Fahrgasse ueber ihre Buchten.
      ringSegs.push({ a: ring[k], b: end, u });
      // Korridor STUECKWEISE sperren - `makeIndex` sortiert ein Viereck nur
      // nach seinen Ecken ein, eine lange Fahrbahn faende in der Mitte niemand.
      for (let t = 0; t < hit.t; t += S.sw) {
        const p0 = add(ring[k], mul(u, t));
        const p1 = add(ring[k], mul(u, Math.min(t + S.sw, hit.t)));
        const w = mul(nn, S.ai / 2);
        occupied.add([add(p0, w), add(p1, w), sub(p1, w), sub(p0, w)]);
      }
      const mid = mul(add(ring[nb], ring[k]), 0.5);
      notchLines.push({ corner: ring[k], u, nn, back, reach, aisleIndex,
        inSign: distToBoundary(add(mid, mul(nn, 3)), site)
              > distToBoundary(sub(mid, mul(nn, 3)), site) ? 1 : -1 });
    }
  }
  // ERST jetzt die Buchten - sonst legt die zweite Eckgasse ihren Korridor
  // ueber die Buchten der ersten (gemessen 27 Buchten auf einer Fahrbahn).
  for (const L of notchLines) {
    const { corner, u, nn, back, reach, inSign, aisleIndex } = L;
    for (const side of [-1, 1]) {
      let run = [];
      const flush = () => {
        // Auch die Kerbenreihe braucht an BEIDEN Enden eine regelgerechte
        // Kappe. Vorher wurden alle passenden Rasterplaetze sofort zu Buchten;
        // der Streifen entstand zwar dahinter, fuer Kappen blieb aber kein
        // einziger Platz. Gemessen: L-Form 3 Streifen, 6 Enden, 0 buendig.
        // Deshalb werden die beiden Endplaetze als Einzelreihen-Kappen gebaut
        // und nur die Plaetze dazwischen als Buchten uebernommen.
        if (run.length < 3) { run = []; return; }
        const bayRun = run.slice(1, -1);
        const rowCaps = [];
        let rowStrip = null;
        const capAcross = side * (S.ai / 2 + (rowDepth + S.md) / 2);
        const capHalf = (rowDepth + S.md) / 2;
        for (const end of [run[0], run[run.length - 1]]) {
          const cc = add(add(corner, mul(u, end.t)), mul(nn, capAcross));
          const q = rect(cc, u, nn, S.sw / 2, capHalf);
          if (!notchCaps.some((z) => quadsOverlap(q, z, 0.05))) {
            notchCaps.push(q);
            rowCaps.push(q);
            occupied.add(q);
          }
        }
        for (const { q } of bayRun) {
          occupied.add(q);
          out.bay.push(q); out.bayKind.push("extra");
          out.extraStalls++;
        }
        protectedNotchBays.push(bayRun[0].q);
        if (bayRun.length > 1) protectedNotchBays.push(bayRun[bayRun.length - 1].q);
        if (S.md > 0.05) {
          const lo = bayRun[0].t, hi = bayRun[bayRun.length - 1].t;
          const cc = add(add(corner, mul(u, (lo + hi) / 2)),
                         mul(nn, side * (S.ai / 2 + rowDepth + S.md / 2)));
          rowStrip = rect(cc, u, nn, (hi - lo) / 2 + S.sw / 2, S.md / 2);
          notchGreens.push(rowStrip);
        }
        // Diese Reihe entsteht VOR pass() und gehoert deshalb nicht zu
        // dessen gemeinsamem Raster. Ihre Lage wird spaeter fuer genau die
        // Reihe gebraucht, die ihr ueber den Mittelstreifen gegenuebersteht.
        if (rowCaps.length === 2 && rowStrip) {
          const rowOffset = side * (S.ai / 2 + rowDepth / 2);
          notchRows.push({ u, back: mul(nn, side), caps: rowCaps, strip: rowStrip,
            rowPoint: add(add(corner, mul(u, bayRun[0].t)), mul(nn, rowOffset)),
            loPoint: add(corner, mul(u, bayRun[0].t - S.sw / 2)),
            hiPoint: add(corner, mul(u, bayRun[bayRun.length - 1].t + S.sw / 2)) });
        }
        run = [];
      };
      // Nur die durchlaufende Seite erbt das Raster des Randstrassenstuecks.
      // Die Gegenseite beginnt erst an der Eckgasse: Ihre erste Kappe setzt
      // am Fahrbahnrand an; die spaetere Gegenreihe erbt es ueber notchMate.
      const start = side === inSign
        ? -back + S.sw / 2
        : S.ai / 2 + S.sw / 2;
      aisleGrids[aisleIndex][side] = makeLongGrid(
        u, axisCoord(corner, u) + start - S.sw / 2);
      for (let t = start; t < reach; t += S.sw) {
        // Auf dem Randstrassenstueck nur die Innenseite - aussen steht die
        // Randreihe.
        if (t < 0 && side !== inSign) { flush(); continue; }
        // Den Rueckschnitt der Gassenmittellinie bei t=0 nicht als
        // Abschnittsende behandeln; die Flaechentests unten bleiben massgeblich.
        const c = add(add(corner, mul(u, t)),
                      mul(nn, side * (S.ai / 2 + rowDepth / 2)));
        if (!rectClear(c, u, nn, S.sw / 2, rowDepth / 2, site, S.es)
            || !insideBy(add(c, mul(nn, side * (rowDepth / 2 + S.md))), site, S.es)) {
          flush(); continue;
        }
        const q = rect(c, u, nn, S.sw / 2, rowDepth / 2);
        if (occupied.hits(q) || ringRoad.some((z) => quadsOverlap(q, z, 0.05))) {
          flush(); continue;
        }
        run.push({ t, q });
      }
      flush();
    }
  }
  for (const g of notchGreens) out.median.push(g);
  for (const g of notchCaps) out.cap.push(g);

  for (let i = 0; i < ring.length; i++) {
    const a = ring[i], b = ring[(i + 1) % ring.length];
    const L = len(sub(b, a)); if (L < 0.2) continue;
    const dd = norm(sub(b, a));
    let nn = [-dd[1], dd[0]];
    if (!pointIn(add(mul(add(a, b), 0.5), mul(nn, 0.5)), site)) nn = [-nn[0], -nn[1]];
    for (const run of perimeterGridRuns(perimeterGrids[i], L))
      for (let t = run.edgeOffset + S.sw / 2; t < run.to; t += S.sw) {
        if (t < run.from - FIT_EPS) continue;
        // nn zeigt nach innen, -nn also zur Grundstueckskante: nur dort.
        const c = add(add(a, mul(dd, t)),
                      mul(nn, -(S.ai / 2 + rowDepth / 2)));
        if (!rectClear(c, dd, nn, S.sw / 2, rowDepth / 2, site, clear)) continue;
        const q = rect(c, dd, nn, S.sw / 2, rowDepth / 2);
        // Die Randreihe gehoert AUSSERHALB der Randstrasse - weder auf ihr noch
        // hinter ihr im Areal.
        if (ringRoad.some((z) => quadsOverlap(q, z, 0.05))) continue;
        if (q.some((pp) => pointIn(pp, ring) && distToBoundary(pp, ring) >= S.ai / 2))
          continue;
        if (occupied.hits(q)) continue;
        occupied.add(q);
        out.bay.push(q); out.bayKind.push("rand");
        out.perimeterStalls++;
      }
  }

  let c0, span;
  function frameFromPart() {
    const xs = part.map((p) => p[0]), ys = part.map((p) => p[1]);
    c0 = [(Math.min(...xs) + Math.max(...xs)) / 2,
          (Math.min(...ys) + Math.max(...ys)) / 2];
    span = Math.hypot(Math.max(...xs) - Math.min(...xs),
                      Math.max(...ys) - Math.min(...ys));
  }

    /**
     * Gruenstreifen mit FESTER Tiefe zwischen zwei Buchtenreihen. Der Aufrufer
     * uebergibt nur noch die Buchtenspanne EINES Abschnitts - Verbindungs-
     * strassen liegen ausserhalb davon und muessen hier nicht mehr
     * herausgeschnitten werden.
     */
    function emitStrip(d, n, tLo, tHi, across) {
      const half = S.md / 2;
      // Die Laenge darf sich ans Areal anpassen, die Tiefe nie. Vorher fiel
      // ein ganzer Abschnitt weg, sobald sein Ende an eine schraege Kante
      // stiess - so fehlte bei der Referenz der halbe Streifen.
      const step = 0.5;
      // Beide Enden GENAU treffen: die Schrittfolge landet sonst irgendwo vor
      // tHi und der Streifen bleibt bis zu einem halben Schritt hinter der
      // Kappe zurueck.
      const probes = [];
      for (let t = tLo; t < tHi - 1e-9; t += step) probes.push(t);
      probes.push(tHi);
      let lo = null, hi = null;
      const flush = () => {
        if (lo === null) return;
        // Eine bestandene Probe bei t beweist, dass [t-step/2, t+step/2] frei
        // ist - das Aufmass gehoert dazu. Ohne es klaffte an jedem Ende eine
        // halbe Probe (gemessen 0,30 m Fuge zur Kappe).
        const a = Math.max(tLo, lo - step / 2), b = Math.min(tHi, hi + step / 2);
        if (b - a > S.sw) {
          const cc = add(add(c0, mul(d, (a + b) / 2)), mul(n, across));
          greenCand.push({ list: 'median',
                           q: rect(cc, d, n, (b - a) / 2, half), shift: 0 });
        }
        lo = null;
      };
      for (const t of probes) {
        const c = add(add(c0, mul(d, t)), mul(n, across));
        if (!rectClear(c, d, n, step / 2, half, site, S.es)) { flush(); continue; }
        if (lo === null) lo = t;
        hi = t;
      }
      flush();
    }

  let part = site;
  // Nur wenn  wirklich geschnitten hat, begrenzt das Teilstueck die
  // Abschnitte. Bei EINEM Teilstueck ist , und dessen Kanten
  // liegen ausserhalb des Rings - sie duerfen dort nichts abschneiden.
  let cutEdges = false;
  // WIDERLEGT: die Abstandsregel nur bei konkaven Polygonen anzuwenden.
  // Die Ueberlegung war "konvexer Ring, da kann nichts laengs danebenlaufen" -
  // falsch. Auch ein konvexes Polygon hat gerade Kanten. Gemessen holte sich
  // das Parallelogramm ohne die Regel +4 Buchten und dafuer **113 m**
  // Parallellauf. Die Regel gilt fuer ALLE Formen.
  const greenCand = [];

  /**
   * Passt eine Bucht hierhin? Der Gruenstreifen zur Randstrasse gilt QUER zur
   * Reihe, nicht laengs: laengs uebernimmt die Kapsel dieselbe Aufgabe und ist
   * selbst gruen. `interiorClear` wirkte ueber `rectClear` in alle Richtungen
   * gleich und zog den Streifen deshalb doppelt ab - gemessen fiel dadurch am
   * Anfang und am Ende JEDES Abschnitts genau eine Bucht weg (Rechteck 50
   * geplant, 41 gesetzt), und zwischen Kappe und erster Bucht klaffte eine
   * Buchtbreite.
   */
  function bayFits(c, d, n, side) {
    // 1. Im Areal, mit Randabstand.
    if (!rectClear(c, d, n, S.sw / 2, rowDepth / 2, site, S.es)) return false;
    // 2. Vorrangregel fuer das Fahrgassenraster: keine seiner Buchten auf oder
    //    innerhalb der Randstrasse. Gemessen gegen den RING, nicht gegen die
    //    Grundstueckskante - an den Ringecken
    //    fallen die beiden auseinander, und dort rutschten Buchten bis auf
    //    1,40 m an die Randstrasse heran.
    if (!rectClear(c, d, n, S.sw / 2, rowDepth / 2, ring, S.ai / 2)) return false;
    // 3. Der Gruenstreifen gehoert HINTER den Ruecken, nicht rundherum: laengs
    //    der Reihe uebernimmt die Kapsel dieselbe Aufgabe. Als isotroper Test
    //    () hat er 26 Buchten verworfen, davon 12 in EINER
    //    Reihe - die Luecke sah aus, als waere die Fahrgasse verschoben.
    const back = add(c, mul(n, side * (rowDepth / 2 + S.md)));
    return insideBy(back, site, roadClear) && insideBy(back, ring, S.ai / 2);
  }

  /**
   * Rueckfall fuer einen Teilbereich ohne Vollmodul: dieselbe Gasse darf nur
   * die Seite behalten, deren Buchten und voller Gruenstreifen einzeln passen.
   * Verbindungen bleiben dabei aus; eine einzelne Gasse erreicht den Ring
   * bereits an beiden Enden.
   */
  function pass(deg, phase, collect, halfFallback = false) {
    const rad = deg * Math.PI / 180;
    const d = [Math.cos(rad), Math.sin(rad)], n = [-d[1], d[0]];
    const kMax = Math.ceil(span / module) + 1;

    // 1. Durchgang: wo laeuft welche Reihe, und liegt die Fahrgasse weit genug
    //    von der Randstrasse? Ohne diese Bedingung schob sich eine Fahrgasse
    //    direkt an den Ring.
    const runs = [];
    for (let k = -kMax; k <= kMax; k++) {
      const across = k * module + phase;
      let lo = null, hi = null, count = 0;
      for (const side of [-1, 1]) {
        const bandC = across + side * (S.ai / 2 + rowDepth / 2);
        for (let t = -span; t <= span; t += S.sw) {
          const c = add(add(c0, mul(d, t)), mul(n, bandC));
          // Hier bewusst das strengere `interiorClear`: dieser Durchgang
          // entscheidet, WO eine Reihe liegen darf. Mit dem gelockerten Mass
          // taucht an fast jedem Winkel eine Ein-Gassen-Loesung auf, und die
          // Bewertung nach minimalen Konfliktpunkten waehlt sie dann immer -
          // gemessen Rechteck 154 -> 121 Buchten. Gelockert wird erst beim
          // Setzen der einzelnen Bucht, siehe `bayFits`.
          if (!rectClear(c, d, n, S.sw / 2, rowDepth / 2, site, interiorClear)) continue;
          if (!rectClear(c, d, n, S.sw / 2, rowDepth / 2, part, 0)) continue;
          count++;
          if (lo === null || t < lo) lo = t;
          if (hi === null || t > hi) hi = t;
        }
      }
      if (count === 0) continue;
      // Abstand zur Randstrasse gegen den RING messen, nicht gegen die
      // Grundstueckskante: in einer Kerbe liefert die Kante den Abstand zur
      // falschen Seite. Geprueft wird nur der innere Teil der Reihe - an ihren
      // Enden trifft sie den Ring absichtlich.
      // Nutzerregel: parallel laufende Fahrbahnen brauchen Abstand. Zwischen
      // Randstrasse und Fahrgasse muss eine Buchtenreihe samt Gruenstreifen
      // Platz haben - sonst liegen zwei Strassen aneinander und der Streifen
      // dazwischen ist unbrauchbar. Der alte Test verlangte nur
      // `Mittellinienabstand >= ai`, also exakt "Raender beruehren sich":
      // gemessen liefen L-Form 11 m und Referenz 20 m mit 0,04 bzw. 0,13 m
      // Rand-zu-Rand nebeneinander.
      //
      // Gemessen wird NUR gegen annaehernd parallele Ringsegmente. Gegen den
      // ganzen Ring gepruegt wuerde jede Einmuendung anschlagen - dort trifft
      // die Gasse die Randstrasse absichtlich, aber rechtwinklig.
      // Ueber die GANZE Fahrgasse pruefen, nicht nur ueber ihren Buchtenteil.
      // Vor der ersten Bucht laeuft sie oft noch ein Stueck weiter - und genau
      // dort lag die letzte enge Stelle (Browser-Fall, 9 m bei 0,16 m).
      const rcT = lineCrossings(add(c0, mul(n, across)), d, ring);
      // MINDESTWINKEL an der Einmuendung. Trifft die Fahrgasse die Randstrasse
      // zu spitz, laeuft sie `(ai/2)/sin(Winkel)` weit IN ihr entlang - bei 25
      // Grad gemessene 10,5 m. In CS2 duerfen sich zwei Strassen aber nicht
      // ueberlagern. `dn` ist genau dieser Sinus.
      if (rcT.length >= 2
          && (rcT[0].dnRaw < MIN_JUNCTION_SIN
              || rcT[rcT.length - 1].dnRaw < MIN_JUNCTION_SIN)) continue;
      const tA = rcT.length >= 2 ? Math.min(lo, rcT[0].t) : lo;
      const tB = rcT.length >= 2 ? Math.max(hi, rcT[rcT.length - 1].t) : hi;
      // Der Halbmodul-Rueckfall aendert nur die Zahl der Buchtenreihen, nicht
      // den Pflichtabstand zu parallelen Fahrbahnen: eine Reihe plus voller
      // Gruenstreifen muss weiterhin zwischen die Strassen passen.
      const parallelClear = S.ai + rowDepth + S.md - FIT_EPS;
      const nah = (t) => {
        const q = add(add(c0, mul(d, t)), mul(n, across));
        for (const sg of ringSegs) {
          if (Math.abs(d[0] * sg.u[0] + d[1] * sg.u[1]) < PARALLEL_COS) continue;
          const dc = sideGap(q, sg.a, sg.b);
          if (dc > MERGE_TOL && dc < parallelClear) return true;
        }
        return false;
      };
      // KAPPEN statt verwerfen. Die Gasse ganz zu streichen kostete im
      // Browser-Fall 40 Buchten, obwohl nur 9 von 71 m eng lagen. Gesucht ist
      // das laengste Stueck, das den Abstand einhaelt; die Enden bleiben frei,
      // dort muendet die Gasse absichtlich in die Randstrasse.
      // Wo laeuft die Gasse zu nah an einer parallelen Strasse? Die ersten und
      // letzten `ai` sind ausgenommen, dort muendet sie ein.
      let engA = null, engB = null, mitteEng = false;
      for (let t = tA; t <= tB + 1e-9; t += S.sw) {
        if (t < tA + S.ai || t > tB - S.ai || !nah(t)) continue;
        if (engA === null) engA = t;
        engB = t;
      }
      if (engA !== null) {
        // Beruehrt das enge Stueck ein ENDE, muss die Gasse trotzdem
        // hindurch - sonst endet sie im Feld. Genau das war der Fall an der
        // schraegen Unterkante der Referenz: 9 m lang bis zu 1,35 m zu nah,
        // und die Gasse blieb 9,75 m vor der Randstrasse stehen. Ueberlappt
        // wird dabei nichts, der Gruenstreifen dazwischen wird nur schmaler.
        // Liegt das enge Stueck dagegen MITTENDRIN, liefe sie wirklich neben
        // einer Strasse her - dann faellt der ganze Lauf weg, denn Kappen
        // wuerde ihn in zwei Sackgassen zerlegen.
        const amRand = engA <= tA + 2 * S.ai || engB >= tB - 2 * S.ai;
        if (!amRand || engB - engA > MAX_PASS * S.ai) { mitteEng = true; }
      }
      if (mitteEng) continue;
      const bestA = tA, bestB = tB;
      if (bestB - bestA < S.ai + 3 * S.sw) continue;
      runs.push({ across, lo: Math.max(lo, bestA), hi: Math.min(hi, bestB),
                  clipA: bestA, clipB: bestB });
    }
    if (runs.length === 0) return { count: 0, aisles: 0 };

    // Verbindungsstrassen NUR im Inneren. An den Reihenenden erledigt das die
    // Randstrasse - dort gesetzt lagen sie frueher auf ihr drauf.
    const gLo = Math.min(...runs.map((r) => r.lo));
    const gHi = Math.max(...runs.map((r) => r.hi));
    const crossTargets = [];
    const inner = halfFallback ? 0
      : Math.floor((gHi - gLo) / Math.max(S.cr, S.sw * 4));
    for (let j = 1; j <= inner; j++)
      crossTargets.push(gLo + (gHi - gLo) * j / (inner + 1));
    let crossAt = crossTargets.slice();

    /**
     * Die Grenzen eines Abschnitts: Strassenmitte zu Strassenmitte (t0/t1) und
     * der jeweilige Fahrbahnrand (e0/e1), laengs der Reihe gemessen.
     */
    /** `clipA/clipB` begrenzen die Gasse dort, wo sie sonst neben der
     *  Randstrasse herliefe - das sind keine Strassen, also kein Abzug. */
    function sectionFrame(across, clipA, clipB) {
      const base = add(c0, mul(n, across));
      const cuts = lineCrossings(base, d, ring);
      if (cuts.length < 2) return null;
      const first = cuts[0].t, last = cuts[cuts.length - 1].t;
      const fixed = cuts.map((c) => ({ ...c, width: S.ai, kind: "ring" }))
        // Schnittkanten der Zerlegung sind KEINE Strassen, begrenzen den
        // Abschnitt aber genauso. Ohne sie spannt der Abschnitt ueber das ganze
        // Areal, waehrend die Buchten nur ins Teilstueck duerfen - bei der
        // L-Form wurden so 64 Buchten geplant und nur 26 gesetzt.
        .concat(cutEdges
          ? lineCrossings(base, d, part).map((c) => ({ ...c, width: 0, kind: "cut" }))
          : [])
        .concat(clipA === undefined ? []
          : [{ t: clipA, dn: 1, width: 0, kind: "clip" },
             { t: clipB, dn: 1, width: 0, kind: "clip" }]);
      return { base, first, last, fixed, clipA, clipB };
    }
    function boundsFromFrame(frame, crosses) {
      if (!frame) return [];
      const { base, first, last, fixed, clipA, clipB } = frame;
      // Verbindungsstrassen laufen senkrecht zur Reihe, dort ist dn = 1.
      const marks = fixed.concat(
        crosses.filter((t) => t > first + 0.01 && t < last - 0.01)
          .map((t) => ({ t, dn: 1, width: crossWidth, kind: "cross" })))
        .sort((a, b) => a.t - b.t);
      const secs = [];
      for (let i = 0; i + 1 < marks.length; i++) {
        const m0 = marks[i], m1 = marks[i + 1];
        const mid = add(base, mul(d, (m0.t + m1.t) / 2));
        // Zwischen zwei Ringkanten kann auch Aussenraum liegen (Kerbe).
        if (!pointIn(mid, ring)) continue;
        if (cutEdges && !pointIn(mid, part)) continue;
        if (clipA !== undefined
            && ((m0.t + m1.t) / 2 < clipA || (m0.t + m1.t) / 2 > clipB)) continue;
        // Nur an einer Strasse wird die halbe Fahrbahnbreite abgezogen.
        const e0 = m0.t + m0.width / 2 / m0.dn;
        const e1 = m1.t - m1.width / 2 / m1.dn;
        // Auch zu kurze Abschnitte bleiben in der Liste: sie tragen keine
        // Buchten, die Fahrgasse muss aber trotzdem durch sie hindurch bis zur
        // Randstrasse laufen. Sonst beginnt sie erst hinter dem Loch und endet
        // frei im Feld - gemessen 15,67 m vor dem Ring, also ein totes Ende.
        secs.push({ t0: m0.t, t1: m1.t, dn0: m0.dn, dn1: m1.dn,
                    end0: m0.kind, end1: m1.kind, e0, e1, L: e1 - e0 });
      }
      return secs;
    }
    function sectionBounds(across, clipA, clipB) {
      return boundsFromFrame(sectionFrame(across, clipA, clipB), crossAt);
    }

    /**
     * Buchten eines Abschnitts auf EIN gemeinsames Laengsraster setzen:
     *     Strasse | Kapsel | Bucht Bucht ... Bucht | Kapsel | Strasse
     * Die erste Bucht ist der erste Rasterplatz, der mindestens eine Buchtbreite
     * hinter dem Fahrbahnrand liegt; die Kapsel ist der Rest davor und misst
     * damit immer >= 1 und < 2 Buchtbreiten - genau die Regel.
     *
     * Warum GEMEINSAM statt je Abschnitt zentriert: zentriert bekommt jeder
     * Abschnitt seine eigene Phase, weil jede Fahrgasse den Ring woanders
     * trifft. Gemessen an Referenz 08s standen die Reihen beiderseits des
     * Gruenstreifens dadurch 1,08 m gegeneinander versetzt - fast eine halbe
     * Bucht. Die beiden Kapseln eines Abschnitts sind dafuer nicht mehr
     * zwangslaeufig gleich lang.
     */
    function layoutSection(b, gridPhase) {
      const i0 = Math.ceil((b.e0 + S.sw - gridPhase) / S.sw - 1e-9);
      const i1 = Math.floor((b.e1 - S.sw - gridPhase) / S.sw + 1e-9);
      if (i1 - i0 < 1) return null;
      const bayLo = gridPhase + i0 * S.sw, bayHi = gridPhase + i1 * S.sw;
      // `b` kann vorher auf die tatsaechliche Fahrgassenspannweite geklemmt
      // worden sein. Dann gehoert L zu diesen Grenzen, nicht zum Quellabschnitt.
      return { ...b, L: b.e1 - b.e0, bayLo, bayHi, bays: i1 - i0,
               cap0: bayLo - b.e0, cap1: b.e1 - bayHi };
    }
    /** Buchtenzahl und exakt rasterbreite Kapseln einer Phase. */
    function scorePhase(all, gridPhase) {
      let bays = 0, exact = 0;
      for (const b of all) {
        const s = layoutSection(b, gridPhase);
        if (!s) continue;
        bays += s.bays;
        if (Math.abs(s.cap0 - S.sw) <= 1e-6) exact++;
        if (Math.abs(s.cap1 - S.sw) <= 1e-6) exact++;
      }
      return { bays, exact };
    }

    // Passt ein Gruenstreifen an seiner Sollposition nicht, wandert die
    // FAHRGASSE nach innen, bis er passt - und die Buchten mit ihr. Vorher
    // wanderte nur der Streifen und geriet aus der Flucht seiner Reihe.
    if (collect)
      for (const r of runs) {
        for (const side of [-1, 1]) {
          if (runs.some((o) => o !== r
              && Math.abs(o.across - (r.across + side * module)) < 0.5)) continue;
          const need = side * (S.ai / 2 + rowDepth + S.md / 2);
          let best = null;
          for (let sh = 0; sh <= S.md * 2 + 0.01; sh += 0.25) {
            const a = r.across - side * sh;
            const c = add(add(c0, mul(d, (r.lo + r.hi) / 2)), mul(n, a + need));
            if (!rectClear(c, d, n, Math.max(S.sw, (r.hi - r.lo) / 2),
                           S.md / 2, site, S.es)) continue;
            best = sh; break;
          }
          if (best !== null && best > 0) { r.across -= side * best; out.shifted++; }
        }
      }

    // EIN Laengsraster fuer alle Reihen dieses Durchgangs. Die Phase wird so
    // gewaehlt, dass ueber alle Abschnitte zusammen die meisten Buchten
    // herauskommen - ein festes t=0 verschenkt je nach Zuschnitt eine ganze
    // Bucht pro Abschnitt.
    // Grenzen JE REIHE, nicht je Fahrgasse. Die Sektion entsteht aus den
    // Ringschnitten einer Linie; die beiden Buchtenreihen liegen aber
    // `ai/2 + sl/2` neben der Gassenmitte. Bei schraegem Rand hat eine Seite
    // deutlich mehr Platz als die Mittellinie hergibt - gemessen an Referenz
    // 08s passten 22 weitere Buchten auf die BESTEHENDEN Reihen, allein weil
    // die Mittellinie sie zu frueh abschnitt.
    // Ring- und Teilflaechenschnitte sind fuer jede Rasterphase gleich. Nur die
    // Querstrassen wandern; deshalb einmal vorrechnen und in der Phasensuche
    // lediglich deren Marken einsetzen.
    /**
     * Die Kerbenreihe liegt nicht in runs. Steht eine spaetere Reihe genau
     * hinter ihrem Mittelstreifen, erbt NUR diese Seite deren Laengsraster.
     * Die Reihe auf der anderen Seite derselben Fahrgasse behaelt ihr Raster.
     */
    const notchForRow = (r, side) => {
      const bandC = r.across + side * (S.ai / 2 + rowDepth / 2);
      const rowPoint = add(c0, mul(n, bandC));
      for (const z of notchRows) {
        if (Math.abs(d[0] * z.u[0] + d[1] * z.u[1]) < 1 - 1e-6) continue;
        const gap = (rowPoint[0] - z.rowPoint[0]) * z.back[0]
                  + (rowPoint[1] - z.rowPoint[1]) * z.back[1];
        if (Math.abs(gap - (rowDepth + S.md)) > 0.05) continue;
        const za = (z.loPoint[0] - c0[0]) * d[0] + (z.loPoint[1] - c0[1]) * d[1];
        const zb = (z.hiPoint[0] - c0[0]) * d[0] + (z.hiPoint[1] - c0[1]) * d[1];
        if (Math.min(r.hi, Math.max(za, zb)) - Math.max(r.lo, Math.min(za, zb))
            >= S.sw - FIT_EPS) return z;
      }
      return null;
    };
    const notchGrid = (z) => (z.loPoint[0] - c0[0]) * d[0]
                           + (z.loPoint[1] - c0[1]) * d[1];
    for (const r of runs) {
      r.frame = sectionFrame(r.across, r.clipA, r.clipB);
      r.sideFrames = {}; r.notchMate = {};
      for (const side of [-1, 1]) {
        r.sideFrames[side] = sectionFrame(
          r.across + side * (S.ai / 2 + rowDepth / 2), r.clipA, r.clipB);
        r.notchMate[side] = notchForRow(r, side);
      }
    }
    /**
     * Die erste Querstrassenkante liegt auf dem gemeinsamen Buchtenraster.
     * Ist `cw` ein ganzzahliges Vielfaches von `sw` (im CS2-Satz 3/3), gilt
     * das automatisch auch fuer die zweite Kante. Jede Querstrasse wandert
     * hoechstens eine halbe Buchtbreite von ihrer gleichmaessigen Sollposition.
     */
    const crossesForPhase = (gridPhase) => {
      const origin = gridPhase + crossWidth / 2;
      const snapped = [];
      for (const target of crossTargets) {
        const t = origin + Math.round((target - origin) / S.sw) * S.sw;
        if (!snapped.length || Math.abs(t - snapped[snapped.length - 1]) > 1e-6)
          snapped.push(t);
      }
      return snapped;
    };
    let grid = 0, bestBays = -1, bestExact = -1, bestCrossAt = [];
    for (let k = 0; k < 20; k++) {
      const p = k * S.sw / 20;
      const crosses = crossesForPhase(p);
      // Schon die Kandidatensuche muss den festen Kerben-Rasterverlust sehen.
      // Erst im Sammellauf umzustellen wuerde den falschen Sieger waehlen.
      const got = { bays: 0, exact: 0 };
      for (const r of runs) for (const side of [-1, 1]) {
        const one = scorePhase(boundsFromFrame(r.sideFrames[side], crosses),
          r.notchMate[side] ? notchGrid(r.notchMate[side]) : p);
        got.bays += one.bays; got.exact += one.exact;
      }
      // Buchten bleiben das Hauptziel. Bei Gleichstand nutzt die freie Phase
      // moeglichst viele ebenfalls rastertreffende Rand-/Schnittkanten.
      if (got.bays > bestBays || (got.bays === bestBays && got.exact > bestExact)) {
        bestBays = got.bays; bestExact = got.exact; grid = p; bestCrossAt = crosses;
      }
    }
    crossAt = bestCrossAt;
    for (const r of runs) {
      r.bounds = boundsFromFrame(r.frame, crossAt);
      r.sideBounds = {}; r.sideGrid = {};
      for (const side of [-1, 1]) {
        r.sideBounds[side] = boundsFromFrame(r.sideFrames[side], crossAt);
        r.sideGrid[side] = r.notchMate[side] ? notchGrid(r.notchMate[side]) : grid;
      }
    }

    let total = 0, aisles = 0;
    for (const r of runs) {
      // Eine Buchtenreihe darf NICHT ueber ihre Fahrgasse hinausragen. Die
      // Reihe rechnet ihre Ausdehnung aus ihrem EIGENEN Ringschnitt; bei
      // schraegem Rand liegt der weiter aussen als der der Gassenmitte, und
      // seit die Gasse am Fahrbahnrand endet, hingen dort Buchten ohne Fahrweg
      // daneben (gemessen 2 Stueck, 1,8 bis 2,1 m vor dem Gassenanfang).
      // Gegen die TATSAECHLICHE Gassenlinie klammern, nicht gegen die
      // rechnerische Spannweite: `clipSegment` kuerzt sie danach noch (laengster
      // zusammenhaengender Lauf, 0,25-m-Schritte, Ende am Fahrbahnrand).
      r.seg = null;
      let spanA = -Infinity, spanB = Infinity;
      if (r.bounds.length) {
        const rcS = lineCrossings(add(c0, mul(n, r.across)), d, ring);
        const sA = Math.max(rcS.length ? rcS[0].t : r.clipA, r.clipA);
        const sB = Math.min(rcS.length ? rcS[rcS.length - 1].t : r.clipB, r.clipB);
        // Der exakte Eckabzug unten setzt genau zwei Ringschnitte voraus.
        // Komplexere konkave Laeufe bleiben deshalb beim bewaehrten Vollmodul.
        if (halfFallback && rcS.length !== 2) continue;
        if (rcS.length >= 2 && sB - sA >= S.sw) {
          if (halfFallback) {
            // Nicht nur die Mittellinie, auch die aeussere Fahrgassenecke muss
            // an der KANTE der hoeher rangierenden Ringstrasse enden.
            const roadBack = (c) => S.ai / 2
              * (1 + Math.sqrt(Math.max(0, 1 - c.dnRaw * c.dnRaw))) / c.dnRaw;
            const a = Math.max(sA, rcS[0].t + roadBack(rcS[0]));
            const b = Math.min(sB, rcS[1].t - roadBack(rcS[1]));
            if (b - a >= S.sw)
              r.seg = [add(add(c0, mul(d, a)), mul(n, r.across)),
                       add(add(c0, mul(d, b)), mul(n, r.across))];
          } else {
            r.seg = clipSegment(
              add(add(c0, mul(d, sA)), mul(n, r.across)),
              add(add(c0, mul(d, sB)), mul(n, r.across)),
              ring, S.ai / 2);
          }
        }
        // Eine Gasse aus einem Teilbereich darf am Rand einer bereits
        // erreichten Eckgasse enden. Sonst laeuft sie ring-zu-ring durch den
        // naechsten Teilbereich und verdraengt dort dessen Buchten.
        //
        // Gemessen an der L-Form: die Gasse bei x = 33,26 trifft die Eckgasse
        // schon bei y = 35, lief aber noch 41,5 m weiter durch den oberen
        // Schenkel und kreuzte dort 8 Buchten des 25-Grad-Rasters. Dieselbe
        // Regel wie bei den anderen Fahrbahnen: der rangniedrigere Weg endet
        // an der KANTE des ranghoeheren, nicht an dessen Mittellinie.
        if (r.seg && out.notchAisles > 0) {
          const v = sub(r.seg[1], r.seg[0]), L = len(v), u = mul(v, 1 / L);
          const aIn = pointIn(add(r.seg[0], mul(u, 0.1)), part);
          const bIn = pointIn(add(r.seg[1], mul(u, -0.1)), part);
          if (aIn !== bIn) {
            const anchor = aIn ? r.seg[0] : r.seg[1];
            const toward = aIn ? u : mul(u, -1);
            let stop = L;
            for (const [p0, p1] of out.aisleLine.slice(0, out.notchAisles)) {
              const pv = sub(p1, p0), pL = len(pv);
              if (pL < 0.2) continue;
              const pu = mul(pv, 1 / pL);
              const hit = lineIntersect(anchor, toward, p0, pu);
              if (!hit) continue;
              const t = (hit[0] - anchor[0]) * toward[0]
                      + (hit[1] - anchor[1]) * toward[1];
              const along = ((hit[0] - p0[0]) * pv[0] + (hit[1] - p0[1]) * pv[1])
                          / (pL * pL);
              const dn = Math.abs(toward[0] * pu[1] - toward[1] * pu[0]);
              if (t <= S.ai / 2 || t >= stop || along < -1e-9 || along > 1 + 1e-9
                  || dn < MIN_JUNCTION_SIN) continue;
              stop = t - S.ai / 2 / dn;
            }
            if (stop < L) {
              const end = add(anchor, mul(toward, stop));
              r.seg = aIn ? [anchor, end] : [end, anchor];
            }
          }
        }
        if (r.seg) {
          const proj = (p2) => (p2[0] - c0[0]) * d[0] + (p2[1] - c0[1]) * d[1];
          spanA = Math.min(proj(r.seg[0]), proj(r.seg[1]));
          spanB = Math.max(proj(r.seg[0]), proj(r.seg[1]));
        } else {
          spanA = r.bounds[0].e0;
          spanB = r.bounds[r.bounds.length - 1].e1;
        }
      }
      // Eine spaetere Gasse darf das bereits gesetzte Kerbengruen nicht
      // zerschneiden. Als harte Sperrflaeche kann die Phasensuche die Gasse
      // noch verschieben; gibt es keine saubere Phase, entfaellt dieser Lauf.
      if (r.seg) {
        const rv = sub(r.seg[1], r.seg[0]), rL = len(rv), ru = mul(rv, 1 / rL);
        const rn = [-ru[1], ru[0]];
        const road = rect(mul(add(r.seg[0], r.seg[1]), 0.5),
                          ru, rn, rL / 2, S.ai / 2);
        if ([...notchGreens, ...notchCaps]
            .some((q) => quadsOverlap(road, q, 0.05))) continue;
      }
      r.secs = {};
      for (const side of [-1, 1])
        r.secs[side] = r.sideBounds[side]
          .map((b) => layoutSection({ ...b, e0: Math.max(b.e0, spanA - (0)),
                                      e1: Math.min(b.e1, spanB + (0)) }, r.sideGrid[side]))
          .filter(Boolean);
      if (!r.secs[-1].length && !r.secs[1].length) continue;
      let used = false;
      for (const side of [-1, 1])
        for (const sec of r.secs[side]) {
          const bandC = r.across + side * (S.ai / 2 + rowDepth / 2);
          for (let i = 0; i < sec.bays; i++) {
            const c = add(add(c0, mul(d, sec.bayLo + (i + 0.5) * S.sw)), mul(n, bandC));
            if (!bayFits(c, d, n, side)) continue;
            // Der Abschnitt kommt vom Ring des GANZEN Areals - ohne diese
            // Schranke griffe ein Teilbereich in den naechsten hinueber.
            if (!rectClear(c, d, n, S.sw / 2, rowDepth / 2, part, 0)) continue;
            const q = rect(c, d, n, S.sw / 2, rowDepth / 2);
            // Schon die Kandidatensuche muss die vorab gesetzten Kerbenreihen
            // und ihre Kappen sehen. Sonst bewertet sie Buchten, die der
            // Sammellauf wegen `occupied` niemals setzen kann.
            if (occupied.hits(q)) continue;
            if (collect) {
              occupied.add(q);
              out.bay.push(q); out.bayKind.push("innen");
            }
            total++; used = true;
          }
        }
      if (!used) continue;
      r.used = true;
      aisles++;
      if (!collect) continue;
      // Die Abschnittsrechnung mitschreiben, damit sie sich nachmessen laesst
      // statt geglaubt werden zu muessen: 2*Kapsel + Buchten*sw muss L ergeben.
      for (const side of [-1, 1])
        for (const sec of r.secs[side])
          out.sections.push({ L: sec.L, bays: sec.bays,
                              cap0: sec.cap0, cap1: sec.cap1,
                              end0: sec.end0, end1: sec.end1 });
      // Die Fahrgasse laeuft von Randstrasse zu Randstrasse. Ihre Enden sind
      // jetzt die Schnittpunkte selbst - kein Verlaengern von der letzten Bucht
      // aus mehr noetig.
      // Gegen den RING schneiden, nicht gegen den Kantenabstand. Die Enden sind
      // exakte Ringschnittpunkte; der Ring weicht durch die Gehrungs-Deckelung
      // aber vom reinen Abstandsort ab, und der Abstandstest kappte die Gasse
      // deshalb weit vor der Randstrasse - gemessen bis 15,67 m, also ein
      // totes Ende. Der Ring liegt selbst im Areal, die Strasse bleibt drin.
      // Spannweite aus den RINGschnitten, nicht aus : dort stecken
      // seit der Zerlegungs-Begrenzung auch die Schnittkanten. Die begrenzen
      // die Buchten, aber nicht die Fahrbahn - sonst endet die Gasse mitten im
      // Areal an einer gedachten Linie (gemessen 10,56 m vor der Randstrasse).
      if (r.seg) {
        out.aisleLine.push(r.seg);
        const along0 = axisCoord(c0, d);
        aisleGrids.push({
          [-1]: makeLongGrid(d, along0 + r.sideGrid[-1]),
          [1]: makeLongGrid(d, along0 + r.sideGrid[1]),
        });
      }
    }
    if (collect) {
      // Motorhaube an Motorhaube: die Kappe verbindet die beiden Reihen, die
      // sich ueber den Mittelstreifen gegenueberstehen - nicht die beiden
      // Reihen derselben Fahrgasse. Autos fahren vorwaerts ein, die Front zeigt
      // vom Fahrweg weg.
      const capHalf = rowDepth + S.md / 2;
      const shared = (a, b) => Math.min(a.bayHi, b.bayHi) - Math.max(a.bayLo, b.bayLo);
      /** Der Abschnitt aus `list`, der sich am weitesten mit `s` deckt. */
      const matching = (list, s) => {
        let best = null;
        for (const o of list) if (shared(o, s) > (best ? shared(best, s) : 0)) best = o;
        return best;
      };
      // Nur gleiche Laengsausdehnung ergibt zwei rechteckige Doppelreihenkappen.
      const pairedNotch = (z, sec) => {
        if (!z) return false;
        const za = (z.loPoint[0] - c0[0]) * d[0] + (z.loPoint[1] - c0[1]) * d[1];
        const zb = (z.hiPoint[0] - c0[0]) * d[0] + (z.hiPoint[1] - c0[1]) * d[1];
        return Math.abs(Math.min(za, zb) - sec.bayLo) <= FIT_EPS
            && Math.abs(Math.max(za, zb) - sec.bayHi) <= FIT_EPS;
      };
      /**
       * Kappe je Abschnittsende: vom Fahrbahnrand bis BUENDIG an die erste
       * Bucht. Ihre Laenge ist damit die gerechnete Kapsel - kein Anpassen per
       * Modulo mehr, und keine Fuge zwischen Kappe und Strasse.
       */
      const emitCaps = (sec, lo, hi, across, half, guard, notchMate = null) => {
        // Die Kappe ist ein Rechteck, die Strasse trifft sie schraeg: ihre
        // AEUSSERE Ecke kommt der Fahrbahn naeher als ihre Mitte. Ohne dieses
        // Aufmass ragte die Ecke in die Fahrbahn und der Ueberlappungstest warf
        // die ganze Kappe weg - gemessen 7 verworfene Gruenstuecke beim
        // Rechteck, 5 von 10 Streifenenden standen dadurch ohne Kappe da.
        // An einer Querstrasse beginnt die Kappe an deren 3-m-Fahrbahnrand;
        // Ring-, Schnitt- und Clip-Enden behalten das bisherige ai-Aufmass.
        const widthAt = (kind) => kind === "cross" ? crossWidth : S.ai;
        const back = (width, dn) =>
          (width / 2 + half * Math.sqrt(Math.max(0, 1 - dn * dn))) / dn;
        for (const [a, b] of [[sec.t0 + back(widthAt(sec.end0), sec.dn0), lo],
                              [hi, sec.t1 - back(widthAt(sec.end1), sec.dn1)]]) {
          if (b - a < 0.3) continue;
          const cc = add(add(c0, mul(d, (a + b) / 2)), mul(n, across));
          if (guard && !rectClear(cc, d, n, (b - a) / 2, half, site, S.es)) continue;
          greenCand.push({ list: 'cap',
                           q: rect(cc, d, n, (b - a) / 2, half), shift: 0,
                           notchMate });
        }
      };
      for (let i = 0; i + 1 < runs.length; i++) {
        const r1 = runs[i], r2 = runs[i + 1];
        if (Math.abs(r2.across - r1.across - module) > 0.5) continue;
        if (!r1.secs || !r2.secs) continue;
        // r1 blickt mit +1 zu r2, r2 mit -1 zurueck - das sind die beiden
        // Reihen, die Motorhaube an Motorhaube stehen.
        const rows1 = r1.secs[1], rows2 = r2.secs[-1];
        const medAcross = (r1.across + r2.across) / 2;
        // Die Kappe sitzt auf dem Mittelstreifen, also zaehlen DESSEN
        // Schnittpunkte mit der Randstrasse, nicht die der Fahrgasse.
        const medSecs = sectionBounds(medAcross, Math.max(r1.clipA, r2.clipA),
          Math.min(r1.clipB, r2.clipB))
          .map((x) => layoutSection(x, grid)).filter(Boolean);
        for (const s1 of rows1) {
          const s2 = matching(rows2, s1);
          if (!s2) continue;
          // VEREINIGUNG, nicht Schnitt: die Kappe reicht bis zur ersten Bucht
          // IRGENDEINER der beiden Reihen. Mit dem Schnitt (max/min) deckte sie
          // die vorderen Buchten der frueher beginnenden Reihe mit ab und wurde
          // dafuer komplett verworfen - gemessen 4 von 6 Kappen beim Rechteck,
          // und genau dort stand das Streifenende dann ohne Kappe da.
          const lo = Math.min(s1.bayLo, s2.bayLo);
          const hi = Math.max(s1.bayHi, s2.bayHi);
          if (hi - lo < S.sw) continue;
          emitCaps(matching(medSecs, s1) || s1, lo, hi, medAcross, capHalf, false);
          // Gruenstreifen zwischen den beiden Reihen, die Motorhaube an
          // Motorhaube stehen. Er haelt exakt die eingestellte Streifentiefe -
          // anders als die Kappe, die von Fahrweg zu Fahrweg reicht.
          if (S.md > 0.05) emitStrip(d, n, lo, hi, medAcross);
        }
      }
      // Reihen OHNE Gegenreihe: sie stehen nicht Motorhaube an Motorhaube,
      // bekommen aber genauso Kappen und einen Gruenstreifen hinter sich. Der
      // Streifen haelt das eingestellte Mass, die Kappe reicht vom Fahrweg bis
      // hinter den Streifen.
      for (const r of runs) {
        if (!r.secs) continue;
        for (const side of [-1, 1]) {
          if (runs.some((o) => Math.abs(o.across - (r.across + side * module)) < 0.5))
            continue;
          const stripAcross = r.across + side * (S.ai / 2 + rowDepth + S.md / 2);
          const capAcross = r.across + side * (S.ai / 2 + (rowDepth + S.md) / 2);
          const capHalfSingle = (rowDepth + S.md) / 2;
          for (const sec of r.secs[side]) {
            const mate = pairedNotch(r.notchMate[side], sec) ? r.notchMate[side] : null;
            emitCaps(sec, sec.bayLo, sec.bayHi, capAcross, capHalfSingle, true, mate);
            // Der vorhandene Kerbenstreifen liegt bereits zwischen beiden Reihen.
            if (S.md > 0.05 && !mate)
              emitStrip(d, n, sec.bayLo, sec.bayHi, stripAcross);
          }
        }
      }
      for (const ct of crossAt) {
        // Von Ring zu Ring durchziehen, nicht nur von Gasse zu Gasse.
        //
        // Der Rohstrich geht ueber die GANZE Spanne. Vorher endete er ein
        // Modul hinter der aeussersten Fahrgasse - und das ist nicht dasselbe:
        // beim Rechteck lag die erste Gasse bei 34,75, ein Modul davor also
        // 14,25, waehrend die Randstrasse erst bei 13,50 aufhoert. Die
        // Verbindung begann damit 0,75 m VOR der Randstrasse und war gar nicht
        // angeschlossen. Der exakte Zuschnitt weiter unten konnte das nicht
        // heilen: es gab nichts abzuschneiden. Gemessen vier Restfuellungs-
        // schnipsel von 3 x 0,75 m, je einer an jedem Ende.
        const seg = clipSegment(add(add(c0, mul(d, ct)), mul(n, -span)),
                                add(add(c0, mul(d, ct)), mul(n, span)),
                                site, ringDist);
        if (seg) out.crossLine.push(seg);
      }
    }
    // Eine Fahrgasse, die genau die erste Bucht hinter einer Kerbenkappe
    // schneidet, laesst eine formal vorhandene Kappe ohne Nachbarbucht zurueck.
    // Das muss schon die Kandidatenauswahl sehen; erst im Sammellauf zu filtern
    // waehlte bei der L-Form reproduzierbar die falsche Feinphase (1,55 m Fuge).
    const cut = new Set();
    for (const r of runs) {
      if (!r.used || !r.seg) continue;
      const v = sub(r.seg[1], r.seg[0]), Lv = len(v);
      if (Lv < 0.2) continue;
      const u = mul(v, 1 / Lv), nn = [-u[1] * S.ai / 2, u[0] * S.ai / 2];
      const road = [add(r.seg[0], nn), add(r.seg[1], nn),
                    sub(r.seg[1], nn), sub(r.seg[0], nn)];
      for (const q of protectedNotchBays)
        if (quadsOverlap(q, road, 0.05)) cut.add(q);
    }
    return { count: total, aisles, crossings: crossAt.length, capCuts: cut.size };
  }

  // Je Teilbereich ein eigenes Raster mit eigenem Winkel und eigener Phase.
  // Zerlegen ODER nicht - beides rechnen, das Bessere gewinnt. Die Zerlegung
  // an konkaven Ecken hilft dort, wo ein Schenkel sonst leer bliebe; bei der
  // L-Form schadet sie aber: JEDER Schenkel ist quer zu schmal fuer ein Modul
  // (45 bzw. 60 m bei 40,8 m Randbedarf), waehrend durchgehende Reihen ueber
  // beide Schenkel laufen. Gemessen 138 mit Zerlegung gegen 155 ohne.
  const parts = (S.__single ? [site] : decompose(site));
  // Nur ein Rueckfall pro Gesamtlayout: der groesste Teilbereich verspricht
  // den groessten Flaechengewinn und vermeidet ein Netz aus halben Restgassen.
  const halfPart = S.__single ? null : parts.reduce((best, p) =>
    !best || Math.abs(signedArea(p)) > Math.abs(signedArea(best)) ? p : best, null);
  out.parts = parts.length;
  let firstAngle = null;
  for (const pt of parts) {
    part = pt;
    cutEdges = parts.length > 1;
    frameFromPart();
    let bestDeg = S.auto ? 0 : S.angle, bestPhase = 0, bestHalf = false;
    const degs = S.angleMode === "edge"
      ? [longestEdgeAngle(pt)]
      : (S.auto ? Array.from({ length: 36 }, (_, i) => i * 5) : [S.angle]);

    /**
     * Auswahlziel ist NICHT die groesste Buchtenzahl, sondern die kleinste Zahl
     * an Konfliktpunkten. Jede Fahrgasse muendet zweimal in die Randstrasse,
     * jede Verbindung ebenfalls, und jede Kreuzung im Inneren kommt dazu:
     *     Knoten = 2*Gassen + 2*Verbindungen + Gassen*Verbindungen
     * Nach Buchtenzahl gewaehlt schob sich immer eine Gasse zusaetzlich ins
     * Raster und muendete dicht neben einer bestehenden Einmuendung.
     * Bei gleicher Knotenzahl gewinnt die hoehere Buchtenzahl.
     */
    let bestScore = null;
    const cands = [], halfWindows = [];
    for (const deg of degs) {
      // Verfuegbare Breite quer zur Reihenrichtung, dann Anzahl rechnen und das
      // Raster mittig setzen - der Rest verteilt sich auf beide Seiten, statt
      // einseitig aufzulaufen und eine Gasse an den Ring zu druecken.
      const rad = deg * Math.PI / 180;
      const dd = [Math.cos(rad), Math.sin(rad)], nn = [-dd[1], dd[0]];
      let lo = Infinity, hi = -Infinity;
      for (let a = -span; a <= span; a += 0.5)
        for (let t = -span; t <= span; t += 2) {
          if (!insideBy(add(add(c0, mul(dd, t)), mul(nn, a)), part, interiorClear))
            continue;
          lo = Math.min(lo, a); hi = Math.max(hi, a); break;
        }
      if (lo === Infinity) continue;
      if (pt === halfPart) halfWindows.push({ deg, phase: (lo + hi) / 2 });

      /**
       * KONKAV-AUSNAHME. Laeuft ein Stueck Randstrasse parallel zur
       * Reihenrichtung und beruehrt eine konkave Ecke, dann soll dort eine
       * Fahrgasse GENAU auf der Randstrassenlinie liegen. Beide sind dann
       * kollinear, und die Buchten laufen von der Randstrasse fugenlos in die
       * Fahrgasse weiter - das ist der Sinn der Regel.
       *
       * Ohne das gewinnt die Rasteroptimierung: sie sucht die groesste
       * Buchtenzahl, Kollinearitaet ist dabei keine Bedingung. Gemessen lag die
       * Gasse der L-Form bei y = 35,775 statt 35,000 - 0,775 m daneben, und
       * genau um diesen Betrag waren die beiden Buchtreihen an der Ecke
       * versetzt.
       */
      const alignedPhases = [];
      {
        // Nur SCHARFE konkave Ecken. Referenz 08s ist unten links aus vielen
        // flachen Knicken zusammengesetzt; greift die Regel auch dort, erzwingt
        // sie ein schlechteres Raster - gemessen 208 -> 199 Buchten, weil der
        // Reihenwinkel von 125 auf 55 Grad sprang. Die L-Form hat dagegen eine
        // echte 90-Grad-Kerbe.
        const reflex = [];
        for (let i = 0; i < site.length; i++) {
          if (!isReflex(site, i)) continue;
          const N = site.length;
          const ein = norm(sub(site[i], site[(i - 1 + N) % N]));
          const aus = norm(sub(site[(i + 1) % N], site[i]));
          const knick = Math.acos(Math.max(-1, Math.min(1,
            ein[0] * aus[0] + ein[1] * aus[1]))) * 180 / Math.PI;
          if (knick >= MIN_NOTCH_TURN) reflex.push(site[i]);
        }
        if (reflex.length) {
          const abstand = (p, a, b) => {
            const v = sub(b, a), L2 = v[0] * v[0] + v[1] * v[1];
            if (L2 < 1e-9) return len(sub(p, a));
            let t = ((p[0] - a[0]) * v[0] + (p[1] - a[1]) * v[1]) / L2;
            t = Math.max(0, Math.min(1, t));
            return len(sub(p, add(a, mul(v, t))));
          };
          for (const s of ringSegs) {
            if (Math.abs(s.u[0] * dd[0] + s.u[1] * dd[1]) < PARALLEL_COS) continue;
            // Das Ringstueck muss die konkave Ecke wirklich beruehren - der
            // Ring liegt `ringDist` innen, deshalb dieser Spielraum.
            if (!reflex.some((p) => abstand(p, s.a, s.b) < ringDist + 2)) continue;
            const a0 = (s.a[0] - c0[0]) * nn[0] + (s.a[1] - c0[1]) * nn[1];
            if (!alignedPhases.some((x) => Math.abs(x - a0) < 0.01))
              alignedPhases.push(a0);
          }
        }
      }
      for (const ph of alignedPhases) {
        const r = pass(deg, ph, false);
        if (r.count === 0) continue;
        cands.push({ deg, phase: ph, count: r.count, capCuts: r.capCuts, aligned: true,
          nodes: 2 * r.aisles + 2 * r.crossings + r.aisles * r.crossings });
      }

      const band = S.ai + 2 * rowDepth;
      const avail = hi - lo;
      const nMax = Math.max(1, Math.floor((avail - band) / module) + 1);
      for (let count = nMax; count >= 1; count--) {
        const used = (count - 1) * module + band;
        if (used > avail + 0.01) continue;
        const phase = lo + (avail - used) / 2 + S.ai / 2 + rowDepth;
        const r = pass(deg, phase, false);
        if (r.count === 0) continue;
        cands.push({ deg, phase, count: r.count, capCuts: r.capCuts,
          nodes: 2 * r.aisles + 2 * r.crossings + r.aisles * r.crossings });
      }
    }
    // Erst wenn ALLE Vollmodul-Kandidaten dieses Teilbereichs leer sind, wird
    // ein Modulfenster in 1/20-Schritten als einseitige 12,5-m-Gasse geprueft.
    // bayFits behaelt weiterhin nur Buchten mit vollem Gruen und Ringabstand.
    if (!cands.length && pt === halfPart) {
      for (const w of halfWindows) for (let k = -10; k <= 10; k++) {
        const phase = w.phase + k * module / 20;
        const r = pass(w.deg, phase, false, true);
        if (r.count === 0) continue;
        cands.push({ deg: w.deg, phase, count: r.count, capCuts: r.capCuts, half: true,
          nodes: 2 * r.aisles + 2 * r.crossings + r.aisles * r.crossings });
      }
    }
    // Erst die Untergrenze, dann die Knoten. Die Knotenzahl allein hat kein
    // Minimum nach unten: die duennste Loesung hat immer die wenigsten
    // Einmuendungen, im Grenzfall die leere. Gemessen kostete das
    // Rechteck 161 statt 185, Referenz(CS2) 71 statt 121 und die L-Form
    // 1 Fahrgasse statt 4. Umgekehrt schiebt reine Buchtenzahl eine
    // zusaetzliche Gasse ins Raster, die dicht neben einer bestehenden
    // Einmuendung liegt - deshalb beides, in dieser Reihenfolge.
    /** Aus einer Kandidatenliste: erst Untergrenze bei den Buchten, dann Knoten. */
    const choose = (list, preserveCaps = false) => {
      // Die Konkav-Ausnahme geht VOR. Gibt es ein Raster, das eine Fahrgasse
      // genau auf die Randstrasse legt, wird nur unter diesen gewaehlt - sonst
      // sticht die reine Buchtenzahl die Regel aus.
      const fluchtend = list.filter((c) => c.aligned);
      if (fluchtend.length) list = fluchtend;
      const leastCuts = preserveCaps ? Math.min(...list.map((c) => c.capCuts || 0)) : 0;
      const valid = preserveCaps
        ? list.filter((c) => (c.capCuts || 0) === leastCuts)
        : list;
      const top = Math.max(...valid.map((c) => c.count));
      let pick = null;
      for (const c of valid) {
        if (c.count < top * (1 - STALL_TOLERANCE)) continue;
        // VERSUCHT UND VERWORFEN: hier stand zusaetzlich ein Mass fuer ENGE
        // Einmuendungen. Die Idee ist richtig - ein verschobenes Raster bringt
        // dieselbe Knotenzahl mit mehr Luft dazwischen (gemessen L-Form 16 -> 28 m
        // fuer 12 Buchten, Browser-Fall 6,3 -> 7,5 m fuer 8). Aber in DIESER
        // Schleife stehen die Knoten noch vor dem Teilen der Verbindungsstrassen,
        // das Mass passt also nicht zum Endergebnis: Schraeg wurde damit
        // schlechter (2 -> 6 enge Paare) statt besser. Richtig waere, die letzten
        // paar Kandidaten voll durchzurechnen und erst dann zu vergleichen.
        if (pick === null || c.nodes < pick.nodes
            || (c.nodes === pick.nodes && c.count > pick.count)) pick = c;
      }
      return pick;
    };
    if (cands.length) {
      let pick = choose(cands);
      // Feinsuche im QUERVERSATZ - und nur fuer den Sieger. Winkel und Anzahl
      // stehen dann schon fest, deshalb kostet das rund 20 Auswertungen statt
      // des Zwanzigfachen der ganzen Suche. Vorher wurde das Raster immer
      // mittig gesetzt; gemessen lagen dadurch bei der Referenz mit CS2-Massen
      // 11 Buchten brach, bei den uebrigen Faellen 0 bis 2. Ein ganzes Modul
      // Fensterbreite reicht: darueber hinaus wiederholt sich das Raster, und
      // die Zahl der Gassen deckt schon die aeussere Schleife ab.
      // Ein fluchtendes Raster darf die Feinsuche NICHT verschieben - jeder
      // Bruchteil eines Moduls zerstoert genau die Kollinearitaet, wegen der
      // es gewaehlt wurde.
      const stepPhase = module / 20;
      const fine = [pick];
      for (let k = -10; k <= 10 && !pick.aligned; k++) {
        if (k === 0) continue;
        const ph = pick.phase + k * stepPhase;
        const r = pass(pick.deg, ph, false, !!pick.half);
        if (r.count === 0) continue;
        fine.push({ deg: pick.deg, phase: ph, count: r.count, capCuts: r.capCuts,
          half: !!pick.half,
          nodes: 2 * r.aisles + 2 * r.crossings + r.aisles * r.crossings });
      }
      pick = choose(fine, true);
      bestScore = [pick.nodes, -pick.count];
      bestDeg = pick.deg; bestPhase = pick.phase; bestHalf = !!pick.half;
    }
    if (bestScore === null) continue;
    const res = pass(bestDeg, bestPhase, true, bestHalf);
    out.innerStalls += res.count;
    out.aisles += res.aisles;
    if (firstAngle === null) firstAngle = bestDeg;
  }
  out.angle = firstAngle === null ? 0 : firstAngle;

  /**
   * RANGFOLGE DER STRASSEN. Randstrasse > Fahrgasse > Verbindung. Die
   * rangniedrigere endet am RAND der ranghoeheren, statt in sie hineinzulaufen -
   * in CS2 duerfen sich zwei Strassen nicht ueberlagern.
   *
   * Die Fahrgassen erledigen das schon beim Bau (sie enden am Fahrbahnrand der
   * Randstrasse). Die Verbindungen liefen bisher ungekuerzt durch alles
   * hindurch: gemessen 56-178 m2 auf der Randstrasse und 105-243 m2 auf den
   * Fahrgassen. Hier werden sie an beidem geteilt; aus einer durchgehenden
   * Verbindung werden dadurch mehrere Stuecke, die sauber anstossen. Buchten
   * kostet das nichts - die Abschnitte richten sich nach `crossAt`, nicht nach
   * der gezeichneten Linie.
   */
  {
    const hoeher = [];
    for (const [a, b] of [...out.perimeterLine, ...ringJunctionLine, ...out.aisleLine]) {
      const v = sub(b, a), Lv = len(v);
      if (Lv < 0.2) continue;
      const u = mul(v, 1 / Lv), nn = [-u[1] * S.ai / 2, u[0] * S.ai / 2];
      hoeher.push([add(a, nn), add(b, nn), sub(b, nn), sub(a, nn)]);
    }
    // EXAKTER Schnitt statt Abtasten. Vorher lief hier eine Schleife in
    // 0,25-m-Schritten: der erste Abtastpunkt ausserhalb der Fahrbahn wurde
    // zum Anfang, lag aber bis zu 0,25 m hinter deren Kante. Gemessen klaffte
    // an fast jeder Einmuendung eine Fuge von genau 0,25 m - in CS2 waere das
    // eine nicht angeschlossene Strasse. Dieselbe Fehlerart wie damals bei
    // `emitStrip`, wo die Abtastung das obere Ende nie traf.
    const neu = [];
    for (const [a, b] of out.crossLine) {
      const Lv = len(sub(b, a));
      if (Lv < 0.5) continue;
      const u = mul(sub(b, a), 1 / Lv);
      // Belegte Parameterbereiche sammeln, zusammenfassen, Rest uebrig lassen.
      const belegt = [];
      for (const q of hoeher) {
        const iv = segInConvex(a, u, Lv, q);
        if (iv) belegt.push(iv);
      }
      belegt.sort((x, y) => x[0] - y[0]);
      let t = 0;
      const frei = [];
      for (const [lo, hi] of belegt) {
        if (lo > t) frei.push([t, Math.min(lo, Lv)]);
        t = Math.max(t, hi);
      }
      if (t < Lv) frei.push([t, Lv]);
      for (const [lo, hi] of frei)
        if (hi - lo >= S.sw) neu.push([add(a, mul(u, lo)), add(a, mul(u, hi))]);
    }
    out.crossLine = neu;
  }

  // Gruen darf weder auf einer Fahrbahn noch auf einer Bucht liegen. Geprueft
  // wird erst jetzt, wenn Strassen und Buchten vollstaendig stehen - vorher
  // kannte der Streifen die spaeter gesetzten Fahrgassen gar nicht.
  const corridors = [];
  for (const q of out.perimeterQuad) corridors.push(q.slice());
  for (const [list, width] of [[out.perimeterLine.slice(out.perimeterQuad.length), S.ai],
                               [out.aisleLine, S.ai], [out.crossLine, crossWidth]])
    for (const [a, b] of list) {
      const v = sub(b, a), L = len(v);
      if (L < 0.2) continue;
      const u = mul(v, 1 / L), nn = [-u[1] * width / 2, u[0] * width / 2];
      corridors.push([add(a, nn), add(b, nn), sub(b, nn), sub(a, nn)]);
    }
  // Die Einfahrt bringt ihre FLAECHE mit. Aus der Mittellinie gerechnet waere
  // sie an einer schraegen Ecke ein Rechteck, und an beiden Enden bliebe ein
  // Keil unbelegt - gemessen 8,7 m2. Ausserdem duerfen Buchten sie nicht
  // ueberschneiden; auch dieser Test haengt an dieser Liste.
  for (const q of out.entranceQuad) corridors.push(q.slice());

  const placed = [...notchGreens, ...notchCaps];
  const freeForGreen = (q) => !corridors.some((r) => quadsOverlap(q, r, 0.05))
                           && !out.bay.some((r) => quadsOverlap(q, r, 0.05))
                           && !placed.some((r) => quadsOverlap(q, r, 0.05));
  /**
   * Kerben- und Modulreihe erzeugen zunaechst je eine Einzelreihenkappe,
   * deren Mittelstreifenteile sich ueberdecken. Ihre rechteckige Huellkappe
   * verbindet beide Reihen und hat exakt die Doppeltiefe 2*sl + md.
   */
  const mergeNotchCap = (cand) => {
    if (!cand.notchMate) return false;
    const mate = cand.notchMate;
    const old = mate.caps.find((q) => quadsOverlap(cand.q, q, 0.05));
    if (!old) return false;
    const pts = [...old, ...cand.q], u = mate.u, n = mate.back;
    const along = pts.map((p) => p[0] * u[0] + p[1] * u[1]);
    const across = pts.map((p) => p[0] * n[0] + p[1] * n[1]);
    const lo = Math.min(...along), hi = Math.max(...along);
    const a0 = Math.min(...across), a1 = Math.max(...across);
    if (hi - lo < S.sw - FIT_EPS || hi - lo >= 2 * S.sw - FIT_EPS
        || Math.abs(a1 - a0 - (2 * rowDepth + S.md)) > 0.05) return false;
    const cc = add(mul(u, (lo + hi) / 2), mul(n, (a0 + a1) / 2));
    const cap = rect(cc, u, n, (hi - lo) / 2, (a1 - a0) / 2);
    if (!cap.every((p) => insideBy(p, site, S.es))) return false;
    if (corridors.some((q) => quadsOverlap(cap, q, 0.05))) return false;
    if (out.bay.some((q) => quadsOverlap(cap, q, 0.05))) return false;
    if (placed.some((q) => q !== old && q !== mate.strip
        && quadsOverlap(cap, q, 0.05))) return false;
    for (const list of [notchCaps, out.cap, placed, mate.caps]) {
      const i = list.indexOf(old);
      if (i >= 0) list[i] = cap;
    }
    occupied.add(cap);
    return true;
  };
  for (const cand of greenCand) {
    if (mergeNotchCap(cand)) continue;
    if (!freeForGreen(cand.q)) { out.dropped++; continue; }
    if (cand.shift > 0) out.shifted++;
    placed.push(cand.q);
    out[cand.list].push(cand.q);
  }

  /**
   * Nachruecken: was nach dem Raster frei bleibt und an einer Fahrbahn liegt,
   * bekommt noch eine Reihe - nach derselben Regel wie ueberall, also Bucht
   * plus Gruenstreifen in voller Tiefe dahinter. Ohne diesen Durchgang blieb
   * die Restflaeche ungenutzt: bei der L-Form 16,6 % der Flaeche, in der
   * gemessen 23 Buchten Platz gehabt haetten.
   */
  {
    // NUR Fahrgassen. Innere Randbuchten bekommen weiter unten einen eigenen
    // Lauf, nachdem Fahrgassen, deren Buchten, Streifen und Kappen feststehen.
    // Wuerden sie schon hier mitlaufen, koennten sie hoeher priorisierte
    // Fahrgassenbuchten blockieren.
    for (const [aisleIndex, [a, b]] of out.aisleLine.entries()) {
      const L = len(sub(b, a));
      if (L < S.sw) continue;
      const u = norm(sub(b, a)), m = [-u[1], u[0]];
      for (const side of [-1, 1]) {
        const off = side * (S.ai / 2 + rowDepth / 2);
        let run = [];
        const flushStrip = () => {
          // Eine nachgerueckte Reihe ist nur dann vollstaendig, wenn neben den
          // Buchten auch an beiden Enden je eine Kappe Platz hat. Vorher wurde
          // selbst ein einzelner freier Rasterplatz als Bucht plus Streifen
          // gesetzt (Referenz: 3-m-Streifen mit zwei kapplosen Enden).
          if (!run.length) return;
          const commit = (bays) => {
            for (const { q } of bays) {
              occupied.add(q);
              out.bay.push(q); out.bayKind.push("extra");
              out.extraStalls++;
            }
          };
          const allLo = run[0].t, allHi = run[run.length - 1].t;
          const allCc = add(add(a, mul(u, (allLo + allHi) / 2)),
                            mul(m, side * (S.ai / 2 + rowDepth + S.md / 2)));
          const allStrip = rect(allCc, u, m, (allHi - allLo) / 2 + S.sw / 2, S.md / 2);
          // Wenn schon der Streifen selbst nicht frei ist, verhaelt sich dieser
          // Nachruecklauf wie zuvor: die Bucht darf bleiben, erzeugt aber keinen
          // gemeldeten Streifen. Der Kappenbefund betrifft nur Laeufe, deren
          // Streifen tatsaechlich ausgegeben wuerde.
          if (S.md <= 0.05 || !freeForGreen(allStrip)) {
            commit(run); run = []; return;
          }
          if (run.length < 3) { run = []; return; }
          const bays = run.slice(1, -1);
          const capAcross = side * (S.ai / 2 + (rowDepth + S.md) / 2);
          const capHalf = (rowDepth + S.md) / 2;
          const caps = [run[0], run[run.length - 1]].map(({ t }) => {
            const cc = add(add(a, mul(u, t)), mul(m, capAcross));
            return rect(cc, u, m, S.sw / 2, capHalf);
          });
          const lo = bays[0].t, hi = bays[bays.length - 1].t;
          const cc = add(add(a, mul(u, (lo + hi) / 2)),
                         mul(m, side * (S.ai / 2 + rowDepth + S.md / 2)));
          const strip = rect(cc, u, m, (hi - lo) / 2 + S.sw / 2, S.md / 2);
          if (!caps.every(freeForGreen) || !freeForGreen(strip)) { run = []; return; }
          commit(bays);
          for (const q of caps) { placed.push(q); out.cap.push(q); }
          placed.push(strip); out.median.push(strip);
          run = [];
        };
        const grid = aisleGrids[aisleIndex][side];
        const aAlong = axisCoord(a, grid.u);
        for (let bayIndex = grid.firstEdgeAtOrAfter(aAlong); ; bayIndex++) {
          const t = grid.center(bayIndex) - aAlong;
          if (t >= L) break;
          const c = add(add(a, mul(u, t)), mul(m, off));
          // Bucht im Areal, und HINTER ihr der Streifen in voller Tiefe.
          if (!rectClear(c, u, m, S.sw / 2, rowDepth / 2, site, S.es)
              || !insideBy(add(c, mul(m, side * (rowDepth / 2 + S.md))), site, S.es)
              // Diese nachgerueckte Bucht gehoert zur Fahrgasse und behaelt ihren
              // Vorrang samt vollem Gruenstreifen. Die Ringgrenze verhindert,
              // dass sie an einem Gassenende in den spaeteren Bereich fuer
              // innere Randbuchten rutscht.
              || !rectClear(c, u, m, S.sw / 2, rowDepth / 2, ring, S.ai / 2 + S.md)) {
            flushStrip(); continue;
          }
          const q = rect(c, u, m, S.sw / 2, rowDepth / 2);
          if (occupied.hits(q) || placed.some((z) => quadsOverlap(q, z, 0.05))
              || corridors.some((z) => quadsOverlap(q, z, 0.05))) {
            flushStrip(); continue;
          }
          run.push({ t, q });
        }
        flushStrip();
      }
    }
  }

  // Wo eine Fahrbahn eine Buchtenreihe KREUZT, muss die Bucht weichen. Das
  // trifft vor allem die Reihen an den Eckgassen: sie entstehen vor dem
  // Modulraster, dessen Gassen sie spaeter queren (gemessen 23 Stueck).
  //
  // Laeuft VOR der Restfuellung. Vorher stand dieser Block danach - die
  // Fuellung hielt die weggeworfenen Buchten fuer belegt und liess dort ein
  // Loch. Weil `quadsOverlap` schon bei Teilueberdeckung entfernt, blieb
  // links und rechts jeder Querstrasse ein Streifen ungedeckt: bei der L-Form
  // mit dem 16-m-Modul gemessene 413 m2 (5,1 % der Flaeche).
  {
    const keep = [], kind = [];
    let rand = 0, innen = 0, extra = 0;
    for (let i = 0; i < out.bay.length; i++) {
      if (corridors.some((z) => quadsOverlap(out.bay[i], z, 0.05))) continue;
      keep.push(out.bay[i]); kind.push(out.bayKind[i]);
      if (out.bayKind[i] === "rand") rand++;
      else if (out.bayKind[i] === "innen") innen++;
      else extra++;
    }
    out.bay = keep; out.bayKind = kind;
    out.perimeterStalls = rand; out.innerStalls = innen; out.extraStalls = extra;
  }

  /**
   * Versetztes Ende zweier gegenueberliegender Reihen. An einer schraegen
   * Randstrasse kann eine Reihe genau eine Bucht weiter reichen als die andere.
   * `emitCaps` versucht dort zu Recht keine Doppelreihen-Kappe: die laengere
   * Reihe belegt deren Platz bereits. Der gemeinsame Streifen lief bisher aber
   * trotzdem bis zum Ende dieser Bucht (Referenz: ein Ende 14,07 m von jeder
   * Kappe entfernt).
   *
   * Dort wird die letzte Bucht der laengeren Reihe durch eine EINZELreihen-
   * Kappe ersetzt und der Streifen um genau einen Rasterplatz gekuerzt. Das ist
   * echte Geometrie: eine Bucht weniger, dafuer Kappe und Streifen buendig.
   */
  {
    const remove = new Set();
    const atQuad = (p, q) => pointIn(p, q) || distToBoundary(p, q) <= 0.05;
    const middle = (a, b) => mul(add(a, b), 0.5);
    for (let qi = 0; qi < out.median.length; qi++) {
      // Nach jeder Reparatur neu vermessen: falls beide Enden betroffen sind,
      // haben Mittelpunkt und Laenge sich nach der ersten bereits geaendert.
      for (let fix = 0; fix < 2; fix++) {
        const strip = out.median[qi];
        const edgeData = strip.map((a, i) => {
          const b = strip[(i + 1) % strip.length];
          return { a, b, L: len(sub(b, a)), p: middle(a, b) };
        });
        const longEdge = edgeData.reduce((best, e) => e.L > best.L ? e : best);
        const d = norm(sub(longEdge.b, longEdge.a)), n = [-d[1], d[0]];
        const long = longEdge.L, nextLong = long - S.sw;
        if (nextLong <= S.sw) break;
        const ends = edgeData.slice().sort((a, b) => a.L - b.L).slice(0, 2)
          .filter((end) => !out.cap.some((q) => atQuad(end.p, q)));
        if (!ends.length) break;
        const c = strip.reduce((s, p) => add(s, p), [0, 0]).map((x) => x / strip.length);
        const half = (rowDepth + S.md) / 2;
        let best = null;
        for (const end of ends) {
          const outward = norm(sub(end.p, c));
          for (const across of [-rowDepth / 2, rowDepth / 2]) {
            const cc = add(add(end.p, mul(outward, -S.sw / 2)), mul(n, across));
            const cap = rect(cc, d, n, S.sw / 2, half);
            if (!cap.every((p) => pointIn(p, site))) continue;
            if (corridors.some((q) => quadsOverlap(cap, q, 0.05))) continue;
            if (out.cap.some((q) => quadsOverlap(cap, q, 0.05))) continue;
            if (out.green.some((q) => quadsOverlap(cap, q, 0.05))) continue;
            if (out.median.some((q, i) => i !== qi && quadsOverlap(cap, q, 0.05))) continue;
            let roadGap = Infinity;
            for (const p of cap) for (const road of corridors)
              roadGap = Math.min(roadGap, pointIn(p, road) ? 0 : distToBoundary(p, road));
            if (roadGap > 0.05) continue;
            const hits = [];
            for (let i = 0; i < out.bay.length; i++)
              if (!remove.has(i) && quadsOverlap(cap, out.bay[i], 0.05)) hits.push(i);
            if (!hits.length) continue;
            let bayGap = Infinity;
            for (let i = 0; i < out.bay.length; i++) {
              if (remove.has(i) || hits.includes(i)) continue;
              for (const p of cap) for (const z of out.bay[i])
                bayGap = Math.min(bayGap, len(sub(p, z)));
            }
            if (bayGap > 0.05) continue;
            if (!best || hits.length < best.hits.length)
              best = { cap, hits, outward };
          }
        }
        if (!best) break;
        for (const i of best.hits) remove.add(i);
        out.cap.push(best.cap);
        const cc = add(c, mul(best.outward, -S.sw / 2));
        out.median[qi] = rect(cc, d, n, nextLong / 2, S.md / 2);
      }
    }
    if (remove.size) {
      const bay = [], kind = [];
      for (let i = 0; i < out.bay.length; i++) {
        if (remove.has(i)) continue;
        bay.push(out.bay[i]); kind.push(out.bayKind[i]);
      }
      out.bay = bay; out.bayKind = kind;
      out.perimeterStalls = kind.filter((x) => x === "rand").length;
      out.innerStalls = kind.filter((x) => x === "innen").length;
      out.extraStalls = kind.filter((x) => x === "extra").length;
    }
  }

  /**
   * Zu kurze Rechteckkappen an schraegen Strassen.
   *
   * layoutSection reserviert laengs eine volle Bucht, aber die aeussere Ecke
   * der Kappe muss um half*slope weiter von der Strasse zurueckweichen.
   * Bleibt dadurch weniger als sw, darf auf dem gemeinsamen Raster kein
   * Bruchteil einer Bucht verschoben werden: Die Kappe ersetzt den
   * unmittelbar folgenden Rasterplatz. Ihre Strassenseite bleibt dabei exakt
   * stehen, die andere Seite wandert um eine Buchtbreite nach innen. So misst
   * die Kappe danach alte Laenge + sw, also sicher >= sw und < 2*sw.
   *
   * Der zugehoerige Gruenstreifen wird nur LAENGS um denselben Rasterplatz
   * gekuerzt; seine eingestellte Tiefe bleibt unveraendert.
   */
  {
    const remove = new Set();
    const center = (q) => mul(q.reduce((s, p) => add(s, p), [0, 0]), 1 / q.length);
    const assess = (cap, ci) => {
      if (!cap.every((p) => insideBy(p, site, S.es))) return null;
      if (corridors.some((q) => quadsOverlap(cap, q, 0.05))) return null;
      if (out.cap.some((q, i) => i !== ci && quadsOverlap(cap, q, 0.05))) return null;
      if (out.green.some((q) => quadsOverlap(cap, q, 0.05))) return null;

      const bays = [];
      for (let i = 0; i < out.bay.length; i++)
        if (!remove.has(i) && quadsOverlap(cap, out.bay[i], 0.05)) bays.push(i);
      if (!bays.length) return null;

      const toward = norm(sub(center(cap), center(out.cap[ci])));
      const strips = [];
      for (let i = 0; i < out.median.length; i++) {
        const q = out.median[i];
        if (!quadsOverlap(cap, q, 0.05)) continue;
        const along = q.map((p) => p[0] * toward[0] + p[1] * toward[1]);
        const lo = Math.min(...along), hi = Math.max(...along);
        if (hi - lo <= S.sw + FIT_EPS) return null;
        strips.push({ i, q: q.map((p, k) =>
          along[k] <= lo + FIT_EPS ? add(p, mul(toward, S.sw)) : p) });
      }
      return { cap, bays, strips };
    };

    for (let ci = 0; ci < out.cap.length; ci++) {
      const q = out.cap[ci];
      const capLen = len(sub(q[1], q[0]));
      if (capLen >= S.sw - FIT_EPS) continue;
      const u = norm(sub(q[1], q[0]));
      const options = [
        [q[0], add(q[1], mul(u, S.sw)), add(q[2], mul(u, S.sw)), q[3]],
        [add(q[0], mul(u, -S.sw)), q[1], q[2], add(q[3], mul(u, -S.sw))],
      ].map((cap) => assess(cap, ci)).filter(Boolean)
        .sort((a, b) => a.bays.length - b.bays.length);
      if (!options.length) continue;

      const pick = options[0];
      out.cap[ci] = pick.cap;
      for (const strip of pick.strips) out.median[strip.i] = strip.q;
      for (const i of pick.bays) remove.add(i);
    }

    if (remove.size) {
      const bay = [], kind = [];
      for (let i = 0; i < out.bay.length; i++) {
        if (remove.has(i)) continue;
        bay.push(out.bay[i]); kind.push(out.bayKind[i]);
      }
      out.bay = bay; out.bayKind = kind;
      out.perimeterStalls = kind.filter((x) => x === "rand").length;
      out.innerStalls = kind.filter((x) => x === "innen").length;
      out.extraStalls = kind.filter((x) => x === "extra").length;
    }
  }

  /**
   * NIEDRIGSTE PRIORITAET: Buchten an der Innenseite der Randstrasse.
   *
   * Dieser Lauf kommt erst nach Fahrgassen, deren Buchten, Streifen und Kappen.
   * Seine Kandidaten duerfen nichts davon verschieben oder ueberdecken. Ein
   * sinnvoller gerader Lauf braucht mindestens MIN_INNER_RING_RUN
   * zusammenhaengende Buchtbreiten. Jede gesetzte Bucht braucht ausserdem die
   * volle Tiefe aus Bucht plus Gruenstreifen bis zum naechsten harten Hindernis.
   * Vorhandenes Gruen darf diese Reserve bereits erfuellen; die Restfuellung
   * direkt danach deckt den noch freien Teil.
   *
   * Die Innenrichtung folgt dem Umlaufsinn des Rings. Sie wird ausdruecklich
   * NICHT mit pointIn() direkt auf der Ringkante bestimmt.
   */
  {
    const hard = [...corridors, ...out.bay];
    const fixedGreen = [...out.green, ...out.median, ...out.cap];
    const added = [], reserved = [];
    const hits = (q, list) => list.some((z) => quadsOverlap(q, z, 0.05));
    const winding = signedArea(ring) >= 0 ? 1 : -1;
    for (const [ringIndex, [a, b]] of out.perimeterLine.entries()) {
      const innerCarry = perimeterGrids[ringIndex].carry;
      const v = sub(b, a), L = len(v);
      if (L < 0.2) continue;
      const u = mul(v, 1 / L);
      const n = mul([-u[1], u[0]], winding);
      const rowAngle = ((Math.atan2(u[1], u[0]) * 180 / Math.PI) % 180 + 180) % 180;
      const preferredAngle = ((out.angle % 180) + 180) % 180;
      const rawDeviation = Math.abs(rowAngle - preferredAngle);
      const angleDeviation = Math.min(rawDeviation, 180 - rawDeviation);
      const transverse = angleDeviation >= MIN_TRANSVERSE_ROW_ANGLE;
      const minRun = transverse ? MIN_TRANSVERSE_INNER_RING_RUN : MIN_INNER_RING_RUN;

      /**
       * RASTERPHASE WIE BEI DEN FAHRGASSEN.
       *
       * Vorher lief diese Reihe stur auf dem durchgehenden Ringraster
       * (`innerCarry`). Die Fahrgassenreihen suchen dagegen unter 20 Phasen
       * die mit den meisten Buchten - und landen dadurch mit genau einer
       * Buchtbreite hinter der Fahrbahnkante. Gemessen am Rechteck: Fahrgasse
       * beginnt bei 16,50 = Kante 13,50 + 3,00, die Randreihe erst bei 19,00
       * = Kante + 5,50. Auf denselben Abschnitt passten dadurch 7 statt 8
       * Buchten.
       *
       * Der Probelauf darf nichts veraendern: er bekommt eigene Kopien von
       * `added`/`reserved` und fasst `occupied` nicht an.
       */
      const platziere = (phase, commit) => {
        const lokalAdded = commit ? added : added.slice();
        const lokalReserved = commit ? reserved : reserved.slice();
        const frei = (cand) => cand.depthOK
          && !hits(cand.q, lokalAdded) && !hits(cand.q, lokalReserved)
          && !(cand.reserve && hits(cand.reserve, lokalAdded));
        let run = [], gesetzt = 0;
        const flush = () => {
          const placeable = transverse ? run.filter(frei).length : run.length;
          if (placeable >= minRun) {
            for (const cand of run) {
              if (!frei(cand)) continue;
              gesetzt++;
              lokalAdded.push(cand.q);
              if (cand.reserve) lokalReserved.push(cand.reserve);
              if (!commit) continue;
              occupied.add(cand.q);
              out.bay.push(cand.q); out.bayKind.push("rand-innen");
              out.perimeterStalls++; out.innerPerimeterStalls++;
            }
          }
          run = [];
        };
        for (let t = phase + S.sw / 2; t < L; t += S.sw) {
          const along = add(a, mul(u, t));
          const c = add(along, mul(n, S.ai / 2 + rowDepth / 2));
          const q = rect(c, u, n, S.sw / 2, rowDepth / 2);
          if (!rectClear(c, u, n, S.sw / 2, rowDepth / 2, site, S.es)
              || hits(q, hard) || hits(q, fixedGreen)) {
            flush(); continue;
          }
          let reserve = null, depthOK = true;
          if (S.md > 0.05) {
            const gc = add(along, mul(n, S.ai / 2 + rowDepth + S.md / 2));
            reserve = rect(gc, u, n, S.sw / 2, S.md / 2);
            depthOK = rectClear(gc, u, n, S.sw / 2, S.md / 2, site, S.es)
                   && !hits(reserve, hard);
          }
          run.push({ q, reserve, depthOK });
        }
        flush();
        return gesetzt;
      };

      /**
       * Die Phasen kommen aus den HINDERNISKANTEN, nicht aus einem Raster.
       *
       * Ein Rasterlauf in 20 Schritten von sw/20 trifft die richtige Phase nur
       * zufaellig: beim Rechteck lag sie bei 0,50 und fiel zwischen die
       * Schritte von 0,15. Ergebnis 7 Buchten mit Kapseln 5,95/3,05, waehrend
       * die Fahrgasse im selben 30-m-Abschnitt 8 Buchten mit 3,00/3,00 hatte.
       *
       * Soll die erste Bucht genau eine Buchtbreite hinter einer Kante
       * beginnen, muss ihre linke Kante bei `tKante + sw` liegen. Wegen des
       * Rasters mit Schrittweite sw heisst das schlicht `phase = tKante % sw`
       * - fuer die Kante links wie rechts derselbe Wert. Damit entsteht die
       * Kapsel wie bei den Fahrgassen aus der Abschnittslaenge, statt sie zu
       * suchen.
       */
      const phasen = new Set();
      const modSw = (x) => ((x % S.sw) + S.sw) % S.sw;
      phasen.add(modSw(innerCarry));
      for (const z of corridors) {
        let lo = Infinity, hi = -Infinity;
        for (const pt of z) {
          const t = (pt[0] - a[0]) * u[0] + (pt[1] - a[1]) * u[1];
          if (t < lo) lo = t;
          if (t > hi) hi = t;
        }
        if (hi < -S.sw || lo > L + S.sw) continue;
        phasen.add(modSw(lo));
        phasen.add(modSw(hi));
      }
      // Grobraster als Rueckfall, falls keine Kante etwas hergibt.
      for (let k = 0; k < 20; k++) phasen.add(modSw(innerCarry + k * S.sw / 20));

      let besteS = modSw(innerCarry), besteZahl = -1;
      for (const phase of phasen) {
        const zahl = platziere(phase, false);
        if (zahl > besteZahl) { besteZahl = zahl; besteS = phase; }
      }
      platziere(besteS, true);
    }
  }

  /**
   * KAPSELREGEL FUER RANDREIHEN. Die aeussere Randreihe ist an Einfahrten in
   * getrennte Rasterlaeufe geteilt; rand-innen benutzt das ununterbrochene
   * Grundraster. Stoesst ein freies Reihenende innerhalb einer Buchtbreite auf
   * eine Fahrbahn, faellt die Endbucht. Stoesst es direkt auf eine andere
   * Bucht, bleibt der lueckenlose Lauf unveraendert.
   *
   * Die Kappe wird nur ausgegeben, wenn ihre volle Tiefe geometrisch passt:
   * aussen sl, innen sl + md. Die Endbucht bleibt auch dann entfernt, wenn
   * Rand, Fahrbahn oder festes Gruen die Kappe verhindern: der Mindestabstand
   * zur Strasse ist die harte Regel.
   */
  const perimeterBaseCaps = out.cap.slice();
  finishPerimeterRows(out, site, S, crossWidth, ringJunctionLine,
                      corridors, rowDepth);

  // Die Zusatzsegmente codieren die Knotenflaechen fuer Zeichnung, Debug und
  // unveraenderte externe Flaechenpruefungen als Teil der Randstrasse.

  // Restlicher freier Raum wird ebenfalls gruen. Bewusst als EIGENE Kategorie:
  // diese Flaechen haben kein Sollmass, anders als Streifen und Kappen. Im Mod
  // waren genau solche Restflaechen die Quelle der 1-m-Baender und
  // 8-cm-Splitter - hier bleiben sie dadurch unterscheidbar.
  //
  // Laeuft ZULETZT. Vorher stand dieser Block vor der Gruenvergabe und las
  // `out.median`/`out.cap`, die zu dem Zeitpunkt noch leer waren - die
  // Restfuellung legte sich also ueber Streifen und Kappen.
  {
    const covered = [...out.bay, ...out.median, ...out.cap, ...out.green,
                     ...corridors];
    const inCovered = makeCoverIndex(covered, Math.max(4, rowDepth));
    // Punktgenau statt gerastert: die Restflaeche wird aus denselben Kanten
    // aufgebaut, die sie begrenzen. Kein Saum, keine Treppe.
    const frei = (q) => !pointIn(q, site) || inCovered(q);
    const teile = slabFill(site, covered, frei, 0.02);
    for (const poly of teile.aussen) out.fill.push(poly);
    // Loecher getrennt: die Seite zeichnet Aussenringe und Loecher in EINEM
    // Pfad, dann bleibt das Loch nach der Nonzero-Regel leer.
    for (const poly of teile.loecher) out.fillHole.push(poly);
  }

  out.stalls = out.perimeterStalls + out.innerStalls + out.extraStalls;
  // Innere Randbuchten duerfen auch die Wahl zwischen zwei Kernlayouts nicht
  // beeinflussen; sonst koennten sie indirekt Fahrgassenbuchten verdraengen.
  const priorityStalls = (r) => r.stalls - r.innerPerimeterStalls;
  if (out.stalls === 0) out.warnings.push("Keine Bucht platziert.");
  // Gegenprobe ohne Zerlegung - nur wenn ueberhaupt zerlegt wurde, sonst
  // waere es dieselbe Rechnung zweimal.
  // Die Eckgassen-Ausnahme muss sich beweisen. Gemessen kostet sie bei der
  // L-Form mehr als sie bringt (147 ohne, 142 mit) - der 9-m-Fahrweg verdraengt
  // mehr Rasterbuchten, als seine eigene Reihe traegt. Bei anderem Zuschnitt
  // kann es umgekehrt sein, deshalb gegenrechnen statt pauschal entscheiden.
  if (!S.__noNotch && out.notchAisles > 0) {
    const alt = buildImpl(site, { ...S, __noNotch: true });
    if (priorityStalls(alt) > priorityStalls(out)) return alt;
  }
  if (!S.__single && out.parts > 1) {
    // Nur nachrechnen, wenn ein Teilstueck ueberhaupt zu schmal fuer ein Modul
    // ist - sonst kostet die Gegenprobe nur Zeit. Schwelle: Modulband plus der
    // Randbedarf auf beiden Seiten.
    const noetig = S.ai + 2 * rowDepth + 2 * interiorClear;
    const eng = parts.some((q) => {
      const xs = q.map((p) => p[0]), ys = q.map((p) => p[1]);
      return Math.min(Math.max(...xs) - Math.min(...xs),
                      Math.max(...ys) - Math.min(...ys)) < noetig;
    });
    if (eng) {
      const alt = buildImpl(site, { ...S, __single: true });
      if (priorityStalls(alt) > priorityStalls(out)) return alt;
    }
  }
  const staticBay = [], staticKind = [];
  const firstOuter = out.bayKind.indexOf("rand");
  let outerInsert = 0;
  for (let i = 0; i < out.bay.length; i++) {
    if (out.bayKind[i] === "rand") continue;
    if (firstOuter < 0 || i < firstOuter) outerInsert++;
    staticBay.push(out.bay[i]); staticKind.push(out.bayKind[i]);
  }
  ENTRANCE_REUSE.set(out, {
    entranceGreenBase, perimeterBaseCaps, staticBay, staticKind, outerInsert,
  });
  return out;
}

/**
 * Ergebnisbezogener Einfahrts-Cache ausserhalb des Ausgabeobjekts.
 */
const ENTRANCE_REUSE = new WeakMap();
const entranceReuseState = (site, S) => ({
  site: site.map((p) => p.slice()),
  settings: Object.keys(S).filter((key) => key !== "entrances").sort()
    .map((key) => [key, S[key]]),
});
function sameEntranceReuse(state, site, S) {
  if (!state || state.site.length !== site.length) return false;
  for (let i = 0; i < site.length; i++) {
    if (state.site[i].length !== site[i].length) return false;
    for (let j = 0; j < site[i].length; j++)
      if (!Object.is(state.site[i][j], site[i][j])) return false;
  }
  const settings = Object.keys(S).filter((key) => key !== "entrances").sort();
  return settings.length === state.settings.length
    && settings.every((key, i) => key === state.settings[i][0]
      && Object.is(S[key], state.settings[i][1]));
}
function sameEntranceTopology(a, b) {
  if (a.length !== b.length) return false;
  return a.every((item, i) => {
    const other = b[i];
    if (!item || !other || typeof item !== "object" || typeof other !== "object")
      return Object.is(item, other);
    const aKeys = Object.keys(item).filter((key) => key !== "along").sort();
    const bKeys = Object.keys(other).filter((key) => key !== "along").sort();
    return aKeys.length === bKeys.length
      && aKeys.every((key, j) => key === bKeys[j] && Object.is(item[key], other[key]));
  });
}
function attachEntranceReuse(out, site, S) {
  const cache = out && ENTRANCE_REUSE.get(out);
  if (cache) {
    cache.state = entranceReuseState(site, S);
    cache.entrances = out.entrances.map((entrance) => ({ ...entrance }));
  }
  return out;
}
function build(site, S) {
  // Sonderplaetze ganz ZUM SCHLUSS. `buildImpl` rechnet intern Varianten
  // gegeneinander und vergleicht dabei Buchtenzahlen (`priorityStalls`).
  // Behindertengruppen aendern diese Zahl - stuenden sie schon drin, wuerde
  // die Auswahl verfaelscht. Gemessen: die L-Form kippte dadurch von
  // 4 Fahrgassen bei 90 Grad auf 2 bei 0 Grad.
  const out = attachEntranceReuse(buildImpl(site, S), site, S);
  assignBayRoles(out, S);
  return out;
}

/**
 * Nur eine geaenderte Laengsposition darf den Teilcache benutzen. Polygon,
 * jede andere Einstellung, Anzahl, Reihenfolge oder Kante erzwingen build().
 */
function rebuildEntrances(site, S, previous) {
  const cache = previous && ENTRANCE_REUSE.get(previous);
  const requests = Array.isArray(S.entrances) ? S.entrances : [];
  // Eckgefangene Einfahrten haben eine eigene Richtung und ein Parallelogramm
  // als Flaeche. Der schnelle Weg kennt beides nicht, also VOLL rechnen.
  //
  // Hier stand `return null`. Die Seite uebernimmt den Rueckgabewert direkt
  // (`lastResult = rebuildEntrances(...)`), also war beim Einrasten an einer
  // Ecke schlagartig das ganze Ergebnis weg: das Areal wurde komplett
  // "unbelegt" gezeichnet und "Send Debug" meldete "Nichts zu melden".
  // Diese Funktion darf NIE null liefern - sie faellt immer auf `build`
  // zurueck, so wie unten bei `!entranceOnly` auch.
  if (requests.some((request) => request && request.corner)) return build(site, S);
  // Kerbengassen entstehen vor der Randreihe und koennen eine Einfahrt
  // geometrisch beruehren; fuer sie bleibt deshalb die Vollberechnung massgeblich.
  const entranceOnly = cache && previous.notchAisles === 0
    && sameEntranceReuse(cache.state, site, S)
    && requests.length === cache.entrances.length
    && requests.every((request) => request && Number.isFinite(request.along))
    && sameEntranceTopology(requests, cache.entrances)
    && requests.some((request, i) => request.along !== cache.entrances[i].along);
  if (!entranceOnly) return build(site, S);

  const rowDepth = S.sl;
  const crossWidth = Number.isFinite(S.cw) ? S.cw : 3;
  const ringDist = S.es + rowDepth + S.ai / 2;
  const ringCount = previous.ring.length;
  const ringLines = previous.perimeterLine.slice(0, ringCount);
  const ringJunctionLine = previous.perimeterLine.slice(ringCount);
  const out = { ...previous,
    bay: [], bayKind: [], cap: cache.perimeterBaseCaps.slice(),
    green: cache.entranceGreenBase.slice(), fill: [], fillHole: [],
    perimeterLine: ringLines.slice(), entrances: [], entranceLine: [], warnings: [],
  };

  let ringCarry = 0;
  const perimeterGrids = ringLines.map(([a, b]) => {
    const grid = { carry: ringCarry, breaks: [] };
    const L = len(sub(b, a));
    if (L >= 0.2) ringCarry = (ringCarry + L) % S.sw;
    return grid;
  });
  const perimeterGridRuns = (grid, L) => {
    const runs = [];
    let from = 0, edgeOffset = grid.carry;
    const modulo = (x, m) => ((x % m) + m) % m;
    for (const brk of grid.breaks.slice().sort((a, b) => a.start - b.start)) {
      edgeOffset = from + modulo(brk.start - from, S.sw);
      runs.push({ from, to: brk.start, edgeOffset });
      from = brk.end; edgeOffset = brk.end;
    }
    runs.push({ from, to: L, edgeOffset });
    return runs;
  };
  const axisCoord = (p, u) => p[0] * u[0] + p[1] * u[1];
  const edges = site.map((a, edge) => {
    const b = site[(edge + 1) % site.length], v = sub(b, a), L = len(v);
    return { edge, a, b, L, u: L > FIT_EPS ? mul(v, 1 / L) : [1, 0] };
  });
  const winding = signedArea(site) >= 0 ? 1 : -1;
  const placementsFor = (edge, wantedAlong) => {
    const placements = [];
    ringLines.forEach(([a, b], ringIndex) => {
      const v = sub(b, a), L = len(v);
      if (L < 0.2) return;
      const u = mul(v, 1 / L);
      if (axisCoord(u, edge.u) < 1 - 1e-8) return;
      const across = axisCoord(sub(a, edge.a),
        mul([-edge.u[1], edge.u[0]], winding));
      if (Math.abs(across - ringDist) > 0.05) return;
      const alongOffset = axisCoord(sub(a, edge.a), edge.u);
      for (const run of perimeterGridRuns(perimeterGrids[ringIndex], L)) {
        const [lo, hi] = entrancePlacementBounds(
          site, edge, run, L, alongOffset, S);
        if (lo > hi + FIT_EPS) continue;
        const wantedStart = wantedAlong - alongOffset - S.ai / 2;
        const start = Math.max(lo, Math.min(hi, wantedStart));
        placements.push({ along: alongOffset + start + S.ai / 2,
          ringIndex, start, end: start + S.ai });
      }
    });
    return placements.sort((a, b) =>
      Math.abs(a.along - wantedAlong) - Math.abs(b.along - wantedAlong)
      || a.along - b.along);
  };
  const queued = requests.map((request, order) =>
    ({ request, order, edge: edges[request.edge] }))
    .sort((a, b) => a.edge.edge - b.edge.edge
                    || a.request.along - b.request.along);
  const chosenEntrances = [];
  for (const { request, order, edge } of queued) {
    const chosen = placementsFor(edge, request.along)[0];
    if (!chosen) {
      out.warnings.push("Kein Randabschnitt mit beidseitiger Kappe fuer Einfahrt gefunden.");
      continue;
    }
    perimeterGrids[chosen.ringIndex].breaks.push(
      { start: chosen.start, end: chosen.end });
    const n = mul([-edge.u[1], edge.u[0]], winding);
    const start = add(edge.a, mul(edge.u, chosen.along));
    const end = add(start, mul(n, ringDist - S.ai / 2));
    chosenEntrances.push({ order, entrance: { edge: edge.edge, along: chosen.along },
                           line: [start, end] });
  }
  chosenEntrances.sort((a, b) => a.order - b.order);
  out.entrances.push(...chosenEntrances.map((x) => x.entrance));
  out.entranceLine.push(...chosenEntrances.map((x) => x.line));

  const roadQuads = (segments, width) => segments.flatMap(([a, b]) => {
    const v = sub(b, a), L = len(v);
    if (L < 0.2) return [];
    const u = mul(v, 1 / L), nn = [-u[1] * width / 2, u[0] * width / 2];
    return [[add(a, nn), add(b, nn), sub(b, nn), sub(a, nn)]];
  });
  const entranceRoad = roadQuads(out.entranceLine, S.ai);
  if (entranceRoad.length) {
    const green = [];
    for (const q0 of out.green) {
      const cuts = entranceRoad.filter((z) => quadsOverlap(q0, z, 0));
      if (!cuts.length) { green.push(q0); continue; }
      const q = signedArea(q0) >= 0 ? q0 : q0.slice().reverse();
      const teile = slabFill(q, cuts, (p) => cuts.some((z) => pointIn(p, z)), 0);
      green.push(...teile.aussen);
    }
    out.green = green;
  }

  const ringRoad = roadQuads([...ringLines, ...ringJunctionLine], S.ai);
  ringRoad.push(...entranceRoad);
  const occupied = makeIndex(Math.max(4, rowDepth * 2));
  const outerBay = [];
  for (let i = 0; i < out.ring.length; i++) {
    const a = out.ring[i], b = out.ring[(i + 1) % out.ring.length];
    const L = len(sub(b, a)); if (L < 0.2) continue;
    const dd = norm(sub(b, a));
    let nn = [-dd[1], dd[0]];
    if (!pointIn(add(mul(add(a, b), 0.5), mul(nn, 0.5)), site)) nn = [-nn[0], -nn[1]];
    for (const run of perimeterGridRuns(perimeterGrids[i], L))
      for (let t = run.edgeOffset + S.sw / 2; t < run.to; t += S.sw) {
        if (t < run.from - FIT_EPS) continue;
        const c = add(add(a, mul(dd, t)), mul(nn, -(S.ai / 2 + rowDepth / 2)));
        if (!rectClear(c, dd, nn, S.sw / 2, rowDepth / 2, site, S.es)) continue;
        const q = rect(c, dd, nn, S.sw / 2, rowDepth / 2);
        if (ringRoad.some((z) => quadsOverlap(q, z, 0.05))) continue;
        if (q.some((pp) => pointIn(pp, out.ring)
            && distToBoundary(pp, out.ring) >= S.ai / 2)) continue;
        if (occupied.hits(q)) continue;
        occupied.add(q); outerBay.push(q);
      }
  }

  const corridors = [
    ...roadQuads([...ringLines, ...ringJunctionLine], S.ai),
    ...roadQuads(out.aisleLine, S.ai),
    ...roadQuads(out.crossLine, crossWidth),
    ...entranceRoad,
  ];
  const dynamicBay = outerBay.filter((q) =>
    !corridors.some((z) => quadsOverlap(q, z, 0.05)));
  // Gegenprobe zur Cache-Annahme: Beruehrt die neue Zufahrt doch einen
  // inneren Baustein, ist er nicht invariant und der Vollauf entscheidet.
  const invariant = [...cache.staticBay, ...previous.median, ...cache.perimeterBaseCaps];
  if (invariant.some((q) => entranceRoad.some((z) => quadsOverlap(q, z, 0.05))))
    return build(site, S);
  out.bay.push(...cache.staticBay.slice(0, cache.outerInsert), ...dynamicBay,
               ...cache.staticBay.slice(cache.outerInsert));
  out.bayKind.push(...cache.staticKind.slice(0, cache.outerInsert),
                   ...dynamicBay.map(() => "rand"),
                   ...cache.staticKind.slice(cache.outerInsert));

  finishPerimeterRows(out, site, S, crossWidth, ringJunctionLine, corridors, rowDepth);
  out.perimeterStalls = out.bayKind.filter((kind) =>
    kind === "rand" || kind === "rand-innen").length;
  out.innerPerimeterStalls = out.bayKind.filter((kind) => kind === "rand-innen").length;
  out.innerStalls = out.bayKind.filter((kind) => kind === "innen").length;
  out.extraStalls = out.bayKind.filter((kind) => kind === "extra").length;
  out.stalls = out.perimeterStalls + out.innerStalls + out.extraStalls;
  if (out.stalls === 0) out.warnings.push("Keine Bucht platziert.");
  out.perimeterLine.push(...ringJunctionLine);

  const covered = [...out.bay, ...out.median, ...out.cap, ...out.green, ...corridors];
  const inCovered = makeCoverIndex(covered, Math.max(4, rowDepth));
  const teile = slabFill(site, covered, (q) => !pointIn(q, site) || inCovered(q), 0.02);
  out.fill.push(...teile.aussen); out.fillHole.push(...teile.loecher);

  ENTRANCE_REUSE.set(out, {
    ...cache, state: entranceReuseState(site, S),
    entrances: out.entrances.map((entrance) => ({ ...entrance })),
  });
  assignBayRoles(out, S);
  return out;
}


/**
 * SONDERPLAETZE: Behinderten- (blau) und Elektroplaetze (gruen).
 *
 * CS2 kennt beide und kann sie bauen. Der GENERATOR kennzeichnet sie nur
 * farblich - er entwirft das Layout, gebaut wird spaeter im Mod. Die Rolle
 * muss dorthin also mit uebergeben werden.
 *
 * Was das Buchtmass angeht: Behindertenplaetze sind in CS2 breiter
 * (`Invisible Parking Lane - Perpendicular 4.7x5.9`), 4,7 m passen aber nicht
 * in unser 3,0-Raster und 5,9 m nicht in die Reihentiefe 5,5. Hier bleiben sie
 * deshalb normal gross und nur eingefaerbt. Echte 4,7-m-Plaetze brauchten zwei
 * Rasterfelder (6,0 m) und kosteten je Sonderplatz eine normale Bucht - eine
 * eigene Entscheidung, die noch aussteht.
 *
 * WO: so nah wie moeglich an einer Ein-/Ausfahrt, denn dort laufen die
 * Fussgaenger. Und als ZUSAMMENHAENGENDER BLOCK in derselben Reihe, nicht
 * verstreut - so ist es auf echten Parkplaetzen auch. Behindertenplaetze
 * liegen dabei am naechsten, Elektroplaetze direkt daneben.
 *
 * WIE VIELE: 3 % behindert (mind. 1) und 4 % elektro (mind. 2), beides bei 8
 * gedeckelt. Bei 230 Buchten sind das 7 + 8 = 15 Stueck, also rund 6,5 %.
 */
// Ein Behindertenplatz ist BREITER. Gemessen an den Vanilla-Aufklebern:
// `ParkingLotDisabledDecal01` ist 5,0 x 6,2 gross und traegt die Spur
// `Invisible Parking Lane - Perpendicular 4.7x5.9`. Der Aufkleber belegt also
// 5,00 m, die Spur darin ist 4,70 m breit.
//
// 5,00 geht in unserem 3,0-Raster nicht auf - drei Plaetze aber schon:
// 3 x 5,00 = 15,00 m = genau 5 Rasterfelder, ohne Rest. Deshalb entstehen sie
// als DREIERGRUPPE und kosten je Gruppe 2 normale Buchten. Genauso macht es
// CS2 mit `ParkingLotDisabledDecal02`: ein Aufkleber, drei Plaetze.
const BEHINDERT_BREITE = 5.0;

/**
 * Wie viele Rasterfelder eine Behindertengruppe belegt - GERECHNET, nicht fest.
 *
 * Bei sw = 3,0 sind es 5 Felder: 5 x 3,00 = 15,00 = 3 x 5,00, exakt auf.
 * Bei anderer Buchtbreite geht das nicht auf. Fest verdrahtete 5 Felder
 * liessen die Gruppe dann ueber ihren Platz hinausragen - im zweispurigen
 * Modul gab das prompt eine Buchtueberlappung.
 *
 * Deshalb: unter 3 bis 8 Feldern die Aufteilung suchen, deren Platzbreite der
 * echten 5,00 m am naechsten kommt. Die Gruppe fuellt ihre Felder immer genau
 * aus, sonst bliebe Flaeche ungedeckt.
 */
function behindertGruppe(sw) {
  let best = null;
  for (let felder = 3; felder <= 8; felder++) {
    const spanne = felder * sw;
    const plaetze = Math.round(spanne / BEHINDERT_BREITE);
    if (plaetze < 2 || plaetze >= felder) continue;
    const breite = spanne / plaetze;
    if (breite < 4.7 || breite > 5.6) continue;
    const fehler = Math.abs(breite - BEHINDERT_BREITE);
    if (!best || fehler < best.fehler) best = { felder, plaetze, breite, fehler };
  }
  return best;
}

// Elektro ist geometrisch ein GANZ NORMALER Platz: `ParkingLotElectricDecal01`
// ist 3,2 x 6,2 und traegt dieselbe Spur wie der normale Aufkleber (2.9x5.9).
// Der Unterschied ist nur die Grafik - kostet also keine Bucht.
const ELEKTRO_ANTEIL = 0.04;
const ELEKTRO_MIN = 2;
const ELEKTRO_MAX = 8;

/**
 * SONDERPLAETZE: Behinderten- (blau) und Elektroplaetze (gruen).
 *
 * CS2 baut beide als AUFKLEBER (`StaticObjectPrefab`), die ihre eigene
 * Parkspur mitbringen. Der Generator entwirft nur das Layout; gebaut wird im
 * Mod, die Rolle muss also mitgereicht werden.
 *
 * WO: so nah wie moeglich an einer Ein-/Ausfahrt, denn dort laufen die
 * Fussgaenger. Als zusammenhaengender Block, nicht verstreut. Behindert am
 * naechsten, Elektro direkt daneben.
 *
 * OFFEN: Die Aufkleber sind 6,2 m tief, ihre Spur 5,9 - unsere Reihe ist 5,5.
 * Die Tiefe bleibt hier bei der Reihentiefe, sonst verschoebe sich die ganze
 * Reihe und mit ihr das Modul.
 */
function assignBayRoles(out, S) {
  out.bayRole = new Array(out.bay.length).fill("normal");
  out.specialStalls = { behindert: 0, elektro: 0 };
  if (!out.bay.length || !out.entranceLine || !out.entranceLine.length) return;

  const ziele = out.entranceLine.map(([, b]) => b);
  const zentrum = (q) => q.reduce((a, pt) => [a[0] + pt[0] / 4, a[1] + pt[1] / 4], [0, 0]);
  const nah = (c) => {
    let best = Infinity;
    for (const z of ziele) {
      const dx = c[0] - z[0], dy = c[1] - z[1], d = dx * dx + dy * dy;
      if (d < best) best = d;
    }
    return Math.sqrt(best);
  };
  // Achse und Tiefe einer Bucht aus ihrer eigenen Form ableiten.
  const achse = (q) => {
    const s0 = len(sub(q[1], q[0]));
    return Math.abs(s0 - S.sw) < 0.05
      ? { u: norm(sub(q[1], q[0])), tief: len(sub(q[2], q[1])) }
      : { u: norm(sub(q[2], q[1])), tief: s0 };
  };
  const reihenVon = () => {
    const m = new Map();
    for (let i = 0; i < out.bay.length; i++) {
      const { u } = achse(out.bay[i]);
      const c = zentrum(out.bay[i]);
      const quer = c[0] * -u[1] + c[1] * u[0];
      const w = ((Math.atan2(u[1], u[0]) * 180 / Math.PI) + 360) % 180;
      const key = `${out.bayKind[i]}|${w.toFixed(1)}|${quer.toFixed(2)}`;
      if (!m.has(key)) m.set(key, []);
      m.get(key).push(i);
    }
    return [...m.values()];
  };

  // --- Behindertenplaetze: eine Gruppe ersetzt mehrere normale Buchten ----
  const G = behindertGruppe(S.sw);
  const gruppen = G ? (out.bay.length >= 150 ? 2 : 1) : 0;
  for (let g = 0; g < gruppen; g++) {
    const kandidaten = reihenVon()
      .map((idx) => {
        const { u } = achse(out.bay[idx[0]]);
        return idx.slice().sort((a, b) => {
          const ca = zentrum(out.bay[a]), cb = zentrum(out.bay[b]);
          return (ca[0] * u[0] + ca[1] * u[1]) - (cb[0] * u[0] + cb[1] * u[1]);
        });
      })
      .filter((idx) => idx.length >= G.felder
                    && idx.every((i) => out.bayRole[i] === "normal"))
      .sort((a, b) => Math.min(...a.map((i) => nah(zentrum(out.bay[i]))))
                    - Math.min(...b.map((i) => nah(zentrum(out.bay[i])))));

    let gesetzt = false;
    for (const reihe of kandidaten) {
      const { u, tief } = achse(out.bay[reihe[0]]);
      const t = (i) => {
        const c = zentrum(out.bay[i]);
        return c[0] * u[0] + c[1] * u[1];
      };
      // Fuenf LUECKENLOS benachbarte Buchten, moeglichst einfahrtsnah.
      let beste = -1, besteNah = Infinity;
      for (let k = 0; k + G.felder <= reihe.length; k++) {
        let ok = true;
        for (let m = 1; m < G.felder; m++)
          if (Math.abs(t(reihe[k + m]) - t(reihe[k + m - 1]) - S.sw) > 0.05) {
            ok = false; break;
          }
        if (!ok) continue;
        const d = Math.min(...reihe.slice(k, k + G.felder)
          .map((i) => nah(zentrum(out.bay[i]))));
        if (d < besteNah) { besteNah = d; beste = k; }
      }
      if (beste < 0) continue;

      const raus = reihe.slice(beste, beste + G.felder);
      const nvec = [-u[1], u[0]];
      const start = sub(zentrum(out.bay[raus[0]]), mul(u, S.sw / 2));
      const art = out.bayKind[raus[0]];
      const neueQ = [];
      for (let k = 0; k < G.plaetze; k++)
        neueQ.push(rect(add(start, mul(u, G.breite * (k + 0.5))),
                        u, nvec, G.breite / 2, tief / 2));
      for (const i of raus.slice().sort((a, b) => b - a)) {
        out.bay.splice(i, 1); out.bayKind.splice(i, 1); out.bayRole.splice(i, 1);
      }
      for (const q of neueQ) {
        out.bay.push(q); out.bayKind.push(art); out.bayRole.push("behindert");
        out.specialStalls.behindert++;
      }
      gesetzt = true;
      break;
    }
    if (!gesetzt) break;
  }

  // --- Elektroplaetze: nur einfaerben, gleiche Groesse --------------------
  let offen = Math.max(ELEKTRO_MIN,
    Math.min(ELEKTRO_MAX, Math.round(out.bay.length * ELEKTRO_ANTEIL)));
  // Gemessen am Abstand zum BLAUEN BLOCK, nicht zur Einfahrt. Sonst landen die
  // gruenen Plaetze naeher am Eingang als die blauen: die Behindertengruppe
  // braucht mehrere freie Felder am Stueck und rutscht dadurch etwas weg,
  // waehrend Elektro jede einzelne Bucht nehmen kann.
  const blau = [];
  for (let i = 0; i < out.bay.length; i++)
    if (out.bayRole[i] === "behindert") blau.push(zentrum(out.bay[i]));
  const bezug = (c) => {
    if (!blau.length) return nah(c);
    let best = Infinity;
    for (const z of blau) {
      const dx = c[0] - z[0], dy = c[1] - z[1], d = dx * dx + dy * dy;
      if (d < best) best = d;
    }
    return Math.sqrt(best);
  };
  const sortiert = reihenVon()
    .map((idx) => idx.slice().sort((a, b) =>
      bezug(zentrum(out.bay[a])) - bezug(zentrum(out.bay[b]))))
    .sort((a, b) => bezug(zentrum(out.bay[a[0]])) - bezug(zentrum(out.bay[b[0]])));
  for (const reihe of sortiert) {
    for (const i of reihe) {
      if (offen <= 0) break;
      if (out.bayRole[i] !== "normal") continue;
      out.bayRole[i] = "elektro";
      out.specialStalls.elektro++;
      offen--;
    }
    if (offen <= 0) break;
  }

  // Nach dem Ersetzen stimmen die Zaehler nicht mehr - neu bilden.
  out.perimeterStalls = out.bayKind.filter(
    (k) => k === "rand" || k === "rand-innen").length;
  out.innerPerimeterStalls = out.bayKind.filter((k) => k === "rand-innen").length;
  out.innerStalls = out.bayKind.filter((k) => k === "innen").length;
  out.extraStalls = out.bayKind.filter((k) => k === "extra").length;
  out.stalls = out.perimeterStalls + out.innerStalls + out.extraStalls;
}

/**
 * Textbericht zur Fehlersuche. Ohne DOM, damit ihn die Seite UND die Tests
 * erzeugen koennen.
 *
 * `markers` ist entweder null (dann zaehlt das ganze Areal), ein einzelnes
 * Polygon oder eine LISTE von Polygonen - jeder Bereich wird einzeln
 * ausgewertet. Kopf, Abschnittsmasse und Rohdaten stehen nur einmal da.
 */
function debugReport(site, S, r, markers) {
  const L = [];
  const say = (t) => L.push(t);
  const f = (v, n = 2) => Number(v).toFixed(n);
  const marks = !markers || !markers.length ? [null]
    : (Array.isArray(markers[0][0]) ? markers : [markers]);

  const corridors = [], roadKind = [];
  const crossWidth = Number.isFinite(S.cw) ? S.cw : 3;
  for (const q of r.entranceQuad || []) {
    corridors.push(q.slice());
    roadKind.push("Ein-/Ausfahrt");
  }
  for (const [nm, list, width] of [["Randstrasse", r.perimeterLine, S.ai],
                                   ["Fahrgasse", r.aisleLine, S.ai],
                                   ["Verbindung", r.crossLine, crossWidth]])
    for (const [a, b] of list) {
      const v = sub(b, a), Lv = len(v);
      if (Lv < 0.2) continue;
      const u = mul(v, 1 / Lv), nn = [-u[1] * width / 2, u[0] * width / 2];
      corridors.push([add(a, nn), add(b, nn), sub(b, nn), sub(a, nn)]);
      roadKind.push(nm);
    }
  const inFill = (p) => {
    let w = 0;
    for (const q of r.fill) if (pointIn(p, q)) w++;
    for (const q of r.fillHole) if (pointIn(p, q)) w--;
    return w > 0;
  };
  const kurz = (q) => {
    let m = Infinity;
    for (let i = 0; i < q.length; i++) m = Math.min(m, len(sub(q[(i + 1) % q.length], q[i])));
    return m;
  };

  say("=== StreetBlock Linienmodell - Debug ===");
  say("Zeit         " + new Date().toISOString());
  say("Einstellung  Rand " + S.es + " | Gasse " + S.ai + " | Querstrasse "
      + crossWidth + " | Bucht " + S.sw
      + " x " + S.sl + " | Streifen " + S.md + " | Querabstand " + S.cr
      + " | Winkelregel " + (S.angleMode || "-"));
  say("Modul        " + f(S.ai + 2 * S.sl + S.md) + " m");
  say("Ergebnis     " + r.stalls + " Buchten (Rand " + r.perimeterStalls
      + ", innen " + r.innerStalls + ", nachgerueckt " + r.extraStalls + ") | "
      + r.aisles + " Fahrgassen | " + r.entranceLine.length + " Einfahrt(en) | "
      + r.angle + " Grad | " + r.parts + " Teilbereich(e)");
  say("Areal        " + f(Math.abs(signedArea(site)), 0) + " m2, " + site.length + " Ecken");
  say("Areal-Ecken  " + site.map((p) => "(" + f(p[0], 1) + "," + f(p[1], 1) + ")").join(" "));
  say("Bereiche     " + (marks[0] ? marks.length : "keiner - ganzes Areal"));

  marks.forEach((marker, nr) => {
    const inMark = (p) => !marker || pointIn(p, marker);
    const quadInMark = (q) => !marker
      || q.some((p) => pointIn(p, marker))
      || pointIn(mul(add(q[0], q[2]), 0.5), marker);

    say("");
    say("############ BEREICH " + (nr + 1) + " von " + marks.length + " ############");
    if (marker) {
      say("  " + f(Math.abs(signedArea(marker)), 0) + " m2, " + marker.length + " Ecken: "
          + marker.map((p) => "(" + f(p[0], 1) + "," + f(p[1], 1) + ")").join(" "));
    } else {
      say("  ganzes Areal");
    }

    say("");
    say("--- Inhalt ---");
    for (const [nm, list] of [["Bucht", r.bay], ["Randgruen", r.green],
                              ["Streifen", r.median], ["Kappe", r.cap],
                              ["Restfuellung", r.fill], ["Loch", r.fillHole]]) {
      const drin = list.filter(quadInMark);
      if (!drin.length) { say("  " + nm.padEnd(13) + "   0"); continue; }
      const fl = drin.reduce((a, q) => a + Math.abs(signedArea(q)), 0);
      const kn = drin.map((q) => q.length);
      say("  " + nm.padEnd(13) + String(drin.length).padStart(4) + " | "
          + f(fl, 0) + " m2 | Knoten " + Math.min(...kn) + "-" + Math.max(...kn));
    }
    const roadsIn = corridors.map((q, i) => [q, roadKind[i]]).filter((x) => quadInMark(x[0]));
    say("  " + "Fahrbahnen".padEnd(13) + String(roadsIn.length).padStart(4) + " | "
        + roadsIn.map((x) => x[1]).filter((v, i, a) => a.indexOf(v) === i).join(", "));

    /* ---- Luecken ---- */
    const xs = (marker || site).map((p) => p[0]), ys = (marker || site).map((p) => p[1]);
    const step = 0.25;
    const belegt = [...r.bay, ...r.median, ...r.cap, ...r.green, ...corridors];
    const luecken = new Map();
    let flaeche = 0;
    for (let y = Math.min(...ys); y < Math.max(...ys); y += step)
      for (let x = Math.min(...xs); x < Math.max(...xs); x += step) {
        const p = [x + step / 2, y + step / 2];
        if (!pointIn(p, site) || !inMark(p)) continue;
        flaeche += step * step;
        if (belegt.some((q) => pointIn(p, q)) || inFill(p)) continue;
        luecken.set(Math.round(p[0] / step) + ":" + Math.round(p[1] / step), p);
      }
    say("");
    say("--- Luecken (weder Belag noch Gruen) ---");
    say("  " + f(luecken.size * step * step, 1) + " m2 von " + f(flaeche, 0) + " m2 = "
        + f(100 * luecken.size * step * step / Math.max(flaeche, 1e-9), 2) + " %");
    const genutzt = new Set(), nester = [];
    for (const [k0] of luecken) {
      if (genutzt.has(k0)) continue;
      const stapel = [k0], zelle = [];
      genutzt.add(k0);
      while (stapel.length) {
        const kk = stapel.pop();
        zelle.push(luecken.get(kk));
        const ij = kk.split(":").map(Number);
        for (const d of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
          const nk = (ij[0] + d[0]) + ":" + (ij[1] + d[1]);
          if (luecken.has(nk) && !genutzt.has(nk)) { genutzt.add(nk); stapel.push(nk); }
        }
      }
      nester.push(zelle);
    }
    nester.sort((a, b) => b.length - a.length);
    for (const z of nester.slice(0, 10)) {
      const zx = z.map((p) => p[0]), zy = z.map((p) => p[1]);
      say("    " + f(z.length * step * step, 2).padStart(8) + " m2 bei ("
          + f((Math.min(...zx) + Math.max(...zx)) / 2, 1) + ", "
          + f((Math.min(...zy) + Math.max(...zy)) / 2, 1) + ")  "
          + f(Math.max(...zx) - Math.min(...zx) + step, 2) + " x "
          + f(Math.max(...zy) - Math.min(...zy) + step, 2) + " m");
    }
    if (nester.length > 10) say("    ... und " + (nester.length - 10) + " weitere");

    /* ---- Regelpruefung ---- */
    const gruenAlle = [...r.median, ...r.cap, ...r.fill].filter(quadInMark);
    const baysIn = r.bay.filter(quadInMark);
    let ueber = 0;
    for (let i = 0; i < baysIn.length; i++)
      for (let j = i + 1; j < baysIn.length; j++)
        if (quadsOverlap(baysIn[i], baysIn[j], 0.05)) { ueber++; break; }
    const aufStrasse = baysIn.filter((q) => corridors.some((z) => quadsOverlap(q, z, 0.05)));
    const ueberlappFlaeche = (ziel) => {
      let a = 0;
      for (const q of gruenAlle) {
        const qx = q.map((z) => z[0]), qy = q.map((z) => z[1]);
        for (let x = Math.min(...qx); x < Math.max(...qx); x += 0.25)
          for (let y = Math.min(...qy); y < Math.max(...qy); y += 0.25) {
            const pt = [x + 0.125, y + 0.125];
            if (pointIn(pt, q) && ziel.some((z) => pointIn(pt, z))) a += 0.0625;
          }
      }
      return a;
    };
    say("");
    say("--- Regelpruefung im Bereich ---");
    say("  Buchten ueberlappen sich    " + ueber);
    say("  Buchten auf Fahrbahn        " + aufStrasse.length
        + (aufStrasse.length ? "  bei " + aufStrasse.slice(0, 5).map((q) =>
            "(" + f((q[0][0] + q[2][0]) / 2, 1) + "," + f((q[0][1] + q[2][1]) / 2, 1) + ")").join(" ") : ""));
    say("  Gruen auf Fahrbahn          " + f(ueberlappFlaeche(corridors), 2) + " m2");
    say("  Gruen auf Bucht             " + f(ueberlappFlaeche(r.bay), 2) + " m2");
    say("  Gruenstuecke unter 0,2 m    " + gruenAlle.filter((q) => kurz(q) < 0.2).length
        + "   (CS2 zieht 0,1 m ein)");
    {
      let konflikt = 0;
      const xs2 = [], ys2 = [];
      for (const q of corridors) for (const z of q) { xs2.push(z[0]); ys2.push(z[1]); }
      if (xs2.length) {
        const st = 0.25;
        for (let x = Math.min(...xs2); x < Math.max(...xs2); x += st)
          for (let y = Math.min(...ys2); y < Math.max(...ys2); y += st) {
            const pt = [x + st / 2, y + st / 2];
            if (!inMark(pt)) continue;
            const arten = new Set();
            for (let i = 0; i < corridors.length; i++)
              if (pointIn(pt, corridors[i])) arten.add(roadKind[i]);
            if (arten.size > 1) konflikt += st * st;
          }
      }
      say("  Strasse auf Strasse         " + f(konflikt, 2) + " m2");
    }
    say("  Streifen falsche Tiefe      "
        + r.median.filter(quadInMark).filter((q) => Math.abs(kurz(q) - S.md) > 0.05).length
        + "   (Soll " + S.md + " m)");

    /* ---- Fahrgassen ---- */
    say("");
    say("--- Fahrgassen im Bereich ---");
    let gezeigt = 0;
    r.aisleLine.forEach(([a, b], i) => {
      if (marker && !inMark(a) && !inMark(b) && !inMark(mul(add(a, b), 0.5))) return;
      gezeigt++;
      const eigen = corridors[r.perimeterLine.length + i];
      const abst = [a, b].map((p) => {
        let best = Infinity;
        for (let k = 0; k < corridors.length; k++) {
          if (corridors[k] === eigen) continue;
          best = Math.min(best, pointIn(p, corridors[k]) ? 0 : distToBoundary(p, corridors[k]));
        }
        return best === Infinity ? -1 : best;
      });
      say("  Gasse " + i + ": " + f(len(sub(b, a)), 1) + " m von ("
          + f(a[0], 1) + "," + f(a[1], 1) + ") nach (" + f(b[0], 1) + "," + f(b[1], 1)
          + ") | Enden " + f(abst[0]) + " / " + f(abst[1]) + " m zur naechsten Fahrbahn"
          + (Math.max(abst[0], abst[1]) > 1 ? "   <== SACKGASSE" : ""));
    });
    if (!gezeigt) say("  keine");
  });

  /* ---- einmal am Ende ---- */
  const imBereich = (q) => marks.some((m) => !m
    || q.some((p) => pointIn(p, m)) || pointIn(mul(add(q[0], q[2]), 0.5), m));
  say("");
  say("--- Abschnitte (gesamt) ---");
  say("  " + r.sections.length + " Abschnitte | Kapseln " + (r.sections.length
    ? f(Math.min(...r.sections.map((x) => Math.min(x.cap0, x.cap1)))) + " - "
      + f(Math.max(...r.sections.map((x) => Math.max(x.cap0, x.cap1)))) + " m (Soll "
      + f(S.sw) + " bis " + f(2 * S.sw) + ")"
    : "-"));
  if (r.warnings.length) { say(""); say("WARNUNGEN"); for (const w of r.warnings) say("  " + w); }

  say("");
  say("--- Rohdaten (JSON) ---");
  say(JSON.stringify({ site: site, marker: marks[0] ? marks : null, settings: S,
    stalls: r.stalls, angle: r.angle, aisles: r.aisles,
    bay: r.bay.filter(imBereich), median: r.median.filter(imBereich),
    cap: r.cap.filter(imBereich), fill: r.fill.filter(imBereich),
    fillHole: r.fillHole.filter(imBereich), green: r.green.filter(imBereich),
    perimeterLine: r.perimeterLine, entranceLine: r.entranceLine,
    entranceQuad: r.entranceQuad,
    aisleLine: r.aisleLine, crossLine: r.crossLine }));
  return L.join("\n");
}

const PRESETS = {
  rect: [[0, 0], [120, 0], [120, 90], [0, 90]],
  l:    [[0, 0], [120, 0], [120, 45], [60, 45], [60, 90], [0, 90]],
  skew: [[0, 0], [120, 0], [150, 90], [30, 90]],
  // Areal aus VideoFrames/parking_08s.png: Rechteck zwischen French Pl (oben),
  // E 32nd St (links), Edgewood Ave (rechts); unten links von Breeze Ter schraeg
  // und geschwungen abgeschnitten. ~0,3 m je Bildpunkt -> 127 x 112 m.
  ref:  [[0, 0], [127, 0], [127, 112], [110, 106], [95, 100], [80, 92],
         [68, 83], [56, 74], [43, 67], [23, 64], [0, 56]],
};

// Masse im Frame sind Fuss: Bucht 9 x 18 ft, Fahrgasse ~24 ft, Setback 10 ft.
const REF_SETTINGS = {
  es: 3, ai: 7.3, cw: 3, sl: 5.5, sw: 2.7, md: 4, cr: 60, auto: true, angle: 0,
};

/**
 * GEMESSENE CS2-Masse, Stand 2026-08-01. Aus dem Laufzeit-Dump des Mods
 * gelesen, nicht geschaetzt und nicht aus Prefab-Namen gedeutet.
 *
 * Zielmodul ist der VANILLA-Bauweg: `Invisible Road Path - 2xTwoway
 * 2xPerpendicular`, ein unsichtbarer Weg mit zwei senkrechten Parkreihen.
 * So sind die Vanilla-Parkplatz-Assets aufgebaut (Asphalt und Gruen als
 * Flaechen, unsichtbare Wege als Fahrgassen) - NICHT als Strasse mit
 * angebauten Parkstreifen.
 *
 *      2 x 3,0 m Fahrgasse   (Invisible Car Oneway Section 3)
 *   +  2 x 0,5 m Gehstreifen (Invisible Pedestrian Section 0.5)
 *   +  2 x 5,5 m Parken      (Invisible Parking Section 5.5)
 *   = 18,00 m
 *
 * ACHTUNG, FALLE: `m_DefaultWidth` meldet fuer dieses Prefab nur **7,00 m**,
 * dieselbe Zahl wie die Variante OHNE Parken. Bei den unsichtbaren Wegen
 * zaehlt das Feld nur den befahrbaren Kern, die Parkstreifen ragen darueber
 * hinaus. Bei den Strassen war `m_DefaultWidth` dagegen die Gesamtbreite.
 * Gleiches Feld, zwei Bedeutungen - deshalb die Abschnitte summieren.
 *
 * Abbildung auf unsere Parameter:
 *   ai = Fahrgasse + beide Gehstreifen = 6 + 2 x 0,5 = 7,0
 *   cw = Querstrasse `Invisible Car Path - 1xTwoway`       = 3,0
 *   sl = Tiefe des Parkstreifens                    = 5,5
 *   Probe: ai + 2*sl = 7 + 11 = 18,00 = Breite des Prefabs
 *
 * WICHTIG: die 18,00 m sind der MINDESTABSTAND zweier Fahrgassen, nicht der
 * vorgeschriebene. CS2 zwingt uns zu keinem `md` - genau wie `es` und `cr`
 * bleibt der Mittelstreifen unsere Entwurfsgroesse, und der Achsabstand ist
 * 18,00 + md. `md` = 0 waere die dichteste Packung (die Module stossen an),
 * loescht aber saemtliche Mittelstreifen: gemessen am Rechteck 487 m2 Gruen
 * gegen 0, fuer 26 Buchten mehr. Das verstoesst gegen die Gruenstreifen-Regel,
 * deshalb 2,5 - derselbe Wert wie `MedianWidth` im Mod, damit beide Seiten
 * dieselbe Vorgabe haben.
 *
 * Die Bucht ist `Pathway Parking Lane - Perpendicular 3x5.5`, gelesen aus
 * `NetPieceLanes` des Stuecks `Invisible Parking Piece 5.5`:
 * **3,0 x 5,5**, `m_SlotInterval` 3,0 - die Buchten stossen laengs an, es
 * bleibt keine Fuge. Also sw = 3,0.
 *
 * es (Randgruen) und cr (Querabstand) gibt CS2 NICHT vor - das bleiben
 * unsere eigenen Entwurfsgroessen.
 */
const CS2_SETTINGS = {
  es: 1, ai: 7, cw: 3, sl: 5.5, sw: 3, md: 2.5, cr: 34, auto: true, angle: 0,
};

/**
 * Das zweispurige Gegenstueck `Alley - Double Sided Parking`, 24,00 m:
 * 2 x 3,0 Fahrbahn + 2 x 1,0 Schulter + 2 x 8,0 Parkstreifen.
 *
 * Achtung, hier klaffen Streifen und Bucht auseinander: der Streifen ist
 * 8,0 m tief, die Bucht aber nur 6,5 m lang (`Alley Parking Lane -
 * Perpendicular 3x6.5`). Die 1,5 m Ueberschuss braucht CS2 fuer die
 * Schraegvariante - 6,5 * sin 67 + 2,9 * cos 67 = 7,11 m. `sl` steht deshalb
 * auf der Streifentiefe, nicht auf der Buchtlaenge: belegt wird der Streifen.
 *
 * Kostet 8 m Breite bei gleicher Buchtzahl wie die Einbahnvariante und ist
 * damit die schlechtere Wahl - hier nur zum Vergleichen.
 */
const CS2_SETTINGS_ZWEISPURIG = {
  es: 1, ai: 8, cw: 3, sl: 8, sw: 3, md: 0, cr: 34, auto: true, angle: 0,
};

// Wieviel Buchtenzahl darf die knotenaermere Loesung kosten? 0 = nur Buchten
// zaehlen, 1 = nur Knoten. Wert gemessen gewaehlt, siehe Auswahlblock.
const STALL_TOLERANCE = 0.05;

/**
 * Kleinster sinnvoller gerader Lauf fuer innere Randbuchten. Referenz 08s:
 * Markierung 2 bietet hoechstens 1 Rasterplatz, Markierung 4 dagegen 4.
 * Drei Plaetze trennen beide Urteile und entsprechen mit CS2-Massen 9,0 m.
 */
const MIN_INNER_RING_RUN = 3;
// Nur nahezu rechtwinklige Querlaeufe werden strenger behandelt: im L-Fall
// verlieren drei 90-Grad-Buchten gegen die dadurch von 7 auf 10 wachsende Reihe.
const MIN_TRANSVERSE_INNER_RING_RUN = 4;
const MIN_TRANSVERSE_ROW_ANGLE = 75;

/**
 * Nur Fahrbahnen, deren Richtung mindestens so nah an der Querrichtung wie an
 * der Reihenrichtung liegt, loesen die pauschale Randkapsel aus. Mess-Sweep:
 * falsche Treffer 8,53-19,77 Grad, naechste echte Querung 45,00 Grad.
 */
const MIN_TRANSVERSE_CAP_ANGLE = 45;

/**
 * Ab welchem Knick gilt eine konkave Ecke als KERBE, an der die Randstrasse
 * als Fahrgasse fortgesetzt wird? Gemessen: bei 0 Grad greift die Regel auch
 * an den flachen Knicken von Referenz 08s und kostet dort 9 Buchten.
 */
const MIN_NOTCH_TURN = 60;

const ROAD_COLORS = Object.freeze({
  perimeter: "#5f6270", entrance: "#b15f36", connector: "#3979ad",
  aisle: "#3f8b72", extension: "#d4537e",
});

const LinienModel = {
  sub, add, mul, len, norm, signedArea, pointIn, rect, distToBoundary, insideBy,
  rectClear, quadsOverlap, offsetRing, offsetRingParts, lineCrossings, clipSegment, decompose,
  slabFill,
  longestEdgeAngle, sideGap, entranceCornerCanSnap, entranceCornerFit,
  build, rebuildEntrances, debugReport, assignBayRoles,
  PRESETS, REF_SETTINGS, CS2_SETTINGS, CS2_SETTINGS_ZWEISPURIG,
  MIN_TRANSVERSE_CAP_ANGLE, ENTRANCE_SNAP_COS, entranceQuadOf, ROAD_COLORS,
};
if (typeof window !== "undefined") window.LinienModel = LinienModel;
if (typeof module !== "undefined" && module.exports) module.exports = LinienModel;
