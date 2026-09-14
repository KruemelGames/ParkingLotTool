using System.Collections.Generic;

namespace ParkingLotTool.Geometry
{
    // Auswahl ganzer Leitungsketten ab erfolgreich wieder angeschlossenen Knoten.
    // Der Aufrufer liefert ausschliesslich uebernehmbare eigene Kanten: fremde,
    // geloeschte und temporaere Leitungen sind keine Bruecken im Suchgraphen.
    internal static class Versorgungsuebernahme
    {
        internal static HashSet<T> Finde<T>(
            IReadOnlyList<(T Kante, T Start, T Ende)> kanten, IEnumerable<T> starts)
        {
            var nachbarn = new Dictionary<T, List<int>>();
            void Eintragen(T knoten, int index)
            {
                if (!nachbarn.TryGetValue(knoten, out var liste))
                    nachbarn[knoten] = liste = new List<int>();
                liste.Add(index);
            }
            for (var i = 0; i < kanten.Count; i++)
            {
                Eintragen(kanten[i].Start, i);
                Eintragen(kanten[i].Ende, i);
            }
            var besucht = new HashSet<T>();
            var offen = new List<T>();
            foreach (var start in starts)
                if (besucht.Add(start)) offen.Add(start);
            var ergebnis = new HashSet<T>();
            for (var position = 0; position < offen.Count; position++)
            {
                var knoten = offen[position];
                if (!nachbarn.TryGetValue(knoten, out var liste)) continue;
                foreach (var i in liste)
                {
                    var k = kanten[i];
                    ergebnis.Add(k.Kante);
                    if (besucht.Add(k.Start)) offen.Add(k.Start);
                    if (besucht.Add(k.Ende)) offen.Add(k.Ende);
                }
            }
            return ergebnis;
        }
    }
}
