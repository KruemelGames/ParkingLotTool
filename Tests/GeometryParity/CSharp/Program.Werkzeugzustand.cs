using System;
using System.Linq;
using ParkingLotTool.Geometry;

/**
 * DIE MATRIX IST DIE PRUEFUNG.
 *
 * Befund des Nutzers am 2026-09-09: *"Ich bin im Reiter 'Parkplaetze', kann
 * ich weiter bauen, was sinnlos ist. Waehrend ich gerade Zugaenge baue, dann
 * kann ich die Polygonpunkte verschieben."*
 *
 * Ursache war, dass es gar keinen Zustand gab: die Klickkette verteilte nach
 * ihrer eigenen Reihenfolge und fragte den Reiter nie. Hier steht jede Zelle
 * einzeln - wer eine Regel aendert, aendert sie sichtbar.
 */
internal static partial class Program
{
    private static int RunWerkzeugzustand()
    {
        var fehler = 0;
        void Pruefe(bool ok, string meldung)
        {
            if (ok) return;
            fehler++;
            Console.WriteLine("FEHLER: " + meldung);
        }

        /*
         * DIE TABELLE, ZEILE FUER ZEILE.
         *
         * Spalten in der Reihenfolge der Aufzaehlung `Weltarbeit`:
         *   Umriss Zugang Zoningflaeche Zoningseite Ausrichtwahl
         *   Marke Zurueck Rueckgaengig Bauen Abzug
         *
         * '.' erlaubt, '-' gesperrt.
         */
        var reiterZeilen = new (Werkzeugreiter Reiter, string Muster)[]
        {
            (Werkzeugreiter.Entwurf, "..--.-...."),
            (Werkzeugreiter.Zoning,  "--..---..."),
            (Werkzeugreiter.Liste,   "---------."),
            (Werkzeugreiter.Debug,   "-------.-."),
            (Werkzeugreiter.Melden,  "-----.---."),
        };

        var arbeiten = (Weltarbeit[])Enum.GetValues(typeof(Weltarbeit));
        // Sichtbarkeit gehoert zur selben Matrix wie die Klickfreigabe.
        // Der alte Renderer pruefte nur zwei Zoning-Schalter und zeigte
        // deshalb das Polygonwerkzeug auch in Parking Lots und Debug.
        foreach (Werkzeugreiter reiter in Enum.GetValues(typeof(Werkzeugreiter)))
        {
            Pruefe(Werkzeugzustand.ZeigtWerkzeugvorschau(reiter)
                == (reiter != Werkzeugreiter.Liste), "Vorschau im Reiter " + reiter);
            foreach (Werkzeugmodus modus in Enum.GetValues(typeof(Werkzeugmodus)))
                Pruefe(Werkzeugzustand.ZeigtUmrisshilfe(reiter, modus)
                    == (reiter == Werkzeugreiter.Entwurf && modus == Werkzeugmodus.Grund),
                    "Zeichenhilfe " + reiter + "/" + modus);
        }
        Console.WriteLine($"WERKZEUGZUSTAND: {arbeiten.Length} Arbeiten, "
            + $"{Enum.GetValues(typeof(Werkzeugreiter)).Length} Reiter, "
            + $"{Enum.GetValues(typeof(Werkzeugmodus)).Length} Modi");

        foreach (var zeile in reiterZeilen)
        {
            Pruefe(zeile.Muster.Length == arbeiten.Length,
                $"{zeile.Reiter}: Muster hat {zeile.Muster.Length} Zeichen, "
                + $"es gibt {arbeiten.Length} Arbeiten");
            if (zeile.Muster.Length != arbeiten.Length) continue;
            var ist = string.Concat(arbeiten.Select(a =>
                Werkzeugzustand.ReiterErlaubt(zeile.Reiter, a) ? '.' : '-'));
            Console.WriteLine($"  {zeile.Reiter,-8} {ist}");
            Pruefe(ist == zeile.Muster,
                $"{zeile.Reiter}: {ist} statt {zeile.Muster}");
        }

        /*
         * EIN MODUS BEDIENT SEINE ARBEIT UND SPERRT DEN REST.
         *
         * Ausgenommen sind die drei, die am Werkzeug haengen statt am Modus:
         * Schritt zurueck, Rueckgaengig, Bauen. Und der Abzug, der nichts
         * aendert.
         */
        var werkzeugweit = new[]
        {
            Weltarbeit.SchrittZurueck, Weltarbeit.Rueckgaengig,
            Weltarbeit.Bauen, Weltarbeit.Abzug,
        };
        foreach (Werkzeugmodus modus in Enum.GetValues(typeof(Werkzeugmodus)))
        foreach (var arbeit in arbeiten)
        {
            if (werkzeugweit.Contains(arbeit))
            {
                Pruefe(Werkzeugzustand.ModusErlaubt(modus, arbeit),
                    $"{modus}: {arbeit} haengt am Werkzeug und darf nie am "
                    + "Modus scheitern");
                continue;
            }
            var erwartet = arbeit == Werkzeugzustand.EigeneArbeit(modus);
            Pruefe(Werkzeugzustand.ModusErlaubt(modus, arbeit) == erwartet,
                $"{modus} + {arbeit}: "
                + $"{Werkzeugzustand.ModusErlaubt(modus, arbeit)} statt {erwartet}");
        }

        /*
         * KEIN UMRISS AUSSERHALB DES ENTWURFS.
         *
         * Die Regel, die die drei Einzelentscheidungen des Nutzers ersetzt:
         * *"Nein weil wir nicht in Draft sind."*
         */
        foreach (Werkzeugreiter reiter in Enum.GetValues(typeof(Werkzeugreiter)))
            Pruefe(Werkzeugzustand.ReiterErlaubt(reiter, Weltarbeit.Umriss)
                    == (reiter == Werkzeugreiter.Entwurf),
                $"Umrissarbeit im Reiter {reiter}");

        /*
         * KEIN MODUS UEBERLEBT EINEN REITERWECHSEL.
         *
         * Und der Modus, auf den gewechselt wird, muss zum Reiter passen -
         * sonst ginge der naechste Klick ins Leere.
         */
        foreach (Werkzeugreiter reiter in Enum.GetValues(typeof(Werkzeugreiter)))
        {
            var danach = Werkzeugzustand.ModusBeimReiter(reiter);
            Console.WriteLine($"  Reiterwechsel -> {reiter,-8} setzt Modus {danach}");
            Pruefe(Werkzeugzustand.ModusPasstZuReiter(reiter, danach),
                $"{reiter} landet in Modus {danach}, dessen eigene Arbeit "
                + "der Reiter verbietet");
        }

        // Der Zoning-Reiter bringt sein Werkzeug mit, der Melde-Reiter auch.
        Pruefe(Werkzeugzustand.ModusBeimReiter(Werkzeugreiter.Zoning)
            == Werkzeugmodus.Zoningflaeche, "Zoning startet nicht mit Parzellen");
        Pruefe(Werkzeugzustand.ModusBeimReiter(Werkzeugreiter.Melden)
            == Werkzeugmodus.Meldemarke, "Melden startet nicht mit Marken");

        /*
         * ESCAPE STUFT AB.
         *
         * Aus einem Modus heraus faellt zuerst der Modus, nicht das Werkzeug.
         * Im Grundzustand des Reiters gibt es nichts mehr abzustufen - dann
         * schliesst Escape das Werkzeug.
         */
        foreach (Werkzeugreiter reiter in Enum.GetValues(typeof(Werkzeugreiter)))
        foreach (Werkzeugmodus modus in Enum.GetValues(typeof(Werkzeugmodus)))
        {
            var stuft = Werkzeugzustand.EscapeStuftAb(reiter, modus, out var danach);
            var grund = Werkzeugzustand.ModusBeimReiter(reiter);
            Pruefe(stuft == (modus != grund),
                $"Escape in {reiter}/{modus}: stuft {stuft}, erwartet "
                + $"{modus != grund}");
            Pruefe(danach == grund,
                $"Escape in {reiter}/{modus} landet in {danach} statt {grund}");
        }

        /*
         * IM REITER LISTE IST NICHTS ERLAUBT AUSSER DEM ABZUG.
         *
         * Der Reiter ist eine Anzeige gebauter Parkplaetze. Ein Klick in die
         * Welt zog dort bis zum 2026-09-09 Polygonecken herum.
         */
        foreach (Werkzeugmodus modus in Enum.GetValues(typeof(Werkzeugmodus)))
        foreach (var arbeit in arbeiten)
            Pruefe(Werkzeugzustand.Erlaubt(Werkzeugreiter.Liste, modus, arbeit)
                    == (arbeit == Weltarbeit.Abzug),
                $"Liste/{modus}: {arbeit} ist erlaubt");

        /*
         * ES BLEIBT KATEGORISCH.
         *
         * Ansage des Nutzers am 2026-09-09: *"Das ganze was wir gerade machen
         * soll nur kategorisch sein, nicht auf jede einzelne Funktion."* Und
         * konkret zu den Zugaengen: *"Wenn ich Zufahrt z.B. Einfahrt
         * angeklickt habe, dann will ich weiterhin Ausfahrt loeschen oder
         * verschieben."*
         *
         * Der erste Entwurf hatte fuenf Umrisseintraege - das Raster, in dem
         * eine Funktion vergessen wird. Diese Liste haelt den Schnitt fest:
         * wer sie erweitert, tut es absichtlich.
         */
        var erwarteteKategorien = new[]
        {
            "Umriss", "Zugang", "Zoningflaeche", "Zoningseite", "Ausrichtwahl",
            "Meldemarke", "SchrittZurueck", "Rueckgaengig", "Bauen", "Abzug",
        };
        var kategorien = Enum.GetNames(typeof(Weltarbeit));
        Pruefe(kategorien.SequenceEqual(erwarteteKategorien),
            "Die Kategorien sind: " + string.Join(", ", kategorien)
            + " - erwartet: " + string.Join(", ", erwarteteKategorien));

        // Genau EINE Kategorie fuer alle vier Zugangsarten.
        Pruefe(kategorien.Count(n => n.Contains("Zufahrt") || n.Contains("Fussweg")
                || n.Contains("Einfahrt") || n.Contains("Ausfahrt")) == 0,
            "Eine Zugangsart ist zu einer eigenen Kategorie geworden - die "
            + "gewaehlte Art darf nur entscheiden, was ein NEUER Klick "
            + "erzeugt, nicht was sich verschieben oder loeschen laesst");

        /*
         * IM ZONING ZERLEGT KEIN RECHTSKLICK DEN UMRISS.
         *
         * Befund des Nutzers am 2026-09-09: *"Rechtsklick beim Zoning fuehrt
         * immer noch dazu, dass das Polygon aufgeloest wird."* `StepBack`
         * oeffnet einen geschlossenen, unberuehrten Umriss wieder - das darf
         * ausserhalb des Entwurfs nicht erreichbar sein.
         */
        foreach (Werkzeugreiter reiter in Enum.GetValues(typeof(Werkzeugreiter)))
            Pruefe(Werkzeugzustand.ReiterErlaubt(reiter, Weltarbeit.SchrittZurueck)
                    == (reiter == Werkzeugreiter.Entwurf),
                $"Schritt zurueck im Reiter {reiter} - er zerlegt den Umriss");

        Console.WriteLine($"Werkzeugzustand: {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }
}
