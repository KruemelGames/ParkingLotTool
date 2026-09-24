using System.Collections.Generic;
using System.Text;
using Colossal.UI.Binding;
using Game.Common;
using Game.Tools;
using Game.UI;
using ParkingLotTool.Geometry;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * Die Parkplatzliste: alle eigenen Plaetze auf einen Blick.
     *
     * ALLES GEHT ALS EIN STRING HINUEBER, Zeilen durch Zeilenumbruch und
     * Felder durch Tabulator getrennt. Das ist keine Bequemlichkeit: ein
     * `ValueBinding<string[]>` braucht in CS2 einen eigenen Schreiber, und
     * ohne ihn wirft schon der Konstruktor - mitten in `OnLoad`, womit der
     * ganze Mod abgeraeumt wird. Steht ausfuehrlich an der Flaechenliste in
     * `ParkingLotUISystem.cs`.
     *
     * ZWEI GETRENNTE BINDUNGEN, weil sie unterschiedlich schnell altern:
     * die Grunddaten (Belegung, Gebuehr) laufen mit, die Infokarten stehen
     * eine ganze Runde lang still. Waeren sie zusammen, flackerte entweder
     * die Karte oder die Belegung waere eine Minute alt.
     */
    public sealed partial class ParkingLotListeUISystem : UISystemBase
    {
        private const string Group = "ParkingLotTool";

        /** Grunddaten etwa jede Sekunde neu. */
        internal const int AktualisierungFrames = 60;

        internal const int MaximaleGebuehr = 50;

        /** Felder je Infoblock - siehe `SchreibeBlock`. */
        internal const int FelderJeBlock = 7;

        private Game.UI.InGame.SelectedInfoUISystem _selectedInfo;
        private NameSystem _namen;
        private ParkingLotComfortSystem _comfort;
        private Game.Simulation.SimulationSystem _simulation;
        private Game.Simulation.TimeSystem _zeit;
        private ICityStatistikQuelle _stadtwerte;
        private EntityQuery _lotQuery;

        private ValueBinding<string> _liste;
        private ValueBinding<string> _infos;
        private ValueBinding<string> _runde;
        private ValueBinding<bool> _hatVorrunde;
        private ValueBinding<bool> _waisenAuto;
        private ParkingLotWaisenSystem _waisen;
        private bool _zeigtVorrunde;

        private readonly List<Entity> _lots = new List<Entity>();
        private readonly Dictionary<Entity, Infowerte> _werte =
            new Dictionary<Entity, Infowerte>();

        private readonly Infoart[] _angebot = new Infoart[48];
        private readonly Infoart[] _rundeJetzt =
            new Infoart[Infokarten.RotierendeKategorien.Length];
        private readonly List<Infoart> _rundeVorher = new List<Infoart>();
        private Infozufall _zufall = new Infozufall(0u);

        private string _infosJetzt = string.Empty;
        private string _infosVorher = string.Empty;
        private string _rundeTextJetzt = string.Empty;
        private string _rundeTextVorher = string.Empty;
        private int _rundennummer;
        private bool _rundeSteht;

        private int _frames;

        /** Bestand der letzten Runde - aendert er sich, wird sofort neu gebaut. */
        private int _zuletztGezaehlt = -1;

        private readonly StringBuilder _bau = new StringBuilder(4096);

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _selectedInfo = World.GetOrCreateSystemManaged<
                Game.UI.InGame.SelectedInfoUISystem>();
            _namen = World.GetOrCreateSystemManaged<NameSystem>();
            _comfort = World.GetOrCreateSystemManaged<ParkingLotComfortSystem>();
            _simulation = World.GetOrCreateSystemManaged<
                Game.Simulation.SimulationSystem>();
            _zeit = World.GetOrCreateSystemManaged<Game.Simulation.TimeSystem>();
            _stadtwerte = new CityStatistikQuelle(World);
            _waisen = World.GetOrCreateSystemManaged<ParkingLotWaisenSystem>();

            /*
             * DIE KAPAZITAET IST KEINE BEDINGUNG.
             *
             * Zuerst stand hier auch `ParkingLotEconomyData`. Damit tauchte
             * ein frisch gebauter Parkplatz erst Sekunden spaeter auf: die
             * Kapazitaet wird aus den WIRKLICHEN Parkspuren gemessen, und die
             * gibt es direkt nach dem Bau noch nicht - `ParkingLotEconomySystem`
             * misst dann spaeter nach. Der Nutzer hat genau das gemeldet.
             *
             * Jetzt reicht der Rueckweg zum Traeger. Was noch nicht gemessen
             * ist, steht als Null in der Zeile und fuellt sich von selbst.
             */
            _lotQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ParkingLotCarrierReference>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });

            AddBinding(_liste = new ValueBinding<string>(
                Group, "ParkplatzListe", string.Empty));
            AddBinding(_infos = new ValueBinding<string>(
                Group, "ParkplatzInfos", string.Empty));
            AddBinding(_runde = new ValueBinding<string>(
                Group, "ParkplatzRunde", string.Empty));

            AddBinding(_hatVorrunde = new ValueBinding<bool>(
                Group, "ParkplatzHatVorrunde", false));
            AddBinding(new TriggerBinding<string>(
                Group, "ParkplatzWaehlen", ParkplatzWaehlen));
            AddBinding(new TriggerBinding<string>(
                Group, "ParkplatzGebuehr", ParkplatzGebuehr));
            AddBinding(new TriggerBinding<string>(
                Group, "ParkplatzUmbenennen", ParkplatzUmbenennen));
            AddBinding(new TriggerBinding<string>(
                Group, "ParkplatzBearbeiten", ParkplatzBearbeiten));
            /*
             * VERWAISTE PARKPLAETZE. Der Schalter ist dieselbe Einstellung
             * wie im Optionsmenue; der Abgleich in die andere Richtung steht
             * in `PflegeWaisenSchalter`.
             */
            AddBinding(_waisenAuto = new ValueBinding<bool>(
                Group, "WaisenAuto",
                Mod.Optionen?.WaisenAutomatischReparieren ?? false));
            AddBinding(new TriggerBinding<bool>(Group, "SetWaisenAuto", wert =>
            {
                if (Mod.Optionen != null)
                {
                    Mod.Optionen.WaisenAutomatischReparieren = wert;
                    Mod.Optionen.ApplyAndSave();
                }
                _waisenAuto.Update(wert);
                if (wert) _waisen?.StarteAutomatik();
            }));
            AddBinding(new TriggerBinding<string>(
                Group, "ParkplatzReparieren", ParkplatzReparieren));
            AddBinding(new TriggerBinding(
                Group, "ParkplaetzeReparieren", ParkplaetzeReparieren));
            AddBinding(new TriggerBinding(
                Group, "ParkplatzRundeVor", RundeVor));
            AddBinding(new TriggerBinding(
                Group, "ParkplatzRundeZurueck", RundeZurueck));
        }

        [Preserve]
        protected override void OnUpdate()
        {
            using var uhr = ParkingLotMessung.Miss(
                ParkingLotMessung.Sys.Liste);
            base.OnUpdate();
            /*
             * EIN NEUER PARKPLATZ WARTET NICHT AUF DEN TAKT.
             *
             * Die Zahlen einmal je Sekunde nachzufuehren reicht - dass ein
             * gerade gebauter Platz aber bis zu einer Sekunde fehlt, sieht
             * nach einem kaputten Reiter aus. `CalculateEntityCount` ist ein
             * Zaehler ueber die Chunks der Abfrage und billig genug fuer
             * jedes Bild; nur bei einer Aenderung wird sofort neu gebaut.
             */
            PflegeWaisenSchalter();
            // Waisen zaehlen mit: wird eine repariert oder abgerissen, soll
            // die Liste das sofort zeigen, nicht erst im Takt.
            var bestand = _lotQuery.CalculateEntityCount()
                + (_waisen?.OffeneWaisen.Count ?? 0) * 100000
                + (_waisen?.OhneBauzettel.Count ?? 0) * 1000;
            if (bestand != _zuletztGezaehlt)
            {
                _zuletztGezaehlt = bestand;
                _frames = AktualisierungFrames;
            }
            if (++_frames < AktualisierungFrames) return;
            _frames = 0;

            SammleLots();
            SammleWaisen();
            if (_lots.Count == 0 && _waisenListe.Count == 0)
            {
                Setze(_liste, string.Empty);
                Setze(_infos, string.Empty);
                Setze(_runde, string.Empty);
                _rundeSteht = false;
                _zeigtVorrunde = false;
                _infosVorher = _rundeTextVorher = string.Empty;
                _hatVorrunde.Update(false);
                return;
            }

            SammleWerte();
            if (!_rundeSteht)
            {
                ZieheNeueRunde();
                SchreibeInfos();
            }
            // Belegung/Gebuehr bleiben live; Infos bleiben bis zum Rundenwechsel
            // stehen. Vorher wurde die historische Ansicht alle 60 Frames ersetzt.
            SchreibeListe();
        }

        private static void Setze(ValueBinding<string> bindung, string wert)
        {
            if (bindung.value != wert) bindung.Update(wert);
        }

        // ------------------------------------------------------------------
        // Bestand
        // ------------------------------------------------------------------

        private void SammleLots()
        {
            _lots.Clear();
            using var gefunden =
                _lotQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (var i = 0; i < gefunden.Length; i++) _lots.Add(gefunden[i]);

            // Stabile Reihenfolge: nach Name, damit die Liste nicht bei jedem
            // Bildaufbau springt. Der Entity-Index als Nachrang haelt zwei
            // gleichnamige Plaetze auseinander.
            _lots.Sort(delegate (Entity a, Entity b)
            {
                var na = NameVon(a);
                var nb = NameVon(b);
                var vergleich = string.CompareOrdinal(na, nb);
                return vergleich != 0 ? vergleich : a.Index.CompareTo(b.Index);
            });
        }

        private readonly List<Entity> _waisenListe = new List<Entity>();

        private void SammleWaisen()
        {
            _waisenListe.Clear();
            if (_waisen == null) return;
            foreach (var lot in _waisen.OffeneWaisen)
                if (EntityManager.Exists(lot)
                    && !EntityManager.HasComponent<Deleted>(lot)
                    && !EntityManager.HasComponent<ParkingLotCarrierReference>(lot))
                    _waisenListe.Add(lot);
        }

        /**
         * Waisen als Zeilen wie jeder andere Parkplatz - nur mit dem, was
         * noch bekannt ist: Name und Alter. Belegung, Gebuehr und Groesse
         * hingen an den verlorenen Daten und stehen als Null da; die
         * Oberflaeche legt ohnehin den Schleier darueber.
         */
        private void SchreibeWaisen()
        {
            foreach (var lot in _waisenListe)
            {
                if (_bau.Length > 0) _bau.Append('\n');
                _bau.Append(SchluesselVon(lot)).Append('\t')
                    .Append(Saeubere(NameVon(lot)))
                    .Append("\t0\t0\t0\t0\t0\t0\t0\t")
                    .Append(AlterInTagen(lot))
                    .Append("\t0\t0");
                // Leerer Mangelblock (7 Felder).
                _bau.Append("\t0\t0\t0\t0\t0\t\t");
                _bau.Append('\t').Append(_waisen.Zustand(lot)).Append("\t0");
            }
        }

        private void PflegeWaisenSchalter()
        {
            if (_waisenAuto == null || Mod.Optionen == null) return;
            var jetzt = Mod.Optionen.WaisenAutomatischReparieren;
            if (_waisenAuto.value != jetzt) _waisenAuto.Update(jetzt);
        }

        private void ParkplatzReparieren(string schluessel)
        {
            if (!VersucheSchluessel(schluessel, out var lot)) return;
            _waisen?.Reparieren(lot);
            _frames = AktualisierungFrames;
        }

        private void ParkplaetzeReparieren()
        {
            _waisen?.ReparierenAlle();
            _frames = AktualisierungFrames;
        }

        private string NameVon(Entity lot)
        {
            // Wird bei jeder Anzeige neu gebildet und folgt der Sprache
            // deshalb sofort - anders als der gespeicherte Name selbst.
            var rueckfall = ParkingLotTexte.T("Parkplatz", "Parking Lot");
            if (_namen == null) return rueckfall;
            var name = _namen.GetRenderedLabelName(lot);
            return string.IsNullOrEmpty(name) ? rueckfall : name;
        }

        // ------------------------------------------------------------------
        // Ziehung
        // ------------------------------------------------------------------

        /**
         * Die Runde gilt fuer ALLE Karten gemeinsam.
         *
         * Gezogen wird aus der Vereinigung dessen, was die Plaetze hergeben.
         * Ein Platz, der die gezogene Info nicht hat, zeigt „noch keine
         * Daten" - er bekommt KEINE Ersatzinfo. Sonst stuende auf Karte eins
         * die Belegung und auf Karte drei das Alter der Gaeste, und
         * Vergleichen waere unmoeglich; genau dafuer gibt es die Liste aber.
         */
        private void ZieheNeueRunde()
        {
            var vereinigung = new List<Infoart>();
            var gesehen = new HashSet<Infoart>();
            foreach (var paar in _werte)
            {
                var werte = paar.Value;
                var n = Infoauswahl.SammleVerfuegbare(werte, _angebot);
                for (var i = 0; i < n; i++)
                {
                    if (gesehen.Add(_angebot[i])) vereinigung.Add(_angebot[i]);
                }
            }

            if (_zufall.Naechste(2) == 0 && _rundennummer == 0)
            {
                // Erste Ziehung: Saat aus dem Spielstand, damit zwei Sitzungen
                // am selben Stand nicht dieselbe Reihenfolge zeigen.
                _zufall = new Infozufall(
                    (_simulation != null ? _simulation.frameIndex : 1u) | 1u);
            }

            var anzahl = Infokarten.ZieheRunde(
                vereinigung, _rundeVorher, ref _zufall, _rundeJetzt);

            _rundeVorher.Clear();
            for (var i = 0; i < anzahl; i++) _rundeVorher.Add(_rundeJetzt[i]);
            for (var i = anzahl; i < _rundeJetzt.Length; i++)
                _rundeJetzt[i] = Infoart.Keine;

            _rundennummer++;
            _rundeTextVorher = _rundeTextJetzt;
            _bau.Length = 0;
            _bau.Append(_rundennummer);
            for (var i = 0; i < _rundeJetzt.Length; i++)
                _bau.Append('\t').Append((int)_rundeJetzt[i]);
            _rundeTextJetzt = _bau.ToString();
            _rundeSteht = true;
        }

        /** Die UI hat alle Karten der Runde gezeigt und will die naechste. */
        private void RundeVor()
        {
            if (_zeigtVorrunde)
            {
                _zeigtVorrunde = false;
                Setze(_infos, _infosJetzt);
                Setze(_runde, _rundeTextJetzt);
                _hatVorrunde.Update(!string.IsNullOrEmpty(_infosVorher));
                return;
            }
            // Mehrere Klicks vor dem naechsten Update ziehen nur eine Runde.
            if (!_rundeSteht) return;
            _infosVorher = _infosJetzt;
            _rundeSteht = false;
            _frames = AktualisierungFrames;
        }

        private void RundeZurueck()
        {
            if (_zeigtVorrunde || !_rundeSteht || string.IsNullOrEmpty(_infosVorher)) return;
            _zeigtVorrunde = true;
            Setze(_infos, _infosVorher);
            Setze(_runde, _rundeTextVorher);
            _hatVorrunde.Update(false);
        }

        // ------------------------------------------------------------------
        // Ausgabe
        // ------------------------------------------------------------------

        private void SchreibeListe()
        {
            _bau.Length = 0;
            for (var i = 0; i < _lots.Count; i++)
            {
                var lot = _lots[i];
                if (!_werte.TryGetValue(lot, out var werte)) continue;
                // Frisch gebaut: die Wirtschaftsdaten kommen erst, wenn die
                // Parkspuren stehen. Bis dahin Nullen statt keiner Zeile.
                var hatWirtschaft = EntityManager
                    .HasComponent<ParkingLotEconomyData>(lot);
                var wirtschaft = hatWirtschaft
                    ? EntityManager.GetComponentData<ParkingLotEconomyData>(lot)
                    : default(ParkingLotEconomyData);

                MisstGroesse(lot, out var breite, out var tiefe);
                var ergebnis = MonatsErgebnis(lot, werte, wirtschaft,
                    out var geschaetzt);

                if (_bau.Length > 0) _bau.Append('\n');
                _bau.Append(SchluesselVon(lot)).Append('\t')
                    .Append(Saeubere(NameVon(lot))).Append('\t')
                    .Append(werte.Kapazitaet).Append('\t')
                    .Append(BelegtJetztVon(lot)).Append('\t')
                    .Append(GebuehrVon(wirtschaft)).Append('\t')
                    .Append(MonatlicherUnterhalt(wirtschaft)).Append('\t')
                    .Append(werte.Proben).Append('\t')
                    .Append(breite).Append('\t')
                    .Append(tiefe).Append('\t')
                    .Append(AlterInTagen(lot)).Append('\t')
                    .Append(ergebnis).Append('\t')
                    .Append(geschaetzt ? 1 : 0);

                SchreibeBlock(lot, Infoauswahl.WaehleMangel(werte), werte);
                // Feld 19: Waisenzustand, Feld 20: Bauzettel vorhanden.
                _bau.Append('\t').Append(_waisen?.Zustand(lot) ?? 0).Append('\t')
                    .Append(EntityManager.HasComponent<ParkingLotBuildReceipt>(lot)
                        ? 1 : 0);
            }
            SchreibeWaisen();
            Setze(_liste, _bau.ToString());
        }

        private void SchreibeInfos()
        {
            _bau.Length = 0;
            for (var i = 0; i < _lots.Count; i++)
            {
                var lot = _lots[i];
                if (!_werte.TryGetValue(lot, out var werte)) continue;

                if (_bau.Length > 0) _bau.Append('\n');
                _bau.Append(SchluesselVon(lot));

                var n = Infoauswahl.SammleVerfuegbare(werte, _angebot);
                for (var slot = 0; slot < _rundeJetzt.Length; slot++)
                {
                    var art = _rundeJetzt[slot];
                    // Hat dieser Platz die gezogene Info nicht, bleibt der
                    // Block leer - die Karte sagt dann „noch keine Daten".
                    if (!Enthalten(_angebot, n, art)) art = Infoart.Keine;
                    SchreibeBlock(lot, art, werte);
                }
            }
            _infosJetzt = _bau.ToString();
            Setze(_infos, _infosJetzt);
            Setze(_runde, _rundeTextJetzt);
            _hatVorrunde.Update(!string.IsNullOrEmpty(_infosVorher));
        }

        private static bool Enthalten(Infoart[] feld, int anzahl, Infoart art)
        {
            if (art == Infoart.Keine) return false;
            for (var i = 0; i < anzahl; i++) if (feld[i] == art) return true;
            return false;
        }

        /**
         * Ein Infoblock: Art, drei Zahlen, ein eingesetztes Wort, ein Ziel.
         *
         * Mehr Slots braucht keine Karte aus PLAN-Infokarten.md - und weniger
         * ginge nicht, ohne Saetze aus Textstuecken zusammenzusetzen. Genau
         * das verbietet Regel 4 des Plans, weil es sich nicht uebersetzen
         * laesst.
         */
        private void SchreibeBlock(Entity lot, Infoart art, Infowerte werte)
        {
            var block = BereiteBlock(lot, art, werte);
            _bau.Append('\t').Append((int)block.Art)
                .Append('\t').Append(block.Zahl1)
                .Append('\t').Append(block.Zahl2)
                .Append('\t').Append(block.Zahl3)
                .Append('\t').Append(block.Wort)
                .Append('\t').Append(block.ZielSchluessel)
                .Append('\t').Append(Saeubere(block.ZielName));
        }

        internal static string SchluesselVon(Entity entity)
            => entity.Index + ":" + entity.Version;

        private bool VersucheSchluessel(string schluessel, out Entity entity)
        {
            entity = Entity.Null;
            if (string.IsNullOrEmpty(schluessel)) return false;
            var teile = schluessel.Split(':');
            if (teile.Length != 2) return false;
            if (!int.TryParse(teile[0], out var index)) return false;
            if (!int.TryParse(teile[1], out var version)) return false;
            entity = new Entity { Index = index, Version = version };
            return EntityManager.Exists(entity);
        }

        /**
         * Tabulatoren und Zeilenumbrueche aus einem Namen entfernen.
         *
         * Der Nutzer darf seinen Parkplatz nennen, wie er will - aber ein
         * Tabulator im Namen wuerde die ganze Zeile verschieben, und der
         * Fehler saehe aus wie ein Fehler der Liste.
         */
        private static string Saeubere(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (text.IndexOf('\t') < 0 && text.IndexOf('\n') < 0
                && text.IndexOf('\r') < 0)
                return text;
            return text.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
        }

        private static int GebuehrVon(ParkingLotEconomyData wirtschaft)
            => wirtschaft.ParkingFee ==
               ParkingLotEconomyData.UninitializedParkingFee
                ? 0
                : UnityEngine.Mathf.Clamp(
                    wirtschaft.ParkingFee, 0, MaximaleGebuehr);

        /**
         * Was CS2 im Auswahlfenster als monatlichen Unterhalt zeigt.
         *
         * Das ist die HAELFTE des Prefabwerts - so rechnet CS2, und so steht
         * es auch in `ParkingLotEconomySystem`. Die Liste muss dieselbe Zahl
         * zeigen wie das Infofenster, sonst glaubt der Nutzer zu Recht keiner
         * von beiden.
         */
        private static int MonatlicherUnterhalt(ParkingLotEconomyData w)
            => w.Upkeep / 2;

        /**
         * Aussenmass des Parkplatzes in Metern.
         *
         * Aus den gespeicherten Umrisspunkten des Bauzettels. Ohne Bauzettel
         * bleibt es bei 0/0 und die Karte laesst die Zeile weg, statt eine
         * Groesse zu erfinden.
         */
        private void MisstGroesse(Entity lot, out int breite, out int tiefe)
        {
            breite = 0;
            tiefe = 0;
            if (!EntityManager.HasBuffer<ParkingLotBuildPoint>(lot)) return;
            var punkte = EntityManager
                .GetBuffer<ParkingLotBuildPoint>(lot, true);
            if (punkte.Length == 0) return;

            var minX = float.MaxValue;
            var maxX = float.MinValue;
            var minZ = float.MaxValue;
            var maxZ = float.MinValue;
            for (var i = 0; i < punkte.Length; i++)
            {
                var pos = punkte[i].Position;
                if (pos.x < minX) minX = pos.x;
                if (pos.x > maxX) maxX = pos.x;
                if (pos.z < minZ) minZ = pos.z;
                if (pos.z > maxZ) maxZ = pos.z;
            }
            breite = UnityEngine.Mathf.RoundToInt(maxX - minX);
            tiefe = UnityEngine.Mathf.RoundToInt(maxZ - minZ);
        }

        /** Tage seit dem Bau, oder -1 wenn das Baudatum unbekannt ist. */
        private int AlterInTagen(Entity lot)
        {
            if (!EntityManager.HasComponent<ParkingLotStatistik>(lot)) return -1;
            var s = EntityManager.GetComponentData<ParkingLotStatistik>(lot);
            if (s.GebautFrame == 0u) return -1;
            var jetzt = _simulation != null ? _simulation.frameIndex : 0u;
            if (jetzt <= s.GebautFrame) return 0;
            return (int)((jetzt - s.GebautFrame)
                / (uint)Game.Simulation.TimeSystem.kTicksPerDay);
        }

        /**
         * Was der Parkplatz im Monat kostet - und was er einbringt.
         *
         * DIE EINNAHME IST UNSERE ZAHL, NICHT DIE DES SPIELS. CS2 bucht
         * Parkgebuehren nirgends je Parkplatz; sie landen stadtweit in einem
         * Topf und wirken je Parkspur nur als Abschreckung. Was hier steht,
         * ist deshalb gerechnet: beobachtete Ankuenfte je Tag mal Gebuehr mal
         * Tage im Monat, minus Unterhalt.
         *
         * Und die Ankuenfte sind eher zu niedrig als zu hoch: wer zwischen
         * zwei Proben kommt UND wieder faehrt, wird nie gezaehlt. Deshalb
         * `geschaetzt` - die Oberflaeche kennzeichnet die Zahl entsprechend.
         *
         * Ohne genug Tage im Ring gibt es gar keine Schaetzung, sondern nur
         * die reinen Kosten. Die sind exakt.
         */
        private int MonatsErgebnis(Entity lot, Infowerte werte,
                                   ParkingLotEconomyData wirtschaft,
                                   out bool geschaetzt)
        {
            var kosten = MonatlicherUnterhalt(wirtschaft);
            geschaetzt = false;
            if (werte.TageImRing < Infoauswahl.TageFuerWoche) return -kosten;
            if (werte.AutosLetzteWoche <= 0) return -kosten;

            var gebuehr = GebuehrVon(wirtschaft);
            if (gebuehr <= 0) return -kosten;

            geschaetzt = true;
            var proTag = werte.AutosLetzteWoche / werte.TageImRing;
            return proTag * gebuehr * TageJeMonat() - kosten;
        }

        /**
         * Tage in einem Spielmonat.
         *
         * Nicht geraten: CS2 teilt sein Jahr in zwoelf Monate, und wie lang
         * ein Jahr ist, steht am `TimeSystem`. Ein fest verdrahtetes „30"
         * waere in einem Spielstand mit anderer Jahreslaenge einfach falsch.
         */
        private int TageJeMonat()
        {
            var proJahr = _zeit != null ? _zeit.daysPerYear : 12;
            return UnityEngine.Mathf.Max(1, proJahr / 12);
        }

        private int BelegtJetztVon(Entity lot)
            => EntityManager.HasComponent<ParkingLotStatistik>(lot)
                ? EntityManager.GetComponentData<ParkingLotStatistik>(lot)
                    .BelegtJetzt
                : 0;

        // ------------------------------------------------------------------
        // Ausloeser aus der UI
        // ------------------------------------------------------------------

        /**
         * Klick auf einen Parkplatz oder einen Ortsnamen in einer Info.
         *
         * Auswaehlen UND hinspringen. Nur auswaehlen hiesse, dass der Nutzer
         * etwas markiert, das gar nicht im Bild ist. `Focus` ist genau das,
         * was in CS2 das Fadenkreuz-Icon im Infofenster tut.
         */
        private void ParkplatzWaehlen(string schluessel)
        {
            if (!VersucheSchluessel(schluessel, out var entity)) return;
            if (EntityManager.HasComponent<Deleted>(entity)) return;
            _selectedInfo.SetSelection(entity);
            _selectedInfo.Focus(entity);
        }

        private void ParkplatzGebuehr(string nutzlast)
        {
            var teile = nutzlast.Split('\t');
            if (teile.Length != 2) return;
            if (!VersucheSchluessel(teile[0], out var lot)) return;
            if (!int.TryParse(teile[1], out var gebuehr)) return;
            if (!EntityManager.HasComponent<ParkingLotEconomyData>(lot)) return;

            gebuehr = UnityEngine.Mathf.Clamp(gebuehr, 0, MaximaleGebuehr);
            var wirtschaft = EntityManager
                .GetComponentData<ParkingLotEconomyData>(lot);
            if (wirtschaft.ParkingFee == gebuehr) return;
            wirtschaft.ParkingFee = gebuehr;
            EntityManager.SetComponentData(lot, wirtschaft);

            /*
             * DIESELBE STELLE WIE DER REGLER IM AUSWAHLFENSTER.
             *
             * Der Nutzer hat verlangt, dass alle Einstellungen synchron
             * laufen. Deshalb geht die Aenderung durch dieselbe Methode, die
             * auch `ParkingLotFeeUISystem` benutzt - sonst haette die Liste
             * einen zweiten, langsam abweichenden Weg auf die Parkspuren.
             */
            var geaendert = _comfort != null
                ? _comfort.SetParkingFeeImmediately(lot, gebuehr) : 0;
            Mod.log.Info("PLT-Liste: Gebuehr an Lot " + lot.Index + " auf "
                + gebuehr + "; " + geaendert + " Parkspuren sofort geaendert.");
            _frames = AktualisierungFrames;
        }

        /**
         * Bearbeiten - aber erst hinspringen.
         *
         * Ansage des Nutzers: „Beim Editieren/Bearbeiten muesste ebenfalls
         * direkt fokusiert werden bevor ueberhaupt bearbeitet wird." Deshalb
         * liegt beides in EINEM Ausloeser und nicht in zwei, die die UI
         * hintereinander abschickt: zwei getrennte Ausloeser koennten in
         * verschiedenen Bildern landen, und dann bearbeitet der Mod kurz den
         * vorher ausgewaehlten Parkplatz.
         */
        private void ParkplatzBearbeiten(string schluessel)
        {
            if (!VersucheSchluessel(schluessel, out var lot)) return;
            if (EntityManager.HasComponent<Deleted>(lot)) return;
            _selectedInfo.SetSelection(lot);
            _selectedInfo.Focus(lot);
            World.GetOrCreateSystemManaged<ParkingLotToolSystem>()
                .RequestEdit(lot);
        }

        private void ParkplatzUmbenennen(string nutzlast)
        {
            var trenner = nutzlast.IndexOf('\t');
            if (trenner <= 0) return;
            if (!VersucheSchluessel(nutzlast.Substring(0, trenner), out var lot))
                return;
            var name = nutzlast.Substring(trenner + 1).Trim();
            if (name.Length == 0) return;
            if (name.Length > 64) name = name.Substring(0, 64);

            // Derselbe Weg, den CS2 fuer jedes umbenennbare Objekt geht -
            // damit der neue Name auch im Auswahlfenster steht.
            _namen.SetCustomName(lot, name);
            _frames = AktualisierungFrames;
        }
    }
}
