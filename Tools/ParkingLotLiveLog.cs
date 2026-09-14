using System;
using System.Globalization;
using System.IO;
using ParkingLotTool.Geometry;
using UnityEngine;

namespace ParkingLotTool.Tools
{
    /**
     * DER LIVE-LOG: eine Datei, die waehrend des Spielens mitwaechst.
     *
     * WOZU. Der Nutzer sieht im Spiel Dinge, die ich nicht nachstellen kann -
     * am 2026-09-01 war das *"irgendwas braucht enorm lange beim Align"*. Die
     * Form, die es ausloest, hat er frei gezogen; sie existiert nirgends
     * sonst. Damit bleibt nur: der Mod berichtet, waehrend es passiert, und
     * ich lese die Datei mit.
     *
     * WAS IHN VOM NORMALEN LOG UNTERSCHEIDET - beides wichtig:
     *
     *   - Er ist AUS, bis jemand ihn einschaltet (Debug-Reiter). Ein
     *     Dauerlog, der jeden Bau mitschreibt, ist nach einer Stunde
     *     unbrauchbar.
     *   - Er ist KURZ. Ansage des Nutzers: *"nicht bei einmal ändern gleich
     *     10k Zeilen ausspucken"*. Ein Bau ergibt zwei Zeilen: was gerechnet
     *     wurde, und woran die Zeit hing. Kein Ring, kein Punkt, keine Bucht
     *     einzeln.
     *
     * Er startet bei jedem Einschalten NEU (die alte Datei wird ersetzt).
     * Genau so soll man ihn benutzen: einschalten, EINE Sache tun,
     * nachlesen. Eine Datei, die drei Sitzungen sammelt, beantwortet keine
     * Frage mehr.
     */
    internal static class ParkingLotLiveLog
    {
        private const string DateiName = "ParkingLotTool-live.log";

        private static readonly object Schloss = new object();
        private static StreamWriter _schreiber;

        internal static bool Aktiv { get; private set; }

        internal static string Pfad
        {
            get
            {
                var ordner = Path.Combine(Application.persistentDataPath, "Logs");
                Directory.CreateDirectory(ordner);
                return Path.Combine(ordner, DateiName);
            }
        }

        internal static void Schalte(bool an)
        {
            if (an == Aktiv) return;
            if (an) Ein(); else Aus();
        }

        private static void Ein()
        {
            try
            {
                lock (Schloss)
                {
                    // Neu anfangen, nicht anhaengen - siehe Kopf.
                    _schreiber = new StreamWriter(
                        new FileStream(Pfad, FileMode.Create, FileAccess.Write,
                            FileShare.ReadWrite))
                    { AutoFlush = true };
                }
                Aktiv = true;
                /*
                 * `FileShare.ReadWrite` ist hier PFLICHT, nicht Bequemlichkeit:
                 * ohne sie kann niemand die Datei lesen, waehrend der Mod sie
                 * offen haelt - und Mitlesen ist der ganze Zweck.
                 * `AutoFlush` aus demselben Grund: gepuffert stuende die
                 * interessante Zeile erst nach dem Ausschalten drin.
                 */
                Zeile("live-log an  ·  " + Pfad);
                ParkingGeometry.LiveSchreiber = Zeile;
                Mod.log.Info("PLT-LiveLog: an, schreibt nach " + Pfad);
            }
            catch (Exception fehler)
            {
                Aktiv = false;
                _schreiber = null;
                ParkingGeometry.LiveSchreiber = null;
                Mod.log.Warn("PLT-LiveLog: konnte nicht geoeffnet werden - "
                    + fehler.Message);
            }
        }

        private static void Aus()
        {
            ParkingGeometry.LiveSchreiber = null;
            Aktiv = false;
            lock (Schloss)
            {
                try
                {
                    _schreiber?.WriteLine(Stempel() + "live-log aus");
                    _schreiber?.Dispose();
                }
                catch (Exception fehler)
                {
                    Mod.log.Warn("PLT-LiveLog: Schliessen fehlgeschlagen - "
                        + fehler.Message);
                }
                _schreiber = null;
            }
            Mod.log.Info("PLT-LiveLog: aus.");
        }

        private static string Stempel()
            => DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)
               + "  ";

        /**
         * Eine Zeile. Aufrufbar aus JEDEM Thread: der Vorschaulauf steckt in
         * einem Hintergrundtask, und genau dessen Zeiten sind die
         * interessanten.
         */
        internal static void Zeile(string text)
        {
            if (!Aktiv) return;
            lock (Schloss)
            {
                if (_schreiber == null) return;
                try { _schreiber.WriteLine(Stempel() + text); }
                catch
                {
                    // Ein Log darf nie ein Spiel umbringen.
                    _schreiber = null;
                    Aktiv = false;
                    ParkingGeometry.LiveSchreiber = null;
                }
            }
        }

        /** Zahl ohne Landeskomma - der Log wird maschinell gelesen. */
        internal static string Zahl(double wert, int stellen = 1)
            => wert.ToString("F" + stellen, CultureInfo.InvariantCulture);
    }
}
