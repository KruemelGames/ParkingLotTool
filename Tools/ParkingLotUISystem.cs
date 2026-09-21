using static ParkingLotTool.Tools.ParkingLotTexte;
using System;
using System.IO;
using Colossal.UI.Binding;
using Game.UI;
using Newtonsoft.Json;
using ParkingLotTool.Geometry;
using UnityEngine;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * Die Bruecke zwischen Panel und Geometrie.
     *
     * Das Panel selbst ist React/TypeScript unter `UI/` und spricht nur ueber
     * diese Bindings; hier liegt der einzige gueltige Stand der Einstellungen.
     * `ParkingLotToolSystem` liest ihn ueber `CurrentSettings` und rechnet neu,
     * sobald sich `Revision` aendert - so gibt es keine zweite Wahrheit.
     *
     * NICHT REGELBAR ist das Buchtmass. 3,0 x 5,9 m gibt CS2 vor (jeder
     * Parkplatz-Aufkleber traegt eine 2,9x5,9-Spur), und ein Regler dafuer
     * wuerde nur Layouts erzeugen, die das Spiel nicht darstellen kann. Es
     * steht deshalb nur im Log, nicht als Bedienelement im Panel.
     */
    public sealed partial class ParkingLotUISystem : UISystemBase
    {
        private const string Group = "ParkingLotTool";
        // Version 2: "Abstand Verbindungen" in Metern wurde zu
        // "Verbindung alle N Buchten". Version-1-Dateien werden beim Laden
        // umgerechnet, damit die uebrigen gespeicherten Standards erhalten
        // bleiben - ein blosser Versionssprung haette sie alle verworfen.
        private const int SettingsVersion = 2;

        /** Buchtbreite fuer die Umrechnung Buchten <-> Meter. */
        private static double Buchtbreite => LayoutSettings.Cs2.Sw;

        /**
         * Genau die neun Werte, die der Nutzer im Panel aendern kann.
         * `GreenMedian` bleibt getrennt von `MedianWidth`, weil ein
         * abgeschalteter Streifen seine gemerkte Tiefe nicht verlieren darf.
         */
        [JsonObject(MemberSerialization.OptIn)]
        private sealed class UserDefaults
        {
            [JsonProperty("version", Required = Required.Always)]
            public int Version { get; set; }

            [JsonProperty("EdgeSetback", Required = Required.Always)]
            public float EdgeSetback { get; set; }

            [JsonProperty("AisleWidth", Required = Required.Always)]
            public float AisleWidth { get; set; }

            [JsonProperty("CrossWidth", Required = Required.Always)]
            public float CrossWidth { get; set; }

            [JsonProperty("MedianWidth", Required = Required.Always)]
            public float MedianWidth { get; set; }

            /**
             * Wie viele Buchten zwischen zwei Verbindungsstrassen liegen.
             *
             * Frueher stand hier ein Abstand in Metern. Der Nutzer hat am
             * 2026-08-21 darauf bestanden, dass an dem Regler steht, was
             * wirklich passiert: gerechnet wird in Buchten, damit alle
             * Abschnitte gleich viele tragen.
             */
            [JsonProperty("CrossBays", Required = Required.Default)]
            public float CrossBays { get; set; }

            /**
             * Die beiden Flaechen. FREIWILLIG (`Required.Default`), damit eine
             * Datei aus der Zeit vor der Flaechenwahl weiter gilt - fehlt der
             * Wert, greifen die Werkswerte. Ein Versionssprung haette hier
             * alle uebrigen gespeicherten Standards mitgerissen.
             */
            [JsonProperty("SurfaceRoad", Required = Required.Default)]
            public string SurfaceRoad { get; set; }

            [JsonProperty("SurfaceDecoration", Required = Required.Default)]
            public string SurfaceDecoration { get; set; }

            /**
             * DREI SCHALTER, DIE BISHER NIEMAND SPEICHERN KONNTE.
             *
             * Im Panel hing an ihnen eine Diskette wie an jeder anderen
             * Einstellung, aber `SetAsDefault` kannte ihre Schluessel nicht -
             * der Knopf schrieb nichts und meldete auch nichts. Nach einem
             * Neustart standen sie wieder auf "an". Genau davor warnt der
             * Kommentar an `SetAsDefault` selbst: sonst ist die Diskette ein
             * falsches Versprechen.
             *
             * `bool?` und freiwillig, nicht `bool` und Pflicht: eine
             * Einstellungsdatei aus der Zeit davor kennt die Schluessel nicht,
             * und `false` waere dort die falsche Antwort. Fehlt der Wert, ist
             * der Schalter an - so, wie es bisher nach jedem Start war.
             */
            [JsonProperty("SurfaceRoadOn", Required = Required.Default)]
            public bool? SurfaceRoadOn { get; set; }

            [JsonProperty("SurfaceDecorationOn", Required = Required.Default)]
            public bool? SurfaceDecorationOn { get; set; }

            /**
             * Laeuft der Belag ueber den Fussgaengerweg bis an die Strasse?
             * Ein Schalter fuer ALLE Zufahrten, so entschieden.
             */
            [JsonProperty("SurfaceApronOn", Required = Required.Default)]
            public bool? SurfaceApronOn { get; set; }

            [JsonProperty("BayIcons", Required = Required.Default)]
            public bool? BayIcons { get; set; }

            /** Nur noch fuer die Umrechnung einer Version-1-Datei. */
            [JsonProperty("CrossSpacing", Required = Required.Default)]
            public float CrossSpacing { get; set; }

            [JsonProperty("RowAngle", Required = Required.Always)]
            public float RowAngle { get; set; }

            [JsonProperty("GreenMedian", Required = Required.Always)]
            public bool GreenMedian { get; set; }

            [JsonProperty("CrossCaps", Required = Required.Always)]
            public bool CrossCaps { get; set; }

            /**
             * DIESE EIGENSCHAFT HATTE KEIN `JsonProperty` - und die Klasse
             * ist `MemberSerialization.OptIn`.
             *
             * Damit wurde sie NIE geschrieben und nie gelesen. Der
             * Merkknopf an "Perimeter road" sah aus wie die anderen
             * dreizehn, meldete "Benutzerstandard gespeichert" und war nach
             * jedem Neustart wieder auf `true`. Gemessen am 2026-09-21 an
             * der Datei des Nutzers: 21 Schluessel, keiner davon
             * `Randstrassen`.
             *
             * `Required.Default`, damit die vorhandenen Dateien ohne diesen
             * Schluessel weiter gelten - dann greift der Initialisierer und
             * es bleibt beim bisherigen Verhalten, statt dass die ganze
             * Datei als ungueltig verworfen wird.
             */
            [JsonProperty("Randstrassen", Required = Required.Default)]
            public bool Randstrassen { get; set; } = true;

            [JsonProperty("AngleMode", Required = Required.Always)]
            public string AngleMode { get; set; }

            /**
             * Wo das Fenster steht - als ANTEIL des Bildschirms, 0 bis 1.
             *
             * Weder rem noch Pixel taugen dafuer. `rem` haengt in CS2 an der
             * Fenstergroesse (`html { font-size: .0925926vh }`), und eine
             * Pixelangabe liegt bei jeder anderen Aufloesung woanders. Ein
             * Anteil sitzt auf jedem Bildschirm an derselben Stelle, und die
             * Oberflaeche muss den rem-Faktor nirgends ausrechnen.
             *
             * Freiwillig (`Required.Default`), damit eine Datei aus der Zeit
             * vor dem Verschieben weiter gilt: fehlt der Wert, greift die
             * Werksposition statt der Ecke 0,0.
             */
            [JsonProperty("PanelX", Required = Required.Default)]
            public float? PanelX { get; set; }

            [JsonProperty("PanelY", Required = Required.Default)]
            public float? PanelY { get; set; }

            /**
             * Die Ecke der HOCHKANT-Spalte, getrennt von der Leiste.
             *
             * Ein gemeinsames Paar waere beim ersten Stilwechsel verloren:
             * 1680 rem breit und 364 rem breit gehoeren nicht an dieselbe
             * Stelle, und wer zurueckschaltet, faende sein Fenster woanders.
             */
            [JsonProperty("PanelXHochkant", Required = Required.Default)]
            public float? PanelXHochkant { get; set; }

            [JsonProperty("PanelYHochkant", Required = Required.Default)]
            public float? PanelYHochkant { get; set; }

            internal UserDefaults Clone() => new UserDefaults
            {
                Version = Version,
                EdgeSetback = EdgeSetback,
                AisleWidth = AisleWidth,
                CrossWidth = CrossWidth,
                MedianWidth = MedianWidth,
                CrossBays = CrossBays,
                SurfaceRoad = SurfaceRoad,
                SurfaceDecoration = SurfaceDecoration,
                SurfaceRoadOn = SurfaceRoadOn,
                SurfaceDecorationOn = SurfaceDecorationOn,
                SurfaceApronOn = SurfaceApronOn,
                BayIcons = BayIcons,
                CrossSpacing = CrossSpacing,
                RowAngle = RowAngle,
                GreenMedian = GreenMedian,
                CrossCaps = CrossCaps,
            Randstrassen = Randstrassen,
                AngleMode = AngleMode,
                PanelX = PanelX,
                PanelY = PanelY,
                PanelXHochkant = PanelXHochkant,
                PanelYHochkant = PanelYHochkant,
            };
        }

        private UserDefaults _defaults;

        private ValueBinding<bool> _panelOpen;
        private ValueBinding<bool> _toolActive;
        private ValueBinding<float> _edgeSetback;
        private ValueBinding<float> _aisleWidth;
        private ValueBinding<float> _crossWidth;
        private ValueBinding<bool> _greenMedian;
        private ValueBinding<bool> _crossCaps;
            private ValueBinding<bool> _randstrassen;
        private ValueBinding<float> _medianWidth;
        private ValueBinding<float> _crossBays;
        private ValueBinding<string> _angleMode;
        /** "alt" oder "zellen" - siehe LayoutSettings.Zellen. */
        private ValueBinding<string> _engine;

        /**
         * Spiegelt die gespeicherte Option, damit das Panel weiss, ob es vor
         * dem alten Rechenweg noch nachfragen soll. Der Wert lebt in den
         * Optionen und nicht nur hier - sonst waere "nicht mehr anzeigen"
         * nach dem naechsten Spielstart wieder vergessen.
         */
        private ValueBinding<bool> _altwegOhneWarnung;
        private ValueBinding<bool> _autoZufahrtModus;
        private ValueBinding<float> _rowAngle;
        private ValueBinding<float> _edgeSetbackDefault;
        private ValueBinding<float> _aisleWidthDefault;
        private ValueBinding<float> _crossWidthDefault;
        private ValueBinding<bool> _greenMedianDefault;
        private ValueBinding<bool> _crossCapsDefault;
            private ValueBinding<bool> _randstrassenDefault;
        private ValueBinding<float> _medianWidthDefault;
        private ValueBinding<float> _crossBaysDefault;
        private ValueBinding<string> _angleModeDefault;
        private ValueBinding<float> _rowAngleDefault;
        private ValueBinding<bool> _polygonClosed;
        private ValueBinding<bool> _undoAvailable;
        private ValueBinding<int> _altbestand;
        private ValueBinding<bool> _trennmodus;
        private ValueBinding<bool> _zoningModus;
        private ValueBinding<string> _zoningWinkelmodus;
        private ValueBinding<string> _zoningZug;
        private ValueBinding<int> _zoningAussentiefe;
        private ValueBinding<bool> _zoningLinienwahl;
        private ValueBinding<float> _zoningAusrichtwinkel;
        private ValueBinding<int> _zoningAuswahl;
        private ValueBinding<float> _zoningWinkel;
        private ValueBinding<int> _zoningFlaechen;
        private ValueBinding<int> _zoningParzellen;
        private ValueBinding<bool> _liveLog;
        private ValueBinding<string> _liveLogPfad;
        private ValueBinding<string> _ueberlappungsstand;
        private ValueBinding<bool> _ausrichtWahl;
        private ValueBinding<bool> _ausrichtAktiv;
        private double? _ausrichtwinkel;
        private ValueBinding<bool> _redoAvailable;
        private ValueBinding<bool> _entranceMode;
        private ValueBinding<int> _entranceCount;
        private ValueBinding<int> _entranceKind;
        private ValueBinding<int> _entranceMax;
        /*
         * Was zum Bauen noch fehlt - leer heisst "es darf gebaut werden".
         * Die Regel gehoert ins Werkzeug, nicht ins Panel: dort liegen die
         * Zufahrten samt Art, und dieselbe Regel entscheidet auch ueber Enter.
         * Zwei Fassungen derselben Bedingung waeren zwei Gelegenheiten, sie
         * auseinanderlaufen zu lassen.
         */
        private ValueBinding<string> _entranceMissing;

        /** Anteil des Bildschirms, 0 bis 1; siehe UserDefaults.PanelX. */
        private ValueBinding<float> _panelX;
        private ValueBinding<float> _panelY;
        private ValueBinding<string> _panelStil;
        private ValueBinding<int> _leistungRest;

        /** Wie lange der Knopf im Melde-Reiter misst. */
        private const int LeistungSekunden = 60;

        internal const string PanelStilHorizontal = "horizontal";
        internal const string PanelStilHochkant = "hochkant";

        private ValueBinding<int> _stalls;
        private ValueBinding<int> _perimeterStalls;
        private ValueBinding<string> _areaPerStall;
        private ValueBinding<int> _aisles;
        private ValueBinding<string> _rowAngleResult;
        private ValueBinding<string> _siteArea;
        private ValueBinding<string> _status;

        /**
         * Was der Rechenweg an der Eingabe NICHT konnte.
         *
         * Der Zellenweg legt solche Saetze in `ParkingLayout.Warnings` ab -
         * etwa dass er die automatische Winkelsuche nicht beherrscht und
         * stattdessen mit einem festen Winkel gerechnet hat. Bis zum
         * 2026-08-21 landeten sie ausschliesslich im Debug-Abzug, den der
         * Nutzer nie oeffnet. Eine Luecke, die niemand sieht, ist schlimmer
         * als eine Fehlermeldung: das Ergebnis ist still falsch.
         */
        private ValueBinding<string> _hinweis;
        private ValueBinding<int> _fensterHeim;
        private ValueBinding<string> _flaecheStrasse;
        private ValueBinding<string> _flaecheDeko;
        private ValueBinding<string> _flaecheZoning;
        private ValueBinding<string> _flaecheStrasseStd;
        private ValueBinding<string> _flaecheDekoStd;
        private ValueBinding<bool> _flaecheStrasseAnStd;
        private ValueBinding<bool> _flaecheDekoAnStd;
        private ValueBinding<bool> _vorflaecheAnStd;
        private ValueBinding<bool> _buchtsymboleStd;
        private ValueBinding<string> _flaechenListe;
        private ValueBinding<bool> _traegerLaeuft;
        private ValueBinding<string> _traegerstand;
        private ValueBinding<bool> _buchtsymbole;
        private ValueBinding<bool> _flaecheStrasseAn;
        private ValueBinding<bool> _flaecheDekoAn;
        private ValueBinding<bool> _vorflaecheAn;

        /** Laeuft der Belag bis an die Strasse? */
        internal bool VorflaecheAn => _vorflaecheAn?.value ?? true;

        /** Wird die Fahrflaeche ueberhaupt gesetzt? */
        internal bool FlaecheStrasseAn => _flaecheStrasseAn?.value ?? true;

        /** Wird die Zwischenflaeche ueberhaupt gesetzt? */
        internal bool FlaecheDekoAn => _flaecheDekoAn?.value ?? true;


        /** Worauf gefahren und geparkt wird. */
        internal string FlaecheStrasse => _flaecheStrasse?.value;

        /** Alles dazwischen. */
        internal string FlaecheDekoration => _flaecheDeko?.value;

        /** Leer heisst: es gilt die Dekoflaeche. */
        internal string FlaecheZoning => _flaecheZoning?.value;

        /** Sollen Rollstuhl- und Elektrosymbole gesetzt werden? */
        internal bool Buchtsymbole => _buchtsymbole?.value ?? true;

        internal double AktuelleMedianbreite => _medianWidth?.value ?? 2.5f;
        internal bool MittelgruenAn => _greenMedian?.value ?? true;
        internal double AktuelleQuerbuchten => _crossBays?.value ?? 9f;

        /**
         * Nicht jeder Wert aus LayoutSettings hat einen Regler. Beim Bearbeiten
         * bleibt deshalb der vollstaendige alte Satz die Basis; CurrentSettings
         * ueberschreibt darauf nur die tatsaechlich bedienbaren Werte.
         */
        private LayoutSettings _loadedBuildReceiptTemplate;

        /**
         * Den Stand des Traegertests ins Panel geben.
         *
         * Der Zustand steht in der Akte auf der Platte, nicht im Speicher -
         * der Test soll ja ein Speichern und Laden ueberdauern. Deshalb wird
         * er hier gelesen und nicht mitgefuehrt.
         */
        internal void PflegeTraegerstand()
        {
            var werkzeug = Tool();
            if (werkzeug == null || _traegerLaeuft == null) return;
            var laeuft = werkzeug.TraegertestLaeuft();
            if (_traegerLaeuft.value != laeuft) _traegerLaeuft.Update(laeuft);
            var stand = werkzeug.Traegerstand() ?? string.Empty;
            if (!string.Equals(_traegerstand.value, stand, StringComparison.Ordinal))
                _traegerstand.Update(stand);
        }

        /** Die Auswahl veroeffentlichen; das Spiel kennt sie erst nach dem Laden. */
        internal void SetzeFlaechenliste(string[] namen)
        {
            if (namen == null || namen.Length == 0 || _flaechenListe == null) return;
            var zusammen = string.Join("\n", namen);
            if (_flaechenListe.value != zusammen) _flaechenListe.Update(zusammen);
        }

        private static bool Neu(ValueBinding<string> bindung, string wert)
        {
            if (bindung == null || string.IsNullOrEmpty(wert)
                || bindung.value == wert) return false;
            bindung.Update(wert);
            return true;
        }
        private ValueBinding<string> _sondeStand;
        private ValueBinding<string> _sondeErgebnis;

        /** Fortschritt und Ergebnisse des Sondenlaufs ins Panel. */
        internal void SetzeSondenstand(string stand,
                                       System.Collections.Generic.List<string> zeilen)
        {
            _sondeStand?.Update(stand ?? string.Empty);
            _sondeErgebnis?.Update(zeilen == null || zeilen.Count == 0
                ? string.Empty : string.Join(Environment.NewLine, zeilen));
        }

        private ValueBinding<string> _sprache;

        /**
         * Der Reiter "Fehler melden".
         *
         * Hier war vorher nur die Tastenkombination Alt+M - und die merkt
         * sich niemand, der den Mod aus dem Workshop laedt. Ein Meldeweg,
         * den man nicht findet, wird nicht benutzt.
         */
        private ValueBinding<string> _tab;
        private ValueBinding<bool> _markerMode;
        private ValueBinding<int> _markerCount;
        private ValueBinding<string> _reportPath;

        /** Zaehlt hoch, sobald ein Regler bewegt wurde. */
        internal int Revision { get; private set; }
        internal bool PanelOpen => _panelOpen?.value ?? false;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _defaults = LoadDefaults();
            InitVegetation();

            /*
             * DER MELDEREITER BRAUCHT VIER ZUSTAENDE UND VIER AUSLOESER.
             *
             * Ansage des Nutzers am 2026-09-14 zur Testveroeffentlichung: der
             * bisherige Debug-Reiter heisst kuenftig "Dev-Debug" und ist aus,
             * und "Report a problem" wird der Reiter, in dem ein Nutzer seine
             * Meldung erzeugt - Absturzbericht, Vorschau-Bericht, Ordner auf.
             *
             * Der Absturzzustand kommt aus `ParkingLotAbsturzwache` und steht
             * schon beim Laden fest; die UI fragt ihn nur ab. Ohne Absturz
             * bleibt der Knopf weg - ein Knopf, der meistens nichts tut,
             * erzieht dazu, ihn zu ignorieren.
             */
            AddBinding(_entwicklerDebug = new ValueBinding<bool>(
                Group, "EntwicklerDebug",
                Mod.Optionen?.EntwicklerDebug ?? false));
            // Sofort nachziehen, wenn der Schalter faellt - siehe Setting.
            Setting.Geaendert += wert => _entwicklerDebug?.Update(wert);
            AddBinding(_absturzErkannt = new ValueBinding<bool>(
                Group, "AbsturzErkannt",
                ParkingLotAbsturzwache.LetzteSitzungAbgestuerzt));
            AddBinding(_absturzBefund = new ValueBinding<string>(
                Group, "AbsturzBefund", ParkingLotAbsturzwache.Befund));
            AddBinding(_meldungPfad = new ValueBinding<string>(
                Group, "MeldungPfad", string.Empty));
            AddBinding(_baubefund = new ValueBinding<string>(
                Group, "Baubefund", string.Empty));
            AddBinding(_baukurzinfo = new ValueBinding<string>(
                Group, "Baukurzinfo", string.Empty));

            AddBinding(new TriggerBinding(Group, "MeldungAbsturz",
                () => SchnuereMeldung(
                    ParkingLotMeldepaket.Anlass.Absturz)));
            AddBinding(new TriggerBinding(Group, "MeldungVorschau",
                () => SchnuereMeldung(
                    ParkingLotMeldepaket.Anlass.Vorschau)));
            AddBinding(new TriggerBinding(Group, "MeldungBau",
                () => SchnuereMeldung(ParkingLotMeldepaket.Anlass.Bau)));
            AddBinding(new TriggerBinding(Group, "MeldungOrdner",
                OeffneLogordner));

            AddBinding(_panelOpen = new ValueBinding<bool>(Group, "PanelOpen", false));
            AddBinding(_toolActive = new ValueBinding<bool>(Group, "ToolActive", false));
            AddBinding(_edgeSetback =
                new ValueBinding<float>(Group, "EdgeSetback", _defaults.EdgeSetback));
            AddBinding(_aisleWidth =
                new ValueBinding<float>(Group, "AisleWidth", _defaults.AisleWidth));
            AddBinding(_crossWidth =
                new ValueBinding<float>(Group, "CrossWidth", _defaults.CrossWidth));
            AddBinding(_greenMedian =
                new ValueBinding<bool>(Group, "GreenMedian", _defaults.GreenMedian));
            AddBinding(_crossCaps =
                new ValueBinding<bool>(Group, "CrossCaps", _defaults.CrossCaps));
            AddBinding(_randstrassen =
                new ValueBinding<bool>(Group, "Randstrassen", _defaults.Randstrassen));
            AddBinding(_medianWidth =
                new ValueBinding<float>(Group, "MedianWidth", _defaults.MedianWidth));
            AddBinding(_crossBays =
                new ValueBinding<float>(Group, "CrossBays", _defaults.CrossBays));
            AddBinding(_angleMode =
                new ValueBinding<string>(Group, "AngleMode", _defaults.AngleMode));
            AddBinding(_rowAngle =
                new ValueBinding<float>(Group, "RowAngle", _defaults.RowAngle));
            /**
             * STANDARD IST SEIT DEM 2026-08-22 DER NEUE RECHENWEG.
             *
             * Er baut mehr Buchten in einem Bruchteil der Zeit und schafft
             * Formen, an denen der alte am 7-Sekunden-Budget scheitert
             * (L schraeg: 439 in 240 ms gegen Abbruch). Der alte bleibt als
             * Rueckfall im Panel erreichbar.
             */
            AddBinding(_engine = new ValueBinding<string>(Group, "Engine", "zellen"));
            AddBinding(_altwegOhneWarnung = new ValueBinding<bool>(Group,
                "AltEngineOhneWarnung",
                Mod.Optionen?.AltRechenwegOhneWarnung ?? false));

            AddBinding(_autoZufahrtModus = new ValueBinding<bool>(Group,
                "AutoEntryMode",
                Mod.Optionen?.AutomatischZufahrtModus ?? true));

            AddBinding(_edgeSetbackDefault = new ValueBinding<float>(Group,
                "EdgeSetbackDefault", _defaults.EdgeSetback));
            AddBinding(_aisleWidthDefault = new ValueBinding<float>(Group,
                "AisleWidthDefault", _defaults.AisleWidth));
            AddBinding(_crossWidthDefault = new ValueBinding<float>(Group,
                "CrossWidthDefault", _defaults.CrossWidth));
            AddBinding(_greenMedianDefault = new ValueBinding<bool>(Group,
                "GreenMedianDefault", _defaults.GreenMedian));
            AddBinding(_crossCapsDefault = new ValueBinding<bool>(Group,
                "CrossCapsDefault", _defaults.CrossCaps));
            AddBinding(_randstrassenDefault = new ValueBinding<bool>(Group,
                "RandstrassenDefault", _defaults.Randstrassen));
            AddBinding(_medianWidthDefault = new ValueBinding<float>(Group,
                "MedianWidthDefault", _defaults.MedianWidth));
            AddBinding(_crossBaysDefault = new ValueBinding<float>(Group,
                "CrossBaysDefault", _defaults.CrossBays));
            AddBinding(_angleModeDefault = new ValueBinding<string>(Group,
                "AngleModeDefault", _defaults.AngleMode));
            AddBinding(_rowAngleDefault = new ValueBinding<float>(Group,
                "RowAngleDefault", _defaults.RowAngle));
            AddBinding(_polygonClosed =
                new ValueBinding<bool>(Group, "PolygonClosed", false));
            AddBinding(_undoAvailable =
                new ValueBinding<bool>(Group, "UndoAvailable", false));
            AddBinding(_altbestand =
                new ValueBinding<int>(Group, "Altbestand", 0));
            AddBinding(new TriggerBinding(Group, "AltbestandLoeschen",
                () => World.GetOrCreateSystemManaged<
                    ParkingLotAltbestandSystem>().LoescheAltbestand()));
            AddBinding(new TriggerBinding(Group, "AltbestandBehalten",
                () => World.GetOrCreateSystemManaged<
                    ParkingLotAltbestandSystem>().BehalteAltbestand()));
            AddBinding(_ausrichtWahl =
                new ValueBinding<bool>(Group, "AusrichtWahl", false));
            AddBinding(_ausrichtAktiv =
                new ValueBinding<bool>(Group, "AusrichtAktiv", false));
            AddBinding(new TriggerBinding(Group, "AusrichtWaehlen",
                () => Tool()?.BeginAusrichtWahl()));
            AddBinding(new TriggerBinding(Group, "AusrichtZuruecksetzen",
                () => Tool()?.ResetAusrichtung()));
            AddBinding(new TriggerBinding(Group, "AusrichtBestaetigen",
                () => Tool()?.BestaetigeAusrichtWahl()));
            AddBinding(_trennmodus =
                new ValueBinding<bool>(Group, "Trennmodus", false));
            AddBinding(_zoningSeitenModus =
                new ValueBinding<bool>(Group, "ZoningSeitenModus", false));
            // Der Schalter greift nur an GEBAUTEN Strassen. Solange keine
            // steht, ist der Knopf blass - sonst sucht der Nutzer die
            // Wirkung in der Vorschau, wo es sie nicht geben kann.
            AddBinding(_zoningSeitenMoeglich =
                new ValueBinding<bool>(Group, "ZoningSeitenMoeglich", false));
            AddBinding(new TriggerBinding<bool>(Group, "SetZoningSeitenModus",
                an => Tool()?.SetzeZoningSeitenModus(an)));
            AddBinding(_zoningModus =
                new ValueBinding<bool>(Group, "ZoningModus", false));
            AddBinding(new TriggerBinding<bool>(Group, "SetZoningModus",
                an => Tool()?.SetzeZoningModus(an)));
            AddBinding(_zoningWinkelmodus = new ValueBinding<string>(
                Group, "ZoningWinkelmodus", "edge"));
            AddBinding(new TriggerBinding<string>(Group, "SetZoningWinkelmodus",
                modus =>
                {
                    Tool()?.SetzeZoningWinkelmodus(modus);
                    _zoningWinkelmodus.Update(modus);
                }));
            AddBinding(_zoningWinkel = new ValueBinding<float>(
                Group, "ZoningWinkel", 0f));
            AddBinding(new TriggerBinding<float>(Group, "SetZoningWinkel",
                grad =>
                {
                    Tool()?.SetzeZoningReglerwinkel(grad);
                    _zoningWinkel.Update(grad);
                }));
            AddBinding(_zoningLinienwahl = new ValueBinding<bool>(
                Group, "ZoningLinienwahl", false));
            AddBinding(new TriggerBinding<bool>(Group, "SetZoningLinienwahl",
                an => Tool()?.SetzeZoningLinienwahl(an)));
            AddBinding(_zoningAusrichtwinkel = new ValueBinding<float>(
                Group, "ZoningAusrichtwinkel", float.NaN));
            AddBinding(_zoningAuswahl = new ValueBinding<int>(
                Group, "ZoningAuswahl", -1));
            AddBinding(_zoningAussentiefe = new ValueBinding<int>(
                Group, "ZoningAussentiefe", 2));
            AddBinding(new TriggerBinding<int>(Group, "SetZoningAussentiefe",
                wert => Tool()?.SetzeZoningAussentiefe(wert)));
            AddBinding(_zoningZug =
                new ValueBinding<string>(Group, "ZoningZug", string.Empty));
            AddBinding(_zoningFlaechen =
                new ValueBinding<int>(Group, "ZoningFlaechen", 0));
            AddBinding(_zoningParzellen =
                new ValueBinding<int>(Group, "ZoningParzellen", 0));
            AddBinding(new TriggerBinding(Group, "TrennungFertig",
                () => Tool()?.BeendeTrennmodus()));
            AddBinding(_liveLog =
                new ValueBinding<bool>(Group, "LiveLog", false));
            AddBinding(new TriggerBinding(Group, "LiveLogUmschalten", () =>
            {
                ParkingLotLiveLog.Schalte(!ParkingLotLiveLog.Aktiv);
                _liveLog.Update(ParkingLotLiveLog.Aktiv);
                _liveLogPfad.Update(ParkingLotLiveLog.Aktiv
                    ? ParkingLotLiveLog.Pfad : string.Empty);
            }));
            AddBinding(_liveLogPfad =
                new ValueBinding<string>(Group, "LiveLogPfad", string.Empty));
            AddBinding(_ueberlappungsstand = new ValueBinding<string>(
                Group, "Ueberlappungsstand", string.Empty));
            AddBinding(new TriggerBinding(Group, "UeberlappungMessen", () =>
            {
                var selectedInfo = World.GetOrCreateSystemManaged<
                    Game.UI.InGame.SelectedInfoUISystem>();
                Tool()?.FordereUeberlappungsdiagnose(selectedInfo?.selectedEntity
                    ?? Unity.Entities.Entity.Null);
            }));
            AddBinding(new TriggerBinding(Group, "PrefabsVergleichen",
                () => Tool()?.VergleicheStrassenprefabs()));
            AddBinding(_redoAvailable =
                new ValueBinding<bool>(Group, "RedoAvailable", false));
            AddBinding(_entranceMode =
                new ValueBinding<bool>(Group, "EntranceMode", false));
            AddBinding(_entranceKind =
                new ValueBinding<int>(Group, "EntranceKind", 0));
            /*
             * Die Obergrenze kommt aus dem Werkzeug statt als Zahl im
             * Panel-Text. Sonst steht sie an zwei Orten und driftet beim
             * naechsten Mal auseinander - genau das ist am 2026-08-27 mit der
             * alten Zehn passiert, die in zwei Texten und einer Konstante
             * stand.
             */
            AddBinding(_entranceMax = new ValueBinding<int>(Group,
                "EntranceMax", ParkingLotToolSystem.MaxEntranceCount));
            AddBinding(_entranceMissing =
                new ValueBinding<string>(Group, "EntranceMissing", "zugang"));
            AddBinding(_entranceCount =
                new ValueBinding<int>(Group, "EntranceCount", 0));
            AddBinding(_panelX = new ValueBinding<float>(Group, "PanelX",
                StilX() ?? 0f));
            AddBinding(_panelY = new ValueBinding<float>(Group, "PanelY",
                StilY() ?? 0f));
            /*
             * Die Fangarten als EIGENE Bindung. `tool.availableSnapMask`
             * geht nicht mehr: die meldet seit dem 2026-09-17 absichtlich
             * 0, damit CS2 kein zweites Fangfenster baut.
             */
            /*
             * Sekunden bis zum Ende der Leistungsmessung; 0 heisst: laeuft
             * keine. Der Knopf im Melde-Reiter zeigt daran, dass es lauft
             * und wie lange noch - ohne Rueckmeldung druecken Leute ein
             * zweites Mal.
             */
            AddBinding(_leistungRest = new ValueBinding<int>(
                Group, "LeistungRest", 0));
            AddBinding(new TriggerBinding(Group, "StarteLeistungstest",
                StarteLeistungstest));
            AddBinding(new ValueBinding<uint>(Group, "Fangarten",
                (uint)ParkingLotToolSystem.Fangarten));
            AddBinding(_panelStil = new ValueBinding<string>(Group, "PanelStil",
                Mod.Optionen?.FensterstilKuerzel() ?? PanelStilHorizontal));
            AddBinding(new TriggerBinding<string>(Group, "SetPanelStil",
                SetPanelStil));
            // Derselbe Weg wie der Knopf in den Modeinstellungen - nur
            // erreichbar, ohne das Spiel zu pausieren.
            AddBinding(new TriggerBinding(Group, "FensterHeim",
                ResetPanelPosition));

            AddBinding(_stalls = new ValueBinding<int>(Group, "Stalls", 0));
            AddBinding(_perimeterStalls =
                new ValueBinding<int>(Group, "PerimeterStalls", 0));
            AddBinding(_areaPerStall =
                new ValueBinding<string>(Group, "AreaPerStall", "-"));
            AddBinding(_aisles = new ValueBinding<int>(Group, "Aisles", 0));
            AddBinding(_rowAngleResult =
                new ValueBinding<string>(Group, "RowAngleResult", "-"));
            AddBinding(_siteArea = new ValueBinding<string>(Group, "SiteArea", "-"));
            /**
             * STARTWERTE LAUFEN NICHT DURCH `SetStatus`.
             *
             * "Bereit." stand hier als reines Literal und war deshalb das
             * einzige Wort, das ein englischer Nutzer beim Oeffnen des
             * Werkzeugs auf Deutsch sah - gemeldet am 2026-08-22. Ein
             * Startwert wird angelegt, nicht gesetzt; die Uebersetzung muss
             * also schon hier stehen.
             */
            AddBinding(_status = new ValueBinding<string>(
                Group, "Status", T("Bereit.", "Ready.")));
            AddBinding(_hinweis = new ValueBinding<string>(Group, "Hinweis", ""));
            AddBinding(_fensterHeim = new ValueBinding<int>(Group, "PanelHome", 0));
            /**
             * Was das Panel gemessen hat, ins Log.
             *
             * Zweimal hat die Heimposition nicht gestimmt, und zweimal habe
             * ich die Ursache geraten. Die Oberflaeche kennt die Zahlen -
             * also soll sie sie sagen, statt dass ich sie mir ausdenke.
             */
            AddBinding(new TriggerBinding<string>(Group, "PanelDiagnose",
                text => Mod.log.Info("PLT-Fensterposition: " + text)));
            AddBinding(_flaecheStrasse = new ValueBinding<string>(
                Group, "SurfaceRoad",
                /**
                 * DER GESPEICHERTE NAME, NICHT DER WERKSNAME.
                 *
                 * Hier stand fest "Pavement Surface 01". Die gespeicherte Wahl
                 * wurde zwar in `_defaults` geladen, aber die laufende Bindung
                 * startete trotzdem mit dem Werkswert - wer seine Flaeche als
                 * Standard gespeichert hatte, fand nach dem Neustart wieder
                 * die Werksflaeche vor. Jede andere Einstellung im Panel
                 * benutzt `_defaults` an dieser Stelle; diese beiden waren die
                 * Ausnahme.
                 */
                string.IsNullOrEmpty(_defaults.SurfaceRoad)
                    ? "Pavement Surface 01" : _defaults.SurfaceRoad));
            AddBinding(_flaecheDeko = new ValueBinding<string>(
                Group, "SurfaceDecoration",
                string.IsNullOrEmpty(_defaults.SurfaceDecoration)
                    ? "Grass Surface 01" : _defaults.SurfaceDecoration));
            /*
             * DIE DRITTE FLAECHE: der Boden unter den Parzellen.
             *
             * Leer als Vorgabe, und das ist Absicht - dann faellt sie auf
             * die Dekoflaeche zurueck. Ein alter Bauzettel bringt sie gar
             * nicht mit, und ein Parkplatz von gestern soll deshalb nicht
             * anders aussehen als gestern.
             */
            AddBinding(_flaecheZoning = new ValueBinding<string>(
                Group, "SurfaceZoning", string.Empty));
            AddBinding(new TriggerBinding<string>(Group, "SetSurfaceZoning",
                wert => ChangeDraftSetting("Baulandfläche geändert", () =>
                    Neu(_flaecheZoning, wert))));
            AddBinding(_flaecheStrasseStd = new ValueBinding<string>(
                Group, "SurfaceRoadDefault",
                string.IsNullOrEmpty(_defaults.SurfaceRoad)
                    ? "Pavement Surface 01" : _defaults.SurfaceRoad));
            AddBinding(_flaecheDekoStd = new ValueBinding<string>(
                Group, "SurfaceDecorationDefault",
                string.IsNullOrEmpty(_defaults.SurfaceDecoration)
                    ? "Grass Surface 01" : _defaults.SurfaceDecoration));
            /**
             * DIE LISTE ALS EIN STRING, GETRENNT DURCH ZEILENUMBRUCH.
             *
             * Erst stand hier `ValueBinding<string[]>`. CS2 braucht fuer
             * zusammengesetzte Typen einen eigenen Schreiber; ohne ihn wirft
             * der Konstruktor. Das passiert in `OnCreate`, also mitten in
             * `OnLoad` - der Mod wurde 65 ms nach dem Start wieder abgeraeumt,
             * und die halb registrierten Systeme liessen danach den ganzen
             * Preload des Spiels auflaufen ("system state is not initialized
             * or has already been destroyed"). Im Modlog stand nur "geladen"
             * und direkt darauf "OnDispose".
             *
             * Ein einzelner String braucht keinen Schreiber. Denselben Weg
             * geht die Hinweiszeile seit heute Mittag, und der traegt.
             */
            AddBinding(_flaechenListe = new ValueBinding<string>(
                Group, "SurfaceList", string.Empty));
            AddBinding(_buchtsymbole = new ValueBinding<bool>(
                Group, "BayIcons", _defaults.BayIcons ?? true));
            AddBinding(_buchtsymboleStd = new ValueBinding<bool>(
                Group, "BayIconsDefault", _defaults.BayIcons ?? true));
            /**
             * OB EINE FLAECHE UEBERHAUPT GESETZT WIRD.
             *
             * Wunsch des Nutzers am 2026-08-22: *"das dient dem, dass einige
             * User gerne den vorhandenen Boden nutzen moechten, da die lieber
             * den Terrain-Brush nutzen."*
             *
             * Ausdrueckliche Bedingung: an der RECHNUNG aendert sich nichts.
             * Buchten, Wege und Zufahrten bleiben identisch - es wird nur
             * nicht gesetzt. Deshalb sitzt der Schalter am Setzen und nicht
             * im Generator.
             */
            AddBinding(_flaecheStrasseAn = new ValueBinding<bool>(
                Group, "SurfaceRoadOn", _defaults.SurfaceRoadOn ?? true));
            AddBinding(_flaecheDekoAn = new ValueBinding<bool>(
                Group, "SurfaceDecorationOn", _defaults.SurfaceDecorationOn ?? true));
            AddBinding(_flaecheStrasseAnStd = new ValueBinding<bool>(
                Group, "SurfaceRoadOnDefault", _defaults.SurfaceRoadOn ?? true));
            AddBinding(_flaecheDekoAnStd = new ValueBinding<bool>(
                Group, "SurfaceDecorationOnDefault",
                _defaults.SurfaceDecorationOn ?? true));
            AddBinding(_vorflaecheAn = new ValueBinding<bool>(
                Group, "SurfaceApronOn", _defaults.SurfaceApronOn ?? true));
            AddBinding(_vorflaecheAnStd = new ValueBinding<bool>(
                Group, "SurfaceApronOnDefault", _defaults.SurfaceApronOn ?? true));
            AddBinding(new TriggerBinding<bool>(Group, "SetSurfaceApronOn",
                wert => ChangeDraftSetting("Einstellung Vorfläche geändert", () =>
                    UpdateValue(_vorflaecheAn, wert))));
            AddBinding(new TriggerBinding<bool>(Group, "SetSurfaceRoadOn",
                wert => ChangeDraftSetting("Einstellung Fahrfläche geändert", () =>
                    UpdateValue(_flaecheStrasseAn, wert))));
            AddBinding(new TriggerBinding<bool>(Group, "SetSurfaceDecorationOn",
                wert => ChangeDraftSetting("Einstellung Zwischenfläche geändert", () =>
                    UpdateValue(_flaecheDekoAn, wert))));
            AddBinding(new TriggerBinding<string>(Group, "SetSurfaceRoad",
                wert => ChangeDraftSetting("Fahrfläche geändert", () =>
                    Neu(_flaecheStrasse, wert))));
            AddBinding(new TriggerBinding<string>(Group, "SetSurfaceDecoration",
                wert => ChangeDraftSetting("Zwischenfläche geändert", () =>
                    Neu(_flaecheDeko, wert))));
            AddBinding(new TriggerBinding<bool>(Group, "SetBayIcons",
                wert => ChangeDraftSetting("Buchtmarkierungen geändert", () =>
                    UpdateValue(_buchtsymbole, wert))));
            // Die Oberflaeche haengt an dieser einen Bindung. Standard "en" -
            // auch wenn die Einstellungen noch nicht geladen sind, soll das
            // Panel englisch starten und nicht kurz deutsch aufblitzen.
            AddBinding(_sprache = new ValueBinding<string>(
                Group, "Sprache", Mod.Optionen?.SprachKuerzel() ?? "en"));
            AddBinding(_tab = new ValueBinding<string>(Group, "Tab", "layout"));
            /**
             * DER SONDENLAUF misst, was CS2 von Flaechen annimmt.
             *
             * Der Fortschritt ist eine eigene Bindung, die Ergebnisse sind
             * zeilenweise zusammengefasst - ein `string[]` hat am 2026-08-22
             * das ganze Spiel beim Laden mitgerissen, deshalb hier wieder ein
             * einzelner String mit Zeilenumbruechen.
             */
            AddBinding(_sondeStand = new ValueBinding<string>(
                Group, "ProbeState", ""));
            AddBinding(_sondeErgebnis = new ValueBinding<string>(
                Group, "ProbeResults", ""));
            AddBinding(new TriggerBinding(Group, "StartProbe",
                () => Tool()?.StarteSondenlauf()));
            AddBinding(new TriggerBinding(Group, "CancelProbe",
                () => Tool()?.BrichSondenlaufAb()));
            AddBinding(new TriggerBinding<string>(Group, "BuildZoningProbe",
                road => Tool()?.StarteZoningsonde(road)));
            AddBinding(new TriggerBinding(Group, "MeasureZoningProbe",
                () => Tool()?.MissZoningsonde()));
            AddBinding(new TriggerBinding(Group, "CleanupZoningProbe",
                () => Tool()?.RaeumeZoningsondeAuf()));
            AddBinding(_markerMode =
                new ValueBinding<bool>(Group, "MarkerMode", false));
            AddBinding(_markerCount = new ValueBinding<int>(Group, "MarkerCount", 0));
            AddBinding(_reportPath = new ValueBinding<string>(Group, "ReportPath", ""));

            AddBinding(new TriggerBinding(Group, "TogglePanel",
                () => SetPanelOpen(!_panelOpen.value)));
            AddBinding(new TriggerBinding<bool>(Group, "SetPanelOpen", SetPanelOpen));
            AddBinding(new TriggerBinding(Group, "ToggleTool", ToggleTool));
            AddBinding(new TriggerBinding(Group, "PlaceEntrance",
                () => Tool()?.PlaceEntranceFromPanel()));
            AddBinding(new TriggerBinding<float, float>(Group, "SetPanelPosition",
                SetPanelPosition));
            AddBinding(new TriggerBinding<bool>(Group, "SetEntranceMode",
                value => Tool()?.SetEntranceModeFromPanel(value)));
            AddBinding(new TriggerBinding<int>(Group, "SetEntranceKind",
                value => Tool()?.SetZufahrtsartFromPanel(value)));
            AddBinding(new TriggerBinding(Group, "BuildNow",
                () => Tool()?.RequestBuildFromPanel()));
            AddBinding(new TriggerBinding(Group, "Undo",
                () => Tool()?.UndoFromPanel()));
            AddBinding(new TriggerBinding(Group, "Redo",
                () => Tool()?.RedoFromPanel()));
            AddBinding(new TriggerBinding(Group, "EditSelectedParkingLot",
                EditSelectedParkingLot));
            AddBinding(new TriggerBinding(Group, "MeldeGewaehltenParkplatz",
                MeldeGewaehltenParkplatz));
            AddBinding(_meldeLotWahl = new ValueBinding<bool>(
                Group, "MeldeLotWahl", false));
            AddBinding(new TriggerBinding<bool>(Group, "SchalteMeldeLotWahl",
                an => Tool()?.SchalteMeldeLotWahl(an)));

            Bind(_edgeSetback, "SetEdgeSetback");
            Bind(_aisleWidth, "SetAisleWidth");
            Bind(_crossWidth, "SetCrossWidth");
            Bind(_medianWidth, "SetMedianWidth");
            Bind(_crossBays, "SetCrossBays");
            Bind(_rowAngle, "SetRowAngle");
            AddBinding(new TriggerBinding<bool>(Group, "SetGreenMedian",
                value => ChangeDraftSetting("Mittelgrün geändert", () =>
                    UpdateValue(_greenMedian, value))));
            AddBinding(new TriggerBinding<bool>(Group, "SetCrossCaps",
                value => ChangeDraftSetting("Kappen geändert", () =>
                    UpdateValue(_crossCaps, value))));
            AddBinding(new TriggerBinding<bool>(Group, "SetRandstrassen",
                value => ChangeDraftSetting("Randstraßen geändert", () =>
                    UpdateValue(_randstrassen, value))));
            AddBinding(new TriggerBinding<string>(Group, "SetAngleMode",
                value => ChangeDraftSetting("Winkelmodus geändert", () =>
                {
                    var geaendert = UpdateValue(_angleMode, value);
                    /*
                     * WER EINEN WINKELMODUS WAEHLT, VERLAESST DAS AUSRICHTEN.
                     *
                     * Befund des Nutzers am 2026-09-09: *"Wenn ich 'align to
                     * polygon line' anklicke, muss ich erst auf 'done'
                     * klicken, um ueberhaupt wieder edge oder einen anderen
                     * Modus zu aktivieren."*
                     *
                     * Hier wurde nur die fertige LINIE verworfen, und auch
                     * das nur bei "fixed". Eine laufende AUSWAHL blieb
                     * stehen und frass weiter jeden Klick - das Panel zeigte
                     * derweil gar keinen Modus mehr an (`value={ausrichtWahl
                     * ? "" : angleMode}`). Ein Gegenmodus muss den laufenden
                     * beenden, nicht auf einen Knopf warten.
                     */
                    var tool = Tool();
                    if (tool != null && tool.AusrichtWahlAktiv)
                    {
                        tool.AbortAusrichtWahl("Winkelmodus gewählt");
                        geaendert = true;
                    }
                    // "Fest" und "Ausrichten" schliessen einander aus: wer
                    // "Fest" waehlt, verwirft auch die fertige Bezugslinie.
                    if (value == "fixed" && _ausrichtwinkel.HasValue)
                    {
                        tool?.ResetAusrichtung();
                        geaendert = true;
                    }
                    return geaendert;
                })));
            AddBinding(new TriggerBinding<string>(Group, "SetEngine",
                value => ChangeDraftSetting("Rechenweg geändert", () =>
                    UpdateValue(_engine, value))));
            /**
             * Der Haken aus dem Nachfrage-Dialog. Er wandert in die Optionen,
             * damit er das Spiel ueberlebt und im ESC-Menue umkehrbar bleibt.
             */
            AddBinding(new TriggerBinding<bool>(Group, "SetAltEngineOhneWarnung",
                value =>
                {
                    if (Mod.Optionen != null)
                    {
                        Mod.Optionen.AltRechenwegOhneWarnung = value;
                        Mod.Optionen.ApplyAndSave();
                    }
                    _altwegOhneWarnung.Update(value);
                }));
            /*
             * Der Knopf im Panel schreibt dieselbe Einstellung wie das
             * Optionsmenue und speichert sie. Der Abgleich in die andere
             * Richtung steht in `PflegeSchalter`.
             */
            AddBinding(new TriggerBinding<bool>(Group, "SetAutoEntryMode",
                value =>
                {
                    if (Mod.Optionen != null)
                    {
                        Mod.Optionen.AutomatischZufahrtModus = value;
                        Mod.Optionen.ApplyAndSave();
                    }
                    _autoZufahrtModus.Update(value);
                }));
            AddBinding(new TriggerBinding(Group, "ResetAll", ResetAll));
            AddBinding(new TriggerBinding<string>(Group, "ResetOne", ResetOne));
            AddBinding(new TriggerBinding<string>(Group, "SetAsDefault", SetAsDefault));
            AddBinding(new TriggerBinding(Group, "DiscardDefaults", DiscardDefaults));

            AddBinding(new TriggerBinding<string>(Group, "SetTab", SetTab));
            AddBinding(new TriggerBinding<bool>(Group, "SetMarkerMode",
                value => Tool()?.SetMarkerMode(value)));
            AddBinding(new TriggerBinding(Group, "RemoveLastMarker",
                () => Tool()?.RemoveLastMarker()));
            AddBinding(new TriggerBinding(Group, "ClearMarkers",
                () => Tool()?.ClearMarkers()));
            AddBinding(new TriggerBinding(Group, "WriteReport",
                () => Tool()?.WriteMarkerReportNow()));
            /*
             * Der Prefab-Sezierer. Er baut nichts und aendert nichts - er
             * schreibt nur einen Abzug. Deshalb darf er jederzeit laufen,
             * auch ohne gezeichnetes Polygon.
             */
            AddBinding(new TriggerBinding(Group, "DissectPrefabs",
                () => Tool()?.SeziereParkanlagen()));
            /*
             * Der Traegertest. Er haengt ein paar Aufkleber eines bereits
             * gebauten Parkplatzes an eine nackte Traeger-Entity um und
             * schaut, ob sie liegenbleiben. Ein Versuch, kein Bauschritt -
             * deshalb im Debug-Reiter und mit eigener Warnung im Panel.
             */
            AddBinding(new TriggerBinding(Group, "ToggleCarrierTest",
                () => { Tool()?.SchalteTraegertest(); PflegeTraegerstand(); }));
            AddBinding(_traegerLaeuft = new ValueBinding<bool>(
                Group, "CarrierTestRunning", false));
            AddBinding(_traegerstand = new ValueBinding<string>(
                Group, "CarrierTestState", string.Empty));
        }

        private static string SettingsPath()
            => Path.Combine(Application.persistentDataPath, "ModsSettings",
                "ParkingLotTool", "einstellungen.json");

        /** Die Werkswerte entsprechen dem bisherigen Panel-Startzustand. */
        private static UserDefaults FactoryDefaults()
        {
            var cs2 = LayoutSettings.Cs2;
            return new UserDefaults
            {
                Version = SettingsVersion,
                EdgeSetback = (float)cs2.Es,
                AisleWidth = (float)cs2.Ai,
                CrossWidth = (float)cs2.Cw,
                MedianWidth = (float)cs2.Md,
                SurfaceRoad = "Pavement Surface 01",
                SurfaceDecoration = "Grass Surface 01",
                // Werkszustand: es wird gesetzt und markiert.
                SurfaceRoadOn = true,
                SurfaceApronOn = true,
                SurfaceDecorationOn = true,
                BayIcons = true,
                CrossBays = (float)Math.Round(
                    (cs2.Cr - 2 * Buchtbreite) / Buchtbreite,
                    MidpointRounding.AwayFromZero),
                RowAngle = (float)cs2.Angle,
                GreenMedian = cs2.Md > 0,
                CrossCaps = cs2.Qk,
            Randstrassen = cs2.Randstrassen,
                // "edge" war schon vor der Persistenz der UI-Startmodus.
                AngleMode = "edge",
                // Genau da, wo das Fenster bisher fest sass: left 10rem,
                // top 60rem - bei 1080p sind rem und Pixel dasselbe.
                /*
                 * Werksplaetze, beide knapp ueber der Werkzeugleiste (die
                 * ist 50 rem hoch, CS2 haelt 12 rem Abstand):
                 *   Leiste    waagerecht mittig, 1680 breit, ~320 hoch
                 *   Hochkant  links im Slot von CS2s Werkzeugfenster
                 * Genau nachgerechnet wird beim Zuruecksetzen - nur das
                 * Panel kennt Aufloesung und tatsaechliche Breite.
                 */
                PanelX = 120f / 1920f,
                // Abstand der UNTERKANTE vom unteren Bildrand, gemessen:
                // CS2s Hauptleiste beginnt bei 980 px, unser Fenster soll
                // bei 974 enden - also 106 von 1080.
                PanelY = 106f / 1080f,
                PanelXHochkant = 12f / 1920f,
                PanelYHochkant = 68f / 1080f,
            };
        }

        /**
         * Eine kaputte Einstellungsdatei darf niemals den Spielstart kosten.
         * Auch fehlende Pflichtfelder und Werte ausserhalb derselben Skalen
         * wie im Panel gelten deshalb als kaputt und fallen komplett zurueck.
         */
        private static UserDefaults LoadDefaults()
        {
            var factory = FactoryDefaults();
            var path = SettingsPath();
            try
            {
                if (!File.Exists(path))
                {
                    Mod.log.Info("PLT-Einstellungen: Datei fehlt; CS2-Werte aktiv: "
                        + path);
                    return factory;
                }

                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    Mod.log.Warn("PLT-Einstellungen: Datei ist leer; CS2-Werte aktiv: "
                        + path);
                    return factory;
                }

                var loaded = JsonConvert.DeserializeObject<UserDefaults>(json);
                MigriereAufBuchten(loaded);
                if (!ValidDefaults(loaded, out var reason))
                {
                    Mod.log.Warn("PLT-Einstellungen: Inhalt ungueltig (" + reason
                        + "); CS2-Werte aktiv: " + path);
                    return factory;
                }

                FuelleFehlendeAuf(loaded, factory);

                Mod.log.Info("PLT-Einstellungen geladen: " + path);
                return loaded;
            }
            catch (Exception exception)
            {
                Mod.log.Warn("PLT-Einstellungen nicht lesbar; CS2-Werte aktiv: "
                    + path + " (" + exception.Message + ")");
                return factory;
            }
        }

        /**
         * JEDES FREIWILLIGE FELD BEKOMMT EINEN WERT - hier und nirgends sonst.
         *
         * Die freiwilligen Felder gibt es, damit eine aeltere Datei
         * weitergilt, statt alle uebrigen Standards mitzureissen. Der Preis
         * dafuer ist, dass sie nach dem Einlesen leer sein koennen, und genau
         * das ist am 2026-08-25 aufgeflogen:
         *
         * In der Datei des Nutzers vom 2026-08-22 standen `"SurfaceRoad":
         * null` und `"SurfaceDecoration": null`. Die Pruefung liess das durch
         * - sie sieht sich diese zwei Felder gar nicht an. Danach hat der
         * Zuruecksetzen-Knopf der beiden Flaechen NICHTS getan: `Neu()` steigt
         * bei einem leeren Wert aus, ohne etwas zu melden. Der Knopf sah dabei
         * benutzbar aus, weil `PublishDefaults` den leeren Standard ebenfalls
         * uebersprang und das Panel deshalb seinen eigenen Startwert fuer den
         * gespeicherten Standard hielt.
         *
         * Aufgefuellt wird deshalb an EINER Stelle, fuer ALLE freiwilligen
         * Felder - nicht dort, wo der Wert spaeter gebraucht wird. Ein `??`
         * an der Benutzungsstelle sieht aus wie eine Absicherung, ist aber
         * nur eine Verzweigung mehr, die man beim naechsten Feld vergisst.
         * Nach dieser Zeile gilt: `_defaults` ist vollstaendig.
         */
        private static void FuelleFehlendeAuf(UserDefaults geladen,
            UserDefaults werk)
        {
            geladen.PanelX = geladen.PanelX ?? werk.PanelX;
            geladen.PanelY = geladen.PanelY ?? werk.PanelY;
            geladen.PanelXHochkant = geladen.PanelXHochkant ?? werk.PanelXHochkant;
            geladen.PanelYHochkant = geladen.PanelYHochkant ?? werk.PanelYHochkant;
            geladen.SurfaceRoadOn = geladen.SurfaceRoadOn ?? werk.SurfaceRoadOn;
            geladen.SurfaceApronOn = geladen.SurfaceApronOn ?? werk.SurfaceApronOn;
            geladen.SurfaceDecorationOn =
                geladen.SurfaceDecorationOn ?? werk.SurfaceDecorationOn;
            geladen.BayIcons = geladen.BayIcons ?? werk.BayIcons;

            if (string.IsNullOrEmpty(geladen.SurfaceRoad))
                geladen.SurfaceRoad = werk.SurfaceRoad;
            if (string.IsNullOrEmpty(geladen.SurfaceDecoration))
                geladen.SurfaceDecoration = werk.SurfaceDecoration;
        }

        /**
         * Version-1-Datei: Meter in Buchten umrechnen.
         *
         * (Cr - 2 * Buchtbreite) / Buchtbreite ist genau die Umkehrung der
         * Rechnung im Generator (`Layoutplanung.Querstrassenmitten`): ein
         * innerer Abschnitt besteht aus N Buchten und zwei Kappen von je
         * einer Buchtbreite. 34 m ergeben damit 9 Buchten.
         */
        private static void MigriereAufBuchten(UserDefaults values)
        {
            if (values == null || values.Version != 1) return;
            var buchten = (float)Math.Round(
                (values.CrossSpacing - 2 * Buchtbreite) / Buchtbreite,
                MidpointRounding.AwayFromZero);
            values.CrossBays = Math.Max(3, Math.Min(30, buchten));
            values.Version = SettingsVersion;
            Mod.log.Info($"PLT-Einstellungen: Abstand {values.CrossSpacing:0.#} m "
                + $"auf {values.CrossBays:0} Buchten umgerechnet (Version 1 -> "
                + $"{SettingsVersion}).");
        }

        private static bool ValidDefaults(UserDefaults values, out string reason)
        {
            if (values == null)
            {
                reason = "kein Objekt";
                return false;
            }
            if (values.Version != SettingsVersion)
            {
                reason = "Version " + values.Version;
                return false;
            }
            if (!OnScale(values.EdgeSetback, 0, 6, 0.5f))
            {
                reason = "EdgeSetback";
                return false;
            }
            if (!OnScale(values.AisleWidth, 3, 12, 0.5f))
            {
                reason = "AisleWidth";
                return false;
            }
            if (!OnScale(values.CrossWidth, 3, 9, 0.5f))
            {
                reason = "CrossWidth";
                return false;
            }
            if (!OnScale(values.MedianWidth, 0, 8, 0.5f))
            {
                reason = "MedianWidth";
                return false;
            }
            if (!OnScale(values.CrossBays, 3, 30, 1))
            {
                reason = "CrossBays";
                return false;
            }
            if (!OnScale(values.RowAngle, 0, 175, 5))
            {
                reason = "RowAngle";
                return false;
            }
            // Eine Liste, eine Wahrheit - siehe `Geometry/Winkelmodus.cs`.
            if (!Winkelmodus.IstGueltig(values.AngleMode))
            {
                reason = "AngleMode";
                return false;
            }
            if (!ValidFraction(values.PanelX) || !ValidFraction(values.PanelY)
                || !ValidFraction(values.PanelXHochkant)
                || !ValidFraction(values.PanelYHochkant))
            {
                reason = "PanelPosition";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        /** Nicht gesetzt ist erlaubt; sonst muss es ein Anteil von 0 bis 1 sein. */
        private static bool ValidFraction(float? value)
            => !value.HasValue
               || (!float.IsNaN(value.Value) && !float.IsInfinity(value.Value)
                   && value.Value >= 0f && value.Value <= 1f);

        private static bool OnScale(float value, float minimum, float maximum,
            float step)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)
                || value < minimum || value > maximum) return false;
            var steps = (value - minimum) / step;
            return Math.Abs(steps - Math.Round(steps)) <= 1e-5;
        }

        private bool TryWriteDefaults(UserDefaults values)
        {
            var path = SettingsPath();
            try
            {
                if (!ValidDefaults(values, out var reason))
                {
                    Mod.log.Warn("PLT-Einstellungen nicht gespeichert: " + reason + ".");
                    SetStatus(T("Standard konnte nicht gespeichert werden.", "Could not save the default."));
                    return false;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var json = JsonConvert.SerializeObject(values, Formatting.Indented);
                File.WriteAllText(path, json, new System.Text.UTF8Encoding(false));
                Mod.log.Info("PLT-Einstellungen gespeichert: " + path);
                return true;
            }
            catch (Exception exception)
            {
                Mod.log.Warn("PLT-Einstellungen konnten nicht gespeichert werden: "
                    + path + " (" + exception.Message + ")");
                SetStatus(T("Standard konnte nicht gespeichert werden.", "Could not save the default."));
                return false;
            }
        }

        /**
         * Das Fenster wurde losgelassen.
         *
         * Waehrend des Ziehens bewegt sich das Panel in der Oberflaeche
         * selbst; hierher kommt nur der Endstand. Anders waere bei jedem
         * Mausschritt eine Datei geschrieben worden - bei 60 Bildern in der
         * Sekunde sind das 60 Schreibvorgaenge, fuer ein Ergebnis, das erst
         * beim Loslassen feststeht.
         */
        /**
         * Nimmt nur die beiden bekannten Werte an.
         *
         * Rueckgabe `null` heisst "unbrauchbar" - dann greift beim Laden die
         * Werkseinstellung. Eine von Hand verstellte Datei mit "Hochkant "
         * oder "seitlich" darf das Panel nicht in einen Stil bringen, fuer
         * den es kein Aussehen gibt; dann steht es ohne jede Regel da.
         */
        private static string NormalisiereStil(string wert)
        {
            if (string.IsNullOrWhiteSpace(wert)) return null;
            var k = wert.Trim().ToLowerInvariant();
            if (k == PanelStilHochkant) return PanelStilHochkant;
            if (k == PanelStilHorizontal) return PanelStilHorizontal;
            return null;
        }

        /**
         * Holt den Stil aus den Modeinstellungen an die Oberflaeche.
         *
         * Laeuft in jedem Bild mit, wie die Sprache - und aus demselben
         * Grund: CS2 meldet die Aenderung einer ModSetting nicht an fremde
         * Systeme. Wer den Stil im Optionsmenue umstellt, hat das Werkzeug
         * gerade nicht offen; ohne diese Zeile saehe er die Aenderung erst
         * nach einem Neustart.
         *
         * Kostet einen Zeichenkettenvergleich je Bild.
         */
        internal void PflegeFensterstil()
        {
            PflegeSchalter();
            var jetzt = Mod.Optionen?.FensterstilKuerzel() ?? PanelStilHorizontal;
            if (_panelStil == null || _panelStil.value == jetzt) return;
            UebernimmStil(jetzt);
        }

        /**
         * Zieht die Schalter im Panel nach, wenn sie anderswo umgelegt wurden.
         *
         * Ohne das zeigte der Knopf im Panel weiter den alten Stand, wenn
         * der Nutzer die Einstellung im ESC-Menue aendert - zwei Anzeigen
         * derselben Sache, die sich widersprechen. Kostet einen
         * Wahrheitswertvergleich je Bild.
         */
        private void PflegeSchalter()
        {
            if (_autoZufahrtModus == null || Mod.Optionen == null) return;
            var jetzt = Mod.Optionen.AutomatischZufahrtModus;
            if (_autoZufahrtModus.value != jetzt)
                _autoZufahrtModus.Update(jetzt);
        }

        /**
         * Stil setzen UND die Ecke des neuen Stils mitziehen.
         *
         * Jeder Stil merkt sich seine eigene Stelle. Wird nur der Stil
         * gewechselt, zeigt die Oberflaeche die Stelle des alten - und
         * schreibt sie beim naechsten Verschieben ins Fach des neuen.
         *
         * Genau das ist passiert, weil es den Wechsel an zwei Stellen gab
         * und nur eine davon die Ecke nachzog. Deshalb steht die Antwort
         * jetzt nur noch hier, und beide Wege gehen hindurch.
         *
         * Reihenfolge ist Pflicht: `StilX`/`StilY` lesen den Stil aus der
         * Bindung, die also vorher stimmen muss.
         */
        private void UebernimmStil(string stil)
        {
            _panelStil?.Update(stil);
            _panelX?.Update(StilX() ?? 0f);
            _panelY?.Update(StilY() ?? 0f);
        }

        /**
         * Der Umschalter aus der Kopfzeile des Panels.
         *
         * Wird sofort geschrieben, nicht erst beim Schliessen: anders als
         * beim Verschieben gibt es hier keine Zwischenzustaende, die man
         * buendeln muesste - ein Klick, ein Wert.
         */
        private void SetPanelStil(string wert)
        {
            var stil = NormalisiereStil(wert);
            if (stil == null)
            {
                Mod.log.Warn("PLT-Panel: unbekannter Stil '" + wert
                    + "' verworfen; es bleibt bei '"
                    + (_panelStil?.value ?? PanelStilHorizontal) + "'.");
                return;
            }
            if (_panelStil != null && _panelStil.value == stil) return;
            /*
             * Geschrieben wird in die MODEINSTELLUNG, nicht in die
             * Panel-Datei. Sie ist der einzige Speicherort; die Bindung wird
             * ohnehin jedes Bild aus ihr nachgezogen.
             */
            var optionen = Mod.Optionen;
            if (optionen == null)
            {
                UebernimmStil(stil);
                return;
            }
            optionen.Fensterstil = stil == PanelStilHochkant
                ? Setting.Fensterstilwahl.Hochkant
                : Setting.Fensterstilwahl.Horizontal;
            optionen.ApplyAndSave();
            UebernimmStil(stil);
        }

        /**
         * Steht das Fenster gerade hochkant?
         *
         * ZUERST DIE EINSTELLUNG, DANN DIE BINDUNG - und nicht umgekehrt.
         * Die Bindung entsteht in `OnCreate` erst NACH `_panelX`/`_panelY`,
         * die sich ihren Startwert von hier holen. Wer hier nur die Bindung
         * liest, bekommt in diesem Moment `null` und damit "waagerecht":
         * ein hochkantes Fenster stand nach jedem Spielstart an der
         * waagerechten Ecke, und `PflegeFensterstil` zog es nie nach, weil
         * der Stil ja stimmte.
         *
         * Die Einstellung ist ohnehin der einzige Speicherort; die Bindung
         * ist nur ihr Abbild fuer die Oberflaeche.
         */
        private bool IstHochkant()
            => (Mod.Optionen?.FensterstilKuerzel()
                ?? _panelStil?.value
                ?? PanelStilHorizontal) == PanelStilHochkant;

        private float? StilX() => IstHochkant()
            ? _defaults.PanelXHochkant : _defaults.PanelX;

        private float? StilY() => IstHochkant()
            ? _defaults.PanelYHochkant : _defaults.PanelY;

        private void SetPanelPosition(float x, float y)
        {
            if (float.IsNaN(x) || float.IsNaN(y)) return;
            var kx = Mathf.Clamp01(x);
            var ky = Mathf.Clamp01(y);
            _panelX?.Update(kx);
            _panelY?.Update(ky);
            // Nur das Paar des AKTUELLEN Stils; das andere bleibt stehen.
            if (IstHochkant())
            {
                _defaults.PanelXHochkant = kx;
                _defaults.PanelYHochkant = ky;
            }
            else
            {
                _defaults.PanelX = kx;
                _defaults.PanelY = ky;
            }
            TryWriteDefaults(_defaults);
        }

        /**
         * Der Knopf aus den Modeinstellungen (ESC).
         *
         * Er ist die Rettung fuer den einen Fall, in dem man sich mit dem
         * Verschieben selbst aussperrt: ein Fenster, das aus dem Bild
         * geschoben wurde, laesst sich nicht mehr greifen - der Griff ist ja
         * mit hinausgewandert.
         */
        /**
         * DAS PANEL RECHNET SEINE HEIMPOSITION SELBST AUS.
         *
         * Hier stand vorher ein fester Anteil (10/1920 und 60/1080). Der war
         * schon beim Schreiben ungenau und wurde mit jeder Aenderung an der
         * Leiste falscher: als sie fuer die vierte Spalte von 1400 auf
         * 1650rem wuchs, lag die "zurueckgesetzte" Position nicht mehr oben
         * und nicht mehr sauber links. Nutzer am 2026-08-22: *"Das sollte
         * IMMER funktionieren, egal ob du was am UI aenderst."*
         *
         * Eine Zahl im C#-Teil kann das nicht leisten - sie kennt weder die
         * Breite der Leiste noch die Aufloesung. Beides weiss nur die
         * Oberflaeche. Deshalb wird hier nur noch ein Zaehler erhoeht; das
         * Panel sieht ihn steigen, misst sich selbst und setzt die Position.
         */
        internal void ResetPanelPosition()
        {
            _fensterHeim?.Update(_fensterHeim.value + 1);
            SetStatus(T("Fensterposition zurückgesetzt.", "Window position reset."));
            Mod.log.Info("PLT: Fensterposition zurueckgesetzt - das Panel "
                + "rechnet sie aus Leistenbreite und Aufloesung selbst.");
        }

        private ParkingLotToolSystem Tool()
            => World.GetOrCreateSystemManaged<ParkingLotToolSystem>();

        /** Vom Werkzeug gerufen, damit das Panel den echten Stand zeigt. */
        internal void SetMarkerState(bool mode, int count)
        {
            if (_markerMode != null && _markerMode.value != mode)
                _markerMode.Update(mode);
            if (_markerCount != null && _markerCount.value != count)
                _markerCount.Update(count);
        }

        internal void SetReportPath(string path)
            => _reportPath?.Update(path ?? string.Empty);

        /** Ein Bindingsatz haelt Knopf, aktiven Modus und Zaehler zusammen. */
        internal void SetDraftState(bool polygonClosed, bool entranceMode,
            int entranceCount, int entranceKind, int entranceMax,
            string entranceMissing)
        {
            entranceMissing = entranceMissing ?? string.Empty;
            if (_entranceMissing != null && _entranceMissing.value != entranceMissing)
                _entranceMissing.Update(entranceMissing);
            if (_entranceKind != null && _entranceKind.value != entranceKind)
                _entranceKind.Update(entranceKind);
            if (_entranceMax != null && _entranceMax.value != entranceMax)
                _entranceMax.Update(entranceMax);
            if (_polygonClosed != null && _polygonClosed.value != polygonClosed)
                _polygonClosed.Update(polygonClosed);
            if (_entranceMode != null && _entranceMode.value != entranceMode)
                _entranceMode.Update(entranceMode);
            if (_entranceCount != null && _entranceCount.value != entranceCount)
                _entranceCount.Update(entranceCount);
        }

        private void Bind(ValueBinding<float> binding, string trigger)
        {
            AddBinding(new TriggerBinding<float>(Group, trigger, value =>
                ChangeDraftSetting("Einstellung " + trigger + " geändert", () =>
                    UpdateValue(binding, value))));
        }

        private void SetAngleModeInternal(string modus)
            => UpdateValue(_angleMode, modus);

        /** Wie viele alt gebaute Parkplaetze im Spielstand liegen. */
        internal void SetAltbestand(int anzahl)
        {
            if (_altbestand != null && _altbestand.value != anzahl)
                _altbestand.Update(anzahl);
        }

        internal void SetAusrichtWahl(bool wartet)
        {
            if (_ausrichtWahl != null && _ausrichtWahl.value != wartet)
                _ausrichtWahl.Update(wartet);
        }

        /**
         * Laeuft gerade der Trennmodus?
         *
         * Die Oberflaeche haengt zwei Dinge daran: der Ausrichtknopf heisst
         * dann "Trennung fertig" und beendet ihn, und die Modusknoepfe bleiben
         * unbeleuchtet - in diesem Schritt gilt noch keiner von ihnen.
         */
        /** Laeuft der Zoning-Modus? Der Reiter faerbt daran seine Kachel. */
        internal void SetZoningModus(bool laeuft)
        {
            if (_zoningModus != null && _zoningModus.value != laeuft)
                _zoningModus.Update(laeuft);
        }

        /** Wartet das Werkzeug auf den Klick auf eine Polygonlinie? */
        internal void SetZoningLinienwahl(bool an)
        {
            if (_zoningLinienwahl != null && _zoningLinienwahl.value != an)
                _zoningLinienwahl.Update(an);
        }

        /**
         * Die Bezugslinie der Parzellen, `NaN` heisst keine.
         *
         * Dieselbe Vereinbarung wie beim Reihenwinkel: die Oberflaeche
         * schaltet daran den Namen des ersten Knopfes um ("Kante" gegen
         * "Normal") - mit gewaehlter Linie heisst "Kante" nicht mehr
         * "laengste Kante", sondern "entlang der Linie".
         */
        internal void SetZoningAusrichtwinkel(double? grad)
        {
            var wert = grad.HasValue ? (float)grad.Value : float.NaN;
            if (_zoningAusrichtwinkel == null) return;
            var alt = _zoningAusrichtwinkel.value;
            if (float.IsNaN(alt) && float.IsNaN(wert)) return;
            if (alt == wert) return;
            _zoningAusrichtwinkel.Update(wert);
        }

        /** Welche Flaeche ist gewaehlt? -1, wenn es keine gibt. */
        internal void SetZoningAuswahl(int index)
        {
            if (_zoningAuswahl != null && _zoningAuswahl.value != index)
                _zoningAuswahl.Update(index);
        }

        internal void SetZoningAussentiefe(int parzellen)
        {
            if (_zoningAussentiefe != null
                && _zoningAussentiefe.value != parzellen)
                _zoningAussentiefe.Update(parzellen);
        }

        /** Was gerade gezogen wird - leer, wenn nichts gezogen wird. */
        internal void SetZoningZug(string text)
        {
            if (_zoningZug != null && _zoningZug.value != text)
                _zoningZug.Update(text ?? string.Empty);
        }

        /** Wieviele Flaechen und Parzellen stehen? Zeigt der Zoning-Reiter. */
        internal void SetZoningZahlen(int flaechen, int parzellen)
        {
            if (_zoningFlaechen != null && _zoningFlaechen.value != flaechen)
                _zoningFlaechen.Update(flaechen);
            if (_zoningParzellen != null && _zoningParzellen.value != parzellen)
                _zoningParzellen.Update(parzellen);
        }

        private ValueBinding<bool> _zoningSeitenModus;
        private ValueBinding<bool> _zoningSeitenMoeglich;

        /**
         * Der Modus, in dem einzelne Strassenseiten geschaltet werden.
         *
         * Er hat einen eigenen Wert, weil das Panel ihn anzeigen muss: wer
         * ihn versehentlich anlaesst, klickt sonst weiter Seiten um, statt
         * Flaechen zu ziehen.
         */
        internal void SetZoningSeitenModus(bool an)
        {
            if (_zoningSeitenModus != null && _zoningSeitenModus.value != an)
                _zoningSeitenModus.Update(an);
        }

        internal void SetZoningSeitenMoeglich(bool moeglich)
        {
            if (_zoningSeitenMoeglich != null
                && _zoningSeitenMoeglich.value != moeglich)
                _zoningSeitenMoeglich.Update(moeglich);
        }

        internal void SetTrennmodus(bool laeuft)
        {
            if (_trennmodus != null && _trennmodus.value != laeuft)
                _trennmodus.Update(laeuft);
        }

        /**
         * `null` heisst: keine Linie gewaehlt, es gilt wieder die laengste
         * Kante. Die Oberflaeche schaltet daran den Namen des ersten Knopfes
         * ("Kante" gegen "Normal"), blendet "Fest" aus und graut den
         * Winkelregler aus - er zaehlt nur bei "Fest".
         */
        internal void SetAusrichtwinkel(double? grad)
        {
            _ausrichtwinkel = grad;
            var aktiv = grad.HasValue;
            if (_ausrichtAktiv != null && _ausrichtAktiv.value != aktiv)
                _ausrichtAktiv.Update(aktiv);
            // "Fest" und "Ausrichten" schliessen einander aus.
            if (aktiv && _angleMode != null && _angleMode.value == "fixed")
                SetAngleModeInternal("edge");
        }

        internal void SetUndoAvailable(bool available)
        {
            if (_undoAvailable != null && _undoAvailable.value != available)
                _undoAvailable.Update(available);
        }

        internal void SetRedoAvailable(bool available)
        {
            if (_redoAvailable != null && _redoAvailable.value != available)
                _redoAvailable.Update(available);
        }

        private void ResetAll()
        {
            var tool = Tool();
            var before = tool?.CaptureUndoState();
            if (ApplyDefaults(_defaults))
            {
                tool?.CommitUndoState(before, T("Einstellungen zurückgesetzt",
                    "settings reset"));
                Revision++;
            }
            SetStatus(T("Benutzerstandards geladen.", "Your defaults loaded."));
        }

        private void ResetOne(string key)
        {
            var tool = Tool();
            var before = tool?.CaptureUndoState();
            bool changed;
            switch (key)
            {
                case "EdgeSetback":
                    changed = UpdateValue(_edgeSetback, _defaults.EdgeSetback);
                    break;
                case "AisleWidth":
                    changed = UpdateValue(_aisleWidth, _defaults.AisleWidth);
                    break;
                case "CrossWidth":
                    changed = UpdateValue(_crossWidth, _defaults.CrossWidth);
                    break;
                case "MedianWidth":
                    changed = UpdateValue(_medianWidth, _defaults.MedianWidth);
                    break;
                case "SurfaceRoad":
                    changed = Neu(_flaecheStrasse, _defaults.SurfaceRoad);
                    break;
                case "SurfaceDecoration":
                    changed = Neu(_flaecheDeko, _defaults.SurfaceDecoration);
                    break;
                case "SurfaceRoadOn":
                    changed = UpdateValue(_flaecheStrasseAn,
                        _defaults.SurfaceRoadOn ?? true);
                    break;
                case "SurfaceApronOn":
                    changed = UpdateValue(_vorflaecheAn,
                        _defaults.SurfaceApronOn ?? true);
                    break;
                case "SurfaceDecorationOn":
                    changed = UpdateValue(_flaecheDekoAn,
                        _defaults.SurfaceDecorationOn ?? true);
                    break;
                case "BayIcons":
                    changed = UpdateValue(_buchtsymbole, _defaults.BayIcons ?? true);
                    break;
                case "CrossBays":
                    changed = UpdateValue(_crossBays, _defaults.CrossBays);
                    break;
                case "RowAngle":
                    changed = UpdateValue(_rowAngle, _defaults.RowAngle);
                    break;
                case "GreenMedian":
                    changed = UpdateValue(_greenMedian, _defaults.GreenMedian);
                    break;
                case "CrossCaps":
                    changed = UpdateValue(_crossCaps, _defaults.CrossCaps);
                    break;
                case "Randstrassen":
                    changed = UpdateValue(_randstrassen, _defaults.Randstrassen);
                    break;
                case "AngleMode":
                    changed = UpdateValue(_angleMode, _defaults.AngleMode);
                    break;
                default:
                    Mod.log.Warn("PLT: unbekannter Einstellungsschluessel: " + key);
                    return;
            }

            /*
             * WENN NICHTS PASSIERT, MUSS ES IM LOG STEHEN.
             *
             * Das Panel ruft diesen Weg nur auf, wenn der Wert vom Standard
             * abweicht - der Knopf ist sonst gesperrt. Also MUSS sich hier
             * etwas aendern. Tut es das nicht, weichen Panel und Mod in ihrer
             * Vorstellung vom Standard voneinander ab, und der Nutzer drueckt
             * auf einen Knopf, der nichts tut.
             *
             * Genau so lag der Fall am 2026-08-25 bei den beiden Flaechen,
             * und er war von aussen nicht zu sehen: kein Fehler, keine
             * Meldung, nur ein Knopf ohne Wirkung. Diese Zeile haette ihn
             * beim ersten Klick verraten.
             */
            if (!changed)
            {
                Mod.log.Warn("PLT: Zuruecksetzen von " + key
                    + " hat nichts geaendert - Panel und Mod sind sich ueber "
                    + "den Standard nicht einig.");
                return;
            }
            tool?.CommitUndoState(before, T("Einstellung " + key + " zurückgesetzt",
                "setting " + key + " reset"));
            Revision++;
        }

        private void SetAsDefault(string key)
        {
            var next = _defaults.Clone();
            switch (key)
            {
                case "EdgeSetback": next.EdgeSetback = _edgeSetback.value; break;
                case "AisleWidth": next.AisleWidth = _aisleWidth.value; break;
                case "CrossWidth": next.CrossWidth = _crossWidth.value; break;
                case "MedianWidth": next.MedianWidth = _medianWidth.value; break;
                case "CrossBays": next.CrossBays = _crossBays.value; break;
                case "SurfaceRoad": next.SurfaceRoad = _flaecheStrasse.value; break;
                case "SurfaceDecoration":
                    next.SurfaceDecoration = _flaecheDeko.value; break;
                case "SurfaceRoadOn":
                    next.SurfaceRoadOn = _flaecheStrasseAn.value; break;
                case "SurfaceApronOn":
                    next.SurfaceApronOn = _vorflaecheAn.value; break;
                case "SurfaceDecorationOn":
                    next.SurfaceDecorationOn = _flaecheDekoAn.value; break;
                case "BayIcons": next.BayIcons = _buchtsymbole.value; break;
                case "RowAngle": next.RowAngle = _rowAngle.value; break;
                case "GreenMedian": next.GreenMedian = _greenMedian.value; break;
                case "CrossCaps": next.CrossCaps = _crossCaps.value; break;
            case "Randstrassen": next.Randstrassen = _randstrassen.value; break;
                case "AngleMode": next.AngleMode = _angleMode.value; break;
                default:
                    Mod.log.Warn("PLT: unbekannter Einstellungsschluessel: " + key);
                    return;
            }

            // Erst nach erfolgreichem Schreiben darf das Panel den neuen
            // Standard zeigen; sonst waere die Diskette ein falsches Versprechen.
            if (!TryWriteDefaults(next)) return;
            _defaults = next;
            PublishDefaults();
            SetStatus(T("Benutzerstandard gespeichert.", "Default saved."));
        }

        /**
         * Einstieg von der Optionsseite: dasselbe wie der Knopf im Panel.
         *
         * Getrennt benannt, damit an der Aufrufstelle steht, WER hier
         * aufraeumt - der Knopf im Panel heisst fuer den Nutzer anders als
         * der in den Einstellungen, tut aber dasselbe.
         */
        internal void VerwirfBenutzerstandards() => DiscardDefaults();

        private void DiscardDefaults()
        {
            var tool = Tool();
            var before = tool?.CaptureUndoState();
            var path = SettingsPath();
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception exception)
            {
                Mod.log.Warn("PLT-Einstellungen konnten nicht verworfen werden: "
                    + path + " (" + exception.Message + ")");
                SetStatus(T("Gespeicherte Standards konnten nicht verworfen werden.", "Could not discard the saved defaults."));
                return;
            }

            _defaults = FactoryDefaults();
            // Die Werksposition gehoert zu den Werkswerten, das Fenster geht
            // also mit zurueck. Das ist zugleich der zweite Weg heraus, wenn
            // man es aus dem Bild geschoben hat.
            // Beide Stile gehen zurueck; gezeigt wird das Paar des aktuellen.
            _panelX?.Update(StilX() ?? 0f);
            _panelY?.Update(StilY() ?? 0f);
            PublishDefaults();
            if (ApplyDefaults(_defaults))
            {
                tool?.CommitUndoState(before, T("Werkswerte geladen",
                    "factory values loaded"));
                Revision++;
            }
            SetStatus(T("Werkswerte geladen; gespeicherte Standards verworfen.",
                "Factory values loaded; saved defaults discarded."));
            Mod.log.Info("PLT-Einstellungen verworfen; CS2-Werte aktiv: " + path);
        }

        private bool ApplyDefaults(UserDefaults values)
        {
            var changed = false;
            changed |= UpdateValue(_edgeSetback, values.EdgeSetback);
            changed |= UpdateValue(_aisleWidth, values.AisleWidth);
            changed |= UpdateValue(_crossWidth, values.CrossWidth);
            changed |= UpdateValue(_medianWidth, values.MedianWidth);
            changed |= UpdateValue(_crossBays, values.CrossBays);
            changed |= Neu(_flaecheStrasse, values.SurfaceRoad);
            changed |= Neu(_flaecheDeko, values.SurfaceDecoration);
            changed |= UpdateValue(_flaecheStrasseAn, values.SurfaceRoadOn ?? true);
            changed |= UpdateValue(_vorflaecheAn, values.SurfaceApronOn ?? true);
            changed |= UpdateValue(_flaecheDekoAn,
                values.SurfaceDecorationOn ?? true);
            changed |= UpdateValue(_buchtsymbole, values.BayIcons ?? true);
            changed |= UpdateValue(_rowAngle, values.RowAngle);
            changed |= UpdateValue(_greenMedian, values.GreenMedian);
            changed |= UpdateValue(_crossCaps, values.CrossCaps);
            changed |= UpdateValue(_randstrassen, values.Randstrassen);
            changed |= UpdateValue(_angleMode, values.AngleMode);
            return changed;
        }

        private void PublishDefaults()
        {
            _edgeSetbackDefault.Update(_defaults.EdgeSetback);
            _aisleWidthDefault.Update(_defaults.AisleWidth);
            _crossWidthDefault.Update(_defaults.CrossWidth);
            _medianWidthDefault.Update(_defaults.MedianWidth);
            _crossBaysDefault.Update(_defaults.CrossBays);
            /*
             * OHNE BEDINGUNG. Hier stand `if (!string.IsNullOrEmpty(...))`,
             * und genau das hat den Fehler vom 2026-08-25 unsichtbar gemacht:
             * bei einem leeren Standard behielt die Bindung ihren Startwert,
             * das Panel hielt "Pavement Surface 01" fuer den gespeicherten
             * Standard und zeigte den Zuruecksetzen-Knopf als benutzbar an -
             * obwohl Zuruecksetzen nichts tun konnte. Leer kann hier seit
             * `FuelleFehlendeAuf` nicht mehr vorkommen; falls doch, soll das
             * Panel die Wahrheit zeigen statt einer bequemen Luege.
             */
            _flaecheStrasseStd.Update(_defaults.SurfaceRoad);
            _flaecheDekoStd.Update(_defaults.SurfaceDecoration);
            _flaecheStrasseAnStd.Update(_defaults.SurfaceRoadOn ?? true);
            _vorflaecheAnStd.Update(_defaults.SurfaceApronOn ?? true);
            _flaecheDekoAnStd.Update(_defaults.SurfaceDecorationOn ?? true);
            _buchtsymboleStd.Update(_defaults.BayIcons ?? true);
            _rowAngleDefault.Update(_defaults.RowAngle);
            _greenMedianDefault.Update(_defaults.GreenMedian);
            _crossCapsDefault.Update(_defaults.CrossCaps);
            _randstrassenDefault.Update(_defaults.Randstrassen);
            _angleModeDefault.Update(_defaults.AngleMode);
        }

        private static bool UpdateValue(ValueBinding<float> binding, float value)
        {
            if (Math.Abs(binding.value - value) <= 1e-6f) return false;
            binding.Update(value);
            return true;
        }

        private static bool UpdateValue(ValueBinding<bool> binding, bool value)
        {
            if (binding.value == value) return false;
            binding.Update(value);
            return true;
        }

        private static bool UpdateValue(ValueBinding<string> binding, string value)
        {
            if (string.Equals(binding.value, value, StringComparison.Ordinal)) return false;
            binding.Update(value);
            return true;
        }

        /**
         * Der aktuelle Stand als Geometrie-Einstellung.
         *
         * `Md` wird auf 0 gezogen, wenn das Mittelgruen abgeschaltet ist -
         * genau so macht es der Prototyp (`md: green ? medianWidth : 0`).
         * Eine eigene Kennung dafuer gibt es im Modell nicht.
         */
        private ValueBinding<bool> _entwicklerDebug;
        private ValueBinding<bool> _absturzErkannt;
        private ValueBinding<string> _absturzBefund;
        private ValueBinding<string> _meldungPfad;
        private ValueBinding<string> _baubefund;
        private ValueBinding<string> _baukurzinfo;

        /**
         * WAS BEIM LETZTEN BAU AUFFIEL - IM KLARTEXT, IM MELDEREITER.
         *
         * Der Nutzer hat sich das zur Testveroeffentlichung ausgesucht: ein
         * Tester soll selbst sehen, ob etwas faul war, BEVOR er meldet. Und
         * steht dort nichts, weiss er auch das.
         *
         * Hier stehen die Hinweise UNGEFILTERT. Die Statusleiste filtert
         * bewusst (siehe `Hinweisfilter`), weil sie beim Arbeiten nicht
         * zutexten soll. Beim Melden ist es umgekehrt: was weggelassen wurde,
         * ist genau die Sorte Satz, die den Befund traegt.
         *
         * Die Kurzinfo daneben beantwortet die Fragen, die sonst jede Meldung
         * kostet: wie gross, wann, welche Fassung. Damit taugt schon ein
         * Bildschirmfoto des Reiters, wenn jemand die Datei vergisst.
         */
        internal void SetzeBaubefund(ParkingLayout layout, double arealflaeche)
        {
            if (layout == null) return;

            var hinweise = layout.Warnings ?? Array.Empty<string>();
            _baubefund?.Update(hinweise.Length == 0
                ? string.Empty
                : string.Join("\n", hinweise));

            var flaechen = (layout.GrassSurface?.Length ?? 0)
                + (layout.AsphaltSurface?.Length ?? 0);
            var wege = layout.NetLine?.Length ?? 0;
            var fassung = typeof(ParkingLotUISystem).Assembly
                .GetName().Version?.ToString() ?? "?";
            _baukurzinfo?.Update(
                $"{layout.Stalls} Buchten · {layout.Aisles} Gassen · "
                + $"{flaechen} Flächen · {wege} Wege · "
                + $"{arealflaeche:F0} m² · "
                + DateTime.Now.ToString("HH:mm:ss") + " · PLT " + fassung);
        }

        /**
         * Schnuert ein Meldepaket und sagt der UI, wo es liegt.
         *
         * Der Pfad geht in eine eigene Bindung statt in die Statuszeile: der
         * Nutzer soll ihn markieren und kopieren koennen, und die Statuszeile
         * wird vom naechsten Klick ueberschrieben.
         */
        /**
         * Startet die Leistungsmessung auf Zeit.
         *
         * Eine Minute ist lang genug, um mehrere Ausreisser einzufangen, und
         * kurz genug, dass jemand sie abwartet. Wer laenger messen will,
         * drueckt danach noch einmal.
         */
        private void StarteLeistungstest()
        {
            if (ParkingLotMessung.Zeichnetauf) return;
            ParkingLotMessung.StarteAufzeichnung(LeistungSekunden,
                "Knopf im Melde-Reiter");
            _leistungRest?.Update(LeistungSekunden);
            SetStatus(ParkingLotTexte.T(
                "Leistungsmessung läuft. Spiel normal weiter - gerade das "
                    + "Gewöhnliche soll gemessen werden.",
                "Performance measurement running. Just keep playing - the "
                    + "ordinary case is what we want to see."));
            Mod.log.Info("PLT-Messung: Aufzeichnung ueber "
                + LeistungSekunden + " s gestartet (Melde-Reiter).");
        }

        /**
         * Schaut je Bild nach, ob die Messung fertig ist, und schnuert dann.
         *
         * Laeuft im selben Takt wie Sprache und Fensterstil - also auch bei
         * zugem Werkzeug. Das ist Absicht: gerade dieser Zustand ist der
         * gemessene, und niemand soll das Panel offenhalten muessen.
         */
        internal void PflegeLeistungstest()
        {
            if (_leistungRest == null) return;
            if (ParkingLotMessung.Zeichnetauf)
            {
                var rest = ParkingLotMessung.Restsekunden;
                if (_leistungRest.value != rest) _leistungRest.Update(rest);
                return;
            }
            if (!ParkingLotMessung.Abholbereit) return;
            _leistungRest.Update(0);
            SchnuereMeldung(ParkingLotMeldepaket.Anlass.Leistung);
        }

        private void SchnuereMeldung(ParkingLotMeldepaket.Anlass anlass)
        {
            var pfad = ParkingLotMeldepaket.Schnuere(anlass, out var grund);
            if (pfad == null)
            {
                _meldungPfad?.Update(string.Empty);
                SetStatus(grund ?? ParkingLotTexte.T(
                    "Die Meldung konnte nicht erstellt werden.",
                    "The report could not be created."));
                return;
            }
            _meldungPfad?.Update(pfad);
            SetStatus(ParkingLotTexte.T(
                "Meldung erstellt. Schick die Datei mit.",
                "Report created. Send the file along."));
        }

        /**
         * Macht den Logs-Ordner auf.
         *
         * `Application.OpenURL` mit `file://` ist der Weg, der ohne
         * Prozessstart auskommt - CS2 laeuft im Vollbild, und ein eigener
         * Explorer-Aufruf hat sich dort schon als heikel erwiesen.
         */
        private void OeffneLogordner()
        {
            try
            {
                // UNSER Ordner, nicht der von CS2. Dort liegen die
                // Meldungen nach Sparte getrennt - siehe ParkingLotMeldepaket.
                var ordner = ParkingLotMeldepaket.Meldeordner;
                System.IO.Directory.CreateDirectory(ordner);
                UnityEngine.Application.OpenURL("file:///"
                    + ordner.Replace('\\', '/'));
            }
            catch (Exception ausnahme)
            {
                Mod.log.Warn("PLT: Logs-Ordner nicht zu oeffnen: "
                    + ausnahme.Message);
            }
        }

        /**
         * Wieviel schmaler der Belag ist als die Gasse, in Metern.
         *
         * Zusammen, nicht je Seite: 2,5 laesst an einer 8-m-Gasse 1,25 m
         * Bordstein je Seite frei, der Belag misst dann 5,50 m. Der Nutzer
         * hat am 2026-09-18 der Reihe nach 7, 6 und 5,5 verlangt - jedes Mal
         * lag der Belag noch auf der Kante.
         */
        private const float GassenbelagLuft = 2.5f;

        internal LayoutSettings CurrentSettings()
        {
            var settings = _loadedBuildReceiptTemplate?.Clone()
                ?? LayoutSettings.Cs2;
            settings.Es = _edgeSetback.value;
            settings.Ai = _aisleWidth.value;
            /*
             * DIE GEMESSENE BREITE DER GASSE, an der einen Stelle, an der
             * alle Einstellungen zusammenlaufen. Ist das Prefab noch nicht
             * bereit, bleibt der Standardwert stehen.
             */
            var gassenbreite = Tool()?.Gassenbreite() ?? 0f;
            /*
             * EIN HALBER METER JE SEITE BLEIBT DER GASSE.
             *
             * `m_DefaultWidth` misst die ganze Strasse samt Bordsteinen. Ein
             * Belag in voller Breite legt sich ueber die Bordsteinkante und
             * laesst sie verschwinden; bei 8,00 m Gasse sind 7,00 m Belag
             * die Breite, bei der die Kante stehen bleibt.
             */
            if (gassenbreite > GassenbelagLuft)
                settings.Gassenbreite = gassenbreite - GassenbelagLuft;
            // Eine Messung fuer alle drei Gassenarten: sie entstehen seit
            // dem 2026-09-18 aus demselben Vorbild, und getrennte Werte
            // koennten auseinanderlaufen. Dann faehren die Autos optisch
            // ueber das Gras.
            settings.Cw = _crossWidth.value;
            settings.Md = _greenMedian.value ? _medianWidth.value : 0;
            /**
             * DIE +2 SIND DIE BEIDEN KAPPEN.
             *
             * "Verbindung alle N Buchten" wird in einen Abstand in Metern
             * umgerechnet. Ein Abschnitt enthaelt aber nicht nur die N
             * Buchten, sondern an jedem Ende noch eine Kappe von einer
             * Buchtbreite - deshalb `N + 2`.
             *
             * Schaltet der Nutzer die Kappen ab, faellt diese Reserve im
             * Spaltenplan weg, und in dieselbe Laenge passen N + 2 Buchten.
             * Nutzerbefund am 2026-08-24: eingestellt 5, gebaut 7. Der Zaehler
             * auf dem Regler stimmte dann nicht mehr mit dem ueberein, was im
             * Spiel steht.
             *
             * Also mitrechnen: ohne Kappen ist der Abstand N Buchtbreiten.
             */
            settings.Qk = _crossCaps.value;
            settings.Randstrassen = _randstrassen.value;
            settings.Cr = (_crossBays.value + (settings.Qk ? 2 : 0))
                * Buchtbreite;
            settings.AngleMode = _angleMode.value;
            // Die gewaehlte Bezugslinie ersetzt die laengste Kante als
            // Nullpunkt. Ist keine gewaehlt, bleibt alles wie bisher.
            settings.Ausrichtwinkel = _ausrichtwinkel;
            settings.Auto = _angleMode.value == "auto";
            settings.Angle = _rowAngle.value;
            settings.Zellen = _engine.value == "zellen";
            // Gleiches Prefab fuer beide Kategorien: dann eine Flaeche statt
            // vieler. Verglichen wird der NAME, nicht der aufgeloeste Entity -
            // der steht hier noch gar nicht fest.
            settings.EineFlaeche = !string.IsNullOrEmpty(FlaecheStrasse)
                && FlaecheStrasse == FlaecheDekoration;
            return settings;
        }

        /**
         * Laedt den Bauzettel in dieselben Bindungen, die auch ein neuer
         * Entwurf benutzt. Die Benutzerstandards bleiben dabei unberuehrt.
         */
        internal void LoadBuildReceipt(ParkingLotBuildReceipt receipt,
                                       string surfaceRoad,
                                       string surfaceDecoration,
                                       string surfaceZoning = "")
        {
            // Leer ist ein gueltiger Wert: dann gilt die Dekoflaeche. So
            // verhaelt sich auch ein Bauzettel von vor dem 2026-09-02.
            UpdateValue(_flaecheZoning, surfaceZoning ?? string.Empty);
            /*
             * GEGENPROBE FUER DEN BAUZETTEL-WAECHTER.
             *
             * Der Waechter in `PruefeBauzettelUebernahme` meldet sich nur,
             * wenn etwas nicht uebernommen wurde. Ein Waechter, der schweigt,
             * beweist aber nichts - er koennte auch kaputt sein. Steht
             * `bauzettelvorlage` in `<Spielordner>/Logs/PLT-AUS.txt`, faellt
             * das Template absichtlich weg. Dann MUSS der Waechter beim
             * naechsten Bearbeiten `Sl`, `Sw`, `NoNotch`, `Single`, `NoHalf`,
             * `KantenVersatz` und `AutomaticEntrances` nennen.
             *
             * Genau die sieben Werte haengen an diesem Template und an keinem
             * Regler. Wer die Datei wieder leert, ist zurueck im Normalbetrieb.
             */
            _loadedBuildReceiptTemplate = Mod.Aus("bauzettelvorlage")
                ? null
                : receipt.ToLayoutSettings(Array.Empty<Entrance>());
            if (_loadedBuildReceiptTemplate == null)
                Mod.log.Warn("PLT-AUS: die Bauzettel-Vorlage wurde absichtlich "
                    + "weggelassen. Das ist die Gegenprobe fuer den "
                    + "Bauzettel-Waechter - er muss jetzt Abweichungen melden.");
            var changed = false;
            changed |= UpdateValue(_edgeSetback, (float)receipt.Es);
            changed |= UpdateValue(_aisleWidth, (float)receipt.Ai);
            changed |= UpdateValue(_crossWidth, (float)receipt.Cw);
            changed |= UpdateValue(_medianWidth, (float)receipt.MedianWidth);
            changed |= UpdateValue(_crossBays, (float)receipt.CrossBays);
            changed |= UpdateValue(_rowAngle, (float)receipt.Angle);
            changed |= UpdateValue(_greenMedian, receipt.GreenMedian);
            changed |= UpdateValue(_crossCaps, receipt.Qk);
            changed |= UpdateValue(_randstrassen, receipt.Randstrassen);
            changed |= UpdateValue(_angleMode,
                ParkingLotBuildReceipt.DecodeAngleMode(receipt.AngleMode));
            changed |= UpdateValue(_engine, receipt.Zellen ? "zellen" : "alt");
            changed |= UpdateValue(_flaecheStrasse, surfaceRoad);
            changed |= UpdateValue(_flaecheDeko, surfaceDecoration);
            changed |= UpdateValue(_flaecheStrasseAn, receipt.SurfaceRoadOn);
            changed |= UpdateValue(_flaecheDekoAn, receipt.SurfaceDecorationOn);
            changed |= UpdateValue(_vorflaecheAn, receipt.SurfaceApronOn);
            changed |= UpdateValue(_buchtsymbole, receipt.BayIcons);
            if (changed) Revision++;
            SetTab(DraftTab);
        }

        internal void ClearBuildReceiptTemplate()
            => _loadedBuildReceiptTemplate = null;

        /** Rueckmeldung des asynchron im naechsten Werkzeugframe laufenden Suchlaufs. */
        internal void SetUeberlappungsstand(string text)
        {
            text ??= string.Empty;
            if (_ueberlappungsstand != null
                && !string.Equals(_ueberlappungsstand.value, text,
                    StringComparison.Ordinal))
                _ueberlappungsstand.Update(text);
        }

        private void EditSelectedParkingLot()
        {
            var selectedInfo = World.GetOrCreateSystemManaged<
                Game.UI.InGame.SelectedInfoUISystem>();
            Tool()?.RequestEdit(selectedInfo?.selectedEntity
                ?? Unity.Entities.Entity.Null);
        }

        /**
         * SCHREIBT EINEN BERICHT ZUM ANGEWAEHLTEN PARKPLATZ.
         *
         * Ansage des Nutzers am 2026-09-14: *"Wenn er auf den Parkplatz
         * klickt, der gebaut wurde, und im Info-Panel auf 'Debug schreiben'
         * klickt, dann halt von dem, der gebaut wurde, die ganzen Infos."*
         *
         * Der Abzug nimmt deshalb GENAU DIESEN Parkplatz - er traegt seinen
         * Bauzettel selbst, und `FordereLotAbzug` legt ihn dem Abzug vor.
         * Ohne das haette der Abzug genommen, was das Werkzeug gerade in der
         * Hand haelt; beim Melden ist das meistens nichts.
         *
         * Das Paket wird EINEN Frame spaeter geschnuert: der Abzug selbst
         * laeuft im naechsten Werkzeugdurchlauf, und wer sofort einpackt,
         * packt den vorigen ein.
         */
        private void MeldeGewaehltenParkplatz()
        {
            var selectedInfo = World.GetOrCreateSystemManaged<
                Game.UI.InGame.SelectedInfoUISystem>();
            var lot = selectedInfo?.selectedEntity
                ?? Unity.Entities.Entity.Null;
            if (lot == Unity.Entities.Entity.Null)
            {
                SetStatus(ParkingLotTexte.T(
                    "Kein Parkplatz gewählt.", "No parking lot selected."));
                return;
            }
            MeldeParkplatz(lot);
        }

        /**
         * Der eine Meldeweg fuer einen bestimmten Parkplatz.
         *
         * Aufgerufen aus dem Auswahlfenster UND aus dem Melden-Reiter, wo man
         * den Parkplatz im Gelaende anklickt. Zwei Wege zum selben Ergebnis
         * waeren zwei Wege, die auseinanderlaufen koennen.
         */
        internal void MeldeParkplatz(Unity.Entities.Entity lot)
        {
            if (lot == Unity.Entities.Entity.Null) return;
            Tool()?.FordereLotAbzug(lot);
            _meldungNachAbzug = true;
            SetStatus(ParkingLotTexte.T(
                "Bericht wird erstellt …", "Creating report …"));
        }

        /** Laeuft die Parkplatzwahl gerade? Fuer den Knopf im Reiter. */
        private ValueBinding<bool> _meldeLotWahl;

        internal void SetMeldeLotWahl(bool an) => _meldeLotWahl?.Update(an);

        /**
         * Steht der Abzug noch aus? Siehe `MeldeGewaehltenParkplatz`.
         *
         * Abgefragt wird das dort, wo der Abzug fertig meldet - so bleibt
         * die Reihenfolge "erst abziehen, dann einpacken" ohne einen Warter,
         * der jeden Frame nachsieht.
         */
        private bool _meldungNachAbzug;

        internal void AbzugFertig()
        {
            if (!_meldungNachAbzug) return;
            _meldungNachAbzug = false;
            SchnuereMeldung(ParkingLotMeldepaket.Anlass.Bau);
        }

        /**
         * Der Knopf oben schaltet das WERKZEUG, nicht nur das Panel.
         *
         * Der erste Anlauf zeigte ihn nur bei laufendem Werkzeug - damit war
         * er zirkulaer: zum Einschalten taugte er nicht, und wer Strg+P nicht
         * kennt, findet den Mod gar nicht. Jetzt ist er immer da und ist der
         * sichtbare Weg hinein.
         */
        private void ToggleTool()
        {
            var toolSystem = World.GetOrCreateSystemManaged<Game.Tools.ToolSystem>();
            var parkingTool = World.GetOrCreateSystemManaged<ParkingLotToolSystem>();
            if (toolSystem.activeTool == parkingTool)
            {
                toolSystem.activeTool =
                    World.GetOrCreateSystemManaged<Game.Tools.DefaultToolSystem>();
                return;
            }
            toolSystem.activeTool = parkingTool;
        }

        /**
         * Mit dem Werkzeug geht auch das Panel auf.
         *
         * Wer das Werkzeug startet, will einstellen - ein leerer Bildschirm
         * mit einem zweiten noetigen Klick waere nur eine Huerde. Beim
         * Beenden bleibt der Merker stehen, das Panel haengt ohnehin am
         * Werkzeugzustand.
         */
        internal void SetToolActive(bool active)
        {
            _toolActive?.Update(active);
            if (active)
            {
                _panelOpen?.Update(true);
                RefreshVegetation();
                Tool()?.SetzeReiter(ParkingLotToolSystem.ReiterAus(_tab?.value));
            }
            else CloseDown();
        }

        /**
         * Der Reiter "Entwurf" ist der Startzustand.
         *
         * Wer das Fenster im Melde-Reiter verlaesst, will beim naechsten Mal
         * entwerfen, nicht melden. Und das Markieren gehoert ausschliesslich
         * dorthin: bliebe es an, wuerden Linksklicks auf der Karte weiter
         * Fehlerstellen setzen statt Polygonecken - im Werkzeug ist der
         * Markiermodus eine harte Weiche vor der ganzen Klickbehandlung.
         */
        private const string DraftTab = "layout";
        private const string ReportTab = "report";

        private const string ZoningTab = "zoning";

        private void SetTab(string value)
        {
            var vorher = _tab.value;
            _tab.Update(value);
            if (value != ReportTab) VerlasseMeldemodus();
            if (vorher == value)
            {
                Tool()?.SetzeReiter(ParkingLotToolSystem.ReiterAus(value));
                return;
            }

            /*
             * DER ZONING-REITER SCHALTET SEIN WERKZEUG SELBST EIN UND AUS.
             *
             * Ansage des Nutzers: *"'Place Patches' sollte automatisch an
             * sein, wenn ich in den Reiter Zoning gehe"* und *"Place Patches
             * und Toggle road side sollten ausgehen, wenn der Reiter
             * gewechselt wird."*
             *
             * Ein Modus, der einen Reiter ueberlebt, ist eine Falle: der
             * naechste Linksklick geht dann irgendwo hin, wo der Nutzer
             * gerade gar nicht arbeitet.
             */
            /*
             * DER REITER GEHT JETZT ANS WERKZEUG, nicht nur der Zoning-Modus.
             *
             * Hier standen zwei Zeilen, die genau die zwei Zoning-Modi
             * abraeumten. Dieselbe Regel gilt fuer den Zugangsmodus und das
             * Ausrichten - `SetzeReiter` raeumt sie alle ab und schaltet das
             * Zoningwerkzeug ein, wenn der Reiter es mitbringt.
             */
            Tool()?.SetzeReiter(ParkingLotToolSystem.ReiterAus(value));
        }

        /**
         * Melde-Reiter verlassen heisst: Markiermodus AUS und Markierungen weg.
         *
         * Nutzerwunsch vom 2026-08-12. Die Markierungen ueberlebten bisher den
         * Reiterwechsel, lagen also weiter magenta auf der Karte, obwohl das
         * Meldewerkzeug gar nicht mehr offen war - und landeten im naechsten
         * Bericht, der eigentlich einen anderen Fall zeigen sollte.
         *
         * Wer melden will, schreibt den Bericht (Alt+P), BEVOR er den Reiter
         * wechselt. Das ist ohnehin die Reihenfolge, in der das Werkzeug
         * gedacht ist.
         */
        private void VerlasseMeldemodus()
        {
            var werkzeug = Tool();
            if (werkzeug == null) return;
            werkzeug.SetMarkerMode(false);
            werkzeug.ClearMarkers();
        }

        private void SetPanelOpen(bool open)
        {
            _panelOpen.Update(open);
            /*
             * DEN ENTWICKLER-REITER BEIM OEFFNEN NACHFUEHREN.
             *
             * Die Einstellung liegt in CS2s Optionsmenue, das Panel liest sie
             * nicht von selbst. Wer den Schalter umlegt, macht danach das
             * Panel auf - genau hier ist also der richtige Moment. Ein eigenes
             * System, das jeden Frame nachsieht, waere fuer einen Schalter
             * zuviel.
             */
            _entwicklerDebug?.Update(Mod.Optionen?.EntwicklerDebug ?? false);
            if (!open) CloseDown();
        }

        private void CloseDown()
        {
            _tab?.Update(DraftTab);
            VerlasseMeldemodus();
        }

        internal void SetStatus(string text) => _status?.Update(text ?? string.Empty);

        /**
         * Die Sprache neu einlesen - nach jeder Aenderung in den Optionen.
         *
         * CS2 meldet Aenderungen an einer ModSetting nicht von selbst an
         * fremde Systeme; deshalb wird sie im Werkzeug-Durchlauf mitgefuehrt.
         * Das kostet einen Vergleich je Frame und spart eine Ereigniskette,
         * die man vergessen kann.
         */
        internal void PflegeSprache()
        {
            var jetzt = Mod.Optionen?.SprachKuerzel() ?? "en";
            if (_sprache != null && _sprache.value != jetzt) _sprache.Update(jetzt);
        }

        /** Mehrere Hinweise durch Zeilenumbruch getrennt; leer heisst: keine. */
        internal void SetHinweise(string[] hinweise)
            => _hinweis?.Update(hinweise == null || hinweise.Length == 0
                ? string.Empty
                : string.Join("\n", hinweise));

        /** Kennzahlen nach jedem Vorschaulauf. */
        internal void ShowResult(ParkingLayout layout, double siteArea)
        {
            if (layout == null) return;
            _stalls.Update(layout.Stalls);
            _perimeterStalls.Update(layout.PerimeterStalls);
            _aisles.Update(layout.Aisles);
            _rowAngleResult.Update(layout.Angle.ToString("F0") + " Grad");
            _siteArea.Update(siteArea.ToString("F0") + " m²");
            _areaPerStall.Update(layout.Stalls > 0
                ? (siteArea / layout.Stalls).ToString("F1") + " m²" : "-");
            var settings = CurrentSettings();
            // Die feste Bucht 3,00 x 5,90 m und der daraus berechnete
            // Modulabstand standen vorher im Panel. Im Log bleiben beide je
            // abgeschlossenem Vorschaulauf am tatsaechlich benutzten Stand.
            Mod.log.Info("PLT-Vorschaumasse: Bucht " + Measure(settings.Sw)
                + " x " + Measure(settings.Sl) + " m, Modulabstand "
                + Measure(settings.Ai + 2 * settings.Sl + settings.Md) + " m.");
        }

        private static string Measure(double value) =>
            value.ToString("F2").Replace('.', ',');
    }
}
