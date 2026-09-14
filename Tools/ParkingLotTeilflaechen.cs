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
     * WARUM RECHTECKE UND NICHT DIE ECHTE FORM. Ansage des Nutzers: *"Als
     * Userfeedback fuers Auswaehlen der Teilflaechen koennen wir einfach grob
     * Rechtecke anzeigen, die anklickbar sind, statt der Preview."* Ein grobes
     * Rechteck ueber der Flaeche ist eindeutig anzuklicken und verdeckt beim
     * Zeigen nichts, was man gerade beurteilen will. Die echte Zerlegungsform
     * waere genauer und im Zweifel unbedienbar - sie kann spitz zulaufen.
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
            /** Das grobe Rechteck darueber - das ist das Klickziel. */
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
        private int HervorgehobeneTeilflaeche()
        {
            if (!AusrichtWahlAktiv || !_hasHover || !ShiftGehalten()) return -1;
            return TeilflaecheUnter(
                new float2(_hoverPosition.x, _hoverPosition.z));
        }

        /** Welche Teilflaeche liegt unter diesem Weltpunkt? -1 wenn keine. */
        private int TeilflaecheUnter(float2 punkt)
        {
            // Das Rechteck ist das Klickziel, nicht die echte Form - so hat
            // der Nutzer es gewollt. Bei Ueberlappung gewinnt das kleinere:
            // sonst waere ein schmaler Schenkel nie zu treffen, weil das
            // Rechteck des grossen Teils ueber ihm liegt.
            var treffer = -1;
            var besteFlaeche = float.MaxValue;
            for (var i = 0; i < _teilflaechen.Count; i++)
            {
                var teil = _teilflaechen[i];
                if (punkt.x < teil.Min.x || punkt.x > teil.Max.x
                    || punkt.y < teil.Min.y || punkt.y > teil.Max.y) continue;
                var groesse = (teil.Max.x - teil.Min.x) * (teil.Max.y - teil.Min.y);
                if (groesse >= besteFlaeche) continue;
                besteFlaeche = groesse;
                treffer = i;
            }
            return treffer;
        }
    }
}
