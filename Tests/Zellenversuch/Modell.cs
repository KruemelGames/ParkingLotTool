namespace Zellenversuch;

internal readonly record struct Punkt(double X, double Y)
{
    public static Punkt operator +(Punkt a, Punkt b) => new(a.X + b.X, a.Y + b.Y);
    public static Punkt operator -(Punkt a, Punkt b) => new(a.X - b.X, a.Y - b.Y);
    public static Punkt operator *(Punkt a, double faktor) => new(a.X * faktor, a.Y * faktor);
}

internal sealed class Knoten
{
    internal Knoten(int id, Punkt punkt)
    {
        Id = id;
        Punkt = punkt;
    }

    internal int Id { get; }
    internal Punkt Punkt { get; }
}

internal enum Linienart
{
    Aussenkante,
    Teilungsnaht,
    RasterX,
    BandY,
    Innenrand,
    Randstrassenkante,
    Querstrassenkante,
    Zufahrtskante,
}

internal enum Schnittachse
{
    Keine,
    X,
    Y,
}

internal sealed class Linie
{
    internal Linie(
        int id,
        Linienart art,
        string name,
        double a = 0,
        double b = 0,
        double c = 0,
        Schnittachse achse = Schnittachse.Keine,
        double achsenwert = 0)
    {
        Id = id;
        Art = art;
        Name = name;
        A = a;
        B = b;
        C = c;
        Achse = achse;
        Achsenwert = achsenwert;
    }

    internal int Id { get; }
    internal Linienart Art { get; }
    internal string Name { get; }
    internal double A { get; }
    internal double B { get; }
    internal double C { get; }
    internal Schnittachse Achse { get; }
    internal double Achsenwert { get; }

    internal double Seite(Punkt punkt) => A * punkt.X + B * punkt.Y - C;
}

/// <summary>
/// Ecke eines gegen den Uhrzeigersinn laufenden Rings. Die Linie gehoert zur
/// Kante von diesem Knoten zum naechsten. Ihre Identitaet ueberlebt jede
/// Unterteilung; dadurch lassen sich kollineare Zwischenknoten spaeter ohne
/// Koordinatenvergleich entfernen.
/// </summary>
internal readonly record struct Ecke(Knoten Knoten, Linie LinieBisNaechste);

internal sealed class Polygon
{
    internal Polygon(IEnumerable<Ecke> ecken)
    {
        Ecken = ecken.ToList();
        if (Ecken.Count < 3)
            throw new InvalidOperationException("Ein Polygon braucht mindestens drei Ecken.");
    }

    internal List<Ecke> Ecken { get; }
    internal int Anzahl => Ecken.Count;
    internal Knoten Knoten(int index) => Ecken[Geometrie.Mod(index, Anzahl)].Knoten;
    internal Linie Linie(int index) => Ecken[Geometrie.Mod(index, Anzahl)].LinieBisNaechste;
    internal IEnumerable<Punkt> Punkte => Ecken.Select(ecke => ecke.Knoten.Punkt);
}

internal enum Material
{
    Asphalt,
    Gruen,
}

internal enum Zellart
{
    Randband,
    Restgruen,
    Bucht,
    Restbelag,
    Kappe,
    Fahrgasse,
    Gruenstreifen,
    Querstrasse,
    Zufahrt,
}

internal sealed class Zelle
{
    internal required int Id { get; init; }
    internal required Polygon Polygon { get; init; }
    internal required Material Material { get; init; }
    internal required Zellart Art { get; init; }
    internal required int QuellzelleId { get; init; }
    internal required Material Ursprungsmaterial { get; init; }
    internal required Zellart Ursprungsart { get; init; }
    internal int? BuchtId { get; init; }
    internal int? QuerstrassenId { get; init; }
    internal int Zufahrtsdeckungen { get; init; }
}

internal readonly record struct KantenSchluessel(int Klein, int Gross)
{
    internal static KantenSchluessel Von(Knoten a, Knoten b) =>
        a.Id < b.Id ? new(a.Id, b.Id) : new(b.Id, a.Id);
}

