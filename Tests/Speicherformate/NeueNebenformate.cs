using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Colossal.Serialization.Entities;
using ParkingLotTool.Tools;

internal static partial class Program
{
    private static void NeueNebenformate()
    {
        var fixtures = JsonSerializer.Deserialize<Fixtur[]>(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixturen/nebenarbeit.json")));
        foreach (var f in fixtures)
        {
            var strom = f.Datensaetze.SelectMany(r => r).ToList();
            strom.Add(new Feld { Name = "$ende", Typ = "String", Wert = JsonSerializer.SerializeToElement("ENDE_PLT") });
            var r = new Leser(strom);
            foreach (var row in f.Datensaetze)
            {
                int vor = r.Position;
                ISerializable wert = f.Art switch
                {
                    "NebenarbeitOffen" => Lies<ParkingLotNebenarbeitOffen>(r),
                    "Erzeugungsauftrag" => Lies<ParkingLotErzeugungsauftrag>(r),
                    "Versorgungsnachweis" => Lies<ParkingLotVersorgungsnachweis>(r),
                    _ => throw new InvalidOperationException(f.Art),
                };
                Pruefe(r.Position - vor == row.Length, f.Art + " genaue Datensatzgrenze");
                var w = new Schreiber(); wert.Serialize(w);
                Pruefe(w.Felder.Count == row.Length, f.Art + " Schreiberlaenge");
                for (int i = 0; i < Math.Min(row.Length, w.Felder.Count); i++)
                    Pruefe(w.Felder[i].Typ.Name == row[i].Typ && Gleich(w.Felder[i].Wert, row[i].Lesen()), f.Art + " Feld " + i);
            }
            r.Read(out string ende);
            Pruefe(ende == "ENDE_PLT" && r.Position == strom.Count, f.Art + " Endmarke nach zwei Datensaetzen");
        }
        var alt = new Leser(new List<Feld> {
            new Feld { Typ = "Int32", Wert = JsonSerializer.SerializeToElement(1) },
            new Feld { Typ = "String", Wert = JsonSerializer.SerializeToElement("ENDE") } });
        var marker = Lies<ParkingLotNebenarbeitOffen>(alt);
        Pruefe(!marker.VersorgungBekannt && (marker.Offen & NebenarbeitPflicht.Versorgung) == 0
            && (marker.Offen & NebenarbeitPflicht.Bushalte) != 0, "V1 erfindet keine historische Versorgungswahl");
        alt.Read(out string letzte);
        Pruefe(letzte == "ENDE", "V1 bleibt genau ein Feld lang");
    }
}
