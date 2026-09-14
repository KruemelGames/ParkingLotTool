// Kopfloser Test des Linienmodells. Benutzt denselben Code wie die Seite.
//   node parking-linien-test.cjs
const { build, signedArea, pointIn, len, sub, add, mul, rect, quadsOverlap,
        offsetRing, distToBoundary, PRESETS, REF_SETTINGS, CS2_SETTINGS,
        CS2_SETTINGS_ZWEISPURIG, MIN_TRANSVERSE_CAP_ANGLE,
        entranceCornerFit, norm } =
  require("./parking-linien-model.js");

// Gemessene CS2-Masse (Alley Oneway - Double Sided Parking, 16 m Modul).
// Vorher standen hier geschaetzte Werte: ai 9, sl 5.9, sw 2.9, md 4.5.
const BASE = { ...CS2_SETTINGS };

// Die alten Schaetzwerte bleiben als Vergleich stehen - sonst sieht man
// nicht, was die Eichung gekostet oder gebracht hat.
const GESCHAETZT = { es: 1, ai: 9, cw: 3, sl: 5.9, sw: 2.9, md: 4.5, cr: 34,
                     auto: true, angle: 0 };

/** Strassenmittellinie zu einem Rechteck der Fahrbahnbreite. */
function corridor(a, b, w) {
  const v = sub(b, a), L = len(v);
  if (L < 0.2) return null;
  const u = mul(v, 1 / L), nn = [-u[1] * w / 2, u[0] * w / 2];
  return [add(a, nn), add(b, nn), sub(b, nn), sub(a, nn)];
}
/**
 * Die Fahrbahnflaechen. Randstrasse und Einfahrt bringen ihre FLAECHE selbst
 * mit (`perimeterQuad`, `entranceQuad`) - die Randstrasse mit gehrten Ecken,
 * die Einfahrt an einer schraegen Ecke als Parallelogramm. Aus den
 * Mittellinien nachgebaut waeren beide Rechtecke, und der Test wuerde etwas
 * anderes messen als das Modell rechnet: beim Entfernen der Eckstummel
 * meldete er so 0,5 bis 0,9 % Loecher, die es gar nicht gab.
 */
function corridors(r, S) {
  const out = [];
  const cw = Number.isFinite(S.cw) ? S.cw : 3;
  for (const q of r.perimeterQuad || []) out.push(q.slice());
  for (const q of r.entranceQuad || []) out.push(q.slice());
  const ringRest = r.perimeterLine.slice((r.perimeterQuad || []).length);
  for (const [list, width] of [[ringRest, S.ai], [r.aisleLine, S.ai],
                               [r.crossLine, cw]])
    for (const [a, b] of list) {
      const q = corridor(a, b, width);
      if (q) out.push(q);
    }
  return out;
}

/** Anteil des Areals, den keine erzeugte Flaeche deckt. */
function coverGap(site, r, S, step = 0.5) {
  const all = [...r.bay, ...r.median, ...r.cap, ...r.green, ...corridors(r, S)];
  // Die Restfuellung zaehlt mit Nonzero-Regel: Aussenring plus, Loch minus.
  const inFill = (p) => {
    let w = 0;
    for (const q of r.fill) if (pointIn(p, q)) w++;
    for (const q of r.fillHole) if (pointIn(p, q)) w--;
    return w > 0;
  };
  const xs = site.map((p) => p[0]), ys = site.map((p) => p[1]);
  let total = 0, gap = 0;
  for (let y = Math.min(...ys) + step / 2; y < Math.max(...ys); y += step)
    for (let x = Math.min(...xs) + step / 2; x < Math.max(...xs); x += step) {
      const p = [x, y];
      if (!pointIn(p, site)) continue;
      total += step * step;
      // Ein Stichprobenpunkt auf einer gemeinsamen Kante hat Flaeche null.
      // Ohne Epsilon wurden bei mittiger Referenzeinfahrt 11 solcher Punkte
      // trotzdem als je step*step grosses Loch gezaehlt.
      if (!all.some((q) => pointIn(p, q)) && !inFill(p)
          && !all.some((q) => distToBoundary(p, q) <= 1e-6))
        gap += step * step;
    }
  return { total, gap };
}

/* ---------- Messungen statt Behauptungen ---------- */

/** Buchten, die sich mit einer anderen Bucht ueberlappen. */
function overlapCount(bays) {
  let n = 0;
  for (let i = 0; i < bays.length; i++)
    for (let j = i + 1; j < bays.length; j++)
      if (quadsOverlap(bays[i], bays[j], 0.05)) { n++; break; }
  return n;
}

/**
 * Buchten, die auf einer Fahrbahn liegen. Muss 0 bleiben - der gelockerte
 * Randabstand beim Setzen darf die Buchten nicht auf die Randstrasse schieben.
 */
function bayOnRoad(r, S) {
  const roads = corridors(r, S);
  let n = 0;
  for (const b of r.bay) if (roads.some((q) => quadsOverlap(b, q, 0.05))) n++;
  return n;
}

/**
 * Flaeche, auf der zwei Strassen uebereinander liegen. In CS2 ist das verboten;
 * die Rangfolge lautet Randstrasse > Fahrgasse > Verbindung, jede niedrigere
 * endet am Rand der hoeheren.
 */
