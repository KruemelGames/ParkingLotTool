using System;
using System.IO;
using System.Text;
using ParkingLotTool.Tools;

internal static partial class Program
{
    private static void Logauszuege()
    {
        var ordner = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../.codex-build/speichersicherheit-1-2/testdaten"));
        Directory.CreateDirectory(ordner);
        var pfad = Path.Combine(ordner, "test.log");
        var utf8 = new UTF8Encoding(false, true);
        void Schreibe(string text) => File.WriteAllText(pfad, text, utf8);
        void Grenzen(string text, string name)
        {
            Pruefe(utf8.GetByteCount(text) <= 1024 * 1024, name + ": 1 MiB einschliesslich Kopf und Kuerzung");
            foreach (var zeile in text.Split(new[] { Environment.NewLine }, StringSplitOptions.None))
                Pruefe(utf8.GetByteCount(zeile + Environment.NewLine) <= 8 * 1024, name + ": Zeile innerhalb 8 KiB");
            Pruefe(!text.Contains("\ufffd"), name + ": kein durch Trennung erzeugtes Ersatzzeichen");
        }
        Schreibe("alt\r\nmittel\nneu\rletzte");
        var text = ParkingLotLogauszug.Lesen(pfad, 2, "Kopf");
        Pruefe(text.Contains("gekuerzt") && !text.Contains("mittel") && text.IndexOf("neu", StringComparison.Ordinal)
            < text.IndexOf("letzte", StringComparison.Ordinal), "juengste Zeilen bei CRLF, LF und CR in richtiger Reihenfolge");
        Grenzen(text, "Zeilenring");

        Schreibe("abc\r\n\r\nxyz\n");
        text = ParkingLotLogauszug.Lesen(pfad, 20, "Kopf");
        Pruefe(!text.Contains("gekuerzt") && text.Contains("abc") && text.Contains("xyz"),
            "kleine vollstaendige Datei bleibt ungekuerzt");
        var grenze = 8192 - utf8.GetByteCount(Environment.NewLine);
        Schreibe(new string('a', grenze) + "\n");
        text = ParkingLotLogauszug.Lesen(pfad, 2, "");
        Pruefe(!text.Contains("Zeile gekuerzt"), "genau passende 8-KiB-Zeile bleibt vollstaendig");
        Grenzen(text, "exakte Zeilengrenze");
        Schreibe(new string('a', grenze - 3) + "🌳" + "Ende\nNEUSTE\n");
        text = ParkingLotLogauszug.Lesen(pfad, 2, "系統 🌳");
        Pruefe(text.Contains("Zeile gekuerzt") && text.Contains("NEUSTE"), "UTF-8-Paar an der Schnittgrenze und folgende Zeile bleiben gueltig");
        Grenzen(text, "UTF-8-Schnitt");

        using (var w = new StreamWriter(pfad, false, utf8))
        {
            for (int i = 0; i < 7 * 1024; i++) w.Write(new string('x', 1024));
            w.Write("\nLETZTE\n");
        }
        ParkingLotLogauszug.Lesen(pfad, 2, ""); // JIT und Decoder vor Allokationsmessung.
        var vorher = GC.GetAllocatedBytesForCurrentThread();
        text = ParkingLotLogauszug.Lesen(pfad, 2, "");
        var allokation = GC.GetAllocatedBytesForCurrentThread() - vorher;
        Pruefe(allokation < 1024 * 1024, "7-MiB-Einzelzeile wird ohne megabytegrossen Zeilenpuffer gelesen");
        Pruefe(text.Contains("Zeile gekuerzt") && text.Contains("LETZTE"), "7-MiB-Zeile sichtbar gekuerzt, juengste Zeile erhalten");
        Grenzen(text, "7-MiB-Zeile");

        Schreibe("PLT-Sync alt\nignorieren\n" + new string('x', 4093) + "PLT-Sync ueber Blockgrenze\n"
            + new string('x', 16000) + "Exception nach Zeilengrenze\n");
        text = ParkingLotLogauszug.Lesen(pfad, 2, "Sync", new[] { "PLT-Sync", "Exception" });
        Pruefe(!text.Contains("PLT-Sync alt") && !text.Contains("ignorieren") && text.Contains("PLT-Sync ueber"),
            "Sync-Filter erkennt Blockgrenze und behaelt die juengsten passenden Zeilen");
        Pruefe(text.Contains("Zeile gekuerzt"), "Sync-Marke hinter gekuerztem Zeilenrest wird trotzdem gefunden");
        Grenzen(text, "Sync-Filter");

        using (var w = new StreamWriter(pfad, false, utf8))
            for (int i = 0; i < 1500; i++) w.WriteLine("ZEILE_" + i.ToString("D4") + new string('a', 1500));
        text = ParkingLotLogauszug.Lesen(pfad, 2000, "Report");
        Pruefe(text.Contains("Auszug gekuerzt") && text.Contains("ZEILE_1499") && !text.Contains("ZEILE_0000"),
            "Bytegrenze entfernt alte statt neue Zeilen");
        Grenzen(text, "Gesamtauszug");
        var anonym = ParkingLotLogauszug.Begrenzen(text.Replace("aaa", "<nutzer>"));
        Pruefe(anonym.Contains("ZEILE_1499"), "Laengenzuwachs durch Anonymisierung behaelt juengste Zeile");
        Grenzen(anonym, "nach Anonymisierung");
        Schreibe("\ufeffPLT-Sync 🌳\n");
        text = ParkingLotLogauszug.Lesen(pfad, 2, "Sync", new[] { "PLT-Sync" });
        Pruefe(text.Contains("PLT-Sync 🌳") && !text.Contains("\ufeff"), "UTF-8-BOM wird gelesen und nicht in Auszug geschrieben");
        Grenzen(text, "BOM");
        // Echte Reportmethoden und fertige ZIP-Eintraege, nicht nur der Leser.
        using (var w = new StreamWriter(pfad, false, utf8))
            for (int i = 0; i < 1500; i++) w.WriteLine("PLT-Sync ZEILE_" + i.ToString("D4") + new string('a', 1500));
        foreach (var sync in new[] { false, true })
        {
            var report = ParkingLotMeldepaket.TestArchiv(ParkingLotMeldepaket.TestLog(pfad, sync), true);
            Grenzen(report, sync ? "Sync-ZIP" : "Build-ZIP");
            Pruefe(report.Contains("ZEILE_1499") && !report.Contains("ZEILE_0000"),
                "echter Reportpfad behaelt juengste Zeile: " + sync);
        }
        var username = Environment.UserName;
        var vorAnon = new StringBuilder();
        for (int i = 0; i < 900; i++)
            vorAnon.Append("ANON_").Append(i.ToString("D4")).Append(' ')
                .Append(new string('a', 600)).Append(username).AppendLine();
        var archivtext = ParkingLotMeldepaket.TestArchiv(vorAnon.ToString(), true);
        Grenzen(archivtext, "ZIP nach echter Anonymisierung");
        if (username.Length >= 3)
            Pruefe(!archivtext.Contains(username), "ZIP bleibt anonymisiert");
        var json = "{\"Daten\":\"" + new string('x', 1100000) + "\"}";
        Pruefe(ParkingLotMeldepaket.TestArchiv(json, false) == json,
            "JSON-Bauzettel wird durch Loggrenzen nicht beschnitten");

        Schreibe("Game version: TEST\n" + new string('x', 12000) + "\nvor Absturz\nCrash!!!\nStack🌳\nLETZTE\n");
        var player = ParkingLotMeldepaket.TestArchiv(ParkingLotMeldepaket.TestPlayer(pfad), true);
        Grenzen(player, "Player-ZIP");
        Pruefe(player.Contains("Game version: TEST") && player.Contains("vor Absturz")
            && player.Contains("Crash!!!") && player.Contains("Stack🌳") && player.Contains("LETZTE"),
            "Player-Auszug behaelt Systemkopf, Anlauf und Crashblock");
        Schreibe("Game version: TEST\nkein Absturz\nLETZTE\n");
        player = ParkingLotMeldepaket.TestPlayer(pfad);
        Pruefe(player.Contains("NO crash block") && player.Contains("LETZTE"), "Player-Auszug ohne Crash behaelt letztes Logende");
        Schreibe("");
        Pruefe(ParkingLotLogauszug.Lesen(pfad, 2, "") == "", "leeres Log ergibt leeren Auszug");
        Console.WriteLine($"LOGAUSZUG: 7-MiB-Zeile, Leserallokation {allokation} Bytes.");
    }
}
