using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ParkingLotTool.Tools
{
    /**
     * MERKT SICH, OB DIE LETZTE SITZUNG ABGESTUERZT IST.
     *
     * Ansage des Nutzers am 2026-09-14: *"Wir brauchen auch noch am besten
     * einen 'Absturz'-Debug-Auswurf im Reiter Debug fuer den User."*
     *
     * WARUM DAS NICHT WIE DIE ANDEREN KNOEPFE GEHT. Stuerzt CS2 nativ ab, ist
     * das Spiel im selben Moment weg - `Managed Stacktrace` bleibt bei dieser
     * Absturzsorte sogar leer. Es kann also niemand mehr etwas anklicken. Der
     * einzige Weg fuehrt ueber den NAECHSTEN Start.
     *
     * DIE MARKE. Beim Laden legt die Mod eine Datei an und loescht sie beim
     * ordentlichen Beenden wieder. Liegt sie beim Start noch da, hat die
     * vorige Sitzung nicht sauber aufgehoert. Das ist absichtlich grob: ein
     * abgewuergter Prozess und ein echter Absturz sehen gleich aus, und lieber
     * einmal zu viel gefragt als einen Absturz verschwiegen.
     *
     * DIE BESTAETIGUNG kommt aus `Player-prev.log`. Dort steht die Meldung des
     * VORIGEN Laufs - bei unseren bisherigen Abstuerzen immer dieselbe:
     * `UpdateFrame added to unsupported type`, gefolgt von
     * `Native Crash Reporting`. Steht sie da, ist es wirklich ein Absturz
     * gewesen und kein Alt+F4.
     */
    internal static class ParkingLotAbsturzwache
    {
        private const string MarkenName = "ParkingLotTool-sitzung.marke";

        /** Steht nach `Pruefe` fest und wird von der UI abgefragt. */
        internal static bool LetzteSitzungAbgestuerzt { get; private set; }

        /** Was im vorigen Player.log gefunden wurde - fuer die Anzeige. */
        internal static string Befund { get; private set; } = string.Empty;

        private static string Ordner =>
            Path.Combine(Application.persistentDataPath, "Logs");

        /**
         * Beim Laden aufrufen: liest die Marke, stellt den Befund fest und
         * legt die Marke fuer DIESE Sitzung neu an.
         */
        internal static void Pruefe()
        {
            try
            {
                var marke = Path.Combine(Ordner, MarkenName);
                var offen = File.Exists(marke);
                if (offen)
                {
                    LetzteSitzungAbgestuerzt = true;
                    Befund = LiesVorigesLog();
                    Rette();
                    Mod.log.Warn("PLT-Absturzwache: die vorige Sitzung hat "
                        + "nicht ordentlich aufgehoert. " + Befund
                        + " Im Debug-Reiter steht jetzt ein Knopf fuer den "
                        + "Absturzbericht.");
                }

                Directory.CreateDirectory(Ordner);
                File.WriteAllText(marke,
                    "Diese Datei sagt nur: PLT laeuft gerade." + Environment.NewLine
                    + "Beim ordentlichen Beenden loescht die Mod sie wieder."
                    + Environment.NewLine + "Liegt sie beim Start noch da, "
                    + "ist die vorige Sitzung abgestuerzt." + Environment.NewLine
                    + "Gestartet: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch (Exception ausnahme)
            {
                Mod.log.Warn("PLT-Absturzwache nicht moeglich: "
                    + ausnahme.Message);
            }
        }

        /** Beim ordentlichen Beenden aufrufen. */
        internal static void Beende()
        {
            try
            {
                var marke = Path.Combine(Ordner, MarkenName);
                if (File.Exists(marke)) File.Delete(marke);
            }
            catch
            {
                // Bleibt die Marke liegen, fragt die Mod beim naechsten Start
                // einmal zu viel. Das ist der harmlose Ausgang.
            }
        }

        /**
         * Sucht im Player.log des vorigen Laufs nach der Absturzmeldung.
         *
         * `Player-prev.log` ist der vorige Lauf - genau der, der abgestuerzt
         * ist. Das aktuelle `Player.log` gehoert schon zu dieser Sitzung.
         */
        private static string LiesVorigesLog()
        {
            try
            {
                var pfad = Path.Combine(
                    Directory.GetParent(Ordner)?.FullName ?? Ordner,
                    "Player-prev.log");
                if (!File.Exists(pfad))
                    return "Ein vorheriges Player.log gibt es nicht.";

                using var strom = new FileStream(pfad, FileMode.Open,
                    FileAccess.Read, FileShare.ReadWrite);
                using var leser = new StreamReader(strom);
                var text = leser.ReadToEnd();

                var nativ = text.Contains("Native Crash Reporting");
                var updateFrame = text.Contains(
                    "UpdateFrame added to unsupported type");

                if (!nativ && !updateFrame)
                    return "Im vorigen Player.log steht keine Absturzmeldung - "
                        + "moeglicherweise wurde das Spiel nur hart beendet.";

                var teile = new System.Collections.Generic.List<string>();
                if (nativ) teile.Add("nativer Absturz");
                if (updateFrame)
                    teile.Add("\"UpdateFrame added to unsupported type\"");
                return "Im vorigen Player.log steht: "
                    + string.Join(" und ", teile) + ".";
            }
            catch (Exception ausnahme)
            {
                return "Das vorige Player.log war nicht lesbar ("
                    + ausnahme.Message + ").";
            }
        }

        /** Vorsilbe der geretteten Dateien. Eigene Sorte fuer die Logpflege. */
        private const string Rettung = "ParkingLotTool-absturzstand-";

        /**
         * RETTET DIE SPUREN DES ABGESTUERZTEN LAUFS, BEVOR SIE UEBERSCHRIEBEN
         * WERDEN.
         *
         * Ohne diesen Schritt waere der Absturzbericht fast wertlos, und das
         * faellt erst auf, wenn man ihn braucht:
         *
         *   - `ParkingLotTool-schritt.log` wird beim ERSTEN Schritt dieser
         *     Sitzung ueberschrieben - also genau dann, wenn der Nutzer das
         *     Spiel neu startet, um den Bericht zu erzeugen.
         *   - `ParkingLotTool.Mod.log` rotiert NICHT. Es gibt nur eine Datei,
         *     und CS2 faengt beim Start von vorn an. Das Log des abgestuerzten
         *     Laufs ist zu diesem Zeitpunkt bereits verloren - dagegen kann
         *     die Mod nichts tun, es gehoert ihr nicht. Genau deshalb ist die
         *     Schrittspur so wichtig: sie gehoert uns.
         *   - die juengsten Abzuege stammen aus dem abgestuerzten Lauf - aber
         *     nur, solange der Nutzer nichts Neues baut. Ein einziger Bau
         *     nach dem Neustart, und `Juengste` liefert den falschen.
         *
         * Kopiert wird deshalb sofort und unter eigenem Namen. Was hier liegt,
         * gehoert zum Absturz und kann von nichts mehr verdraengt werden.
         */
        private static void Rette()
        {
            var gerettet = 0;
            try
            {
                var spur = Path.Combine(Ordner, "ParkingLotTool-schritt.log");
                if (File.Exists(spur))
                {
                    File.Copy(spur, Path.Combine(Ordner, Rettung + "schritte.log"),
                        true);
                    gerettet++;
                }

                foreach (var sorte in new[] { "vorbau", "debug", "bericht" })
                {
                    var juengste = Directory
                        .GetFiles(Ordner, "ParkingLotTool-" + sorte + "-*")
                        .Where(pfad => !pfad.Contains("-latest")
                            && !pfad.Contains(Rettung))
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .FirstOrDefault();
                    if (juengste == null) continue;
                    File.Copy(juengste, Path.Combine(Ordner,
                        Rettung + Path.GetFileName(juengste)), true);
                    gerettet++;
                }

                Mod.log.Warn($"PLT-Absturzwache: {gerettet} Datei(en) des "
                    + "abgestuerzten Laufs gerettet. Das Modlog selbst ist "
                    + "nicht zu retten - es rotiert nicht und war beim Laden "
                    + "schon neu.");
            }
            catch (Exception ausnahme)
            {
                Mod.log.Warn("PLT-Absturzwache konnte die Spuren nicht "
                    + "retten: " + ausnahme.Message);
            }
        }

        /**
         * Die Dateien, die nur in einen ABSTURZBERICHT gehoeren.
         *
         * Das Player.log des vorigen Laufs traegt die Absturzmeldung, unser
         * eigenes Log von damals den Weg dorthin. Beides liegt beim naechsten
         * Start schon als `-prev` daneben - wer erst beim Melden danach sucht,
         * findet es nicht mehr.
         */
        internal static string[] Absturzdateien()
        {
            try
            {
                var spiel = Directory.GetParent(Ordner)?.FullName ?? Ordner;
                var fest = new[]
                {
                    Path.Combine(spiel, "Player-prev.log"),
                    Path.Combine(spiel, "Player.log"),
                };
                // Die geretteten Dateien zuerst - sie sind der eigentliche
                // Inhalt eines Absturzberichts.
                return Directory.GetFiles(Ordner, Rettung + "*")
                    .Concat(fest)
                    .Where(File.Exists)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }
}
