using System;
using System.Collections.Generic;
using System.Reflection;
using Game;
using Game.Prefabs;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * WARUM DIE ZONING-STRASSE EINE ECHTE, ABER UNSICHTBARE STRASSE IST.
     *
     * Unsere Parkplatzwege sind unsichtbare Fusswege; den Asphalt liefert
     * unsere eigene Flaeche. Fuer Zoning geht das nicht: `PathwayPrefab`
     * bringt keinen Zonenblock mit, und ohne Block entstehen keine Parzellen.
     * Gebraucht wird also ein `RoadPrefab` - und das ist von Haus aus
     * sichtbar, mit Bordstein und Gehweg mitten im Parkplatz.
     *
     * Dieses System klont deshalb die gewaehlte Strasse und blendet an jedem
     * Querschnitt alle vier Ebenen aus. Der Nutzer hat am 2026-09-02
     * ausdruecklich die unsichtbare Variante gewaehlt.
     *
     * DASS DAS TRAEGT, IST IM DEKOMPILAT NACHGELESEN, NICHT VERMUTET:
     *
     *  - `NetInitializeSystem` uebersetzt `m_HiddenLayers` in die vier
     *    `NetSectionFlags.Hidden*`-Bits.
     *  - `NetCompositionMeshRefSystem` laesst genau die Bauteile mit
     *    `NetSectionFlags.Hidden` beim Sammeln der Meshes weg. Nur dort
     *    wirkt die Ausblendung.
     *  - `NetCompositionHelpers` setzt daraus `CompositionState.Hidden` und
     *    rechnet Versatz und Breite unveraendert weiter.
     *  - `LaneSystem` nimmt bei diesem Zustand zusaetzlich die Grafik der
     *    Fahrspuren heraus - fuer uns erwuenscht.
     *  - `BlockSystem` fragt `CompositionState` an KEINER Stelle ab. Die
     *    Zonenblockbildung interessiert die Sichtbarkeit nicht.
     *
     * Die Breite bleibt damit die gemessenen 8,00 m der Gasse. Wer hier
     * kuerzt und stattdessen `m_Sections` leert, bekommt Breite 0 und eine
     * Sackgasse: leere Sections sind etwas anderes als versteckte.
     *
     * ZUR PHASE: `AddPrefab` legt nur die Entity samt Nullwerten an; den
     * Instanzarchetyp baut `PrefabInitializeSystem`. Deshalb laeuft dieses
     * System wie die Vorflaeche in `PrefabUpdate` davor und nie aus
     * `ToolUpdate` heraus.
     */
    public sealed partial class ParkingLotZoningRoadPrefabSystem : GameSystemBase
    {
        private sealed class Eintrag
        {
            public Entity Original;
            public Entity KlonEntity;
            public RoadPrefab Klon;
            public string Name;
            public int Querschnitte;
            public int EntfernteSubObjects;
            public int EntfernteKompositionsobjekte;
            public bool AufbautenGeloggt;
            public string LetzteMessung;
            public int LetzteKompositionsverweiszahl = -1;
            public int Pruefungen;
            public bool Bereit;
            public bool Fehler;
            public bool Aufgegeben;
        }

        /**
         * Dieselbe Grenze wie bei der Vorflaeche: zwei Zyklen reichen im
         * Normalfall, 120 ist die Schwelle, ab der etwas grundlegend anders
         * laeuft als gemessen.
         */
        private const int AufgabenNachZyklen = 120;

        private const NetPieceLayerMask AlleEbenen = NetPieceLayerMask.Surface
            | NetPieceLayerMask.Bottom | NetPieceLayerMask.Top
            | NetPieceLayerMask.Side;


        /**
         * Diese Klone entstehen IMMER, ohne dass jemand sie anfordert.
         *
         * Ein Spielstand speichert unsere Strassenkanten als Verweis auf das
         * Prefab. Wird der Klon erst erzeugt, wenn der Nutzer eine Flaeche
         * zieht, existiert er beim Laden noch nicht - und der Verweis geht
         * ins Leere. Die harte Grenze ist der Aufruf von
         * `PrefabSystem.Deserialize` durch den `SerializerSystem`; danach ist
         * jede Anmeldung fuer diesen Ladevorgang zu spaet.
         *
         * Deshalb beide moeglichen Vorbilder, nicht nur das eingestellte:
         * welches der Nutzer beim Bauen gewaehlt hatte, weiss der Spielstand,
         * das Hauptmenue aber nicht.
         */
        private static readonly string[] StandardStrassen =
        {
            "Alley", "Gravel Road",
        };

        private readonly Dictionary<Entity, Eintrag> _eintraege
            = new Dictionary<Entity, Eintrag>();
        private readonly HashSet<string> _gesaet
            = new HashSet<string>(StringComparer.Ordinal);
        private EntityQuery _strassenprefabs;
        private PrefabSystem _prefabSystem;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _strassenprefabs = GetEntityQuery(
                ComponentType.ReadOnly<RoadData>(),
                ComponentType.ReadOnly<NetGeometryData>(),
                ComponentType.ReadOnly<PrefabData>(),
                ComponentType.Exclude<PlaceholderObjectElement>());

            /*
             * `NetCompositionObject` entsteht nicht im PrefabUpdate des
             * RoadPrefabs, sondern fuer jede spaeter benoetigte Kanten- oder
             * Knotenkomposition neu. Der zweite Lauf bleibt in DIESER Datei
             * und sitzt nach `NetCompositionSystem` (Modification4), aber vor
             * `SecondaryObjectSystem` (Modification4B). So sieht der Erzeuger
             * von Laternen und Schildern bereits den leeren Puffer.
             */
            World.GetOrCreateSystemManaged<UpdateSystem>().UpdateAfter<
                ParkingLotZoningRoadObjectCleanupSystem,
                NetCompositionSystem>(SystemUpdatePhase.Modification4);
        }

        /**
         * Legt die Standardklone an, sobald ihre Vorbilder da sind.
         *
         * Laeuft in jedem PrefabUpdate, bis beide sitzen - wann genau CS2 die
         * Vanilla-Strassen bereitstellt, ist im Dekompilat nicht als Vertrag
         * belegt. Die Logzeile beim Anmelden ist deshalb Teil der Abnahme:
         * sie muss VOR dem ersten Ladevorgang stehen.
         */
        private void SaeheStandardklone()
        {
            if (_gesaet.Count >= StandardStrassen.Length) return;
            if (_strassenprefabs.IsEmptyIgnoreFilter) return;

            using var kandidaten = _strassenprefabs.ToEntityArray(
                Unity.Collections.Allocator.Temp);
            for (var i = 0; i < kandidaten.Length; i++)
            {
                if (!_prefabSystem.TryGetPrefab<PrefabBase>(kandidaten[i],
                        out var vorbild) || vorbild == null) continue;
                for (var n = 0; n < StandardStrassen.Length; n++)
                {
                    var name = StandardStrassen[n];
                    if (_gesaet.Contains(name)) continue;
                    if (!string.Equals(vorbild.name, name,
                            StringComparison.Ordinal)) continue;
                    _gesaet.Add(name);
                    FordereAn(kandidaten[i], out _, out _);
                }
            }
        }

        /**
         * Das Werkzeug stellt nur die Anforderung; angemeldet wird in
         * OnUpdate und damit in der richtigen Phase. Rueckgabe ist
         * `Entity.Null`, solange der Klon nicht fertig ist.
         */
        public Entity FordereAn(Entity original, out bool fehlgeschlagen,
            out bool aufgegeben)
        {
            fehlgeschlagen = false;
            aufgegeben = false;
            if (original == Entity.Null || !EntityManager.Exists(original))
                return Entity.Null;

            if (!_eintraege.TryGetValue(original, out var eintrag))
            {
                if (!_prefabSystem.TryGetPrefab<RoadPrefab>(original,
                        out var vorbild) || vorbild == null)
                {
                    Mod.log.Warn("PLT-Zoningstrasse: Das angeforderte Prefab "
                        + "ist kein RoadPrefab; kein Klon moeglich.");
                    fehlgeschlagen = true;
                    return Entity.Null;
                }
                if (vorbild.m_ZoneBlock == null)
                {
                    Mod.log.Warn("PLT-Zoningstrasse: '" + vorbild.name
                        + "' hat keinen ZoneBlock; daran wuerde nichts wachsen.");
                    fehlgeschlagen = true;
                    return Entity.Null;
                }

                eintrag = new Eintrag
                {
                    Original = original,
                    KlonEntity = Entity.Null,
                    Name = "PLT Zoningstrasse (" + vorbild.name + ")",
                };
                _eintraege.Add(original, eintrag);
                Mod.log.Info("PLT-Zoningstrasse: '" + eintrag.Name
                    + "' fuer den naechsten PrefabUpdate-Zyklus angefordert.");
            }

            fehlgeschlagen = eintrag.Fehler;
            aufgegeben = eintrag.Aufgegeben;
            return eintrag.Bereit ? eintrag.KlonEntity : Entity.Null;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            SaeheStandardklone();
            foreach (var eintrag in _eintraege.Values)
            {
                if (eintrag.Fehler || eintrag.Bereit) continue;

                if (eintrag.KlonEntity == Entity.Null)
                {
                    Registriere(eintrag);
                    // Die Spielsysteme hinter uns muessen erst laufen. Eine
                    // Pruefung hier saehe absichtlich nur die Nullwerte.
                    continue;
                }

                eintrag.Pruefungen++;
                if (Pruefe(eintrag, out var zustand))
                {
                    EntferneTerraineingriff(eintrag);
                    eintrag.Bereit = true;
                    EntferneKompositionsobjekteUndMesse(eintrag);
                    Mod.log.Info("PLT-Zoningstrasse: '" + eintrag.Name
                        + "' ist nach " + eintrag.Pruefungen
                        + " Zyklus/Zyklen benutzbar. " + zustand);
                }
                else if (eintrag.Pruefungen >= AufgabenNachZyklen)
                {
                    eintrag.Aufgegeben = true;
                    eintrag.Fehler = true;
                    Mod.log.Error("PLT-Zoningstrasse: '" + eintrag.Name
                        + "' ist nach " + eintrag.Pruefungen
                        + " Zyklen nicht benutzbar. " + zustand);
                }
            }
        }

        private void Registriere(Eintrag eintrag)
        {
            RoadPrefab klon = null;
            try
            {
                if (!_prefabSystem.TryGetPrefab<RoadPrefab>(eintrag.Original,
                        out var original) || original == null)
                {
                    Fehlschlag(eintrag, "Das Original ist verschwunden.");
                    return;
                }

                /*
                 * FRISCHES PREFAB, KEIN Object.Instantiate - dieselbe Regel
                 * wie bei der Vorflaeche. Instantiate teilt die
                 * ComponentBase-Objekte mit dem Original; `AddComponentFrom`
                 * erzeugt je Bauteil eine neue Instanz samt Rueckverknuepfung
                 * auf den Klon.
                 *
                 * Die einfachen Felder werden dagegen ueber Reflexion
                 * uebernommen. Von Hand abgetippt waeren sie eine stille
                 * Fehlerquelle: `NetPrefab` und `NetGeometryPrefab` bringen
                 * zusammen ein Dutzend mit, und ein vergessenes Feld faellt
                 * erst im Spiel auf.
                 */
                klon = ScriptableObject.CreateInstance<RoadPrefab>();
                klon.name = eintrag.Name;
                KopiereFelder(original, klon);
                foreach (var bauteil in original.components)
                {
                    if (bauteil == null) continue;
                    /*
                     * DAS UI-BAUTEIL BLEIBT DRAUSSEN.
                     *
                     * `UIObject` traegt Menuegruppe, Priorität und Symbol.
                     * Mitkopiert stand unser Klon im STRASSENMENUE des
                     * Nutzers, direkt neben der Gasse - und der Klick auf
                     * die Straßenauswahl riss die Oberflaeche mit.
                     *
                     * Der Klon ist Innenausstattung: er wird von uns gebaut
                     * und soll nirgends auswaehlbar sein.
                     */
                    if (bauteil is UIObject) continue;
                    klon.AddComponentFrom(bauteil);
                }

                LeereDirekteSubObjects(eintrag, klon);

                if (!BlendeQuerschnitteAus(eintrag, original, klon))
                {
                    UnityEngine.Object.Destroy(klon);
                    return;
                }

                if (!_prefabSystem.AddPrefab(klon))
                {
                    Fehlschlag(eintrag, "PrefabSystem.AddPrefab gab false zurueck.");
                    UnityEngine.Object.Destroy(klon);
                    return;
                }

                eintrag.Klon = klon;
                eintrag.KlonEntity = _prefabSystem.GetEntity(klon);
                Mod.log.Info("PLT-Zoningstrasse: '" + eintrag.Name
                    + "' in PrefabUpdate vor PrefabInitializeSystem angemeldet. "
                    + eintrag.Querschnitte + " Querschnitt(e) ausgeblendet, "
                    + "ZoneBlock='"
                    + (klon.m_ZoneBlock != null ? klon.m_ZoneBlock.name : "-")
                    + "', RoadType=" + klon.m_RoadType + ".");
            }
            catch (Exception ausnahme)
            {
                if (klon != null && eintrag.KlonEntity == Entity.Null)
                    UnityEngine.Object.Destroy(klon);
                eintrag.Fehler = true;
                Mod.log.Error(ausnahme, "PLT-Zoningstrasse: '" + eintrag.Name
                    + "' konnte nicht angemeldet werden.");
            }
        }

        /**
         * Kopiert die Felder, die das NETZ beschreiben - und keines darueber
         * hinaus.
         *
         * DIE GRENZE IST `PrefabBase`, UND SIE IST TEUER BEZAHLT. Die erste
         * Fassung lief ueber ALLE Basisklassen und nahm damit die
         * Buchhaltung des Prefabsystems mit: `PrefabBase` erbt von
         * `ComponentBase`, und dort steht `public PrefabBase prefab` - ein
         * Rueckverweis, der am Klon auf ein fremdes Prefab zeigte. Dazu
         * `asset` und `active`.
         *
         * Folge beim Nutzer am 2026-09-02: NullReferenceException in
         * `PrefabSystem.Serialize` -> `PrefabID..ctor` -> `prefab.name`,
         * also ein Absturz BEIM SPEICHERN.
         *
         * Ich hatte die Reflexion damit begruendet, dass ein von Hand
         * abgetipptes Feld leicht vergessen wird. Das stimmt - aber die
         * Vorflaeche kopiert aus gutem Grund nur benannte Felder, und
         * pauschal alles zu nehmen ist strikt schlechter. Alles ab
         * `PrefabBase` gehoert Unity und dem Prefabsystem, nicht uns.
         */
        private static void KopiereFelder(RoadPrefab original, RoadPrefab klon)
        {
            for (var typ = original.GetType();
                 typ != null && typ != typeof(PrefabBase);
                 typ = typ.BaseType)
            {
                var felder = typ.GetFields(BindingFlags.Instance
                    | BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly);
                foreach (var feld in felder)
                {
                    if (feld.IsInitOnly || feld.IsLiteral) continue;
                    feld.SetValue(klon, feld.GetValue(original));
                }
            }
        }

        /**
         * Laesst `NetSubObjects` als Bauteil am Klon und leert nur dessen
         * eigene Liste. Das ist absichtlich nicht dieselbe Abkuerzung wie bei
         * `UIObject`: `NetSubObjects.GetPrefabComponents` fordert den
         * Prefab-Puffer `Game.Prefabs.SubObject` an. Ohne das Bauteil wuerde
         * sich also die GetPrefabComponents-Kette aendern. Fuer den
         * Instanzarchetyp fuegt es dagegen nichts hinzu; dessen
         * `Game.Objects.SubObject` kommt bei Netzknoten und -kanten bereits
         * aus `NetGeometryPrefab`.
         *
         * `AddComponentFrom` hat das Bauteil selbst geklont. Durch das neue
         * leere Array bleibt das gemeinsam benutzte Vanilla-Array unangetastet.
         */
        private static void LeereDirekteSubObjects(Eintrag eintrag,
            RoadPrefab klon)
        {
            var subObjects = klon.GetComponent<NetSubObjects>();
            if (subObjects == null) return;

            eintrag.EntfernteSubObjects = subObjects.m_SubObjects?.Length ?? 0;
            subObjects.m_SubObjects = Array.Empty<NetSubObjectInfo>();
        }

        /**
         * Ersetzt `m_Sections` durch eine eigene Liste, in der jeder Eintrag
         * alle vier Ebenen versteckt. Die Eintraege selbst muessen kopiert
         * werden: `NetSectionInfo` ist eine Klasse, und ein Schreiben in das
         * Original wuerde die Vanilla-Strasse im ganzen Spiel unsichtbar
         * machen.
         */
        private bool BlendeQuerschnitteAus(Eintrag eintrag,
            RoadPrefab original, RoadPrefab klon)
        {
            var quelle = original.m_Sections;
            if (quelle == null || quelle.Length == 0)
            {
                Fehlschlag(eintrag, "'" + original.name + "' hat keine "
                    + "Querschnitte; daraus entstuende eine Strasse der "
                    + "Breite 0.");
                return false;
            }

            var ziel = new NetSectionInfo[quelle.Length];
            for (var i = 0; i < quelle.Length; i++)
            {
                var vorbild = quelle[i];
                if (vorbild == null) continue;
                ziel[i] = new NetSectionInfo
                {
                    m_Section = vorbild.m_Section,
                    m_RequireAll = vorbild.m_RequireAll,
                    m_RequireAny = vorbild.m_RequireAny,
                    m_RequireNone = vorbild.m_RequireNone,
                    m_HiddenLayers = AlleEbenen,
                    m_Invert = vorbild.m_Invert,
                    m_Flip = vorbild.m_Flip,
                    m_Median = vorbild.m_Median,
                    m_HalfLength = vorbild.m_HalfLength,
                    m_Offset = vorbild.m_Offset,
                };
                eintrag.Querschnitte++;
            }

            klon.m_Sections = ziel;
            return true;
        }

        /**
         * NIMMT DER STRASSE DAS GELAENDE WEG - der Grund, warum unser Belag
         * ueber ihr unsichtbar war.
         *
         * Der Nutzer sah nach dem ersten Bau: *"Die Flaechen sind durch die
         * Transparenz der Strasse zum Teil nicht sichtbar."* Es lag nicht an
         * der Transparenz und auch nicht an der Decal-Ebene - die Diagnose
         * im Log nannte es beim Namen:
         *
         *     'Alley', 14 Kurse [zoning], Flags ... FlattenTerrain,
         *     ClipTerrain ... -> FlattenTerrain JA, ClipTerrain JA
         *
         * `ClipTerrain` schneidet das GELAENDE weg. Unsere Flaechen sind
         * Gelaende-Decals; wo kein Gelaende ist, zeichnet auch kein Decal.
         * Deshalb half der `Terrain | Roads`-Klon nicht: es fehlte nicht die
         * Ebene, es fehlte der Untergrund.
         *
         * Am Prefab-OBJEKT ist das nicht abzustellen: `NetInitializeSystem`
         * setzt `FlattenTerrain | ClipTerrain` bei jedem RoadPrefab fest
         * dazu. Die Bits muessen deshalb am fertig initialisierten Klon
         * fallen - hier, nachdem der Archetyp steht und bevor der erste Kurs
         * damit gebaut wird.
         *
         * `FlattenTerrain` faellt gleich mit: unsere uebrigen Netze
         * veraendern das Gelaende nicht, und ein planierender Ring mitten im
         * Parkplatz waere eine Stufe im Gelaende des Nutzers.
         *
         * `BlockZone` BLEIBT. Daran haengen die Zonenbloecke.
         */
        private void EntferneTerraineingriff(Eintrag eintrag)
        {
            var entity = eintrag.KlonEntity;
            if (!EntityManager.HasComponent<NetGeometryData>(entity)) return;
            var geometrie = EntityManager.GetComponentData<NetGeometryData>(entity);
            var vorher = geometrie.m_Flags;
            geometrie.m_Flags &= ~(Game.Net.GeometryFlags.FlattenTerrain
                | Game.Net.GeometryFlags.ClipTerrain);
            if (geometrie.m_Flags == vorher) return;
            EntityManager.SetComponentData(entity, geometrie);
            Mod.log.Info("PLT-Zoningstrasse: Terraineingriff entfernt. Flags "
                + vorher + " -> " + geometrie.m_Flags);
        }

        /**
         * Entfernt die zweite Quelle der Aufbauten ausschliesslich aus den
         * klonspezifischen Kompositions-Prefabs und protokolliert zugleich
         * die Sichtbarkeitskette fuer die Abnutzungsspur.
         *
         * `CompositionSelectSystem` haengt jede solche Komposition an den
         * `NetGeometryComposition`-Puffer genau des RoadPrefabs, aus dem sie
         * entstand. Wir folgen deshalb nur diesem Puffer und veraendern nie
         * ein anhand von Pieces oder Namen geratenes Vanilla-Prefab.
         *
         * Die Messung endet bewusst am Kompositions-Prefab. Ob eine bereits
         * gebaute oder wiederverwendete Lane entgegen diesem Zustand noch
         * `MeshBatch` traegt, ist nur an der Laufzeit-Lane messbar; dafuer
         * waeren zusaetzlich Owner und SubLane der gebauten Kante noetig.
         */
        internal void EntferneKompositionsobjekteUndMesse()
        {
            foreach (var eintrag in _eintraege.Values)
            {
                if (!eintrag.Bereit) continue;
                EntferneKompositionsobjekteUndMesse(eintrag);
            }
        }

        private void EntferneKompositionsobjekteUndMesse(Eintrag eintrag)
        {
            var prefab = eintrag.KlonEntity;
            if (!EntityManager.Exists(prefab)
                || !EntityManager.HasBuffer<NetGeometryComposition>(prefab))
                return;

            var kompositionsverweise = EntityManager
                .GetBuffer<NetGeometryComposition>(prefab);
            /*
             * `NetCompositionSystem` befuellt einen Kompositions-Puffer nur
             * beim einmaligen `Created`-Lauf. Eine neue Variante wird zugleich
             * als neuer Verweis angehaengt. Ohne diese Schranke wuerden zwei
             * dauerhafte Klone in jedem Frame nur fuer eine unveraenderte
             * Diagnose HashSets und Strings anlegen.
             */
            if (eintrag.LetzteKompositionsverweiszahl
                    == kompositionsverweise.Length
                && eintrag.LetzteMessung != null)
                return;
            eintrag.LetzteKompositionsverweiszahl
                = kompositionsverweise.Length;

            var kompositionen = 0;
            var teile = 0;
            var versteckteTeile = 0;
            var kompositionenOhneZustand = 0;
            var diesmalEntfernt = 0;
            var offeneTeile = new List<string>();
            var laneNamen = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < kompositionsverweise.Length; i++)
            {
                var komposition = kompositionsverweise[i].m_Composition;
                if (komposition == Entity.Null
                    || !EntityManager.Exists(komposition)) continue;
                kompositionen++;

                if (EntityManager.HasBuffer<NetCompositionPiece>(komposition))
                {
                    var stuecke = EntityManager
                        .GetBuffer<NetCompositionPiece>(komposition);
                    teile += stuecke.Length;
                    for (var n = 0; n < stuecke.Length; n++)
                    {
                        if ((stuecke[n].m_SectionFlags
                                & NetSectionFlags.Hidden) != 0)
                        {
                            versteckteTeile++;
                            continue;
                        }
                        /*
                         * WELCHES Teil bleibt sichtbar - mit Namen.
                         *
                         * Die Zahl allein ("27 Teile, davon 18 versteckt")
                         * sagt nur, DASS neun uebrig sind. Der Nutzer sieht
                         * Abnutzungsspuren auf einer Strasse, deren sieben
                         * Querschnitte alle vier Ebenen verstecken - die
                         * neun muessen also aus einer anderen Quelle
                         * stammen, etwa aus Knotenkompositionen. Ohne den
                         * Namen bleibt das eine Vermutung.
                         */
                        var kennung = Prefabname(stuecke[n].m_Piece)
                            + " [" + stuecke[n].m_PieceFlags + "]";
                        if (!offeneTeile.Contains(kennung))
                            offeneTeile.Add(kennung);
                    }
                }

                var zustandBekannt = EntityManager
                    .HasComponent<NetCompositionData>(komposition);
                var versteckt = zustandBekannt
                    && (EntityManager.GetComponentData<NetCompositionData>(
                            komposition).m_State & CompositionState.Hidden) != 0;
                if (!zustandBekannt) kompositionenOhneZustand++;

                if (zustandBekannt && !versteckt
                    && EntityManager.HasBuffer<NetCompositionLane>(komposition))
                {
                    var lanes = EntityManager
                        .GetBuffer<NetCompositionLane>(komposition);
                    for (var n = 0; n < lanes.Length; n++)
                        laneNamen.Add(Prefabname(lanes[n].m_Lane));
                }

                if (!EntityManager.HasBuffer<NetCompositionObject>(komposition))
                    continue;
                var objekte = EntityManager
                    .GetBuffer<NetCompositionObject>(komposition);
                diesmalEntfernt += objekte.Length;
                objekte.Clear();
            }

            eintrag.EntfernteKompositionsobjekte += diesmalEntfernt;
            if (!eintrag.AufbautenGeloggt || diesmalEntfernt != 0)
            {
                eintrag.AufbautenGeloggt = true;
                Mod.log.Info("PLT-Zoningstrasse-Aufbauten: '" + eintrag.Name
                    + "': SubObjects entfernt=" + eintrag.EntfernteSubObjects
                    + ", Kompositionsobjekte entfernt="
                    + eintrag.EntfernteKompositionsobjekte
                    + " (dieser Lauf=" + diesmalEntfernt + ").");
            }

            var sectionen = eintrag.Klon?.m_Sections;
            var sectionAnzahl = sectionen?.Length ?? 0;
            var ganzVersteckteSectionen = 0;
            if (sectionen != null)
            {
                for (var i = 0; i < sectionen.Length; i++)
                {
                    var section = sectionen[i];
                    if (section != null
                        && (section.m_HiddenLayers & AlleEbenen) == AlleEbenen)
                        ganzVersteckteSectionen++;
                }
            }

            var namen = new List<string>(laneNamen);
            namen.Sort(StringComparer.Ordinal);
            string laneBefund;
            if (kompositionen == 0)
            {
                laneBefund = "noch nicht messbar (0 Kompositionen)";
            }
            else if (laneNamen.Count == 0)
            {
                laneBefund = "0";
            }
            else
            {
                laneBefund = laneNamen.Count + " ["
                    + string.Join(", ", namen) + "]";
            }

            var messung = "Sections=" + sectionAnzahl
                + ", alle vier Ebenen versteckt=" + ganzVersteckteSectionen
                + "; Kompositionen=" + kompositionen
                + ", Teile=" + teile + ", davon Hidden=" + versteckteTeile
                + (offeneTeile.Count == 0
                    ? string.Empty
                    : "; SICHTBARE TEILE: " + string.Join(", ", offeneTeile))
                + "; nicht versteckte Lane-Prefabs=" + laneBefund
                + (kompositionenOhneZustand == 0 ? ""
                    : "; Kompositionen ohne Zustand="
                        + kompositionenOhneZustand)
                + ".";
            if (!string.Equals(eintrag.LetzteMessung, messung,
                    StringComparison.Ordinal))
            {
                eintrag.LetzteMessung = messung;
                Mod.log.Info("PLT-Zoningstrasse-Messung: '" + eintrag.Name
                    + "': " + messung);
            }
        }

        private string Prefabname(Entity entity)
        {
            if (entity != Entity.Null
                && _prefabSystem.TryGetPrefab<PrefabBase>(entity,
                    out var prefab) && prefab != null)
                return "'" + prefab.name + "'";
            return "Entity " + entity;
        }

        private bool Pruefe(Eintrag eintrag, out string zustand)
        {
            var entity = eintrag.KlonEntity;
            if (!EntityManager.Exists(entity))
            {
                zustand = "'" + eintrag.Name + "': Prefab-Entity existiert nicht.";
                return false;
            }

            var netData = EntityManager.HasComponent<NetData>(entity);
            var roadData = EntityManager.HasComponent<RoadData>(entity);
            var geometrie = EntityManager.HasComponent<NetGeometryData>(entity);
            var block = roadData
                && EntityManager.GetComponentData<RoadData>(entity)
                    .m_ZoneBlockPrefab != Entity.Null;

            zustand = "NetData=" + JaNein(netData)
                + ", RoadData=" + JaNein(roadData)
                + ", NetGeometryData=" + JaNein(geometrie)
                + ", ZoneBlockPrefab=" + JaNein(block) + ".";
            return netData && roadData && geometrie && block;
        }

        private void Fehlschlag(Eintrag eintrag, string grund)
        {
            eintrag.Fehler = true;
            Mod.log.Error("PLT-Zoningstrasse: '" + eintrag.Name
                + "' fehlgeschlagen. " + grund);
        }

        private static string JaNein(bool wert) => wert ? "ja" : "nein";
    }

    /**
     * Der Lauf ist ein Teil der Zoningstrassen-Verwaltung und steht nur
     * deshalb als zweites System in derselben Datei, weil eine
     * PrefabUpdate-Instanz nicht zugleich hinter `NetCompositionSystem` in
     * Modification4 laufen kann. Die Registrierung erfolgt oben aus dem
     * Hauptsystem; Mod.cs muss dafuer nicht erweitert werden.
     */
    public sealed partial class ParkingLotZoningRoadObjectCleanupSystem
        : GameSystemBase
    {
        private ParkingLotZoningRoadPrefabSystem _zoningRoads;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _zoningRoads = World.GetOrCreateSystemManaged<
                ParkingLotZoningRoadPrefabSystem>();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            _zoningRoads.EntferneKompositionsobjekteUndMesse();
        }
    }
}
