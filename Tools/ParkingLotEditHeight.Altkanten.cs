using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * HOEHEN UNTER DEN ALTEN WEGEN - NICHT AUS DEM GELAENDE, DAS SIE GEFORMT
     * HABEN.
     *
     * Befund 2026-09-25 (Bauabzuege 16:41-16:43): die Gasse lag um 510 m.
     * Der Nutzer entfernte sie; die Knoten, die danach an ihrer Stelle neu
     * entstanden, lasen im Gelaende VOR dem Abriss 496,6 und 497,7 m - 11 bis
     * 13 m zu tief. Das Gelaende unter einem eigenen Weg ist von ihm selbst
     * planiert/beschnitten (CS2 haelt auf der CPU nur die Kopie MIT allen
     * Eingriffen, siehe `_altknotenhoehen`). Ab da erbte jeder Umbau die
     * falsche Hoehe: "Gasse verschwindet im Boden".
     *
     * Zwei Regeln, beide gegen dieselbe Wurzel:
     *   1. Ein neuer Knoten im Streifen eines alten eigenen Wegs bekommt
     *      die Hoehe dieses Wegs an der Stelle - nicht das Gelaende darunter.
     *   2. Jede so gewonnene oder von einem alten Knoten uebernommene Hoehe
     *      wird gegen das UNBERUEHRTE Gelaende geprueft: gemessen beidseits
     *      knapp ausserhalb aller alten Wegstreifen, gemittelt (eine gerade
     *      Querneigung hebt sich so auf). Weicht sie mehr als
     *      `Hoehenschranke` ab, gilt das Gelaende. Das heilt auch Parkplaetze,
     *      deren Hoehe schon verdorben ist.
     */
    public sealed partial class ParkingLotToolSystem
    {
        /** Mehr Abstand zum unberuehrten Gelaende ist kein Planieren mehr, sondern ein Fehler. */
        private const float Hoehenschranke = 2f;

        /** So weit neben dem Wegrand gilt das Gelaende als unberuehrt. */
        private const float Umgebungsabstand = 3f;

        private readonly List<(Bezier4x3 Kurve, float Halb)> _altkanten = new();

        private void MerkeAltkante(Entity kante)
        {
            if (!EntityManager.HasComponent<Curve>(kante)) return;
            var kurve = EntityManager.GetComponentData<Curve>(kante).m_Bezier;
            var halb = 4f;
            if (EntityManager.HasComponent<Composition>(kante))
            {
                var komposition = EntityManager.GetComponentData<Composition>(kante).m_Edge;
                if (EntityManager.HasComponent<NetCompositionData>(komposition))
                {
                    var breite = EntityManager.GetComponentData<NetCompositionData>(
                        komposition).m_Width;
                    if (breite > 0.1f) halb = breite / 2f;
                }
            }
            _altkanten.Add((kurve, halb));
        }

        /** Liegt der Punkt im Streifen einer alten Kante? Dann deren Hoehe dort. */
        private bool AltkanteBei(float2 p, out float hoehe, out float3 richtung,
            out float halb)
        {
            hoehe = 0f;
            richtung = default;
            halb = 0f;
            var bester = float.PositiveInfinity;
            foreach (var (kurve, h) in _altkanten)
            {
                var abstand = MathUtils.Distance(kurve.xz, p, out var t);
                if (abstand > h + 0.5f || abstand >= bester) continue;
                bester = abstand;
                hoehe = MathUtils.Position(kurve, t).y;
                richtung = MathUtils.Tangent(kurve, t);
                halb = h;
            }
            return !float.IsPositiveInfinity(bester);
        }

        private bool InAltstreifen(float2 p)
        {
            foreach (var (kurve, h) in _altkanten)
                if (MathUtils.Distance(kurve.xz, p, out _) <= h + 0.5f) return true;
            return false;
        }

        /**
         * Gelaendehoehe neben dem Streifen, beidseits quer zur Kante in
         * wachsendem Abstand, bis ein Punkt ausserhalb aller alten Streifen
         * liegt. Ohne Treffer: NaN.
         */
        private float UnberuehrtesGelaende(float2 p, float3 richtung, float halb,
            ref TerrainHeightData gelaende)
        {
            var r = math.normalizesafe(richtung.xz);
            if (math.lengthsq(r) < 0.5f) return float.NaN;
            var quer = new float2(-r.y, r.x);
            var summe = 0f;
            var anzahl = 0;
            foreach (var seite in new[] { -1f, 1f })
                for (var weite = halb + Umgebungsabstand; weite <= halb + 24f; weite += 3f)
                {
                    var q = p + quer * seite * weite;
                    if (InAltstreifen(q)) continue;
                    var y = TerrainUtils.SampleHeight(ref gelaende, new float3(q.x, 0f, q.y));
                    if (!math.isfinite(y)) break;
                    summe += y;
                    anzahl++;
                    break;
                }
            return anzahl == 0 ? float.NaN : summe / anzahl;
        }

        /**
         * Regel 1 + 2 fuer einen neuen Knoten. `false`: kein alter Weg hier,
         * das Gelaende gilt wie bisher.
         */
        private bool HoeheUnterAltbestand(float2 p, ref TerrainHeightData gelaende,
            out float hoehe)
        {
            hoehe = 0f;
            if (_altkanten.Count == 0) return false;
            if (!AltkanteBei(p, out var wegHoehe, out var richtung, out var halb))
                return false;
            var umgebung = UnberuehrtesGelaende(p, richtung, halb, ref gelaende);
            if (math.isfinite(umgebung) && math.abs(wegHoehe - umgebung) > Hoehenschranke)
            {
                _verworfeneAlthoehen++;
                hoehe = umgebung;
                return true;
            }
            hoehe = wegHoehe;
            return true;
        }

        /** Regel 2 fuer eine vom alten Knoten uebernommene Hoehe. */
        private bool AlthoeheGlaubwuerdig(float2 p, float alt, ref TerrainHeightData gelaende,
            out float umgebung)
        {
            umgebung = float.NaN;
            if (!AltkanteBei(p, out _, out var richtung, out var halb)) return true;
            umgebung = UnberuehrtesGelaende(p, richtung, halb, ref gelaende);
            return !math.isfinite(umgebung) || math.abs(alt - umgebung) <= Hoehenschranke;
        }

        private int _verworfeneAlthoehen;
    }
}
