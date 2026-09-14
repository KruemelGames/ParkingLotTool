using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * Aus zwei Flaechen wird eine.
     *
     * WARUM DAS DER RICHTIGE WEG IST. Die Naht zwischen Zufahrtsbelag und
     * Vorflaeche hat fuenf Anlaeufe ueberlebt: Ueberlappung 1,5 m, 1,0 m,
     * keine, Eckenrundung abschalten, gemeinsamer Materialstapel. Sie ist
     * jedes Mal geblieben oder mitgewandert. Solange zwei Polygone
     * aneinanderstossen, gibt es eine Kante, an der sie das tun.
     *
     * Also stossen sie nicht mehr aneinander: die Vorflaeche wird in den
     * Asphaltring EINGESETZT. Der Umriss laeuft dann in einem Zug von der
     * Fahrgasse ueber die Polygonkante bis an die Fahrbahn. Genau so hat das
     * Projekt schon einmal die Nahtfreiheit beim Gras erreicht - ein Umriss
     * statt aneinandergelegter Teilstuecke.
     *
     * DER RING WAECHST, ER SCHRUMPFT NICHT. Ob der eingesetzte Umweg nach
     * aussen zeigt, wird nicht geglaubt, sondern nachgerechnet: die Flaeche
     * des Rings muss um die Flaeche der Vorflaeche zunehmen. Nimmt sie ab,
     * lief der Umweg nach innen; dann bleibt es beim getrennten Polygon, und
     * die Meldung sagt es.
     */
    public sealed partial class ParkingLotToolSystem
    {
        /**
         * Wie weit ein Vorflaechenpunkt von der Asphaltkante entfernt sein
         * darf, um noch als "auf dieser Kante" zu gelten.
         *
         * Das ist eine Suchweite, kein Bauteilmass. Der tatsaechlich
         * gemessene Abstand steht in der Meldung - liegt er dauerhaft nahe
         * an dieser Schranke, stimmt etwas anderes nicht.
         */
        private const float VerschmelzenSuchweite = 0.75f;

        /**
         * Ab wann zwei Punkte derselbe Punkt sind. Reine Rechengenauigkeit -
         * die Vorflaeche uebernimmt ihre Ecken jetzt woertlich aus dem
         * Zufahrtsrechteck, der Abstand ist also 0 oder ein Float-Rest. Der
         * gemessene Wert steht in der Meldung.
         */
        private const float ZusammenfallendePunkte = 0.01f;

        private void VerschmelzeVorflaechen(
            float2[][] asphalt,
            out float2[][] verschmolzen,
            out float2[][] rest,
            out float2[][] einzeln,
            out int[] einzelnArt)
        {
            verschmolzen = Array.Empty<float2[]>();
            rest = asphalt ?? Array.Empty<float2[]>();
            einzeln = _vorflaechen;
            einzelnArt = _vorflaechenArt;
            if (_vorflaechen == null || _vorflaechen.Length == 0) return;
            if (_vorflaechenKante == null
                || _vorflaechenKante.Length != _vorflaechen.Length)
                return;
            if (asphalt == null || asphalt.Length == 0) return;

            // Arbeitskopien: der Ring wird beim Einsetzen laenger.
            var ringe = new List<float2[]>(asphalt.Length);
            for (var i = 0; i < asphalt.Length; i++) ringe.Add(asphalt[i]);
            var beruehrt = new bool[asphalt.Length];

            var offenRinge = new List<float2[]>();
            var offenArt = new List<int>();
            var meldungen = new List<string>();

            for (var v = 0; v < _vorflaechen.Length; v++)
            {
                var kante = _vorflaechenKante[v];
                if (kante == null || kante.Length != 4)
                {
                    offenRinge.Add(_vorflaechen[v]);
                    offenArt.Add(_vorflaechenArt[v]);
                    continue;
                }

                if (TrySetzeEin(ringe, beruehrt, kante, _vorflaechen[v],
                        out var meldung))
                {
                    meldungen.Add(meldung);
                    continue;
                }

                meldungen.Add(meldung);
                offenRinge.Add(_vorflaechen[v]);
                offenArt.Add(_vorflaechenArt[v]);
            }

            var mitVorflaeche = new List<float2[]>();
            var ohne = new List<float2[]>();
            for (var i = 0; i < ringe.Count; i++)
            {
                if (beruehrt[i]) mitVorflaeche.Add(ringe[i]);
                else ohne.Add(ringe[i]);
            }

            verschmolzen = mitVorflaeche.ToArray();
            rest = ohne.ToArray();
            einzeln = offenRinge.ToArray();
            einzelnArt = offenArt.ToArray();

            var text = "PLT-Vorflaeche verschmolzen: " + mitVorflaeche.Count
                + " Asphaltring(e) erweitert, " + offenRinge.Count
                + " Vorflaeche(n) blieben eigenstaendig. "
                + string.Join(" | ", meldungen);
            if (text != _letzteVerschmelzmeldung)
            {
                _letzteVerschmelzmeldung = text;
                Mod.log.Info(text);
            }
        }

        private string _letzteVerschmelzmeldung = string.Empty;

        /**
         * Setzt eine Vorflaeche in den passenden Asphaltring ein.
         *
         * Gesucht wird die EINE Ringkante, auf der beide Innenpunkte liegen.
         * Liegen sie auf zwei verschiedenen Kanten, ist die Zufahrt ueber
         * eine Polygonecke gebaut - dann wird nicht verschmolzen, denn dabei
         * entstuende ein Ring, der sich selbst schneidet.
         */
        private static bool TrySetzeEin(
            List<float2[]> ringe,
            bool[] beruehrt,
            float2[] kante,
            float2[] vorflaeche,
            out string meldung)
        {
            var innenLinks = kante[0];
            var innenRechts = kante[1];
            var aussenLinks = kante[2];
            var aussenRechts = kante[3];

            var besterRing = -1;
            var besteKante = -1;
            var besterAbstand = float.PositiveInfinity;
            var besterTLinks = 0f;
            var besterTRechts = 0f;

            for (var r = 0; r < ringe.Count; r++)
            {
                var ring = ringe[r];
                if (ring == null || ring.Length < 3) continue;
                for (var i = 0; i < ring.Length; i++)
                {
                    var a = ring[i];
                    var b = ring[(i + 1) % ring.Length];
                    var abstandLinks = AbstandZurStrecke(a, b, innenLinks,
                        out var tL);
                    var abstandRechts = AbstandZurStrecke(a, b, innenRechts,
                        out var tR);
                    var schlechtester = math.max(abstandLinks, abstandRechts);
                    if (schlechtester >= besterAbstand) continue;
                    besterAbstand = schlechtester;
                    besterRing = r;
                    besteKante = i;
                    besterTLinks = tL;
                    besterTRechts = tR;
                }
            }

            if (besterRing < 0 || besterAbstand > VerschmelzenSuchweite)
            {
                meldung = "keine gemeinsame Asphaltkante gefunden (naechster "
                    + "Abstand " + Zahl(besterAbstand) + " m, Suchweite "
                    + Zahl(VerschmelzenSuchweite) + " m)";
                return false;
            }

            var quelle = ringe[besterRing];
            var eckeA = quelle[besteKante];
            var eckeB = quelle[(besteKante + 1) % quelle.Length];
            var erster = besterTLinks <= besterTRechts ? innenLinks : innenRechts;
            var letzter = besterTLinks <= besterTRechts ? innenRechts : innenLinks;
            var ersterAussen = besterTLinks <= besterTRechts
                ? aussenLinks : aussenRechts;
            var letzterAussen = besterTLinks <= besterTRechts
                ? aussenRechts : aussenLinks;

            /*
             * KEIN PUNKT ZWEIMAL.
             *
             * Seit die Vorflaeche ihre Breite aus dem Zufahrtsrechteck nimmt,
             * fallen ihre Innenpunkte mit den vorhandenen Ringecken zusammen.
             * Wuerden sie trotzdem eingefuegt, saessen zwei gleiche Punkte
             * hintereinander im Ring - ein Doppelpunkt, an dem CS2 eine
             * Flaeche verwirft. Aus dem Zufahrtsrechteck wird so ein
             * Viereck von der Randstrasse bis zur Fahrbahn, ohne die zwei
             * ueberfluessigen Punkte auf der Polygonkante.
             */
            var abstandA = math.distance(erster, eckeA);
            var abstandB = math.distance(letzter, eckeB);
            var doppeltA = abstandA < ZusammenfallendePunkte;
            var doppeltB = abstandB < ZusammenfallendePunkte;

            var neu = new List<float2>(quelle.Length + 4);
            for (var i = 0; i <= besteKante; i++) neu.Add(quelle[i]);
            if (!doppeltA) neu.Add(erster);
            neu.Add(ersterAussen);
            neu.Add(letzterAussen);
            if (!doppeltB) neu.Add(letzter);
            for (var i = besteKante + 1; i < quelle.Length; i++)
                neu.Add(quelle[i]);

            /*
             * Die Probe: der Ring muss um die Vorflaeche GEWACHSEN sein.
             * Ein nach innen gelegter Umweg macht ihn kleiner und erzeugt
             * eine Selbstueberschneidung - genau das, woran CS2 eine Flaeche
             * verwirft.
             */
            var vorher = math.abs(RingFlaeche(quelle));
            var nachher = math.abs(RingFlaeche(neu.ToArray()));
            var erwartet = math.abs(RingFlaeche(vorflaeche));
            var zuwachs = nachher - vorher;
            if (zuwachs <= 0f || erwartet <= 0f
                || math.abs(zuwachs - erwartet) > erwartet * 0.25f)
            {
                meldung = "Umweg zeigt nicht nach aussen (Zuwachs "
                    + Zahl(zuwachs) + " m2, erwartet " + Zahl(erwartet)
                    + " m2) - Vorflaeche bleibt eigenstaendig";
                return false;
            }

            ringe[besterRing] = neu.ToArray();
            beruehrt[besterRing] = true;
            meldung = "eingesetzt in Ring " + besterRing + " an Kante "
                + besteKante + " (Kantenabstand " + Zahl(besterAbstand)
                + " m, Zuwachs " + Zahl(zuwachs) + " m2, Punkte "
                + quelle.Length + " -> " + neu.Count
                + ", Ringecken getroffen " + Zahl(abstandA) + "/"
                + Zahl(abstandB) + " m, "
                + ((doppeltA ? 1 : 0) + (doppeltB ? 1 : 0))
                + " Doppelpunkt(e) vermieden)";
            return true;
        }

        private static float AbstandZurStrecke(float2 a, float2 b, float2 p,
            out float t)
        {
            var ab = b - a;
            var laenge = math.lengthsq(ab);
            t = laenge < 1e-9f ? 0f : math.saturate(math.dot(p - a, ab) / laenge);
            return math.distance(p, a + ab * t);
        }

        private static float RingFlaeche(float2[] ring)
        {
            if (ring == null || ring.Length < 3) return 0f;
            var summe = 0f;
            for (var i = 0; i < ring.Length; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Length];
                summe += a.x * b.y - b.x * a.y;
            }
            return summe * 0.5f;
        }

        private static string Zahl(float wert)
            => float.IsInfinity(wert)
                ? "unendlich"
                : wert.ToString("F2", CultureInfo.InvariantCulture);
    }
}
