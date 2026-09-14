using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Entities;
using Game.Prefabs;
using Unity.Entities;
using static ParkingLotTool.Tools.ParkingLotTexte;

namespace ParkingLotTool.Tools
{
    /**
     * VERGLEICHT UNSEREN STRASSENKLON MIT SEINEM VORBILD.
     *
     * Ansage des Nutzers am 2026-09-05:
     *
     *   *"Es wird an unserer Strasse liegen bzw. dem kopierten Prefab, dass
     *   das fehlt oder falsch ist. Ansonsten wuerde CS2 das nicht direkt
     *   sagen, DENN in CS2 kann man jede Strasse mit jeder anderen verbinden
     *   ohne dass InvalidShape kommt."*
     *
     * Das Argument stimmt: Vanilla-Strassen lassen sich beliebig verbinden.
     * Kann unsere es nicht, fehlt ihr etwas. Nur WAS, war bisher Vermutung -
     * und Vermutungen haben heute genug Zeit gekostet.
     *
     * Deshalb hier kein Urteil, sondern eine Gegenueberstellung: derselbe
     * Satz Angaben fuer den Klon, fuer sein Vorbild und fuer die unsichtbaren
     * Wege, die der Mod sonst noch verlegt. Was sich unterscheidet, faellt
     * beim Lesen von selbst auf.
     *
     * Warum diese Felder: `Game.Net.ValidationHelpers.IgnoreCollision` duldet
     * zwei Netze an derselben Stelle NUR, wenn sie im Knoten als echte
     * Mittelverbindung stehen. Ob zwei Netze ueberhaupt einen Knoten bilden
     * duerfen, entscheiden ihre Ebenen und Verbindungsangaben - genau die
     * stehen unten.
     */
    public sealed partial class ParkingLotToolSystem
    {
        internal void VergleicheStrassenprefabs()
        {
            /*
             * Namen kommen ueber `PrefabSystem.TryGetPrefab`, nicht ueber die
             * Instanzaufloesung - ein PREFAB ist keine Instanz. Und gefragt
             * wird auf `NetData` statt auf `RoadData`: die unsichtbaren Wege
             * sind Pathways und haetten sonst gefehlt, obwohl sie die
             * interessanteste Gegenprobe sind.
             */
            var gesucht = new[]
            {
                "Alley",
                "Invisible Road Path - 2xTwoway",
                "Invisible Car Path - 1xTwoway",
            };
            var treffer = new List<(string Name, Entity E)>();
            var query = GetEntityQuery(
                ComponentType.ReadOnly<NetData>(),
                ComponentType.Exclude<PlaceholderObjectElement>());
            using (var alle = query.ToEntityArray(
                Unity.Collections.Allocator.Temp))
                for (var i = 0; i < alle.Length; i++)
                {
                    if (!_prefabSystem.TryGetPrefab<PrefabBase>(
                            alle[i], out var prefab) || prefab == null)
                        continue;
                    var name = prefab.name;
                    if (string.IsNullOrEmpty(name)) continue;
                    if (name.StartsWith("PLT Zoningstrasse",
                            StringComparison.OrdinalIgnoreCase)
                        || Array.IndexOf(gesucht, name) >= 0)
                        treffer.Add((name, alle[i]));
                }

            if (treffer.Count == 0)
            {
                Mod.log.Warn("PLT-Prefabvergleich: kein passendes Prefab "
                    + "gefunden - weder unser Klon noch 'Alley'.");
                _uiSystem?.SetUeberlappungsstand(T(
                    "Prefabvergleich: nichts gefunden.",
                    "Prefab comparison: nothing found."));
                return;
            }

            Mod.log.Info($"PLT-Prefabvergleich: {treffer.Count} Prefab(s).");
            foreach (var (name, e) in treffer.OrderBy(t => t.Name))
            {
                var zeile = $"PLT-Prefabvergleich '{name}':";

                if (EntityManager.TryGetComponent<NetData>(e, out var netz))
                    zeile += $" ConnectLayers [{netz.m_ConnectLayers}]"
                        + $" RequiredLayers [{netz.m_RequiredLayers}]"
                        + $" LocalConnect [{netz.m_LocalConnectLayers}]"
                        + $" NodePriority {netz.m_NodePriority:F2}"
                        + $" GeneralMask [{netz.m_GeneralFlagMask}]"
                        + $" SideMask [{netz.m_SideFlagMask}];";
                else zeile += " KEIN NetData;";

                if (EntityManager.TryGetComponent<NetGeometryData>(
                        e, out var geo))
                    zeile += $" Breite {geo.m_DefaultWidth:F2} m,"
                        + $" Flags [{geo.m_Flags}],"
                        + $" MergeLayers [{geo.m_MergeLayers}],"
                        + $" IntersectLayers [{geo.m_IntersectLayers}],"
                        + $" MinNodeOffset {geo.m_MinNodeOffset:F2},"
                        + $" Kantenlaenge {geo.m_EdgeLengthRange.min:F1}"
                        + $"..{geo.m_EdgeLengthRange.max:F1} m;";
                else zeile += " KEIN NetGeometryData;";

                zeile += EntityManager.HasComponent<RoadData>(e)
                    ? " RoadData JA;" : " RoadData NEIN;";
                zeile += EntityManager.HasComponent<PathwayData>(e)
                    ? " PathwayData JA;" : " PathwayData NEIN;";
                zeile += EntityManager.HasComponent<PlaceableNetData>(e)
                    ? " PlaceableNetData JA;" : " PlaceableNetData NEIN;";

                Mod.log.Info(zeile);
                MeldeKompositionen(name, e);
            }

            _uiSystem?.SetUeberlappungsstand(T(
                treffer.Count + " Straßenprefab(s) ins Log geschrieben.",
                treffer.Count + " road prefab(s) written to the log."));

            Mod.log.Info("PLT-Prefabvergleich: fertig. Unterschiede zwischen "
                + "'PLT Zoningstrasse (Alley)' und 'Alley' sind die Kandidaten "
                + "fuer fehlende Kreuzungsfaehigkeit; die beiden 'Invisible'-"
                + "Eintraege zeigen, was ein reiner Weg mitbringt.");
        }

