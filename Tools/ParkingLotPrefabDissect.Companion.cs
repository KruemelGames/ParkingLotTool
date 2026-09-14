using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game.Buildings;
using Game.Common;
using Game.Policies;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    /**
     * Der Begleiter-Teil des Prefab-Sezierers.
     *
     * Vanilla und PLT erzeugen ein Building aus `ObjectData.m_Archetype`.
     * Deshalb muessen drei verschiedene Mengen im selben Abzug stehen:
     * Prefab-Entity, abgeleiteter Laufzeitarchetyp und tatsaechlich gebaute
     * Entity. Die letzte Menge belegt zusaetzlich den PLT-Marker `Hidden`.
     */
    public sealed partial class ParkingLotToolSystem
    {
        private Entity FindePrefabMitName(string name)
        {
            if (_prefabSystem == null) return Entity.Null;
            var query = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());
            using var alle = query.ToEntityArray(Allocator.TempJob);
            for (var i = 0; i < alle.Length; i++)
                if (_prefabSystem.TryGetPrefab<PrefabBase>(alle[i], out var prefab)
                    && prefab != null
                    && string.Equals(prefab.name, name, StringComparison.Ordinal))
                    return alle[i];
            return Entity.Null;
        }

        private Entity FindeBegleiter(Entity lot)
        {
            var query = GetEntityQuery(
                ComponentType.ReadOnly<ParkingLotBuildingEconomyEnabled>(),
                ComponentType.ReadOnly<ParkingLotPartRelation>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
            using var alle = query.ToEntityArray(Allocator.TempJob);
            for (var i = 0; i < alle.Length; i++)
            {
                var relation = EntityManager.GetComponentData<
                    ParkingLotPartRelation>(alle[i]);
                if (lot == Entity.Null || relation.Lot == lot) return alle[i];
            }
            return Entity.Null;
        }

        private Entity FindeGebauteParkanlage(string prefabName,
            NativeArray<Entity> welt)
        {
            for (var i = 0; i < welt.Length; i++)
                if (string.Equals(PrefabnameVon(welt[i]), prefabName,
                    StringComparison.Ordinal))
                    return welt[i];
            return Entity.Null;
        }

        private void SchreibeBegleiterPrefab(StringBuilder sb, Entity prefab)
        {
            if (prefab == Entity.Null || !EntityManager.Exists(prefab))
            {
                sb.AppendLine("  'PLT Wirtschaftsbegleiter' nicht gefunden. "
                    + "Die Prefab-Anmeldung steht noch aus oder ist fehlgeschlagen.");
                return;
            }
            if (_prefabSystem.TryGetPrefab<PrefabBase>(prefab, out var managed)
                && managed != null)
                ZerlegePrefab(sb, prefab, managed.name, managed.GetType().Name);

            sb.AppendLine("  Abgeleitete Prefabdaten:");
            SchreibePrefabKernwerte(sb, prefab, "    ");
            SchreibeLaufzeitarchetyp(sb, prefab, "    ");
            SchreibePuffer(sb, prefab, "    ");
        }

        private void SchreibePrefabKernwerte(StringBuilder sb, Entity prefab,
            string einzug)
        {
            if (EntityManager.HasComponent<BuildingData>(prefab))
            {
                var data = EntityManager.GetComponentData<BuildingData>(prefab);
                sb.AppendLine(einzug + "BuildingData: LotSize=" + data.m_LotSize
                    + ", Flags=" + data.m_Flags);
            }
            else sb.AppendLine(einzug + "BuildingData: FEHLT");

            if (EntityManager.HasComponent<ConsumptionData>(prefab))
            {
                var data = EntityManager.GetComponentData<ConsumptionData>(prefab);
                sb.AppendLine(einzug + "ConsumptionData: Strom="
                    + data.m_ElectricityConsumption + ", Wasser="
                    + data.m_WaterConsumption + ", Muell="
                    + data.m_GarbageAccumulation);
            }
            else sb.AppendLine(einzug + "ConsumptionData: FEHLT");

            if (EntityManager.HasComponent<ObjectGeometryData>(prefab))
            {
                var data = EntityManager.GetComponentData<ObjectGeometryData>(
                    prefab);
                sb.AppendLine(einzug + "Unsichtbarkeit: GeometryFlags="
                    + data.m_Flags + ", Layers=" + data.m_Layers
                    + ", SubMesh="
                    + (EntityManager.HasBuffer<SubMesh>(prefab)
                        ? EntityManager.GetBuffer<SubMesh>(prefab, true).Length
                            .ToString()
                        : "KEIN PUFFER")
                    + ", Terraform="
                    + EntityManager.HasComponent<BuildingTerraformData>(prefab));
            }
            else sb.AppendLine(einzug + "Unsichtbarkeit: ObjectGeometryData FEHLT");

            if (EntityManager.HasBuffer<DefaultPolicyData>(prefab))
            {
                var policies = EntityManager.GetBuffer<DefaultPolicyData>(
                    prefab, true);
                sb.AppendLine(einzug + "DefaultPolicyData ("
                    + policies.Length + "):");
                for (var i = 0; i < policies.Length; i++)
                    sb.AppendLine(einzug + "  " + i + ": "
                        + PrefabdefinitionnameVon(policies[i].m_Policy));
            }
            else sb.AppendLine(einzug + "DefaultPolicyData: KEIN PUFFER");
        }

        private void SchreibeLaufzeitarchetyp(StringBuilder sb, Entity prefab,
            string einzug)
        {
            if (!EntityManager.HasComponent<ObjectData>(prefab))
            {
                sb.AppendLine(einzug + "ObjectData.m_Archetype: ObjectData FEHLT");
                return;
            }
            var archetyp = EntityManager.GetComponentData<ObjectData>(prefab)
                .m_Archetype;
            if (!archetyp.Valid)
            {
                sb.AppendLine(einzug + "ObjectData.m_Archetype: UNGUELTIG");
                return;
            }
            var komponenten = Komponenten(archetyp);
            sb.AppendLine(einzug + "ObjectData.m_Archetype ("
                + komponenten.Count + "):");
            sb.AppendLine(einzug + "  " + string.Join(", ", komponenten));
        }

        private void SchreibeBegleiterInstanz(StringBuilder sb, Entity begleiter)
        {
            if (begleiter == Entity.Null || !EntityManager.Exists(begleiter))
            {
                sb.AppendLine("  Kein Begleiter im Spielstand. Stufe 1 reicht "
                    + "fuer einen gefahrlosen Vergleich; danach PLT bauen und "
                    + "den Abzug erneut schreiben.");
                return;
            }
            sb.AppendLine("  Entity " + begleiter.Index + ", Prefab "
                + PrefabnameVon(begleiter) + ":");
            SchreibeArchetyp(sb, begleiter, "    ");
            SchreibePuffer(sb, begleiter, "    ");
            SchreibeBuildingZustand(sb, begleiter, "    ");
        }

        private void SchreibeBuildingZustand(StringBuilder sb, Entity entity,
            string einzug)
        {
            if (EntityManager.HasComponent<Building>(entity))
            {
                var building = EntityManager.GetComponentData<Building>(entity);
                sb.AppendLine(einzug + "Building: RoadEdge="
                    + EntityKurz(building.m_RoadEdge) + ", OptionMask="
                    + building.m_OptionMask + ", PaidParking="
                    + BuildingUtils.CheckOption(building,
                        BuildingOption.PaidParking));
            }
            else sb.AppendLine(einzug + "Building: FEHLT");

            sb.AppendLine(einzug + "Laufzeitdaten: ElectricityConsumer="
                + EntityManager.HasComponent<ElectricityConsumer>(entity)
                + ", ElectricityBuildingConnection="
                + EntityManager.HasComponent<
                    Game.Simulation.ElectricityBuildingConnection>(entity)
                + ", ParkingFacility="
                + EntityManager.HasComponent<Game.Buildings.ParkingFacility>(entity)
                + ", Object="
                + EntityManager.HasComponent<Game.Objects.Object>(entity)
                + ", Hidden="
                + EntityManager.HasComponent<Game.Tools.Hidden>(entity)
                + ", Updated=" + EntityManager.HasComponent<Updated>(entity)
                + ", Created=" + EntityManager.HasComponent<Created>(entity));

            if (EntityManager.HasComponent<ElectricityConsumer>(entity))
            {
                var strom = EntityManager.GetComponentData<ElectricityConsumer>(
                    entity);
                sb.AppendLine(einzug + "ElectricityConsumer: gewollt="
                    + strom.m_WantedConsumption + ", erfuellt="
                    + strom.m_FulfilledConsumption);
            }
            if (EntityManager.HasBuffer<Policy>(entity))
            {
                var policies = EntityManager.GetBuffer<Policy>(entity, true);
                sb.AppendLine(einzug + "Policy (" + policies.Length + "):");
                for (var i = 0; i < policies.Length; i++)
                    sb.AppendLine(einzug + "  " + i + ": "
                        + PrefabdefinitionnameVon(policies[i].m_Policy)
                        + ", Flags="
                        + policies[i].m_Flags + ", Adjustment="
                        + policies[i].m_Adjustment);
            }
        }

        private string PrefabdefinitionnameVon(Entity entity)
        {
            if (_prefabSystem != null
                && _prefabSystem.TryGetPrefab<PrefabBase>(entity, out var prefab)
                && prefab != null)
                return prefab.name;
            return PrefabnameVon(entity);
        }

        private static string EntityKurz(Entity entity)
            => entity == Entity.Null ? "Null" : entity.Index.ToString();

        private void SchreibeBegleiterDifferenzen(StringBuilder sb,
            Entity parkingLot01Prefab, Entity parkingLot04Prefab,
            Entity begleiterPrefab, Entity parkingLot01,
            Entity parkingLot04, Entity begleiter)
        {
            SchreibeBauteildifferenz(sb, "ParkingLot01-Bauteile",
                parkingLot01Prefab, "Begleiter-Bauteile", begleiterPrefab);
            SchreibeBauteildifferenz(sb, "ParkingLot04-Bauteile",
                parkingLot04Prefab, "Begleiter-Bauteile", begleiterPrefab);
            SchreibeEntityDifferenz(sb, "PREFAB ParkingLot01",
                parkingLot01Prefab, "Begleiter-PREFAB", begleiterPrefab);
            SchreibeEntityDifferenz(sb, "PREFAB ParkingLot04",
                parkingLot04Prefab, "Begleiter-PREFAB", begleiterPrefab);
            SchreibeArchetypDifferenz(sb, "ParkingLot01-Laufzeitrezept",
                parkingLot01Prefab, "Begleiter-Laufzeitrezept", begleiterPrefab);
            SchreibeArchetypDifferenz(sb, "ParkingLot04-Laufzeitrezept",
                parkingLot04Prefab, "Begleiter-Laufzeitrezept", begleiterPrefab);
            SchreibeEntityDifferenz(sb, "GEBAUT ParkingLot01", parkingLot01,
                "GEBAUT Begleiter", begleiter);
            SchreibeEntityDifferenz(sb, "GEBAUT ParkingLot04", parkingLot04,
                "GEBAUT Begleiter", begleiter);
            SchreibeRezeptGegenWirklichkeit(sb, begleiterPrefab, begleiter);
        }

        private void SchreibeBauteildifferenz(StringBuilder sb, string linksName,
            Entity links, string rechtsName, Entity rechts)
        {
            sb.AppendLine("  --- " + linksName + " <-> " + rechtsName + " ---");
            if (!TryBauteiltypen(links, out var linksTypen)
                || !TryBauteiltypen(rechts, out var rechtsTypen))
            {
                sb.AppendLine("    Vergleich nicht moeglich: mindestens ein "
                    + "Managed-Prefab fehlt.");
                return;
            }
            SchreibeMengendifferenz(sb, linksTypen, rechtsTypen, linksName,
                rechtsName, "Managed-Bauteiltypen");
        }

        private bool TryBauteiltypen(Entity entity,
            out SortedSet<string> typen)
        {
            typen = new SortedSet<string>(StringComparer.Ordinal);
            if (!IstVorhanden(entity) || _prefabSystem == null
                || !_prefabSystem.TryGetPrefab<PrefabBase>(entity, out var prefab)
                || prefab == null) return false;
            foreach (var bauteil in prefab.components)
                if (bauteil != null) typen.Add(bauteil.GetType().Name);
            return true;
        }

        private void SchreibeEntityDifferenz(StringBuilder sb, string linksName,
            Entity links, string rechtsName, Entity rechts)
        {
            sb.AppendLine("  --- " + linksName + " <-> " + rechtsName + " ---");
            if (!IstVorhanden(links) || !IstVorhanden(rechts))
            {
                sb.AppendLine("    Vergleich nicht moeglich: "
                    + (!IstVorhanden(links) ? linksName : rechtsName)
                    + " fehlt.");
                return;
            }
            SchreibeMengendifferenz(sb, Komponenten(links), Komponenten(rechts),
                linksName, rechtsName, "Komponenten");
            SchreibeMengendifferenz(sb, Pufferkomponenten(links),
                Pufferkomponenten(rechts), linksName, rechtsName, "Puffer");
        }

        private void SchreibeArchetypDifferenz(StringBuilder sb,
            string linksName, Entity linksPrefab, string rechtsName,
            Entity rechtsPrefab)
        {
            sb.AppendLine("  --- " + linksName + " <-> " + rechtsName + " ---");
            if (!TryLaufzeitarchetyp(linksPrefab, out var links)
                || !TryLaufzeitarchetyp(rechtsPrefab, out var rechts))
            {
                sb.AppendLine("    Vergleich nicht moeglich: mindestens ein "
                    + "ObjectData.m_Archetype fehlt oder ist ungueltig.");
                return;
            }
            SchreibeMengendifferenz(sb, links, rechts, linksName, rechtsName,
                "Archetypkomponenten");
        }

        private void SchreibeRezeptGegenWirklichkeit(StringBuilder sb,
            Entity prefab, Entity instanz)
        {
            sb.AppendLine("  --- Begleiter-Laufzeitrezept <-> GEBAUT Begleiter ---");
            if (!TryLaufzeitarchetyp(prefab, out var rezept)
                || !IstVorhanden(instanz))
            {
                sb.AppendLine("    Vergleich nicht moeglich: Rezept oder "
                    + "gebaute Begleiter-Entity fehlt.");
                return;
            }
            SchreibeMengendifferenz(sb, rezept, Komponenten(instanz),
                "Laufzeitrezept", "gebaute Entity", "Komponenten");
        }

        private bool TryLaufzeitarchetyp(Entity prefab,
            out SortedSet<string> komponenten)
        {
            komponenten = new SortedSet<string>(StringComparer.Ordinal);
            if (!IstVorhanden(prefab)
                || !EntityManager.HasComponent<ObjectData>(prefab)) return false;
            var archetyp = EntityManager.GetComponentData<ObjectData>(prefab)
                .m_Archetype;
            if (!archetyp.Valid) return false;
            komponenten = Komponenten(archetyp);
            return true;
        }

        private bool IstVorhanden(Entity entity)
            => entity != Entity.Null && EntityManager.Exists(entity);

        private void SchreibeMengendifferenz(StringBuilder sb,
            IEnumerable<string> links, IEnumerable<string> rechts,
            string linksName, string rechtsName, string art)
        {
            var a = new HashSet<string>(links, StringComparer.Ordinal);
            var b = new HashSet<string>(rechts, StringComparer.Ordinal);
            var nurLinks = a.Except(b).OrderBy(x => x,
                StringComparer.Ordinal).ToList();
            var nurRechts = b.Except(a).OrderBy(x => x,
                StringComparer.Ordinal).ToList();
            sb.AppendLine("    " + art + " nur " + linksName + " ("
                + nurLinks.Count + "): "
                + (nurLinks.Count == 0 ? "-" : string.Join(", ", nurLinks)));
            sb.AppendLine("    " + art + " nur " + rechtsName + " ("
                + nurRechts.Count + "): "
                + (nurRechts.Count == 0 ? "-" : string.Join(", ", nurRechts)));
        }

        private SortedSet<string> Komponenten(EntityArchetype archetyp)
        {
            var result = new SortedSet<string>(StringComparer.Ordinal);
            var typen = archetyp.GetComponentTypes(Allocator.Temp);
            for (var i = 0; i < typen.Length; i++)
                result.Add(typen[i].GetManagedType()?.Name
                    ?? typen[i].ToString());
            typen.Dispose();
            return result;
        }

        private SortedSet<string> Pufferkomponenten(Entity entity)
        {
            var result = new SortedSet<string>(StringComparer.Ordinal);
            var typen = EntityManager.GetChunk(entity).Archetype
                .GetComponentTypes(Allocator.Temp);
            for (var i = 0; i < typen.Length; i++)
                if (typen[i].IsBuffer)
                    result.Add(typen[i].GetManagedType()?.Name
                        ?? typen[i].ToString());
            typen.Dispose();
            return result;
        }
    }
}
