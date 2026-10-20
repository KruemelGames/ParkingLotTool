using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using static ParkingLotTool.Tools.ParkingLotPreviewStyle;
using Kurven = ParkingLotTool.Geometry.Strassenkurve;

namespace ParkingLotTool.Tools
{
    /**
     * STRASSENKURVE FOLGEN (Etappe 3, 19.10.2026).
     *
     * Der Nutzer klickt zwei Punkte an derselben gekruemmten Strasse; die
     * Kante dazwischen folgt dann dem Bordstein. Die Zwischenpunkte der
     * eingefrorenen Abtastung stehen als normale Formpunkte in `_points` -
     * Vorschau, Layout und Flaechen rechnen damit ohne zweite Tessellierung.
     * Sie haben aber keine Griffe: anfassen kann man nur die beiden Enden.
     *
     * Nur beim Setzen NEUER Parkplaetze (kein Edit, kein Sync). Der Bauzettel
     * speichert die Kurve mit Formel (Etappe 4, ParkingLotKurvenzettelPuffer);
     * Edit fuehrt sie wieder als Kurve, ohne neu abzutasten.
     */
    public sealed partial class ParkingLotToolSystem
    {
        internal sealed class Kurvenkante
        {
            internal float2 Von, Bis;
            internal float2[] Zwischen;
            /** Formel und Zusammenfassung fuer den Bauzettel; unveraenderlich, darf geteilt werden. */
            internal Geometry.Kurvenzettel Zettel;
            /**
             * Der urspruenglich verfolgte ganze Verlauf (Von -> Bis), wenn ein
             * Kantenzug die Kurve gekuerzt hat. Zieht man die Kante wieder heraus,
             * gleitet das Ende darauf zurueck (Nutzer 19.10.: sonst lief die
             * verlaengerte Sehne ueber die Strasse). null = Zwischen ist der ganze Verlauf.
             * Unveraenderlich, darf geteilt werden; nicht im Bauzettel.
             */
            internal float2[] Ganz;

            internal Kurvenkante Kopie() => new Kurvenkante
            {
                Von = Von, Bis = Bis, Zwischen = (float2[])Zwischen.Clone(), Zettel = Zettel, Ganz = Ganz,
            };
        }

        private readonly List<Kurvenkante> _kurvenkanten = new List<Kurvenkante>();
        private readonly HashSet<float2> _kurvenZwischen = new HashSet<float2>();
        private int _kurvenformRevision = -1;

        private bool KurveFolgenAn => Mod.Optionen?.StrassenkurveFolgen == true
            && !_bauarbeiter && !IsEditing;

        /** Zwischenpunkte einer Kurve sind keine Griffe. */
        private bool IstKurvenzwischenpunkt(int index)
            => index >= 0 && index < _points.Count && _kurvenZwischen.Contains(_points[index]);

        private bool HatKurvenkanten => _kurvenkanten.Count > 0;

        /**
         * Folgt die Kante `von` -> `bis` einer gekruemmten Strasse, kommen die
         * Zwischenpunkte dazwischen. Gerade, Ecke, Kreuzung oder ein Fehler:
         * die Kante bleibt, wie sie ist.
         */
        private void UebernimmKurve(int von, int bis)
        {
            if (!KurveFolgenAn || von < 0 || bis < 0 || von >= _points.Count || bis >= _points.Count
                || von == bis) return;
            if (!_kurvenPunkte.TryGetValue(_points[von], out var a)
                || !_kurvenPunkte.TryGetValue(_points[bis], out var b)) return;
            var e = VerfolgeKurvenpaar(a, b, 0);
            if (e == null || !e.Folgt || e.Grund != Kurven.Grund.Keiner
                || e.Punkte == null || e.Punkte.Length < 3) return;
            if (FolgtSchonKurve(e))
            {
                Mod.log.Info("PLT-Kurve: Kante bleibt gerade - die Form folgt diesem Bordstein schon.");
                return;
            }

            var vonPunkt = _points[von];
            var bisPunkt = _points[bis];
            var zwischen = new float2[e.Punkte.Length - 2];
            for (int i = 0; i < zwischen.Length; i++) zwischen[i] = e.Punkte[i + 1].xz;

            // Die Schliesskante haengt hinten an, jede andere hinter `von`.
            int stelle = bis == 0 && von == _points.Count - 1 ? _points.Count : von + 1;
            SyncSnapAxes();
            for (int i = 0; i < zwischen.Length; i++)
            {
                _points.Insert(stelle + i, zwischen[i]);
                _worldPoints.Insert(stelle + i, e.Punkte[i + 1]);
                _pointAxes.Insert(stelle + i, float2.zero);
                _kurvenZwischen.Add(zwischen[i]);
            }
            _kurvenkanten.Add(new Kurvenkante
            {
                Von = vonPunkt, Bis = bisPunkt, Zwischen = zwischen,
                Zettel = Geometry.Kurvenzettel.Aus(e, vonPunkt, bisPunkt),
            });
            _kurvenformRevision = -1;
            Mod.log.Info($"PLT-Kurve: Kante uebernommen, {zwischen.Length} Zwischenpunkte, Radius {e.Radius:F1} m.");
        }

