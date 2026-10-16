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
     * das auch fuer Spielstaende, die schon mit dem Fehler gespeichert wurden
     * - ABER NUR, wenn CS2 das Prefab damals selbst mitgespeichert hat.
     * Issue #17 (2026-10-15): ein mit 1.0.10 gespeicherter Rueckweg kam mit
     * leeren Verweisen zurueck ("0 geprueft"). Seitdem tragen Rueckwegkurs
     * und Anschluss zusaetzlich die PrefabID; sie wird hier ZUERST
     * aufgeloest, das Umbiegen ist nur noch Rueckfall fuer alte Eintraege.
     * Fehlt beides, verwirft der Rueckweg sich und der Parkplatz wird aus
     * dem Bauplan neu gebaut (ParkingLotHintergrundSystem).
     */
    internal sealed partial class ParkingLotPrefabVerweiseSystem : GameSystemBase
    {
        private CheckPrefabReferencesSystem _pruefung;
        private EntityQuery _rueckwegkurse;
        private EntityQuery _erhalteneKurse;
        private EntityQuery _anschluesse;
        private EntityQuery _stufeA;
        private EntityQuery _stufeAID;
        private EntityQuery _sonden;

        protected override void OnCreate()
        {
            base.OnCreate();
            _pruefung = World.GetOrCreateSystemManaged<CheckPrefabReferencesSystem>();
            _rueckwegkurse = GetEntityQuery(ComponentType.ReadWrite<ParkingLotRueckwegkurs>());
            _erhalteneKurse = GetEntityQuery(ComponentType.ReadWrite<ParkingLotErhaltenerKurs>());
            _anschluesse = GetEntityQuery(ComponentType.ReadWrite<ParkingLotRueckweganschluss>());
            _stufeA = GetEntityQuery(ComponentType.ReadWrite<ParkingLotStufeAPrefab>());
            _stufeAID = GetEntityQuery(ComponentType.ReadWrite<ParkingLotStufeAPrefabID>());
            _sonden = GetEntityQuery(ComponentType.ReadWrite<ParkingLotZoningProbeMarker>());
        }

        protected override void OnUpdate()
        {
            if (_rueckwegkurse.IsEmptyIgnoreFilter && _erhalteneKurse.IsEmptyIgnoreFilter
                && _anschluesse.IsEmptyIgnoreFilter && _stufeA.IsEmptyIgnoreFilter
                && _stufeAID.IsEmptyIgnoreFilter && _sonden.IsEmptyIgnoreFilter) return;

            var verweise = _pruefung.GetPrefabReferences(this, out var abhaengig);
            abhaengig.Complete();
            int geprueft = 0, umgebogen = 0, unbekannt = 0, perName = 0, leer = 0;

            // Name zuerst; danach Check, damit das Prefab beim Speichern als
            // benutzt gilt (beim Laden laesst Check echte Prefabs unveraendert).
            Entity PruefeMitName(Entity prefab, ref FixedString512Bytes name)
            {
                var ausName = ParkingLotPrefabname.Aufloesen(EntityManager, name);
                if (ausName != Entity.Null)
                {
                    geprueft++;
                    if (ausName != prefab) perName++;
                    return verweise.Check(EntityManager, ausName);
                }
                if (prefab == Entity.Null && name.IsEmpty) leer++;
                var ergebnis = Pruefe(prefab);
                if (name.IsEmpty) name = ParkingLotPrefabname.Von(EntityManager, ergebnis);
                return ergebnis;
            }

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
                    { var k = puffer[i]; k.Prefab = PruefeMitName(k.Prefab, ref k.PrefabName); puffer[i] = k; }
                }
            using (var lots = _erhalteneKurse.ToEntityArray(Allocator.Temp))
                foreach (var lot in lots)
                {
                    var puffer = EntityManager.GetBuffer<ParkingLotErhaltenerKurs>(lot);
                    for (var i = 0; i < puffer.Length; i++)
                    { var k = puffer[i]; k.Bestand.Prefab = PruefeMitName(k.Bestand.Prefab, ref k.Bestand.PrefabName); puffer[i] = k; }
                }
            using (var lots = _anschluesse.ToEntityArray(Allocator.Temp))
                foreach (var lot in lots)
                {
                    var puffer = EntityManager.GetBuffer<ParkingLotRueckweganschluss>(lot);
                    for (var i = 0; i < puffer.Length; i++)
                    { var a = puffer[i]; a.Prefab = PruefeMitName(a.Prefab, ref a.PrefabName); puffer[i] = a; }
                }
            using (var lots = _stufeAID.ToEntityArray(Allocator.Temp))
                foreach (var lot in lots)
                {
                    var s = EntityManager.GetComponentData<ParkingLotStufeAPrefabID>(lot);
                    s.Prefab = PruefeMitName(s.Prefab, ref s.PrefabName);
                    EntityManager.SetComponentData(lot, s);
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
                + perName + " ueber den Namen gefunden, "
                + umgebogen + " auf das geladene Prefab umgebogen, "
                + unbekannt + " ohne Prefab (bleiben stehen), "
                + leer + " leer ohne Namen.");
        }
    }
}
