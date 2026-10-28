using System.Collections.Generic;
using System.Linq;
using Colossal.Collections;
using Colossal.Mathematics;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * WEM GEHOEREN DIE WEGE EINES HERRENLOSEN TRAEGERS? (28.10.2026)
     *
     * "Parking Lot (192 spaces)" hat kein Wegenetz mehr: Bauprotokoll 44
     * Wege, Netz-Suchbaum 0, nur die Zoningstrassen am Lot sind da. Im selben
     * Spielstand haelt ein Traeger, auf den keine Lot-Flaeche verweist, 47
     * Kanten und 33 Knoten - seit mindestens 27.10. in jedem Log. Ob das die
     * Wege von 192 sind, sagt nur die Messung: wen der Traeger als Besitzer
     * nennt, wo seine Kanten liegen und in welchem Umriss, welche Prefabs,
     * ob sie versteckt sind oder keine Geometrie haben - und, sobald CS2 den
     * Suchbaum gefuellt hat, ob er sie fuehrt. VERAENDERT NICHTS.
     */
    public sealed partial class ParkingLotWaisenSystem
    {
        /** So viele Bilder nach dem Laden wird der Suchbaum befragt. */
        private const int SuchbaumWartezeit = 300;

        private readonly List<(Entity Traeger, List<Entity> Kanten, Bounds2 Kasten)> _herrenlosProbe = new();
        private int _bilderSeitLaden;

        private void BeschreibeHerrenlos(Entity traeger, List<Entity> lots)
        {
            var besitzer = EntityManager.HasComponent<Owner>(traeger)
                ? EntityManager.GetComponentData<Owner>(traeger).m_Owner : Entity.Null;
            var kanten = new List<Entity>();
            if (EntityManager.HasBuffer<Game.Net.SubNet>(traeger))
            {
                var puffer = EntityManager.GetBuffer<Game.Net.SubNet>(traeger, true);
                for (var i = 0; i < puffer.Length; i++)
                {
                    var k = puffer[i].m_SubNet;
                    if (EntityManager.Exists(k) && EntityManager.HasComponent<Game.Net.Edge>(k)
                        && EntityManager.HasComponent<Game.Net.Curve>(k)) kanten.Add(k);
                }
            }
            var min = new float2(float.MaxValue);
            var max = new float2(float.MinValue);
            var namen = new Dictionary<string, int>();
            int versteckt = 0, ohneGeometrie = 0, geloescht = 0, temp = 0, fremd = 0;
            foreach (var k in kanten)
            {
                var c = EntityManager.GetComponentData<Game.Net.Curve>(k).m_Bezier;
                min = math.min(min, math.min(c.a.xz, c.d.xz));
                max = math.max(max, math.max(c.a.xz, c.d.xz));
                var name = "?";
                if (EntityManager.HasComponent<PrefabRef>(k)
                    && _prefabSystem.TryGetPrefab<PrefabBase>(EntityManager.GetComponentData<PrefabRef>(k).m_Prefab, out var p)
                    && p != null) name = p.name;
                namen[name] = namen.TryGetValue(name, out var n) ? n + 1 : 1;
                if (EntityManager.HasComponent<Hidden>(k)) versteckt++;
                if (!EntityManager.HasComponent<Game.Net.EdgeGeometry>(k)) ohneGeometrie++;
                if (EntityManager.HasComponent<Deleted>(k)) geloescht++;
                if (EntityManager.HasComponent<Temp>(k)) temp++;
                if (!EntityManager.HasComponent<Owner>(k) || EntityManager.GetComponentData<Owner>(k).m_Owner != traeger) fremd++;
            }
            var lage = "keine Kanten";
            if (kanten.Count > 0)
            {
                var mitte = (min + max) * 0.5f;
                var umriss = new List<string>();
                foreach (var lot in lots)
                {
                    if (!ParkingLotBaukontextLeser.TryRead(EntityManager, lot, out var k, out _, melden: false)) continue;
                    var ring = k.Punkte.Select(q => q.xz).ToArray();
                    if (InRing(mitte, ring)) umriss.Add(lot.Index.ToString());
                }
                lage = $"Kasten ({min.x:0.0}/{min.y:0.0})..({max.x:0.0}/{max.y:0.0}), Mitte ({mitte.x:0.0}/{mitte.y:0.0}) "
                    + "in Umriss von Lot " + (umriss.Count > 0 ? string.Join(", ", umriss) : "keinem");
                _herrenlosProbe.Add((traeger, kanten, new Bounds2(min - 1f, max + 1f)));
            }
            var besitzerText = besitzer == Entity.Null ? "keiner"
                : besitzer.Index + (EntityManager.Exists(besitzer)
                    ? (EntityManager.HasComponent<Game.Areas.Area>(besitzer) ? " (Flaeche" : " (keine Flaeche")
                      + (EntityManager.HasComponent<ParkingLotCarrierReference>(besitzer)
                          ? ", verweist auf Traeger " + EntityManager.GetComponentData<ParkingLotCarrierReference>(besitzer).Carrier.Index
                          : ", ohne Traegerverweis")
                      + (EntityManager.HasComponent<Deleted>(besitzer) ? ", Deleted" : "") + ")"
                    : " (existiert nicht)");
            Mod.log.Info($"PLT-Waisen: herrenloser Traeger {traeger.Index} - Besitzer {besitzerText}; "
                + $"{kanten.Count} Kanten: {lage}; Prefabs "
                + string.Join(", ", namen.OrderByDescending(x => x.Value).Select(x => x.Value + "x " + x.Key))
                + $"; versteckt {versteckt}, ohne EdgeGeometry {ohneGeometrie}, Deleted {geloescht}, Temp {temp}, "
                + $"anderer Owner {fremd}.");
        }

        /** Die Traeger der Lot-Flaechen und ihre Kanten - ob einer leer ist. */
        private void BeschreibeLotTraeger(List<Entity> lots)
        {
            var zeilen = new List<string>();
            foreach (var lot in lots)
            {
                if (!EntityManager.HasComponent<ParkingLotCarrierReference>(lot)) continue;
                var t = EntityManager.GetComponentData<ParkingLotCarrierReference>(lot).Carrier;
                var kanten = 0;
                if (EntityManager.Exists(t) && EntityManager.HasBuffer<Game.Net.SubNet>(t))
                {
                    var puffer = EntityManager.GetBuffer<Game.Net.SubNet>(t, true);
                    for (var i = 0; i < puffer.Length; i++)
                        if (EntityManager.Exists(puffer[i].m_SubNet)
                            && EntityManager.HasComponent<Game.Net.Edge>(puffer[i].m_SubNet)) kanten++;
                }
                zeilen.Add($"Lot {lot.Index} -> Traeger {t.Index} ({kanten} Kanten)");
            }
            Mod.log.Info("PLT-Waisen: Traeger der Lot-Flaechen: " + string.Join(" | ", zeilen) + ".");
        }

        /** Einmal, wenn CS2 den Netz-Suchbaum nach dem Laden gefuellt hat. */
        private void PruefeNachDemLaden()
        {
            if (_herrenlosProbe.Count == 0 && _wegebestandLots.Count == 0) return;
            if (++_bilderSeitLaden < SuchbaumWartezeit) return;
            var baum = World.GetOrCreateSystemManaged<Game.Net.SearchSystem>().GetNetSearchTree(true, out var deps);
            deps.Complete();
            PruefeHerrenlos(baum);
            PruefeWegebestand(baum);
        }

        private void PruefeHerrenlos(NativeQuadTree<Entity, QuadTreeBoundsXZ> baum)
        {
            foreach (var (traeger, kanten, kasten) in _herrenlosProbe)
            {
                using var treffer = new NativeList<Entity>(256, Allocator.Temp);
                var suche = new KantenImKasten { Kasten = kasten, Treffer = treffer };
                baum.Iterate(ref suche);
                var gefunden = new HashSet<Entity>();
                for (var i = 0; i < treffer.Length; i++) gefunden.Add(treffer[i]);
                var drin = kanten.Count(k => gefunden.Contains(k));
                var lebend = kanten.Count(k => EntityManager.Exists(k) && !EntityManager.HasComponent<Deleted>(k));
                Mod.log.Info($"PLT-Waisen: herrenloser Traeger {traeger.Index} nach {SuchbaumWartezeit} Bildern: "
                    + $"{drin} von {kanten.Count} Kanten im Netz-Suchbaum, {lebend} noch vorhanden; "
                    + $"im selben Kasten fuehrt der Suchbaum {treffer.Length} Eintraege.");
            }
            _herrenlosProbe.Clear();
        }

        private struct KantenImKasten : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public Bounds2 Kasten;
            public NativeList<Entity> Treffer;

            public bool Intersect(QuadTreeBoundsXZ bounds) => MathUtils.Intersect(bounds.m_Bounds.xz, Kasten);

            public void Iterate(QuadTreeBoundsXZ bounds, Entity entity)
            {
                if (MathUtils.Intersect(bounds.m_Bounds.xz, Kasten)) Treffer.Add(entity);
            }
        }

        private static bool InRing(float2 p, float2[] ring)
        {
            var innen = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                if ((ring[i].y > p.y) != (ring[j].y > p.y)
                    && p.x < (ring[j].x - ring[i].x) * (p.y - ring[i].y) / (ring[j].y - ring[i].y) + ring[i].x)
                    innen = !innen;
            return innen;
        }
    }
}
