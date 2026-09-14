namespace Zellenversuch;

internal static class Vereinigung
{
    private sealed record Kantenfund(int Zelle, GerichteteKante Kante);

    internal static (List<Flaeche> Flaechen, Topologiebericht Topologie) Vereinige(
        IReadOnlyList<Zelle> zellen,
        IReadOnlySet<KantenSchluessel>? gesperrteKanten = null)
    {
        var kantenindex = new Dictionary<KantenSchluessel, List<Kantenfund>>();
        foreach (var zelle in zellen)
        {
            if (Geometrie.Vorzeichenflaeche(zelle.Polygon.Punkte) <= 0)
                throw new InvalidOperationException($"Zelle {zelle.Id} ist nicht CCW.");
            for (var i = 0; i < zelle.Polygon.Anzahl; i++)
            {
                var kante = new GerichteteKante(
                    zelle.Polygon.Knoten(i),
                    zelle.Polygon.Knoten(i + 1),
                    zelle.Polygon.Linie(i));
                if (!kantenindex.TryGetValue(kante.Schluessel, out var funde))
                    kantenindex[kante.Schluessel] = funde = new List<Kantenfund>();
                funde.Add(new Kantenfund(zelle.Id, kante));
            }
        }

        var topologie = PruefeTopologie(kantenindex);
        var komponenten = new DisjunkteMenge(zellen.Count);
        foreach (var funde in kantenindex.Values)
        {
            if (funde.Count != 2) continue;
            if (gesperrteKanten?.Contains(funde[0].Kante.Schluessel) == true) continue;
            var erste = zellen[funde[0].Zelle];
            var zweite = zellen[funde[1].Zelle];
            if (erste.Material == zweite.Material)
                komponenten.Vereinige(erste.Id, zweite.Id);
        }

        var gruppen = zellen.GroupBy(zelle => komponenten.Wurzel(zelle.Id));
        var flaechen = new List<Flaeche>();
        foreach (var gruppe in gruppen)
        {
            var zellliste = gruppe.ToList();
            var randkanten = Randkanten(zellliste);
            var ringe = ZieheRinge(randkanten);
            var aussenringe = ringe.Where(ring => ring.Vorzeichenflaeche > 0).ToList();
            var lochringe = ringe.Where(ring => ring.Vorzeichenflaeche < 0).ToList();
            if (aussenringe.Count == 0 || aussenringe.Count + lochringe.Count != ringe.Count)
                throw new InvalidOperationException("Eine Materialkomponente hat keinen gueltigen Aussenring.");

            var erzeugt = aussenringe.Select(aussenring => new Flaeche
            {
                Id = flaechen.Count,
                Material = zellliste[0].Material,
                Aussenring = aussenring,
                Loecher = new List<Ring>(),
                ZellIds = zellliste.Select(zelle => zelle.Id).ToList(),
            }).ToList();

            foreach (var loch in lochringe)
            {
                var pruefpunkt = loch.Kanten[0].Von.Punkt;
                var traeger = erzeugt.FirstOrDefault(flaeche => Geometrie.Enthaelt(
                    flaeche.Aussenring.Knoten.Select(knoten => knoten.Punkt).ToArray(),
                    pruefpunkt));
                if (traeger is null)
                    throw new InvalidOperationException("Ein Loch liegt in keinem Aussenring.");
                traeger.Loecher.Add(loch);
            }
            flaechen.AddRange(erzeugt);
        }

        // IDs werden erst nach allen Komponenten gesetzt, weil eine seltene
        // Punktberuehrung mehr als einen Aussenring liefern kann.
        for (var i = 0; i < flaechen.Count; i++)
        {
            var alt = flaechen[i];
            flaechen[i] = new Flaeche
            {
                Id = i,
                Material = alt.Material,
                Aussenring = alt.Aussenring,
                Loecher = alt.Loecher,
                ZellIds = alt.ZellIds,
            };
        }
        return (flaechen, topologie);
    }

    private static Topologiebericht PruefeTopologie(
        IReadOnlyDictionary<KantenSchluessel, List<Kantenfund>> kantenindex)
    {
        var bericht = new Topologiebericht();
        foreach (var funde in kantenindex.Values)
        {
            if (funde.Count == 1)
            {
                if (funde[0].Kante.Linie.Art == Linienart.Aussenkante)
                    bericht.Aussenkanten++;
                else
                    bericht.UnerwarteteOffeneKanten++;
                if (funde[0].Kante.Linie.Art == Linienart.Teilungsnaht)
                    bericht.OffeneTeilungsnaehte++;
                continue;
            }
            if (funde.Count != 2)
            {
                bericht.NichtMannigfaltigeKanten++;
                continue;
            }
            bericht.Innenkanten++;
            var a = funde[0].Kante;
            var b = funde[1].Kante;
            if (a.Von.Id != b.Nach.Id || a.Nach.Id != b.Von.Id)
                bericht.GleichgerichteteDoppelkanten++;
            if (a.Linie.Id != b.Linie.Id) bericht.AbweichendeLinienIds++;
        }
        return bericht;
    }

