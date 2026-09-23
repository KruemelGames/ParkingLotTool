using System;
using System.Collections.Generic;
using System.Globalization;
using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Input;
using Game.Settings;
using ParkingLotTool.Geometry;
using ParkingLotTool.Tools;
using Unity.Entities;

namespace ParkingLotTool
{
    /**
     * Die Seite unter ESC -> Optionen -> Mods -> Parking Lot Tool.
     *
     * WOFUER sie ueberhaupt existiert: seit das Panel verschiebbar ist, kann
     * man es aus dem Bild schieben. Danach ist es nicht mehr zu greifen - der
     * Griff ist mit hinausgewandert. Ein Weg zurueck, der NICHT im Panel
     * selbst liegt, ist deshalb Pflicht und kein Zubehoer.
     *
     * Nur dieser eine Knopf steht hier. Alles, was man beim Bauen einstellt,
     * gehoert ins Panel neben das Ergebnis - nicht in ein Menue, das man nur
     * ueber eine Pause im Spiel erreicht.
     */
    [FileLocation("ModsSettings/ParkingLotTool/optionen")]
    [SettingsUIGroupOrder(GruppeUeber, GruppeSprache, GruppeTasten, GruppeZufahrt,
        GruppeZoning, GruppeVegetation, GruppeWirtschaft, GruppeHinweise,
        GruppeFenster, GruppeZuruecksetzen, GruppeEntwickler,
        GruppeDeinstallation)]
    [SettingsUIShowGroupName(GruppeUeber, GruppeSprache, GruppeTasten, GruppeZufahrt,
        GruppeZoning, GruppeVegetation, GruppeWirtschaft, GruppeHinweise,
        GruppeFenster, GruppeZuruecksetzen, GruppeEntwickler,
        GruppeDeinstallation)]
    [SettingsUIKeyboardAction(AktionWerkzeug, ActionType.Button, usages: new[] { "PLT" })]
    public class Setting : ModSetting
    {
        public const string ReiterAllgemein = "Allgemein";
        /**
         * GANZ OBEN. Wer die Einstellungen aufmacht, um eine Version
         * nachzusehen, soll nicht scrollen muessen - und wer einen Fehler
         * meldet, wird danach gefragt.
         */
        public const string GruppeUeber = "Ueber";
        public const string GruppeFenster = "Fenster";
        public const string GruppeHinweise = "Hinweise";
        public const string GruppeWirtschaft = "Wirtschaft";
        public const string GruppeZufahrt = "Zufahrt";
        public const string GruppeSprache = "Sprache";
        public const string GruppeTasten = "Tasten";
        public const string GruppeZoning = "Zoning";
        public const string GruppeVegetation = "Vegetation";
        public const string GruppeZuruecksetzen = "Zuruecksetzen";
        public const string GruppeEntwickler = "Entwickler";

        /**
         * GANZ UNTEN, und das ist Absicht: was hier steht, macht man genau
         * einmal und dann nie wieder. Zwischen den Reglern waere es eine
         * Stolperfalle.
         */
        public const string GruppeDeinstallation = "Deinstallation";

        /**
         * ZEIGT DEN ENTWICKLER-REITER IM PANEL.
         *
         * Der Debug-Reiter ist ueber Monate an einzelnen Faellen gewachsen:
         * Zoning-Sonde, Prefab-Zerlegung, Traegertest, Ueberlappungsmessung,
         * Prefab-Vergleich, Live-Log. Das sind Werkzeuge fuer die
         * Entwicklung, nicht fuer jemanden, der die Mod benutzt - sie
         * erklaeren sich nicht von selbst, und einige greifen tief ein.
         *
         * Vor der Testveroeffentlichung an interessierte Nutzer trennt sich
         * das: der Reiter `Debug` bekommt, was ein Nutzer zum MELDEN braucht,
         * der Reiter `dev-Debug` behaelt den Rest und ist standardmaessig aus.
         *
         * Warum ein Schalter und kein Loeschen: die Werkzeuge sind mehrfach
         * der einzige Weg gewesen, an eine Ursache zu kommen - zuletzt am
         * 2026-09-14 die Platzhalterliste. Was man beim Suchen braucht, wirft
         * man nicht weg, man legt es in die Schublade.
         */
        [SettingsUISection(ReiterAllgemein, GruppeEntwickler)]
        public bool EntwicklerDebug
        {
            get => _entwicklerDebug;
            set
            {
                _entwicklerDebug = value;
                /*
                 * SOFORT MELDEN, NICHT ERST BEIM NAECHSTEN OEFFNEN.
                 *
                 * Der erste Anlauf las die Einstellung, wenn das Panel
                 * aufging. Der Nutzer legt den Schalter aber im Optionsmenue
                 * um, WAEHREND das Panel schon offen ist - und dann passierte
                 * sichtbar nichts. Sein Befund: *"Wenn ich in den
                 * Einstellungen Dev-Debug anschalte, erscheint der Reiter
                 * nicht direkt im Panel."*
                 *
                 * Ein Ereignis von CS2 dafuer habe ich nicht gefunden; die
                 * Eigenschaft gehoert aber uns, also sagt sie selbst
                 * Bescheid. Das Panel haengt sich in `Geaendert` ein.
                 */
                Geaendert?.Invoke(value);
            }
        }

        private bool _entwicklerDebug;

        /** Wird gerufen, sobald der Entwickler-Schalter umgelegt wird. */
        internal static event Action<bool> Geaendert;

        /**
         * Der Name, unter dem CS2 die Tastenbelegung fuehrt.
         *
         * Er steht als Konstante hier und nicht als Zeichenkette an drei
         * Stellen: das Aktivierungssystem fragt die Aktion damit ab, die
         * Uebersetzung baut ihren Schluessel daraus, und CS2 selbst zeigt sie
         * in der Tastenuebersicht.
         */
        public const string AktionWerkzeug = "PLT.WerkzeugUmschalten";

        /**
         * DIE TASTE, MIT DER DAS WERKZEUG AUFGEHT - und warum sie hier steht.
         *
         * Bis zum 2026-08-25 war der Hotkey handgeschrieben: ein System fragte
         * `Keyboard.current` jeden Frame selbst ab. Das funktionierte, war aber
         * fuer CS2 unsichtbar - und genau daran ist an diesem Tag ueber eine
         * Stunde verlorengegangen.
         *
         * Der Befund: Strg+P oeffnete das Werkzeug nicht mehr. Im Modlog stand
         * bei jedem Druck `vorher aktiv: FindIt.Picker`. Unser Hotkey feuerte
         * korrekt, aber Find It hoert auf dieselbe Taste und behielt das
         * Werkzeug. Bewiesen hat es der Nutzer, indem er Find It abschaltete -
         * dann ging es sofort.
         *
         * Ueber `SettingsUIKeyboardBinding` steht die Belegung jetzt in CS2s
         * eigener Tastenuebersicht. Der Gewinn ist nicht Bequemlichkeit,
         * sondern SICHTBARKEIT: das Spiel zeigt Konflikte dort an, statt dass
         * sie sich als raetselhaftes Nicht-Funktionieren aeussern.
         *
         * Vorgabe Strg+Umschalt+P, gewaehlt vom Nutzer. Sie war in unserem
         * eigenen Schema noch frei - Alt+P schreibt den Debug-Abzug,
         * Strg+Alt+P den Aufbau eines Objekts, Umschalt+P schaltet die
         * Lot-Besitzerflaeche um.
         *
         * Ausdruecklich NICHT gemacht: Find It verdraengen. Der Nutzer dazu:
         * *"Wir duerfen aber Find It auch nicht umgehen."* Sein Picker ist
         * genauso berechtigt wie unser Werkzeug; wer sich durchsetzt, schiebt
         * das Problem nur zum naechsten.
         */
        [SettingsUIKeyboardBinding(BindingKeyboard.P, AktionWerkzeug,
            ctrl: true, shift: true)]
        [SettingsUISection(ReiterAllgemein, GruppeTasten)]
        public ProxyBinding WerkzeugTaste { get; set; }

