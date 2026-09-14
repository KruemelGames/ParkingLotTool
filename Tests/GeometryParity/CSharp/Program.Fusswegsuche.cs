using System;
using ParkingLotTool.Geometry;

internal static partial class Program
{
    private static void PruefeFusswegsuche(Action<bool, string> pruefe)
    {
        var faelle = 0;
        var autoAnschluesse = 0;
        var fussAnschluesse = 0;
        foreach (var breite in new[] { 8f, 16f, 32f, 64f })
        foreach (var weg in new[] { 2f, 3f, 4f, 6f, 7f })
        foreach (var besitzer in new[] { false, true })
        foreach (var grad in new[] { 1, 2, 3 })
        foreach (var art in new[] { Zufahrtsart.Fussweg, Zufahrtsart.Zufahrt, Zufahrtsart.Einfahrt, Zufahrtsart.Ausfahrt })
        for (var schritt = 0; schritt <= 240; schritt++)
        {
            var abstand = schritt * .25f;
            // Unabhaengige Bedingungen aus GenerateEdgesSystem: beide
            // Ebenenmasken, RequireDeadend, Breitenradius, 4 m Suche und 8 m
            // Besitzerzugabe. Eine Suchweite von 0 sperrt den Breitenradius NICHT.
            const uint strassenEbene = 1u;
            const uint pathwayEbene = 2u;
            bool Verbindet(uint suche) => (suche & strassenEbene) != 0
                && (pathwayEbene & pathwayEbene) != 0 && grad == 1
                && abstand - (besitzer ? 8f : 0f) <= breite / 2 + weg / 2 + 4;
            var original = Verbindet(strassenEbene);
            var ist = Verbindet(FusswegAnschluss.Suchmaske(art, strassenEbene));
            if (art == Zufahrtsart.Fussweg)
            {
                if (ist) fussAnschluesse++;
                pruefe(!ist, $"Fusswegsuche: Verbindung bei Abstand {abstand}, Strasse {breite}, Weg {weg}, Besitzer {besitzer}, Grad {grad}");
            }
            else
            {
                if (ist) autoAnschluesse++;
                pruefe(ist == original, $"Autoanschluss veraendert: {art}, Abstand {abstand}");
            }
            faelle++;
        }
        pruefe(autoAnschluesse > 0, "Suchpruefung hat keinen gueltigen Autoanschluss gefunden");
        Console.WriteLine($"Fusswegsuche: {faelle} Kombinationen, {fussAnschluesse} Fussverlaengerungen, {autoAnschluesse} Autoanschluesse");
    }
}
