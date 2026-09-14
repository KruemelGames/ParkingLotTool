namespace Zellenversuch;

internal static class Geometrie
{
    internal static int Mod(int wert, int modul)
    {
        var rest = wert % modul;
        return rest < 0 ? rest + modul : rest;
    }

    internal static double Kreuz(Punkt a, Punkt b) => a.X * b.Y - a.Y * b.X;
    internal static double Skalar(Punkt a, Punkt b) => a.X * b.X + a.Y * b.Y;
    internal static double Laenge(Punkt vektor) => Math.Sqrt(Skalar(vektor, vektor));

    internal static double Vorzeichenflaeche(IEnumerable<Punkt> punkte)
    {
        var ring = punkte.ToArray();
        var summe = 0.0;
        for (var i = 0; i < ring.Length; i++)
            summe += Kreuz(ring[i], ring[(i + 1) % ring.Length]);
        return summe / 2;
    }

    internal static double Flaeche(Polygon polygon) => Math.Abs(Vorzeichenflaeche(polygon.Punkte));

    internal static Rahmen Reihenrahmen(IReadOnlyList<Punkt> punkte)
    {
        var besteLaenge = -1.0;
        var richtung = default(Punkt);
        for (var i = 0; i < punkte.Count; i++)
        {
            var kante = punkte[(i + 1) % punkte.Count] - punkte[i];
            var quadrat = Skalar(kante, kante);
            if (quadrat <= besteLaenge) continue;
            besteLaenge = quadrat;
            richtung = kante;
        }

        if (besteLaenge <= 0) throw new InvalidOperationException("Das Areal hat keine Kante mit Laenge.");
        var x = richtung * (1 / Math.Sqrt(besteLaenge));
        return new Rahmen(x, new Punkt(-x.Y, x.X));
    }

    internal static bool IstReflex(Polygon polygon, int index)
    {
        if (Vorzeichenflaeche(polygon.Punkte) <= 0)
            throw new InvalidOperationException("Die Reflexpruefung erwartet einen CCW-Ring.");
        var a = polygon.Knoten(index - 1).Punkt;
        var b = polygon.Knoten(index).Punkt;
        var c = polygon.Knoten(index + 1).Punkt;
        return Kreuz(b - a, c - b) < 0;
    }

    internal static bool IstKonvex(Polygon polygon)
    {
        if (Vorzeichenflaeche(polygon.Punkte) <= 0) return false;
        for (var i = 0; i < polygon.Anzahl; i++)
            if (IstReflex(polygon, i)) return false;
        return true;
    }

    /// <summary>
    /// Gerade-Ungerade-Test ohne Rand-Epsilon. Die Messpunkte liegen in der
    /// Mitte der 0,1-m-Rasterzellen; die fuenf Versuchsformen benoetigen daher
    /// keine Sonderbehandlung fuer beinahe getroffene Kanten.
    /// </summary>
    internal static bool Enthaelt(IReadOnlyList<Punkt> ring, Punkt punkt)
    {
        var innen = false;
        for (var i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            if ((a.Y > punkt.Y) == (b.Y > punkt.Y)) continue;
            var schnittX = a.X + (punkt.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y);
            if (punkt.X < schnittX) innen = !innen;
        }
        return innen;
    }

    internal static bool EnthaeltOderRand(IReadOnlyList<Punkt> ring, Punkt punkt)
    {
        for (var i = 0; i < ring.Count; i++)
            if (LiegtAufStrecke(punkt, ring[i], ring[(i + 1) % ring.Count]))
                return true;
        return Enthaelt(ring, punkt);
    }

    internal static bool LiegtAufStrecke(Punkt punkt, Punkt a, Punkt b)
    {
        if (Kreuz(b - a, punkt - a) != 0) return false;
        return Skalar(punkt - a, punkt - b) <= 0;
    }

    internal static bool Enthaelt(Flaeche flaeche, Punkt punkt)
    {
        if (!Enthaelt(flaeche.Aussenring.Knoten.Select(k => k.Punkt).ToArray(), punkt))
            return false;
        return flaeche.Loecher.All(loch =>
            !Enthaelt(loch.Knoten.Select(k => k.Punkt).ToArray(), punkt));
    }

    internal static Punkt Mittelwert(Polygon polygon)
    {
        var x = 0.0;
        var y = 0.0;
        foreach (var punkt in polygon.Punkte)
        {
            x += punkt.X;
            y += punkt.Y;
        }
        return new Punkt(x / polygon.Anzahl, y / polygon.Anzahl);
    }

    internal static Punkt Schwerpunkt(Ring ring)
    {
        var doppelteFlaeche = 0.0;
        var x = 0.0;
        var y = 0.0;
        var punkte = ring.Knoten.Select(knoten => knoten.Punkt).ToArray();
        for (var i = 0; i < punkte.Length; i++)
        {
            var a = punkte[i];
            var b = punkte[(i + 1) % punkte.Length];
            var kreuz = Kreuz(a, b);
            doppelteFlaeche += kreuz;
            x += (a.X + b.X) * kreuz;
            y += (a.Y + b.Y) * kreuz;
        }
        if (doppelteFlaeche == 0)
            throw new InvalidOperationException("Ein Ring ohne Flaeche hat keinen Schwerpunkt.");
        return new Punkt(x / (3 * doppelteFlaeche), y / (3 * doppelteFlaeche));
    }

    internal static double AbstandPunktStrecke(Punkt punkt, Punkt a, Punkt b)
    {
        var kante = b - a;
        var laengenquadrat = Skalar(kante, kante);
        if (laengenquadrat == 0) return Laenge(punkt - a);
        var t = Skalar(punkt - a, kante) / laengenquadrat;
        t = Math.Max(0, Math.Min(1, t));
        return Laenge(punkt - (a + kante * t));
    }
}