function roadConflict(r, S) {
  const bau = (list, width) => list.map(([a, b]) => corridor(a, b, width)).filter(Boolean);
  const cw = Number.isFinite(S.cw) ? S.cw : 3;
  const R = bau(r.perimeterLine, S.ai), G = bau(r.aisleLine, S.ai),
        V = bau(r.crossLine, cw), E = bau(r.entranceLine, S.ai);
  const paar = (A, C) => {
    const alle = [...A, ...C];
    if (!alle.length) return 0;
    const xs = [], ys = [];
    for (const q of alle) for (const z of q) { xs.push(z[0]); ys.push(z[1]); }
    const st = 0.25;
    let a = 0;
    for (let x = Math.min(...xs); x < Math.max(...xs); x += st)
      for (let y = Math.min(...ys); y < Math.max(...ys); y += st) {
        const p2 = [x + st / 2, y + st / 2];
        if (!A.some((q) => pointIn(p2, q))) continue;
        if (C.some((q) => pointIn(p2, q))) a += st * st;
      }
    return a;
  };
  return { rg: paar(R, G), rv: paar(R, V), gv: paar(G, V),
           re: paar(R, E), ge: paar(G, E), ve: paar(V, E) };
}

/** Kleinster Abstand zweier endlicher Mittelliniensegmente. */
function segmentDistance([a, b], [c, d]) {
  const pointToSegment = (p, x, y) => {
    const xy = sub(y, x), L2 = xy[0] * xy[0] + xy[1] * xy[1];
    if (L2 < 1e-12) return len(sub(p, x));
    const t = Math.max(0, Math.min(1,
      ((p[0] - x[0]) * xy[0] + (p[1] - x[1]) * xy[1]) / L2));
    return len(sub(p, add(x, mul(xy, t))));
  };
  const ab = sub(b, a), cd = sub(d, c), ac = sub(c, a);
  const cross = (u, v) => u[0] * v[1] - u[1] * v[0];
  const den = cross(ab, cd);
  if (Math.abs(den) > 1e-9) {
    const t = cross(ac, cd) / den, s = cross(ac, ab) / den;
    if (t >= -1e-9 && t <= 1 + 1e-9 && s >= -1e-9 && s <= 1 + 1e-9)
      return 0;
  }
  return Math.min(pointToSegment(a, c, d), pointToSegment(b, c, d),
                  pointToSegment(c, a, b), pointToSegment(d, a, b));
}

/** Fahrbahnen, die ueber keine Beruehrungskette mit dem Randring verbunden sind. */
function unreachableRoads(r, S) {
  const roads = [];
  const addRoads = (segments, width, isRing) => {
    for (const segment of segments) roads.push({ segment, width, isRing });
  };
  const cw = Number.isFinite(S.cw) ? S.cw : 3;
  addRoads(r.perimeterLine, S.ai, true);
  addRoads(r.aisleLine, S.ai, false);
  addRoads(r.crossLine, cw, false);
  addRoads(r.entranceLine, S.ai, false);

  // Dualer Graph: Fahrbahnen sind hier die Knoten; eine Beruehrung erzeugt die
  // Nachbarschaft. Fuer die Erreichbarkeit ist das zum Beruehrpunkt-Graphen
  // (Beruehrpunkte = Knoten, Fahrbahnen = Kanten) aequivalent.
  const reached = new Set(), open = [];
  roads.forEach((road, i) => {
    if (road.isRing) { reached.add(i); open.push(i); }
  });
  while (open.length) {
    const i = open.pop(), a = roads[i];
    for (let j = 0; j < roads.length; j++) {
      if (reached.has(j)) continue;
      const b = roads[j];
      if (segmentDistance(a.segment, b.segment)
          > (a.width + b.width) / 2 + 0.2) continue;
      reached.add(j);
      open.push(j);
    }
  }
  return roads.reduce((n, road, i) =>
    n + (!road.isRing && !reached.has(i) ? 1 : 0), 0);
}

/** Strassenenden, die ausserhalb des Polygons liegen. */
function endsOutside(r, site) {
  let n = 0;
  for (const [a, b] of [...r.aisleLine, ...r.crossLine])
    for (const p of [a, b]) if (!pointIn(p, site)) n++;
  return n;
}

/**
 * Einfahrten: Anzahl laut Laengstkantenregel, beide Anschlussfugen, rechter
 * Winkel zur lokalen Randstrasse, Laenge der Mittellinie und beidseitig exakt
 * eine Buchtbreite Gruen bis zur ersten aeusseren Randbucht.
 */
function entranceCheck(r, site, S) {
  const edgeLengths = site.map((a, i) => len(sub(site[(i + 1) % site.length], a)));
  const longest = Math.max(...edgeLengths);
  const expected = edgeLengths.filter((L) =>
    longest - L <= Math.max(1e-6, longest * 1e-9)).length;
  const centerError = r.entrances.reduce((worst, entrance) =>
    Math.max(worst, Math.abs(entrance.along - edgeLengths[entrance.edge] / 2)), 0);
  const ringSegments = r.perimeterLine.slice(0, r.ring.length);
  const ringRoads = ringSegments.map((segment) =>
    corridor(segment[0], segment[1], S.ai)).filter(Boolean);
  const entranceRoads = r.entranceLine.map((segment) =>
    corridor(segment[0], segment[1], S.ai));
  let siteGap = 0, ringGap = 0, overlap = 0;
  let angleLo = Infinity, angleHi = 0, lengthLo = Infinity, lengthHi = 0;
  for (let i = 0; i < r.entranceLine.length; i++) {
    const [a, b] = r.entranceLine[i];
    siteGap = Math.max(siteGap, distToBoundary(a, site));
    if (ringRoads.some((q) => quadsOverlap(entranceRoads[i], q, 1e-6))) overlap++;
    const eu = mul(sub(b, a), 1 / len(sub(b, a)));
    let best = null;
    for (const segment of ringSegments) {
      const q = corridor(segment[0], segment[1], S.ai);
      if (!q) continue;
      const gap = distToQuad(b, q);
      if (!best || gap < best.gap) best = { gap, segment };
    }
    if (!best) continue;
    ringGap = Math.max(ringGap, best.gap);
    const ru = mul(sub(best.segment[1], best.segment[0]),
      1 / len(sub(best.segment[1], best.segment[0])));
    const dot = Math.min(1, Math.abs(eu[0] * ru[0] + eu[1] * ru[1]));
    const angle = Math.acos(dot) * 180 / Math.PI;
    angleLo = Math.min(angleLo, angle); angleHi = Math.max(angleHi, angle);
    const L = len(sub(b, a));
    lengthLo = Math.min(lengthLo, L); lengthHi = Math.max(lengthHi, L);
  }
  if (!r.entranceLine.length)
    angleLo = angleHi = lengthLo = lengthHi = 0;
  return { count: r.entranceLine.length, expected, centerError,
           siteGap, ringGap, overlap, angleLo, angleHi, lengthLo, lengthHi,
           bayGaps: entranceBayGaps(r, S) };
}

