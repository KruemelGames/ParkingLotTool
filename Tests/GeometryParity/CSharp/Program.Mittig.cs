using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    /**
     * SITZT DAS STRASSENRASTER MITTIG?
     *
     * Nutzerbefund 2026-09-01: *"Manchmal haengt eine Strasse zu weit rechts
     * und dadurch die andere auch und es sieht optisch unstimmig aus."* Auf
     * Rueckfrage: beide Strassenarten, und mittig geht ihm vor Buchtenzahl.
     *
     * WAS HIER NICHT GEMESSEN WIRD, und warum: mein erster Versuch verglich
     * die RESTFLAECHE links und rechts des Rasters. Das kann "Raster
     * verschoben" nicht von "Form ist unsymmetrisch" unterscheiden - ein
     * keilfoermiges Teil hat am breiten Ende zwangslaeufig mehr Rest, auch
     * wenn das Raster exakt mittig sitzt. Eine Messung, die zwei Dinge
     * verwechselt, taugt nicht als Massstab.
     *
     * Gemessen wird deshalb der ABSTAND vom aeussersten Fahrweg zum Rand des
     * Teils, in der Reihenrichtung - genau die Groesse, die `Planung.Baender`
     * und `Planung.Querstrassenmitten` gleich gross machen.
     *
     * ZWEI DINGE, DIE HIER GROSS AUSSCHLAGEN UND TROTZDEM RICHTIG SIND -
     * dieser Lauf ist ein Werkzeug zum Hinsehen, kein Urteil:
     *
     *   1. AN DER TRENNKANTE fehlt die Randstrasse. Dort kommen die Wege
     *      rund 13,9 m naeher an den Umriss als an den drei anderen Seiten.
     *      Gemessen 22,53 gegen 11,83 m - das ist genau dieser Betrag, kein
     *      verschobenes Raster.
     *   2. EINE VERBINDUNGSSTRASSE, DIE NICHT HINPASST, gibt es nicht. Wo
     *      eine Reihe zum Ende hin zu kurz wird, steht dort keine - der
     *      Abstand zum Rand wird dann gross (gemessen bis 85 m), ohne dass
     *      etwas verschoben waere.
     *
     * Der Bezug muesste die Innenkontur sein, nicht der Umriss; die ist
     * nicht oeffentlich. Solange das so ist, urteilt das Auge.
     */
    private static (double Vorn, double Hinten) Randabstaende(
        float2[] teil, IEnumerable<float2[]> wege, double gradWinkel)
    {
        var bogen = -gradWinkel * Math.PI / 180.0;
        var cos = Math.Cos(bogen);
        var sin = Math.Sin(bogen);
        double2 Dreh(float2 p) => new double2(
            p.x * cos - p.y * sin, p.x * sin + p.y * cos);

        // Nur die Wege DIESER Teilflaeche - ein Quad der anderen Haelfte
        // wuerde die Grenze verschieben und die Messung wertlos machen.
        var eigene = wege.Where(quad =>
        {
            var mitte = float2.zero;
            foreach (var punkt in quad) mitte += punkt;
            return PointInRing(mitte / quad.Length, teil);
        }).SelectMany(quad => quad).Select(Dreh).ToArray();
        if (eigene.Length == 0 || teil.Length < 3)
            return (double.NaN, double.NaN);

        var rand = teil.Select(Dreh).ToArray();
        return (eigene.Min(p => p.y) - rand.Min(p => p.y),
                rand.Max(p => p.y) - eigene.Max(p => p.y));
    }

    /** Eine Zeile je Teilflaeche - jede in IHREM Winkel UND IHRER Form. */
    private static void ZeigeMittigkeit(
        string name, IReadOnlyList<(float2[] Form, double Winkel)> teile,
        ParkingLayout layout)
    {
        string Paar((double Vorn, double Hinten) wert) =>
            double.IsNaN(wert.Vorn)
                ? "     -  /      -  ->     - "
                : $"{wert.Vorn,7:F2} / {wert.Hinten,7:F2} -> "
                    + $"{Math.Abs(wert.Vorn - wert.Hinten),5:F2}";

        for (var t = 0; t < teile.Count; t++)
        {
            var gassen = Randabstaende(
                teile[t].Form, layout.AisleQuad, teile[t].Winkel);
            var quer = Randabstaende(
                teile[t].Form, layout.CrossQuad, teile[t].Winkel + 90);
            Console.WriteLine($"{string.Empty,-34} Teil {t} "
                + $"{teile[t].Winkel,6:F1} Grad | Gassen {Paar(gassen)} m"
                + $" | Verbindungen {Paar(quer)} m");
        }
    }
}
