using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        /**
         * Misst Kante und echte Autospuren nach dem Bau in mehreren Bildern.
         * Im Nutzerbericht vom 2026-09-23 wechselte der Pfeil sichtbar von
         * innen nach aussen. Die bisherigen 30-Frame-Befunde enthalten 0
         * Richtungswerte; darum werden Bild 1..10, 15 und 30 erfasst.
         */
        private void MeldeGassenrichtungImFrame()
        {
            var bild = UnityEngine.Time.frameCount - _gassenrichtungStartFrame;
            var ende = UnityEngine.Time.frameCount >= _gassenbefundAb;
            if (!ende && bild > 10 && bild != 15) return;

            var query = GetEntityQuery(
                ComponentType.ReadOnly<Edge>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
            using var kanten = query.ToEntityArray(Allocator.TempJob);
            foreach (var plan in _gassenplan)
            {
                var befund = GassenrichtungSatz(plan, kanten, out var zustand);
                if (!ende && zustand == plan.LetzterRichtungszustand) continue;
                plan.LetzterRichtungszustand = zustand;
                Mod.log.Info($"PLT-Gassenrichtung: Zufahrt {plan.Index}, Bild {bild}, "
                    + $"Soll {(plan.FaehrtHinaus ? "hinaus" : "hinein")}: "
                    + befund + ".");
            }
        }

        private string GassenrichtungSatz(Gassenplan plan,
            NativeArray<Entity> kanten, out string zustand)
        {
            var innen = math.normalizesafe(plan.Ende - plan.Mitte);
            var kandidat = Entity.Null;
            var kandidatAmStart = false;
            var abstand = float.MaxValue;
            var wert = float.MaxValue;
            for (var i = 0; i < kanten.Length; i++)
            {
                var entity = kanten[i];
                if (EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab
                    != plan.Gassenprefab) continue;
                var edge = EntityManager.GetComponentData<Edge>(entity);
                if (!EntityManager.HasComponent<Node>(edge.m_Start)
                    || !EntityManager.HasComponent<Node>(edge.m_End)) continue;
                var a = EntityManager.GetComponentData<Node>(edge.m_Start)
                    .m_Position.xz;
                var b = EntityManager.GetComponentData<Node>(edge.m_End)
                    .m_Position.xz;
                var da = math.distance(a, plan.Mitte);
                var db = math.distance(b, plan.Mitte);
                var amStart = da <= db;
                var nahe = amStart ? a : b;
                var fern = amStart ? b : a;
                var d = math.min(da, db);
                if (d > 15f || math.dot(fern - nahe, innen) <= 1f) continue;
                var score = d + math.distance(fern, plan.Ende) * 0.1f;
                if (score >= wert) continue;
                wert = score;
                abstand = d;
                kandidat = entity;
                kandidatAmStart = amStart;
            }

            if (kandidat == Entity.Null)
            {
                zustand = "keine Kante";
                return "keine fertige Gassenkante mit Endpunkt in 15 m "
                    + "und Fortsetzung zum Parkplatz gefunden";
            }

            var gefunden = EntityManager.GetComponentData<Edge>(kandidat);
            var wurzel = kandidatAmStart ? gefunden.m_Start : gefunden.m_End;
            var start = EntityManager.GetComponentData<Node>(gefunden.m_Start)
                .m_Position.xz;
            var ziel = EntityManager.GetComponentData<Node>(gefunden.m_End)
                .m_Position.xz;
            var kantenSkalar = math.dot(math.normalizesafe(ziel - start), innen);
            var strassen = 0;
            var andere = 0;
            if (EntityManager.HasBuffer<ConnectedEdge>(wurzel))
            {
                var verbunden = EntityManager.GetBuffer<ConnectedEdge>(wurzel, true);
                for (var i = 0; i < verbunden.Length; i++)
                {
                    var nachbar = verbunden[i].m_Edge;
                    if (nachbar == kandidat || !EntityManager.Exists(nachbar)
                        || EntityManager.HasComponent<Deleted>(nachbar)
                        || EntityManager.HasComponent<Temp>(nachbar)) continue;
                    andere++;
                    if (EntityManager.HasComponent<Road>(nachbar)
                        && EntityManager.HasComponent<PrefabRef>(nachbar)
                        && EntityManager.GetComponentData<PrefabRef>(nachbar)
                            .m_Prefab != plan.Gassenprefab)
                        strassen++;
                }
            }

            var vorwaerts = 0;
            var rueckwaerts = 0;
            var quer = 0;
            if (EntityManager.HasBuffer<Game.Net.SubLane>(kandidat))
            {
                var spuren = EntityManager.GetBuffer<Game.Net.SubLane>(
                    kandidat, true);
                for (var i = 0; i < spuren.Length; i++)
                {
                    var spur = spuren[i].m_SubLane;
                    if (spur == Entity.Null || !EntityManager.Exists(spur)
                        || !EntityManager.HasComponent<Game.Net.CarLane>(spur)
                        || !EntityManager.HasComponent<Curve>(spur)) continue;
                    var kurve = EntityManager.GetComponentData<Curve>(spur)
                        .m_Bezier;
                    var skalar = math.dot(math.normalizesafe(
                        kurve.d.xz - kurve.a.xz), innen);
                    if (skalar > 0.5f) vorwaerts++;
                    else if (skalar < -0.5f) rueckwaerts++;
                    else quer++;
                }
            }

            var kompositionVorwaerts = 0;
            var kompositionRueckwaerts = 0;
            var kompositionFlags = "fehlt";
            if (EntityManager.HasComponent<Composition>(kandidat))
            {
                var prefab = EntityManager.GetComponentData<Composition>(
                    kandidat).m_Edge;
                if (prefab != Entity.Null
                    && EntityManager.HasComponent<NetCompositionData>(prefab))
                {
                    kompositionFlags = EntityManager
                        .GetComponentData<NetCompositionData>(prefab)
                        .m_Flags.m_General.ToString();
                }
                if (prefab != Entity.Null
                    && EntityManager.HasBuffer<NetCompositionLane>(prefab))
                {
                    var spuren = EntityManager.GetBuffer<NetCompositionLane>(
                        prefab, true);
                    for (var i = 0; i < spuren.Length; i++)
                    {
                        var flags = spuren[i].m_Flags;
                        if ((flags & LaneFlags.Road) == 0) continue;
                        if ((flags & LaneFlags.Invert) != 0)
                            kompositionRueckwaerts++;
                        else kompositionVorwaerts++;
                    }
                }
            }

            var flaggen = EntityManager.HasComponent<NetGeometryData>(
                    plan.Gassenprefab)
                ? EntityManager.GetComponentData<NetGeometryData>(
                    plan.Gassenprefab).m_Flags.ToString()
                : "NetGeometryData fehlt";
            zustand = $"{kandidat.Index}:{kandidat.Version}/"
                + $"{(kantenSkalar > 0.5f ? 1 : kantenSkalar < -0.5f ? -1 : 0)}/"
                + $"{vorwaerts}/{rueckwaerts}/{quer}/{strassen}/{andere}/"
                + $"{kompositionVorwaerts}/{kompositionRueckwaerts}/"
                + kompositionFlags;
            return $"Kante {kandidat.Index}:{kandidat.Version}, "
                + $"Wurzel {(kandidatAmStart ? "Start" : "Ende")}, "
                + $"Abstand zur geplanten Strassenmitte {abstand:F2} m, "
                + $"Kantenrichtung Skalar {kantenSkalar:F3}, "
                + $"Autospuren hinein/hinaus/quer "
                + $"{vorwaerts}/{rueckwaerts}/{quer}, "
                + $"Kompositionsspuren ohne/mit Invert "
                + $"{kompositionVorwaerts}/{kompositionRueckwaerts}, "
                + $"Kompositionsflags {kompositionFlags}, "
                + $"an derselben Wurzel {strassen} andere Strassenkante(n) "
                + $"von {andere} sonstigen Kante(n), Prefabflags {flaggen}";
        }
    }
}
