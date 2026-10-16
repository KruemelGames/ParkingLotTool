using Game.Common;
using Game.Prefabs;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotEditSection
    {
        private bool _bearbeitbar, _reparierbar;
        private string _grund = string.Empty;

        private bool IstParkplatz(Entity lot)
        {
            if (lot == Entity.Null || !EntityManager.Exists(lot)
                || EntityManager.HasComponent<Deleted>(lot) || EntityManager.HasComponent<Temp>(lot)
                || !EntityManager.HasComponent<Game.Areas.Area>(lot)) return false;
            if (EntityManager.HasComponent<ParkingLotCarrierReference>(lot)
                || EntityManager.HasComponent<ParkingLotBuildReceipt>(lot)) return true;
            return EntityManager.HasComponent<PrefabRef>(lot)
                && World.GetOrCreateSystemManaged<PrefabSystem>().TryGetPrefab<PrefabBase>(
                    EntityManager.GetComponentData<PrefabRef>(lot).m_Prefab, out var prefab)
                && prefab != null && prefab.name == ParkingLotToolSystem.LotOwnerPrefabName;
        }

        private void AktualisiereFreigabe(Entity lot)
        {
            _bauzettel = ParkingLotBaukontextLeser.Vollstaendig(EntityManager, lot);
            _waise = _waisen.Zustand(lot);
            var hintergrund = World.GetOrCreateSystemManaged<ParkingLotHintergrundSystem>();
            var sperrgrund = hintergrund.Sperrgrund(lot);
            var wiederherstellung = sperrgrund.Length > 0
                || EntityManager.HasBuffer<ParkingLotRueckwegkurs>(lot)
                || EntityManager.HasComponent<ParkingLotOffenerErsatz>(lot)
                || EntityManager.HasComponent<ParkingLotNeubauFaellig>(lot)
                // NebenarbeitOffen sperrt NICHT: ein Edit baut die Nacharbeit mit,
                // und ein liegengebliebener Marker darf Edit nie dauerhaft sperren.
                || EntityManager.HasBuffer<ParkingLotStufeAKnoten>(lot);
            var traeger = EntityManager.HasComponent<ParkingLotCarrierReference>(lot)
                ? EntityManager.GetComponentData<ParkingLotCarrierReference>(lot).Carrier : Entity.Null;
            var traegerDa = traeger != Entity.Null && EntityManager.Exists(traeger)
                && !EntityManager.HasComponent<Deleted>(traeger) && !EntityManager.HasComponent<Temp>(traeger)
                && !EntityManager.HasComponent<ParkingLotAbrissabsicht>(traeger);
            var sperre = EditFreigabe.Pruefe(wiederherstellung,
                World.GetOrCreateSystemManaged<ParkingLotSyncSystem>().ArbeitLaeuft,
                World.GetOrCreateSystemManaged<ParkingLotToolSystem>().IsEditing,
                _bauzettel, traegerDa, _waise, _waisen.Halbwaisen.Contains(lot) && (!_bauzettel || !traegerDa));
            _bearbeitbar = sperre == EditSperre.Keine;
            _reparierbar = EditFreigabe.DarfReparieren(sperre, _waise);
            _grund = sperrgrund.Length > 0 ? sperrgrund : Grundtext(sperre);
        }

        private static string Grundtext(EditSperre sperre) => sperre switch
        {
            EditSperre.Wiederherstellung => ParkingLotTexte.T("editSection.recovery"),
            EditSperre.Arbeit => ParkingLotTexte.T("editSection.work"),
            EditSperre.Bearbeitung => ParkingLotTexte.T("editSection.editing"),
            EditSperre.Waise => ParkingLotTexte.T("editSection.orphan"),
            EditSperre.Halbwaise => ParkingLotTexte.T("editSection.partial"),
            EditSperre.Bauplan => ParkingLotTexte.T("editSection.plan"),
            EditSperre.Traeger => ParkingLotTexte.T("editSection.carrier"),
            _ => string.Empty,
        };
    }
}
