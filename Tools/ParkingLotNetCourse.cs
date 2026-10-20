using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private bool CreateCourseDefinition(
            string kind,
            int index,
            float2 from,
            float2 to,
            Entity prefab,
            ref TerrainHeightData heightData,
            Dictionary<(long, long), float> heights,
            ref Unity.Mathematics.Random random,
            Anschluss anschlussAnfang = default,
            Anschluss anschlussEnde = default,
            Game.Net.Upgraded? upgraded = null)
        {
            if (!math.all(math.isfinite(from)) || !math.all(math.isfinite(to)))
                throw new InvalidOperationException(
                    $"Der Fahrweg '{kind}' {index} enthält eine nicht-endliche Koordinate.");
            if (_bauarbeiter && _erhalteneKursketten.ContainsKey((kind,index))) return false;

            var a = new float3(from.x, SampleCourseHeight(from, ref heightData, heights),
                from.y);
            var b = new float3(to.x, SampleCourseHeight(to, ref heightData, heights),
                to.y);
            if (_definitionsmodus == ParkingLotDefinitionsmodus.Permanent)
            {
                // Erhaltenes Zoning gehoert noch dem alten Anker. Ein Endpunkt
                // auf dessen Knoten braucht die echte ID und volle Weltlage;
                // blosses NodeMap-Matching nach Lage beweist diesen Anschluss nicht.
                HintergrundKnotenanschluss(ref anschlussAnfang,ref a,prefab);
                HintergrundKnotenanschluss(ref anschlussEnde,ref b,prefab);
            }
            if (_bauarbeiter && (EntityManager.HasComponent<Edge>(anschlussAnfang.Entity)
                || EntityManager.HasComponent<Edge>(anschlussEnde.Entity)))
                throw new InvalidOperationException($"Sync-Kurs {kind}/{index} verlangt eine Kantenteilung: "
                    + $"Start {anschlussAnfang.Entity} t={anschlussAnfang.Teilung:F6}, Ende {anschlussEnde.Entity} t={anschlussEnde.Teilung:F6}; 0 Definitionen ausgegeben.");
            var length = math.distance(a, b);
            // Kuerzer als ein Meter ist kein Fahrweg, sondern ein Rundungsrest.
            // CS2 legt daraus einen Knoten ohne Kante an. Der Kern plant seit
            // Runde 15 keine solchen Stuecke mehr (gemeinsamer Vertrag); greift
            // die Pruefung trotzdem, fehlt ein Stueck - das melden, nicht schlucken.
            if (!(length >= ParkingGeometry.NetzMindestlaenge))
            {
                Mod.log.Warn($"PLT-Netz: Kurs {kind}/{index} mit {length:F3} m unter der Mindestlaenge ausgelassen.");
                return false;
            }

            // Ohne CoursePosFlags.FreeHeight - und das ist RICHTIG, aber die
            // frueher hier stehende Begruendung war falsch. Sie behauptete, das
            // Flag lese ausschliesslich `NetToolSystem`; geprueft worden waren
            // nur GenerateNodes/GenerateEdges/Validation. Am Volldekompilat vom
            // 2026-08-17 nachgesehen: `CourseSplitSystem.InitializeCoursePos`
            // (Zeile 1437) liest es ebenfalls und wuerde `m_Position.y` aus
            // Terrain- und Wasserhoehe NEU berechnen. Genau das wollen wir
            // nicht - unsere Hoehen sind bereits exakte Terrainwerte aus
            // `SampleCourseHeight`, je 2D-Punkt nur einmal abgetastet.
            //
            // Zusammen mit dem fehlenden Besitzer ist damit auch die alte
            // Absackerei erklaert. `CourseSplitSystem` Zeile 1933:
            //
            //     bool flag2 = (m_CreationDefinition.m_Owner == Entity.Null
            //                   && m_OwnerDefinition.m_Prefab == Entity.Null) || flag;
            //
            // Steht ein Besitzer in der CREATIONDEFINITION, ist flag2 falsch und
            // CS2 nimmt einen anderen Hoehenweg - damals lag die erste Einfahrt
            // dadurch 183 m zu tief (angefordert y=512,70, gebaut y=329,16).
            // Deshalb bleibt die CreationDefinition hier besitzerlos; der Owner
            // kommt erst an die fertigen Entities. Vanilla loest es voellig
            // anders: `ObjectSubNets` deklariert die Fahrwege IM PREFAB in
            // lokalen Koordinaten - eine Komponente, die es laut ComponentMenu
            // nur fuer BuildingPrefab und BuildingExtensionPrefab gibt, fuer
            // unser LotPrefab also nicht.
            var curve = NetUtils.StraightCurve(a, b);
            var definition = EntityManager.CreateEntity();
            EntityManager.AddComponentData(definition, new CreationDefinition
            {
                m_Prefab = prefab,
                m_RandomSeed = random.NextInt(),
            });
            EntityManager.AddComponent<Updated>(definition);
            /*
             * Die Gasse bringt ihren eigenen Knoten mit; Zebrastreifen
             * gehoeren dort nicht hin. `Upgraded` an der Definition wandert
             * mit auf die fertige Kante - so macht es CS2s eigenes
             * Strassenwerkzeug auch.
             */
            if (string.Equals(kind, "entrance-gasse", StringComparison.Ordinal))
                EntityManager.AddComponentData(definition, new Game.Net.Upgraded
                {
                    m_Flags = new Game.Prefabs.CompositionFlags(
                        default,
                        Game.Prefabs.CompositionFlags.Side.RemoveCrosswalk,
                        Game.Prefabs.CompositionFlags.Side.RemoveCrosswalk),
                });
            else if (upgraded.HasValue)
                EntityManager.AddComponentData(definition, upgraded.Value);
            EntityManager.AddComponentData(definition, new NetCourse
            {
                m_Curve = curve,
                m_Length = length,
                m_FixedIndex = -1,
                m_Elevation = float2.zero,
                m_StartPosition = new CoursePos
                {
                    m_Entity = anschlussAnfang.Entity,
                    m_SplitPosition = anschlussAnfang.Teilung,
                    m_Position = a,
                    m_Rotation = NetUtils.GetNodeRotation(MathUtils.StartTangent(curve)),
                    m_CourseDelta = 0f,
                    m_Elevation = float2.zero,
                    m_Flags = CoursePosFlags.IsFirst,
                    m_ParentMesh = -1,
                },
                m_EndPosition = new CoursePos
                {
                    m_Entity = anschlussEnde.Entity,
                    m_SplitPosition = anschlussEnde.Teilung,
                    m_Position = b,
                    m_Rotation = NetUtils.GetNodeRotation(MathUtils.EndTangent(curve)),
                    m_CourseDelta = 1f,
                    m_Elevation = float2.zero,
                    m_Flags = CoursePosFlags.IsLast,
                    m_ParentMesh = -1,
                },
            });
            RecordNetDefinition(kind, index, prefab, definition, a, b);
            return true;
        }

        /**
         * Setzt eine Hoehe fest, bevor das Gelaende befragt wird.
         *
         * Dieselbe Rasterung wie `SampleCourseHeight` - sonst traefe der
         * Schluessel nicht, und der Eintrag bliebe wirkungslos.
         */
        private static void MerkeHoehe(float2 point, float hoehe,
                                       Dictionary<(long, long), float> heights)
        {
            var key = ((long)math.round(point.x * 40f),
                       (long)math.round(point.y * 40f));
            heights[key] = hoehe;
        }

        private float SampleCourseHeight(float2 point, ref TerrainHeightData heightData,
                                         Dictionary<(long, long), float> heights)
        {
            // Ein Vierteldezimeter-Raster: fein genug, dass getrennte Knoten
            // getrennt bleiben, grob genug, dass zwei Segmente an derselben
            // Ecke garantiert dieselbe Hoehe bekommen.
            var key = ((long)math.round(point.x * 40f), (long)math.round(point.y * 40f));
            if (heights.TryGetValue(key, out var cached)) return cached;

            // Unter einem alten eigenen Weg gilt SEINE Hoehe (geprueft), nicht
            // das Gelaende, das er selbst geformt hat.
            if (HoeheUnterAltbestand(point, ref heightData, out var alt))
            {
                heights[key] = alt;
                return alt;
            }

            var height = TerrainUtils.SampleHeight(
                ref heightData, new float3(point.x, 0f, point.y));
            if (!math.isfinite(height))
                throw new InvalidOperationException(
                    "Die Terrain-Abtastung eines Fahrwegknotens ist nicht endlich.");
            heights[key] = height;
            return height;
        }
    }
}
