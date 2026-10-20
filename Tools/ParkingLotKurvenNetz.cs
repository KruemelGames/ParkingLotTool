using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using ParkingLotTool.Geometry;
using Game.Tools;
using Unity.Entities;
using Unity.Mathematics;
using Kurven = ParkingLotTool.Geometry.Strassenkurve;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private readonly Dictionary<long,Kurven.Kante> _kurvenNetz=new Dictionary<long,Kurven.Kante>();
        private readonly Dictionary<long,Kurven.Knoten> _kurvenKnoten=new Dictionary<long,Kurven.Knoten>();
        private readonly List<long> _kurvenSuche=new List<long>();
        private static long KurvenId(Entity e) => ((long)e.Index<<32)|(uint)e.Version;
        private static Entity KurvenEntity(long id) => new Entity {Index=(int)(id>>32),Version=(int)id};
        private static Kurven.Bezier Kurvenform(Bezier4x3 c) => new Kurven.Bezier(c.a,c.b,c.c,c.d);

        private Kurven.Kante LeseKurvenkante(Entity e)
        {
            if(!IstKurvenstrasse(e) || !EntityManager.HasComponent<EdgeGeometry>(e)
                || !EntityManager.HasComponent<Game.Net.Curve>(e))return null;
            var edge=EntityManager.GetComponentData<Game.Net.Edge>(e);
            if(!EntityManager.HasComponent<Game.Net.Node>(edge.m_Start))return null;
            var start=EntityManager.GetComponentData<Game.Net.Node>(edge.m_Start).m_Position;
            var g=EntityManager.GetComponentData<EdgeGeometry>(e);
            var comp=EntityManager.HasComponent<Composition>(e)?EntityManager.GetComponentData<Composition>(e):default;
            var flags=Kurvenflags(comp.m_Edge);
            bool hoehe=(flags & (CompositionFlags.General.Elevated|CompositionFlags.General.Tunnel))!=0;
            var c=EntityManager.GetComponentData<Game.Net.Curve>(e).m_Bezier;
            var terrain=_terrainSystem.GetHeightData();
            for(int i=0;i<=4;i++)
            {
                var p=MathUtils.Position(c,i/4f);
                if(!math.all(math.isfinite(p)) || Math.Abs(p.y-TerrainUtils.SampleHeight(ref terrain,p))>Kurven.Gelaendeabstand)
                    hoehe=true;
            }
            var links = Kurven.Ordne(Kurvenform(g.m_Start.m_Left), Kurvenform(g.m_End.m_Left), start);
            var rechts = Kurven.Ordne(Kurvenform(g.m_Start.m_Right), Kurvenform(g.m_End.m_Right), start);
            if (EntityManager.HasComponent<StartNodeGeometry>(e))
            {
                var knoten = EntityManager.GetComponentData<StartNodeGeometry>(e).m_Geometry;
                if (GlatterKurvenknoten(e, edge.m_Start, knoten))
                {
                    links = MitKnotenhaelfte(links, knoten, true);
                    rechts = MitKnotenhaelfte(rechts, knoten, true);
                }
            }
            if (EntityManager.HasComponent<EndNodeGeometry>(e))
            {
                var knoten = EntityManager.GetComponentData<EndNodeGeometry>(e).m_Geometry;
                if (GlatterKurvenknoten(e, edge.m_End, knoten))
                {
                    links = MitKnotenhaelfte(links, knoten, false);
                    rechts = MitKnotenhaelfte(rechts, knoten, false);
                }
            }
            return new Kurven.Kante {Id=KurvenId(e),Start=KurvenId(edge.m_Start),Ende=KurvenId(edge.m_End),Hoehe=hoehe,
                Mittellinie = Kurvenform(c), HatMittellinie = true, Links = links, Rechts = rechts};
        }

        /**
         * Glatter Durchlaufknoten: genau zwei Strassen, deren Mittellinien ohne
         * Knick ineinander uebergehen (gleiche Grenze wie PruefeFortsetzung).
         * Dort ist die native Knotenkurve ein Stueck desselben Bogens - an
         * Ecken und Kreuzungen bleibt sie die Abrundung, die man begradigt.
         */
        private bool GlatterKurvenknoten(Entity strasse, Entity knoten, EdgeNodeGeometry geometrie)
        {
            if (geometrie.m_MiddleRadius > 0f || !EntityManager.HasBuffer<ConnectedEdge>(knoten)
                || !EntityManager.HasComponent<Game.Net.Curve>(strasse)) return false;
            var buffer = EntityManager.GetBuffer<ConnectedEdge>(knoten, true);
            Entity andere = Entity.Null;
            int grad = 0;
            for (int i = 0; i < buffer.Length; i++)
            {
                var e = buffer[i].m_Edge;
                if (!IstKurvenstrasse(e)) continue;
                grad++;
                if (e != strasse) andere = e;
            }
            if (grad != 2 || andere == Entity.Null || !EntityManager.HasComponent<Game.Net.Curve>(andere)) return false;
            var ea = EntityManager.GetComponentData<Game.Net.Edge>(strasse);
            var eb = EntityManager.GetComponentData<Game.Net.Edge>(andere);
            if (ea.m_Start == ea.m_End || eb.m_Start == eb.m_End) return false;
            var ca = EntityManager.GetComponentData<Game.Net.Curve>(strasse).m_Bezier;
            var cb = EntityManager.GetComponentData<Game.Net.Curve>(andere).m_Bezier;
            var hinein = ea.m_End == knoten ? ca.d - ca.c : ca.a - ca.b;
            var hinaus = eb.m_Start == knoten ? cb.b - cb.a : cb.c - cb.d;
            if (math.lengthsq(hinein.xz) < 1e-8f || math.lengthsq(hinaus.xz) < 1e-8f) return false;
            float cos = math.dot(math.normalize(hinein.xz), math.normalize(hinaus.xz));
            return cos >= Math.Cos(Kurven.Fortsetzungswinkel);
        }

        // Haengt die passende Knotenhaelfte (Aussenrand) vorne bzw. hinten an den Randzug.
        private static Kurven.Teil[] MitKnotenhaelfte(Kurven.Teil[] teile, EdgeNodeGeometry knoten, bool amStart)
        {
            if (teile == null || teile.Length == 0) return teile;
            var anschluss = amStart ? teile[0].Kurve.A : teile[teile.Length - 1].Kurve.D;
            var kandidaten = new[] { Kurvenform(knoten.m_Left.m_Left), Kurvenform(knoten.m_Right.m_Right) };
            Kurven.Teil? fund = null;
            foreach (var c in kandidaten)
            {
                if (!c.Endlich || math.distance(c.A, c.D) < .01f) continue;
                // Am Start muss die Haelfte am Randzug ENDEN, am Ende dort BEGINNEN.
                bool passt = math.distance(amStart ? c.D : c.A, anschluss) <= Kurven.Anschlussluecke;
                bool verkehrt = math.distance(amStart ? c.A : c.D, anschluss) <= Kurven.Anschlussluecke;
                if (!passt && !verkehrt) continue;
                if (fund != null) return teile;
                fund = new Kurven.Teil { Kurve = passt ? c : c.Umgekehrt(), Quelle = amStart ? 2 : 3, Umgekehrt = !passt };
            }
            if (fund == null) return teile;
            var neu = new Kurven.Teil[teile.Length + 1];
            if (amStart) { neu[0] = fund.Value; Array.Copy(teile, 0, neu, 1, teile.Length); }
            else { Array.Copy(teile, neu, teile.Length); neu[teile.Length] = fund.Value; }
            return neu;
        }
        private bool IstKurvenstrasse(Entity e) => e!=Entity.Null && EntityManager.Exists(e)
            && EntityManager.HasComponent<Game.Net.Edge>(e) && EntityManager.HasComponent<Road>(e)
            && !EntityManager.HasComponent<Owner>(e) && !EntityManager.HasComponent<Temp>(e)
            && !EntityManager.HasComponent<Deleted>(e);
        private CompositionFlags.General Kurvenflags(Entity e) => e!=Entity.Null
            && EntityManager.HasComponent<NetCompositionData>(e)
                ? EntityManager.GetComponentData<NetCompositionData>(e).m_Flags.m_General : default;

        private Kurven.Knoten LeseKurvenknoten(Entity e)
        {
            long id=KurvenId(e);if(_kurvenKnoten.TryGetValue(id,out var alt))return alt;
            var kanten=new List<long>();
            var richtungen=new List<float2>();
            if(EntityManager.Exists(e) && EntityManager.HasBuffer<ConnectedEdge>(e))
            {
                var buffer=EntityManager.GetBuffer<ConnectedEdge>(e,true);
                for(int i=0;i<buffer.Length;i++)if(IstKurvenstrasse(buffer[i].m_Edge))
                {
                    kanten.Add(KurvenId(buffer[i].m_Edge));
                    richtungen.Add(Abgangsrichtung(buffer[i].m_Edge,e));
                }
            }
            var lage=EntityManager.HasComponent<Game.Net.Node>(e)?EntityManager.GetComponentData<Game.Net.Node>(e).m_Position:default;
            var k=new Kurven.Knoten {Id=id,Kanten=kanten.ToArray(),Kreuzung=kanten.Count>2,
                Lage=lage,Richtungen=richtungen.ToArray()};
            _kurvenKnoten.Add(id,k);return k;
        }
        // Richtung, in der die Strasse den Knoten verlaesst (Mittellinie, XZ); 0 = unbekannt.
        private float2 Abgangsrichtung(Entity strasse,Entity knoten)
        {
            if(!EntityManager.HasComponent<Game.Net.Curve>(strasse))return default;
            var edge=EntityManager.GetComponentData<Game.Net.Edge>(strasse);
            var c=EntityManager.GetComponentData<Game.Net.Curve>(strasse).m_Bezier;
            bool start=edge.m_Start==knoten;
            if(!start && edge.m_End!=knoten)return default;
            var r=(start?c.b-c.a:c.c-c.d).xz;
            if(math.lengthsq(r)<1e-8f)r=(start?c.d-c.a:c.a-c.d).xz;
            return math.lengthsq(r)<1e-8f?default:math.normalize(r);
        }
        private void ErgaenzeKurvenknoten(Kurven.Knoten n,Entity road,bool start)
        {
            var comp=EntityManager.HasComponent<Composition>(road)?EntityManager.GetComponentData<Composition>(road):default;
            var flags=Kurvenflags(start?comp.m_StartNode:comp.m_EndNode);
            n.Kreuzung|=(flags & CompositionFlags.General.Intersection)!=0;
            n.Kreisverkehr|=(flags & CompositionFlags.General.Roundabout)!=0;
            n.Hoehe|=(flags & (CompositionFlags.General.Elevated|CompositionFlags.General.Tunnel))!=0;
            EdgeNodeGeometry g;
            if(start && EntityManager.HasComponent<StartNodeGeometry>(road))g=EntityManager.GetComponentData<StartNodeGeometry>(road).m_Geometry;
            else if(!start && EntityManager.HasComponent<EndNodeGeometry>(road))g=EntityManager.GetComponentData<EndNodeGeometry>(road).m_Geometry;
            else return;
            n.Kreisverkehr|=g.m_MiddleRadius>0;
            StrassenkurvenNetz.ErgaenzeAngebot(n,Kurvenform(g.m_Left.m_Left),Kurvenform(g.m_Left.m_Right),
                Kurvenform(g.m_Right.m_Left),Kurvenform(g.m_Right.m_Right));
        }
        private Kurven.Grund KurvenKnotengrund(Entity road,float3 punkt)
        {
            if(!IstKurvenstrasse(road))return Kurven.Grund.KeinFang;
            var e=EntityManager.GetComponentData<Game.Net.Edge>(road);
            if(!EntityManager.HasComponent<Game.Net.Node>(e.m_Start) || !EntityManager.HasComponent<Game.Net.Node>(e.m_End))return Kurven.Grund.Luecke;
            var a=EntityManager.GetComponentData<Game.Net.Node>(e.m_Start).m_Position;
            var b=EntityManager.GetComponentData<Game.Net.Node>(e.m_End).m_Position;
            bool start=math.distance(a,punkt)<math.distance(b,punkt);
            var comp=EntityManager.HasComponent<Composition>(road)?EntityManager.GetComponentData<Composition>(road):default;
            var flags=Kurvenflags(start?comp.m_StartNode:comp.m_EndNode);
            if((flags & CompositionFlags.General.Roundabout)!=0)return Kurven.Grund.Kreisverkehr;
            if(start && EntityManager.HasComponent<StartNodeGeometry>(road)
                && EntityManager.GetComponentData<StartNodeGeometry>(road).m_Geometry.m_MiddleRadius>0)return Kurven.Grund.Kreisverkehr;
            if(!start && EntityManager.HasComponent<EndNodeGeometry>(road)
                && EntityManager.GetComponentData<EndNodeGeometry>(road).m_Geometry.m_MiddleRadius>0)return Kurven.Grund.Kreisverkehr;
            if((flags & (CompositionFlags.General.Elevated|CompositionFlags.General.Tunnel))!=0)return Kurven.Grund.Hoehe;
            if((flags & CompositionFlags.General.Intersection)!=0)return Kurven.Grund.Kreuzung;
            var node=start?e.m_Start:e.m_End;int grad=0;
            if(EntityManager.HasBuffer<ConnectedEdge>(node))
            {
                var buffer=EntityManager.GetBuffer<ConnectedEdge>(node,true);
                for(int i=0;i<buffer.Length;i++)if(IstKurvenstrasse(buffer[i].m_Edge))grad++;
            }
            return grad>2?Kurven.Grund.Kreuzung:Kurven.Grund.Tangente;
        }
        private void LadeKurvennetz(Kurven.Fang a,Kurven.Fang b)
        {
            StrassenkurvenNetz.Lade(a,b,_kurvenNetz,_kurvenKnoten,_kurvenSuche,
                id=>LeseKurvenkante(KurvenEntity(id)),ErgaenzeKurvenkante);
        }
        private void ErgaenzeKurvenkante(Kurven.Kante k)
        {
            var road=KurvenEntity(k.Id);
            ErgaenzeKurvenknoten(LeseKurvenknoten(KurvenEntity(k.Start)),road,true);
            ErgaenzeKurvenknoten(LeseKurvenknoten(KurvenEntity(k.Ende)),road,false);
        }
    }
}
