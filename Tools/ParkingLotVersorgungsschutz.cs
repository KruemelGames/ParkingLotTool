using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.Common;
using Game.Net;
using Game.Simulation;
using Unity.Entities;
using UnityEngine;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private string _versorgungSchutzDatei;

        private bool VersorgungsentityLebt(Entity entity)
            => entity != Entity.Null && EntityManager.Exists(entity)
                && !EntityManager.HasComponent<Deleted>(entity)
                && !EntityManager.HasComponent<Game.Tools.Temp>(entity);

        // Vor CreateFlowEdge pruefen: beide Enden brauchen lebende Flussknoten
        // und die Puffer, in die CS2 die neue Kante eintraegt. Keine Reparatur
        // fremder Graphen; ungueltige Eingaben werden ausgelassen und benannt.
        private bool VersorgungsflussBereit(Entity a, Entity b, string art)
        {
            if (a != b && VersorgungsentityLebt(a) && VersorgungsentityLebt(b)
                && EntityManager.HasBuffer<ConnectedFlowEdge>(a)
                && EntityManager.HasBuffer<ConnectedFlowEdge>(b)) return true;
            Mod.log.Warn($"PLT-Versorgungsschutz: {art}-Flusskante "
                + $"{a} -> {b} NICHT angelegt: ungueltiger, geloeschter, "
                + "temporaerer oder pufferloser Endknoten / Selbstverbindung.");
            ProtokolliereVersorgungsschutz("FLUSSKANTE-BLOCKIERT " + art);
            return false;
        }

        // Ein BEGIN wird vor dem Lesen der ECS-Daten auf die Platte gespült.
        // Fehlt END, ist auch ein Abbruch innerhalb der Diagnose erkennbar.
        // Kein Frame-Logging: nur feste Uebergaenge eines Editierumbaus.
        private void ProtokolliereVersorgungsschutz(string phase)
        {
            try
            {
                if (_versorgungSchutzDatei == null)
                {
                    var dir = Path.Combine(Application.persistentDataPath, "Logs");
                    Directory.CreateDirectory(dir);
                    _versorgungSchutzDatei = Path.Combine(dir,
                        "ParkingLotTool-utilityguard-"
                        + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")
                        + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".log");
                }
                SchreibeVersorgungsschutz($"BEGIN {phase} Zeit={DateTime.Now:O} "
                    + $"Frame={UnityEngine.Time.frameCount} Knoten={_versorgungKnotenPruefung.Count} "
                    + $"Strassen={_versorgungNeueKanten.Count} Build=Schutz-20260906" + Environment.NewLine);
                var text = new StringBuilder();
                var gesehen = new HashSet<Entity>();
                foreach (var n in _versorgungKnotenPruefung)
                    BeschreibeVersorgungsentity(n, text, gesehen);
                foreach (var e in _versorgungNeueKanten)
                    BeschreibeVersorgungsentity(e, text, gesehen);
                text.AppendLine("END " + phase);
                SchreibeVersorgungsschutz(text.ToString());
                Mod.log.Info($"PLT-Versorgungsschutz: {phase} dauerhaft gespeichert: "
                    + _versorgungSchutzDatei);
            }
            catch (Exception ex)
            {
                // Ein Dateifehler hebt die Unterdrueckung von Updated nicht auf.
                // Native Abstuerze werden ausdruecklich nicht per catch behandelt.
                Mod.log.Warn("PLT-Versorgungsschutz: Diagnose unvollstaendig: " + ex);
            }
        }

        private void SchreibeVersorgungsschutz(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            using (var stream = new FileStream(_versorgungSchutzDatei,
                FileMode.Append, FileAccess.Write, FileShare.Read,
                4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private string Versorgungszustand(Entity e)
        {
            if (e == Entity.Null || !EntityManager.Exists(e)) return "FEHLT";
            return "lebt=" + VersorgungsentityLebt(e)
                + " Deleted=" + EntityManager.HasComponent<Deleted>(e)
                + " Temp=" + EntityManager.HasComponent<Game.Tools.Temp>(e)
                + " Updated=" + EntityManager.HasComponent<Updated>(e);
        }

        private void BeschreibeVersorgungsentity(Entity e, StringBuilder text,
            HashSet<Entity> gesehen)
        {
            if (!gesehen.Add(e)) return;
            text.AppendLine($"NETZ {e}: {Versorgungszustand(e)}");
            if (!VersorgungsentityLebt(e)) return;
            if (EntityManager.HasComponent<Edge>(e))
            {
                var edge = EntityManager.GetComponentData<Edge>(e);
                text.AppendLine($"  Enden {edge.m_Start} [{Versorgungszustand(edge.m_Start)}]"
                    + $" -> {edge.m_End} [{Versorgungszustand(edge.m_End)}]");
            }
            if (EntityManager.HasBuffer<ConnectedEdge>(e))
            {
                var edges = EntityManager.GetBuffer<ConnectedEdge>(e, true);
                var count = Math.Min(edges.Length, 128);
                text.AppendLine($"  ConnectedEdge Anzahl={edges.Length} erfasst={count}");
                for (var i = 0; i < count; i++)
                {
                    var target = edges[i].m_Edge;
                    text.AppendLine($"    {target}: {Versorgungszustand(target)}");
                }
            }
            if (EntityManager.HasBuffer<ConnectedNode>(e))
            {
                var nodes = EntityManager.GetBuffer<ConnectedNode>(e, true);
                var count = Math.Min(nodes.Length, 128);
                text.AppendLine($"  ConnectedNode Anzahl={nodes.Length} erfasst={count}");
                for (var i = 0; i < count; i++)
                {
                    var target = nodes[i].m_Node;
                    text.AppendLine($"    {target}: {Versorgungszustand(target)}");
                }
            }
            if (EntityManager.HasComponent<ElectricityNodeConnection>(e))
                BeschreibeVersorgungsfluss(EntityManager
                    .GetComponentData<ElectricityNodeConnection>(e).m_ElectricityNode,
                    "Strom", text);
            if (EntityManager.HasComponent<WaterPipeNodeConnection>(e))
                BeschreibeVersorgungsfluss(EntityManager
                    .GetComponentData<WaterPipeNodeConnection>(e).m_WaterPipeNode,
                    "Wasser", text);
        }

        private void BeschreibeVersorgungsfluss(Entity node, string art, StringBuilder text)
        {
            text.AppendLine($"  FLUSS {art} {node}: {Versorgungszustand(node)}");
            if (!VersorgungsentityLebt(node)) return;
            if (!EntityManager.HasBuffer<ConnectedFlowEdge>(node))
            {
                text.AppendLine("    FEHLER: ConnectedFlowEdge-Puffer fehlt");
                return;
            }
            var edges = EntityManager.GetBuffer<ConnectedFlowEdge>(node, true);
            var count = Math.Min(edges.Length, 128);
            text.AppendLine($"    Kanten Anzahl={edges.Length} erfasst={count}");
            for (var i = 0; i < count; i++)
            {
                var e = edges[i].m_Edge;
                text.AppendLine($"    Kante {e}: {Versorgungszustand(e)}");
                if (!VersorgungsentityLebt(e)) continue;
                Entity a, b;
                if (EntityManager.HasComponent<ElectricityFlowEdge>(e))
                {
                    var data = EntityManager.GetComponentData<ElectricityFlowEdge>(e);
                    a = data.m_Start; b = data.m_End;
                    text.AppendLine($"      Strom Fluss={data.m_Flow} Kapazitaet={data.m_Capacity}");
                }
                else if (EntityManager.HasComponent<WaterPipeEdge>(e))
                {
                    var data = EntityManager.GetComponentData<WaterPipeEdge>(e);
                    a = data.m_Start; b = data.m_End;
                    text.AppendLine($"      Wasser Fluss={data.m_FreshFlow} Abwasser={data.m_SewageFlow}");
                }
                else { text.AppendLine("      FEHLER: Flusskomponente fehlt"); continue; }
                text.AppendLine($"      {a} [{Versorgungszustand(a)}] -> "
                    + $"{b} [{Versorgungszustand(b)}] BezugKorrekt={a == node || b == node}");
            }
        }
    }
}
