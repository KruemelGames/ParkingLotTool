using System;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    private static int RunGassenlaenge()
    {
        var fehler = 0;
        var anzahl = 0;
        var hoechste = 0f;
        var groessterRichtungsfehler = 0f;
        var laengen = new[] { 1f, 14.32f, 16f, 16.01f, 17.10f,
            20.82f, 37.92f, 100f, 1000f };
        var nutzerlaengen = new[] { 14.32f, 14.32f, 14.32f, 14.32f,
            17.10f, 20.82f, 37.92f, 37.92f };
        var nutzerteile = 0;
        foreach (var nutzerlaenge in nutzerlaengen)
            nutzerteile += ParkingGeometry.TeileGassenkurs(new NetSegment(
                "entrance-gasse", float2.zero,
                new float2(nutzerlaenge, 0))).Length;
        if (nutzerteile != 14) fehler++;
        foreach (var laenge in laengen)
        foreach (var start in new[] { float2.zero, new float2(8000f, -8000f) })
        foreach (var vorzeichen in new[] { 1f, -1f })
        {
            var richtung = new float2(0.6f, 0.8f) * vorzeichen;
            var ende = start + richtung * laenge;
            var original = new NetSegment("entrance-gasse", start, ende,
                Zufahrtsart.GasseAus);
            var teile = ParkingGeometry.TeileGassenkurs(original);
            var soll = math.normalizesafe(ende - start);
            if (teile.Length == 0 || !teile[0].A.Equals(start)
                || !teile[teile.Length - 1].B.Equals(ende)) fehler++;
            for (var i = 0; i < teile.Length; i++)
            {
                var teil = teile[i];
                var d = teil.B - teil.A;
                var l = math.length(d);
                var abweichung = math.distance(math.normalizesafe(d), soll);
                hoechste = math.max(hoechste, l);
                groessterRichtungsfehler = math.max(groessterRichtungsfehler,
                    abweichung);
                if (l <= 0f || l > 16f || abweichung > 0.001f
                    || teil.Art != original.Art || teil.Kind != original.Kind
                    || (i > 0 && !teile[i - 1].B.Equals(teil.A))) fehler++;
                anzahl++;
            }
            if (teile.Length != (int)Math.Ceiling(math.length(ende - start) / 16f))
                fehler++;
        }
        Console.WriteLine($"Gassenlaenge: Nutzer 8 -> {nutzerteile} Teilkanten; "
            + $"Matrix {anzahl} Teilkanten, Maximum "
            + $"{hoechste:F4} m, Richtungsfehler "
            + $"{groessterRichtungsfehler:F6}, {fehler} Fehler");
        return fehler == 0 ? 0 : 1;
    }
}
