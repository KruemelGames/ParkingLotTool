using static ParkingLotTool.Tools.ParkingLotTexte;
using System;
using System.Collections.Generic;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace ParkingLotTool.Tools
{
    /**
     * Setzt die Stellplatz-Decals.
     *
     * WARUM DECALS UND NICHT SELBSTGEBAUTE SPUREN: am Vanilla-Parkplatz
     * gemessen (Bauteilliste ParkingLot03). `ParkingLotDecal01` ist ein
     * StaticObjectPrefab, dessen Prefab eine `Prefabs.SubLane` traegt; beim
     * Setzen erzeugt CS2 daraus selbst eine
     * `Invisible Parking Lane - Perpendicular 2.9x5.9` mit `Net.ParkingLane`.
     * Ein Decal liefert also Markierung UND funktionierenden Stellplatz in
     * einem - wir muessen keine Spur von Hand bauen.
     *
     * Wo die Decals hingehoeren, rechnet `ParkingBayDecals` aus. Das ist
     * reine Geometrie und liegt deshalb im Geometry-Teil, wo der
     * Paritaetstest sie erreicht. Hier steht nur noch die Uebergabe an CS2.
     */
    public sealed partial class ParkingLotToolSystem
    {
        /**
         * Die Aufkleber, gefunden im Prefab-Blob des Spiels.
         *
         * CS2 SIMULIERT weder Behinderten- noch Elektroplaetze - in Game.dll
         * gibt es kein Charging, kein Handicap, kein Accessib*, und
         * `ParkingLaneFlags.ParkingDisabled` heisst "Parken hier abgeschaltet".
         * Die Aufkleber sind also reine Grafik; funktional bleibt es normales
         * Parken. Genau so macht es der Vanilla-Parkplatz auch.
         *
         * Es gaebe ausserdem Diagonal-, Laengs- und Motorradvarianten sowie
         * reine Symbolaufkleber (ParkingIcon*Decal01) zum Drueberlegen.
         */
        private const string BayDecalName = "ParkingLotDecal01";
        private const string DisabledDecalName = "ParkingLotDisabledDecal01";
        private const string ElectricDecalName = "ParkingLotElectricDecal01";
        /**
         * Die Ladesaeule - anders als die Aufkleber ein echtes Objekt.
         *
         * Sie ist der Grund, warum es Elektroplaetze nur PAARWEISE gibt: eine
         * Saeule steht zwischen zwei Buchten und bedient beide. Funktional
         * aendert auch sie nichts (CS2 simuliert kein Laden), aber ohne sie
         * saehe ein Elektroplatz aus wie ein normaler mit anderem Aufkleber.
         */
        private const string ChargerName = "ParkingLotCharger01";

        /**
         * MARKIERUNG AUS, PARKEN BLEIBT - ueber ein eigenes Prefab.
         *
         * Am 2026-08-22 hat der Nutzer `ParkingLotDecal04` gesetzt und
         * untersuchen lassen. Die Bauteilliste zeigt die Trennung, auf die es
         * ankommt:
         *
         *     Prefab-Komponenten: ... Prefabs.SubLane, Prefabs.SubMesh ...
         *     Spur "Invisible Parking Lane - Perpendicular 2.9x5.9"
         *         Net.ParkingLane, Besitzer = das Decal
         *
         * `SubLane` macht das Parken, `SubMesh` macht die Linien. Wer die
         * Markierung loswerden will, muss also nicht die Spur von Hand setzen
         * (daran waere die Anbindung gescheitert), sondern nur ein Prefab
         * ohne Netz haben. `m_Meshes` leer heisst kein `SubMesh` - und CS2
         * erzeugt die Spur weiterhin selbst, mit demselben Besitzer wie sonst.
         *
         * Das Original traegt `Builtin, ReadOnly` und laesst sich nicht
         * aendern - also kopieren.
         */
        private const string UnsichtbarName = "PLT Invisible Parking Bay";
        private Entity _unsichtbaresDecalPrefab = Entity.Null;
        private bool _unsichtbarFehlgeschlagen;

        /** Frame, in dem angemeldet wurde - vorher darf es niemand benutzen. */
        private int _unsichtbarFrame = -1;

        /**
         * Ist das unsichtbare Prefab da UND von CS2 fertig eingerichtet?
         *
         * Drei Bedingungen, jede aus einem Fehlschlag gelernt:
         * die Entity existiert, CS2 hat ihr die Objektdaten gegeben, und seit
         * dem Anmelden sind mindestens zwei Frames vergangen. Am 2026-08-22
         * stuerzte das Spiel beim Bauen ab, weil das Prefab zwar da war, aber
         * noch nicht fertig - der Archetyp entsteht erst spaeter.
         */
        /**
         * STILLGELEGT, NACHDEM ES ZWEIMAL ABGESTUERZT IST.
         *
         * Ein StaticObjectPrefab OHNE `m_Meshes` laesst sich anlegen - CS2
         * meldet nichts - aber sobald der Bau es benutzt, reisst es das Spiel
         * nativ mit, ohne Stack und ohne Logzeile. Naheliegendste Erklaerung:
         * ein Objekt ohne Netz hat keine Geometrie, und die Platzierung
         * greift trotzdem darauf zu.
         *
         * DER HINWEIS DES NUTZERS WAR DER AUSSCHLAG: *"Ich habe das Gefuehl,
         * dass du das Prefab, wenn du eins erstellt hast, nicht live laden
         * kannst, sondern wir das vorher erstellen muessen."* Genau so ist es.
         * CS2 baut seine Prefab-Archetypen beim LADEN; ein Prefab, das
         * mitten im Spiel dazukommt, ist nie vollstaendig eingerichtet - und
         * der Bau reisst dann das Spiel mit, ohne Log und ohne Stack.
         *
         * Angelegt wird es deshalb jetzt in `OnGameLoadingComplete`, wenn die
         * Prefabs des Spiels stehen und die Welt noch nicht laeuft.
         */
        private const bool UnsichtbaresDecalErlaubt = true;

        private bool UnsichtbaresDecalBereit => UnsichtbarGrund() == null;

        /**
         * WARUM das unsichtbare Prefab nicht benutzt wird - oder null, wenn es
         * benutzbar ist.
         *
         * Vorher war das eine Kette aus sieben `&&`. Sachlich richtig, aber
         * STUMM: fiel eine Bedingung, tat der Schalter einfach nichts, und im
         * Log stand darueber kein Wort. Am 2026-08-24 meldete der Nutzer, das
         * Abschalten funktioniere "nicht mehr richtig" - und es gab keine
         * einzige Zeile, die die Frage haette beantworten koennen. Genau der
         * Fall, fuer den die Projektregel "Zaehler vor Theorie" gilt.
         *
         * Jede Bedingung nennt jetzt ihren eigenen Grund.
         */
        private string UnsichtbarGrund()
        {
            if (!UnsichtbaresDecalErlaubt) return "im Code abgeschaltet";
            if (_unsichtbarFehlgeschlagen)
                return "das Anlegen ist fehlgeschlagen (siehe fruehere Logzeile)";
            if (_unsichtbaresDecalPrefab == Entity.Null)
                return "nie angemeldet - OnGameLoadingComplete lief nicht oder "
                    + $"fand '{BayDecalName}' zu dem Zeitpunkt noch nicht";
            if (_unsichtbarFrame < 0) return "kein Anmeldeframe vermerkt";
            if (UnityEngine.Time.frameCount <= _unsichtbarFrame + 2)
                return "erst seit "
                    + (UnityEngine.Time.frameCount - _unsichtbarFrame)
                    + " Frame(s) angemeldet - CS2 baut den Archetyp erst";
            if (!EntityManager.Exists(_unsichtbaresDecalPrefab))
                return "die Prefab-Entity gibt es nicht mehr";
            if (!EntityManager.HasComponent<ObjectGeometryData>(
                    _unsichtbaresDecalPrefab))
                return "ObjectGeometryData fehlt";
            if (!EntityManager.HasComponent<PlaceableObjectData>(
                    _unsichtbaresDecalPrefab))
                return "PlaceableObjectData fehlt";
            if (!EntityManager.HasComponent<Game.Prefabs.SubLane>(
                    _unsichtbaresDecalPrefab))
                return "Prefabs.SubLane fehlt - ohne sie gaebe es keine "
                    + "Stellplaetze, nur unsichtbares Nichts";
            return null;
        }

        /**
         * Einmal anlegen, ab dem naechsten Frame benutzbar.
         *
         * ANLEGEN UND BENUTZEN NIE IM SELBEN FRAME. Daran ist der Mod schon
         * einmal nativ abgestuerzt, ohne Stack - CS2 baut den Archetyp erst
         * danach. Deshalb laeuft das hier im Werkzeug-Durchlauf mit, statt
         * beim Bauen.
         */
        /**
         * WELCHE OBJEKTE IM SPIEL TRAGEN EINE PARKSPUR - UND WELCHE DAVON SIND
         * UNSICHTBAR?
         *
         * Das ist die Suche, die ich am Anfang vorgeschlagen und dann
         * uebersprungen habe. Statt dessen habe ich zweimal ein eigenes
         * Prefab gebaut, und zweimal ist das Spiel abgestuerzt.
         *
         * Wenn CS2 selbst ein Objekt kennt, das Stellplaetze mitbringt aber
         * nichts zeichnet, brauchen wir gar nichts zu bauen - dann tauscht
         * der Schalter nur das Prefab. Gebaeude mit eingebautem Parken
         * brauchen genau so etwas.
         *
         * `SubMesh` ist dabei die Antwort auf "sichtbar": am untersuchten
         * ParkingLotDecal04 stand genau diese Komponente neben `SubLane`.
         */
        private void LogParkspurPrefabs(NativeArray<Entity> prefabs)
        {
            try
            {
                var gefunden = 0;
                var unsichtbare = 0;
                for (var i = 0; i < prefabs.Length; i++)
                {
                    var entity = prefabs[i];
                    if (!EntityManager.HasComponent<Game.Prefabs.SubLane>(entity))
                        continue;
                    gefunden++;
                    var sichtbar = EntityManager.HasComponent<SubMesh>(entity);
                    if (!sichtbar) unsichtbare++;
                    if (!_prefabSystem.TryGetPrefab<PrefabBase>(entity, out var prefab)
                        || prefab == null) continue;
                    /*
                     * NUR DIE AUSNAHMEN NENNEN.
                     *
                     * Hier stand eine Zeile je Prefab - 1975 Stueck bei
                     * JEDEM Spielstart, gemessen am 2026-09-14: 79 % des
                     * ganzen Modlogs, geschrieben in 0,66 Sekunden. Die
                     * Zusammenfassung darunter sagt dasselbe in einer Zeile,
                     * und interessant ist ohnehin nur, WELCHES Prefab kein
                     * sichtbares Netz hat. Zuletzt waren das null.
                     */
                    if (!sichtbar)
                        Mod.log.Info($"PLT-Parkspur-Prefab: '{prefab.name}' "
                            + $"[{prefab.GetType().Name}] OHNE Netz "
                            + "(UNSICHTBAR)");
                }
                Mod.log.Info($"PLT-Parkspur-Prefabs: {gefunden} Objekte mit "
                    + $"SubLane, davon {unsichtbare} ohne sichtbares Netz.");
            }
            catch (Exception ausnahme)
            {
                Mod.log.Warn("PLT: Parkspur-Prefabs nicht lesbar: "
                    + ausnahme.Message);
            }
        }

        /**
         * CS2 baut seine Prefab-Archetypen beim LADEN.
         *
         * Deshalb entsteht das unsichtbare Prefab hier und nicht im laufenden
         * Frame: `OnGameLoadingComplete` liegt nach dem Laden der Spielprefabs
         * und vor dem ersten Spielframe. Genau das hatte der Nutzer vermutet,
         * nachdem zwei Versuche im laufenden Spiel das Spiel mitgerissen
         * hatten.
         */
        protected override void OnGameLoadingComplete(
            Colossal.Serialization.Entities.Purpose purpose, Game.GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (mode != Game.GameMode.Game && mode != Game.GameMode.Editor) return;
            // Der Traegertest hat am 2026-08-25 nach Save/Load 109 -> 0
            // SubNet-Eintraege gemessen. Noch vor dem ersten Spielframe wird
            // der Puffer deshalb aus der exakt referenzierten PLT-Flaeche
            // wiederhergestellt; ein spaeter Anlass findet ihn bereits vor.
            RestoreCarrierSubNetsAfterLoad();
            ResolveBayDecalPrefabs();
            PflegeUnsichtbaresDecal();
        }

        private bool _unsichtbarGemeldet;

        /**
         * Einmal melden, sobald CS2 das Prefab fertig eingerichtet hat.
         *
         * Ohne diese Zeile war der einzige Beleg fuer den Zustand des Prefabs
         * die Anmeldezeile beim Laden - und die sagt nur, dass es ANGELEGT
         * wurde, nicht dass CS2 seinen Archetyp gebaut hat. Genau dazwischen
         * liegt der Fall, in dem der Schalter stumm nichts tut.
         */
        private void MeldeUnsichtbaresDecalBereit()
        {
            if (_unsichtbarGemeldet) return;
            if (_unsichtbaresDecalPrefab == Entity.Null) return;
            if (UnsichtbarGrund() != null) return;
            _unsichtbarGemeldet = true;
            Mod.log.Info($"PLT: '{UnsichtbarName}' ist einsatzbereit "
                + "(Archetyp steht, SubLane vorhanden). Der Schalter "
                + "'Buchtsymbole' kann ab jetzt wirklich abschalten.");
        }

        private void PflegeUnsichtbaresDecal()
        {
            if (!UnsichtbaresDecalErlaubt) return;
            if (_unsichtbarFehlgeschlagen) return;
            if (_unsichtbaresDecalPrefab != Entity.Null) return;
            if (_bayDecalPrefab == Entity.Null || _prefabSystem == null) return;
            try
            {
                if (!_prefabSystem.TryGetPrefab<ObjectGeometryPrefab>(
                        _bayDecalPrefab, out var original) || original == null)
                {
                    _unsichtbarFehlgeschlagen = true;
                    Mod.log.Warn($"PLT: '{BayDecalName}' ist kein "
                        + "ObjectGeometryPrefab - unsichtbare Buchten gibt es "
                        + "deshalb nicht.");
                    return;
                }
                /**
                 * NICHT DAS PREFAB KLONEN - EIN NEUES BAUEN.
                 *
                 * `Object.Instantiate(prefab)` macht eine flache Kopie: die
                 * Komponentenliste zeigt danach auf DIESELBEN Objekte wie das
                 * Original. CS2 merkt das sofort und meldet
                 * "Component on prefab ... is referenced from another prefab";
                 * der halbfertige Klon riss beim Bauen das Spiel mit.
                 *
                 * Also ein frisches Prefab, und die Komponenten einzeln
                 * kopiert. `m_Meshes` bleibt leer - das ist der ganze
                 * Unterschied zum Original.
                 */
                var klon = ScriptableObject.CreateInstance<StaticObjectPrefab>();
                klon.name = UnsichtbarName;
                klon.m_Meshes = new ObjectMeshInfo[0];
                klon.m_Circular = original.m_Circular;
                /**
                 * KOPIEREN GEHT NUR MIT `AddComponentFrom` - NICHT MIT
                 * `Instantiate` PLUS `components.Add`.
                 *
                 * Das ist die Wurzel von zwei Fehlern, die ich nacheinander
                 * falsch behandelt habe. `PrefabBase.AddComponent` (Game.dll)
                 * macht drei Dinge:
                 *
                 *     componentBase.name = type.Name;
                 *     componentBase.prefab = this;      <- die Rueckverknuepfung
                 *     components.Add(componentBase);
                 *
                 * `AddComponentFrom` ruft das auf und kopiert danach die Werte
                 * per JsonUtility - also ein echter Wertetausch ohne geteilte
                 * Referenzen. Genau so kopiert CS2 seine Prefabs selbst
                 * (`PrefabBase.cs`, Zeile 412).
                 *
                 * `Object.Instantiate` setzt `prefab` NICHT. `OnEnable` haette
                 * es nachgeholt, laeuft aber nur beim `CreateInstance` - also
                 * bevor hier ueberhaupt eine Komponente in der Liste steht.
                 * Deshalb war `base.prefab` null, und deshalb kam beim Laden
                 *
                 *   Error when initializing prefab: PLT Invisible Parking Bay
                 *
                 * aus `SubObjectDefaultProbability.Initialize`, das als erste
                 * Zeile `base.prefab.Has<ServiceUpgrade>()` liest.
                 *
                 * DER TEURE TEIL: am 2026-08-22 habe ich daraufhin diese eine
                 * Komponente uebersprungen. Sie ist aber die einzige, die
                 *
                 *     components.Add(ComponentType.ReadWrite<PlaceableObjectData>());
                 *
                 * beitraegt. Ohne sie fehlte dem Prefab `PlaceableObjectData`,
                 * die Bereitschaftspruefung sagte Nein, und der Schalter tat
                 * stumm nichts - zwei Tage lang, bis die Meldezeile vom
                 * 2026-08-24 es in einem Satz sagte. Ich habe das Symptom
                 * behandelt und dabei das Teil weggeworfen, an dem alles hing.
                 *
                 * Es wird deshalb NICHT mehr uebersprungen.
                 */
                var kopiert = new List<string>();
                foreach (var bauteil in original.components)
                {
                    if (bauteil == null) continue;
                    klon.AddComponentFrom(bauteil);
                    kopiert.Add(bauteil.GetType().Name);
                }
                if (!_prefabSystem.AddPrefab(klon))
                {
                    _unsichtbarFehlgeschlagen = true;
                    Mod.log.Warn($"PLT konnte '{UnsichtbarName}' nicht anmelden.");
                    return;
                }
                _unsichtbaresDecalPrefab = _prefabSystem.GetEntity(klon);
                _unsichtbarFrame = UnityEngine.Time.frameCount;
                Mod.log.Info($"PLT hat '{UnsichtbarName}' angemeldet - eine "
                    + $"Kopie von '{BayDecalName}' ohne Netz. Benutzt wird sie "
                    + "erst, wenn CS2 ihren Archetyp gebaut hat.");
                // Welche Bauteile mitgekommen sind. Ohne diese Zeile war nicht
                // zu sehen, dass eine fehlte - und genau eine fehlende hat den
                // Schalter zwei Tage lang stillgelegt.
                Mod.log.Info($"PLT-Bauteile von '{UnsichtbarName}': "
                    + (kopiert.Count == 0 ? "KEINE" : string.Join(", ", kopiert)));
            }
            catch (Exception ausnahme)
            {
                _unsichtbarFehlgeschlagen = true;
                Mod.log.Error(ausnahme, $"PLT konnte '{UnsichtbarName}' nicht "
                    + "anlegen; die Markierung bleibt sichtbar.");
            }
        }

        /**
         * Zeigt die +Z-Achse der Saeule zur Fahrgasse hin?
         *
         * NOCH NICHT IM SPIEL GEMESSEN. Bei den Aufklebern war es umgekehrt
         * als erwartet, deshalb steht das hier als einzelner Schalter: eine
         * Sichtprobe entscheidet, kein Umbau. Angenommen ist "ja", weil die
         * Vorderseite einer Saeule zu den Autos zeigt.
         */
        private const bool ChargerFacesAisle = true;

        /**
         * Welches Modell die Ladesaeule zeigt.
         *
         * Das Prefab bringt mehrere Modelle mit; ausgewaehlt wird ueber den
         * Zufallswert. 1 ist der erste Versuch - stimmt das Modell im Spiel
         * nicht, ist das hier die einzige Zahl, die sich aendert.
         */
        private const uint ChargerVariantSeed = 1u;

        /**
         * Zeigt die +Z-Achse des Decals zur Fahrgasse hin oder von ihr weg?
         *
         * IM SPIEL GEMESSEN: von ihr WEG. Mit `true` standen alle Aufkleber
         * um 180 Grad verdreht. Die +Z-Achse zeigt also aus der Bucht heraus
         * in Fahrtrichtung des einparkenden Autos, nicht zur Gasse hin.
         */
        private const bool BayDecalFacesAisle = false;

        private EntityQuery _bayDecalPrefabQuery;
        private Entity _bayDecalPrefab = Entity.Null;
        private Entity _disabledDecalPrefab = Entity.Null;
        private Entity _electricDecalPrefab = Entity.Null;
        private Entity _chargerPrefab = Entity.Null;
        private bool _missingBayDecalLogged;

        private void InitializeBayObjects()
        {
            _bayDecalPrefabQuery = GetEntityQuery(
                ComponentType.ReadOnly<StaticObjectData>(),
                ComponentType.ReadOnly<ObjectGeometryData>(),
                ComponentType.ReadOnly<PlaceableObjectData>(),
                ComponentType.Exclude<PlaceholderObjectElement>());
        }

        private Entity[] _cleanupPrefabs;

        /**
         * Was `ParkingLotCleanupSystem` braucht, um beim Bulldozern die
         * eigenen Objekte zu finden.
         *
         * Die Aufloesung wird hier ANGESTOSSEN, nicht nur abgefragt: nach dem
         * Laden eines Spielstands hat in dieser Sitzung vielleicht noch
         * niemand gebaut, dann stuenden die Prefabfelder auf Null - und ein
         * Parkplatz aus dem Spielstand liesse sich nicht mehr sauber
         * wegbaggern. `ResolveBayDecalPrefabs` merkt sich das Ergebnis, der
         * Aufruf ist ab dem zweiten Mal umsonst.
         *
         * Das Lot-Prefab wird NICHT angelegt, falls es fehlt - nur gelesen.
         * Ohne Parkplatz gibt es nichts aufzuraeumen.
         */
        internal bool TryGetCleanupPrefabs(out Entity lotPrefab,
                                           out Entity[] objectPrefabs)
        {
            lotPrefab = _lotOwnerPrefab;
            objectPrefabs = null;
            if (lotPrefab == Entity.Null || !EntityManager.Exists(lotPrefab))
                return false;
            if (!ResolveBayDecalPrefabs()) return false;

            /**
             * DAS UNSICHTBARE PREFAB GEHOERT MIT IN DIESE LISTE.
             *
             * Bis zum 2026-08-24 stand es nicht drin. Der Aufraeumer erkennt
             * eigene Objekte allein am Prefab - ein Parkplatz, der mit
             * abgeschalteter Markierung gebaut wurde, liess sich also gar
             * nicht sauber wegbaggern: die unsichtbaren Buchten blieben mit
             * ihren Parkspuren stehen, und Autos parkten weiter auf einer
             * Flaeche, auf der nichts mehr zu sehen war.
             *
             * `Entity.Null` schadet nicht: der Vergleich laeuft gegen das
             * Prefab echter Entities, und das ist nie Null.
             */
            _cleanupPrefabs ??= new Entity[5];
            _cleanupPrefabs[0] = _bayDecalPrefab;
            _cleanupPrefabs[1] = _disabledDecalPrefab;
            _cleanupPrefabs[2] = _electricDecalPrefab;
            _cleanupPrefabs[3] = _chargerPrefab;
            _cleanupPrefabs[4] = _unsichtbaresDecalPrefab;
            objectPrefabs = _cleanupPrefabs;
            return true;
        }

        /**
         * Loest alle drei Aufkleber in EINEM Durchlauf auf.
         *
         * Nur der normale ist Pflicht. Fehlt ein Sonderaufkleber, bleiben die
         * betroffenen Plaetze unmarkiert - das ist besser, als deswegen den
         * ganzen Parkplatz nicht zu bauen.
         */
        private bool ResolveBayDecalPrefabs()
        {
            if (_bayDecalPrefab != Entity.Null && EntityManager.Exists(_bayDecalPrefab))
                return true;

            _bayDecalPrefab = Entity.Null;
            _disabledDecalPrefab = Entity.Null;
            _electricDecalPrefab = Entity.Null;
            _chargerPrefab = Entity.Null;
            using var prefabs = _bayDecalPrefabQuery.ToEntityArray(Allocator.TempJob);
            for (var i = 0; i < prefabs.Length; i++)
            {
                if (!_prefabSystem.TryGetPrefab<PrefabBase>(prefabs[i], out var prefab)
                    || prefab == null || !prefab.isBuiltin) continue;
                if (string.Equals(prefab.name, BayDecalName, StringComparison.Ordinal))
                    _bayDecalPrefab = prefabs[i];
                else if (string.Equals(prefab.name, DisabledDecalName,
                             StringComparison.Ordinal))
                    _disabledDecalPrefab = prefabs[i];
                else if (string.Equals(prefab.name, ElectricDecalName,
                             StringComparison.Ordinal))
                    _electricDecalPrefab = prefabs[i];
                else if (string.Equals(prefab.name, ChargerName,
                             StringComparison.Ordinal))
                    _chargerPrefab = prefabs[i];
            }
            LogChargerPrefabSurvey(prefabs);
            LogParkspurPrefabs(prefabs);

            if (_bayDecalPrefab != Entity.Null)
            {
                _missingBayDecalLogged = false;
                Mod.log.Info($"PLT-Stellplätze: '{BayDecalName}' gefunden, "
                    + $"'{DisabledDecalName}' "
                    + (_disabledDecalPrefab != Entity.Null ? "gefunden" : "FEHLT")
                    + $", '{ElectricDecalName}' "
                    + (_electricDecalPrefab != Entity.Null ? "gefunden" : "FEHLT")
                    + $", '{ChargerName}' "
                    + (_chargerPrefab != Entity.Null ? "gefunden" : "FEHLT")
                    + ".");
                // Die Masse der Saeule sind noch nicht im Spiel geprueft.
                // Statt sie zu raten, werden sie hier ausgelesen - dann steht
                // beim ersten Bau im Log, ob sie zwischen zwei 3,0-m-Buchten
                // passt und wie tief sie ist.
                if (_chargerPrefab != Entity.Null
                    && EntityManager.HasComponent<ObjectGeometryData>(_chargerPrefab))
                {
                    var geometry = EntityManager
                        .GetComponentData<ObjectGeometryData>(_chargerPrefab);
                    var size = geometry.m_Bounds.max - geometry.m_Bounds.min;
                    Mod.log.Info($"PLT-Ladesäule '{ChargerName}' gemessen: "
                        + $"{size.x:F2} breit x {size.z:F2} tief x {size.y:F2} hoch, "
                        + $"Pivot-Versatz z {(geometry.m_Bounds.min.z + geometry.m_Bounds.max.z) / 2:F2} m.");
                    /*
                     * WARUM DIE SAEULEN UNSICHTBAR SIND - die entscheidende
                     * Zeile, gemessen statt vermutet.
                     *
                     * `Game.Objects.OverrideSystem` (Game.dll, Zeile 383)
                     * setzt `overridableCollision`, sobald das Objekt selbst
                     *
                     *   (m_Flags & (Overridable | DeleteOverridden)) == Overridable
                     *
                     * ist und mit irgendetwas kollidiert. Dann bekommt es
                     * `Overridden` - und ein ueberschriebenes Objekt erhaelt
                     * im `PreCullingSystem` NIE eine echte Render-Ebene. Es
                     * steht also da, ist aber nicht zu sehen. Genau das
                     * meldet der Nutzer: platziert, aber unsichtbar.
                     *
                     * Aufkleber tragen das Flag nicht, die Saeule womoeglich
                     * schon - dann waere sie ihr eigenes Opfer.
                     */
                    var over = (geometry.m_Flags
                        & Game.Objects.GeometryFlags.Overridable) != 0;
                    var del = (geometry.m_Flags
                        & Game.Objects.GeometryFlags.DeleteOverridden) != 0;
                    Mod.log.Info($"PLT-Ladesäule Geometrieflags: {geometry.m_Flags}"
                        + " -> bei Kollision "
                        + (over && !del
                            ? "WIRD SIE UEBERSCHRIEBEN und damit unsichtbar"
                            : "bleibt sie sichtbar"));
                }
                return true;
            }

            if (!_missingBayDecalLogged)
            {
                _missingBayDecalLogged = true;
                RecordPreviewDiagnostic("Warning",
                    $"Stellplatz-Decal '{BayDecalName}' ist noch nicht auflösbar.");
                Mod.log.Warn($"PLT wartet auf das Prefab '{BayDecalName}'.");
            }
            return false;
        }

        /**
         * OHNE UNSICHTBARES PREFAB TUT DER SCHALTER GAR NICHTS.
         *
         * Zwischenstand am 2026-08-22 war schlechter als kein Schalter: er
         * ersetzte die Sonderplaetze durch normale Markierungen, ohne dafuer
         * irgendetwas zu verbergen. Der Nutzer sah nur, dass seine
         * Behinderten- und E-Plaetze verschwanden.
         *
         * Also: nur wenn das unsichtbare Prefab wirklich bereit ist, wird
         * getauscht - und dann fuer ALLE Buchtarten. Sonst bleibt alles, wie
         * es ist.
         *
         * Die Entscheidung faellt EINMAL je Bau in
         * `CreateBayDecalDefinitions` und wird hier nur noch angewandt.
         * Vorher fragte diese Methode je Bucht neu - bei 322 Buchten also
         * 322 Mal dieselbe Antwort, und keine davon stand irgendwo.
         */
        private Entity PrefabFor(ParkingBayDecals.DecalKind kind, bool unsichtbar)
        {
            if (unsichtbar) return _unsichtbaresDecalPrefab;
            switch (kind)
            {
                case ParkingBayDecals.DecalKind.Disabled: return _disabledDecalPrefab;
                case ParkingBayDecals.DecalKind.Electric: return _electricDecalPrefab;
                default: return _bayDecalPrefab;
            }
        }

        private int CreateBayDecalDefinitions(ParkingLayout layout,
                                              LayoutSettings settings,
                                              ref TerrainHeightData heightData)
        {
            if (layout?.Bay == null || layout.Bay.Length == 0 || settings == null)
                return 0;
            if (!ResolveBayDecalPrefabs()) return 0;

            var plan = ParkingBayDecals.Plan(layout, settings);
            if (plan.Placements.Length == 0) return 0;

            /**
             * DIE EINE ENTSCHEIDUNG - UND SIE STEHT AB JETZT IM LOG.
             *
             * Der Schalter allein reicht nicht: ohne fertiges Prefab
             * verschluckte ein Tausch die Sonderplaetze, ohne dafuer etwas zu
             * verbergen. "Schalter aus" und "Prefab bereit" sind deshalb zwei
             * getrennte Aussagen; erst ihre Kombination erklaert, was der
             * Nutzer im Spiel sieht.
             */
            var markierungAus = !(_uiSystem?.Buchtsymbole ?? true);
            var grund = markierungAus ? UnsichtbarGrund() : null;
            var unsichtbar = markierungAus && grund == null;

            var random = new Unity.Mathematics.Random((uint)Environment.TickCount | 3u);
            var created = 0;
            var skipped = 0;
            var unsichtbarGesetzt = 0;
            var fehlgeschlagen = 0;
            for (var i = 0; i < plan.Placements.Length; i++)
            {
                var placement = plan.Placements[i];
                var prefab = PrefabFor(placement.Kind, unsichtbar);
                if (prefab == Entity.Null) { skipped++; continue; }
                var facing = BayDecalFacesAisle
                    ? placement.Facing : -placement.Facing;
                if (!CreateBayDecalDefinition("BayDecal", i, prefab, placement.Center,
                        facing, ref heightData, ref random))
                {
                    /*
                     * FEHLGESCHLAGEN IST NICHT UEBERSPRUNGEN.
                     *
                     * Bis zum 2026-09-01 sprang die Schleife hier still
                     * weiter: weder `created` noch `skipped` stieg, und die
                     * Logzeile darunter nannte nur `created` - ohne die Zahl
                     * der GEPLANTEN Aufkleber. Fehlte also einer, sah man es
                     * nirgends. Genau danach hat der Nutzer gefragt:
                     * "Decals, die nicht gesetzt werden".
                     */
                    fehlgeschlagen++;
                    continue;
                }
                created++;
                if (unsichtbar) unsichtbarGesetzt++;
            }

            /**
             * DIE LADESAEULE FOLGT DEM SCHALTER.
             *
             * Sie war bis zum 2026-08-24 davon ausgenommen. Wer die
             * Markierungen abschaltete, bekam einen Parkplatz ohne jeden
             * Aufkleber - und mittendrin bis zu vier Ladesaeulen, die auf
             * Elektroplaetze zeigten, die man nicht mehr sehen konnte. Ohne
             * den E-Aufkleber gibt es nichts mehr, wozu die Saeule gehoert.
             */
            var chargers = unsichtbar
                ? 0
                : CreateChargerDefinitions(layout, settings, plan, ref heightData);

            Mod.log.Info($"PLT-Stellplätze: {created} von "
                + $"{plan.Placements.Length} geplanten Decals gesetzt "
                + $"({plan.Normal} normal, {plan.Disabled} behindert, "
                + $"{plan.Electric} elektro) für {plan.Stalls} von "
                + $"{layout.Stalls} Buchten; {plan.Unrecognized} Vierecke "
                + $"unerkannt, {skipped} ohne verfügbares Prefab, "
                + $"{fehlgeschlagen} beim Setzen fehlgeschlagen. "
                + $"Ladesäulen: {chargers} von {plan.Chargers.Length} "
                + $"(je eine zwischen zwei Elektrobuchten).");
            ParkingLotLiveLog.Zeile("decals " + created + "/"
                + plan.Placements.Length + " gesetzt | buchten "
                + plan.Stalls + "/" + layout.Stalls
                + " | unerkannt " + plan.Unrecognized
                + " | ohne prefab " + skipped
                + " | fehlgeschlagen " + fehlgeschlagen
                + " | ladesaeulen " + chargers + "/" + plan.Chargers.Length);
            MeldeMarkierungszustand(markierungAus, grund, created,
                unsichtbarGesetzt, chargers, plan.Chargers.Length);
            return created + chargers;
        }

        /**
         * Was der Schalter gerade wirklich getan hat.
         *
         * Absichtlich eine eigene Zeile neben der Stellplatzzeile: die zaehlt
         * den PLAN (normal/behindert/elektro) und sieht deshalb identisch aus,
         * egal welches Prefab tatsaechlich gesetzt wurde. Genau daran liess
         * sich am 2026-08-24 aus dem Log nicht ablesen, ob das Abschalten
         * ueberhaupt gegriffen hat.
         *
         * Der Fall "Schalter aus, aber Prefab nicht bereit" ist der einzige,
         * der den Nutzer aktiv taeuscht - er sieht Markierungen, obwohl er sie
         * abgeschaltet hat. Deshalb geht genau der zusaetzlich in den Abzug.
         */
        private void MeldeMarkierungszustand(bool markierungAus, string grund,
                                             int decals, int unsichtbare,
                                             int saeulen, int saeulenGeplant)
        {
            if (!markierungAus)
            {
                Mod.log.Info($"PLT-Markierung: Schalter AN - {decals} sichtbare "
                    + $"Aufkleber, {saeulen} von {saeulenGeplant} Ladesaeulen.");
                return;
            }
            if (grund == null)
            {
                Mod.log.Info($"PLT-Markierung: Schalter AUS und '{UnsichtbarName}' "
                    + $"bereit - {unsichtbare} von {decals} Buchten ohne "
                    + $"Aufkleber gesetzt, {saeulenGeplant} Ladesaeule(n) "
                    + "ausgelassen. Geparkt wird trotzdem.");
                return;
            }
            Mod.log.Warn($"PLT-Markierung: Schalter AUS, aber '{UnsichtbarName}' "
                + $"ist NICHT benutzbar ({grund}). Alle {decals} Buchten "
                + "behalten deshalb ihre sichtbare Markierung.");
            RecordPreviewDiagnostic("Warning", T(
                "Markierungen sind abgeschaltet, aber das unsichtbare Prefab ist "
                + $"nicht benutzbar ({grund}) - die Markierungen bleiben sichtbar.",
                "Bay markings are switched off, but the invisible prefab is not "
                + $"usable ({grund}) - the markings stay visible."));
        }

        private bool CreateBayDecalDefinition(
            string label,
            int index,
            Entity prefab,
            double2 center,
            double2 facing,
            ref TerrainHeightData heightData,
            ref Unity.Mathematics.Random random)
        {
            var point = new float2((float)center.x, (float)center.y);
            if (!math.all(math.isfinite(point)))
                throw new InvalidOperationException(
                    $"{label} hat eine nicht-endliche Position.");

            var height = TerrainUtils.SampleHeight(
                ref heightData, new float3(point.x, 0f, point.y));
            if (!math.isfinite(height))
                throw new InvalidOperationException(
                    $"Die Terrain-Abtastung von {label} ist nicht endlich.");

            var position = new float3(point.x, height, point.y);
            var forward = new float3((float)facing.x, 0f, (float)facing.y);
            var rotation = math.lengthsq(forward) < 1e-9f
                ? quaternion.identity
                : quaternion.LookRotationSafe(math.normalize(forward), math.up());

            var definition = EntityManager.CreateEntity();
            EntityManager.AddComponentData(definition, new CreationDefinition
            {
                m_Prefab = prefab,
                m_RandomSeed = random.NextInt(),
            });
            EntityManager.AddComponent<Updated>(definition);
            // Feld fuer Feld wie CS2s eigenes ObjectToolBaseSystem: Probability
            // 100, PrefabSubIndex -1, Scale und Intensity 1, ParentMesh -1.
            // m_LocalPosition und m_LocalRotation bleiben ABSICHTLICH leer -
            // CS2 fuellt sie beim freien Setzen auch nicht, und der Besitzer
            // kommt bei uns erst nach dem Materialisieren dazu.
            var objectDefinition = default(ObjectDefinition);
            objectDefinition.m_Position = position;
            objectDefinition.m_Rotation = rotation;
            objectDefinition.m_Probability = 100;
            objectDefinition.m_PrefabSubIndex = -1;
            objectDefinition.m_Scale = 1f;
            objectDefinition.m_Intensity = 1f;
            objectDefinition.m_ParentMesh = -1;
            EntityManager.AddComponentData(definition, objectDefinition);
            RecordObjectDefinition(label, index, prefab, definition, position);
            return true;
        }
    }
}
