using System;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

/**
 * ZONT DAS RANDZONING WIRKLICH NUR NACH AUSSEN?
 *
 * Befund des Nutzers am 2026-09-09: *"Bei einer L-Form ging eine Seite beim
 * Rand-Toggle nach innen."*
 *
 * Randzoning hat keine waehlbare Seite - es zont nach aussen, weil nach
 * innen der Parkplatz liegt. Eine Strasse auf der Innenseite ist deshalb
 * kein Schoenheitsfehler, sondern eine falsche Zuordnung.
 */
internal static partial class Program
{
    private static int RunRandzoningseite()
    {
        var fehler = 0;
        void Pruefe(bool ok, string meldung)
        {
            if (ok) return;
            fehler++;
            Console.WriteLine("FEHLER: " + meldung);
        }

        /*
         * EINE L-FORM MIT SCHMALEM ARM.
         *
         * Der untere Arm ist 24 m breit. Die Randstrasse liegt 10,4 m
         * innerhalb des Umrisses; die Innenkante der GEGENUEBERLIEGENDEN
         * Armseite liegt damit 13,6 m von der unteren Umrisslinie entfernt -
         * innerhalb des 15-m-Suchguertels von `RandzoningEnthaelt`. Und weil
         * dort der BETRAG des Kreuzprodukts geprueft wird, gilt sie auch noch
         * als parallel, obwohl sie genau andersherum laeuft.
         */
        var umriss = new[]
        {
            new float2(0, 0), new float2(60, 0), new float2(60, 24),
            new float2(24, 24), new float2(24, 60), new float2(0, 60),
        };
        const double tiefe = 10.4;

        // Randzoning an der unteren Aussenkante.
        var linien = new[]
        {
            new ParkingGeometry.RandzoningLinie
            {
                A = new float2(0, 0), B = new float2(60, 0),
            },
        };

        var strassen = ParkingGeometry.RandzoningStrassen(linien, umriss, tiefe);
        Console.WriteLine($"  L-Form, Arm 24 m breit, Randzoning an y=0:");
        if (strassen == null)
        {
            Pruefe(false, "keine Randzoning-Straße geplant");
            Console.WriteLine($"Randzoningseite: {fehler} Fehler");
            return 1;
        }
        foreach (var (a, b) in strassen)
            Console.WriteLine($"    Achse ({a.x,6:F1}/{a.y,6:F1})"
                + $" .. ({b.x,6:F1}/{b.y,6:F1})");

        /*
         * ES DARF GENAU EINE SEIN, UND SIE MUSS AUF DER GEWAEHLTEN LIEGEN.
         *
         * Die richtige Achse laeuft bei y = 10,4 - also `tiefe` innerhalb
         * der gewaehlten Linie. Alles andere gehoert zu einer anderen Kante.
         */
        Pruefe(strassen.Count == 1,
            $"{strassen.Count} Randzoning-Straßen statt einer - eine davon "
            + "gehört zur gegenüberliegenden Armseite");
        foreach (var (a, b) in strassen)
        {
            var abstand = (a.y + b.y) / 2;
            Pruefe(Math.Abs(abstand - tiefe) < 0.5,
                $"Achse liegt {abstand:F1} m von der gewählten Linie entfernt, "
                + $"erwartet {tiefe:F1} m - sie zont nach innen");
        }

        /*
         * UND DIE RICHTUNG STIMMT.
         *
         * Die Achse laeuft parallel zu ihrer Umrisslinie, nicht dagegen.
         * Waere sie antiparallel, gehoerte sie zur anderen Seite.
         */
        foreach (var (a, b) in strassen)
        {
            var richtung = math.normalize(b - a);
            var soll = math.normalize(linien[0].B - linien[0].A);
            Pruefe(math.dot(richtung, soll) > 0.9f,
                $"Achse läuft entgegengesetzt zur gewählten Linie "
                + $"(Skalar {math.dot(richtung, soll):F2})");
        }

        /*
         * ZUM VERGLEICH: DAS RECHTECK MUSS UNVERAENDERT STIMMEN.
         *
         * Dort liegt die gegenueberliegende Seite weit genug weg; wer den
         * Fehler behebt, darf diesen Fall nicht mitnehmen.
         */
        var rechteck = new[]
        {
            new float2(0, 0), new float2(60, 0),
            new float2(60, 80), new float2(0, 80),
        };
        var rStrassen = ParkingGeometry.RandzoningStrassen(linien, rechteck, tiefe);
        Console.WriteLine($"  Rechteck 60 x 80, Randzoning an y=0: "
            + $"{(rStrassen?.Count ?? 0)} Achse(n)");
        Pruefe(rStrassen != null && rStrassen.Count == 1,
            $"Rechteck: {(rStrassen?.Count ?? 0)} Straßen statt einer");

        /*
         * UND DER BAU MUSS DIESELBE ZUORDNUNG TREFFEN.
         *
         * Vorschau und Bau gehen zwei verschiedene Wege zur selben Frage -
         * `RandzoningStrassen` fuer das, was der Nutzer sieht,
         * `ZellenStrassen` fuer das, was entsteht. Beide ordnen jetzt ueber
         * die Kantennummer zu statt ueber einen Abstandsguertel.
         *
         * DIE FORM IST EINE ANDERE, UND DAS IST SELBST EIN BEFUND. Die
         * 24-m-Form oben laesst sich gar nicht bauen: der Bau versetzt den
         * Umriss um 13,9 m nach innen, ein 24 m breiter Arm klappt dabei
         * zusammen ("The boundary inset by 13,9 m is not counter-clockwise").
         * Ein Arm muss also ueber 27,8 m breit sein - dann liegt die
         * gegenueberliegende Armachse aber schon 17,4 m entfernt und faellt
         * aus dem 15-m-Guertel heraus. Der ALTE Fehler war im Bau damit
         * unerreichbar; die Vorschau versetzt nur 10,4 m und sieht die Form.
         *
         * Gemessen wird hier deshalb das andere Risiko: dass die neue
         * Zuordnung ueberhaupt trifft. Faellt sie aus, entsteht gar keine
         * Zoning-Strasse mehr - und die Randstrasse bliebe unter dem
         * Bauland liegen.
         */
        var bauUmriss = new[]
        {
            new float2(0, 0), new float2(90, 0), new float2(90, 36),
            new float2(36, 36), new float2(36, 90), new float2(0, 90),
        };
        var bauLinien = new[]
        {
            new ParkingGeometry.RandzoningLinie
            {
                A = new float2(0, 0), B = new float2(90, 0),
            },
        };
        var einst = LayoutSettings.Cs2;
        einst.Zellen = true;
        einst.AngleMode = "edge";
        einst.Auto = false;
        einst.AutomaticEntrances = false;
        einst.Entrances = new[] { new Entrance { Edge = 0, Along = 45 } };
        einst.Randzoning = bauLinien;

        var bau = ParkingGeometry.Build(bauUmriss, einst);

        // Der Index der naechsten Umrisskante zu einem Punkt.
        int NaechsteKante(float2 punkt)
        {
            var beste = -1;
            var bester = double.PositiveInfinity;
            for (var k = 0; k < bauUmriss.Length; k++)
            {
                var ka = bauUmriss[k];
                var kb = bauUmriss[(k + 1) % bauUmriss.Length];
                var d = kb - ka;
                var l2 = math.lengthsq(d);
                var t = l2 < 1e-9f ? 0f
                    : math.clamp(math.dot(punkt - ka, d) / l2, 0f, 1f);
                var abstand = math.distance(punkt, ka + d * t);
                if (abstand >= bester) continue;
                bester = abstand;
                beste = k;
            }
            return beste;
        }

        var zoningRichtig = 0.0;
        var randAufGewaehlter = 0.0;
        var randAmGegenarm = 0.0;
        foreach (var n in bau.NetLine)
        {
            var mitte = (n.A + n.B) * 0.5f;
            var kante = NaechsteKante(mitte);
            var laenge = math.distance(n.A, n.B);
            if (string.Equals(n.Kind, "zoning", StringComparison.Ordinal))
            {
                if (kante == 0) zoningRichtig += laenge;
                else Pruefe(false,
                    $"Zoning-Straße an Umrisskante {kante} statt 0: "
                    + $"({n.A.x:F1}/{n.A.y:F1})..({n.B.x:F1}/{n.B.y:F1})");
            }
            else if (string.Equals(n.Kind, "perimeter", StringComparison.Ordinal))
            {
                if (kante == 0) randAufGewaehlter += laenge;
                else if (kante == 2) randAmGegenarm += laenge;
            }
        }

        Console.WriteLine($"  L-Form 90 x 90, Arm 36 m, gebaut: "
            + $"{zoningRichtig:F1} m Zoning-Straße an der gewählten Kante, "
            + $"{randAufGewaehlter:F1} m Randstraße dort, "
            + $"{randAmGegenarm:F1} m Randstraße am Gegenarm");

        /*
         * Ansage des Nutzers: *"Die Randstrasse in dem Bereich wird ERSETZT
         * durch eine Zoning-Strasse."* Also beides messen - was entstanden
         * ist und was dort nicht mehr liegen darf.
         */
        Pruefe(zoningRichtig > 40,
            $"nur {zoningRichtig:F1} m Zoning-Straße an der gewählten Kante - "
            + "die Zuordnung über die Kantennummer trifft nicht");
        Pruefe(randAufGewaehlter < 1,
            $"{randAufGewaehlter:F1} m Randstraße im Randzoning-Abschnitt - "
            + "sie sollte dort ersetzt sein");
        Pruefe(randAmGegenarm > 20,
            $"nur {randAmGegenarm:F1} m Randstraße am gegenüberliegenden Arm");

        /*
         * EIN PUNKT AUF DER RANDZONING-KANTE DARF DIE AUSWAHL NICHT KOSTEN.
         *
         * Befund von Astra am 2026-09-09, gemessen an der Nutzerform: setzt
         * man einen Punkt mitten auf eine Kante mit Randzoning, bleiben von
         * 178,912277 m gebauter Zoning-Straße **0 m**. Die gemerkte Linie ist
         * mit keiner der beiden neuen Kanten mehr identisch.
         *
         * Beide Hälften liegen aber weiter AUF ihr. Gemessen wird deshalb die
         * Gesamtlänge: sie muss dieselbe bleiben.
         */
        var geteilt = new[]
        {
            new float2(0, 0), new float2(38, 0), new float2(90, 0),
            new float2(90, 36), new float2(36, 36), new float2(36, 90),
            new float2(0, 90),
        };
        var einstGeteilt = einst;
        einstGeteilt.Randzoning = bauLinien;   // unverändert: (0,0)..(90,0)
        einstGeteilt.Entrances = new[]
        {
            new Entrance { Edge = 0, Along = 30 },
        };

        double ZoningLaenge(float2[] form, LayoutSettings s)
            => (ParkingGeometry.Build(form, s).NetLine
                    ?? Array.Empty<NetSegment>())
                .Where(n => string.Equals(n.Kind, "zoning",
                    StringComparison.Ordinal))
                .Sum(n => (double)math.distance(n.A, n.B));

        var einstGanz = einst;
        einstGanz.Entrances = new[] { new Entrance { Edge = 0, Along = 30 } };
        var ganzeLaenge = ZoningLaenge(bauUmriss, einstGanz);
        var geteilteLaenge = ZoningLaenge(geteilt, einstGeteilt);

        Console.WriteLine($"  Punkt auf der Randzoning-Kante: "
            + $"{ganzeLaenge:F1} m ganz, {geteilteLaenge:F1} m geteilt");
        Pruefe(Math.Abs(ganzeLaenge - geteilteLaenge) < 1.0,
            $"die geteilte Kante trägt nur {geteilteLaenge:F1} m statt "
            + $"{ganzeLaenge:F1} m Zoning-Straße - der Punkt kostet die "
            + "Auswahl");

        Console.WriteLine($"Randzoningseite: {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }
}
