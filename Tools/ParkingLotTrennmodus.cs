using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;
using static ParkingLotTool.Tools.ParkingLotTexte;

namespace ParkingLotTool.Tools
{
    /**
     * DER TRENNMODUS: der Nutzer zieht die Teilflaechen selbst.
     *
     * WARUM ES IHN GIBT. Die automatische Zerlegung schneidet nach der Form -
     * und lag am 2026-09-01 an einer schiefen T-Form daneben. Schlimmer: sie
     * haengt an der PUNKTLISTE, nicht an der Form. Gemessen an den echten
     * Umrissen des Nutzers (`--zerlegung` im Prueflauf):
     *
     *   dasselbe L mit 6 Punkten   2 Teile
     *   dasselbe L mit 7 Punkten   6 Teile, VIER davon ohne Flaeche
     *   dasselbe T, andere Startecke   2 statt 6 Teile
     *
     * Ein Punkt auf einer geraden Kante aendert nichts an der Form - aber
     * alles an der Zerlegung. Statt die Rateregel zu verbessern, bekommt der
     * Nutzer den Stift: *"Es waere gut, wenn der User am besten selbst
     * bestimmen kann, wo geschnitten wird - rein nur fuer das Align."*
     *
     * DER ABLAUF, wie er ihn festgelegt hat:
     *
     *   1. "An Polygonlinie ausrichten" druecken.
     *   2. Der Knopf heisst jetzt "Trennung fertig". Man kann Schnitte
     *      ziehen - muss aber nicht.
     *   3. Ein Schnitt = zwei Polygonpunkte anklicken. Sie werden verbunden
     *      gezeigt.
     *   4. Rechtsklick auf eine Linie loescht sie; sonst nimmt Rechtsklick
     *      den letzten Schritt zurueck.
     *   5. "Trennung fertig" - ab hier gelten NUR die gezogenen Schnitte,
     *      die Automatik wird ignoriert. Dann laeuft das Ausrichten wie
     *      gewohnt: Flaeche, Linie, Flaeche, Linie.
     *
     * DREI REGELN, die er genannt hat oder die aus der Sache folgen:
     *
     *   - Die beiden Punkte duerfen NICHT benachbart sein; mindestens einer
     *     muss dazwischen liegen. Ein Dreieck laesst sich damit nicht
     *     trennen, ein Viereck schon.
     *   - Der Schnitt muss INNEN liegen. Bei einem L verbindet die Strecke
     *     zwischen den beiden Enden zwei echte Ecken und laeuft trotzdem
     *     durch die Luft (meine Ergaenzung, von ihm nicht genannt).
     *   - Zwei Schnitte duerfen sich nicht KREUZEN, sonst sind die
     *     entstehenden Flaechen nicht mehr eindeutig (ebenfalls meine
     *     Ergaenzung).
     *
     * GEMERKT WIRD DIE WELTLAGE, nicht die Punktnummer - dieselbe Lehre wie
     * bei den Zugaengen und den Bezugslinien.
     */
    public sealed partial class ParkingLotToolSystem
    {
        private readonly List<Teilflaechenschnitt> _trennschnitte
            = new List<Teilflaechenschnitt>();

        /** Der erste schon gewaehlte Punkt eines halben Schnitts, sonst -1. */
        private int _trennAnfang = -1;

        internal IReadOnlyList<Teilflaechenschnitt> Trennschnitte => _trennschnitte;
        internal int TrennAnfang => _trennAnfang;

        internal bool TrennmodusAktiv => Ausrichtwahl == Ausrichtschritt.Trennen;

        /** Laesst sich diese Form ueberhaupt trennen? */
        internal bool TrennungMoeglich => _closed && _points.Count >= 4;

        /** Setzt den ganzen Satz - fuer Rueckgaengig und den Bauzettel. */
        internal void SetzeTrennschnitte(IReadOnlyList<Teilflaechenschnitt> satz)
        {
            _trennschnitte.Clear();
            if (satz != null)
                foreach (var s in satz)
                    _trennschnitte.Add(new Teilflaechenschnitt { A = s.A, B = s.B });
        }

