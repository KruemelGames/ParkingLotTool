using System;
using System.Linq;
using ParkingLotTool.Geometry;

/**
 * UEBERLEBT JEDE EINSTELLUNG DEN BAUZETTEL?
 *
 * Befund des Nutzers am 2026-09-09: *"Ich habe das Gefuehl dass einiges was
 * ich aendere nicht wirklich uebernommen wird ... zb gestern wurde 'Across'
 * nicht uebernommen."*
 *
 * Der Reihenwinkel-Modus wird als Zahl im Bauzettel abgelegt und beim
 * Oeffnen im Edit zurueckuebersetzt. Faellt dabei ein Modus durchs Raster,
 * merkt das niemand: gebaut wird richtig, zurueck kommt etwas anderes.
 * Genau so war es bei `quer`.
 */
internal static partial class Program
{
    private static int RunWinkelmodus()
    {
        var fehler = 0;
        void Pruefe(bool ok, string meldung)
        {
            if (ok) return;
            fehler++;
            Console.WriteLine("FEHLER: " + meldung);
        }

        Console.WriteLine($"WINKELMODUS: {Winkelmodus.Alle.Length} Modi");

        foreach (var modus in Winkelmodus.Alle)
        {
            var zahl = Winkelmodus.Kodiere(modus);
            var zurueck = Winkelmodus.Dekodiere(zahl);
            Console.WriteLine($"  {modus,-8} -> {zahl} -> {zurueck}");
            Pruefe(zurueck == modus,
                $"'{modus}' kommt als '{zurueck}' zurueck - im Edit steht "
                + "dann etwas anderes als gebaut wurde");
            Pruefe(Winkelmodus.IstGueltig(modus),
                $"'{modus}' steht in Alle, gilt aber nicht als gueltig");
            Pruefe(zahl >= 0 && zahl <= Winkelmodus.GroessteZahl,
                $"'{modus}' bekommt die Zahl {zahl}, der Bauzettel laesst "
                + $"nur 0..{Winkelmodus.GroessteZahl} durch");
        }

        /*
         * KEINE ZWEI MODI AUF DERSELBEN ZAHL.
         *
         * Genau das war der Fehler: `quer` und `edge` teilten sich die 0.
         * Beim Bauen fiel es nicht auf, beim Oeffnen wurde daraus `edge`.
         */
        var zahlen = Winkelmodus.Alle.Select(Winkelmodus.Kodiere).ToArray();
        Pruefe(zahlen.Distinct().Count() == zahlen.Length,
            "zwei Modi teilen sich dieselbe Bauzettel-Zahl: "
            + string.Join(", ", Winkelmodus.Alle.Zip(zahlen,
                (m, z) => $"{m}={z}")));

        /*
         * DIE ALTEN ZAHLEN STEHEN IN GESPEICHERTEN STAEDTEN.
         *
         * 0, 1 und 2 gab es vom ersten Bauzettel an. Wer sie umdeutet,
         * aendert bestehende Parkplaetze beim naechsten Oeffnen.
         */
        Pruefe(Winkelmodus.Dekodiere(0) == "edge", "0 muss 'edge' bleiben");
        Pruefe(Winkelmodus.Dekodiere(1) == "fixed", "1 muss 'fixed' bleiben");
        Pruefe(Winkelmodus.Dekodiere(2) == "auto", "2 muss 'auto' bleiben");

        // Ein unbekannter Wert aus einem neueren Bauzettel darf nicht werfen.
        Pruefe(Winkelmodus.Dekodiere(99) == "edge",
            "unbekannte Zahlen muessen auf 'edge' zurueckfallen");
        Pruefe(!Winkelmodus.IstGueltig("across"),
            "'across' ist der Panel-Text, nicht der Wert - er darf nicht gelten");

        Console.WriteLine($"Winkelmodus: {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }
}
