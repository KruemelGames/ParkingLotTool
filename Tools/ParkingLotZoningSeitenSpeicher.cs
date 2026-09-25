using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * HANDGESCHALTETE STRASSENSEITEN UEBERLEBEN DEN NEUBAU.
     *
     * Der Nutzer kann einzelne Seiten einer Zoning-Strasse umschalten. Ohne
     * diesen Speicher galte das nur bis zum naechsten Bau - der setzt alle
     * Seiten wieder auf die Panelwahl. Seine Frage: *"geht das auch in den
     * Bauzettel?"* Ja, und dort gehoert es hin: der Zettel ist das, woraus
     * derselbe Parkplatz wieder entsteht.
     *
     * DIE KANTE WIRD UEBER IHRE ENDPUNKTE WIEDERGEFUNDEN, nicht ueber ihre
     * Entity. Entity-Verweise ueberleben das Laden nicht - das hat der
     * Sondenlauf am 2026-09-02 gezeigt, als der Marker nach dem Laden
     * `Entity.Null` enthielt. Aendert sich die Geometrie so weit, dass keine
     * Kante mehr passt, verfaellt der Eintrag stillschweigend; das ist
     * besser, als eine Seite zu schalten, die jemand anders gemeint hat.
     */
    public sealed partial class ParkingLotToolSystem
    {
        private EntityQuery _lotCarrierQuery;

        // Beim Edit ist _editLot das gewaehlte Lot (Bauzettel 23.09.:
        // Einstieg Lot 63914). _lotOwner wird beim Edit-Einstieg 0-mal
        // gesetzt; nach einem Laden ist er null oder zeigt auf den letzten Bau.
        private Entity AktuellesZoningLot => IsEditing ? _editLot : _lotOwner;

        private void InitialisiereZoningSeitenSpeicher()
        {
            _lotCarrierQuery = GetEntityQuery(
                ComponentType.ReadOnly<ParkingLotCarrierReference>());
        }

        private bool LiestSeite(Entity kante, bool links)
        {
            if (!EntityManager.HasComponent<Upgraded>(kante)) return false;
            var flags = EntityManager.GetComponentData<Upgraded>(kante).m_Flags;
            var seite = links ? flags.m_Left : flags.m_Right;
            return (seite & CompositionFlags.Side.ZonesDisabled) != 0;
        }

        /** Dieselbe Kante, auch wenn sie andersherum gespeichert wurde. */
        private static bool ZoningSelbeKante(
            float2 a1, float2 b1, float2 a2, float2 b2)
        {
            const float toleranz = 0.5f;
            var gleich = math.distance(a1, a2) < toleranz
                && math.distance(b1, b2) < toleranz;
            var gedreht = math.distance(a1, b2) < toleranz
                && math.distance(b1, a2) < toleranz;
            return gleich || gedreht;
        }

        /** Die PLT-Flaeche, an der dieser Traeger haengt. */
        private Entity FindeFlaecheZuTraeger(Entity traeger)
        {
            if (_lotOwner != Entity.Null
                && EntityManager.Exists(_lotOwner)
                && EntityManager.HasComponent<ParkingLotCarrierReference>(_lotOwner)
                && EntityManager.GetComponentData<ParkingLotCarrierReference>(
                        _lotOwner).Carrier == traeger)
                return _lotOwner;

            using var flaechen = _lotCarrierQuery.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < flaechen.Length; i++)
            {
                if (EntityManager.GetComponentData<ParkingLotCarrierReference>(
                        flaechen[i]).Carrier == traeger)
                    return flaechen[i];
            }
            return Entity.Null;
        }
    }
}