        public Setting(IMod mod) : base(mod) { }

        /**
         * Nach dem Schliessen des Polygons sofort in den Zufahrt-Modus?
         *
         * Bisher war das fest verdrahtet, und wer erst am Zuschnitt
         * weiterarbeiten wollte, musste sich mit Rechtsklick wieder
         * herausklicken. Ansage des Nutzers am 2026-08-20: er will selbst
         * entscheiden, wann die Zufahrt drankommt.
         *
         * Steht auf AN, also wie bisher - wer nichts aendert, merkt nichts.
         */
        [SettingsUISection(ReiterAllgemein, GruppeZufahrt)]
        public bool AutomatischZufahrtModus { get; set; } = true;

        /**
         * Legt der Mod beim Bauen selbst Strom und Wasser?
         *
         * Ansage des Nutzers am 2026-09-05, nachdem das nachtraegliche
         * Wiederherstellen bestehender Anschluesse einen ganzen Tag gekostet
         * hatte: *"MACH jetzt los mit dem Automatischen verbinden zur
         * Strasse."*
         *
         * Steht auf AN, weil er das Merkmal ausdruecklich bestellt hat. Wer es
         * nicht will, schaltet es hier ab - abgeschaltet entstehen keine neuen
         * Leitungen, vorhandene werden aber weiterhin erhalten. Das eine ist
         * ein Komfortmerkmal, das andere Schadensvermeidung.
         */
        [SettingsUISection(ReiterAllgemein, GruppeZufahrt)]
        public bool AutomatischVersorgung { get; set; } = true;

        /**
         * Nachfrage vor dem Umschalten auf den alten Rechenweg.
         *
         * Der alte Weg ist nur noch Rueckfall. Am 2026-08-26 ist der Nutzer
         * versehentlich darauf gelandet - die beiden Knoepfe liegen als
         * schmale Leiste direkt unter dem Modnamen, also dort, wo man das
         * Fenster anfasst - und hat danach eine halbe Stunde einen Fehler
         * gesucht, den es ohne diesen Klick nicht gaebe.
         *
         * Der Haken steht HIER und nicht nur im Dialog, damit "nicht mehr
         * anzeigen" umkehrbar bleibt. Wer ihn im Dialog setzt, findet ihn
         * sonst nie wieder.
         */
        /*
         * NICHT MEHR AUF DER SEITE.
         *
         * Die Nachfrage, die dieser Haken ausblendet, kann seit dem Ausbau
         * des alten Rechenwegs gar nicht mehr erscheinen. Ein Schalter ohne
         * Wirkung gehoert nicht in die Optionen.
         *
         * Die Eigenschaft bleibt, damit vorhandene Einstellungsdateien
         * unveraendert weiterladen; `SettingsUIHidden` nimmt sie nur aus der
         * Anzeige.
         */
        [SettingsUIHidden]
        public bool AltRechenwegOhneWarnung { get; set; } = false;

        [SettingsUISection(ReiterAllgemein, GruppeHinweise)]
        public bool ParkplatzLoeschenBestaetigen { get; set; } = true;

        /**
         * SCHREIBT BEI JEDEM BILD MIT, WAS DIE MOD GERADE TUT.
         *
         * Die Schrittmarke gab es bisher nur an den Bau- und Abrisswegen.
         * Stuerzt CS2 beim blossen Spielen ab - fertige Parkplaetze, nichts
         * wird gebaut -, stand dort der letzte BAU, womoeglich Stunden
         * vorher. Genau dieser Fall wurde am 2026-09-22 gemeldet.
         *
         * WARUM NICHT IMMER AN. Jede Marke schreibt die Datei neu und
         * erzwingt mit `Flush(true)` den Weg bis auf die Platte. Das ist
         * Absicht: was im Betriebssystempuffer bleibt, ist nach einem
         * nativen Absturz weg. Es ist damit aber zu teuer, um es jedem
         * Nutzer dauernd zuzumuten - deshalb ein Schalter, aus als Standard.
         *
         * WER IHN BRAUCHT: wer einen Absturz WIEDERHOLEN kann. Anmachen,
         * abstuerzen lassen, neu starten - dann steht im automatisch
         * erzeugten Absturzbericht, in welchem unserer Systeme es passiert
         * ist.
         */
        [SettingsUISection(ReiterAllgemein, GruppeHinweise)]
        public bool Absturzspur
        {
            get => _absturzspur;
            set
            {
                _absturzspur = value;
                // Sofort, nicht erst beim naechsten Start: wer den Schalter
                // umlegt, will den naechsten Absturz mitgeschrieben haben.
                // Und im Log steht, ab wann die Spur gilt - sonst raetselt
                // man spaeter, warum die Datei erst ab der Mitte etwas sagt.
                Tools.ParkingLotSchrittmarke.Mitschreiben = value;
                Mod.log.Info("PLT-Absturzspur " + (value ? "AN" : "AUS")
                    + (value
                        ? " - ab jetzt wird bei jedem Bild mitgeschrieben, "
                          + "das kostet Leistung."
                        : "."));
            }
        }

        private bool _absturzspur;


        /**
         * HAUPTSCHALTER FUER DIE GESAMTE WIRTSCHAFT.
         *
         * Daran haengt seit dem 2026-08-26 alles Wirtschaftliche: Unterhalt,
         * Parkgebuehr, Laerm, Angestellte und der Komfort, mit dem der
         * Parkplatz um Autos wirbt. Vorher war es ein Nebenschalter nur fuer
         * Laerm und Angestellte - Ansage des Nutzers: "Alles was derzeit an
         * der eingebauten Wirtschaft am Toggle haengt muss auch rein."
         *
         * STANDARD AN. Der Mod soll die volle Simulation mitbringen, ohne
         * dass man sie erst suchen muss.
         *
         * Der Name ist absichtlich neu und nicht `ErweiterteWirtschaft`: die
         * Bedeutung hat sich geaendert, und ein neuer Schluessel sorgt dafuer,
         * dass ein alter gespeicherter Wert nicht als "aus" haengenbleibt.
         *
         * DAS UMSCHALTEN MUSS IM LAUFENDEN SPIEL TRAGEN. Wer ausschaltet,
         * verliert nur die WIRKUNG, nicht die Werte: `ParkingLotEconomyData`
         * bleibt an jedem Parkplatz stehen, damit die eingestellte Gebuehr
         * beim Wiedereinschalten zurueckkommt. Parkplaetze, die im
         * ausgeschalteten Zustand gebaut wurden, haben noch keine solchen
         * Daten und bekommen beim Einschalten den Baustandard von unten.
         *
         * Im Aus-Zustand werden ausserdem keine Bauteile an- oder abgehaengt,
         * sondern nur Zahlen auf null gesetzt. Strukturaenderungen sind das,
         * woran CS2 abstuerzt; ein Schalter darf so etwas nicht ausloesen.
         */
        [SettingsUISection(ReiterAllgemein, GruppeWirtschaft)]
        public bool Wirtschaft { get; set; } = true;

        /**
         * Nur fuer die Oberflaeche: graut die Standardgebuehr aus, solange die
         * Wirtschaft aus ist. Ein Regler, der nichts bewirkt, gehoert nicht
         * bedienbar auf den Bildschirm.
         */
        [SettingsUIHidden]
        public bool WirtschaftAus => !Wirtschaft;

