using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;

/**
 * `--klonnamen`: stimmen die Namen unserer Flaechenklone?
 *
 * Die Namen stehen im Spielstand. Zwei Klone mit demselben Namen hiessen:
 * beim Laden kommt irgendeiner zurueck (so war es bis 2026-09-24 mit
 * Vorflaeche -90 und Zoningbelag -94). Ein Name, der sich nicht
 * zurueckrechnen laesst, hiesse: die Reparatur beim Laden weiss nicht,
 * welchen Klon sie anlegen soll.
 *
 * Geprueft wird deshalb je Vorbild: alle Varianten verschieden, jeder Name
 * zurueck zu genau Vorbild, Aufschlag und Raeumwirkung - und die alten
 * Namen aus bestehenden Spielstaenden bleiben lesbar.
 */
internal static partial class Program
{
    private static int PruefeKlonnamen()
    {
        var vorbilder = new[]
        {
            "Pavement Surface 01", "Grass Surface 01", "Concrete Surface 02",
            // Echte Mod-Namen vom Nutzer, 2026-09-24: lang, mit Leerzeichen.
            "G87 Vanilla Asphalt Pavement G87 VA Surface ORM Surface",
            "Urban Decay Pack 2 Rock RZZirrah Urban Decay Surface 5 Surface",
            "TRL Grass Dark Surface",
            // Absichtlich boese: Komma und Klammern im Vorbild.
            "Odd (Surface), Test",
        };

        var fehler = 0;
        var namen = 0;
        foreach (var vorbild in vorbilder)
        {
            var gesehen = new Dictionary<string, string>();
            var varianten = new List<(int, bool)> { (0, false) };
            varianten.AddRange(Flaechenklonname.Bauvarianten);
            foreach (var (aufschlag, raeumt) in varianten)
            {
                namen++;
                var name = Flaechenklonname.Name(vorbild, aufschlag, raeumt);
                var was = aufschlag + (raeumt ? "|R" : "");
                if (gesehen.TryGetValue(name, out var vorher))
                {
                    fehler++;
                    Console.WriteLine($"DOPPELT: '{name}' fuer {vorher} und {was}");
                }
                gesehen[name] = was;

                if (!Flaechenklonname.TryLese(name, out var v, out var a, out var r)
                    || v != vorbild || a != aufschlag || r != raeumt)
                {
                    fehler++;
                    Console.WriteLine($"RUECKWEG FALSCH: '{name}' -> '{v}', {a}, {r}");
                }
                if (!Flaechenklonname.IstEigener(name))
                {
                    fehler++;
                    Console.WriteLine($"NICHT ALS EIGENER ERKANNT: '{name}'");
                }
            }
        }

        // Namen, auf die bestehende Spielstaende verweisen. Sie muessen
        // lesbar bleiben, sonst kann die Reparatur alte Parkplaetze nicht
        // retten. Gemessen in optionen.coc und im Log vom 2026-09-24.
        var alt = new (string Name, string Vorbild, int A, bool R)[]
        {
            ("PLT Vorflaeche (Pavement Surface 01)", "Pavement Surface 01", 0, false),
            ("PLT Zoningbelag (Pavement Surface 01)", "Pavement Surface 01", -94, false),
            ("PLT Zoningbelag (Grass Surface 01)", "Grass Surface 01", -94, false),
            ("PLT Raeumbelag (Grass Surface 01, -94)", "Grass Surface 01", -94, true),
            ("PLT Raeumbelag (Pavement Surface 01, -90)", "Pavement Surface 01", -90, true),
        };
        foreach (var f in alt)
        {
            namen++;
            if (!Flaechenklonname.TryLese(f.Name, out var v, out var a, out var r)
                || v != f.Vorbild || a != f.A || r != f.R)
            {
                fehler++;
                Console.WriteLine($"ALTER NAME NICHT LESBAR: '{f.Name}' -> '{v}', {a}, {r}");
            }
        }

        // Fremde Namen duerfen nicht als unsere gelten.
        foreach (var fremd in new[] { "Grass Surface 01", "Missing Area", "PLTX" })
            if (Flaechenklonname.IstEigener(fremd)
                || Flaechenklonname.TryLese(fremd, out _, out _, out _))
            {
                fehler++;
                Console.WriteLine($"FREMD ALS EIGEN GELESEN: '{fremd}'");
            }

        Console.WriteLine($"Klonnamen: {vorbilder.Length} Vorbilder, {namen} Namen, "
            + $"{Flaechenklonname.Bauvarianten.Length} Bauvarianten, {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }
}
