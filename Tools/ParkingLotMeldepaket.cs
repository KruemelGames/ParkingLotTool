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
    internal static class ParkingLotMeldepaket
    {
        /** Soviel vom Ende des Modlogs kommt mit. */
        private const int LogzeilenAmEnde = 2000;

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

        /** Wo die Meldungen liegen - fuer den Ordner-Knopf im Panel. */
        internal static string Meldeordner => Path.Combine(
            Path.Combine(UnityEngine.Application.persistentDataPath, "Logs"),
            "ParkingLotTool-Logs");

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
        internal enum Anlass { Bau, Vorschau, Absturz }

        internal static string Schnuere(Anlass anlass, out string grund)
        {
            var nurVorschau = anlass == Anlass.Vorschau;
            grund = null;
            try
            {
                var ordner = Path.Combine(
                    Application.persistentDataPath, "Logs");
                if (!Directory.Exists(ordner))
                {
                    grund = "Der Logs-Ordner wurde nicht gefunden.";
                    return null;
                }

                var teile = new List<FileInfo>();
                if (!nurVorschau) Juengste(ordner, "debug", teile);
                Juengste(ordner, "vorbau", teile);
                Juengste(ordner, "bericht", teile);

                if (teile.Count == 0 && anlass != Anlass.Absturz)
                {
                    grund = nurVorschau
                        ? "Es gibt noch keinen Vorschau-Abzug. Zeichne einen "
                          + "Parkplatz und versuch es noch einmal."
                        : "Es gibt noch keinen Abzug zu diesem Parkplatz.";
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
                var sparte = anlass == Anlass.Absturz ? "Absturz"
                    : anlass == Anlass.Vorschau ? "Vorschau" : "Bau";
                var unterordner = Path.Combine(
                    Path.Combine(ordner, "ParkingLotTool-Logs"), sparte);
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

                    LegeText(archiv, "umgebung.txt", Umgebung());

                    /*
                     * DIE SCHRITTSPUR IN JEDES PAKET.
                     *
                     * Sie ist winzig und beantwortet die teuerste Frage:
                     * WO war die Mod, als es passierte. Bei einem Absturz ist
                     * sie das Einzige, was garantiert auf der Platte steht.
                     */
                    if (File.Exists(ParkingLotSchrittmarke.Pfad))
                        Lege(archiv, ParkingLotSchrittmarke.Pfad,
                            "schritte.log");

                    var log = Path.Combine(ordner, "ParkingLotTool.Mod.log");
                    if (File.Exists(log))
                        LegeText(archiv, "modlog-ende.txt", Logende(log));

                    /*
                     * BEIM ABSTURZ KOMMEN DIE LOGS DES VORIGEN LAUFS DAZU.
                     *
                     * Die Absturzmeldung selbst steht in `Player-prev.log`
                     * und sonst nirgends - bei dieser Absturzsorte gibt CS2
                     * keinen managed Stacktrace aus. Ohne diese Datei ist der
                     * Bericht wertlos.
                     */
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
                                LegeText(archiv, name, Auszug(pfad));
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
                grund = "Das Paket konnte nicht geschnuert werden: "
                    + ausnahme.Message;
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
            LegeText(archiv, name, Anonymisiere(LiesBegrenzt(pfad)));
        }

        private static void LegeText(ZipArchive archiv, string name,
            string inhalt)
        {
            var eintrag = archiv.CreateEntry(name, System.IO.Compression.CompressionLevel.Optimal);
            using var hinein = new StreamWriter(eintrag.Open(),
                new UTF8Encoding(false));
            hinein.Write(Anonymisiere(inhalt));
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

        /**
         * HOLT AUS `Player.log`, WAS GEBRAUCHT WIRD - UND SONST NICHTS.
         *
         * Der Nutzer am 2026-09-15, nachdem der erste Anlauf die ganze Datei
         * eingepackt und hinterher gewischt hatte: *"Ehm ernsthaft warum wird
         * das ueberhaupt abgegriffen? Du behebst wieder Symptome anstatt die
         * Wurzel."*
         *
         * Er hat recht. Die Datei gehoert dem Spiel, sie ist voller Pfade,
         * Kennungen und Ladegeraeusch - und gebraucht wird daraus dreierlei:
         *
         *   - der Kopf: Spielversion, Betriebssystem, CPU, Grafikkarte,
         *     Speicher. Fuer "laeuft das nur bei ihm so?"
         *   - der Absturzblock samt Stacktrace. Bei dieser Absturzsorte gibt
         *     CS2 KEINEN managed Stacktrace aus, deshalb ist die native
         *     Meldung das Einzige, was es gibt.
         *   - die Zeilen unmittelbar DAVOR. Genau die haben am 2026-09-14 den
         *     Absturz erklaert ("UpdateFrame added to unsupported type").
         *
         * Eine Modliste steht hier uebrigens NICHT drin - nachgesehen. Die
         * holen wir beim Spiel selbst, siehe Umgebung.
         *
         * Was nicht auf die Liste passt, kommt gar nicht erst ins Paket. Das
         * ist der Unterschied zum Wischen: was nie eingesammelt wird, kann
         * auch nicht durchrutschen.
         */
        private const int ZeilenVorDemAbsturz = 200;

        private static string Auszug(string pfad)
        {
            string[] zeilen;
            try { zeilen = File.ReadAllLines(pfad); }
            catch (Exception ausnahme)
            {
                return "Diese Datei war nicht lesbar: " + ausnahme.Message;
            }

            var kopfmerkmale = new[]
            {
                "Initialize engine version", "Game version:", "Type:", "OS:",
                "System memory:", "Graphics device:", "Graphics memory:",
                "CPU:", "Core count:", "Screen resolution:",
                "Rendering Threading Mode:", "Modding runtime:",
                "Scripting runtime:", "Shader level:",
            };

            var text = new StringBuilder();
            text.AppendLine("Auszug aus " + Path.GetFileName(pfad)
                + " - nur Systemangaben und der Absturz.");
            text.AppendLine("Pfade, Kennungen und Ladeprotokoll sind nicht "
                + "enthalten; sie werden fuer die Fehlersuche nicht "
                + "gebraucht.");
            text.AppendLine(new string('-', 70));

            foreach (var zeile in zeilen)
                foreach (var merkmal in kopfmerkmale)
                    if (zeile.StartsWith(merkmal, StringComparison.Ordinal))
                    { text.AppendLine(zeile); break; }

            var absturz = -1;
            for (var i = 0; i < zeilen.Length; i++)
                if (zeilen[i].Contains("Native Crash Reporting")
                    || zeilen[i].Contains("Crash!!!"))
                { absturz = i; break; }

            text.AppendLine();
            if (absturz < 0)
            {
                text.AppendLine("KEIN Absturzblock in dieser Datei. Es folgen "
                    + "die letzten Zeilen:");
                text.AppendLine(new string('-', 70));
                for (var i = Math.Max(0, zeilen.Length - ZeilenVorDemAbsturz);
                     i < zeilen.Length; i++)
                    text.AppendLine(zeilen[i]);
                return text.ToString();
            }

            text.AppendLine("Absturz ab Zeile " + (absturz + 1) + "; davor "
                + ZeilenVorDemAbsturz + " Zeilen Vorlauf:");
            text.AppendLine(new string('-', 70));
            for (var i = Math.Max(0, absturz - ZeilenVorDemAbsturz);
                 i < zeilen.Length; i++)
                text.AppendLine(zeilen[i]);
            return text.ToString();
        }

        /**
         * NIMMT DIE PERSOENLICHEN ANGABEN AUS DEM PAKET.
         *
         * Ansage des Nutzers am 2026-09-15, nachdem der Hinweistext im Issue
         * sie noch aufgezaehlt hatte: *"Windows User name geht gar nicht
         * finde ich. Sehr persoenlich. Besonders weil meiner Meinung nach
         * brauchen wir die Infos nicht!"*
         *
         * Er hat recht, und das ist nachgemessen und nicht geschaetzt: an
         * der Absturzsuche vom 2026-09-14 waren das Modlog, die Schrittspur,
         * der Absturzblock aus `Player.log`, die Modliste und die
         * Spielversion beteiligt. Benutzername und Steam-ID kein einziges
         * Mal. Sie tragen nichts bei und gehoeren damit nicht in ein Paket,
         * das oeffentlich in einem GitHub-Issue landet.
         *
         * GESCHRIEBEN HAT SIE NICHT DIESE MOD, sondern CS2 beziehungsweise
         * Unity:
         *
         *     Logs at C:/Users/<nutzer>/AppData/LocalLow/...
         *     SteamInternal_SetMinidumpSteamID: Caching Steam ID: 7656119...
         *
         * ES REICHT NICHT, NUR `Player.log` ANZUFASSEN. Der Benutzername
         * steckt auch in UNSEREN Dateien: das Startlog schreibt DLL- und
         * Asset-Pfad, und der Bauzettel fuehrt einen ganzen Abschnitt
         * `Framework` mit `OutputPath`, `LatestPath`, `DllPath`. Deshalb
         * laeuft JEDE Datei des Archivs hier durch - auch die, die wir
         * selbst erzeugen.
         *
         * WAS BLEIBT: die Namen der Spielstaende. Ein Stadtname ist keine
         * persoenliche Angabe, und er sagt gelegentlich etwas aus - etwa,
         * dass jemand auf einer Mod-Karte gebaut hat.
         *
         * Die Ersetzung ist bewusst stumpf und damit verlaesslich: der
         * Benutzername ist eine bekannte Zeichenkette, keine Vermutung.
         */
        internal static string Anonymisiere(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            /*
             * Steam64-Kennungen beginnen alle mit 7656119 und sind 17
             * Stellen lang. Eng genug, dass keine Messzahl aus unseren
             * eigenen Logs versehentlich getroffen wird.
             */
            text = System.Text.RegularExpressions.Regex.Replace(
                text, @"\b7656119\d{10}\b", "<steamid>");

            /*
             * Ein sehr kurzer Benutzername koennte als Silbe mitten in
             * fremden Woertern stehen. Ab drei Zeichen ist das Risiko klein
             * und der Gewinn gross; darunter wird lieber nichts ersetzt als
             * das halbe Log zerschossen.
             */
            var nutzer = Environment.UserName;
            if (!string.IsNullOrEmpty(nutzer) && nutzer.Length >= 3)
                text = text.Replace(nutzer, "<nutzer>");

            return text;
        }

        /**
         * Der Schluss des Modlogs.
         *
         * `FileShare.ReadWrite`, weil CS2 dieselbe Datei offen haelt -
         * ohne das schlaegt das Oeffnen fehl, und zwar genau dann, wenn man
         * die Datei braucht.
         */
        private static string Logende(string pfad)
        {
            using var strom = new FileStream(pfad, FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite);
            using var leser = new StreamReader(strom);

            /*
             * EIGENER RING STATT `Queue<T>`.
             *
             * Seit dem Verweis auf `System.IO.Compression` liegt `Queue<T>`
             * in ZWEI Assemblies, und der Uebersetzer weigert sich zu Recht.
             * Ein Feld mit Schreibzeiger ist hier ohnehin genauer: es steht
             * sofort da, dass nur die letzten Zeilen gehalten werden.
             */
            var ring = new string[LogzeilenAmEnde];
            var naechste = 0;
            var gesamt = 0;
            string zeile;
            while ((zeile = leser.ReadLine()) != null)
            {
                ring[naechste] = zeile;
                naechste = (naechste + 1) % LogzeilenAmEnde;
                gesamt++;
            }

            var gehalten = Math.Min(gesamt, LogzeilenAmEnde);
            var zeilen = new string[gehalten];
            var anfang = gesamt <= LogzeilenAmEnde ? 0 : naechste;
            for (var i = 0; i < gehalten; i++)
                zeilen[i] = ring[(anfang + i) % LogzeilenAmEnde];

            var kopf = $"Die letzten {gehalten} von {gesamt} Zeilen aus "
                + "ParkingLotTool.Mod.log." + Environment.NewLine
                + new string('-', 70) + Environment.NewLine;
            return kopf + string.Join(Environment.NewLine, zeilen);
        }

        private static string Umgebung()
        {
            var text = new StringBuilder();
            text.AppendLine("Parking Lot Tool - Umgebung der Meldung");
            text.AppendLine(new string('-', 70));
            // Dieselbe Kennung wie im Dateinamen: damit ein Archiv, das
            // umbenannt weitergereicht wurde, sich selbst noch zuordnen kann.
            text.AppendLine("Kennung:        " + _kennung);
            text.AppendLine("Zeitpunkt:      "
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
                var dll = typeof(ParkingLotMeldepaket).Assembly.Location;
                if (!string.IsNullOrEmpty(dll))
                {
                    text.AppendLine("Mod-DLL:        " + Path.GetFileName(dll));
                    if (File.Exists(dll))
                        text.AppendLine("DLL gebaut:     "
                            + File.GetLastWriteTime(dll)
                                .ToString("yyyy-MM-dd HH:mm:ss"));
                }
                text.AppendLine("Mod-Fassung:    "
                    + typeof(ParkingLotMeldepaket).Assembly
                        .GetName().Version);
            }
            catch (Exception ausnahme)
            {
                text.AppendLine("Mod-Fassung:    nicht lesbar ("
                    + ausnahme.Message + ")");
            }
            text.AppendLine("Spielversion:   " + Application.version);
            text.AppendLine("Unity:          " + Application.unityVersion);
            text.AppendLine("Betriebssystem: " + SystemInfo.operatingSystem);

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
                text.AppendLine("Geladene Mods (" + namen.Count + "):");
                foreach (var name in namen)
                    text.AppendLine("  " + name);
            }
            catch (Exception ausnahme)
            {
                text.AppendLine("Geladene Mods: nicht lesbar ("
                    + ausnahme.Message + ")");
            }
            return text.ToString();
        }
    }
}
