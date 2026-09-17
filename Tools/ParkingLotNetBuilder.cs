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
         * Die naechste STADTSTRASSE zu einem Punkt.
         *
         * `Owner` schliesst unsere eigenen Kanten aus - an einer Zoning- oder
         * Randstrasse des Parkplatzes hat eine Zufahrtsgasse nichts zu
         * suchen, und ein Knoten dort wuerde nur unser eigenes Netz teilen.
         */
        private bool SucheStadtstrasseFuerGasse(float2 punkt, out float2 mitte,
                                                out float halbeBreite)
        {
            mitte = default;
            halbeBreite = 0f;
            if (_gassenStrassen == default)
                _gassenStrassen = GetEntityQuery(
                    ComponentType.ReadOnly<Game.Net.Edge>(),
                    ComponentType.ReadOnly<Curve>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.Exclude<Owner>(),
                    ComponentType.Exclude<Deleted>(),
                    ComponentType.Exclude<Temp>());

            var beste = GassenSuchweite;
            var treffer = Entity.Null;
            var trefferMitte = float2.zero;
            using var kanten = _gassenStrassen.ToEntityArray(Allocator.TempJob);
            for (var i = 0; i < kanten.Length; i++)
            {
                var prefab = EntityManager.GetComponentData<PrefabRef>(kanten[i]).m_Prefab;
                if (!EntityManager.HasComponent<RoadData>(prefab)) continue;
                var bogen = EntityManager.GetComponentData<Curve>(kanten[i]).m_Bezier;
                var abstand = MathUtils.Distance(bogen.xz, punkt, out var t);
                if (abstand >= beste) continue;
                beste = abstand;
                treffer = kanten[i];
                trefferMitte = MathUtils.Position(bogen, t).xz;
            }
            if (treffer == Entity.Null) return false;

            mitte = trefferMitte;
            var strassenprefab = EntityManager
                .GetComponentData<PrefabRef>(treffer).m_Prefab;
            // Die Breite steht am Prefab, nicht an den Querschnitten -
            // NetCompositionSystem Zeile 151 nimmt `m_DefaultWidth`.
            halbeBreite = EntityManager.HasComponent<NetGeometryData>(strassenprefab)
                ? EntityManager.GetComponentData<NetGeometryData>(strassenprefab)
                    .m_DefaultWidth * 0.5f
                : 4f;
            return true;
        }

        /**
         * Legt das aeussere Gassenstueck einer Gassen-Zufahrt an.
         *
         * Rueckgabe ist die Zahl erzeugter Kurse, also 0 oder 1. Faellt das
         * Stueck aus, wird die Zufahrt trotzdem gebaut - sie verhaelt sich
         * dann wie eine gewoehnliche Zufahrt, nur eben ohne geoeffneten
         * Bordstein. Der Grund steht im Bauzettel.
         */
        private int CreateGassenstueck(
            NetSegment piece,
            int index,
            ref TerrainHeightData heightData,
            Dictionary<(long, long), float> heights,
            ref Unity.Mathematics.Random random,
            List<string> bericht)
        {
            if (!TryResolveZufahrtsgasse(out var gasse))
            {
                bericht.Add($"Zufahrt {index}: Gassenklon noch nicht bereit");
                return 0;
            }
            if (!SucheStadtstrasseFuerGasse(piece.A, out var mitte,
                    out var halbeBreite))
            {
                bericht.Add($"Zufahrt {index}: keine Stadtstrasse in "
                    + $"{GassenSuchweite:F0} m");
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
             * Normalfall ist der Polygonrand als Ende - dort faengt der
             * unsichtbare Weg an, und beruehrende Enden geben dem
             * LocalConnect den kuerzesten Weg.
             *
             * Liegt der Rand naeher an der Strasse als der noetige
             * Ueberstand, wird die Gasse trotzdem so lang gemacht. Sie ragt
             * dann ein Stueck in den Parkplatz - unsichtbar, unter unserem
             * eigenen Belag, und immer noch besser als eine Gasse, die CS2
             * als "Ungueltige Form" ablehnt.
             */
            var laenge = math.max(abstand, halbeBreite + GassenUeberstand);
            var ende = mitte + richtung * laenge;

            if (!CreateCourseDefinition("entrance-gasse", index, mitte, ende,
                    gasse, ref heightData, heights, ref random))
            {
                bericht.Add($"Zufahrt {index}: Gassenkurs abgelehnt "
                    + $"({laenge:F2} m)");
                return 0;
            }
            bericht.Add($"Zufahrt {index}: Gasse {laenge:F2} m ab Strassenmitte, "
                + $"Fahrbahnrand bei {halbeBreite:F2} m, Ueberstand "
                + $"{laenge - halbeBreite:F2} m"
                + (laenge > abstand + 1e-3f
                    ? $", davon {laenge - abstand:F2} m im Parkplatz"
                    : string.Empty));
            return 1;
        }

        private bool TryResolvePedestrianPath(out Entity prefab)
        {
            prefab = World.GetOrCreateSystemManaged<ParkingLotFusswegPrefabSystem>().Bereit;
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

        /** Die Zufahrtsgasse kommt immer aus der Vanilla-Gasse. */
        private bool TryResolveZufahrtsgasse(out Entity prefab)
            => TryResolveStrassenklon("Alley", Strassenklonart.Zufahrtsgasse,
                out prefab);

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
                    if (piece.Art == Zufahrtsart.Einfahrt
                        || piece.Art == Zufahrtsart.Ausfahrt)
                    {
                        created += CreateOnewayEntranceDefinitions(
                            piece, i, ref heightData, heights, ref random);
                        continue;
                    }
                    /*
                     * Die Gasse ist ein ZUSAETZLICHES Stueck vor der Zufahrt,
                     * kein Ersatz. Deshalb kein `continue` - der gewohnte
                     * Weg nach innen wird gleich darunter genauso gebaut wie
                     * bei jeder anderen Zufahrt.
                     */
                    if (piece.Art == Zufahrtsart.Gasse)
                    {
                        gassenGeplant++;
                        var gebaut = CreateGassenstueck(piece, i, ref heightData,
                            heights, ref random, gassenBericht);
                        created += gebaut;
                        gassenGebaut += gebaut;
                    }
                }
                if (string.Equals(piece.Kind, "zoning", StringComparison.Ordinal))
                {
                    zoningGeplant++;
                    zoningStuecke.Add((piece.A, piece.B));
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
                    if (CreateCourseDefinition(piece.Kind, i, piece.A, piece.B,
                            zoningRoad, ref heightData, heights, ref random))
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
            if (!TryResolvePedestrianPath(out var pedestrian))
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

            var outgoing = piece.Art == Zufahrtsart.Ausfahrt;
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
            ref Unity.Mathematics.Random random)
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
            EntityManager.AddComponentData(definition, new NetCourse
            {
                m_Curve = curve,
                m_Length = length,
                m_FixedIndex = -1,
                m_Elevation = float2.zero,
                m_StartPosition = new CoursePos
                {
                    m_Entity = Entity.Null,
                    m_Position = a,
                    m_Rotation = NetUtils.GetNodeRotation(MathUtils.StartTangent(curve)),
                    m_CourseDelta = 0f,
                    m_Elevation = float2.zero,
                    m_Flags = CoursePosFlags.IsFirst,
                    m_ParentMesh = -1,
                    m_SplitPosition = 0f,
                },
                m_EndPosition = new CoursePos
                {
                    m_Entity = Entity.Null,
                    m_Position = b,
                    m_Rotation = NetUtils.GetNodeRotation(MathUtils.EndTangent(curve)),
                    m_CourseDelta = 1f,
                    m_Elevation = float2.zero,
                    m_Flags = CoursePosFlags.IsLast,
                    m_ParentMesh = -1,
                    m_SplitPosition = 0f,
                },
            });
            RecordNetDefinition(kind, index, prefab, definition, a, b);
            return true;
        }

        private float SampleCourseHeight(float2 point, ref TerrainHeightData heightData,
                                         Dictionary<(long, long), float> heights)
        {
            // Ein Vierteldezimeter-Raster: fein genug, dass getrennte Knoten
            // getrennt bleiben, grob genug, dass zwei Segmente an derselben
            // Ecke garantiert dieselbe Hoehe bekommen.
            var key = ((long)math.round(point.x * 40f), (long)math.round(point.y * 40f));
            if (heights.TryGetValue(key, out var cached)) return cached;

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
