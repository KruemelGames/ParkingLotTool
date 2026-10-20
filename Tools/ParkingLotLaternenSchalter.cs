using Game;
using Game.Common;
using Game.Objects;
using Game.Rendering;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * SCHALTET UNSERE LATERNEN NACH TAG UND NACHT (2026-10-04).
     *
     * Die Inventur hat gezeigt: alle Laternen-Effekte brennen, solange
     * `StreetLight.m_State` nicht `TurnedOff` traegt. Diesen Zustand setzt
     * `StreetLightSystem` nur fuer Unterobjekte von Strassen, Gebaeuden und
     * Schiffen - an unserem Traeger niemand, die Laternen braennten also auch
     * am Tag. Strom gibt es auf dem Parkplatz nicht (Entscheidung 2026-08-26).
     *
     * Deshalb dieselbe Regel wie Vanilla, ohne Stromfrage: an, solange die
     * Tageshelligkeit (`LightingSystem.dayLightBrightness`, x1000) unter einer
     * Schwelle zwischen 200 und 300 liegt. Vanilla wuerfelt die Schwelle je
     * Strasse, damit nicht alle Strassen im selben Moment angehen; wir je
     * Parkplatz - seine Laternen schalten zusammen, wie an einer Zeitschaltuhr.
     * Gleiches Intervall wie Vanilla (16 Bilder).
     *
     * DER ZUSTAND ALLEIN SCHALTET NICHTS (Spielermeldung 20.10.: "die
     * Laternen gehen nachts nicht an"). Der Lichteffekt wird nur neu
     * bewertet, wenn die Laterne `EffectsUpdated` traegt; Vanilla haengt es
     * bei jedem Umschalten ueber den EndFrameBarrier an (StreetLightSystem,
     * UpdateStreetLightObjects). Ohne blieb das Licht so, wie es beim Bau war.
     */
    public sealed partial class ParkingLotLaternenSchalterSystem : GameSystemBase
    {
        private LightingSystem _licht;
        private EndFrameBarrier _barriere;
        private EntityQuery _laternen;

        public override int GetUpdateInterval(SystemUpdatePhase phase) => 16;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _licht = World.GetOrCreateSystemManaged<LightingSystem>();
            _barriere = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            _laternen = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadWrite<StreetLight>(), ComponentType.ReadOnly<ParkingLotPartRelation>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            RequireForUpdate(_laternen);
        }

        [Preserve]
        protected override void OnUpdate()
        {
            var helligkeit = (int)math.round(_licht.dayLightBrightness * 1000f);
            using var alle = _laternen.ToEntityArray(Allocator.Temp);
            int an = 0, ausgeschaltet = 0;
            EntityCommandBuffer befehle = default;
            bool hatBefehle = false;
            foreach (var e in alle)
            {
                var lot = EntityManager.GetComponentData<ParkingLotPartRelation>(e).Lot;
                var schwelle = new Random(math.hash(new int2(lot.Index, PseudoRandomSeed.kBrightnessLimit)) | 1u).NextInt(200, 300);
                var aus = helligkeit >= schwelle;
                var licht = EntityManager.GetComponentData<StreetLight>(e);
                if (((licht.m_State & StreetLightState.TurnedOff) != 0) == aus) continue;
                licht.m_State = aus ? licht.m_State | StreetLightState.TurnedOff : licht.m_State & ~StreetLightState.TurnedOff;
                EntityManager.SetComponentData(e, licht);
                if (!hatBefehle) { befehle = _barriere.CreateCommandBuffer(); hatBefehle = true; }
                befehle.AddComponent(e, default(EffectsUpdated));
                if (aus) ausgeschaltet++; else an++;
            }
            if (an + ausgeschaltet > 0)
                ParkingLotSchrittmarke.Aenderung("Laternen: " + an + " an, " + ausgeschaltet + " aus (Helligkeit " + helligkeit + ")");
        }
    }
}
