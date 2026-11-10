using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static class Autovergleich
{
    private static int Main(string[] args)
    {
        try
        {
            var formen = new[]
            {
                new[]{new float2(0,0),new float2(120,0),new float2(120,90),new float2(0,90)},
                new[]{new float2(0,0),new float2(120,0),new float2(120,45),new float2(60,45),new float2(60,90),new float2(0,90)},
                new[]{new float2(0,0),new float2(120,0),new float2(150,90),new float2(30,90)},
            };
            var result = new SortedDictionary<string,string>();
            for(var i=0;i<formen.Length;i++)
            foreach(var ring in new[]{false,true})
            foreach(var ort in new[]{new float2(0,0),new float2(-1250,842)})
            {
                var settings=LayoutSettings.Cs2;
                settings.Randstrassen=ring;
                var layout=ParkingGeometry.Build(formen[i].Select(p=>p+ort).ToArray(),settings);
                // Alle Ausgabefelder einschliesslich Materialringe und interner
                // Rollen, alle Float-/Double-Bitmuster; kein Zahlenrunden.
                var json=JsonSerializer.Serialize(Erfasse(layout));
                var name=$"{i}/{ring}/{ort.x}";
                var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
                result.Add(name,hash);
                Console.WriteLine(name+" "+hash);
            }
            File.WriteAllText(args[0],JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception e){Console.WriteLine(e);return 1;}
    }
    private static object Erfasse(object obj, int tiefe = 0)
    {
        if(tiefe>40)throw new InvalidOperationException("Rekursion im Geometrie-Abzug: "+obj?.GetType());
        if(obj==null)return null;
        if(obj is float f)return BitConverter.SingleToInt32Bits(f);
        if(obj is double d)return BitConverter.DoubleToInt64Bits(d);
        var type=obj.GetType();
        if(type.IsPrimitive || type.IsEnum || obj is string)return obj;
        if(obj is IEnumerable liste)return liste.Cast<object>().Select(v=>Erfasse(v,tiefe+1)).ToArray();
        var result=new SortedDictionary<string,object>();
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        // Unity-Vektoren haben rekursive Swizzle-Properties; ihre benannten
        // Felder x/y/z/w enthalten bereits alle Bits.
        if(type.Namespace!="Unity.Mathematics")foreach(var p in type.GetProperties(flags))
            if(p.GetIndexParameters().Length==0 && p.Name!="Fahrrad")result[p.Name]=Erfasse(p.GetValue(obj),tiefe+1);
        foreach(var field in type.GetFields(flags))
            if(!field.Name.Contains("k__BackingField"))result[field.Name]=Erfasse(field.GetValue(obj),tiefe+1);
        return result;
    }
}
