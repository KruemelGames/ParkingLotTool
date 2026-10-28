using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Colossal.Collections;
using Colossal.Mathematics;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * HAT JEDER PARKPLATZ SEIN WEGENETZ? (28.10.2026)
     *
     * "Parking Lot (192 spaces)" stand im Spielstand ohne einen einzigen
     * Weg da: Bauprotokoll 74 Netzteile, in der Welt nur die 9 Zoningstrassen.
     * Flaechen, Begleiter und Bauzettel waren heil, also fiel es niemandem
     * auf. Wann es passiert ist, liess sich aus den Logs nicht mehr klaeren.
     *
     * Deshalb zaehlt diese Aufnahme nach jedem Laden je Parkplatz die
     * eigenen Wege: Kanten im Umriss, deren Besitzerkette beim Lot endet
     * (auch ueber einen alten Traeger), aus dem Netz-Suchbaum - nicht aus
     * einem Puffer, der Puffer luegt bei alten Bauweisen. Ohne einen
     * einzigen Weg (Gassen und Zoning zaehlen nicht) fehlt das Netz; dann
     * steht der Parkplatz in `OhneWegenetz` und im Log. VERAENDERT NICHTS.
     */
    public sealed partial class ParkingLotWaisenSystem
    {
        /** Parkplaetze, bei denen nach dem Laden kein eigener Weg im Umriss liegt. */
        internal readonly HashSet<Entity> OhneWegenetz = new();

        private readonly List<Entity> _wegebestandLots = new();

        private void PruefeWegebestand(NativeQuadTree<Entity, QuadTreeBoundsXZ> baum)
        {
            if (_wegebestandLots.Count == 0) return;
            var protokoll = ParkingLotToolSystem.LeseBauprotokoll();
            var namen = World.GetOrCreateSystemManaged<Game.UI.NameSystem>();
            var zeilen = new List<string>();
            foreach (var lot in _wegebestandLots)
            {
                if (!EntityManager.Exists(lot) || EntityManager.HasComponent<Deleted>(lot)) continue;
                if (!ParkingLotBaukontextLeser.TryRead(EntityManager, lot, out var k, out _, melden: false)) continue;
                var ring = k.Punkte.Select(q => q.xz).ToArray();
                if (ring.Length < 3) continue;
                var min = ring[0];
                var max = ring[0];
                foreach (var q in ring) { min = math.min(min, q); max = math.max(max, q); }
                using var treffer = new NativeList<Entity>(512, Allocator.Temp);
                var suche = new KantenImKasten { Kasten = new Bounds2(min - 2f, max + 2f), Treffer = treffer };
                baum.Iterate(ref suche);
                int wege = 0, gassen = 0, zoning = 0, sonst = 0;
                var gesehen = new HashSet<Entity>();
                for (var i = 0; i < treffer.Length; i++)
                {
                    var e = treffer[i];
                    if (!gesehen.Add(e) || !EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e)
                        || EntityManager.HasComponent<Temp>(e) || !EntityManager.HasComponent<Game.Net.Edge>(e)
                        || !EntityManager.HasComponent<PrefabRef>(e)) continue;
                    if (!GehoertZu(e, lot)) continue;
                    var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                    var name = _prefabSystem.TryGetPrefab<PrefabBase>(prefab, out var p) && p != null ? p.name : "";
                    if (name.StartsWith("PLT Zoningstrasse", System.StringComparison.Ordinal)) zoning++;
                    else if (GassenPrefab.Ist(_prefabSystem, prefab) || name == "Alley") gassen++;
                    else if (name.Contains("Path")) wege++;
                    else sonst++;
                }
                var gebaut = "kein Eintrag";
                if (protokoll.TryGetValue(ParkingLotToolSystem.LotId(ring), out var eintrag))
                {
                    var m = Regex.Match(eintrag.Result ?? "", @"(\d+) net pieces");
                    gebaut = (m.Success ? m.Groups[1].Value + " Netzteile" : "?") + ", juengster Bau " + eintrag.When;
                }
                var name0 = namen.TryGetCustomName(lot, out var n0) && !string.IsNullOrEmpty(n0) ? n0 : "ohne Namen";
                // Gassen allein sind kein Netz: steht nach einem halben
                // Reparaturversuch nur die vorgebaute Gasse, fehlt es weiter.
                var fehlt = wege == 0;
                if (fehlt) OhneWegenetz.Add(lot);
                zeilen.Add($"Lot {lot.Index} '{name0}': {wege} Wege, {gassen} Gassen, {zoning} Zoningstrassen, "
                    + $"{sonst} sonstige eigene Kanten; Bauprotokoll {gebaut}" + (fehlt ? " -> WEGENETZ FEHLT" : ""));
            }
            Mod.log.Info($"PLT-Wegebestand nach {SuchbaumWartezeit} Bildern: {OhneWegenetz.Count} von {zeilen.Count} "
                + "Parkplatz/Parkplaetzen ohne eigenes Wegenetz. " + string.Join(" | ", zeilen) + ".");
            _wegebestandLots.Clear();
        }

        /** Fuehrt die Owner-Kette ueber das Lot - Weg, alter Traeger, Lot? */
        private bool GehoertZu(Entity e, Entity lot)
        {
            var aktuell = e;
            for (var i = 0; i < 6 && EntityManager.HasComponent<Owner>(aktuell); i++)
            {
                aktuell = EntityManager.GetComponentData<Owner>(aktuell).m_Owner;
                if (aktuell == lot) return true;
                if (aktuell == Entity.Null || !EntityManager.Exists(aktuell)) return false;
            }
            return false;
        }
    }
}
