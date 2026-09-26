using System;
using System.Collections.Generic;
using System.IO;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using ParkingLotTool.Tools;
using ParkingLotTool.Geometry;

namespace ParkingLotTool
{
    public class Mod : IMod
    {
        /// <summary>Pfad der geladenen DLL, aus dem Mod-Asset. Siehe OnLoad.</summary>
        public static string AssetPath = string.Empty;

        public static ILog log = LogManager.GetLogger($"{nameof(ParkingLotTool)}.{nameof(Mod)}").SetShowsErrorsInUI(false);

        /// <summary>Die Seite unter ESC -> Optionen -> Mods. Siehe Setting.cs.</summary>
        private Setting _setting;

        /**
         * Damit das Werkzeug die Einstellungen lesen kann. Statisch, weil ein
         * ECS-System keinen Weg zur Mod-Instanz hat; null-sicher benutzen, die
         * Optionsseite wird erst in OnLoad angelegt.
         */
        internal static Setting Optionen;

        /**
         * EINE STELLE ENTSCHEIDET, OB DIE WIRTSCHAFT LAEUFT.
         *
         * Vier Systeme fragen das ab - Unterhalt, Komfort und Gebuehr,
         * Begleiter, Angestellte - dazu die Oberflaeche. Stuende der Ausdruck
         * fuenfmal einzeln da, waere genau eine Stelle vergessen worden,
         * sobald der Schalter das naechste Mal seine Bedeutung aendert.
         *
         * Vor dem Laden der Optionen ist `Optionen` null. Dann gilt AN, denn
         * das ist der Standard; ein Parkplatz soll nicht deshalb kurz ohne
         * Wirtschaft dastehen, weil ein System frueh dran ist.
         */
        internal static bool WirtschaftAn => Optionen == null || Optionen.Wirtschaft;

