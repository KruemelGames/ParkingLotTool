using System.Collections.Generic;
using Colossal.Mathematics;
using Game;
using Game.Areas;
using Game.Buildings;
using Game.City;
using Game.Common;
using Game.Companies;
using Game.Policies;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotBuildingEconomySystem
    {
        /**
         * DAS URTEIL DARF NICHT EINMALIG SEIN.
         *
         * Bis zum 2026-08-27 stand hier ein `if (_companionPrefabConfigured)
         * return;` VOR allem, und ganz am Ende wurde das Flag gesetzt. Damit
         * fiel auch die Tauglichkeitspruefung genau einmal - und zwar 0,7
         * Sekunden nach dem Anmelden des Prefabs, als CS2 den Archetyp noch
         * gar nicht gebaut hatte. Aus dem Log des Nutzers:
         *
         *     PLT-Begleiter bleibt AUS: ... Archetyp=False, Vertrag=False,
         *     Flags=RestrictedPedestrian,... Strom=0
         *
         * Danach wurde nie wieder geprueft. Der Begleiter ist deshalb in der
         * ganzen Sitzung NIE entstanden - kein Laerm, keine Angestellten, und
         * folglich auch keine Angestellten-Zeile im Fenster. Es sah nach einem
         * Fehler in der Oberflaeche aus und war keiner.
         *
         * Getrennt wird jetzt: die Aenderungen am Prefab passieren einmal,
         * das Urteil bei jedem Durchgang neu, bis es taugt.
         */
        private void ConfigureCompanionPrefab(Entity prefab)
        {
            if (!_companionPrefabConfigured) MutierePrefab(prefab);
            NeutralisiereFlachzone(prefab);
            BeurteilePrefab(prefab);
        }

        /**
         * DIE SPERRE DECKT DIE FLACHZONE NICHT AB.
         *
         * `BuildingUtils.CalculateLotInfo` uebernimmt aus
         * `BuildingTerraformData` fuenf Groessen. `m_DontRaise` und
         * `m_DontLower` ueberschreiben davon nur `m_MinLimit`/`m_MaxLimit`,
         * also die Glaettung am Lotrand - und zwar mit einem umgedrehten
         * Rechteck `(8,8,-8,-8)`, das leer ist. Die Flachzone
         * `m_FlatX0..m_FlatZ1` reicht die Funktion unveraendert durch.
         *
         * Diese Zone entsteht in `BuildingInitializeSystem.InitializeTerraformData`
         * aus den Objektmassen des REZEPTS, das wir klonen - nicht aus unseren
         * eigenen. Sie planiert den Lotkern auf die Begleiterhoehe. Auf ebenem
         * Grund faellt das nicht auf; an Hang und Senke schon, und genau dort
         * meldete die Messung `TERRAFORM-SPERRE VERLETZT`.
         *
         * Null ist keine geratene Konstante: `InitializeTerraformData` liefert
         * bei einer Flaeche unter 2 m selbst den Mittelpunkt, und fuer ein
         * symmetrisches Lot ist der 0. Ein Markerpunkt ohne Mesh hat keine
         * Flaeche, die planiert werden will.
         *
         * Laeuft bei JEDEM Durchgang, nicht einmal: baut CS2 die Komponente
         * bei einem spaeteren `Updated` neu auf, wird sie wieder eingezogen.
         */
        private void NeutralisiereFlachzone(Entity prefab)
        {
            if (!EntityManager.HasComponent<BuildingTerraformData>(prefab))
                return;
            var terraform = EntityManager
                .GetComponentData<BuildingTerraformData>(prefab);
            if (FlachzoneNeutral(terraform)) return;

            if (!_flachzoneGemeldet)
            {
                _flachzoneGemeldet = true;
                Mod.log.Info("PLT-Begleiter: Flachzone aus dem Rezept war X "
                    + terraform.m_FlatX0.y.ToString("F3") + ".."
                    + terraform.m_FlatX1.y.ToString("F3") + " m, Z "
                    + terraform.m_FlatZ0.y.ToString("F3") + ".."
                    + terraform.m_FlatZ1.y.ToString("F3") + " m (Randwerte X "
                    + terraform.m_FlatX0.x.ToString("F3") + "/"
                    + terraform.m_FlatX0.z.ToString("F3") + ".."
                    + terraform.m_FlatX1.x.ToString("F3") + "/"
                    + terraform.m_FlatX1.z.ToString("F3") + "). Sie wird auf "
                    + "den Mittelpunkt zusammengezogen; die beiden Sperren "
                    + "erreichen sie nicht.");
            }

            terraform.m_FlatX0 = float3.zero;
            terraform.m_FlatZ0 = float3.zero;
            terraform.m_FlatX1 = float3.zero;
            terraform.m_FlatZ1 = float3.zero;
            EntityManager.SetComponentData(prefab, terraform);
        }

        private static bool FlachzoneNeutral(BuildingTerraformData terraform)
            => math.all(terraform.m_FlatX0 == 0f)
               && math.all(terraform.m_FlatZ0 == 0f)
               && math.all(terraform.m_FlatX1 == 0f)
               && math.all(terraform.m_FlatZ1 == 0f);

        /**
         * DEN ARCHETYP NACHHOLEN - `AddPrefab` BAUT IHN NICHT.
         *
         * Am 2026-08-27 stand im Log auch nach 600 Durchgaengen
         * `Archetyp=False`. Der Grund steht im Dekompilat:
         *
         *     ObjectPrefab.RefreshArchetype(...)     setzt m_Archetype
         *     ObjectPrefab.LateInitialize(...)       ruft RefreshArchetype
         *
         * Und `LateInitialize` ruft im Spiel NUR `PrefabInitializeSystem`,
         * gleich hinter seinem eigenen `AddPrefab`:
         *
         *     if (m_PrefabSystem.AddPrefab(...)) { InitializePrefab(...); }
         *     foreach (...) LateInitializePrefab(...);
         *
         * Wer wie wir `AddPrefab` selbst aufruft, bekommt die Entity mit allen
         * Prefabkomponenten - aber ohne Archetyp. Ein Prefab ohne Archetyp
         * kann nie eine Instanz erzeugen, und genau daran ist der Begleiter
         * seit dem Umbau auf das Vollrezept gescheitert.
         *
         * NICHT IM SELBEN FRAME WIE `AddPrefab`. Das steht hier nicht aus
         * Vorsicht, sondern aus Erfahrung: Anmelden und Benutzen im selben
         * Frame endet in einem nativen Absturz ohne Stapel. Dieser Aufruf
         * sitzt deshalb in `BeurteilePrefab`, also fruehestens einen Frame
         * spaeter, in einer anderen Phase.
         */
        private void HoleArchetypNach(Entity prefab)
        {
            if (_archetypNachgeholt) return;
            if (EntityManager.HasComponent<ObjectData>(prefab)
                && EntityManager.GetComponentData<ObjectData>(prefab)
                    .m_Archetype.Valid)
                return;
            if (!_prefabSystem.TryGetPrefab<BuildingPrefab>(prefab,
                    out var rezept) || rezept == null)
                return;

            _archetypNachgeholt = true;
            try
            {
                rezept.LateInitialize(EntityManager, prefab);
                var gueltig = EntityManager.HasComponent<ObjectData>(prefab)
                    && EntityManager.GetComponentData<ObjectData>(prefab)
                        .m_Archetype.Valid;
                Mod.log.Info("PLT-Begleiter: Archetyp nachgeholt ueber "
                    + "LateInitialize - jetzt gueltig: " + gueltig + ".");
            }
            catch (System.Exception fehler)
            {
                Mod.log.Error("PLT-Begleiter: LateInitialize ist "
                    + "fehlgeschlagen, der Begleiter bleibt aus. Ursache: "
                    + fehler);
            }
        }

        private void MutierePrefab(Entity prefab)
        {
            _companionPrefabConfigured = true;

            /*
             * KEIN `NoRoadConnection` MEHR - CS2 soll die Strasse selbst
             * finden und pflegen.
             *
             * Mit dem Flag steigt `RoadConnectionSystem` sofort aus (Zeile 73).
             * Damit faellt genau das System weg, das fuer ein Gebaeude
             *   - die zustaendige Strasse sucht,
             *   - `Building.m_RoadEdge` setzt,
             *   - den `ConnectedBuilding`-Eintrag anlegt UND wieder entfernt
             *     (die einzige Entfernstelle im ganzen Spiel, Zeile 628).
             *
             * Wir hatten das von Hand nachgebaut, und beide Anlaeufe sind im
             * Spiel gescheitert: erst eine INTERNE Fahrgasse als Strasse (kein
             * Strom, Warnsymbol bleibt), dann eine echte Stadtstrasse, in
             * deren Puffer wir selbst geschrieben haben (Absturz schon beim
             * Bauen). Eine fremde Datenstruktur pflegen, die man nicht besitzt,
             * geht nicht gut aus.
             *
             * Das Lot ist 1x1 statt 0x0, damit die Vanilla-Suche eine
             * Vorderseite hat, an der sie ansetzen kann.
             */
            var buildingData = EntityManager.GetComponentData<BuildingData>(prefab);
            buildingData.m_LotSize = new int2(1, 1);
            buildingData.m_Flags &= ~Game.Prefabs.BuildingFlags.NoRoadConnection;
            EntityManager.SetComponentData(prefab, buildingData);
            SetOrAdd(prefab, new PollutionData
            {
                m_NoisePollution = 5000f,
                m_ScaleWithRenters = false,
            });
            SetOrAdd(prefab, new DestructibleObjectData
            {
                // Der unsichtbare Simulationspunkt darf kein Brandziel werden.
                m_FireHazard = 0f,
                m_StructuralIntegrity = float.MaxValue,
            });
            SetOrAdd(prefab, new ObjectGeometryData
            {
                // Der Vollarchetyp enthaelt zwingend `Object`. Marker wird vom
                // normalen Raycast vor dem Mesh-Test verworfen. Unsichtbar
                // macht die Instanz `Game.Tools.Hidden`; der leere SubMesh-
                // Puffer ist die zweite, unabhaengige Schranke.
                m_Bounds = new Bounds3(new float3(-0.5f), new float3(0.5f)),
                m_Size = new float3(1f),
                m_Flags = Game.Objects.GeometryFlags.Marker,
                m_Layers = MeshLayer.Marker,
            });
            SetOrAdd(prefab, new WorkplaceData
            {
                m_MaxWorkers = 4,
                m_MinimumWorkersLimit = 0,
                m_WorkConditions = 0,
            });
            RemoveBufferIfPresent<AdditionalBuildingTerraformElement>(prefab);
        }

        private void BeurteilePrefab(Entity prefab)
        {
            if (_companionPrefabSafe) return;
            HoleArchetypNach(prefab);

            var buildingData = EntityManager.GetComponentData<BuildingData>(
                prefab);
            var objectData = EntityManager.GetComponentData<ObjectData>(prefab);
            var geometry = EntityManager.GetComponentData<ObjectGeometryData>(
                prefab);
            var subMeshes = EntityManager.HasBuffer<SubMesh>(prefab)
                ? EntityManager.GetBuffer<SubMesh>(prefab, true).Length
                : -1;
            var consumption = EntityManager.GetComponentData<ConsumptionData>(
                prefab);
            var archetype = objectData.m_Archetype;
            var archetypeVollstaendig = archetype.Valid
                && ArchetypeEnthaelt(archetype,
                    ComponentType.ReadOnly<Created>())
                && ArchetypeEnthaelt(archetype,
                    ComponentType.ReadOnly<Updated>())
                && ArchetypeEnthaelt(archetype,
                    ComponentType.ReadOnly<Game.Objects.Object>())
                && ArchetypeEnthaelt(archetype,
                    ComponentType.ReadOnly<Game.Objects.Transform>())
                && ArchetypeEnthaelt(archetype,
                    ComponentType.ReadOnly<Building>());
            var archetypeTerraform = archetype.Valid
                && ArchetypeEnthaelt(archetype,
                    ComponentType.ReadOnly<BuildingTerraformData>());
            var terraformVorhanden = EntityManager
                .HasComponent<BuildingTerraformData>(prefab);
            var terraform = terraformVorhanden
                ? EntityManager.GetComponentData<BuildingTerraformData>(prefab)
                : default(BuildingTerraformData);
            var terraformGesperrt = terraformVorhanden
                && terraform.m_DontRaise && terraform.m_DontLower;
            /*
             * ZWEI BEDINGUNGEN SIND HIER ENTFERNT WORDEN, WEIL SIE VERALTET
             * WAREN.
             *
             *     RequireRoad             wurde nie gesetzt - die Zeile
             *                             darueber loescht nur
             *                             `NoRoadConnection`.
             *     Strom > 0               `MutierePrefab` setzt den Verbrauch
             *                             ausdruecklich auf 0.
             *
             * Beide stammen aus dem Stromweg, den der Nutzer am 2026-08-26
             * abgeblasen hat ("wir lassen es ohne strom wie jetzt"). Die
             * Schranke blieb stehen und hat danach den Begleiter dauerhaft
             * verhindert - eine Bedingung, die der eigene Code nicht mehr
             * erfuellen KANN, ist keine Sicherung, sondern ein Ausschalter.
             */
            _companionPrefabSafe = archetypeVollstaendig
                && (geometry.m_Flags & Game.Objects.GeometryFlags.Marker) != 0
                && geometry.m_Layers == MeshLayer.Marker
                && subMeshes == 0
                && !archetypeTerraform
                && terraformGesperrt
                && !EntityManager.HasBuffer<AdditionalBuildingTerraformElement>(
                    prefab);

            var noise = EntityManager.GetComponentData<PollutionData>(prefab)
                .m_NoisePollution;
            if (_companionPrefabSafe)
                Mod.log.Info("PLT-Begleiter-Prefab einsatzbereit nach "
                    + _beurteilungen + " Durchgaengen: 1x1, Strom "
                    + consumption.m_ElectricityConsumption + ", Laermbasis "
                    + noise + ", Rezept mit Created/Updated/Object/Transform/"
                    + "Building, Marker-Geometrie, SubMesh 0, Rezept-Terraform "
                    + archetypeTerraform + ", Prefab-Terraform DontRaise="
                    + terraform.m_DontRaise + "/DontLower="
                    + terraform.m_DontLower + ". Das PLT-Lot-Prefab bleibt "
                    + "unveraendert.");
            /*
             * Der Archetyp entsteht erst ein paar Frames nach dem Anmelden.
             * Solange still bleiben - sonst steht die Fehlermeldung im Log,
             * waehrend gleich darauf alles in Ordnung ist. Erst wenn es nach
             * reichlich Durchgaengen immer noch nicht taugt, ist es ein Fehler.
             */
            else if (++_beurteilungen == BeurteilungenBisMeldung)
                Mod.log.Error("PLT-Begleiter bleibt AUS: sein Prefabrezept ist "
                    + "nicht gleichzeitig vollstaendig und unsichtbar. Werte: "
                    + "Archetyp=" + archetype.Valid
                    + ", Vertrag=" + archetypeVollstaendig
                    + ", Flags=" + buildingData.m_Flags
                    + ", Strom=" + consumption.m_ElectricityConsumption
                    + ", GeometryFlags=" + geometry.m_Flags
                    + ", Layers=" + geometry.m_Layers
                    + ", SubMesh=" + subMeshes
                    + ", Rezept-Terraform=" + archetypeTerraform
                    + ", Terraform=" + terraformVorhanden
                    + ", DontRaise=" + terraform.m_DontRaise
                    + ", DontLower=" + terraform.m_DontLower
                    + " nach " + _beurteilungen + " Durchgaengen. Die "
                    + "Lot-Flaeche wird nicht angetastet.");
        }

        private bool TerraformGesperrt(Entity prefab)
        {
            if (!EntityManager.HasComponent<BuildingTerraformData>(prefab))
                return false;
            var terraform = EntityManager
                .GetComponentData<BuildingTerraformData>(prefab);
            return terraform.m_DontRaise && terraform.m_DontLower;
        }

    }
}
