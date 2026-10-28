using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

internal static class Vergleich
{
    // Alle produktiven Daten einschliesslich interner Rollenflaechen und
    // Warnungen; float/double als rohe Bits, Reihenfolgen unveraendert.
    internal static string Abdruck(object wert)
    {
        using var speicher = new MemoryStream();
        using var w = new BinaryWriter(speicher);
        Schreibe(w, wert);
        return Convert.ToHexString(SHA256.HashData(speicher.ToArray()));
    }

    /**
     * Wie `Abdruck`, aber ohne eine oberste Eigenschaft - fuer Referenzen, die
     * vor einem NEU hinzugekommenen Feld entstanden sind (StrassenbelagSurface,
     * 27.10.). Dieselbe Byte-Folge wie damals, solange das Feld fehlt; dass es
     * im Fall wirklich leer ist, prueft der Aufrufer selbst.
     */
    internal static string AbdruckOhne(object wert, params string[] ohne)
    {
        using var speicher = new MemoryStream();
        using var w = new BinaryWriter(speicher);
        w.Write(true);
        foreach (var p in wert.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(p => p.GetIndexParameters().Length == 0 && !ohne.Contains(p.Name)).OrderBy(p => p.Name))
        {
            w.Write(p.Name); Schreibe(w, p.GetValue(wert), 1);
        }
        return Convert.ToHexString(SHA256.HashData(speicher.ToArray()));
    }

    private static void Schreibe(BinaryWriter w, object v, int tiefe = 0)
    {
        if (tiefe > 32) throw new InvalidOperationException("Zyklische Layoutdaten: " + v?.GetType().FullName);
        w.Write(v != null);
        if (v == null) return;
        var t = v.GetType();
        if (v is float f) { w.Write(BitConverter.SingleToInt32Bits(f)); return; }
        if (v is double d) { w.Write(BitConverter.DoubleToInt64Bits(d)); return; }
        if (v is string s) { w.Write(s); return; }
        if (t.IsPrimitive || t.IsEnum) { w.Write(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)); return; }
        if (v is IEnumerable folge)
        {
            foreach (var e in folge) { w.Write(true); Schreibe(w, e, tiefe + 1); }
            w.Write(false); return;
        }
        // Unity-Vektoren haben viele swizzle-Properties: nur ihre echten Felder.
        if (t.Namespace == "Unity.Mathematics" || t.FullName.StartsWith("System.ValueTuple"))
        {
            foreach (var feld in t.GetFields(BindingFlags.Instance | BindingFlags.Public).OrderBy(p => p.Name))
                Schreibe(w, feld.GetValue(v), tiefe + 1);
            return;
        }
        foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(p => p.GetIndexParameters().Length == 0).OrderBy(p => p.Name))
        {
            w.Write(p.Name); Schreibe(w, p.GetValue(v), tiefe + 1);
        }
    }
}
