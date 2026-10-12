using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Notifications;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * PLT-PARKPLAETZE IN DER INFOANSICHT "ROADS" (GitHub-Issue #14, 2026-10-12).
     *
     * Vanilla faerbt Parkplaetze dort blau und setzt ein Parkplatz-Symbol.
     * Beides verlangt ein GEBAEUDE (`ObjectColorSystem`, `MarkerCreateSystem`);
     * unser Parkplatz ist eine Flaeche und blieb grau - obwohl er das Merkmal
     * `CarParkingFacility` schon traegt. Der Wirtschaftsbegleiter taugt nicht:
     * er traegt `Hidden`, und Vanilla gibt das an sein Symbol weiter.
     *
     * FLAECHE: `AreaColorSystem` laeuft bei jeder Flaeche die Besitzerkette
     * hoch bis zur ersten `Game.Objects.Color`. Belag und Gras gehoeren der
     * Lot-Flaeche; traegt sie den Farbindex des aktiven Parkplatz-Modus mit
     * `m_SubColor`, faerben sie sich mit - dasselbe Blau, derselbe Modus. Der
     * Index wird jedes Mal gelesen, das Spiel vergibt ihn je Ansicht neu.
     *
     * SYMBOL: das Vanilla-Parkplatz-Icon (`BuildingMarkerData`), Besitzer ist
     * die Lot-Flaeche, Position die Mitte des Parkplatzes. Kommt mit dem
     * Parkplatz-Modus, geht mit ihm; beim Abriss raeumt Vanilla es selbst ab.
     *
     * STATISTIK ("Parking Facility Availability", Plaetze, Belegung): die
     * Roads-Ansicht zaehlt jeden Eintrag mit `CarParkingFacility` und folgt
     * von dort SubNet/SubObject EINE Ebene tief zu den Parkspuren. Das Merkmal
     * sitzt an der Lot-Flaeche, die Spuren aber an den Aufklebern unter dem
     * TRAEGER - gezaehlt wurden 0 Plaetze. Der Traeger bekommt deshalb das
     * leere Merkmal mit (kein `ParkingFacility`, also keine zweite Simulation;
     * kein Gebaeude/Objekt, also kein zweites Symbol, keine Faerbung). Die
     * Flaeche selbst findet weiter 0 Spuren - doppelt gezaehlt wird nichts.
     * Dasselbe Problem hatte das Infofenster, siehe ParkingLotParkingDataPatch.
     *
     * Kein Harmony, kein Eingriff in die Vanilla-Ansicht, kein Sync-Schritt:
     * bestehende Parkplaetze bekommen beides beim naechsten Oeffnen der Ansicht.
     */
    public sealed partial class ParkingLotInfoansichtSystem : GameSystemBase
    {
        private EntityQuery _lots;
        private EntityQuery _modi;
        private EntityQuery _marker;
        private IconCommandSystem _icons;

        /** Farbindex, mit dem die Parkplaetze zuletzt gefaerbt wurden; 0 = keine Farbe. */
        private int _index;
        private int _lotzahl = -1;
        private readonly HashSet<Entity> _mitSymbol = new();
        private bool _nachLadenAufraeumen;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _icons = World.GetOrCreateSystemManaged<IconCommandSystem>();
            _lots = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ParkingLotCarrierReference>(), ComponentType.ReadOnly<Game.Areas.Node>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            _modi = GetEntityQuery(ComponentType.ReadOnly<InfomodeActive>(), ComponentType.ReadOnly<InfoviewBuildingData>());
            _marker = GetEntityQuery(ComponentType.ReadOnly<BuildingMarkerData>(), ComponentType.ReadOnly<PrefabData>());
        }

        [Preserve]
        protected override void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            _index = 0;
            _lotzahl = -1;
            _mitSymbol.Clear();
            // Symbole werden mitgespeichert: eins aus einem Speicherstand mit
            // offener Ansicht darf nicht stehen bleiben.
            _nachLadenAufraeumen = true;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            var spiel = GameManager.instance;
            if (spiel == null || spiel.isGameLoading || !spiel.gameMode.IsGame()) return;

            var index = AktiverParkplatzIndex();
            var lotzahl = _lots.CalculateEntityCount();
            if (lotzahl != _lotzahl || _nachLadenAufraeumen) SichereZaehlmerkmal();
            if (index == _index && lotzahl == _lotzahl && !_nachLadenAufraeumen) return;

            var marker = ParkplatzMarker();
            using var lots = _lots.ToEntityArray(Allocator.Temp);
            var befehle = _icons.CreateCommandBuffer();

            if (_nachLadenAufraeumen && marker != Entity.Null)
                for (var i = 0; i < lots.Length; i++)
                    befehle.Remove(lots[i], marker, default, IconFlags.IgnoreTarget);
            _nachLadenAufraeumen = false;

            var farbe = new Game.Objects.Color((byte)math.clamp(index, 0, 255), 0, index > 0);
            var lebend = new HashSet<Entity>();
            for (var i = 0; i < lots.Length; i++)
            {
                var lot = lots[i];
                lebend.Add(lot);
                if (EntityManager.HasComponent<Game.Objects.Color>(lot))
                    EntityManager.SetComponentData(lot, farbe);
                else if (index > 0)
                    EntityManager.AddComponentData(lot, farbe);

                if (marker == Entity.Null) continue;
                if (index > 0 && !_mitSymbol.Contains(lot))
                {
                    befehle.Add(lot, marker, Mitte(lot), IconPriority.Info, IconClusterLayer.Marker,
                        IconFlags.Unique | IconFlags.IgnoreTarget);
                    _mitSymbol.Add(lot);
                }
                else if (index == 0 && _mitSymbol.Contains(lot))
                {
                    befehle.Remove(lot, marker, default, IconFlags.IgnoreTarget);
                }
            }
            if (index == 0) _mitSymbol.Clear();
            // Abgerissene Plaetze: ihr Symbol raeumt Vanilla mit dem Besitzer ab.
            _mitSymbol.RemoveWhere(e => !lebend.Contains(e));

            if (index != _index)
                Mod.log.Info("PLT-Infoansicht: Parkplatz-Modus " + (index > 0 ? "aktiv, Farbindex " + index : "aus")
                    + "; " + lots.Length + " Parkplatz/Parkplaetze"
                    + (marker == Entity.Null ? ", KEIN Parkplatz-Symbol gefunden" : "") + ".");
            _index = index;
            _lotzahl = lotzahl;
        }

        /** Jeder Traeger traegt `CarParkingFacility`, damit die Roads-Statistik unsere Spuren findet. */
        private void SichereZaehlmerkmal()
        {
            using var lots = _lots.ToEntityArray(Allocator.Temp);
            var neu = 0;
            for (var i = 0; i < lots.Length; i++)
            {
                var traeger = EntityManager.GetComponentData<ParkingLotCarrierReference>(lots[i]).Carrier;
                if (traeger == Entity.Null || !EntityManager.Exists(traeger)
                    || EntityManager.HasComponent<Deleted>(traeger)
                    || EntityManager.HasComponent<Game.Buildings.CarParkingFacility>(traeger)) continue;
                EntityManager.AddComponent<Game.Buildings.CarParkingFacility>(traeger);
                neu++;
            }
            if (neu > 0)
                Mod.log.Info("PLT-Infoansicht: " + neu + " Traeger fuer die Parkplatz-Statistik der Roads-Ansicht markiert.");
        }

        /** Index des aktiven Vanilla-Infomodes fuer Auto-Parkplaetze, 0 wenn keiner aktiv ist. */
        private int AktiverParkplatzIndex()
        {
            if (_modi.IsEmptyIgnoreFilter) return 0;
            using var modi = _modi.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < modi.Length; i++)
                if (EntityManager.GetComponentData<InfoviewBuildingData>(modi[i]).m_Type == BuildingType.CarParkingFacility)
                    return EntityManager.GetComponentData<InfomodeActive>(modi[i]).m_Index;
            return 0;
        }

        /** Das Vanilla-Symbol fuer Auto-Parkplaetze. */
        private Entity ParkplatzMarker()
        {
            using var marker = _marker.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < marker.Length; i++)
                if (EntityManager.GetComponentData<BuildingMarkerData>(marker[i]).m_BuildingType == BuildingType.CarParkingFacility)
                    return marker[i];
            return Entity.Null;
        }

        /**
         * Mitte des Parkplatzes, wie Vanilla bei Gebaeuden: Flaechenschwerpunkt
         * der Umrissknoten, Hoehe deren Mittel plus ein Stueck darueber.
         */
        private float3 Mitte(Entity lot)
        {
            var knoten = EntityManager.GetBuffer<Game.Areas.Node>(lot, true);
            var n = knoten.Length;
            if (n == 0) return float3.zero;
            double flaeche = 0, cx = 0, cz = 0, y = 0;
            for (var i = 0; i < n; i++)
            {
                var a = knoten[i].m_Position;
                var b = knoten[(i + 1) % n].m_Position;
                var kreuz = (double)a.x * b.z - (double)b.x * a.z;
                flaeche += kreuz;
                cx += (a.x + b.x) * kreuz;
                cz += (a.z + b.z) * kreuz;
                y += a.y;
            }
            y /= n;
            if (System.Math.Abs(flaeche) < 1e-6)
            {
                cx = cz = 0;
                for (var i = 0; i < n; i++) { cx += knoten[i].m_Position.x; cz += knoten[i].m_Position.z; }
                return new float3((float)(cx / n), (float)y + 2f, (float)(cz / n));
            }
            return new float3((float)(cx / (3 * flaeche)), (float)y + 2f, (float)(cz / (3 * flaeche)));
        }
    }
}
