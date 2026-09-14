namespace Zellenversuch;

/// <summary>
/// Oeffnet einen Materialring als Zellgraph: Eine kuerzeste Kette von Zellen
/// verbindet Loch- und Aussenrand und wird zu einer zweiten Flaeche. Getrennt
/// wird ausschliesslich an bereits vorhandenen gemeinsamen Zellkanten. Die
/// Polygonteilung ist zu diesem Zeitpunkt beendet; neue Punkte sind unmoeglich.
/// </summary>
internal static class Lochtrenner
{
    private sealed record Kantenbesitz(int ZellId, GerichteteKante Kante);

    internal static (List<Flaeche> Flaechen, Lochtrennbericht Bericht) Trenne(
        IReadOnlyList<Zelle> zellen,
        IReadOnlyList<Flaeche> vorher,
        Rahmen rahmen)
    {
        var kantenindex = BaueKantenindex(zellen);
        var gesperrt = new HashSet<KantenSchluessel>();
        var naehte = new List<Trennnaht>();
        var aktuell = vorher.ToList();

        while (aktuell.Any(flaeche => flaeche.Loecher.Count != 0))
        {
            var neueKantenInRunde = 0;
            foreach (var lochflaeche in aktuell.Where(
                         flaeche => flaeche.Loecher.Count != 0).ToList())
            {
                var flaechenzellen = lochflaeche.ZellIds.ToHashSet();
                var trennzellen = new HashSet<int>();
                var ziele = Randzellen(
                    lochflaeche.Aussenring, flaechenzellen, kantenindex);
                foreach (var loch in lochflaeche.Loecher)
                {
                    var pfad = FindePfadZuZielen(
                        lochflaeche, loch, ziele, zellen, kantenindex, gesperrt);
                    trennzellen.UnionWith(pfad);
                    ziele.UnionWith(pfad);
                }
                if (trennzellen.Count == lochflaeche.ZellIds.Count)
                    throw new InvalidOperationException(
                        "Der Ring kann nicht an vorhandenen Zellkanten geoeffnet werden: "
                        + "Das verbindende Zellennetz belegt seine gesamte Materialflaeche.");

                var neueKanten = new List<GerichteteKante>();
                foreach (var funde in kantenindex.Values)
                {
                    if (funde.Count != 2) continue;
                    var a = funde[0];
                    var b = funde[1];
                    if (!flaechenzellen.Contains(a.ZellId)
                        || !flaechenzellen.Contains(b.ZellId))
                        continue;
                    if (trennzellen.Contains(a.ZellId) == trennzellen.Contains(b.ZellId))
                        continue;
                    if (gesperrt.Add(a.Kante.Schluessel)) neueKanten.Add(a.Kante);
                }
                if (neueKanten.Count == 0)
                    throw new InvalidOperationException(
                        "Das gefundene Zellennetz besitzt keine trennende vorhandene Kante.");

                neueKantenInRunde += neueKanten.Count;
                naehte.Add(new Trennnaht(
                    naehte.Count,
                    lochflaeche.Material,
                    rahmen.NachWelt(Geometrie.Schwerpunkt(lochflaeche.Loecher[0])),
                    trennzellen.Count,
                    neueKanten.Count,
                    neueKanten.Sum(kante =>
                        Geometrie.Laenge(kante.Nach.Punkt - kante.Von.Punkt))));
            }
            if (neueKantenInRunde == 0)
                throw new InvalidOperationException("Die Ringtrennung fand keine neue Zellkante.");
            aktuell = Vereinigung.Vereinige(zellen, gesperrt).Flaechen;
            if (naehte.Count > zellen.Count)
                throw new InvalidOperationException("Die Ringtrennung macht keinen endlichen Fortschritt.");
        }

        var zellknoten = zellen
            .SelectMany(zelle => zelle.Polygon.Ecken)
            .Select(ecke => ecke.Knoten.Id)
            .ToHashSet();
        var neuePunkte = aktuell
            .SelectMany(flaeche => flaeche.AlleRinge)
            .SelectMany(ring => ring.Knoten)
            .Select(knoten => knoten.Id)
            .Distinct()
            .Count(id => !zellknoten.Contains(id));
        var nurZellkanten = gesperrt.All(schluessel =>
            kantenindex.TryGetValue(schluessel, out var funde) && funde.Count == 2);

        return (aktuell, new Lochtrennbericht
        {
            FlaechenVorher = vorher.Count,
            FlaechenNachher = aktuell.Count,
            LochflaechenVorher = vorher.Count(flaeche => flaeche.Loecher.Count != 0),
            LoecherVorher = vorher.Sum(flaeche => flaeche.Loecher.Count),
            LochflaechenNachher = aktuell.Count(flaeche => flaeche.Loecher.Count != 0),
            LoecherNachher = aktuell.Sum(flaeche => flaeche.Loecher.Count),
            NeueGeometriepunkte = neuePunkte,
            NurVorhandeneZellkanten = nurZellkanten,
            Trennnaehte = naehte,
        });
    }

