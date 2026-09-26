using Game;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * ENTFERNT ALLE PLT-PARKPLAETZE EINER STADT - vor dem Deinstallieren.
     *
     * WARUM ES DAS GEBEN MUSS. Deinstalliert jemand den Mod, ist unser Code
     * weg und niemand kann mehr etwas an den Hinterlassenschaften tun. Was im
     * Spielstand steht, zerfaellt dann in zwei Sorten:
     *
     *   Unsere Prefabs - Lot-Flaeche, Fusswege, Begleiter, Strassenklone.
     *   CS2 loescht die Objekte NICHT: es merkt sich die Kennung als
     *   "veraltet" und schreibt sie beim Speichern wieder mit
     *   (`PrefabSystem.m_LoadedObsoleteIDs`, `ResolvePrefabsSystem` ab
     *   Zeile 528). Was bleibt, ist ein Objekt, das auf eine Bauanleitung
     *   zeigt, die nichts mehr sagt - `PrefabData` abgeschaltet, keine
     *   Geometrie, keine Netz- und keine Flaechendaten.
     *
     *   Unsere Datenkomponenten - Bauzettel, Teilebeziehung, Statistik.
     *   Die wirft CS2 beim Laden weg: der Typname loest nicht auf,
     *   `ComponentSerializer.DeserializeType` meldet
     *   `Not serializable type: {0}` und der Obsolete-Serializer liest den
     *   Block nur an, um ihn zu ueberspringen.
     *
     * Die Struktur ueberlebt also, das Wissen nicht - und endgueltig verloren
     * ist es erst, wenn der Nutzer OHNE Mod speichert. Beides laesst sich
     * nachher nicht mehr heilen. Also raeumen wir auf, solange wir laufen.
     *
     * WARUM IN DEN EINSTELLUNGEN UND NICHT IM PANEL. Wer ans Deinstallieren
     * denkt, geht in die Modliste, nicht in ein Werkzeug. Und von dort aus
     * sieht man, ob ueberhaupt ein Spielstand geladen ist: im Hauptmenue ist
     * der Knopf ausgegraut, statt ins Leere zu greifen.
     *
     * WORAN EIN PARKPLATZ ERKANNT WIRD: am Namen seines Prefabs, nicht an
     * seinen Komponenten. `ParkingLotAltbestandSystem` verlangt Bauzettel UND
     * Traegerreferenz - das ist dort Absicht, weil es nur alt gebaute
     * Parkplaetze anbieten darf.
     *
     * Von diesen beiden Bedingungen faellt hier der BAUZETTEL weg: ein
     * Parkplatz ohne ihn ist trotzdem einer, und ihn stehenzulassen waere
     * genau der Fehler. Die TRAEGERREFERENZ bleibt - aber nicht als
     * Erkennungsmerkmal, sondern weil der Aufraeumer sie braucht, um den
     * Auftrag ueberhaupt anzunehmen. Flaechen ohne sie werden gezaehlt und
     * gemeldet, nicht markiert; siehe den Kommentar bei `_flaechen`.
     *
     * GELOESCHT WIRD NUR DAS LOT. Den Rest raeumt der vorhandene
     * relationsbasierte Aufraeumer ab, genau wie beim Bulldozer, mitsamt
     * Leitungsabriss und Wirtschafts-Gegenbuchung. Ein zweiter Abrissweg
     * waere eine zweite Wahrheit ueber dasselbe.
     *
     * WAS ER NICHT ANFASST: gewachsene Zoninghaeuser. Die gehoeren dem
     * Spieler und sind gewoehnliche CS2-Gebaeude; sie stehen nur zufaellig
     * auf Land, das wir erschlossen haben. "Leeres Gelaende" waere deshalb zu
     * viel versprochen - uebrig bleibt das Gelaende plus die Haeuser.
     */
    public sealed partial class ParkingLotStadtreinigungSystem : GameSystemBase
    {
        private EntityQuery _flaechen;
        private EntityQuery _teile;
        private EntityQuery _markierteLots;
        private EntityQuery _besitzkinder;
        private EntityQuery _alleKanten;
        private PrefabSystem _prefabSystem;
        private ParkingLotUISystem _uiSystem;

        /** Laeuft gerade ein Abriss? `-1` heisst nein. */
        private int _erwartet = -1;
        private int _rest = -1;
        private int _offeneLots = -1;
        private int _ohneFortschritt;
        private int _durchgaenge;
        private int _uebergangen;
        private bool _gewarnt;

        /**
         * Nach so vielen Durchgaengen OHNE Fortschritt wird gemeldet.
         *
         * Nicht nach einer festen Gesamtzahl: der Aufraeumer nimmt 32 Teile
         * je Durchgang, eine grosse Stadt braucht also beliebig viele, und
         * eine feste Schranke haette dort faelschlich Alarm geschlagen. Und
         * ein Durchgang ist kein Bild: das System laeuft in `PreTool`,
         * und wieviele Durchgaenge auf ein Bild kommen, haengt am Spiel.
         * "600 Bilder sind zehn Sekunden" war schlicht falsch gerechnet.
         *
         * Stillstand ist das richtige Merkmal: solange die Restzahl faellt,
         * arbeitet jemand.
         */
        private const int StillstandsDurchgaenge = 300;

        /**
         * WAS AUF DEM KNOPF STEHT.
         *
         * Der Knopf sitzt im Optionsmenue, die Antwort landete bisher in
         * einer Logdatei. Fuer uns beim Entwickeln reicht das; ein Spieler
         * drueckt, sieht nichts passieren und drueckt nochmal.
         *
         * Der Zustand steht deshalb auf dem Knopf selbst - dort, wo geklickt
         * wurde, und ohne eine zweite Stelle im Spiel. Statisch, damit die
         * Beschriftungsquelle ihn ohne Umweg ueber die Welt lesen kann; beim
         * Stadtwechsel wird er zurueckgesetzt.
         */
        internal enum Knopfzustand
        {
            Bereit, Laeuft, Fertig, Haengt, Nichts,
        }

        internal static Knopfzustand Stand { get; private set; }
            = Knopfzustand.Bereit;

        /** Zahlen fuer die Beschriftung: entfernt, uebrig, stehengeblieben. */
        internal static int StandEntfernt { get; private set; }
        internal static int StandUebrig { get; private set; }
        internal static int StandStehen { get; private set; }

        /**
         * Setzt den Zustand und frischt die Beschriftungen auf.
         *
         * `ReloadActiveLocale` laesst CS2 unsere Quelle neu lesen, und damit
         * aendert sich der Knopftext im offenen Menue. Nur bei echter
         * Aenderung - jedes Bild aufzufrischen waere teuer und voellig
         * unnoetig.
         */
        private static void SetzeStand(Knopfzustand neu, int entfernt = 0,
            int uebrig = 0, int stehen = 0)
        {
            if (Stand == neu && StandEntfernt == entfernt
                && StandUebrig == uebrig && StandStehen == stehen) return;
            Stand = neu;
            StandEntfernt = entfernt;
            StandUebrig = uebrig;
            StandStehen = stehen;
            try
            {
                Game.SceneFlow.GameManager.instance?.localizationManager
                    ?.ReloadActiveLocale();
            }
            catch (System.Exception ausnahme)
            {
                // Der Knopftext ist Beiwerk; das Log bleibt die Wahrheit.
                Mod.log.Warn("PLT-Stadtreinigung: Knopftext nicht "
                    + "aufgefrischt (" + ausnahme.Message + ").");
            }
        }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _uiSystem = World.GetOrCreateSystemManaged<ParkingLotUISystem>();

            /*
             * NUR, WAS DER AUFRAEUMER AUCH ANNIMMT.
             *
             * `ParkingLotCleanupSystem` verlangt `ParkingLotCarrierReference`
             * am geloeschten Lot (dort Zeile 74-83). Ein Lot ohne diese
             * Referenz wuerde von uns zwar markiert, aber nie abgeraeumt -
             * Vanilla zerstoerte die Flaeche, und Teile, Traeger und
             * Leitungen blieben als Waisen stehen. Solche Faelle werden
             * deshalb GEMELDET statt markiert.
             */
            _flaechen = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Areas.Area>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });

            /*
             * DIE RESTPRUEFUNG MISST TEILE, NICHT LOTS.
             *
             * Der erste Anlauf zaehlte wieder Lots - und die Lotabfrage
             * schliesst `Deleted` aus. Sobald markiert war, stand die Zahl
             * also auf 0 und der erste Durchgang meldete "fertig", egal ob
             * irgendetwas abgerissen war. Eine Pruefung, die das Markieren
             * selbst als Erfolg liest, belohnt genau das Nichtstun, das sie
             * finden soll.
             *
             * Dieselbe Abfrage benutzt der Aufraeumer fuer seine Teile
             * (`ParkingLotCleanupSystem` Zeile 87-94). Sie faellt erst, wenn
             * wirklich etwas verschwindet.
             */
            _teile = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ParkingLotPartRelation>(),
                },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });

            /*
             * NULL TEILE HEISST NOCH NICHT FERTIG.
             *
             * Gemessen an der Abfolge im Aufraeumer: er nimmt den Auftrag im
             * ersten Durchgang an, merkt im zweiten die Teile zum Loeschen
             * vor - und stellt erst im DRITTEN das leere Teileregister fest
             * und gibt den Traeger frei. Waer die Teilezahl das einzige
             * Merkmal, meldete die Pruefung fertig, waehrend der Traeger noch
             * steht. Und an einem lebenden Traeger haelt `ParkplatzIstFort`
             * die Versorgungsleitung ausdruecklich fest.
             *
             * Also wird zusaetzlich gewartet, bis kein markiertes Lot mehr
             * da ist. Dieselbe Bedingung wie die Auftragsabfrage des
             * Aufraeumers (`ParkingLotCleanupSystem` Zeile 74-83): ist sie
             * leer, hat er nichts mehr vorliegen.
             */
            /*
             * ALLES, WAS EINEN BESITZER HAT.
             *
             * Gebraucht fuer die Listenpflege unten: CS2s Kaskade liest die
             * Puffer des Besitzers, wir kennen die Kinder aber ueber ihren
             * `Owner`. Beides muss vor dem Markieren zusammenpassen.
             */
            _besitzkinder = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Owner>() },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });

            /*
             * ALLE NETZKANTEN, NICHT NUR UNSERE.
             *
             * Fuer die Schlusspruefung, und zwar absichtlich ohne `Owner`
             * in der Bedingung. Eine Zoningstrasse, die ihren Besitzer
             * verloren hat, ist genau der Fall, den die Pruefung finden
             * soll - eine Abfrage mit `Owner` haette ihn stumm uebersehen
             * und eine beruhigende 0 gemeldet. Entschieden wird am Prefab.
             */
            _alleKanten = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Net.Edge>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });

            _markierteLots = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Areas.Area>(),
                    ComponentType.ReadOnly<ParkingLotCarrierReference>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
                None = new[] { ComponentType.ReadOnly<Temp>() },
            });
        }

        /**
         * MARKIERT DEN PARKPLATZ - MEHR BRAUCHT ES NICHT.
         *
         * Hier stand zwischendurch eine `CreationDefinition` mit
         * `CreationFlags.Delete`, dem Bulldozer nachgebaut. Die hat nie
         * gewirkt, und zwar aus zwei unabhaengigen Gruenden:
         *
         *  - `GenerateAreasSystem` (Modification1) verlangt in seiner
         *    Abfrage `CreationDefinition` UND den `Game.Areas.Node`-Puffer
         *    (Zeile 529). Unsere Definition hatte keinen; sie passte in
         *    keine Abfrage und lag nur herum.
         *  - Selbst mit Puffer erzeugt dieser Weg nur eine `Temp`-Flaeche
         *    mit `TempFlags.Delete` - eine Vorschau. Dauerhaft wird die
         *    erst, wenn ein WERKZEUG anwendet. Wir sind kein Werkzeug.
         *
         * Die Kaskade macht CS2 selbst, in `SubElementDeleteSystem`
         * (Phase `PostTool`): jede Entity mit `Deleted`, die einen
         * `SubArea`- oder `SubNet`-Puffer hat, vererbt das `Deleted` an
         * alle Kinder darin. Belag, Gras, Vorflaeche, Zoningstrasse und
         * Zufahrtsgasse haengen genau dort.
         *
         * Diese Markierung muss deshalb VOR `PostTool` im selben Bild
         * fallen. Darum laeuft dieses System in `PreTool`; die ganze
         * Herleitung steht bei der Anmeldung in `Mod.cs`.
         */
        private void MarkiereZumAbriss(Entity lot)
        {
            FuelleBesitzlisten(lot);
            EntityManager.AddComponent<Deleted>(lot);
        }

        /**
         * TRAEGT NACH, WAS DIE KASKADE SONST NICHT FINDET.
         *
         * Die Kaskade liest die Puffer des Besitzers. Wir kennen unsere
         * Kinder aber ueber ihren `Owner`, und die beiden Auskuenfte sind
         * NICHT dasselbe:
         *
         *  - Der `SubNet`-Puffer ueberlebt das Laden nicht (gemessen
         *    109 -> 0, siehe [[cs2-nackter-traeger]]). Nach einem Neustart
         *    gehoeren die Zoningstrassen dem Parkplatz weiter, stehen aber
         *    in keiner Liste - die Kaskade sieht sie nie.
         *  - Ein Teil der Kinder haengt am technischen TRAEGER, nicht an der
         *    Lot-Flaeche. Der Traeger wird erst vom Aufraeumer entfernt, in
         *    `Modification3` - da ist `PostTool` in diesem Bild vorbei, und
         *    am Bildende ist er zerstoert. Seine Kaskade liefe nie.
         *
         * Deshalb wird VOR dem Markieren alles an der LOT-FLAECHE
         * eingetragen - die wird in `PreTool` markiert, und `PostTool`
         * kommt in demselben Bild danach.
         *
         * Eintragen ist billig und idempotent: was schon drinsteht, bleibt
         * unberuehrt.
         */
        private void FuelleBesitzlisten(Entity lot)
        {
            if (!EntityManager.Exists(lot)) return;

            var traeger = Entity.Null;
            if (EntityManager.HasComponent<ParkingLotCarrierReference>(lot))
                traeger = EntityManager
                    .GetComponentData<ParkingLotCarrierReference>(lot).Carrier;

            if (!EntityManager.HasBuffer<Game.Areas.SubArea>(lot))
                EntityManager.AddBuffer<Game.Areas.SubArea>(lot);
            if (!EntityManager.HasBuffer<Game.Net.SubNet>(lot))
                EntityManager.AddBuffer<Game.Net.SubNet>(lot);

            var flaechen = 0;
            var kanten = 0;
            var fremd = 0;
            using var kinder = _besitzkinder.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < kinder.Length; i++)
            {
                var kind = kinder[i];
                if (kind == lot || kind == traeger) continue;
                var besitzer = EntityManager
                    .GetComponentData<Owner>(kind).m_Owner;
                if (besitzer != lot
                    && (traeger == Entity.Null || besitzer != traeger))
                    continue;

                if (EntityManager.HasComponent<Game.Areas.Area>(kind))
                {
                    if (TrageEin<Game.Areas.SubArea>(lot, kind,
                            (in Game.Areas.SubArea e) => e.m_Area,
                            k => new Game.Areas.SubArea(k))) flaechen++;
                }
                else if (EntityManager.HasComponent<Game.Net.Edge>(kind))
                {
                    if (TrageEin<Game.Net.SubNet>(lot, kind,
                            (in Game.Net.SubNet e) => e.m_SubNet,
                            k => new Game.Net.SubNet(k))) kanten++;
                }
                else fremd++;
            }

            if (flaechen > 0 || kanten > 0)
                Mod.log.Info("PLT-Stadtreinigung: Besitzlisten ergaenzt - "
                    + flaechen + " Flaeche(n) und " + kanten + " Strasse(n) "
                    + "gehoerten dem Parkplatz, standen aber in keiner "
                    + "Liste. Ohne diesen Eintrag haette CS2s Kaskade sie "
                    + "nicht gefunden; genau das passiert nach dem Laden, "
                    + "wo der SubNet-Puffer leer ankommt.");
            if (fremd > 0)
                Mod.log.Info("PLT-Stadtreinigung: " + fremd + " Kind(er) "
                    + "ohne Flaeche und ohne Kante - Aufkleber und "
                    + "Pflanzen. Die holt der Aufraeumer ueber die "
                    + "Teilebeziehung.");
        }

        private delegate Entity Lies<T>(in T eintrag) where T : unmanaged;

        /** true, wenn wirklich etwas dazugekommen ist. */
        private bool TrageEin<T>(Entity besitzer, Entity kind,
                                 Lies<T> lies, System.Func<Entity, T> baue)
            where T : unmanaged, IBufferElementData
        {
            var puffer = EntityManager.GetBuffer<T>(besitzer);
            for (var i = 0; i < puffer.Length; i++)
            {
                var eintrag = puffer[i];
                if (lies(in eintrag) == kind) return false;
            }
            puffer.Add(baue(kind));
            return true;
        }

        /** Laeuft gerade ein Abriss dieses Systems? */
        internal bool Laeuft => _erwartet >= 0 || _beauftragt;

        private bool _beauftragt;

        /**
         * DER SETTER STELLT NUR EINEN AUFTRAG.
         *
         * Er laeuft aus dem Optionsmenue, also in einem beliebigen Frame und
         * einer beliebigen Phase. Der Aufraeumer sammelt geloeschte Lots
         * aber nur in Phase 3 - wer danach markiert, dessen Lot kann CS2
         * noch im selben Frame zerstoeren, bevor je ein Auftrag entsteht.
         * Angenommen wird der Auftrag deshalb in `OnUpdate`, und dieses
         * System ist in `Mod.cs` ausdruecklich VOR den Aufraeumer gehaengt.
         */
        internal void Beauftrage() => _beauftragt = true;

        /**
         * Zaehlt, was in dieser Stadt steht: abraeumbare Lots, Lots ohne
         * Traegerreferenz (die der Aufraeumer NICHT annimmt) und die noch
         * lebenden Teile.
         */
        internal void Bestand(out int abraeumbar, out int ohneTraeger,
            out int teile)
        {
            _flaechen.CompleteDependency();
            abraeumbar = 0;
            ohneTraeger = 0;
            using (var flaechen = _flaechen.ToEntityArray(Allocator.Temp))
            {
                for (var i = 0; i < flaechen.Length; i++)
                {
                    if (!IstUnserLot(flaechen[i])) continue;
                    if (EntityManager.HasComponent<ParkingLotCarrierReference>(
                            flaechen[i])) abraeumbar++;
                    else ohneTraeger++;
                }
            }
            _teile.CompleteDependency();
            teile = _teile.CalculateEntityCount();
        }

        /**
         * Uebergibt alle abraeumbaren PLT-Parkplaetze dem Aufraeumer. Gibt
         * zurueck, wieviele markiert wurden.
         */
        internal int EntferneAlle()
        {
            _flaechen.CompleteDependency();
            var markiert = 0;
            var uebergangen = 0;
            using (var flaechen = _flaechen.ToEntityArray(Allocator.Temp))
            {
                // Erst sammeln, dann markieren - das liest sich besser als
                // beides ineinander. `ToEntityArray` liefert eine Kopie und
                // ueberlebt die strukturelle Aenderung ohnehin; eine Gefahr
                // gibt es hier nicht, der frueher stehende Hinweis darauf
                // war falsch.
                using var lots = new NativeList<Entity>(16, Allocator.Temp);
                for (var i = 0; i < flaechen.Length; i++)
                {
                    if (!IstUnserLot(flaechen[i])) continue;
                    if (EntityManager.HasComponent<ParkingLotCarrierReference>(
                            flaechen[i])) lots.Add(flaechen[i]);
                    else uebergangen++;
                }
                for (var i = 0; i < lots.Length; i++)
                {
                    MarkiereZumAbriss(lots[i]);
                    markiert++;
                }
            }

            Mod.log.Info("PLT-Stadtreinigung: " + markiert
                + " Parkplatz/Parkplaetze dem Aufraeumer uebergeben. "
                + "Gewachsene Zoninghaeuser bleiben stehen - sie gehoeren "
                + "dem Spieler, nicht uns.");

            if (uebergangen > 0)
                Mod.log.Warn("PLT-Stadtreinigung: " + uebergangen
                    + " Flaeche(n) tragen unser Lot-Prefab, aber KEINE "
                    + "Traegerreferenz. Der Aufraeumer nimmt sie nicht an, "
                    + "und ein blosses Markieren haette ihre Teile als "
                    + "Waisen zurueckgelassen. Sie bleiben stehen und "
                    + "gehoeren gemeldet.");

            /*
             * NULL MARKIERTE LOTS IST KEIN AUFTRAG.
             *
             * Sonst haette der naechste Durchgang "Alle PLT-Parkplaetze
             * entfernt" gemeldet, ohne dass dieser Knopf ein einziges Lot
             * angefasst hat - der Setter kann vorher etwas gezaehlt haben,
             * das inzwischen anderweitig weg ist.
             */
            _uebergangen = uebergangen;
            if (markiert == 0)
            {
                Mod.log.Warn("PLT-Stadtreinigung: bei der Annahme war kein "
                    + "abraeumbares Lot mehr da. Kein Auftrag, keine "
                    + "Erfolgsmeldung.");
                SetzeStand(Knopfzustand.Nichts);
                _erwartet = -1;
                return 0;
            }

            _erwartet = markiert;
            _rest = int.MaxValue;
            _ohneFortschritt = 0;
            _durchgaenge = 0;
            _gewarnt = false;
            SetzeStand(Knopfzustand.Laeuft, 0, 0, uebergangen);
            return markiert;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (_beauftragt)
            {
                _beauftragt = false;
                EntferneAlle();
                // Nicht im selben Durchgang nachzaehlen: der Aufraeumer
                // kommt erst gleich, und eine Messung vor ihm saehe den
                // Anfangsbestand und nichts sonst.
                return;
            }

            if (_erwartet < 0) return;

            _durchgaenge++;
            _teile.CompleteDependency();
            _markierteLots.CompleteDependency();
            var uebrig = _teile.CalculateEntityCount();
            var offeneLots = _markierteLots.CalculateEntityCount();

            if (uebrig == 0 && offeneLots == 0)
            {
                var nachsatz = _uebergangen > 0
                    ? " ABER: " + _uebergangen + " Flaeche(n) ohne "
                      + "Traegerreferenz stehen weiter, die konnten nicht "
                      + "abgeraeumt werden."
                    : string.Empty;
                Mod.log.Info("PLT-Stadtreinigung: der beauftragte Abriss ist "
                    + "durch, nach " + _durchgaenge + " Nachmessung(en). "
                    + "0 lebende Teile, 0 offene Auftraege." + nachsatz
                    + " Jetzt speichern - dass der Stand ohne den Mod "
                    + "unauffaellig ist, zeigt erst das Laden ohne ihn.");
                Schlusspruefung();
                _uiSystem?.SetStatus(_uebergangen > 0
                    ? ParkingLotTexte.T(
                        "Abriss durch, aber " + _uebergangen
                            + " Fläche(n) blieben stehen. Siehe Log.",
                        "Teardown done, but " + _uebergangen
                            + " patch(es) remain. See the log.")
                    : ParkingLotTexte.T(
                        "PLT-Parkplätze entfernt. Jetzt speichern.",
                        "PLT parking lots removed. Save now."));
                SetzeStand(Knopfzustand.Fertig, _erwartet, 0, _uebergangen);
                _erwartet = -1;
                return;
            }

            /*
             * VERGLICHEN WIRD MIT DER VORIGEN MESSUNG, nicht mit dem
             * Tiefststand.
             *
             * Vorher stand hier `uebrig < _rest` mit `_rest` als historischem
             * Minimum. Steigt die Zahl einmal an - anderswo wird gebaut - und
             * faellt danach wieder, erreicht sie das alte Minimum lange nicht,
             * und der Stillstandszaehler laeuft durch, obwohl bei jeder
             * Messung etwas verschwindet. Codex hat die Folge
             * 100, 400, 399, ... 101 durchgerechnet: Warnung bei Messung 301,
             * nachdem die Zahl 299-mal hintereinander gefallen war.
             */
            var fortschritt = uebrig < _rest || offeneLots < _offeneLots;
            _rest = uebrig;
            _offeneLots = offeneLots;
            if (fortschritt)
            {
                _ohneFortschritt = 0;
                // Die Zahl auf dem Knopf mitziehen, damit man sieht, dass es
                // laeuft - und nicht nur, dass irgendwann etwas passiert ist.
                SetzeStand(Knopfzustand.Laeuft, 0, uebrig, _uebergangen);
                return;
            }

            if (++_ohneFortschritt < StillstandsDurchgaenge || _gewarnt) return;

            /*
             * NUR EINMAL WARNEN, UND WEITER ZUSCHAUEN.
             *
             * Vorher endete hier die Ueberwachung. Loeste sich der Blockierer
             * danach auf, wurde das Fertigwerden nie gemeldet, und ein
             * zweiter Klick galt nicht mehr als Zweitklick.
             */
            _gewarnt = true;
            Mod.log.Warn("PLT-Stadtreinigung: seit " + _ohneFortschritt
                + " Nachmessungen kein Fortschritt. Es leben noch " + uebrig
                + " Teil(e), " + offeneLots + " Auftrag/Auftraege sind offen, "
                + "beauftragt waren " + _erwartet + " Parkplaetze. Moegliche "
                + "Ursachen: ein temporaeres Teil am ersten Auftrag haelt die "
                + "Warteschlange auf, oder die Teile gehoeren zu einem anderen "
                + "Parkplatz - die Zahl ist stadtweit. Die Ueberwachung laeuft "
                + "weiter; wird es doch noch fertig, steht das hier.");
            _uiSystem?.SetStatus(ParkingLotTexte.T(
                "Abriss kommt nicht voran: noch " + uebrig + " Teile.",
                "Teardown not progressing: " + uebrig + " parts left."));
            SetzeStand(Knopfzustand.Haengt, 0, uebrig, _uebergangen);
        }

        /** Beim Stadtwechsel darf kein Auftrag der alten Stadt weiterzaehlen. */
        /**
         * WAS VON PLT NOCH IN DER STADT STEHT.
         *
         * Die Zahlen darueber messen nur den Auftrag: Teile mit
         * Teilebeziehung und offene Auftraege. "0 lebende Teile" heisst
         * deshalb nicht "alles weg" - Flaechen, Strassen und der Traeger
         * haben keine Teilebeziehung und tauchen dort gar nicht auf.
         *
         * Der Nutzer am 2026-09-22: *"Und ist alles geloescht was auch
         * gesetzt wurde?"* Genau diese Frage beantwortet die Zeile hier,
         * und zwar stadtweit gezaehlt statt ueberschlagen.
         *
         * NICHT gezaehlt wird, was ABSICHTLICH bleibt: zonierte Kacheln und
         * die Haeuser, die darauf gewachsen sind. Die gehoeren dem Spieler.
         */
        private void Schlusspruefung()
        {
            var flaechen = 0;
            var strassen = 0;
            var traeger = 0;
            var lots = 0;

            var klone = new System.Collections.Generic.HashSet<Entity>();
            World.GetOrCreateSystemManaged<ParkingLotZoningRoadPrefabSystem>()
                ?.SammleKlonprefabs(klone);

            using (var alle = _flaechen.ToEntityArray(Allocator.Temp))
                for (var i = 0; i < alle.Length; i++)
                {
                    if (IstUnserLot(alle[i])) { lots++; continue; }
                    if (IstUnsereFlaeche(alle[i])) flaechen++;
                }

            using (var kanten = _alleKanten.ToEntityArray(Allocator.Temp))
                for (var i = 0; i < kanten.Length; i++)
                    if (klone.Contains(EntityManager
                            .GetComponentData<PrefabRef>(kanten[i]).m_Prefab))
                        strassen++;

            /*
             * DER TRAEGER IST NUR UEBER SEINE KINDER SICHTBAR.
             *
             * Er traegt selbst keine eigene Kennkomponente - gefunden wird
             * er ausschliesslich ueber `ParkingLotCarrierReference` am Lot
             * oder ueber `ParkingLotPartRelation.Carrier` an einem Teil.
             * Sind Lot und Teile weg, ist ein uebriggebliebener Traeger von
             * hier aus nicht mehr zu sehen. Gezaehlt wird deshalb, was
             * sichtbar ist: die Teile. Steht dort 0 und beim Lot auch, hat
             * der Aufraeumer seinen Traeger mit abgeraeumt - genau das
             * meldet er auch ("technischer Traeger entfernt").
             */
            _teile.CompleteDependency();
            traeger = _teile.CalculateEntityCount();
            var bushalte = 0;
            using (var teile = _teile.ToEntityArray(Allocator.Temp))
                for (var i = 0; i < teile.Length; i++)
                    if (EntityManager.HasComponent<Game.Routes.TransportStop>(
                        teile[i])) bushalte++;

            var summe = flaechen + strassen + traeger + lots;
            var text = "PLT-Schlusspruefung: " + lots + " Parkplatzflaeche(n), "
                + flaechen + " Belagflaeche(n), " + strassen
                + " Zoningstrasse/Zufahrtsgasse(n), " + traeger
                + " lebende(s) Teil(e), davon " + bushalte
                + " Bushaltestelle(n), noch in der Stadt. Zonierte "
                + "Kacheln und gewachsene Haeuser sind NICHT mitgezaehlt - "
                + "die gehoeren dem Spieler und bleiben mit Absicht.";
            if (summe == 0) Mod.log.Info(text + " Es ist nichts uebrig.");
            else Mod.log.Warn(text + " Diese " + summe + " Entity/Entities "
                + "haette der Abriss holen muessen.");
        }

        /** Traegt diese Flaeche eines unserer Belag-Prefabs? */
        private bool IstUnsereFlaeche(Entity flaeche)
        {
            if (!EntityManager.HasComponent<PrefabRef>(flaeche)) return false;
            var prefab = EntityManager
                .GetComponentData<PrefabRef>(flaeche).m_Prefab;
            if (!_prefabSystem.TryGetPrefab<PrefabBase>(prefab, out var basis)
                || basis == null) return false;
            return basis.name != null && basis.name.StartsWith("PLT ");
        }

        [Preserve]
        protected override void OnGameLoadingComplete(
            Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            _erwartet = -1;
            _rest = -1;
            _offeneLots = -1;
            _ohneFortschritt = 0;
            _durchgaenge = 0;
            _uebergangen = 0;
            _gewarnt = false;
            // Ein Auftrag der alten Stadt darf die neue nicht treffen.
            _beauftragt = false;
            SetzeStand(Knopfzustand.Bereit);
        }

        /**
         * Der Prefabname ist das Merkmal, nicht die Komponenten.
         */
        private bool IstUnserLot(Entity lot)
        {
            if (lot == Entity.Null || !EntityManager.Exists(lot)
                || !EntityManager.HasComponent<PrefabRef>(lot)) return false;
            var prefab = EntityManager.GetComponentData<PrefabRef>(lot).m_Prefab;
            return _prefabSystem.TryGetPrefab<PrefabBase>(prefab, out var wert)
                && wert != null
                && wert.name == ParkingLotToolSystem.LotOwnerPrefabName;
        }
    }
}
