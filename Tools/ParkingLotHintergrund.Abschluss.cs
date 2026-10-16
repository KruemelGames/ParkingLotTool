using System;
using Game.Common;
using ParkingLotTool.Geometry;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    internal sealed partial class ParkingLotHintergrundSystem
    {
        private void Fehler(Auftrag a, Exception e)
        {
            if (!Aktuell(a)) return;
            a.Erfolg = false;
            bool uebernommen = a.Phase == Phase.Nebenarbeit
                || a.Phase == Phase.Nachpruefung && ParkingLotNetzRueckweg.Lebt(EntityManager,a.Neu)
                    && EntityManager.HasComponent<ParkingLotBuildReceipt>(a.Neu)
                    && (!EntityManager.Exists(a.Lot) || !EntityManager.HasComponent<Game.Areas.Area>(a.Lot)
                        || EntityManager.HasComponent<Deleted>(a.Lot));
            Diagnose(() => ParkingLotNetzRueckweg.Melde($"Fehler Auftrag {a.Id}, Phase {a.Phase}: {e.Message}; "
                + (uebernommen ? "dauerhafter Neubau bleibt auf Stand 7 erhalten." : "Rueckweg erforderlich.")));
            a.GateOffen = false;
            a.Bauer?.HintergrundPortionenBeenden();
            ParkingLotNetzRueckweg.VerwerfeOffeneSicherung(EntityManager,a.Lot);
            if (uebernommen)
            {
                // Auch bei einer Ausnahme NACH der irreversiblen Uebernahme
                // niemals den geprueften Neubau samt Zoningbesitzern loeschen.
                if (!ParkingLotHintergrundAbschluss.MerkeNebenarbeit(EntityManager, a.Neu, a.Traeger))
                    Diagnose(() => ParkingLotNetzRueckweg.Melde($"Neubau {a.Neu} oder Traeger {a.Traeger} nicht mehr gueltig; keine Abschlusskomponenten geschrieben."));
                if (EntityManager.Exists(a.Lot))
                {
                    EntityManager.RemoveComponent<ParkingLotErhaltenerKurs>(a.Lot);
                    EntityManager.RemoveComponent<ParkingLotRueckwegkurs>(a.Lot);
                    EntityManager.RemoveComponent<ParkingLotRueckweganschluss>(a.Lot);
                    EntityManager.RemoveComponent<ParkingLotOffenerErsatz>(a.Lot);
                    EntityManager.RemoveComponent<ParkingLotStufeAKnoten>(a.Lot);
                    ParkingLotStufeA.VergissPrefab(EntityManager, a.Lot);
                }
                Ende(a); return;
            }
            if (a.Phase == Phase.Ruecknahme || a.Phase == Phase.Ruhe
                || a.Phase == Phase.Rueckweg || a.Phase == Phase.Rueckabschluss)
            {
                HalteRueckwegAn(a); return;
            }
            a.Phase = Phase.Ruecknahme;
        }

        /**
         * Issue #17: Ein Rueckweg, dessen Prefabs es nicht mehr gibt, kann die
         * alten Kanten nie bauen. Statt den Parkplatz fuer immer zu sperren,
         * wird der Schnappschuss verworfen und SOFORT ein voller Neubau aus
         * dem Bauplan eingereiht - ein normaler Sync baut fehlende eigene
         * Kanten nicht nach (Schritt 9 tauscht nur vorhandene). Der Neubau
         * sichert vor seinem Abriss einen neuen, vollstaendigen Rueckweg.
         * Nur vor der ersten Ausgabe und nur mit lesbarem Bauplan - ohne Plan
         * bleibt es bei der Sperre wie bisher.
         */
        private bool VerwirfUnbaubarenRueckweg(Auftrag a)
        {
            int fehlen = ParkingLotNetzRueckweg.FehlendePrefabs(EntityManager, a.Lot);
            if (fehlen == 0) return false;
            if (EntityManager.HasComponent<ParkingLotOffenerErsatz>(a.Lot))
            {
                ParkingLotNetzRueckweg.Melde($"Rueckweg Lot {a.Lot.Index}: {fehlen} Prefab(s) fehlen, offener Ersatz vorhanden; nicht verworfen.");
                return false;
            }
            if (!KannNeubauen(a.Lot, out var grund))
            {
                ParkingLotNetzRueckweg.Melde($"Rueckweg Lot {a.Lot.Index}: {fehlen} Prefab(s) fehlen, kein Bauplan ({grund}); Sperre bleibt.");
                return false;
            }
            // Erst die gespeicherte Pflicht, dann den alten Schnappschuss weg:
            // ein Speichern bis zur neuen Sicherung darf den Neubau nicht verlieren.
            if (!EntityManager.HasComponent<ParkingLotNeubauFaellig>(a.Lot))
                EntityManager.AddComponent<ParkingLotNeubauFaellig>(a.Lot);
            EntityManager.RemoveComponent<ParkingLotRueckwegkurs>(a.Lot);
            ParkingLotNetzRueckweg.Melde($"Rueckweg Lot {a.Lot.Index} VERWORFEN: {fehlen} gesicherte Prefab(s) fehlen nach dem Laden; "
                + "Neubau aus dem Bauplan wird eingereiht.");
            a.Fortsetzen = true;
            RueckwegFertig(a, 0, 0);
            if (!a.Beendet) return true;
            SetzeFort(a);
            return true;
        }

        private void HalteRueckwegAn(Auftrag a)
        {
            // Keine Freigabe bei 20/22 und keine neue Definitionsserie. Der
            // serialisierte Schnappschuss bleibt auch nach World-Wechsel erhalten.
            _angehalten.Add(a.Lot);
            Diagnose(() => World.GetOrCreateSystemManaged<ParkingLotUISystem>().SetStatus(RueckwegHinweis));
            Diagnose(() => ParkingLotNetzRueckweg.Melde($"Rueckweg ANGEHALTEN Lot {a.Lot.Index}, nach {a.Rueckwegpruefungen} Messfenstern; "
                + "Schnappschuss und Sperre erhalten. " + RueckwegHinweis));
            Ende(a);
        }

        private void RueckwegFertig(Auftrag a, int ist, int soll)
        {
            bool gesichert = EntityManager.HasBuffer<ParkingLotRueckwegkurs>(a.Lot);
            if (gesichert)
            {
                a.Bauer ??= ParkingLotToolSystem.Bauarbeiter(World);
                if (!a.Bauer.HintergrundRueckwegFlussPortion(a.Lot))
                { a.RueckIst = ist; a.RueckSoll = soll; a.Phase = Phase.Rueckabschluss; return; }
            }
            Diagnose(() => ParkingLotNetzRueckweg.Melde(gesichert
                ? $"Rueckweg geprueft: Kanten {ist}/{soll}, Lage-/Anschlussabweichungen 0 (Enden 3D, innen gemaess Vanilla-Regel); Versuch {a.Versuch}/3."
                : $"Auftrag vor eigenem Abriss beendet; kein Rueckweg erforderlich; Versuch {a.Versuch}/3."));
            if (EntityManager.HasBuffer<ParkingLotErhaltenerKurs>(a.Lot)) EntityManager.RemoveComponent<ParkingLotErhaltenerKurs>(a.Lot);
            if (EntityManager.HasBuffer<ParkingLotRueckwegkurs>(a.Lot)) EntityManager.RemoveComponent<ParkingLotRueckwegkurs>(a.Lot);
            if (EntityManager.HasComponent<ParkingLotOffenerErsatz>(a.Lot)) EntityManager.RemoveComponent<ParkingLotOffenerErsatz>(a.Lot);
            if (EntityManager.HasBuffer<ParkingLotRueckweganschluss>(a.Lot)) EntityManager.RemoveComponent<ParkingLotRueckweganschluss>(a.Lot);
            if (EntityManager.HasBuffer<ParkingLotStufeAKnoten>(a.Lot)) EntityManager.RemoveComponent<ParkingLotStufeAKnoten>(a.Lot);
            ParkingLotStufeA.VergissPrefab(EntityManager, a.Lot);
            bool wieder = !a.NurRueckweg && gesichert && _regel.Wiederholen(a.Lot,true);
            a.Fortsetzen |= wieder;
            a.Erfolg = a.NurRueckweg;
            if (!a.NurRueckweg && !wieder)
                Diagnose(() => World.GetOrCreateSystemManaged<ParkingLotUISystem>().SetStatus(gesichert ? ParkingLotTexte.T("hintergrundSystem.backgroundRebuildStoppedAfterThreeAttempts") : ParkingLotTexte.T("hintergrundSystem.backgroundPlanningStoppedNoOldPaths")));
            Ende(a);
            if (a.NurRueckweg) World.GetOrCreateSystemManaged<ParkingLotSyncSystem>().MeldeRueckwegEnde();
            if (wieder) SetzeFort(a);
        }
        private void Freigeben(Auftrag a)
        {
            a.Ersatzabriss?.Dispose(); a.Ersatzabriss = null;
            a.Rueckpruefung?.Dispose(); a.Rueckpruefung = null;
            a.Bauer?.HintergrundPortionenBeenden();
            if (a.Bauer != null)
            {
                if (a.Phase == Phase.Nebenarbeit && a.Bauer.HintergrundBeobachtetFluss) _flussbeobachter.Add((a.Bauer, a.Kennung.Generation));
                else World.DestroySystemManaged(a.Bauer);
            }
        }

    }
}