    private static List<int> FindePfadZuZielen(
        Flaeche flaeche,
        Ring loch,
        IReadOnlySet<int> zielzellen,
        IReadOnlyList<Zelle> zellen,
        IReadOnlyDictionary<KantenSchluessel, List<Kantenbesitz>> kantenindex,
        IReadOnlySet<KantenSchluessel> gesperrt)
    {
        var zellIds = flaeche.ZellIds.ToHashSet();
        var lochzellen = Randzellen(loch, zellIds, kantenindex);
        if (lochzellen.Count == 0 || zielzellen.Count == 0)
            throw new InvalidOperationException("Loch- oder Zielrand hat keine tragende Zelle.");

        // Ein 1-Zellen-Pfad ist ideal, aber nicht jede Randzelle ist gleich
        // brauchbar. Die erste ID traf bei L schraeg eine 0,242-m-Ecke; die
        // groesste Mindestkante liefert dort 1,089 m, ohne Fangwert.
        var direkterPfad = lochzellen
            .Where(zielzellen.Contains)
            .OrderByDescending(id => Zellmindestkante(zellen[id]))
            .ThenByDescending(id => Geometrie.Flaeche(zellen[id].Polygon))
            .ThenBy(id => id)
            .FirstOrDefault(-1);
        if (direkterPfad >= 0) return new List<int> { direkterPfad };

        var nachbarn = zellIds.ToDictionary(id => id, _ => new List<int>());
        foreach (var (schluessel, funde) in kantenindex)
        {
            if (gesperrt.Contains(schluessel) || funde.Count != 2) continue;
            var a = funde[0].ZellId;
            var b = funde[1].ZellId;
            if (!zellIds.Contains(a) || !zellIds.Contains(b)) continue;
            nachbarn[a].Add(b);
            nachbarn[b].Add(a);
        }

        var warteschlange = new Queue<int>();
        var vorgaenger = new Dictionary<int, int?>();
        foreach (var start in lochzellen
                     .OrderByDescending(id => Zellmindestkante(zellen[id]))
                     .ThenByDescending(id => Geometrie.Flaeche(zellen[id].Polygon))
                     .ThenBy(id => id))
        {
            warteschlange.Enqueue(start);
            vorgaenger.Add(start, null);
        }

        int? ziel = null;
        while (warteschlange.Count != 0)
        {
            var aktuell = warteschlange.Dequeue();
            if (zielzellen.Contains(aktuell))
            {
                ziel = aktuell;
                break;
            }
            foreach (var nachbar in nachbarn[aktuell].OrderBy(id => id))
            {
                if (vorgaenger.ContainsKey(nachbar)) continue;
                vorgaenger.Add(nachbar, aktuell);
                warteschlange.Enqueue(nachbar);
            }
        }
        if (!ziel.HasValue)
            throw new InvalidOperationException(
                "Der Ring kann nicht an vorhandenen Zellkanten geoeffnet werden: "
                + "Loch- und Aussenrand sind im Materialgraph nicht verbunden.");

        var pfad = new List<int>();
        for (int? zelle = ziel; zelle.HasValue; zelle = vorgaenger[zelle.Value])
            pfad.Add(zelle.Value);
        pfad.Reverse();
        return pfad;
    }

    private static double Zellmindestkante(Zelle zelle)
    {
        var minimum = double.PositiveInfinity;
        for (var i = 0; i < zelle.Polygon.Anzahl; i++)
            minimum = Math.Min(minimum, Geometrie.Laenge(
                zelle.Polygon.Knoten(i + 1).Punkt - zelle.Polygon.Knoten(i).Punkt));
        return minimum;
    }

    private static HashSet<int> Randzellen(
        Ring ring,
        IReadOnlySet<int> flaechenzellen,
        IReadOnlyDictionary<KantenSchluessel, List<Kantenbesitz>> kantenindex)
    {
        var ausgabe = new HashSet<int>();
        foreach (var kante in ring.Rohkanten)
        {
            if (!kantenindex.TryGetValue(kante.Schluessel, out var funde)) continue;
            foreach (var fund in funde)
                if (flaechenzellen.Contains(fund.ZellId)) ausgabe.Add(fund.ZellId);
        }
        return ausgabe;
    }

    private static Dictionary<KantenSchluessel, List<Kantenbesitz>> BaueKantenindex(
        IReadOnlyList<Zelle> zellen)
    {
        var ausgabe = new Dictionary<KantenSchluessel, List<Kantenbesitz>>();
        foreach (var zelle in zellen)
            for (var i = 0; i < zelle.Polygon.Anzahl; i++)
            {
                var kante = new GerichteteKante(
                    zelle.Polygon.Knoten(i),
                    zelle.Polygon.Knoten(i + 1),
                    zelle.Polygon.Linie(i));
                if (!ausgabe.TryGetValue(kante.Schluessel, out var funde))
                    ausgabe.Add(kante.Schluessel, funde = new List<Kantenbesitz>());
                funde.Add(new Kantenbesitz(zelle.Id, kante));
            }
        return ausgabe;
    }
}