/** Laengsabstand jedes Einfahrtsrandes zur ersten aeusseren Randbucht. */
function entranceBayGaps(r, S) {
  const dot = (a, b) => a[0] * b[0] + a[1] * b[1];
  const center = (q) => mul(q.reduce((sum, p) => add(sum, p), [0, 0]), 1 / q.length);
  return r.entranceLine.map(([a, b]) => {
    const ev = sub(b, a), inward = mul(ev, 1 / len(ev));
    const row = [-inward[1], inward[0]], intervals = [];
    r.bay.forEach((q, i) => {
      if (r.bayKind[i] !== "rand") return;
      const qv = sub(q[1], q[0]), qu = mul(qv, 1 / len(qv));
      if (Math.abs(dot(qu, row)) < 1 - 1e-8) return;
      if (Math.abs(dot(sub(center(q), a), inward) - (S.es + S.sl / 2)) > 0.05)
        return;
      const along = q.map((p) => dot(sub(p, a), row));
      intervals.push({ lo: Math.min(...along), hi: Math.max(...along) });
    });
    const entranceLo = -S.ai / 2, entranceHi = S.ai / 2;
    const left = intervals.filter((x) => x.hi <= entranceLo + 1e-8);
    const right = intervals.filter((x) => x.lo >= entranceHi - 1e-8);
    return [left.length ? entranceLo - Math.max(...left.map((x) => x.hi)) : Infinity,
            right.length ? Math.min(...right.map((x) => x.lo)) - entranceHi : Infinity];
  });
}


/** Stufenlose Vorgaben muessen Position UND beidseitigen Rasterneustart behalten. */
function entranceRasterProbe(site, S, offset) {
  const edgeLengths = site.map((a, i) => len(sub(site[(i + 1) % site.length], a)));
  const longest = Math.max(...edgeLengths);
  const requests = edgeLengths.map((L, edge) => ({ edge, L }))
    .filter(({ L }) => longest - L <= Math.max(1e-6, longest * 1e-9))
    .map(({ edge, L }) => ({ edge, along: L / 2 + offset }));
  const settings = { ...S, entrances: requests };
  const r = build(site, settings);
  const gaps = entranceBayGaps(r, settings);
  const positionError = requests.reduce((worst, request, i) =>
    Math.max(worst, r.entrances[i]
      ? Math.abs(r.entrances[i].along - request.along) : Infinity), 0);
  const gapError = gaps.reduce((worst, pair) =>
    Math.max(worst, ...pair.map((gap) => Math.abs(gap - S.sw))), 0);
  return { r, gaps, positionError, gapError };
}

/**
 * Abstand der Fahrgassenenden zur Randstrassen-Mittellinie. Soll 0 sein: die
 * Gasse muendet in die Randstrasse. Gemessen wird gegen den Ring, nicht gegen
 * die Grundstueckskante.
 */
function aisleToRing(r, site, S) {
  // Gegen den FAHRBAHNRAND messen, nicht gegen die Mittellinie: die Gasse endet
  // seit dem Umbau am Rand der Randstrasse, damit sich in CS2 keine zwei
  // Strassen ueberlagern. 0 heisst also "beruehrt sauber".
  // OHNE die Fahrgassen selbst. Vorher stand hier `corridors(r, S)` - darin
  // steckt auch der Korridor der Gasse, in dem ihr eigener Endpunkt
  // natuerlich liegt. `pointIn` lieferte true, der Abstand also 0, und die
  // Pruefung meldete stur 0,00 m. Eine echte Fuge von 0,22 m blieb dadurch
  // unentdeckt.
  const cw = Number.isFinite(S.cw) ? S.cw : 3;
  const roads = [...(r.perimeterQuad || []), ...(r.entranceQuad || [])];
  for (const [a, b] of r.perimeterLine.slice((r.perimeterQuad || []).length)) {
    const q = corridor(a, b, S.ai);
    if (q) roads.push(q);
  }
  for (const [a, b] of r.crossLine) {
    const q = corridor(a, b, cw);
    if (q) roads.push(q);
  }
  let worst = 0, sum = 0, n = 0;
  for (const [a, b] of r.aisleLine)
    for (const p of [a, b]) {
      let best = Infinity;
      for (const q of roads) best = Math.min(best, pointIn(p, q) ? 0 : distToBoundary(p, q));
      if (best === Infinity) continue;
      worst = Math.max(worst, best); sum += best; n++;
    }
  return { worst, avg: n ? sum / n : 0, n };
}

