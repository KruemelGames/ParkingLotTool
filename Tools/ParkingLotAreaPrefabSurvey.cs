using System;
using System.Collections.Generic;
using System.Linq;
using Game.Areas;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    /**
     * Traegt zusammen, welche Flaechen-Prefabs es gibt und welcher Art sie
     * sind.
     *
     * HINTERGRUND: der Parkplatz laesst sich bisher nur auf den Decals
     * anklicken, nicht auf der ganzen Flaeche. Ursache ist gemessen:
     * `DefaultToolSystem.InitializeRaycast` setzt
     * `areaTypeMask |= AreaTypeMask.Lots` und NIE `Surfaces`; welche Maske
     * eine Flaeche bekommt, entscheidet `AreaUtils.GetTypeMask` aus
     * `AreaData.m_Type` am Prefab. Unsere Flaechen sind `Surface` (Maske 16),
     * gesucht ist `Lot` (Maske 1).
     *
     * `LotPrefab` fuehrt ausserdem `Game.Objects.SubObject` im Archetyp -
     * Lot-Flaechen sind also dafuer gemacht, Objekte zu besitzen. Genau das
     * brauchen wir fuer die Decals, deren Verschwinden beim Hovern daher
     * kommt, dass `SubObjectHiddenSystem` Unterobjekte ausblendet, sobald
     * der Besitzer eine Temp-Kopie hat.
     *
     * Diese Uebersicht sagt, welche Lot-Prefabs das Spiel mitbringt und ob
     * eines davon neutral genug ist. Raten waere hier teuer: ein
     * Extraktor-Areal brachte eigene Wirtschaftslogik mit.
     */
    public sealed partial class ParkingLotToolSystem
    {
        private EntityQuery _areaPrefabQuery;

        private void InitializeAreaPrefabSurvey()
        {
            // Die Art steht in AreaGeometryData.m_Type, NICHT in AreaData -
            // das traegt nur den Archetyp. Genau diese Komponente liest auch
            // Game.Areas.RaycastJobs fuer seinen Maskenvergleich.
            _areaPrefabQuery = GetEntityQuery(
                ComponentType.ReadOnly<AreaGeometryData>(),
                ComponentType.ReadOnly<PrefabData>());
        }

        /**
         * Die waehlbaren Flaechen ans Panel geben.
         *
         * NICHT NACH NAMEN FILTERN - jedenfalls nicht bei fremden Flaechen.
         * Eine Sperrliste auf "*Placeholder" bricht in dem Moment, in dem
         * jemand einen Flaechen-Mod installiert, und genau dafuer ist die
         * Auswahl da. Deshalb gilt: was NICHT aus dem Grundspiel kommt, ist
         * immer dabei. Nur die eingebauten Platzhalter und die drei
         * Sonderflaechen des Spiels werden aussortiert - die sind namentlich
         * bekannt und stellen im Spiel Fehler dar, keine Materialien.
         *
         * Gemessen am 2026-08-21: 29 Flaechen, davon 16 brauchbar.
         */
        private static readonly string[] KeineFlaechen =
        {
            "Clip Surface", "Missing Area", "Surface Area",
        };

        private void VeroeffentlicheFlaechenliste()
        {
            if (_uiSystem == null) return;
            try
            {
                using var prefabs = _areaPrefabQuery.ToEntityArray(Allocator.TempJob);
                var namen = new List<string>();
                var symbole = new Dictionary<string, string>();
                for (var i = 0; i < prefabs.Length; i++)
                {
                    var entity = prefabs[i];
                    var geometrie =
                        EntityManager.GetComponentData<AreaGeometryData>(entity);
                    if (geometrie.m_Type != AreaType.Surface) continue;
                    if (_prefabSystem == null
                        || !_prefabSystem.TryGetPrefab<PrefabBase>(entity, out var prefab)
                        || prefab == null) continue;
                    var name = prefab.name;
                    if (string.IsNullOrEmpty(name)) continue;
                    if (prefab.isBuiltin)
                    {
                        if (name.EndsWith("Placeholder", StringComparison.Ordinal)) continue;
                        if (Array.IndexOf(KeineFlaechen, name) >= 0) continue;
                    }
                    namen.Add(name);
                    symbole[name] = Vorschaubild(prefab);
                }
                namen.Sort(StringComparer.Ordinal);
                for (var i = 0; i < namen.Count; i++)
                    namen[i] = namen[i] + "\t" + symbole[namen[i]];
                _uiSystem.SetzeFlaechenliste(namen.ToArray());
            }
            catch (Exception ausnahme)
            {
                Mod.log.Warn("PLT: Flaechenliste nicht lesbar: " + ausnahme.Message);
            }
        }

        /**
         * Das Vorschaubild einer Flaeche - oder ein leerer String.
         *
         * WARUM WIR NICHT SELBST EINEN PFAD BAUEN. Naheliegend waere
         * gewesen, die Adresse aus dem Namen zusammenzusetzen: die Asset
         * Icon Library legt ihre Bilder unter
         * `coui://ail/SurfacePrefab.<Name>.svg` ab, und fuer die 17
         * Flaechen des Grundspiels stimmt das auch (nachgezaehlt am
         * 2026-08-25 im entpackten Ordner `ModsData/AssetIconLibrary/
         * .Thumbnails`). Es waere trotzdem falsch gewesen:
         *
         *   - Der Dateiname haengt am Stil, den der Nutzer in den
         *     Einstellungen der Bibliothek waehlt. `GetAvailableIcons`
         *     nimmt nur Dateien direkt im Ordner ODER in einem Unterordner,
         *     der genau so heisst wie der eingestellte Stil - dann steht
         *     der Stil mit im Pfad.
         *   - Flaechen aus anderen Mods bringen ihre Bilder selbst mit,
         *     ueber eigene Ordner und die Icon-API der Bibliothek. Ein
         *     geratener Pfad findet die nie.
         *   - Fehlt die Bibliothek, zeigt ein geratener Pfad auf nichts.
         *
         * Deshalb fragen wir das Prefab selbst. Die Bibliothek schreibt ihre
         * Adressen in `UIObject.m_Icon` - gemessen im Dekompilat,
         * `IconReplacerSystem.UpdatePrefab`: existiert die Komponente, wird
         * `m_Icon` ueberschrieben, fehlt sie, wird sie angelegt. Wir lesen
         * also genau das Feld, das die Bibliothek fuellt, und bekommen ohne
         * eigenes Zutun das Richtige fuer JEDE Flaeche - eingebaute wie
         * fremde - und automatisch das vorhandene Bild des Spiels, wo es
         * eins gibt.
         *
         * Leerer String heisst: kein Bild. Das Panel zeigt dann nur den
         * Namen, so wie bisher.
         */
        private static string Vorschaubild(PrefabBase prefab)
        {
            if (prefab == null) return string.Empty;
            return prefab.TryGet<UIObject>(out var ui) && ui != null
                   && !string.IsNullOrEmpty(ui.m_Icon)
                ? ui.m_Icon
                : string.Empty;
        }

        private DebugAreaPrefabs SurveyAreaPrefabs()
        {
            try
            {
                using var prefabs = _areaPrefabQuery.ToEntityArray(Allocator.TempJob);
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                var lots = new List<DebugAreaPrefab>();
                var flaechen = new List<DebugAreaPrefab>();

                for (var i = 0; i < prefabs.Length; i++)
                {
                    var entity = prefabs[i];
                    var geometryData =
                        EntityManager.GetComponentData<AreaGeometryData>(entity);
                    var type = geometryData.m_Type.ToString();
                    counts.TryGetValue(type, out var seen);
                    counts[type] = seen + 1;
                    /*
                     * WER RAEUMT BAEUME? `Game.Objects.OverrideSystem`
                     * (Game.dll, Zeile 1319) steigt SOFORT aus, wenn der
                     * Flaeche `CanOverrideObjects` fehlt:
                     *
                     *   if ((areaGeometryData.m_Flags
                     *        & GeometryFlags.CanOverrideObjects) == 0) return;
                     *
                     * Der Nutzer meldet am 2026-08-14, dass manche Baeume
                     * unter dem Parkplatz stehen bleiben und andere nicht,
                     * ohne Muster nach Groesse. Also entscheidet nicht der
                     * Baum, sondern WO er steht. Diese Zeile sagt, welche
                     * unserer Flaechen ueberhaupt raeumen duerfen.
                     */
                    if (_prefabSystem != null
                        && _prefabSystem.TryGetPrefab<PrefabBase>(entity, out var known)
                        && known != null
                        && (known.name == "Grass Surface 01"
                            || known.name == "Pavement Surface 01"
                            || known.name.StartsWith("PLT ", StringComparison.Ordinal)))
                    {
                        var raeumt = (geometryData.m_Flags
                            & Game.Areas.GeometryFlags.CanOverrideObjects) != 0;
                        Mod.log.Info("PLT-Flaechenprefab '" + known.name + "': Typ "
                            + type + ", Flags " + geometryData.m_Flags
                            + " -> raeumt Objekte " + (raeumt ? "JA" : "NEIN"));
                    }
                    /**
                     * FLAECHEN MITSCHREIBEN, NICHT NUR LOTS.
                     *
                     * Der Nutzer will Belag und Gruen frei waehlen koennen.
                     * Welche Flaechen es ueberhaupt gibt, weiss nur das Spiel -
                     * und mit Mods des Nutzers sind es andere als bei mir.
                     * Deshalb zaehlt der Abzug sie jetzt namentlich auf.
                     */
                    if (geometryData.m_Type == AreaType.Surface)
                    {
                        var flaeche = new DebugAreaPrefab { AreaType = type };
                        if (_prefabSystem != null
                            && _prefabSystem.TryGetPrefab<PrefabBase>(
                                entity, out var flaechenPrefab)
                            && flaechenPrefab != null)
                        {
                            flaeche.Name = flaechenPrefab.name;
                            flaeche.PrefabClass = flaechenPrefab.GetType().Name;
                            flaeche.Builtin = flaechenPrefab.isBuiltin;
                        }
                        flaechen.Add(flaeche);
                        continue;
                    }
                    if (geometryData.m_Type != AreaType.Lot) continue;

                    var record = new DebugAreaPrefab { AreaType = type };
                    if (_prefabSystem != null
                        && _prefabSystem.TryGetPrefab<PrefabBase>(entity, out var prefab)
                        && prefab != null)
                    {
                        record.Name = prefab.name;
                        record.PrefabClass = prefab.GetType().Name;
                        record.Builtin = prefab.isBuiltin;
                    }
                    // LotData entscheidet, ob ein Prefab als stiller Besitzer
                    // taugt: ein editierbares Areal mit Reichweitenfarbe waere
                    // im Spiel sichtbar und vom Nutzer verschiebbar.
                    if (EntityManager.HasComponent<LotData>(entity))
                    {
                        var lotData = EntityManager.GetComponentData<LotData>(entity);
                        record.MaxRadius = lotData.m_MaxRadius;
                        record.AllowOverlap = lotData.m_AllowOverlap;
                        record.AllowEditing = lotData.m_AllowEditing;
                        record.OnWater = lotData.m_OnWater;
                    }
                    lots.Add(record);
                }

                return new DebugAreaPrefabs
                {
                    Total = prefabs.Length,
                    ByType = counts.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => $"{pair.Key}: {pair.Value}").ToArray(),
                    Lots = lots.OrderBy(item => item.Name ?? string.Empty,
                        StringComparer.Ordinal).ToArray(),
                    Surfaces = flaechen.OrderBy(item => item.Name ?? string.Empty,
                        StringComparer.Ordinal).ToArray(),
                    Note = "Anklickbar ist im Spiel nur AreaType.Lot - "
                        + "DefaultToolSystem setzt AreaTypeMask.Lots und nie "
                        + "Surfaces. Gesucht ist ein Lot-Prefab ohne eigene "
                        + "Spiellogik, das als stiller Besitzer taugt.",
                };
            }
            catch (Exception exception)
            {
                Mod.log.Error(exception, "PLT konnte die Flächen-Prefabs nicht listen.");
                return new DebugAreaPrefabs
                {
                    Note = "Konnte nicht gelesen werden: " + exception.Message,
                };
            }
        }
    }
}
