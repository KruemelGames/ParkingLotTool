using System;
using Game.Common;
using Game.Prefabs;
using Game.City;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using ParkingLotTool.Geometry;

namespace ParkingLotTool.Tools
{
    /**
     * Der Fahrtrichtungspfeil auf Ein- und Ausfahrt.
     *
     * WARUM DER PLATZHALTER UND NICHT NA ODER EU. Das Spiel fuehrt den Pfeil
     * dreifach: "NA RoadArrow Forward", "EU RoadArrow Forward" und den
     * Platzhalter "RoadArrow Forward". Vanilla-Parkplaetze setzen den
     * PLATZHALTER; CS2 loest daraus die Variante des gewaehlten Themas auf.
     *
     * Wuerden wir selbst NA oder EU waehlen, muessten wir das Thema kennen und
     * bei jedem Wechsel nachziehen - und laegen falsch, sobald der Nutzer es
     * umstellt. Der Platzhalter kann gar nicht auseinanderlaufen.
     *
     * Deshalb schliesst die Abfrage hier PlaceholderObjectElement NICHT aus,
     * anders als die der Buchtaufkleber: dort ist ein Platzhalter unerwuenscht,
     * hier ist er genau das Gesuchte.
     */
    public sealed partial class ParkingLotToolSystem
    {
        /*
         * DER NAME HEISST AUF "Placeholder" AUS.
         *
         * Am 2026-08-27 stand hier "RoadArrow Forward", und im Spiel kam
         * verlaesslich "nicht gefunden". Der Fehler war mein eigener
         * Suchbefehl im Prefababzug: sein Muster brach nach dem zweiten Wort
         * ab, und ich habe die abgeschnittene Fassung in den Code uebernommen.
         * Im Abzug steht vollstaendig:
         *
         *     m_Object=-> RoadArrow Forward Placeholder
         *
         * Lehre: einen Namen aus einer eigenen Greppung nie ungeprueft
         * uebernehmen - erst die Fundstelle im Ganzen ansehen.
         */
        /**
         * DREI NAMEN, IN DIESER REIHENFOLGE.
         *
         * Am liebsten der Platzhalter: CS2 loest ihn selbst in die Variante
         * des gewaehlten Themas auf, und wir muessen das Thema gar nicht
         * kennen. Vanilla-Parkplaetze fuehren ihn so in ihren Prefabs.
         *
         * Der Nutzer hat am 2026-08-27 aber gemeldet, dass er ueber Find It
         * NUR die beiden fertigen Varianten findet, keinen Platzhalter. Gut
         * moeglich, dass der Platzhalter nur INNERHALB von Prefabs existiert
         * und sich nicht frei setzen laesst. Deshalb die beiden konkreten
         * Namen als Rueckfall - lieber ein Pfeil im falschen Thema als gar
         * keiner, und das Log sagt, welcher es geworden ist.
         *
         * Der erste gefundene gewinnt. Zeigt sich, dass damit das falsche
         * Thema gewaehlt wird, ist die Abhilfe eine Themaerkennung - dann
         * aber mit einem gemessenen Beleg statt einer Vermutung.
         */
        private static readonly string[] ArrowPrefabNames =
        {
            "RoadArrow Forward Placeholder",
            "NA RoadArrow Forward",
            "EU RoadArrow Forward",
        };

        private string _arrowPrefabUsed;

        /*
         * NACHMESSEN, OB CS2 DEN PLATZHALTER WIRKLICH AUFLOEST.
         *
         * Dass wir den Platzhalter SETZEN, steht im Log. Ob daraus im Spiel
         * eine themenrichtige Variante wird, war bis zum 2026-08-27 eine
         * Annahme - die einzige, die an diesem Merkmal noch offen war. Die
         * Ladesaeulen werden auf demselben Weg geprueft; hier dieselbe Idee,
         * nur schlanker: ein paar Frames nach dem Bau nachsehen, welches
         * Prefab an den gemerkten Stellen tatsaechlich steht.
         */
        private const int PfeilPruefungFrames = 8;
        private int _pfeilPruefungFrame = -1;
        private bool _pfeilSchutzKontrolle;
        private readonly System.Collections.Generic.List<float3> _pfeilStellen =
            new System.Collections.Generic.List<float3>();

