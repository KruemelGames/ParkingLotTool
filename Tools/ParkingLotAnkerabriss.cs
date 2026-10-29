using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * DER ABRISS NIMMT DEN ANKER MIT (29.10.2026).
     *
     * Ein Neubau im Hintergrund (Sync, Reparatur) laesst unveraenderte
     * Strassen stehen. Sie gehoeren dann einem Netzerhalt-Anker - dem alten
     * Lot ohne Flaeche -, der per Owner am neuen haengt
     * (`HintergrundBesitzanker`); im SubNet des Lots stehen sie als geliehene
     * Verweise (`UebertrageZoningbestand`, nach dem Laden
     * `TrageAnkerkantenEin`). Die Strassen reisst der Bulldozer ueber diese
     * Verweise selbst ab (`BulldozeToolSystem.AddSubNets`), und CS2
     * entscheidet dabei ueber die Knoten.
     *
     * Hier bleibt nur, was der Bulldozer nicht sieht, und zwar im selben Bild,
     * direkt VOR `SubElementDeleteSystem` (PostTool):
     *
     *  - Der Anker selbst. Er steht in keinem Puffer des Lots; bisher nahm ihn
     *    erst der Aufraeumer in Modification2 mit, nach der Kaskade. Was CS2
     *    von seinen Knoten behalten hatte, zeigte dann auf einen zerstoerten
     *    Besitzer - das Muster des Absturzes vom 12.10. Mit `Deleted` im selben
     *    Bild behandelt die Kaskade seine Reste wie die des Lots: was an
     *    fremden Strassen haengt, bleibt und verliert nur den Besitzer.
     *  - Tote geliehene Verweise. CS2 pflegt nur den SubNet des echten
     *    Besitzers. Faellt eine geliehene Kante, solange das Lot lebt, bleibt
     *    ihr Eintrag im Lot stehen, und die Kaskade schriebe `Deleted` an eine
     *    Entity, die es nicht mehr gibt.
     *
     * Edit und Neubau haengen erhaltene Anker VOR dem Abriss des alten Lots auf
     * das neue um (`UebertrageZoningbestand`, `HintergrundBesitzanker`); ein
     * Rueckweg loescht nur den Ersatz, waehrend die Anker noch am alten,
     * lebenden Lot haengen. Beides beruehrt dieses System nicht.
     */
    internal sealed partial class ParkingLotAnkerabrissSystem : GameSystemBase
    {
        private EntityQuery _geloeschteLots, _anker;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _geloeschteLots = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ParkingLotCarrierReference>(), ComponentType.ReadOnly<Deleted>() },
                None = new[] { ComponentType.ReadOnly<Temp>() },
            });
            _anker = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ParkingLotZoninganker>(), ComponentType.ReadOnly<Owner>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            RequireForUpdate(_geloeschteLots);
        }

        [Preserve]
        protected override void OnUpdate()
        {
            var geloescht = new HashSet<Entity>();
            var tot = 0;
            using (var lots = _geloeschteLots.ToEntityArray(Allocator.Temp))
                foreach (var lot in lots)
                {
                    geloescht.Add(lot);
                    if (!EntityManager.HasBuffer<Game.Net.SubNet>(lot)) continue;
                    var puffer = EntityManager.GetBuffer<Game.Net.SubNet>(lot);
                    for (var i = puffer.Length - 1; i >= 0; i--)
                        if (!EntityManager.Exists(puffer[i].m_SubNet)) { puffer.RemoveAt(i); tot++; }
                }

            var faellig = new List<Entity>();
            var lotNummern = new HashSet<int>();
            if (!_anker.IsEmptyIgnoreFilter)
                using (var anker = _anker.ToEntityArray(Allocator.Temp))
                using (var besitzer = _anker.ToComponentDataArray<Owner>(Allocator.Temp))
                    for (var i = 0; i < anker.Length; i++)
                        if (geloescht.Contains(besitzer[i].m_Owner))
                        {
                            faellig.Add(anker[i]);
                            lotNummern.Add(besitzer[i].m_Owner.Index);
                        }
            foreach (var anker in faellig) EntityManager.AddComponent<Deleted>(anker);

            if (faellig.Count > 0 || tot > 0)
                Mod.log.Info($"PLT-Ankerabriss: {faellig.Count} Netzerhalt-Anker von Lot "
                    + $"{string.Join(", ", lotNummern)} im selben Bild mitgeloescht, {tot} tote "
                    + "Verweis(e) aus dem SubNet entfernt; ueber die Knoten entscheidet CS2.");
        }
    }
}