        internal void VergissTrennschnitte()
        {
            _trennschnitte.Clear();
            _trennAnfang = -1;
        }

        /**
         * DIE TEILUNG FOLGT DER FORM (Nutzer 2026-10-08).
         *
         * Bis hier merkten sich die Schnitte nur Positionen und hingen beim
         * Bau am jeweils naechsten Punkt. Wurde die Form abgebaut, blieben
         * sie stehen, dockten an falsche Punkte an, und "2 areas" stand
         * weiter im Panel. Ausserdem lief eine Linien- oder Flaechenwahl
         * weiter, wenn der Umriss aufging - sie schluckte dann die Klicks,
         * mit denen man den Umriss neu zeichnen wollte.
         *
         * Nach jeder Formaenderung (einmal je Geometriestand):
         *   - verschobene Eckpunkte: die Schnitt-Enden ziehen mit;
         *   - geloeschter Eckpunkt, beide Enden auf demselben Punkt oder
         *     Nachbarn: dieser Schnitt faellt weg;
         *   - Umriss offen oder weniger als vier Punkte: die ganze Teilung
         *     faellt weg, und jede laufende Auswahl endet.
         * Rueckgaengig holt alles zurueck: die Sicherung vor der Aenderung
         * enthaelt die Schnitte.
         */
        private int _teilungGeprueft = -1;
        private float2[] _teilungPunkte = System.Array.Empty<float2>();

        private void PflegeTeilung()
        {
            if (_teilungGeprueft == _geometryRevision) return;
            _teilungGeprueft = _geometryRevision;
            var vorher = _teilungPunkte;
            _teilungPunkte = _points.ToArray();

            if ((!_closed || _points.Count < 3) && AusrichtWahlAktiv)
            {
                Ausrichtwahl = Ausrichtschritt.Aus;
                AusrichtFlaeche = -1;
                _trennAnfang = -1;
                _schnitteVorTeilen = null;
                _uiSystem?.SetStatus(T("trennmodus.auswahlBeendetUmrissOffen"));
                Mod.log.Info("PLT-Ausrichten: Auswahl beendet, der Umriss ist offen.");
            }

            var aufgeloest = 0;
            if (_trennschnitte.Count != 0)
            {
                if (!_closed || _points.Count < 4)
                {
                    aufgeloest = _trennschnitte.Count;
                    _trennschnitte.Clear();
                    _trennAnfang = -1;
                }
                else
                {
                    for (var i = _trennschnitte.Count - 1; i >= 0; i--)
                    {
                        var schnitt = _trennschnitte[i];
                        var a = FolgePunkt(schnitt.A, vorher);
                        var b = FolgePunkt(schnitt.B, vorher);
                        var benachbart = a >= 0 && b >= 0
                            && ((a + 1) % _points.Count == b || (b + 1) % _points.Count == a);
                        if (a < 0 || b < 0 || a == b || benachbart)
                        {
                            _trennschnitte.RemoveAt(i);
                            aufgeloest++;
                            continue;
                        }
                        schnitt.A = _points[a];
                        schnitt.B = _points[b];
                    }
                }
            }

            if (aufgeloest > 0)
            {
                // Ohne Schnitt gibt es nur noch eine Flaeche - wie bei "Remove
                // split" gilt die erste Linie fuer alles.
                if (_trennschnitte.Count == 0 && _ausrichtungen.Count > 1)
                    _ausrichtungen.RemoveRange(1, _ausrichtungen.Count - 1);
                _uiSystem?.SetAusrichtwinkel(Ausrichtwinkel);
                _layoutDirty = _closed;
                _uiSystem?.SetStatus(_trennschnitte.Count == 0
                    ? T("trennmodus.teilungAufgeloest")
                    : TN("trennmodus.schnitteEntfallen", aufgeloest));
                Mod.log.Info("PLT-Trennmodus: " + aufgeloest
                    + " Schnitt(e) nach Formaenderung entfallen, "
                    + _trennschnitte.Count + " bleiben.");
            }
            AktualisiereTeilflaechen();
        }