        private EntityQuery _arrowPrefabQuery;
        private Entity _arrowPrefab = Entity.Null;
        private bool _arrowPrefabReported;

        private void InitializeEntranceArrows()
        {
            _arrowPrefabQuery = GetEntityQuery(
                ComponentType.ReadOnly<ObjectData>(),
                ComponentType.ReadOnly<ObjectGeometryData>());
        }

        /**
         * Ein Pfeil je einspuriger Zufahrt, in Fahrtrichtung gedreht.
         *
         * Er sitzt bei 40 Prozent der Strecke von der Strasse aus. Genau in
         * der Mitte laege er dort, wo die Fahrbahn in den Parkplatz muendet
         * und die Randreihe kreuzt; naeher an der Strasse sieht man ihn beim
         * Heranfahren, und das ist der Zweck.
         */
        private int CreateEntranceArrowDefinitions(ParkingLayout layout,
                                                   ref TerrainHeightData heightData)
        {
            if (layout?.NetLine == null || layout.NetLine.Length == 0) return 0;
            if (!ResolveArrowPrefab()) return 0;

            var random = new Unity.Mathematics.Random(
                (uint)Environment.TickCount | 1u);
            var gesetzt = 0;
            for (var i = 0; i < layout.NetLine.Length; i++)
            {
                var piece = layout.NetLine[i];
                if (piece.Art != Zufahrtsart.Einfahrt
                    && piece.Art != Zufahrtsart.Ausfahrt)
                    continue;

                /*
                 * A ist das aeussere Ende an der Strasse, B das innere. Die
                 * Ausfahrt faehrt andersherum - derselbe Kurs mit vertauschten
                 * Enden, genau wie im Netzbau.
                 */
                var ausfahrt = piece.Art == Zufahrtsart.Ausfahrt;
                var von = ausfahrt ? piece.B : piece.A;
                var nach = ausfahrt ? piece.A : piece.B;
                var richtung = nach - von;
                if (math.lengthsq(richtung) < 1e-6f) continue;

                /*
                 * IN DIE MITTE DER FLAECHE, nicht auf einen Streckenanteil.
                 *
                 * Vorher lag der Pfeil bei 40 Prozent der Strecke von der
                 * Strasse aus. Am 2026-08-27 stand er dort nachweislich mit
                 * richtigem Prefab - der Nutzer sah trotzdem nichts, und die
                 * Pipette fand nichts. Bei 40 Prozent liegt der Punkt noch im
                 * Bereich von Strasse und Gehweg; deren Belag deckt ihn zu.
                 *
                 * Ansage des Nutzers: "das decal sollte in der Mitte der
                 * flaeche auftauchen." Genau das ist jetzt moeglich, weil
                 * jedes Zufahrtsrechteck seit heute seine Art traegt - der
                 * Schwerpunkt der passenden Flaeche ist ein echtes Merkmal
                 * und keine Schaetzung ueber einen Anteil.
                 *
                 * Gibt es kein passendes Rechteck, bleibt die Mitte des
                 * Kurses als Rueckfall - immer noch besser als 40 Prozent.
                 */
                var punkt = FlaechenmitteFuer(layout, piece.Art, von, nach);
                if (CreateArrowDefinition(punkt, math.normalize(richtung),
                        i, ref heightData, ref random))
                    gesetzt++;
            }

            if (gesetzt > 0)
            {
                _pfeilPruefungFrame = UnityEngine.Time.frameCount + PfeilPruefungFrames;
                _pfeilSchutzKontrolle = false;
            }
            if (gesetzt > 0)
                Mod.log.Info("PLT-Richtungspfeil: " + gesetzt + " gesetzt, "
                    + "Prefab '" + _arrowPrefabUsed + "'.");
            return gesetzt;
        }

