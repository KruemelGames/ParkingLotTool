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
    [SettingsUIGroupOrder(GruppeWirtschaft, GruppeZoning, GruppeTasten,
        GruppeSprache, GruppeZufahrt, GruppeHinweise, GruppeFenster,
        GruppeEntwickler)]
    [SettingsUIShowGroupName(GruppeWirtschaft, GruppeZoning, GruppeTasten,
        GruppeSprache, GruppeZufahrt, GruppeHinweise, GruppeFenster,
        GruppeEntwickler)]
    [SettingsUIKeyboardAction(AktionWerkzeug, ActionType.Button, usages: new[] { "PLT" })]
    public class Setting : ModSetting
    {
        public const string ReiterAllgemein = "Allgemein";
        public const string GruppeFenster = "Fenster";
        public const string GruppeHinweise = "Hinweise";
        public const string GruppeWirtschaft = "Wirtschaft";
        public const string GruppeZufahrt = "Zufahrt";
        public const string GruppeSprache = "Sprache";
        public const string GruppeTasten = "Tasten";
        public const string GruppeZoning = "Zoning";
        public const string GruppeEntwickler = "Entwickler";

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
        [SettingsUISection(ReiterAllgemein, GruppeHinweise)]
        public bool AltRechenwegOhneWarnung { get; set; } = false;

        [SettingsUISection(ReiterAllgemein, GruppeHinweise)]
        public bool ParkplatzLoeschenBestaetigen { get; set; } = true;


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
            var pfadAltweg = seite + "." + nameof(Setting) + "."
                       + nameof(Setting.AltRechenwegOhneWarnung);
            var pfadWirtschaft = seite + "." + nameof(Setting) + "."
                       + nameof(Setting.Wirtschaft);
            var pfadGebuehr = seite + "." + nameof(Setting) + "."
                       + nameof(Setting.Parkgebuehr);
            var pfadZonBreite = seite + "." + nameof(Setting) + "."
                       + nameof(Setting.ZoningMaxBreiteText);
            var pfadEntwickler = seite + "." + nameof(Setting) + "."
                       + nameof(Setting.EntwicklerDebug);
            var pfadZonTiefe = seite + "." + nameof(Setting) + "."
                       + nameof(Setting.ZoningMaxTiefeText);

            return new Dictionary<string, string>
            {
                { "Options.OPTION[" + pfadLoeschen + "]",
                    _deutsch ? "Löschen von Parkplätzen bestätigen" : "Confirm parking lot demolition" },
                { "Options.OPTION_DESCRIPTION[" + pfadLoeschen + "]",
                    _deutsch ? "Vor dem Bulldozen eines PLT-Parkplatzes nachfragen. Standard: an. Gilt sofort; Bestätigungen anderer Gebäude bleiben unverändert."
                        : "Ask before bulldozing a PLT parking lot. Default: on. Takes effect immediately; confirmations for other buildings remain unchanged." },
                { "Options.SECTION[" + seite + "]", "Parking Lot Tool" },
                {
                    "Options.TAB[" + seite + "." + Setting.ReiterAllgemein + "]",
                    _deutsch ? "Allgemein" : "General"
                },
                {
                    "Options.GROUP[" + seite + "." + Setting.GruppeFenster + "]",
                    _deutsch ? "Fenster" : "Window"
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
                    "Options.GROUP[" + seite + "." + Setting.GruppeWirtschaft + "]",
                    _deutsch ? "Wirtschaft" : "Economy"
                },
                {
                    "Options.GROUP[" + seite + "." + Setting.GruppeZoning + "]",
                    _deutsch ? "Zoning" : "Zoning"
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
                          + "Zoning-Sonde, Prefab-Zerlegung, Traegertest, "
                          + "Ueberlappungsmessung, Prefab-Vergleich und das "
                          + "Live-Log." + "\n\n" + "Zum Melden eines Fehlers "
                          + "brauchst du das nicht - dafuer ist der Reiter "
                          + "\"Debug\" da, der immer sichtbar ist. Einige "
                          + "dieser Werkzeuge bauen und loeschen etwas in "
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
                    _deutsch ? "Zoningflaeche: groesste Breite"
                        : "Zoning area: maximum width"
                },
                {
                    "Options.OPTION_DESCRIPTION[" + pfadZonBreite + "]",
                    _deutsch
                        ? "Wieviele Parzellen eine gezogene Zoningflaeche "
                          + "hoechstens breit sein darf. Eine Parzelle ist "
                          + "8 m. Standard 25." + "\n\n" + "Beachte: gezont wird von "
                          + "den Strassen rundherum, und CS2 bebaut je "
                          + "Strasse hoechstens 6 Parzellen tief. Ab etwa 12 "
                          + "Parzellen bleibt in der Mitte ein Streifen ohne "
                          + "Haeuser - dorthin reicht keine Strasse mehr."
                        : "How many parcels wide a zoning area may be at "
                          + "most. One parcel is 8 m. Default 25." + "\n\n" + "Note: "
                          + "zoning grows from the surrounding roads, and CS2 "
                          + "builds at most 6 parcels deep per road. Beyond "
                          + "roughly 12 parcels a strip in the middle stays "
                          + "empty - no road reaches it."
                },
                {
                    "Options.OPTION[" + pfadZonTiefe + "]",
                    _deutsch ? "Zoningflaeche: groesste Tiefe"
                        : "Zoning area: maximum depth"
                },
                {
                    "Options.OPTION_DESCRIPTION[" + pfadZonTiefe + "]",
                    _deutsch
                        ? "Wieviele Parzellen eine gezogene Zoningflaeche "
                          + "hoechstens tief sein darf. Eine Parzelle ist "
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
                        ? "Hauptschalter. AN heisst: Unterhaltskosten nach "
                          + "Groesse, Parkgebuehr, Laermbelastung, Angestellte "
                          + "und der Komfort, mit dem der Parkplatz um Autos "
                          + "wirbt. AUS heisst: der Parkplatz steht rein "
                          + "baulich da und kostet nichts. "
                          + "Umschalten geht jederzeit und gilt sofort fuer "
                          + "alle vorhandenen Parkplaetze. Eingestellte "
                          + "Gebuehren bleiben erhalten und kommen beim "
                          + "Wiedereinschalten zurueck. Parkplaetze, die im "
                          + "ausgeschalteten Zustand gebaut wurden, bekommen "
                          + "beim Einschalten die Standardgebuehr von unten. "
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
                    _deutsch ? "Standardgebuehr fuer neue Parkplaetze"
                             : "Default fee for new parking lots"
                },
                {
                    "Options.OPTION_DESCRIPTION[" + pfadGebuehr + "]",
                    _deutsch
                        ? "Wird beim Bau in den neuen Parkplatz kopiert. "
                          + "Bestehende Parkplaetze werden danach einzeln in "
                          + "ihrem Auswahlfenster eingestellt. 0 heisst "
                          + "kostenlos; Einnahmen erscheinen unter 'Parken'."
                        : "Copied into each new parking lot when it is built. "
                          + "Existing lots are then adjusted individually in "
                          + "their selection panel. 0 means free; revenue "
                          + "appears under 'Parking'."
                },
                {
                    "Options.OPTION[" + pfadAltweg + "]",
                    _deutsch
                        ? "Nachfrage beim alten Rechenweg ausblenden"
                        : "Hide the prompt for the old engine"
                },
                {
                    "Options.OPTION_DESCRIPTION[" + pfadAltweg + "]",
                    _deutsch
                        ? "Beim Umschalten auf den alten Rechenweg kommt sonst "
                          + "eine Nachfrage. Der alte Weg ist nur noch ein "
                          + "Rueckfall: langsamer, und bei groesseren Flaechen "
                          + "gibt er auf und laesst Luecken im Belag. Setzt du "
                          + "den Haken im Dialog selbst, landet er hier - so "
                          + "findest du ihn wieder."
                        : "Switching to the old engine asks for confirmation "
                          + "first. The old engine is only a fallback: slower, "
                          + "and on larger shapes it gives up and leaves gaps "
                          + "in the pavement. Ticking the box in the dialog "
                          + "sets this option, so you can find it again."
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
                    _deutsch ? "Werkzeug oeffnen und schliessen"
                             : "Open and close the tool"
                },
                {
                    "Options.OPTION_DESCRIPTION[" + seite + "." + nameof(Setting)
                        + "." + nameof(Setting.WerkzeugTaste) + "]",
                    _deutsch
                        ? "Vorgabe ist Strg+Umschalt+P. Frueher war es Strg+P - "
                          + "die Kombination gehoert aber auch Find It, und dessen "
                          + "Werkzeug behielt die Oberhand. Aendere die Taste hier, "
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
                        ? "Strom und Wasser automatisch anschliessen"
                        : "Connect power and water automatically"
                },
                {
                    "Options.OPTION_DESCRIPTION[" + pfadVersorgung + "]",
                    _deutsch
                        ? "Legt beim Bauen selbst eine Stromleitung und ein "
                          + "Doppelrohr von der naechsten Strasse zum "
                          + "Parkplatz. Die Leitung setzt an einer Sackgasse "
                          + "an, sonst an einer Ecke, und laeuft nie unter "
                          + "unseren eigenen Strassen entlang." + "\n\n"
                          + "Aus: es entstehen keine neuen Leitungen. Bereits "
                          + "von dir gelegte Anschluesse bleiben beim "
                          + "Bearbeiten trotzdem erhalten - das ist ein "
                          + "eigener Vorgang und haengt nicht an diesem "
                          + "Schalter."
                        : "Lays a power line and a combined pipe from the "
                          + "nearest road to the parking lot while building. "
                          + "The line starts at a dead end, otherwise at a "
                          + "corner, and never runs underneath our own roads."
                          + "\n\n"
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
                        ? "Wechselt direkt nach dem Schliessen des Polygons in "
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
