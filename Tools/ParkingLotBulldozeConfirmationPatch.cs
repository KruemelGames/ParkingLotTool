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

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Game.Areas;
using Game.Tools;
using HarmonyLib;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace ParkingLotTool.Tools
{
    // Vanilla prueft nur Gebaeude. PLT-Wurzeln sind Areas; deshalb wird erst
    // unmittelbar vor Apply die unveraenderte Vanilla-Bestaetigung angefordert.
    // PatchAll/UnpatchAll des vorhandenen Raycast-Hooks verwaltet diese Hooks mit.
    [HarmonyPatch]
    internal static class ParkingLotBulldozeConfirmationPatch
    {
        private sealed class Freigabe { internal bool Bestaetigt; }
        private static readonly ConditionalWeakTable<BulldozeToolSystem, Freigabe> Freigaben
            = new ConditionalWeakTable<BulldozeToolSystem, Freigabe>();
        private static readonly FieldInfo Zustand = AccessTools.Field(typeof(BulldozeToolSystem), "m_State");

        /**
         * Diese Hooks haengen an drei privaten Namen auf einmal.
         *
         * `m_State` wird gelesen UND gesetzt, `applyMode` ueber den privaten
         * Setter von `ToolBaseSystem`. Faellt einer davon weg, liefe der
         * Prefix in eine `NullReferenceException` bei jedem Bulldozer-Update -
         * und `PatchAll` risse ohne diese Schranke den Raycast-Hook mit, also
         * das Anklicken des Parkplatzes.
         *
         * Harmony ruft `Prepare` vor dem Patchen und ueberspringt die Klasse
         * bei `false`. Dann faellt nur die Rueckfrage aus: der Bulldozer
         * loescht den Parkplatz wie jedes andere Objekt, ohne zu fragen.
         */
        [HarmonyPrepare]
        private static bool Prepare()
        {
            var fehlt = new System.Collections.Generic.List<string>();
            if (Zustand == null) fehlt.Add("BulldozeToolSystem.m_State");
            if (AccessTools.Method(typeof(BulldozeToolSystem), "OnUpdate") == null)
                fehlt.Add("BulldozeToolSystem.OnUpdate");
            if (AccessTools.Method(typeof(BulldozeToolSystem), "Apply") == null)
                fehlt.Add("BulldozeToolSystem.Apply");
            if (AccessTools.PropertySetter(typeof(ToolBaseSystem), "applyMode") == null)
                fehlt.Add("ToolBaseSystem.applyMode");
            if (fehlt.Count == 0) return true;

            Mod.log.Warn("PLT: die Bulldozer-Rueckfrage bleibt aus, es fehlt "
                + string.Join(", ", fehlt.ToArray())
                + ". Der Parkplatz laesst sich dann ohne Nachfrage loeschen.");
            return false;
        }

        [HarmonyPatch(typeof(BulldozeToolSystem), "OnUpdate")]
        [HarmonyPrefix]
        private static void VorUpdate(BulldozeToolSystem __instance)
        {
            // Freigabe gilt nur fuer den aktuellen Confirmed-Update, nie fuer
            // einen spaeteren Klick oder einen abgebrochenen Werkzeugwechsel.
            Freigaben.GetOrCreateValue(__instance).Bestaetigt =
                Zustand.GetValue(__instance).ToString() == "Confirmed";
        }

        [HarmonyPatch(typeof(BulldozeToolSystem), "Apply")]
        [HarmonyPrefix]
        private static bool VorAnwenden(BulldozeToolSystem __instance,
            JobHandle inputDeps, ref JobHandle __result)
        {
            var freigabe = Freigaben.GetOrCreateValue(__instance);
            if (freigabe.Bestaetigt) { freigabe.Bestaetigt = false; return true; }
            if (Mod.Optionen?.ParkplatzLoeschenBestaetigen == false) return true;
            var em = __instance.EntityManager;
            inputDeps.Complete();
            bool parkplatz = false;
            using (var query = em.CreateEntityQuery(ComponentType.ReadOnly<Area>(),
                ComponentType.ReadOnly<Temp>()))
            using (var entities = query.ToEntityArray(Allocator.Temp))
            {
                foreach (var entity in entities)
                {
                    var temp = em.GetComponentData<Temp>(entity);
                    if ((temp.m_Flags & TempFlags.Delete) == 0) continue;
                    var original = temp.m_Original;
                    if (original != Entity.Null && em.Exists(original)
                        && em.HasComponent<ParkingLotCarrierReference>(original))
                    { parkplatz = true; break; }
                }
            }
            if (!parkplatz) return true;
            // Ohne UI-Empfaenger niemals stillschweigend loeschen.
            __result = inputDeps;
            AccessTools.PropertySetter(typeof(ToolBaseSystem), "applyMode")
                .Invoke(__instance, new object[] { ApplyMode.None });
            if (__instance.EventConfirmationRequested == null)
            {
                Zustand.SetValue(__instance, Enum.Parse(Zustand.FieldType, "Cancelled"));
                Mod.log.Warn("PLT-Loeschen abgebrochen: CS2-Bestaetigungsdialog nicht verfuegbar.");
                return false;
            }
            Zustand.SetValue(__instance, Enum.Parse(Zustand.FieldType, "Waiting"));
            __instance.EventConfirmationRequested();
            return false;
        }
    }
}
