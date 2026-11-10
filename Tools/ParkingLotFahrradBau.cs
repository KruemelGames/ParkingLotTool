using System;
using System.Collections.Generic;
using System.Linq;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private bool Fahrradbau => _areaPreviewLayout?.Fahrrad != null;
        private readonly Dictionary<(float2, float2), float2> _fahrradZugaenge = new();
        private readonly Dictionary<(float2, float2), float> _fahrradZugangshoehen = new();

        private float2 FahrradZugangsende(Fahrradweg w, float2[] polygon = null, LayoutSettings settings = null)
        {
            if (_fahrradZugaenge.TryGetValue((w.A,w.B),out var gespeichert)) return gespeichert;
            var richtung = FahrradBaukurse.Zugangsrichtung(w,polygon ?? _areaPreviewLayout?.Grundstueck,
                settings ?? _areaPreviewSettings);
            var ende = w.A;
            if (SucheStadtstrasseFuerGasse(w.A,richtung,out var mitte,out _,out var halb,out _,out _,out var hoehe))
            {
                ende = mitte-richtung*(halb+.5f);
                _fahrradZugangshoehen[(w.A,w.B)] = hoehe;
            }
            else Mod.log.Warn("PLT-Bauzettel Fahrradzugang: keine Stadtstrasse im Strahl; LocalConnect bleibt Vanilla.");
            _fahrradZugaenge[(w.A,w.B)] = ende;
            return ende;
        }

        private void FahrradVorflaechen(FahrradPlan plan, float2[] polygon = null, LayoutSettings settings = null)
        {
            _fahrradZugaenge.Clear();
            _fahrradZugangshoehen.Clear();
            var ringe = new List<float2[]>();
            foreach (var w in FahrradBaukurse.Plan(plan))
            {
                if (!w.Zufahrt) continue;
                var ende = FahrradZugangsende(w,polygon,settings);
                if (math.distance(ende,w.A)<.5f) continue;
                var d = math.normalize(w.A-ende);
                var n = new float2(-d.y,d.x)*1.5f;
                var ring = new[]{ende-n,w.A-n,w.A+n,ende+n};
                if (Cs2Triangulierung.Dreiecke(ring)==2) ringe.Add(ring);
            }
            if (!(_baukontext?.Zettel.SurfaceApronOn ?? _uiSystem?.VorflaecheAn ?? true)
                || !(_baukontext?.Zettel.SurfaceRoadOn ?? _uiSystem?.FlaecheStrasseAn ?? true)) return;
            _vorflaechen = _vorflaechenSicht = ringe.ToArray();
            _vorflaechenArt = new int[ringe.Count];
        }

        private bool FahrradPrefab(string name, out Entity prefab)
        {
            prefab = Entity.Null;
            if (!_prefabSystem.TryGetPrefab(new PrefabID(nameof(StaticObjectPrefab), name), out var basis)
                || !_prefabSystem.TryGetEntity(basis, out prefab)) return false;
            return EntityManager.HasComponent<ObjectGeometryData>(prefab)
                && EntityManager.HasComponent<PlaceableObjectData>(prefab);
        }

        private bool FahrradPrefabsBereit(FahrradPlan plan)
        {
            var system = World.GetOrCreateSystemManaged<ParkingLotFahrradPrefabSystem>();
            if (!system.TryLot(plan.Layout, out _)) return false;
            foreach (var w in plan.Wege)
                if (w.Art == Fahrradwegart.Fahrradweg && !system.TryWeg(w.Zufahrt, w.Breite, out _)) return false;
            foreach (var o in plan.Objekte)
                if (!FahrradPrefab(o.Prefab, out _) || (o.BodenPrefab != null && !FahrradPrefab(o.BodenPrefab, out _))) return false;
            return true;
        }

        private IEnumerable<int> FahrradObjekte(FahrradPlan plan, TerrainHeightData heightData)
        {
            var random = new Unity.Mathematics.Random((uint)Environment.TickCount | 1u);
            var anzahl = 0;
            for (var i = 0; i < plan.Objekte.Count; i++)
            {
                var o = plan.Objekte[i];
                var vorn = new double2(-Math.Sin(o.Drehung), Math.Cos(o.Drehung));
                if (FahrradPrefab(o.Prefab, out var prefab)
                    && CreateBayDecalDefinition("Fahrradobjekt", i, prefab, (double2)o.Position, vorn,
                        ref heightData, ref random)) { anzahl++; yield return 1; }
                if (o.BodenPrefab != null && FahrradPrefab(o.BodenPrefab, out var boden)
                    && CreateBayDecalDefinition("Fahrradaufkleber", i, boden, (double2)o.Position, vorn,
                        ref heightData, ref random)) yield return 1;
            }
            Mod.log.Info($"PLT-Bauzettel Fahrradobjekte: {anzahl}/{plan.Objekte.Count}, Sollkapazitaet {plan.Kapazitaet}.");
            foreach (var n in FahrradZugaenge(plan, heightData)) yield return n;
        }

        /**
         * Bruecke Strasse <-> Gehflaeche wie bei Vanilla: BicycleParkingArea01
         * setzt 7 "Pedestrian Access Location" 0,5 m innerhalb der Lotkante
         * (sonde-1.log). Der Marker ist eine TakeoffLocation Pedestrian/Pedestrian:
         * WaypointConnectionSystem:1263-1308 sucht im 10-m-Umkreis eine Gehspur,
         * eine Gehspur eines anderen Besitzers und eine Radspur, RoutesModifiedSystem:
         * 315-328 baut daraus die Fusskante und die Radkante. Ohne ihn erreicht
         * niemand die reine Pedestrian-Gehflaeche, auch nicht zu Fuss nach dem Abstellen.
         */
        private IEnumerable<int> FahrradZugaenge(FahrradPlan plan, TerrainHeightData heightData)
        {
            if (!_prefabSystem.TryGetPrefab(new PrefabID("MarkerObjectPrefab", "Pedestrian Access Location"), out var basis)
                || !_prefabSystem.TryGetEntity(basis, out var marker)
                || !EntityManager.HasComponent<ObjectGeometryData>(marker))
            {
                Mod.log.Warn("PLT-Bauzettel Fahrradzugang: Vanilla-Marker 'Pedestrian Access Location' fehlt; Gehflaeche ohne Strassenanschluss.");
                yield break;
            }
            var random = new Unity.Mathematics.Random((uint)Environment.TickCount | 1u);
            var kurse = FahrradBaukurse.Plan(plan);
            var gesetzt = new List<float2>();
            foreach (var w in kurse)
            {
                // Nur das aeussere Stueck: sein A liegt auf der Lotkante, kein
                // anderer Zufahrtskurs endet dort.
                if (!w.Zufahrt || kurse.Any(k => k.Zufahrt && math.distance(k.B, w.A) < .01f)
                    || gesetzt.Any(p => math.distance(p, w.A) < .05f)) continue;
                var innen = math.normalizesafe(w.B - w.A);
                var abstand = math.distance(FahrradZugangsende(w), w.A);
                if (abstand > 8f)
                    Mod.log.Warn($"PLT-Bauzettel Fahrradzugang: Lotkante {abstand:F1} m von der Strasse; der Marker sucht nur 10 m weit.");
                if (CreateBayDecalDefinition("Fahrradzugang", gesetzt.Count, marker,
                        (double2)(w.A + innen * .5f), (double2)innen, ref heightData, ref random))
                    yield return 1;
                gesetzt.Add(w.A);
            }
            Mod.log.Info($"PLT-Bauzettel Fahrradzugang: {gesetzt.Count} Zugangsmarker.");
        }

        private IEnumerable<int> FahrradNetze(FahrradPlan plan, TerrainHeightData heightData)
        {
            var system = World.GetOrCreateSystemManaged<ParkingLotFahrradPrefabSystem>();
            var heights = new Dictionary<(long, long), float>();
            var random = new Unity.Mathematics.Random((uint)Environment.TickCount | 1u);
            var anzahl = 0;
            var kurse = FahrradBaukurse.Plan(plan);
            for (var i = 0; i < kurse.Length; i++)
            {
                var w = kurse[i];
                // D benutzt die native NavigationArea am Lot: keine nackten
                // Fusswegspuren, die LaneSystem:4296 ohne Transform-Wurzel
                // nicht fuer Fahrraeder freigibt. Die Linien sind Laufgassen.
                if (w.Art != Fahrradwegart.Fahrradweg) continue;
                if (!system.TryWeg(w.Zufahrt, w.Breite, out var prefab)) continue;
                var a = w.A;
                var zugang = w.Zufahrt;
                if (w.Zufahrt)
                {
                    a = FahrradZugangsende(w);
                    if (_fahrradZugangshoehen.TryGetValue((w.A,w.B),out var hoehe))
                        MerkeHoehe(a,hoehe,heights);
                }
                // Gefangene schraege Strasse: der Bogen knickt AM gesetzten
                // Polygonzugang, nicht quer durch die erste Buchtreihe.
                if (w.Zufahrt && math.distance(a,w.A)>.001f
                    && math.dot(math.normalizesafe(w.A-a),math.normalizesafe(w.B-w.A))<.9999f)
                {
                    if (math.distance(a,w.A)>=ParkingGeometry.NetzMindestlaenge)
                    {
                        if (CreateCourseDefinition("fahrrad-zugang",i,a,w.A,prefab,
                            ref heightData,heights,ref random)) { anzahl++; yield return 1; }
                        if (!system.TryWeg(false,3,out prefab)) continue;
                        zugang=false;
                    }
                    a=w.A;
                }
                if (CreateCourseDefinition(zugang ? "fahrrad-zugang" : "fahrrad-weg", i,
                    a, w.B, prefab, ref heightData, heights, ref random)) { anzahl++; yield return 1; }
            }
            Mod.log.Info($"PLT-Bauzettel Fahrradnetz: {anzahl} Kurse; Geh-Flaeche ueber NavigationArea, keine Auto-/Zoningkurse.");
        }

        private void FahrradTraeger()
        {
            // Eine einzige Zaehlwurzel: VehicleUtils beginnt am Traeger mit
            // seinen echten SubObjects. Beide Bicycle-Marker wuerden doppelt zaehlen.
            EntityManager.RemoveComponent<Game.Buildings.BicycleParkingFacility>(_lotOwner);
            EntityManager.AddComponent<Game.Buildings.BicycleParkingFacility>(_lotCarrier);
            EntityManager.AddComponentData(_lotCarrier, new Game.Buildings.ParkingFacility());
            var areas = EntityManager.AddBuffer<Game.Areas.SubArea>(_lotCarrier);
            foreach (var record in _areaTransferRecords)
                if (record.Kind == "FahrradGehflaeche" && record.MaterializedEntity != Entity.Null)
                    areas.Add(new Game.Areas.SubArea { m_Area = record.MaterializedEntity });
            Mod.log.Info("PLT-Bauzettel Fahrradtraeger: ParkingFacility/BicycleParkingFacility, 1 NavigationArea, 0 CarParkingFacility.");
        }
    }
}