        /**
         * DIE FANGAUSWAHL DES MAGNET-PANELS, ueber Sitzungen hinweg.
         *
         * CS2 setzt `selectedSnap` in `ToolBaseSystem.OnCreate` fest auf
         * `Snap.All` - bei jedem Spielstart. Wer eine Fangart abschaltet,
         * findet sie beim naechsten Mal wieder an. Ansage des Nutzers am
         * 2026-09-03: *"Es sollte mitgespeichert werden, wenn ich eine
         * Snapping-Funktion ausschalte. Also nicht ueber den Spielstand,
         * sondern von uns aus. Derzeit resetet sich das immer, wenn ich
         * wieder ins Spiel gehe, und das nervt voll."*
         *
         * Hier und nicht im Spielstand: die Auswahl ist eine Vorliebe des
         * Nutzers, kein Merkmal einer Stadt.
         *
         * -1 heisst "noch nie gesetzt". Ein eigener Wert dafuer ist noetig,
         * weil 0 legitim ist - naemlich alle Fangarten aus.
         */
        [SettingsUIHidden]
        public int Fangauswahl { get; set; } = -1;

        /**
         * OB DIE ZAHL OBEN UEBERHAUPT ETWAS BEDEUTET.
         *
         * Eine eigene Flagge, weil die Zahl allein es nicht sagen kann:
         * `Snap.All` ist -1, also GENAU der Wert, der vorher "noch nie
         * gesetzt" heissen sollte. Das Log vom 2026-09-03 zeigte die Folge
         * in zwei Zeilen - "noch nie gesetzt" und siebzehn Millisekunden
         * spaeter "gespeichert: -1 (All)". Ein Wert, der sich selbst
         * ausloescht.
         */
        [SettingsUIHidden]
        public bool FangauswahlGesetzt { get; set; }

        /**
         * WELCHE FLAECHENKLONE SCHON GEBRAUCHT WURDEN.
         *
         * Unsere Flaechen sind Klone von Vanilla-Flaechen ('PLT Zoningbelag
         * (Grass Surface 01)' und so weiter). Sie entstehen bisher erst beim
         * BAUEN - und damit zu spaet: beim Laden eines Spielstands sucht CS2
         * die Prefabs der gespeicherten Flaechen, findet sie nicht, und alles
         * Gras, jede Dekoflaeche und jede Zufahrt kam weiss zurueck. Genau
         * das hat der Nutzer am 2026-09-03 im Bild gezeigt.
         *
         * Deshalb wird gemerkt, welche gebraucht wurden. Beim naechsten Start
         * entstehen sie wieder, BEVOR der Spielstand gelesen wird.
         *
         * Format: je Eintrag "Vorbildname|Aufschlag", getrennt durch
         * Zeilenumbruch. Ein Name kann Klammern enthalten, aber kein
         * senkrechter Strich - deshalb der als Trenner.
         */
        [SettingsUIHidden]
        public string Flaechenklone { get; set; } = string.Empty;

        /**
         * Baustandard fuer neue Parkplaetze. 0 heisst kostenlos.
         *
         * Beim Bau wird er in die gespeicherten Wirtschaftsdaten der Flaeche
         * kopiert. Danach gehoert die Gebuehr dem einzelnen Parkplatz und
         * wird in dessen Auswahlfenster verstellt. Bezahlt wird weiter von
         * CS2 selbst: `PersonalCarAISystem` (628-680) bucht vom Haushalt an
         * die Stadt und traegt es unter "Parken" ein.
         *
         * Standard 10 - Ansage des Nutzers.
         *
         * NULL IST KEIN BETRAG, SONDERN "AUS". CS2 fuehrt die Parkgebuehr von
         * 1 bis 50; darunter ist sie nicht kostenlos, sondern abgeschaltet.
         * Der Abschnitt am Parkplatz zeigt deshalb "Aus" statt einer 0, und
         * dieser Regler ist der Startwert fuer NEUE Parkplaetze.
         */
        [SettingsUISlider(min = 0, max = 50, step = 1, scalarMultiplier = 1)]
        [SettingsUISection(ReiterAllgemein, GruppeWirtschaft)]
        [SettingsUIDisableByCondition(typeof(Setting), nameof(WirtschaftAus))]
        public int Parkgebuehr { get; set; } = 10;

        /**
         * WIEVIELE PFLANZEN JE QUADRATMETER - RELATIV ZUR SICHTBARKEITSGRENZE.
         *
         * 1,0 ist der Abstand, ab dem CS2 selbst anfaengt, Pflanzen zu
         * verstecken (`OverrideSystem.ObjectIterator`, Kreise mit Radius
         * `m_Size.x * 0.5`). Dort steht jede gesetzte Pflanze auch sichtbar da.
         *
         * Darueber wird es luftiger, darunter dichter - und unter 1,0 blendet
         * CS2 einen Teil wieder weg. Das ist kein Fehler, sondern der Preis;
         * wer Dickicht will, nimmt ihn bewusst in Kauf.
         *
         * Gefuehrt als Ganzzahl in Zehnteln, weil `SettingsUISlider` mit
         * Kommaschritten nicht zuverlaessig umgeht.
         */
        [SettingsUISlider(min = 0.5f, max = 3f, step = 0.1f)]
        [SettingsUICustomFormat(fractionDigits = 1)]
        [SettingsUISection(ReiterAllgemein, GruppeVegetation)]
        public float VegetationsdichteFaktor { get; set; } = 1f;

        /** Der Reglerwert, gegen Unfug aus einer alten Datei abgesichert. */
        public float Vegetationsdichte
            => System.Math.Min(3f, System.Math.Max(0.5f, VegetationsdichteFaktor));

        /**
         * Sprache der Oberflaeche. Standard ENGLISCH - Wunsch des Nutzers am
         * 2026-08-21: der Mod geht in den Workshop, dort ist Englisch die
         * Sprache, die jeder lesen kann.
         */
        public enum Sprachwahl
        {
            Automatic,
            English,
            Deutsch,
        }

        /**
         * NUR LESEN, NICHTS EINSTELLEN.
         *
         * Eine Eigenschaft ohne Setter zeigt CS2 als Wert an. Gespeichert
         * wird sie nicht - `ModSetting` serialisiert nur, was auch
         * geschrieben werden kann.
         *
         * Neben der Nummer steht die BAUZEIT der geladenen DLL, und die ist
         * im Alltag die wichtigere Zahl: die Version steht waehrend der
         * Entwicklung wochenlang still, die Bauzeit wechselt bei jedem
         * Uebersetzen. Wer wissen will, ob im Spiel wirklich der neue Stand
         * liegt, liest hier nach - genau diese Frage kam heute dreimal auf.
         */
        [SettingsUISection(ReiterAllgemein, GruppeUeber)]
        public string Version
        {
            get
            {
                var nummer = typeof(Mod).Assembly.GetName().Version?.ToString()
                    ?? "?";
                var gebaut = Mod.Bauzeit();
                return gebaut.HasValue
                    ? nummer + "  ·  Build "
                      + gebaut.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm",
                          System.Globalization.CultureInfo.InvariantCulture)
                    : nummer;
            }
        }

        [SettingsUISection(ReiterAllgemein, GruppeSprache)]
        public Sprachwahl Sprache { get; set; } = Sprachwahl.English;

        /** "en" oder "de" - das, woran die Oberflaeche haengt. */
        internal string SprachKuerzel()
        {
            if (Sprache == Sprachwahl.Deutsch) return "de";
            if (Sprache == Sprachwahl.English) return "en";
            try
            {
                var aktiv = Game.SceneFlow.GameManager.instance?.localizationManager
                    ?.activeLocaleId;
                return aktiv != null && aktiv.StartsWith("de") ? "de" : "en";
            }
            catch
            {
                return "en";
            }
        }

