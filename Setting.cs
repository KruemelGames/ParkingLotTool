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
        GruppeZoning, GruppeVegetation, GruppeWirtschaft, GruppeSpielstand,
        GruppeHinweise,
        GruppeFenster, GruppeZuruecksetzen, GruppeEntwickler,
        GruppeDeinstallation)]
    [SettingsUIShowGroupName(GruppeUeber, GruppeSprache, GruppeTasten, GruppeZufahrt,
        GruppeZoning, GruppeVegetation, GruppeWirtschaft, GruppeSpielstand,
        GruppeHinweise,
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
        public const string GruppeSpielstand = "Spielstand";
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

        /**
         * Verwaiste Parkplaetze beim Laden selbst wieder verbinden.
         *
         * Verwaist ist ein Parkplatz, wenn der Spielstand ohne PLT
         * gespeichert wurde. Ansage des Nutzers am 2026-09-24: automatisch
         * nur, wenn diese Einstellung an ist; sonst zeigen Liste und
         * Infofenster einen Reparaturknopf. Standard AUS, wie das
         * automatische Synchronisieren.
         */
        [SettingsUISection(ReiterAllgemein, GruppeSpielstand)]
        public bool WaisenAutomatischReparieren { get; set; } = false;

        /**
         * Bestehende Parkplaetze nach einem Update selbst nachruesten.
         * Ansage des Nutzers am 2026-09-24: Standard AUS; synchron mit dem
         * Schalter in der Parkplatzliste.
         */
        [SettingsUISection(ReiterAllgemein, GruppeSpielstand)]
        public bool AutomatischSynchronisieren { get; set; } = false;

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
         *
         * NUR FUER EINEN LAUF (2026-10-05): `Mod.OnLoad` schaltet sie bei
         * jedem Start wieder aus. Sie blieb sonst nach dem Absturz an und
         * bremste das Spiel ueber Updates hinweg (Issue #6).
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
                /*
                 * SOFORT SPEICHERN. CS2 schreibt die Optionen erst beim
                 * ordentlichen Beenden - und wer diesen Schalter braucht,
                 * beendet nie ordentlich. Am 2026-09-25 war die Spur deshalb
                 * nach jedem Absturz wieder aus, zweimal genau im Lauf, der
                 * sie gebraucht haette. Waehrend `LoadSettings` (noch nicht
                 * `Geladen`) wird nicht gespeichert.
                 */
                if (Geladen) ApplyAndSave();
            }
        }

        private bool _absturzspur;

        /** Von `Mod.OnLoad` nach `LoadSettings` gesetzt. */
        [SettingsUIHidden]
        internal bool Geladen { get; set; }


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
         * WIE TEUER DER UNTERHALT IST - ALS ANTEIL AM VANILLA-NIVEAU.
         *
         * Gerechnet wird weiter je Stellplatz (48 je Bucht plus 438, geeicht
         * an den Vanilla-Parkplaetzen); diese Wahl skaliert nur das Ergebnis.
         * Wunsch eines Spielers (Ghost0993, 204 Buchten = 10.000 im Monat),
         * vom Nutzer am 2026-10-07 so beschlossen: Stufen statt Formel,
         * Standard 100 %. Wirkt sofort auf alle Parkplaetze, weil der
         * Wirtschaftslauf den Wert in jedem Durchgang neu setzt.
         */
        [SettingsUISection(ReiterAllgemein, GruppeWirtschaft)]
        [SettingsUIDisableByCondition(typeof(Setting), nameof(WirtschaftAus))]
        public Unterhaltwahl Unterhalt { get; set; } = Unterhaltwahl.Voll;

        /** Der Wert ist der Prozentsatz - siehe `Mod.UnterhaltFaktor`. */
        public enum Unterhaltwahl
        {
            Voll = 100,
            DreiViertel = 75,
            Haelfte = 50,
            Viertel = 25,
            Aus = 0,
        }

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
                    ? Geometry.Sprachtexte.Text("settings.Version.wert",
                        ("nummer", nummer),
                        ("gebaut", gebaut.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm",
                            System.Globalization.CultureInfo.InvariantCulture)))
                    : nummer;
            }
        }

        [SettingsUISection(ReiterAllgemein, GruppeSprache)]
        public Sprachwahl Sprache { get; set; } = Sprachwahl.English;

        /**
         * Die Sprachdatei, aus der angezeigt wird ("en-US", "de-DE", ...).
         *
         * "Automatisch" folgt der Spielsprache, sofern es dafuer eine Datei
         * gibt - sonst Englisch. Die Spielsprache kommt als Kennung des
         * Spiels ("zh-HANS"), und genau so heissen die Dateien in `Lang/`.
         */
        internal string SprachId()
        {
            if (Sprache == Sprachwahl.Deutsch) return "de-DE";
            if (Sprache == Sprachwahl.English) return Geometry.Sprachtexte.Rueckfall;
            try
            {
                var aktiv = Game.SceneFlow.GameManager.instance?.localizationManager
                    ?.activeLocaleId;
                return aktiv != null && Geometry.Sprachtexte.Sprachen.ContainsKey(aktiv)
                    ? aktiv
                    : Geometry.Sprachtexte.Rueckfall;
            }
            catch
            {
                return Geometry.Sprachtexte.Rueckfall;
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
            WaisenAutomatischReparieren = false;
            AutomatischSynchronisieren = false;
            VegetationsdichteFaktor = 1f;
            AltRechenwegOhneWarnung = false;
            // Ausdruecklich, nicht nur per Feldvorgabe: "Auf Standard
            // zuruecksetzen" soll den Entwickler-Reiter sicher ausschalten.
            EntwicklerDebug = false;
            Wirtschaft = true;
            Parkgebuehr = 10;
            Unterhalt = Unterhaltwahl.Voll;
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
        private string _sprache = Geometry.Sprachtexte.Rueckfall;

        private string S(string schluessel, params (string Name, object Wert)[] werte)
            => Geometry.Sprachtexte.TextIn(_sprache, schluessel, werte);

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
                    return S("settings.Entfernen.knopf.laeuft", ("uebrig", uebrig));
                case Tools.ParkingLotStadtreinigungSystem
                        .Knopfzustand.Fertig:
                    var weg = Tools.ParkingLotStadtreinigungSystem
                        .StandEntfernt;
                    var stehen = Tools.ParkingLotStadtreinigungSystem
                        .StandStehen;
                    if (stehen > 0)
                        return S("settings.Entfernen.knopf.fertigMitResten", ("weg", weg), ("stehen", stehen));
                    return S("settings.Entfernen.knopf.fertig", ("weg", weg));
                case Tools.ParkingLotStadtreinigungSystem
                        .Knopfzustand.Haengt:
                    return S("settings.Entfernen.knopf.haengt");
                case Tools.ParkingLotStadtreinigungSystem
                        .Knopfzustand.Nichts:
                    return S("settings.Entfernen.knopf.nichts");
                default:
                    return S("settings.Entfernen.knopf.bereit");
            }
        }

        public Beschriftungen(Setting setting)
        {
            _setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(
            IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            /**
             * DIE SPRACHE WIRD BEI JEDEM LESEN BESTIMMT.
             *
             * Bis 1.0.6 stand sie einmal im Konstruktor fest - und der lief vor
             * `LoadSettings`, kannte also nur den Werksstand. Jetzt liest das
             * Spiel diese Quelle nach jedem Sprachwechsel neu
             * (`ParkingLotSprachdateien.Pflege`), und sie antwortet in der
             * Sprache, die gerade gilt. Dieselbe Quelle steht unter jeder
             * Spielsprache; so gilt die Wahl im Mod auch dann, wenn das Spiel
             * anders eingestellt ist.
             */
            try { _sprache = _setting?.SprachId() ?? Geometry.Sprachtexte.Rueckfall; }
            catch { _sprache = Geometry.Sprachtexte.Rueckfall; }
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
                    S("settings.Version.label")},
                { "Options.OPTION_DESCRIPTION[" + pfadVersion + "]",
                    S("settings.Version.desc")},
                { "Options.OPTION[" + pfadLoeschen + "]",
                    S("settings.Loeschen.label")},
                { "Options.OPTION_DESCRIPTION[" + pfadLoeschen + "]",
                    S("settings.Loeschen.desc")},
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
                    S("settings.Entfernen.desc")},
                { "Options.WARNING[" + pfadEntfernen + "]",
                    S("settings.Entfernen.warning")},
                { "Options.OPTION[" + pfadAbsturzspur + "]",
                    S("settings.Absturzspur.label")},
                { "Options.OPTION_DESCRIPTION[" + pfadAbsturzspur + "]",
                    S("settings.Absturzspur.desc")},
                { "Options.SECTION[" + seite + "]", S("settings.section") },
                {
                    "Options.TAB[" + seite + "." + Setting.ReiterAllgemein + "]",
                    S("settings.tab.Allgemein")},
                {
                    "Options.GROUP[" + seite + "." + Setting.GruppeUeber + "]",
                    S("settings.group.Ueber")},
                {
                    "Options.GROUP[" + seite + "." + Setting.GruppeFenster + "]",
                    S("settings.group.Fenster")},
                {
                    "Options.OPTION[" + seite + "." + nameof(Setting) + "."
                        + nameof(Setting.Fensterstil) + "]",
                    S("settings.Fensterstil.label")},
                {
                    "Options.OPTION_DESCRIPTION[" + seite + "." + nameof(Setting)
                        + "." + nameof(Setting.Fensterstil) + "]",
                    S("settings.Fensterstil.desc")},
                {
                    _setting.GetEnumValueLocaleID(
                        Setting.Fensterstilwahl.Horizontal),
                    S("settings.enum.Fensterstilwahl.Horizontal")},
                {
                    _setting.GetEnumValueLocaleID(
                        Setting.Fensterstilwahl.Hochkant),
                    S("settings.enum.Fensterstilwahl.Hochkant")},
                {
                    "Options.GROUP[" + seite + "." + Setting.GruppeZufahrt + "]",
                    S("settings.group.Zufahrt")},
                {
                    "Options.GROUP[" + seite + "." + Setting.GruppeHinweise + "]",
                    S("settings.group.Hinweise")},
                {
                    "Options.GROUP[" + seite + "." + Setting.GruppeSpielstand + "]",
                    S("settings.group.Spielstand")},
                {
                    "Options.OPTION[" + seite + "." + nameof(Setting) + "."
                        + nameof(Setting.AutomatischSynchronisieren) + "]",
                    S("settings.AutomatischSynchronisieren.label")},
                {
                    "Options.OPTION_DESCRIPTION[" + seite + "." + nameof(Setting) + "."
                        + nameof(Setting.AutomatischSynchronisieren) + "]",
                    S("settings.AutomatischSynchronisieren.desc")},
                {
                    "Options.OPTION[" + seite + "." + nameof(Setting) + "."
                        + nameof(Setting.WaisenAutomatischReparieren) + "]",
                    S("settings.WaisenAutomatischReparieren.label")},
                {
                    "Options.OPTION_DESCRIPTION[" + seite + "." + nameof(Setting) + "."
                        + nameof(Setting.WaisenAutomatischReparieren) + "]",
                    S("settings.WaisenAutomatischReparieren.desc")},
                {
                    "Options.GROUP[" + seite + "."
                        + Setting.GruppeDeinstallation + "]",
                    S("settings.group.Deinstallation")},
                {
                    "Options.GROUP[" + seite + "." + Setting.GruppeWirtschaft + "]",
                    S("settings.group.Wirtschaft")},
                {
                    "Options.GROUP[" + seite + "." + Setting.GruppeZoning + "]",
                    S("settings.group.Zoning")},
                {
                    "Options.GROUP[" + seite + "." + Setting.GruppeVegetation + "]",
                    S("settings.group.Vegetation")},
                {
                    "Options.GROUP[" + seite + "." + Setting.GruppeZuruecksetzen + "]",
                    S("settings.group.Zuruecksetzen")},
                {
                    "Options.OPTION[" + pfadZuruecksetzen + "]",
                    S("settings.Zuruecksetzen.label")},
                {
                    "Options.OPTION_DESCRIPTION[" + pfadZuruecksetzen + "]",
                    S("settings.Zuruecksetzen.desc")},
                {
                    "Options.WARNING[" + pfadZuruecksetzen + "]",
                    S("settings.Zuruecksetzen.warning")},
                {
                    "Options.OPTION[" + pfadVegetation + "]",
                    S("settings.Vegetation.label")},
                {
                    "Options.OPTION_DESCRIPTION[" + pfadVegetation + "]",
                    S("settings.Vegetation.desc")},
                {
                    "Options.GROUP[" + seite + "." + Setting.GruppeEntwickler + "]",
                    S("settings.group.Entwickler")},
                {
                    "Options.OPTION[" + pfadEntwickler + "]",
                    S("settings.Entwickler.label")},
                {
                    "Options.OPTION_DESCRIPTION[" + pfadEntwickler + "]",
                    S("settings.Entwickler.desc")},
                {
                    "Options.OPTION[" + pfadZonBreite + "]",
                    S("settings.ZonBreite.label")},
                {
                    "Options.OPTION_DESCRIPTION[" + pfadZonBreite + "]",
                    S("settings.ZonBreite.desc")},
                {
                    "Options.OPTION[" + pfadZonTiefe + "]",
                    S("settings.ZonTiefe.label")},
                {
                    "Options.OPTION_DESCRIPTION[" + pfadZonTiefe + "]",
                    S("settings.ZonTiefe.desc")},
                {
                    "Options.OPTION[" + pfadWirtschaft + "]",
                    S("settings.Wirtschaft.label")},
                {
                    "Options.OPTION_DESCRIPTION[" + pfadWirtschaft + "]",
                    S("settings.Wirtschaft.desc")},
                {
                    "Options.OPTION[" + pfadGebuehr + "]",
                    S("settings.Gebuehr.label")},
                {
                    "Options.OPTION_DESCRIPTION[" + pfadGebuehr + "]",
                    S("settings.Gebuehr.desc")},
                {
                    "Options.OPTION[" + seite + "." + nameof(Setting) + "."
                        + nameof(Setting.Unterhalt) + "]",
                    S("settings.Unterhalt.label")},
                {
                    "Options.OPTION_DESCRIPTION[" + seite + "." + nameof(Setting) + "."
                        + nameof(Setting.Unterhalt) + "]",
                    S("settings.Unterhalt.desc")},
                { _setting.GetEnumValueLocaleID(Setting.Unterhaltwahl.Voll), S("settings.enum.Unterhaltwahl.Voll") },
                { _setting.GetEnumValueLocaleID(Setting.Unterhaltwahl.DreiViertel), S("settings.enum.Unterhaltwahl.DreiViertel") },
                { _setting.GetEnumValueLocaleID(Setting.Unterhaltwahl.Haelfte), S("settings.enum.Unterhaltwahl.Haelfte") },
                { _setting.GetEnumValueLocaleID(Setting.Unterhaltwahl.Viertel), S("settings.enum.Unterhaltwahl.Viertel") },
                { _setting.GetEnumValueLocaleID(Setting.Unterhaltwahl.Aus), S("settings.enum.Unterhaltwahl.Aus") },
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
                    S("settings.enum.Sprachwahl.Automatic")},
                {
                    _setting.GetEnumValueLocaleID(Setting.Sprachwahl.English),
                    S("settings.enum.Sprachwahl.English")},
                {
                    _setting.GetEnumValueLocaleID(Setting.Sprachwahl.Deutsch),
                    S("settings.enum.Sprachwahl.Deutsch")},
                {
                    "Options.GROUP[" + seite + "." + Setting.GruppeTasten + "]",
                    S("settings.group.Tasten")},
                {
                    "Options.OPTION[" + seite + "." + nameof(Setting) + "."
                        + nameof(Setting.WerkzeugTaste) + "]",
                    S("settings.WerkzeugTaste.label")},
                {
                    "Options.OPTION_DESCRIPTION[" + seite + "." + nameof(Setting)
                        + "." + nameof(Setting.WerkzeugTaste) + "]",
                    S("settings.WerkzeugTaste.desc")},
                /*
                 * DIE TASTENBELEGUNGS-SEITE DES SPIELS. CS2 listet die Aktion
                 * dort unter dem Namen des Mods; ohne diese drei Eintraege
                 * stand der rohe Schluessel da.
                 */
                { _setting.GetBindingMapLocaleID(), S("settings.section") },
                {
                    _setting.GetBindingKeyLocaleID(Setting.AktionWerkzeug),
                    S("settings.WerkzeugTaste.label")},
                {
                    _setting.GetBindingKeyHintLocaleID(Setting.AktionWerkzeug),
                    S("settings.WerkzeugTaste.label")},
                {
                    "Options.GROUP[" + seite + "." + Setting.GruppeSprache + "]",
                    S("settings.group.Sprache")},
                {
                    "Options.OPTION[" + seite + "." + nameof(Setting) + "."
                        + nameof(Setting.Sprache) + "]",
                    S("settings.Sprache.label")},
                {
                    "Options.OPTION_DESCRIPTION[" + seite + "." + nameof(Setting) + "."
                        + nameof(Setting.Sprache) + "]",
                    S("settings.Sprache.desc")},
                {
                    "Options.OPTION[" + pfadVersorgung + "]",
                    S("settings.Versorgung.label")},
                {
                    "Options.OPTION_DESCRIPTION[" + pfadVersorgung + "]",
                    S("settings.Versorgung.desc")},
                {
                    "Options.OPTION[" + pfadAuto + "]",
                    S("settings.Auto.label")},
                {
                    "Options.OPTION_DESCRIPTION[" + pfadAuto + "]",
                    S("settings.Auto.desc")},
                {
                    "Options.OPTION[" + pfad + "]",
                    S("settings.FensterZuruecksetzen.label")},
                {
                    "Options.OPTION_DESCRIPTION[" + pfad + "]",
                    S("settings.FensterZuruecksetzen.desc")},
            };
        }

        public void Unload() { }
    }
}
