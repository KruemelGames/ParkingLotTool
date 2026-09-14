using System;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

/**
 * LASSEN SICH FUSSWEGE NEBEN EINEM ENDFUSSWEG SETZEN?
 *
 * Befund des Nutzers am 2026-09-09: *"Fusswege (zum manuell setzen) an
 * Fahrtgassenfusswegen wurden auch in der Preview nicht richtig angezeigt und
 * dadurch auch nicht gebaut."*
 *
 * Der Bauzettel 2026-09-09 01:10:48 zeigt dazu zwei Meldungen -
 * "Zufahrt 2 trifft ein Hindernis" und "Zufahrt 3 hat keinen geraden
 * Anschluss". Form, Regler und alle fuenf Zugaenge stammen von dort.
 */
internal static partial class Program
{
    private static int RunFusswegzugang()
    {
        var form = new[]
        {
            new float2(-1037.0201416015625f, 118.68981170654297f),
            new float2(-1135.4090576171875f, 122.06500244140625f),
            new float2(-1138.5321044921875f, 31.056001663208008f),
            new float2(-1192.5501708984375f, 32.909629821777344f),
            new float2(-1196.0260009765625f, -68.36100006103516f),
            new float2(-1043.6160888671875f, -73.59200286865234f),
        };
        var zugaenge = new[]
        {
            new Entrance { Edge = 0, Along = 72.94681549072266, Art = Zufahrtsart.Einfahrt },
            new Entrance { Edge = 0, Along = 36.94664764404297, Art = Zufahrtsart.Fussweg },
            new Entrance { Edge = 5, Along = 181.99491348956846, Art = Zufahrtsart.Fussweg },
            new Entrance { Edge = 5, Along = 10.40000000175471, Art = Zufahrtsart.Fussweg },
            new Entrance { Edge = 5, Along = 61.50260925292969, Art = Zufahrtsart.Ausfahrt },
        };

        var fehler = 0;
        var e = LayoutSettings.Cs2;
        e.Randstrassen = false;
        e.AngleMode = "edge"; e.Auto = false; e.Zellen = true;
        e.Es = 1; e.Ai = 7; e.Cw = 3; e.Sl = 5.9; e.Sw = 3;
        e.Md = 2.5; e.Cr = 33; e.Qk = true; e.Angle = 0;
        e.Entrances = zugaenge;

        var layout = ParkingGeometry.Build(form, e);
        Console.WriteLine($"  Buchten {layout.Stalls}, Gassen {layout.Aisles}"
            + $", gesetzte Zugaenge {zugaenge.Length}"
            + $", Zufahrtslinien {layout.EntranceLine.Length}");
        foreach (var w in layout.Warnings ?? Array.Empty<string>())
            Console.WriteLine("      ! " + w.Substring(0, Math.Min(100, w.Length)));

        /*
         * JEDER GESETZTE ZUGANG MUSS ANKOMMEN.
         *
         * Der Nutzer setzt einen Zugang bewusst an eine Stelle. Kommt er
         * nicht heraus, sieht er weder in der Vorschau noch im Bau etwas -
         * und weiss nicht, warum. Eine Warnung allein reicht nicht: die
         * liest er im Bauzettel, nicht beim Bauen.
         */
        var fehlend = zugaenge.Length - layout.EntranceLine.Length;
        if (fehlend > 0)
        {
            fehler++;
            Console.WriteLine($"FEHLER: {fehlend} von {zugaenge.Length} gesetzten"
                + " Zugaengen kommen nicht im Layout an");
        }

        Console.WriteLine($"Fusswegzugang: {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }
}