        public void OnLoad(UpdateSystem updateSystem)
        {
            /*
             * ZUERST AUFRAEUMEN.
             *
             * Ansage des Nutzers am 2026-09-14: die Mod soll keinen Datenmuell
             * ansammeln, egal wie lange jemand spielt. Gemessen lagen zu dem
             * Zeitpunkt 1535 eigene Dateien mit 214 MB im Logs-Ordner.
             * `ParkingLotLogpflege` haelt je Sorte die juengsten fuenf und
             * deckelt das Bauprotokoll.
             *
             * Ganz am Anfang, damit der Platz frei ist, bevor dieser Lauf
             * selbst wieder schreibt.
             */
            Tools.ParkingLotLogpflege.Raeume();
            /*
             * DIREKT NACH DEM AUFRAEUMEN, VOR ALLEM ANDEREN.
             *
             * Die Absturzwache liest eine Marke aus der vorigen Sitzung und
             * legt sie fuer diese neu an. Waere sie spaeter dran, koennte ein
             * Fehler beim Laden die Marke verschlucken - und genau der Lauf,
             * der abstuerzt, waere der, der nichts hinterlaesst.
             */
            Tools.ParkingLotAbsturzwache.Pruefe();

            // Beim Start UNUEBERSEHBAR ins Log, mit Pfad und Bauzeit der DLL.
            //
            // Warum so ausfuehrlich: heute lief stundenlang der ALTE Mod weiter,
            // weil CS2 nicht nur `Mods/` durchsucht, sondern das ganze
            // Spielverzeichnis - der beiseitegelegte Ordner wurde trotzdem
            // geladen. Aufgefallen ist es nur an der Oberflaeche. Ohne diese
            // Zeilen raet man, welche DLL gerade laeuft.
            /*
             * ERST DEN PFAD, DANN DEN KOPF.
             *
             * Bis zum 2026-09-24 stand die Asset-Abfrage HINTER diesen
             * Zeilen. Der Kopf las `Assembly.Location` - bei CS2-Mods leer -
             * und schrieb "DLL: " und "gebaut am: unbekannt", waehrend zwei
             * Zeilen spaeter "Mod-Asset: ParkingLotTool.dll" den Pfad
             * nachreichte. Genau die Zeile, an der man eine veraltete DLL
             * erkennt, fehlte also bei jedem Start.
             */
            string assetPfad = null;
            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
            {
                assetPfad = asset.path;
                AssetPath = asset.path;
            }
            var dll = !string.IsNullOrEmpty(assetPfad)
                ? assetPfad : typeof(Mod).Assembly.Location;
            var bauzeit = Bauzeit();
            var gebaut = bauzeit.HasValue
                ? bauzeit.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                : "unbekannt";
            log.Info($"===== Parking Lot Tool geladen =====");
            // Die Version gehoert in die ERSTE Zeile jedes Berichts. Seit
            // Fehler ueber GitHub-Issues kommen, ist "welche Fassung hattest
            // du?" die erste Rueckfrage - und die kostet einen halben Tag
            // Wartezeit, wenn sie nicht im Log steht.
            log.Info($"  Version:    "
                + $"{typeof(Mod).Assembly.GetName().Version}");
            log.Info($"  DLL:        {System.IO.Path.GetFileName(dll)}");
            // Im Abzug 01:52 war Assembly.Location leer; Dateizeiten allein
            // unterschieden den geladenen Stand nicht vom neueren Quelltext.
            log.Info($"  Modul-ID:   {typeof(Mod).Module.ModuleVersionId}");
            log.Info($"  gebaut am:  {gebaut}");
            log.Info($"  jetzt ist:  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            if (!string.IsNullOrEmpty(assetPfad))
            {
                // Assembly.Location ist bei CS2-Mods LEER - das Spiel laedt die
                // DLL aus dem Speicher. Der einzige verlaessliche Pfad kommt vom
                // Mod-Asset. Ohne ihn stand im Debug-Abzug `DllBuiltAt: null`,
                // und die Vorsichtsmassnahme gegen eine veraltete DLL war
                // wertlos.
                // Nur der Dateiname: diese Zeile landet ueber `modlog-ende.txt`
                // in jedem Meldepaket, und der volle Pfad faengt mit
                // C:\Users\<Name> an.
                log.Info($"  Mod-Asset:  {System.IO.Path.GetFileName(assetPfad)}");
            }

            /*
             * Die Optionsseite VOR den Systemen anmelden.
             *
             * Sie enthaelt nur den Knopf, der das verschobene Panel
             * zurueckholt - und genau der muss auch dann erreichbar sein,
             * wenn im Panel selbst nichts mehr zu greifen ist.
             *
             * `LoadSettings` mit einem zweiten, frischen `Setting` als
             * Werksstand: so beschreibt das Spiel selbst, was
             * "zuruecksetzen" auf dieser Seite bedeutet.
             */
            _setting = new Setting(this);
            Optionen = _setting;
            /*
             * DIE GROESSTMASSE DER ZONINGFLAECHE AN DEN GEOMETRIEKERN.
             *
             * Als Quelle, nicht als Wert: der Kern holt die Zahl bei jedem
             * Zugriff frisch. Verstellt der Nutzer den Regler mitten im Spiel,
             * gilt er sofort - ohne dass irgendwo etwas nachgezogen werden
             * muss. Ein gespiegeltes Feld waere genau die Sorte Fehler, die
             * hier schon einmal die Anzeige eingefroren hat.
             */
            ParkingGeometry.ZoningMaxBreiteQuelle
                = () => Optionen?.ZoningMaxBreite
                    ?? ParkingGeometry.ZoningMaxStandard;
            ParkingGeometry.ZoningMaxTiefeQuelle
                = () => Optionen?.ZoningMaxTiefe
                    ?? ParkingGeometry.ZoningMaxStandard;
            _setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource(
                "en-US", new Beschriftungen(_setting, deutsch: false));
            GameManager.instance.localizationManager.AddSource(
                "de-DE", new Beschriftungen(_setting, deutsch: true));
            AssetDatabase.global.LoadSettings(
                nameof(ParkingLotTool), _setting, new Setting(this));

            /*
             * DEN GELESENEN WERT EINMAL DURCHREICHEN.
             *
             * `LoadSettings` schreibt in das Feld hinter der Eigenschaft und
             * laeuft nicht zwingend durch deren Setter - der Schalter waere
             * dann im Optionsmenue an und die Spur trotzdem aus, bis jemand
             * ihn einmal umlegt. Genau diese Sorte stiller Abweichung hat
             * uns schon zweimal Zeit gekostet.
             */
            _setting.Geladen = true;
            Tools.ParkingLotSchrittmarke.Mitschreiben = _setting.Absturzspur;
            if (_setting.Absturzspur)
                log.Info("PLT-Absturzspur ist AN (aus den Einstellungen). "
                    + "Jedes Bild wird mitgeschrieben; das kostet Leistung "
                    + "und ist nur zum Einkreisen eines Absturzes gedacht.");

            /*
             * DIE NOTBREMSE IST WEG - und das ist eine Aussage, keine
             * Nachlaessigkeit.
             *
             * Sie stand hier, weil die erweiterte Wirtschaft am 2026-08-26
             * das Spiel abgeschossen hat und ein gespeicherter Schalter den
             * Absturz beim naechsten Start wiederholt haette. Die beiden
             * Ausloeser sind seither RAUS: der Strom und das
             * Richtlinien-Ereignis der Parkgebuehr. Was bleibt - Laerm und
             * Angestellte ueber den Begleiter - ist im Spiel bestaetigt.
             *
             * Ein Schalter, der sich bei jedem Start selbst zurueckstellt, ist
             * fuer ein Experiment richtig und fuer ein Feature falsch.
             */

            updateSystem.UpdateAt<ParkingLotUISystem>(SystemUpdatePhase.UIUpdate);
            // Der Gebuehrenabschnitt im Auswahlfenster. Er traegt sich in
            // OnCreate selbst bei SelectedInfoUISystem ein.
            updateSystem.UpdateAt<ParkingLotFeeSection>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<ParkingLotEmployeeSection>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<ParkingLotEditSection>(SystemUpdatePhase.UIUpdate);
            // Zaehlt nach dem Laden die Parkplaetze aus dem ausgebauten
            // Rechenweg und bietet sie zum Loeschen an.
            updateSystem.UpdateAt<ParkingLotAltbestandSystem>(
                SystemUpdatePhase.GameSimulation);
            // Verwaiste Parkplaetze (ohne PLT gespeichert): Bestandsaufnahme
            // beim Laden, Reparatur auf Knopfdruck oder automatisch.
            // ModificationEnd statt GameSimulation: die Simulation steht
            // bei Pause still, und nach dem Laden ist das Spiel oft pausiert.
            updateSystem.UpdateAt<ParkingLotWaisenSystem>(
                SystemUpdatePhase.ModificationEnd);
            // Synchronisieren: bestehende Parkplaetze nachruesten, gedrosselt.
            // UIUpdate, weil es auch in der Pause laufen soll und die
            // Bindungen fuer Liste, Panelkopf und Fortschrittsmeldung traegt.
            updateSystem.UpdateAt<ParkingLotSyncSystem>(SystemUpdatePhase.UIUpdate);
            // Der Hinweis am Mauszeiger waehrend der Linienauswahl. Gleiche
            // Phase wie die Vanilla-Werkzeugtooltips.
            updateSystem.UpdateAt<ParkingLotAlignTooltipSystem>(
                SystemUpdatePhase.UITooltip);
            // Kachelzahl beim Ziehen und Befund des Seitenschalters - beides
            // gehoert an den Zeiger, nicht ans Panel.
            updateSystem.UpdateAt<ParkingLotZoningTooltipSystem>(
                SystemUpdatePhase.UITooltip);
            // Eigener Abschnitt im Auswahlfenster. Das System liest nur die
            // aktuelle Auswahl und die gespeicherten Daten dieser PLT-Flaeche.
            updateSystem.UpdateAt<ParkingLotFeeUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<ParkingLotListeUISystem>(
                SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<ParkingLotToolSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<ParkingLotAutoVersorgungApplySystem>(SystemUpdatePhase.PostTool);
            // Laeuft unabhaengig vom aktiven Werkzeug: der Nutzer schliesst
            // es nach dem Bau, um Zoning zu malen und Haeuser zu setzen.
            updateSystem.UpdateAt<ParkingLotSurfaceWatchSystem>(
                SystemUpdatePhase.GameSimulation);
            // Das Werkzeug fordert Laufzeit-SurfacePrefabs erst in ToolUpdate
            // an. Registriert werden sie im folgenden PrefabUpdate, solange
            // `Created` noch von allen Vanilla-Initialisierern gesehen wird.
            if (!Aus("vorflaeche"))
            updateSystem.UpdateBefore<ParkingLotApronPrefabSystem,
                Game.Prefabs.PrefabInitializeSystem>(
                    SystemUpdatePhase.PrefabUpdate);
            // Dieselbe Phase aus demselben Grund: der unsichtbare Klon der
            // Zoning-Strasse braucht seinen Instanzarchetyp von
            // PrefabInitializeSystem und muss deshalb davor entstehen.
            if (!Aus("zoningstrasse"))
            updateSystem.UpdateBefore<ParkingLotZoningRoadPrefabSystem,
                Game.Prefabs.PrefabInitializeSystem>(
                    SystemUpdatePhase.PrefabUpdate);
            if (!Aus("fussweg"))
            updateSystem.UpdateBefore<ParkingLotFusswegPrefabSystem,
                Game.Prefabs.PrefabInitializeSystem>(SystemUpdatePhase.PrefabUpdate);
            if (!Aus("fussweg"))
            updateSystem.UpdateAfter<ParkingLotFusswegPrefabAbschlussSystem,
                Game.Prefabs.NetInitializeSystem>(SystemUpdatePhase.PrefabUpdate);
            // Die einebnenden Wegvarianten fuer Gassenenden: vor dem
            // Initialisieren angelegt. Sie klonen auch den Fusswegklon; ist der
            // noch nicht da, holt der naechste Durchlauf ihn nach (das System
            // sucht, bis alle sechs stehen). Vollendet nach dem Fussweg-Abschluss.
            updateSystem.UpdateBefore<ParkingLotEbenerWegPrefabSystem,
                Game.Prefabs.PrefabInitializeSystem>(SystemUpdatePhase.PrefabUpdate);
            updateSystem.UpdateAfter<ParkingLotEbenerWegAbschlussSystem,
                ParkingLotFusswegPrefabAbschlussSystem>(SystemUpdatePhase.PrefabUpdate);
            if (!Aus("zoningstrasse"))
            updateSystem.UpdateAfter<ParkingLotVanillaGasseAbschlussSystem,
                Game.Prefabs.NetInitializeSystem>(SystemUpdatePhase.PrefabUpdate);
            // Laeuft unabhaengig vom aktiven Werkzeug: neue Instanzen werden
            // aus ihren echten Parkspuren geeicht, geladene erhalten ihren
            // gespeicherten ServiceUsage- und UpdateFrame-Wert zurueck.
            updateSystem.UpdateBefore<ParkingLotEconomySystem,
                Game.Simulation.CityServiceUpkeepSystem>(
                    SystemUpdatePhase.GameSimulation);

            /*
             * Laerm und Angestellte ueber den unsichtbaren Begleiter.
             * Teil des Hauptschalters - siehe Setting.Wirtschaft, Standard AN.
             *
             * Die Reihenfolge ist nicht beliebig: alle Begleitkomponenten
             * muessen stehen, BEVOR `Building` sichtbar wird. Sonst liest ein
             * fremdes System in dem einen Zwischenframe eine halbfertige
             * Entity - und mehrere lesen ungeprueft.
             */
            updateSystem.UpdateBefore<ParkingLotBuildingEconomySystem,
                Game.Buildings.InitializeSystem>(SystemUpdatePhase.Modification2);
            // Zwei reine Leser rahmen den einzigen statisch passenden
            // Transform-Schreiber ein. So nennt der Trace den Schreiber aus
            // einem Laufzeitvergleich, nicht nur aus dem Dekompilat.
            updateSystem.UpdateBefore<ParkingLotGroundHeightBeforeSystem,
                Game.Simulation.GroundHeightSystem>(
                    SystemUpdatePhase.Modification2);
            updateSystem.UpdateAfter<ParkingLotGroundHeightAfterSystem,
                Game.Simulation.GroundHeightSystem>(
                    SystemUpdatePhase.Modification2);
            // Das eigene BuildingPrefab muss `Created` noch tragen, wenn CS2
            // seinen Prefabvertrag baut. Registrierung im spaeteren
            // Wirtschaftstick waere dafuer einen Lebenszyklus zu spaet.
            if (!Aus("begleiter"))
            updateSystem.UpdateBefore<ParkingLotBuildingEconomyPrefabSystem,
                Game.Prefabs.BuildingInitializeSystem>(
                    SystemUpdatePhase.PrefabUpdate);
            updateSystem.UpdateBefore<ParkingLotEmployeeSystem,
                Game.Simulation.WorkProviderSystem>(
                    SystemUpdatePhase.GameSimulation);
            /*
             * ZURUECK NACH ToolUpdate - PostTool war die Ursache dafuer, dass
             * Strg+P am 2026-08-25 nicht mehr wirkte.
             *
             * Die Umstellung auf PostTool sollte verhindern, dass ein spaeteres
             * Werkzeug-System die Zuweisung im selben Zyklus ueberschreibt. Im
             * Spiel war die Wirkung genau umgekehrt: das Werkzeug blitzte einen
             * Frame auf und war wieder weg.
             *
             * Das Modlog nennt den Gegenspieler beim Namen - bei JEDEM Druck:
             *
             *   PLT-Hotkey: Strg+P aktiviert das Werkzeug in PostTool;
             *   vorher aktiv: FindIt.Picker
             *
             * Unser Hotkey feuert also korrekt und genau einmal
             * (`wasPressedThisFrame`); ein fremdes Werkzeug holt sich die
             * Zuweisung danach zurueck. Gegen einen Gegenspieler, der jeden
             * Frame zugreift, hilft SPAETER laufen nicht - man muss vor ihm
             * dran sein. Genau das tat die alte Einordnung, und mit ihr ging
             * es monatelang.
             */
            updateSystem.UpdateBefore<ParkingLotToolActivationSystem, ParkingLotToolSystem>(
                SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<ParkingLotDebugTooltipSystem>(
                SystemUpdatePhase.UITooltip);
            // Muss laufen, wenn das Werkzeug NICHT aktiv ist - der Bulldozer
            // schlaegt genau dann zu. Phase 3 ist Pflicht, nicht Geschmack:
            // `LaneSystem` raeumt in Phase 4 die Parkspuren geloeschter
            // Besitzer weg. Wer spaeter markiert, laesst Waisen zurueck und
            // reisst das Spiel ab.
            updateSystem.UpdateAt<ParkingLotCleanupSystem>(
                SystemUpdatePhase.Modification3);
            /*
             * DIE STADTREINIGUNG MARKIERT UNMITTELBAR VOR DEM AUFRAEUMER.
             *
             * Der erste Anlauf markierte direkt aus dem Options-Setter, also
             * in einem beliebigen Frame und einer beliebigen Phase. Der
             * Aufraeumer sammelt geloeschte Lots aber NUR in Phase 3; wird
             * danach markiert, kann CS2s eigenes Aufraeumen die Flaeche noch
             * im selben Frame zerstoeren, ohne dass je ein Auftrag entsteht -
             * und die Teile blieben als Waisen stehen.
             *
             * Der Setter stellt deshalb nur noch einen Auftrag. Hier wird er
             * angenommen.
             *
             * PRETOOL, NICHT MODIFICATION3 - berichtigt am 2026-09-22.
             *
             * Bis hierher stand die Annahme in `Modification3`, direkt vor
             * dem Aufraeumer. Damit konnte der Belag gar nicht verschwinden,
             * und zwar aus einem Grund, den ich zweimal falsch erklaert
             * habe. Die Reihenfolge IM Bild ist (`SystemOrder` Zeile 58/60):
             *
             *     ToolSystem      -> PreTool, ToolUpdate, PostTool
             *     ModificationSystem -> Modification1 .. ModificationEnd
             *     PrepareCleanUpSystem / CleanUpSystem -> zerstoert
             *
             * `Game.Objects.SubElementDeleteSystem` laeuft in `PostTool`.
             * Es nimmt JEDE Entity mit `Deleted`, die einen `SubArea`-,
             * `SubNet`- oder `SubRoute`-Puffer hat, und setzt `Deleted` an
             * alle Kinder darin. CS2 KASKADIERT ALSO DOCH - mein Satz
             * "CS2 kaskadiert nicht" war falsch, der Bulldozer legt seine
             * `CreationDefinition`s fuer Vorschau und Rueckgaengig an, nicht
             * fuers Loeschen.
             *
             * Nur: aus `Modification3` markiert, ist `PostTool` in diesem
             * Bild laengst vorbei, und am Bildende zerstoert `CleanUpSystem`
             * die Flaeche samt Puffer. Das naechste `PostTool` findet
             * nichts mehr. Die Kaskade lief nie - nicht ein einziges Mal.
             *
             * In `PreTool` markiert, passiert alles in EINEM Bild und in
             * der richtigen Reihenfolge:
             *
             *     PreTool         wir markieren den Parkplatz
             *     PostTool        CS2 markiert Flaechen und Strassen
             *     Modification2B  `Game.Net.ReferencesSystem` raeumt die
             *                     Knotenpuffer der geloeschten Kanten
             *     Modification3   unser Aufraeumer sieht den Auftrag noch
             *     Cleanup         alles wird zerstoert
             *
             * Damit ist auch die Absturzserie vom 2026-09-22 erklaert: wir
             * haben die Kanten selbst geloescht, aus Phase 3, also hinter
             * dem System, das die Verweise darauf haette raeumen muessen.
             * Jetzt loescht CS2 sie selbst, eine Phase vor seinem eigenen
             * Aufraeumer.
             */
            updateSystem.UpdateAt<ParkingLotStadtreinigungSystem>(
                SystemUpdatePhase.PreTool);
            /*
             * MODIFICATION2, NICHT 3 - UND DAS IST DER GANZE WITZ.
             *
             * `Game.Net.ReferencesSystem` laeuft in `Modification2B` und nimmt
             * eine geloeschte Kante aus den Puffern beider Endknoten. Wer
             * spaeter loescht, laesst dort Verweise auf eine Entity zurueck,
             * die es gleich nicht mehr gibt - und genau darueber stuerzt CS2
             * ab, wenn es den verwaisten Knoten abraeumt.
             *
             * Die vollstaendige Herleitung samt Messung steht im Kopf von
             * ParkingLotLeitungsabriss.cs.
             */
            updateSystem.UpdateAt<ParkingLotLeitungsabrissSystem>(
                SystemUpdatePhase.Modification2);
            /*
             * UIUpdate, und das ist keine Geschmacksfrage.
             *
             * `NameSystem.SetCustomName` braucht die `EndFrameBarrier`. Deren
             * Fenster oeffnet `AllowBarrier<EndFrameBarrier>` (SystemOrder
             * Zeile 62); `ToolSystem` laeuft in Zeile 58, also davor, und
             * `UIUpdateSystem` in Zeile 67, also danach. Aus dem Werkzeug
             * heraus ging es deshalb nie.
             */
            updateSystem.UpdateAt<ParkingLotStrassennameSystem>(
                SystemUpdatePhase.UIUpdate);
            // Vorversuch mit eigenem Flaechennetz, Alt+F. `Rendering` ist die
            // Phase, in der CS2 selbst zeichnet.
            updateSystem.UpdateAt<ParkingLotFlaechennetzSystem>(
                SystemUpdatePhase.Rendering);
            // Direkt HINTER `ParkingLaneDataSystem`, das in derselben Phase
            // laeuft (Game.Common.SystemOrder Zeile 251) und den Komfortwert
            // unserer Parkspuren bei jedem Nachrechnen auf 0 setzt, weil unser
            // Wurzelbesitzer eine Area und kein Gebaeude ist. Davor geschrieben
            // waere der Wert sofort wieder weg; danach steht er, bevor ihn im
            // selben Frame jemand liest.
            updateSystem.UpdateAfter<ParkingLotComfortSystem, Game.Pathfind.ParkingLaneDataSystem>(
                SystemUpdatePhase.ModificationEnd);
            // Der Abtaster der Parkplatzstatistik teilt sich den
            // Rueckweg von der Parkspur zum Lot mit dem
            // Komfortsystem und laeuft deshalb in derselben Phase.
            // Strukturaenderungen (neue Komponente, neue Puffer)
            // gehoeren in eine Modification-Phase, nicht in die
            // Simulation.
            updateSystem.UpdateAfter<ParkingLotStatistikSystem,
                ParkingLotComfortSystem>(
                    SystemUpdatePhase.ModificationEnd);
            // Reine Leser um die dritte Terrain-Query. Sie protokollieren den
            // Ist-Archetyp und dieselben Heightmapzellen vor/nach dem Lauf;
            // weder Terrain noch Entities werden veraendert.
            updateSystem.UpdateBefore<ParkingLotBuildingTerrainBeforeSystem,
                Game.Simulation.TerrainSystem>(
                    SystemUpdatePhase.ModificationEnd);
            updateSystem.UpdateAfter<ParkingLotBuildingTerrainAfterSystem,
                Game.Simulation.TerrainSystem>(
                    SystemUpdatePhase.ModificationEnd);
            // Der Raycast-Hook greift vor Hover, Auswahl und Bulldozer zu.
            // Das fruehere PostTool-System war erst nach diesen Verbrauchern
            // dran und suchte ausserdem raeumlich im Polygon.
            //
            // Der zweite Fang ist kein Doppel: `Install` faengt selbst, aber
            // wirft Mono schon beim Uebersetzen der Methode - etwa weil eine
            // fremde Harmony-Version ein Mitglied nicht kennt -, greift der
            // innere Block nie. Alles ab hier, insbesondere die
            // Tastenbelegung, muss trotzdem angemeldet werden.
            try { ParkingLotRaycastPatch.Install(); }
            catch (Exception ausnahme)
            {
                log.Error(ausnahme, "PLT: Harmony liess sich nicht laden. "
                    + "Anklicken, Bulldozer-Rueckfrage und Unterhaltsanzeige "
                    + "fallen aus; alles andere laeuft weiter.");
            }
            // Ohne diesen Aufruf taucht die Belegung nicht in CS2s
            // Tastenuebersicht auf und die Aktion bleibt leer.
            Optionen?.RegisterKeyBindings();
            log.Info("  Werkzeug registriert. Strg+Umschalt+P schaltet es um "
                + "(aenderbar unter ESC -> Optionen -> Mods -> Parking Lot Tool); "
                + "Alt+P schreibt den Debug-Abzug; Alt+F misst am Bordstein (nur bei offenem Werkzeug).");
        }

        /**
         * WELCHE PREFABS LEGEN WIR BEIM START NICHT AN?
         *
         * Suchhilfe fuer den Befund vom 2026-09-14: per Zoning gesetzte
         * HAEUSER bekommen Belag bis zur Strasse, und zwar allein dadurch,
         * dass PLT geladen ist - der Nutzer hat es in einem frischen
         * Spielstand ohne einen einzigen gebauten Parkplatz nachgestellt.
         *
         * Im Code habe ich die Stelle nicht gefunden: die vier
         * Harmony-Patches fassen keine Flaechen an, jeder Schreibzugriff geht
         * per Namenspruefung auf unsere eigenen Prefabs, die Ebenenmaske des
         * Vanilla-Belags ist laut unserem eigenen Log unveraendert, und
         * `PrefabBase.Clone` kopiert jedes Bauteil in eine eigene Instanz
         * (dekompiliert).
         *
         * Also derselbe Weg wie beim Absturz vom 2026-09-10: WEGLASSEN statt
         * nachdenken. Jede Gruppe laesst sich einzeln abschalten; verschwindet
         * das Phaenomen, liegt es dort.
         *
         * BEDIENUNG: die Datei `<Spielordner>/Logs/PLT-AUS.txt` anlegen und
         * die Gruppen hineinschreiben, durch Komma oder Leerzeichen getrennt:
         *
         *     vorflaeche     die Flaechen-Klone (Vorflaeche, Zoningbelag)
         *     zoningstrasse  die Strassen-Klone
         *     fussweg        der unsichtbare Pfad-Klon
         *     begleiter      das Gebaeude-Prefab der Wirtschaft
         *     alle           alle vier
         *
         * Einmal beim Laden gelesen. Das ist ein Werkzeug zum Suchen, keine
         * Einstellung - wenn der Befund steht, faellt es wieder raus.
         */
        /**
         * Komma, Leerzeichen, Tabulator und Zeilenende trennen die Gruppen.
         * Als Feld, weil ein Zeichenliteral mit Escape im Patch-Skript schon
         * einmal als roher Tabulator im Quelltext gelandet ist.
         */
        private static readonly char[] Trennzeichen =
            { ',', ' ', '\t', '\r', '\n' };

        private static HashSet<string> _ausgeschaltet;

        internal static bool Aus(string gruppe)
        {
            if (_ausgeschaltet == null)
            {
                _ausgeschaltet = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                try
                {
                    var pfad = System.IO.Path.Combine(
                        System.IO.Path.Combine(
                            UnityEngine.Application.persistentDataPath, "Logs"),
                        "PLT-AUS.txt");
                    if (System.IO.File.Exists(pfad))
                    {
                        foreach (var teil in System.IO.File.ReadAllText(pfad)
                                     .Split(Trennzeichen))
                            if (teil.Trim().Length > 0)
                                _ausgeschaltet.Add(teil.Trim());
                        log.Warn("PLT-AUS ist aktiv: "
                            + string.Join(", ", _ausgeschaltet)
                            + ". Suchhilfe, keine Einstellung - die Mod "
                            + "arbeitet damit unvollstaendig.");
                    }
                }
                catch (Exception ausnahme)
                {
                    log.Warn("PLT-AUS nicht lesbar: " + ausnahme.Message);
                }
            }
            return _ausgeschaltet.Contains(gruppe)
                || _ausgeschaltet.Contains("alle");
        }

        /**
         * WELCHE AUSFUEHRLICHEN MESSUNGEN SIND ZUGESCHALTET?
         *
         * Dieselbe Datei, andere Richtung. `Aus` schaltet etwas ab, das
         * normalerweise laeuft; `An` schaltet etwas zu, das normalerweise
         * schweigt.
         *
         * Hintergrund: am 2026-09-14 hat der Nutzer gefragt, welche Logs
         * staendig mitlaufen und nicht mehr noetig sind. Gezaehlt waren es
         * 1975 Zeilen je Spielstart allein fuer die Prefab-Inventur, dazu
         * je Bucht-Aufkleber eine Zeile bei jedem Bau. Solche Messgeraete
         * loescht man aber nicht - man legt sie in die Schublade. Wer sie
         * braucht, schreibt ihr Wort in dieselbe Datei:
         *
         *     kantenmessung   die ausfuehrliche Fahrbahnkanten-Messung
         *
         * Ohne Eintrag bleibt es bei der Zusammenfassung.
         */
        internal static bool An(string gruppe)
        {
            // Loest dieselbe einmalige Lesung aus wie `Aus`.
            Aus("nichts");
            return _ausgeschaltet.Contains(gruppe);
        }


        public void OnDispose()
        {
            // Ordentlich beendet - die Marke darf weg. Bleibt sie liegen,
            // fragt die Mod beim naechsten Start nach einem Absturzbericht.
            Tools.ParkingLotAbsturzwache.Beende();
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated)
                world.GetExistingSystemManaged<ParkingLotToolSystem>()
                    ?.CancelEditingForShutdown();
            ParkingLotRaycastPatch.Uninstall();
            log.Info(nameof(OnDispose));
            if (_setting != null)
            {
                _setting.UnregisterInOptionsUI();
                _setting = null;
                Optionen = null;
            }
        }
        /**
         * WANN DIE GELADENE DLL GESCHRIEBEN WURDE - oder null.
         *
         * `Assembly.Location` ist bei CS2-Mods LEER, weil das Spiel die DLL
         * aus dem Speicher laedt; der brauchbare Pfad kommt aus dem
         * Mod-Asset. Dieselbe Regel galt schon im Vorbau-Abzug, und sie
         * steht jetzt an EINER Stelle statt an zweien.
         *
         * Warum das ueberhaupt jemanden interessiert: die Versionsnummer
         * steht waehrend der Entwicklung wochenlang still, waehrend die DLL
         * zwanzigmal neu entsteht. Nur die Schreibzeit sagt, ob im Spiel
         * wirklich der neue Stand liegt.
         */
        internal static System.DateTime? Bauzeit()
        {
            var pfad = !string.IsNullOrEmpty(AssetPath)
                ? AssetPath
                : typeof(Mod).Assembly.Location;
            if (string.IsNullOrEmpty(pfad) || !System.IO.File.Exists(pfad))
                return null;
            return System.IO.File.GetLastWriteTimeUtc(pfad);
        }

    }
}
