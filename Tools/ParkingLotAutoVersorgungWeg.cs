using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Net;
using Game.Prefabs;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private float _avStrombreite, _avWasserbreite, _avAchsabstand;
        private Entity _avStromprefab, _avWasserprefab;
        private List<Versorgungsweg.Hindernis> _avHindernisse;

        private List<Versorgungsweg.Hindernis> AvHindernisse(List<Entity> unsere, float zugabe)
        {
            var r = new List<Versorgungsweg.Hindernis>();
            var querbar = 0;
            foreach (var e in unsere)
            {
                if (!EntityManager.HasComponent<Curve>(e) || !EntityManager.HasComponent<PrefabRef>(e)) continue;
                var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                if (!EntityManager.HasComponent<NetGeometryData>(prefab)) continue;
                var b = EntityManager.GetComponentData<Curve>(e).m_Bezier;
                var radius = EntityManager.GetComponentData<NetGeometryData>(prefab).m_DefaultWidth / 2;
                var fahrgasse = EntityManager.HasComponent<NetData>(prefab)
                    && (EntityManager.GetComponentData<NetData>(prefab).m_LocalConnectLayers
                        & (Layer.PowerlineLow | Layer.WaterPipe | Layer.SewagePipe)) == 0;
                if (fahrgasse) querbar++;
                Versorgungsweg.Bogen(r, e.Index, b.a.xz, b.b.xz, b.c.xz, b.d.xz,
                    radius + AutoVersorgungSicherheitszugabe + zugabe, querbar: fahrgasse);
            }
            /*
             * UND DIE FREMDEN ERDLEITUNGEN - IMMER GESPERRT.
             *
             * Unsere Fahrgassen darf eine Leitung queren, weil unter ihnen
             * nichts liegt. Unter einem Kabel liegt ein Kabel: CS2 prueft Netz
             * gegen Netz rein geometrisch und kennt keine Querungsausnahme.
             * Deshalb `querbar: false`, ohne Winkelregel.
             */
            var leitungen = 0;
            foreach (var e in _avFremdleitungen)
            {
                if (!EntityManager.HasComponent<Curve>(e)
                    || !EntityManager.HasComponent<PrefabRef>(e)) continue;
                var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                if (!EntityManager.HasComponent<NetGeometryData>(prefab)) continue;
                var b = EntityManager.GetComponentData<Curve>(e).m_Bezier;
                var radius = EntityManager
                    .GetComponentData<NetGeometryData>(prefab).m_DefaultWidth / 2;
                leitungen++;
                Versorgungsweg.Bogen(r, e.Index, b.a.xz, b.b.xz, b.c.xz, b.d.xz,
                    radius + AutoVersorgungSicherheitszugabe + zugabe, querbar: false);
            }
            Mod.log.Info($"PLT-Autoversorgung QUERUNGSREGEL: {querbar} eigene Fahrgassen-/Pfadkanten querbar, "
                + $"{r.FindAll(h => !h.Querbar).Count} gesperrte Huellen davon "
                + $"{leitungen} fremde Erdleitung(en); "
                + "Querwinkel mindestens 45 Grad, Laengsfahrt bleibt gesperrt.");
            return r;
        }

        private Entity AvStartknoten(float3 start, List<Entity> gruppe)
        {
            foreach (var e in gruppe)
            {
                var edge = EntityManager.GetComponentData<Edge>(e);
                foreach (var n in new[] { edge.m_Start, edge.m_End })
                    if (EntityManager.HasComponent<Node>(n)
                        && math.distance(EntityManager.GetComponentData<Node>(n).m_Position, start) <= 0.1f) return n;
            }
            return Entity.Null;
        }

        private HashSet<int> AvStartstrassen(float3 start, List<Entity> gruppe)
        {
            var r = new HashSet<int>();
            var n = AvStartknoten(start, gruppe);
            foreach (var e in gruppe)
            {
                var edge = EntityManager.GetComponentData<Edge>(e);
                if ((n != Entity.Null && (edge.m_Start == n || edge.m_End == n))
                    || (KanteNimmtVersorgung(e) && EntityManager.HasComponent<Curve>(e)
                        && MathUtils.Distance(EntityManager.GetComponentData<Curve>(e).m_Bezier.xz,
                            start.xz, out _) <= 0.1f)) r.Add(e.Index);
            }
            return r;
        }

        private bool AvWege(List<float2> weg, HashSet<int> startstrassen, Entity ziel,
            out List<float2> strom, out List<float2> wasser)
            => Versorgungsweg.Spuren(weg, _avStrombreite, _avWasserbreite,
                _avHindernisse, startstrassen, AutoVersorgungAnschlussbereich, out strom, out wasser,
                (p, s) => AvZieltor(ziel, s ? _avStromprefab : _avWasserprefab, p, out _, out _),
                AvZielstrassen(ziel), (p, s) => AvStarttor(startstrassen, p, s));

        private IEnumerable<Versorgungsweg.Ziel> AvZielpunkte(float2 p,
            List<(Entity Kante, Bezier4x3 Bogen)> ziele)
        {
            for (var i = 0; i < ziele.Count; i++)
            {
                MathUtils.Distance(ziele[i].Bogen.xz, p, out var t);
                yield return new Versorgungsweg.Ziel { Index = i, Punkt = MathUtils.Position(ziele[i].Bogen, t).xz };
                // Gesperrte Lotpunkte duerfen vorhandene Alternativen nicht verdecken.
                for (var k = 0; k <= 16; k++)
                    yield return new Versorgungsweg.Ziel { Index = i, Punkt = MathUtils.Position(ziele[i].Bogen, k / 16f).xz };
            }
        }

        private bool AvUmweg(List<float3> starts, List<Entity> gruppe, List<Entity> unsere,
            List<(Entity Kante, Bezier4x3 Bogen)> fremde, string name, ref Versorgungstrasse trasse, float maxLaenge)
        {
            var uhr = System.Diagnostics.Stopwatch.StartNew();
            var punkte = starts.ConvertAll(p => p.xz);
            var startstrassen = starts.ConvertAll(p => AvStartstrassen(p, gruppe));
            // Gehrung hoechstens 2 * halber Achsabstand; Leitungsbreite ist
            // bereits in _avHindernisse enthalten. So passt auch der Knick.
            var hindernisse = AvHindernisse(unsere,
                math.max(_avStrombreite, _avWasserbreite) / 2 + _avAchsabstand);
            /*
             * EINE WARNUNG VOR DER RECHNUNG, NICHT DANACH.
             *
             * Der Sichtbarkeitsgraph waechst mit den Huellenecken, und die
             * Suche darauf ist quadratisch. Am 2026-09-16 waren es 219
             * Huellen, 1671 Knoten und 22 Sekunden. Diese Zahl gehoert vor
             * den Lauf, damit man beim naechsten Mal weiss, was kommt.
             */
            var ecken = 0;
            foreach (var h in hindernisse) ecken += h.Ring.Length;
            if (ecken > 800)
                Mod.log.Warn($"PLT-Autoversorgung {name}: {hindernisse.Count} Huellen mit "
                    + $"{ecken} Ecken - die Wegesuche darauf ist quadratisch und kann "
                    + "mehrere Sekunden dauern.");
            var r = Versorgungsweg.Suche(punkte, hindernisse, i => startstrassen[i], p => AvZielpunkte(p, fremde),
                (weg, zielIndex) => {
                    var index = punkte.IndexOf(weg[0]);
                    return index >= 0 && AvWege(weg, startstrassen[index], fremde[zielIndex].Kante, out _, out _);
                }, AutoVersorgungAnschlussbereich + _avAchsabstand, i => AvZielstrassen(fremde[i].Kante), maxLaenge);
            Mod.log.Info($"PLT-Autoversorgung HINDERNISWEG [{name}]: {starts.Count} Starts, "
                + $"{fremde.Count} Zielstrassen, {hindernisse.Count} aufgeweitete Huellen, "
                + $"{r.Erreicht}/{r.Knoten} Graphknoten erreicht, {r.Sichtpruefungen} Sichtpruefungen, "
                + $"{r.Zielpruefungen} Zielpruefungen, {(r.Punkte == null ? 0 : r.Punkte.Count - 1)} Teilstrecken, "
                + $"{uhr.Elapsed.TotalMilliseconds:F1} ms, Suchgrenze {maxLaenge:F3} m. "
                + (r.Punkte == null ? "Keine zulaessige Verbesserung innerhalb der Suchgrenze; keine Strassenquerung freigegeben."
                    : $"Gewaehlter Graphweg {r.Laenge:F2} m."));
            if (r.Punkte == null || (trasse.Gefunden && !Versorgungsnetz.Kuerzer(r.Laenge, trasse.Laenge))) return false;
            MathUtils.Distance(fremde[r.Ziel].Bogen.xz, r.Punkte[r.Punkte.Count - 1], out var lage);
            AvWege(r.Punkte, startstrassen[r.Start], fremde[r.Ziel].Kante, out var strom, out var wasser);
            trasse = new Versorgungstrasse { Start = starts[r.Start],
                Startknoten = AvStartknoten(starts[r.Start], gruppe),
                Startkanten = gruppe.FindAll(e => startstrassen[r.Start].Contains(e.Index)),
                Ziel = MathUtils.Position(fremde[r.Ziel].Bogen, lage), Zielkante = fremde[r.Ziel].Kante,
                Herkunft = name + ": Hindernisweg", Laenge = r.Laenge, Stromweg = strom, Wasserweg = wasser };
            return true;
        }
    }
}
