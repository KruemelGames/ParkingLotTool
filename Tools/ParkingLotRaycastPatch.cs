// Diese Datei ist PLT-Projektcode unter GPL-3.0 und verwendet Harmony 2.2.2
// als Bibliotheksabhaengigkeit. Es wurde kein Harmony-Quellcode uebernommen.
//
// Herkunft der Abhaengigkeit:
//   Lib.Harmony 2.2.2 / 0Harmony.dll
//   https://github.com/pardeike/Harmony
//   Copyright (c) 2017 Andreas Pardeike
//
// MIT-Lizenztext der Harmony-Abhaengigkeit:
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using Game.Areas;
using Game.Common;
using Game.Tools;
using HarmonyLib;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    /**
     * Hebt Treffer auf einem PLT-Teil frueh auf dessen fachliche Lot-Flaeche.
     *
     * `ToolBaseSystem` liest den hier zurueckgegebenen Besitzer unmittelbar
     * fuer Hover, Auswahl und Bulldozer. Das fruehere PostTool-System kam erst
     * danach und konnte deshalb nur noch das Infofenster umbiegen.
     *
     * Die Schranke `SubElements` ist fachlich zwingend: In diesem Modus gibt
     * Vanilla Zwischenbesitzer absichtlich zurueck. Eine Umleitung wuerde die
     * Netz- und Editorwerkzeuge ihrer Unterelemente berauben. Ohne eigene
     * `ParkingLotPartRelation` bleibt das Ergebnis bytegenau unangetastet.
     */
    [HarmonyPatch(typeof(ToolRaycastSystem),
        nameof(ToolRaycastSystem.GetRaycastResult))]
    internal static class ParkingLotRaycastPatch
    {
        private const string HarmonyId = "de.kruemelmonster.parkinglottool.raycast";
        private static Harmony _harmony;

        internal static void Install()
        {
            if (_harmony != null) return;

            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(typeof(ParkingLotRaycastPatch).Assembly);
            Mod.log.Info("  Raycast-Hook aktiv: PLT-Teilrelation -> Lot-Flaeche; "
                + "SubElements bleibt unangetastet.");
            Mod.log.Info("  Belegungs-Hook aktiv: PLT-Lot -> technischer Traeger; "
                + "Vanilla zaehlt weiter die echten Parkspuren.");
        }

        internal static void Uninstall()
        {
            if (_harmony == null) return;

            _harmony.UnpatchAll(HarmonyId);
            _harmony = null;
        }

        [HarmonyPostfix]
        private static void RedirectParkingLotPart(
            ToolRaycastSystem __instance,
            bool __result,
            ref RaycastResult result)
        {
            if (!__result || result.m_Owner == Entity.Null) return;
            if ((__instance.raycastFlags & RaycastFlags.SubElements) != 0) return;

            const TypeMask normaleAuswahl = TypeMask.StaticObjects | TypeMask.Areas;
            if ((__instance.typeMask & normaleAuswahl) == 0) return;

            var world = __instance.World;
            if (world == null || !world.IsCreated) return;
            var entityManager = world.EntityManager;
            var teil = result.m_Owner;
            if (!entityManager.Exists(teil)
                || !entityManager.HasComponent<ParkingLotPartRelation>(teil))
                return;

            var relation = entityManager
                .GetComponentData<ParkingLotPartRelation>(teil);
            if (relation.Lot == Entity.Null
                || !entityManager.Exists(relation.Lot)
                || !entityManager.HasComponent<Area>(relation.Lot))
                return;

            // Nur das Abfrageergebnis aendert sich. Die Relation, der Treffer
            // und saemtliche Weltkomponenten bleiben unveraendert.
            result.m_Owner = relation.Lot;
        }
    }
}