internal readonly record struct SchnittSchluessel(int Klein, int Gross, int Schnittlinie)
{
    internal static SchnittSchluessel Von(Knoten a, Knoten b, Linie linie) =>
        a.Id < b.Id
            ? new(a.Id, b.Id, linie.Id)
            : new(b.Id, a.Id, linie.Id);
}

internal readonly record struct GerichteteKante(Knoten Von, Knoten Nach, Linie Linie)
{
    internal KantenSchluessel Schluessel => KantenSchluessel.Von(Von, Nach);
}

internal sealed class Ring
{
    internal required List<GerichteteKante> Kanten { get; init; }
    internal required List<GerichteteKante> Rohkanten { get; init; }
    internal IEnumerable<Knoten> Knoten => Kanten.Select(kante => kante.Von);
    internal double Vorzeichenflaeche => Geometrie.Vorzeichenflaeche(Knoten.Select(k => k.Punkt));
}

internal sealed class Flaeche
{
    internal required int Id { get; init; }
    internal required Material Material { get; init; }
    internal required Ring Aussenring { get; init; }
    internal required List<Ring> Loecher { get; init; }
    internal required List<int> ZellIds { get; init; }

    internal IEnumerable<Ring> AlleRinge
    {
        get
        {
            yield return Aussenring;
            foreach (var loch in Loecher) yield return loch;
        }
    }

    internal double Flaecheninhalt =>
        Aussenring.Vorzeichenflaeche + Loecher.Sum(ring => ring.Vorzeichenflaeche);
}

internal sealed record Formdefinition(string Name, IReadOnlyList<Punkt> Punkte);

internal sealed record Zufahrtsvorgabe(int Kante, double Along);

internal sealed class Zufahrtsgeometrie
{
    internal required int Nummer { get; init; }
    internal required Zufahrtsvorgabe Vorgabe { get; init; }
    internal required Punkt Start { get; init; }
    internal required Punkt Tangente { get; init; }
    internal required Punkt Innennormale { get; init; }
    internal required double Tiefe { get; init; }
    internal required Linie LinkeKante { get; init; }
    internal required Linie RechteKante { get; init; }
}

internal sealed class Zufahrtsbericht
{
    internal required int Vorgaben { get; init; }
    internal required int Teilungslinien { get; init; }
    internal required int WirksameTeilungslinien { get; init; }
    internal required int NeueGeometriepunkte { get; init; }
    internal required int GemeinsamGenutzteNeuePunkte { get; init; }
    internal required int GemeinsamePunktabweichungen { get; init; }
    internal required int NeuePunkteMitZuWenigNutzern { get; init; }
    internal required IReadOnlyList<Punktnutzungsfehler> Punktnutzungsfehler { get; init; }
    internal required int NullflaechenzellenVorher { get; init; }
    internal required int Nullflaechenzellen { get; init; }
    internal required int SplitterzellenVorher { get; init; }
    internal required int Splitterzellen { get; init; }
    internal required double KleinsteZellflaecheVorher { get; init; }
    internal required double KleinsteZellflaeche { get; init; }
    internal required double Zufahrtsflaeche { get; init; }
    internal required double MehrfachUeberdeckteFlaeche { get; init; }
    internal required double UmgewidmetesGruen { get; init; }
    internal required int EntfalleneBuchten { get; init; }
    internal required int GetroffeneRandseiten { get; init; }
    internal required IReadOnlyList<Zufahrtsgeometrie> Zufahrten { get; init; }
}

internal sealed record Punktnutzungsfehler(
    Knoten Knoten,
    int Nutzer,
    int Erwartet,
    Linienart Quelllinienart);

internal sealed record Schnittbeobachtung(
    Linie Quelllinie,
    Linie Schnittlinie,
    Knoten Knoten,
    KantenSchluessel Quellkante,
    int Anfragen);

internal readonly record struct Rahmen(Punkt XAchse, Punkt YAchse)
{
    internal Punkt NachLokal(Punkt welt) =>
        new(Geometrie.Skalar(welt, XAchse), Geometrie.Skalar(welt, YAchse));

    internal Punkt NachWelt(Punkt lokal) => XAchse * lokal.X + YAchse * lokal.Y;
}

internal sealed record Bandabschnitt(
    int Id,
    double Anfang,
    double Ende,
    Zellart Art,
    int? ReihenId = null);

