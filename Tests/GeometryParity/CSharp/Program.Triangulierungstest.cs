using System;
using System.Globalization;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    /**
     * TRIFFT DER NACHBAU DAS SPIEL?
     *
     * `Cs2Triangulierung` ist aus dem Dekompilat abgeschrieben. Abgeschrieben
     * heisst nicht richtig: eine vertauschte Reihenfolge in einer
     * float-Multiplikation reicht, um das letzte Bit zu aendern - und genau um
     * dieses Bit geht es hier.
     *
     * Der Nutzer hat am 2026-08-24 im Spiel gemessen. Dieser Lauf stellt
     * dieselben Polygone an dieselbe Weltposition und sucht dieselben Grenzen.
     * Kommen dieselben Zahlen heraus, ist der Nachbau belegt und wir koennen
     * offline messen, statt jedes Mal das Spiel zu starten.
     *
     * Die Sollwerte stammen aus `PLT-Sondenergebnis`, Lauf 15:07:37 bei
     * -1421,5 / -328,0:
     *
     *     Kuerzeste Kante              0,1415 bis 0,1418 m
     *     Kuerzeste Kante doppelte Hoehe 0,1619 bis 0,1622 m
     *     Engster Hals                 bis 0,0005 m angenommen
     *     Spitzester Winkel            0,1159 bis 0,1195 Grad
     *     Umlaufrichtung im Uhrzeigersinn  angenommen
     *     Kreis mit 200 Punkten            angenommen
     */
    private static int RunTriangulierungstest(float x = -1421.5f,
                                             float z = -328.0f)
    {
        // Die Weltposition ist keine Nebensache: die Ausloeschung im
        // Kreuzprodukt haengt an der Groesse der Koordinaten. Deshalb ist sie
        // einstellbar - so laesst sich jeder Sondenlauf des Nutzers, egal wo
        // er ihn gestartet hat, offline nachrechnen.
        var mitte = new float2(x, z);

        Console.WriteLine("TRIANGULIERUNGS-GEGENPROBE gegen die Ingame-Messung");
        Console.WriteLine($"  Weltposition {mitte.x} / {mitte.y}, "
            + "float wie im Spiel");
        Console.WriteLine();

        var referenz = math.abs(x + 1421.5f) < 0.01f
            && math.abs(z + 328.0f) < 0.01f;
        if (!referenz)
            Console.WriteLine("  Andere Stelle als die Referenzmessung - die "
                + "Sollwerte gelten dort nicht, die Zahlen sind zum Vergleich "
                + "mit einem Sondenlauf an genau dieser Stelle gedacht.");
        var fehler = 0;

        fehler += Suche("Kuerzeste Kante", 0.1415, 0.1418, p => Versetze(mitte, new[]
        {
            new float2(0f, 0f), new float2((float)p, 0f),
            new float2(20f, 20f), new float2(0f, 20f),
        }));

        fehler += Suche("Kuerzeste Kante doppelte Hoehe", 0.1619, 0.1622,
            p => Versetze(mitte, new[]
        {
            new float2(0f, 0f), new float2((float)p, 0f),
            new float2(20f, 40f), new float2(0f, 40f),
        }));

        fehler += Suche("Spitzester Winkel", 0.1159, 0.1195, p =>
        {
            var halb = math.radians((float)p * 0.5f);
            return Versetze(mitte, new[]
            {
                new float2(0f, 0f),
                new float2(40f * math.sin(halb), 40f * math.cos(halb)),
                new float2(-40f * math.sin(halb), 40f * math.cos(halb)),
            });
        }, 60.0, 0.05);

        // Zwei Einzelproben: hier gibt es keine Grenze, nur ein Ja.
        fehler += Einzeln("Engster Hals 0,0008 m", true, Versetze(mitte, new[]
        {
            new float2(0f, 0f), new float2(20f, 0f), new float2(20f, 20f),
            new float2(12f, 20f), new float2(12f, 20.000805f),
            new float2(20f, 20.000805f), new float2(20f, 40f), new float2(0f, 40f),
        }));

        var kreis = new float2[200];
        for (var i = 0; i < kreis.Length; i++)
        {
            var w = math.radians(360f * i / kreis.Length);
            kreis[i] = new float2(30f * math.cos(w), 30f * math.sin(w));
        }
        fehler += Einzeln("Kreis mit 200 Punkten", true, Versetze(mitte, kreis));

        Console.WriteLine();
        if (fehler == 0)
        {
            Console.WriteLine("  NACHBAU BELEGT: alle Sollwerte getroffen.");
            return 0;
        }
        Console.WriteLine($"  NACHBAU TRIFFT NICHT: {fehler} Abweichung(en). "
            + "Offline gemessene Zahlen sind damit nicht belastbar.");
        return 1;
    }

    /** Verschiebt ein Testpolygon an die Weltposition der Ingame-Messung. */
    private static float2[] Versetze(float2 mitte, float2[] polygon)
    {
        var raus = new float2[polygon.Length];
        for (var i = 0; i < polygon.Length; i++) raus[i] = mitte + polygon[i];
        return raus;
    }

    /**
     * Dieselbe Binaersuche wie die Sonde im Spiel - gleiche Startwerte,
     * gleiche Rundenzahl. Ein anderer Suchweg koennte bei gleichem Kriterium
     * eine andere Grenze melden.
     */
    private static int Suche(string name, double sollVon, double sollBis,
                             Func<double, float2[]> baue,
                             double gut = 5.0, double schlecht = 0.0005)
    {
        for (var runde = 0; runde < 14; runde++)
        {
            var wert = (gut + schlecht) * 0.5;
            if (Cs2Triangulierung.Dreiecke(baue(wert)) > 0) gut = wert;
            else schlecht = wert;
        }
        var treffer = Math.Abs(schlecht - sollVon) < sollVon * 0.02
                   && Math.Abs(gut - sollBis) < sollBis * 0.02;
        Console.WriteLine($"  {name,-32} nachgebaut "
            + $"{schlecht.ToString("G4", CultureInfo.InvariantCulture)} bis "
            + $"{gut.ToString("G4", CultureInfo.InvariantCulture)} | Spiel "
            + $"{sollVon.ToString("G4", CultureInfo.InvariantCulture)} bis "
            + $"{sollBis.ToString("G4", CultureInfo.InvariantCulture)} | "
            + (treffer ? "TRIFFT" : "WEICHT AB"));
        return treffer ? 0 : 1;
    }

    private static int Einzeln(string name, bool soll, float2[] polygon)
    {
        var dreiecke = Cs2Triangulierung.Dreiecke(polygon);
        var ist = dreiecke > 0;
        Console.WriteLine($"  {name,-32} nachgebaut "
            + (ist ? "angenommen" : "abgelehnt")
            + $" ({dreiecke} Dreiecke) | Spiel "
            + (soll ? "angenommen" : "abgelehnt") + " | "
            + (ist == soll ? "TRIFFT" : "WEICHT AB"));
        return ist == soll ? 0 : 1;
    }
}