        /**
         * Setzt das Panel an seinen Platz zurueck: oben am Rand, waagerecht
         * mittig. Ausgerechnet wird das im Panel selbst - nur dort sind
         * Leistenbreite und Aufloesung bekannt.
         *
         * Nur ein `set` und kein `get`: so bauen die Spielmenues einen Knopf
         * statt eines Schalters. Der Wert selbst wird nie gelesen.
         *
         * `GetExistingSystemManaged` und nicht `GetOrCreate`: waere die Welt
         * noch nicht so weit, wuerde ein zweites, leeres System entstehen,
         * dessen Bindings niemand sieht. Fehlt es, passiert lieber nichts.
         */
        /**
         * Wie das Panel aufgebaut ist. Der INHALT ist in beiden derselbe -
         * dieselben Reiter, dieselben Regler, dieselben Knoepfe.
         *
         *   Horizontal  breite Leiste, die Spalten nebeneinander. Laesst
         *               die obere Bildschirmhaelfte frei.
         *   Hochkant    schmale Spalte, die Spalten untereinander. Steht
         *               dort, wo CS2 seine eigenen Werkzeugeinstellungen
         *               zeigt, und verdeckt am wenigsten.
         */
        public enum Fensterstilwahl
        {
            Horizontal,
            Hochkant,
        }

        /**
         * DIE EINSTELLUNG IST DER EINZIGE SPEICHERORT.
         *
         * Der Umschalter in der Kopfzeile des Panels schreibt hierher. Ein
         * zweiter Speicherort in der Panel-Datei waere irgendwann
         * auseinandergelaufen - und dann zeigte diese Seite etwas anderes
         * an, als das Fenster tut.
         */
        [SettingsUISection(ReiterAllgemein, GruppeFenster)]
        public Fensterstilwahl Fensterstil { get; set; }
            = Fensterstilwahl.Horizontal;

        /** "hochkant" oder "horizontal" - das, woran die Oberflaeche haengt. */
        internal string FensterstilKuerzel()
            => Fensterstil == Fensterstilwahl.Hochkant ? "hochkant" : "horizontal";

        [SettingsUIButton]
        [SettingsUISection(ReiterAllgemein, GruppeFenster)]
        public bool FensterZuruecksetzen
        {
            set
            {
                var welt = World.DefaultGameObjectInjectionWorld;
                welt?.GetExistingSystemManaged<ParkingLotUISystem>()
                    ?.ResetPanelPosition();
            }
        }

        /**
         * ALLES AUF WERKSZUSTAND - BEIDE SEITEN.
         *
         * Die Einstellungen des Mods liegen an zwei Orten: diese Seite in
         * `ModsSettings/ParkingLotTool/optionen`, und die Werte, die man im
         * Panel als Standard merkt, in einer eigenen Datei daneben. Ein Knopf,
         * der nur eine der beiden leert, laesst den Nutzer im Glauben, er
         * haette aufgeraeumt.
         *
         * Anlass ist die Testveroeffentlichung: die Einstellungen des
         * Entwicklers sollen nicht mit dem Mod bei allen anderen landen.
         *
         * Die Sprache wird ausdruecklich MIT zurueckgesetzt - Werkszustand
         * heisst Englisch, und wer den Knopf drueckt, will genau das. Die
         * gemerkten Flaechenklone bleiben dagegen stehen: sie sind keine
         * Einstellung, sondern die Liste der Prefabs, die vorhandene
         * Spielstaende zum Laden brauchen.
         */
        /**
         * AUFRAEUMEN VOR DEM DEINSTALLIEREN.
         *
         * Ist der Mod erst weg, kann niemand mehr etwas an dem tun, was er
         * hinterlassen hat. Die Objekte bleiben stehen - CS2 haelt die
         * Kennung unserer Prefabs als "veraltet" fest -, aber sie zeigen auf
         * eine Bauanleitung, die nichts mehr sagt, und unsere Datenkomponenten
         * wirft der Lader weg. Speichert der Nutzer in dem Zustand, ist das
         * Wissen endgueltig verloren.
         *
         * Also der Griff, den er VORHER hat. Er steht hier und nicht im
         * Panel, weil man beim Deinstallieren in der Modliste ist und nicht
         * in einem Werkzeug - und weil man von hier aus sieht, ob ueberhaupt
         * eine Stadt geladen ist.
         */
        [SettingsUIButton]
        [SettingsUIConfirmation]
        [SettingsUIDisableByCondition(typeof(Setting), nameof(KeineStadt))]
        [SettingsUISection(ReiterAllgemein, GruppeDeinstallation)]
        public bool ParkplaetzeEntfernen
        {
            set
            {
                var welt = World.DefaultGameObjectInjectionWorld;
                var reinigung = welt?
                    .GetExistingSystemManaged<ParkingLotStadtreinigungSystem>();
                if (reinigung == null)
                {
                    Mod.log.Warn("PLT-Stadtreinigung: kein System - es ist "
                        + "keine Stadt geladen.");
                    return;
                }

                if (reinigung.Laeuft)
                {
                    Mod.log.Info("PLT-Stadtreinigung: es laeuft schon ein "
                        + "Abriss. Der zweite Klick bleibt folgenlos.");
                    return;
                }

                reinigung.Bestand(out var abraeumbar, out var ohneTraeger,
                    out var teile);
                if (abraeumbar == 0)
                {
                    /*
                     * "Nichts zu tun" nur sagen, wenn auch wirklich nichts
                     * mehr lebt. Null Lots und trotzdem Teile heisst nicht
                     * "sauber", sondern "Waisen" - und das ist ein Befund.
                     */
                    if (teile == 0 && ohneTraeger == 0)
                        Mod.log.Info("PLT-Stadtreinigung: in dieser Stadt "
                            + "steht kein PLT-Parkplatz. Nichts zu tun.");
                    else
                        Mod.log.Warn("PLT-Stadtreinigung: kein abraeumbarer "
                            + "Parkplatz, aber " + teile + " lebende Teil(e) "
                            + "und " + ohneTraeger + " Flaeche(n) ohne "
                            + "Traegerreferenz. Das sind Waisen - bitte "
                            + "melden, von hier aus sind sie nicht "
                            + "abzuraeumen.");
                    return;
                }

                Mod.log.Info("PLT-Stadtreinigung: Bestand vor dem Abriss - "
                    + abraeumbar + " abraeumbare Parkplaetze, " + ohneTraeger
                    + " ohne Traegerreferenz, " + teile + " lebende Teile. "
                    + "Auftrag gestellt; ausgefuehrt wird er in der "
                    + "naechsten Werkzeugphase, vor CS2s eigener "
                    + "Besitzkaskade.");
                reinigung.Beauftrage();
            }
        }

        /**
         * ABGELEITET, nicht nachgefuehrt: im Hauptmenue gibt es keine Stadt,
         * und dann ist der Knopf ausgegraut statt ins Leere zu greifen.
         *
         * Eine mitgefuehrte Flagge muesste bei jedem Laden und Verlassen
         * nachgezogen werden, und genau daran ist in diesem Projekt schon
         * einmal eine Anzeige haengengeblieben.
         */
        [SettingsUIHidden]
        public bool KeineStadt =>
            Game.SceneFlow.GameManager.instance == null
            || Game.SceneFlow.GameManager.instance.gameMode != Game.GameMode.Game;

        [SettingsUIButton]
        [SettingsUIConfirmation]
        [SettingsUISection(ReiterAllgemein, GruppeZuruecksetzen)]
        public bool AllesZuruecksetzen
        {
            set
            {
                SetDefaults();
                Sprache = Sprachwahl.English;
                Fangauswahl = -1;
                FangauswahlGesetzt = false;
                ApplyAndSave();
                var welt = World.DefaultGameObjectInjectionWorld;
                welt?.GetExistingSystemManaged<ParkingLotUISystem>()
                    ?.VerwirfBenutzerstandards();
                Mod.log.Info("PLT-Einstellungen: alles auf Werkszustand "
                    + "zurueckgesetzt - Optionsseite und Panelstandards.");
            }
        }

