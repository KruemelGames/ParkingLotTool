using System;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text.Json;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    private static int PruefeZettelvollstaendigkeit()
    {
        static string[] Mitglieder(Type type)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            return type.GetMembers(flags)
                .Where(m => m is FieldInfo f && !f.IsSpecialName
                        && (f.IsPublic || f.IsAssembly)
                    || m is PropertyInfo p && p.GetMethod != null
                        && (p.GetMethod.IsPublic || p.GetMethod.IsAssembly))
                .Select(m => m.Name).OrderBy(n => n).ToArray();
        }

        var layout = Mitglieder(typeof(LayoutSettings));
        var vegetation = Mitglieder(typeof(VegetationOptions));
        var fehlt = layout.Except(ParkingSettingsInventory.Layout)
            .Select(n => "LayoutSettings." + n)
            .Concat(vegetation.Except(ParkingSettingsInventory.Vegetation)
                .Select(n => "VegetationOptions." + n)).ToArray();
        var zuviel = ParkingSettingsInventory.Layout.Except(layout)
            .Select(n => "LayoutSettings." + n)
            .Concat(ParkingSettingsInventory.Vegetation.Except(vegetation)
                .Select(n => "VegetationOptions." + n)).ToArray();
        var dopplungen = ParkingSettingsInventory.Layout
            .GroupBy(n => n).Where(g => g.Count() > 1)
            .Select(g => "LayoutSettings." + g.Key)
            .Concat(ParkingSettingsInventory.Vegetation
                .GroupBy(n => n).Where(g => g.Count() > 1)
                .Select(g => "VegetationOptions." + g.Key)).ToArray();
        var bild = ParkingSettingsInventory.Erfasse(LayoutSettings.Cs2,
            ParkingSettingsInventory.Layout);
        var pflanzen = ParkingSettingsInventory.Erfasse(new VegetationOptions(),
            ParkingSettingsInventory.Vegetation);
        var probe = LayoutSettings.Cs2;
        probe.Zoningflaechen = new[] { new ParkingGeometry.Zoningflaeche
        {
            Ecke = new float2(13, 27), Spalten = 2, Reihen = 3,
        } };
        var form = ParkingSettingsInventory.Erfasse(probe,
            ParkingSettingsInventory.Layout);
        var zonen = (object[])form["Zoningflaechen"];
        var ecke = (float[])((System.Collections.Generic.Dictionary<string,
            object>)zonen[0])["Ecke"];
        if (ecke.Length != 2 || ecke[0] != 13 || ecke[1] != 27)
            throw new InvalidOperationException(
                "Zoning-Ecke geht im JSON-Abbild verloren");
        var fehler = fehlt.Length + zuviel.Length + dopplungen.Length
            + Math.Abs(bild.Count - layout.Length)
            + Math.Abs(pflanzen.Count - vegetation.Length);
        var cloneProbe = LayoutSettings.Cs2;
        foreach (var name in ParkingSettingsInventory.Layout)
        {
            var property = typeof(LayoutSettings).GetProperty(name,
                BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance);
            if (property == null) throw new InvalidOperationException(name);
            var type = property.PropertyType;
            object value = type == typeof(double) ? 17.25
                : type == typeof(double?) ? (double?)18.25
                : type == typeof(bool) ? !(bool)property.GetValue(cloneProbe)
                : type == typeof(string) ? "zettelprobe"
                : type.IsArray ? Array.CreateInstance(type.GetElementType(), 1)
                : throw new InvalidOperationException("Unbekannter Typ: " + name);
            if (type.IsArray)
                ((Array)value).SetValue(Activator.CreateInstance(
                    type.GetElementType()), 0);
            property.SetValue(cloneProbe, value);
        }
        var soll = ParkingSettingsInventory.Erfasse(cloneProbe,
            ParkingSettingsInventory.Layout);
        var ist = ParkingSettingsInventory.Erfasse(cloneProbe.Clone(),
            ParkingSettingsInventory.Layout);
        foreach (var name in ParkingSettingsInventory.Layout)
            if (JsonSerializer.Serialize(soll[name]) !=
                JsonSerializer.Serialize(ist[name]))
            {
                Console.WriteLine("CLONE VERLIERT: " + name);
                fehler++;
            }
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName,
            "UI", "src", "mods", "bindings.ts"))) root = root.Parent;
        if (root == null) throw new InvalidOperationException(
            "UI/src/mods/bindings.ts nicht gefunden");
        var bindingText = File.ReadAllText(Path.Combine(root.FullName,
            "UI", "src", "mods", "bindings.ts"));
        var triggers = Regex.Matches(bindingText,
            "trigger\\(MOD,\\s*\"(Set\\w+)\"")
            .Select(m => m.Groups[1].Value).Distinct().OrderBy(n => n)
            .ToArray();
        var eingeteilt = ParkingSettingsInventory.BauTrigger
            .Concat(ParkingSettingsInventory.AndereTrigger).ToArray();
        var unbekannt = triggers.Except(eingeteilt).ToArray();
        var veraltet = eingeteilt.Except(triggers).ToArray();
        foreach (var name in unbekannt) Console.WriteLine("UI NEU: " + name);
        foreach (var name in veraltet) Console.WriteLine("UI VERALTET: " + name);
        fehler += unbekannt.Length + veraltet.Length
            + eingeteilt.Length - eingeteilt.Distinct().Count();
        var journal = File.ReadAllText(Path.Combine(root.FullName,
            "Tools", "ParkingLotBuildJournal.cs"));
        var uiKeys = Regex.Matches(journal, "\\[\"(\\w+)\"\\]\\s*=")
            .Select(m => m.Groups[1].Value).ToArray();
        foreach (var name in ParkingSettingsInventory.Panelwerte)
            if (uiKeys.Count(key => key == name) != 2)
            {
                Console.WriteLine("PANEL FEHLT IN EINEM ZETTEL: " + name);
                fehler++;
            }
        foreach (var name in uiKeys.Except(ParkingSettingsInventory.Panelwerte))
        {
            Console.WriteLine("PANEL NICHT IM INVENTAR: " + name);
            fehler++;
        }
        foreach (var name in fehlt) Console.WriteLine("FEHLT: " + name);
        foreach (var name in zuviel) Console.WriteLine("ZU VIEL: " + name);
        foreach (var name in dopplungen) Console.WriteLine("DOPPELT: " + name);
        Console.WriteLine($"Zettelvollstaendigkeit: {layout.Length} Layoutwerte, "
            + $"{vegetation.Length} Vegetationswerte, "
            + $"{ParkingSettingsInventory.Panelwerte.Length} Panelwerte, "
            + $"{triggers.Length} UI-Trigger, {fehler} Fehler.");
        return fehler == 0 ? 0 : 1;
    }
}