/** Kuerzeste Seite eines Vierecks - bei Streifen die Tiefe. */
function shortSide(q) {
  let m = Infinity;
  for (let i = 0; i < q.length; i++)
    m = Math.min(m, len(sub(q[(i + 1) % q.length], q[i])));
  return m;
}
function longSide(q) {
  let m = 0;
  for (let i = 0; i < q.length; i++)
    m = Math.max(m, len(sub(q[(i + 1) % q.length], q[i])));
  return m;
}

/** Streifentiefe gegen das Sollmass. */
function stripCheck(r, S) {
  let bad = 0;
  for (const q of r.median) if (Math.abs(shortSide(q) - S.md) > 0.05) bad++;
  return bad;
}

/**
 * Kappentiefe soll 2*sl+md (Doppelreihe), sl+md (Einzelreihe mit
 * Mittelstreifen) oder sl (Randreihe ohne Mittelstreifen) sein. Die ANDERE
 * Seite ist dann die Kapsellaenge und muss in [sw, 2*sw) liegen.
 */
function capCheck(r, S) {
  const depths = [2 * S.sl + S.md, S.sl + S.md, S.sl];
  const passt = (L) => L >= S.sw - 0.05 && L < 2 * S.sw + 0.05;
  let bad = 0, lo = Infinity, hi = 0, unknown = 0;
  for (const q of r.cap) {
    const a = shortSide(q), b = longSide(q);
    // BLINDER FLECK, behoben: 5,5 und 8,0 sind BEIDE gueltige Solltiefen
    // (sl und sl+md). Bei einer Kappe 5,5 x 8,0 nahm der Test die erste
    // passende Seite als Tiefe - also 8,0 - und hielt 5,5 fuer die Laenge.
    // Die liegt in [3,6), also kein Fehler. Tatsaechlich war die Kappe 8 m
    // LANG, wo hoechstens 6 erlaubt sind.
    // Jetzt gilt eine Deutung nur, wenn die ANDERE Seite auch als Laenge
    // durchgeht; erst wenn keine passt, wird gemeldet.
    const kandidaten = [];
    for (const t of depths) {
      if (Math.abs(a - t) < 0.05) kandidaten.push(b);
      if (Math.abs(b - t) < 0.05) kandidaten.push(a);
    }
    if (!kandidaten.length) { unknown++; continue; }
    const L = kandidaten.find(passt) ?? kandidaten[0];
    lo = Math.min(lo, L); hi = Math.max(hi, L);
    if (!passt(L)) bad++;
  }
  return { bad, unknown, lo: r.cap.length ? lo : 0, hi };
}

/**
 * Die Abschnittsrechnung selbst: 2*Kapsel + Buchten*sw muss die nutzbare
 * Laenge L ergeben, und die Kapsel muss in [sw, 1,5*sw) liegen.
 */
function sectionCheck(r, S) {
  let badSum = 0, badCap = 0, lo = Infinity, hi = 0;
  for (const s of r.sections) {
    if (Math.abs(s.cap0 + s.cap1 + s.bays * S.sw - s.L) > 1e-6) badSum++;
    for (const c of [s.cap0, s.cap1]) {
      // Regel: Kapsel >= 1 Bucht und < 2 Buchten.
      if (c < S.sw - 1e-6 || c >= 2 * S.sw + 1e-6) badCap++;
      lo = Math.min(lo, c); hi = Math.max(hi, c);
    }
  }
  // Ein Abschnitt gehoert seit dem Umbau zu EINER Reihe, nicht zur Fahrgasse -
  // also nicht mehr verdoppeln.
  const planned = r.sections.reduce((a, s) => a + s.bays, 0);
  return { n: r.sections.length, badSum, badCap, planned,
           lo: r.sections.length ? lo : 0, hi };
}

/**
 * Fuge zwischen Kappe und der naechsten Bucht. Soll 0 sein - ist sie eine
 * Buchtbreite gross, wurde die erste Bucht des Abschnitts verworfen.
 */
function capToBay(r) {
  let worst = 0;
  for (const q of r.cap) {
    let best = Infinity;
    for (const b of r.bay) for (const p of q) for (const s of b)
      best = Math.min(best, len(sub(p, s)));
    if (best !== Infinity) worst = Math.max(worst, best);
  }
  return worst;
}

/**
 * Fuge zwischen Gruenstreifen und Kappe. Gemessen an den Mittelpunkten der
 * beiden SCHMALEN Seiten des Streifens - das sind seine Enden. Die Ecken taugen
 * dafuer nicht: Streifen und Kappe sind unterschiedlich tief, ihre Ecken liegen
 * also auch bei buendigem Anschluss auseinander.
 */
function stripToCap(r, S) {
  const far = 3 * S.sw;   // weiter weg heisst: an diesem Ende steht gar keine Kappe
  let worst = 0, gaps = 0, orphan = 0, ends = 0;
  for (const q of r.median) {
    const mids = [];
    for (let i = 0; i < 4; i++) {
      const a = q[i], b = q[(i + 1) % 4];
      mids.push({ d: len(sub(b, a)), p: mul(add(a, b), 0.5) });
    }
    mids.sort((x, y) => x.d - y.d);
    for (const m of mids.slice(0, 2)) {
      ends++;
      let best = Infinity;
      for (const c of r.cap) best = Math.min(best, distToQuad(m.p, c));
      if (best > far) { orphan++; continue; }
      if (best > 0.05) { gaps++; worst = Math.max(worst, best); }
    }
  }
  return { ends, gaps, orphan, worst };
}

/** Punkt-zu-Viereck, 0 wenn innen. */
function distToQuad(p, q) {
  return pointIn(p, q) ? 0 : distToBoundary(p, q);
}

