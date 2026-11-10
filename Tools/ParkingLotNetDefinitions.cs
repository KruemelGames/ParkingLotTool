using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private int CreateNetDefinitions(ParkingLayout layout, LayoutSettings settings,
                                         ref TerrainHeightData heightData)
        {
            int n = 0;
            foreach (int teil in CreateNetDefinitionsSchritte(layout, settings, heightData)) n += teil;
            return n;
        }

        private System.Collections.Generic.IEnumerable<int> CreateNetDefinitionsSchritte(ParkingLayout layout, LayoutSettings settings, TerrainHeightData heightData)
        {
            if (layout == null || settings == null) yield break;
            if (layout.Fahrrad != null)
            {
                // Versuch 02.11.2026 (Nutzer): keine Wege, nur die Gehflaeche wie Vanilla.
                // Die Raeder fahren ueber die NavigationArea (AreaConnectionSystem:514-517),
                // der Marker an der Lotkante findet Fahrspur + Gehweg der Stadtstrasse
                // (WaypointConnectionSystem:1268-1313). Unser Zugangsweg lag dem Marker
                // naeher und hatte keinen Gehweg -> kein Fussweg nach dem Abstellen.
                // FahrradNetze bleibt fuer die Rueckkehr bestehen.
                yield break;
            }
            if (!TryChooseDrivablePath(settings.Ai, out var wide, out var wideCore))
                yield break;
            if (!TryChooseDrivablePath(settings.Cw, out var narrow, out var narrowCore))
                yield break;

            var heights = new Dictionary<(long, long), float>();
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
            var gassenErhalten = 0;
            var gassenBericht = new List<string>();
            // Der Befund gehoert zu DIESEM Bau; ein alter Plan wuerde sonst
            // eine Kreuzung melden, die niemand gerade gebaut hat.
            VergissGassenplan();
            var zoningStuecke = new List<(float2 A, float2 B)>();
            MerkeGassenenden(layout);
            BelegeGassenendenAusAltgassen(heights, _gassenenden);
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
                yield return 0;
                int vorher = created;
                var piece = layout.NetLine[i];
                if (string.Equals(piece.Kind, "entrance", StringComparison.Ordinal))
                {
                    if (piece.Art == Zufahrtsart.Fussweg)
                    {
                        created += CreatePedestrianEntranceDefinition(
                            piece, i, ref heightData, heights, ref random);
                        yield return created - vorher;
                        continue;
                    }
                    // NUR die unsichtbaren Einbahn-Wege. Die gerichteten
                    // GASSEN werden weiter unten als geteilte Strasse gebaut.
                    if (piece.Art == Zufahrtsart.Einfahrt
                        || piece.Art == Zufahrtsart.Ausfahrt)
                    {
                        created += CreateOnewayEntranceDefinitions(
                            piece, i, ref heightData, heights, ref random);
                        yield return created - vorher;
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
                        else if (_bauarbeiter && _erhalteneKursketten.ContainsKey(("entrance-gasse",i))) gassenErhalten++;
                        yield return created - vorher;
                        continue;
                    }
                }
                if (string.Equals(piece.Kind, "zoning", StringComparison.Ordinal))
                {
                    zoningGeplant++;
                    zoningStuecke.Add((piece.A, piece.B));
                    if (_zoningErhalten) { yield return 0; continue; }
                    // Fehlt der Klon noch, entfaellt nur die Zoning-Strasse.
                    // Der Parkplatz selbst wird trotzdem fertig gebaut.
                    if (zoningRoad == Entity.Null)
                    {
                        zoningOhneKlon++;
                        yield return created - vorher;
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
                    yield return created - vorher;
                    continue;
                }
                var prefab = string.Equals(piece.Kind, "cross", StringComparison.Ordinal)
                    ? narrow : wide;
                if (CreateCourseDefinition(piece.Kind, i, piece.A, piece.B, prefab,
                        ref heightData, heights, ref random))
                    created++;
                yield return created - vorher;
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
                    + $"{gassenGeplant} Gassenstueck(en) erzeugt, {gassenErhalten} erhalten. "
                    + string.Join("; ", gassenBericht));
            MeldeZoningZusammenhang(zoningStuecke);
            if (created > 0)
                Mod.log.Info($"PLT-Wege: {created} Kurse, Fahrgasse {wideCore:F0} m "
                    + $"(eingestellt {settings.Ai:F1} m), Querweg {narrowCore:F0} m "
                    + $"(eingestellt {settings.Cw:F1} m).");
            yield break;
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
            int created = 0;
            foreach (var kurs in InnereNetzkurse(piece,Entity.Null,Entity.Null))
                if (CreateCourseDefinition(kurs.Kind,index,kurs.A,kurs.B,kurs.Prefab,
                    ref heightData,heights,ref random)) created++;
            return created;
        }

    }
}
