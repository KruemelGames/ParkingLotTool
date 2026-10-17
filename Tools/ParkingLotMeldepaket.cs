using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Game.SceneFlow;
using UnityEngine;

namespace ParkingLotTool.Tools
{
    /**
     * PACKT ALLES, WAS ZU EINER MELDUNG GEHOERT, IN EINE DATEI.
     *
     * Ansage des Nutzers am 2026-09-14, vor der Testveroeffentlichung: *"Wenn
     * denen was auffaellt, sollen die einfach auf nen Parkplatz klicken
     * koennen und nen Button druecken, um nen Debug zu erstellen."*
     *
     * WARUM EIN ARCHIV UND NICHT DREI DATEIEN. Ein Abzug besteht heute aus
     * `debug-*.json`, `vorbau-*.json` und `bericht-*.txt`, und das Modlog
     * liegt daneben. Wer das melden soll, muss vier Dateien in einem Ordner
     * mit Hunderten finden. Bei mir selbst lagen dort am selben Tag 1535
     * Stueck. Ein Tester macht das ein Mal und dann nicht mehr.
     *
     * Also sammelt diese Stelle die Teile ein, die zusammengehoeren, und legt
     * EINE Datei an, deren Pfad der Nutzer kopieren kann.
     *
     * WAS HINEINGEHOERT, und warum jedes Stueck:
     *
     *   - der Bauzettel-Abzug: was gebaut wurde und womit.
     *   - der Vorbau-Abzug: was die Vorschau gerechnet hat. Bei Meldungen
     *     ueber die VORSCHAU ist er das einzige, was etwas sagt.
     *   - der Textbericht: dieselben Zahlen ohne JSON, zum Ueberfliegen.
     *   - der SCHLUSS des Modlogs: ein Absturz oder eine Warnung steht dort
     *     und in keinem Abzug. Ohne ihn hat schon mehrfach der halbe Befund
     *     gefehlt. Der Anfang der Datei bleibt draussen - der ist Ladekram.
     *   - eine `umgebung.txt`: Mod-Fassung, Bauzeit der DLL, Spielversion,
     *     Zeitpunkt. Die Frage "welche Fassung hattest du denn?" kostet sonst
     *     jedes Mal eine Runde.
     *
     * WAS NICHT HINEINGEHOERT: der Spielstand. Er ist gross, er gehoert dem
     * Nutzer, und bisher hat ihn kein einziger Befund gebraucht.
     */
    internal static partial class ParkingLotMeldepaket
    {
        /** Die Kennung des Pakets, das gerade geschnuert wird. */
        private static string _kennung = string.Empty;

        /**
         * Eine kurze, sprechbare Kennung.
         *
         * Vier Zeichen aus Ziffern und Grossbuchstaben - genug, um zwei
         * Meldungen desselben Nutzers zu unterscheiden, kurz genug, um sie
         * im Chat zu nennen. Aehnlich aussehende Zeichen sind draussen:
         * 0/O und 1/I verwechselt man beim Abtippen.
         */
        private static string Kennung()
        {
            const string zeichen = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
            // Voll ausgeschrieben: `Random` gibt es auch in UnityEngine.
            var zufall = new System.Random();
            var bau = new char[4];
            for (var i = 0; i < bau.Length; i++)
                bau[i] = zeichen[zufall.Next(zeichen.Length)];
            return new string(bau);
        }

        /**
         * DER PUNKT VOR DEM NAMEN HAELT CS2 HERAUS (2026-10-05).
         *
         * CS2 durchsucht beim Start den ganzen Logs-Ordner nach Assets und
         * oeffnet dabei jede ZIP als Asset-Paket. Unsere Meldepakete sind
         * gepackt (Deflated); CS2 schrieb fuer jede Datei darin eine Warnung
         * "uses compression method Deflated that is not supported" ins
         * FileSystem.log - gemessen 111 Zeilen. Ordner, deren Name mit `.`
         * oder `~` beginnt, ueberspringt `FileSystemDataSource` (im
         * Dekompilat belegt, dieselbe Regel wie beim Deaktivieren von Mods).
         * Der alte Ordner `ParkingLotTool-Logs` wird beim Start umgezogen,
         * siehe `ParkingLotLogpflege.ZieheMeldungenUm`.
         */
        internal const string Wurzelname = ".ParkingLotTool-Logs";
        internal const string AlterWurzelname = "ParkingLotTool-Logs";