        /**
         * Zu welchem heutigen Punkt gehoert ein gemerktes Schnitt-Ende?
         * -1, wenn sein Punkt verschwunden ist.
         *
         * Liegt dort noch genau ein Punkt, ist es dieser. Sonst wird der
         * Punkt im vorigen Stand gesucht: bei gleicher Punktzahl wurde er
         * verschoben und steht an derselben Stelle der Liste; hat sich die
         * Zahl geaendert, ist er geloescht worden. Ein Ende, das in keinem
         * Stand vorkommt (frisch aus dem Bauzettel), nimmt den naechsten
         * Punkt - so wie bisher.
         */
        private int FolgePunkt(float2 ende, float2[] vorher)
        {
            for (var i = 0; i < _points.Count; i++)
                if (math.distancesq(_points[i], ende) < 1e-6f) return i;
            for (var j = 0; j < vorher.Length; j++)
                if (math.distancesq(vorher[j], ende) < 1e-6f)
                    return vorher.Length == _points.Count ? j : -1;
            return NaechsterPunkt(ende);
        }

        /** Die Schnitte beim Betreten des Teilmodus - fuer "Abbrechen". */
        private List<Teilflaechenschnitt> _schnitteVorTeilen;

        /**
         * "SPLIT AREA" - DER EIGENE SCHRITT (Nutzer 2026-10-08).
         *
         * Vorher oder nachher, unabhaengig vom Ausrichten. Der Spieler zieht
         * Schnitte von Ecke zu Ecke; mit "Done splitting" geht es zur Wahl
         * je Flaeche, mit "Cancel" (oder Esc) gilt wieder, was vorher stand.
         */
        internal void BeginTeilen()
        {
            SetzeZoningModus(false);
            // Nur mit eingeschaltetem Ausrichten (Nutzer 2026-10-08): Teilen
            // gibt Flaechen eigene Linien - ohne Ausrichten gibt es keine.
            if (!_ausrichtungAn && !AusrichtWahlAktiv)
            {
                _uiSystem?.SetStatus(T("trennmodus.teilenNurMitAusrichten"));
                return;
            }
            if (!TrennungMoeglich)
            {
                _uiSystem?.SetStatus(T("trennmodus.teilenBrauchtUmriss"));
                return;
            }
            _schnitteVorTeilen = _trennschnitte
                .Select(s => new Teilflaechenschnitt { A = s.A, B = s.B }).ToList();
            _trennAnfang = -1;
            AusrichtFlaeche = -1;
            AktualisiereTeilflaechen();
            Ausrichtwahl = Ausrichtschritt.Trennen;
            _uiSystem?.SetStatus(T("alignPick.splitModeConnectTwoOutlinePoints"));
            Mod.log.Info("PLT-Trennmodus: gestartet, " + _points.Count
                + " Punkte, " + _trennschnitte.Count + " vorhandene Schnitt(e).");
        }

        /** Abbrechen im Teilmodus: die Schnitte von vorher gelten wieder. */
        internal void BrecheTeilenAb(string grund)
        {
            if (!TrennmodusAktiv) return;
            var vorher = _schnitteVorTeilen;
            _schnitteVorTeilen = null;
            if (vorher != null && !GleicheSchnitte(vorher, _trennschnitte))
            {
                SetzeTrennschnitte(vorher);
                AktualisiereTeilflaechen();
                _geometryRevision++;
                _layoutDirty = _closed;
            }
            _trennAnfang = -1;
            Ausrichtwahl = Ausrichtschritt.Aus;
            AusrichtFlaeche = -1;
            _uiSystem?.SetStatus(T("trennmodus.teilenAbgebrochen"));
            Mod.log.Info("PLT-Trennmodus: abgebrochen (" + grund + "), "
                + _trennschnitte.Count + " Schnitt(e) wie vorher.");
        }

