using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry.Zellen;

/** Diagnosekopie der privaten Lochtrennschritte; kein Produktionscode. */
internal static partial class Program
{
    private static IReadOnlyList<GerichteteKante> ExakteRohkanten(
        Ring ring, GerichteteKante vereinfacht)
    {
        for (var start = 0; start < ring.Rohkanten.Count; start++)
        {
            var erste = ring.Rohkanten[start];
            if (erste.Von.Id != vereinfacht.Von.Id
                || erste.Linie.Id != vereinfacht.Linie.Id)
                continue;
            var ausgabe = new List<GerichteteKante>();
            for (var schritt = 0; schritt < ring.Rohkanten.Count; schritt++)
            {
                var roh = ring.Rohkanten[
                    (start + schritt) % ring.Rohkanten.Count];
                if (roh.Linie.Id != vereinfacht.Linie.Id) break;
                ausgabe.Add(roh);
                if (roh.Nach.Id == vereinfacht.Nach.Id) return ausgabe;
            }
        }
        throw new InvalidOperationException(
            "Vereinfachte Kante ist nicht exakt in ihrer Rohknotenfolge enthalten.");
    }

    private static Dictionary<KantenSchluessel, int> Nahtkomponenten(
        IReadOnlyList<KantenSchluessel> kanten)
    {
        var anKnoten = new Dictionary<int, List<int>>();
        for (var i = 0; i < kanten.Count; i++)
            foreach (var knoten in new[] { kanten[i].Klein, kanten[i].Gross })
            {
                List<int> liste;
                if (!anKnoten.TryGetValue(knoten, out liste))
                {
                    liste = new List<int>();
                    anKnoten.Add(knoten, liste);
                }
                liste.Add(i);
            }
        var ausgabe = new Dictionary<KantenSchluessel, int>();
        var offen = new HashSet<int>(Enumerable.Range(0, kanten.Count));
        var komponent = 0;
        while (offen.Count != 0)
        {
            var warteschlange = new List<int> { offen.Min() };
            offen.Remove(warteschlange[0]);
            for (var gelesen = 0; gelesen < warteschlange.Count; gelesen++)
            {
                var index = warteschlange[gelesen];
                ausgabe.Add(kanten[index], komponent);
                foreach (var knoten in new[]
                         { kanten[index].Klein, kanten[index].Gross })
                    foreach (var nachbar in anKnoten[knoten])
                        if (offen.Remove(nachbar)) warteschlange.Add(nachbar);
            }
            komponent++;
        }
        return ausgabe;
    }

    private static (
        List<NahtoperationDiagnose> Operationen,
        HashSet<KantenSchluessel> Gesperrt) DiagnoseLochtrennung(
            IReadOnlyList<Zelle> zellen,
            IReadOnlyList<Flaeche> vorher,
            Rahmen rahmen,
            IReadOnlyDictionary<KantenSchluessel,
                List<KantenfundDiagnose>> kantenindex)
    {
        var gesperrt = new HashSet<KantenSchluessel>();
        var operationen = new List<NahtoperationDiagnose>();
        var aktuell = vorher.ToList();
        while (aktuell.Any(flaeche => flaeche.Loecher.Count != 0))
        {
            var neueKantenInRunde = 0;
            foreach (var lochflaeche in aktuell.Where(
                         flaeche => flaeche.Loecher.Count != 0).ToList())
            {
                var flaechenzellen = new HashSet<int>(lochflaeche.ZellIds);
                var trennzellen = new HashSet<int>();
                var ziele = DiagnoseRandzellen(
                    lochflaeche.Aussenring, flaechenzellen, kantenindex);
                foreach (var loch in lochflaeche.Loecher)
                {
                    var pfad = DiagnoseFindePfad(
                        lochflaeche, loch, ziele, zellen,
                        kantenindex, gesperrt);
                    trennzellen.UnionWith(pfad);
                    ziele.UnionWith(pfad);
                }
                if (trennzellen.Count == lochflaeche.ZellIds.Count)
                    throw new InvalidOperationException(
                        "Diagnosepfad belegt die gesamte Materialflaeche.");

                var neueKanten = new List<KantenSchluessel>();
                foreach (var paar in kantenindex)
                {
                    var funde = paar.Value;
                    if (funde.Count != 2) continue;
                    var a = funde[0];
                    var b = funde[1];
                    if (!flaechenzellen.Contains(a.Zelle.Id)
                        || !flaechenzellen.Contains(b.Zelle.Id))
                        continue;
                    if (trennzellen.Contains(a.Zelle.Id)
                        == trennzellen.Contains(b.Zelle.Id))
                        continue;
                    if (gesperrt.Add(paar.Key)) neueKanten.Add(paar.Key);
                }
                if (neueKanten.Count == 0)
                    throw new InvalidOperationException(
                        "Diagnose fand keine neue Trennkante.");
                neueKantenInRunde += neueKanten.Count;
                operationen.Add(new NahtoperationDiagnose
                {
                    Nummer = operationen.Count,
                    Material = lochflaeche.Material,
                    LochSchwerpunktWelt = rahmen.NachWelt(
                        Geometrie.Schwerpunkt(lochflaeche.Loecher[0])),
                    Pfadzellen = trennzellen.Count,
                    Kanten = neueKanten,
                });
            }
            if (neueKantenInRunde == 0)
                throw new InvalidOperationException(
                    "Diagnose-Lochtrennung macht keinen Fortschritt.");
            aktuell = Vereinigung.Vereinige(zellen, gesperrt).Flaechen;
            if (operationen.Count > zellen.Count)
                throw new InvalidOperationException(
                    "Diagnose-Lochtrennung endet nicht.");
        }
        return (operationen, gesperrt);
    }

