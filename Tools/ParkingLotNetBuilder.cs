using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * Legt die unsichtbaren Fahrwege des Parkplatzes an.
     *
     * WARUM UNSICHTBAR: die sichtbare Breite ist unser eigener Asphalt. Der
     * Weg legt nur fest, WO Autos fahren duerfen. Deshalb darf der
     * befahrbare Kern schmaler sein als der Belag - aber niemals breiter,
     * sonst fuehren Autos ueber die Wiese.
     *
     * ANBINDUNG AN DIE STADT: passiert von selbst, aber NICHT ueber eine
     * Kreuzung. Die unsichtbaren Wege liegen auf `MarkerPathway`, und diese
     * Ebene steht nicht in `m_IntersectLayers` normaler Strassen -
     * `CourseSplitSystem` steigt an `NetUtils.CanConnect` aus und splittet
     * nichts. Was greift, ist `LocalConnect`: `NetInitializeSystem` gibt
     * JEDEM PathwayPrefab `m_Layers |= Layer.Road` und `m_SearchDistance = 4`,
     * der RoadPrefab-Block setzt spiegelbildlich
     * `m_LocalConnectLayers |= Pathway | MarkerPathway`. `GenerateEdgesSystem`
     * verbindet dann jeden Knoten mit `Net.LocalConnect`, dessen Abstand
     * `Strassenbreite/2 + Wegbreite/2 + 4 m` unterschreitet (und noch 8 m
     * grosszuegiger, sobald der Weg einen Besitzer hat).
     *
     * Die eine Bedingung ist `LocalConnectFlags.RequireDeadend`: der Knoten
     * darf nur EINE Kante tragen. Genau das liefert unsere Regel, dass zwei
     * Strassen sich nie ueberlappen duerfen - die Fahrgasse endet an der
     * KANTE der Randstrasse, also rangrenzend statt einmuendend. Der Rest
     * ist Sache des Spiels.
     *
     * Reine Fusswege nutzen den eigenen PLT-Klon. Dessen LocalConnect-
     * Suchmaske ist nach NetInitialize 0: sonst werden beim Nutzerfall vier
     * freie Enden bis zur Stadtstrasse verlaengert. Die Auto-Prefabs bleiben
     * Vanilla; Suchweite 0 allein wuerde den halben Breitenradius nicht sperren.
     */
    public sealed partial class ParkingLotToolSystem
    {
        /**
         * Die auswaehlbaren Kernbreiten, aus den Querschnittsteilen im
         * Laufzeit-Dump gemessen. Mehr gibt es nicht: 2, 3, 4,
         * 6 und 7 m. Der 2-m-Fussweg fehlt hier bewusst, ueber ihn faehrt
         * kein Auto.
         *
         * NICHT genommen wird `Invisible Road Path - 2xTwoway 2xPerpendicular`
         * (ebenfalls 7 m): die Variante bringt eigene Parkspuren mit, unsere
         * Stellplaetze kommen aus den Decals. Der Vanilla-Parkplatz benutzt
         * denselben schlichten 2xTwoway.
         */
        private static readonly (string Name, float Width)[] DrivablePaths =
            ParkingLotFahrregeln.Wege.Take(4).Select(p => (p.Quelle, p.Breite)).ToArray();

        /*
         * Im Laufzeitdump vom 2026-08-09 aus den Stueckbreiten gemessen:
         * 1xOneway = 3,0 m mittige Autospur + 2 * 0,5 m Gehabschnitt;
         * der reine Fussweg besteht aus einem mittigen 2,0-m-Stueck.
         */
        private static string OnewayPathName => ParkingLotFahrregeln.Wege[4].Quelle;
        private static float OnewayPathWidth => ParkingLotFahrregeln.Wege[4].Breite;
        private const float PedestrianPathWidth = 2f;

        /**
         * Wie weit die Gasse ueber den Fahrbahnrand hinausragen MUSS.
         *
         * Gemessen am 2026-09-17 mit der Bordsteinsonde an einer 16-m-Strasse:
         * von der Mittellinie aus wurde ab 8,56 m angenommen, bei 8,53 m kam
         * "InvalidShape". Der Ueberstand ueber den Fahrbahnrand betraegt dort
         * also 0,56 m. Zwei Meter sind die aufgerundete Sicherheitsmarge -
         * ob die Schwelle bei schmaleren Strassen absolut oder anteilig
         * wirkt, ist NICHT gemessen.
         */
        private const float GassenUeberstand = 2f;

        /**
         * Suchweite fuer die Stadtstrasse am aeusseren Zufahrtsende.
         *
         * Groesser als die LocalConnect-Reichweite des Weges (Breite/2 + 4 m),
         * denn eine Zufahrt, die weiter weg liegt, ist ohnehin schon heute
         * nicht angeschlossen - dann soll die Meldung das sagen und nicht
         * die Suche vorher aufgeben.
         */
        private const float GassenSuchweite = 40f;



        private EntityQuery _gassenStrassen;

        /**
         * Die STADTSTRASSE, die eine Gasse in ihrer Fangrichtung erreicht.
         *
         * Bis zum 2026-09-24 kam hier der NAECHSTE Punkt der naechsten
         * Strasse im Umkreis von 40 m zurueck. An einer schraegen Strasse
         * liegt der senkrecht zur Strasse - die Gasse knickte vom Fang weg,
         * und eine Strasse, die in Fangrichtung weiter weg lag als senkrecht,
         * wurde falsch getroffen. Die Regel steht jetzt in
         * `Gassenreichweite`: Strahl ab Polygonrand in Fangrichtung,
         * Bordstein hoechstens 16 m entfernt.
         *
         * `Owner` schliesst unsere eigenen Kanten aus - an einer Zoning- oder
         * Randstrasse des Parkplatzes hat eine Zufahrtsgasse nichts zu
         * suchen, und ein Knoten dort wuerde nur unser eigenes Netz teilen.
         */
        private bool SucheStadtstrasseFuerGasse(float2 rand, float2 nachAussen,
                                                out float2 mitte,
                                                out float halbeBreite,
                                                out float halbeBreiteImStrahl,
                                                out Entity strasse, out float t,
                                                out float hoehe)
        {
            mitte = default;
            halbeBreite = 0f;
            halbeBreiteImStrahl = 0f;
            strasse = Entity.Null;
            t = 0f;
            hoehe = 0f;
            if (_gassenStrassen == default)
                _gassenStrassen = GetEntityQuery(
                    ComponentType.ReadOnly<Game.Net.Edge>(),
                    ComponentType.ReadOnly<Curve>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.Exclude<Owner>(),
                    ComponentType.Exclude<Deleted>(),
                    ComponentType.Exclude<Temp>());

            var kandidaten = new List<Gassenreichweite.Strasse>();
            var kanten = new List<Entity>();
            var boegen = new List<Colossal.Mathematics.Bezier4x3>();
            using var alle = _bauarbeiter ? LokaleHintergrundStrassen(rand) : _gassenStrassen.ToEntityArray(Allocator.TempJob);
            for (var i = 0; i < alle.Length; i++)
            {
                if (!EntityManager.HasComponent<Game.Net.Edge>(alle[i]) || !EntityManager.HasComponent<PrefabRef>(alle[i])
                    || EntityManager.HasComponent<Owner>(alle[i]) || EntityManager.HasComponent<Deleted>(alle[i])
                    || EntityManager.HasComponent<Temp>(alle[i])) continue;
                var prefab = EntityManager.GetComponentData<PrefabRef>(alle[i]).m_Prefab;
                if (!EntityManager.HasComponent<RoadData>(prefab)) continue;
                var bogen = EntityManager.GetComponentData<Curve>(alle[i]).m_Bezier;
                // Grobfilter: weiter weg als Reichweite plus breiteste Strasse
                // kann keine Mittellinie im Strahl liegen.
                if (MathUtils.Distance(bogen.xz, rand, out _)
                    > GassenSuchweite) continue;
                var halb = EntityManager.HasComponent<NetGeometryData>(prefab)
                    ? EntityManager.GetComponentData<NetGeometryData>(prefab)
                        .m_DefaultWidth * 0.5f
                    : 4f;
                var linie = new float2[GassenAbtastung + 1];
                for (var k = 0; k <= GassenAbtastung; k++)
                    linie[k] = MathUtils.Position(bogen, k / (float)GassenAbtastung).xz;
                kandidaten.Add(new Gassenreichweite.Strasse
                {
                    Mittellinie = linie,
                    HalbeBreite = halb,
                    Kennung = kanten.Count,
                });
                kanten.Add(alle[i]);
                boegen.Add(bogen);
            }

            if (!Gassenreichweite.Finde(rand, nachAussen, kandidaten,
                    out var treffer)) return false;

            // Beides nur zum Nachmessen: WELCHE Kante wir getroffen haben und
            // WO auf ihr. Nahe 0 oder 1 heisst Kantenende, also ein
            // vorhandener Knoten statt einer Teilung.
            strasse = kanten[treffer.Kennung];
            /*
             * AUF DIE ECHTE KURVE, NICHT AUF DIE ABTASTUNG.
             *
             * Der Strahltest arbeitet auf 16 Sehnen je Kurve. CS2 teilt die
             * Strasse aber an einer Stelle der BEZIERKURVE - der Punkt muss
             * also auf ihr liegen, sonst entsteht genau der Versatz, den der
             * Gassenbefund am 2026-09-24 meldete (0,10 und 0,26 m). Gesucht
             * wird die Kurvenstelle, die dem Strahltreffer am naechsten ist;
             * bei einer geraden Strasse ist das derselbe Punkt.
             */
            var bogenTreffer = boegen[treffer.Kennung];
            MathUtils.Distance(bogenTreffer.xz, treffer.Mitte, out t);
            var aufKurve = MathUtils.Position(bogenTreffer, t);
            // Die HOEHE der Fahrbahn an dieser Stelle - nicht die des
            // Gelaendes darunter, das dort weggeschnitten ist.
            hoehe = aufKurve.y;
            mitte = aufKurve.xz;
            // Die Breite steht am Prefab, nicht an den Querschnitten -
            // NetCompositionSystem Zeile 151 nimmt `m_DefaultWidth`.
            halbeBreite = kandidaten[treffer.Kennung].HalbeBreite;
            halbeBreiteImStrahl = treffer.HalbeBreiteImStrahl;
            return true;
        }

        /** Stuecke je Strassenkurve fuer den Strahltest. */
        private const int GassenAbtastung = 16;

        /**
         * Woran ein Kursende andockt - so, wie CS2s Strassenwerkzeug es
         * uebergibt (Game.dll, NetToolSystem.GetCoursePos): die KANTE samt
         * Teilungsstelle, oder deren Endknoten, wenn die Stelle am Ende liegt.
         * `default` heisst: kein Anschluss, CS2 verbindet ueber die Position.
         */
        private struct Anschluss
        {
            internal Entity Entity;
            internal float Teilung;
        }

        /**
         * Naeher als das an einem Kantenende, und die Gasse haengt sich an
         * den vorhandenen Knoten statt die Kante zu teilen. CS2s Werkzeug
         * macht dasselbe (`m_CurvePosition <= 0` / `>= 1`); bei uns kommt die
         * Stelle aus einer Rechnung, deshalb eine kleine Toleranz. Beim Edit
         * ist das der Normalfall: die alte Gasse hat die Strasse schon
         * geteilt, ihr Knoten ueberlebt den Abriss, und der Strahl trifft
         * ihn wieder (Gassenbefund 2026-09-24: t=0,019, "AM KANTENENDE").
         */
        private const float GassenKnotenfang = 0.5f;

        /**
         * `position` liegt auf der Kurve von `kante` bei `t`. Liefert den
         * Anschluss und schiebt `position` auf den Knoten, falls einer
         * gewaehlt wird - der Kurs muss GENAU dort beginnen.
         */
        private Anschluss AnschlussAnStrasse(Entity kante, float t,
            ref float3 position)
        {
            if (kante == Entity.Null || !EntityManager.Exists(kante)
                || !EntityManager.HasComponent<Game.Net.Edge>(kante))
                return default;
            var edge = EntityManager.GetComponentData<Game.Net.Edge>(kante);
            foreach (var (knoten, teilung) in new[] { (edge.m_Start, 0f), (edge.m_End, 1f) })
            {
                if (knoten == Entity.Null || !EntityManager.Exists(knoten)
                    || EntityManager.HasComponent<Deleted>(knoten)
                    || !EntityManager.HasComponent<Game.Net.Node>(knoten))
                    continue;
                var lage = EntityManager.GetComponentData<Game.Net.Node>(knoten).m_Position;
                if (math.distance(lage.xz, position.xz) > GassenKnotenfang) continue;
                position = lage;
                return new Anschluss { Entity = knoten, Teilung = teilung };
            }
            return new Anschluss { Entity = kante, Teilung = t };
        }

        /**
         * Legt das aeussere Gassenstueck einer Gassen-Zufahrt an.
         *
         * Rueckgabe ist die Zahl erzeugter Teilkurse, also 0 oder mehr. Faellt das
         * Stueck aus, wird die Zufahrt trotzdem gebaut - sie verhaelt sich
         * dann wie eine gewoehnliche Zufahrt, nur eben ohne geoeffneten
         * Bordstein. Der Grund steht im Bauzettel.
         */
        private int CreateGassenstueck(NetSegment piece, int index, float vorflaechenbreite,
            ref TerrainHeightData heightData, Dictionary<(long,long),float> heights,
            ref Unity.Mathematics.Random random, List<string> bericht)
        {
            Gassenkurs p;
            if (_bauarbeiter) _hintergrundGassenkurse.TryGetValue(index,out p);
            else if (!PlaneGassenkurs(piece,index,vorflaechenbreite,heights,bericht,out p)) return 0;
            if (p == null) return 0;
            MerkeGassenplan(index,p.Mitte,p.Ende,p.Strasse,p.T,piece.B-piece.A,
                p.HalbeBreite,p.Prefab,vorflaechenbreite,Zufahrtsarten.FaehrtHinaus(piece.Art),piece.Art);
            if (_bauarbeiter && _erhalteneKursketten.ContainsKey(("entrance-gasse",index)))
            {
                bericht.Add($"Zufahrt {index}: unveraenderte Gasse erhalten, Stadtanschluss {p.Anschluss.Entity}; 0 Abriss/Neubau/Teilung.");
                return 0;
            }
            if (_bauarbeiter && EntityManager.HasComponent<Edge>(p.Anschluss.Entity))
            {
                _nichtBaubareHintergrundGassen++;
                ParkingLotNetzRueckweg.Melde($"Zufahrt {index}: neue Stadtteilung waere erforderlich: Kante {p.Anschluss.Entity}, t={p.Anschluss.Teilung:F6}, Lage {p.Mitte}; 0 Teilungen, Kurs nicht ausgegeben, Rueckweg erforderlich.");
                return 0;
            }
            // Ein Stummel endet frei im Gelaende; nur ein Strassenanschluss hat eine Zielhoehe.
            if (p.Strasse != Entity.Null) MerkeHoehe(p.Mitte,p.Hoehe,heights);
            if (!CreateCourseDefinition("entrance-gasse",index,p.Von,p.Nach,p.Prefab,
                    ref heightData,heights,ref random,
                    anschlussAnfang: Zufahrtsarten.FaehrtHinaus(piece.Art) ? default : p.Anschluss,
                    anschlussEnde: Zufahrtsarten.FaehrtHinaus(piece.Art) ? p.Anschluss : default))
            {
                bericht.Add($"Zufahrt {index}: Gassenkurs abgelehnt ({p.Laenge:F2} m)");
                return 0;
            }
            if (p.Strasse == Entity.Null)
            {
                bericht.Add($"Zufahrt {index}: Stummel {p.Laenge:F2} m bis zur Fahrgasse gebaut, Ende frei.");
                return 1;
            }
            bericht.Add($"Zufahrt {index}: Gasse {p.Laenge:F2} m in einem Kurs ab Strassenmitte "
                + $"bis zur Fahrgasse, Fahrbahnrand bei {p.HalbeBreite:F2} m, Ueberstand {p.Laenge-p.HalbeBreite:F2} m"
                + (p.Laenge > p.Abstand+1e-3f ? $", davon {p.Laenge-p.Abstand:F2} m im Parkplatz" : string.Empty));
            return 1;
        }

        private bool TryResolvePedestrianPath(out Entity prefab, bool gesetzterZugang = false)
        {
            var system = World.GetOrCreateSystemManaged<ParkingLotFusswegPrefabSystem>();
            prefab = gesetzterZugang ? system.ZugangBereit : system.Bereit;
            if (prefab != Entity.Null) return true;
            Mod.log.Warn("PLT-Bauzettel: Fusswegklon noch nicht bereit; Fusskurs entfaellt.");
            return false;
        }

        private EntityQuery _pathPrefabQuery;
        private EntityQuery _zoningRoadQuery;
        private ParkingLotZoningRoadPrefabSystem _zoningRoadPrefabSystem;
        private bool _missingZoningRoadLogged;
        private readonly Dictionary<string, Entity> _zoningRoadOriginals =
            new Dictionary<string, Entity>(StringComparer.Ordinal);
        private readonly Dictionary<string, Entity> _pathPrefabs =
            new Dictionary<string, Entity>(StringComparer.Ordinal);
        private bool _missingPathPrefabLogged;

        private void InitializeNetBuilder()
        {
            _pathPrefabQuery = GetEntityQuery(
                ComponentType.ReadOnly<PathwayData>(),
                ComponentType.ReadOnly<NetGeometryData>(),
                ComponentType.ReadOnly<NetData>(),
                ComponentType.Exclude<PlaceholderObjectElement>());
            // Die Zoning-Strasse braucht ein ROAD-Prefab; nur die tragen
            // einen Zonenblock.
            _zoningRoadQuery = GetEntityQuery(
                ComponentType.ReadOnly<RoadData>(),
                ComponentType.ReadOnly<NetGeometryData>(),
                ComponentType.ReadOnly<NetData>(),
                ComponentType.Exclude<PlaceholderObjectElement>());
        }

        /**
         * Groesster verfuegbarer Kern, der noch in den Belag passt.
         *
         * Aufgerundet waere der Kern breiter als der Asphalt und Autos
         * fuehren neben der Fahrbahn. Nach unten ist bei 3 m Schluss, das
         * ist die schmalste Zweirichtungsvariante, die CS2 anbietet.
         */
        private bool TryChooseDrivablePath(double width, out Entity prefab,
                                           out float coreWidth)
        {
            prefab = Entity.Null;
            coreWidth = 0f;
            if (!ResolvePathPrefabs()) return false;

            for (var i = DrivablePaths.Length - 1; i >= 0; i--)
            {
                var candidate = DrivablePaths[i];
                // Die 1e-3 fangen ab, dass 7.0 als float minimal unter 7 liegt.
                if (candidate.Width > width + 1e-3) continue;
                if (!_pathPrefabs.TryGetValue(candidate.Name, out var entity)) continue;
                prefab = entity;
                coreWidth = candidate.Width;
                return true;
            }

            var narrowest = DrivablePaths[0];
            if (!_pathPrefabs.TryGetValue(narrowest.Name, out prefab)) return false;
            coreWidth = narrowest.Width;
            return true;
        }

        private bool ResolvePathPrefabs()
        {
            var complete = true;
            for (var i = 0; i < DrivablePaths.Length; i++)
                if (!_pathPrefabs.ContainsKey(DrivablePaths[i].Name)) complete = false;
            if (complete) return true;

            var fahrprefabs = World.GetOrCreateSystemManaged<ParkingLotFahrprefabSystem>();
            foreach (var weg in DrivablePaths)
                if (fahrprefabs.TryWeg(weg.Name, out var entity)) _pathPrefabs[weg.Name] = entity;

            var missing = new List<string>();
            for (var i = 0; i < DrivablePaths.Length; i++)
                if (!_pathPrefabs.ContainsKey(DrivablePaths[i].Name))
                    missing.Add(DrivablePaths[i].Name);
            if (missing.Count == 0)
            {
                _missingPathPrefabLogged = false;
                return true;
            }

            // Ohne das breiteste Prefab waehlen wir stillschweigend zu schmal.
            // Deshalb wird jedes fehlende genannt, aber nur einmal.
            if (!_missingPathPrefabLogged)
            {
                _missingPathPrefabLogged = true;
                RecordPreviewDiagnostic("Warning",
                    "Unsichtbare Wege fehlen noch: " + string.Join(", ", missing));
                Mod.log.Warn("PLT wartet auf unsichtbare Weg-Prefabs: "
                    + string.Join(", ", missing));
            }
            return _pathPrefabs.Count > 0;
        }

        /** Loest ein Sonderprefab erst dann auf, wenn eine neue Zugangsart es braucht. */
        private bool TryResolvePathPrefab(string name, out Entity entity)
        {
            if (ParkingLotFahrregeln.IstFahrweg(name))
                return World.GetOrCreateSystemManaged<ParkingLotFahrprefabSystem>().TryWeg(name, out entity);
            if (_pathPrefabs.TryGetValue(name, out entity)) return true;
            // Nebenbei den bestaetigten Altbestand fuellen; sein Verhalten
            // darf nicht davon abhaengen, ob ein Sonderprefab schon da ist.
            ResolvePathPrefabs();
            if (_pathPrefabs.TryGetValue(name, out entity)) return true;

            using var prefabs = _pathPrefabQuery.ToEntityArray(Allocator.TempJob);
            for (var i = 0; i < prefabs.Length; i++)
            {
                var candidate = prefabs[i];
                if (!_prefabSystem.TryGetPrefab<PrefabBase>(candidate, out var prefab)
                    || prefab == null || !prefab.isBuiltin
                    || !string.Equals(prefab.name, name, StringComparison.Ordinal))
                    continue;
                _pathPrefabs[name] = candidate;
                entity = candidate;
                return true;
            }

            entity = Entity.Null;
            Mod.log.Warn("PLT wartet auf unsichtbares Weg-Prefab: " + name);
            return false;
        }

        /**
         * Das unsichtbare Strassenprefab fuer die Zoning-Strasse.
         *
         * Zwei Schritte, und beide muessen sein: erst das Vanilla-Vorbild
         * suchen, dann bei ParkingLotZoningRoadPrefabSystem den
         * ausgeblendeten Klon bestellen. Der Klon entsteht in PrefabUpdate
         * und ist einen Zyklus spaeter da; bis dahin wird die Zoning-Strasse
         * uebersprungen und beim naechsten Bau nachgeholt.
         *
         * Ein unsichtbarer FUSSWEG ginge hier nicht: `PathwayPrefab` bringt
         * keinen Zonenblock mit, und ohne Block waechst nichts.
         */
        private bool TryResolveZoningRoad(string name, out Entity prefab)
            => TryResolveStrassenklon(name, Strassenklonart.Zoning, out prefab);

        /**
         * BEIDE GASSEN KOMMEN AUS DERSELBEN VANILLA-STRASSE.
         *
         * Nicht aus "Alley" und "Alley Oneway": letztere bringt links und
         * rechts je 2,5 m Parkstreifen mit, auf denen Fahrzeuge mitten in
         * der Zufahrt parken. Gemessen am 2026-09-18, und der Nutzer hat
         * es im Spiel bestaetigt.
         *
         * Stattdessen entsteht die gerichtete Gasse aus derselben "Alley",
         * deren beide Fahrspuren im Klon auf dieselbe Richtung gedreht
         * werden. Zwei Spuren statt einer, dafuer ohne Parkgasse - der
         * Vorschlag des Nutzers.
         */
        private bool TryResolveZufahrtsgasse(Zufahrtsart art, out Entity prefab)
            => TryResolveStrassenklon(
                "Alley",
                art == Zufahrtsart.Gasse
                    ? Strassenklonart.Zufahrtsgasse
                    : Strassenklonart.ZufahrtsgasseEinbahn,
                out prefab);

        /*
         * false = wir bauen mit UNSEREM Klon, nicht mit dem Spiel-Prefab.
         *
         * Der fruehere Kommentar hier behauptete das Gegenteil ("Zufahrten
         * verwenden dauerhaft das unveraenderte Vanilla-Alley-Prefab") und
         * hat am 2026-09-23 eine Fehlersuche in die falsche Richtung
         * geschickt: Weil unsere Gasse dieses Prefab gar nicht benutzt,
         * konnte ein Eingriff daran unseren Parkplaetzen nie helfen - er
         * traf nur die von Hand gesetzten Gassen des Nutzers. Siehe
         * `GasseOhneGelaendeschnitt` in ParkingLotZoningRoadPrefab.cs.
         *
         * Der Grund fuer den Klon bleibt gueltig: ein Klon haelt eigene
         * Sections und Pieces, das Spiel-Prefab teilt sie mit dem globalen
         * CS2-Prefabcache.
         */
        private const bool VerwendeVanillaAlley = false;

        private bool TryResolveStrassenklon(string name, Strassenklonart art,
            out Entity prefab)
        {
            prefab = Entity.Null;
            if (string.IsNullOrEmpty(name)) return false;

            if (_zoningRoadQuery.IsEmptyIgnoreFilter) return false;
            // Gemerkt, weil diese Suche pro Frame laufen kann: das Werkzeug
            // waermt den Klon schon beim Zeichnen vor, damit der erste Bau
            // die Strasse hat und nicht erst der zweite.
            if (!_zoningRoadOriginals.TryGetValue(name, out var original))
            {
                original = Entity.Null;
                using (var kandidaten =
                       _zoningRoadQuery.ToEntityArray(Allocator.TempJob))
                {
                    for (var i = 0; i < kandidaten.Length; i++)
                    {
                        if (!_prefabSystem.TryGetPrefab<PrefabBase>(
                                kandidaten[i], out var vorbild)
                            || vorbild == null
                            || !string.Equals(vorbild.name, name,
                                StringComparison.Ordinal))
                            continue;
                        original = kandidaten[i];
                        break;
                    }
                }
                if (original != Entity.Null)
                    _zoningRoadOriginals[name] = original;
            }
            if (original == Entity.Null)
            {
                if (!_missingZoningRoadLogged)
                {
                    _missingZoningRoadLogged = true;
                    Mod.log.Warn("PLT-Zoning: Strassenprefab '" + name
                        + "' nicht gefunden; die Zoning-Strasse entfaellt.");
                }
                return false;
            }

            if (VerwendeVanillaAlley && art != Strassenklonart.Zoning)
            {
                prefab = original;
                return true;
            }

            _zoningRoadPrefabSystem ??= World
                .GetOrCreateSystemManaged<ParkingLotZoningRoadPrefabSystem>();
            prefab = _zoningRoadPrefabSystem.FordereAn(original, art,
                out var fehlgeschlagen, out var aufgegeben);
            if ((fehlgeschlagen || aufgegeben) && !_missingZoningRoadLogged)
            {
                _missingZoningRoadLogged = true;
                Mod.log.Warn("PLT-Zoning: Der unsichtbare Klon von '" + name
                    + "' konnte nicht angemeldet werden; Grund siehe "
                    + "'PLT-Zoningstrasse'.");
            }
            return prefab != Entity.Null;
        }

        /**
         * Bestellt den Strassenklon, sobald ueberhaupt eine Zoning-Flaeche
         * gezeichnet ist.
         *
         * Ohne das faellt die Zoning-Strasse beim ERSTEN Bau aus: der Klon
         * entsteht in PrefabUpdate und ist erst im naechsten Zyklus
         * benutzbar. Der Nutzer haette zweimal bauen muessen, ohne zu
         * wissen warum.
         */
        internal void WaermeZoningstrasseVor(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            TryResolveZoningRoad(name, out _);
        }

        /**
         * Alle Fahrwege des Layouts als Kurs-Definitionen.
         *
         * Reihenfolge egal: das Spiel verschmilzt Segmente mit identischem
         * Endpunkt selbst zu einem Knoten. Deshalb wird die Hoehe je 2D-Punkt
         * nur EINMAL abgetastet - zwei Segmente an derselben Ecke muessen
         * exakt denselben 3D-Punkt melden, sonst entstehen zwei Knoten
         * uebereinander statt einer Kreuzung.
         */
        /**
         * WARUM HIER KEINE BUCHT STEHT - als Zahl, nicht als Vermutung.
         *
         * Der Nutzer sieht im Spiel nur, was gebaut wurde; was abgelehnt wurde,
         * hinterlaesst keine Spur. Am 2026-08-17 kostete genau das eine Stunde
         * Suche: die komplette aeussere Randreihe fehlte, und erst eine
         * handgebaute Sonde in den Ablehnungszweigen zeigte, dass 124 von 134
         * Buchten an "liegt innerhalb des Rings" scheiterten - Folge eines
         * Richtungstests, der bei im Uhrzeigersinn gezeichneten Arealen kippte.
         *
         * Zu jedem Grund steht die ERSTE Fundstelle dabei, damit man im Spiel
         * hinfliegen und nachsehen kann.
         */
        /*
         * DER INNERE GASSENKNOTEN WIRD NICHT AUFS GELAENDE GELEGT.
         *
         * Befund 2026-09-26 (Messung "PLT-Gassenhoehe", Dekompilat, zwei
         * Codex-Berichte in _codex-gassenknoten2): Gasse und unsichtbare Wege
         * teilen am inneren Ende einen NEUEN Knoten; sein Prefab kommt vom
         * zuletzt verarbeiteten Kurs (`GenerateNodesSystem.CollectUpdatesJob`).
         * Traegt er einen Weg, legt `GroundHeightSystem` ihn aufs Gelaende,
         * das die Gasse dort beschnitten hat: 0,3-0,5 m tiefer je Bau, und
         * jeder Edit erbte es. Trug er zufaellig die Gasse, blieb er oben
         * (5 von 5 beim ersten Bau).
         *
         * Gescheitert und zurueckgenommen:
         *   - `CoursePos.m_Elevation` 1 mm: `CourseSplitSystem.
         *     CalculateElevation` misst neu und setzt < 2 m auf null.
         *   - Gasse nach dem Apply ueberbauen: Prefab stimmt dann, aber zu
         *     spaet; CS2 behaelt die (schon gesunkene) Hoehe.
         *   - Erst Wege, dann Gasse angedockt: der Wegknoten traegt
         *     `Standalone` + Owner, CS2 behaelt sein Prefab - Gassen als Bruecke.
         *
         * Jetzt: `Game.Net.Elevation(0)` an den Temp-Knoten VOR dem Apply,
         * derselbe Zeitpunkt wie unser Owner an den Temps.
         * `GroundHeightSystem.NetIterator.Iterate` laesst jeden Knoten mit
         * einer `Elevation` aus, ohne den Wert zu pruefen;
         * `ApplyNetSystem.Create` entfernt nur `Temp`; ein Nullwert erreicht in
         * `NetCompositionHelpers.GetElevationFlags` keine Schwelle, also keine
         * Bruecke. Vanilla-Knoten mit `m_ParentMesh` tragen genau das.
         */
        private readonly List<float2> _gassenenden = new();

        private void MerkeGassenenden(ParkingLayout layout)
        {
            _gassenenden.Clear();
            foreach (var piece in layout.NetLine)
                if (string.Equals(piece.Kind, "entrance", StringComparison.Ordinal)
                    && Zufahrtsarten.IstGasse(piece.Art))
                    // Nach aussen ist A, innen B (siehe `CreateGassenstueck`).
                    _gassenenden.Add(piece.B);
        }

        /** Vor dem Apply, nach dem Besitzer: die Temp-Knoten an den inneren Gassenenden schuetzen. */
        private void SchuetzeGassenknoten()
        {
            if (_gassenenden.Count == 0) return;
            var query = GetEntityQuery(ComponentType.ReadOnly<Game.Net.Node>(),
                ComponentType.ReadOnly<Temp>(), ComponentType.Exclude<Deleted>());
            using var knoten = query.ToEntityArray(Allocator.Temp);
            int geschuetzt = 0, mitOriginal = 0, gasse = 0;
            var gefunden = new HashSet<int>();
            foreach (var k in knoten)
            {
                var lage = EntityManager.GetComponentData<Game.Net.Node>(k).m_Position.xz;
                var ende = -1;
                for (var i = 0; i < _gassenenden.Count && ende < 0; i++)
                    if (math.distance(_gassenenden[i], lage) < 0.05f) ende = i;
                if (ende < 0 || !HaengtAnGasse(k)) continue;
                gefunden.Add(ende);
                if (EntityManager.GetComponentData<Temp>(k).m_Original != Entity.Null)
                {
                    // Ein vorhandener Knoten wird beim Apply aktualisiert, und
                    // dabei kopiert CS2 `Elevation` nicht mit.
                    mitOriginal++;
                    continue;
                }
                if (GassenPrefab.Ist(_prefabSystem, EntityManager.GetComponentData<PrefabRef>(k).m_Prefab)) gasse++;
                if (!EntityManager.HasComponent<Game.Net.Elevation>(k))
                    EntityManager.AddComponentData(k, new Game.Net.Elevation(float2.zero));
                geschuetzt++;
            }
            var text = $"PLT-Gassenknoten: {geschuetzt} von {_gassenenden.Count} inneren Gassenende(n) "
                + $"vor dem Apply mit Elevation 0 geschuetzt ({gasse} davon tragen ohnehin das Gassen-Prefab); "
                + $"{mitOriginal} mit vorhandenem Original, {_gassenenden.Count - gefunden.Count} ohne Temp-Knoten.";
            if (geschuetzt < _gassenenden.Count) Mod.log.Warn(text); else Mod.log.Info(text);
        }

        private bool HaengtAnGasse(Entity knoten)
        {
            if (!EntityManager.HasBuffer<ConnectedEdge>(knoten)) return false;
            var puffer = EntityManager.GetBuffer<ConnectedEdge>(knoten, true);
            for (var i = 0; i < puffer.Length; i++)
                if (EntityManager.HasComponent<PrefabRef>(puffer[i].m_Edge)
                    && GassenPrefab.Ist(_prefabSystem, EntityManager.GetComponentData<PrefabRef>(puffer[i].m_Edge).m_Prefab))
                    return true;
            return false;
        }

    }
}
