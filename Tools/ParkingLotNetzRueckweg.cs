using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using SubNet = Game.Net.SubNet;

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

    internal static class ParkingLotNetzRueckweg
    {
        internal static void Sichere(EntityManager em, Entity lot, IEnumerable<Entity> teile,
            HashSet<Entity> erhalten)
        {
            foreach (int n in SichereSchritte(em,lot,teile,erhalten)) { }
        }
        internal static IEnumerable<int> SichereSchritte(EntityManager em, Entity lot, IEnumerable<Entity> teile,
            HashSet<Entity> erhalten)
        {
            VerwerfeOffeneSicherung(em,lot);
            if (em.HasBuffer<ParkingLotRueckwegkurs>(lot)) yield break;
            var kandidaten = new HashSet<Entity>(teile);
            var kurse = new List<ParkingLotRueckwegkurs>();
            var bleibt = new List<ParkingLotErhaltenerKurs>();
            var anschluesse = new List<ParkingLotRueckweganschluss>();
            int eigene = 0, fremde = 0;
            var knoten = new HashSet<Entity>();
            foreach (var e in kandidaten)
            {
                yield return 0;
                bool eigenerBesitzer = em.HasComponent<Owner>(e) && ParkingLotBesitz.GehoertZu(em,em.GetComponentData<Owner>(e).m_Owner,lot);
                bool behalten = erhalten.Contains(e);
                if (!RueckwegKnotenregel.KursSichern(eigenerBesitzer,em.HasComponent<Edge>(e),
                    em.HasComponent<Deleted>(e),em.HasComponent<Temp>(e),false)) continue;
                if (!behalten && em.HasBuffer<ConnectedNode>(e))
                    foreach (var a in em.GetBuffer<ConnectedNode>(e,true))
                    {
                        bool eigen = em.HasComponent<Owner>(a.m_Node) && ParkingLotBesitz.GehoertZu(em,em.GetComponentData<Owner>(a.m_Node).m_Owner,lot);
                        bool original = RueckwegKnotenregel.OriginalVerwenden(eigen,!erhalten.Contains(a.m_Node));
                        anschluesse.Add(new ParkingLotRueckweganschluss { Kursindex = kurse.Count,
                            Knoten = original ? a.m_Node : Entity.Null,Lage = a.m_CurvePosition,
                            Prefab = em.GetComponentData<PrefabRef>(a.m_Node).m_Prefab,
                            Besitzer = em.HasComponent<Owner>(a.m_Node) ? em.GetComponentData<Owner>(a.m_Node).m_Owner : Entity.Null,
                            Position = em.GetComponentData<Game.Net.Node>(a.m_Node).m_Position });
                    }
                var edge = em.GetComponentData<Edge>(e);
                var curve = em.GetComponentData<Curve>(e);
                Entity Original(Entity n)
                {
                    var eigen = em.HasComponent<Owner>(n) && ParkingLotBesitz.GehoertZu(em,em.GetComponentData<Owner>(n).m_Owner,lot);
                    if (knoten.Add(n)) { if (eigen && !erhalten.Contains(n)) eigene++; else fremde++; }
                    return RueckwegKnotenregel.OriginalVerwenden(eigen, !erhalten.Contains(n)) ? n : Entity.Null;
                }
                var start = Original(edge.m_Start); var ende = Original(edge.m_End);
                var elevation = em.HasComponent<Elevation>(e) ? em.GetComponentData<Elevation>(e).m_Elevation : float2.zero;
                CoursePos Pos(Entity n, Entity original, float3 p, bool erste)
                {
                    var node = em.GetComponentData<Game.Net.Node>(n);
                    return new CoursePos { m_Entity = original, m_Position = p, m_Rotation = node.m_Rotation,
                        m_Elevation = em.HasComponent<Elevation>(n) ? em.GetComponentData<Elevation>(n).m_Elevation : elevation,
                        m_ParentMesh = -1, m_CourseDelta = erste ? 0 : 1,
                        m_Flags = erste ? CoursePosFlags.IsFirst : CoursePosFlags.IsLast };
                }
                var bestand = new ParkingLotRueckwegkurs { Kante = e,
                    Prefab = em.GetComponentData<PrefabRef>(e).m_Prefab, Besitzer = em.GetComponentData<Owner>(e).m_Owner,
                    Start = start, Ende = ende, HatUpgrade = em.HasComponent<Upgraded>(e),
                    Upgrade = em.HasComponent<Upgraded>(e) ? em.GetComponentData<Upgraded>(e) : default,
                    Kurs = new NetCourse { m_Curve = curve.m_Bezier, m_Length = curve.m_Length,
                        m_FixedIndex = -1, m_Elevation = elevation,
                        m_StartPosition = Pos(edge.m_Start, start, curve.m_Bezier.a, true),
                        m_EndPosition = Pos(edge.m_End, ende, curve.m_Bezier.d, false) } };
                if (!behalten) bestand = Originalkurs(em,bestand);
                if (behalten)
                {
                    bestand.Start = edge.m_Start; bestand.Ende = edge.m_End;
                    bestand.Kurs.m_StartPosition = Pos(edge.m_Start,edge.m_Start,em.GetComponentData<Game.Net.Node>(edge.m_Start).m_Position,true);
                    bestand.Kurs.m_EndPosition = Pos(edge.m_End,edge.m_End,em.GetComponentData<Game.Net.Node>(edge.m_End).m_Position,false);
                    bleibt.Add(new ParkingLotErhaltenerKurs { Bestand=bestand });
                }
                else kurse.Add(bestand);
            }
            em.AddComponent<ParkingLotRueckwegsicherungOffen>(lot);
            em.AddBuffer<ParkingLotRueckwegkurs>(lot);
            foreach (var k in kurse)
            {
                var kurs = k; kurs.PrefabName = ParkingLotPrefabname.Von(em, kurs.Prefab);
                em.GetBuffer<ParkingLotRueckwegkurs>(lot).Add(kurs); yield return 0;
            }
            em.AddBuffer<ParkingLotRueckweganschluss>(lot);
            foreach (var x in anschluesse)
            {
                var a = x; a.PrefabName = ParkingLotPrefabname.Von(em, a.Prefab);
                em.GetBuffer<ParkingLotRueckweganschluss>(lot).Add(a); yield return 0;
            }
            em.AddBuffer<ParkingLotErhaltenerKurs>(lot);
            foreach (var k in bleibt)
            {
                var kurs = k; kurs.Bestand.PrefabName = ParkingLotPrefabname.Von(em, kurs.Bestand.Prefab);
                em.GetBuffer<ParkingLotErhaltenerKurs>(lot).Add(kurs); yield return 0;
            }
            em.RemoveComponent<ParkingLotRueckwegsicherungOffen>(lot);
            // Ab hier traegt der neue Schnappschuss die Pflicht (Issue #17).
            if (em.HasComponent<ParkingLotNeubauFaellig>(lot)) em.RemoveComponent<ParkingLotNeubauFaellig>(lot);
            Melde($"Schnappschuss Lot {lot.Index}: neu aufzubauende Kanten {kurse.Count}, erhaltene Kanten {bleibt.Count}, eigene Knoten {eigene}, erhaltene/fremde Anschlussknoten {fremde}, seitliche Anschluesse {anschluesse.Count}.");
        }

        /**
         * Zaehlt Eintraege des Rueckwegs, deren Prefab es nicht mehr gibt.
         * Dann kann der Rueckweg die alten Kanten nie bauen (Issue #17).
         */
        internal static int FehlendePrefabs(EntityManager em, Entity lot)
        {
            bool Fehlt(Entity p) => !em.Exists(p) || !em.HasComponent<PrefabData>(p);
            int n = 0;
            if (em.HasBuffer<ParkingLotRueckwegkurs>(lot))
                foreach (var k in em.GetBuffer<ParkingLotRueckwegkurs>(lot, true)) if (Fehlt(k.Prefab)) n++;
            if (em.HasBuffer<ParkingLotRueckweganschluss>(lot))
                foreach (var a in em.GetBuffer<ParkingLotRueckweganschluss>(lot, true)) if (Fehlt(a.Prefab)) n++;
            if (em.HasBuffer<ParkingLotErhaltenerKurs>(lot))
                foreach (var k in em.GetBuffer<ParkingLotErhaltenerKurs>(lot, true)) if (Fehlt(k.Bestand.Prefab)) n++;
            return n;
        }

        internal static void VerwerfeOffeneSicherung(EntityManager em,Entity lot)
        {
            if (!em.Exists(lot) || !em.HasComponent<ParkingLotRueckwegsicherungOffen>(lot)) return;
            em.RemoveComponent<ParkingLotRueckwegkurs>(lot);
            em.RemoveComponent<ParkingLotRueckweganschluss>(lot);
            em.RemoveComponent<ParkingLotErhaltenerKurs>(lot);
            em.RemoveComponent<ParkingLotRueckwegsicherungOffen>(lot);
            Melde($"Unvollstaendige Sicherung Lot {lot.Index} verworfen; Abriss hatte noch nicht begonnen.");
        }

        internal static Entity[] Besitzteile(EntityManager em,Entity lot)
        {
            var teile = new HashSet<Entity>();
            var owner = new[] {lot,em.HasComponent<ParkingLotCarrierReference>(lot)
                ? em.GetComponentData<ParkingLotCarrierReference>(lot).Carrier : Entity.Null};
            foreach (var o in owner)
                if (em.HasBuffer<SubNet>(o))
                    foreach (var s in em.GetBuffer<SubNet>(o,true))
                    {
                        var e = s.m_SubNet;
                        if (!Lebt(em,e)) continue;
                        teile.Add(e);
                        if (!em.HasComponent<Edge>(e)) continue;
                        var k = em.GetComponentData<Edge>(e);
                        teile.Add(k.m_Start); teile.Add(k.m_End);
                        if (em.HasBuffer<ConnectedNode>(e))
                            foreach (var n in em.GetBuffer<ConnectedNode>(e,true)) teile.Add(n.m_Node);
                    }
            var kopie = new Entity[teile.Count]; teile.CopyTo(kopie); return kopie;
        }

        internal static int Erzeuge(EntityManager em, Entity lot, int auftrag, int anfang = 0, int anzahl = int.MaxValue)
            => Erzeuge(em,lot,auftrag,anfang,anzahl,double.PositiveInfinity,out _);

        internal static int Erzeuge(EntityManager em, Entity lot, int auftrag, int anfang, int anzahl,
            double budgetMs, out int weiter)
        {
            weiter = anfang;
            var uhr = System.Diagnostics.Stopwatch.StartNew();
            if (!em.HasBuffer<ParkingLotRueckwegkurs>(lot)) return 0;
            // Strukturveraenderungen invalidieren DynamicBuffer: ZUERST kopieren.
            var kopie = em.GetBuffer<ParkingLotRueckwegkurs>(lot, true).ToNativeArray(Allocator.Temp);
            try
            {
                // Erst ALLE Originale validieren, dann schreiben. Ein fehlender
                // Anschluss darf keinen halb erzeugten Rueckweg hinterlassen.
                foreach (var alt in kopie)
                {
                    if (!Lebt(em,alt.Besitzer) || !em.Exists(alt.Prefab))
                        throw new InvalidOperationException("Gesicherter Besitzer/Prefab fehlt.");
                    foreach (var n in new[] { alt.Start, alt.Ende })
                        if (n != Entity.Null && !Lebt(em,n))
                            throw new InvalidOperationException("Gesicherter Anschlussknoten fehlt: " + n);
                }
                if (em.HasBuffer<ParkingLotRueckweganschluss>(lot))
                    foreach (var a in em.GetBuffer<ParkingLotRueckweganschluss>(lot,true))
                        if (a.Knoten != Entity.Null && !Lebt(em,a.Knoten))
                            throw new InvalidOperationException("Gesicherter seitlicher Anschluss fehlt: " + a.Knoten);
                var eigeneKanten = EigeneKanten(em,kopie.ToArray());
                var benutzt = new HashSet<Entity>();
                int erzeugt = 0;
                for (int i = anfang; i < kopie.Length && i - anfang < anzahl; i++)
                {
                    if (i > anfang && uhr.Elapsed.TotalMilliseconds >= budgetMs) break;
                    weiter = i+1;
                    var alt = Originalkurs(em,kopie[i]);
                    // Auch nach einem unterbrochenen Rueckweg: vorhandene
                    // exakte Ergebnisse nicht ein zweites Mal erzeugen.
                    if (FindeKurs(em,eigeneKanten,alt,benutzt,out _) != null) continue;
                    // Ein vorhandener falscher/teilweiser Kurs darf nach Laden
                    // keinen deckungsgleichen zweiten Satz bekommen.
                    if (Belegt(em,eigeneKanten,alt))
                    { Melde($"Rueckweg Originalkante {alt.Kante}: XZ bereits belegt; 0 weitere Definitionen fuer diesen Kurs."); continue; }
                    var d = em.CreateEntity();
                    em.AddComponentData(d, new ParkingLotAuftragsdefinition { Auftrag = auftrag });
                    em.AddComponentData(d, new CreationDefinition { m_Prefab = alt.Prefab,
                        m_Owner = alt.Besitzer, m_Flags = CreationFlags.Permanent,
                        m_RandomSeed = math.max(1, d.Index) });
                    em.AddComponentData(d, Eingabekurs(alt));
                    em.SetComponentData(d, BindeRueckwegknoten(em,Eingabekurs(alt),eigeneKanten,alt.Besitzer,alt.Prefab));
                    if (alt.HatUpgrade) em.AddComponentData(d, alt.Upgrade);
                    ParkingLotToolSystem.NurDiesesBild(em, d);
                    ParkingLotDauerkursSystem.Plane(em,d);
                    em.AddComponent<Updated>(d);
                    em.World.GetOrCreateSystemManaged<ParkingLotDefinitionsendeSystem>().Merke(alt.Besitzer,auftrag);
                    erzeugt++;
                }
                return erzeugt;
            }
            finally { kopie.Dispose(); }
        }

        internal sealed class Prueflauf : IDisposable
        {
            private readonly HintergrundPortion _portion;
            internal int Ist, Soll;
            internal bool Richtig;
            internal bool Fertig => _portion.Fertig;
            internal Prueflauf(EntityManager em,Entity lot,bool anschluesse=true,bool diagnose=false,HintergrundTempo tempo=null)
            { _portion = new HintergrundPortion(Pruefschritte(em,lot,this,anschluesse,diagnose),tempo); }
            internal void Weiter()
            { var uhr = System.Diagnostics.Stopwatch.StartNew(); _portion.Weiter(() => uhr.Elapsed.TotalMilliseconds); }
            public void Dispose() { _portion.Dispose(); }
        }
        internal static bool Pruefe(EntityManager em, Entity lot, out int ist, out int soll, bool anschluesse = true, bool diagnose = false)
        {
            using var lauf = new Prueflauf(em,lot,anschluesse,diagnose);
            while (!lauf.Fertig) lauf.Weiter();
            ist = lauf.Ist; soll = lauf.Soll;
            return lauf.Richtig;
        }
        private static IEnumerable<int> Pruefschritte(EntityManager em,Entity lot,Prueflauf lauf,bool anschluesse,bool diagnose)
{
            lauf.Ist = lauf.Soll = 0;
            if (!em.Exists(lot) || !em.HasBuffer<ParkingLotRueckwegkurs>(lot)) yield break;
            ParkingLotRueckwegkurs[] kurse;
            using (var a = em.GetBuffer<ParkingLotRueckwegkurs>(lot,true).ToNativeArray(Allocator.Temp)) kurse = a.ToArray();
            var eigeneKanten = EigeneKanten(em,kurse);
            var benutzt = new HashSet<Entity>();
            int index = -1, fehlmeldungen = 0;
            foreach (var gespeichert in kurse)
            {
                var alt = Originalkurs(em,gespeichert);
                index++;
                yield return 0;
                lauf.Soll++;
                var teile = FindeKurs(em,eigeneKanten,alt,benutzt,out string grund);
                if (teile != null && anschluesse && !SeitenanschluesseDa(em,lot,index,teile))
                { grund = "seitlicher Anschluss/Parameter nicht beidseitig vorhanden"; teile = null; }
                if (teile == null)
                {
                    if (diagnose && fehlmeldungen++ < 4)
                    {
                        int nr = index;
                        ParkingLotHintergrundDiagnose.Sicher(() => ParkingLotHintergrundDiagnose.Kurs(em,
                            em.World.GetExistingSystemManaged<PrefabSystem>(),"Rueckweg",nr,
                            "Originalkante " + alt.Kante,alt.Prefab,alt.Besitzer,alt.Kurs.m_Curve,grund,alt.Start,alt.Ende,
                            (e,um) =>
                            {
                                var u = alt.Upgrade;
                                if (um) u.m_Flags = new CompositionFlags(u.m_Flags.m_General,u.m_Flags.m_Right,u.m_Flags.m_Left);
                                return $"Upgrade Soll vorhanden={alt.HatUpgrade}, Flags={u.m_Flags}; "
                                    + $"Ist vorhanden={em.HasComponent<Upgraded>(e)}, "
                                    + $"gleich={em.HasComponent<Upgraded>(e) == alt.HatUpgrade && (!alt.HatUpgrade || em.GetComponentData<Upgraded>(e).Equals(u))}";
                            },ParkingLotKursabgleich.InnenhoeheVanilla(em,alt.Prefab,Eingabekurs(alt),alt.Besitzer)
                                && ParkingLotKursabgleich.InnenhoeheNachGeneratoren(em,alt.Prefab,Eingabekurs(alt),alt.Besitzer)));
                    }
                    continue;
                }
                lauf.Ist++;
            }
            if (em.HasBuffer<ParkingLotErhaltenerKurs>(lot))
            {
                ParkingLotErhaltenerKurs[] bleibt;
                using (var a = em.GetBuffer<ParkingLotErhaltenerKurs>(lot,true).ToNativeArray(Allocator.Temp)) bleibt = a.ToArray();
                int erhalten = 0;
                foreach (var r in bleibt)
                {
                    lauf.Soll++;
                    if (ParkingLotNetzerhalt.Pruefe(em,r.Bestand)) { lauf.Ist++; erhalten++; }
                    else if (diagnose) Melde($"Rueckweg ERHALTEN FEHLT: Originalkante {r.Bestand.Kante}, Prefab {r.Bestand.Prefab}, Knoten {r.Bestand.Start}/{r.Bestand.Ende}; 0 Neubau dieser Kante.");
                    yield return 0;
                }
                Melde($"Rueckweg: erhaltene Originalkanten {erhalten}/{bleibt.Length}, 0 Definitionen dafuer.");
            }
            lauf.Richtig = lauf.Ist == lauf.Soll;
        }

        private static List<ParkingLotKursabgleich.Teil> FindeKurs(EntityManager em, IEnumerable<Entity> kanten,
            ParkingLotRueckwegkurs alt, HashSet<Entity> benutzt, out string grund)
        {
            alt = Originalkurs(em,alt);
            var teile = ParkingLotKursabgleich.Sammle(em,kanten,alt.Kurs.m_Curve,alt.Prefab,alt.Besitzer,benutzt,
                ParkingLotKursabgleich.InnenhoeheVanilla(em,alt.Prefab,Eingabekurs(alt),alt.Besitzer)
                    && ParkingLotKursabgleich.InnenhoeheNachGeneratoren(em,alt.Prefab,Eingabekurs(alt),alt.Besitzer));
            int lage = teile.Count;
            teile.RemoveAll(p =>
            {
                var upgrade = alt.Upgrade;
                if (p.Umgekehrt) upgrade.m_Flags = new CompositionFlags(upgrade.m_Flags.m_General,
                    upgrade.m_Flags.m_Right,upgrade.m_Flags.m_Left);
                return em.HasComponent<Upgraded>(p.Kante) != alt.HatUpgrade
                    || alt.HatUpgrade && !em.GetComponentData<Upgraded>(p.Kante).Equals(upgrade)
                    || !Verbunden(em,p.Start,p.Kante) || !Verbunden(em,p.Ende,p.Kante);
            });
            grund = $"keine volle verbundene Kurvenkette; Lage/Prefab/Owner {lage}, davon Upgrade/ConnectedEdge gueltig {teile.Count}";
            if (!ParkingLotKursabgleich.Kette(teile,out var start,out var ende)) return null;
            if (alt.Start != Entity.Null && alt.Start != start || alt.Ende != Entity.Null && alt.Ende != ende)
            { grund = $"Originalknoten verschieden: Ist {start}/{ende}, Soll {alt.Start}/{alt.Ende}"; return null; }
            foreach (var p in teile) benutzt.Add(p.Kante);
            grund = "vollstaendig"; return teile;
        }

        private static List<Entity> EigeneKanten(EntityManager em, ParkingLotRueckwegkurs[] kurse)
        {
            var besitzer = new HashSet<Entity>();
            foreach (var k in kurse) besitzer.Add(k.Besitzer);
            var eigene = new List<Entity>();
            var gesehen = new HashSet<Entity>();
            foreach (var o in besitzer)
                if (em.HasBuffer<SubNet>(o))
                    foreach (var n in em.GetBuffer<SubNet>(o,true))
                    {
                        var e = n.m_SubNet;
                        if (gesehen.Add(e) && Lebt(em,e) && !em.HasComponent<Temp>(e) && em.HasComponent<Edge>(e)
                            && em.HasComponent<Curve>(e) && em.HasComponent<PrefabRef>(e)
                            && em.HasComponent<Owner>(e) && besitzer.Contains(em.GetComponentData<Owner>(e).m_Owner)) eigene.Add(e);
                    }
            return eigene;
        }

        private static NetCourse BindeRueckwegknoten(EntityManager em,NetCourse kurs,List<Entity> kanten,Entity owner,Entity prefab)
        {
            kurs.m_StartPosition = Binde(kurs.m_StartPosition);
            kurs.m_EndPosition = Binde(kurs.m_EndPosition);
            var c = HintergrundDauerkurs.BindeEnden(ParkingLotKursabgleich.Form(kurs.m_Curve),
                kurs.m_StartPosition.m_Position,kurs.m_EndPosition.m_Position);
            kurs.m_Curve = new Bezier4x3(c.A,c.B,c.C,c.D);
            kurs.m_Length = MathUtils.Length(kurs.m_Curve);
            return kurs;
            CoursePos Binde(CoursePos pos)
            {
                if (pos.m_Entity != Entity.Null)
                {
                    // 22:03: derselbe Originalknoten wurde mit zwei alten
                    // Kurvenhoehen (122,684/122,844 m) angefordert. Die Node-ID
                    // und ihre aktuelle Lage gehoeren gemeinsam zur Definition.
                    pos.m_Position = em.GetComponentData<Game.Net.Node>(pos.m_Entity).m_Position;
                    return pos;
                }
                foreach (var e in kanten)
                {
                    var k = em.GetComponentData<Edge>(e);
                    foreach (var n in new[] {k.m_Start,k.m_End})
                        if (Lebt(em,n) && em.HasComponent<Game.Net.Node>(n) && em.HasComponent<Owner>(n)
                            && em.GetComponentData<Owner>(n).m_Owner == owner
                            && math.distance(em.GetComponentData<Game.Net.Node>(n).m_Position,pos.m_Position) <= .05f
                            && Anschliessbar(n))
                        { pos.m_Entity = n; pos.m_Position = em.GetComponentData<Game.Net.Node>(n).m_Position; return pos; }
                }
                return pos;
            }
            /*
             * 2026-10-15: PLT legt Wege verschiedener Typen an dieselben
             * Punkte. Ohne diese Pruefung band der Rueckweg eine Kante an den
             * erstbesten eigenen Knoten in 5 cm - auch einen mit
             * unvereinbaren Layern. `ParkingLotDauerkursSystem.Plane` lehnte
             * das danach ab ("unvereinbare Layer"), der Rueckweg hielt an,
             * und weil der Schnappschuss gleich blieb, nach JEDEM Laden
             * wieder. Hier gilt dieselbe Regel wie dort: CanConnect, oder der
             * erlaubte direkte Pathway-an-Road-Anschluss.
             */
            bool Anschliessbar(Entity n)
            {
                if (!em.HasComponent<PrefabRef>(n) || !em.HasComponent<NetData>(prefab)) return false;
                var ziel = em.GetComponentData<PrefabRef>(n).m_Prefab;
                if (!em.HasComponent<NetData>(ziel)) return false;
                var eigen = em.GetComponentData<NetData>(prefab);
                var anderer = em.GetComponentData<NetData>(ziel);
                if (NetUtils.CanConnect(eigen, anderer)) return true;
                return em.HasComponent<PathwayData>(prefab)
                    && (!em.HasBuffer<AuxiliaryNet>(prefab) || em.GetBuffer<AuxiliaryNet>(prefab,true).Length == 0)
                    && (anderer.m_RequiredLayers & Layer.Road) != 0;
            }
        }

        private static NetCourse Eingabekurs(ParkingLotRueckwegkurs alt)
        {
            var kurs = alt.Kurs;
            // Zulassung und Erzeugung muessen dieselben ParentMesh-Werte
            // sehen. Der gespeicherte Schnappschuss hat hier noch -1/-1;
            // ohne diesen gemeinsamen Adapter wuerde der Abgleich auch fuer
            // feste StraightEdges irrtuemlich Vanilla-Y freigeben.
            if (HintergrundKurspruefung.NullhoeheFixieren(kurs.m_Elevation))
            { kurs.m_StartPosition.m_ParentMesh = 0; kurs.m_EndPosition.m_ParentMesh = 0; }
            return kurs;
        }

        private static ParkingLotRueckwegkurs Originalkurs(EntityManager em, ParkingLotRueckwegkurs alt)
        {
            var kurs = alt.Kurs;
            var a = kurs.m_Curve.a; var d = kurs.m_Curve.d;
            if (alt.Start != Entity.Null && Lebt(em,alt.Start)) a = em.GetComponentData<Game.Net.Node>(alt.Start).m_Position;
            if (alt.Ende != Entity.Null && Lebt(em,alt.Ende)) d = em.GetComponentData<Game.Net.Node>(alt.Ende).m_Position;
            var c = HintergrundDauerkurs.BindeEnden(ParkingLotKursabgleich.Form(kurs.m_Curve),a,d);
            kurs.m_Curve = new Bezier4x3(c.A,c.B,c.C,c.D);
            kurs.m_StartPosition.m_Position = a; kurs.m_EndPosition.m_Position = d;
            kurs.m_Length = MathUtils.Length(kurs.m_Curve);
            alt.Kurs = kurs;
            return alt;
        }

        private static bool Belegt(EntityManager em, IEnumerable<Entity> kanten, ParkingLotRueckwegkurs alt)
        {
            Bezier4x3 Flach(Bezier4x3 c) => new Bezier4x3(new float3(c.a.x,0,c.a.z),new float3(c.b.x,0,c.b.z),
                new float3(c.c.x,0,c.c.z),new float3(c.d.x,0,c.d.z));
            foreach (var e in kanten)
                if (em.GetComponentData<Owner>(e).m_Owner == alt.Besitzer && em.GetComponentData<PrefabRef>(e).m_Prefab == alt.Prefab
                    && ParkingLotKursabgleich.Abstand(Flach(alt.Kurs.m_Curve),Flach(em.GetComponentData<Curve>(e).m_Bezier),out var t) <= .05f
                    && math.abs(t.y-t.x) > 1e-6f) return true;
            return false;
        }

        internal static void StelleFlussWiederHer(EntityManager em, Entity lot, Action<Entity,Entity,Entity> verbinde)
        { foreach (int n in StelleFlussWiederHerSchritte(em,lot,verbinde)) { } }
        internal static IEnumerable<int> StelleFlussWiederHerSchritte(EntityManager em, Entity lot, Action<Entity,Entity,Entity> verbinde)
        {
            if (!em.HasBuffer<ParkingLotRueckweganschluss>(lot)) yield break;
            ParkingLotRueckwegkurs[] kurse;
            ParkingLotRueckweganschluss[] seiten;
            using (var a = em.GetBuffer<ParkingLotRueckwegkurs>(lot,true).ToNativeArray(Allocator.Temp)) kurse = a.ToArray();
            using (var a = em.GetBuffer<ParkingLotRueckweganschluss>(lot,true).ToNativeArray(Allocator.Temp)) seiten = a.ToArray();
            var eigeneKanten = EigeneKanten(em,kurse);
            var benutzt = new HashSet<Entity>();
            var paare = new List<(Entity Kante,Entity Knoten,Entity Prefab)>();
            for (int i = 0; i < kurse.Length; i++)
            {
                yield return 0;
                var teile = FindeKurs(em,eigeneKanten,kurse[i],benutzt,out _);
                if (teile == null) continue;
                foreach (var a in seiten)
                    if (a.Kursindex == i)
                        foreach (var p in teile)
                            if (Enthaelt(p,a.Lage)) paare.Add((p.Kante,Anschlussknoten(em,a),a.Prefab));
            }
            // Der gemeinsame Graphhelfer kann Strukturveraenderungen ausloesen;
            // die physische Netzpruefung ist davor vollstaendig abgeschlossen.
            foreach (var p in paare) { verbinde(p.Kante,p.Knoten,p.Prefab); yield return 0; }
        }

        private static bool Enthaelt(ParkingLotKursabgleich.Teil p, float lage)
            => HintergrundAbgleich.Teilparameter(p.Bereich,lage,out _);

        private static bool SeitenanschluesseDa(EntityManager em, Entity lot, int index, List<ParkingLotKursabgleich.Teil> teile)
        {
            if (!em.HasBuffer<ParkingLotRueckweganschluss>(lot)) return true;
            // Anschlussknoten erzeugt eine Query; deshalb keine geliehene
            // DynamicBuffer-Enumeration ueber diesen Aufruf hinweg halten.
            using var anschluesse = em.GetBuffer<ParkingLotRueckweganschluss>(lot,true).ToNativeArray(Allocator.Temp);
            foreach (var a in anschluesse)
            {
                if (a.Kursindex != index) continue;
                var knoten = Anschlussknoten(em,a);
                bool gefunden = false;
                foreach (var p in teile)
                {
                    if (!Enthaelt(p,a.Lage) || !Verbunden(em,knoten,p.Kante) || !em.HasBuffer<ConnectedNode>(p.Kante)) continue;
                    HintergrundAbgleich.Teilparameter(p.Bereich,a.Lage,out float t);
                    foreach (var n in em.GetBuffer<ConnectedNode>(p.Kante,true))
                        if (n.m_Node == knoten && math.abs(n.m_CurvePosition-t) <= .001f) gefunden = true;
                }
                if (!gefunden) return false;
            }
            return true;
        }
        internal static void MeldeAnschluesse(EntityManager em, Entity lot)
        {
            if (!em.HasBuffer<ParkingLotRueckweganschluss>(lot)) return;
            var knoten = new HashSet<Entity>();
            // Query-Erzeugung ist eine Strukturveraenderung: Buffer zuerst kopieren.
            using var anschluesse = em.GetBuffer<ParkingLotRueckweganschluss>(lot,true).ToNativeArray(Allocator.Temp);
            foreach (var a in anschluesse) knoten.Add(Anschlussknoten(em,a));
            foreach (var n in knoten) if (Lebt(em,n)) em.AddComponent<Updated>(n);
            var kanten = Besitzteile(em,lot);
            foreach (var e in kanten)
                if (Lebt(em,e) && em.HasComponent<Edge>(e) && em.HasComponent<Owner>(e)
                    && ParkingLotBesitz.GehoertZu(em,em.GetComponentData<Owner>(e).m_Owner,lot)) em.AddComponent<Updated>(e);
            Melde($"Rueckweg: {knoten.Count} seitliche Knoten mit eigenen Kanten bei Vanilla angemeldet; 0 direkte Netzeintraege.");
        }
        private static Entity Anschlussknoten(EntityManager em, ParkingLotRueckweganschluss a)
        {
            if (a.Knoten != Entity.Null) return a.Knoten;
            var lot = em.HasComponent<Owner>(a.Besitzer)
                ? em.GetComponentData<Owner>(a.Besitzer).m_Owner : a.Besitzer;
            var nodes = Besitzteile(em,lot);
            Entity gefunden = Entity.Null;
            foreach (var e in nodes)
                if (Lebt(em,e) && !em.HasComponent<Temp>(e) && em.HasComponent<Game.Net.Node>(e)
                    && em.HasComponent<PrefabRef>(e) && em.HasComponent<Owner>(e)
                    && em.GetComponentData<PrefabRef>(e).m_Prefab == a.Prefab
                    && em.GetComponentData<Owner>(e).m_Owner == a.Besitzer
                    && math.distance(em.GetComponentData<Game.Net.Node>(e).m_Position,a.Position) <= .05f)
                {
                    if (gefunden != Entity.Null) return Entity.Null;
                    gefunden = e;
                }
            return gefunden;
        }
        internal static void FuellTraeger(EntityManager em, Entity lot)
        { foreach (int n in FuellTraegerSchritte(em,lot)) { } }
        internal static IEnumerable<int> FuellTraegerSchritte(EntityManager em, Entity lot)
        {
            if (!em.HasComponent<ParkingLotCarrierReference>(lot)) yield break;
            var traeger = em.GetComponentData<ParkingLotCarrierReference>(lot).Carrier;
            if (!em.Exists(traeger) || !em.HasBuffer<SubNet>(traeger)) yield break;
            var kanten = Besitzteile(em,lot);
            foreach (var e in kanten)
            {
                yield return 0;
                if (Lebt(em,e) && em.HasComponent<Edge>(e) && em.HasComponent<Owner>(e)
                    && !em.HasComponent<Temp>(e) && ParkingLotBesitz.GehoertZu(em,em.GetComponentData<Owner>(e).m_Owner,lot))
                {
                    var b = em.GetBuffer<SubNet>(traeger);
                    bool da = false; foreach (var n in b) if (n.m_SubNet == e) da = true;
                    if (!da) b.Add(new SubNet(e));
                }
            }
        }
        private static bool Verbunden(EntityManager em, Entity node, Entity edge)
        {
            if (!em.HasBuffer<ConnectedEdge>(node)) return false;
            foreach (var c in em.GetBuffer<ConnectedEdge>(node, true)) if (c.m_Edge == edge) return true;
            return false;
        }
        internal static bool Lebt(EntityManager em, Entity e)
            => e != Entity.Null && em.Exists(e) && !em.HasComponent<Deleted>(e);
        internal static void Melde(string text)
        { var z = "PLT-Hintergrund: " + text; Mod.log.Info(z); ParkingGeometry.Live(z); }
    }
}