        /**
         * Der Schwerpunkt des Zufahrtsrechtecks dieser Art, das dem Kurs am
         * naechsten liegt.
         *
         * Bei mehreren Zufahrten derselben Art gibt es mehrere Rechtecke;
         * gewaehlt wird das, dessen Schwerpunkt der Kursmitte am naechsten
         * ist. Die ART grenzt dabei schon so weit ein, dass die Naehe nur
         * noch zwischen gleichartigen Nachbarn entscheidet.
         */
        private float2 FlaechenmitteFuer(ParkingLayout layout, Zufahrtsart art,
                                         float2 von, float2 nach)
        {
            var kursmitte = (von + nach) * 0.5f;
            var arten = layout.EntranceQuadArt;
            if (layout.EntranceQuad == null || arten == null)
                return kursmitte;

            var beste = float.PositiveInfinity;
            var treffer = kursmitte;
            for (var i = 0; i < layout.EntranceQuad.Length; i++)
            {
                if (i >= arten.Length || arten[i] != (int)art) continue;
                var ecken = layout.EntranceQuad[i];
                if (ecken == null || ecken.Length == 0) continue;
                var mitte = float2.zero;
                for (var k = 0; k < ecken.Length; k++) mitte += ecken[k];
                mitte /= ecken.Length;
                var abstand = math.distancesq(mitte, kursmitte);
                if (abstand >= beste) continue;
                beste = abstand;
                treffer = mitte;
            }
            return treffer;
        }

        private bool ResolveArrowPrefab()
        {
            if (_arrowPrefab != Entity.Null && EntityManager.Exists(_arrowPrefab))
                return true;

            using var prefabs = _arrowPrefabQuery.ToEntityArray(Allocator.TempJob);
            var gefundene = string.Empty;
            foreach (var gesucht in ArrowPrefabNames)
            {
                for (var i = 0; i < prefabs.Length; i++)
                {
                    if (!_prefabSystem.TryGetPrefab<PrefabBase>(prefabs[i], out var prefab)
                        || prefab == null) continue;
                    if (!string.Equals(prefab.name, gesucht, StringComparison.Ordinal))
                        continue;
                    /*
                     * Beim Platzhalter sofort auf die Themenvariante
                     * umschalten - der Platzhalter selbst hat kein Aussehen.
                     * Bei einem der konkreten Namen greift das nicht und wir
                     * bleiben bei dem, was gefunden wurde.
                     */
                    var variante = WaehleThemenvariante(prefabs[i]);
                    if (variante != Entity.Null)
                    {
                        _arrowPrefab = variante;
                        _arrowPrefabUsed = _prefabSystem
                            .TryGetPrefab<PrefabBase>(variante, out var vp)
                            && vp != null ? vp.name : gesucht;
                    }
                    else
                    {
                        _arrowPrefab = prefabs[i];
                        _arrowPrefabUsed = gesucht;
                    }
                    // ERST melden, wenn der Name steht - sonst druckt die
                    // Zeile eine leere Zeichenkette und die Messung ist wertlos.
                    MeldeArrowPrefab(prefabs[i]);
                    return true;
                }
            }

            if (!_arrowPrefabReported)
            {
                _arrowPrefabReported = true;
                for (var k = 0; k < prefabs.Length; k++)
                {
                    if (!_prefabSystem.TryGetPrefab<PrefabBase>(prefabs[k], out var kandidat)
                        || kandidat == null
                        || kandidat.name.IndexOf("Arrow",
                            StringComparison.OrdinalIgnoreCase) < 0) continue;
                    gefundene += (gefundene.Length == 0 ? "" : ", ") + kandidat.name;
                }
                Mod.log.Warn("PLT-Richtungspfeil: keiner der gesuchten Namen "
                    + "vorhanden (" + string.Join(", ", ArrowPrefabNames)
                    + "). Ein- und Ausfahrt werden ohne Pfeil gebaut; alles "
                    + "andere bleibt unveraendert. Mit 'Arrow' im Namen "
                    + "gefunden: "
                    + (gefundene.Length == 0 ? "gar nichts" : gefundene));
            }
            return false;
        }

