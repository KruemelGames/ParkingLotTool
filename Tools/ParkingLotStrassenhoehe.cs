using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * KNOTEN IM FUSSABDRUCK EINER STADTSTRASSE NEHMEN DEREN HOEHE.
     *
     * Befund 2026-09-26 (Bericht UFS7 eines Testers): sechs Gassen an einer
     * RoadBuilder-Strasse, fuenf davon mit dem inneren Ende rund 3 m im
     * Boden - "Gassenknoten Strasse 53.25 m / innen 50.32 m". Das Gelaende
     * im Parkplatz lag eben bei 53,1 bis 53,3 m. Der Tester hatte den Umriss
     * bis an den Bordstein gezogen (Polygonkante 5 m ab Strassenmitte); das
     * innere Gassenende 3 m weiter lag damit noch auf dem Gehweg der
     * Strasse, und dort schneidet CS2 das Gelaende weg (`ClipTerrain`). Die
     * Abtastung fand das weggeschnittene Gelaende. Die sechste Gasse lag
     * knapp ausserhalb und war richtig.
     *
     * Der Gassenbau wusste das schon fuer das STRASSEN-Ende (siehe
     * `SucheStadtstrasseFuerGasse`: "die HOEHE der Fahrbahn ... nicht die
     * des Gelaendes darunter"). Fuer jeden anderen Knoten fehlte die Regel.
     * Jetzt gilt sie fuer alle Knoten an einer Stelle, `SampleCourseHeight`,
     * gleichrangig neben `HoeheUnterAltbestand` fuer unsere eigenen alten
     * Wege: im Querschnitt einer ebenerdigen Stadtstrasse gilt deren Hoehe.
     *
     * Brücken und Tunnel (`Elevation` merklich ungleich 0) zaehlen nicht -
     * unter einer Bruecke gilt weiter das Gelaende.
     */
    public sealed partial class ParkingLotToolSystem
    {
        private readonly List<(Bezier4x3 Kurve, float Halb)> _strassenStreifen = new();
        private int _strassenhoeheTreffer;
        private float _strassenhoeheGroessterSprung;
        private EntityQuery _strassenStreifenQuery;

        /** Stadtstrassen rund um den Umriss einsammeln - einmal je Bau. */
        private void SammleStrassenStreifen(IReadOnlyList<float2> umriss)
        {
            _strassenStreifen.Clear();
            _strassenhoeheTreffer = 0;
            _strassenhoeheGroessterSprung = 0f;
            if (umriss == null || umriss.Count == 0) return;
            var min = new float2(float.MaxValue);
            var max = new float2(float.MinValue);
            foreach (var p in umriss) { min = math.min(min, p); max = math.max(max, p); }
            min -= 40f;
            max += 40f;

            if (_strassenStreifenQuery == default)
                _strassenStreifenQuery = GetEntityQuery(
                    ComponentType.ReadOnly<Edge>(),
                    ComponentType.ReadOnly<Curve>(),
                    ComponentType.ReadOnly<Composition>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.Exclude<Owner>(),
                    ComponentType.Exclude<Deleted>(),
                    ComponentType.Exclude<Temp>());
            using var kanten = _strassenStreifenQuery.ToEntityArray(Allocator.Temp);
            foreach (var kante in kanten)
            {
                var prefab = EntityManager.GetComponentData<PrefabRef>(kante).m_Prefab;
                if (!EntityManager.HasComponent<RoadData>(prefab)) continue;
                if (EntityManager.HasComponent<Elevation>(kante)
                    && math.any(math.abs(EntityManager
                        .GetComponentData<Elevation>(kante).m_Elevation) > 1f))
                    continue;
                var kurve = EntityManager.GetComponentData<Curve>(kante).m_Bezier;
                var grob = MathUtils.Bounds(kurve.xz);
                if (math.any(grob.max < min) || math.any(grob.min > max)) continue;
                var komposition = EntityManager.GetComponentData<Composition>(kante).m_Edge;
                if (!EntityManager.HasComponent<NetCompositionData>(komposition)) continue;
                // Die GANZE Breite samt Gehweg - genau dort liegt der Knoten,
                // um den es geht. `m_DefaultWidth` am Prefab ist schmaler.
                var breite = EntityManager.GetComponentData<NetCompositionData>(
                    komposition).m_Width;
                if (!(breite > 0.1f)) continue;
                _strassenStreifen.Add((kurve, breite * 0.5f));
            }
        }

        /**
         * Liegt der Punkt im Querschnitt einer ebenerdigen Stadtstrasse?
         * Dann deren Hoehe an der naechsten Stelle der Achse.
         */
        private bool HoeheUnterStrasse(float2 p, ref TerrainHeightData gelaende,
            out float hoehe)
        {
            hoehe = 0f;
            var bester = float.PositiveInfinity;
            foreach (var (kurve, halb) in _strassenStreifen)
            {
                var abstand = MathUtils.Distance(kurve.xz, p, out var t);
                if (abstand > halb || abstand >= bester) continue;
                bester = abstand;
                hoehe = MathUtils.Position(kurve, t).y;
            }
            if (float.IsPositiveInfinity(bester)) return false;
            _strassenhoeheTreffer++;
            var gelaendeHier = TerrainUtils.SampleHeight(ref gelaende,
                new float3(p.x, 0f, p.y));
            if (math.isfinite(gelaendeHier))
                _strassenhoeheGroessterSprung = math.max(
                    _strassenhoeheGroessterSprung, math.abs(hoehe - gelaendeHier));
            return true;
        }

        private void MeldeStrassenhoehe()
        {
            if (_strassenhoeheTreffer == 0) return;
            Mod.log.Info($"PLT-Strassenhoehe: {_strassenhoeheTreffer} Knoten liegen im "
                + $"Querschnitt einer Stadtstrasse ({_strassenStreifen.Count} Kanten in "
                + "der Naehe) und bekommen deren Hoehe statt des weggeschnittenen "
                + $"Gelaendes; groesster Unterschied {_strassenhoeheGroessterSprung:F2} m.");
        }
    }
}
