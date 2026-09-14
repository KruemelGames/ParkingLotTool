using System;

namespace ParkingLotTool.Geometry
{
    public static partial class ParkingGeometry
    {
        /**
         * DER LIVE-LOG: eine Zeile je Bauvorgang, mitlesbar waehrend gespielt
         * wird.
         *
         * WOZU. Der Nutzer kann im Spiel etwas beobachten, das ich nicht sehe -
         * am 2026-09-01 war das *"irgendwas braucht enorm lange beim Align"*.
         * Er kann es mir nicht zeigen, und ich kann es nicht nachstellen: die
         * Form, die es ausloest, hat er frei gezogen. Also muss der Mod selbst
         * berichten, waehrend es passiert.
         *
         * WARUM EIN HAKEN UND KEIN SCHREIBER. Die Geometrie kennt weder Datei
         * noch Spielverzeichnis noch Uhrzeit - sie ist auch das Herz des
         * Pruefprogramms, das ohne CS2 laeuft. Sie liefert deshalb nur den
         * TEXT; wohin er geht, entscheidet die Werkzeugseite
         * (`ParkingLotLiveLog`). Ist niemand da, faellt jede Zeile ins Leere.
         *
         * WARUM DIE MESSUNG AM HAKEN HAENGT, nicht an einem eigenen Schalter:
         * so kostet sie im Normalbetrieb genau einen Nullvergleich. Die
         * vorhandene `PhaseLog`-Maschinerie taugt dafuer NICHT - sie prueft
         * jeden Ring auf Selbstschnitte und Doppelpunkte, quadratisch ueber
         * die Ecken. Wer damit misst, misst die Messung mit.
         */
        public static Action<string> LiveSchreiber;

        /** Laeuft gerade jemand mit? Nur dann wird ueberhaupt gestoppt. */
        internal static bool LiveAn => LiveSchreiber != null;

        internal static void Live(string zeile)
        {
            var schreiber = LiveSchreiber;
            if (schreiber == null) return;
            try { schreiber(zeile); }
            catch
            {
                // Ein Log darf niemals einen Bau umbringen. Faellt der
                // Schreiber aus, haengt er sich selbst ab.
                LiveSchreiber = null;
            }
        }
    }
}