        /**
         * DEN PLATZHALTER SELBST AUFLOESEN - CS2 TUT ES NICHT FUER UNS.
         *
         * Am 2026-08-27 im Spiel nachgemessen: an beiden Stellen stand
         * hinterher `RoadArrow Forward Placeholder`, und der Nutzer sah gar
         * keinen Pfeil - auch die Pipette von Find It fand dort nichts. Der
         * Platzhalter ist ein leerer Marker ohne Aussehen.
         *
         * Meine Annahme, CS2 waehle beim freien Setzen selbst die Variante,
         * war also falsch. Sie gilt offenbar nur fuer Objekte, die INNERHALB
         * eines Prefabs als Unterobjekt gefuehrt werden - dort loest die
         * Prefabinitialisierung auf, nicht der Bauvorgang.
         *
         * Die Auswahl ist aber nachvollziehbar, statt geraten. Aus dem
         * Dekompilat:
         *
         *     ThemeObject.LateInitialize  haengt ans Variantenprefab ein
         *     ObjectRequirementElement(m_Requirement = Themen-Entity)
         *     CityConfigurationSystem.defaultTheme  ist das aktive Thema
         *
         * Also: die Variante nehmen, deren Anforderung auf das aktive Thema
         * zeigt. Findet sich keine, die erste - lieber ein Pfeil im falschen
         * Thema als gar keiner.
         */
        private Entity WaehleThemenvariante(Entity platzhalter)
        {
            if (!EntityManager.HasBuffer<PlaceholderObjectElement>(platzhalter))
                return Entity.Null;

            var stadt = World.GetExistingSystemManaged<CityConfigurationSystem>();
            var thema = stadt != null ? stadt.defaultTheme : Entity.Null;

            var puffer = EntityManager
                .GetBuffer<PlaceholderObjectElement>(platzhalter, true);
            var erste = Entity.Null;
            for (var i = 0; i < puffer.Length; i++)
            {
                var variante = puffer[i].m_Object;
                if (variante == Entity.Null || !EntityManager.Exists(variante))
                    continue;
                if (erste == Entity.Null) erste = variante;
                if (thema == Entity.Null
                    || !EntityManager.HasBuffer<ObjectRequirementElement>(variante))
                    continue;
                var anforderungen = EntityManager
                    .GetBuffer<ObjectRequirementElement>(variante, true);
                for (var k = 0; k < anforderungen.Length; k++)
                    if (anforderungen[k].m_Requirement == thema) return variante;
            }
            return erste;
        }

        /**
         * Sagt EINMAL, welche Varianten hinter dem Platzhalter stehen und
         * welche davon genommen wurde.
         */
        private void MeldeArrowPrefab(Entity prefab)
        {
            if (_arrowPrefabReported) return;
            _arrowPrefabReported = true;

            var varianten = string.Empty;
            if (EntityManager.HasBuffer<PlaceholderObjectElement>(prefab))
            {
                var puffer = EntityManager
                    .GetBuffer<PlaceholderObjectElement>(prefab, true);
                for (var i = 0; i < puffer.Length; i++)
                {
                    if (!_prefabSystem.TryGetPrefab<PrefabBase>(
                            puffer[i].m_Object, out var variante)
                        || variante == null) continue;
                    varianten += (varianten.Length == 0 ? string.Empty : ", ")
                        + variante.name;
                }
            }

            var stadt = World.GetExistingSystemManaged<CityConfigurationSystem>();
            var thema = "unbekannt";
            if (stadt != null && stadt.defaultTheme != Entity.Null
                && _prefabSystem.TryGetPrefab<PrefabBase>(stadt.defaultTheme,
                    out var themenPrefab) && themenPrefab != null)
                thema = themenPrefab.name;

            Mod.log.Info("PLT-Richtungspfeil: gesetzt wird '" + _arrowPrefabUsed
                + "'. Stadtthema: " + thema + ". Varianten am Platzhalter: "
                + (varianten.Length == 0 ? "keine gelistet" : varianten));
        }

