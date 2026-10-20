using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry.Zellen;

namespace ParkingLotTool.Geometry
{
    public static partial class ParkingGeometry
    {
        // Vertrag mit dem Kurserzeuger: darunter entsteht in CS2 keine Kante.
        public const float NetzMindestlaenge = 1f;

        private static List<ZellenStrasse> ZellenBindeNetzknoten(
            List<ZellenStrasse> stuecke, Rahmen rahmen, ISet<Punkt> festeEcken)
        {
            double Laenge(Punkt a, Punkt b)
            {
                // Der Erzeuger erhaelt Welt-floats, keine lokalen doubles.
                var wa = ZellenPunkt(rahmen.NachWelt(a));
                var wb = ZellenPunkt(rahmen.NachWelt(b));
                double x = (double)wb.x - wa.x, y = (double)wb.y - wa.y;
                return Math.Sqrt(x * x + y * y);
            }
            if (!stuecke.Any(s => Laenge(s.A, s.B) < NetzMindestlaenge))
                return stuecke;

            var arten = new Dictionary<Punkt, HashSet<string>>();
            foreach (var stueck in stuecke)
                foreach (var p in new[] { stueck.A, stueck.B })
                {
                    if (!arten.TryGetValue(p, out var werte))
                        arten[p] = werte = new HashSet<string>();
                    werte.Add(stueck.Kind);
                }
            var rang = arten.Keys
                .OrderByDescending(p => festeEcken?.Contains(p) == true)
                .ThenByDescending(p => arten[p].Count)
                .Select((p, i) => (Punkt: p, Rang: i))
                .ToDictionary(p => p.Punkt, p => p.Rang);
            var bindung = arten.Keys.ToDictionary(p => p, p => p);
            // Nur Enden EINER kurzen Kante binden. Benachbarte, aber getrennte
            // Wege bleiben getrennt. Eine Bindung nimmt einen Knoten weg;
            // ein Rundungsfall erhaelt beide Enden mit baubarem Abstand.
            while (true)
            {
                var kurz = stuecke.FirstOrDefault(s =>
                    !bindung[s.A].Equals(bindung[s.B])
                    && Laenge(bindung[s.A], bindung[s.B]) < NetzMindestlaenge);
                if (kurz == null) break;
                var a = bindung[kurz.A];
                var b = bindung[kurz.B];
                var ziel = rang[a] <= rang[b] ? a : b;
                var quelle = ziel.Equals(a) ? b : a;
                if (Geometrie.Laenge(b - a) >= NetzMindestlaenge)
                {
                    // Ein lokal baubarer Meter darf durch float-Rundung kein
                    // Nullkurs werden. Nur den nachrangigen Endpunkt um die
                    // erforderlichen Welt-float-Schritte nach aussen runden.
                    var w = ZellenPunkt(rahmen.NachWelt(quelle));
                    var z = ZellenPunkt(rahmen.NachWelt(ziel));
                    do
                    {
                        if (Math.Abs((double)w.x - z.x) >= Math.Abs((double)w.y - z.y))
                            w.x = NaechsterNetzfloat(w.x, w.x > z.x);
                        else w.y = NaechsterNetzfloat(w.y, w.y > z.y);
                        a = rahmen.NachLokal(new Punkt(w.x, w.y));
                    }
                    while (Laenge(a, ziel) < NetzMindestlaenge);
                    ziel = a;
                    rang[ziel] = rang[quelle];
                }
                foreach (var p in arten.Keys)
                    if (bindung[p].Equals(quelle)) bindung[p] = ziel;
            }
            var ausgabe = new List<ZellenStrasse>();
            foreach (var stueck in stuecke)
            {
                var a = bindung[stueck.A];
                var b = bindung[stueck.B];
                if (a.Equals(b)) continue;
                ZellenFuegeNetzstueckHinzu(ausgabe, new ZellenStrasse
                {
                    Kind = stueck.Kind, Art = stueck.Art,
                    Randzoning = stueck.Randzoning,
                    RandzoningInnen = stueck.RandzoningInnen,
                    A = a, B = b,
                });
            }
            return ausgabe;
        }

        private static float NaechsterNetzfloat(float wert, bool groesser)
        {
            if (wert == 0) return groesser ? float.Epsilon : -float.Epsilon;
            int bits = BitConverter.SingleToInt32Bits(wert);
            bits += (wert > 0) == groesser ? 1 : -1;
            return BitConverter.Int32BitsToSingle(bits);
        }
    }
}
