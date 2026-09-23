using System;
using System.Collections.Generic;
using System.Reflection;
using Game;
using Game.Common;
using Game.Prefabs;
using Unity.Mathematics;
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
    /**
     * Wozu ein Klon gebraucht wird - und damit, ob er einen Zonenblock
     * bekommt.
     */
    public enum Strassenklonart
    {
        /** Die Zoningstrasse im Parkplatz. Braucht den Zonenblock. */
        Zoning,
        /**
         * Die Zufahrtsgasse zwischen Stadtstrasse und Polygonrand. Sie ist
         * nur da, um den Bordstein zu oeffnen, und darf kein Bauland
         * erzeugen.
         */
        Zufahrtsgasse,
        /**
         * Dieselbe Gasse, aber als EINBAHN.
         *
         * Gleiches Vorbild wie `Zufahrtsgasse` - die Vanilla-"Alley" -,
         * nur fahren beide Spuren in dieselbe Richtung. Sie ist deshalb
         * eine eigene Klonart und nicht ein eigenes Vorbild: "Alley
         * Oneway" waere das naheliegende Vorbild gewesen, bringt aber
         * links und rechts je 2,5 m Parkstreifen mit, auf denen Fahrzeuge
         * mitten in der Zufahrt stehen.
         */
        ZufahrtsgasseEinbahn,
    }

    public sealed partial class ParkingLotZoningRoadPrefabSystem : GameSystemBase
    {
        private sealed class Eintrag
        {
            public Strassenklonart Art;
            public Entity Original;
            public Entity KlonEntity;
            public RoadPrefab Klon;
            public string Name;
            public int Querschnitte;
            /** Wie viele Sektionen eine Parkspur tragen. Nur gemessen. */
            public int Parkspuren;
            /** Wie viele Fahrsektionen auf die Gegenrichtung gedreht wurden. */
            public int GleichgerichteteSpuren;
            /**
             * Summe der Sektionsbreiten, die eine FAHRSPUR tragen.
             *
             * Nicht die Gesamtbreite: die enthaelt Schulter und Seitenteile,
             * ueber denen kein Auto faehrt. Der Belag der Gasse richtet sich
             * nach diesem Mass, damit er bei jeder Gassenbreite gleich weit
             * innerhalb der Fahrbahn endet.
             */
            public float Fahrbahnbreite;
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
        private static readonly (string Name, Strassenklonart Art)[] StandardStrassen =
        {
            ("Alley", Strassenklonart.Zoning),
            ("Gravel Road", Strassenklonart.Zoning),
            /*
             * ZWEI VORBILDER FUER GASSEN: zweispurig aus "Alley", einspurig
             * aus "Alley Oneway". Beide entstehen beim Spielstart, damit der
             * Nutzer zwischen den Zufahrtsarten wechseln kann, ohne dass
             * erst ein Prefab angelegt werden muss - `AddPrefab` und
             * Benutzen im selben Frame ist der Weg in einen nativen
             * Absturz.
             */
            ("Alley", Strassenklonart.Zufahrtsgasse),
            ("Alley", Strassenklonart.ZufahrtsgasseEinbahn),
        };

        /*
         * Der Schluessel ist Vorbild UND Sorte: aus derselben Gasse entstehen
         * zwei verschiedene Klone, und der eine darf den anderen nicht
         * verdraengen.
         */
        private readonly Dictionary<(Entity Vorbild, Strassenklonart Art), Eintrag>
            _eintraege
            = new Dictionary<(Entity, Strassenklonart), Eintrag>();
        private readonly HashSet<(string Name, Strassenklonart Art)> _gesaet
            = new HashSet<(string, Strassenklonart)>();
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
                    var eintrag = StandardStrassen[n];
                    if (_gesaet.Contains(eintrag)) continue;
                    if (!string.Equals(vorbild.name, eintrag.Name,
                            StringComparison.Ordinal)) continue;
                    _gesaet.Add(eintrag);
                    FordereAn(kandidaten[i], eintrag.Art, out _, out _);
                }
            }
        }

        /**
         * Das Werkzeug stellt nur die Anforderung; angemeldet wird in
         * OnUpdate und damit in der richtigen Phase. Rueckgabe ist
         * `Entity.Null`, solange der Klon nicht fertig ist.
         */
        public Entity FordereAn(Entity original, Strassenklonart art,
            out bool fehlgeschlagen, out bool aufgegeben)
        {
            fehlgeschlagen = false;
            aufgegeben = false;
            if (original == Entity.Null || !EntityManager.Exists(original))
                return Entity.Null;

            var schluessel = (original, art);
            if (!_eintraege.TryGetValue(schluessel, out var eintrag))
            {
                if (!_prefabSystem.TryGetPrefab<RoadPrefab>(original,
                        out var vorbild) || vorbild == null)
                {
                    Mod.log.Warn("PLT-Zoningstrasse: Das angeforderte Prefab "
                        + "ist kein RoadPrefab; kein Klon moeglich.");
                    fehlgeschlagen = true;
                    return Entity.Null;
                }
                // Nur die Zoningstrasse braucht den Block. Die Zufahrtsgasse
                // wirft ihn gleich wieder weg und kaeme mit dieser Schranke
                // gar nicht erst durch.
                if (art == Strassenklonart.Zoning && vorbild.m_ZoneBlock == null)
                {
                    Mod.log.Warn("PLT-Zoningstrasse: '" + vorbild.name
                        + "' hat keinen ZoneBlock; daran wuerde nichts wachsen.");
                    fehlgeschlagen = true;
                    return Entity.Null;
                }

                eintrag = new Eintrag
                {
                    Art = art,
                    Original = original,
                    KlonEntity = Entity.Null,
                    Name = Klonname(art) + " (" + vorbild.name + ")",
                };
                _eintraege.Add(schluessel, eintrag);
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
                if (eintrag.Fehler) continue;
                if (eintrag.Bereit)
                {
                    /*
                     * NACHSCHAU STATT EINMALAKTION.
                     *
                     * `NetInitializeSystem` (PrefabUpdate, direkt hinter
                     * `PrefabInitializeSystem`) setzt `ClipTerrain` und
                     * `FlattenTerrain` per |= an jedem Strassenprefab, das
                     * in diesem Frame `Created` traegt. Wir laufen VOR
                     * beiden. Faellt unsere Korrektur zufaellig auf genau
                     * diesen einen Frame, wird sie noch im selben Frame
                     * ueberschrieben - und wer sie nur einmal ausfuehrt,
                     * merkt das nie.
                     *
                     * Die Methode schreibt und meldet nur, wenn die Flagge
                     * wirklich wieder dasteht. Erscheint ihre Logzeile ein
                     * zweites Mal, ist genau dieser Fall eingetreten.
                     */
                    EntferneTerraineingriff(eintrag);
                    continue;
                }

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

                /*
                 * DER EINE UNTERSCHIED ZWISCHEN DEN BEIDEN SORTEN.
                 *
                 * Die Gasse liegt zwischen Stadtstrasse und Polygonrand.
                 * Mit Zonenblock entstuende genau dort Bauland - in dem
                 * Streifen, den der Nutzer als Zufahrt gezeichnet hat.
                 */
                if (IstGasse(eintrag.Art))
                    klon.m_ZoneBlock = null;

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
                MeldeQuerschnitt(eintrag, original);
                SchreibeMaterialkatalog();
                SchreibeFlaechenabzug();
                Mod.log.Info("PLT-Zoningstrasse: '" + eintrag.Name
                    + "' in PrefabUpdate vor PrefabInitializeSystem angemeldet. "
                    + eintrag.Querschnitte + " Querschnitt(e), davon "
                    + eintrag.Parkspuren + " mit Parkspur, "
                    + eintrag.GleichgerichteteSpuren
                    + " Spur(en) gleichgerichtet, Fahrbahn "
                    + eintrag.Fahrbahnbreite.ToString("F2") + " m, "
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

            var ziel = new List<NetSectionInfo>(quelle.Length);
            for (var i = 0; i < quelle.Length; i++)
            {
                var vorbild = quelle[i];
                if (vorbild == null) continue;

                /*
                 * KEINE SEKTION WIRD ENTFERNT.
                 *
                 * Der Versuch vom 2026-09-18, die Parkspur-Sektionen
                 * herauszuwerfen, ist im Spiel gescheitert: 'Alley' hatte
                 * danach nur noch 5 statt 7 Sektionen, weil die beiden
                 * `Alley Shoulder 1` eine Parkspur tragen, ohne dass ihr
                 * Name das verraet. Ohne Schultern hat kein Fahrzeug mehr
                 * eine der Gassen benutzt, und in der Flaeche standen
                 * Kerben.
                 *
                 * GEMESSEN WIRD TROTZDEM: welche Sektion eine Parkspur
                 * traegt und wie breit die Fahrbahn ist, ist die Grundlage
                 * fuer den richtigen Weg - `CompositionFlags.Side.ParkingSpaces`.
                 */
                var sektion = vorbild.m_Section;
                if (SektionTraegt<Game.Prefabs.ParkingLane>(sektion))
                    eintrag.Parkspuren++;

                var fahrsektion = SektionTraegt<Game.Prefabs.CarLane>(sektion);
                if (fahrsektion) eintrag.Fahrbahnbreite += Sektionsbreite(sektion);

                /*
                 * BEIDE SPUREN IN DIESELBE RICHTUNG.
                 *
                 * `NetCompositionHelpers` setzt `LaneFlags.Invert` genau
                 * dann, wenn `m_Invert` und `m_Flip` VERSCHIEDEN sind. Die
                 * linke Fahrsektion der Alley traegt `m_Invert`; kommt
                 * `m_Flip` dazu, sind beide gleich und die Spur faehrt
                 * vorwaerts. Die Position bleibt gespiegelt, denn das macht
                 * `m_Invert` unabhaengig davon.
                 *
                 * Nur Fahrsektionen. Die Schulter traegt die Fussgaenger-
                 * und die Haltespur; die haben mit der Fahrtrichtung der
                 * Autos nichts zu tun.
                 */
                var einbahn = eintrag.Art == Strassenklonart.ZufahrtsgasseEinbahn
                              && fahrsektion && vorbild.m_Invert;
                if (einbahn) eintrag.GleichgerichteteSpuren++;

                ziel.Add(new NetSectionInfo
                {
                    m_Section = sektion,
                    m_RequireAll = vorbild.m_RequireAll,
                    m_RequireAny = vorbild.m_RequireAny,
                    m_RequireNone = vorbild.m_RequireNone,
                    /*
                     * DIE ZUFAHRTSGASSE BLEIBT SICHTBAR.
                     *
                     * Belegt am 2026-09-18 in einem Versuch mit binaerem
                     * Ausgang: ausgeblendet steht ein Grasskeil in der
                     * Einmuendung, sichtbar nicht. Die Einmuendung gehoert
                     * zur Komposition dieser Kante - blendet man jedes
                     * Bauteil aus, zeichnet sie niemand.
                     *
                     * Die Zoningstrasse bleibt unsichtbar: sie liegt mitten
                     * im Parkplatz unter unserem eigenen Belag und hat gar
                     * keine Einmuendung.
                     */
                    /*
                     * DIE ZUFAHRTSGASSE BLEIBT VOLLSTAENDIG SICHTBAR.
                     *
                     * Zwei Versuche, zwei Loecher: alles auszublenden brachte
                     * einen Grasskeil auf ganzer Laenge der Einmuendung, und
                     * am 2026-09-18 auch nur die Sektion "Alley Median 0"
                     * auszublenden riss ein Loch mitten in die Kreuzung. Das
                     * Kreuzungsstueck darin IST die Fuellung der Einmuendung.
                     *
                     * Der helle Halbkreis, den der Nutzer sieht, ueberlebte
                     * beide Versuche - er kommt also weder von der
                     * Sichtbarkeit dieser Sektion noch von der
                     * Zeichenprioritaet. Was er ist, ist offen; geraten wird
                     * daran nicht mehr.
                     */
                    m_HiddenLayers = IstGasse(eintrag.Art)
                        ? vorbild.m_HiddenLayers
                        : AlleEbenen,
                    m_Invert = vorbild.m_Invert,
                    m_Flip = einbahn ? true : vorbild.m_Flip,
                    m_Median = vorbild.m_Median,
                    m_HalfLength = vorbild.m_HalfLength,
                    m_Offset = vorbild.m_Offset,
                });
                eintrag.Querschnitte++;
            }

            klon.m_Sections = ziel.ToArray();
            return true;
        }

        /**
         * Traegt irgendein Bauteil dieser Sektion eine Spur mit `T`?
         *
         * Der Weg ist `NetSectionPrefab` -> `m_Pieces[].m_Piece` ->
         * `NetPieceLanes.m_Lanes[].m_Lane` -> die gesuchte Komponente am
         * `NetLanePrefab`. Untersektionen zaehlen mit; die Tiefe ist
         * begrenzt, weil nichts garantiert, dass sie sich nicht im Kreis
         * verweisen.
         */
        private static bool SektionTraegt<T>(NetSectionPrefab sektion,
                                             int tiefe = 0)
            where T : ComponentBase
        {
            if (sektion == null || tiefe > 4) return false;

            var stuecke = sektion.m_Pieces;
            if (stuecke != null)
            {
                for (var i = 0; i < stuecke.Length; i++)
                {
                    var spuren = stuecke[i]?.m_Piece?
                        .GetComponent<NetPieceLanes>()?.m_Lanes;
                    if (spuren == null) continue;
                    for (var j = 0; j < spuren.Length; j++)
                        if (spuren[j]?.m_Lane?.GetComponent<T>() != null)
                            return true;
                }
            }

            var unter = sektion.m_SubSections;
            if (unter != null)
                for (var i = 0; i < unter.Length; i++)
                    if (SektionTraegt<T>(unter[i]?.m_Section, tiefe + 1))
                        return true;

            return false;
        }

        /**
         * Die Breite einer Sektion: das breiteste ihrer Bauteile.
         *
         * Eine Sektion listet dasselbe Stueck in mehreren Fassungen - flach,
         * erhoeht, im Tunnel. Sie alle zu addieren ergaebe ein Vielfaches
         * der wahren Breite; das breiteste ist das Mass, das die Strasse
         * belegt.
         */
        private static float Sektionsbreite(NetSectionPrefab sektion)
        {
            if (sektion == null) return 0f;
            var breite = 0f;
            var stuecke = sektion.m_Pieces;
            if (stuecke != null)
                for (var i = 0; i < stuecke.Length; i++)
                {
                    var teil = stuecke[i]?.m_Piece;
                    if (teil != null) breite = math.max(breite, teil.m_Width);
                }
            var unter = sektion.m_SubSections;
            if (unter != null)
                for (var i = 0; i < unter.Length; i++)
                    breite = math.max(breite, Sektionsbreite(unter[i]?.m_Section));
            return breite;
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
         * `FlattenTerrain` faellt bei der ZONINGSTRASSE gleich mit: die
         * liegt mitten im Parkplatz, unsere uebrigen Netze veraendern das
         * Gelaende nicht, und ein planierender Ring dort waere eine Stufe im
         * Gelaende des Nutzers.
         *
         * BEI DER ZUFAHRTSGASSE BLEIBT ES DRAN, und das ist der Unterschied
         * zwischen den beiden Sorten. Die Gasse trifft auf eine Stadtstrasse
         * und bildet eine echte Kreuzung; eine Kreuzung ohne Planierung sieht
         * aus wie die Bilder des Nutzers vom 2026-09-18: eine Beule im
         * Boden, ein hochgerollter Bordstein und rohes Gelaende mitten in der
         * Kreuzungsflaeche. Die von Hand gesetzte Gasse an derselben Strasse
         * hat nichts davon - sie bringt beide Flaggen mit.
         *
         * `ClipTerrain` bleibt trotzdem auch bei ihr aus. Sie ist
         * absichtlich unsichtbar; schnitte CS2 auf ihrer ganzen Laenge das
         * Gelaende weg, bliebe ein Loch, das unser Belag nicht fuellen kann,
         * weil er selbst ein Gelaendedecal ist.
         *
         * `BlockZone` BLEIBT. Daran haengen die Zonenbloecke.
         */
        /**
         * NUR DIE MELDUNG IST EINMALIG, NICHT DIE HEILUNG.
         *
         * Der erste Entwurf merkte sich "schon erledigt" und hoerte danach
         * auf hinzuschauen. Das waere derselbe Fehler gewesen, der den Fix
         * vom 2026-09-20 hat bruechig wirken lassen: `NetGeometryPrefab`
         * setzt die Flags beim Initialisieren aus den Bauteilen, und ein
         * Prefab wird nicht nur einmal initialisiert. Kommt `ClipTerrain`
         * zurueck, muss es wieder weg - sonst ist es genau das
         * "weg, da, weg, da", das der Nutzer beschrieben hat.
         *
         * Geprueft wird deshalb in jedem Zyklus; das ist ein
         * Komponentenlesen. Geschrieben und gemeldet wird nur, wenn die
         * Flagge wirklich dasteht.
         */
        private bool _vanillaGasseGemeldet;
        private Entity _vanillaGasse = Entity.Null;
        private int _vanillaGasseAbgeraeumt;

        /**
         * NIMMT DER VANILLA-GASSE DEN GELAENDESCHNITT.
         *
         * Das hier fasst ein SPIEL-ASSET an, nicht unseren Klon. Der Nutzer
         * hat das am 2026-09-22 ausdruecklich so entschieden, nachdem der
         * Befund klar war.
         *
         * DER BEFUND: die Alley bringt an ihren Enden eigene
         * Intersection-Flaechen mit. `ClipTerrain` schneidet darunter einen
         * Terrainkeil heraus - und weil die Kappen ueber diesem Keil liegen,
         * fehlt ihnen der Untergrund. Sichtbar als helle, durchscheinende
         * Viertelkreise an beiden Enden. Fuer UNSEREN Klon hat Codex die
         * Flagge am 2026-09-20 entfernt (`EntferneTerraineingriff`); die
         * Vanilla-Gasse behielt sie und zeigt den Fehler weiter, auch ohne
         * dass ein Parkplatz in der Naehe ist.
         *
         * WAS ES KOSTET: jede Gasse in dieser Stadt schneidet sich nicht
         * mehr ins Gelaende. An einer Steigung kann sie dadurch eher
         * aufliegen statt sich einzugraben. `FlattenTerrain` bleibt, die
         * Gasse planiert also weiterhin unter sich.
         *
         * WAS ES NICHT IST: dauerhaft. Prefabs stehen nicht im Spielstand;
         * ohne den Mod ist die Flagge beim naechsten Start wieder da. Es
         * bleibt also nichts zurueck, was jemand spaeter nicht erklaeren
         * koennte.
         */
        /**
         * AUS SEIT DEM 2026-09-23 - UND ZWAR GEMESSEN.
         *
         * Der Eingriff sollte die hellen Viertelkreise an den Enden der
         * Vanilla-Gasse beheben. Er hat zwei Voraussetzungen, und beide
         * treffen nicht zu:
         *
         * 1. UNSERE Gasse benutzt dieses Prefab gar nicht.
         *    `ParkingLotNetBuilder.VerwendeVanillaAlley` steht auf false,
         *    gebaut wird mit unserem Klon. Der Eingriff konnte unseren
         *    Parkplaetzen also nie helfen.
         * 2. Er trifft dafuer JEDE von Hand gesetzte Gasse der Stadt.
         *
         * Was der Nutzer am 2026-09-23 um 23:15 fotografiert hat, ist genau
         * das: eine von Hand gesetzte Gasse mit einem Riss im Belag und
         * Gras darunter. Der Log derselben Sitzung schliesst die andere
         * Erklaerung aus -
         *
         *     23:05:56  ClipTerrain am SPIEL-PREFAB 'Alley' entfernt.
         *               0 vorhandene Alley-Kante(n) fuer Terrain neu
         *               markiert.
         *
         * - null vorhandene Kanten heisst: er hat die Gasse DANACH gesetzt.
         * Sie bekam `Created` und eine frische Terrainberechnung mit
         * sauberem Prefab. Ein veralteter Cache kann es also nicht sein.
         * Uebrig bleibt der fehlende Schnitt selbst.
         *
         * Steht der Schalter auf true, wird wieder eingegriffen. Dann sind
         * die Viertelkreise das Thema - und die gehoeren vor dem naechsten
         * Versuch gemessen, nicht erinnert.
         */
        private static readonly bool GasseOhneGelaendeschnitt = false;

        internal void HeileVanillaGasse()
        {
            if (!GasseOhneGelaendeschnitt)
            {
                if (_vanillaGasseGemeldet) return;
                _vanillaGasseGemeldet = true;
                Mod.log.Info("PLT-Vanillagasse: das Spiel-Prefab 'Alley' "
                    + "wird NICHT angefasst (GasseOhneGelaendeschnitt = "
                    + "false). Von Hand gesetzte Gassen schneiden wieder "
                    + "ins Gelaende wie im Grundspiel.");
                return;
            }
            if (_vanillaGasse == Entity.Null)
            {
                const string name = "Alley";
                if (!_prefabSystem.TryGetPrefab(
                        new PrefabID(nameof(RoadPrefab), name), out var basis)
                    || basis == null) return;
                var gefunden = _prefabSystem.GetEntity(basis);
                if (gefunden == Entity.Null
                    || !EntityManager.HasComponent<NetGeometryData>(gefunden))
                    return;
                _vanillaGasse = gefunden;
            }

            if (!EntityManager.Exists(_vanillaGasse)
                || !EntityManager.HasComponent<NetGeometryData>(_vanillaGasse))
            {
                // Prefab-Entity ist weg (Weltwechsel). Beim naechsten Zyklus
                // neu suchen, statt auf eine tote Entity zu schreiben.
                _vanillaGasse = Entity.Null;
                _vanillaGasseGemeldet = false;
                return;
            }

            var geometrie = EntityManager
                .GetComponentData<NetGeometryData>(_vanillaGasse);
            var vorher = geometrie.m_Flags;
            // Flags 0 kam im Nutzerlog vor der Initialisierung vor. Erst
            // nach NetInitialize und mit Breite > 0 ist dies ein Befund.
            if (vorher == 0 || geometrie.m_DefaultWidth <= 0f) return;
            if ((vorher & Game.Net.GeometryFlags.ClipTerrain) == 0)
            {
                if (!_vanillaGasseGemeldet)
                {
                    _vanillaGasseGemeldet = true;
                    Mod.log.Info("PLT-Vanillagasse: 'Alley' hat kein "
                        + "ClipTerrain - nichts zu tun. Flags " + vorher);
                }
                return;
            }

            geometrie.m_Flags &= ~Game.Net.GeometryFlags.ClipTerrain;
            EntityManager.SetComponentData(_vanillaGasse, geometrie);
            var neuBerechnet = FordereVanillaGassenTerrainNeu();

            if (_vanillaGasseGemeldet)
            {
                // ZWEITE UND JEDE WEITERE MELDUNG IST EIN BEFUND.
                // Nach NetInitialize darf die Flagge nicht erneut erscheinen.
                // Ein solcher Lauf ist ein neuer Befund und wird gezaehlt.
                _vanillaGasseAbgeraeumt++;
                Mod.log.Warn("PLT-Vanillagasse: ClipTerrain war WIEDER da "
                    + "und wurde erneut entfernt (" + _vanillaGasseAbgeraeumt
                    + ". Wiederholung). " + neuBerechnet
                    + " vorhandene Alley-Kante(n) fuer Terrain neu markiert.");
                return;
            }

            _vanillaGasseGemeldet = true;
            Mod.log.Info("PLT-Vanillagasse: ClipTerrain am SPIEL-PREFAB "
                + "'Alley' entfernt, damit die Endkappen ihren Untergrund "
                + "behalten. FlattenTerrain bleibt. Flags " + vorher + " -> "
                + geometrie.m_Flags + ". " + neuBerechnet
                + " vorhandene Alley-Kante(n) fuer Terrain neu markiert. "
                + "Gilt nur zur Laufzeit; ohne den Mod "
                + "ist die Flagge beim naechsten Start wieder da.");
        }

        /**
         * Prefab-Aenderungen wecken TerrainSystem nicht. Dessen
         * m_RoadsChanged fragt nur Created/Updated/Deleted auf Netzen ab;
         * im Nutzerlog blieb das Prefabbit nach 1 Wiederholung sauber.
         * Nur bei einer wirklichen Flag-Aenderung werden daher die schon
         * vorhandenen Alley-Kanten einmalig zur Neuberechnung markiert.
         */
        private int FordereVanillaGassenTerrainNeu()
        {
            var query = GetEntityQuery(
                ComponentType.ReadOnly<Game.Net.EdgeGeometry>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Game.Tools.Temp>());
            var anzahl = 0;
            using var kanten = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (var i = 0; i < kanten.Length; i++)
            {
                var kante = kanten[i];
                if (EntityManager.GetComponentData<PrefabRef>(kante).m_Prefab
                    != _vanillaGasse) continue;
                if (!EntityManager.HasComponent<Updated>(kante))
                    EntityManager.AddComponent<Updated>(kante);
                anzahl++;
            }
            return anzahl;
        }

        private void EntferneTerraineingriff(Eintrag eintrag)
        {
            var entity = eintrag.KlonEntity;
            if (!EntityManager.HasComponent<NetGeometryData>(entity)) return;
            var geometrie = EntityManager.GetComponentData<NetGeometryData>(entity);
            var vorher = geometrie.m_Flags;
            /*
             * NUR DIE ZONINGSTRASSE VERLIERT DEN GELAENDEEINGRIFF.
             *
             * Sie liegt unter unserem Belag, und unsere Flaechen sind
             * Gelaendedecals - `ClipTerrain` macht sie unsichtbar, weil der
             * Untergrund fehlt, und `FlattenTerrain` zoege eine Stufe durch
             * den Parkplatz.
             *
             * Die Zufahrtsgasse ist seit dem 2026-09-18 eine sichtbare
             * Strasse OHNE eigenen Belag darueber. Damit gilt fuer sie
             * nichts von beidem: sie darf planieren und schneiden wie jede
             * andere Strasse, und genau das braucht eine saubere
             * Einmuendung.
             */
            if (IstGasse(eintrag.Art))
            {
                /*
                 * DIE GASSE SCHNEIDET WIEDER WIE JEDE VANILLA-STRASSE.
                 *
                 * Vom 2026-09-20 bis 24 wurde ihr `ClipTerrain` genommen, um
                 * durchsichtige Dreiecke an den Enden loszuwerden. Die Folge
                 * meldete der Nutzer am 2026-09-24: Gelaende drueckt durch den
                 * Belag, sichtbar als Luecken und Hoehenlinien auf der Gasse.
                 * Dasselbe Bild hatte die handgesetzte Vanilla-Gasse, der wir
                 * denselben Schnitt genommen hatten (Riss mit Gras).
                 *
                 * Kommen die Dreiecke zurueck, wird ihre Ursache gesucht -
                 * nicht wieder der Schnitt genommen. Moeglich ist, dass sie am
                 * ungenauen Andocken hingen, das seit demselben Tag behoben ist.
                 */
                var soll = Game.Net.GeometryFlags.FlattenTerrain
                    | Game.Net.GeometryFlags.ClipTerrain;
                geometrie.m_Flags |= soll;
                if (geometrie.m_Flags == vorher) return;
                EntityManager.SetComponentData(entity, geometrie);
                Mod.log.Info("PLT-Zufahrtsgasse: FlattenTerrain und "
                    + "ClipTerrain aktiv wie bei Vanilla-Strassen. Flags "
                    + vorher + " -> " + geometrie.m_Flags);
                return;
            }
            var weg = Game.Net.GeometryFlags.FlattenTerrain
                | Game.Net.GeometryFlags.ClipTerrain;
            geometrie.m_Flags &= ~weg;
            if (geometrie.m_Flags == vorher) return;
            EntityManager.SetComponentData(entity, geometrie);
            Mod.log.Info("PLT-Zoningstrasse: Terraineingriff entfernt fuer "
                + Klonname(eintrag.Art) + ". Entfernt: " + weg
                + ". Flags " + vorher + " -> " + geometrie.m_Flags);
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
        /**
         * Die gemessene Fahrbahnbreite eines fertigen Klons, in Metern.
         *
         * 0, wenn dieser Klon nicht von uns stammt oder noch nicht fertig
         * ist. Der Aufrufer behaelt dann sein eigenes Mass - eine etwas zu
         * schmale Vorflaeche ist besser als eine geratene.
         */
        /**
         * Alle fertigen Klon-Prefabs, in eine vorhandene Menge hinein.
         *
         * Gedacht fuer die Namenswache: die muss wissen, welche Strassen
         * UNSERE sind, und zwar ohne den `SubNet`-Puffer eines Traegers -
         * der ueberlebt das Laden nicht (gemessen 109 -> 0).
         *
         * Gefragt wird die eigene Liste, nicht der Prefabname. Namen sind
         * Zeichenketten, die Liste ist die Quelle.
         */
        internal void SammleKlonprefabs(HashSet<Entity> ziel)
        {
            if (ziel == null) return;
            foreach (var eintrag in _eintraege.Values)
            {
                if (!eintrag.Bereit) continue;
                if (eintrag.KlonEntity == Entity.Null) continue;
                ziel.Add(eintrag.KlonEntity);
            }
        }

        internal float FahrbahnbreiteVon(Entity klon)
        {
            if (klon == Entity.Null) return 0f;
            foreach (var eintrag in _eintraege.Values)
                if (eintrag.KlonEntity == klon)
                    return eintrag.Fahrbahnbreite;
            return 0f;
        }

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
            // Bei der Zufahrtsgasse ist der FEHLENDE Block die Abnahme.
            var blockStimmt = eintrag.Art == Strassenklonart.Zoning
                ? block : !block;

            zustand = "NetData=" + JaNein(netData)
                + ", RoadData=" + JaNein(roadData)
                + ", NetGeometryData=" + JaNein(geometrie)
                + ", ZoneBlockPrefab=" + JaNein(block)
                + " (erwartet " + JaNein(eintrag.Art == Strassenklonart.Zoning)
                + ").";
            return netData && roadData && geometrie && blockStimmt;
        }

        private void Fehlschlag(Eintrag eintrag, string grund)
        {
            eintrag.Fehler = true;
            Mod.log.Error("PLT-Zoningstrasse: '" + eintrag.Name
                + "' fehlgeschlagen. " + grund);
        }

        private static string JaNein(bool wert) => wert ? "ja" : "nein";

        /**
         * DIE GANZE QUERSCHNITTSKETTE INS LOG, EINMAL JE KLON.
         *
         * Schreibt nichts, aendert nichts - sie beantwortet nur die Frage,
         * die man sonst raet: aus welchen Bauteilen besteht diese Strasse,
         * und welches Material traegt jedes davon?
         *
         *   RoadPrefab.m_Sections[] -> NetSectionPrefab
         *   NetSectionPrefab.m_Pieces[] -> NetPiecePrefab : RenderPrefab
         *   RenderPrefab.materialCount / GetSurfaceAsset(i)
         *
         * Daran haengt die Frage des Nutzers vom 2026-09-18, ob wir der
         * Gasse das Aussehen unseres Belags geben koennen: die Zahl der
         * Bauteile ist der Preis (jedes einzeln zu klonen), die
         * Materialnamen sind die Auswahl.
         *
         * NUR FUER DIE GASSE. Die Zoningstrasse ist unsichtbar, ihr
         * Aussehen ist bedeutungslos.
         */
        private void MeldeQuerschnitt(Eintrag eintrag, RoadPrefab original)
        {
            if (!IstGasse(eintrag.Art)) return;
            var abschnitte = original?.m_Sections;
            if (abschnitte == null || abschnitte.Length == 0)
            {
                Mod.log.Info("PLT-Querschnitt: '" + (original?.name ?? "?")
                    + "' hat keine Sektionen.");
                return;
            }

            var zeilen = new List<string>();
            var bauteile = 0;
            for (var i = 0; i < abschnitte.Length; i++)
            {
                var abschnitt = abschnitte[i].m_Section;
                if (abschnitt == null) { zeilen.Add(i + ": (leer)"); continue; }
                var stuecke = abschnitt.m_Pieces;
                if (stuecke == null || stuecke.Length == 0)
                {
                    zeilen.Add(i + ": " + abschnitt.name + " (keine Bauteile)");
                    continue;
                }
                for (var j = 0; j < stuecke.Length; j++)
                {
                    var teil = stuecke[j]?.m_Piece;
                    if (teil == null) continue;
                    bauteile++;
                    var material = "-";
                    try
                    {
                        var anzahl = teil.materialCount;
                        var namen = new List<string>();
                        for (var k = 0; k < anzahl; k++)
                            namen.Add(teil.GetSurfaceAsset(k)?.name ?? "?");
                        material = anzahl + "x [" + string.Join(", ", namen) + "]";
                    }
                    catch (Exception ausnahme)
                    {
                        material = "nicht lesbar (" + ausnahme.GetType().Name + ")";
                    }
                    zeilen.Add(abschnitt.name + Bedingungen(abschnitte[i])
                        + " / " + teil.name + Bedingungen(stuecke[j])
                        + " Breite " + teil.m_Width.ToString("F2")
                        + " Ebene " + teil.m_Layer + " Material " + material
                        + Spuren(teil));
                }
            }

            Mod.log.Info("PLT-Querschnitt von '" + original.name + "': "
                + abschnitte.Length + " Sektion(en), " + bauteile
                + " Bauteil(e). " + string.Join(" | ", zeilen));
        }

        /**
         * Die Bedingungen eines Querschnitts oder Bauteils, kurz notiert.
         *
         * `+X` muss erfuellt sein, `?X` eine von mehreren, `-X` darf nicht.
         * Leer, wenn es keine gibt - das ist der Normalfall und soll die
         * Zeile nicht aufblaehen.
         */
        private static string Bedingungen(NetSectionInfo info)
            => info == null ? string.Empty
                : Bedingungen(info.m_RequireAll, info.m_RequireAny,
                    info.m_RequireNone);

        private static string Bedingungen(NetPieceInfo info)
            => info == null ? string.Empty
                : Bedingungen(info.m_RequireAll, info.m_RequireAny,
                    info.m_RequireNone);

        private static string Bedingungen(NetPieceRequirements[] alle,
                                          NetPieceRequirements[] eine,
                                          NetPieceRequirements[] keine)
        {
            var teile = new List<string>();
            if (alle != null) foreach (var r in alle) teile.Add("+" + r);
            if (eine != null) foreach (var r in eine) teile.Add("?" + r);
            if (keine != null) foreach (var r in keine) teile.Add("-" + r);
            return teile.Count == 0 ? string.Empty
                : " {" + string.Join(" ", teile) + "}";
        }

        /**
         * Die Spuren eines Bauteils, mit Vermerk, welche eine Parkspur ist.
         *
         * Das ist die Stelle, an der sich die Parkspur wirklich versteckt:
         * kein eigenes Mesh, sondern ein `NetLanePrefab` am Bauteil.
         */
        private static string Spuren(NetPiecePrefab teil)
        {
            var spuren = teil?.GetComponent<NetPieceLanes>()?.m_Lanes;
            if (spuren == null || spuren.Length == 0) return string.Empty;
            var namen = new List<string>();
            for (var i = 0; i < spuren.Length; i++)
            {
                var spur = spuren[i]?.m_Lane;
                if (spur == null) continue;
                namen.Add(spur.name
                    + (spur.GetComponent<Game.Prefabs.ParkingLane>() != null
                        ? " <PARKSPUR>" : string.Empty));
            }
            return namen.Count == 0 ? string.Empty
                : " Spuren [" + string.Join(", ", namen) + "]";
        }

        private bool _katalogGeschrieben;
        private bool _flaechenGeschrieben;

        /**
         * WAS TRAEGT EIN FLAECHEN-PREFAB AN AUSSEHEN?
         *
         * Der Nutzer will der Gasse das Aussehen einer GEWAEHLTEN Flaeche
         * geben - "Pavement Surface 02", "Sand Surface 01". Ein
         * Strassenbauteil bezieht sein Material ausschliesslich aus einem
         * `SurfaceAsset`; `ObtainMaterials()` baut den Materialspeicher bei
         * jedem Aufruf daraus neu. Die Frage ist also nicht, ob wir ein
         * Material hinschreiben koennen, sondern ob es fuer diese Flaechen
         * ein Asset gibt.
         *
         * Dieser Abzug listet fuer jedes Flaechen-Prefab alle Bauteile
         * (`ComponentBase`) und, wo vorhanden, was am `RenderedArea` haengt.
         * Findet sich dort eine Spur zu einem `SurfaceAsset`, ist der Weg
         * frei. Findet sich nur ein `Material`, ist er versperrt - und dann
         * sage ich das, statt einen vierten Anlauf zu nehmen.
         */
        private void SchreibeFlaechenabzug()
        {
            if (_flaechenGeschrieben) return;
            _flaechenGeschrieben = true;

            var flaechen = GetEntityQuery(
                ComponentType.ReadOnly<Game.Prefabs.SurfaceData>(),
                ComponentType.ReadOnly<PrefabData>());
            using var kandidaten = flaechen.ToEntityArray(
                Unity.Collections.Allocator.Temp);
            var zeilen = new List<string>();
            for (var i = 0; i < kandidaten.Length; i++)
            {
                if (!_prefabSystem.TryGetPrefab<PrefabBase>(kandidaten[i],
                        out var prefab) || prefab == null) continue;
                var name = prefab.name;
                if (!name.Contains("Surface") && !name.Contains("Pavement")
                    && !name.Contains("Sand") && !name.Contains("Grass")) continue;

                var teile = new List<string>();
                foreach (var teil in prefab.components)
                {
                    if (teil == null) continue;
                    var art = teil.GetType().Name;
                    if (teil is RenderedArea gerendert)
                        art += "{Version=" + gerendert.m_Version
                            + ", Material=" + (gerendert.m_Material != null
                                ? gerendert.m_Material.name : "null")
                            + ", Shader=" + (gerendert.m_Material != null
                                && gerendert.m_Material.shader != null
                                ? gerendert.m_Material.shader.name : "-")
                            + "}";
                    if (teil is RenderPrefab rp)
                        art += "{Materialien=" + rp.materialCount + "}";
                    teile.Add(art);
                }
                zeilen.Add(name + ": " + string.Join(", ", teile));
            }
            zeilen.Sort(StringComparer.OrdinalIgnoreCase);
            Mod.log.Info("PLT-Flaechenabzug: " + zeilen.Count
                + " Flaechen-Prefab(s). " + string.Join(" | ", zeilen));
        }

        /**
         * EINMAL JE SPIELSTART: welche Strasse traegt welches Material?
         *
         * Nur die Ebenen `Surface` und `Side` - was man an einer ebenerdigen
         * Strasse sieht. Je Strasse eine Zeile mit den VERSCHIEDENEN Assets;
         * Wiederholungen sagen nichts.
         *
         * Der Katalog beantwortet die Frage des Nutzers vom 2026-09-18, ob
         * die Gasse das Aussehen seines Belags bekommen kann: gibt es eine
         * Strasse mit einem Asset, das wie Pflaster aussieht, ist der Weg
         * ein Klon der Bauteile mit getauschtem Asset. Gibt es keine,
         * braeuchte es eine eigene Textur - eine andere Groessenordnung.
         */
        private void SchreibeMaterialkatalog()
        {
            if (_katalogGeschrieben) return;
            _katalogGeschrieben = true;

            using var strassen = _strassenprefabs.ToEntityArray(
                Unity.Collections.Allocator.Temp);
            var zeilen = new List<string>();
            for (var i = 0; i < strassen.Length; i++)
            {
                if (!_prefabSystem.TryGetPrefab<RoadPrefab>(strassen[i],
                        out var strasse) || strasse == null) continue;
                if (strasse.name.StartsWith("PLT ")) continue;
                var abschnitte = strasse.m_Sections;
                if (abschnitte == null) continue;

                var assets = new HashSet<string>();
                for (var a = 0; a < abschnitte.Length; a++)
                {
                    var stuecke = abschnitte[a].m_Section?.m_Pieces;
                    if (stuecke == null) continue;
                    for (var b = 0; b < stuecke.Length; b++)
                    {
                        var teil = stuecke[b]?.m_Piece;
                        if (teil == null) continue;
                        if (teil.m_Layer != NetPieceLayer.Surface
                            && teil.m_Layer != NetPieceLayer.Side) continue;
                        try
                        {
                            for (var k = 0; k < teil.materialCount; k++)
                                assets.Add(teil.GetSurfaceAsset(k)?.name ?? "?");
                        }
                        catch { assets.Add("nicht lesbar"); }
                    }
                }
                if (assets.Count == 0) continue;
                zeilen.Add(strasse.name + " -> " + string.Join(", ", assets));
            }

            zeilen.Sort(StringComparer.OrdinalIgnoreCase);
            Mod.log.Info("PLT-Materialkatalog: " + zeilen.Count
                + " Strasse(n) mit sichtbaren Bauteilen. "
                + string.Join(" | ", zeilen));
        }

        private static string Klonname(Strassenklonart art)
        {
            switch (art)
            {
                case Strassenklonart.Zoning: return "PLT Zoningstrasse";
                case Strassenklonart.ZufahrtsgasseEinbahn:
                    return "PLT Zufahrtsgasse Einbahn";
                default: return "PLT Zufahrtsgasse";
            }
        }

        /**
         * Beide Gassenarten, an einer Stelle.
         *
         * Vorher stand `art == Strassenklonart.Zufahrtsgasse` an fuenf
         * Stellen. Mit der Einbahn waeren daraus fuenf Gelegenheiten
         * geworden, eine zu vergessen - und genau so ist am 2026-09-18
         * schon einmal eine Regel halb angekommen.
         */
        private static bool IstGasse(Strassenklonart art)
            => art == Strassenklonart.Zufahrtsgasse
               || art == Strassenklonart.ZufahrtsgasseEinbahn;
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

    // Nach NetInitialize lesen: im Nutzerlog war Flags 0 noch uninitialisiert.
    // Damit kann derselbe PrefabUpdate-Zyklus ClipTerrain nicht wieder setzen.
    public sealed partial class ParkingLotVanillaGasseAbschlussSystem : GameSystemBase
    {
        [Preserve]
        protected override void OnUpdate()
            => World.GetOrCreateSystemManaged<ParkingLotZoningRoadPrefabSystem>()
                .HeileVanillaGasse();
    }
}