        private bool CreateArrowDefinition(float2 punkt,
                                           float2 richtung,
                                           int index,
                                           ref TerrainHeightData heightData,
                                           ref Unity.Mathematics.Random random)
        {
            if (!math.all(math.isfinite(punkt))) return false;
            var hoehe = TerrainUtils.SampleHeight(
                ref heightData, new float3(punkt.x, 0f, punkt.y));
            if (!math.isfinite(hoehe)) return false;

            var definition = EntityManager.CreateEntity();
            EntityManager.AddComponentData(definition, new CreationDefinition
            {
                m_Prefab = _arrowPrefab,
                m_RandomSeed = random.NextInt(),
            });
            EntityManager.AddComponent<Updated>(definition);

            var vorgabe = default(ObjectDefinition);
            vorgabe.m_Position = new float3(punkt.x, hoehe, punkt.y);
            vorgabe.m_Rotation = quaternion.LookRotationSafe(
                new float3(richtung.x, 0f, richtung.y), math.up());
            vorgabe.m_Probability = 100;
            vorgabe.m_PrefabSubIndex = -1;
            vorgabe.m_Scale = 1f;
            vorgabe.m_Intensity = 1f;
            vorgabe.m_ParentMesh = -1;
            EntityManager.AddComponentData(definition, vorgabe);
            RecordObjectDefinition("Richtungspfeil", index, _arrowPrefab,
                definition, vorgabe.m_Position);
            _pfeilStellen.Add(vorgabe.m_Position);
            Mod.log.Info("PLT-Richtungspfeil gesetzt bei "
                + $"{vorgabe.m_Position.x:F1}/{vorgabe.m_Position.z:F1}, "
                + $"Hoehe {vorgabe.m_Position.y:F2} m.");
            return true;
        }

