using System;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /** Vor dem ersten Abriss vollstaendig sichern. Ein Save mitten in der
     *  Kopie darf keinen halben Rueckweg als fertigen Plan behandeln. */
    public struct ParkingLotRueckwegsicherungOffen : IComponentData, ISerializable
    {
        public void Serialize<TWriter>(TWriter w) where TWriter : IWriter { w.Write(1); }
        public void Deserialize<TReader>(TReader r) where TReader : IReader { r.Read(out int version); }
    }
    /** Liegt bis zum Erfolg am ALTEN Lot, auch im Save. Keine Temp-ID wird
     *  gespeichert. Eigene entfernte Knoten bekommen im Rueckweg neue IDs. */
    [InternalBufferCapacity(0)]
    public struct ParkingLotRueckwegkurs : IBufferElementData, ISerializable
    {
        public Entity Kante, Prefab, Besitzer, Start, Ende;
        /** PrefabID des Prefabs (ParkingLotPrefabname); ueberlebt das Laden auch ohne Umbiegen (Issue #17). */
        public FixedString512Bytes PrefabName;
        public NetCourse Kurs;
        public bool HatUpgrade;
        public Upgraded Upgrade;
        public void Serialize<TWriter>(TWriter w) where TWriter : IWriter
        {
            w.Write(2); w.Write(Kante); w.Write(Prefab); w.Write(Besitzer);
            w.Write(Start); w.Write(Ende);
            w.Write(Kurs.m_Curve.a); w.Write(Kurs.m_Curve.b);
            w.Write(Kurs.m_Curve.c); w.Write(Kurs.m_Curve.d);
            w.Write(Kurs.m_Length); w.Write(Kurs.m_Elevation); w.Write(HatUpgrade);
            w.Write((uint)Upgrade.m_Flags.m_General);
            w.Write((uint)Upgrade.m_Flags.m_Left); w.Write((uint)Upgrade.m_Flags.m_Right);
            SchreibePos(w, Kurs.m_StartPosition); SchreibePos(w, Kurs.m_EndPosition);
            w.Write(PrefabName.ToString());
        }
        private static void SchreibePos<TWriter>(TWriter w, CoursePos p) where TWriter : IWriter
        {
            w.Write(p.m_Position); w.Write(p.m_Rotation); w.Write(p.m_Elevation);
            w.Write((uint)p.m_Flags);
        }
        public void Deserialize<TReader>(TReader r) where TReader : IReader
        {
            r.Read(out int version); if (version != 1 && version != 2) throw new InvalidOperationException("Rueckwegversion");
            r.Read(out Kante); r.Read(out Prefab); r.Read(out Besitzer);
            r.Read(out Start); r.Read(out Ende);
            r.Read(out float3 a); r.Read(out float3 b); r.Read(out float3 c); r.Read(out float3 d);
            r.Read(out float laenge); r.Read(out float2 elevation); r.Read(out HatUpgrade);
            r.Read(out uint general); r.Read(out uint links); r.Read(out uint rechts);
            Upgrade = new Upgraded { m_Flags = new CompositionFlags((CompositionFlags.General)general,
                (CompositionFlags.Side)links, (CompositionFlags.Side)rechts) };
            Kurs = new NetCourse { m_Curve = new Bezier4x3(a,b,c,d), m_Length = laenge,
                m_Elevation = elevation, m_FixedIndex = -1,
                m_StartPosition = LiesPos(r, Start, true), m_EndPosition = LiesPos(r, Ende, false) };
            PrefabName = default;
            if (version >= 2) { r.Read(out string name); PrefabName = ParkingLotPrefabname.Fest(name); }
        }
        private static CoursePos LiesPos<TReader>(TReader r, Entity original, bool start) where TReader : IReader
        {
            r.Read(out float3 p); r.Read(out quaternion q); r.Read(out float2 e); r.Read(out uint flags);
            return new CoursePos { m_Entity = original, m_Position = p, m_Rotation = q,
                m_Elevation = e, m_Flags = (CoursePosFlags)flags, m_ParentMesh = -1,
                m_CourseDelta = start ? 0 : 1 };
        }
    }

    // Separat vom Rueckbauplan: diese IDs werden weder geloescht noch neu
    // erzeugt. Der gespeicherte Befund bleibt auch nach Laden pruefbar.
    [InternalBufferCapacity(0)]
    public struct ParkingLotErhaltenerKurs : IBufferElementData, ISerializable
    {
        public ParkingLotRueckwegkurs Bestand;
        public void Serialize<TWriter>(TWriter w) where TWriter : IWriter => Bestand.Serialize(w);
        public void Deserialize<TReader>(TReader r) where TReader : IReader => Bestand.Deserialize(r);
    }

}
