using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ParkingLotTool.Tools
{
    internal static partial class ParkingLotMeldepaket
    {
        /** Soviel vom Ende des Modlogs kommt mit. */
        private const int LogzeilenAmEnde = 2000;

        private static void LegeText(ZipArchive archiv, string name,
            string inhalt, bool logauszug = false)
        {
            inhalt = Anonymisiere(inhalt);
            // Ersetzungen koennen Text verlaengern; die Archivdatei selbst bleibt begrenzt.
            if (logauszug) inhalt = ParkingLotLogauszug.Begrenzen(inhalt);
            var eintrag = archiv.CreateEntry(name, System.IO.Compression.CompressionLevel.Optimal);
            using var hinein = new StreamWriter(eintrag.Open(),
                new UTF8Encoding(false));
            hinein.Write(inhalt);
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
            var kopfmerkmale = new[]
            {
                "Initialize engine version", "Game version:", "Type:", "OS:",
                "System memory:", "Graphics device:", "Graphics memory:",
                "CPU:", "Core count:", "Screen resolution:",
                "Rendering Threading Mode:", "Modding runtime:",
                "Scripting runtime:", "Shader level:",
            };
            var kopf = new StringBuilder();
            kopf.AppendLine("Extract from " + Path.GetFileName(pfad)
                + " - system details and the crash only.");
            kopf.AppendLine("Paths, identifiers and the loading log are left out.");
            var gesehen = new HashSet<string>();
            var sammlung = new ParkingLotLogauszug.Sammlung(LogzeilenAmEnde);
            long nummer = 0, absturz = -1;
            try
            {
                foreach (var zeile in ParkingLotLogauszug.Zeilen(pfad,
                    new[] { "Native Crash Reporting", "Crash!!!" }))
                {
                    nummer++;
                    foreach (var merkmal in kopfmerkmale)
                        if (zeile.Text.StartsWith(merkmal, StringComparison.Ordinal)
                            && gesehen.Add(merkmal))
                        { kopf.AppendLine(zeile.Text); break; }
                    if (absturz < 0 && (zeile.Passt
                        || (zeile.Text.Contains("* Assertion at ")
                            && zeile.Text.Contains("reflection_bind_generic_method_parameters"))))
                        absturz = nummer;
                    sammlung.FuegeHinzu(zeile.Text);
                    if (absturz < 0) sammlung.BegrenzeZeilen(ZeilenVorDemAbsturz);
                }
            }
            catch (Exception ausnahme)
            {
                return "Diese Datei war nicht lesbar: " + ausnahme.Message;
            }
            kopf.AppendLine(absturz < 0
                ? "NO crash block in this file. Last lines follow:"
                : "Crash from line " + absturz + "; up to " + ZeilenVorDemAbsturz + " preceding lines:");
            return sammlung.Text(kopf.ToString());
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
        /** Diese Zeilen gehoeren in den Sync-Bericht. */
        private static readonly string[] Syncmarken =
        {
            "PLT-Sync", "PLT-Hintergrund", "PLT-Prefabverweise", "PLT-Laden", "PLT-Fahrweg",
            "PLT-Bestand", "PLT-Flaechensync", "PLT-Begleiter", "PLT-Waisen", "[WARN]", "[ERROR]", "Exception",
        };
        private const int SyncauszugZeilen = 800;

        /** Die juengsten passenden Zeilen; auch eine Einzelzeile bleibt begrenzt. */
        private static string Syncauszug(string pfad)
            => ParkingLotLogauszug.Lesen(pfad, SyncauszugZeilen,
                "Sync-related lines of ParkingLotTool.Mod.log.", Syncmarken);

        private static string Logende(string pfad)
            => ParkingLotLogauszug.Lesen(pfad, LogzeilenAmEnde,
                "Die juengsten Zeilen aus ParkingLotTool.Mod.log.");

    }
}
