using System;
using System.Collections.Generic;
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
        {
            ("Invisible Car Path - 1xTwoway", 3f),
            ("Invisible Road Path - 1xTwoway", 4f),
            ("Invisible Car Path - 2xTwoway", 6f),
            ("Invisible Road Path - 2xTwoway", 7f),
        };

        /*
         * Im Laufzeitdump vom 2026-08-09 aus den Stueckbreiten gemessen:
         * 1xOneway = 3,0 m mittige Autospur + 2 * 0,5 m Gehabschnitt;
         * der reine Fussweg besteht aus einem mittigen 2,0-m-Stueck.
         */
        private const string OnewayPathName = "Invisible Road Path - 1xOneway";
        private const float OnewayPathWidth = 4f;
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
            using var alle = _gassenStrassen.ToEntityArray(Allocator.TempJob);
            for (var i = 0; i < alle.Length; i++)
            {
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
        private int CreateGassenstueck(
            NetSegment piece,
            int index,
            float vorflaechenbreite,
            ref TerrainHeightData heightData,
            Dictionary<(long, long), float> heights,
            ref Unity.Mathematics.Random random,
            List<string> bericht)
        {
            if (!TryResolveZufahrtsgasse(piece.Art, out var gasse))
            {
                bericht.Add($"Zufahrt {index}: Gassenklon noch nicht bereit");
                return 0;
            }
            // piece.A liegt am Polygonrand, piece.B innen; nach aussen ist
            // also A - B. Das ist die Fangachse der Zufahrt.
            if (!SucheStadtstrasseFuerGasse(piece.A, piece.A - piece.B,
                    out var mitte, out var halbeBreite, out var halbeImStrahl,
                    out var strasse, out var t, out var strassenhoehe))
            {
                bericht.Add($"Zufahrt {index}: keine Stadtstrasse in "
                    + "Fangrichtung, Bordstein hoechstens "
                    + $"{Gassenreichweite.Reichweite:F0} m ab Polygonrand");
                return 0;
            }

            var richtung = piece.A - mitte;
            var abstand = math.length(richtung);
            if (!(abstand > 0.5f))
            {
                bericht.Add($"Zufahrt {index}: liegt auf der Strassenmitte");
                return 0;
            }
            richtung /= abstand;

            /*
             * DIE GASSE GEHT DURCH BIS ZUR FAHRGASSE.
             *
             * Bis zum 2026-09-18 endete sie 2 m hinter dem Bordstein, und
             * der unsichtbare Weg begann am Polygonrand - die beiden lagen
             * zwei Meter uebereinander. Autos fuhren herein, wendeten und
             * fuhren wieder hinaus.
             *
             * `piece.B` IST der Punkt, an dem der Weg bisher an die
             * Fahrgasse stiess, und er wird UNVERAENDERT uebernommen: innen
             * verbindet CS2 nur ueber einen identischen Endpunkt, nicht ueber
             * LocalConnect. Ein neu gerechneter Punkt laege um Float-Reste
             * daneben und verbaende nichts.
             */
            var ende = piece.B;
            var laenge = math.distance(mitte, ende);
            // Schraeg gemessen ist die halbe Breite laenger als senkrecht.
            if (!(laenge > halbeImStrahl))
            {
                bericht.Add($"Zufahrt {index}: zu kurz - {laenge:F2} m ab "
                    + $"Strassenmitte, Fahrbahnrand bei {halbeBreite:F2} m");
                return 0;
            }

            /*
             * DER ENDPUNKT AN DER STRASSE BEKOMMT DIE HOEHE DER STRASSE.
             *
             * `SampleCourseHeight` tastet das GELAENDE ab, und unter einer
             * Strasse ist das weggeschnitten und liegt tiefer als der
             * Asphalt. Ohne diese Zeile setzt die Gasse dort unter der
             * Fahrbahn an und graebt sich ein - im Bild des Nutzers vom
             * 2026-09-18 ein Loch mitten in der Einmuendung.
             *
             * Der Hoehenspeicher wird VOR der Abtastung befragt; ihn zu
             * fuellen genuegt.
             */
            MerkeHoehe(mitte, strassenhoehe, heights);

            /*
             * DIE RICHTUNG STECKT IN DER REIHENFOLGE DER ENDPUNKTE.
             *
             * `mitte` liegt auf der Strasse, `ende` im Parkplatz. Eine
             * Gasse HINAUS faehrt also von `ende` nach `mitte` - dieselbe
             * Regel wie bei `Ausfahrt`, wo `piece.B` vor `piece.A` kommt.
             * Bei der zweispurigen Gasse ist die Reihenfolge gleichgueltig.
             */
            var hinaus = Zufahrtsarten.FaehrtHinaus(piece.Art);
            var kursVon = hinaus ? ende : mitte;
            var kursNach = hinaus ? mitte : ende;

            /*
             * EIN KURS, NICHT STUECKE.
             *
             * Vom 2026-09-23 bis 24 wurde die Gasse hier in Stuecke von
             * hoechstens 16 m geteilt - ein Missverstaendnis: gemeint war die
             * REICHWEITE bis zur Strasse (siehe `Gassenreichweite`). Die
             * Stuecke kamen im Spiel als zwei Gassen an, die sich in der
             * Mitte nicht verbanden; der Nutzer konnte beide einzeln mit dem
             * Bulldozer anwaehlen.
             */
            /*
             * DAS STRASSENENDE DOCKT AN WIE BEIM STRASSENWERKZEUG.
             *
             * Bis zum 2026-09-24 ging nur eine Koordinate an CS2, und das
             * Spiel suchte sich die Strasse daneben selbst. Der Gassenbefund
             * zeigte, was dabei herauskam: Einmuendungen 0,10 und 0,26 m
             * neben dem geplanten Punkt, und nahe am Kantenende an einem
             * anderen Knoten. Jetzt bekommt der Kurs die Kante und die
             * Teilungsstelle - oder den Knoten, wenn er dort schon steht.
             */
            var strassenpunkt = new float3(mitte.x, strassenhoehe, mitte.y);
            var anschluss = AnschlussAnStrasse(strasse, t, ref strassenpunkt);
            if (anschluss.Entity != Entity.Null
                && math.distance(strassenpunkt.xz, mitte) > 1e-4f)
            {
                // Auf einen vorhandenen Knoten verschoben: Start und Hoehe
                // wandern mit, damit Kurs und Knoten uebereinstimmen.
                mitte = strassenpunkt.xz;
                strassenhoehe = strassenpunkt.y;
                MerkeHoehe(mitte, strassenhoehe, heights);
                kursVon = hinaus ? ende : mitte;
                kursNach = hinaus ? mitte : ende;
            }
            if (!CreateCourseDefinition("entrance-gasse", index, kursVon,
                    kursNach, gasse, ref heightData, heights, ref random,
                    anschlussAnfang: hinaus ? default : anschluss,
                    anschlussEnde: hinaus ? anschluss : default))
            {
                bericht.Add($"Zufahrt {index}: Gassenkurs abgelehnt "
                    + $"({laenge:F2} m)");
                return 0;
            }
            var erzeugt = 1;
            /*
             * Was wir wussten, fuer die Rueckschau nach dem Bau. Der Zettel
             * hier haelt nur die Absicht fest; ob daraus eine ordentliche
             * Kreuzung geworden ist, misst `PLT-Gassenbefund` am fertigen
             * Netz.
             */
            MerkeGassenplan(index, mitte, ende, strasse, t,
                piece.B - piece.A, halbeBreite, gasse, vorflaechenbreite,
                hinaus);
            bericht.Add($"Zufahrt {index}: Gasse {laenge:F2} m in "
                + "einem Kurs ab Strassenmitte "
                + "bis zur Fahrgasse, "
                + $"Fahrbahnrand bei {halbeBreite:F2} m, Ueberstand "
                + $"{laenge - halbeBreite:F2} m"
                + (laenge > abstand + 1e-3f
                    ? $", davon {laenge - abstand:F2} m im Parkplatz"
                    : string.Empty));
            return erzeugt;
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

            using var prefabs = _pathPrefabQuery.ToEntityArray(Allocator.TempJob);
            for (var i = 0; i < prefabs.Length; i++)
            {
                var entity = prefabs[i];
                if (!_prefabSystem.TryGetPrefab<PrefabBase>(entity, out var prefab)
                    || prefab == null || !prefab.isBuiltin) continue;
                for (var j = 0; j < DrivablePaths.Length; j++)
                    if (string.Equals(prefab.name, DrivablePaths[j].Name,
                            StringComparison.Ordinal))
                        _pathPrefabs[prefab.name] = entity;
            }

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
        private int CreateNetDefinitions(ParkingLayout layout, LayoutSettings settings,
                                         ref TerrainHeightData heightData)
        {
            if (layout == null || settings == null) return 0;
            if (!TryChooseDrivablePath(settings.Ai, out var wide, out var wideCore))
                return 0;
            if (!TryChooseDrivablePath(settings.Cw, out var narrow, out var narrowCore))
                return 0;

            var heights = new Dictionary<(long, long), float>();
            SammleStrassenStreifen(_points);
            // Beim Edit: Hoehen der alten Knoten behalten, siehe
            // `BelegeHoehenAusAltbestand` in ParkingLotEditHeight.cs.
            BelegeHoehenAusAltbestand(heights, ref heightData);
            var random = new Unity.Mathematics.Random(
                (uint)Environment.TickCount | 1u);
            var created = 0;
            var zoningGebaut = 0;
            var zoningGeplant = 0;
            var zoningOhneKlon = 0;
            var zoningAbgelehnt = new List<string>();
            var gassenGeplant = 0;
            var gassenGebaut = 0;
            var gassenBericht = new List<string>();
            // Der Befund gehoert zu DIESEM Bau; ein alter Plan wuerde sonst
            // eine Kreuzung melden, die niemand gerade gebaut hat.
            VergissGassenplan();
            var zoningStuecke = new List<(float2 A, float2 B)>();
            // Nur anfordern, wenn das Layout ueberhaupt eine Zoning-Strasse
            // enthaelt - sonst bestellt jeder Parkplatz einen Prefabklon.
            var zoningRoad = Entity.Null;
            for (var i = 0; i < layout.NetLine.Length; i++)
            {
                if (!string.Equals(layout.NetLine[i].Kind, "zoning",
                        StringComparison.Ordinal)) continue;
                TryResolveZoningRoad(settings.Zoningstrasse, out zoningRoad);
                break;
            }
            // AUS DER NETZFASSUNG, nicht aus den Belaglinien. Der Unterschied
            // ist der Grund, warum im Spiel keine Abbiegepfeile erschienen:
            // die Belaglinien enden an der KANTE der getroffenen Strasse, also
            // Ai/2 = 3,50 m vor deren Achse. CS2 verschmilzt aber nur Segmente
            // mit IDENTISCHEM Endpunkt zu einem Knoten - 3,50 m daneben heisst
            // keine Kreuzung. `NetLine` ist an jeder Einmuendung geteilt und
            // bis zur Mittellinie gefuehrt. Gemessen im Nutzerbau: vorher 20
            // von 32 Enden voellig frei, danach nur noch das aeussere
            // Zufahrtsende - und das MUSS frei bleiben, dort haengt sich der
            // Road->Pathway-LocalConnect an die Stadtstrasse.
            for (var i = 0; i < layout.NetLine.Length; i++)
            {
                var piece = layout.NetLine[i];
                if (string.Equals(piece.Kind, "entrance", StringComparison.Ordinal))
                {
                    if (piece.Art == Zufahrtsart.Fussweg)
                    {
                        created += CreatePedestrianEntranceDefinition(
                            piece, i, ref heightData, heights, ref random);
                        continue;
                    }
                    // NUR die unsichtbaren Einbahn-Wege. Die gerichteten
                    // GASSEN werden weiter unten als geteilte Strasse gebaut.
                    if (piece.Art == Zufahrtsart.Einfahrt
                        || piece.Art == Zufahrtsart.Ausfahrt)
                    {
                        created += CreateOnewayEntranceDefinitions(
                            piece, i, ref heightData, heights, ref random);
                        continue;
                    }
                    /*
                     * DIE GASSE ERSETZT DIE ZUFAHRT, sie ergaenzt sie nicht
                     * mehr.
                     *
                     * Beides zu bauen hiess: zwei Meter Ueberlappung
                     * zwischen Gasse und unsichtbarem Weg, und Autos, die in
                     * der Einfahrt wenden. Seit dem 2026-09-18 geht die
                     * Gasse durch bis zur Fahrgasse, und der Weg entfaellt.
                     */
                    if (Zufahrtsarten.IstGasse(piece.Art))
                    {
                        gassenGeplant++;
                        var breite = (float)new ParkingLotTool.Geometry.Entrance { Art = piece.Art }
                            .Breite(settings.Ai, settings.Gassenbreite);
                        var gebaut = CreateGassenstueck(piece, i, breite,
                            ref heightData, heights, ref random, gassenBericht);
                        created += gebaut;
                        if (gebaut > 0) gassenGebaut++;
                        continue;
                    }
                }
                if (string.Equals(piece.Kind, "zoning", StringComparison.Ordinal))
                {
                    zoningGeplant++;
                    zoningStuecke.Add((piece.A, piece.B));
                    if (_zoningErhalten) continue;
                    // Fehlt der Klon noch, entfaellt nur die Zoning-Strasse.
                    // Der Parkplatz selbst wird trotzdem fertig gebaut.
                    if (zoningRoad == Entity.Null)
                    {
                        zoningOhneKlon++;
                        continue;
                    }
                    // Alle bereits geteilten T-Arme gehoeren in denselben
                    // GenerateNodes-Durchlauf: 3 gleiche Kursenden -> 1 Knoten.
                    // Eine Temp-Kante ist kein Original fuer einen Folgekurs.
                    /*
                     * DIE ZONINGSEITEN KOMMEN MIT DER DEFINITION, wie beim
                     * Vanilla-Werkzeug. Frueher schrieb die Nacharbeit sie
                     * nach dem Bau direkt in die fertige Kante - das hat CS2
                     * beim Bearbeiten sechsmal nativ abstuerzen lassen.
                     */
                    MerkeZoningSeitenGrundlage();
                    Game.Net.Upgraded? seiten = null;
                    if (EntscheideZoningseiten(piece.A, piece.B, true,
                            out var linksAus, out var rechtsAus, out _)
                        && (linksAus || rechtsAus))
                        seiten = new Game.Net.Upgraded
                        {
                            m_Flags = new Game.Prefabs.CompositionFlags(
                                default,
                                linksAus ? Game.Prefabs.CompositionFlags.Side.ZonesDisabled : default,
                                rechtsAus ? Game.Prefabs.CompositionFlags.Side.ZonesDisabled : default),
                        };
                    if (CreateCourseDefinition(piece.Kind, i, piece.A, piece.B,
                            zoningRoad, ref heightData, heights, ref random,
                            upgraded: seiten))
                    {
                        created++;
                        zoningGebaut++;
                    }
                    else
                    {
                        /*
                         * WELCHES STUECK ABGELEHNT WURDE, UND WIE LANG.
                         *
                         * Befund des Nutzers am 2026-09-03: *"Es werden
                         * weiterhin alle Zoning-Strassen in der Preview
                         * angezeigt, auch wenn sie durch Ecke oder Rand gar
                         * nicht gebaut werden."* Die Vorschau zeigt den PLAN;
                         * wenn davon etwas nicht ankommt, faellt es hier
                         * heraus - und bisher stumm.
                         *
                         * Statt zu raten, wo Plan und Bau auseinandergehen,
                         * nennt der naechste Bau die Stuecke selbst. Erst
                         * danach laesst sich entscheiden, ob die Vorschau
                         * weniger zeigen muss oder der Bau mehr schafft.
                         */
                        zoningAbgelehnt.Add(
                            $"({piece.A.x:F1}/{piece.A.y:F1})-"
                            + $"({piece.B.x:F1}/{piece.B.y:F1}) "
                            + $"{math.distance(piece.A, piece.B):F1} m");
                    }
                    continue;
                }
                var prefab = string.Equals(piece.Kind, "cross", StringComparison.Ordinal)
                    ? narrow : wide;
                if (CreateCourseDefinition(piece.Kind, i, piece.A, piece.B, prefab,
                        ref heightData, heights, ref random))
                    created++;
            }

            MeldeAblehnungen(layout);
            var fussPrefab = World.GetOrCreateSystemManaged<ParkingLotFusswegPrefabSystem>().Bereit;
            if (fussPrefab != Entity.Null)
                Mod.log.Info("PLT-Bauzettel: Fussweg-LocalConnect-Suchmaske "
                    + EntityManager.GetComponentData<LocalConnectData>(fussPrefab).m_Layers
                    + "; Autoanschluss ueber unveraenderte Vanilla-Prefabs.");
            if (zoningGeplant > 0)
                Mod.log.Info($"PLT-Zoning: {zoningGebaut} von {zoningGeplant} "
                    + $"geplanten Strassenkursen gemeinsam erzeugt, aus "
                    + $"'{settings.Zoningstrasse}' (unsichtbarer Klon)."
                    + (zoningOhneKlon > 0
                        ? $" {zoningOhneKlon} ohne Prefabklon entfallen."
                        : string.Empty));
            // Was die Vorschau zeigt, der Bau aber nicht liefert - genau die
            // Luecke, nach der der Nutzer gefragt hat.
            if (zoningAbgelehnt.Count > 0)
                Mod.log.Warn($"PLT-Zoning: {zoningAbgelehnt.Count} geplante(s) "
                    + "Stueck(e) NICHT gebaut: "
                    + string.Join("; ", zoningAbgelehnt));
            if (gassenGeplant > 0)
                Mod.log.Info($"PLT-Zufahrtsgasse: {gassenGebaut} von "
                    + $"{gassenGeplant} Gassenstueck(en) erzeugt. "
                    + string.Join("; ", gassenBericht));
            MeldeZoningZusammenhang(zoningStuecke);
            if (created > 0)
                Mod.log.Info($"PLT-Wege: {created} Kurse, Fahrgasse {wideCore:F0} m "
                    + $"(eingestellt {settings.Ai:F1} m), Querweg {narrowCore:F0} m "
                    + $"(eingestellt {settings.Cw:F1} m).");
            MeldeStrassenhoehe();
            return created;
        }

        /** Fussgaengerzugang: genau ein mittiger Fussweg, kein Autokurs. */
        private int CreatePedestrianEntranceDefinition(
            NetSegment piece,
            int index,
            ref TerrainHeightData heightData,
            Dictionary<(long, long), float> heights,
            ref Unity.Mathematics.Random random)
        {
            if (!TryResolvePedestrianPath(out var pedestrian, gesetzterZugang: true))
                return 0;
            return CreateCourseDefinition(
                "entrance-pedestrian", index, piece.A, piece.B, pedestrian,
                ref heightData, heights, ref random) ? 1 : 0;
        }

        /**
         * Einfahrt: Kursrichtung von der Strasse (A) in den Parkplatz (B).
         * Ausfahrt: derselbe konstruierte Kurs mit vertauschten Enden.
         *
         * Die beiden 2-m-Fusswege liegen konstruktiv in den beiden Haelften
         * der 4-m-Flaeche: Achsabstand `(4 - 2) / 2 = 1 m`. Damit passen Netz
         * und Flaeche ohne nachtraegliches Zuschneiden exakt zusammen.
         */
        private int CreateOnewayEntranceDefinitions(
            NetSegment piece,
            int index,
            ref TerrainHeightData heightData,
            Dictionary<(long, long), float> heights,
            ref Unity.Mathematics.Random random)
        {
            if (!TryResolvePathPrefab(OnewayPathName, out var oneway))
                return 0;

            var outgoing = Zufahrtsarten.FaehrtHinaus(piece.Art);
            var from = outgoing ? piece.B : piece.A;
            var to = outgoing ? piece.A : piece.B;
            var created = CreateCourseDefinition(
                outgoing ? "entrance-out" : "entrance-in",
                index, from, to, oneway,
                ref heightData, heights, ref random) ? 1 : 0;
            if (!TryResolvePedestrianPath(out var pedestrian)) return created;
            var vector = to - from;
            var length = math.length(vector);
            if (!(length >= 1f)) return created;
            var normal = new float2(-vector.y, vector.x) / length;
            var offset = (OnewayPathWidth - PedestrianPathWidth) / 2f;
            for (var side = -1; side <= 1; side += 2)
            {
                var shift = normal * (offset * side);
                if (CreateCourseDefinition(
                        side < 0 ? "entrance-ped-right" : "entrance-ped-left",
                        index, from + shift, to + shift, pedestrian,
                        ref heightData, heights, ref random))
                    created++;
            }
            return created;
        }

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
        /**
         * ZERFAELLT DAS ZONING-NETZ IN MEHRERE ZUEGE?
         *
         * Der Nutzer hat die Luecke ueber die Wasser- und Abwasseransicht
         * gefunden: Rohre laufen in den Strassen, und wo die Strasse
         * unterbrochen ist, reisst das Rohr ab. Bis dahin fiel es niemandem
         * auf, weil die Strassen unsichtbar sind.
         *
         * Diese Zeile misst es beim Bauen. Zwei Zuege sind nicht immer
         * falsch - zwei weit auseinander liegende Flaechen haben zu Recht
         * getrennte Ringe -, aber sie sind IMMER der Punkt, an dem man
         * nachsehen muss. Deshalb steht auch dabei, wo die freien Enden
         * liegen.
         */
        private static void MeldeZoningZusammenhang(
            IReadOnlyList<(float2 A, float2 B)> stuecke)
        {
            if (stuecke == null || stuecke.Count == 0) return;

            var eltern = new int[stuecke.Count];
            for (var i = 0; i < eltern.Length; i++) eltern[i] = i;
            int Wurzel(int i)
            {
                while (eltern[i] != i) i = eltern[i] = eltern[eltern[i]];
                return i;
            }
            for (var i = 0; i < stuecke.Count; i++)
            for (var k = i + 1; k < stuecke.Count; k++)
            {
                if (!ZoningStueckeBeruehren(stuecke[i], stuecke[k])) continue;
                var a = Wurzel(i);
                var b = Wurzel(k);
                if (a != b) eltern[a] = b;
            }

            var zuege = new HashSet<int>();
            for (var i = 0; i < stuecke.Count; i++) zuege.Add(Wurzel(i));
            if (zuege.Count <= 1)
            {
                Mod.log.Info($"PLT-Zoning: {stuecke.Count} Stueck(e) bilden "
                    + "EINEN zusammenhaengenden Strassenzug.");
                return;
            }

            var enden = new List<string>();
            for (var i = 0; i < stuecke.Count; i++)
            {
                foreach (var ende in new[] { stuecke[i].A, stuecke[i].B })
                {
                    var haengt = false;
                    for (var k = 0; k < stuecke.Count && !haengt; k++)
                        if (k != i)
                            haengt = ZoningPunktAufStueck(
                                stuecke[k].A, stuecke[k].B, ende);
                    if (!haengt) enden.Add($"({ende.x:F1}/{ende.y:F1})");
                }
            }
            Mod.log.Warn($"PLT-Zoning: {stuecke.Count} Stueck(e) zerfallen in "
                + $"{zuege.Count} Strassenzuege - dort reissen Wasser, "
                + "Abwasser und Strom ab. Freie Enden: "
                + (enden.Count == 0 ? "keine" : string.Join(" ", enden)));
        }

        private static bool ZoningStueckeBeruehren(
            (float2 A, float2 B) x, (float2 A, float2 B) y)
            // 01:52 meldete die Naehepruefung 1 Zug, Temp und Apply aber 2.
            // Der fertige Kursplan braucht exakt gemeinsame Endpunkte.
            => x.A.Equals(y.A) || x.A.Equals(y.B)
                || x.B.Equals(y.A) || x.B.Equals(y.B);

        private static bool ZoningPunktAufStueck(float2 a, float2 b, float2 p)
        {
            const float toleranz = 0.01f;
            var d = b - a;
            var laenge = math.length(d);
            if (laenge < toleranz) return false;
            var r = d / laenge;
            var w = p - a;
            var laengs = math.dot(w, r);
            if (laengs < -toleranz || laengs > laenge + toleranz) return false;
            return math.abs(r.x * w.y - r.y * w.x) < toleranz;
        }

        private void MeldeAblehnungen(ParkingLayout layout)
        {
            if (layout == null || layout.RejectTotals.Length == 0) return;
            var zeile = string.Empty;
            foreach (var topf in layout.RejectTotals)
            {
                var gesetzt = (int)topf.First.x;
                zeile += (zeile.Length > 0 ? " | " : "")
                    + $"{topf.Reason} {gesetzt}/{topf.Count}";
            }
            Mod.log.Info("PLT-Ablehnungen (gesetzt/versucht): " + zeile);
            if (layout.Rejects.Length == 0)
            {
                Mod.log.Info("  nichts verworfen.");
                return;
            }
            foreach (var grund in layout.Rejects)
                Mod.log.Info($"  {grund.Count,5}x  {grund.Reason}"
                    + $"   (erste bei {grund.First.x:F1}/{grund.First.y:F1})");
        }

        private bool CreateCourseDefinition(
            string kind,
            int index,
            float2 from,
            float2 to,
            Entity prefab,
            ref TerrainHeightData heightData,
            Dictionary<(long, long), float> heights,
            ref Unity.Mathematics.Random random,
            Anschluss anschlussAnfang = default,
            Anschluss anschlussEnde = default,
            Game.Net.Upgraded? upgraded = null)
        {
            if (!math.all(math.isfinite(from)) || !math.all(math.isfinite(to)))
                throw new InvalidOperationException(
                    $"Der Fahrweg '{kind}' {index} enthält eine nicht-endliche Koordinate.");

            var a = new float3(from.x, SampleCourseHeight(from, ref heightData, heights),
                from.y);
            var b = new float3(to.x, SampleCourseHeight(to, ref heightData, heights),
                to.y);
            var length = math.distance(a, b);
            // Kuerzer als ein Meter ist kein Fahrweg, sondern ein Rundungsrest.
            // CS2 legt daraus einen Knoten ohne Kante an.
            if (!(length >= 1f)) return false;

            // Ohne CoursePosFlags.FreeHeight - und das ist RICHTIG, aber die
            // frueher hier stehende Begruendung war falsch. Sie behauptete, das
            // Flag lese ausschliesslich `NetToolSystem`; geprueft worden waren
            // nur GenerateNodes/GenerateEdges/Validation. Am Volldekompilat vom
            // 2026-08-17 nachgesehen: `CourseSplitSystem.InitializeCoursePos`
            // (Zeile 1437) liest es ebenfalls und wuerde `m_Position.y` aus
            // Terrain- und Wasserhoehe NEU berechnen. Genau das wollen wir
            // nicht - unsere Hoehen sind bereits exakte Terrainwerte aus
            // `SampleCourseHeight`, je 2D-Punkt nur einmal abgetastet.
            //
            // Zusammen mit dem fehlenden Besitzer ist damit auch die alte
            // Absackerei erklaert. `CourseSplitSystem` Zeile 1933:
            //
            //     bool flag2 = (m_CreationDefinition.m_Owner == Entity.Null
            //                   && m_OwnerDefinition.m_Prefab == Entity.Null) || flag;
            //
            // Steht ein Besitzer in der CREATIONDEFINITION, ist flag2 falsch und
            // CS2 nimmt einen anderen Hoehenweg - damals lag die erste Einfahrt
            // dadurch 183 m zu tief (angefordert y=512,70, gebaut y=329,16).
            // Deshalb bleibt die CreationDefinition hier besitzerlos; der Owner
            // kommt erst an die fertigen Entities. Vanilla loest es voellig
            // anders: `ObjectSubNets` deklariert die Fahrwege IM PREFAB in
            // lokalen Koordinaten - eine Komponente, die es laut ComponentMenu
            // nur fuer BuildingPrefab und BuildingExtensionPrefab gibt, fuer
            // unser LotPrefab also nicht.
            var curve = NetUtils.StraightCurve(a, b);
            var definition = EntityManager.CreateEntity();
            EntityManager.AddComponentData(definition, new CreationDefinition
            {
                m_Prefab = prefab,
                m_RandomSeed = random.NextInt(),
            });
            EntityManager.AddComponent<Updated>(definition);
            /*
             * Die Gasse bringt ihren eigenen Knoten mit; Zebrastreifen
             * gehoeren dort nicht hin. `Upgraded` an der Definition wandert
             * mit auf die fertige Kante - so macht es CS2s eigenes
             * Strassenwerkzeug auch.
             */
            if (string.Equals(kind, "entrance-gasse", StringComparison.Ordinal))
                EntityManager.AddComponentData(definition, new Game.Net.Upgraded
                {
                    m_Flags = new Game.Prefabs.CompositionFlags(
                        default,
                        Game.Prefabs.CompositionFlags.Side.RemoveCrosswalk,
                        Game.Prefabs.CompositionFlags.Side.RemoveCrosswalk),
                });
            else if (upgraded.HasValue)
                EntityManager.AddComponentData(definition, upgraded.Value);
            EntityManager.AddComponentData(definition, new NetCourse
            {
                m_Curve = curve,
                m_Length = length,
                m_FixedIndex = -1,
                m_Elevation = float2.zero,
                m_StartPosition = new CoursePos
                {
                    m_Entity = anschlussAnfang.Entity,
                    m_SplitPosition = anschlussAnfang.Teilung,
                    m_Position = a,
                    m_Rotation = NetUtils.GetNodeRotation(MathUtils.StartTangent(curve)),
                    m_CourseDelta = 0f,
                    m_Elevation = float2.zero,
                    m_Flags = CoursePosFlags.IsFirst,
                    m_ParentMesh = -1,
                },
                m_EndPosition = new CoursePos
                {
                    m_Entity = anschlussEnde.Entity,
                    m_SplitPosition = anschlussEnde.Teilung,
                    m_Position = b,
                    m_Rotation = NetUtils.GetNodeRotation(MathUtils.EndTangent(curve)),
                    m_CourseDelta = 1f,
                    m_Elevation = float2.zero,
                    m_Flags = CoursePosFlags.IsLast,
                    m_ParentMesh = -1,
                },
            });
            RecordNetDefinition(kind, index, prefab, definition, a, b);
            return true;
        }

        /**
         * Setzt eine Hoehe fest, bevor das Gelaende befragt wird.
         *
         * Dieselbe Rasterung wie `SampleCourseHeight` - sonst traefe der
         * Schluessel nicht, und der Eintrag bliebe wirkungslos.
         */
        private static void MerkeHoehe(float2 point, float hoehe,
                                       Dictionary<(long, long), float> heights)
        {
            var key = ((long)math.round(point.x * 40f),
                       (long)math.round(point.y * 40f));
            heights[key] = hoehe;
        }

        private float SampleCourseHeight(float2 point, ref TerrainHeightData heightData,
                                         Dictionary<(long, long), float> heights)
        {
            // Ein Vierteldezimeter-Raster: fein genug, dass getrennte Knoten
            // getrennt bleiben, grob genug, dass zwei Segmente an derselben
            // Ecke garantiert dieselbe Hoehe bekommen.
            var key = ((long)math.round(point.x * 40f), (long)math.round(point.y * 40f));
            if (heights.TryGetValue(key, out var cached)) return cached;

            // Unter einem alten eigenen Weg gilt SEINE Hoehe (geprueft), nicht
            // das Gelaende, das er selbst geformt hat.
            if (HoeheUnterAltbestand(point, ref heightData, out var alt))
            {
                heights[key] = alt;
                return alt;
            }

            // Im Querschnitt einer Stadtstrasse gilt deren Hoehe - das
            // Gelaende darunter hat CS2 weggeschnitten (ParkingLotStrassenhoehe.cs).
            if (HoeheUnterStrasse(point, ref heightData, out var strasse))
            {
                heights[key] = strasse;
                return strasse;
            }

            var height = TerrainUtils.SampleHeight(
                ref heightData, new float3(point.x, 0f, point.y));
            if (!math.isfinite(height))
                throw new InvalidOperationException(
                    "Die Terrain-Abtastung eines Fahrwegknotens ist nicht endlich.");
            heights[key] = height;
            return height;
        }
    }
}
