using static ParkingLotTool.Tools.ParkingLotTexte;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.IO;
using System.Text;
using Game.Common;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace ParkingLotTool.Tools
{
    /**
     * Schreibt auf: woraus ein im Spiel ANGEWAEHLTES Objekt wirklich besteht.
     *
     * Gedacht fuer die Frage "wie ist ein echter Parkplatz gebaut?". Statt
     * das aus dem Dekompilat zusammenzureimen, setzt der Nutzer einen
     * Vanilla-Parkplatz, waehlt ihn an und drueckt Strg+Alt+P - danach steht
     * die vollstaendige Bauteilliste in einer Datei.
     *
     * Ausgelesen wird ueber `SelectedInfoUISystem.selectedEntity`; die
     * Eigenschaft ist oeffentlich, es braucht also keinen eigenen Strahl und
     * keine Annahme darueber, was anwaehlbar ist.
     *
     * Berichtet werden je Ebene: alle Komponenten der Entity, das Prefab mit
     * seinen Komponenten, und die Unterelemente aus SubArea, SubNet und
     * SubObject. Zwei Ebenen tief - das reicht, um Aufbau und Zustaendigkeiten
     * zu erkennen, und begrenzt die Dateigroesse.
     */
    public sealed partial class ParkingLotToolSystem
    {
        // Fuer den Vanilla-Vergleich reichen 2 Ebenen nicht: Gebaeude ->
        // SubObject (Aufkleber) -> dessen SubLane (Parkspur) sind schon drei.
        private const int InspectorMaxDepth = 5;
        private const int InspectorMaxChildrenPerLevel = 200;

        private Game.UI.InGame.SelectedInfoUISystem _selectedInfoSystem;

        /** Merkt sich den vorigen Aufruf fuer die Gegenueberstellung. */
        private HashSet<string> _vergleichKomponenten = new HashSet<string>();
        private string _vergleichName;

        /**
         * Alle Komponentennamen der Entity UND ihrer Unterelemente, mit einem
         * Praefix je Ebene. So faellt auf, wenn Vanilla eine ganze Sorte
         * Unterelement hat, die uns fehlt - nicht nur eine Komponente.
         */
        private HashSet<string> SammleKomponenten(Entity wurzel)
        {
            var ergebnis = new HashSet<string>(StringComparer.Ordinal);
            var gesehen = new HashSet<Entity>();
            void Gehe(Entity e, int tiefe, string pfad)
            {
                if (tiefe > InspectorMaxDepth || e == Entity.Null
                    || !EntityManager.Exists(e) || !gesehen.Add(e)) return;
                foreach (var typ in EntityManager.GetComponentTypes(e))
                    ergebnis.Add(pfad + typ.GetManagedType()?.Name);
                if (EntityManager.HasComponent<PrefabRef>(e))
                {
                    var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (EntityManager.Exists(prefab))
                        foreach (var typ in EntityManager.GetComponentTypes(prefab))
                            ergebnis.Add(pfad + "Prefab." + typ.GetManagedType()?.Name);
                }
                void Kinder<T>(string art, Func<T, Entity> waehle) where T : unmanaged, IBufferElementData
                {
                    if (!EntityManager.HasBuffer<T>(e)) return;
                    var puffer = EntityManager.GetBuffer<T>(e);
                    for (var i = 0; i < puffer.Length && i < InspectorMaxChildrenPerLevel; i++)
                        Gehe(waehle(puffer[i]), tiefe + 1, pfad + art + "/");
                }
                Kinder<Game.Objects.SubObject>("Objekt", x => x.m_SubObject);
                Kinder<Game.Net.SubNet>("Netz", x => x.m_SubNet);
                Kinder<Game.Net.SubLane>("Spur", x => x.m_SubLane);
                Kinder<Game.Areas.SubArea>("Flaeche", x => x.m_Area);
            }
            Gehe(wurzel, 0, string.Empty);
            return ergebnis;
        }

        /**
         * WELCHE PANEL-ABSCHNITTE ZEIGT DAS SPIEL FUER DIESE AUSWAHL?
         *
         * Der Nutzer fragte zu Recht, ob der Vergleich auch die FUNKTIONEN
         * erfasst - Komfort, Klickverhalten, UI - und nicht nur die Bauteile.
         * Tat er nicht. `Game.UI.InGame.InfoSectionBase.visible` ist
         * oeffentlich, also laesst sich genau das auslesen: jeder Abschnitt des
         * Infofensters ist ein eigenes System, und `visible` sagt, ob es fuer
         * die aktuelle Auswahl anspringt.
         *
         * Damit steht im Bericht schwarz auf weiss, welche Funktionen ein
         * Vanilla-Parkplatz anbietet und unserer nicht - etwa Parkgebuehr,
         * Auslastung oder Unterhaltskosten.
         */
        /**
         * UNSERE EIGENEN AUFKLEBER MIT AUFNEHMEN.
         *
         * Ohne das ist der Vergleich mit einem Vanilla-Parkplatz schief: dort
         * sind die Aufkleber `SubObject` des Gebaeudes, der Inspektor findet
         * sie also von selbst. Unsere haben absichtlich KEINEN Besitzer
         * (sonst verstreut `RelocateSubObjects` sie), stehen damit in keinem
         * `SubObject`-Puffer - und fehlten im Bericht genau dort, wo man sie
         * braucht.
         *
         * Beschrieben wird je Sorte EIN Beispiel. Fuer den Komponentenvergleich
         * reicht das; hunderte gleiche Aufkleber wuerden die Datei nur fluten.
         */
        private void BeschreibeEigeneObjekte(StringBuilder text, Entity selected,
                                             HashSet<Entity> seen)
        {
            if (!TryGetCleanupPrefabs(out var lotPrefab, out var objektPrefabs)) return;
            if (!EntityManager.HasComponent<PrefabRef>(selected)) return;
            if (EntityManager.GetComponentData<PrefabRef>(selected).m_Prefab != lotPrefab)
                return;

            text.AppendLine();
            text.AppendLine("=== UNSERE EIGENEN OBJEKTE (haben keinen Besitzer, "
                + "deshalb hier gesondert) ===");
            var query = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Game.Objects.Transform>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Game.Tools.Temp>(),
                },
            });
            using var alle = query.ToEntityArray(Allocator.TempJob);
            var gezeigt = new HashSet<Entity>();
            for (var k = 0; k < objektPrefabs.Length; k++)
            {
                var gesucht = objektPrefabs[k];
                if (gesucht == Entity.Null) continue;
                // OHNE BESITZER, sonst erwischt man Vanilla. Wir benutzen
                // DIESELBEN Prefabs wie das Spiel (`ParkingLotDecal01` und
                // Geschwister) - eine Suche nur nach dem Prefab fand am
                // 2026-08-17 prompt einen Aufkleber des Vanilla-Parkplatzes
                // und meldete ihn als unseren. Unsere sind daran zu erkennen,
                // dass sie KEINEN Besitzer haben; genau deshalb gibt es sie
                // hier ja gesondert.
                for (var i = 0; i < alle.Length; i++)
                {
                    if (EntityManager.GetComponentData<PrefabRef>(alle[i]).m_Prefab != gesucht)
                        continue;
                    if (EntityManager.HasComponent<Owner>(alle[i])) continue;
                    if (!gezeigt.Add(gesucht)) break;
                    DescribeEntity(text, alle[i], 0, seen, "EIGENES OBJEKT");
                    break;
                }
            }
            if (gezeigt.Count == 0)
                text.AppendLine("    keines gefunden - steht hier ueberhaupt "
                    + "einer unserer Parkplaetze?");
        }

        private List<string> SammleUiAbschnitte()
        {
            var namen = new List<string>();
            try
            {
                foreach (var system in World.Systems)
                {
                    if (!(system is Game.UI.InGame.InfoSectionBase abschnitt)) continue;
                    if (!abschnitt.visible) continue;
                    namen.Add(system.GetType().Name);
                }
            }
            catch (Exception exception)
            {
                // Eine Bauteilliste darf niemals das Spiel kosten.
                namen.Add("(nicht lesbar: " + exception.Message + ")");
            }
            namen.Sort(StringComparer.Ordinal);
            return namen;
        }

        private void BeschreibeUiAbschnitte(StringBuilder text)
        {
            var namen = SammleUiAbschnitte();
            text.AppendLine();
            text.AppendLine("=== SICHTBARE ABSCHNITTE IM INFOFENSTER ===");
            text.AppendLine("(was das Spiel fuer diese Auswahl anbietet - "
                + "Funktionen, nicht Bauteile)");
            if (namen.Count == 0)
                text.AppendLine("    keine - das Infofenster zeigt fuer diese "
                    + "Auswahl nichts an.");
            foreach (var n in namen) text.AppendLine("    " + n);
        }

        private string BeschreibeKurz(Entity e)
        {
            if (!EntityManager.HasComponent<PrefabRef>(e)) return "Entity " + e.Index;
            var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
            var name = _prefabSystem != null
                && _prefabSystem.TryGetPrefab<PrefabBase>(prefab, out var basis) && basis != null
                ? basis.name : "Prefab " + prefab.Index;
            return name + " (Entity " + e.Index + ")";
        }

        private void InitializeInspector()
        {
            _selectedInfoSystem =
                World.GetOrCreateSystemManaged<Game.UI.InGame.SelectedInfoUISystem>();
        }

        internal void WriteSelectionInspection()
        {
            try
            {
                var selected = _selectedInfoSystem?.selectedEntity ?? Entity.Null;
                if (selected == Entity.Null || !EntityManager.Exists(selected))
                {
                    Mod.log.Warn("PLT-Bauteilliste: nichts angewählt. Erst ein Objekt "
                        + "im Spiel anklicken, dann Strg+Alt+P.");
                    _debugTooltipSystem?.Show(T("Nichts angewählt - erst anklicken.", "Nothing selected - click something first."));
                    return;
                }

                var text = new StringBuilder();
                text.AppendLine("Bauteilliste eines angewählten Objekts");
                text.AppendLine("erstellt " + DateTime.Now.ToString("O"));
                text.AppendLine();
                var seen = new HashSet<Entity>();
                DescribeEntity(text, selected, 0, seen, "ANGEWÄHLT");

                /**
                 * GEGENÜBERSTELLUNG. Ziel ist, so nah wie möglich an einen
                 * echten Vanilla-Parkplatz zu kommen - dafür muss man sehen,
                 * was der hat und wir nicht.
                 *
                 * Zwei Aufrufe genügen: erst einen Vanilla-Parkplatz anwählen
                 * und Strg+Alt+P, dann unseren. Der zweite Bericht enthält
                 * automatisch den Unterschied.
                 */
                BeschreibeEigeneObjekte(text, selected, seen);
                BeschreibeUiAbschnitte(text);
                var jetzt = SammleKomponenten(selected);
                foreach (var a in SammleUiAbschnitte()) jetzt.Add("UI/" + a);
                if (_vergleichName != null)
                {
                    text.AppendLine();
                    text.AppendLine("=== UNTERSCHIED ZUM VORIGEN AUFRUF ===");
                    text.AppendLine("vorher: " + _vergleichName);
                    text.AppendLine("jetzt:  " + BeschreibeKurz(selected));
                    text.AppendLine();
                    text.AppendLine("NUR VORHER (fehlt uns, wenn vorher Vanilla war):");
                    foreach (var k in _vergleichKomponenten.Except(jetzt).OrderBy(x => x))
                        text.AppendLine("    " + k);
                    text.AppendLine();
                    text.AppendLine("NUR JETZT (haben wir zusätzlich):");
                    foreach (var k in jetzt.Except(_vergleichKomponenten).OrderBy(x => x))
                        text.AppendLine("    " + k);
                    text.AppendLine();
                    text.AppendLine("in beiden: "
                        + jetzt.Intersect(_vergleichKomponenten).Count() + " Komponenten");
                }
                _vergleichKomponenten = jetzt;
                _vergleichName = BeschreibeKurz(selected);

                var folder = Path.Combine(Application.persistentDataPath, "Logs");
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder,
                    "ParkingLotTool-bauteile-"
                    + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
                File.WriteAllText(path, text.ToString());
                Mod.log.Info("PLT-Bauteilliste geschrieben: " + path);
                _debugTooltipSystem?.Show(T("Bauteilliste geschrieben.", "Component list written."));
            }
            catch (Exception exception)
            {
                Mod.log.Error(exception, "PLT-Bauteilliste fehlgeschlagen.");
            }
        }

        private void DescribeEntity(StringBuilder text, Entity entity, int depth,
                                    HashSet<Entity> seen, string label)
        {
            var pad = new string(' ', depth * 2);
            if (!EntityManager.Exists(entity))
            {
                text.AppendLine($"{pad}{label} {Show(entity)} EXISTIERT NICHT");
                return;
            }
            if (!seen.Add(entity))
            {
                text.AppendLine($"{pad}{label} {Show(entity)} (schon oben aufgeführt)");
                return;
            }

            text.AppendLine($"{pad}{label} {Show(entity)}  {PrefabName(entity)}"
                + LaneKind(entity));
            text.AppendLine($"{pad}  Komponenten: {ComponentList(entity)}");

            if (EntityManager.HasComponent<PrefabRef>(entity))
            {
                var prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                if (EntityManager.Exists(prefab))
                    text.AppendLine($"{pad}  Prefab-Komponenten: {ComponentList(prefab)}");
            }
            if (EntityManager.HasComponent<Owner>(entity))
            {
                var owner = EntityManager.GetComponentData<Owner>(entity).m_Owner;
                text.AppendLine($"{pad}  Besitzer: {Show(owner)} {PrefabName(owner)}");
            }
            if (EntityManager.HasComponent<Game.Objects.Transform>(entity))
            {
                var transform =
                    EntityManager.GetComponentData<Game.Objects.Transform>(entity);
                text.AppendLine($"{pad}  Lage: "
                    + transform.m_Position.x.ToString("F2", CultureInfo.InvariantCulture)
                    + " / "
                    + transform.m_Position.z.ToString("F2", CultureInfo.InvariantCulture));
                // Ohne die Blickrichtung liesse sich aus einer Bauteilliste
                // nicht ablesen, wie herum ein Stellplatz-Decal gehoert.
                var forward = math.forward(transform.m_Rotation);
                var yaw = math.degrees(math.atan2(forward.x, forward.z));
                text.AppendLine($"{pad}  Blickrichtung: "
                    + yaw.ToString("F1", CultureInfo.InvariantCulture) + " Grad ("
                    + forward.x.ToString("F3", CultureInfo.InvariantCulture) + " / "
                    + forward.z.ToString("F3", CultureInfo.InvariantCulture) + ")");
            }
            DescribePrefabMeasures(text, pad, entity);

            if (depth >= InspectorMaxDepth) return;
            DescribeChildren<Game.Areas.SubArea>(text, entity, depth, seen, "Fläche",
                buffer => buffer.m_Area);
            DescribeChildren<Game.Net.SubNet>(text, entity, depth, seen, "Netz",
                buffer => buffer.m_SubNet);
            DescribeChildren<Game.Objects.SubObject>(text, entity, depth, seen, "Objekt",
                buffer => buffer.m_SubObject);
            // Die eigentlichen Parkspuren stecken als SubLane an den Netzen -
            // die Unterobjekte sind nur Markierungen und Schilder.
            DescribeChildren<Game.Net.SubLane>(text, entity, depth, seen, "Spur",
                buffer => buffer.m_SubLane);
        }

        private void DescribeChildren<T>(StringBuilder text, Entity entity, int depth,
                                         HashSet<Entity> seen, string kind,
                                         Func<T, Entity> pick)
            where T : unmanaged, IBufferElementData
        {
            if (!EntityManager.HasBuffer<T>(entity)) return;
            var buffer = EntityManager.GetBuffer<T>(entity, true);
            var pad = new string(' ', depth * 2);
            text.AppendLine($"{pad}  {kind}-Unterelemente: {buffer.Length}");
            // Indexweise kopieren und NICHT ueber den Aufruf hinaus halten:
            // ein DynamicBuffer wird beim naechsten Strukturwechsel ungueltig.
            var children = new List<Entity>(buffer.Length);
            for (var i = 0; i < buffer.Length; i++) children.Add(pick(buffer[i]));
            var shown = Math.Min(children.Count, InspectorMaxChildrenPerLevel);
            for (var i = 0; i < shown; i++)
                DescribeEntity(text, children[i], depth + 1, seen, kind);
            if (shown < children.Count)
                text.AppendLine($"{pad}    … {children.Count - shown} weitere "
                    + $"{kind}-Unterelemente nicht aufgeführt.");
        }

        /**
         * Die Masse, die man zum Nachbauen wirklich braucht.
         *
         * Die reine Komponentenliste sagt, DASS ein Decal eine Parkspur
         * mitbringt, aber nicht, wie gross es ist und wieviele Stellplaetze
         * darauf passen. Ohne diese Zahlen bleibt der Decal-Abstand geraten.
         */
        private void DescribePrefabMeasures(StringBuilder text, string pad, Entity entity)
        {
            if (!EntityManager.HasComponent<PrefabRef>(entity)) return;
            var prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
            if (!EntityManager.Exists(prefab)) return;

            if (EntityManager.HasComponent<ObjectGeometryData>(prefab))
            {
                var geometry = EntityManager.GetComponentData<ObjectGeometryData>(prefab);
                text.AppendLine($"{pad}  Objektmaß: "
                    + Measure(geometry.m_Size.x) + " x " + Measure(geometry.m_Size.z)
                    + " m (Höhe " + Measure(geometry.m_Size.y) + " m)");
                /*
                 * ENTSCHEIDET, ob wir Bäume und Felsen unter dem Parkplatz
                 * ausblenden dürfen. Im Dekompilat von `OverrideSystem`
                 * (Game.dll, Zeile 1321 und 1458) steht die Regel:
                 *
                 *   Fläche  : AreaGeometryData.m_Flags & CanOverrideObjects
                 *   Objekt  : (ObjectGeometryData.m_Flags
                 *              & (Overridable | DeleteOverridden)) == Overridable
                 *
                 * Träfe sie auch auf UNSERE Ladesäulen und Aufkleber zu, würde
                 * der Parkplatz seine eigene Ausstattung verstecken. Der
                 * vorhandene Dump nannte die Flags nicht, deshalb stehen sie
                 * jetzt hier - gemessen statt angenommen.
                 */
                var over = (geometry.m_Flags
                    & Game.Objects.GeometryFlags.Overridable) != 0;
                var del = (geometry.m_Flags
                    & Game.Objects.GeometryFlags.DeleteOverridden) != 0;
                text.AppendLine($"{pad}  Geometrieflags: " + geometry.m_Flags
                    + "  ->  vom Parkplatz " + (over && !del
                        ? "WUERDE VERSTECKT" : "bleibt sichtbar"));
            }
            if (EntityManager.HasComponent<ParkingLaneData>(prefab))
            {
                var lane = EntityManager.GetComponentData<ParkingLaneData>(prefab);
                text.AppendLine($"{pad}  Stellplatzmaß: "
                    + Measure(lane.m_SlotSize.x) + " x " + Measure(lane.m_SlotSize.y)
                    + " m, Abstand " + Measure(lane.m_SlotInterval)
                    + " m, Winkel " + Measure(lane.m_SlotAngle));
            }
            if (EntityManager.HasComponent<Game.Net.Curve>(entity))
            {
                var curve = EntityManager.GetComponentData<Game.Net.Curve>(entity);
                text.AppendLine($"{pad}  Kurvenlänge: " + Measure(curve.m_Length) + " m");
            }
            if (EntityManager.HasComponent<NetGeometryData>(prefab))
            {
                var net = EntityManager.GetComponentData<NetGeometryData>(prefab);
                text.AppendLine($"{pad}  Wegbreite: " + Measure(net.m_DefaultWidth)
                    + " m");
            }
        }

        private static string Measure(float value) =>
            value.ToString("F2", CultureInfo.InvariantCulture);

        private string ComponentList(Entity entity)
        {
            using var types = EntityManager.GetComponentTypes(entity, Allocator.Temp);
            var names = new List<string>(types.Length);
            for (var i = 0; i < types.Length; i++)
            {
                var full = types[i].GetManagedType()?.FullName ?? types[i].ToString();
                names.Add(full.StartsWith("Game.", StringComparison.Ordinal)
                    ? full.Substring(5) : full);
            }
            names.Sort(StringComparer.Ordinal);
            return string.Join(", ", names);
        }

        /** Kurzform der Spurart - das ist die Frage bei einem Parkplatz. */
        private string LaneKind(Entity entity)
        {
            var kinds = new List<string>();
            if (EntityManager.HasComponent<Game.Net.ParkingLane>(entity))
                kinds.Add("ParkingLane");
            if (EntityManager.HasComponent<Game.Net.CarLane>(entity)) kinds.Add("CarLane");
            if (EntityManager.HasComponent<Game.Net.PedestrianLane>(entity))
                kinds.Add("PedestrianLane");
            if (EntityManager.HasComponent<Game.Net.ConnectionLane>(entity))
                kinds.Add("ConnectionLane");
            if (EntityManager.HasComponent<Game.Net.SlaveLane>(entity)) kinds.Add("SlaveLane");
            if (EntityManager.HasComponent<Game.Net.MasterLane>(entity)) kinds.Add("MasterLane");
            return kinds.Count == 0 ? string.Empty : "  -> " + string.Join("+", kinds);
        }

        private string PrefabName(Entity entity)
        {
            if (entity == Entity.Null || !EntityManager.Exists(entity)) return string.Empty;
            if (!EntityManager.HasComponent<PrefabRef>(entity)) return "(ohne Prefab)";
            var prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
            if (_prefabSystem != null
                && _prefabSystem.TryGetPrefab<PrefabBase>(prefab, out var found)
                && found != null)
                return $"\"{found.name}\" [{found.GetType().Name}]";
            return "(Prefab nicht auflösbar)";
        }

        private static string Show(Entity entity) =>
            entity == Entity.Null ? "Entity.Null"
                : $"#{entity.Index}.{entity.Version}";
    }
}
