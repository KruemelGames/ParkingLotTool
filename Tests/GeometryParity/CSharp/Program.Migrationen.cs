using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ParkingLotTool.Geometry;

/**
 * `--migrationen`: Katalog und Ausfuehrung der Synchronisationsschritte
 * passen zusammen.
 *
 * Der Katalog steht in Geometry/, die Ausfuehrung in Tools/ - das
 * Testprojekt uebersetzt Tools/ nicht, liest die Datei aber als Text. Rot,
 * wenn eine Nummer fehlt oder doppelt ist, ein Name doppelt ist, ein
 * Schritt keine Ausfuehrung hat oder eine Ausfuehrung zu keinem Schritt
 * gehoert. Ein Parkplatz bliebe sonst fuer immer vor dem Schritt stehen.
 */
internal static partial class Program
{
    private static int PruefeMigrationen()
    {
        var fehler = 0;
        var schritte = Migrationskatalog.Schritte;
        for (var i = 0; i < schritte.Length; i++)
            if (schritte[i].Nummer != i + 1)
            {
                Console.WriteLine("NUMMER: Platz " + (i + 1) + " traegt "
                    + schritte[i].Nummer + " - Nummern muessen lueckenlos ab 1 laufen.");
                fehler++;
            }
        foreach (var g in schritte.GroupBy(s => s.Name).Where(g => g.Count() > 1))
        {
            Console.WriteLine("DOPPELT: " + g.Key);
            fehler++;
        }
        foreach (var s in schritte.Where(s => string.IsNullOrWhiteSpace(s.Zweck)))
        {
            Console.WriteLine("OHNE ZWECK: " + s.Name);
            fehler++;
        }
        if (Migrationskatalog.Aktuell != schritte.Length)
        {
            Console.WriteLine("AKTUELL passt nicht zur Schrittzahl.");
            fehler++;
        }

        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName,
            "ParkingLotTool.csproj"))) root = root.Parent;
        if (root == null) throw new InvalidOperationException("Projekt nicht gefunden");
        var text = File.ReadAllText(Path.Combine(root.FullName, "Tools",
            "ParkingLotSync.Schritte.cs"));
        var ausfuehrungen = Regex.Matches(text, @"\[""(\w+)""\]\s*=\s*new Ausfuehrung")
            .Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
        if (ausfuehrungen.Length == 0)
        {
            Console.WriteLine("KEINE Ausfuehrung gefunden - der Sucher ist kaputt.");
            return 1;
        }
        foreach (var s in schritte.Where(s => !ausfuehrungen.Contains(s.Name)))
        {
            Console.WriteLine("OHNE AUSFUEHRUNG: " + s.Nummer + " " + s.Name);
            fehler++;
        }
        foreach (var a in ausfuehrungen.Where(a => schritte.All(s => s.Name != a)))
        {
            Console.WriteLine("AUSFUEHRUNG OHNE SCHRITT: " + a);
            fehler++;
        }
        Console.WriteLine("MIGRATIONEN: " + schritte.Length + " Schritte, "
            + ausfuehrungen.Length + " Ausfuehrungen, " + fehler + " Fehler.");
        return fehler == 0 ? 0 : 1;
    }
}