        /**
         * DIE TATSAECHLICH VERWENDETEN KOMPOSITIONEN - nicht nur die
         * Prefabdaten.
         *
         * Astra hat am 2026-09-05 aus dem Dekompilat belegt, wo mein erster
         * Vergleich zu kurz greift:
         *
         *   *"Der bisherige Vergleich erfasst die tatsaechlich verwendeten
         *   Knoten-/Kantenkompositionen nicht."*
         *
         * `ValidationHelpers.ValidateEdge` und `NetIterator.CheckOverlap`
         * lesen DREI Kompositionen je Kante - Kante, Startknoten, Endknoten -
         * und `CompositionSelectSystem` setzt `Intersection` erst, wenn
         * mindestens zwei weitere passende Kanten gezaehlt werden.
         *
         * Genau an diesen Kompositionen operiert der Mod herum, um die
         * Strasse unsichtbar zu machen: im Bauzettel stehen
         * "Kompositionsobjekte entfernt=19" und "Teile=37, davon Hidden=25".
         * Ob dabei etwas verlorengeht, das eine Kreuzung braucht, ist hier
         * abzulesen und nicht am Prefab.
         *
         * `CompositionFlags` traegt die Unterscheidung: `General.Intersection`,
         * `General.Node`, `General.Edge`, `General.DeadEnd`. Fehlt dem Klon
         * eine Knoten- oder Kreuzungskomposition, die das Vorbild hat, ist das
         * der Befund.
         */
        private void MeldeKompositionen(string name, Entity prefab)
        {
            if (!EntityManager.HasBuffer<NetGeometryComposition>(prefab))
            {
                Mod.log.Info($"PLT-Prefabvergleich '{name}': KEIN "
                    + "NetGeometryComposition-Puffer.");
                return;
            }
            var puffer = EntityManager.GetBuffer<NetGeometryComposition>(
                prefab, true);
            if (puffer.Length == 0)
            {
                Mod.log.Warn($"PLT-Prefabvergleich '{name}': "
                    + "Kompositionspuffer ist LEER.");
                return;
            }

            var knoten = 0;
            var kreuzungen = 0;
            var sackgassen = 0;
            var kanten = 0;
            var zeilen = new List<string>();
            for (var i = 0; i < puffer.Length; i++)
            {
                var k = puffer[i].m_Composition;
                if (k == Entity.Null || !EntityManager.Exists(k)) continue;
                if (!EntityManager.TryGetComponent<NetCompositionData>(
                        k, out var daten)) continue;
                var allgemein = daten.m_Flags.m_General;
                if ((allgemein & CompositionFlags.General.Intersection) != 0)
                    kreuzungen++;
                if ((allgemein & CompositionFlags.General.Node) != 0) knoten++;
                if ((allgemein & CompositionFlags.General.DeadEnd) != 0)
                    sackgassen++;
                if ((allgemein & CompositionFlags.General.Edge) != 0) kanten++;
                if (zeilen.Count < 12)
                    zeilen.Add($"[{allgemein}] Breite {daten.m_Width:F2}"
                        + $" Zustand {daten.m_State}"
                        + $" Maske [{puffer[i].m_Mask.m_General}]");
            }

            Mod.log.Info($"PLT-Prefabvergleich '{name}': "
                + $"{puffer.Length} Komposition(en) - Kante {kanten}, "
                + $"Knoten {knoten}, Kreuzung {kreuzungen}, "
                + $"Sackgasse {sackgassen}."
                + (kreuzungen == 0
                    ? "  KEINE KREUZUNGSKOMPOSITION - das waere der Befund."
                    : string.Empty));
            foreach (var z in zeilen)
                Mod.log.Info($"PLT-Prefabvergleich '{name}':   {z}");
        }
    }
}