        /**
         * WIE GROSS EINE ZONINGFLAECHE HOECHSTENS WERDEN DARF.
         *
         * Ansage des Nutzers am 2026-09-04. Bis dahin standen 25 x 25 fest im
         * Geometriekern.
         *
         * WAS MAN DABEI WISSEN MUSS - und was die Beschreibung im Spiel auch
         * sagt: gezont wird von den vier Strassen des Rings her, und CS2
         * laesst je Strasse hoechstens 6 Parzellen tief bebauen. Ab etwa 12
         * Parzellen Tiefe bleibt in der MITTE ein Streifen, den keine Strasse
         * mehr erreicht; dort wachsen keine Haeuser. Innere Strassen baut der
         * Mod nicht. Der Regler nimmt dem Nutzer die Entscheidung nicht ab,
         * aber er verschweigt ihm die Folge auch nicht.
         */
        /*
         * EIN TEXTFELD, WEIL CS2 FUER ZAHLEN KEINES HAT.
         *
         * Wunsch des Nutzers: *"Nein, kein Regler, sondern zum Zahlen
         * eintragen."* GEPRUEFT im Dekompilat von
         * `Game.UI.Menu.AutomaticSettings`: eine `int`-Eigenschaft wird dort
         * ausschliesslich als Dropdown oder Regler gezeichnet, alles andere
         * ergibt `WidgetType.None` - die Option erschiene dann gar nicht.
         * Ein Eingabefeld gibt es nur fuer `string`
         * (`WidgetType.StringTextInput`).
         *
         * Also steht die Zahl als Text in den Optionen, und der Mod liest sie
         * daraus. Unsinn im Feld faellt auf den Standard zurueck statt das
         * Werkzeug lahmzulegen - ein halb getippter Wert ist ein normaler
         * Zwischenzustand, kein Fehler.
         */
        [SettingsUITextInput]
        [SettingsUISection(ReiterAllgemein, GruppeZoning)]
        public string ZoningMaxBreiteText { get; set; }
            = ParkingGeometry.ZoningMaxStandard.ToString(
                CultureInfo.InvariantCulture);

        [SettingsUITextInput]
        [SettingsUISection(ReiterAllgemein, GruppeZoning)]
        public string ZoningMaxTiefeText { get; set; }
            = ParkingGeometry.ZoningMaxStandard.ToString(
                CultureInfo.InvariantCulture);

        [SettingsUIHidden]
        public int ZoningMaxBreite => Parzellenzahl(ZoningMaxBreiteText);

        [SettingsUIHidden]
        public int ZoningMaxTiefe => Parzellenzahl(ZoningMaxTiefeText);

        /**
         * Liest eine Parzellenzahl aus dem Textfeld.
         *
         * Leer, Buchstaben, ein Minus - alles das ist waehrend des Tippens
         * normal. Dann gilt der Standard; gekappt wird bei
         * `ZoningMaxGrenze`, damit ein verrutschter Tastendruck nicht
         * Millionen Zellen rastert.
         */
        private static int Parzellenzahl(string text)
            => int.TryParse(text?.Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var zahl)
                ? System.Math.Max(1,
                    System.Math.Min(ParkingGeometry.ZoningMaxGrenze, zahl))
                : ParkingGeometry.ZoningMaxStandard;