        /** Wo die Meldungen liegen - fuer den Ordner-Knopf im Panel. */
        internal static string Meldeordner => Path.Combine(
            Path.Combine(UnityEngine.Application.persistentDataPath, "Logs"),
            Wurzelname);

        /**
         * Schnuert das Paket. Gibt den Pfad zurueck, oder `null` samt Grund.
         *
         * `nurVorschau` laesst den Bauzettel-Abzug weg: wer sich ueber die
         * VORSCHAU wundert, hat oft gar nicht gebaut, und ein Abzug vom
         * letzten Bau vor zwei Stunden fuehrt dann in die Irre.
         */
        /**
         * Wofuer das Paket geschnuert wird.
         *
         * `Absturz` ist der Sonderfall: da gibt es vielleicht gar keinen
         * frischen Abzug, dafuer aber die Logs des VORIGEN Laufs - und die
         * sind dort das Wichtigste. Deshalb darf dieser Anlass auch dann ein
         * Paket abliefern, wenn kein einziger Abzug herumliegt.
         */
        internal enum Anlass { Bau, Vorschau, Absturz, Leistung, Sync }

        internal static string Schnuere(Anlass anlass, out string grund,
            IReadOnlyList<string> frischeAbzuege = null,
            string zusatzName = null, string zusatzText = null)
        {
            var nurVorschau = anlass == Anlass.Vorschau;
            grund = null;
            try
            {
                var ordner = Path.Combine(
                    Application.persistentDataPath, "Logs");
                if (!Directory.Exists(ordner))
                {
                    grund = ParkingLotTexte.T("meldepaket.theGameSLogsFolderWas");
                    return null;
                }

                var teile = new List<FileInfo>();
                /*
                 * DIE LEISTUNGSMESSUNG BRAUCHT KEINEN BAUABZUG.
                 *
                 * Sie misst den laufenden Betrieb, nicht einen Parkplatz.
                 * Wer eine Minute lang misst, ohne je gebaut zu haben,
                 * bekaeme sonst "Es gibt noch keinen Abzug" statt seiner
                 * Messung - und das ist genau der Fall, den wir sehen
                 * wollen.
                 */
                if (nurVorschau && frischeAbzuege != null)
                {
                    // Nur, was die Meldung gerade selbst geschrieben hat - ein
                    // aelterer Bauabzug beschriebe einen anderen Stand.
                    foreach (var pfad in frischeAbzuege)
                        if (File.Exists(pfad)) teile.Add(new FileInfo(pfad));
                }
                // Der Sync-Bericht beschreibt den Sync, keinen einzelnen Bau.
                else if (anlass != Anlass.Leistung && anlass != Anlass.Sync)
                {
                    if (!nurVorschau) Juengste(ordner, "debug", teile);
                    Juengste(ordner, "prebuild", teile);
                    Juengste(ordner, "summary", teile);
                }

                if (teile.Count == 0 && anlass != Anlass.Absturz
                    && anlass != Anlass.Leistung && anlass != Anlass.Sync)
                {
                    grund = nurVorschau
                        ? ParkingLotTexte.T("meldepaket.thereIsNoPreviewToReport")
                        : ParkingLotTexte.T("meldepaket.thereIsNoRecordOfThis");
                    return null;
                }

                /*
                 * EIGENER ORDNER, UND JE ANLASS EIN UNTERORDNER.
                 *
                 * Ansage des Nutzers am 2026-09-14: *"Um fuer Klarheit zu
                 * sorgen, sollten wir wenigstens einen eigenen Ordner haben
                 * und am besten fuer jedes eigene Problem einen in dem."*
                 *
                 * Vorher landeten die Archive mitten im Logs-Ordner des
                 * Spiels - dort, wo auch CS2 und jede andere Mod schreiben.
                 * Auf der Entwicklungsmaschine lagen da 1535 Dateien. Wer
                 * eine Meldung verschicken soll, sucht dann.
                 *
                 * DER NAME TRAEGT EINE KENNUNG. `Bau_A7F3_2026-09-14_20-46-03`
                 * laesst sich in einem Chat nennen ("mein Log A7F3"), ohne den
                 * ganzen Pfad zu tippen - und die Kennung steht auch in der
                 * Umgebungsdatei im Archiv, damit beides zusammenfindet.
                 * Datum in der Reihenfolge Jahr-Monat-Tag, weil sich das von
                 * selbst sortiert.
                 */
                /*
                 * DIE NAMEN IM ARCHIV SIND ENGLISCH, DER CODE BLEIBT DEUTSCH.
                 *
                 * Ein Meldepaket verlaesst diesen Rechner: es geht an uns,
                 * oft ueber einen Spieler, der kein Deutsch kann, und
                 * gelegentlich an einen anderen Modder. Ordner wie "Absturz"
                 * und Dateien wie "umgebung.txt" sind dort eine Huerde.
                 * Ansage des Nutzers am 2026-09-22: *"wir muessen nochmal
                 * schauen dass die erstellten Zips und ordner auf englisch
                 * sind bei ALLEN reports."*
                 *
                 * Das gilt nur fuer das, was nach aussen geht. Der Quelltext
                 * und die Logzeilen bleiben, wie sie sind.
                 */
                var sparte = anlass == Anlass.Absturz ? "Crash"
                    : anlass == Anlass.Leistung ? "Performance"
                    : anlass == Anlass.Vorschau ? "Preview"
                    : anlass == Anlass.Sync ? "Sync" : "Build";
                var unterordner = Path.Combine(
                    Path.Combine(ordner, Wurzelname), sparte);
                Directory.CreateDirectory(unterordner);

                _kennung = Kennung();
                var ziel = Path.Combine(unterordner,
                    sparte + "_" + _kennung + "_"
                    + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".zip");

                using (var strom = new FileStream(ziel, FileMode.Create))
                using (var archiv = new ZipArchive(strom, ZipArchiveMode.Create))
                {
                    foreach (var datei in teile)
                        Lege(archiv, datei.FullName, datei.Name);

                    LegeText(archiv, "environment.txt", Umgebung());
                    if (zusatzName != null && zusatzText != null)
                        LegeText(archiv, zusatzName, zusatzText);

                    /*
                     * Die gesammelten Messzeilen. Sie stehen zwar auch im
                     * Modlog, aber dort zwischen zehntausend anderen - hier
                     * ist es genau die gemessene Minute.
                     */
                    if (anlass == Anlass.Leistung)
                        LegeText(archiv, "performance.txt",
                            ParkingLotMessung.Ernte());

                    /*
                     * DIE SCHRITTSPUR IN JEDES PAKET.
                     *
                     * Sie ist winzig und beantwortet die teuerste Frage:
                     * WO war die Mod, als es passierte. Bei einem Absturz ist
                     * sie das Einzige, was garantiert auf der Platte steht.
                     */
                    if (File.Exists(ParkingLotSchrittmarke.Pfad))
                        Lege(archiv, ParkingLotSchrittmarke.Pfad,
                            "steps.log");

                    var log = Path.Combine(ordner, "ParkingLotTool.Mod.log");
                    /*
                     * BEIM SYNC NUR DIE ZEILEN, DIE ZUM SYNC GEHOEREN
                     * (Nutzer: Berichte klein halten). Das ganze Logende war
                     * in Issue #16 3,4 MB; gebraucht wurden davon die Zeilen
                     * von Sync, Hintergrund und Laden sowie jede Warnung.
                     */
                    if (File.Exists(log))
                        LegeText(archiv, anlass == Anlass.Sync ? "modlog-sync.txt" : "modlog-tail.txt",
                            anlass == Anlass.Sync ? Syncauszug(log) : Logende(log), logauszug: true);

                    /*
                     * BEIM ABSTURZ KOMMEN DIE LOGS DES VORIGEN LAUFS DAZU.
                     *
                     * Die Absturzmeldung selbst steht in `Player-prev.log`
                     * und sonst nirgends - bei dieser Absturzsorte gibt CS2
                     * keinen managed Stacktrace aus. Ohne diese Datei ist der
                     * Bericht wertlos.
                     */
                    /*
                     * DIE JUENGSTEN ABSTURZABBILDER, AUSGEWERTET (2026-10-04).
                     * Auch dann, wenn die Wache beim Start keinen Absturz
                     * erkannt hat - "Report last crash" soll immer den
                     * letzten Absturz zeigen. Nur Text, nie das Abbild.
                     */
                    if (anlass == Anlass.Absturz)
                        LegeText(archiv, "crash-dumps.txt", ParkingLotAbsturzabbild.Bericht(
                            ParkingLotAbsturzabbild.Juengste(3)));

                    if (anlass == Anlass.Absturz)
                        foreach (var pfad in
                                 ParkingLotAbsturzwache.Absturzdateien())
                        {
                            var name = Path.GetFileName(pfad);
                            // `Player.log` gehoert dem Spiel und steht voller
                            // Dinge, die niemanden etwas angehen. Statt sie
                            // einzupacken und hinterher zu wischen, holen wir
                            // uns heraus, was gebraucht wird. Siehe Auszug.
                            if (name.StartsWith("Player",
                                    StringComparison.OrdinalIgnoreCase))
                                LegeText(archiv, name, Auszug(pfad), logauszug: true);
                            else
                                Lege(archiv, pfad, name);
                        }
                }

                Mod.log.Info($"PLT-Meldepaket: {ziel} geschnuert, "
                    + $"{teile.Count} Abzug/Abzuege plus Umgebung und "
                    + "Logende.");
                return ziel;
            }
            catch (Exception ausnahme)
            {
                // Der Ausnahmetext bleibt im Log; der Spieler braucht nur,
                // DASS es scheiterte und was er tun kann.
                grund = ParkingLotTexte.T("meldepaket.theReportCouldNotBeCreated");
                Mod.log.Warn("PLT-Meldepaket fehlgeschlagen: " + ausnahme);
                return null;
            }
        }