    private static List<int> DiagnoseFindePfad(
        Flaeche flaeche,
        Ring loch,
        ISet<int> zielzellen,
        IReadOnlyList<Zelle> zellen,
        IReadOnlyDictionary<KantenSchluessel,
            List<KantenfundDiagnose>> kantenindex,
        ISet<KantenSchluessel> gesperrt)
    {
        var zellIds = new HashSet<int>(flaeche.ZellIds);
        var lochzellen = DiagnoseRandzellen(loch, zellIds, kantenindex);
        if (lochzellen.Count == 0 || zielzellen.Count == 0)
            throw new InvalidOperationException(
                "Loch oder Zielring hat keine tragende Zelle.");

        var direkt = lochzellen.Where(zielzellen.Contains)
            .OrderByDescending(id => ZellmindestkanteDiagnose(zellen[id]))
            .ThenByDescending(id => Geometrie.Flaeche(zellen[id].Polygon))
            .ThenBy(id => id).DefaultIfEmpty(-1).First();
        if (direkt >= 0) return new List<int> { direkt };

        var nachbarn = zellIds.ToDictionary(id => id, id => new List<int>());
        foreach (var paar in kantenindex)
        {
            if (gesperrt.Contains(paar.Key) || paar.Value.Count != 2) continue;
            var a = paar.Value[0].Zelle.Id;
            var b = paar.Value[1].Zelle.Id;
            if (!zellIds.Contains(a) || !zellIds.Contains(b)) continue;
            nachbarn[a].Add(b);
            nachbarn[b].Add(a);
        }

        var warteschlange = new List<int>();
        var gelesen = 0;
        var vorgaenger = new Dictionary<int, int?>();
        foreach (var start in lochzellen
                     .OrderByDescending(id => ZellmindestkanteDiagnose(zellen[id]))
                     .ThenByDescending(id => Geometrie.Flaeche(zellen[id].Polygon))
                     .ThenBy(id => id))
        {
            warteschlange.Add(start);
            vorgaenger.Add(start, null);
        }
        int? ziel = null;
        while (gelesen < warteschlange.Count)
        {
            var wert = warteschlange[gelesen++];
            if (zielzellen.Contains(wert))
            {
                ziel = wert;
                break;
            }
            foreach (var nachbar in nachbarn[wert].OrderBy(id => id))
            {
                if (vorgaenger.ContainsKey(nachbar)) continue;
                vorgaenger.Add(nachbar, wert);
                warteschlange.Add(nachbar);
            }
        }
        if (!ziel.HasValue)
            throw new InvalidOperationException("Diagnose findet keinen Zellpfad.");
        var pfad = new List<int>();
        for (int? zelle = ziel;
             zelle.HasValue;
             zelle = vorgaenger[zelle.Value])
            pfad.Add(zelle.Value);
        pfad.Reverse();
        return pfad;
    }

    private static HashSet<int> DiagnoseRandzellen(
        Ring ring,
        ISet<int> flaechenzellen,
        IReadOnlyDictionary<KantenSchluessel,
            List<KantenfundDiagnose>> kantenindex)
    {
        var ausgabe = new HashSet<int>();
        foreach (var kante in ring.Rohkanten)
        {
            List<KantenfundDiagnose> funde;
            if (!kantenindex.TryGetValue(kante.Schluessel, out funde)) continue;
            foreach (var fund in funde)
                if (flaechenzellen.Contains(fund.Zelle.Id))
                    ausgabe.Add(fund.Zelle.Id);
        }
        return ausgabe;
    }

    private static void PruefeNahtaequivalenz(
        Bauergebnis bau,
        IReadOnlyList<NahtoperationDiagnose> diagnose,
        ISet<KantenSchluessel> gesperrt,
        IReadOnlyDictionary<KantenSchluessel,
            List<KantenfundDiagnose>> kantenindex)
    {
        var produktion = bau.Lochtrennung.Trennnaehte;
        if (produktion.Count != diagnose.Count)
            throw new InvalidOperationException(
                "Diagnose und Produktion haben verschiedene Nahtzahlen.");
        for (var i = 0; i < diagnose.Count; i++)
        {
            var a = produktion[i];
            var b = diagnose[i];
            var laenge = b.Kanten.Sum(schluessel =>
            {
                var kante = kantenindex[schluessel][0].Kante;
                return Geometrie.Laenge(kante.Nach.Punkt - kante.Von.Punkt);
            });
            if (a.Material != b.Material
                || a.Pfadzellen != b.Pfadzellen
                || a.VorhandeneKanten != b.Kanten.Count
                || Math.Abs(a.Kantenlaenge - laenge) > 1e-9)
                throw new InvalidOperationException(
                    $"Diagnoseoperation {i} weicht vom Produktionsbericht ab.");
        }

        var endnaehte = new HashSet<KantenSchluessel>(bau.Flaechen
            .SelectMany(flaeche => flaeche.AlleRinge)
            .SelectMany(ring => ring.Rohkanten)
            .Where(kante =>
            {
                List<KantenfundDiagnose> funde;
                return kantenindex.TryGetValue(kante.Schluessel, out funde)
                    && funde.Count == 2
                    && funde[0].Zelle.Material == funde[1].Zelle.Material;
            }).Select(kante => kante.Schluessel));
        if (!endnaehte.SetEquals(gesperrt))
            throw new InvalidOperationException(
                "Gesperrte Diagnosekanten und gleichmaterialiger Endrand weichen ab.");
    }
}