        public override void SetDefaults()
        {
            ParkplatzLoeschenBestaetigen = true;
            AutomatischZufahrtModus = true;
            AutomatischVersorgung = true;
            VegetationsdichteFaktor = 1f;
            AltRechenwegOhneWarnung = false;
            // Ausdruecklich, nicht nur per Feldvorgabe: "Auf Standard
            // zuruecksetzen" soll den Entwickler-Reiter sicher ausschalten.
            EntwicklerDebug = false;
            Wirtschaft = true;
            Parkgebuehr = 10;
            ZoningMaxBreiteText = ParkingGeometry.ZoningMaxStandard
                .ToString(CultureInfo.InvariantCulture);
            ZoningMaxTiefeText = ParkingGeometry.ZoningMaxStandard
                .ToString(CultureInfo.InvariantCulture);
        }
    }

    /**
     * Die Beschriftungen der Optionsseite.
     *
     * Die Schluesselform stammt aus dem Dekompilat, nicht aus dem Gedaechtnis:
     * `Game.UI.Menu.AutomaticSettings` baut sie als
     * "Options.OPTION[<Seiten-Id>.<Klasse>.<Eigenschaft>]",
     * `OptionsUISystem` die Seite als "Options.SECTION[<Seiten-Id>]" und den
     * Reiter als "Options.TAB[<Seiten-Id>.<Reiter>]". Die Seiten-Id setzt
     * `ModSetting` aus Assembly, Namensraum und Typ des Mods zusammen -
     * deshalb steht sie hier nicht als Zeichenkette, sondern kommt aus `id`.
     */
    public class Beschriftungen : IDictionarySource
    {
        private readonly Setting _setting;
        private readonly bool _deutsch;

        /**
         * Die Aufschrift des Aufraeumknopfs - sie traegt den Zustand.
         *
         * Wird bei jedem Neulesen der Quelle ausgewertet, und das Neulesen
         * stoesst die Stadtreinigung selbst an, wenn sich etwas aendert.
         */
        private string Entfernenknopf()
        {
            switch (Tools.ParkingLotStadtreinigungSystem.Stand)
            {
                case Tools.ParkingLotStadtreinigungSystem
                        .Knopfzustand.Laeuft:
                    var uebrig = Tools.ParkingLotStadtreinigungSystem
                        .StandUebrig;
                    return _deutsch
                        ? "Abriss läuft… noch " + uebrig + " Teile"
                        : "Teardown running… " + uebrig + " parts left";
                case Tools.ParkingLotStadtreinigungSystem
                        .Knopfzustand.Fertig:
                    var weg = Tools.ParkingLotStadtreinigungSystem
                        .StandEntfernt;
                    var stehen = Tools.ParkingLotStadtreinigungSystem
                        .StandStehen;
                    if (stehen > 0)
                        return _deutsch
                            ? "Fertig: " + weg + " entfernt, " + stehen
                              + " blieben stehen — siehe Log"
                            : "Done: " + weg + " removed, " + stehen
                              + " remained — see the log";
                    return _deutsch
                        ? "Fertig: " + weg + " entfernt — jetzt speichern"
                        : "Done: " + weg + " removed — save now";
                case Tools.ParkingLotStadtreinigungSystem
                        .Knopfzustand.Haengt:
                    return _deutsch
                        ? "Kommt nicht voran — siehe Log"
                        : "Not progressing — see the log";
                case Tools.ParkingLotStadtreinigungSystem
                        .Knopfzustand.Nichts:
                    return _deutsch
                        ? "Nichts zu entfernen"
                        : "Nothing to remove";
                default:
                    return _deutsch
                        ? "Alle PLT-Parkplätze entfernen"
                        : "Remove all PLT parking lots";
            }
        }

        public Beschriftungen(Setting setting, bool deutsch)
        {
            _setting = setting;
            /**
             * DIE EINSTELLUNGSSEITE FOLGT DER GEWAEHLTEN SPRACHE - ERST NACH
             * EINEM NEUSTART.
             *
             * CS2 liest die Textquellen einer Optionsseite beim Laden ein und
             * fragt sie danach nicht erneut; ein Umschalten im laufenden Spiel
             * kommt dort nicht an. Deshalb wird hier EINMAL beim Laden
             * entschieden. Der Nutzer am 2026-08-21 dazu: *"dann hat halt der
             * User erst nach nem Neustart die Aenderung. Ich denke besser als
             * nix."*
             *
             * Der Parameter `deutsch` sagt, fuer welche SPIELsprache diese
             * Quelle registriert ist - das interessiert hier nicht mehr:
             * beide registrierten Quellen liefern die Sprache, die der Nutzer
             * im Mod eingestellt hat. Nur so gilt seine Wahl auch dann, wenn
             * das Spiel in einer dritten Sprache laeuft.
             */
            _ = deutsch;
            var gewaehlt = "en";
            try { gewaehlt = setting?.SprachKuerzel() ?? "en"; }
            catch { /* Vor dem ersten Laden gibt es noch keine Wahl. */ }
            _deutsch = gewaehlt == "de";
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(
            IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            var seite = _setting.id;
            var pfad = seite + "." + nameof(Setting) + "."
                       + nameof(Setting.FensterZuruecksetzen);
            var pfadAuto = seite + "." + nameof(Setting) + "."
                       + nameof(Setting.AutomatischZufahrtModus);
            var pfadVersorgung = seite + "." + nameof(Setting) + "."
                       + nameof(Setting.AutomatischVersorgung);
            var pfadLoeschen = seite + "." + nameof(Setting) + "."
                       + nameof(Setting.ParkplatzLoeschenBestaetigen);
            var pfadWirtschaft = seite + "." + nameof(Setting) + "."
                       + nameof(Setting.Wirtschaft);
            var pfadVegetation = seite + "." + nameof(Setting) + "."
                       + nameof(Setting.VegetationsdichteFaktor);
            var pfadZuruecksetzen = seite + "." + nameof(Setting) + "."
                       + nameof(Setting.AllesZuruecksetzen);
            var pfadGebuehr = seite + "." + nameof(Setting) + "."
                       + nameof(Setting.Parkgebuehr);
            var pfadZonBreite = seite + "." + nameof(Setting) + "."
                       + nameof(Setting.ZoningMaxBreiteText);
            var pfadEntwickler = seite + "." + nameof(Setting) + "."
                       + nameof(Setting.EntwicklerDebug);
            var pfadAbsturzspur = seite + "." + nameof(Setting) + "."
                       + nameof(Setting.Absturzspur);
            var pfadEntfernen = seite + "." + nameof(Setting) + "."
                       + nameof(Setting.ParkplaetzeEntfernen);
            var pfadZonTiefe = seite + "." + nameof(Setting) + "."
                       + nameof(Setting.ZoningMaxTiefeText);
            var pfadVersion = seite + "." + nameof(Setting) + "."
                       + nameof(Setting.Version);

            return new Dictionary<string, string>
            {
                { "Options.OPTION[" + pfadVersion + "]",
                    _deutsch ? "Version" : "Version" },
                { "Options.OPTION_DESCRIPTION[" + pfadVersion + "]",
                    _deutsch ? "Die geladene Fassung und wann ihre DLL geschrieben wurde. Bei einer Fehlermeldung gehört beides dazu; die Bauzeit sagt, ob im Spiel wirklich der neue Stand liegt."
                        : "The loaded version and when its DLL was written. Both belong in a bug report; the build time tells you whether the game is really running the newer build." },
                { "Options.OPTION[" + pfadLoeschen + "]",
                    _deutsch ? "Löschen von Parkplätzen bestätigen" : "Confirm parking lot demolition" },
                { "Options.OPTION_DESCRIPTION[" + pfadLoeschen + "]",
                    _deutsch ? "Vor dem Bulldozen eines PLT-Parkplatzes nachfragen. Standard: an. Gilt sofort; Bestätigungen anderer Gebäude bleiben unverändert."
                        : "Ask before bulldozing a PLT parking lot. Default: on. Takes effect immediately; confirmations for other buildings remain unchanged." },
                /*
                 * DER KNOPF SAGT SELBST, WAS LOS IST.
                 *
                 * Vorher stand hier ein fester Text, und die Antwort landete
                 * in einer Logdatei. Wer drueckt, sieht dann nichts passieren
                 * und drueckt nochmal. `ReloadActiveLocale` laesst CS2 diese
                 * Quelle neu lesen, sobald sich der Zustand aendert - der
                 * Text wechselt also im offenen Menue.
                 */
                { "Options.OPTION[" + pfadEntfernen + "]", Entfernenknopf() },
                { "Options.OPTION_DESCRIPTION[" + pfadEntfernen + "]",
                    _deutsch ? "Reißt die mit diesem Mod gebauten Parkplätze in der geladenen Stadt ab. Gedacht für den Schritt VOR dem Deinstallieren: danach kann der Mod nichts mehr aufräumen, und ein ohne ihn gespeicherter Stand verliert die Daten seiner Parkplätze endgültig. Gewachsene Zoning-Häuser bleiben stehen — die gehören dir, nicht dem Mod; ihre Straßen- und Versorgungsanbindung kann allerdings mit abgerissen werden. Warte ab, bis auf dem Knopf „Fertig“ steht, und speichere dann. Im Hauptmenü ausgegraut, weil es dort keine Stadt gibt."
                        : "Demolishes the parking lots this mod built in the loaded city. Meant for the step BEFORE uninstalling: afterwards the mod can no longer clean anything up, and a save written without it loses its parking lot data for good. Grown zoning buildings stay — those are yours, not the mod's; their road and utility connections may go with the teardown, though. Wait until the button says “Done”, then save. Greyed out in the main menu, where there is no city." },
                { "Options.WARNING[" + pfadEntfernen + "]",
                    _deutsch ? "Alle mit diesem Mod gebauten Parkplätze in dieser Stadt abreißen? Das lässt sich im Werkzeug nicht rückgängig machen — sichere vorher deinen Spielstand."
                        : "Demolish every parking lot this mod built in this city? The tool's undo cannot take this back — back up your save first." },
                { "Options.OPTION[" + pfadAbsturzspur + "]",
                    _deutsch ? "Absturzspur mitschreiben"
                        : "Record a crash trace" },
                { "Options.OPTION_DESCRIPTION[" + pfadAbsturzspur + "]",
                    _deutsch ? "Schreibt bei JEDEM Bild mit, welches System des Mods gerade läuft, und zwingt es sofort auf die Platte — damit nach einem Absturz dort steht, wo es passiert ist. Standard: aus, weil es spürbar Leistung kostet. Nur einschalten, wenn du einen Absturz wiederholen kannst."
                        : "Records which of the mod's systems is running on EVERY frame and forces it straight to disk, so after a crash the trace says where it happened. Default: off, because it costs noticeable performance. Only turn it on if you can reproduce a crash." },
                { "Options.SECTION[" + seite + "]", "Parking Lot Tool" },
                {
                    "Options.TAB[" + seite + "." + Setting.ReiterAllgemein + "]",
                    _deutsch ? "Allgemein" : "General"
                },
                {
                    "Options.GROUP[" + seite + "." + Setting.GruppeUeber + "]",
                    _deutsch ? "Über diesen Mod" : "About this mod"
                },
                {
                    "Options.GROUP[" + seite + "." + Setting.GruppeFenster + "]",
                    _deutsch ? "Fenster" : "Window"
                },
                {
                    "Options.OPTION[" + seite + "." + nameof(Setting) + "."
                        + nameof(Setting.Fensterstil) + "]",
                    _deutsch ? "Aufbau des Panels" : "Panel layout"
                },
                {
                    "Options.OPTION_DESCRIPTION[" + seite + "." + nameof(Setting)
                        + "." + nameof(Setting.Fensterstil) + "]",
                    _deutsch
                        ? "Horizontal ist die breite Leiste mit den Spalten "
                          + "nebeneinander. Hochkant ist die schmale Spalte "
                          + "links unten, dort wo auch das Spiel seine "
                          + "Werkzeugeinstellungen zeigt - sie verdeckt am "
                          + "wenigsten. Die Einstellungen sind in beiden "
                          + "dieselben."
                        : "Horizontal is the wide bar with its columns side "
                          + "by side. Upright is the narrow column at the "
                          + "bottom left, where the game shows its own tool "
                          + "options - it covers the least. Both hold the "
                          + "same settings."
                },
                {
                    _setting.GetEnumValueLocaleID(
                        Setting.Fensterstilwahl.Horizontal),
                    _deutsch ? "Horizontal" : "Horizontal"
                },
                {
                    _setting.GetEnumValueLocaleID(
                        Setting.Fensterstilwahl.Hochkant),
                    _deutsch ? "Hochkant" : "Upright"
                },
                {
                    "Options.GROUP[" + seite + "." + Setting.GruppeZufahrt + "]",
                    _deutsch ? "Zufahrten" : "Entrances"
                },
                {
                    "Options.GROUP[" + seite + "." + Setting.GruppeHinweise + "]",
                    _deutsch ? "Hinweise" : "Prompts"
                },
                {
                    "Options.GROUP[" + seite + "."
                        + Setting.GruppeDeinstallation + "]",
                    _deutsch ? "Deinstallation" : "Uninstall"
                },
                {
                    "Options.GROUP[" + seite + "." + Setting.GruppeWirtschaft + "]",
                    _deutsch ? "Wirtschaft" : "Economy"
                },
                {
                    "Options.GROUP[" + seite + "." + Setting.GruppeZoning + "]",
                    _deutsch ? "Zoning" : "Zoning"
                },
                {
                    "Options.GROUP[" + seite + "." + Setting.GruppeVegetation + "]",
                    _deutsch ? "Vegetation" : "Vegetation"
                },
                {
                    "Options.GROUP[" + seite + "." + Setting.GruppeZuruecksetzen + "]",
                    _deutsch ? "Zurücksetzen" : "Reset"
                },
                {
                    "Options.OPTION[" + pfadZuruecksetzen + "]",
                    _deutsch ? "Alle Einstellungen zurücksetzen"
                        : "Reset all settings"
                },
                {
                    "Options.OPTION_DESCRIPTION[" + pfadZuruecksetzen + "]",
                    _deutsch
                        ? "Setzt diese Seite UND die im Panel gemerkten Standards "
                          + "auf den Auslieferungszustand zurück. Die Sprache geht "
                          + "dabei auf Englisch." + "\n\n"
                          + "Gebaute Parkplätze bleiben unberührt - zurückgesetzt "
                          + "wird nur, womit der nächste gebaut wird."
                        : "Resets this page AND the defaults remembered in the "
                          + "panel back to how the mod ships. The language goes "
                          + "back to English." + "\n\n"
                          + "Parking lots you already built are untouched - only "
                          + "the values the next one is built with are reset."
                },
                {
                    "Options.WARNING[" + pfadZuruecksetzen + "]",
                    _deutsch
                        ? "Alle Einstellungen des Mods auf Werkszustand "
                          + "zurücksetzen?"
                        : "Reset every setting of this mod to factory values?"
                },
                {
                    "Options.OPTION[" + pfadVegetation + "]",
                    _deutsch ? "Pflanzdichte" : "Planting density"
                },
                {
                    "Options.OPTION_DESCRIPTION[" + pfadVegetation + "]",
                    _deutsch
                        ? "Wieviele Pflanzen je Quadratmeter gesetzt werden, im "
                          + "Verhältnis zu 1,0." + "\n\n"
                          + "Bei 1,0 hält jede Pflanze genau den Abstand ein, ab "
                          + "dem das Spiel selbst anfängt, sich überschneidende "
                          + "Pflanzen unsichtbar zu machen - alles, was gesetzt "
                          + "wird, steht also auch da. Darüber wird es luftiger. "
                          + "Darunter wird es dichter, und das Spiel blendet "
                          + "einen Teil wieder weg; für Dickicht ist das der "
                          + "Preis." + "\n\n"
                          + "Der Regler im Panel bleibt davon unberührt: er sagt, "
                          + "wieviel vom Möglichen gepflanzt wird. Vorhandene "
                          + "Parkplätze behalten ihre Pflanzen, bis sie neu "
                          + "gebaut werden."
                        : "How many plants per square metre are placed, relative "
                          + "to 1.0." + "\n\n"
                          + "At 1.0 every plant keeps exactly the distance at "
                          + "which the game itself starts hiding overlapping "
                          + "plants - so everything placed is also visible. "
                          + "Above that it gets airier. Below it gets denser and "
                          + "the game hides some again; that is the price of a "
                          + "thicket." + "\n\n"
                          + "The slider in the panel is unaffected: it says how "
                          + "much of the possible is planted. Existing lots keep "
                          + "their plants until they are rebuilt."
                },
                {
                    "Options.GROUP[" + seite + "." + Setting.GruppeEntwickler + "]",
                    _deutsch ? "Entwicklung" : "Development"
                },
                {
                    "Options.OPTION[" + pfadEntwickler + "]",
                    _deutsch ? "Entwickler-Reiter anzeigen"
                        : "Show developer tab"
                },
                {
                    "Options.OPTION_DESCRIPTION[" + pfadEntwickler + "]",
                    _deutsch
                        ? "Blendet im Panel den Reiter \"dev-Debug\" ein. "
                          + "Dort liegen Messwerkzeuge aus der Entwicklung: "
                          + "Zoning-Sonde, Prefab-Zerlegung, Trägertest, "
                          + "Überlappungsmessung, Prefab-Vergleich und das "
                          + "Live-Log." + "\n\n" + "Zum Melden eines Fehlers "
                          + "brauchst du das nicht - dafür ist der Reiter "
                          + "\"Debug\" da, der immer sichtbar ist. Einige "
                          + "dieser Werkzeuge bauen und löschen etwas in "
                          + "deiner Stadt."
                        : "Shows the \"dev-Debug\" tab in the panel. It holds "
                          + "measuring tools from development: zoning probe, "
                          + "prefab dissection, carrier test, overlap scan, "
                          + "prefab comparison and the live log."
                          + "\n\n" + "You do not need this to report a "
                          + "problem - the always-visible \"Debug\" tab is "
                          + "for that. Some of these tools build and delete "
                          + "things in your city."
                },
                {
                    "Options.OPTION[" + pfadZonBreite + "]",
                    _deutsch ? "Zoningfläche: größte Breite"
                        : "Zoning area: maximum width"
                },
                {
                    "Options.OPTION_DESCRIPTION[" + pfadZonBreite + "]",
                    _deutsch
                        ? "Wieviele Parzellen eine gezogene Zoningfläche "
                          + "höchstens breit sein darf. Eine Parzelle ist "
                          + "8 m. Standard 25." + "\n\n" + "Beachte: gezont wird von "
                          + "den Straßen rundherum, und CS2 bebaut je "
                          + "Straße höchstens 6 Parzellen tief. Ab etwa 12 "
                          + "Parzellen bleibt in der Mitte ein Streifen ohne "
                          + "Häuser - dorthin reicht keine Straße mehr."
                        : "How many parcels wide a zoning area may be at "
                          + "most. One parcel is 8 m. Default 25." + "\n\n" + "Note: "
                          + "zoning grows from the surrounding roads, and CS2 "
                          + "builds at most 6 parcels deep per road. Beyond "
                          + "roughly 12 parcels a strip in the middle stays "
                          + "empty - no road reaches it."
                },
                {
                    "Options.OPTION[" + pfadZonTiefe + "]",
                    _deutsch ? "Zoningfläche: größte Tiefe"
                        : "Zoning area: maximum depth"
                },
                {
                    "Options.OPTION_DESCRIPTION[" + pfadZonTiefe + "]",
                    _deutsch
                        ? "Wieviele Parzellen eine gezogene Zoningfläche "
                          + "höchstens tief sein darf. Eine Parzelle ist "
                          + "8 m. Standard 25. Derselbe Hinweis wie bei der "
                          + "Breite gilt auch hier."
                        : "How many parcels deep a zoning area may be at "
                          + "most. One parcel is 8 m. Default 25. The same "
                          + "note as for the width applies here."
                },
                {
                    "Options.OPTION[" + pfadWirtschaft + "]",
                    _deutsch ? "Wirtschaft simulieren" : "Simulate economy"
                },
                {
                    "Options.OPTION_DESCRIPTION[" + pfadWirtschaft + "]",
                    _deutsch
                        ? "Hauptschalter. AN heißt: Unterhaltskosten nach "
                          + "Größe, Parkgebühr, Lärmbelastung, Angestellte "
                          + "und der Komfort, mit dem der Parkplatz um Autos "
                          + "wirbt. AUS heißt: der Parkplatz steht rein "
                          + "baulich da und kostet nichts. "
                          + "Umschalten geht jederzeit und gilt sofort für "
                          + "alle vorhandenen Parkplätze. Eingestellte "
                          + "Gebühren bleiben erhalten und kommen beim "
                          + "Wiedereinschalten zurück. Parkplätze, die im "
                          + "ausgeschalteten Zustand gebaut wurden, bekommen "
                          + "beim Einschalten die Standardgebühr von unten. "
                          + "Strom braucht der Parkplatz in keinem Fall."
                        : "Master switch. ON means: upkeep scaled by size, "
                          + "parking fee, noise pollution, workers, and the "
                          + "comfort that makes the lot attractive to drivers. "
                          + "OFF means: the lot is purely a structure and "
                          + "costs nothing. "
                          + "You can toggle it at any time; it applies at once "
                          + "to every existing lot. Fees you have set are kept "
                          + "and come back when you switch it on again. Lots "
                          + "built while it was off get the default fee below "
                          + "when you switch it on. "
                          + "The lot never needs power."
                },
                {
                    "Options.OPTION[" + pfadGebuehr + "]",
                    _deutsch ? "Standardgebühr für neue Parkplätze"
                             : "Default fee for new parking lots"
                },
                {
                    "Options.OPTION_DESCRIPTION[" + pfadGebuehr + "]",
                    _deutsch
                        ? "Wird beim Bau in den neuen Parkplatz kopiert. "
                          + "Bestehende Parkplätze werden danach einzeln in "
                          + "ihrem Auswahlfenster eingestellt. 0 heisst "
                          + "kostenlos; Einnahmen erscheinen unter 'Parken'."
                        : "Copied into each new parking lot when it is built. "
                          + "Existing lots are then adjusted individually in "
                          + "their selection panel. 0 means free; revenue "
                          + "appears under 'Parking'."
                },
                /**
                 * DEN SCHLUESSEL BAUT DAS SPIEL SELBST.
                 *
                 * `ModSetting.GetEnumValueLocaleID` liefert genau die Form, die
                 * CS2 dann auch nachschlaegt. Vorher standen hier vier geratene
                 * Formen - alle daneben, und im Dropdown stand der rohe
                 * Schluessel. Der Hinweis kam vom Nutzer ueber I18NEverywhere:
                 * andere Mods raten nicht, sie fragen.
                 */
                {
                    _setting.GetEnumValueLocaleID(Setting.Sprachwahl.Automatic),
                    _deutsch ? "Automatisch" : "Automatic"
                },
                {
                    _setting.GetEnumValueLocaleID(Setting.Sprachwahl.English),
                    "English"
                },
                {
                    _setting.GetEnumValueLocaleID(Setting.Sprachwahl.Deutsch),
                    "Deutsch"
                },
                {
                    "Options.GROUP[" + seite + "." + Setting.GruppeTasten + "]",
                    _deutsch ? "Tasten" : "Keyboard"
                },
                {
                    "Options.OPTION[" + seite + "." + nameof(Setting) + "."
                        + nameof(Setting.WerkzeugTaste) + "]",
                    _deutsch ? "Werkzeug öffnen und schließen"
                             : "Open and close the tool"
                },
                {
                    "Options.OPTION_DESCRIPTION[" + seite + "." + nameof(Setting)
                        + "." + nameof(Setting.WerkzeugTaste) + "]",
                    _deutsch
                        ? "Vorgabe ist Strg+Umschalt+P. Früher war es Strg+P - "
                          + "die Kombination gehört aber auch Find It, und dessen "
                          + "Werkzeug behielt die Oberhand. Ändere die Taste hier, "
                          + "wenn sie mit einem anderen Mod kollidiert."
                        : "Default is Ctrl+Shift+P. It used to be Ctrl+P, but Find "
                          + "It uses that combination too and its tool won. Change "
                          + "the key here if it clashes with another mod."
                },
                {
                    "Options.GROUP[" + seite + "." + Setting.GruppeSprache + "]",
                    _deutsch ? "Sprache" : "Language"
                },
                {
                    "Options.OPTION[" + seite + "." + nameof(Setting) + "."
                        + nameof(Setting.Sprache) + "]",
                    _deutsch ? "Sprache der Oberfläche" : "Interface language"
                },
                {
                    "Options.OPTION_DESCRIPTION[" + seite + "." + nameof(Setting) + "."
                        + nameof(Setting.Sprache) + "]",
                    _deutsch
                        ? "Aus: Englisch. Gilt für das Parkplatz-Fenster sofort, für "
                          + "diese Seite nach dem nächsten Start."
                        : "Applies to the parking lot window and its messages. "
                          + "Automatic follows the game language."
                },
                {
                    "Options.OPTION[" + pfadVersorgung + "]",
                    _deutsch
                        ? "Strom und Wasser automatisch anschließen"
                        : "Connect power and water automatically"
                },
                {
                    "Options.OPTION_DESCRIPTION[" + pfadVersorgung + "]",
                    _deutsch
                        ? "Legt beim Bauen selbst eine Stromleitung und ein "
                          + "Doppelrohr bis zum Parkplatz. Gebaut wird immer "
                          + "die kürzeste Verbindung zwischen zwei Teilen, "
                          + "die noch nicht zusammenhängen - liegen mehrere "
                          + "Zoningflächen weit auseinander, hängen sie sich "
                          + "auch aneinander. Mindestens eine Leitung geht "
                          + "dabei immer an eine Stadtstraße, sonst käme kein "
                          + "Strom herein." + "\n\n"
                          + "Umfahren werden unsere eigenen Straßen und "
                          + "fremde Erdleitungen; weist CS2 einen Weg ab, "
                          + "wird ein anderer versucht." + "\n\n"
                          + "Aus: es entstehen keine neuen Leitungen. Bereits "
                          + "von dir gelegte Anschlüsse bleiben beim "
                          + "Bearbeiten trotzdem erhalten - das ist ein "
                          + "eigener Vorgang und hängt nicht an diesem "
                          + "Schalter."
                        : "Lays a power line and a combined pipe to the "
                          + "parking lot while building. It always builds the "
                          + "shortest link between two parts that are not "
                          + "connected yet - several zoning patches far apart "
                          + "may hook up to each other. At least one line "
                          + "always reaches a city road, otherwise no power "
                          + "comes in." + "\n\n"
                          + "It routes around our own roads and around "
                          + "existing buried lines; if CS2 rejects one route, "
                          + "another is tried." + "\n\n"
                          + "Off: no new lines are created. Connections you "
                          + "laid yourself are still preserved when editing - "
                          + "that is a separate mechanism and does not depend "
                          + "on this switch."
                },
                {
                    "Options.OPTION[" + pfadAuto + "]",
                    _deutsch
                        ? "Automatisch in den Zufahrt-Modus wechseln"
                        : "Switch to entrance mode automatically"
                },
                {
                    "Options.OPTION_DESCRIPTION[" + pfadAuto + "]",
                    _deutsch
                        ? "Wechselt direkt nach dem Schließen des Polygons in "
                          + "den Zufahrt-Modus. Aus: das Polygon bleibt zuerst "
                          + "bearbeitbar, und du wechselst selbst, wenn du so "
                          + "weit bist."
                        : "Switches to entrance mode right after the polygon is "
                          + "closed. Off: the polygon stays editable first and "
                          + "you switch over yourself when you are ready."
                },
                {
                    "Options.OPTION[" + pfad + "]",
                    _deutsch ? "Fensterposition zurücksetzen" : "Reset window position"
                },
                {
                    "Options.OPTION_DESCRIPTION[" + pfad + "]",
                    _deutsch
                        ? "Stellt das Parkplatz-Fenster wieder oben an den "
                          + "Rand, waagerecht mittig. Hilft, wenn es aus dem "
                          + "Bild geschoben wurde und nicht mehr zu greifen ist."
                        : "Puts the parking lot window back to the top edge, "
                          + "horizontally centred. Use this if it was dragged "
                          + "off screen and can no longer be grabbed."
                },
            };
        }

        public void Unload() { }
    }
}