        /**
         * EINE KURVE FOLGT NIE EINEM BORDSTEIN, DEM DIE FORM SCHON FOLGT (20.10.).
         *
         * U-foermige Strasse: beide offenen Enden liegen am selben Bordstein.
         * Die Schliesskante wuerde sonst die ganze Rundung zuruecklaufen, die
         * Form liefe auf sich selbst zurueck ("Pingpong", Nutzer). Liegt der
         * neue Weg zur Haelfte oder mehr auf einer vorhandenen Kurve, bleibt
         * die Kante gerade.
         */
        private bool FolgtSchonKurve(Kurven.Ergebnis e)
        {
            if (e?.Punkte == null || e.Punkte.Length < 3 || _kurvenkanten.Count == 0) return false;
            int innen = e.Punkte.Length - 2, belegt = 0;
            for (int i = 1; i < e.Punkte.Length - 1; i++)
            {
                var p = e.Punkte[i].xz;
                foreach (var k in _kurvenkanten)
                {
                    var vorher = k.Von;
                    bool trifft = false;
                    for (int j = 0; j <= k.Zwischen.Length && !trifft; j++)
                    {
                        var naechster = j < k.Zwischen.Length ? k.Zwischen[j] : k.Bis;
                        var r = naechster - vorher;
                        float l2 = math.lengthsq(r);
                        float t = l2 > 0f ? math.clamp(math.dot(p - vorher, r) / l2, 0f, 1f) : 0f;
                        trifft = math.distance(p, vorher + r * t) <= (float)Kurven.Pfeilhoehe + .05f;
                        vorher = naechster;
                    }
                    if (trifft) { belegt++; break; }
                }
            }
            return belegt * 2 >= innen;
        }

        /** Nach dem Ziehen eines Endpunkts: beide Nachbarkanten neu versuchen. */
        private void UebernimmKurvenAn(int index)
        {
            if (!KurveFolgenAn || index < 0 || index >= _points.Count) return;
            var punkt = _points[index];
            int n = _points.Count;
            if (_closed || index + 1 < n)
            {
                int naechster = (index + 1) % n;
                if (naechster != index) UebernimmKurve(index, naechster);
            }
            index = _points.IndexOf(punkt);
            n = _points.Count;
            if (index >= 0 && (_closed || index > 0))
            {
                int voriger = (index - 1 + n) % n;
                if (voriger != index) UebernimmKurve(voriger, index);
            }
        }

        /** Loest jede Kurve, die an diesem Endpunkt haengt; ihre Kante wird gerade. */
        private void LoeseKurvenAn(float2 anker)
        {
            for (int i = _kurvenkanten.Count - 1; i >= 0; i--)
            {
                var k = _kurvenkanten[i];
                if (!math.all(k.Von == anker) && !math.all(k.Bis == anker)) continue;
                EntferneKurvenkante(i);
            }
        }

        private void EntferneKurvenkante(int i)
        {
            var k = _kurvenkanten[i];
            _kurvenkanten.RemoveAt(i);
            foreach (var p in k.Zwischen)
            {
                _kurvenZwischen.Remove(p);
                int index = _points.IndexOf(p);
                if (index >= 0) EntfernePunkt(index);
            }
            _geometryRevision++;
            if (_closed) _layoutDirty = true;
            // Blieb weniger als ein Dreieck uebrig (U aus EINER Kurve plus Schliesskante),
            // geht es wie nach dem Loeschen zurueck ins Zeichnen - sonst hing das Werkzeug
            // mit einem geschlossenen Zweipunkt-Umriss fest (20.10.).
            if (_closed && _points.Count < MinPolygonPoints) NachDemLoeschen();
        }

