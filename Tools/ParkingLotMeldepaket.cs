using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
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
                            Lege(archiv, pfad, Path.GetFileName(pfad));
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

        private static void Lege(ZipArchive archiv, string pfad, string name)
        {
            var eintrag = archiv.CreateEntry(name, System.IO.Compression.CompressionLevel.Optimal);
            using var quelle = new FileStream(pfad, FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite);
            using var hinein = eintrag.Open();
            quelle.CopyTo(hinein);
        }

        private static void LegeText(ZipArchive archiv, string name,
            string inhalt)
        {
            var eintrag = archiv.CreateEntry(name, System.IO.Compression.CompressionLevel.Optimal);
            using var hinein = new StreamWriter(eintrag.Open(),
                new UTF8Encoding(false));
            hinein.Write(inhalt);
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
                var dll = typeof(ParkingLotMeldepaket).Assembly.Location;
                text.AppendLine("Mod-DLL:        " + dll);
                if (File.Exists(dll))
                    text.AppendLine("DLL gebaut:     "
                        + File.GetLastWriteTime(dll)
                            .ToString("yyyy-MM-dd HH:mm:ss"));
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
            return text.ToString();
        }
    }
}