        /**
         * Die juengste Datei dieser Sorte.
         *
         * Nicht die `-latest`-Fassung: die zeigt zwar auf denselben Inhalt,
         * traegt aber keinen Zeitstempel im Namen. Im Paket soll man sehen,
         * WANN der Abzug entstanden ist.
         */
        private static void Juengste(string ordner, string sorte,
            List<FileInfo> ziel)
        {
            var treffer = Directory
                .GetFiles(ordner, "ParkingLotTool-" + sorte + "-*")
                .Where(pfad => !pfad.Contains("-latest"))
                .Select(pfad => new FileInfo(pfad))
                .OrderByDescending(datei => datei.LastWriteTimeUtc)
                .FirstOrDefault();
            if (treffer != null) ziel.Add(treffer);
        }

        /**
         * OBERGRENZE FUERS EINLESEN.
         *
         * Zum Anonymisieren muss die Datei durch den Speicher. `Player.log`
         * ist gewoehnlich ein paar Kilobyte, kann bei einer langen Sitzung
         * mit gespraechigen Mods aber deutlich wachsen. Bei mehr als dieser
         * Grenze wird nur das ENDE genommen - dort steht der Absturz.
         */
        private const long LesegrenzeBytes = 16L * 1024 * 1024;
        private const int SchwanzBytes = 2 * 1024 * 1024;

