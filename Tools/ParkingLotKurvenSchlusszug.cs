using System.Collections.Generic;
using Unity.Mathematics;
using Kurven = ParkingLotTool.Geometry.Strassenkurve;

namespace ParkingLotTool.Tools
{
    /**
     * DIE SCHLIESSKANTE EINER KURVE ZIEHEN (Nutzer 20.10.).
     *
     * Ein U aus EINER Kurve: die gerade Kante oben verbindet Von und Bis
     * derselben Kurve. Starr gezogen zerfiel die Kurve und es blieben zwei
     * Punkte. Jetzt gleiten beide Enden zugleich auf dem (ganzen) Verlauf:
     * nach innen die Arme hinunter, nach aussen zurueck bis zum Anfang des
     * Verlaufs - am Bordsteinende vor der Node ist Schluss, dort bleibt der
     * Zug stehen. Ohne Loesen der Kurve: der Umriss behaelt waehrend des
     * Zugs alle Punkte, uebernommen wird erst beim Loslassen.
     */
    public sealed partial class ParkingLotToolSystem
    {
        private sealed class Schlusszug
        {
            internal Kurvenkante Kurve;
            internal int[] Kette;
            internal int Richtung;
            internal float2[] Start;
            internal float2[] Ganz;
            internal float[] Bogen;
            internal float VonLage, BisLage, NeuVon, NeuBis;
            internal float GuterVersatz;
            internal float3[] Vorschau;
        }

        private Schlusszug _schlusszug;

        private Schlusszug BeginneSchlusszug(Kurvenkante k)
        {
            var kette = KurvenKette(k);
            if (kette == null || kette.Length < 3) return null;
            int n = _points.Count;
            var start = new float2[kette.Length];
            for (int i = 0; i < kette.Length; i++) start[i] = _points[kette[i]];
            var ganz = GanzeKette(k);
            var bogen = new float[ganz.Length];
            for (int i = 1; i < ganz.Length; i++) bogen[i] = bogen[i - 1] + math.distance(ganz[i - 1], ganz[i]);
            float von = LageAufKette(ganz, k.Von), bis = LageAufKette(ganz, k.Bis);
            return new Schlusszug
            {
                Kurve = k, Kette = kette, Start = start, Ganz = ganz, Bogen = bogen,
                Richtung = kette[1] == (kette[0] + 1) % n ? 1 : -1,
                VonLage = von, BisLage = bis, NeuVon = von, NeuBis = bis,
            };
        }

