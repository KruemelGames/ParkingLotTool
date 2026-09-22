using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ParkingLotTool.Tools
{
    /**
     * MISST, WOHIN DIE BILDZEIT GEHT.
     *
     * Anlass ist eine Rueckmeldung auf Reddit: *"something is causing the
     * main thread to lock up"*, mit einer Liste von Verdaechtigen - synchron
     * auf dem Hauptthread, zu haeufiges Abtasten, zu grosse Schleifen,
     * unnoetige Protokoll-Ein/Ausgabe.
     *
     * Beim Lesen habe ich drei Stellen gefunden, an denen das plausibel ist.
     * Plausibel ist aber keine Zahl, und die Reihenfolge der Reparatur haengt
     * genau daran (arbeitsweise-zaehler-vor-theorie: erst zaehlen, dann
     * erklaeren). Diese Datei liefert die Zahlen.
     *
     * WAS SIE KOSTET: ein `Stopwatch.GetTimestamp()` je Messpunkt. Das ist
     * ein QPC-Aufruf, im zweistelligen Nanosekundenbereich, und es gibt ein
     * knappes Dutzend davon je Bild. Gemessen wird ausserdem NUR, solange das
     * Werkzeug laeuft oder ein Bild aus der Reihe faellt - im normalen Spiel
     * passiert hier nichts ausser einem Vergleich.
     *
     * KEINE ZUTEILUNGEN: feste Felder, keine Listen, keine Zeichenketten, bis
     * eine Zeile wirklich geschrieben wird. Ein Messapparat, der selbst den
     * Muellsammler fuettert, misst sich selbst.
     */
    internal static class ParkingLotMessung
    {
        /** Die gemessenen Abschnitte. */
        internal enum Punkt
        {
            /** Das ganze OnUpdate des Werkzeugs. */
            Werkzeug,
            /** RenderOverlay komplett. */
            Overlay,
            /** Nur das Warten auf die Overlay-Schreiber. */
            Warten,
            /** Nur die Zeichenaufrufe in den Overlay-Puffer. */
            Zeichnen,
            /** Das Zeichnen der gefuellten Flaechen. */
            Flaechennetz,
            /** Das Anlegen der Vorschau-Entities. */
            Vorschau,
            Anzahl,
        }

        /** Die gezaehlten Stueckzahlen. */
        internal enum Zaehler
        {
            /** Kreise fuer Pflanzen. */
            Pflanzen,
            /** Baender fuer Gras, Belag und Buchten. */
            Baender,
            /** Sonstige Zeichenaufrufe (Punkte, Linien, Marken). */
            Sonstige,
            /** Netzgruppen, die in diesem Bild gezeichnet wurden. */
            Netzgruppen,
            /** Uebertragungen der Zeichenargumente an die Grafikkarte. */
            Uebertragungen,
            Anzahl,
        }

        /**
         * Die Systeme, die in einer Bildphase laufen und mit Entities
         * arbeiten - also die, die auch bei zugem Werkzeug Zeit kosten
         * koennen.
         *
         * Reine Anzeigesysteme ohne `OnUpdate` stehen nicht dabei; sie
         * rechnen nur, wenn CS2 eine Bindung abholt.
         */
        internal enum Sys
        {
            Komfort,
            Statistik,
            Wirtschaft,
            Begleiter,
            Angestellte,
            Aufraeumen,
            Flaechenwache,
            Strassenname,
            Leitungsabriss,
            Liste,
            Altbestand,
            Versorgung,
            Anzahl,
        }

        private static readonly long[] SysSumme = new long[(int)Sys.Anzahl];
        private static readonly long[] SysHoechst = new long[(int)Sys.Anzahl];
        private static readonly long[] SysBild = new long[(int)Sys.Anzahl];

        private static readonly string[] SysName =
        {
            "Komfort", "Statistik", "Wirtschaft", "Begleiter", "Angestellte",
            "Aufraeumen", "Flaechenwache", "Strassenname", "Leitungsabriss",
            "Liste", "Altbestand", "Versorgung",
        };

        /**
         * Eine Stoppuhr zum Hinschreiben:
         *
         *     using var _ = ParkingLotMessung.Miss(Sys.Komfort);
         *
         * `ref struct` und deshalb ohne Zuteilung - sie lebt auf dem Stapel
         * und kann gar nicht auf den Haufen wandern. Ein Messapparat, der
         * selbst den Muellsammler fuettert, misst sich selbst.
         */
        internal readonly ref struct Uhr
        {
            private readonly int _slot;
            private readonly long _start;

            internal Uhr(int slot)
            {
                _slot = slot;
                _start = Stopwatch.GetTimestamp();
            }

            public void Dispose()
            {
                var dauer = Stopwatch.GetTimestamp() - _start;
                SysSumme[_slot] += dauer;
                SysBild[_slot] += dauer;
                if (dauer > SysHoechst[_slot]) SysHoechst[_slot] = dauer;
            }
        }

        /**
         * HIER HAENGT AUCH DIE ABSTURZSPUR DRAN.
         *
         * Jedes unserer Simulationssysteme beginnt sein `OnUpdate` mit
         * dieser Uhr - zwoelf Stellen, alle schon da. Die Marke gehoert
         * deshalb hierher und nicht in zwoelf Systeme einzeln: eine Regel an
         * einem Ort, und kein System kann sie beim naechsten Mal vergessen.
         *
         * Sie wird VOR der Arbeit gesetzt, nicht danach. Nur so steht nach
         * einem Absturz drin, WORIN es gekracht hat - eine Marke am Ende
         * haette das System, das gerade sauber fertig wurde.
         *
         * Ist der Schalter aus, ist das ein Feldvergleich.
         */
        internal static Uhr Miss(Sys system)
        {
            if (ParkingLotSchrittmarke.Mitschreiben)
                ParkingLotSchrittmarke.Setze("System: " + SysName[(int)system]);
            return new Uhr((int)system);
        }

        /**
         * Ab dieser Bildzeit gilt ein Bild als Ausreisser und wird einzeln
         * gemeldet - auch wenn das Werkzeug zu ist.
         *
         * 50 ms sind 20 Bilder je Sekunde. Darunter merkt man ein einzelnes
         * langes Bild nicht; darueber ist es genau das, was als Haenger
         * beschrieben wird.
         */
        private const double AusreisserMs = 50.0;

        private static readonly long[] Summe = new long[(int)Punkt.Anzahl];
        private static readonly long[] Hoechstwert = new long[(int)Punkt.Anzahl];
        private static readonly int[] Laeufe = new int[(int)Punkt.Anzahl];
        private static readonly long[] BildSumme = new long[(int)Punkt.Anzahl];
        private static readonly long[] Stueck = new long[(int)Zaehler.Anzahl];
        private static readonly long[] BildStueck = new long[(int)Zaehler.Anzahl];

        private static long _fensterStart;
        private static long _letztesBild;
        private static int _bilder;
        private static long _bildHoechstwert;
        private static bool _werkzeugLief;

        private static readonly double MsJeTick =
            1000.0 / Stopwatch.Frequency;

        // ------------------------------------------------ Aufzeichnung

        private const int HoechstZeilen = 4000;

        private static readonly List<string> Aufzeichnung = new List<string>();
        private static long _aufzeichnungBis;
        private static int _verworfeneZeilen;
        private static string _anlass;

        /** Laeuft gerade eine Aufzeichnung? */
        internal static bool Zeichnetauf
            => _aufzeichnungBis != 0
               && Stopwatch.GetTimestamp() < _aufzeichnungBis;

        /** Wieviele Sekunden noch - fuer die Anzeige im Panel. */
        internal static int Restsekunden
        {
            get
            {
                if (_aufzeichnungBis == 0) return 0;
                var rest = (_aufzeichnungBis - Stopwatch.GetTimestamp())
                    * MsJeTick / 1000.0;
                return rest <= 0 ? 0 : (int)Math.Ceiling(rest);
            }
        }

        /** Fertig und noch nicht abgeholt. */
        internal static bool Abholbereit
            => _aufzeichnungBis != 0 && !Zeichnetauf;

        internal static void StarteAufzeichnung(int sekunden, string anlass)
        {
            Aufzeichnung.Clear();
            _verworfeneZeilen = 0;
            _anlass = anlass;
            _aufzeichnungBis = Stopwatch.GetTimestamp()
                + (long)(sekunden * 1000.0 / MsJeTick);
            Notiere("=== performance measurement started, " + sekunden
                + " seconds, reason: " + anlass + " ===");
        }

        /**
         * Holt die gesammelten Zeilen ab und beendet die Aufzeichnung.
         *
         * Der Kopf nennt, was man zum Lesen braucht: wie lange gemessen
         * wurde und ob etwas verlorenging.
         */
        internal static string Ernte()
        {
            _aufzeichnungBis = 0;
            // Englisch, weil diese Datei im Meldepaket den Rechner verlaesst.
            var kopf = "Performance measurement, reason: " + (_anlass ?? "-")
                + Environment.NewLine
                + Aufzeichnung.Count + " lines"
                + (_verworfeneZeilen > 0
                    ? " (" + _verworfeneZeilen + " more not collected, "
                      + "cap " + HoechstZeilen + ")"
                    : string.Empty)
                + Environment.NewLine
                + "How to read it: OUTLIER is one single long frame. If "
                + "everything there is 0.0 and no system is named, it was "
                + "not this mod." + Environment.NewLine + Environment.NewLine;
            var text = kopf + string.Join(Environment.NewLine, Aufzeichnung);
            Aufzeichnung.Clear();
            return text;
        }

        private static void Notiere(string zeile)
        {
            if (!Zeichnetauf && _aufzeichnungBis == 0) return;
            if (Aufzeichnung.Count >= HoechstZeilen)
            {
                _verworfeneZeilen++;
                return;
            }
            Aufzeichnung.Add(DateTime.Now.ToString("HH:mm:ss.fff")
                + "  " + zeile);
        }

        /** Startpunkt eines Abschnitts. Rueckgabe geht an `Ende`. */
        internal static long Start() => Stopwatch.GetTimestamp();

        internal static void Ende(Punkt punkt, long start)
        {
            var dauer = Stopwatch.GetTimestamp() - start;
            var i = (int)punkt;
            Summe[i] += dauer;
            BildSumme[i] += dauer;
            Laeufe[i]++;
            if (dauer > Hoechstwert[i]) Hoechstwert[i] = dauer;
        }

        internal static void Zaehle(Zaehler zaehler, long anzahl = 1)
        {
            Stueck[(int)zaehler] += anzahl;
            BildStueck[(int)zaehler] += anzahl;
        }

        /**
         * Einmal je Bild aufrufen, aus einem System, das immer laeuft.
         *
         * `werkzeugAktiv` entscheidet ueber die regelmaessige Meldung: ohne
         * Werkzeug ist die interessante Frage nur, ob ein Bild aus der Reihe
         * faellt.
         */
        internal static void Bild(bool werkzeugAktiv)
        {
            var jetzt = Stopwatch.GetTimestamp();
            if (_letztesBild != 0)
            {
                var dauer = jetzt - _letztesBild;
                _bilder++;
                if (dauer > _bildHoechstwert) _bildHoechstwert = dauer;
                if (dauer * MsJeTick >= AusreisserMs) MeldeAusreisser(dauer);
            }
            _letztesBild = jetzt;
            if (_fensterStart == 0) _fensterStart = jetzt;

            /*
             * JEDE SEKUNDE, AUCH OHNE WERKZEUG.
             *
             * Bis zum 2026-09-17 wurde nur bei offenem Werkzeug gemeldet.
             * Damit fehlte ausgerechnet der Fall, um den es geht: Mod
             * installiert, Werkzeug zu, normal gespielt.
             */
            var fenster = (jetzt - _fensterStart) * MsJeTick;
            if (fenster >= 1000.0) Melde(fenster, werkzeugAktiv);
            _werkzeugLief = werkzeugAktiv;

            for (var i = 0; i < BildSumme.Length; i++) BildSumme[i] = 0;
            for (var i = 0; i < BildStueck.Length; i++) BildStueck[i] = 0;
            for (var i = 0; i < SysBild.Length; i++) SysBild[i] = 0;
        }

        /**
         * Ein einzelnes langes Bild, sofort gemeldet.
         *
         * Der Mittelwert ueber eine Sekunde versteckt genau das, worum es
         * geht: 59 schnelle Bilder und eines mit 400 ms ergeben zusammen
         * einen harmlosen Schnitt. Deshalb steht der Ausreisser fuer sich,
         * mit der Aufteilung DIESES Bildes.
         */
        private static void MeldeAusreisser(long dauer)
        {
            // Englisch: dieselbe Zeile landet in `performance.txt` im
            // Meldepaket und damit bei jemandem, der kein Deutsch liest.
            var zeile = "OUTLIER: frame "
                + Ms(dauer) + " ms"
                + " | tool " + Ms(BildSumme[(int)Punkt.Werkzeug])
                + " | Overlay " + Ms(BildSumme[(int)Punkt.Overlay])
                + " (waiting " + Ms(BildSumme[(int)Punkt.Warten])
                + ", drawing " + Ms(BildSumme[(int)Punkt.Zeichnen]) + ")"
                + " | surface mesh " + Ms(BildSumme[(int)Punkt.Flaechennetz])
                + " | preview " + Ms(BildSumme[(int)Punkt.Vorschau])
                + " | calls " + (BildStueck[(int)Zaehler.Pflanzen]
                    + BildStueck[(int)Zaehler.Baender]
                    + BildStueck[(int)Zaehler.Sonstige])
                + " (plants " + BildStueck[(int)Zaehler.Pflanzen]
                + ", bands " + BildStueck[(int)Zaehler.Baender]
                + ", other " + BildStueck[(int)Zaehler.Sonstige] + ")"
                + " | mesh groups " + BildStueck[(int)Zaehler.Netzgruppen]
                + ", uploads " + BildStueck[(int)Zaehler.Uebertragungen]
                + SystemeDiesesBild() + ".";
            Mod.log.Info("PLT-Messung " + zeile);
            Notiere(zeile);
        }

        /** Wie `Systeme`, aber fuer das eine lange Bild. */
        private static string SystemeDiesesBild()
        {
            var text = string.Empty;
            for (var i = 0; i < SysBild.Length; i++)
            {
                if (SysBild[i] * MsJeTick < 1.0) continue;
                text += " | " + SysName[i] + " " + Ms(SysBild[i]);
            }
            return text;
        }

        private static void Melde(double fensterMs, bool werkzeugAktiv)
        {
            if (_bilder > 0)
            {
                // Englisch: diese Zeile landet in `performance.txt` im
                // Meldepaket, genau wie die Ausreisserzeile.
                var zeile = (werkzeugAktiv ? "" : "(tool closed) ")
                    + _bilder + " frames in "
                    + fensterMs.ToString("F0") + " ms = "
                    + (fensterMs / _bilder).ToString("F1") + " ms/frame"
                    + ", worst " + Ms(_bildHoechstwert) + " ms."
                    + Abschnitt("tool", Punkt.Werkzeug)
                    + Abschnitt("Overlay", Punkt.Overlay)
                    + Abschnitt("of that waiting", Punkt.Warten)
                    + Abschnitt("of that drawing", Punkt.Zeichnen)
                    + Abschnitt("surface mesh", Punkt.Flaechennetz)
                    + Abschnitt("preview", Punkt.Vorschau)
                    + " | per frame: plants "
                    + (Stueck[(int)Zaehler.Pflanzen] / _bilder)
                    + ", bands " + (Stueck[(int)Zaehler.Baender] / _bilder)
                    + ", other " + (Stueck[(int)Zaehler.Sonstige] / _bilder)
                    + ", mesh groups "
                    + (Stueck[(int)Zaehler.Netzgruppen] / _bilder)
                    + ", uploads "
                    + (Stueck[(int)Zaehler.Uebertragungen] / _bilder)
                    + Systeme() + ".";
                Mod.log.Info("PLT-Messung: " + zeile);
                Notiere(zeile);
            }

            for (var i = 0; i < Summe.Length; i++)
            {
                Summe[i] = 0;
                Hoechstwert[i] = 0;
                Laeufe[i] = 0;
            }
            for (var i = 0; i < Stueck.Length; i++) Stueck[i] = 0;
            for (var i = 0; i < SysSumme.Length; i++)
            {
                SysSumme[i] = 0;
                SysHoechst[i] = 0;
            }
            _bilder = 0;
            _bildHoechstwert = 0;
            _fensterStart = Stopwatch.GetTimestamp();
        }

        /**
         * Mittelwert je BILD und Hoechstwert je LAUF.
         *
         * Zwei verschiedene Nenner, und das ist Absicht: "wie teuer ist
         * dieser Abschnitt fuer die Bildrate" beantwortet nur der Schnitt je
         * Bild, "wie schlimm kann ein einzelner Lauf werden" nur der
         * Hoechstwert. Der Schnitt je Lauf waere die Zahl, die beides
         * verdeckt.
         */
        private static string Abschnitt(string name, Punkt punkt)
        {
            var i = (int)punkt;
            if (Laeufe[i] == 0) return " | " + name + " -";
            return " | " + name + " " + (Summe[i] * MsJeTick / _bilder)
                    .ToString("F2") + " ms/Bild (max " + Ms(Hoechstwert[i])
                + ", " + Laeufe[i] + "x)";
        }

        /**
         * Nur die Systeme, die in diesem Fenster ueberhaupt Zeit verbraucht
         * haben.
         *
         * Zwoelf Namen mit "0,00 ms" in jeder Zeile machen das Log
         * unlesbar - und unlesbare Logs sind der Grund, aus dem man spaeter
         * Befunde uebersieht. Die Schwelle liegt bei einer Hundertstel
         * Millisekunde je Bild; darunter ist es Messrauschen.
         */
        private static string Systeme()
        {
            var text = string.Empty;
            for (var i = 0; i < SysSumme.Length; i++)
            {
                var jeBild = SysSumme[i] * MsJeTick / _bilder;
                if (jeBild < 0.01) continue;
                text += " | " + SysName[i] + " " + jeBild.ToString("F2")
                    + " (max " + Ms(SysHoechst[i]) + ")";
            }
            return text.Length == 0 ? " | Systeme unter 0,01 ms" : text;
        }

        private static string Ms(long ticks)
            => (ticks * MsJeTick).ToString("F1");
    }
}