        private static void Lege(ZipArchive archiv, string pfad, string name)
        {
            // Text statt Bytes, weil zwischen Lesen und Schreiben die
            // persoenlichen Angaben herausfallen. Siehe Anonymisiere.
            var logauszug = string.Equals(Path.GetExtension(pfad), ".log", StringComparison.OrdinalIgnoreCase);
            var text = logauszug
                ? ParkingLotLogauszug.Lesen(pfad, LogzeilenAmEnde, "Logauszug: " + Path.GetFileName(pfad))
                : LiesBegrenzt(pfad);
            LegeText(archiv, name, text, logauszug);
        }

        /** Liest eine Datei, bei Ueberlaenge nur deren Ende. */
        private static string LiesBegrenzt(string pfad)
        {
            using var quelle = new FileStream(pfad, FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite);
            if (quelle.Length > LesegrenzeBytes)
            {
                quelle.Seek(-SchwanzBytes, SeekOrigin.End);
                using var kurz = new StreamReader(quelle);
                return "[... gekuerzt, nur das Ende dieser Datei ...]\n"
                    + kurz.ReadToEnd();
            }
            using var leser = new StreamReader(quelle);
            return leser.ReadToEnd();
        }

        private static string Umgebung()
        {
            var text = new StringBuilder();
            text.AppendLine("Parking Lot Tool - report environment");
            text.AppendLine(new string('-', 70));
            // Dieselbe Kennung wie im Dateinamen: damit ein Archiv, das
            // umbenannt weitergereicht wurde, sich selbst noch zuordnen kann.
            text.AppendLine("Report ID:      " + _kennung);
            text.AppendLine("Time:           "
                + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            try
            {
                /*
                 * NUR DER DATEINAME, NICHT DER PFAD.
                 *
                 * Der volle Pfad faengt mit C:\Users\<Name> an - und
                 * gebraucht wird davon nichts. Was zaehlt, ist WELCHE
                 * Fassung laeuft, und das sagen Bauzeit und Versionsnummer
                 * darunter.
                 */
                /*
                 * NICHT `Assembly.Location` ALLEIN - DER IST OFT LEER.
                 *
                 * Im Testerabzug VZ4A vom 2026-09-23 fehlten beide Zeilen
                 * ersatzlos, waehrend "Mod version" danebenstand. Der
                 * try-Block lief also; `Location` war leer, und die
                 * Bedingung hat beide Zeilen wortlos verschluckt.
                 *
                 * Das trifft ausgerechnet die Leute, von denen man die
                 * Angabe braucht: bei einem aus der Asset-Datenbank
                 * geladenen Mod steht der Pfad nicht in `Location`, sondern
                 * am Asset. `Mod.Bauzeit()` nimmt zuerst diesen und faellt
                 * erst danach auf `Location` zurueck.
                 *
                 * Und wenn wirklich nichts zu holen ist, steht das jetzt DA.
                 * Eine fehlende Zeile liest sich wie "nicht eingebaut" - der
                 * Nutzer hat am 2026-09-23 genau das vermutet.
                 */
                var dll = !string.IsNullOrEmpty(Mod.AssetPath)
                    ? Mod.AssetPath
                    : typeof(ParkingLotMeldepaket).Assembly.Location;
                text.AppendLine("Mod DLL:        " + (string.IsNullOrEmpty(dll)
                    ? "path unknown" : Path.GetFileName(dll)));
                var gebaut = Mod.Bauzeit();
                text.AppendLine("DLL built:      " + (gebaut.HasValue
                    ? gebaut.Value.ToLocalTime()
                        .ToString("yyyy-MM-dd HH:mm:ss")
                    : "unknown (file not readable)"));
                text.AppendLine("Mod version:    "
                    + typeof(ParkingLotMeldepaket).Assembly
                        .GetName().Version);
            }
            catch (Exception ausnahme)
            {
                text.AppendLine("Mod version:    unreadable ("
                    + ausnahme.Message + ")");
            }
            text.AppendLine("Game version:   " + Application.version);
            text.AppendLine("Unity:          " + Application.unityVersion);
            text.AppendLine("OS:             " + SystemInfo.operatingSystem);

            /*
             * DIE MODLISTE GEHOERT HIERHER, NICHT IN EINEN LOGAUSZUG.
             *
             * In `Player.log` steht sie gar nicht - nachgesehen am
             * 2026-09-15. Und sie ist bei fremden Meldungen die zweitwichtigste
             * Angabe nach dem Absturz selbst: das meiste seltsame Verhalten in
             * CS2 kommt daher, dass zwei Mods dasselbe anfassen.
             *
             * Nur Namen, keine Pfade.
             */
            try
            {
                var namen = new List<string>();
                foreach (var eintrag in GameManager.instance.modManager)
                    if (eintrag?.asset != null && eintrag.isLoaded)
                        namen.Add(eintrag.asset.name);
                namen.Sort(StringComparer.OrdinalIgnoreCase);
                text.AppendLine();
                text.AppendLine("Loaded mods (" + namen.Count + "):");
                foreach (var name in namen)
                    text.AppendLine("  " + name);
            }
            catch (Exception ausnahme)
            {
                text.AppendLine("Loaded mods: unreadable ("
                    + ausnahme.Message + ")");
            }
            return text.ToString();
        }
    }
}