internal sealed class Bandplan
{
    internal required IReadOnlyList<Bandabschnitt> Baender { get; init; }

    internal IEnumerable<double> InnereGrenzen => Baender
        .SelectMany(band => new[] { band.Anfang, band.Ende })
        .Distinct()
        .OrderBy(wert => wert);

    internal Bandabschnitt Bei(double y) => Baender.First(band => y >= band.Anfang && y < band.Ende);

    internal static Material MaterialVon(Zellart art) =>
        art is Zellart.Bucht or Zellart.Restbelag or Zellart.Fahrgasse
            or Zellart.Querstrasse or Zellart.Zufahrt
            ? Material.Asphalt
            : Material.Gruen;
}

internal enum Spaltenart
{
    Buchtfeld,
    Kappenrest,
    Querstrasse,
}

internal sealed record Spaltenabschnitt(
    int Id,
    double Anfang,
    double Ende,
    Spaltenart Art,
    int? QuerstrassenId = null);

internal sealed record Querstrassenplan(
    int Id,
    double Mitte,
    double Anfang,
    double Ende,
    Linie LinkeKante,
    Linie RechteKante);

internal sealed class Spaltenplan
{
    internal required IReadOnlyList<Spaltenabschnitt> Spalten { get; init; }
    internal required IReadOnlyList<Querstrassenplan> Querstrassen { get; init; }
    internal required IReadOnlyList<Linie> Schnittlinien { get; init; }

    internal Spaltenabschnitt Bei(double x) =>
        Spalten.First(spalte => x >= spalte.Anfang && x < spalte.Ende);
}

internal sealed record Lochlage(
    Material Material,
    Punkt SchwerpunktWelt,
    double MinX,
    double MaxX,
    double MinY,
    double MaxY);

internal sealed record Trennnaht(
    int Nummer,
    Material Material,
    Punkt LochSchwerpunktWelt,
    int Pfadzellen,
    int VorhandeneKanten,
    double Kantenlaenge);

internal sealed class Lochtrennbericht
{
    internal required int FlaechenVorher { get; init; }
    internal required int FlaechenNachher { get; init; }
    internal required int LochflaechenVorher { get; init; }
    internal required int LoecherVorher { get; init; }
    internal required int LochflaechenNachher { get; init; }
    internal required int LoecherNachher { get; init; }
    internal required int NeueGeometriepunkte { get; init; }
    internal required bool NurVorhandeneZellkanten { get; init; }
    internal required IReadOnlyList<Trennnaht> Trennnaehte { get; init; }
}

internal sealed class Topologiebericht
{
    internal int NichtMannigfaltigeKanten { get; set; }
    internal int GleichgerichteteDoppelkanten { get; set; }
    internal int OffeneTeilungsnaehte { get; set; }
    internal int UnerwarteteOffeneKanten { get; set; }
    internal int AbweichendeLinienIds { get; set; }
    internal int Aussenkanten { get; set; }
    internal int Innenkanten { get; set; }
}

internal sealed class Bauergebnis
{
    internal required Formdefinition Form { get; init; }
    internal required Rahmen Rahmen { get; init; }
    internal required Polygon ArealLokal { get; init; }
    internal required List<Polygon> KonvexeTeile { get; init; }
    internal required List<Zelle> ZellenVorZufahrt { get; init; }
    internal required List<Zelle> Zellen { get; init; }
    internal required List<Flaeche> FlaechenVorTrennung { get; init; }
    internal required List<Flaeche> Flaechen { get; init; }
    internal required Bandplan Bandplan { get; init; }
    internal required Spaltenplan Spaltenplan { get; init; }
    internal required IReadOnlyList<Punkt> Innenrand { get; init; }
    internal required IReadOnlyList<Punkt> Randstrassenrand { get; init; }
    internal required Zufahrtsbericht Zufahrtsbericht { get; init; }
    internal required Lochtrennbericht Lochtrennung { get; init; }
    internal required Topologiebericht Topologie { get; init; }
    internal required TimeSpan EinzelneBauzeit { get; init; }

    internal int Buchtenzahl => Zellen
        .Where(zelle => zelle.BuchtId.HasValue)
        .Select(zelle => zelle.BuchtId!.Value)
        .Distinct()
        .Count();
}
