using System;
using System.IO;
using System.Text;

namespace ParkingLotTool.Tools
{
    /**
     * SCHREIBT SOFORT AUF DIE PLATTE, WO DIE MOD GERADE IST.
     *
     * Ansage des Nutzers am 2026-09-14 vor der Testveroeffentlichung: *"Es
     * waere wirklich wichtig, dass das so viel abfaengt wie moeglich, bevor es
     * ueberhaupt crasht"* - und der Grund dahinter: *"Das waere das groesste
     * Uebel, weil es Zeit kostet und wir die User nerven und evtl. nix fixen
     * koennen, weil wir gar nicht erfahren, warum der Crash kam."*
     *
     * DAS LOCH IM BISHERIGEN BERICHT. Ein Absturzbericht ist nur so gut wie
     * das Modlog, das er einpackt. Der gewoehnliche Logschreiber puffert
     * aber - und bei einem nativen Absturz ist der Prozess weg, bevor der
     * Puffer die Platte erreicht. Ausgerechnet die LETZTEN Zeilen fehlen dann,
     * also genau die, die sagen, was die Mod gerade tat.
     *
     * DESHALB DIESE SPUR. Sie haelt die letzten Schritte im Speicher und
     * schreibt bei jedem neuen Schritt die ganze Liste neu - mit
     * `Flush(true)`, das erzwingt das Durchschreiben bis auf die Platte. Nach
     * einem Absturz steht in der Datei als letzte Zeile der Schritt, in dem es
     * passiert ist.
     *
     * WAS SIE NICHT IST: kein Ersatz fuers Log und keine Messung. Hier stehen
     * grobe Schritte - "Bau: Flaechen uebergeben", "Versorgung: Leitungen
     * anlegen" -, keine Zahlen und nichts je Bild. Wer sie feiner macht,
     * macht sie langsam, und eine Spur, die bremst, schaltet irgendwann
     * jemand ab.
     */
    internal static class ParkingLotSchrittmarke
    {
        /**
         * Soviele Schritte bleiben stehen.
         *
         * Zwanzig, weil die interessante Frage "was war unmittelbar davor?"
         * lautet und nicht "was war vor einer Stunde?". Die Datei bleibt damit
         * unter zwei Kilobyte und das Schreiben unter einer Millisekunde.
         */
        private const int Schritte = 20;

        private static readonly string[] _ring = new string[Schritte];
        private static int _naechste;
        private static int _gesamt;
        private static readonly object _schloss = new object();
        private static string _pfad;
        private static bool _kaputt;

        internal static string Pfad
        {
            get
            {
                if (_pfad != null) return _pfad;
                _pfad = Path.Combine(
                    Path.Combine(UnityEngine.Application.persistentDataPath,
                        "Logs"),
                    "ParkingLotTool-schritt.log");
                return _pfad;
            }
        }

        /**
         * Haelt einen Schritt fest.
         *
         * Kurz halten: der Text landet unveraendert in der Datei, und die soll
         * man in drei Sekunden ueberblicken.
         */
        internal static void Setze(string schritt)
        {
            if (_kaputt) return;
            try
            {
                lock (_schloss)
                {
                    _ring[_naechste] = DateTime.Now.ToString("HH:mm:ss.fff")
                        + "  " + schritt;
                    _naechste = (_naechste + 1) % Schritte;
                    _gesamt++;
                    Schreibe();
                }
            }
            catch (Exception ausnahme)
            {
                // Einmal melden, dann Ruhe. Eine Spur, die bei jedem Schritt
                // eine Warnung erzeugt, ist schlimmer als keine.
                _kaputt = true;
                Mod.log.Warn("PLT-Schrittmarke abgeschaltet: "
                    + ausnahme.Message);
            }
        }

        /**
         * Schreibt die ganze Liste neu und erzwingt das Durchschreiben.
         *
         * `Flush(true)` geht bis auf die Platte, nicht nur in den
         * Betriebssystem-Puffer. Genau darum geht es hier: was gepuffert
         * bleibt, ist nach einem nativen Absturz weg.
         */
        private static void Schreibe()
        {
            var text = new StringBuilder();
            text.AppendLine("Die letzten Schritte des Parking Lot Tool.");
            text.AppendLine("Die UNTERSTE Zeile ist die juengste. Nach einem "
                + "Absturz steht dort, was die Mod gerade tat.");
            text.AppendLine(new string('-', 70));

            var gehalten = Math.Min(_gesamt, Schritte);
            var anfang = _gesamt <= Schritte ? 0 : _naechste;
            for (var i = 0; i < gehalten; i++)
                text.AppendLine(_ring[(anfang + i) % Schritte]);

            Directory.CreateDirectory(Path.GetDirectoryName(Pfad));
            using var strom = new FileStream(Pfad, FileMode.Create,
                FileAccess.Write, FileShare.ReadWrite);
            var bytes = new UTF8Encoding(false).GetBytes(text.ToString());
            strom.Write(bytes, 0, bytes.Length);
            strom.Flush(true);
        }
    }
}
