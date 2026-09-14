using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ParkingLotTool.Tools
{
    /**
     * RAEUMT DIE EIGENEN DATEIEN AUF.
     *
     * Ansage des Nutzers am 2026-09-14: *"Mir geht's darum, dass die Logs
     * derzeit nicht wirklich gross sind, aber enorm viel werden koennen, wenn
     * ein User doch mal paar Monate spielt und Parkplaetze bei sich baut. Ich
     * will keinen Datenmuell ansammeln."*
     *
     * Das ist kein Zukunftsproblem. GEMESSEN am selben Tag im Logs-Ordner der
     * Entwicklungsmaschine: **1535 Dateien mit 214 MB** nach fuenf Wochen.
     * Einzelne Bauteil-Abzuege wiegen 200 bis 550 kB, ein
     * Versorgungsschutz-Protokoll 266 kB. Geschrieben hat sie jemand
     * absichtlich - weggeraeumt hat sie nie jemand.
     *
     * WARUM EIN HAUSMEISTER UND NICHT WENIGER SCHREIBEN. Beides. Was nur beim
     * Suchen hilft, ist zuschaltbar (siehe `Mod.An`). Aber ein Abzug, den man
     * heute braucht, ist morgen Muell, und kein Schreiber kann das wissen.
     * Deshalb entscheidet EINE Stelle, wie viel bleibt.
     *
     * WAS SIE NIE ANFASST:
     *
     *   - `ParkingLotTool.Mod.log` - die gehoert CS2, nicht uns.
     *   - alles, was nicht mit `ParkingLotTool-` beginnt.
     *   - die `-latest`-Dateien, auf die Werkzeuge fest verweisen.
     *
     * Gehalten wird je Sorte die juengste Handvoll. Sorte heisst: der Name
     * ohne Zeitstempel - `bauteile`, `bericht`, `debug`, `vorbau` und so fort.
     * Wer gerade an einer Sache arbeitet, hat seine letzten Abzuege also
     * immer noch da; was drei Wochen alt ist, geht.
     */
    internal static class ParkingLotLogpflege
    {
        /**
         * Wie viele Abzuege je Sorte bleiben.
         *
         * Fuenf, weil man beim Vergleichen selten mehr als zwei oder drei
         * nebeneinanderlegt und ein bisschen Luft nicht schadet. Die Zahl ist
         * gewaehlt, nicht gemessen - sie steht hier, damit man sie findet.
         */
        private const int JeSorteBehalten = 5;

        /**
         * Wie viele Bauten im Protokoll bleiben.
         *
         * `ParkingLotTool-bauten.jsonl` waechst um eine Zeile je Bau und wird
         * nie kuerzer. Es ist die einzige Quelle, mit der sich ein Bau
         * nachstellen laesst (siehe plt-berichte-und-bauprotokoll), darf also
         * nicht verschwinden - aber tausend Bauten reichen weit zurueck.
         */
        private const int BautenBehalten = 1000;

        internal static void Raeume()
        {
            try
            {
                var ordner = Path.Combine(
                    Application.persistentDataPath, "Logs");
                if (!Directory.Exists(ordner)) return;

                var alt = new List<FileInfo>();
                var behalten = 0;
                foreach (var gruppe in Directory
                             .GetFiles(ordner, "ParkingLotTool-*")
                             .Select(pfad => new FileInfo(pfad))
                             .Where(datei => !datei.Name.Contains("-latest"))
                             .GroupBy(Sorte))
                {
                    var sortiert = gruppe
                        .OrderByDescending(datei => datei.LastWriteTimeUtc)
                        .ToArray();
                    behalten += Math.Min(JeSorteBehalten, sortiert.Length);
                    for (var i = JeSorteBehalten; i < sortiert.Length; i++)
                        alt.Add(sortiert[i]);
                }

                var frei = 0L;
                var weg = 0;
                foreach (var datei in alt)
                {
                    var groesse = datei.Length;
                    try { datei.Delete(); }
                    catch { continue; }
                    frei += groesse;
                    weg++;
                }

                KuerzeBauprotokoll(ordner, ref frei, ref weg);
                RaeumeMeldungen(ordner, ref frei, ref weg);

                if (weg > 0)
                    Mod.log.Info($"PLT-Logpflege: {weg} eigene Datei(en) "
                        + $"entfernt, {frei / 1048576.0:F1} MB frei; je Sorte "
                        + $"bleiben die juengsten {JeSorteBehalten} "
                        + $"({behalten} Datei(en) behalten).");
            }
            catch (Exception ausnahme)
            {
                // Aufraeumen darf den Start nie kippen.
                Mod.log.Warn("PLT-Logpflege nicht moeglich: "
                    + ausnahme.Message);
            }
        }

        /**
         * RAEUMT AUCH DIE MELDEARCHIVE AUF.
         *
         * Sie liegen seit dem 2026-09-14 in `ParkingLotTool-Logs/<Sparte>`
         * und nicht mehr im Logs-Ordner selbst - die Schleife oben sieht sie
         * also nicht. Ohne diesen Zusatz waere genau der Ordner der einzige,
         * der wieder unbegrenzt waechst, und das war der Anlass der ganzen
         * Aufraeumerei.
         *
         * Je Sparte bleiben dieselben fuenf wie ueberall.
         */
        private static void RaeumeMeldungen(string ordner,
            ref long frei, ref int weg)
        {
            var wurzel = Path.Combine(ordner, "ParkingLotTool-Logs");
            if (!Directory.Exists(wurzel)) return;

            foreach (var sparte in Directory.GetDirectories(wurzel))
            {
                var alt = Directory.GetFiles(sparte)
                    .Select(pfad => new FileInfo(pfad))
                    .OrderByDescending(datei => datei.LastWriteTimeUtc)
                    .Skip(JeSorteBehalten)
                    .ToArray();
                foreach (var datei in alt)
                {
                    var groesse = datei.Length;
                    try { datei.Delete(); }
                    catch { continue; }
                    frei += groesse;
                    weg++;
                }
            }
        }

        /**
         * Der Name bis zum Datum - alles danach gehoert zum einzelnen Abzug.
         *
         * `ParkingLotTool-bauteile-20260811-145639.txt` wird zu
         * `ParkingLotTool-bauteile`. Gibt es kein Datum, ist der ganze Name
         * die Sorte; so bleibt `bauten.jsonl` fuer sich und wird gesondert
         * gekuerzt.
         *
         * ERSTER ANLAUF SCHNITT VON HINTEN - solange die Endung aus Ziffern
         * bestand. Der Trockenlauf am 2026-09-14 zeigte den Fehler sofort:
         * `ParkingLotTool-Versorgungsschutz-20260906-213722-310-5a538cb6`
         * endet auf einen Hash, nicht auf Ziffern. Jede solche Datei wurde
         * damit ihre EIGENE Sorte, und eine Sorte mit einer Datei raeumt man
         * nie auf. Genau die Dateien mit 266 kB waeren liegengeblieben.
         *
         * Das Datum ist der zuverlaessige Schnitt: acht Ziffern am Stueck.
         */
        private static string Sorte(FileInfo datei)
        {
            var name = Path.GetFileNameWithoutExtension(datei.Name);
            var teile = name.Split('-');
            for (var i = 0; i < teile.Length; i++)
                if (teile[i].Length == 8 && teile[i].All(char.IsDigit))
                    return string.Join("-", teile.Take(i));
            return name;
        }

        private static void KuerzeBauprotokoll(string ordner,
            ref long frei, ref int weg)
        {
            var pfad = Path.Combine(ordner, "ParkingLotTool-bauten.jsonl");
            if (!File.Exists(pfad)) return;

            var zeilen = File.ReadAllLines(pfad);
            if (zeilen.Length <= BautenBehalten) return;

            var vorher = new FileInfo(pfad).Length;
            File.WriteAllLines(pfad,
                zeilen.Skip(zeilen.Length - BautenBehalten),
                new UTF8Encoding(false));
            frei += vorher - new FileInfo(pfad).Length;
            weg++;
            Mod.log.Info($"PLT-Logpflege: Bauprotokoll von {zeilen.Length} "
                + $"auf {BautenBehalten} Bauten gekuerzt.");
        }
    }
}