        /**
         * Misst den dauerhaften Pfeil VOR und NACH seinem Besitzerschutz.
         *
         * Der Laufzeitabzug vom 2026-08-17 belegt den Unterschied zum
         * sichtbaren Buchtaufkleber: nur der Pfeil traegt `Overridable`.
         * `OverrideSystem` blendet ein solches frei stehendes Objekt aus,
         * sobald seine 3,00-x-6,00-m-Geometrie Netz oder Flaeche trifft.
         * Deshalb genuegt die alte Prefab-/Positionsmessung nicht: dieselbe
         * Entity kann existieren und trotzdem `Overridden` sein.
         */
        private void PruefeRichtungspfeile()
        {
            if (_pfeilPruefungFrame < 0
                || UnityEngine.Time.frameCount < _pfeilPruefungFrame) return;
            _pfeilPruefungFrame = -1;
            if (_pfeilStellen.Count == 0) return;

            using var objekte = _permanentObjectQuery.ToEntityArray(Allocator.Temp);
            var gefunden = string.Empty;
            var treffer = 0;
            var pfeile = new Entity[_pfeilStellen.Count];
            var benutzt = new System.Collections.Generic.HashSet<Entity>();
            for (var t = 0; t < _pfeilStellen.Count; t++)
            {
                var beste = 4f * 4f;
                var name = string.Empty;
                for (var i = 0; i < objekte.Length; i++)
                {
                    if (benutzt.Contains(objekte[i])) continue;
                    var lage = EntityManager
                        .GetComponentData<Game.Objects.Transform>(objekte[i]);
                    var abstand = math.distancesq(lage.m_Position.xz,
                        _pfeilStellen[t].xz);
                    if (abstand > beste) continue;
                    var prefab = EntityManager
                        .GetComponentData<PrefabRef>(objekte[i]).m_Prefab;
                    if (!_prefabSystem.TryGetPrefab<PrefabBase>(prefab, out var p)
                        || p == null
                        || p.name.IndexOf("Arrow",
                            StringComparison.OrdinalIgnoreCase) < 0) continue;
                    beste = abstand;
                    name = p.name;
                    pfeile[t] = objekte[i];
                }
                if (name.Length == 0) continue;
                benutzt.Add(pfeile[t]);
                treffer++;
                if (gefunden.IndexOf(name, StringComparison.Ordinal) < 0)
                    gefunden += (gefunden.Length == 0 ? "" : ", ") + name;
            }
            var stellen = _pfeilStellen.Count;
            Mod.log.Info("PLT-Richtungspfeil nachgemessen: " + treffer
                + " von " + stellen + " Stellen tragen einen Pfeil"
                + (gefunden.Length == 0
                    ? ". KEINER gefunden - CS2 hat den Platzhalter nicht "
                      + "aufgeloest oder das Objekt nicht materialisiert."
                    : ". Tatsaechliches Prefab: " + gefunden));

            var phase = _pfeilSchutzKontrolle ? "NACH Schutz" : "VOR Schutz";
            var heightData = _terrainSystem.GetHeightData(waitForPending: true);
            for (var i = 0; i < pfeile.Length; i++)
            {
                var pfeil = pfeile[i];
                if (pfeil == Entity.Null || !EntityManager.Exists(pfeil))
                {
                    Mod.log.Warn($"PLT-Richtungspfeil Messung {phase}, Stelle "
                        + $"{i}: keine Entity innerhalb 4,00 m gefunden.");
                    continue;
                }
                LogRichtungspfeilZustand(phase, i, pfeil, _pfeilStellen[i],
                    objekte, ref heightData);
            }

            if (!_pfeilSchutzKontrolle
                && SchuetzeRichtungspfeile(pfeile) > 0)
            {
                _pfeilSchutzKontrolle = true;
                _pfeilPruefungFrame = UnityEngine.Time.frameCount
                    + PfeilPruefungFrames;
                Mod.log.Info("PLT-Richtungspfeil: Besitzerschutz gesetzt; "
                    + "Kontrollmessung folgt in " + PfeilPruefungFrames
                    + " Frames.");
                return;
            }

            // Die Stellen erst NACH der Kontrollmessung leeren. Sonst koennte
            // die zweite Zeile nicht denselben Pfeil gegen dieselbe Soll-Lage
            // messen.
            _pfeilStellen.Clear();
            _pfeilSchutzKontrolle = false;
        }

        /**
         * Gibt nur der permanenten Instanz einen Besitzer - keinen Eintrag im
         * SubObject-Puffer der Flaeche. Genau dieses Muster ist fuer die
         * Ladesaeulen bereits im Projekt belegt: `OverrideSystem` erkennt
         * Pfeil und PLT-Netz als zusammengehoerig, waehrend
         * `RelocateSubObjects` den Pfeil ohne Puffereintrag nicht verstreut.
         */
        private int SchuetzeRichtungspfeile(Entity[] pfeile)
        {
            if (_lotOwner == Entity.Null || !EntityManager.Exists(_lotOwner))
            {
                Mod.log.Warn("PLT-Richtungspfeil: Besitzerschutz nicht gesetzt, "
                    + "weil die Parkplatzflaeche fehlt.");
                return 0;
            }

            var geschuetzt = 0;
            for (var i = 0; i < pfeile.Length; i++)
            {
                var pfeil = pfeile[i];
                if (pfeil == Entity.Null || !EntityManager.Exists(pfeil)) continue;
                if (EntityManager.HasComponent<Owner>(pfeil)) continue;
                EntityManager.AddComponentData(pfeil,
                    new Owner { m_Owner = _lotOwner });
                if (!EntityManager.HasComponent<Updated>(pfeil))
                    EntityManager.AddComponent<Updated>(pfeil);
                geschuetzt++;
            }
            return geschuetzt;
        }

