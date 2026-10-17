using System;
using System.Linq;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;
using System.Collections.Generic;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        /*
         * NUR KNOTEN, AN DIE DER WEG DARF (16.10.).
         *
         * Beide Suchen unten nahmen den naechsten Knoten in 5 cm ohne Blick auf
         * den Wegtyp. PLT legt aber verschiedene Wege an dieselben Punkte;
         * ParkingLotDauerkursSystem.Plane lehnte den Anschluss danach ab
         * ("unvereinbare Layer"), und der Hintergrund-Neubau scheiterte dreimal
         * an derselben Stelle. Dieselbe Regel wie der Rueckweg
         * (ParkingLotNetzRueckweg.KannAnschliessen).
         */
        private void HintergrundKnotenanschluss(ref Anschluss anschluss, ref float3 lage, Entity prefab)
        {
            if (EntityManager.HasComponent<Game.Net.Node>(anschluss.Entity))
            {
                lage = HintergrundAbgleich.Anschlusslage(lage,EntityManager.GetComponentData<Game.Net.Node>(anschluss.Entity).m_Position,true);
                return;
            }
            if (anschluss.Entity != Entity.Null) return; // Expliziter Anschluss wird nicht durch einen Nahknoten ersetzt.
            // Erhaltene Originale haben Vorrang vor neuen Portionsknoten.
            // Eine bereits vorhandene Doppel-ID darf nicht gewinnen.
            var kandidaten = new List<(int Id,float3 Lage)>();
            foreach (var e in _erhalteneNetzteile)
                if (ParkingLotNetzRueckweg.Lebt(EntityManager,e) && EntityManager.HasComponent<Game.Net.Node>(e)
                    && ParkingLotNetzRueckweg.KannAnschliessen(EntityManager,prefab,e))
                    kandidaten.Add((e.Index,EntityManager.GetComponentData<Game.Net.Node>(e).m_Position));
            int index = HintergrundAbgleich.ErhaltenerKnoten(lage.xz,kandidaten);
            if (index == -2) throw new InvalidOperationException($"Mehrdeutiger erhaltener Anschluss bei {lage}; 0 Ausgabe dieses Kurses.");
            foreach (var e in _erhalteneNetzteile)
                if (e.Index == index)
                {
                    anschluss = new Anschluss { Entity = e };
                    lage = EntityManager.GetComponentData<Game.Net.Node>(e).m_Position;
                    return;
                }
            var key = ((long)math.round(lage.x*40),(long)math.round(lage.z*40));
            var neue = new List<(Entity Id,float3 Lage)>();
            // 5 cm ueberdecken zwei 2,5-cm-Nachbarzellen in jeder Richtung.
            // Ein vorhandener gueltiger Knoten darf nicht am Indexrand fehlen.
            for (int x = -2; x <= 2; x++) for (int z = -2; z <= 2; z++)
                if (_hintergrundKnoten.TryGetValue((key.Item1+x,key.Item2+z),out var liste))
                    foreach (var n in liste)
                        if (ParkingLotNetzRueckweg.Lebt(EntityManager,n) && EntityManager.HasComponent<Game.Net.Node>(n)
                            && ParkingLotNetzRueckweg.KannAnschliessen(EntityManager,prefab,n))
                            neue.Add((n,EntityManager.GetComponentData<Game.Net.Node>(n).m_Position));
            var knoten = HintergrundAbgleich.Knoten(lage,neue,Entity.Null);
            if (knoten != Entity.Null)
            { anschluss = new Anschluss {Entity = knoten}; lage = EntityManager.GetComponentData<Game.Net.Node>(knoten).m_Position; return; }
        }

        private HintergrundPortion _anschlussportion;
        internal bool HintergrundMeldeAnschluesse()
        {
            _anschlussportion ??= new HintergrundPortion(HintergrundAnschlussmeldeschritte()) { Tempo = _hintergrundTempo };
            var uhr = System.Diagnostics.Stopwatch.StartNew();
            _anschlussportion.Weiter(() => uhr.Elapsed.TotalMilliseconds);
            return _anschlussportion.Fertig;
        }

        private IEnumerable<int> HintergrundAnschlussmeldeschritte()
        {
            StelleVersorgungsanschluesseWiederHer(_lotCarrier);
            yield return 0;
            // Erhaltene Strassen behalten Kante/Owner/Geometrie. Vanilla
            // erneuert ihre alten Fahrspuren aus den bereits fertigen Klonen;
            // diese echten Werte gehoeren zur 30-Bilder-Nachpruefung.
            int n = 0;
            foreach (var e in _erhalteneNetzteile)
            {
                if (ParkingLotNetzRueckweg.Lebt(EntityManager,e)) { EntityManager.AddComponent<Updated>(e); n++; }
                yield return 0;
            }
            ParkingLotNetzRueckweg.Melde($"Erhaltene Netzteile bei Vanilla angemeldet: {n}; 0 Owner-/Kurven-/Upgrade-Aenderungen.");
        }

        internal bool HintergrundAnschluesseDa(out int ist, out int soll)
        {
            ist = 0; soll = _versorgungsanschluesse.Count;
            foreach (var a in _versorgungsanschluesse)
                foreach (var e in _versorgungNeueKanten)
                {
                    if (!ParkingLotNetzRueckweg.Lebt(EntityManager,e)
                        || !EntityManager.HasBuffer<ConnectedNode>(e)
                        || !EntityManager.HasBuffer<ConnectedEdge>(a.Fremdknoten)) continue;
                    bool hin = ParkingLotPuffer.Hat(EntityManager.GetBuffer<ConnectedNode>(e,true), a.Fremdknoten);
                    bool zurueck = ParkingLotPuffer.Hat(EntityManager.GetBuffer<ConnectedEdge>(a.Fremdknoten,true), e);
                    if (!hin || !zurueck) continue;
                    // Gemeinsamer Vanilla-Graphhelfer erzeugt fehlende
                    // Simulations-Flusskanten; keine physische Netzkante
                    // oder deren Geometrie wird direkt geaendert.
                    VerbindeVersorgungsgraph(e,a.Fremdknoten,
                        EntityManager.GetComponentData<PrefabRef>(a.Fremdknoten).m_Prefab,a.Leitung);
                    ist++; break;
                }
            return ist == soll;
        }

        internal void HintergrundRueckwegFluss(Entity lot)
            => ParkingLotNetzRueckweg.StelleFlussWiederHer(EntityManager,lot,
                (kante,knoten,prefab) => VerbindeVersorgungsgraph(kante,knoten,prefab,"Rueckweg"));

        internal bool HintergrundBeobachtetFluss => _avPhase == AvPhase.Nachmessen || _zoningNamenRest > 0;
        internal void HintergrundPflegeFluss()
        { PflegeAutoVersorgungsmessung(); PflegeZoningNamenswache(); }
    }
}