        private static bool GleicheSchnitte(List<Teilflaechenschnitt> a, List<Teilflaechenschnitt> b)
        {
            if (a.Count != b.Count) return false;
            for (var i = 0; i < a.Count; i++)
                if (math.distancesq(a[i].A, b[i].A) > 1e-6f || math.distancesq(a[i].B, b[i].B) > 1e-6f)
                    return false;
            return true;
        }

        /**
         * "REMOVE SPLIT" - zurueck zu einer Flaeche.
         *
         * Die erste Linie bleibt: sie war die Vorgabe fuer alle Flaechen und
         * gilt jetzt fuer den ganzen Parkplatz. Die Linien der uebrigen
         * Flaechen fallen weg - es gibt diese Flaechen nicht mehr. Mit
         * Rueckgaengig wieder herstellbar.
         */
        internal void EntferneTeilung()
        {
            if (_trennschnitte.Count == 0) return;
            if (TrennmodusAktiv) { Ausrichtwahl = Ausrichtschritt.Aus; _schnitteVorTeilen = null; }
            var before = CaptureUndoState();
            VergissTrennschnitte();
            if (_ausrichtungen.Count > 1)
                _ausrichtungen.RemoveRange(1, _ausrichtungen.Count - 1);
            AktualisiereTeilflaechen();
            _uiSystem?.SetAusrichtwinkel(Ausrichtwinkel);
            _geometryRevision++;
            _layoutDirty = _closed;
            CommitUndoState(before, () => T("undo.teilungEntfernt"));
            _uiSystem?.SetStatus(T("trennmodus.teilungEntfernt"));
            Mod.log.Info("PLT-Trennmodus: Teilung entfernt, "
                + _ausrichtungen.Count + " Zuweisung(en) bleiben.");
        }

        /**
         * Die Punktnummern eines gemerkten Schnitts - oder (-1, -1).
         *
         * Wie bei den Bezugslinien ohne Metergrenze: zu jedem gemerkten Ende
         * wird der naechste heutige Punkt gesucht. Verschwindet einer, faellt
         * der Schnitt fuer diesen Umriss aus, ohne geloescht zu werden.
         */
        private (int A, int B) SchnittPunkte(Teilflaechenschnitt schnitt)
        {
            var a = NaechsterPunkt(schnitt.A);
            var b = NaechsterPunkt(schnitt.B);
            return a == b ? (-1, -1) : (a, b);
        }

        /** Kreuzt eine neue Strecke einen schon gezogenen Schnitt? */
        private bool KreuztVorhandenen(int i, int j)
        {
            var a1 = _points[i];
            var a2 = _points[j];
            foreach (var schnitt in _trennschnitte)
            {
                var (k, l) = SchnittPunkte(schnitt);
                if (k < 0 || l < 0) continue;
                // Ein gemeinsamer Endpunkt ist kein Kreuzen.
                if (k == i || k == j || l == i || l == j) continue;
                if (StreckenKreuzen(a1, a2, _points[k], _points[l])) return true;
            }
            return false;
        }

