using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry.Zellen;

internal static partial class Program
{
    /**
     * Was die Zufahrt kostet - gemessen, nicht geschaetzt.
     *
     * Befund des Nutzers am 2026-09-08: „die Fahrtgasse wird durch die
     * Einfahrt eingekuerzt weil die Einfahrt bis zur naechsten Querstrasse
     * will. Dadurch fallen Parkplaetze weg."
     *
     * Dieser Lauf stellt denselben Bau zweimal auf: ohne Zufahrt und mit
     * einer Zufahrt an der Kante, die PARALLEL zu den Fahrgassen liegt. Er
     * nennt Buchtenzahl, Gassenlaengen und die Laenge der Zufahrt selbst.
     */
    private static int RunZufahrtsverlust()
    {
        var fehler = 0;
        void Pruefe(bool ok, string meldung)
        {
            if (ok) return;
            fehler++;
            Console.WriteLine("FEHLER: " + meldung);
        }

        // 150 x 120 m, aehnlich dem Grundstueck an der Ulmholzstrasse.
        var form = new Formdefinition("Ulmholz-artig", new List<Punkt>
        {
            new Punkt(0, 0), new Punkt(150, 0),
            new Punkt(150, 120), new Punkt(0, 120),
        });

        var einstellungen = new Zelleneinstellungen
        {
            Randstrassen = false,
            Reihenwinkel = 90,          // Gassen laufen senkrecht zur Strasse
            Gruenstreifenbreite = 2.5,
            Querstrassenkappen = true,
            Querstrassenabstand = (9 + 2) * 3,
        };

        void Zeige(string was, IReadOnlyList<Zufahrtsvorgabe> zufahrten)
        {
            var bau = Layoutbauer.Baue(form, einstellungen, zufahrten);
            var r = bau.Ringlos;
            Console.WriteLine($"  {was,-22} {bau.Buchtenzahl,4} Buchten");
            if (r == null) { Console.WriteLine("      (kein ringloser Plan)"); return; }

            var gassen = r.Gassen
                .Select(g => Geometrie.Laenge(g.B - g.A))
                .OrderByDescending(l => l).ToList();
            Console.WriteLine($"      {gassen.Count} Gassen, Laengen "
                + string.Join(" / ", gassen.Select(l => l.ToString("F1"))));
            foreach (var z in r.Zufahrten)
                Console.WriteLine($"      Zufahrt laeuft "
                    + $"{Geometrie.Laenge(z.B - z.A):F1} m weit hinein, "
                    + $"{z.Breite:F1} m breit");
            var quer = r.Querwege.Count;
            var baender = bau.Bandplan.Baender
                .GroupBy(b => b.Art)
                .Select(g => $"{g.Key} {g.Count()}")
                .ToList();
            Console.WriteLine($"      {quer} Querwege; Baender: "
                + string.Join(", ", baender));
            var reihen = bau.Bandplan.Baender
                .Where(b => b.Art == Zellart.Bucht)
                .Select(b => $"{b.Ende - b.Anfang:F1}")
                .ToList();
            Console.WriteLine($"      Buchtbaender ({reihen.Count}): "
                + string.Join(" / ", reihen));
            foreach (var f in r.Fusswege)
                Console.WriteLine($"      Fussweg  A({f.A.X,7:F1}/{f.A.Y,7:F1})"
                    + $"  B({f.B.X,7:F1}/{f.B.Y,7:F1})"
                    + $"  Laenge {Geometrie.Laenge(f.B - f.A),6:F1}"
                    + $"  Breite {f.Breite:F1}");
            var yMin = r.Gassen.Min(g => Math.Min(g.A.Y, g.B.Y));
            var yMax = r.Gassen.Max(g => Math.Max(g.A.Y, g.B.Y));
            Console.WriteLine($"      Gassenachsen Y {yMin:F1}..{yMax:F1}");

            /*
             * DER ENDFUSSWEG MUSS UEBER DIE RANDGASSE HINAUSREICHEN.
             *
             * Er lief bis zur ACHSE der aeussersten Gasse und hoerte damit
             * mitten in ihr auf - bei 7 m Gassenbreite fehlten je 3,5 m.
             * Befund des Nutzers am 2026-09-08. Sichtbar als abgeschnittener
             * Weg, als Naht an dieser Stelle und als unsauberer Anschluss.
             */
            foreach (var f in r.Fusswege)
            {
                var halbe = r.Gassen.Max(g => g.Breite) / 2;
                var unten = Math.Min(f.A.Y, f.B.Y);
                var oben = Math.Max(f.A.Y, f.B.Y);
                Pruefe(unten <= yMin - halbe + 1e-6,
                    $"{was}: Fussweg endet bei {unten:F1} statt "
                    + $"{yMin - halbe:F1} - mitten in der Randgasse");
                Pruefe(oben >= yMax + halbe - 1e-6,
                    $"{was}: Fussweg endet bei {oben:F1} statt "
                    + $"{yMax + halbe:F1} - mitten in der Randgasse");
            }

            /*
             * UND DIE ZUFAHRT MUSS AM ERSTEN ZIEL HALTEN.
             *
             * Bei 88 Grad lief sie 60,0 m tief, weil sie nur Kreuzungen
             * suchte und die fast parallele Gasse nie kreuzte - sie hielt
             * erst am ZWEITEN Ziel. Das erste lag bei 5,4 m.
             *
             * Hier stand bis zum 2026-09-09 eine feste Schranke von 20 m.
             * Die ist mit der N-Regel unvereinbar geworden: seit die erste
             * Querstrasse den geforderten Abstand zum Gassenende einhaelt,
             * liegt sie 21 m innen, und eine Zufahrt, die sie erreichen
             * SOLL, kann gar nicht unter 20 m bleiben. Eine Zahl, die der
             * eigenen Regel widerspricht, misst nichts mehr.
             *
             * Deshalb wird jetzt die gemeinte Eigenschaft selbst geprueft:
             * die Zufahrt endet am NAECHSTEN Ziel, nicht am uebernaechsten.
             * Die Ziele werden hier unabhaengig nachgerechnet.
             */
            foreach (var z in r.Zufahrten)
            {
                var tiefe = Geometrie.Laenge(z.B - z.A);
                if (tiefe < 1e-6) continue;
                var d = z.B - z.A;
                var richtung = new Punkt(d.X / tiefe, d.Y / tiefe);
                var seitlich = z.Breite / 2 + 1e-6;
                var ziele = r.Gassen.Select(g => (g.A, g.B))
                    .Concat(r.Querwege.Select(q => (q.Anfang, q.Ende)))
                    .ToList();
                var treffer = new List<double>();
                foreach (var ziel in ziele)
                    foreach (var ecke in new[] { ziel.Item1, ziel.Item2 })
                    {
                        var nach = ecke - z.A;
                        var laengs = Geometrie.Skalar(nach, richtung);
                        if (laengs <= 1e-6) continue;
                        if (Math.Abs(Geometrie.Kreuz(richtung, nach)) > seitlich) continue;
                        treffer.Add(laengs);
                    }
                if (treffer.Count == 0) continue;
                var erstes = treffer.Min();
                Pruefe(tiefe <= erstes + 0.5,
                    $"{was}: Zufahrt laeuft {tiefe:F1} m tief, das naechste "
                    + $"Ziel liegt aber schon bei {erstes:F1} m");
            }

            /*
             * DIE N-REGEL GILT AN BEIDEN ENDEN.
             *
             * Befund des Nutzers am 2026-09-08: *"Dort liegt eine Querstrasse
             * direkt neben einem Randstrasse-aus-Fussweg. Anscheinend
             * funktioniert die Regel nur auf einer Seite."* Am Bauzettel
             * gemessen: je Korridor lag genau eine Querstrasse 1,5 m vom
             * Gassenende - die halbe Querstrassenbreite, also am Anschlag -,
             * waehrend 21 m gefordert waren. Es war die erste, immer gesetzte
             * Querstrasse: sie wurde nur auf den Korridor beschnitten, nicht
             * auf die Regel.
             *
             * Kurze Korridore sind ausgenommen: passt die Regel gar nicht
             * hinein, ist eine Verbindung besser als zwei unverbundene
             * Fahrgassen.
             */
            var nRegel = Math.Max(1, (int)Math.Round(
                einstellungen.Querstrassenabstand / einstellungen.Buchtbreite
                - (einstellungen.Querstrassenkappen ? 2 : 0),
                MidpointRounding.AwayFromZero));
            var mindest = (Math.Min(5, nRegel)
                + (einstellungen.Querstrassenkappen ? 2 : 0))
                * einstellungen.Buchtbreite;
            foreach (var q in r.Querwege)
            {
                var yUnten = Math.Min(q.Anfang.Y, q.Ende.Y);
                var yOben = Math.Max(q.Anfang.Y, q.Ende.Y);
                var a = r.Gassen.FirstOrDefault(
                    g => Math.Abs(g.A.Y - yUnten) < 1e-6);
                var b = r.Gassen.FirstOrDefault(
                    g => Math.Abs(g.A.Y - yOben) < 1e-6);
                if (a == null || b == null) continue;
                var links = Math.Max(a.A.X, b.A.X)
                    + einstellungen.Querstrassenbreite / 2;
                var rechts = Math.Min(a.B.X, b.B.X)
                    - einstellungen.Querstrassenbreite / 2;
                if (rechts - links < 2 * mindest) continue;   // zu kurz
                var x = q.Anfang.X;
                Pruefe(x - links >= mindest - 1e-6 && rechts - x >= mindest - 1e-6,
                    $"{was}: Querstrasse bei {x:F1} haelt "
                    + $"{Math.Min(x - links, rechts - x):F1} m Abstand zum "
                    + $"Gassenende, gefordert sind {mindest:F1} m "
                    + $"(Korridor {links:F1}..{rechts:F1})");
            }
            foreach (var w in r.Warnungen) Console.WriteLine("      ! " + w);
        }

        Console.WriteLine("Zufahrtsverlust (150 x 120 m, Gassen senkrecht zur Strasse):");
        Zeige("ohne Zufahrt", Array.Empty<Zufahrtsvorgabe>());
        // Kante 0 laeuft von (0,0) nach (150,0) - die Strassenseite.
        // Die Gassen stehen senkrecht darauf, die Zufahrt also PARALLEL.
        Zeige("Zufahrt Mitte", new[] { new Zufahrtsvorgabe(0, 75) });
        Zeige("Zufahrt bei 20 m", new[] { new Zufahrtsvorgabe(0, 20) });

        /*
         * DER FALL DES NUTZERS: Querstrassen weit auseinander. Ohne den
         * Anschluss an die parallele Fahrgasse muesste die Zufahrt bis zur
         * naechsten Querstrasse laufen und die Gasse dabei verdraengen.
         */
        einstellungen.Querstrassenabstand = 300;
        einstellungen.Reihenwinkel = 88;   // wie im Panel des Nutzers
        Console.WriteLine();
        Console.WriteLine("Querstrassen weit auseinander (Abstand 300 m):");
        Zeige("ohne Zufahrt", Array.Empty<Zufahrtsvorgabe>());
        Zeige("Zufahrt Mitte", new[] { new Zufahrtsvorgabe(0, 75) });

        Console.WriteLine($"Zufahrtsverlust: {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }
}