/**
 * Fuge zwischen Kappe und Fahrbahn. Die Kappe soll den Fahrbahnrand beruehren -
 * genau die Luecke war die Meldung am alten Raster.
 */
function capToRoad(r, S) {
  const roads = corridors(r, S);
  let worst = 0;
  for (const q of r.cap) {
    let best = Infinity;
    for (const p of q) for (const road of roads) best = Math.min(best, distToQuad(p, road));
    if (best !== Infinity) worst = Math.max(worst, best);
  }
  return worst;
}

/**
 * Laengsfluchtung der einander zugewandten Reihen benachbarter Fahrgassen.
 * Gemessen wird direkt an den Buchtkanten: Alle Kanten eines Nachbargassen-
 * paars muessen modulo `sw` auf demselben Laengsraster liegen. Rand- und
 * Nachrueckbuchten haben eigene Raster und gehoeren deshalb nicht hierher.
 */
function rowAlignment(r, S) {
  const dot = (a, b) => a[0] * b[0] + a[1] * b[1];
  const center = (q) => q.reduce((s, p) => add(s, mul(p, 1 / q.length)), [0, 0]);
  const moduloGap = (a, b) => {
    const d = ((a - b) % S.sw + S.sw) % S.sw;
    return Math.min(d, S.sw - d);
  };
  const bays = r.bay.map((q, i) => ({ q, kind: r.bayKind[i], c: center(q) }))
    .filter((b) => b.kind === "innen");
  const aisles = r.aisleLine.map(([a, b], i) => {
    let u = mul(sub(b, a), 1 / len(sub(b, a)));
    // Gleiche geometrische Richtung auch bei umgekehrter Linienreihenfolge.
    if (u[0] < -1e-9 || (Math.abs(u[0]) <= 1e-9 && u[1] < 0)) u = mul(u, -1);
    const n = [-u[1], u[0]];
    return { a, b, i, u, n,
      lo: Math.min(dot(a, u), dot(b, u)),
      hi: Math.max(dot(a, u), dot(b, u)) };
  });
  const rowOffset = S.ai / 2 + S.sl / 2;
  const module = S.ai + 2 * S.sl + S.md;
  let worst = 0, pairs = 0, edges = 0;
  for (let i = 0; i < aisles.length; i++)
    for (let j = i + 1; j < aisles.length; j++) {
      const A = aisles[i], B = aisles[j];
      if (Math.abs(dot(A.u, B.u)) < 0.999999) continue;
      const separation = Math.abs(dot(sub(B.a, A.a), A.n));
      if (Math.abs(separation - module) > 0.5) continue;
      const lo = Math.max(A.lo, B.lo), hi = Math.min(A.hi, B.hi);
      if (hi - lo < S.sw) continue;
      const midA = mul(add(A.a, A.b), 0.5), midB = mul(add(B.a, B.b), 0.5);
      const toward = Math.sign(dot(sub(midB, midA), A.n)) || 1;
      const row = (X, side) => bays.filter((bay) => {
        const e = mul(sub(bay.q[1], bay.q[0]), 1 / len(sub(bay.q[1], bay.q[0])));
        const across = dot(sub(bay.c, X.a), X.n);
        const along = dot(bay.c, X.u);
        return Math.abs(dot(e, X.u)) > 0.999
          && Math.abs(across - side * rowOffset) < 0.1
          && along >= lo - S.sw / 2 - 1e-6
          && along <= hi + S.sw / 2 + 1e-6;
      });
      const rowA = row(A, toward), rowB = row(B, -toward);
      if (!rowA.length || !rowB.length) continue;
      const edgeCoords = (bay) => {
        const ts = bay.q.map((p) => dot(p, A.u));
        return [Math.min(...ts), Math.max(...ts)];
      };
      const ref = edgeCoords(rowA[0])[0];
      for (const bay of [...rowA, ...rowB])
        for (const edge of edgeCoords(bay)) {
          worst = Math.max(worst, moduloGap(edge, ref));
          edges++;
        }
      pairs++;
    }
  return { worst, pairs, edges };
}

/**
 * Freie Enden von Randreihen, deren naechste Buchtbreite bereits eine
 * Fahrbahn schneidet. Eine direkt anschliessende Bucht hebt die Kapselpflicht
 * auf. Gezaehlt wird der harte Mindestabstand; ob die volle Kappentiefe
 * geometrisch ausgegeben werden kann, ist davon bewusst unabhaengig. Nur eine
 * tatsaechlich durch den Reihenkoerper laufende Fahrbahn ab 45 Grad zaehlt:
 * die flaechenlose Beruehrung am Ringknick ist keine Querung.
 */
