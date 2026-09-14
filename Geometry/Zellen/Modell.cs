using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingLotTool.Geometry.Zellen
{
    internal readonly struct Punkt
    {
        internal Punkt(double x, double y)
        {
            X = x;
            Y = y;
        }

        internal double X { get; }
        internal double Y { get; }

        public static Punkt operator +(Punkt a, Punkt b) => new Punkt(a.X + b.X, a.Y + b.Y);
        public static Punkt operator -(Punkt a, Punkt b) => new Punkt(a.X - b.X, a.Y - b.Y);
        public static Punkt operator *(Punkt a, double faktor) =>
            new Punkt(a.X * faktor, a.Y * faktor);
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
        Randstrasseninnenkante,
        Randstrassenstoss,
        Randbandstoss,
        Randbuchtgrenze,
        Querstrassenkante,
        Zufahrtskante,
        Zoningkante,
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
            double achsenwert = 0,
            int? tragendeGeradenId = null)
        {
            Id = id;
            Art = art;
            Name = name;
            A = a;
            B = b;
            C = c;
            Achse = achse;
            Achsenwert = achsenwert;
            TragendeGeradenId = tragendeGeradenId ?? id;
        }

        internal int Id { get; }
        internal Linienart Art { get; }
        internal string Name { get; }
        internal double A { get; }
        internal double B { get; }
        internal double C { get; }
        internal Schnittachse Achse { get; }
        internal double Achsenwert { get; }
        // Eine Teilungsnaht verlaengert eine vorhandene Kante. Ihre eigene ID
        // bleibt fuer den Zellgraphen erhalten; diese ID bildet zusaetzlich die
        // konstruktiv bekannte, gemeinsame Gerade ab.
        internal int TragendeGeradenId { get; }

        internal double Seite(Punkt punkt)
        {
            var wert = A * punkt.X + B * punkt.Y - C;
            // Die Koeffizienten beliebig gerichteter Linien sind nicht
            // normiert. Deshalb entspricht erst wert / |(A,B)| einem Abstand.
            // Bei L schraeg lag ein vorhandener Knoten nur in der letzten
            // double-Stelle neben der neuen x=76,2-Rasterlinie; ohne FIT_EPS
            // entstand daraus ein orientierungsloses Femtometer-Polygon statt
            // eines Randtreffers.
            var normalenlaenge = Math.Sqrt(A * A + B * B);
            return normalenlaenge != 0
                && Math.Abs(wert) <= 1e-6 * normalenlaenge
                    ? 0
                    : wert;
        }
    }

    /// <summary>
    /// Ecke eines gegen den Uhrzeigersinn laufenden Rings. Die Linie gehoert zur
    /// Kante von diesem Knoten zum naechsten. Ihre Identitaet ueberlebt jede
    /// Unterteilung; dadurch lassen sich kollineare Zwischenknoten spaeter ohne
    /// Koordinatenvergleich entfernen.
    /// </summary>
    internal readonly struct Ecke
    {
        internal Ecke(Knoten knoten, Linie linieBisNaechste)
        {
            Knoten = knoten;
            LinieBisNaechste = linieBisNaechste;
        }

        internal Knoten Knoten { get; }
        internal Linie LinieBisNaechste { get; }
    }

    internal sealed class Polygon
    {
        internal Polygon(IEnumerable<Ecke> ecken)
        {
            Ecken = ecken.ToList();
            if (Ecken.Count < 3)
                throw new InvalidOperationException("A polygon needs at least three corners.");
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
        /**
         * BAULAND - der Boden unter den Parzellen.
         *
         * Ein drittes Material, kein umgewidmetes Gruen. Der Nutzer stellt
         * dafuer eine eigene Flaeche ein, so wie fuer Strasse und
         * Dekoration; ohne eigenen Wert liessen sich die beiden nicht
         * auseinanderhalten. Sein Befund nach dem ersten Bau: *"Es war
         * einfach alles gruen. Also einfach nur Dekoflaeche."*
         */
        Zoning,

        /**
         * DER BELAG DER ZONING-STRASSE - materiell derselbe Asphalt wie
         * Flaeche 1, aber ein eigener Wert, weil er ein anderes PREFAB
         * braucht.
         *
         * Flaechen sind Decals mit der Ebenenmaske `Terrain` und werden auf
         * Strassen unsichtbar. Der Nutzer hat es sofort gesehen: *"Die
         * Flaechen sind durch die Transparenz der Strasse zum Teil nicht
         * sichtbar."* Die Abhilfe steht seit der Vorflaeche im Projekt - ein
         * Klon mit `Terrain | Roads` - und die geht nur ueber eine eigene
         * Liste, weil ein Ring genau ein Prefab hat.
         */
        Zoningstrasse,
    }

    internal enum Zellart
    {
        Randband,
        Randstrasse,
        Restgruen,
        Bucht,
        Restbelag,
        Kappe,
        Fahrgasse,
        Gruenstreifen,
        Querstrasse,
        Zufahrt,
        /**
         * BAULAND - der Boden unter den Zoning-Parzellen.
         *
         * Eine eigene Art, kein umgewidmetes Restgruen: der Nutzer stellt
         * dafuer eine eigene Flaeche ein, so wie fuer Strasse und
         * Dekoration. Und sie muss sich von Gruen unterscheiden lassen,
         * sonst kann die Pruefung "hier darf keine Bucht stehen" gar nicht
         * formuliert werden.
         */
        Zoning,

        /**
         * Die Fahrbahn der Zoning-Strasse.
         *
         * Materiell Asphalt wie jede andere Strasse von uns, aber eine
         * EIGENE Art: als `Fahrgasse` gefuehrt wuerde die Buchtenplanung
         * sie fuer eine Parkgasse halten und Stellplaetze daran haengen.
         */
        Zoningstrasse,
    }

    internal sealed class Zelle
    {
        internal int Id { get; set; }
        internal Polygon Polygon { get; set; }
        internal Material Material { get; set; }
        internal Zellart Art { get; set; }
        internal int QuellzelleId { get; set; }
        internal Material Ursprungsmaterial { get; set; }
        internal Zellart Ursprungsart { get; set; }
        internal int? BuchtId { get; set; }
        internal int? QuerstrassenId { get; set; }
        internal int Zufahrtsdeckungen { get; set; }
        /**
         * WELCHE Zufahrt diese Zelle deckt - Index in der Zufahrtsliste.
         *
         * Seit es vier Arten gibt, reicht "ist Zufahrt" nicht mehr: die
         * Vorschau faerbt nach Art, und die Art haengt an der einzelnen
         * Zufahrt. Der Index ist dabei ein echtes Merkmal der Zelle, so wie
         * `BuchtId` und `QuerstrassenId` - keine Zuordnung ueber Reihenfolge
         * oder Nachbarschaft, die beim naechsten Filterschritt zerfaellt.
         *
         * Bei Ueberdeckung mehrerer Zufahrten gilt die erste; die Zahl der
         * Deckungen steht ohnehin daneben.
         *
         * Bewusst die ART und kein Index: ein Index zeigte in eine ANDERE
         * Liste als die, mit der die Vorschau spaeter arbeitet, und solche
         * Zuordnungen ueber Reihenfolge zerfallen beim ersten Filterschritt.
         * Die Art traegt sich selbst.
         */
        internal Zufahrtsart? Zugangsart { get; set; }
        // Geschlossene Randbaender werden vor der Zellbildung in zwei
        // lochfreie Umfangsboegen geplant. Die ID ist deren feste Grenze;
        // sie darf bei der gleichmaterialigen Vereinigung nicht verschwinden.
        internal int? Flaechenabschnitt { get; set; }
    }

    internal readonly struct KantenSchluessel : IEquatable<KantenSchluessel>
    {
        internal KantenSchluessel(int klein, int gross)
        {
            Klein = klein;
            Gross = gross;
        }

        internal int Klein { get; }
        internal int Gross { get; }

        internal static KantenSchluessel Von(Knoten a, Knoten b) =>
            a.Id < b.Id
                ? new KantenSchluessel(a.Id, b.Id)
                : new KantenSchluessel(b.Id, a.Id);

        public bool Equals(KantenSchluessel other) => Klein == other.Klein && Gross == other.Gross;
        public override bool Equals(object obj) => obj is KantenSchluessel other && Equals(other);
        public override int GetHashCode() => unchecked((Klein * 397) ^ Gross);
        public static bool operator ==(KantenSchluessel a, KantenSchluessel b) => a.Equals(b);
        public static bool operator !=(KantenSchluessel a, KantenSchluessel b) => !a.Equals(b);
    }

    internal readonly struct SchnittSchluessel : IEquatable<SchnittSchluessel>
    {
        internal SchnittSchluessel(int klein, int gross, int schnittlinie)
        {
            Klein = klein;
            Gross = gross;
            Schnittlinie = schnittlinie;
        }

        internal int Klein { get; }
        internal int Gross { get; }
        internal int Schnittlinie { get; }

        internal static SchnittSchluessel Von(Knoten a, Knoten b, Linie linie) =>
            a.Id < b.Id
                ? new SchnittSchluessel(a.Id, b.Id, linie.Id)
                : new SchnittSchluessel(b.Id, a.Id, linie.Id);

        public bool Equals(SchnittSchluessel other) =>
            Klein == other.Klein && Gross == other.Gross && Schnittlinie == other.Schnittlinie;
        public override bool Equals(object obj) => obj is SchnittSchluessel other && Equals(other);
        public override int GetHashCode() => unchecked(((Klein * 397) ^ Gross) * 397 ^ Schnittlinie);
    }

    internal readonly struct GerichteteKante
    {
        internal GerichteteKante(Knoten von, Knoten nach, Linie linie)
        {
            Von = von;
            Nach = nach;
            Linie = linie;
        }

        internal Knoten Von { get; }
        internal Knoten Nach { get; }
        internal Linie Linie { get; }
        internal KantenSchluessel Schluessel => KantenSchluessel.Von(Von, Nach);
    }

    internal sealed class Ring
    {
        internal List<GerichteteKante> Kanten { get; set; }
        internal List<GerichteteKante> Rohkanten { get; set; }
        internal IEnumerable<Knoten> Knoten => Kanten.Select(kante => kante.Von);
        internal double Vorzeichenflaeche =>
            Geometrie.Vorzeichenflaeche(Knoten.Select(knoten => knoten.Punkt));
    }

    internal sealed class Flaeche
    {
        internal int Id { get; set; }
        internal Material Material { get; set; }
        internal Ring Aussenring { get; set; }
        internal List<Ring> Loecher { get; set; }
        internal List<int> ZellIds { get; set; }

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

    internal sealed class Formdefinition
    {
        internal Formdefinition(string name, IReadOnlyList<Punkt> punkte)
        {
            Name = name;
            Punkte = punkte;
        }

        internal string Name { get; }
        internal IReadOnlyList<Punkt> Punkte { get; }
    }

    internal sealed class Zufahrtsvorgabe
    {
        internal Zufahrtsvorgabe(
            int kante,
            double along,
            Punkt? achsrichtungWelt = null,
            double? achslaenge = null,
            double breite = 7.0,
            Zufahrtsart art = Zufahrtsart.Zufahrt)
        {
            Kante = kante;
            Along = along;
            AchsrichtungWelt = achsrichtungWelt;
            Achslaenge = achslaenge;
            Breite = breite;
            Art = art;
        }

        internal int Kante { get; }
        internal double Along { get; }
        /** Nur der Eckfang gibt eine nicht lotrechte Zufahrtsachse vor. */
        internal Punkt? AchsrichtungWelt { get; }
        internal Punkt? AchsrichtungLokal { get; private set; }
        internal double? Achslaenge { get; }
        internal double Breite { get; }
        internal Zufahrtsart Art { get; }

        internal Zufahrtsvorgabe NachLokal(Rahmen rahmen)
        {
            var ausgabe = new Zufahrtsvorgabe(
                Kante, Along, AchsrichtungWelt, Achslaenge, Breite, Art);
            ausgabe.AchsrichtungLokal = AchsrichtungWelt.HasValue
                ? rahmen.NachLokal(AchsrichtungWelt.Value)
                : (Punkt?)null;
            return ausgabe;
        }
    }

    internal sealed class Zufahrtsgeometrie
    {
        internal int Nummer { get; set; }
        internal Zufahrtsvorgabe Vorgabe { get; set; }
        internal Punkt Start { get; set; }
        internal Punkt Tangente { get; set; }
        internal Punkt Querhalbvektor { get; set; }
        internal Punkt Innennormale { get; set; }
        internal double Tiefe { get; set; }
        internal Linie LinkeKante { get; set; }
        internal Linie RechteKante { get; set; }
    }

    internal sealed class Zufahrtsbericht
    {
        internal int Vorgaben { get; set; }
        internal int Teilungslinien { get; set; }
        internal int WirksameTeilungslinien { get; set; }
        internal int NeueGeometriepunkte { get; set; }
        internal int GemeinsamGenutzteNeuePunkte { get; set; }
        internal int GemeinsamePunktabweichungen { get; set; }
        internal int NeuePunkteMitZuWenigNutzern { get; set; }
        internal IReadOnlyList<Punktnutzungsfehler> Punktnutzungsfehler { get; set; }
        internal int NullflaechenzellenVorher { get; set; }
        internal int Nullflaechenzellen { get; set; }
        internal int SplitterzellenVorher { get; set; }
        internal int Splitterzellen { get; set; }
        internal double KleinsteZellflaecheVorher { get; set; }
        internal double KleinsteZellflaeche { get; set; }
        internal double Zufahrtsflaeche { get; set; }
        internal double MehrfachUeberdeckteFlaeche { get; set; }
        internal double UmgewidmetesGruen { get; set; }
        internal int EntfalleneBuchten { get; set; }
        internal int GetroffeneRandseiten { get; set; }
        internal IReadOnlyList<Zufahrtsgeometrie> Zufahrten { get; set; }
    }

    internal sealed class Punktnutzungsfehler
    {
        internal Punktnutzungsfehler(
            Knoten knoten,
            int nutzer,
            int erwartet,
            Linienart quelllinienart)
        {
            Knoten = knoten;
            Nutzer = nutzer;
            Erwartet = erwartet;
            Quelllinienart = quelllinienart;
        }

        internal Knoten Knoten { get; }
        internal int Nutzer { get; }
        internal int Erwartet { get; }
        internal Linienart Quelllinienart { get; }
    }

    internal sealed class Schnittbeobachtung
    {
        internal Schnittbeobachtung(
            Linie quelllinie,
            Linie schnittlinie,
            Knoten knoten,
            KantenSchluessel quellkante,
            int anfragen)
        {
            Quelllinie = quelllinie;
            Schnittlinie = schnittlinie;
            Knoten = knoten;
            Quellkante = quellkante;
            Anfragen = anfragen;
        }

        internal Linie Quelllinie { get; }
        internal Linie Schnittlinie { get; }
        internal Knoten Knoten { get; }
        internal KantenSchluessel Quellkante { get; }
        internal int Anfragen { get; }
    }

    internal readonly struct Rahmen
    {
        internal Rahmen(Punkt xAchse, Punkt yAchse)
        {
            XAchse = xAchse;
            YAchse = yAchse;
        }

        internal Punkt XAchse { get; }
        internal Punkt YAchse { get; }

        internal Punkt NachLokal(Punkt welt) =>
            new Punkt(Geometrie.Skalar(welt, XAchse), Geometrie.Skalar(welt, YAchse));

        internal Punkt NachWelt(Punkt lokal) => XAchse * lokal.X + YAchse * lokal.Y;
    }

    internal sealed class Bandabschnitt
    {
        internal Bandabschnitt(
            int id,
            double anfang,
            double ende,
            Zellart art,
            int? reihenId = null)
        {
            Id = id;
            Anfang = anfang;
            Ende = ende;
            Art = art;
            ReihenId = reihenId;
        }

        internal int Id { get; }
        internal double Anfang { get; }
        internal double Ende { get; }
        internal Zellart Art { get; }
        internal int? ReihenId { get; }
    }

    internal sealed class Bandplan
    {
        internal IReadOnlyList<Bandabschnitt> Baender { get; set; }
        internal IReadOnlyList<Parkmodulabschnitt> Module { get; set; }

        internal IEnumerable<double> InnereGrenzen => Baender
            .SelectMany(band => new[] { band.Anfang, band.Ende })
            .Distinct()
            .OrderBy(wert => wert);

        internal Bandabschnitt Bei(double y) =>
            Baender.First(band => y >= band.Anfang && y < band.Ende);

        internal static Material MaterialVon(Zellart art) =>
            art == Zellart.Zoningstrasse
                ? Material.Zoningstrasse
                : art == Zellart.Zoning
                ? Material.Zoning
                : art == Zellart.Randstrasse || art == Zellart.Bucht
                    || art == Zellart.Restbelag || art == Zellart.Fahrgasse
                    || art == Zellart.Querstrasse || art == Zellart.Zufahrt
                    ? Material.Asphalt
                    : Material.Gruen;
    }

    internal sealed class Parkmodulabschnitt
    {
        internal int Id { get; set; }
        internal Bandabschnitt ErsteReihe { get; set; }
        internal Bandabschnitt Fahrgasse { get; set; }
        internal Bandabschnitt ZweiteReihe { get; set; }
        internal double Anfang => ErsteReihe.Anfang;
        internal double Ende => ZweiteReihe.Ende;
        internal double Fahrgassenmitte =>
            (Fahrgasse.Anfang + Fahrgasse.Ende) / 2;

        internal IEnumerable<Bandabschnitt> Reihen
        {
            get
            {
                if (ErsteReihe.Ende > ErsteReihe.Anfang + 1e-6) yield return ErsteReihe;
                if (ZweiteReihe.Ende > ZweiteReihe.Anfang + 1e-6) yield return ZweiteReihe;
            }
        }
    }

    internal enum Spaltenart
    {
        Buchtfeld,
        Kappenrest,
        Querstrasse,
    }

    internal sealed class Spaltenabschnitt
    {
        internal Spaltenabschnitt(
            int id,
            double anfang,
            double ende,
            Spaltenart art,
            int? querstrassenId = null)
        {
            Id = id;
            Anfang = anfang;
            Ende = ende;
            Art = art;
            QuerstrassenId = querstrassenId;
        }

        internal int Id { get; }
        internal double Anfang { get; }
        internal double Ende { get; }
        internal Spaltenart Art { get; }
        internal int? QuerstrassenId { get; }
    }

    internal sealed class Querstrassenplan
    {
        internal bool Notwendig { get; set; }
        internal Querstrassenplan(
            int id,
            double mitte,
            double anfang,
            double ende,
            Linie linkeKante,
            Linie rechteKante)
        {
            Id = id;
            Mitte = mitte;
            Anfang = anfang;
            Ende = ende;
            LinkeKante = linkeKante;
            RechteKante = rechteKante;
        }

        internal int Id { get; }
        internal double Mitte { get; }
        internal double Anfang { get; }
        internal double Ende { get; }
        internal Linie LinkeKante { get; }
        internal Linie RechteKante { get; }
    }

    internal sealed class Spaltenplan
    {
        internal IReadOnlyList<Spaltenabschnitt> Spalten { get; set; }
        internal IReadOnlyList<Querstrassenplan> Querstrassen { get; set; }

        internal Spaltenabschnitt Bei(double x) =>
            Spalten.First(spalte => x >= spalte.Anfang && x < spalte.Ende);
    }

    internal sealed class Modulspaltenplan
    {
        internal Parkmodulabschnitt Modul { get; set; }
        internal double MinX { get; set; }
        internal double MaxX { get; set; }
        internal IReadOnlyList<int> UeberlebendeQuerstrassen { get; set; }
        internal IReadOnlyDictionary<int, Spaltenplan> Reihenplaene { get; set; }
    }

    internal sealed class Querstrassenpruefung
    {
        internal int ModulId { get; set; }
        internal int QuerstrassenId { get; set; }
        internal int ErsteLinks { get; set; }
        internal int ErsteRechts { get; set; }
        internal int ZweiteLinks { get; set; }
        internal int ZweiteRechts { get; set; }
        internal bool Bleibt { get; set; }

        internal int Minimum => Math.Min(
            Math.Min(ErsteLinks, ErsteRechts),
            Math.Min(ZweiteLinks, ZweiteRechts));
    }

    internal sealed class Querstrassenstueck
    {
        internal Querstrassenplan Querstrasse { get; set; }
        internal Punkt Anfang { get; set; }
        internal Punkt Ende { get; set; }

        internal bool Enthaelt(double x, double y) =>
            x >= Querstrasse.Anfang && x < Querstrasse.Ende
                && y >= Anfang.Y && y <= Ende.Y;
    }

    internal sealed class Modulplanung
    {
        internal IReadOnlyList<Modulspaltenplan> Plaene { get; set; }
        internal IReadOnlyList<Querstrassenpruefung> Pruefungen { get; set; }
        internal IReadOnlyList<Querstrassenstueck> Querstrassenstuecke { get; set; }
    }

    internal sealed class Randbuchtplan
    {
        internal int Id { get; set; }
        internal int Randkante { get; set; }
        internal Punkt[] Ecken { get; set; }
    }

    internal sealed class Randreihenschnitt
    {
        internal Linie Linie { get; set; }
        internal Punkt Aussen { get; set; }
        internal Punkt Innen { get; set; }
    }

    internal sealed class Randreihenplan
    {
        internal IReadOnlyList<Randbuchtplan> Buchten { get; set; }
        internal IReadOnlyList<Randreihenschnitt> Schnittlinien { get; set; }
    }

    internal sealed class Lochlage
    {
        internal Lochlage(
            Material material,
            Punkt schwerpunktWelt,
            double minX,
            double maxX,
            double minY,
            double maxY)
        {
            Material = material;
            SchwerpunktWelt = schwerpunktWelt;
            MinX = minX;
            MaxX = maxX;
            MinY = minY;
            MaxY = maxY;
        }

        internal Material Material { get; }
        internal Punkt SchwerpunktWelt { get; }
        internal double MinX { get; }
        internal double MaxX { get; }
        internal double MinY { get; }
        internal double MaxY { get; }
    }

    internal sealed class Trennnaht
    {
        internal Trennnaht(
            int nummer,
            Material material,
            Punkt lochSchwerpunktWelt,
            int pfadzellen,
            int vorhandeneKanten,
            double kantenlaenge)
        {
            Nummer = nummer;
            Material = material;
            LochSchwerpunktWelt = lochSchwerpunktWelt;
            Pfadzellen = pfadzellen;
            VorhandeneKanten = vorhandeneKanten;
            Kantenlaenge = kantenlaenge;
        }

        internal int Nummer { get; }
        internal Material Material { get; }
        internal Punkt LochSchwerpunktWelt { get; }
        internal int Pfadzellen { get; }
        internal int VorhandeneKanten { get; }
        internal double Kantenlaenge { get; }
    }

    internal sealed class Lochtrennbericht
    {
        internal int FlaechenVorher { get; set; }
        internal int FlaechenNachher { get; set; }
        internal int LochflaechenVorher { get; set; }
        internal int LoecherVorher { get; set; }
        internal int LochflaechenNachher { get; set; }
        internal int LoecherNachher { get; set; }
        internal int NeueGeometriepunkte { get; set; }
        internal bool NurVorhandeneZellkanten { get; set; }
        internal IReadOnlyList<Trennnaht> Trennnaehte { get; set; }
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

    /** Formteil und fertig abgeleiteter Reihenwinkel in Weltkoordinaten. */
    internal sealed class Teilflaechenrahmenvorgabe
    {
        internal int Index { get; set; }
        internal Punkt[] PunkteWelt { get; set; }
        internal double Winkel { get; set; }
        internal bool EigeneZuweisung { get; set; }

        /**
         * Die TRENNKANTEN dieses Teils - Paare (A, B) mit dem Teil LINKS.
         *
         * Wozu, wenn das Polygon doch schon dasteht: der Innenrand muss auf
         * dieses Teil beschnitten werden, und das geschah bis zum 2026-09-01
         * mit `SchneideMitKonvexemPolygon` - einem Verfahren, das gegen jede
         * Polygonkante als HALBEBENE schneidet. Fuer ein konvexes Teil ist
         * das exakt. Fuer ein konkaves nimmt es viel zu viel weg, und der
         * Teil bekommt fast keine Buchten mehr (gemessen: 46 statt 120 je
         * Hektar).
         *
         * Solange nur die automatische Zerlegung Teile machte, konnte das
         * nicht auffallen - die waren immer konvex. Seit der Nutzer den
         * Schnitt selbst zieht, sind konkave Teile der Normalfall.
         *
         * Die Loesung nutzt aus, dass ein Teil = Umriss GESCHNITTEN MIT den
         * Halbebenen seiner Trennkanten ist. Der Innenrand liegt ohnehin im
         * Umriss; ihn nur mit diesen Halbebenen zu schneiden ist deshalb
         * exakt - und Halbebenen sind genau das, was das vorhandene
         * Verfahren richtig kann.
         */
        internal (Punkt A, Punkt B)[] Trennkanten { get; set; }
    }

    /** Bereits aus genau den angezeigten Teilflaechen abgeleitete gemeinsame Kante. */
    internal sealed class Teilflaechennahtvorgabe
    {
        internal int ErstesTeil { get; set; }
        internal int ZweitesTeil { get; set; }
        internal Punkt AnfangWelt { get; set; }
        internal Punkt EndeWelt { get; set; }
    }

    /** Geplante Achse eines inneren Fahrwegs im gemeinsamen lokalen Rahmen. */
    internal sealed class Rasterstrassenplan
    {
        internal Zellart Art { get; set; }
        internal Punkt Anfang { get; set; }
        internal Punkt Ende { get; set; }
        internal int TeilIndex { get; set; }
        internal bool Teilflaechenverbindung { get; set; }
    }

    /**
     * Ein Bauland-Rechteck im Umriss, wie es der Nutzer gezogen hat.
     *
     * Die Ecke ist der Nullpunkt des Parzellenrasters, `Winkel` seine
     * Richtung - dieselbe Vereinbarung wie in `ParkingGeometry.Zoningflaeche`
     * auf der Werkzeugseite. Hier steht sie noch einmal, weil der Zellenkern
     * nichts von den oeffentlichen Typen wissen soll.
     */
    internal sealed class Zoningvorgabe
    {
        internal Punkt Ecke { get; set; }
        internal int Spalten { get; set; }
        internal int Reihen { get; set; }
        internal double Winkel { get; set; }

        /**
         * Der freizuhaltende Rand um die Parzellen, in Metern.
         *
         * Er traegt die Zoning-Strasse und - wenn aussen auch Bauland
         * entsteht - deren Parzellen. Der Zellenkern kennt nur diese eine
         * Zahl; ob sie aus Strassenbreite allein oder aus Strassenbreite
         * plus Aussentiefe stammt, entscheidet das Werkzeug.
         */
        internal double Rand { get; set; }

        /**
         * Halbe Breite der Zoning-Strasse.
         *
         * Am 2026-09-02 am Prefab GEMESSEN: Gasse und Schotterstrasse sind
         * beide 8,00 m breit. Die Achse liegt deshalb 4,00 m ausserhalb des
         * gezogenen Rechtecks - dann liegt die innere Fahrbahnkante genau
         * auf der Parzellengrenze.
         *
         * Das gilt unabhaengig vom `Rand`: kommt aussen noch Bauland dazu,
         * waechst der Rand, die Strasse bleibt aber an den inneren Parzellen.
         */
        internal const double Strassenhalbbreite = 4.0;

        /**
         * Die vier Ecken der STRASSENACHSE, im Uhrzeigersinn.
         *
         * Hierauf muessen Fahrgassen und Querstrassen enden, nicht am
         * Parzellenrand: CS2 verschmilzt nur Segmente mit IDENTISCHEM
         * Endpunkt zu einem Knoten. Dasselbe Muster wie bei der Zufahrt,
         * die bis zur Ringachse laeuft statt an der Fahrbahnkante zu enden.
         */
        internal Punkt[] Strassenring() =>
            EckenMitAufschlag(Strassenhalbbreite);

        /**
         * Die vier Ecken eines um `aufschlag` vergroesserten Rechtecks um die
         * Parzellen, im Uhrzeigersinn. Mit 0 ist das der gezogene Zug.
         *
         * Reihenfolge und Startpunkt sind festgelegt, weil Overlay,
         * Trefferpruefung und Zonenblock dieselbe Ecke als Nullpunkt
         * brauchen. Wer hier umsortiert, dreht die Parzellen.
         */
        internal Punkt[] EckenMitAufschlag(double aufschlag)
        {
            var bogen = Winkel * Math.PI / 180.0;
            var laengs = new Punkt(Math.Cos(bogen), Math.Sin(bogen));
            var quer = new Punkt(-laengs.Y, laengs.X);
            var ecke = Ecke - laengs * aufschlag - quer * aufschlag;
            var breite = laengs * (Spalten * 8.0 + 2 * aufschlag);
            var tiefe = quer * (Reihen * 8.0 + 2 * aufschlag);
            return new[]
            {
                ecke, ecke + breite, ecke + breite + tiefe, ecke + tiefe,
            };
        }

        /**
         * Die vier Ecken des FREIGEHALTENEN Bereichs, im Uhrzeigersinn.
         *
         * Also Parzellen PLUS Rand - das ist die Flaeche, der der Parkplatz
         * ausweichen muss. Das gezogene Rechteck ist kleiner; es wird nur
         * gezeichnet.
         */
        internal Punkt[] Ecken() => EckenMitAufschlag(Rand);

        /**
         * Liegt der Punkt darin? Im Rahmen der Flaeche gerechnet, nicht ueber
         * eine Kantenschleife - zwei Skalarprodukte, und keine Frage nach dem
         * Umlaufsinn.
         */
        internal bool Enthaelt(Punkt welt)
        {
            var bogen = Winkel * Math.PI / 180.0;
            var laengs = new Punkt(Math.Cos(bogen), Math.Sin(bogen));
            var quer = new Punkt(-laengs.Y, laengs.X);
            var d = welt - (Ecke - laengs * Rand - quer * Rand);
            var u = d.X * laengs.X + d.Y * laengs.Y;
            var v = d.X * quer.X + d.Y * quer.Y;
            return u >= -1e-6 && u <= Spalten * 8.0 + 2 * Rand + 1e-6
                && v >= -1e-6 && v <= Reihen * 8.0 + 2 * Rand + 1e-6;
        }

        /**
         * Liegt der Punkt in einem um `aufschlag` vergroesserten Rechteck
         * um die Parzellen? Mit 0 ist das genau das gezogene Rechteck.
         */
        internal bool EnthaeltMitAufschlag(Punkt welt, double aufschlag)
        {
            var bogen = Winkel * Math.PI / 180.0;
            var laengs = new Punkt(Math.Cos(bogen), Math.Sin(bogen));
            var quer = new Punkt(-laengs.Y, laengs.X);
            var d = welt - (Ecke - laengs * aufschlag - quer * aufschlag);
            var u = d.X * laengs.X + d.Y * laengs.Y;
            var v = d.X * quer.X + d.Y * quer.Y;
            return u >= -1e-6 && u <= Spalten * 8.0 + 2 * aufschlag + 1e-6
                && v >= -1e-6 && v <= Reihen * 8.0 + 2 * aufschlag + 1e-6;
        }

        /**
         * Der Streifen, auf dem die Zoning-Strasse liegt.
         *
         * Er bekommt ASPHALT, nicht die Zoning-Oberflaeche: der Nutzer hat
         * am 2026-09-01 festgelegt, dass die eigene Flaeche nur fuer den
         * Parzellenboden gilt und die Zoning-Strasse wie jede andere
         * Strasse von uns auf Flaeche 1 liegt. Ohne das saehe man Autos
         * ueber Gras fahren - die Strasse selbst ist ja unsichtbar.
         *
         * Ist der Rand groesser als die Strasse (aussen liegt noch Bauland),
         * gehoert nur dieser Streifen zur Fahrbahn; der Rest bleibt Bauland.
         */
        internal bool ImStrassenkorridor(Punkt welt) =>
            EnthaeltMitAufschlag(welt, 2 * Strassenhalbbreite)
                && !EnthaeltMitAufschlag(welt, 0.0);
    }

    internal sealed class Zelleneinstellungen
    {
        internal double Randabstand { get; set; } = 1.0;
        internal double Fahrgassenbreite { get; set; } = 7.0;
        internal double Buchtbreite { get; set; } = 3.0;
        internal double Buchttiefe { get; set; } = 5.9;
        internal double Gruenstreifenbreite { get; set; } = 2.5;
        internal double Querstrassenbreite { get; set; } = 3.0;
        internal double Querstrassenabstand { get; set; } = 34.0;
        /**
         * Bekommt ein Reihenende eine Kappe?
         *
         * Hiess bis zum 2026-08-24 `Querstrassen` und steuerte, ob es
         * ueberhaupt Querstrassen gibt - obwohl der Schalter im Panel
         * "Kappen an Querstrassen" heisst. Wer die Kappen abschaltete, verlor
         * die Verbindungsstrassen. Nutzerbefund: *"Die Verbindungsstrassen
         * duerfen nicht verschwinden, sondern nur die Kappen."*
         *
         * Wieviele Querstrassen es gibt, entscheidet allein der Abstand
         * (`Querstrassenabstand`, im Panel "Verbindung alle N Buchten").
         */
        internal bool Querstrassenkappen { get; set; } = true;
        internal bool Randstrassen { get; set; } = true;
        internal double? Reihenwinkel { get; set; }
        internal IReadOnlyList<Teilflaechenrahmenvorgabe> Teilflaechen
            { get; set; } = Array.Empty<Teilflaechenrahmenvorgabe>();
        internal IReadOnlyList<Teilflaechennahtvorgabe> Teilflaechennaehte
            { get; set; } = Array.Empty<Teilflaechennahtvorgabe>();

        /**
         * DIE BAULANDFLAECHEN - Teil des PLANS, nicht der Korrektur.
         *
         * Sie werden NICHT hinterher aus dem fertigen Layout gestanzt. Ihre
         * vier Kanten gehen als Rasterlinien in die Zellteilung, bevor
         * irgendeine Zelle eingeordnet wird. Damit liegt keine Zelle halb
         * drinnen und halb draussen, und die Einordnung nach dem
         * Zellschwerpunkt kann sich nicht irren.
         *
         * Genau daran ist am 2026-09-01 dreimal etwas zerbrochen: geplant,
         * nachtraeglich beschnitten, und danach waren sich zwei Ableitungen
         * ueber dieselbe Stelle uneinig.
         */
        internal IReadOnlyList<Zoningvorgabe> Zoningflaechen { get; set; }
            = Array.Empty<Zoningvorgabe>();

        /**
         * Die Randzoning-Abschnitte, im Rahmen des Kerns.
         *
         * Was aussen an der Randstrasse liegt - die Randreihe und das
         * Randgruen -, wird dort zu Bauland. Der Nutzer hat den Grund
         * geliefert: *"Wir haben doch aussen an der Randstrasse Parkbuchten.
         * Wieso sollten wir auf diesen Parkbuchten die Tiles erstellen? Dann
         * haetten wir Parkbuchten, die unter Haeusern stehen."*
         */
        internal IReadOnlyList<(Punkt A, Punkt B)> Randzoning { get; set; }
            = Array.Empty<(Punkt, Punkt)>();
        internal double Cs2Mindestkante { get; set; } = 0.375;
        internal double Zufahrtstiefe => Randabstand + Buchttiefe;
        internal double Randstrassenmittellinientiefe =>
            Zufahrtstiefe + Fahrgassenbreite / 2;
        internal double Randstrasseninnentiefe =>
            Zufahrtstiefe + Fahrgassenbreite;
    }

    internal sealed class Bauergebnis
    {
        internal IReadOnlyList<string> Schnittwarnungen { get; set; } = Array.Empty<string>();
        internal bool Randstrassen { get; set; } = true;
        internal Ringlosplan Ringlos { get; set; }
        internal Formdefinition Form { get; set; }
        internal Rahmen Rahmen { get; set; }
        internal Polygon ArealLokal { get; set; }
        internal List<Polygon> KonvexeTeile { get; set; }
        internal List<Zelle> ZellenVorZufahrt { get; set; }
        internal List<Zelle> Zellen { get; set; }
        internal List<Flaeche> FlaechenVorTrennung { get; set; }
        internal List<Flaeche> Flaechen { get; set; }
        internal Bandplan Bandplan { get; set; }
        internal Spaltenplan Spaltenplan { get; set; }
        internal IReadOnlyList<Modulspaltenplan> Modulspaltenplaene { get; set; }
        internal IReadOnlyList<Querstrassenpruefung> Querstrassenpruefungen { get; set; }
        internal IReadOnlyList<Querstrassenstueck> Querstrassenstuecke { get; set; }
        /**
         * Der gezeichnete Umriss im Rahmen des Kerns.
         *
         * Gebraucht, um einen Ring Kante fuer Kante seinem Umriss zuzuordnen:
         * die Ringe kommen aus `Layoutplanung.Innenrand` und behalten dessen
         * Nummerierung. `Form.Punkte` hilft dafuer nicht - der Umlaufsinn
         * wird beim Bau notfalls gedreht, die Nummern verschieben sich.
         */
        /**
         * Die Randzoning-Abschnitte, wie der Layoutbauer sie geplant hat.
         *
         * Sie sind seit dem 2026-09-10 die EINZIGE Quelle der RZ-Strasse:
         * `ZellenStrassen` baut den Kurs daraus, statt ihn noch einmal aus dem
         * Perimeterring abzuleiten. Vorher gab es zwei Stellen, die
         * unabhaengig voneinander bestimmten, wo die Strasse liegt - sie
         * stimmten nur ueberein, weil beide dieselbe feste Tiefe benutzten.
         */
        internal IReadOnlyList<Randzoningabschnitt> Randzoningabschnitte { get; set; }
            = Array.Empty<Randzoningabschnitt>();
        internal IReadOnlyList<Punkt> Umrisslokal { get; set; }
        internal IReadOnlyList<Punkt> Innenrand { get; set; }
        internal IReadOnlyList<Punkt> Randstrassenrand { get; set; }
        internal IReadOnlyList<Punkt> Randstrassenmittellinie { get; set; }
        internal IReadOnlyList<Punkt> Randstrasseninnenrand { get; set; }
        internal IReadOnlyDictionary<int, Randbuchtplan> Randbuchten { get; set; }
        /** Fertige Innenbucht-Ecken im gemeinsamen lokalen Rahmen. */
        internal IReadOnlyDictionary<int, Punkt[]> Innenbuchten { get; set; }
            = new Dictionary<int, Punkt[]>();
        internal Zufahrtsbericht Zufahrtsbericht { get; set; }
        internal Lochtrennbericht Lochtrennung { get; set; }
        internal Topologiebericht Topologie { get; set; }
        internal IReadOnlyList<Rasterstrassenplan> InnereStrassen { get; set; }
            = Array.Empty<Rasterstrassenplan>();
        internal TimeSpan EinzelneBauzeit { get; set; }

        internal int Buchtenzahl => Zellen
            .Where(zelle => zelle.BuchtId.HasValue)
            .Select(zelle => zelle.BuchtId.Value)
            .Distinct()
            .Count();
    }
}
