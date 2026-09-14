using System;
using System.Collections.Generic;
using System.Diagnostics;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    private static readonly (int X, int Y)[] ZoningRichtungsmatrix =
    {
        (-1, -1), (0, -1), (1, -1),
        (-1,  0),           (1,  0),
        (-1,  1), (0,  1), (1,  1),
    };

    /**
     * MATRIX STATT DES BISHERIGEN 6x6-/7x2-EINZELFALLS.
     *
     * Jede Kombination kennt vor dem Lauf ein erlaubtes Rastziel. Das ist
     * entscheidend: nur "nichts Falsches getan" haette den Livefehler nicht
     * gefunden, weil dort gerade GAR NICHTS geschah. Der Test verlangt daher
     * zusaetzlich, dass dieses existierende Ziel in Fangreichweite liegt und
     * der Kern irgendein gueltiges Raster- oder Kontaktziel findet.
     *
     * Die drei Bereichstypen sind der bereits um 1 m Randgruen erodierte
     * Bewegungsraum der ZF-Ecke. Das ist exakt die Information, die auch
     * `ZoningZielErlaubt` dem Kern liefert, nur ohne CS2-Abhaengigkeiten.
     */
    private static int RunZoningrasten()
    {
        var uhr = Stopwatch.StartNew();
        var fehler = 0;
        var faelle = 0;
        var gefunden = 0;
        var freierWunsch = 0;
        var begrenzterWunsch = 0;
        var pruefungen = 0L;
        var maximalePruefungen = 0;
        var nachgewieseneZiele = 0;
        var stabileFolgebilder = 0;
        var meldungen = 0;

        foreach (var spalten in Zahlen1Bis25())
        foreach (var reihen in Zahlen1Bis25())
        foreach (var winkel in new[] { 0.0, 17.0, 45.0, 88.0, 178.0 })
        foreach (var form in new[] { "rechteckig", "schraeg", "konkav" })
        foreach (var richtung in ZoningRichtungsmatrix)
        foreach (var lage in new[] { "frei", "Rand", "Ecke", "gedrueckt" })
        {
            faelle++;
            var basis = new float2(1200f, -700f);
            var ziel = Flaeche(basis, spalten, reihen, winkel);
            var nachbarSpalten = 1 + (spalten * 7 + reihen * 3) % 25;
            var nachbarReihen = 1 + (spalten * 5 + reihen * 11) % 25;
            var nachbar = NachbarAn(ziel, nachbarSpalten, nachbarReihen,
                richtung.X, richtung.Y);
            var zielEcken = ParkingGeometry.ZoningEcken(ziel);
            var nachbarEcken = ParkingGeometry.ZoningEcken(nachbar);
            var weg = ParkingGeometry.ZoningMitte(ziel)
                - ParkingGeometry.ZoningMitte(nachbar);
            var wegVomNachbarn = math.normalize(weg);
            var (laengs, quer) = ParkingGeometry.ZoningRichtungen(winkel);
            var formDrehung = form == "schraeg" ? 17f
                : form == "konkav" ? -13f : 0f;
            var aussen = Drehe(wegVomNachbarn, formDrehung);
            var aussen2 = Drehe(aussen, 30f);
            var aktuelleEcke = ziel.Ecke;
            var mausEcke = ziel.Ecke;

            if (lage == "frei")
            {
                aktuelleEcke += wegVomNachbarn * 3f;
                mausEcke += wegVomNachbarn * 0.7f + quer * 1.3f;
            }
            else if (lage == "gedrueckt")
            {
                aktuelleEcke += wegVomNachbarn * 3f;
                mausEcke -= wegVomNachbarn * 12f;
            }
            else if (lage == "Rand")
                mausEcke += aussen * 9.51f;
            else
            {
                var aussen1 = Drehe(aussen, -30f);
                aussen2 = Drehe(aussen, 30f);
                mausEcke += math.normalize(aussen1 + aussen2) * 9.51f;
                aussen = aussen1;
            }

            var aktuell = ziel.Clone();
            aktuell.Ecke = aktuelleEcke;
            var hindernisA = VerschobenesRechteck(ziel, laengs * 1400f, 2, 2);
            var hindernisB = VerschobenesRechteck(ziel, quer * 1500f, 3, 1);

            bool ImBewegungsraum(ParkingGeometry.Zoningflaeche kandidat)
            {
                var p = kandidat.Ecke - ziel.Ecke;
                if (math.abs(math.dot(p, laengs)) > 512f
                    || math.abs(math.dot(p, quer)) > 512f) return false;
                if ((lage == "Rand" || lage == "Ecke")
                    && math.dot(p, aussen) > 1e-3f) return false;
                if (lage == "Ecke" && math.dot(p, aussen2) > 1e-3f)
                    return false;
                if (form == "konkav")
                {
                    var x = math.dot(p, laengs);
                    var y = math.dot(p, quer);
                    // Eine 48x32-m-Kerbe im sonst rechteckigen Raum. Das
                    // Ziel liegt am Kerbenrand, nicht in einem fernen Alibi-L.
                    if (x > 16f && x < 64f && y > 16f && y < 48f)
                        return false;
                }
                var ecken = ParkingGeometry.ZoningEcken(kandidat);
                return !ParkingGeometry.ZoningRechteckeUeberlappen(
                        ecken, nachbarEcken)
                    && !ParkingGeometry.ZoningRechteckeUeberlappen(
                        ecken, ParkingGeometry.ZoningEcken(hindernisA))
                    && !ParkingGeometry.ZoningRechteckeUeberlappen(
                        ecken, ParkingGeometry.ZoningEcken(hindernisB));
            }

            var achsen = new List<ParkingGeometry.ZoningBewegungsachse>
            {
                new ParkingGeometry.ZoningBewegungsachse(laengs, "ZF"),
                new ParkingGeometry.ZoningBewegungsachse(
                    new float2(-aussen.y, aussen.x), form + " Rand"),
            };
            if (lage == "Ecke")
                achsen.Add(new ParkingGeometry.ZoningBewegungsachse(
                    new float2(-aussen2.y, aussen2.x), form + " Ecke"));

            var wunsch = mausEcke - aktuelleEcke;
            var begrenzung = ParkingGeometry.ZoningVersatzBegrenzen(
                aktuell, wunsch, ImBewegungsraum, () => achsen);
            var fallPruefungen = begrenzung.Pruefungen;
            if (math.distancesq(begrenzung.Versatz, wunsch) < 1e-8f)
                freierWunsch++;
            else begrenzterWunsch++;

            var zielVomStart = ziel.Ecke - aktuelleEcke;
            var zielInReichweite = math.distance(
                begrenzung.Versatz, zielVomStart)
                <= (float)ParkingGeometry.Zoningparzelle
                    * math.sqrt(0.5f) + 0.01f;
            var ankerFlaeche = ParkingGeometry.ZoningVerschoben(
                aktuell, begrenzung.Versatz);
            // Bei diagonalem Druck darf die Begrenzung an einer Seitennaht
            // entlanggleiten. Dann ist nicht mehr der vorbereitete Eckkontakt,
            // sondern bereits der erreichbare Anker selbst ein gueltiges
            // Abstandsziel. Das ist ein zweiter, unabhaengiger Existenzbeleg.
            var ankerIstZiel = ImBewegungsraum(ankerFlaeche)
                && Rasterabstand(ankerFlaeche, nachbar);
            if ((ImBewegungsraum(ziel) && zielInReichweite) || ankerIstZiel)
                nachgewieseneZiele++;
            else
            {
                Fehler("Testaufbau hat kein gueltiges Ziel in Reichweite");
                continue;
            }

            var rastung = ParkingGeometry.ZoningAnNachbarnRasten(
                aktuell, wunsch, begrenzung.Versatz,
                new[] { nachbar }, ImBewegungsraum);
            fallPruefungen += rastung.Pruefungen;
            pruefungen += fallPruefungen;
            maximalePruefungen = Math.Max(maximalePruefungen, fallPruefungen);
            if (!rastung.Gefunden)
            {
                Fehler("existierendes Rastziel nicht gefunden");
                continue;
            }
            gefunden++;
            var ergebnisFlaeche = ParkingGeometry.ZoningVerschoben(
                aktuell, rastung.Versatz);
            var ergebnisEcken = ParkingGeometry.ZoningEcken(ergebnisFlaeche);
            if (!ImBewegungsraum(ergebnisFlaeche))
                Fehler("verbotenes Ziel angenommen");
            if (ParkingGeometry.ZoningRechteckeUeberlappen(
                    ergebnisEcken, nachbarEcken))
                Fehler("ZF-Ueberlappung");
            if (!Rasterabstand(ergebnisFlaeche, nachbar))
                Fehler("Abstand weder 0 noch Vielfaches von 8 m");

            // Derselbe absolute Mauspunkt ueber mehrere Bilder: das prueft
            // genau die befuerchtete 3,0/5,1/3,0-m-Rueckkopplung.
            var fest = ergebnisFlaeche;
            var festesZiel = fest.Ecke;
            for (var bild = 0; bild < 3; bild++)
            {
                var bildWunsch = mausEcke - fest.Ecke;
                var bildBegrenzung = ParkingGeometry.ZoningVersatzBegrenzen(
                    fest, bildWunsch, ImBewegungsraum, () => achsen);
                var bildRastung = ParkingGeometry.ZoningAnNachbarnRasten(
                    fest, bildWunsch, bildBegrenzung.Versatz,
                    new[] { nachbar }, ImBewegungsraum, festesZiel);
                fest = ParkingGeometry.ZoningVerschoben(fest,
                    bildRastung.Versatz);
                if (math.distance(fest.Ecke, festesZiel) > 0.002f)
                {
                    Fehler("Rastziel schwingt bei fester Maus");
                    break;
                }
                stabileFolgebilder++;
            }

            void Fehler(string grund)
            {
                fehler++;
                if (meldungen++ >= 20) return;
                Console.Error.WriteLine("  " + grund + ": "
                    + $"{spalten}x{reihen}, {winkel:F0} Grad, {form}, "
                    + $"Nachbar {richtung.X}/{richtung.Y}, {lage}");
            }
        }

        uhr.Stop();
        Console.WriteLine("Zoningrasten-Matrix: " + faelle + " Faelle, "
            + nachgewieseneZiele + " mit vorab nachgewiesenem Ziel, "
            + gefunden + " gefunden, " + fehler + " Fehler.");
        Console.WriteLine("  Mauswunsch frei/begrenzt: " + freierWunsch + "/"
            + begrenzterWunsch + " | Zulassungspruefungen Mittel "
            + (faelle == 0 ? 0 : (double)pruefungen / faelle).ToString("F1")
            + ", Maximum " + maximalePruefungen + " | "
            + uhr.Elapsed.TotalSeconds.ToString("F2") + " s");
        Console.WriteLine("  Feste-Maus-Folgebilder ohne Schwingen: "
            + stabileFolgebilder + "/" + (faelle * 3));
        return fehler == 0 ? 0 : 1;
    }

    private static IEnumerable<int> Zahlen1Bis25()
    {
        for (var i = 1; i <= 25; i++) yield return i;
    }

    private static ParkingGeometry.Zoningflaeche Flaeche(
        float2 ecke, int spalten, int reihen, double winkel) => new()
        {
            Ecke = ecke,
            Spalten = spalten,
            Reihen = reihen,
            Winkel = winkel,
            Rand = 8,
        };

    private static ParkingGeometry.Zoningflaeche NachbarAn(
        ParkingGeometry.Zoningflaeche ziel, int spalten, int reihen,
        int dx, int dy)
    {
        var (u, v) = ParkingGeometry.ZoningRichtungen(ziel.Winkel);
        var zielBreite = (float)(ziel.Spalten * ParkingGeometry.Zoningparzelle);
        var zielTiefe = (float)(ziel.Reihen * ParkingGeometry.Zoningparzelle);
        var breite = (float)(spalten * ParkingGeometry.Zoningparzelle);
        var tiefe = (float)(reihen * ParkingGeometry.Zoningparzelle);
        var x = dx < 0 ? -breite : dx > 0 ? zielBreite
            : (zielBreite - breite) * 0.5f;
        var y = dy < 0 ? -tiefe : dy > 0 ? zielTiefe
            : (zielTiefe - tiefe) * 0.5f;
        return Flaeche(ziel.Ecke + u * x + v * y,
            spalten, reihen, ziel.Winkel);
    }

    private static ParkingGeometry.Zoningflaeche VerschobenesRechteck(
        ParkingGeometry.Zoningflaeche bezug, float2 versatz,
        int spalten, int reihen) => Flaeche(
            bezug.Ecke + versatz, spalten, reihen, bezug.Winkel);

    private static float2 Drehe(float2 v, float grad)
    {
        var bogen = grad * Math.PI / 180.0;
        var c = (float)Math.Cos(bogen);
        var s = (float)Math.Sin(bogen);
        return new float2(c * v.x - s * v.y, s * v.x + c * v.y);
    }

    private static bool Rasterabstand(
        ParkingGeometry.Zoningflaeche a,
        ParkingGeometry.Zoningflaeche b)
    {
        var (u, v) = ParkingGeometry.ZoningRichtungen(a.Winkel);
        var aEcken = ParkingGeometry.ZoningEcken(a);
        var bEcken = ParkingGeometry.ZoningEcken(b);
        static (float Min, float Max) Bereich(float2[] ecken, float2 achse)
        {
            var min = float.MaxValue;
            var max = float.MinValue;
            foreach (var ecke in ecken)
            {
                var wert = math.dot(ecke, achse);
                min = math.min(min, wert);
                max = math.max(max, wert);
            }
            return (min, max);
        }
        static float Luecke((float Min, float Max) x,
            (float Min, float Max) y) => math.max(0f,
                math.max(y.Min - x.Max, x.Min - y.Max));
        static bool Raster(float luecke)
        {
            if (luecke < 0.002f) return true;
            var rest = luecke / (float)ParkingGeometry.Zoningparzelle;
            return math.abs(rest - math.round(rest)) < 2e-4f;
        }
        return Raster(Luecke(Bereich(aEcken, u), Bereich(bEcken, u)))
            && Raster(Luecke(Bereich(aEcken, v), Bereich(bEcken, v)));
    }
}