function perimeterRoadCapViolations(r, S) {
  const dot = (a, b) => a[0] * b[0] + a[1] * b[1];
  const center = (q) => q.reduce((s, p) => add(s, mul(p, 1 / q.length)), [0, 0]);
  const roads = [];
  const addRoads = (segments, width, supportCount = 0) => {
    segments.forEach((segment, i) => {
      const q = corridor(segment[0], segment[1], width);
      if (!q) return;
      const u = mul(sub(segment[1], segment[0]), 1 / len(sub(segment[1], segment[0])));
      roads.push({ q, u, supportsRow: i < supportCount });
    });
  };
  addRoads(r.perimeterLine, S.ai, r.ring.length);
  addRoads(r.aisleLine, S.ai);
  addRoads(r.crossLine, Number.isFinite(S.cw) ? S.cw : 3);
  addRoads(r.entranceLine, S.ai);

  let bad = 0;
  r.bay.forEach((q, i) => {
    if (r.bayKind[i] !== "rand" && r.bayKind[i] !== "rand-innen") return;
    const c = center(q), u = mul(sub(q[1], q[0]), 1 / len(sub(q[1], q[0])));
    const n = [-u[1], u[0]];
    let support = null, supportGap = Infinity;
    for (const road of roads) {
      if (!road.supportsRow || Math.abs(dot(u, road.u)) < 1 - 1e-8) continue;
      const gap = distToBoundary(c, road.q);
      if (gap < supportGap) { support = road; supportGap = gap; }
    }
    if (!support) return;
    for (const sign of [-1, 1]) {
      const edge = add(c, mul(u, sign * S.sw / 2));
      const probe = (L, depth) =>
        rect(add(edge, mul(u, sign * L / 2)), u, n, L / 2, depth / 2);
      if (r.bay.some((b, j) => j !== i
          && quadsOverlap(probe(0.1, S.sl - 0.1), b, 0))) continue;
      if (roads.some((road) => {
        const angle = Math.acos(Math.min(1, Math.abs(dot(u, road.u))))
                    * 180 / Math.PI;
        return road !== support && angle >= MIN_TRANSVERSE_CAP_ANGLE
          && quadsOverlap(probe(S.sw - 1e-6, S.sl), road.q, 0);
      })) bad++;
    }
  });
  return bad;
}

function run(name, site, S) {
  const t0 = Date.now();
  const r = build(site, S);
  const ms = Date.now() - t0;
  const area = Math.abs(signedArea(site));
  const { gap } = coverGap(site, r, S);
  const perStall = r.stalls ? (area / r.stalls).toFixed(1) : "-";
  console.log(
    `${name.padEnd(16)} ${area.toFixed(0).padStart(6)} m2 | ` +
    `${String(r.stalls).padStart(4)} Buchten (Rand ${String(r.perimeterStalls).padStart(3)}, ` +
    `innen ${String(r.innerStalls).padStart(4)}) | ${perStall.padStart(5)} m2/Bucht | ` +
    `${String(r.aisles).padStart(2)} Fahrgassen | ${String(r.angle).padStart(3)} deg | ` +
    `ungedeckt ${(gap / area * 100).toFixed(1).padStart(4)} % | ${ms} ms`);
  const ring = aisleToRing(r, site, S);
  const unreachable = unreachableRoads(r, S);
  const cap = capCheck(r, S);
  const sec = sectionCheck(r, S);
  const align = rowAlignment(r, S);
  const perimeterCaps = perimeterRoadCapViolations(r, S);
  const entrance = entranceCheck(r, site, S);
  const entranceGaps = entrance.bayGaps.length
    ? entrance.bayGaps.map((g) => "[" + g.map((x) => Number.isFinite(x)
      ? x.toFixed(2) : "-").join("/") + "]").join(" ")
    : "-";
  console.log(
    "".padEnd(16) + "   Einfahrten " + entrance.count + " (Soll " + entrance.expected + ") | " +
    "Gruen L/R " + entranceGaps + " m (Soll " + S.sw.toFixed(2) + ") | " +
    "Mitte max " + entrance.centerError.toFixed(6) + " m | " +
    "Fuge Ring " + entrance.ringGap.toFixed(2) + " m | " +
    "Fuge Kante " + entrance.siteGap.toFixed(2) + " m | " +
    "Winkel " + entrance.angleLo.toFixed(2) + "-" + entrance.angleHi.toFixed(2) + " deg | " +
    "Ueberlappung Rand " + entrance.overlap + " | " +
    "Laenge " + entrance.lengthLo.toFixed(2) + "-" + entrance.lengthHi.toFixed(2) + " m");
  console.log(
    `${"".padEnd(16)}   Ueberlappung ${overlapCount(r.bay)} | ` +
    `Bucht auf Fahrbahn ${bayOnRoad(r, S)} | ` +
    `Enden ausserhalb ${endsOutside(r, site)} | ` +
    (() => { const k = roadConflict(r, S);
      return `Strasse auf Strasse ${(k.rg + k.rv + k.gv + k.re + k.ge + k.ve).toFixed(0)} m2 ` +
             `(Rand/Gasse ${k.rg.toFixed(0)}, Rand/Verb ${k.rv.toFixed(0)}, ` +
             `Gasse/Verb ${k.gv.toFixed(0)}) | `; })() +
    `Netz zusammenhaengend: ${unreachable === 0
      ? "ja" : `${unreachable} Fahrbahnen nicht erreichbar`} | ` +
    `Gasse->Ring max ${ring.worst.toFixed(2)} m (Schnitt ${ring.avg.toFixed(2)}) | ` +
    `Fluchtung max ${align.worst.toFixed(2)} m (${align.pairs} Paare, ` +
    `${align.edges} Kanten) | ` +
    `Buchten innen geplant ${sec.planned}, gesetzt ${r.innerStalls} | ` +
    `Randreihe an Strasse ohne Kapsel ${perimeterCaps} | ` +
    `Fuge Kappe/Bucht max ${capToBay(r).toFixed(2)} m`);
  console.log(
    `${"".padEnd(16)}   Abschnitte ${sec.n} (${sec.badSum} Summenfehler, ` +
    `${sec.badCap} Kapsel ausserhalb 1-2 Buchten, ` +
    `${sec.lo.toFixed(2)}-${sec.hi.toFixed(2)} m) | ` +
    `Streifen ${r.median.length} (${stripCheck(r, S)} falsche Tiefe) | ` +
    `Kappen ${r.cap.length} (${cap.bad} falsche Laenge, ${cap.unknown} falsche Tiefe, ` +
    `${cap.lo.toFixed(2)}-${cap.hi.toFixed(2)} m) | ` +
    `Fuge Kappe/Strasse max ${capToRoad(r, S).toFixed(2)} m | ` +
    `Rest ${r.fill.length} Stk`);
  const sc = stripToCap(r, S);
  console.log(
    `${"".padEnd(16)}   Streifenenden ${sc.ends}: ` +
    `${sc.ends - sc.gaps - sc.orphan} buendig, ${sc.gaps} mit Fuge ` +
    `(max ${sc.worst.toFixed(2)} m), ${sc.orphan} ohne Kappe`);
  for (const w of r.warnings) console.log(`${"".padEnd(16)}   WARNUNG: ${w}`);
  return r;
}