        private static bool StreckenKreuzen(
            float2 a1, float2 a2, float2 b1, float2 b2)
        {
            float Seite(float2 p, float2 q, float2 r)
                => (q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x);

            var d1 = Seite(a1, a2, b1);
            var d2 = Seite(a1, a2, b2);
            var d3 = Seite(b1, b2, a1);
            var d4 = Seite(b1, b2, a2);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
                && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        /** Welcher gezogene Schnitt liegt unter dem Zeiger? -1 wenn keiner. */
        private int TrennlinieUnter(float2 punkt)
        {
            var treffer = -1;
            var beste = EdgeHitDistance * EdgeHitDistance;
            for (var i = 0; i < _trennschnitte.Count; i++)
            {
                var (a, b) = SchnittPunkte(_trennschnitte[i]);
                if (a < 0 || b < 0) continue;
                var d = math.lengthsq(punkt
                    - NearestOnSegment(punkt, _points[a], _points[b]));
                if (d > beste) continue;
                beste = d;
                treffer = i;
            }
            return treffer;
        }

        private readonly List<(float3 A, float3 B)> _trennlinien
            = new List<(float3, float3)>();

        /**
         * Die gezogenen Schnitte als Weltstrecken, samt Hoehe.
         *
         * Wiederverwendete Liste: sie wird jedes Bild gebraucht, und je Bild
         * eine neue anzulegen waere Muell fuer nichts.
         */
        internal IReadOnlyList<(float3 A, float3 B)> Trennlinien()
        {
            _trennlinien.Clear();
            foreach (var schnitt in _trennschnitte)
            {
                var (a, b) = SchnittPunkte(schnitt);
                if (a < 0 || b < 0) continue;
                if (a >= _worldPoints.Count || b >= _worldPoints.Count) continue;
                _trennlinien.Add((_worldPoints[a], _worldPoints[b]));
            }
            return _trennlinien;
        }

        /**
         * Nimmt die Klicks im Trennmodus. `true` heisst: verbraucht.
         */
        private bool HandleTrennmodus(bool secondaryPressed, bool escapePressed)
        {
            if (escapePressed)
            {
                BrecheTeilenAb("Esc");
                return true;
            }
            if (secondaryPressed)
            {
                /*
                 * RECHTSKLICK IST ZURUECK - in dieser Reihenfolge, so
                 * gewuenscht: liegt eine Linie unter dem Zeiger, verschwindet
                 * SIE; sonst faellt der halbe Schnitt weg; sonst der letzte
                 * ganze. Ohne die erste Stufe muesste man rueckwaerts durch
                 * alle Schnitte, um einen bestimmten loszuwerden.
                 */
                if (_trennAnfang >= 0)
                {
                    _trennAnfang = -1;
                    _uiSystem?.SetStatus(T("trennmodus.pointDiscarded"));
                    return true;
                }
                var unter = _hasHover
                    ? TrennlinieUnter(new float2(_hoverPosition.x, _hoverPosition.z))
                    : -1;
                if (unter >= 0)
                {
                    var before = CaptureUndoState();
                    _trennschnitte.RemoveAt(unter);
                    NachTrennaenderung(before, () => T("trennmodus.cutDeleted"));
                    return true;
                }
                if (_trennschnitte.Count > 0)
                {
                    var before = CaptureUndoState();
                    _trennschnitte.RemoveAt(_trennschnitte.Count - 1);
                    NachTrennaenderung(before, () => T("trennmodus.lastCutDeleted"));
                    return true;
                }
                BrecheTeilenAb("Rechtsklick");
                return true;
            }

            // Direkt an der Maus, nicht ueber die ProxyAction - CS2 maskiert
            // sie, und der Rest des Werkzeugs liest laengst so.
            var maus = UnityEngine.InputSystem.Mouse.current;
            if (maus == null || !maus.leftButton.wasPressedThisFrame) return true;
            if (!_hasHover) return true;

            var getroffen = PunktUnterZeiger();
            if (getroffen < 0)
            {
                _uiSystem?.SetStatus(T("trennmodus.noOutlinePointUnderTheCursor"));
                return true;
            }

            if (_trennAnfang < 0)
            {
                _trennAnfang = getroffen;
                _uiSystem?.SetStatus(T("trennmodus.nowPickTheSecondPoint"));
                return true;
            }
            if (getroffen == _trennAnfang)
            {
                _trennAnfang = -1;
                _uiSystem?.SetStatus(T("trennmodus.pointDiscarded"));
                return true;
            }

            var i = _trennAnfang;
            var j = getroffen;
            if ((i + 1) % _points.Count == j || (j + 1) % _points.Count == i)
            {
                _uiSystem?.SetStatus(T("trennmodus.thoseTwoPointsAreNeighboursAt"));
                return true;
            }
            var site = new double2[_points.Count];
            for (var k = 0; k < _points.Count; k++)
                site[k] = new double2(_points[k].x, _points[k].y);
            if (!ParkingGeometry.SchnittLiegtInnen(site, i, j))
            {
                _uiSystem?.SetStatus(T("trennmodus.thatCutWouldRunOutsideThe"));
                _trennAnfang = -1;
                return true;
            }
            if (KreuztVorhandenen(i, j))
            {
                _uiSystem?.SetStatus(T("trennmodus.thatCutCrossesAnExistingOne"));
                _trennAnfang = -1;
                return true;
            }

            var vorher = CaptureUndoState();
            _trennschnitte.Add(new Teilflaechenschnitt
            {
                A = _points[i],
                B = _points[j],
            });
            _trennAnfang = -1;
            NachTrennaenderung(vorher, () => T("trennmodus.cutDrawn"));
            return true;
        }

        /** Der Punkt unter dem Zeiger - eigener Griff, grosszuegiger als der Hover. */
        private int PunktUnterZeiger()
        {
            var cursor = new float2(_hoverPosition.x, _hoverPosition.z);
            var treffer = -1;
            var beste = PointHitDistance * PointHitDistance;
            for (var i = 0; i < _points.Count; i++)
            {
                var d = math.lengthsq(cursor - _points[i]);
                if (d > beste) continue;
                beste = d;
                treffer = i;
            }
            return treffer;
        }

        private void NachTrennaenderung(ParkingLotUndoSnapshot vorher, System.Func<string> was)
        {
            AktualisiereTeilflaechen();
            _geometryRevision++;
            _layoutDirty = _closed;
            CommitUndoState(vorher, was);
            _uiSystem?.SetStatus(TN("trennmodus.schnitte", _trennschnitte.Count));
        }

        /**
         * "Trennung fertig" - ab hier gelten nur noch die gezogenen Schnitte.
         *
         * Auch ohne Schnitt: dann ist der ganze Umriss EINE Teilflaeche. So
         * gewollt - "die automatisierten Trennflaechen werden ignoriert".
         * Was gezeichnet ist, gilt; was nicht gezeichnet ist, trennt nicht.
         */
        internal void BeendeTrennmodus()
        {
            if (!TrennmodusAktiv)
            {
                /*
                 * SICHERHEITSNETZ, kein Normalfall.
                 *
                 * Seit `Ausrichtwahl` die Anzeige selbst setzt, kann der
                 * Knopf gar nicht mehr "Trennung fertig" heissen, ohne dass
                 * der Trennmodus laeuft. Sollte die Bindung doch einmal
                 * auseinanderlaufen - ein Neuladen der Oberflaeche, eine
                 * Fassung, die ich noch nicht kenne - dann befreit EIN Druck
                 * den Knopf, statt ins Leere zu laufen. Genau das Ausbleiben
                 * hat den Nutzer am 2026-09-01 festgesetzt.
                 */
                _uiSystem?.SetTrennmodus(false);
                return;
            }
            _trennAnfang = -1;
            _schnitteVorTeilen = null;
            AktualisiereTeilflaechen();
            AusrichtFlaeche = _teilflaechen.Count > 1 ? -1 : 0;
            Ausrichtwahl = _teilflaechen.Count > 1
                ? Ausrichtschritt.Flaeche : Ausrichtschritt.Linie;
            _uiSystem?.SetStatus(_teilflaechen.Count > 1
                ? T("trennmodus.nowPickASubArea")
                : T("trennmodus.nowPickTheReferenceLine"));
            Mod.log.Info("PLT-Trennmodus: beendet mit " + _trennschnitte.Count
                + " Schnitt(en), " + _teilflaechen.Count + " Teilflaeche(n).");
        }
    }
}
