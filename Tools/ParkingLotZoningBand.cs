using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * WAS CS2 VON EINER OUTER ZONE STEHEN LAESST - und was der Parkplatz
     * deshalb freihaelt.
     *
     * CS2 legt jeden Zonenblock sechs Zellen tief an (`BlockSystem.cs:490`)
     * und kuerzt ihn nur an Hindernissen. Hier steht dieselbe Rechnung je
     * Spalte: fuer die gelbe Markierung in der Vorschau ("diese Tiles werden
     * nicht gebaut, weil kein Platz"), fuer die Platzpruefung beim Klick und
     * fuer das, was der Geometriekern freihaelt (nur das Blaue).
     *
     * EIN SPERRSTREIFEN, der CS2 auf die eingestellte Tiefe begrenzt haette,
     * war gebaut und ist wieder raus (Nutzer 21.10.): er braeuchte ein
     * eigenes Prefab, das danach fuer immer in Spielstaenden steckt. Ohne ihn
     * zont CS2 hinter der Outer zone weiter, wo nichts im Weg ist.
     */
    public sealed partial class ParkingLotToolSystem
    {
        /**
         * EINE REIHE ALLEIN GIBT ES IN CS2 NICHT.
         *
         * `CellBlockJobs.CleanBlockedCells` (`:574-579`) sperrt Reihe 1 mit,
         * sobald Reihe 2 gesperrt ist. Nutzerbefund 21.10.: Band 2 tief,
         * Reihe 2 lag auf der Randstrasse - und auch Reihe 1 blieb leer,
         * obwohl dort Platz war. Ein Band braucht deshalb mindestens zwei
         * freie Reihen.
         */
        internal const int ZoningBandMindestreihen = 2;

        /**
         * WIE VIELE REIHEN BEHAELT DIESE AUSSENSEITE HOECHSTENS?
         *
         * Spalte fuer Spalte mit denselben CS2-Regeln wie die gelbe
         * Markierung (`ZoningBandTiefen`), als Hindernis nur die Randstrasse
         * und der Umriss - dahinter liegt die Stadtstrasse. Gassen und
         * Querwege enden seit 21.10. vor dem Band (Geometriekern), sie zaehlen
         * hier nicht. Ergebnis ist die tiefste Spalte: ein Band, das tiefer
         * ist, haelt Boden frei, den keine Spalte bekommt.
         *
         * Nutzerbefund 21.10. (Abzuege 20:41 und 20:43): die erste Fassung
         * sperrte die ganze Seite, sobald die Randstrasse IRGENDWO eine Reihe
         * beruehrte - auch nur in der Eckspalte, wo das Parcel auf ihrer
         * Linie sitzt. CS2 verliert dort eine Spalte, nicht die Seite.
         */
        internal static int ZoningBandPlatz(ParkingGeometry.Zoningflaeche f,
            int seite, float2[][] randstrasse, IReadOnlyList<float2> umriss)
        {
            if (f == null || seite < 0 || seite > 3) return ZoningTiefeGrenze;
            var tiefen = ZoningBandTiefen(f, seite, randstrasse, umriss, ZoningTiefeGrenze);
            var beste = 0;
            foreach (var t in tiefen) beste = Math.Max(beste, t);
            return beste;
        }

        /** Alle Ecken im Umriss und keine Umrisskante schneidet hinein. */
        private static bool RechteckImUmriss(float2[] rechteck, IReadOnlyList<float2> umriss)
        {
            foreach (var p in rechteck)
                if (!PunktImUmriss(umriss, p)) return false;
            for (var i = 0; i < umriss.Count; i++)
            {
                var c = umriss[i];
                var d = umriss[(i + 1) % umriss.Count];
                for (var k = 0; k < rechteck.Length; k++)
                    if (StreckenSchneiden(rechteck[k], rechteck[(k + 1) % rechteck.Length], c, d))
                        return false;
            }
            return true;
        }

        private static bool PunktImUmriss(IReadOnlyList<float2> umriss, float2 p)
        {
            var drin = false;
            for (int i = 0, k = umriss.Count - 1; i < umriss.Count; k = i++)
            {
                var a = umriss[i];
                var b = umriss[k];
                if ((a.y > p.y) != (b.y > p.y)
                    && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    drin = !drin;
            }
            return drin;
        }

        private static bool StreckenSchneiden(float2 a, float2 b, float2 c, float2 d)
        {
            float Kreuz(float2 u, float2 v) => u.x * v.y - u.y * v.x;
            var d1 = Kreuz(b - a, c - a);
            var d2 = Kreuz(b - a, d - a);
            var d3 = Kreuz(d - c, a - c);
            var d4 = Kreuz(d - c, b - c);
            return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f))
                && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
        }

        /**
         * WIE TIEF BLEIBT JEDE SPALTE EINER OUTER ZONE NACH DEM BAUEN?
         *
         * Nutzerbefund 21.10. (Abzug 19:13): die Vorschau zeigte die Kacheln,
         * nach dem Bauen fehlte die ganze Seite. Ursache waren Fahrgassen und
         * Querwege, die quer durch die Outer zone laufen. CS2 rechnet daraus
         * in drei Schritten, und genau die stehen hier nach:
         *
         *   1. Eine Strasse sperrt jede Zelle, die sie beruehrt, und dahinter
         *      die ganze Spalte (`CellBlockJobs.cs:576-590`).
         *   2. Ist Reihe 2 gesperrt, faellt Reihe 1 mit (`:574-579`).
         *   3. Die Tiefe wird ueber die Nachbarspalten geglaettet
         *      (`CellOverlapJobs.cs:110-200`): eine einzelne Spalte zwischen
         *      zwei leeren faellt weg.
         *
         * Naeherung: CS2 teilt eine Seite in Bloecke zu hoechstens zehn
         * Spalten mit verketteten Nachbarn; hier gilt die Seite als eine
         * Reihe von Spalten, und hinter den Enden steht 0. An einer Ecke kann
         * eine Endspalte deshalb verloren aussehen, die CS2 ueber den Block
         * um die Ecke doch haelt.
         *
         * `sperren` sind die Fahrbahnen des Bauplans als Vierecke. Index ist
         * die Spalte ab Ecke `seite` des Strassenrings.
         */
        internal static int[] ZoningBandTiefen(ParkingGeometry.Zoningflaeche f,
            int seite, IReadOnlyList<float2[]> sperren,
            IReadOnlyList<float2> umriss = null, int tiefe = 0)
        {
            if (f == null) return Array.Empty<int>();
            var k = tiefe > 0 ? tiefe : ParkingGeometry.ZoningAussenkacheln(f, seite);
            if (k <= 0) return Array.Empty<int>();
            var ring = ParkingGeometry.ZoningEckenMitAufschlag(
                f, ParkingGeometry.ZoningStrassenbreite / 2.0);
            var mitte = (ring[0] + ring[1] + ring[2] + ring[3]) * 0.25f;
            var a = ring[seite];
            var b = ring[(seite + 1) % 4];
            var laenge = math.distance(a, b);
            var kante = (float)ParkingGeometry.Zoningparzelle;
            var spalten = (int)math.round(laenge / kante) + 1;
            var tiefen = new int[spalten];
            if (laenge < 1e-3f) return tiefen;
            var richtung = (b - a) / laenge;
            var aussen = new float2(-richtung.y, richtung.x);
            if (math.dot((a + b) * 0.5f - mitte, aussen) < 0f) aussen = -aussen;

            var halb = kante * 0.5f - 0.02f;
            for (var c = 0; c < spalten; c++)
            {
                tiefen[c] = k;
                if (sperren == null && umriss == null) continue;
                sperren ??= Array.Empty<float2[]>();
                for (var r = 1; r <= k; r++)
                {
                    var zelle = a + richtung * (c * kante) + aussen * (r * kante);
                    var zellQuad = new[]
                    {
                        zelle - richtung * halb - aussen * halb,
                        zelle + richtung * halb - aussen * halb,
                        zelle + richtung * halb + aussen * halb,
                        zelle - richtung * halb + aussen * halb,
                    };
                    // Hinter dem Umriss liegt die Stadt - dort haelt der
                    // Parkplatz nichts frei.
                    var getroffen = umriss != null && umriss.Count >= 3
                        && !RechteckImUmriss(zellQuad, umriss);
                    if (!getroffen) foreach (var quad in sperren)
                    {
                        if (quad == null || quad.Length < 3) continue;
                        if (KonvexeUeberlappen(zellQuad, quad)) { getroffen = true; break; }
                    }
                    if (!getroffen) continue;
                    tiefen[c] = r - 1;
                    break;
                }
                // Schritt 2: eine Reihe allein bleibt nicht stehen.
                if (tiefen[c] == 1 && k >= 2) tiefen[c] = 0;
            }
            Glaette(tiefen);
            return tiefen;
        }

        /** Schritt 3, nach `CellOverlapJobs.CellReduction` (Fall Blocked). */
        private static void Glaette(int[] t)
        {
            int l = 0, r = t.Length - 1;
            while (r >= 0)
            {
                if (l < t.Length) t[l] = math.min(t[l], TiefeLinks(t, l));
                t[r] = math.min(t[r], TiefeRechts(t, r));
                l++;
                r--;
            }
        }

        private static int Tiefe(int[] t, int x) => x < 0 || x >= t.Length ? 0 : t[x];

        private static int TiefeLinks(int[] t, int x)
        {
            var d = Tiefe(t, x - 1);
            var d2 = Tiefe(t, x);
            if (d2 <= d) return d2;
            var d3 = Tiefe(t, x - 2);
            if (d != d3 && d != 0) return d;
            var d4 = Tiefe(t, x + 1);
            if (d4 - d2 < d2 - d) return math.min(math.max(d, d4), d2);
            if (Tiefe(t, x + 2) != d4) return math.min(math.max(d, d4), d2);
            return d;
        }

        private static int TiefeRechts(int[] t, int x)
        {
            var d = Tiefe(t, x + 1);
            var d2 = Tiefe(t, x);
            if (d2 <= d) return d2;
            var d3 = Tiefe(t, x + 2);
            if (d != d3 && d != 0) return d;
            var d4 = Tiefe(t, x - 1);
            if (d4 - d2 < d2 - d) return math.min(math.max(d4, d), d2);
            if (Tiefe(t, x - 2) != d4) return math.min(math.max(d4, d), d2);
            return d;
        }

        /**
         * Alle Fahrbahnen des letzten Laufs als Vierecke - Randstrasse,
         * Fahrgassen, Querwege, Zufahrten und Zoningstrassen. Breiten aus den
         * Einstellungen (`TryChooseDrivablePath` waehlt das Prefab danach).
         * Zwischengespeichert, bis ein neuer Lauf kommt.
         */
        private const float ZoningSperrToleranz = 0.5f;
        private ParkingLayout _zoningSperrLayout;
        private float2[][] _zoningSperrQuads = Array.Empty<float2[]>();
        private float2[][] _zoningRandQuads = Array.Empty<float2[]>();

        /** Nur die Randstrasse, mit derselben Toleranz - fuer die Platzpruefung beim Klick. */
        internal float2[][] ZoningRandstrassenquads()
        {
            ZoningSperrquads();
            return _zoningRandQuads;
        }

        internal IReadOnlyList<float2[]> ZoningSperrquads()
        {
            var layout = _areaPreviewLayout;
            if (layout == null || !_closed)
            {
                _zoningSperrLayout = null;
                _zoningRandQuads = Array.Empty<float2[]>();
                return null;
            }
            if (ReferenceEquals(layout, _zoningSperrLayout)) return _zoningSperrQuads;
            _zoningSperrLayout = layout;
            _zoningSperrQuads = SperrquadsAus(layout.NetLine,
                _areaPreviewSettings?.Ai ?? 7.0, _areaPreviewSettings?.Cw ?? 3.0,
                out _zoningRandQuads);
            PruefeBandwirkungNachLauf();
            return _zoningSperrQuads;
        }

        /**
         * Alle Fahrbahnen eines Laufs als Vierecke, dazu die Randstrasse
         * allein. Breiten aus den Einstellungen (`TryChooseDrivablePath`
         * waehlt das Prefab danach).
         *
         * 0,5 m SCHMALER ALS EINGESTELLT, gemessen 21.10. (20:56): die
         * Randstrasse ragte rechnerisch 0,2 m in eine Eckspalte, CS2 behielt
         * sie ("5x6 gueltig 4x2" im Zoningblock-Log); die andere Ecke mit
         * 1,6 m fiel weg. Eine Haarberuehrung sperrt nicht.
         */
        internal static float2[][] SperrquadsAus(NetSegment[] netz, double gassenbreite,
            double querbreite, out float2[][] randstrasse)
        {
            var liste = new List<float2[]>();
            var rand = new List<float2[]>();
            foreach (var stueck in netz ?? Array.Empty<NetSegment>())
            {
                var spanne = stueck.B - stueck.A;
                var laenge = math.length(spanne);
                if (laenge < 1e-3f) continue;
                var breite = stueck.Kind == "cross" ? (float)querbreite
                    : stueck.Kind == "zoning"
                        ? (float)ParkingGeometry.ZoningStrassenbreite
                        : (float)gassenbreite;
                var q = new float2(-spanne.y, spanne.x) / laenge
                    * math.max(0.5f, breite * 0.5f - ZoningSperrToleranz);
                var quad = new[] { stueck.A - q, stueck.B - q, stueck.B + q, stueck.A + q };
                liste.Add(quad);
                if (stueck.Kind == "perimeter") rand.Add(quad);
            }
            randstrasse = rand.ToArray();
            return liste.ToArray();
        }

        /**
         * GELB WIRD NICHT FREIGERAEUMT (Nutzer 21.10.).
         *
         * Je Seite dieselbe Spaltenrechnung wie die gelbe Markierung, mit
         * Randstrasse und Umriss als Hindernis (Gassen und Querwege weichen
         * dem Band aus, die zaehlen nicht). Daraus fuer den Geometriekern: die
         * wirksame Tiefe (tiefste blaue Spalte) und wie viele gelbe Spalten an
         * Anfang und Ende wegfallen. Eine gelbe Spalte MITTEN in der Seite
         * bleibt freigehalten - das Band bleibt je Seite ein Rechteck.
         */
        internal ParkingGeometry.Zoningflaeche MitBandwirkung(ParkingGeometry.Zoningflaeche f)
        {
            var umriss = _closed && _worldPoints.Count >= 3
                ? _worldPoints.Select(p => p.xz).ToList() : null;
            return Bandwirkung(f, ZoningRandstrassenquads(), umriss);
        }

        /** Rein rechnend - laeuft auch im Hintergrund-Neubau (Sync, Reparatur). */
        internal static ParkingGeometry.Zoningflaeche Bandwirkung(ParkingGeometry.Zoningflaeche f,
            float2[][] rand, IReadOnlyList<float2> umriss)
        {
            var kopie = f.Clone();
            kopie.AussenWirksam = null;
            kopie.AussenKuerzung = null;
            if (f.Aussentiefen == null) return kopie;
            if (umriss == null && (rand == null || rand.Length == 0)) return kopie;
            var wirksam = new double[4];
            var kuerzung = new double[8];
            var kante = ParkingGeometry.Zoningparzelle;
            for (var s = 0; s < 4; s++)
            {
                var k = ParkingGeometry.ZoningAussenkacheln(f, s);
                if (k <= 0) continue;
                var t = ZoningBandTiefen(f, s, rand, umriss, k);
                int erste = -1, letzte = -1, tiefste = 0;
                for (var c = 0; c < t.Length; c++)
                {
                    if (t[c] <= 0) continue;
                    if (erste < 0) erste = c;
                    letzte = c;
                    tiefste = Math.Max(tiefste, t[c]);
                }
                if (erste < 0) continue;
                wirksam[s] = tiefste * kante;
                kuerzung[2 * s] = erste * kante;
                kuerzung[2 * s + 1] = (t.Length - 1 - letzte) * kante;
            }
            kopie.AussenWirksam = wirksam;
            kopie.AussenKuerzung = kuerzung;
            return kopie;
        }

        /**
         * FUER DEN HINTERGRUND-NEUBAU: dieselbe Wirkung wie in der Vorschau.
         *
         * Sync und Reparatur lesen den Bauzettel, und der traegt nur die
         * gewaehlte Tiefe. Ohne diesen Schritt hielte ein Neubau im
         * Hintergrund wieder die volle Tiefe frei - Gelb schnitte Flaechen
         * weg. Die Randstrasse kommt aus dem ersten Lauf; sie haengt nicht am
         * Band, also ist der zweite Lauf damit exakt. `null`: nichts zu tun.
         */
        internal static ParkingGeometry.Zoningflaeche[] BandwirkungFuerLauf(
            ParkingGeometry.Zoningflaeche[] flaechen, ParkingLayout lauf,
            double gassenbreite, double querbreite, IReadOnlyList<float2> umriss)
        {
            if (flaechen == null || lauf == null) return null;
            if (!flaechen.Any(f => f?.Aussentiefen != null && f.Aussentiefen.Any(t => t > 1e-6)))
                return null;
            SperrquadsAus(lauf.NetLine, gassenbreite, querbreite, out var rand);
            return flaechen.Select(f => f == null ? null : Bandwirkung(f, rand, umriss)).ToArray();
        }

        /** Kurzform der Wirkung, um zu merken, ob ein neuer Lauf sie aendert. */
        private string BandwirkungSchluessel(IEnumerable<ParkingGeometry.Zoningflaeche> flaechen)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var f in flaechen)
            {
                if (f?.AussenWirksam != null) foreach (var w in f.AussenWirksam) sb.Append(w.ToString("0.#")).Append(',');
                if (f?.AussenKuerzung != null) foreach (var w in f.AussenKuerzung) sb.Append(w.ToString("0.#")).Append(',');
                sb.Append('|');
            }
            return sb.ToString();
        }

        private string _bandwirkungImLauf;
        private int _bandwirkungNachlaufRevision = -1;

        /**
         * Die Wirkung haengt an der Randstrasse des LETZTEN Laufs. Kommt ein
         * neuer Lauf, kann sie sich aendern (erster Lauf, Umriss verschoben) -
         * dann noch einmal rechnen. Die Randstrasse haengt nicht am Band, also
         * steht es nach einem Nachlauf; mehr als einen je Revision gibt es nicht.
         */
        private void PruefeBandwirkungNachLauf()
        {
            if (!_closed || _layoutDirty || _bandwirkungImLauf == null) return;
            if (_bandwirkungNachlaufRevision == _geometryRevision) return;
            var jetzt = BandwirkungSchluessel(_zoningflaechen.Select(MitBandwirkung));
            if (jetzt == _bandwirkungImLauf) return;
            _bandwirkungNachlaufRevision = _geometryRevision;
            _layoutDirty = true;
        }

        /** Trennachsensatz fuer zwei konvexe Vielecke. */
        private static bool KonvexeUeberlappen(float2[] p, float2[] q)
            => !HatTrennachse(p, q) && !HatTrennachse(q, p);

        private static bool HatTrennachse(float2[] kanten, float2[] andere)
        {
            for (var i = 0; i < kanten.Length; i++)
            {
                var d = kanten[(i + 1) % kanten.Length] - kanten[i];
                if (math.lengthsq(d) < 1e-12f) continue;
                var n = new float2(-d.y, d.x);
                float minA = float.MaxValue, maxA = float.MinValue;
                float minB = float.MaxValue, maxB = float.MinValue;
                foreach (var v in kanten)
                {
                    var t = math.dot(v, n);
                    minA = math.min(minA, t); maxA = math.max(maxA, t);
                }
                foreach (var v in andere)
                {
                    var t = math.dot(v, n);
                    minB = math.min(minB, t); maxB = math.max(maxB, t);
                }
                if (maxA < minB || maxB < minA) return true;
            }
            return false;
        }
    }
}