console.log("Gemessene CS2-Masse (Invisible Road Path - 2xTwoway 2xPerpendicular):");
console.log("  Bucht 3,0 breit  Streifen 5,5 tief  Fahrgassenkern 7,0  " +
            "Querstrasse 3,0  Mittelstreifen 2,5  Rand 1\n");
run("Rechteck", PRESETS.rect, BASE);
run("L-Form", PRESETS.l, BASE);
run("Schraeg", PRESETS.skew, BASE);
run("Referenz 08s", PRESETS.ref, BASE);

console.log("\nStufenlose Einfahrten und von dort gestartetes Randraster:");
const entranceCases = [["Rechteck", PRESETS.rect], ["L-Form", PRESETS.l],
                       ["Schraeg", PRESETS.skew], ["Referenz 08s", PRESETS.ref]];
for (const sw of [3, 3.15]) for (const offset of [0.37, 1.84]) {
  console.log(`  sw ${sw.toFixed(2)} m, Verschiebung +${offset.toFixed(2)} m:`);
  for (const [name, site] of entranceCases) {
    const probe = entranceRasterProbe(site, { ...BASE, sw }, offset);
    const gaps = probe.gaps.map((pair) =>
      "[" + pair.map((gap) => gap.toFixed(6)).join("/") + "]").join(" ");
    console.log(`    ${name.padEnd(12)} Position ${probe.r.entrances
      .map((entrance) => entrance.along.toFixed(6)).join("/")} m | ` +
      `Gruen L/R ${gaps} m | Lagefehler ${probe.positionError.toExponential(2)} m | ` +
      `Abstandsfehler ${probe.gapError.toExponential(2)} m | ` +
      `${probe.r.stalls} Buchten | Ueberlappung ${overlapCount(probe.r.bay)} | ` +
      `Bucht auf Fahrbahn ${bayOnRoad(probe.r, { ...BASE, sw })}`);
  }
}

/**
 * ECKFANG DER EIN-/AUSFAHRT.
 *
 * An einer konvexen Ecke uebernimmt die Einfahrt den Winkel der dort
 * anschliessenden Randstrasse und bildet deren Verlaengerung nach aussen.
 * Beim Rechteck faellt das nicht auf, weil die Nachbarkante zufaellig
 * senkrecht steht - bei Schraeg sind es 71,6 Grad. Genau daran ist die erste
 * Fassung gescheitert: der 5-Grad-Parallelitaetstest warf das Ziel weg.
 * Deshalb wird es hier dauerhaft gemessen.
 *
 * An einer KONKAVEN Ecke darf es keinen Fang geben - dort laeuft die
 * Randstrasse ins Areal hinein, die Einfahrt laege auf ihr.
 */
console.log("\nEckfang der Ein-/Ausfahrt (Soll: Winkel und Versatz 0):");
for (const [name, site] of [["Rechteck", PRESETS.rect], ["Schraeg", PRESETS.skew]])
  for (const corner of ["start", "end"]) {
    const fit = entranceCornerFit(site, BASE, 0, corner === "start");
    if (!fit) {
      console.log(`  ${name.padEnd(9)} ${corner.padEnd(6)} KEIN FANG - FEHLER`);
      continue;
    }
    const S = { ...BASE, entrances: [{ edge: 0, along: fit.along, corner }] };
    const r = build(site, S);
    const [a, b] = r.entranceLine[0];
    const ue = norm(sub(b, a));
    let winkel = Infinity, versatz = Infinity;
    for (const [ra, rb] of r.perimeterLine) {
      if (len(sub(rb, ra)) < 1) continue;
      const ur = norm(sub(rb, ra)), nr = [-ur[1], ur[0]];
      const w = Math.acos(Math.min(1, Math.abs(ue[0] * ur[0] + ue[1] * ur[1])))
        * 180 / Math.PI;
      if (w > 5) continue;
      const v = Math.abs((b[0] - ra[0]) * nr[0] + (b[1] - ra[1]) * nr[1]);
      if (v < versatz) { versatz = v; winkel = w; }
    }
    const k = roadConflict(r, S);
    const gruen = entranceBayGaps(r, S)[0].filter(Number.isFinite);
    const flaeche = Math.abs(signedArea(site));
    console.log(`  ${name.padEnd(9)} ${corner.padEnd(6)}`
      + ` Winkel ${winkel.toFixed(6)} deg | Versatz ${versatz.toFixed(6)} m |`
      + ` Laenge ${len(sub(b, a)).toFixed(2)} m |`
      + ` Gruen ${(gruen.length ? Math.min(...gruen) : 0).toFixed(6)} m |`
      + ` Einfahrt/Strasse ${(k.re + k.ge + k.ve).toFixed(2)} m2 |`
      + ` ungedeckt ${(coverGap(site, r, S).gap / flaeche * 100).toFixed(1)} % |`
      + ` Bucht auf Fahrbahn ${bayOnRoad(r, S)}`);
  }
{
  // Die konkave Ecke der L-Form bei (60,45) liegt zwischen Kante 2 und 3.
  const konkav = [entranceCornerFit(PRESETS.l, BASE, 2, false),
                  entranceCornerFit(PRESETS.l, BASE, 3, true)];
  console.log("  L-Form konkav (60,45) -> Fang "
    + (konkav.some(Boolean) ? "JA - FEHLER" : "nein, richtig"));
}

