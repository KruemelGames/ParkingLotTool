using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Game;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.Rendering;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * WARUM DIE VORFLAECHE EIN EIGENES PREFAB BRAUCHT.
     *
     * Der Abzug des Nutzers vom 2026-08-27 belegt, dass die Vorflaeche bis
     * auf 10 cm genau an die Asphaltkante reicht. Unsichtbar war sie trotzdem,
     * weil `RenderedArea.m_DecalLayerMask` am Vanilla-Prefab nur `Terrain`
     * enthaelt. Ein Gehweg ist ein Strassenmesh. Die Vorflaeche braucht daher
     * einen Klon mit `Terrain | Roads`; das Original darf nicht veraendert
     * werden, sonst bluten handgemalte Flaechen dieses Belags stadtweit aus.
     */
    public sealed partial class ParkingLotToolSystem
    {
        private ParkingLotApronPrefabSystem _apronPrefabSystem;

        /**
         * `fehlgeschlagen` heisst: der Klon kann grundsaetzlich nicht
         * entstehen, der Bau bricht ab. `aufgegeben` heisst: er ist nicht
         * fertig geworden, aber der Parkplatz wird OHNE Vorflaeche gebaut.
         * Die Unterscheidung ist wichtig - ein Werkzeug, das gar nicht mehr
         * baut, waere der schlimmere Ausfall.
         */
        private Entity VorflaechenPrefab(Entity belagPrefab,
            out bool fehlgeschlagen, out bool aufgegeben,
            int prioritaetsaufschlag = 0)
        {
            fehlgeschlagen = false;
            aufgegeben = _apronPrefabSystem == null;
            return _apronPrefabSystem == null
                ? Entity.Null
                : _apronPrefabSystem.FordereAn(belagPrefab,
                    prioritaetsaufschlag, out fehlgeschlagen, out aufgegeben);
        }
    }

    /**
     * Meldet angeforderte Vorflaechen-Prefabs in CS2s eigener Prefabphase an.
     *
     * `PrefabSystem.AddPrefab` legt nur die Prefab-Entity samt Nullwerten an.
     * Den Instanzarchetyp baut `AreaPrefab.LateInitialize`, aufgerufen vom
     * `PrefabInitializeSystem`. Ein Aufruf aus `ToolUpdate` kommt dafuer zu
     * spaet: `PrefabUpdate` dieses Frames ist schon vorbei und `Created` wird
     * beim Aufraeumen vor dem naechsten entfernt. Dieses System laeuft deshalb
     * vor `PrefabInitializeSystem` in `PrefabUpdate` (Registrierung in Mod.cs).
     * So sehen auch `AreaInitializeSystem` und spaeter `AreaBatchSystem`
     * dasselbe unverbrauchte `Created`.
     */
    public sealed partial class ParkingLotApronPrefabSystem : GameSystemBase
    {
        private sealed class Eintrag
        {
            public Entity Original;
            public Entity KlonEntity;
            public SurfacePrefab Klon;
            public string Name;
            public DecalLayers OriginalMaske;
            public int Prioritaetsaufschlag;
            public int OriginalPrioritaet;
            public int Pruefungen;
            public bool Bereit;
            public bool Fehler;
            public bool Aufgegeben;
        }

        /**
         * Nach so vielen Prefabzyklen ohne Erfolg wird ohne Vorflaeche
         * gebaut. Zwei Zyklen reichen im Normalfall; 120 ist damit kein
         * knappes Zeitfenster, sondern die Grenze, ab der etwas grundlegend
         * anders laeuft als gemessen.
         */
        private const int AufgabenNachZyklen = 120;

        /*
         * Der Schluessel traegt den Prioritaetsaufschlag mit.
         *
         * Vorflaeche und Zoningbelag klonen dasselbe Belagprefab, brauchen
         * aber verschiedene Zeichenreihenfolgen: der Zoningbelag muss ueber
         * die Gebaeudeflaechen, die Vorflaeche soll bleiben, wie sie ist.
         * Ein gemeinsamer Klon koennte nur eine der beiden bedienen.
         */
        private readonly Dictionary<(Entity Original, int Aufschlag), Eintrag>
            _eintraege = new Dictionary<(Entity, int), Eintrag>();
        private PrefabSystem _prefabSystem;
        private EntityQuery _flaechenprefabs;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _flaechenprefabs = GetEntityQuery(
                ComponentType.ReadOnly<SurfaceData>(),
                ComponentType.ReadOnly<PrefabData>());
        }

        /**
         * Das Werkzeug stellt nur die Anforderung. `AddPrefab` bleibt
         * ausschliesslich in OnUpdate und damit in der richtigen Phase.
         */
        public Entity FordereAn(Entity original, int prioritaetsaufschlag,
            out bool fehlgeschlagen, out bool aufgegeben)
        {
            fehlgeschlagen = false;
            aufgegeben = false;
            if (original == Entity.Null || !EntityManager.Exists(original))
                return Entity.Null;

            var schluessel = (original, prioritaetsaufschlag);
            if (!_eintraege.TryGetValue(schluessel, out var eintrag))
            {
                if (!_prefabSystem.TryGetPrefab<SurfacePrefab>(original,
                        out var vorbild) || vorbild == null)
                {
                    Mod.log.Warn("PLT-Vorflaeche: Das angeforderte Belag-Prefab "
                        + "ist kein SurfacePrefab; kein Klon moeglich.");
                    fehlgeschlagen = true;
                    return Entity.Null;
                }

                eintrag = new Eintrag
                {
                    Original = original,
                    KlonEntity = Entity.Null,
                    Prioritaetsaufschlag = prioritaetsaufschlag,
                    // Der Name muss sich unterscheiden: zwei Prefabs mit
                    // derselben PrefabID lehnt PrefabSystem ab.
                    Name = (prioritaetsaufschlag == 0
                            ? "PLT Vorflaeche ("
                            : "PLT Zoningbelag (")
                        + vorbild.name + ")",
                };
                _eintraege.Add(schluessel, eintrag);
                MerkeKlon(vorbild.name, prioritaetsaufschlag);
                Mod.log.Info($"PLT-Vorflaeche: '{eintrag.Name}' fuer den "
                    + "naechsten PrefabUpdate-Zyklus angefordert.");
            }

            fehlgeschlagen = eintrag.Fehler;
            aufgegeben = eintrag.Aufgegeben;
            return eintrag.Bereit ? eintrag.KlonEntity : Entity.Null;
        }

        /**
         * DIE KLONE MUESSEN VOR DEM SPIELSTAND DA SEIN.
         *
         * Bisher entstand ein Flaechenklon erst, wenn gebaut wurde. Beim
         * Laden eines Spielstands sucht CS2 aber die Prefabs der
         * gespeicherten Flaechen - und fand sie nicht. Das Ergebnis hat der
         * Nutzer am 2026-09-03 im Bild gezeigt: *"Immer wenn ich einen
         * Spielstand lade, ist das Gras ploetzlich weiss."* Betroffen war
         * alles, was ueber einen Klon laeuft - Gras, Dekoflaeche, Zufahrt,
         * Zoningstrassenflaeche.
         *
         * Dieselbe Lehre wie bei der Zoning-Strasse, nur eine Woche spaeter:
         * ein Prefab, das in einem Spielstand vorkommen kann, muss
         * BEDINGUNGSLOS beim Start entstehen. Welche gebraucht werden, weiss
         * aber nur der Nutzer - er waehlt die Flaechen aus. Also wird
         * gemerkt, welche je gebraucht wurden, und die entstehen wieder.
         */
        private HashSet<string> _gemerkteKlone;
        private bool _gesaet;
        private int _saeversuche;

        /**
         * Wie lange auf die Einstellungen gewartet wird, bevor aufgegeben
         * wird. Grosszuegig - ein Prefabzyklus kostet nichts, und ein zu
         * frueh abgebrochener Versuch kostet den ganzen Spielstand.
         */
        private const int SaeVersucheMax = 600;

        /**
         * IMMER FRISCH LESEN, NICHT EINMAL MERKEN.
         *
         * Die naheliegende Fassung las die Liste einmal und behielt sie.
         * Genau daran ist am selben Tag schon die Fangauswahl gescheitert:
         * laeuft der erste Aufruf, BEVOR CS2 die Einstellungsdatei gelesen
         * hat, merkt man sich eine leere Liste - und die bleibt leer, so oft
         * man auch nachfragt.
         */
        private HashSet<string> GemerkteKlone()
        {
            var gelesen = new HashSet<string>(StringComparer.Ordinal);
            var text = Mod.Optionen?.Flaechenklone;
            if (!string.IsNullOrEmpty(text))
                foreach (var zeile in text.Split('\n'))
                    if (!string.IsNullOrWhiteSpace(zeile))
                        gelesen.Add(zeile.Trim());
            _gemerkteKlone = gelesen;
            return gelesen;
        }

        private void MerkeKlon(string vorbildname, int aufschlag)
        {
            if (Mod.Optionen == null) return;
            var eintrag = vorbildname + "|" + aufschlag;
            if (!GemerkteKlone().Add(eintrag)) return;
            Mod.Optionen.Flaechenklone =
                string.Join("\n", GemerkteKlone().ToArray());
            Mod.Optionen.ApplyAndSave();
            Mod.log.Info($"PLT-Vorflaeche: '{eintrag}' gemerkt - der Klon "
                + "entsteht ab jetzt schon beim Spielstart.");
        }

        /**
         * Fordert alle gemerkten Klone an - einmal, sobald die
         * Flaechenprefabs da sind.
         */
        private void SaeheGemerkteKlone()
        {
            if (_gesaet) return;
            if (_flaechenprefabs.IsEmptyIgnoreFilter) return;

            var gemerkt = GemerkteKlone();
            if (gemerkt.Count == 0)
            {
                /*
                 * LEER HEISST NICHT FERTIG. Die Einstellungen koennen noch
                 * nicht gelesen sein - dann waere ein Abbruch hier die
                 * Entscheidung, in diesem Spielstart NICHTS zu saeen, und der
                 * Spielstand kaeme wieder weiss zurueck.
                 *
                 * Erst nach vielen Zyklen ohne Eintrag gilt: es gibt wirklich
                 * keine gemerkten Klone. Das ist der Normalfall bei einem
                 * frischen Mod und voellig in Ordnung.
                 */
                if (++_saeversuche < SaeVersucheMax) return;
                _gesaet = true;
                Mod.log.Info("PLT-Vorflaeche: keine gemerkten Flaechenklone - "
                    + "es wurde noch nie einer gebraucht.");
                return;
            }

            var gefunden = 0;
            using var kandidaten = _flaechenprefabs.ToEntityArray(
                Unity.Collections.Allocator.Temp);
            foreach (var eintrag in gemerkt)
            {
                var strich = eintrag.LastIndexOf('|');
                if (strich <= 0) continue;
                var vorbildname = eintrag.Substring(0, strich);
                if (!int.TryParse(eintrag.Substring(strich + 1),
                        out var aufschlag)) continue;

                for (var i = 0; i < kandidaten.Length; i++)
                {
                    if (!_prefabSystem.TryGetPrefab<SurfacePrefab>(
                            kandidaten[i], out var vorbild)
                        || vorbild == null) continue;
                    if (!string.Equals(vorbild.name, vorbildname,
                            StringComparison.Ordinal)) continue;
                    FordereAn(kandidaten[i], aufschlag, out _, out _);
                    gefunden++;
                    break;
                }
            }
            _gesaet = true;
            Mod.log.Info($"PLT-Vorflaeche: {gefunden} von {gemerkt.Count} "
                + "gemerkten Flaechenklonen beim Start angefordert.");
        }

        private bool _prioritaetenGemeldet;

        /**
         * Schreibt EINMAL die Zeichenprioritaeten aller Vanilla-Flaechen ins
         * Log, nach Wert sortiert.
         *
         * Ohne diese Aufstellung ist jeder Aufschlag geraten. Am 2026-09-02
         * habe ich 10 gewaehlt, ohne die Skala zu kennen - sie ist negativ
         * (`Pavement Surface 01` liegt bei -96), und die Flaeche verschwand
         * im Spiel ganz. Der Nutzer musste den Fehler melden, obwohl die
         * Zahl im Spiel abfragbar war.
         */
        private bool _platzhalterGemeldet;

        /**
         * STEHEN UNSERE KLONE IN EINER PLATZHALTER-LISTE?
         *
         * Der Nutzer am 2026-09-14: per Zoning gesetzte Haeuser bekommen
         * Belag bis zur Strasse, allein weil PLT geladen ist - ohne einen
         * einzigen gebauten Parkplatz. Und: *"Die Erweiterungsflaechen an den
         * Haeusern entstehen erst, wenn sie gebaut wurden, nicht wenn die
         * Kraene da stehen."*
         *
         * Genau dann legt CS2 die Unterflaechen eines Gebaeudes an. Einen
         * Teil davon sucht es ueber PLATZHALTER aus einer Kandidatenliste -
         * `PlaceholderObjectElement` am Platzhalter-Prefab. Unsere Klone
         * uebernehmen mit `AddComponentFrom` JEDES Bauteil des Originals.
         * Traegt eins davon die Kandidateneigenschaft, steht der Klon
         * anschliessend mit in dieser Liste, und CS2 waehlt ihn manchmal
         * aus. Unser Klon hat `Roads` in der Decal-Maske - er zeichnet also
         * ueber die Strasse. Das waere genau das gemeldete Bild, samt "nicht
         * immer dieselben Haeuser".
         *
         * Das ist eine Vermutung. Diese Meldung belegt sie oder raeumt sie
         * ab: sie nennt zu jedem Platzhalter-Flaechenprefab seine Kandidaten
         * beim Namen. Steht `PLT` darunter, ist es das.
         */
        private void MeldePlatzhalter()
        {
            if (_platzhalterGemeldet) return;
            _platzhalterGemeldet = true;
            try
            {
                var zeilen = new List<string>();
                using var flaechen = _flaechenprefabs.ToEntityArray(
                    Unity.Collections.Allocator.Temp);
                for (var i = 0; i < flaechen.Length; i++)
                {
                    var entity = flaechen[i];
                    if (!EntityManager.HasBuffer<PlaceholderObjectElement>(
                            entity)) continue;
                    if (!_prefabSystem.TryGetPrefab<PrefabBase>(entity,
                            out var platzhalter) || platzhalter == null)
                        continue;
                    var puffer = EntityManager
                        .GetBuffer<PlaceholderObjectElement>(entity, true);
                    var namen = new List<string>();
                    for (var k = 0; k < puffer.Length; k++)
                    {
                        var kandidat = puffer[k].m_Object;
                        namen.Add(EntityManager.Exists(kandidat)
                            && _prefabSystem.TryGetPrefab<PrefabBase>(
                                kandidat, out var kp) && kp != null
                            ? kp.name : "#" + kandidat.Index);
                    }
                    zeilen.Add(platzhalter.name + " -> "
                        + (namen.Count == 0 ? "leer"
                            : string.Join(", ", namen)));
                }

                Mod.log.Info("PLT-Platzhalterflaechen (" + zeilen.Count
                    + " Platzhalter mit Kandidatenliste): "
                    + (zeilen.Count == 0 ? "keine gefunden"
                        : string.Join(" | ", zeilen)));

                /*
                 * UND JETZT IST DIESE MELDUNG DER WAECHTER.
                 *
                 * Sie war das Werkzeug, das den Befund gebracht hat; sie
                 * bleibt, weil sie ihn auch bewacht. Steht je wieder ein
                 * PLT-Prefab in einer dieser Listen, sagt es das Log beim
                 * naechsten Start - statt dass es jemandem im Spiel auffaellt.
                 */
                var eigene = zeilen.Count(zeile => zeile.Contains("PLT "));
                if (eigene > 0)
                    Mod.log.Error($"PLT-Platzhalterflaechen: {eigene} "
                        + "Platzhalter fuehren ein PLT-Prefab als Kandidaten. "
                        + "Dann bekommen fremde Gebaeude unseren Belag - "
                        + "siehe den Befund vom 2026-09-14. Ein Klon darf "
                        + "`SpawnableArea` nicht mitkopieren.");
            }
            catch (Exception ausnahme)
            {
                Mod.log.Warn("PLT-Platzhalterflaechen nicht ermittelbar: "
                    + ausnahme.Message);
            }
        }

        private void MeldePrioritaeten()
        {
            if (_prioritaetenGemeldet) return;
            _prioritaetenGemeldet = true;
            try
            {
                var werte = new List<string>();
                using var flaechen = _flaechenprefabs.ToEntityArray(
                    Unity.Collections.Allocator.Temp);
                for (var i = 0; i < flaechen.Length; i++)
                {
                    if (!_prefabSystem.TryGetPrefab<SurfacePrefab>(flaechen[i],
                            out var prefab) || prefab == null) continue;
                    var gerendert = prefab.GetComponent<RenderedArea>();
                    if (gerendert == null) continue;
                    werte.Add($"{gerendert.m_RendererPriority} {prefab.name}");
                }
                werte.Sort((a, b) =>
                    int.Parse(a.Split(' ')[0]).CompareTo(
                        int.Parse(b.Split(' ')[0])));
                Mod.log.Info("PLT-Flaechenprioritaeten (" + werte.Count
                    + " Flaechen, aufsteigend; hoeher heisst spaeter "
                    + "gezeichnet und damit oben): "
                    + string.Join(" | ", werte));
            }
            catch (Exception ausnahme)
            {
                Mod.log.Warn("PLT-Flaechenprioritaeten nicht ermittelbar: "
                    + ausnahme.Message);
            }
        }

        [Preserve]
        protected override void OnUpdate()
        {
            MeldePrioritaeten();
            SaeheGemerkteKlone();
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
                    eintrag.Bereit = true;
                    // `MeldeFlaechendaten` stand hier daneben und schrieb
                    // 32 Zeilen je Spielstart - alle Flaechen-Prefabs mit
                    // allen Werten. Das war das Werkzeug, mit dem am
                    // 2026-09-14 der Vergleich "Pavement 01 gegen 02" moeglich
                    // wurde; der Befund steht. Der Waechter darunter bleibt.
                    MeldePlatzhalter();
                    Mod.log.Info($"PLT-Vorflaechenprefab FERTIG: {zustand}");
                }
                else if (eintrag.Pruefungen >= AufgabenNachZyklen)
                {
                    /*
                     * AUFGEBEN IST BESSER ALS EWIG WARTEN.
                     *
                     * Der Bau wartet absichtlich auf dieses Prefab, damit
                     * Enter nicht still einen Parkplatz ohne Vorflaeche
                     * festschreibt. Ein HARTER Fehler bricht sauber ab - aber
                     * eine Bereitschaft, die einfach nie eintritt, haette den
                     * Nutzer dauerhaft am Bauen gehindert. Das waere der
                     * schlimmere Ausfall: der Parkplatz ist die Hauptsache,
                     * die Vorflaeche ist Zierde.
                     *
                     * Deshalb hier der Ausweg. Er ist NICHT still: die Meldung
                     * nennt jeden einzelnen Abnahmewert, also genau den
                     * Schritt, der ausgeblieben ist.
                     */
                    eintrag.Aufgegeben = true;
                    Mod.log.Error("PLT-Vorflaechenprefab AUFGEGEBEN nach "
                        + $"{eintrag.Pruefungen} PrefabUpdate-Zyklen: {zustand}. "
                        + "Es wird ohne Vorflaeche weitergebaut.");
                }
                else if (eintrag.Pruefungen == 1 || eintrag.Pruefungen == 30)
                {
                    var meldung = $"PLT-Vorflaechenprefab NOCH NICHT FERTIG "
                        + $"nach {eintrag.Pruefungen} PrefabUpdate-Zyklen: "
                        + zustand;
                    if (eintrag.Pruefungen < 30) Mod.log.Info(meldung);
                    else Mod.log.Warn(meldung);
                }
            }
        }

        private void Registriere(Eintrag eintrag)
        {
            SurfacePrefab klon = null;
            try
            {
                if (!_prefabSystem.TryGetPrefab<SurfacePrefab>(
                        eintrag.Original, out var original) || original == null)
                {
                    Fehlschlag(eintrag, "Das Original ist nicht mehr aufloesbar.");
                    return;
                }

                /*
                 * FRISCHES PREFAB, KEIN Object.Instantiate.
                 *
                 * Object.Instantiate teilt die ComponentBase-Objekte mit dem
                 * Original. `AddComponentFrom` erzeugt dagegen je Bauteil eine
                 * neue Instanz samt Rueckverknuepfung auf den Klon. Dieses
                 * Muster ist im Projekt fuer die Bucht-Decals belegt.
                 */
                klon = ScriptableObject.CreateInstance<SurfacePrefab>();
                klon.name = eintrag.Name;
                klon.m_Color = original.m_Color;
                klon.m_EdgeColor = original.m_EdgeColor;
                klon.m_SelectionColor = original.m_SelectionColor;
                klon.m_SelectionEdgeColor = original.m_SelectionEdgeColor;
                foreach (var bauteil in original.components)
                {
                    if (bauteil == null) continue;

                    /*
                     * `SpawnableArea` NICHT MITKOPIEREN - DAS IST DIE
                     * ANMELDUNG ALS KANDIDAT.
                     *
                     * Dieses Bauteil traegt `m_Placeholders`: die Liste der
                     * Platzhalter, fuer die das Prefab als Kandidat gilt.
                     * `Pavement Surface 01` ist dort fuer
                     * `Pavement Surface Placeholder` eingetragen. Wer es
                     * mitkopiert, meldet seinen Klon in derselben Liste an.
                     *
                     * GEMESSEN am 2026-09-14 im Log der Mod:
                     *
                     *     Pavement Surface Placeholder -> Pavement Surface 01,
                     *     Pavement Surface 02, PLT Vorflaeche (Pavement
                     *     Surface 01), PLT Zoningbelag (Pavement Surface 01)
                     *
                     * Damit greift CS2 beim Fertigstellen eines gezonten
                     * Hauses manchmal zu UNSEREM Klon - und der traegt
                     * `Terrain | Roads`, zeichnet also ueber die Strasse. Der
                     * Nutzer sah es als Belag, der von der Einfahrt bis auf
                     * die Fahrbahn laeuft, *"und zwar nur bei
                     * Pflaster-Oberflaeche 01, bei 02 nicht"* - weil unsere
                     * Klone Kopien von 01 sind und dasselbe Material
                     * benutzen. Es genuegte, dass die Mod geladen war; ein
                     * gebauter Parkplatz war nie noetig.
                     *
                     * Das ist genau der Eingriff, den AGENTS.md verbietet:
                     * keine globalen Aenderungen am Spiel. Wir haben ihn
                     * nicht an einem Vanilla-Prefab vorgenommen, sondern uns
                     * selbst in eine Vanilla-Liste gestellt - die Wirkung ist
                     * dieselbe.
                     *
                     * Wir brauchen das Bauteil nicht: unsere Flaechen werden
                     * mit einer eigenen `CreationDefinition` gebaut, nie ueber
                     * einen Platzhalter gespawnt.
                     */
                    if (bauteil is SpawnableArea)
                    {
                        Mod.log.Info($"PLT-Vorflaeche: '{eintrag.Name}' "
                            + "bekommt KEIN SpawnableArea - sonst stuende der "
                            + "Klon in CS2s Kandidatenliste und Haeuser "
                            + "bekaemen unseren Belag.");
                        continue;
                    }

                    /*
                     * KEIN `UIObject` - SONST STEHT DER KLON IM MENUE.
                     *
                     * `UIObject` traegt Menuegruppe, Prioritaet und Symbol.
                     * Uebernimmt man es, landet unsere Kopie in CS2s
                     * Flaechenliste und der Spieler kann sie von Hand setzen.
                     * Der Nutzer am 2026-09-15: *"koennen wir noch unsere
                     * PLT-Flaechen aus der Surfaces-Liste rausnehmen? Die
                     * sind einfach drin gelandet."*
                     *
                     * Wir brauchen es nicht: unsere Flaechen entstehen ueber
                     * eine eigene `CreationDefinition`, nie ueber das Menue.
                     * Dieselbe Abkuerzung nehmen `ParkingLotFusswegPrefab`
                     * und `ParkingLotZoningRoadPrefab` schon laenger.
                     */
                    if (bauteil is UIObject)
                    {
                        Mod.log.Info($"PLT-Vorflaeche: '{eintrag.Name}' "
                            + "bekommt KEIN UIObject - sonst stuende der Klon "
                            + "in CS2s Flaechenliste zum Selbersetzen.");
                        continue;
                    }

                    klon.AddComponentFrom(bauteil);
                }

                var originalGerendert = original.GetComponent<RenderedArea>();
                var klonGerendert = klon.GetComponent<RenderedArea>();
                if (originalGerendert == null || klonGerendert == null)
                {
                    Fehlschlag(eintrag, $"'{original.name}' hat keine "
                        + "RenderedArea-Komponente.");
                    UnityEngine.Object.Destroy(klon);
                    return;
                }

                eintrag.OriginalMaske = originalGerendert.m_DecalLayerMask;
                klonGerendert.m_DecalLayerMask = eintrag.OriginalMaske
                    | DecalLayers.Terrain | DecalLayers.Roads;

                /*
                 * ZEICHENREIHENFOLGE - hoeher heisst spaeter und damit oben.
                 *
                 * `ManagedBatchSystem` rechnet
                 * `renderQueue = shader.renderQueue + m_RendererPriority`.
                 * Der Nutzer hat am 2026-09-02 gemeldet, dass die Flaeche
                 * eines Gebaeudes unseren Zoningbelag ueberdeckt; mit einem
                 * Aufschlag liegt unsere Flaeche darueber.
                 *
                 * Die Vorflaeche bekommt Aufschlag 0 und bleibt damit
                 * unveraendert - sie funktioniert seit Tagen, und eine
                 * stillschweigend geaenderte Zeichenreihenfolge waere genau
                 * die Art Nebenwirkung, die man erst drei Fehler spaeter
                 * bemerkt.
                 */
                /*
                 * ZIELWERT, KEIN AUFSCHLAG.
                 *
                 * Der erste Anlauf addierte auf die Prioritaet des
                 * Originals. Damit landete dasselbe "+2" je nach Vorbild
                 * woanders: Pavement (-96) kam auf -94, Gras (-99) aber nur
                 * auf -97. Der Nutzer verlangte fuer beide -94, und ich
                 * haette ihm zugesagt, was der Code nicht liefert.
                 *
                 * 0 heisst weiterhin "unveraendert" - das ist die
                 * Vorflaeche, die ihren Wert behalten soll.
                 */
                eintrag.OriginalPrioritaet = originalGerendert.m_RendererPriority;
                klonGerendert.m_RendererPriority =
                    eintrag.Prioritaetsaufschlag == 0
                        ? eintrag.OriginalPrioritaet
                        : eintrag.Prioritaetsaufschlag;

                /*
                 * DIE ECKENRUNDUNG BLEIBT, WIE SIE IST.
                 *
                 * Ich hatte sie am 2026-08-28 auf das Minimum gesetzt, um die
                 * sichtbare Naht zwischen Zufahrt und Vorflaeche loszuwerden.
                 * Aus derselben Zahl bildet CS2 aber `m_ExpandAmount`, und den
                 * zieht `ParkingLotApron` am Rand ab: mit Rundung 0,5 sind das
                 * 0,19 m, mit 0,01 fast nichts.
                 *
                 * Folge: die Kante wanderte bei ALLEN Strassen um 0,19 m nach
                 * aussen. Der Nutzer sah danach 2 und 3 mitten im Bordstein
                 * statt an seiner Gehwegseite, und die zuvor perfekten 1, 4
                 * und 5 standen vorne ueber.
                 *
                 * Die Naht ist ein eigenes Problem und muss ohne Eingriff in
                 * die Kante geloest werden. Wer die Rundung wieder anfasst,
                 * verschiebt die Kante mit - das ist hier kein Nebeneffekt,
                 * sondern derselbe Wert.
                 */

                if (!_prefabSystem.AddPrefab(klon))
                {
                    Fehlschlag(eintrag, "PrefabSystem.AddPrefab gab false zurueck.");
                    UnityEngine.Object.Destroy(klon);
                    return;
                }

                eintrag.Klon = klon;
                eintrag.KlonEntity = _prefabSystem.GetEntity(klon);

                /*
                 * DIE LAUFZEITMESSUNG ZUR VERMUTUNG AUS DEM AUFTRAG.
                 * Sind AreaData und SurfaceData hier vorhanden, wurde die
                 * Prefab-Klasse selbst bei GetPrefabComponents einbezogen.
                 */
                var entity = eintrag.KlonEntity;
                Mod.log.Info($"PLT-Vorflaeche: '{eintrag.Name}' in "
                    + "PrefabUpdate vor PrefabInitializeSystem angemeldet. "
                    + $"Decal-Ebenen {eintrag.OriginalMaske} -> "
                    + $"{klonGerendert.m_DecalLayerMask}, Zeichenprioritaet "
                    + $"{eintrag.OriginalPrioritaet} -> "
                    + $"{klonGerendert.m_RendererPriority}. ECS nach AddPrefab: "
                    + $"AreaData={JaNein<AreaData>(entity)}, "
                    + $"AreaColorData={JaNein<Game.Prefabs.AreaColorData>(entity)}, "
                    + $"SurfaceData={JaNein<SurfaceData>(entity)}, "
                    + $"AreaGeometryData={JaNein<AreaGeometryData>(entity)}, "
                    + $"RenderedAreaData={JaNein<RenderedAreaData>(entity)}, "
                    + $"Created={JaNein<Created>(entity)}, "
                    + $"Updated={JaNein<Updated>(entity)}.");
            }
            catch (Exception ausnahme)
            {
                if (klon != null && eintrag.KlonEntity == Entity.Null)
                    UnityEngine.Object.Destroy(klon);
                eintrag.Fehler = true;
                Mod.log.Error(ausnahme, $"PLT-Vorflaeche: '{eintrag.Name}' "
                    + "konnte nicht angemeldet werden.");
            }
        }

        /**
         * Diese Pruefung laeuft gerade dann weiter, wenn der Archetyp fehlt.
         * Die fruehere Fassung erreichte ihre Reparatur erst hinter
         * `HasUsableSurfacePrefab` und konnte den Fehler daher nie sehen.
         */
        private bool Pruefe(Eintrag eintrag, out string zustand)
        {
            var entity = eintrag.KlonEntity;
            if (!EntityManager.Exists(entity))
            {
                zustand = $"'{eintrag.Name}': Prefab-Entity existiert nicht.";
                return false;
            }

            if (!EntityManager.HasComponent<AreaData>(entity)
                || !EntityManager.HasComponent<AreaGeometryData>(entity)
                || !EntityManager.HasComponent<RenderedAreaData>(entity))
            {
                zustand = $"'{eintrag.Name}': Pflichtkomponenten fehlen "
                    + $"(AreaData={JaNein<AreaData>(entity)}, "
                    + $"AreaGeometryData={JaNein<AreaGeometryData>(entity)}, "
                    + $"RenderedAreaData={JaNein<RenderedAreaData>(entity)}).";
                return false;
            }

            var area = EntityManager.GetComponentData<AreaData>(entity);
            var geometrie = EntityManager
                .GetComponentData<AreaGeometryData>(entity);
            var stapel = EntityManager
                .GetComponentData<RenderedAreaData>(entity);

            var originalBatch = -1;
            if (EntityManager.Exists(eintrag.Original)
                && EntityManager.HasComponent<RenderedAreaData>(
                    eintrag.Original))
            {
                originalBatch = EntityManager
                    .GetComponentData<RenderedAreaData>(eintrag.Original)
                    .m_BatchIndex;
            }

            var originalMaske = (DecalLayers)0;
            if (_prefabSystem.TryGetPrefab<SurfacePrefab>(eintrag.Original,
                    out var original) && original != null)
            {
                var originalGerendert = original.GetComponent<RenderedArea>();
                if (originalGerendert != null)
                    originalMaske = originalGerendert.m_DecalLayerMask;
            }
            var klonMaske = eintrag.Klon?.GetComponent<RenderedArea>()
                ?.m_DecalLayerMask ?? (DecalLayers)0;

            var archetypSteht = area.m_Archetype.Valid;
            var typStimmt = geometrie.m_Type == AreaType.Surface;
            var stapelSteht = stapel.m_HeightOffset > 0f;
            var eigenerStapel = originalBatch >= 0
                && stapel.m_BatchIndex != originalBatch;
            var maskeStimmt = (klonMaske & DecalLayers.Terrain) != 0
                && (klonMaske & DecalLayers.Roads) != 0;
            var originalUnveraendert = originalMaske == eintrag.OriginalMaske;

            zustand = $"'{eintrag.Name}': Archetyp="
                + (archetypSteht ? "gueltig" : "UNGUELTIG")
                + $", Typ={geometrie.m_Type}, Flags={geometrie.m_Flags}, "
                + $"Materialstapel=Batch {stapel.m_BatchIndex} "
                + $"(Original {originalBatch}), Hoehenversatz "
                + stapel.m_HeightOffset.ToString("F2",
                    CultureInfo.InvariantCulture)
                + $", Decal-Ebenen={klonMaske}, "
                + $"Original-Ebenen={originalMaske}.";

            return archetypSteht && typStimmt && stapelSteht
                && eigenerStapel && maskeStimmt && originalUnveraendert;
        }

        private string JaNein<T>(Entity entity) where T : unmanaged,
            IComponentData
            => EntityManager.HasComponent<T>(entity) ? "ja" : "NEIN";

        private static void Fehlschlag(Eintrag eintrag, string grund)
        {
            eintrag.Fehler = true;
            Mod.log.Error($"PLT-Vorflaeche: '{eintrag.Name}' ist "
                + $"fehlgeschlagen: {grund}");
        }
    }
}
