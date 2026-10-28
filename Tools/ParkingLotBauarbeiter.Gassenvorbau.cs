using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * WELCHE GASSEN MUESSEN EINE STADTSTRASSE TEILEN? (28.10.2026)
     *
     * Der Hintergrund-Neubau baut Permanent-Definitionen und darf keine
     * Stadtstrasse teilen (5c0b8cc). Eine Gasse, deren Knoten an der Strasse
     * fehlt, zaehlt er deshalb als "nicht baubar" und rollt zurueck - an
     * "Parking Lot (192 spaces)" dreimal am 28.10., 47 von 48 Kursen standen.
     *
     * Hier plant der isolierte Bauarbeiter genau diese Gassen aus dem
     * Bauzettel, mit derselben Rechnung wie der Bau (PlaneGassenkurs): Kurs
     * von der Strassenmitte bis zur Fahrgasse, Kante und Teilungsstelle an
     * der Strasse, Hoehe der Strasse. Gebaut werden sie danach wie im Edit -
     * Entwurf und Anwenden, ParkingLotGassenvorbau.cs.
     *
     * MIT DEN WEGEN AN IHREM INNEREN ENDE (28.10. abends). Erster Versuch:
     * nur die Gasse vorgebaut, die Randstrasse danach im Hintergrund. Die
     * Gasse hatte das Gelaende an ihrem Ende da schon ausgeschnitten, CS2
     * legte die beiden neuen Randstrassen-Stuecke darauf - Kurvenende
     * 0,32 m unter dem Gassenknoten, Abnahme "46/48", Rueckweg, dreimal.
     * Im Edit entstehen Gasse und anschliessende Wege im selben Zug, alle
     * Enden auf einer Hoehe (Bau 16:11: 121,2578 m). Genau so jetzt hier;
     * der Hintergrund-Neubau findet diese Wege danach als unveraendert.
     */
    public sealed partial class ParkingLotToolSystem
    {
        internal sealed class Vorbaugasse
        {
            internal int Index;
            internal Entity Prefab, Kante;
            internal float Teilung;
            internal float3 Von, Nach;
            /** Faehrt die Gasse hinein, liegt die Strasse am Anfang des Kurses. */
            internal bool StadtAmAnfang;
            internal Zufahrtsart Art;
            /** Gasse mit Stadtteilung, sonst ein Weg, der an ihrem inneren Ende haengt. */
            internal bool Gasse = true;
            internal string Kind = "entrance-gasse";
            /** Nur Wege: vorhandener Gassenknoten, an dem dieses Ende andockt. */
            internal Entity StartKnoten, EndKnoten;
        }

        /**
         * Die Gassen des vorbereiteten Parkplatzes (BereiteFlaechen), deren
         * Stadtanschluss eine Kante ist. Null, solange der Gassenklon noch
         * nicht bereit ist; `grund` sagt dann warum.
         */
        internal List<Vorbaugasse> PlaneGassenvorbau(Entity lot, ParkingLayout layout, out string grund)
        {
            grund = null;
            var liste = new List<Vorbaugasse>();
            var eigene = ParkingLotNetzRueckweg.Besitzteile(EntityManager, lot);
            // Innere Enden schon stehender Gassen ohne Weg daran: Knoten -> Lage.
            var offeneEnden = new List<(float2 Lage, Entity Knoten)>();
            if (layout?.NetLine == null) { grund = "kein Layout"; return null; }
            _areaPreviewLayout = layout;
            var hoehen = new Dictionary<(long, long), float>();
            var gelaende = _terrainSystem.GetHeightData();
            var bericht = new List<string>();
            for (var i = 0; i < layout.NetLine.Length; i++)
            {
                var piece = layout.NetLine[i];
                if (piece.Kind != "entrance" || !Zufahrtsarten.IstGasse(piece.Art)) continue;
                if (!TryResolveZufahrtsgasse(piece.Art, out _)) { grund = "Gassenklon noch nicht bereit"; return null; }
                var breite = (float)new Entrance { Art = piece.Art }
                    .Breite(_areaPreviewSettings.Ai, _areaPreviewSettings.Gassenbreite);
                if (!PlaneGassenkurs(piece, i, breite, hoehen, bericht, out var p) || p == null) continue;
                /*
                 * Steht die Gasse schon (halber Reparaturversuch, gespeichert),
                 * aber ohne Weg an ihrem inneren Ende, kaeme der Hintergrund
                 * wieder an genau diesen Stuecken nicht vorbei. Dann nur die
                 * Wege, angedockt an ihren Knoten.
                 */
                if (VorhandeneGasseOhneWege(lot, eigene, p, out var knoten)) { offeneEnden.Add((p.Ende, knoten)); continue; }
                // Stummel oder vorhandener Knoten: das baut der Hintergrund selbst.
                if (p.Strasse == Entity.Null || !EntityManager.HasComponent<Edge>(p.Anschluss.Entity)) continue;
                var von = new float3(p.Von.x, SampleCourseHeight(p.Von, ref gelaende, hoehen), p.Von.y);
                var nach = new float3(p.Nach.x, SampleCourseHeight(p.Nach, ref gelaende, hoehen), p.Nach.y);
                liste.Add(new Vorbaugasse
                {
                    Index = i, Prefab = p.Prefab, Kante = p.Anschluss.Entity, Teilung = p.Anschluss.Teilung,
                    Von = von, Nach = nach, StadtAmAnfang = !Zufahrtsarten.FaehrtHinaus(piece.Art), Art = piece.Art,
                });
            }
            if (liste.Count == 0 && offeneEnden.Count == 0) return liste;
            // Dieselbe Zerlegung wie der Netzerhalt (InnereNetzkurse), damit
            // der Hintergrund-Neubau die Wege als unveraendert wiederfindet.
            var innen = new List<float2>();
            foreach (var g in liste) innen.Add((g.StadtAmAnfang ? g.Nach : g.Von).xz);
            foreach (var o in offeneEnden) innen.Add(o.Lage);
            Entity Knoten(float2 lage)
            {
                foreach (var o in offeneEnden) if (math.distance(o.Lage, lage) < 0.05f) return o.Knoten;
                return Entity.Null;
            }
            TryChooseDrivablePath(_areaPreviewSettings.Ai, out var breit, out _);
            TryChooseDrivablePath(_areaPreviewSettings.Cw, out var schmal, out _);
            var wege = new List<Vorbaugasse>();
            for (var i = 0; i < layout.NetLine.Length; i++)
            {
                var piece = layout.NetLine[i];
                if (piece.Kind == "zoning" || piece.Kind == "entrance" && Zufahrtsarten.IstGasse(piece.Art)) continue;
                foreach (var kurs in InnereNetzkurse(piece, breit, schmal))
                {
                    if (kurs.Prefab == Entity.Null) continue;
                    var amEnde = false;
                    foreach (var q in innen)
                        if (math.distance(kurs.A, q) < 0.05f || math.distance(kurs.B, q) < 0.05f) amEnde = true;
                    if (!amEnde) continue;
                    wege.Add(new Vorbaugasse
                    {
                        Index = i, Prefab = kurs.Prefab, Kante = Entity.Null, Gasse = false, Kind = kurs.Kind, Art = piece.Art,
                        Von = Lage(kurs.A, Knoten(kurs.A), ref gelaende, hoehen),
                        Nach = Lage(kurs.B, Knoten(kurs.B), ref gelaende, hoehen),
                        StartKnoten = Knoten(kurs.A), EndKnoten = Knoten(kurs.B),
                    });
                }
            }
            liste.AddRange(wege);
            return liste;
        }

        /*
         * BAUZETTEL OHNE NAMEN DER ZONINGSTRASSE (28.10.2026).
         *
         * "Parking Lot (323 spaces)" vom 04.10. hat Zoning, aber Text 5 ist
         * leer. Der Hintergrund-Neubau suchte dann die Zoningstrasse '' und
         * brach in der Planung ab ("Bauprefabs nicht bereit"), bei jeder
         * Reparatur. Welche es war, zeigt der Bestand: unsere Zoningstrassen
         * heissen "PLT Zoningstrasse (<Vorbild>)". Ohne eine davon gilt der
         * Standard "Alley" - derselbe wie in LayoutSettings. Der Leser selbst
         * bleibt ohne Spieltypen (Tests/Baukontext uebersetzt ihn).
         */
        private string ZoningstrasseAusBestand(Entity lot)
        {
            const string kopf = "PLT Zoningstrasse (";
            if (EntityManager.HasBuffer<Game.Net.SubNet>(lot))
                foreach (var s in EntityManager.GetBuffer<Game.Net.SubNet>(lot, true))
                {
                    if (!EntityManager.Exists(s.m_SubNet) || !EntityManager.HasComponent<PrefabRef>(s.m_SubNet)) continue;
                    if (!_prefabSystem.TryGetPrefab<PrefabBase>(EntityManager.GetComponentData<PrefabRef>(s.m_SubNet).m_Prefab, out var p)
                        || p == null || !p.name.StartsWith(kopf, System.StringComparison.Ordinal)
                        || !p.name.EndsWith(")", System.StringComparison.Ordinal)) continue;
                    var name = p.name.Substring(kopf.Length, p.name.Length - kopf.Length - 1);
                    Mod.log.Info($"PLT-Bauzettel: Lot {lot.Index} ohne Namen der Zoningstrasse - aus dem Bestand '{name}'.");
                    return name;
                }
            Mod.log.Info($"PLT-Bauzettel: Lot {lot.Index} ohne Namen der Zoningstrasse und ohne eigene - Standard 'Alley'.");
            return "Alley";
        }

        /** Am vorhandenen Knoten seine echte Lage, sonst die geplante Hoehe. */
        private float3 Lage(float2 p, Entity knoten, ref Game.Simulation.TerrainHeightData gelaende,
            Dictionary<(long, long), float> hoehen)
            => knoten != Entity.Null ? EntityManager.GetComponentData<Game.Net.Node>(knoten).m_Position
                : new float3(p.x, SampleCourseHeight(p, ref gelaende, hoehen), p.y);

        /** Eigene Gasse genau auf diesem Kurs, und kein eigener Weg endet an ihrem inneren Knoten. */
        private bool VorhandeneGasseOhneWege(Entity lot, Entity[] eigene, Gassenkurs p, out Entity knoten)
        {
            knoten = Entity.Null;
            foreach (var e in eigene)
            {
                if (!EntityManager.HasComponent<Edge>(e) || !EntityManager.HasComponent<Curve>(e)
                    || !EntityManager.HasComponent<PrefabRef>(e)
                    || !GassenPrefab.Ist(_prefabSystem, EntityManager.GetComponentData<PrefabRef>(e).m_Prefab)) continue;
                var c = EntityManager.GetComponentData<Curve>(e).m_Bezier;
                var k = EntityManager.GetComponentData<Edge>(e);
                if (math.distance(c.a.xz, p.Ende) < 0.15f && math.distance(c.d.xz, p.Mitte) < 0.5f) knoten = k.m_Start;
                else if (math.distance(c.d.xz, p.Ende) < 0.15f && math.distance(c.a.xz, p.Mitte) < 0.5f) knoten = k.m_End;
                else continue;
                break;
            }
            if (knoten == Entity.Null || !EntityManager.HasBuffer<ConnectedEdge>(knoten)) { knoten = Entity.Null; return false; }
            foreach (var c in EntityManager.GetBuffer<ConnectedEdge>(knoten, true))
                if (EntityManager.HasComponent<PrefabRef>(c.m_Edge)
                    && !GassenPrefab.Ist(_prefabSystem, EntityManager.GetComponentData<PrefabRef>(c.m_Edge).m_Prefab)
                    && EntityManager.HasComponent<Owner>(c.m_Edge)
                    && ParkingLotBesitz.GehoertZu(EntityManager, EntityManager.GetComponentData<Owner>(c.m_Edge).m_Owner, lot))
                { knoten = Entity.Null; return false; }
            return true;
        }
    }
}