        /** Je Bild nach dem starren Versatz: beide Enden auf den Verlauf legen. */
        private void SchlusszugAufKurve(int aIndex, int bIndex, float offset)
        {
            var s = _schlusszug;
            var startA = _dragEdgeStartPoints[aIndex];
            var startB = _dragEdgeStartPoints[bIndex];
            var q = startA + _dragEdgeNormal * offset;
            var d = startB - startA;
            for (int i = 1; i < s.Kette.Length - 1; i++)
            {
                _points[s.Kette[i]] = s.Start[i];
                _worldPoints[s.Kette[i]] = _dragEdgeStartWorld[s.Kette[i]];
            }
            float gesamt = s.Bogen[s.Bogen.Length - 1];
            // Vor dem Verlaufsanfang (Verlaengerung, negative Lage) und hinter seinem
            // Ende gibt es keinen Bordstein mehr: dort bleibt der Zug stehen.
            bool trifftVon = SchneideKette(s.Ganz, q, d, s.VonLage, out var amVon, out _, out var lageVon);
            bool trifftBis = SchneideKette(s.Ganz, q, d, s.BisLage, out var amBis, out _, out var lageBis);
            bool passt = trifftVon && trifftBis && lageVon >= 0f && lageBis <= gesamt + 1e-3f
                && lageVon + (float)Kurven.Mindestsehne < lageBis;
            if (!passt)
            {
                if (!_kantenzugZurueck && !offset.Equals(s.GuterVersatz)) ZurueckAufVersatz(s.GuterVersatz);
                return;
            }
            s.GuterVersatz = offset;
            s.NeuVon = lageVon;
            s.NeuBis = lageBis;
            amVon = AufGitter(amVon);
            amBis = AufGitter(amBis);
            int von = s.Kette[0], bis = s.Kette[s.Kette.Length - 1];
            _points[von] = amVon;
            _worldPoints[von] = new float3(amVon.x, _dragEdgeStartWorld[von].y, amVon.y);
            _points[bis] = amBis;
            _worldPoints[bis] = new float3(amBis.x, _dragEdgeStartWorld[bis].y, amBis.y);
            // Abgeschnittene Zwischenpunkte liegen bis zum Loslassen auf ihrem Ende.
            for (int i = 1; i < s.Kette.Length - 1; i++)
            {
                float lage = LageAufKette(s.Ganz, s.Start[i]);
                var ziel = lage <= lageVon ? amVon : lage >= lageBis ? amBis : s.Start[i];
                int index = s.Kette[i];
                _points[index] = ziel;
                _worldPoints[index] = new float3(ziel.x, _dragEdgeStartWorld[index].y, ziel.y);
            }
            var y = _dragEdgeStartWorld[von].y;
            var vorschau = new List<float3> { new float3(amVon.x, y, amVon.y) };
            foreach (var p in SchlussRest(s)) vorschau.Add(new float3(p.x, y, p.y));
            vorschau.Add(new float3(amBis.x, y, amBis.y));
            s.Vorschau = vorschau.ToArray();
        }

        private static List<float2> SchlussRest(Schlusszug s)
        {
            var rest = new List<float2>();
            for (int i = 0; i < s.Ganz.Length; i++)
                if (s.Bogen[i] > s.NeuVon + 0.01f && s.Bogen[i] < s.NeuBis - 0.01f) rest.Add(s.Ganz[i]);
            return rest;
        }

        /** Loslassen: Zwischenpunkte neu aus dem Verlauf, die Kurve bleibt eine Kurve. */
        private void UebernimmSchlusszug()
        {
            var s = _schlusszug;
            _schlusszug = null;
            if (s == null) return;
            var amVon = _points[s.Kette[0]];
            var amBis = _points[s.Kette[s.Kette.Length - 1]];
            var weg = new List<int>();
            for (int i = 1; i < s.Kette.Length - 1; i++) weg.Add(s.Kette[i]);
            weg.Sort();
            for (int i = weg.Count - 1; i >= 0; i--) EntfernePunkt(weg[i]);
            var rest = SchlussRest(s);
            int index = _points.IndexOf(amVon);
            if (index < 0) return;
            var y = _worldPoints[index].y;
            var einfuegen = new List<float2>(rest);
            if (s.Richtung < 0) einfuegen.Reverse();
            int stelle = s.Richtung > 0 ? index + 1 : index;
            SyncSnapAxes();
            for (int i = 0; i < einfuegen.Count; i++)
            {
                _points.Insert(stelle + i, einfuegen[i]);
                _worldPoints.Insert(stelle + i, new float3(einfuegen[i].x, y, einfuegen[i].y));
                _pointAxes.Insert(stelle + i, float2.zero);
            }
            var bogenlagen = new float[rest.Count];
            float bogen = 0f;
            for (int i = 0; i < rest.Count; i++)
            {
                bogen += math.distance(i == 0 ? amVon : rest[i - 1], rest[i]);
                bogenlagen[i] = bogen;
            }
            var k = s.Kurve;
            k.Zettel = k.Zettel?.MitPunkten(amVon, amBis, bogenlagen);
            k.Ganz = s.Ganz;
            SetzeKurvenpunkte(k, amVon, amBis, rest.ToArray());
            _kurvenformRevision = -1;
            Mod.log.Info($"PLT-Kurve: Schliesskante gezogen, beide Enden auf der Kurve, {rest.Count} Zwischenpunkte.");
        }
    }
}
