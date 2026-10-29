using Game;
using Game.Common;
using Game.Companies;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * Legt echte, vom Vanilla-Arbeitsmarkt besetzbare Arbeitsplaetze an.
     *
     * WorkProvider und Employee stehen gemeinsam auf derselben Building-
     * Entity. Damit nimmt WorkProviderSystem den Begleiter in seine normale
     * Abfrage auf und erzeugt FreeWorkplaces beziehungsweise echte Worker.
     */
    public sealed partial class ParkingLotEmployeeSystem : GameSystemBase
    {
        private EntityQuery _companions;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _companions = GetEntityQuery(
                ComponentType.ReadOnly<ParkingLotBuildingEconomyEnabled>(),
                ComponentType.ReadOnly<ParkingLotPartRelation>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Game.Buildings.Building>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
        }

        [Preserve]
        protected override void OnUpdate()
        {
            using var uhr = ParkingLotMessung.Miss(
                ParkingLotMessung.Sys.Angestellte);
            PflegePersonalprobe();
            if (!Mod.WirtschaftAn
                || _companions.IsEmptyIgnoreFilter) return;
            using var companions = _companions.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < companions.Length; i++)
                UpdateCompanion(companions[i]);
        }

        private void UpdateCompanion(Entity companion)
        {
            var relation = EntityManager
                .GetComponentData<ParkingLotPartRelation>(companion);
            if (relation.Lot == Entity.Null || !EntityManager.Exists(relation.Lot)
                || !EntityManager.HasComponent<ParkingLotEconomyData>(relation.Lot))
                return;
            var economy = EntityManager
                .GetComponentData<ParkingLotEconomyData>(relation.Lot);
            // Ohne Personal (Issue #23) wie ein kleiner Parkplatz: 0 Stellen,
            // CS2 entlaesst die Angestellten selbst.
            var workers = ParkingLotPersonal.Ohne(EntityManager, relation.Lot)
                ? 0 : CalculateEmployees(economy.Capacity);

            if (workers == 0)
            {
                // Kleine Anlagen haben nach der gemessenen Stufe keinen
                // Abschnitt. Es gibt daher auch keine 0-von-N-Attrappe.
                if (EntityManager.HasComponent<WorkProvider>(companion))
                {
                    var provider = EntityManager
                        .GetComponentData<WorkProvider>(companion);
                    provider.m_MaxWorkers = 0;
                    EntityManager.SetComponentData(companion, provider);
                }
                return;
            }

            if (!EntityManager.HasBuffer<Employee>(companion))
                EntityManager.AddBuffer<Employee>(companion);
            if (!EntityManager.HasComponent<WorkProvider>(companion))
            {
                ParkingLotSchrittmarke.Aenderung("Angestellte: Arbeitsplaetze an Begleiter " + companion.Index);
                EntityManager.AddComponentData(companion, new WorkProvider
                {
                    m_MaxWorkers = workers,
                });
                Mod.log.Info("PLT-Begleiter " + companion.Index + " fuer Lot "
                    + relation.Lot.Index
                    + " mit " + workers + " echten Arbeitsplaetzen.");
            }
            else
            {
                var provider = EntityManager
                    .GetComponentData<WorkProvider>(companion);
                if (provider.m_MaxWorkers != workers)
                {
                    provider.m_MaxWorkers = workers;
                    EntityManager.SetComponentData(companion, provider);
                }
            }
        }

        internal static int CalculateEmployees(int capacity)
            => capacity <= 40 ? 0 : capacity < 300 ? 2 : 4;

        /*
         * TEST IM SPIEL FUER "OHNE PERSONAL" (Issue #23, 29.10.2026).
         *
         * Nach jedem Umschalten im Fenster schreibt das Log den Zustand
         * sofort, nach 10 und nach 90 Sekunden - fuer JEDEN umgeschalteten
         * Parkplatz, nicht nur den letzten. Gemessen wird, was die Stadt
         * wirklich zahlt, gerechnet wie `CityServiceBudgetSystem`:
         * Instandhaltung (ServiceUsage) plus Loehne der Angestellten
         * (`GetWage(Stufe, staedtische Stelle)`). Ohne Personal muessen
         * Stellen, Angestellte und Loehne 0 sein, die Instandhaltung bleibt.
         * Entlassen und Einstellen macht CS2 nur bei laufender Simulation.
         */
        private sealed class Personalprobe
        {
            internal Entity Lot;
            internal bool Ohne;
            internal System.DateTime Start;
            internal int Stufe;
            internal int LoehneVorher;
        }

        private readonly System.Collections.Generic.List<Personalprobe> _proben = new();

        internal void StartePersonalprobe(Entity lot, bool ohne)
        {
            _proben.RemoveAll(p => p.Lot == lot);
            var zustand = Personalzustand(lot, ohne, out _, out var loehne);
            _proben.Add(new Personalprobe { Lot = lot, Ohne = ohne, Start = System.DateTime.UtcNow, LoehneVorher = loehne });
            Mod.log.Info("PLT-Personal: Lot " + lot.Index + " auf " + (ohne ? "OHNE" : "MIT")
                + " Personal gestellt. Jetzt: " + zustand + ".");
        }

        private void PflegePersonalprobe()
        {
            for (var i = _proben.Count - 1; i >= 0; i--)
            {
                var probe = _proben[i];
                if (!EntityManager.Exists(probe.Lot))
                {
                    Mod.log.Info("PLT-Personal: Probe beendet - Lot " + probe.Lot.Index + " gibt es nicht mehr.");
                    _proben.RemoveAt(i);
                    continue;
                }
                var sekunden = (System.DateTime.UtcNow - probe.Start).TotalSeconds;
                if (probe.Stufe == 0 && sekunden >= 10)
                {
                    probe.Stufe = 1;
                    Mod.log.Info("PLT-Personal: Lot " + probe.Lot.Index + " nach 10 s: "
                        + Personalzustand(probe.Lot, probe.Ohne, out _, out _) + ".");
                    continue;
                }
                if (probe.Stufe != 1 || sekunden < 90) continue;
                var zustand = Personalzustand(probe.Lot, probe.Ohne, out var urteil, out var loehne);
                Mod.log.Info("PLT-Personal: Lot " + probe.Lot.Index + " nach 90 s: " + zustand
                    + "; Loehne vor dem Umschalten " + probe.LoehneVorher + ". "
                    + (probe.Ohne ? "OHNE" : "MIT") + " Personal: " + urteil + ".");
                _proben.RemoveAt(i);
            }
        }

        private string Personalzustand(Entity lot, bool erwartetOhne, out string urteil, out int loehne)
        {
            loehne = 0;
            urteil = "NICHT BESTANDEN (keine Wirtschaftsdaten)";
            if (!EntityManager.HasComponent<ParkingLotEconomyData>(lot)) return "keine Wirtschaftsdaten";
            var economy = EntityManager.GetComponentData<ParkingLotEconomyData>(lot);
            var ohne = ParkingLotPersonal.Ohne(EntityManager, lot);
            var instandhaltung = ParkingLotEconomySystem.WirksamerUnterhalt(economy.Upkeep);
            var sollStellen = ohne ? 0 : CalculateEmployees(economy.Capacity);
            var begleiter = Entity.Null;
            using (var alle = _companions.ToEntityArray(Allocator.Temp))
                foreach (var b in alle)
                    if (EntityManager.GetComponentData<ParkingLotPartRelation>(b).Lot == lot) { begleiter = b; break; }
            if (begleiter == Entity.Null)
            {
                urteil = "NICHT BESTANDEN (kein Begleiter - Wirtschaft aus?)";
                return "kein Begleiter";
            }
            var stellen = EntityManager.HasComponent<WorkProvider>(begleiter)
                ? EntityManager.GetComponentData<WorkProvider>(begleiter).m_MaxWorkers : 0;
            var angestellte = EntityManager.HasBuffer<Employee>(begleiter)
                ? EntityManager.GetBuffer<Employee>(begleiter, true).Length : 0;
            loehne = World.GetOrCreateSystemManaged<ParkingLotEconomySystem>().Loehne(lot);
            var gezahlt = EntityManager.HasComponent<Game.Buildings.ServiceUsage>(begleiter)
                ? (int)System.Math.Round(EntityManager.GetComponentData<Game.Buildings.ServiceUsage>(begleiter).m_Usage
                    * ParkingLotEconomySystem.UpkeepBasis)
                : -1;
            var gruende = new System.Collections.Generic.List<string>();
            if (ohne != erwartetOhne) gruende.Add("Kennzeichen " + (ohne ? "ohne" : "mit") + " statt " + (erwartetOhne ? "ohne" : "mit"));
            if (stellen != sollStellen) gruende.Add("Stellen " + stellen + " statt " + sollStellen);
            if (ohne && angestellte != 0) gruende.Add(angestellte + " Angestellte noch da - laeuft das Spiel?");
            if (ohne && loehne != 0) gruende.Add("Loehne " + loehne + " statt 0");
            if (gezahlt != instandhaltung) gruende.Add("Instandhaltung " + gezahlt + " statt " + instandhaltung);
            urteil = gruende.Count == 0 ? "BESTANDEN" : "NICHT BESTANDEN (" + string.Join("; ", gruende) + ")";
            return "Kapazitaet " + economy.Capacity + ", Stellen " + stellen + " (soll " + sollStellen + "), Angestellte "
                + angestellte + ", Loehne " + loehne + ", Instandhaltung " + gezahlt + " (soll " + instandhaltung
                + "), die Stadt zahlt zusammen " + (System.Math.Max(0, gezahlt) + loehne) + " vor Budget-Prozent";
        }
    }
}
