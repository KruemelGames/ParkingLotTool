using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private void SichereVersorgungsverbindungen()
        {
            var lot = AvZugehoerigesLot();
            if (!EntityManager.Exists(lot)) return;
            // Puffer erst nach allen strukturellen Markierungen ausleihen.
            var p = EntityManager.HasBuffer<ParkingLotVersorgungsnachweis>(lot)
                ? EntityManager.GetBuffer<ParkingLotVersorgungsnachweis>(lot)
                : EntityManager.AddBuffer<ParkingLotVersorgungsnachweis>(lot);
            // Ein verworfener frueherer Apply desselben Ziels darf keine
            // leeren Referenzen im spaeter vollstaendigen Nachweis behalten.
            for (int i = p.Length - 1; i >= 0; i--)
                foreach (var k in _avKurse)
                    if (math.all(p[i].Start == k.Trasse.Start) && math.all(p[i].Ziel == k.Trasse.Ziel))
                    { p.RemoveAt(i); break; }
            foreach (var k in _avKurse)
            {
                var kanten = new List<Entity>(k.Kanten); kanten.AddRange(k.Anschlussstuecke);
                foreach (var e in kanten)
                {
                    bool da = false;
                    for (int i = 0; i < p.Length; i++) if (p[i].Kante == e) { da = true; break; }
                    if (!da) p.Add(new ParkingLotVersorgungsnachweis { Kante = e, Start = k.Trasse.Start, Ziel = k.Trasse.Ziel,
                        Stadt = EntityManager.Exists(k.Trasse.Zielkante) && !EntityManager.HasComponent<Game.Common.Owner>(k.Trasse.Zielkante) });
                }
            }
        }

        private void UebertrageVersorgungsnachweise(Entity alt, Entity neu, Entity carrier)
        {
            if (!EntityManager.HasBuffer<ParkingLotVersorgungsnachweis>(alt)) return;
            var gruppen = new Dictionary<(float3, float3, bool), List<ParkingLotVersorgungsnachweis>>();
            var quelle = EntityManager.GetBuffer<ParkingLotVersorgungsnachweis>(alt, true);
            for (int i = 0; i < quelle.Length; i++)
            {
                var q = quelle[i]; var key = (q.Start, q.Ziel, q.Stadt);
                if (!gruppen.TryGetValue(key, out var liste)) gruppen[key] = liste = new List<ParkingLotVersorgungsnachweis>();
                liste.Add(q);
            }
            var uebernehmen = new List<ParkingLotVersorgungsnachweis>();
            foreach (var g in gruppen.Values)
            {
                bool ganz = true;
                foreach (var q in g)
                    ganz &= ParkingLotTeilnetz.Lebt(EntityManager, q.Kante)
                        && !EntityManager.HasComponent<Game.Tools.Temp>(q.Kante)
                        && EntityManager.HasComponent<ParkingLotVersorgungsleitung>(q.Kante)
                        && EntityManager.GetComponentData<ParkingLotVersorgungsleitung>(q.Kante).Lot == neu
                        && EntityManager.GetComponentData<ParkingLotVersorgungsleitung>(q.Kante).Carrier == carrier;
                if (ganz) uebernehmen.AddRange(g);
            }
            if (uebernehmen.Count == 0) return;
            var ziel = EntityManager.HasBuffer<ParkingLotVersorgungsnachweis>(neu)
                ? EntityManager.GetBuffer<ParkingLotVersorgungsnachweis>(neu)
                : EntityManager.AddBuffer<ParkingLotVersorgungsnachweis>(neu);
            foreach (var q in uebernehmen)
            {
                bool da = false;
                for (int i = 0; i < ziel.Length; i++) if (ziel[i].Kante == q.Kante) { da = true; break; }
                if (!da) ziel.Add(q);
            }
        }

        private readonly HashSet<Entity> _avQuittierteKanten = new HashSet<Entity>();
        private void LadeVersorgungsverbindungen(Entity lot)
        {
            _avQuittierteKanten.Clear();
            if (!EntityManager.HasBuffer<ParkingLotVersorgungsnachweis>(lot)) return;
            var gruppen = new Dictionary<(float3, float3, bool), List<Entity>>();
            var p = EntityManager.GetBuffer<ParkingLotVersorgungsnachweis>(lot, true);
            for (int i = 0; i < p.Length; i++)
            {
                var q = p[i]; var key = (q.Start, q.Ziel, q.Stadt);
                if (!gruppen.TryGetValue(key, out var kanten)) gruppen[key] = kanten = new List<Entity>();
                kanten.Add(q.Kante);
            }
            foreach (var g in gruppen)
            {
                bool voll = g.Value.Count > 0;
                foreach (var e in g.Value)
                    voll &= ParkingLotTeilnetz.Lebt(EntityManager, e) && !EntityManager.HasComponent<Game.Tools.Temp>(e)
                        && EntityManager.HasComponent<ParkingLotVersorgungsleitung>(e)
                        && EntityManager.GetComponentData<ParkingLotVersorgungsleitung>(e).Lot == lot;
                if (voll)
                {
                    foreach (var e in g.Value) _avQuittierteKanten.Add(e);
                    _avVerbindungen.Add(new AvVerbindung { Start = g.Key.Item1, Ziel = g.Key.Item2, ZielIstStadt = g.Key.Item3 });
                }
            }
        }
    }
}