    private static List<GerichteteKante> Randkanten(IReadOnlyList<Zelle> zellen)
    {
        var offen = new Dictionary<KantenSchluessel, GerichteteKante>();
        foreach (var zelle in zellen)
            for (var i = 0; i < zelle.Polygon.Anzahl; i++)
            {
                var kante = new GerichteteKante(
                    zelle.Polygon.Knoten(i),
                    zelle.Polygon.Knoten(i + 1),
                    zelle.Polygon.Linie(i));
                if (!offen.Remove(kante.Schluessel)) offen.Add(kante.Schluessel, kante);
            }
        return offen.Values.ToList();
    }

    private static List<Ring> ZieheRinge(IReadOnlyList<GerichteteKante> kanten)
    {
        var ausgehend = new Dictionary<int, List<int>>();
        for (var i = 0; i < kanten.Count; i++)
        {
            if (!ausgehend.TryGetValue(kanten[i].Von.Id, out var liste))
                ausgehend[kanten[i].Von.Id] = liste = new List<int>();
            liste.Add(i);
        }

        var unbenutzt = new HashSet<int>(Enumerable.Range(0, kanten.Count));
        var ringe = new List<Ring>();
        while (unbenutzt.Count != 0)
        {
            var startindex = unbenutzt.First();
            var startknoten = kanten[startindex].Von.Id;
            var ringkanten = new List<GerichteteKante>();
            var index = startindex;
            while (true)
            {
                if (!unbenutzt.Remove(index))
                    throw new InvalidOperationException("Eine Randkante wurde zweimal besucht.");
                var kante = kanten[index];
                ringkanten.Add(kante);
                if (kante.Nach.Id == startknoten) break;
                if (!ausgehend.TryGetValue(kante.Nach.Id, out var kandidaten))
                    throw new InvalidOperationException("Ein Materialrand ist offen.");
                var naechste = kandidaten.Where(unbenutzt.Contains).ToList();
                if (naechste.Count != 1)
                    throw new InvalidOperationException(
                        $"Materialrand an Knoten {kante.Nach.Id}: {naechste.Count} Fortsetzungen.");
                index = naechste[0];
            }
            ringe.Add(new Ring
            {
                Kanten = EntferneLiniengleicheZwischenknoten(ringkanten),
                Rohkanten = ringkanten,
            });
        }
        return ringe;
    }

    private static List<GerichteteKante> EntferneLiniengleicheZwischenknoten(
        IReadOnlyList<GerichteteKante> ring)
    {
        var knoten = ring.Select(kante => kante.Von).ToList();
        var linien = ring.Select(kante => kante.Linie).ToList();
        var geaendert = true;
        while (geaendert && knoten.Count >= 3)
        {
            geaendert = false;
            for (var i = 0; i < knoten.Count; i++)
            {
                var vorher = Geometrie.Mod(i - 1, knoten.Count);
                if (linien[vorher].Id != linien[i].Id) continue;
                knoten.RemoveAt(i);
                linien.RemoveAt(i);
                geaendert = true;
                break;
            }
        }
        if (knoten.Count < 3)
            throw new InvalidOperationException("Die kombinatorische Vereinfachung zerstoerte einen Ring.");
        var ausgabe = new List<GerichteteKante>();
        for (var i = 0; i < knoten.Count; i++)
            ausgabe.Add(new GerichteteKante(knoten[i], knoten[(i + 1) % knoten.Count], linien[i]));
        return ausgabe;
    }

    private sealed class DisjunkteMenge
    {
        private readonly int[] _eltern;
        private readonly byte[] _rang;

        internal DisjunkteMenge(int anzahl)
        {
            _eltern = Enumerable.Range(0, anzahl).ToArray();
            _rang = new byte[anzahl];
        }

        internal int Wurzel(int wert)
        {
            if (_eltern[wert] != wert) _eltern[wert] = Wurzel(_eltern[wert]);
            return _eltern[wert];
        }

        internal void Vereinige(int a, int b)
        {
            var wurzelA = Wurzel(a);
            var wurzelB = Wurzel(b);
            if (wurzelA == wurzelB) return;
            if (_rang[wurzelA] < _rang[wurzelB])
                _eltern[wurzelA] = wurzelB;
            else if (_rang[wurzelA] > _rang[wurzelB])
                _eltern[wurzelB] = wurzelA;
            else
            {
                _eltern[wurzelB] = wurzelA;
                _rang[wurzelA]++;
            }
        }
    }
}
