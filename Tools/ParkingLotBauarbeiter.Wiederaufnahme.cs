using System;
using System.Linq;
using System.Collections.Generic;
using Unity.Mathematics;
using System.Threading.Tasks;
using Game.Common;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private Entity _nebenarbeitLot;
        private long _nebenarbeitGeneration;
        private bool _nebenarbeitWiederaufnahme, _versorgungBestandUnklar;

        internal Task<ParkingLayout> BereiteNebenarbeit(Entity lot, int auftrag)
        {
            if (!_bauarbeiter || !ParkingLotBaukontextLeser.TryRead(EntityManager, lot, out var k, out var grund))
                throw new InvalidOperationException("Nebenarbeit ohne lesbaren Bauplan.");
            var carrier = EntityManager.GetComponentData<ParkingLotCarrierReference>(lot).Carrier;
            if (!ParkingLotSyncGueltigkeit.Bestand(EntityManager, lot, carrier))
                throw new InvalidOperationException("Nebenarbeit ohne dauerhaften Bestand.");
            _nebenarbeitGeneration = World.GetOrCreateSystemManaged<ParkingLotAuftragsSystem>().Register.Generation;
            _baukontext = k; _lotOwner = lot; _lotCarrier = carrier; _nebenarbeitLot = lot;
            _definitionsmodus = ParkingLotDefinitionsmodus.Permanent; _definitionsauftrag = auftrag;
            _nebenarbeitWiederaufnahme = true; _hintergrundStufeBBild = UnityEngine.Time.frameCount - 36;
            _points.Clear(); _points.AddRange(k.Punkte.Select(p => p.xz));
            _worldPoints.Clear(); _worldPoints.AddRange(k.Punkte);
            _entrances.Clear(); _entrances.AddRange(k.Zugaenge);
            _busStops.Clear(); _busStops.AddRange(k.Bushalte);
            _zoningflaechen.Clear(); _zoningflaechen.AddRange(k.Zonen ?? Array.Empty<ParkingGeometry.Zoningflaeche>());
            _randzoning.Clear(); _randzoning.AddRange(k.Randzoning);
            _zoningSeitenPlan.Clear(); _zoningSeitenPlan.AddRange(k.Seitenplan);
            LadeZoningBedienwerte(k.Zettel);
            _areaPreviewSettings = LayoutEinstellungen(k);
            var polygon = k.Punkte.Select(p => p.xz).ToArray();
            var settings = _areaPreviewSettings;
            return Task.Run(() => ParkingGeometry.Build(polygon, settings));
        }

        internal void BeginneNebenarbeit(ParkingLayout layout)
        {
            _areaPreviewLayout = layout;
            if (ParkingLotNebenarbeitDaten.Offen(EntityManager, _lotOwner, NebenarbeitPflicht.Zoning))
                PlaneZoningBlockmessung(_lotCarrier);
            var m = EntityManager.GetComponentData<ParkingLotNebenarbeitOffen>(_lotOwner);
            if (!m.VersorgungBekannt)
                Nebenarbeitsdiagnose(() => Mod.log.Warn($"PLT-Nebenarbeit: Lot {_lotOwner.Index}, alter Marker ohne Versorgungswahl; keine neuen Leitungen auf Verdacht."));
        }

        private void BereiteVersorgungswiederaufnahme()
        {
            if (!_nebenarbeitWiederaufnahme || !ParkingLotNebenarbeitDaten.Offen(EntityManager, _lotOwner, NebenarbeitPflicht.Versorgung)) return;
            LadeVersorgungsverbindungen(_lotOwner);
            // Unvollstaendige historische Leitungen ohne geprueften Anschluss
            // sind kein Anlass fuer eine zweite Leitung auf derselben Strecke.
            using var q = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ParkingLotVersorgungsleitung>(),
                ComponentType.Exclude<Temp>(), ComponentType.Exclude<Deleted>());
            using var kanten = q.ToEntityArray(Allocator.Temp);
            _versorgungBestandUnklar = false;
            foreach (var e in kanten)
                if (EntityManager.GetComponentData<ParkingLotVersorgungsleitung>(e).Lot == _lotOwner
                    && !_avQuittierteKanten.Contains(e)) _versorgungBestandUnklar = true;
            if (_versorgungBestandUnklar)
            {
                _avPhase = AvPhase.Idle;
                Nebenarbeitsdiagnose(() => Mod.log.Warn($"PLT-Nebenarbeit: Lot {_lotOwner.Index}, Leitungsbestand ohne vollstaendigen Apply-Nachweis; Versorgungspflicht bleibt offen, kein doppelter Anschluss."));
            }
        }

        private void PflegeNebenarbeitQuittung()
        {
            var register = World.GetOrCreateSystemManaged<ParkingLotAuftragsSystem>().Register;
            if (!ParkingLotNebenarbeitDaten.Generation(register.Bereit, register.Generation, _nebenarbeitGeneration)) return;
            var lot = _nebenarbeitLot;
            if (!EntityManager.Exists(lot) || EntityManager.HasComponent<Temp>(lot)
                || EntityManager.HasComponent<Deleted>(lot) || !EntityManager.HasComponent<ParkingLotNebenarbeitOffen>(lot)) return;
            if (_pendingBusLot == lot && _pendingBusStops.Length == 0 && _busStopAuditFrame < 0
                && EntityManager.HasComponent<ParkingLotBuildReceipt>(lot))
            {
                int soll = EntityManager.GetComponentData<ParkingLotBuildReceipt>(lot).BusStopCount;
                var geplant = new List<float2>(); var vorhanden = new List<float2>();
                if (EntityManager.HasBuffer<ParkingLotBuildBusStop>(lot))
                {
                    var p = EntityManager.GetBuffer<ParkingLotBuildBusStop>(lot, true);
                    for (int i = 0; i < p.Length; i++)
                        geplant.Add(BusStopSnap.SignPosition(new BusStopPlacement
                            { A = p[i].A, B = p[i].B, Along = p[i].Along, Left = p[i].Left }));
                }
                using var teile = _editRelatedParts.ToEntityArray(Allocator.Temp);
                foreach (var e in teile)
                    if (EntityManager.GetComponentData<ParkingLotPartRelation>(e).Lot == lot
                        && EntityManager.HasComponent<Game.Routes.TransportStop>(e)
                        && EntityManager.HasComponent<Game.Objects.Transform>(e))
                        vorhanden.Add(EntityManager.GetComponentData<Game.Objects.Transform>(e).m_Position.xz);
                if (geplant.Count == soll && Erzeugungsherkunft.BushalteVollstaendig(geplant, vorhanden))
                {
                    ParkingLotNebenarbeitDaten.Quittiere(EntityManager, lot, NebenarbeitPflicht.Bushalte);
                    World.GetOrCreateSystemManaged<ParkingLotErzeugungsSystem>().BestaetigeBushalte(lot);
                }
            }
            if (!_versorgungBestandUnklar && !IsEditing && (_avPhase == AvPhase.Idle || _avPhase == AvPhase.Nachmessen)
                && _avNochOffeneNetze == 0)
            {
                ParkingLotNebenarbeitDaten.Quittiere(EntityManager, lot, NebenarbeitPflicht.Versorgung);
                World.GetOrCreateSystemManaged<ParkingLotErzeugungsSystem>().BestaetigeVersorgung(lot);
            }
            if (_zoningSeitenFrames <= 0 && _zoningBlockFrames <= 0 && _zoningNamenRest <= 0
                && _zoningNamenTraeger == Entity.Null)
                ParkingLotNebenarbeitDaten.Quittiere(EntityManager, lot, NebenarbeitPflicht.Zoning);
            if (!_bauarbeiter && EntityManager.GetComponentData<ParkingLotNebenarbeitOffen>(lot).Offen == NebenarbeitPflicht.Keine)
            {
                EntityManager.RemoveComponent<ParkingLotNebenarbeitOffen>(lot);
                Nebenarbeitsdiagnose(() => Mod.log.Info($"PLT-Nebenarbeit: Lot {lot.Index}, alle Pflichten bestaetigt."));
            }
        }

        internal bool HintergrundNebenarbeitQuittiert()
        {
            PflegeNebenarbeitQuittung();
            return !EntityManager.HasComponent<ParkingLotNebenarbeitOffen>(_lotOwner)
                || EntityManager.GetComponentData<ParkingLotNebenarbeitOffen>(_lotOwner).Offen == NebenarbeitPflicht.Keine;
        }

        private static void Nebenarbeitsdiagnose(Action aktion) => ParkingLotNebenarbeitDaten.Diagnose(aktion);
    }
}
