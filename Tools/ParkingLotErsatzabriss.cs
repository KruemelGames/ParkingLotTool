using System;
using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    internal static class ParkingLotErsatzabriss
    {
        internal static List<Entity> Eigene(EntityManager em, Entity alt, Entity lot, Entity traeger)
        {
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<Owner>(),
                ComponentType.Exclude<Temp>(),ComponentType.Exclude<Deleted>());
            using var alle = q.ToEntityArray(Allocator.Temp);
            var erhalten = new HashSet<Entity>();
            if (em.HasBuffer<ParkingLotErhaltenerKurs>(alt))
                foreach (var k in em.GetBuffer<ParkingLotErhaltenerKurs>(alt,true))
                { erhalten.Add(k.Bestand.Kante); erhalten.Add(k.Bestand.Start); erhalten.Add(k.Bestand.Ende); }
            return HintergrundRuecknahme.Eigene(alle,lot,traeger,
                e => em.GetComponentData<Owner>(e).m_Owner,erhalten.Contains);
        }
        internal static IEnumerable<int> Schritte(EntityManager em, Entity alt, Entity lot, Entity traeger)
        {
            ParkingLotNetzerhalt.EntferneGelieheneVerweise(em,alt,lot,traeger);
            var eigene = Eigene(em,alt,lot,traeger);
            var knoten = new List<Entity>(); int kanten = 0;
            foreach (var e in eigene)
            {
                if (em.HasComponent<Node>(e)) { knoten.Add(e); continue; }
                if (em.HasComponent<Edge>(e)) kanten++;
                if (ParkingLotNetzRueckweg.Lebt(em,e)) em.AddComponent<Deleted>(e);
                yield return 0;
            }
            // Fix 1d680b6: ALLE Kanten vorher, ALLE Knoten im selben Bild.
            // Besitzer folgen ihren Kindern ohne weitere Unterbrechung.
            /*
             * KEIN KNOTEN FAELLT, AN DEM NOCH EINE FREMDE KANTE HAENGT (2026-10-16).
             *
             * Hier stand bis dahin eine Ausnahme: "Ersatzknoten hat noch
             * lebende Kante; Ruecknahme nicht freigegeben" - und der Parkplatz
             * blieb fuer immer gesperrt. Gemessen an einem Spielstand vom
             * 3.10.: der halbe Ersatz hatte sich an eine Stadtstrasse
             * angeschlossen (die Strasse wurde an unserem Knoten geteilt). Den
             * Knoten zu loeschen haette die Stadtstrasse auf eine tote Entity
             * zeigen lassen; ihn mit unserem Besitzer stehen zu lassen ebenso,
             * denn Traeger und Lot fallen gleich danach.
             *
             * Dieselbe Regel wie im normalen Abriss (ParkingLotEditAbriss,
             * seit 1.0.6) und in Vanilla (SubElementDeleteSystem): der Knoten
             * bleibt und verliert nur unseren Besitzer. Alle eigenen Kanten
             * sind in diesem Bild bereits geloescht, jede noch lebende Kante
             * ist also fremd.
             */
            int behalten = 0;
            foreach (var e in knoten)
            {
                if (!ParkingLotNetzRueckweg.Lebt(em,e)) continue;
                if (HaengtNochKanteDran(em,e))
                {
                    behalten++;
                    if (em.HasComponent<Owner>(e)) em.RemoveComponent<Owner>(e);
                    if (!em.HasComponent<Updated>(e)) em.AddComponent<Updated>(e);
                    continue;
                }
                em.AddComponent<Deleted>(e);
            }
            if (ParkingLotNetzRueckweg.Lebt(em,traeger)) em.AddComponent<Deleted>(traeger);
            if (ParkingLotNetzRueckweg.Lebt(em,lot)) em.AddComponent<Deleted>(lot);
            ParkingLotNetzRueckweg.Melde($"Ersatzabriss: eigene Kanten {kanten}, eigene Knoten {knoten.Count}"
                + (behalten > 0 ? $" (davon {behalten} an fremden Kanten behalten, nur Besitzer geloest)" : "")
                + $", eigene Kinder {eigene.Count}; Besitzer zuletzt, erhaltene Originale 0 geloescht.");
        }

        /** Endet an diesem Knoten noch eine lebende Kante? Durchlaufende Kanten zaehlen nicht. */
        private static bool HaengtNochKanteDran(EntityManager em, Entity knoten)
        {
            if (!em.HasBuffer<ConnectedEdge>(knoten)) return false;
            foreach (var k in em.GetBuffer<ConnectedEdge>(knoten,true))
            {
                if (!ParkingLotNetzRueckweg.Lebt(em,k.m_Edge) || !em.HasComponent<Edge>(k.m_Edge)) continue;
                var kante = em.GetComponentData<Edge>(k.m_Edge);
                if (kante.m_Start == knoten || kante.m_End == knoten) return true;
            }
            return false;
        }
    }
}