        private void LogRichtungspfeilZustand(
            string phase,
            int index,
            Entity pfeil,
            float3 soll,
            NativeArray<Entity> objekte,
            ref TerrainHeightData heightData)
        {
            var transform = EntityManager
                .GetComponentData<Game.Objects.Transform>(pfeil);
            var terrain = TerrainUtils.SampleHeight(ref heightData,
                new float3(transform.m_Position.x, 0f, transform.m_Position.z));
            var netObject = EntityManager.HasComponent<Game.Objects.NetObject>(pfeil);
            var netFlags = netObject
                ? EntityManager.GetComponentData<Game.Objects.NetObject>(pfeil)
                    .m_Flags.ToString()
                : "fehlt";
            var owner = EntityManager.HasComponent<Owner>(pfeil)
                ? EntityManager.GetComponentData<Owner>(pfeil).m_Owner
                : Entity.Null;
            var meshBatches = EntityManager
                .HasBuffer<Game.Rendering.MeshBatch>(pfeil)
                ? EntityManager.GetBuffer<Game.Rendering.MeshBatch>(pfeil, true).Length
                : -1;
            var referenz = NaechstesSichtbaresBuchtdecal(objekte,
                transform.m_Position, ref heightData);

            Mod.log.Info($"PLT-Richtungspfeil Messung {phase}, Stelle {index} "
                + $"{Show(pfeil)}: Sollhoehe {soll.y:F3} m, Ist "
                + $"{transform.m_Position.y:F3} m, Terrain {terrain:F3} m "
                + $"(Ist-Terrain {transform.m_Position.y - terrain:+0.000;-0.000;0.000} m); "
                + $"NetObject={(netObject ? "JA " + netFlags : "NEIN")}, "
                + $"Overridden={(EntityManager.HasComponent<Overridden>(pfeil) ? "JA" : "NEIN")}, "
                + $"Owner={(owner == Entity.Null ? "NEIN" : Show(owner))}, "
                + $"Attached={(EntityManager.HasComponent<Game.Objects.Attached>(pfeil) ? "JA" : "NEIN")}, "
                + $"MeshBatch={(meshBatches < 0 ? "FEHLT" : meshBatches.ToString())}, "
                + $"CullingInfo={(EntityManager.HasComponent<Game.Rendering.CullingInfo>(pfeil) ? "JA" : "NEIN")}, "
                + $"Hidden={(EntityManager.HasComponent<Hidden>(pfeil) ? "JA" : "NEIN")}. "
                + referenz);
        }

        /** Vergleich zur sichtbaren Bucht auf demselben, moeglicherweise geneigten Lot. */
        private string NaechstesSichtbaresBuchtdecal(
            NativeArray<Entity> objekte,
            float3 pfeilPosition,
            ref TerrainHeightData heightData)
        {
            var beste = 50f * 50f;
            var treffer = Entity.Null;
            for (var i = 0; i < objekte.Length; i++)
            {
                var prefab = EntityManager.GetComponentData<PrefabRef>(objekte[i]).m_Prefab;
                if (prefab != _bayDecalPrefab && prefab != _disabledDecalPrefab
                    && prefab != _electricDecalPrefab) continue;
                var transform = EntityManager
                    .GetComponentData<Game.Objects.Transform>(objekte[i]);
                var abstand = math.distancesq(transform.m_Position.xz,
                    pfeilPosition.xz);
                if (abstand >= beste) continue;
                beste = abstand;
                treffer = objekte[i];
            }
            if (treffer == Entity.Null) return "Kein sichtbares Buchtdecal in 50 m.";

            var lage = EntityManager
                .GetComponentData<Game.Objects.Transform>(treffer).m_Position;
            var terrain = TerrainUtils.SampleHeight(ref heightData,
                new float3(lage.x, 0f, lage.z));
            return $"Naechstes sichtbares Buchtdecal {math.sqrt(beste):F2} m "
                + $"entfernt: Ist-Terrain {lage.y - terrain:+0.000;-0.000;0.000} m.";
        }
    }
}
