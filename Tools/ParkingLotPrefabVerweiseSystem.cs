using Game;
using Game.Common;
using Game.Prefabs;
using Game.Serialization;
using Unity.Collections;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    /**
     * PREFAB-VERWEISE IN UNSEREN GESPEICHERTEN DATEN UEBER DAS LADEN RETTEN.
     *
     * Issue #16 (2026-10-13): "Updating parking lots" blieb fuer immer stehen.
     * Im Log jedes Mal sofort nach dem Laden: "Phase Rueckweg: Gesicherter
     * Besitzer/Prefab fehlt" und "Rueckweg ANGEHALTEN ... Sperre erhalten".
     *
     * Ursache: Der Rueckweg-Schnappschuss speichert das Prefab einer Kante
     * als rohe Entity. CS2 liest Prefab-Verweise beim Laden zuerst als
     * Platzhalter-Entity und biegt sie in der Phase `PrefabReferences` auf
     * das echte Prefab um - aber nur in den Komponenten, die es kennt
     * (`PrefabRef` usw., siehe `PrimaryPrefabReferencesSystem`). Danach
     * zerstoert `ResolvePrefabsSystem` alle Platzhalter. Unser Verweis zeigte
     * damit ins Leere, JEDER nach einem Speichern fortgesetzte Rueckweg
     * scheiterte an der ersten Pruefung, und das Lot blieb gesperrt.
     *
     * Dieses System tut fuer unsere Komponenten, was CS2 fuer seine eigenen
     * tut: in derselben Phase `PrefabReferences.Check`. Beim Laden wird so der
     * Platzhalter zum echten Prefab; beim Speichern meldet es das Prefab als
     * benutzt, damit CS2 es ueberhaupt in den Spielstand schreibt - auch wenn
     * nach dem Abriss nur noch unser Schnappschuss darauf zeigt.
     *
     * Weil die Platzhalter erst NACH dieser Phase zerstoert werden, greift
     * das auch fuer Spielstaende, die schon mit dem Fehler gespeichert wurden.
     */
    internal sealed partial class ParkingLotPrefabVerweiseSystem : GameSystemBase
    {
        private CheckPrefabReferencesSystem _pruefung;
        private EntityQuery _rueckwegkurse;
        private EntityQuery _erhalteneKurse;
        private EntityQuery _anschluesse;
        private EntityQuery _stufeA;
        private EntityQuery _sonden;

        protected override void OnCreate()
        {
            base.OnCreate();
            _pruefung = World.GetOrCreateSystemManaged<CheckPrefabReferencesSystem>();
            _rueckwegkurse = GetEntityQuery(ComponentType.ReadWrite<ParkingLotRueckwegkurs>());
            _erhalteneKurse = GetEntityQuery(ComponentType.ReadWrite<ParkingLotErhaltenerKurs>());
            _anschluesse = GetEntityQuery(ComponentType.ReadWrite<ParkingLotRueckweganschluss>());
            _stufeA = GetEntityQuery(ComponentType.ReadWrite<ParkingLotStufeAPrefab>());
            _sonden = GetEntityQuery(ComponentType.ReadWrite<ParkingLotZoningProbeMarker>());
        }

        protected override void OnUpdate()
        {
            if (_rueckwegkurse.IsEmptyIgnoreFilter && _erhalteneKurse.IsEmptyIgnoreFilter
                && _anschluesse.IsEmptyIgnoreFilter && _stufeA.IsEmptyIgnoreFilter
                && _sonden.IsEmptyIgnoreFilter) return;

            var verweise = _pruefung.GetPrefabReferences(this, out var abhaengig);
            abhaengig.Complete();
            int geprueft = 0, umgebogen = 0, unbekannt = 0;

            Entity Pruefe(Entity prefab)
            {
                if (prefab == Entity.Null) return prefab;
                geprueft++;
                // Weder Prefab noch Platzhalter: nichts, was CS2 zuordnen
                // koennte. Stehen lassen - die Pruefung im Rueckweg meldet es.
                if (!EntityManager.Exists(prefab) || !EntityManager.HasComponent<PrefabData>(prefab))
                { unbekannt++; return prefab; }
                var neu = verweise.Check(EntityManager, prefab);
                if (neu != prefab) umgebogen++;
                return neu;
            }

            using (var lots = _rueckwegkurse.ToEntityArray(Allocator.Temp))
                foreach (var lot in lots)
                {
                    var puffer = EntityManager.GetBuffer<ParkingLotRueckwegkurs>(lot);
                    for (var i = 0; i < puffer.Length; i++)
                    { var k = puffer[i]; k.Prefab = Pruefe(k.Prefab); puffer[i] = k; }
                }
            using (var lots = _erhalteneKurse.ToEntityArray(Allocator.Temp))
                foreach (var lot in lots)
                {
                    var puffer = EntityManager.GetBuffer<ParkingLotErhaltenerKurs>(lot);
                    for (var i = 0; i < puffer.Length; i++)
                    { var k = puffer[i]; k.Bestand.Prefab = Pruefe(k.Bestand.Prefab); puffer[i] = k; }
                }
            using (var lots = _anschluesse.ToEntityArray(Allocator.Temp))
                foreach (var lot in lots)
                {
                    var puffer = EntityManager.GetBuffer<ParkingLotRueckweganschluss>(lot);
                    for (var i = 0; i < puffer.Length; i++)
                    { var a = puffer[i]; a.Prefab = Pruefe(a.Prefab); puffer[i] = a; }
                }
            using (var lots = _stufeA.ToEntityArray(Allocator.Temp))
                foreach (var lot in lots)
                {
                    var s = EntityManager.GetComponentData<ParkingLotStufeAPrefab>(lot);
                    s.Prefab = Pruefe(s.Prefab);
                    EntityManager.SetComponentData(lot, s);
                }
            using (var sonden = _sonden.ToEntityArray(Allocator.Temp))
                foreach (var e in sonden)
                {
                    var s = EntityManager.GetComponentData<ParkingLotZoningProbeMarker>(e);
                    s.RoadPrefab = Pruefe(s.RoadPrefab);
                    EntityManager.SetComponentData(e, s);
                }

            Mod.log.Info("PLT-Prefabverweise: " + geprueft + " geprueft, "
                + umgebogen + " auf das geladene Prefab umgebogen, "
                + unbekannt + " ohne Prefab (bleiben stehen).");
        }
    }
}
