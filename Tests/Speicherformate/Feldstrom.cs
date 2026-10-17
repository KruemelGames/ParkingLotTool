using System;
using System.Collections.Generic;
using System.Text.Json;
using Colossal.Serialization.Entities;
using Unity.Entities;
using Unity.Mathematics;

// Eingefrorene IReader/IWriter-Feldfolgen; kein Nachbau des nativen .cok-Dateiformats.
internal sealed class Feld
{
    public string Name { get; set; }
    public string Typ { get; set; }
    public JsonElement Wert { get; set; }
    public object Lesen() => Typ switch
    {
        "Int32" => Wert.GetInt32(), "UInt32" => Wert.GetUInt32(),
        "Single" => Wert.GetSingle(), "Double" => Wert.GetDouble(),
        "Boolean" => Wert.GetBoolean(), "String" => Wert.GetString(),
        "Entity" => new Entity(Wert[0].GetInt32(), Wert[1].GetInt32()),
        "float2" => new float2(Wert[0].GetSingle(), Wert[1].GetSingle()),
        "float3" => new float3(Wert[0].GetSingle(), Wert[1].GetSingle(), Wert[2].GetSingle()),
        "quaternion" => new quaternion(Wert[0].GetSingle(), Wert[1].GetSingle(), Wert[2].GetSingle(), Wert[3].GetSingle()),
        _ => throw new InvalidOperationException("Unbekannter Fixturtyp: " + Typ),
    };
}
internal sealed class Fixtur
{
    public string Art { get; set; }
    public int Version { get; set; }
    public string Quelle { get; set; }
    public Feld[][] Datensaetze { get; set; }
}
internal sealed class Leser : IReader
{
    private readonly List<Feld> _felder;
    public int Position { get; private set; }
    public Leser(List<Feld> felder) { _felder = felder; }
    public void Read<T>(out T value)
    {
        if (Position >= _felder.Count) throw new InvalidOperationException("Ueber Endmarke hinaus gelesen.");
        var f = _felder[Position++];
        if (f.Typ != typeof(T).Name)
            throw new InvalidOperationException($"Feld {Position} {f.Name}: {f.Typ} statt {typeof(T).Name}.");
        value = (T)f.Lesen();
    }
}
internal sealed class Schreiber : IWriter
{
    public readonly List<(Type Typ, object Wert)> Felder = new();
    public void Write<T>(T value) => Felder.Add((typeof(T), value));
}
