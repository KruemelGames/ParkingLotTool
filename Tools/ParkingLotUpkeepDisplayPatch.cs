// Diese Datei ist PLT-Projektcode unter GPL-3.0 und verwendet Harmony 2.2.2
// als Bibliotheksabhaengigkeit. Es wurde kein Harmony-Quellcode uebernommen.
// Herkunft und Lizenztext der Abhaengigkeit stehen im Kopf von
// Tools/ParkingLotRaycastPatch.cs.

using Game.Economy;
using Game.Prefabs;
using Game.UI.InGame;
using HarmonyLib;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    /**
     * Zeigt im Auswahlfenster den ECHTEN Unterhalt statt des Basisbetrags.
     *
     * GEMESSEN AM 2026-08-26, Nutzerbau mit 1216 Buchten:
     *
     *     Stadtkasse       58.806 pro Monat   = 48 * 1216 + 438, exakt richtig
     *     Auswahlfenster   65.536             = unser Basisbetrag, unskaliert
     *
     * Die Ursache ist eine Luecke in CS2 selbst. `UpkeepSection`
     * (`CalculateServiceUpkeepDatas`) hat zwei Zweige, und `m_ScaleWithUsage`
     * wird NUR im else-Zweig ausgewertet:
     *
     *     if (... && resource == Resource.Money)   // roher Prefab-Betrag
     *     else                                     // hier steht die Skalierung
     *
     * Das Abrechnungssystem kann es (`CityServiceUpkeepSystem`
     * .GetUpkeepWithUsageScale), die Anzeige nicht. Vanilla benutzt
     * "Geld UND mit Nutzung skaliert" offenbar nirgends, deshalb ist der Fall
     * dort nie gebaut worden - seine skalierten Eintraege sind Strom, Wasser
     * und Material.
     *
     * WARUM SO UND NICHT ANDERS: der Betrag steht am PREFAB, die Anzeige liest
     * ihn auch von dort. Wir koennen ihn also nicht je Parkplatz hinterlegen.
     * Deshalb wird er fuer die Dauer genau dieses einen Aufrufs auf den Wert
     * des angeklickten Parkplatzes gesetzt und danach zurueckgeschrieben. Der
     * Aufruf laeuft synchron im UI-Update; dazwischen liest ihn niemand.
     *
     * REINE OPTIK. Was die Stadt zahlt, entsteht im Abrechnungssystem aus
     * Basisbetrag mal `ServiceUsage` und wird hier nicht angefasst - das war
     * vor diesem Patch richtig und bleibt es.
     *
     * Sollte Colossal Order die Luecke einmal schliessen, wuerde hier doppelt
     * skaliert. Erkennungsmerkmal waere ein absurd kleiner Betrag im Fenster
     * bei weiterhin korrekter Stadtkasse.
     */
    /**
     * Blendet die Unterhaltszeile ganz aus, solange die Wirtschaft aus ist.
     *
     * Ein Betrag von 0 waere immer noch eine Zeile ueber Kosten, die es nicht
     * gibt. `UpkeepSection.OnUpdate` besteht aus genau einer Zuweisung
     * (`base.visible = Visible()`), deshalb genuegt ein Postfix dahinter.
     *
     * Nur fuer UNSERE Parkplaetze: `ParkingLotCarrierReference` traegt jede von
     * PLT gebaute Flaeche und sonst nichts im Spiel. Ein Vanilla-Gebaeude
     * behaelt seine Zeile unveraendert, auch wenn unser Schalter aus ist.
     */
    [HarmonyPatch(typeof(UpkeepSection), "OnUpdate")]
    internal static class ParkingLotUpkeepVisibilityPatch
    {
        [HarmonyPrepare]
        private static bool Prepare()
        {
            if (AccessTools.Method(typeof(UpkeepSection), "OnUpdate") != null)
                return true;
            Mod.log.Warn("PLT: UpkeepSection.OnUpdate gibt es nicht mehr. Die "
                + "Unterhaltszeile bleibt bei ausgeschalteter Wirtschaft "
                + "sichtbar; gerechnet wird trotzdem nichts.");
            return false;
        }

        [HarmonyPostfix]
        private static void Postfix(UpkeepSection __instance)
        {
            if (Mod.WirtschaftAn) return;
            var sicht = Traverse.Create(__instance).Property<bool>("visible");
            if (!sicht.Value) return;

            var welt = World.DefaultGameObjectInjectionWorld;
            if (welt == null) return;
            var gewaehlt = Traverse.Create(__instance)
                .Property<Entity>("selectedEntity").Value;
            if (gewaehlt == Entity.Null
                || !welt.EntityManager.Exists(gewaehlt)
                || !welt.EntityManager
                    .HasComponent<ParkingLotCarrierReference>(gewaehlt))
                return;
            sicht.Value = false;
        }
    }

    [HarmonyPatch(typeof(UpkeepSection), "CalculateServiceUpkeepDatas")]
    internal static class ParkingLotUpkeepDisplayPatch
    {
        // Zwischen Prefix und Finalizer gehalten. Der Aufruf ist synchron und
        // einfach geschachtelt, deshalb genuegt ein einzelner Satz Werte.
        private static bool _ersetzt;
        private static Entity _prefab;
        private static int[] _original;

        /**
         * Ohne diese Schranke reisst ein CS2-Update den Klick-Hook mit.
         *
         * `PatchAll` patcht die ganze Assembly in EINEM Aufruf und wirft, sobald
         * ein einziges Ziel fehlt. Benennt Colossal Order diese private Methode
         * um, faellt also nicht nur die Anzeigekorrektur aus, sondern auch
         * `ParkingLotRaycastPatch` - und damit das Anklicken des Parkplatzes.
         *
         * Harmony ruft `Prepare` vor dem Patchen auf und ueberspringt die Klasse
         * bei `false`. Aus einem kaputten Mod wird so eine fehlende Kleinigkeit.
         */
        [HarmonyPrepare]
        private static bool Prepare()
        {
            var ziel = AccessTools.Method(typeof(UpkeepSection),
                "CalculateServiceUpkeepDatas");
            if (ziel != null) return true;
            Mod.log.Warn("PLT: UpkeepSection.CalculateServiceUpkeepDatas gibt es "
                + "nicht mehr. Der Unterhalt im Auswahlfenster zeigt den "
                + "Basisbetrag; die Stadtkasse rechnet weiter richtig.");
            return false;
        }

        [HarmonyPrefix]
        private static void Prefix(Entity prefabEntity,
                                   Entity buildingOwnerEntity)
        {
            _ersetzt = false;

            var welt = World.DefaultGameObjectInjectionWorld;
            if (welt == null) return;
            var em = welt.EntityManager;

            if (!em.Exists(buildingOwnerEntity) || !em.Exists(prefabEntity))
                return;
            if (!em.HasComponent<ParkingLotEconomyData>(buildingOwnerEntity))
                return;
            if (!em.HasBuffer<ServiceUpkeepData>(prefabEntity)) return;

            /*
             * OHNE WIRTSCHAFT KOSTET ER NICHTS - UND ZWAR AUCH IN DER ANZEIGE.
             *
             * Die gespeicherten Werte bleiben beim Ausschalten absichtlich am
             * Parkplatz stehen, damit die Gebuehr zurueckkommt. Genau deshalb
             * hat dieser Patch am 2026-08-26 weiter den vollen Unterhalt ins
             * Fenster geschrieben, obwohl die Stadt nichts mehr zahlte. Der
             * Nutzer sah einen Betrag, den es nicht gab.
             */
            var soll = Mod.WirtschaftAn
                ? em.GetComponentData<ParkingLotEconomyData>(
                    buildingOwnerEntity).Upkeep
                : 0;
            if (soll < 0) return;

            var puffer = em.GetBuffer<ServiceUpkeepData>(prefabEntity);
            var sicherung = new int[puffer.Length];
            var getroffen = false;
            for (var i = 0; i < puffer.Length; i++)
            {
                var eintrag = puffer[i];
                sicherung[i] = eintrag.m_Upkeep.m_Amount;
                if (!eintrag.m_ScaleWithUsage
                    || eintrag.m_Upkeep.m_Resource != Resource.Money)
                    continue;
                eintrag.m_Upkeep.m_Amount = soll;
                puffer[i] = eintrag;
                getroffen = true;
            }
            if (!getroffen) return;

            _original = sicherung;
            _prefab = prefabEntity;
            _ersetzt = true;
        }

        /**
         * Finalizer statt Postfix: er laeuft auch dann, wenn die
         * Originalmethode eine Ausnahme wirft. Sonst bliebe der Basisbetrag am
         * Prefab veraendert stehen - und der Betrag am Prefab ist der, aus dem
         * die Stadtkasse rechnet.
         */
        [HarmonyFinalizer]
        private static void Finalizer()
        {
            if (!_ersetzt) return;
            _ersetzt = false;

            var welt = World.DefaultGameObjectInjectionWorld;
            if (welt == null) return;
            var em = welt.EntityManager;
            if (!em.Exists(_prefab) || !em.HasBuffer<ServiceUpkeepData>(_prefab))
                return;

            var puffer = em.GetBuffer<ServiceUpkeepData>(_prefab);
            for (var i = 0; i < puffer.Length && i < _original.Length; i++)
            {
                var eintrag = puffer[i];
                eintrag.m_Upkeep.m_Amount = _original[i];
                puffer[i] = eintrag;
            }
        }
    }
}