        /**
         * Haelt Kurven und Formpunkte zusammen, nach jeder Geometrieaenderung.
         * Fehlt ein Endpunkt (geloescht, rechtsgeklickt), faellt die ganze
         * Kurve weg. Fehlt nur ein Zwischenpunkt (Kante gezogen oder geloescht),
         * werden die restlichen zu normalen Punkten.
         */
        private void PflegeKurvenform()
        {
            if (_kurvenformRevision == _geometryRevision) return;
            for (int i = _kurvenkanten.Count - 1; i >= 0; i--)
            {
                var k = _kurvenkanten[i];
                if (!_points.Contains(k.Von) || !_points.Contains(k.Bis))
                {
                    EntferneKurvenkante(i);
                    continue;
                }
                bool vollstaendig = true;
                foreach (var p in k.Zwischen) if (!_points.Contains(p)) { vollstaendig = false; break; }
                if (vollstaendig) continue;
                foreach (var p in k.Zwischen) _kurvenZwischen.Remove(p);
                _kurvenkanten.RemoveAt(i);
            }
            _kurvenformRevision = _geometryRevision;
        }

        /**
         * Ist eine uebernommene Kurve fuer die geschlossene Form zu eng (die
         * Randstrasse rueckt zur Kruemmungsmitte hin)? Dann gibt es eine
         * Fehlermeldung und keine Vorschau - Nutzerentscheidung vom 18.10.
         */
        private string _kurvenformFehlerLog;

        private bool KurvenformFehler(out Kurven.Grund grund)
        {
            grund = Kurven.Grund.Keiner;
            if (_kurvenkanten.Count == 0) return false;
            int innen = Kurveninnenseite();
            foreach (var k in _kurvenkanten)
            {
                if (!_kurvenPunkte.TryGetValue(k.Von, out var a) || !_kurvenPunkte.TryGetValue(k.Bis, out var b))
                    continue;
                var e = VerfolgeKurvenpaar(a, b, innen);
                if (e != null && !e.Folgt && !e.OhneFolgen)
                {
                    grund = e.Grund;
                    // Einmal je Zustand: ohne diese Zeile war am 19.10. nicht
                    // nachvollziehbar, warum eine Kurve keine Vorschau bekam.
                    var zeile = $"PLT-Kurve: keine Vorschau, Grund {e.Grund}, R min {e.Radius:F1} m, "
                        + $"Innenversatz {_kurvenVersatz:F1} m, Innenseite {innen}, Randstrassen {_kurvenRandstrasse}, "
                        + $"{k.Zwischen.Length} Zwischenpunkte.";
                    if (zeile != _kurvenformFehlerLog)
                    {
                        _kurvenformFehlerLog = zeile;
                        Mod.log.Info(zeile);
                    }
                    return true;
                }
            }
            _kurvenformFehlerLog = null;
            return false;
        }

        /** Rechtsklick auf einen Zwischenpunkt: die ganze Kurve geht, nicht ein Punkt. */
        private bool EntferneKurveMitPunkt(float2 punkt)
        {
            for (int i = 0; i < _kurvenkanten.Count; i++)
            {
                foreach (var p in _kurvenkanten[i].Zwischen)
                {
                    if (!math.all(p == punkt)) continue;
                    EntferneKurvenkante(i);
                    return true;
                }
            }
            return false;
        }

        /**
         * Die gestrichelte Zeigerlinie folgt der Kurve, die beim Klick entstuende -
         * auch beim Schliessen. Eine gerade Linie dort hiesse fuer den Nutzer
         * "hier geht keine Kurve" (19.10.).
         */
        private float3[] Zeigerkurve()
        {
            if (!KurveFolgenAn || _closed || _kurvenVorschlag == null
                || _kurvenVorschlag.Grund != Kurven.Grund.Keiner || _kurvenVorschlag.Punkte == null
                || _kurvenVorschlag.Punkte.Length < 3) return null;
            return _kurvenVorschlag.Punkte;
        }

        private (Kurven.Fang, Kurven.Fang, Kurven.Fang) _ziehSchluessel;
        private bool _ziehBereit;
        private float3[] _ziehVor, _ziehNach;

