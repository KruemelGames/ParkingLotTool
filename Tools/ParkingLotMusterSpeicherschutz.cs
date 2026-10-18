using Game;
using Game.Common;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * KEIN VORSCHAUMUSTER IM SPIELSTAND - BEI JEDEM SPEICHERN.
     *
     * Der Nutzer am 18.10.: Flaechen, Baeume, Laternen oder andere Props der
     * echten Vorschau duerfen NIE gespeichert werden, auch nicht beim
     * Autospeichern. Der Speicherstart-Weg (GameManager.onGameSaveLoad)
     * raeumt schon vorher ab; schlaegt er fehl, faengt es diese Stufe.
     *
     * Sie laeuft in CS2s Serialize-Phase direkt vor dem SerializerSystem -
     * diese Phase durchlaeuft JEDES Speichern (F5, Menue, Autosave:
     * AutoSaveSystem -> GameManager.Save -> SaveSimulationData). Der
     * SerializerSystem schreibt keine Entity mit `Deleted`
     * (Game.Serialization/SerializerSystem.cs:201-207). Also markieren wir
     * hier jedes Muster als geloescht; danach baut die Vorschau es neu auf.
     */
    internal sealed partial class ParkingLotMusterSpeicherschutzSystem : GameSystemBase
    {
        private EntityQuery _offen;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _offen = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ParkingLotMusterflaeche>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
                Options = EntityQueryOptions.IncludeDisabledEntities,
            });
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (_offen.IsEmptyIgnoreFilter) return;
            int anzahl = _offen.CalculateEntityCount();
            EntityManager.AddComponent<Deleted>(_offen);
            ParkingLotHintergrundDiagnose.Sicher(() => Mod.log.Info(
                "PLT-SurfacePreview: Speicherschutz: " + anzahl
                + " Mustereintraege vor dem Schreiben als geloescht markiert."));
        }
    }
}
