using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Colossal.Collections;
using Colossal.Mathematics;
using Game.Areas;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Tools;
using Game.Zones;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * Findet ueberlagerte Objekte in genau einem PLT-Parkplatz.
     *
     * DIAGNOSE, KEIN FIX: keine Entity und kein Vanilla-Prefab wird veraendert.
     * Der erste Lauf folgt 24 Frames nach dem Bau; ein zweiter wird im
     * Debug-Reiter ausgeloest. Nur so laesst sich belegen, ob ein spaeter
     * gewachsenes Zoning-Gebaeude beim fruehen Lauf schon da war.
     */
    public sealed partial class ParkingLotToolSystem
    {
        private const string UePrefix = "PLT-Ueberlappung:";
        private const int UeFruehWarteFrames = 24;
        private const int UeKandidatenGrenze = 2000;
        private const int UePaarGrenze = 2500;
        private const float UeZoningRand = 56f;

        private sealed class UeAuftrag
        {
            internal Entity Lot;
            internal bool Frueh;
            internal int Faellig;
            internal string Ausloeser;
        }

        private sealed class UeObjekt
        {
            internal Entity Entity;
            internal Entity Prefab;
            internal Game.Objects.Transform Transform;
            internal ObjectGeometryData Geometry;
            internal CollisionMask Mask;
            internal Bounds3 Bounds;
            internal string Name;
            internal bool Eigen;
            internal bool Gebaeude;
            internal bool Overridden;
        }

        private sealed class UePaar
        {
            internal UeObjekt A;
            internal UeObjekt B;
            internal ParkingOverlapMeasure Messung;
            internal float3 Aabb;
            internal string Grund;
        }

        private sealed class UeBasis
        {
            internal DateTime Zeitpunkt;
            internal HashSet<Entity> Objekte;
            internal HashSet<string> Paare;
        }

        private struct UeObjektIterator
            : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public Bounds2 Bounds;
            public NativeList<Entity> Results;
            public int Treffer;

            public bool Intersect(QuadTreeBoundsXZ bounds)
                => MathUtils.Intersect(bounds.m_Bounds.xz, Bounds);

            public void Iterate(QuadTreeBoundsXZ bounds, Entity entity)
            {
                if (!MathUtils.Intersect(bounds.m_Bounds.xz, Bounds)) return;
                Treffer++;
                if (Results.Length < UeKandidatenGrenze) Results.Add(entity);
            }
        }

        private readonly List<UeAuftrag> _ueAuftraege = new List<UeAuftrag>();
        private readonly Dictionary<Entity, UeBasis> _ueBasen
            = new Dictionary<Entity, UeBasis>();

        /** Wird vom bestehenden Nach-Bau-Messpfad aufgerufen. */
        private void PlaneUeberlappungsdiagnose(Entity lot)
        {
            if (!UeIstLot(lot)) return;
            _ueAuftraege.RemoveAll(a => a.Lot == lot && a.Frueh);
            _ueAuftraege.Add(new UeAuftrag
            {
                Lot = lot,
                Frueh = true,
                Faellig = UnityEngine.Time.frameCount + UeFruehWarteFrames,
                Ausloeser = "automatisch nach Bau",
            });
            Mod.log.Info($"{UePrefix} frueher Lauf fuer {Show(lot)} in "
                + $"{UeFruehWarteFrames} Frames vorgemerkt.");
        }

        /** Knopf im Debug-Reiter; die Arbeit laeuft im naechsten Tool-Update. */
        internal void FordereUeberlappungsdiagnose(Entity auswahl)
        {
            var lot = UeFindeLot(auswahl);
            if (lot == Entity.Null)
            {
                const string text = "Kein PLT-Parkplatz gewählt. Erst den "
                    + "Parkplatz oder ein daran gewachsenes Gebäude auswählen.";
                Mod.log.Warn(UePrefix + " " + text);
                _uiSystem?.SetUeberlappungsstand(text);
                return;
            }
            _ueAuftraege.RemoveAll(a => a.Lot == lot && !a.Frueh);
            _ueAuftraege.Add(new UeAuftrag
            {
                Lot = lot,
                Frueh = false,
                Faellig = UnityEngine.Time.frameCount + 1,
                Ausloeser = "Debug-Knopf",
            });
            _uiSystem?.SetUeberlappungsstand("Diagnose vorgemerkt …");
            Mod.log.Info($"{UePrefix} manueller Lauf fuer {Show(lot)} "
                + $"aus Auswahl {Show(auswahl)} vorgemerkt.");
        }

        /** Wird zusammen mit der Ladesaeulenmessung auch bei inaktivem Tool gepollt. */
        private void PflegeUeberlappungsdiagnose()
        {
            if (_ueAuftraege.Count == 0) return;
            // Log 06.09. 22:34:59: Diagnose blockierte 2903,4 ms mitten im
            // Anschlussbau. Erst nach Umbau, Abriss und Leitungs-Apply messen.
            if (IsEditing || _buildStage != BuildStage.Idle
                || (_avPhase != AvPhase.Idle && _avPhase != AvPhase.Nachmessen)) return;
            var frame = UnityEngine.Time.frameCount;
            var index = _ueAuftraege.FindIndex(a => a.Faellig <= frame);
            if (index < 0) return;
            var auftrag = _ueAuftraege[index];
            _ueAuftraege.RemoveAt(index);
            if (!UeIstDauerhaftesLot(auftrag.Lot))
            {
                Mod.log.Warn($"{UePrefix} {auftrag.Ausloeser}: Lot existiert "
                    + "nicht mehr oder sein Bauzettel fehlt.");
                return;
            }
            try { UeMesse(auftrag); }
            catch (Exception exception)
            {
                Mod.log.Error(exception, UePrefix + " Suchlauf fehlgeschlagen.");
                _uiSystem?.SetUeberlappungsstand(
                    "Diagnose fehlgeschlagen – Einzelheiten im Mod-Log.");
            }
        }

        private void UeMesse(UeAuftrag auftrag)
        {
            var timer = Stopwatch.StartNew();
            var lot = auftrag.Lot;
            var suchraum = UeSuchraum(lot, out var kennung);
            var objekte = UeSammleObjekte(lot, suchraum, out var baumTreffer,
                out var abgeschnitten);
            var paare = UeFindePaare(objekte, out var paarPruefungen,
                out var sonderfaelle, out var paareAbgeschnitten);
            _ueBasen.TryGetValue(lot, out var basis);

            var overridden = objekte.Where(o => o.Overridden).ToList();
            var paarJeObjekt = new Dictionary<Entity, int>();
            for (var i = 0; i < paare.Count; i++)
            {
                UeZaehle(paarJeObjekt, paare[i].A.Entity);
                UeZaehle(paarJeObjekt, paare[i].B.Entity);
            }

            var netzTreffer = 0;
            var flaechenTreffer = 0;
            var gemeldeteGebaeude = new HashSet<Entity>();
            Mod.log.Info($"{UePrefix} START {auftrag.Ausloeser}; Lot "
                + $"{Show(lot)} / {kennung}; Suchraum "
                + $"{UeZahl(suchraum.max.x - suchraum.min.x)} x "
                + $"{UeZahl(suchraum.max.y - suchraum.min.y)} m, "
                + $"Zoning-Rand {UeZahl(UeZoningRand)} m.");
            ParkingLotLiveLog.Zeile("ueberlappung start | " + auftrag.Ausloeser
                + " | lot " + Show(lot) + " | kennung " + kennung
                + " | bounds " + UePunkt(suchraum.min) + " bis "
                + UePunkt(suchraum.max));

            for (var i = 0; i < overridden.Count; i++)
            {
                var objekt = overridden[i];
                var entstehung = UeEntstehung(objekt, null, auftrag.Frueh, basis);
                var zuordnung = UeZuordnung(objekt.Entity, lot);
                var kontext = UeKontext(lot, objekt.Transform.m_Position.xz,
                    objekte);
                paarJeObjekt.TryGetValue(objekt.Entity, out var objektPaare);
                var kopf = $"OVERRIDDEN {Show(objekt.Entity)} '{objekt.Name}' "
                    + $"bei {UePunkt(objekt.Transform.m_Position.xz)}; Flags "
                    + $"[{objekt.Geometry.m_Flags}]; {zuordnung}; {kontext}; "
                    + $"{entstehung}; {objektPaare} exakte Objektpaar(e).";
                Mod.log.Warn(UePrefix + " " + kopf);
                ParkingLotLiveLog.Zeile("  " + kopf);
                ParkingLotLiveLog.Zeile("    bounds " + UeBounds(objekt.Bounds)
                    + " | groesse " + UeFloat3(objekt.Geometry.m_Size)
                    + " | collision " + objekt.Mask + " | besitzer "
                    + UeBesitzerkette(objekt.Entity));
                UeMeldeGebaeude(objekt, lot);
                if (objekt.Gebaeude) gemeldeteGebaeude.Add(objekt.Entity);
                netzTreffer += UeMeldeNetzpartner(objekt, lot);
                flaechenTreffer += UeMeldeFlaechenpartner(objekt, lot);
            }

            // Alle Paare bleiben im Befund und Vergleichsstand. Der automatische
            // Lauf erklaert nur 24 Beispiele statt 2123 teurer Kontext-/Logzeilen.
            // Vollstaendige Einzelheiten bleiben ueber den Debug-Knopf abrufbar.
            var detailzahl = auftrag.Frueh ? math.min(paare.Count, 24) : paare.Count;
            if (detailzahl < paare.Count)
                Mod.log.Info($"{UePrefix} {detailzahl}/{paare.Count} Paare einzeln erklaert; "
                    + "alle gezaehlt und gespeichert. Vollstaendige Details: Debug-Knopf.");
            for (var i = 0; i < detailzahl; i++)
            {
                var paar = paare[i];
                var punkt = (float2)paar.Messung.Center;
                var kontext = UeKontext(lot, punkt, objekte);
                var entstehung = UeEntstehung(paar.A, paar.B,
                    auftrag.Frueh, basis);
                var zeile = $"PAAR {Show(paar.A.Entity)} '{paar.A.Name}' <-> "
                    + $"{Show(paar.B.Entity)} '{paar.B.Name}': "
                    + $"Ueberdeckung {UeZahl(paar.Messung.Area, 2)} m2, "
                    + $"Eindringtiefe {UeZahl(paar.Messung.Penetration, 2)} m, "
                    + $"AABB x/y/z {UeFloat3(paar.Aabb)} m; {kontext}; "
                    + $"{UeZuordnung(paar.A.Entity, lot)} <-> "
                    + $"{UeZuordnung(paar.B.Entity, lot)}; {entstehung}.";
                Mod.log.Warn(UePrefix + " " + zeile);
                ParkingLotLiveLog.Zeile("  " + zeile);
                ParkingLotLiveLog.Zeile("    Test: " + paar.Grund
                    + " | A flags [" + paar.A.Geometry.m_Flags + "] bounds "
                    + UeBounds(paar.A.Bounds) + " | B flags ["
                    + paar.B.Geometry.m_Flags + "] bounds "
                    + UeBounds(paar.B.Bounds));
                if (paar.A.Gebaeude && gemeldeteGebaeude.Add(paar.A.Entity))
                    UeMeldeGebaeude(paar.A, lot);
                if (paar.B.Gebaeude && gemeldeteGebaeude.Add(paar.B.Entity))
                    UeMeldeGebaeude(paar.B, lot);
            }

            var strassenpaare = UeMeldeStrasseAufStrasse(lot);

            timer.Stop();
            var vollstaendig = !abgeschnitten && !paareAbgeschnitten;
            var sauber = overridden.Count == 0 && paare.Count == 0;
            var ergebnis = $"{objekte.Count} lokale Objekte aus "
                + $"{baumTreffer} Baumtreffern, {overridden.Count} Overridden, "
                + $"{paare.Count} exakte Objektpaare, {strassenpaare} "
                + $"Strasse-auf-Strasse, {netzTreffer} Netz- und "
                + $"{flaechenTreffer} Flaechenpartner; {paarPruefungen} "
                + $"Paarpruefungen, {sonderfaelle} geometrische Sonderfaelle; "
                + $"{UeZahl(timer.Elapsed.TotalMilliseconds, 1)} ms; "
                + (vollstaendig ? "Suchgrenze nicht erreicht" : "UNVOLLSTAENDIG: "
                    + (abgeschnitten
                        ? $"mehr als {UeKandidatenGrenze} lokale Baumtreffer"
                        : $"Paargrenze {UePaarGrenze} erreicht"))
                + (sauber ? "; alles sauber." : ".");
            Mod.log.Info(UePrefix + " BEFUND " + ergebnis);
            ParkingLotLiveLog.Zeile("ueberlappung befund | " + ergebnis);
            _uiSystem?.SetUeberlappungsstand(ergebnis);

            if (auftrag.Frueh)
            {
                _ueBasen[lot] = new UeBasis
                {
                    Zeitpunkt = DateTime.UtcNow,
                    Objekte = new HashSet<Entity>(objekte.Select(o => o.Entity)),
                    Paare = new HashSet<string>(paare.Select(UePaarschluessel)),
                };
                Mod.log.Info($"{UePrefix} frueher Vergleichsstand fuer "
                    + $"{Show(lot)} gespeichert: {objekte.Count} Objekte, "
                    + $"{paare.Count} Paare (nur diese Spielsitzung).");
            }
        }

        private List<UeObjekt> UeSammleObjekte(Entity lot, Bounds2 suchraum,
            out int baumTreffer, out bool abgeschnitten)
        {
            var ergebnis = new List<UeObjekt>();
            baumTreffer = 0;
            abgeschnitten = false;
            if (_objectSearchSystem == null) return ergebnis;
            var tree = _objectSearchSystem.GetStaticSearchTree(true, out var deps);
            deps.Complete();
            using var funde = new NativeList<Entity>(256, Allocator.Temp);
            var iterator = new UeObjektIterator
            {
                Bounds = suchraum,
                Results = funde,
            };
            tree.Iterate(ref iterator);
            baumTreffer = iterator.Treffer;
            abgeschnitten = iterator.Treffer > funde.Length;
            var gesehen = new HashSet<Entity>();
            for (var i = 0; i < funde.Length; i++)
            {
                var entity = funde[i];
                if (!gesehen.Add(entity) || entity == Entity.Null
                    || !EntityManager.Exists(entity)
                    || EntityManager.HasComponent<Temp>(entity)
                    || EntityManager.HasComponent<Deleted>(entity)
                    || !TryGetSimpleObject(entity, out var transform,
                        out var geometry, out var mask)) continue;
                var bounds = ObjectUtils.CalculateBounds(transform.m_Position,
                    transform.m_Rotation, geometry);
                if (!MathUtils.Intersect(bounds.xz, suchraum)) continue;
                var prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                ergebnis.Add(new UeObjekt
                {
                    Entity = entity,
                    Prefab = prefab,
                    Transform = transform,
                    Geometry = geometry,
                    Mask = mask,
                    Bounds = bounds,
                    Name = PrefabNameOf(entity) ?? "ohne Prefabnamen",
                    Eigen = UeGehoertZuLot(entity, lot),
                    Gebaeude = EntityManager.HasComponent<Game.Buildings.Building>(entity),
                    Overridden = EntityManager.HasComponent<Overridden>(entity),
                });
            }
            ergebnis.Sort((a, b) => a.Bounds.min.x.CompareTo(b.Bounds.min.x));
            return ergebnis;
        }

        private List<UePaar> UeFindePaare(List<UeObjekt> objekte,
            out int pruefungen, out int sonderfaelle, out bool abgeschnitten)
        {
            var ergebnis = new List<UePaar>();
            pruefungen = 0;
            sonderfaelle = 0;
            abgeschnitten = false;
            for (var i = 0; i < objekte.Count; i++)
            {
                var a = objekte[i];
                for (var j = i + 1; j < objekte.Count; j++)
                {
                    var b = objekte[j];
                    if (b.Bounds.min.x >= a.Bounds.max.x) break;
                    if (b.Bounds.min.z >= a.Bounds.max.z
                        || b.Bounds.max.z <= a.Bounds.min.z) continue;
                    pruefungen++;
                    var treffer = CollisionLikeOverride(a.Entity, a.Transform,
                        a.Geometry, a.Mask, b.Entity, out _, out var grund);
                    if (treffer == ObjectCollisionResult.UnsupportedGeometry)
                    {
                        sonderfaelle++;
                        ParkingLotLiveLog.Zeile("  SONDERFALL "
                            + Show(a.Entity) + " <-> " + Show(b.Entity)
                            + ": " + grund);
                        continue;
                    }
                    if (treffer != ObjectCollisionResult.Exact) continue;
                    var qa = UeGrundriss(a);
                    var qb = UeGrundriss(b);
                    if (!ParkingOverlapGeometry.Measure(qa, qb, out var messung))
                    {
                        // Ein reiner 3D-Treffer ohne messbare Grundflaeche ist
                        // weiterhin ein echtes Paar; sein Mittelpunkt bleibt
                        // deshalb im Bericht, die 2D-Ueberdeckung ist null.
                        messung = new ParkingOverlapMeasure(0, 0,
                            (double2)((a.Transform.m_Position.xz
                                + b.Transform.m_Position.xz) * 0.5f));
                    }
                    ergebnis.Add(new UePaar
                    {
                        A = a,
                        B = b,
                        Messung = messung,
                        Aabb = math.max(float3.zero,
                            math.min(a.Bounds.max, b.Bounds.max)
                            - math.max(a.Bounds.min, b.Bounds.min)),
                        Grund = grund,
                    });
                    if (ergebnis.Count < UePaarGrenze) continue;
                    abgeschnitten = true;
                    return ergebnis;
                }
            }
            return ergebnis;
        }

        private int UeMeldeNetzpartner(UeObjekt objekt, Entity lot)
        {
            if (_netSearchSystem == null) return 0;
            var tree = _netSearchSystem.GetNetSearchTree(true, out var deps);
            deps.Complete();
            using var funde = new NativeList<Entity>(32, Allocator.Temp);
            var iterator = new EntityIterator
            {
                Bounds = objekt.Bounds.xz,
                Results = funde,
            };
            tree.Iterate(ref iterator);
            var gesehen = new HashSet<Entity>();
            var anzahl = 0;
            for (var i = 0; i < funde.Length; i++)
            {
                var netz = funde[i];
                if (!gesehen.Add(netz) || netz == Entity.Null
                    || !EntityManager.Exists(netz)
                    || EntityManager.HasComponent<Temp>(netz)
                    || EntityManager.HasComponent<Deleted>(netz)
                    || !NetCollisionLikeOverride(objekt.Entity, objekt.Transform,
                        objekt.Geometry, objekt.Mask, objekt.Bounds, netz,
                        out var grund)) continue;
                anzahl++;
                var zeile = $"NETZPARTNER zu {Show(objekt.Entity)}: "
                    + $"{Show(netz)} '{PrefabNameOf(netz) ?? "ohne Prefabnamen"}'; "
                    + $"{UeZuordnung(netz, lot)}; {grund}.";
                Mod.log.Warn(UePrefix + " " + zeile);
                ParkingLotLiveLog.Zeile("    " + zeile);
            }
            return anzahl;
        }

        private int UeMeldeFlaechenpartner(UeObjekt objekt, Entity lot)
        {
            if (_areaSearchSystem == null) return 0;
            var tree = _areaSearchSystem.GetSearchTree(true, out var deps);
            deps.Complete();
            using var funde = new NativeList<Entity>(32, Allocator.Temp);
            var iterator = new AreaItemIterator
            {
                Bounds = objekt.Bounds.xz,
                Results = funde,
            };
            tree.Iterate(ref iterator);
            var gesehen = new HashSet<Entity>();
            var anzahl = 0;
            for (var i = 0; i < funde.Length; i++)
            {
                var area = funde[i];
                if (!gesehen.Add(area) || area == Entity.Null
                    || !EntityManager.Exists(area)
                    || EntityManager.HasComponent<Temp>(area)
                    || EntityManager.HasComponent<Deleted>(area)
                    || !AreaCollisionLikeOverride(objekt.Entity, objekt.Transform,
                        objekt.Geometry, objekt.Mask, area, out var flags,
                        out var dreieck)) continue;
                anzahl++;
                var zeile = $"FLAECHENPARTNER zu {Show(objekt.Entity)}: "
                    + $"{Show(area)} '{PrefabNameOf(area) ?? "ohne Prefabnamen"}'; "
                    + $"Flags [{flags}], Dreieck {dreieck}; "
                    + $"{UeZuordnung(area, lot)}.";
                Mod.log.Warn(UePrefix + " " + zeile);
                ParkingLotLiveLog.Zeile("    " + zeile);
            }
            return anzahl;
        }

        private void UeMeldeGebaeude(UeObjekt objekt, Entity lot)
        {
            if (!objekt.Gebaeude) return;
            var building = EntityManager
                .GetComponentData<Game.Buildings.Building>(objekt.Entity);
            var lotSize = EntityManager.HasComponent<BuildingData>(objekt.Prefab)
                ? EntityManager.GetComponentData<BuildingData>(objekt.Prefab).m_LotSize
                : new int2(-1);
            var angebot = UeZoningangebot(building.m_RoadEdge,
                objekt.Transform.m_Position.xz);
            var zeile = $"GEBAEUDE {Show(objekt.Entity)}: Geometrie "
                + $"{UeFloat3(objekt.Geometry.m_Size)} m, Prefab-Lot "
                + (lotSize.x >= 0 ? $"{lotSize.x}x{lotSize.y} Zellen = "
                    + $"{lotSize.x * 8}x{lotSize.y * 8} m" : "unbekannt")
                + $", RoadEdge {Show(building.m_RoadEdge)}, CurvePosition "
                + $"{UeZahl(building.m_CurvePosition, 4)}; {angebot}";
            Mod.log.Info(UePrefix + " " + zeile);
            ParkingLotLiveLog.Zeile("    " + zeile + " | Strasse "
                + UeZuordnung(building.m_RoadEdge, lot));
        }

        private string UeZoningangebot(Entity roadEdge, float2 position)
        {
            if (roadEdge == Entity.Null || !EntityManager.Exists(roadEdge)
                || !EntityManager.HasBuffer<SubBlock>(roadEdge))
                return "kein CS2-Zoningblock an der Gebaeudestrasse lesbar";
            var blocks = EntityManager.GetBuffer<SubBlock>(roadEdge, true);
            var best = Entity.Null;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < blocks.Length; i++)
            {
                var entity = blocks[i].m_SubBlock;
                if (entity == Entity.Null || !EntityManager.Exists(entity)
                    || EntityManager.HasComponent<Deleted>(entity)
                    || !EntityManager.HasComponent<Block>(entity)) continue;
                var block = EntityManager.GetComponentData<Block>(entity);
                var distance = math.distancesq(block.m_Position.xz, position);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = entity;
            }
            if (best == Entity.Null) return "kein gueltiger CS2-Zoningblock gefunden";
            var daten = EntityManager.GetComponentData<Block>(best);
            if (!EntityManager.HasComponent<ValidArea>(best))
                return $"naechster Zoningblock {Show(best)} nominell "
                    + $"{daten.m_Size.x}x{daten.m_Size.y} Zellen; ValidArea fehlt";
            var valid = EntityManager.GetComponentData<ValidArea>(best).m_Area;
            var breite = math.max(0, valid.y - valid.x);
            var tiefe = math.max(0, valid.w - valid.z);
            return $"naechster Zoningblock {Show(best)} bot gueltig "
                + $"{breite}x{tiefe} Zellen = {breite * 8}x{tiefe * 8} m "
                + $"(nominell {daten.m_Size.x}x{daten.m_Size.y})";
        }

        private string UeKontext(Entity lot, float2 punkt,
                                 List<UeObjekt> objekte)
        {
            var funde = new List<string>();
            if (EntityManager.HasBuffer<ParkingLotBuildZoning>(lot))
            {
                var zonen = EntityManager.GetBuffer<ParkingLotBuildZoning>(lot, true);
                for (var i = 0; i < zonen.Length; i++)
                {
                    var ecken = UeZonenecken(zonen[i]);
                    if (!UePunktInPolygon(punkt, ecken)) continue;
                    funde.Add($"Zoning-Parzellenflaeche {i} "
                        + $"({zonen[i].Spalten}x{zonen[i].Reihen} = "
                        + $"{zonen[i].Spalten * 8}x{zonen[i].Reihen * 8} m)");
                }
            }
            for (var i = 0; i < objekte.Count; i++)
            {
                var o = objekte[i];
                if (!o.Eigen || o.Name.IndexOf("Decal",
                        StringComparison.OrdinalIgnoreCase) < 0
                    || !UePunktInPolygon((double2)punkt, UeGrundriss(o))) continue;
                funde.Add("Bucht/Aufkleber " + Show(o.Entity));
                break;
            }
            UeNetzkontext(lot, punkt, funde);
            UeFlaechenkontext(lot, punkt, funde);
            if (funde.Count == 0 && UePunktImLot(lot, punkt))
                funde.Add("im Parkplatzareal, ausserhalb erkannter Bucht, "
                    + "Fahrgasse und Zoning-Parzelle (Rest-/Randflaeche)");
            if (funde.Count == 0) funde.Add("in der unmittelbaren Lot-Umgebung");
            return "Lage: " + string.Join(" + ", funde.Distinct());
        }

        private void UeNetzkontext(Entity lot, float2 punkt, List<string> funde)
        {
            if (!EntityManager.HasBuffer<Game.Net.SubNet>(lot)) return;
            var netze = EntityManager.GetBuffer<Game.Net.SubNet>(lot, true);
            for (var i = 0; i < netze.Length; i++)
            {
                var edge = netze[i].m_SubNet;
                if (edge == Entity.Null || !EntityManager.Exists(edge)
                    || !EntityManager.HasComponent<Curve>(edge)) continue;
                var breite = UeNetzbreite(edge);
                if (breite <= 0 || UeKurvenabstandQuadrat(
                        EntityManager.GetComponentData<Curve>(edge).m_Bezier,
                        punkt) > breite * breite * 0.25f) continue;
                var name = PrefabNameOf(edge) ?? "unbekannter Weg";
                funde.Add(name.StartsWith("PLT Zoningstrasse",
                        StringComparison.OrdinalIgnoreCase)
                    ? "Zoning-Strasse '" + name + "'"
                    : "Fahrgasse/Verbindungsweg '" + name + "'");
            }
        }

        private void UeFlaechenkontext(Entity lot, float2 punkt,
                                       List<string> funde)
        {
            if (!EntityManager.HasBuffer<Game.Areas.SubArea>(lot)) return;
            var areas = EntityManager.GetBuffer<Game.Areas.SubArea>(lot, true);
            for (var i = 0; i < areas.Length; i++)
            {
                var area = areas[i].m_Area;
                if (area == Entity.Null || !EntityManager.Exists(area)
                    || !EntityManager.HasBuffer<Game.Areas.Node>(area)
                    || !EntityManager.HasBuffer<Game.Areas.Triangle>(area)) continue;
                var nodes = EntityManager.GetBuffer<Game.Areas.Node>(area, true);
                var triangles = EntityManager.GetBuffer<Game.Areas.Triangle>(area, true);
                var innen = false;
                for (var t = 0; t < triangles.Length && !innen; t++)
                    innen = MathUtils.Intersect(AreaUtils.GetTriangle3(
                        nodes, triangles[t]).xz, punkt, out _);
                if (!innen) continue;
                funde.Add("eigene Flaeche '"
                    + (PrefabNameOf(area) ?? "ohne Prefabnamen") + "'");
            }
        }

        /**
         * STRASSE AUF STRASSE - der Durchgang, der bisher fehlte.
         *
         * Der Scan fragte bislang nur bei ueberschriebenen OBJEKTEN nach, auf
         * welchem Netz sie stehen. Zwei Fahrbahnen gegeneinander hat er nie
         * geprueft. Seine Zeile "0 Netzpartner" hiess deshalb nicht "keine
         * Strasse liegt auf einer anderen", sondern "danach wurde nicht
         * gefragt".
         *
         * Der Nutzer am 2026-09-04: *"Die Strasse bzw. die Verbindung von
         * Strom, Wasser und Abwasser ueberlagert die andere, anstatt sich dran
         * anzuschliessen. Weil sich das ueberlappt, sind alle Objekte an der
         * RZ-Strasse blockiert."* Seine These war damit nicht widerlegt,
         * sondern ungemessen - dieser Durchgang misst sie.
         *
         * Gerechnet wird ueber die Mittellinien: zwei Fahrbahnen liegen
         * uebereinander, wo ihr Achsabstand kleiner ist als die halbe Summe
         * ihrer Breiten. An einem GEMEINSAMEN KNOTEN ist genau das normal -
         * dort laufen zwei Kanten zusammen -, deshalb bleibt die Umgebung
         * eines geteilten Knotens aussen vor.
         */
        private int UeMeldeStrasseAufStrasse(Entity lot)
        {
            if (lot == Entity.Null || !EntityManager.Exists(lot)) return 0;
            if (!EntityManager.HasBuffer<Game.Net.SubNet>(lot)) return 0;

            var kanten = new List<(Entity E, Bezier4x3 K, float B,
                Entity Start, Entity Ende)>();
            var puffer = EntityManager.GetBuffer<Game.Net.SubNet>(lot, true);
            for (var i = 0; i < puffer.Length; i++)
            {
                var e = puffer[i].m_SubNet;
                if (e == Entity.Null || !EntityManager.Exists(e)) continue;
                if (EntityManager.HasComponent<Temp>(e)
                    || EntityManager.HasComponent<Deleted>(e)) continue;
                if (!EntityManager.HasComponent<Curve>(e)) continue;
                var breite = UeNetzbreite(e);
                if (breite <= 0f) continue;
                var start = Entity.Null;
                var ende = Entity.Null;
                if (EntityManager.HasComponent<Game.Net.Edge>(e))
                {
                    var kante = EntityManager.GetComponentData<Game.Net.Edge>(e);
                    start = kante.m_Start;
                    ende = kante.m_End;
                }
                kanten.Add((e, EntityManager.GetComponentData<Curve>(e).m_Bezier,
                    breite, start, ende));
            }
            if (kanten.Count < 2) return 0;

            const int proben = 24;
            var treffer = 0;
            for (var i = 0; i < kanten.Count; i++)
            for (var k = i + 1; k < kanten.Count; k++)
            {
                var a = kanten[i];
                var b = kanten[k];
                var grenze = (a.B + b.B) * 0.5f;
                // Am gemeinsamen Knoten laufen zwei Kanten zusammen - das ist
                // eine Kreuzung, keine Ueberlagerung. Ein Bogen Abstand.
                var geteilt = a.Start.Equals(b.Start) || a.Start.Equals(b.Ende)
                    || a.Ende.Equals(b.Start) || a.Ende.Equals(b.Ende);
                var schutz = geteilt ? grenze * 1.5f : 0f;

                var laengeA = math.distance(a.K.a.xz, a.K.d.xz);
                var ueberlappt = 0f;
                var engster = float.MaxValue;
                for (var t = 0; t <= proben; t++)
                {
                    var punkt = MathUtils.Position(a.K, t / (float)proben).xz;
                    if (geteilt)
                    {
                        var dStart = math.min(
                            math.distance(punkt, a.K.a.xz),
                            math.distance(punkt, a.K.d.xz));
                        if (dStart < schutz) continue;
                    }
                    var abstand = math.sqrt(
                        UeKurvenabstandQuadrat(b.K, punkt));
                    if (abstand >= grenze) continue;
                    ueberlappt += laengeA / proben;
                    if (abstand < engster) engster = abstand;
                }
                // Eine halbe Fahrbahnbreite Ueberlagerung ist Rauschen an
                // Boegen; darunter wird nicht gemeldet.
                if (ueberlappt < grenze * 0.5f) continue;
                treffer++;
                Mod.log.Warn("PLT-Ueberlappung: STRASSE AUF STRASSE "
                    + UeName(a.E) + " <-> " + UeName(b.E)
                    + $": rund {UeZahl(ueberlappt, 1)} m uebereinander, "
                    + $"engster Achsabstand {UeZahl(engster, 2)} m bei "
                    + $"{UeZahl(grenze * 2f, 1)} m Fahrbahnbreite zusammen; "
                    + (geteilt
                        ? "die beiden teilen einen Knoten - hier waere nur die "
                            + "Kreuzung normal, nicht eine ganze Strecke."
                        : "KEIN gemeinsamer Knoten: zwei getrennte Strassen "
                            + "auf derselben Flaeche."));
            }
            if (treffer == 0)
                Mod.log.Info($"PLT-Ueberlappung: {kanten.Count} eigene "
                    + "Strassenkante(n), keine liegt auf einer anderen.");
            return treffer;
        }

        private string UeName(Entity e)
            => (PrefabNameOf(e) ?? "unbekannt") + " #" + e.Index;

        private float UeNetzbreite(Entity edge)
        {
            if (!EntityManager.HasComponent<Composition>(edge)) return 0;
            var composition = EntityManager.GetComponentData<Composition>(edge).m_Edge;
            return composition != Entity.Null
                && EntityManager.Exists(composition)
                && EntityManager.HasComponent<NetCompositionData>(composition)
                ? EntityManager.GetComponentData<NetCompositionData>(composition).m_Width
                : 0;
        }

        private static float UeKurvenabstandQuadrat(Bezier4x3 curve, float2 punkt)
        {
            var best = float.MaxValue;
            var vorher = curve.a.xz;
            for (var i = 1; i <= 24; i++)
            {
                var jetzt = MathUtils.Position(curve, i / 24f).xz;
                var delta = jetzt - vorher;
                var q = math.lengthsq(delta);
                var t = q <= 1e-10f ? 0
                    : math.clamp(math.dot(punkt - vorher, delta) / q, 0, 1);
                best = math.min(best, math.distancesq(punkt, vorher + delta * t));
                vorher = jetzt;
            }
            return best;
        }

        private string UeZuordnung(Entity entity, Entity lot)
        {
            if (UeGehoertZuLot(entity, lot)) return "unser Lot";
            if (entity != Entity.Null && EntityManager.Exists(entity)
                && EntityManager.HasComponent<Game.Buildings.Building>(entity))
            {
                var building = EntityManager
                    .GetComponentData<Game.Buildings.Building>(entity);
                return UeGehoertZuLot(building.m_RoadEdge, lot)
                    ? "fremdes, an PLT-Zoning-Strasse gewachsenes CS2-Gebaeude"
                    : "fremdes CS2-Gebaeude";
            }
            return "fremd";
        }

        private bool UeGehoertZuLot(Entity entity, Entity lot)
        {
            if (entity == Entity.Null || lot == Entity.Null) return false;
            if (entity == lot) return true;
            if (EntityManager.Exists(entity)
                && EntityManager.HasComponent<ParkingLotPartRelation>(entity)
                && EntityManager.GetComponentData<ParkingLotPartRelation>(entity).Lot
                    == lot) return true;
            var carrier = EntityManager.HasComponent<ParkingLotCarrierReference>(lot)
                ? EntityManager.GetComponentData<ParkingLotCarrierReference>(lot).Carrier
                : Entity.Null;
            var gesehen = new HashSet<Entity>();
            for (var i = 0; i < 64 && entity != Entity.Null
                 && EntityManager.Exists(entity) && gesehen.Add(entity); i++)
            {
                if (entity == lot || entity == carrier) return true;
                if (!EntityManager.HasComponent<Owner>(entity)) break;
                entity = EntityManager.GetComponentData<Owner>(entity).m_Owner;
            }
            return false;
        }

        private Entity UeFindeLot(Entity entity)
        {
            if (UeIstDauerhaftesLot(entity)) return entity;
            if (entity == Entity.Null || !EntityManager.Exists(entity))
                return Entity.Null;
            if (EntityManager.HasComponent<ParkingLotPartRelation>(entity))
            {
                var relation = EntityManager
                    .GetComponentData<ParkingLotPartRelation>(entity);
                if (UeIstDauerhaftesLot(relation.Lot)) return relation.Lot;
            }
            var gesehen = new HashSet<Entity>();
            var current = entity;
            for (var i = 0; i < 64 && current != Entity.Null
                 && EntityManager.Exists(current) && gesehen.Add(current); i++)
            {
                if (UeIstDauerhaftesLot(current)) return current;
                if (!EntityManager.HasComponent<Owner>(current)) break;
                current = EntityManager.GetComponentData<Owner>(current).m_Owner;
            }
            if (EntityManager.HasComponent<Game.Buildings.Building>(entity))
            {
                var edge = EntityManager
                    .GetComponentData<Game.Buildings.Building>(entity).m_RoadEdge;
                return UeFindeLot(edge);
            }
            return Entity.Null;
        }

        private bool UeIstLot(Entity entity)
            => entity != Entity.Null && EntityManager.Exists(entity)
               && EntityManager.HasComponent<ParkingLotBuildReceipt>(entity)
               && EntityManager.HasBuffer<ParkingLotBuildPoint>(entity);

        private bool UeIstDauerhaftesLot(Entity entity)
            => UeIstLot(entity)
               && !EntityManager.HasComponent<Temp>(entity)
               && !EntityManager.HasComponent<Deleted>(entity);

        private Bounds2 UeSuchraum(Entity lot, out string kennung)
        {
            var points = EntityManager.GetBuffer<ParkingLotBuildPoint>(lot, true);
            var min = new float2(float.MaxValue);
            var max = new float2(float.MinValue);
            var site = new List<float2>(points.Length);
            for (var i = 0; i < points.Length; i++)
            {
                var p = points[i].Position.xz;
                site.Add(p);
                min = math.min(min, p);
                max = math.max(max, p);
            }
            kennung = LotId(site);
            if (EntityManager.HasBuffer<ParkingLotBuildZoning>(lot))
            {
                var zonen = EntityManager.GetBuffer<ParkingLotBuildZoning>(lot, true);
                for (var i = 0; i < zonen.Length; i++)
                {
                    var ecken = UeZonenecken(zonen[i]);
                    for (var k = 0; k < ecken.Length; k++)
                    {
                        min = math.min(min, ecken[k]);
                        max = math.max(max, ecken[k]);
                    }
                }
            }
            return new Bounds2(min - UeZoningRand, max + UeZoningRand);
        }

        private static double2[] UeGrundriss(UeObjekt objekt)
        {
            var q = ObjectUtils.CalculateBaseCorners(objekt.Transform.m_Position,
                objekt.Transform.m_Rotation, objekt.Geometry.m_Bounds);
            return new[]
            {
                (double2)q.a.xz, (double2)q.b.xz,
                (double2)q.c.xz, (double2)q.d.xz,
            };
        }

        private static float2[] UeZonenecken(ParkingLotBuildZoning zone)
        {
            var rad = math.radians((float)zone.Winkel);
            var u = new float2(math.cos(rad), math.sin(rad));
            var v = new float2(-u.y, u.x);
            var b = (float)(zone.Spalten * ParkingGeometry.Zoningparzelle);
            var t = (float)(zone.Reihen * ParkingGeometry.Zoningparzelle);
            return new[]
            {
                zone.Ecke,
                zone.Ecke + u * b,
                zone.Ecke + u * b + v * t,
                zone.Ecke + v * t,
            };
        }

        private bool UePunktImLot(Entity lot, float2 punkt)
        {
            var buffer = EntityManager.GetBuffer<ParkingLotBuildPoint>(lot, true);
            var polygon = new float2[buffer.Length];
            for (var i = 0; i < buffer.Length; i++) polygon[i] = buffer[i].Position.xz;
            return UePunktInPolygon(punkt, polygon);
        }

        private static bool UePunktInPolygon(float2 punkt,
                                             IReadOnlyList<float2> polygon)
        {
            var innen = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var a = polygon[j];
                var b = polygon[i];
                if ((a.y > punkt.y) == (b.y > punkt.y)) continue;
                var x = a.x + (punkt.y - a.y) / (b.y - a.y) * (b.x - a.x);
                if (punkt.x < x) innen = !innen;
            }
            return innen;
        }

        private static bool UePunktInPolygon(double2 punkt,
                                             IReadOnlyList<double2> polygon)
        {
            var innen = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var a = polygon[j];
                var b = polygon[i];
                if ((a.y > punkt.y) == (b.y > punkt.y)) continue;
                var x = a.x + (punkt.y - a.y) / (b.y - a.y) * (b.x - a.x);
                if (punkt.x < x) innen = !innen;
            }
            return innen;
        }

        private string UeBesitzerkette(Entity entity)
        {
            var teile = new List<string>();
            if (EntityManager.HasComponent<ParkingLotPartRelation>(entity))
            {
                var r = EntityManager.GetComponentData<ParkingLotPartRelation>(entity);
                teile.Add("Relation Lot=" + Show(r.Lot) + ", Carrier=" + Show(r.Carrier));
            }
            if (EntityManager.HasComponent<Game.Objects.Attached>(entity))
                teile.Add("Attached=" + Show(EntityManager
                    .GetComponentData<Game.Objects.Attached>(entity).m_Parent));
            var gesehen = new HashSet<Entity>();
            var current = entity;
            for (var i = 0; i < 64 && current != Entity.Null
                 && EntityManager.Exists(current) && gesehen.Add(current); i++)
            {
                if (!EntityManager.HasComponent<Owner>(current)) break;
                current = EntityManager.GetComponentData<Owner>(current).m_Owner;
                teile.Add("Owner->" + Show(current) + " '"
                    + (PrefabNameOf(current) ?? "ohne Prefabnamen") + "'");
            }
            return teile.Count == 0 ? "keine Besitzerbeziehung" : string.Join("; ", teile);
        }

    }
}
