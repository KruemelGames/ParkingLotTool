using System;
using System.Collections.Generic;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * Die Teilflaechen des Umrisses, als anklickbare Kacheln.
     *
     * WOZU. Der Nutzer soll je Teilflaeche eine eigene Bezugslinie waehlen
     * koennen: erst die Flaeche, dann die Linie, dann entweder beenden oder
     * die naechste. Was er nicht anfasst, folgt der ersten Zuweisung.
     *
     * FRUEHER RECHTECKE, SEIT DEM 2026-09-15 DIE ECHTE FORM. Die Rechtecke
     * kamen aus einer Ansage des Nutzers: *"Als Userfeedback fuers Auswaehlen
     * der Teilflaechen koennen wir einfach grob Rechtecke anzeigen, die
     * anklickbar sind, statt der Preview."* Der Grund dahinter war, dass die
     * Vorschau damals keine Flaeche fuellen konnte - ein Rechteck war das
     * einzige, was man ueberhaupt sichtbar machen konnte.
     *
     * Seit die Vorschau ein Dreiecksnetz zeichnet, faellt der Grund weg, und
     * das Rechteck war messbar schaedlich: bei einem diagonal geteilten
     * Viereck ueberdecken sich die Huellrechtecke fast vollstaendig, und eine
     * der beiden Haelften war gar nicht anzuklicken.
     *
     * WARUM DIE ZUORDNUNG UEBER EINEN PUNKT LAEUFT. Die Zerlegung wird bei
     * jeder Polygonaenderung neu gerechnet; aus zwei Teilen koennen drei
     * werden, und die Reihenfolge ist nicht zugesichert. Eine Nummer als
     * Merkmal haette dieselbe Falle wie die Kantennummer bei den Zugaengen,
     * die dem Nutzer heute die Zufahrten quer ueber den Parkplatz geschoben
     * hat. Gemerkt wird deshalb ein PUNKT im Inneren; wiedergefunden wird die
     * Teilflaeche, die ihn enthaelt.
     */
    public sealed partial class ParkingLotToolSystem
    {
        internal sealed class Teilflaeche
        {
            /** Die Zerlegungsform, in Weltkoordinaten (XZ). */
            internal float2[] Umriss;
            /** Huellrechteck - nur noch schnelle Vorpruefung, kein Klickziel. */
            internal float2 Min;
            internal float2 Max;
            /** Ein Punkt im Inneren; er ist das Merkmal der Zuweisung. */
            internal float2 Anker;
        }

        private readonly List<Teilflaeche> _teilflaechen = new List<Teilflaeche>();

        internal IReadOnlyList<Teilflaeche> Teilflaechen => _teilflaechen;

        /**
         * Rechnet die Teilflaechen neu.
         *
         * Billig genug fuer jede Polygonaenderung: dieselbe Zerlegung laeuft
         * beim Bauen ohnehin, hier nur einmal statt je Durchgang.
         */
        private void AktualisiereTeilflaechen()
        {
            _teilflaechen.Clear();
            if (!_closed || _points.Count < 3) return;

            var site = new double2[_points.Count];
            for (var i = 0; i < _points.Count; i++)
                site[i] = new double2(_points[i].x, _points[i].y);

            // Handschnitte schlagen die Automatik - vollstaendig. Siehe
            // ParkingLotTrennmodus.
            var teile = _trennschnitte.Count != 0
                ? ParkingGeometry.TeilflaechenAusSchnitten(site, _trennschnitte)
                : ParkingGeometry.Teilflaechen(site);
            foreach (var teil in teile)
            {
                if (teil == null || teil.Length < 3) continue;
                var umriss = new float2[teil.Length];
                var min = new float2(float.MaxValue);
                var max = new float2(float.MinValue);
                var summe = float2.zero;
                for (var i = 0; i < teil.Length; i++)
                {
                    var p = new float2((float)teil[i].x, (float)teil[i].y);
                    umriss[i] = p;
                    min = math.min(min, p);
                    max = math.max(max, p);
                    summe += p;
                }

                _teilflaechen.Add(new Teilflaeche
                {
                    Umriss = umriss,
                    Min = min,
                    Max = max,
                    // Der Schwerpunkt der Ecken liegt bei konvexen Teilen
                    // immer innen - und konvex sind sie nach der Zerlegung.
                    Anker = summe / teil.Length,
                });
            }
        }

        /**
         * Zwei Anker gelten als derselbe Ort.
         *
         * EINE Regel fuer beide Seiten: die Zuweisung sucht damit ihre
         * Teilflaeche wieder, und die Anzeige faerbt damit die zugewiesenen
         * ein. Stuenden hier zwei Zahlen, koennte eine Flaeche zugewiesen
         * sein und trotzdem unbenutzt aussehen.
         */
        internal static bool GleicherAnker(float2 a, float2 b)
            => math.distancesq(a, b) < 0.01f;

        private readonly List<bool> _teilflaecheZugewiesen = new List<bool>();

        /**
         * Je Teilflaeche: haengt an ihr schon eine Bezugslinie?
         *
         * Das Overlay zeichnet zugewiesene Flaechen blasser - so sieht man
         * beim Ausrichten, was man schon abgearbeitet hat. Die Liste wird
         * wiederverwendet; je Bild eine neue anzulegen waere Muell fuer
         * nichts.
         */
        internal IReadOnlyList<bool> ZugewieseneTeilflaechen()
        {
            _teilflaecheZugewiesen.Clear();
            for (var i = 0; i < _teilflaechen.Count; i++)
            {
                var treffer = false;
                for (var k = 0; k < _ausrichtungen.Count; k++)
                    if (GleicherAnker(_ausrichtungen[k].Anker,
                        _teilflaechen[i].Anker))
                    {
                        treffer = true;
                        break;
                    }
                _teilflaecheZugewiesen.Add(treffer);
            }
            return _teilflaecheZugewiesen;
        }

        /**
         * Welche Teilflaeche soll gerade wieder sichtbar sein? -1 fuer keine.
         *
         * Eine zugewiesene Kachel liegt bei 5 Prozent - sie soll ja aus dem
         * Weg sein. Wer nachsehen will, WELCHE Flaeche das eigentlich war,
         * haelt Umschalt und faehrt darueber; dann steht sie kurz wieder auf
         * dem offenen Wert. Wunsch des Nutzers, und der einzige Weg, eine
         * abgehakte Flaeche noch einmal zu pruefen, ohne die Zuweisung
         * anzufassen.
         *
         * Umschalt ist hier frei: im Ausrichtmodus verbraucht die Auswahl
         * jeden Klick, und die Kanten-Sonderfunktionen sind abgeschaltet.
         */
        /**
         * Die Flaeche unter dem Zeiger - fuer die Rueckmeldung beim Zielen.
         *
         * Getrennt von `HervorgehobeneTeilflaeche`: die haengt an Umschalt und
         * holt eine abgehakte Flaeche kurz zurueck. Diese hier sagt schlicht
         * "das hier wuerdest du jetzt treffen", und genau das hat gefehlt.
         */
        internal int ZeigerTeilflaeche()
        {
            if (!_hasHover) return -1;
            // NUR in der Flaechenwahl. Steht die Flaeche schon, kann man
            // keine andere anklicken - dann darf auch keine aufleuchten, als
            // liesse sie sich waehlen.
            if (Ausrichtwahl != Ausrichtschritt.Flaeche
                && !TrennmodusAktiv) return -1;
            return TeilflaecheUnter(
                new float2(_hoverPosition.x, _hoverPosition.z));
        }

        private int HervorgehobeneTeilflaeche()
        {
            if (!AusrichtWahlAktiv || !_hasHover || !ShiftGehalten()) return -1;
            return TeilflaecheUnter(
                new float2(_hoverPosition.x, _hoverPosition.z));
        }

        /**
         * Welche Teilflaeche liegt unter diesem Weltpunkt? -1 wenn keine.
         *
         * GEPRUEFT WIRD DIE ECHTE FORM.
         *
         * Hier stand eine Pruefung gegen die Huellrechtecke, mit der Regel
         * "bei Ueberlappung gewinnt das kleinere". Die geht bei einem
         * diagonal geteilten Viereck nachweislich schief: beide Haelften
         * haben nahezu dasselbe Huellrechteck, also gewinnt immer dieselbe -
         * und die andere ist ueberhaupt nicht anzuklicken. Der Nutzer am
         * 2026-09-15: *"Es fuehlt sich an als koennte ich nur eins anwaehlen
         * und das andere nicht."* Genau das.
         *
         * Das Rechteck war urspruenglich als GROBES Klickziel gedacht, weil
         * die Zerlegungsform spitz zulaufen kann. Dieser Einwand faellt weg,
         * seit die Flaeche in ihrer echten Form eingefaerbt wird: man klickt
         * jetzt genau das an, was man sieht.
         *
         * Das Huellrechteck bleibt als schnelle Vorpruefung stehen - es
         * spart den Punkt-im-Vieleck-Test fuer alles, was weit weg liegt.
         */
        private int TeilflaecheUnter(float2 punkt)
        {
            for (var i = 0; i < _teilflaechen.Count; i++)
            {
                var teil = _teilflaechen[i];
                if (punkt.x < teil.Min.x || punkt.x > teil.Max.x
                    || punkt.y < teil.Min.y || punkt.y > teil.Max.y) continue;
                if (ImVieleck(teil.Umriss, punkt)) return i;
            }
            return -1;
        }

        /** Strahlverfahren: ungerade Zahl von Kantenschnitten heisst innen. */
        private static bool ImVieleck(float2[] umriss, float2 punkt)
        {
            if (umriss == null || umriss.Length < 3) return false;
            var innen = false;
            for (int i = 0, j = umriss.Length - 1; i < umriss.Length; j = i++)
            {
                var a = umriss[i];
                var b = umriss[j];
                if (a.y > punkt.y != b.y > punkt.y
                    && punkt.x < (b.x - a.x) * (punkt.y - a.y) / (b.y - a.y) + a.x)
                    innen = !innen;
            }
            return innen;
        }
    }
}
