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

        private void InitialisiereZoningSeitenSpeicher()
        {
            _lotCarrierQuery = GetEntityQuery(
                ComponentType.ReadOnly<ParkingLotCarrierReference>());
        }

        /** Schreibt eine Handschaltung in den Bauzettel des Parkplatzes. */
        private void MerkeZoningSeite(Entity kante, bool links, bool aus)
        {
            if (!EntityManager.HasComponent<Owner>(kante)) return;
            var traeger = EntityManager.GetComponentData<Owner>(kante).m_Owner;
            if (!EntityManager.Exists(traeger)) return;

            var lot = FindeFlaecheZuTraeger(traeger);
            if (lot == Entity.Null) return;
            if (!EntityManager.HasComponent<Curve>(kante)) return;

            var kurve = EntityManager.GetComponentData<Curve>(kante).m_Bezier;
            var a = new float2(kurve.a.x, kurve.a.z);
            var b = new float2(kurve.d.x, kurve.d.z);

            var puffer = EntityManager.HasBuffer<ParkingLotBuildZoningSeite>(lot)
                ? EntityManager.GetBuffer<ParkingLotBuildZoningSeite>(lot)
                : EntityManager.AddBuffer<ParkingLotBuildZoningSeite>(lot);

            for (var i = 0; i < puffer.Length; i++)
            {
                var vorhanden = puffer[i];
                if (vorhanden.Links != links) continue;
                if (!ZoningSelbeKante(vorhanden.A, vorhanden.B, a, b)) continue;
                vorhanden.Aus = aus;
                puffer[i] = vorhanden;
                return;
            }

            puffer.Add(new ParkingLotBuildZoningSeite
            {
                Version = ParkingLotBuildZoningSeite.CurrentVersion,
                A = a,
                B = b,
                Links = links,
                Aus = aus,
            });
        }

        /**
         * Traegt die gemerkten Handschaltungen wieder auf.
         *
         * Laeuft NACH der automatischen Seitenwahl - sonst wuerde diese die
         * Handschaltung gleich wieder ueberschreiben, und der Nutzer haette
         * denselben Aerger wie vorher.
         */
        private void WendeGemerkteZoningSeitenAn(Entity traeger)
        {
            if (traeger == Entity.Null || !EntityManager.Exists(traeger)) return;
            var lot = FindeFlaecheZuTraeger(traeger);
            if (lot == Entity.Null) return;
            if (!EntityManager.HasBuffer<ParkingLotBuildZoningSeite>(lot)) return;
            if (!EntityManager.HasBuffer<Game.Net.SubNet>(traeger)) return;

            var merker = EntityManager
                .GetBuffer<ParkingLotBuildZoningSeite>(lot, true);
            if (merker.Length == 0) return;

            var prefabs = SammleZoningPrefabs();
            if (prefabs.Count == 0) return;

            var angewandt = 0;
            var subNets = EntityManager.GetBuffer<Game.Net.SubNet>(traeger, true);
            for (var i = 0; i < subNets.Length; i++)
            {
                var kante = subNets[i].m_SubNet;
                if (!EntityManager.Exists(kante)) continue;
                if (!EntityManager.HasComponent<PrefabRef>(kante)) continue;
                if (!prefabs.Contains(
                        EntityManager.GetComponentData<PrefabRef>(kante).m_Prefab))
                    continue;
                if (!EntityManager.HasComponent<Curve>(kante)) continue;

                var kurve = EntityManager.GetComponentData<Curve>(kante).m_Bezier;
                var a = new float2(kurve.a.x, kurve.a.z);
                var b = new float2(kurve.d.x, kurve.d.z);

                for (var m = 0; m < merker.Length; m++)
                {
                    var eintrag = merker[m];
                    if (eintrag.Version
                        != ParkingLotBuildZoningSeite.CurrentVersion) continue;

                    /*
                     * TEILSTUECK GENUEGT - nicht dieselben Endpunkte.
                     *
                     * Hier stand `ZoningSelbeKante`, also der Vergleich
                     * beider Endpunkte. Das setzt voraus, dass aus einem
                     * geplanten Stueck genau eine Kante wird - und genau das
                     * tut CS2 nicht: es teilt an jedem Knoten. Aus einer
                     * geplanten 56-m-Strasse werden dann zwei oder drei
                     * Kanten, keine davon passt auf die gemerkten Punkte,
                     * und die Umschaltung verpuffte stumm.
                     *
                     * Der Nutzer hat es als *"das nachtraegliche Aendern,
                     * an welcher Strassenseite Tiles entstehen, wird nicht
                     * uebernommen"* gemeldet.
                     */
                    if (!ZoningTeilstueckVon(eintrag.A, eintrag.B, a, b,
                            out var gedreht)) continue;

                    // Liegt das Teilstueck andersherum als das gemerkte,
                    // ist sein "links" unser "rechts".
                    var links = gedreht ? !eintrag.Links : eintrag.Links;

                    // Nur die gemeinte Seite anfassen, die andere bleibt so,
                    // wie die automatische Wahl sie gesetzt hat.
                    var linksAus = links ? eintrag.Aus : LiestSeite(kante, true);
                    var rechtsAus = links ? LiestSeite(kante, false) : eintrag.Aus;
                    if (SetzeSeitenflaggen(kante, linksAus, rechtsAus)) angewandt++;
                }
            }

            if (angewandt > 0)
                Mod.log.Info($"PLT-Zoningseiten: {angewandt} Handschaltung(en) "
                    + "aus dem Bauzettel wiederhergestellt.");
        }

        private bool LiestSeite(Entity kante, bool links)
        {
            if (!EntityManager.HasComponent<Upgraded>(kante)) return false;
            var flags = EntityManager.GetComponentData<Upgraded>(kante).m_Flags;
            var seite = links ? flags.m_Left : flags.m_Right;
            return (seite & CompositionFlags.Side.ZonesDisabled) != 0;
        }

        /**
         * Liegt die gebaute Kante auf der gemerkten Strecke?
         *
         * Geprueft wird, ob BEIDE Endpunkte der gebauten Kante nah an der
         * gemerkten Geraden und innerhalb ihrer Laenge liegen. Damit passt
         * auch ein Teilstueck - und das ist der Normalfall, weil CS2 an
         * jedem Knoten teilt.
         *
         * `gedreht` sagt, ob das Teilstueck gegen die gemerkte Richtung
         * laeuft. Dann sind links und rechts vertauscht.
         */
        private static bool ZoningTeilstueckVon(float2 pa, float2 pb,
            float2 ba, float2 bb, out bool gedreht)
        {
            gedreht = false;
            const float toleranz = 0.5f;
            var spanne = pb - pa;
            var laenge = math.length(spanne);
            if (laenge < 1e-3f) return false;
            var richtung = spanne / laenge;

            float Quer(float2 p)
            {
                var w = p - pa;
                return math.abs(richtung.x * w.y - richtung.y * w.x);
            }
            float Laengs(float2 p) => math.dot(p - pa, richtung);

            if (Quer(ba) > toleranz || Quer(bb) > toleranz) return false;
            var ta = Laengs(ba);
            var tb = Laengs(bb);
            if (math.min(ta, tb) < -toleranz) return false;
            if (math.max(ta, tb) > laenge + toleranz) return false;

            gedreht = tb < ta;
            return true;
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