        /**
         * Waehrend ein Kurvenende gezogen wird, zeigt die Strichlinie die
         * Kurve, die beim Loslassen entstuende - fuer beide Nachbarkanten.
         * Gerechnet wird nur, wenn sich ein Fang aendert.
         */
        private void ZeichneZiehkurven(ParkingLotPreviewBuffer buffer)
        {
            if (!KurveFolgenAn || _dragPoint < 0 || _dragPoint >= _points.Count)
            {
                _ziehBereit = false;
                return;
            }
            int n = _points.Count;
            int vor = _dragPoint > 0 ? _dragPoint - 1 : _closed ? n - 1 : -1;
            int nach = _dragPoint < n - 1 ? _dragPoint + 1 : _closed ? 0 : -1;
            Kurven.Fang fv = default, fn = default;
            if (vor >= 0 && vor != _dragPoint) _kurvenPunkte.TryGetValue(_points[vor], out fv);
            if (nach >= 0 && nach != _dragPoint) _kurvenPunkte.TryGetValue(_points[nach], out fn);
            var schluessel = (fv, _kurvenHover, fn);
            if (!_ziehBereit || !_ziehSchluessel.Equals(schluessel))
            {
                _ziehSchluessel = schluessel;
                _ziehBereit = true;
                _ziehVor = fv.Vorhanden && _kurvenHover.Vorhanden ? Kurvenzug(fv, _kurvenHover) : null;
                _ziehNach = fn.Vorhanden && _kurvenHover.Vorhanden ? Kurvenzug(_kurvenHover, fn) : null;
            }
            ZeichneStrichkurve(buffer, _ziehVor);
            ZeichneStrichkurve(buffer, _ziehNach);
        }

        private float3[] Kurvenzug(Kurven.Fang a, Kurven.Fang b)
        {
            var e = VerfolgeKurvenpaar(a, b, 0);
            return e != null && e.Grund == Kurven.Grund.Keiner && e.Punkte != null && e.Punkte.Length >= 3
                ? e.Punkte : null;
        }

        private static void ZeichneStrichkurve(ParkingLotPreviewBuffer buffer, float3[] punkte)
        {
            if (punkte == null) return;
            for (int i = 1; i < punkte.Length; i++)
                buffer.DrawDashedLine(PreviewColor, new Colossal.Mathematics.Line3.Segment(punkte[i - 1], punkte[i]),
                    PreviewLineWidth, DashLength, DashGap);
        }

        private Kurvenkante[] KurvenSchnappschuss()
        {
            var kopie = new Kurvenkante[_kurvenkanten.Count];
            for (int i = 0; i < kopie.Length; i++) kopie[i] = _kurvenkanten[i].Kopie();
            return kopie;
        }

        private void KurvenWiederherstellen(Kurvenkante[] stand)
        {
            _kurvenkanten.Clear();
            _kurvenZwischen.Clear();
            if (stand != null)
                foreach (var k in stand)
                {
                    _kurvenkanten.Add(k.Kopie());
                    foreach (var p in k.Zwischen) _kurvenZwischen.Add(p);
                }
            _kurvenformRevision = -1;
        }

        /** Fuer den Bauzettel: nur vollstaendige Kurven mit Zettel. */
        private List<Geometry.Kurvenzettel> KurvenzettelFuerBau()
        {
            PflegeKurvenform();
            if (_kurvenkanten.Count == 0) return null;
            var liste = new List<Geometry.Kurvenzettel>();
            foreach (var k in _kurvenkanten)
                if (k.Zettel != null && k.Zettel.Zwischen == k.Zwischen.Length) liste.Add(k.Zettel);
            return liste.Count == 0 ? null : liste;
        }

        /**
         * Edit: die gespeicherten Kurven wieder als Kurven fuehren, damit ihre
         * Zwischenpunkte keine Griffe bekommen. Neu gerechnet wird nichts.
         */
        private void LadeKurvenAusZettel(Entity lot)
        {
            VergissKurvenform();
            var zettel = ParkingLotKurvenzettelPuffer.Lies(EntityManager, lot);
            if (zettel == null) return;
            int geladen = 0;
            foreach (var z in zettel)
            {
                if (!z.FindeZwischenpunkte(_points, out var zwischen)) continue;
                _kurvenkanten.Add(new Kurvenkante { Von = z.Von, Bis = z.Bis, Zwischen = zwischen, Zettel = z });
                foreach (var p in zwischen) _kurvenZwischen.Add(p);
                geladen++;
            }
            _kurvenformRevision = -1;
            Mod.log.Info($"PLT-Kurve: {geladen}/{zettel.Length} Kurven aus dem Bauzettel uebernommen.");
        }

        private void VergissKurvenform()
        {
            _kurvenkanten.Clear();
            _kurvenZwischen.Clear();
            _kurvenformRevision = -1;
        }
    }
}
