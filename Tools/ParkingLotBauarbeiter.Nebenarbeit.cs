using System;
using System.Collections.Generic;
using System.Linq;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private bool _nebenarbeitBegonnen, _busDefinitionenFertig, _versorgungAnschlussAngestossen;
        private int _nebenbild;
        private EntityQuery AvDauerQuery() => GetEntityQuery(ComponentType.ReadOnly<Edge>(),
            ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<PrefabRef>(),
            ComponentType.ReadOnly<Owner>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Deleted>());

        private bool AvDauerAnStrasse(Entity knoten, IEnumerable<Entity> strassen, AvKurs kurs)
        {
            var kanten = new List<int2>();
            foreach (var e in kurs.Anschlussstuecke)
                if (ParkingLotNetzRueckweg.Lebt(EntityManager,e) && EntityManager.HasComponent<Edge>(e))
                { var k = EntityManager.GetComponentData<Edge>(e); kanten.Add(new int2(k.m_Start.Index,k.m_End.Index)); }
            var erreichbar = Versorgungsnetz.Erreichbar(new[] { knoten.Index },kanten);
            foreach (var e in strassen)
            {
                if (!ParkingLotNetzRueckweg.Lebt(EntityManager,e) || !EntityManager.HasComponent<Edge>(e)) continue;
                var k = EntityManager.GetComponentData<Edge>(e);
                if (erreichbar.Contains(k.m_Start.Index) || erreichbar.Contains(k.m_End.Index)) return true;
                if (EntityManager.HasBuffer<ConnectedNode>(e))
                    foreach (var n in EntityManager.GetBuffer<ConnectedNode>(e,true))
                        if (erreichbar.Contains(n.m_Node.Index)
                            && EntityManager.HasBuffer<ConnectedEdge>(n.m_Node)
                            && ParkingLotPuffer.Hat(EntityManager.GetBuffer<ConnectedEdge>(n.m_Node,true), e)) return true;
            }
            return false;
        }

        /** Der Erzeuger wird IMMER durch E1 aufgerufen. Die Trassenwahl und
         *  die Kursdefinitionen sind dieselben wie im regulaeren Edit. */
        private HintergrundPortion _nebenportion;
        internal int HintergrundNebenbild()
        {
            _nebenportion ??= new HintergrundPortion(HintergrundNebenschritte(),_hintergrundTempo);
            var uhr = System.Diagnostics.Stopwatch.StartNew();
            int n = _nebenportion.Weiter(() => uhr.Elapsed.TotalMilliseconds);
            _nebenbild = UnityEngine.Time.frameCount;
            // Auch das Zoning-Nachmessen kann eine Haltdefinition liefern.
            // Das Gate protokolliert die tatsaechlichen eigenen Definitionen.
            using var a = GetEntityQuery(ComponentType.ReadOnly<CreationDefinition>(),
                ComponentType.ReadOnly<ParkingLotAuftragsdefinition>()).ToEntityArray(Allocator.Temp);
            n = 0;
            foreach (var e in a)
                if (EntityManager.GetComponentData<ParkingLotAuftragsdefinition>(e).Auftrag == _definitionsauftrag) n++;
            return n;
        }
        private IEnumerable<int> HintergrundNebenschritte()
        {
            PruefeUeberlebendeAusgeblendete();
            if (!_nebenarbeitBegonnen)
            {
                _nebenarbeitBegonnen = true;
                _nebenarbeitLot = _lotOwner;
                MerkeAutoVersorgung(_lotCarrier);
                BereiteVersorgungswiederaufnahme();
                if (ParkingLotNebenarbeitDaten.Offen(EntityManager, _lotOwner, NebenarbeitPflicht.Bushalte))
                    PlanBusStopBuild(_lotOwner,_lotCarrier);
                // Erhaltene Halte bleiben Entity-genau erhalten. Nur die
                // tatsaechlich fehlenden Positionen brauchen Definitionen.
                var halte = new List<float2>();
                using (var teile = _editRelatedParts.ToEntityArray(Allocator.Temp))
                    foreach (var e in teile)
                        if (EntityManager.GetComponentData<ParkingLotPartRelation>(e).Lot == _lotOwner
                            && EntityManager.HasComponent<Game.Routes.TransportStop>(e)
                            && EntityManager.HasComponent<Game.Objects.Transform>(e))
                            halte.Add(EntityManager.GetComponentData<Game.Objects.Transform>(e).m_Position.xz);
                _pendingBusStops = _pendingBusStops.Where(s => !halte.Any(p => math.distance(p,BusStopSnap.SignPosition(s)) < .5f)).ToArray();
                // Mindestens 36 Bilder seit B und alter Abriss abgeschlossen:
                // dieselbe Seiten-/Blockmessung und Benennung wie im Edit.
                // Ihr Haltestellenerzeuger laeuft hier ebenfalls unter E1.
                if (ParkingLotNebenarbeitDaten.Offen(EntityManager, _lotOwner, NebenarbeitPflicht.Zoning))
                {
                    _zoningSeitenFrames = _zoningBlockFrames = 1;
                    PflegeZoningBlockmessung();
                }
                yield return 0;
            }
            if (!_busDefinitionenFertig)
            {
                if (_pendingBusStops.Length > 0) BuildBusStopsOnRoads(_lotCarrier);
                _busDefinitionenFertig = _pendingBusStops.Length == 0;
            }
            if (_avPhase == AvPhase.LotWarten)
            {
                _avKurse.Clear();
                foreach (int n in StarteAutoVersorgungSchritte(_lotCarrier)) yield return n;
                _versorgungAnschlussAngestossen = false;
            }

        }

        /** 0 wartet auf Materialisierung, 1 braucht ein weiteres exklusives
         *  Trassenbild, 2 ist fertig. Fehler der optionalen Versorgung werden
         *  wie heute gemeldet; sie machen keinen zweiten Parkplatzbau. */
        internal int HintergrundNebenstand()
        {
            PruefeUeberlebendeAusgeblendete();
            if (!_nebenarbeitBegonnen)
                return UnityEngine.Time.frameCount-_hintergrundStufeBBild < 36 ? 0 : 1;
            if (_nebenportion != null)
            {
                if (!_nebenportion.Fertig) return 1;
                _nebenportion.Dispose(); _nebenportion = null;
            }
            int vergangen = UnityEngine.Time.frameCount-_nebenbild;
            if (vergangen < 3) return 0;
            AuditBuiltBusStops();
            if (_busStopAuditFrame >= 0) return 0;
            if (!_busDefinitionenFertig && vergangen < 90) return 1;
            if (_avPhase == AvPhase.TempWarten)
            {
                if (!AvTempKantenDa())
                {
                    if (vergangen < 90) return 0;
                    ParkingLotNetzRueckweg.Melde("Versorgung: Permanent-Kurse nach 90 Bildern unvollstaendig; keine Erfolgsbehauptung fuer diese Trasse.");
                    return 3;
                }
                if (!_versorgungAnschlussAngestossen) return 4;
                bool gut = AvPruefeLeitungskollisionen() && AvPruefeTempAnschluesse();
                if (!gut && vergangen < 30) return 0;
                if (!gut) return 3;
                World.GetOrCreateSystemManaged<ParkingLotErzeugungsSystem>().BindeErgebnisse();
                MarkiereVersorgungVorApply(_lotOwner, _lotCarrier);
                SichereVersorgungsverbindungen();
                HintergrundNebenfluss();
                AvStarteBilddiagnose("Permanent-Nebenbau");
                foreach (var t in _avTrassen) AvMerkeAngewandt(t);
                _avGebaut.AddRange(_avKurse);
                MarkiereVersorgungsleitungen(_lotOwner,_lotCarrier);
                _avKurse.Clear(); _avTrassen.Clear();
                _avPhase = AvPhase.LotWarten;
                return 1;
            }
            PflegeZoningBlockmessung();
            if (_zoningSeitenFrames > 0 || _zoningBlockFrames > 0 || _zoningNamenRest > 0) return 0;
            if (!HintergrundNebenarbeitQuittiert())
                throw new InvalidOperationException("Nebenarbeit nicht vollstaendig; gespeicherte Restpflicht bleibt erhalten.");
            return 2;
        }

        internal int HintergrundVerwerfeNebenbau()
        {
            int n = 0;
            foreach (var k in _avKurse)
            {
                foreach (var e in k.Kanten.Concat(k.Anschlussstuecke))
                    if (ParkingLotNetzRueckweg.Lebt(EntityManager,e))
                    { EntityManager.AddComponent<Deleted>(e); n++; }
            }
            foreach (var t in _avTrassen) AvMerkeFehlschlag(t);
            _avKurse.Clear(); _avTrassen.Clear(); _avPhase = AvPhase.LotWarten;
            return n;
        }

        internal int HintergrundMeldeNebenanschluesse()
        {
            foreach (var k in _avKurse)
            {
                foreach (var e in k.Kanten.Concat(k.Anschlussstuecke))
                {
                    if (!EntityManager.HasComponent<Edge>(e)) continue;
                    var edge = EntityManager.GetComponentData<Edge>(e);
                    EntityManager.AddComponent<Updated>(edge.m_Start);
                    EntityManager.AddComponent<Updated>(edge.m_End);
                }
                foreach (var e in k.Trasse.Startnetz.Concat(new[] { k.Trasse.Zielkante }))
                    if (ParkingLotNetzRueckweg.Lebt(EntityManager,e)) EntityManager.AddComponent<Updated>(e);
            }
            _versorgungAnschlussAngestossen = true;
            _nebenbild = UnityEngine.Time.frameCount;
            ParkingLotNetzRueckweg.Melde("Versorgung: eigene Permanent-Knoten und Anschlussstrassen gemeinsam bei Vanilla angemeldet; 0 direkte physische Netzeintraege.");
            return 0;
        }

        private void HintergrundNebenfluss()
        {
            foreach (var k in _avKurse)
                foreach (var e in k.Trasse.Startnetz.Concat(new[] { k.Trasse.Zielkante }))
                    if (EntityManager.HasBuffer<ConnectedNode>(e))
                    {
                        // Graphhelfer kann Strukturveraenderungen ausloesen.
                        using var nodes = EntityManager.GetBuffer<ConnectedNode>(e,true).ToNativeArray(Allocator.Temp);
                        foreach (var n in nodes)
                            if (EntityManager.HasComponent<Owner>(n.m_Node)
                                && EntityManager.GetComponentData<Owner>(n.m_Node).m_Owner == _lotCarrier
                                && EntityManager.GetComponentData<PrefabRef>(n.m_Node).m_Prefab == k.Prefab)
                                VerbindeVersorgungsgraph(e,n.m_Node,k.Prefab,k.Name);
                    }
        }
    }
}