/**
 * SONDERPLAETZE. Sie sollen dort liegen, wo Fussgaenger ankommen - also so nah
 * wie moeglich an einer Ein-/Ausfahrt - und einen zusammenhaengenden Block
 * bilden statt verstreut zu sein. Behindertenplaetze am naechsten, Elektro
 * direkt daneben.
 */
console.log("\nSonderplaetze (blau behindert, gruen elektro):");
for (const [name, site] of [["Rechteck", PRESETS.rect], ["L-Form", PRESETS.l],
                            ["Schraeg", PRESETS.skew], ["Referenz 08s", PRESETS.ref]]) {
  const r = build(site, BASE);
  const z = r.specialStalls || { behindert: 0, elektro: 0 };
  const mitte = (q) => q.reduce((a, p) => [a[0] + p[0] / 4, a[1] + p[1] / 4], [0, 0]);
  const ziele = r.entranceLine.map(([, b]) => b);
  const dist = (c) => Math.min(...ziele.map((z2) => len(sub(c, z2))));
  const alle = r.bay.map((q) => dist(mitte(q))).sort((a, b) => a - b);
  const je = { behindert: [], elektro: [] };
  for (let i = 0; i < r.bay.length; i++)
    if (r.bayRole[i] !== "normal") je[r.bayRole[i]].push(dist(mitte(r.bay[i])));
  // Rang des entferntesten Sonderplatzes: liegt er unter den naechsten Buchten?
  const rang = (d) => (d.length ? alle.filter((x) => x <= Math.max(...d)).length : 0);
  const anteil = (z.behindert + z.elektro) / Math.max(1, r.stalls) * 100;
  console.log(`  ${name.padEnd(13)} ${String(r.stalls).padStart(3)} Buchten | `
    + `behindert ${z.behindert} (Rang ${String(rang(je.behindert)).padStart(3)}) | `
    + `elektro ${z.elektro} (Rang ${String(rang(je.elektro)).padStart(3)}) | `
    + `${anteil.toFixed(1)} % | `
    + `behindert naeher als elektro: `
    + `${!je.behindert.length || !je.elektro.length
        || Math.max(...je.behindert) <= Math.max(...je.elektro) ? "ja" : "NEIN"}`);
}

console.log("\nZum Vergleich das zweispurige Modul (Alley - Double Sided Parking, 24,00 m):");
for (const [name, poly] of [["Rechteck", PRESETS.rect], ["L-Form", PRESETS.l],
                            ["Schraeg", PRESETS.skew], ["Referenz 08s", PRESETS.ref]]) {
  const r = build(poly, CS2_SETTINGS_ZWEISPURIG);
  console.log(`  ${name.padEnd(14)} ${String(r.stalls).padStart(4)} Buchten, ` +
              `${r.aisles} Fahrgassen`);
}

console.log("\nZum Vergleich die alten Schaetzwerte (ai 9, sl 5.9, sw 2.9, md 4.5):");
for (const [name, poly] of [["Rechteck", PRESETS.rect], ["L-Form", PRESETS.l],
                            ["Schraeg", PRESETS.skew], ["Referenz 08s", PRESETS.ref]]) {
  const r = build(poly, GESCHAETZT);
  console.log(`  ${name.padEnd(14)} ${String(r.stalls).padStart(4)} Buchten, ` +
              `${r.aisles} Fahrgassen`);
}

console.log("\nReferenzmasse aus dem Frame (Fuss): Bucht 2,7 x 5,5  Fahrgasse 7,3  " +
            "Streifen 4  Rand 3\n");
run("Referenz 08s", PRESETS.ref, REF_SETTINGS);

console.log("\nWinkel im Vergleich (Referenzmasse, Reihen sollten waagerecht liegen):");
for (const deg of [0, 45, 90, 135]) {
  const r = build(PRESETS.ref, { ...REF_SETTINGS, auto: false, angle: deg });
  console.log(`  ${String(deg).padStart(3)} deg -> ${String(r.stalls).padStart(4)} Buchten`);
}

console.log("\nQuerabstand im Vergleich (Referenzmasse):");
for (const cr of [25, 34, 45, 60, 90, 150]) {
  const r = build(PRESETS.ref, { ...REF_SETTINGS, cr });
  console.log(`  ${String(cr).padStart(3)} m -> ${String(r.stalls).padStart(4)} Buchten, ` +
              `${r.aisles} Fahrgassen, ${r.angle} deg`);
}

console.log("\nRandkapsel-Regel im Winkelvergleich (CS2-Masse):");
for (const mode of ["edge", "auto"]) {
  const S = { ...BASE, angleMode: mode };
  const r = build(PRESETS.ref, S);
  console.log(`  ${mode.padEnd(5)} -> ${String(r.stalls).padStart(4)} Buchten (Rand ${r.perimeterStalls}) | ` +
              `Randreihe an Strasse ohne Kapsel ${perimeterRoadCapViolations(r, S)}`);
}

console.log("\nWinkelregel im Vergleich (Referenzmasse):");
for (const mode of ["edge", "auto"]) {
  const r = build(PRESETS.ref, { ...REF_SETTINGS, angleMode: mode });
  console.log(`  ${mode.padEnd(5)} -> ${String(r.stalls).padStart(4)} Buchten, ` +
              `${r.angle} deg, ${r.aisles} Fahrgassen`);
}
