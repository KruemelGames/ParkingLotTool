using System;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private int CreateAreaPreviewDefinitions(ParkingLayout layout)
        {
            int n = 0;
            foreach (int teil in CreateAreaPreviewDefinitionsSchritte(layout)) n += teil;
            return n;
        }

        private System.Collections.Generic.IEnumerable<int> CreateAreaPreviewDefinitionsSchritte(ParkingLayout layout)
        {
            var strasseAn = _baukontext?.Zettel.SurfaceRoadOn ?? _uiSystem?.FlaecheStrasseAn ?? true;
            var dekoAn = _baukontext?.Zettel.SurfaceDecorationOn ?? _uiSystem?.FlaecheDekoAn ?? true;
            var vorflaechenPrefab = _vorflaechenPrefab;
            GeplanteBauflaechen(layout, strasseAn, dekoAn, vorflaechenPrefab != Entity.Null,
                out var gras, out var asphalt, out var verschmolzen, out var einzelneVorflaechen,
                out var strassenbelag, out var fahrbahn);
            if (!dekoAn && layout.GrassForVegetation?.Length > 0)
                RecordPreviewDiagnostic("Warning", "Dekoflaeche AUS: "
                    + "Gelaendebaeume im Gruenstreifen bleiben sichtbar, "
                    + "auch wenn dort eigene Pflanzen gesetzt werden.");
            if (!strasseAn && layout.AsphaltSurface?.Length > 0)
                RecordPreviewDiagnostic("Warning", "Strassenflaeche AUS: "
                    + "Gelaendebaeume abseits der Wege bleiben sichtbar.");

            BeginAreaTransfer(layout, gras, asphalt);
            // Im Nutzerfall lagen zwischen Abriss und Hoehenlesen nur 45 ms;
            // die Gasse endete 2,80 m unter der Stadtstrasse. Die Kopie vor
            // dem Abriss haelt die alte Planierung fuer ALLE Bauteile fest.
            var editSnapshot = IsEditing && _editHeightSnapshot.isCreated;
            var heightData = editSnapshot
                ? _editHeightSnapshot
                : _terrainSystem.GetHeightData(waitForPending: true);
            if (IsEditing && _editNetRemovalTick != 0)
            {
                var now = System.Diagnostics.Stopwatch.GetTimestamp();
                var sinceRemoval = (now - _editNetRemovalTick) * 1000.0
                    / System.Diagnostics.Stopwatch.Frequency;
                var beforeRemoval = editSnapshot
                    ? (_editNetRemovalTick - _editHeightReadTick) * 1000.0
                      / System.Diagnostics.Stopwatch.Frequency
                    : 0.0;
                Mod.log.Info($"PLT-Edithoehe: Hoehenlesen "
                    + (editSnapshot
                        ? $"{beforeRemoval:F0} ms VOR Abriss"
                        : $"{sinceRemoval:F0} ms NACH Abriss")
                    + $", Verwendung {sinceRemoval:F0} ms nach Abriss; "
                    + (editSnapshot ? "Quelle gesicherte Vorabrisskarte"
                        : "Quelle aktuelle Karte")
                    + $", GetHeightData(waitForPending: true) "
                    + $"vor Abriss {_editHeightWaitMilliseconds} ms.");
            }
            var sampled = 0;
            var minimum = float.PositiveInfinity;
            var maximum = float.NegativeInfinity;
            // Die Vorflaeche liegt AUSSERHALB des Polygons, also ausserhalb
            // des schon gemessenen Bereichs. Ohne diese Zeile fehlten ihre
            // Knoten in der Terrainpruefung.
            foreach (var gruppe in new[] { gras, asphalt, einzelneVorflaechen, verschmolzen, strassenbelag, fahrbahn })
            {
                if (gruppe == null) continue;
                foreach (var ring in gruppe)
                {
                    MeasureAreaPreviewGroup(new[] {ring}, ref heightData,
                        ref sampled, ref minimum, ref maximum);
                    yield return 0;
                }
            }

            if (sampled == 0)
            {
                SetAreaTerrainTransfer(0, 0f, 0f, 0f,
                    limitTriggered: false,
                    "Keine übergabefähigen Flächenknoten vorhanden.");
                yield break;
            }

            var span = maximum - minimum;
            SetAreaTerrainTransfer(sampled, minimum, maximum, span,
                span > MaxCourseHeightDeviation,
                "TerrainUtils.SampleHeight an jedem offenen Knoten der geplanten Flächen.");
            Mod.log.Info($"PLT-Flächenvorschau Terrainhöhen: {sampled} Knoten, "
                + $"Minimum {minimum:F2} m, Maximum {maximum:F2} m, "
                + $"Spanne {span:F2} m.");
            if (span > MaxCourseHeightDeviation)
            {
                // Eine solche Spanne innerhalb eines Parkplatzes kennzeichnet
                // einen unbrauchbaren Schnappschuss. Keine falschen Knoten an
                // CS2 weitergeben; die Warnung macht den Ausfall sichtbar.
                RecordPreviewDiagnostic("Warning",
                    $"Flächenvorschau wegen Terrainspanne verworfen: {span:G9} m > "
                    + $"{MaxCourseHeightDeviation:G9} m (Minimum {minimum:G9} m, "
                    + $"Maximum {maximum:G9} m).");
                Mod.log.Warn($"PLT-Flächenvorschau wegen unplausibler Terrainhöhen "
                    + $"verworfen: {span:F2} m > {MaxCourseHeightDeviation:F0} m "
                    + $"(Minimum {minimum:F2} m, Maximum {maximum:F2} m).");
                yield break;
            }

            var created = 0;
            // Zuerst, damit die Besitzerflaeche schon steht, wenn die Kinder
            // kommen - die Reihenfolge im Puffer ist zwar egal, aber so
            // taucht sie im Abzug oben auf.
            ProtokolliereBauschritt("CreateLotOwnerDefinition");
            created += CreateLotOwnerDefinition(ref heightData);
            yield return created;
            /**
             * HIER wirken die Flaechenschalter unabhaengig - und NUR hier.
             *
             * Das Layout ist zu diesem Zeitpunkt fertig gerechnet; Buchten,
             * Wege, Zufahrten und Aufkleber entstehen unabhaengig davon. Wer
             * eine Flaeche abschaltet, bekommt denselben Parkplatz auf dem
             * vorhandenen Boden - fuer alle, die lieber mit dem Terrain-Brush
             * arbeiten.
             */
            /*
             * FLAECHEN GETRENNT ZAEHLEN.
             *
             * `created` ist die Zahl ALLER Vorschau-Entitaeten: Besitzer,
             * Flaechen, Wege, Aufkleber. Die Logzeile darunter nannte sie
             * bis zum 2026-09-01 "Polygonflaechen" - und meldete damit 445,
             * wo 47 Flaechen geplant waren. Das sah nach einem schweren
             * Fehler aus und war einer im Bericht, nicht im Bau.
             */
            var flaechen = 0;
            /*
             * DIE DEKOFLAECHE BEKOMMT DIESELBE BEHANDLUNG WIE DER
             * ZONINGBELAG - auf Wunsch des Nutzers vom 2026-09-03.
             *
             * Also den Klon mit `Terrain | Roads` und der hoeheren
             * Zeichenprioritaet. MIT RUECKFALL: ist der Klon nicht fertig,
             * wird wie bisher mit dem Vanilla-Prefab gebaut. Ohne diesen
             * Rueckfall haenge das Gras jedes Parkplatzes daran, dass ein
             * Laufzeitprefab rechtzeitig entsteht - ein zu grosser Einsatz
             * fuer eine Verbesserung an einer Stelle.
             *
             * ZWEIFEL, AUSDRUECKLICH: der Nutzer hat selbst beschrieben,
             * dass ein Gebaeude seine Flaeche bis zur Strasse AUFZIEHT.
             * Dagegen hilft keine Einstellung an unserer Flaeche - sie wird
             * nicht verdraengt, sondern ueberwachsen. Ob das hier etwas
             * bringt, ist offen.
             */
            var grasPrefab = _dekoBelagPrefab != Entity.Null
                ? _dekoBelagPrefab
                : _grassSurfacePrefab;
            if (dekoAn && gras.Length > 0 && _dekoBelagPrefab == Entity.Null)
                RecordPreviewDiagnostic("Warning", "Gras raeumt keine "
                    + "Gelaendebaeume: der PLT-Raeumbelag ist noch nicht "
                    + "bereit; der Bau verwendet die gewaehlte Vanilla-Flaeche.");
            if (dekoAn)
                foreach (int n in CreateAreaPreviewGroupSchritte("Grass", gras,
                    grasPrefab, heightData))
                { flaechen += n; yield return n; }
            /*
             * DER HAUPTBELAG BEKOMMT DIESELBEN DECAL-EBENEN WIE DIE
             * VORFLAECHE, ABER EINEN EIGENEN KLON MIT RAEUMFLAG.
             *
             * Befund des Nutzers vom 2026-09-18: die Vorflaeche ueberdeckt
             * den hellen Halbkreis der Gasse, der Hauptbelag nicht. Der
             * Unterschied steht im Log:
             *
             *   PLT Vorflaeche (Pavement Surface 01)  Ebenen Terrain, Roads
             *   Pavement Surface 01 (Vanilla)         Ebenen Terrain
             *
             * Das Vanilla-Prefab zeichnet gar nicht auf Strassen. Seit die
             * Zufahrtsgasse eine sichtbare Strasse MITTEN im Parkplatz ist,
             * reicht das nicht mehr - der Belag muss auch dort zeichnen.
             *
             * MIT RUECKFALL: ist der Klon nicht fertig, wird wie bisher mit
             * dem Vanilla-Prefab gebaut. Der Asphalt jedes Parkplatzes daran
             * zu haengen, dass ein Laufzeitprefab rechtzeitig entsteht,
             * waere ein zu grosser Einsatz.
             */
            if (strasseAn)
            {
                if (asphalt.Length > 0 && _asphaltBelagPrefab == Entity.Null)
                    RecordPreviewDiagnostic("Warning", "Asphalt raeumt keine "
                        + "Gelaendebaeume: der PLT-Raeumbelag ist noch nicht "
                        + "bereit; der Bau verwendet die gewaehlte Vanilla-Flaeche.");
                foreach (int n in CreateAreaPreviewGroupSchritte("Asphalt", asphalt,
                    _asphaltBelagPrefab != Entity.Null
                        ? _asphaltBelagPrefab
                        : _pavementSurfacePrefab,
                    heightData))
                { flaechen += n; yield return n; }
            }
            /*
             * DAS BAULAND HAT SEINEN EIGENEN SCHALTER NICHT.
             *
             * Es haengt weder an "Strasse" noch an "Dekoration": wer die
             * Dekoflaechen abschaltet, will kahlen Asphalt - aber die
             * Parzellen sind kein Schmuck, sie sind der Grund, warum dort
             * kein Parkplatz ist. Sie werden deshalb immer gesetzt.
             */
            if (!ZoningFlaecheAus
                && layout.ZoningSurface != null
                && layout.ZoningSurface.Length > 0)
                foreach (int n in CreateAreaPreviewGroupSchritte("Zoning",
                    layout.ZoningSurface,
                    // Mit Rueckfall wie bei der Dekoflaeche: klappt der Klon
                    // nicht, wird mit dem Vanilla-Prefab gebaut statt gar
                    // nicht.
                    _zoningBodenPrefab != Entity.Null
                        ? _zoningBodenPrefab
                        : _zoningSurfacePrefab,
                    heightData))
                { flaechen += n; yield return n; }
            /*
             * Immer gesetzt, wie die Parzellen selbst: ohne diesen Belag
             * saehe man Autos ueber Gras fahren, denn die Zoning-Strasse ist
             * unsichtbar. Das ist keine Zierde, sondern die Fahrbahn.
             */
            if (layout.ZoningRoadSurface != null
                && layout.ZoningRoadSurface.Length > 0)
            {
                if (_zoningBelagPrefab != Entity.Null)
                {
                    foreach (int n in CreateAreaPreviewGroupSchritte("Zoningstrasse",
                        layout.ZoningRoadSurface, _zoningBelagPrefab,
                        heightData))
                { flaechen += n; yield return n; }
                }
                else
                {
                    /*
                     * NICHT STILL UEBERSPRINGEN.
                     *
                     * Genau diese Bauart - eine Flaeche faellt aus, und
                     * nirgends steht warum - hat am 2026-09-02 einen halben
                     * Nachmittag gekostet. Der Nutzer fragte daraufhin, ob
                     * der Bauzettel so etwas ueberhaupt meldet. Tut er
                     * nicht; er haelt die gewaehlten Flaechen fest, damit
                     * sich derselbe Parkplatz nachbauen laesst. Ein Ausfall
                     * gehoert deshalb mindestens ins Log und in den Abzug.
                     */
                    RecordPreviewDiagnostic("Warning",
                        $"Belag der Zoning-Straße nicht gesetzt: "
                        + $"{layout.ZoningRoadSurface.Length} Ring(e) "
                        + "warten auf den Prefabklon mit der Ebene Roads. "
                        + "Die Straße bleibt befahrbar, sieht aber wie Gras "
                        + "aus.");
                    Mod.log.Warn("PLT-Zoningstrasse: Belag nicht gesetzt - "
                        + layout.ZoningRoadSurface.Length
                        + " Ring(e), aber der Prefabklon ist noch nicht "
                        + "benutzbar.");
                }
            }
            /*
             * STRASSENBELAG (Issue #22): die sichtbaren Alleys im Parkplatz.
             *
             * Asphalt und Gras zeichnen nicht mehr auf Strassen, sonst
             * erscheinen sie auf der Fahrbahn einer Bruecke darueber. Die
             * Alley haben sie bisher genau damit abgedeckt. Der Zellenkern
             * spart sie jetzt aus Asphalt und Gras aus und liefert sie als
             * eigene, lueckenlose Ringliste (`StrassenbelagSurface`); hier
             * entstehen sie mit dem Vorflaechen-Klon, der auf Strassen zeichnet,
             * samt eingesetzter Vorflaeche der Gasse (GeplanteBauflaechen).
             * Rueckfall ohne Klon: Asphalt - dann liegt die Alley offen, aber
             * es fehlt keine Flaeche.
             */
            if (strasseAn && strassenbelag.Length > 0)
            {
                if (_strassenbelagPrefab == Entity.Null)
                    RecordPreviewDiagnostic("Warning", "Strassenbelag mit Asphalt "
                        + "gebaut: der Vorflaechen-Klon ist noch nicht bereit, die "
                        + "Alley bleibt im Parkplatz sichtbar.");
                foreach (int n in CreateAreaPreviewGroupSchritte("Strassenbelag",
                    strassenbelag,
                    _strassenbelagPrefab != Entity.Null ? _strassenbelagPrefab
                        : _asphaltBelagPrefab != Entity.Null ? _asphaltBelagPrefab
                        : _pavementSurfacePrefab,
                    heightData))
                { flaechen += n; yield return n; }
            }
            if (strasseAn && verschmolzen.Length > 0)
                foreach (int n in CreateAreaPreviewGroupSchritte("Asphalt mit Vorflaeche",
                    verschmolzen,
                    _asphaltBelagPrefab != Entity.Null
                        ? _asphaltBelagPrefab : vorflaechenPrefab,
                    heightData))
                { flaechen += n; yield return n; }
            if (strasseAn && einzelneVorflaechen.Length > 0)
                foreach (int n in CreateAreaPreviewGroupSchritte("Vorflaeche",
                    einzelneVorflaechen, vorflaechenPrefab, heightData))
                { flaechen += n; yield return n; }
            /*
             * FAHRBAHN (Issue #22): die unsichtbare Strasse, an der eine
             * Alley endet - meist die Randstrasse - als eigene durchgaengige
             * Flaeche mit Strassen-Ebene. Sie liegt ueber dem Asphalt und
             * damit ueber dem Halbkreis des Alley-Knotens. Texturlage vom
             * Asphaltring darunter (RichteFahrbahnAus). Ohne den Klon nicht
             * bauen: mit dem Asphaltklon deckte sie nichts.
             */
            if (strasseAn && fahrbahn.Length > 0)
            {
                if (_strassenbelagPrefab != Entity.Null)
                    foreach (int n in CreateAreaPreviewGroupSchritte("Fahrbahn",
                        fahrbahn, _strassenbelagPrefab, heightData))
                    { flaechen += n; yield return n; }
                else
                    RecordPreviewDiagnostic("Warning", "Fahrbahn nicht gesetzt: "
                        + "der Vorflaechen-Klon ist noch nicht bereit, der Halbkreis der "
                        + "Alley bleibt sichtbar.");
            }
            created += flaechen;
            // Die beiden Zoning-Listen zaehlen mit: sie werden immer
            // gesetzt, unabhaengig von den Schaltern fuer Strasse und
            // Dekoration. Fehlten sie im Soll, meldete der Zaehler
            // "vollstaendig", waehrend Parzellen oder Fahrbahn fehlten.
            var geplant = (dekoAn ? gras.Length : 0)
                + (strasseAn ? asphalt.Length + verschmolzen.Length
                    + einzelneVorflaechen.Length : 0)
                + (ZoningFlaecheAus ? 0 : layout.ZoningSurface?.Length ?? 0)
                + (layout.ZoningRoadSurface?.Length ?? 0)
                + (strasseAn ? strassenbelag.Length : 0)
                + (strasseAn && _strassenbelagPrefab != Entity.Null ? fahrbahn.Length : 0);
            ParkingLotLiveLog.Zeile("flaechen " + flaechen + "/" + geplant
                + " gesetzt | gras " + gras.Length
                + " asphalt " + asphalt.Length
                + " bauland " + (layout.ZoningSurface?.Length ?? 0)
                + " zoningstrasse " + (layout.ZoningRoadSurface?.Length ?? 0)
                + " vorflaeche " + (verschmolzen.Length
                    + einzelneVorflaechen.Length)
                + " | entitaeten gesamt folgt");
            /*
             * REIHENFOLGE GEMESSEN, NICHT ANGENOMMEN.
             *
             * Am 2026-09-18 standen diese zwei Zeilen versuchsweise VOR den
             * Flaechen - die Vermutung war, dass die Entstehungsreihenfolge
             * ueber die Zeichenreihenfolge entscheidet. Sie tut es nicht:
             * der helle Halbkreis in der Einmuendung blieb unveraendert.
             * Damit ist auch dieser Weg ausgeschlossen, und die Zeilen
             * stehen wieder dort, wo sie immer standen.
             *
             * Wege und Decals haengen an derselben Vorschau-Transaktion. Ein
             * einziges ApplyMode.Apply macht spaeter alles zusammen
             * dauerhaft; getrennte Durchgaenge wuerden auseinanderlaufende
             * Zustaende erzeugen, sobald einer davon fehlschlaegt.
             */
            ProtokolliereBauschritt("CreateNetDefinitions");
            foreach (int n in CreateNetDefinitionsSchritte(layout, _areaPreviewSettings,
                heightData))
            { created += n; yield return n; }
            if (_bauarbeiter)
            {
                int netzbild = UnityEngine.Time.frameCount;
                while (UnityEngine.Time.frameCount - netzbild < 3) yield return 0;
                foreach (int n in SchliesseHintergrundNetzbesitz()) yield return n;
            }
            ProtokolliereBauschritt("CreateBayDecalDefinitions");
            foreach (int n in CreateBayDecalDefinitionsSchritte(layout, _areaPreviewSettings,
                heightData))
            { created += n; yield return n; }
            // Laternen vor den Pflanzen: die Bepflanzung haelt ihre Punkte frei.
            ProtokolliereBauschritt("CreateLanternDefinitions");
            foreach (int n in CreateLanternDefinitionsSchritte(layout, heightData))
            { created += n; yield return n; }
            ProtokolliereBauschritt("CreateVegetationDefinitions");
            /*
             * DIE BEPFLANZUNG HAENGT NICHT AM DEKO-SCHALTER.
             *
             * Hier stand `dekoAn ? gras : leer`. Damit setzte ein
             * abgeschalteter Deko-Belag auch die Baeume ab - der Befund des
             * Nutzers vom 2026-09-15. Die Gruenflaechen sind weiterhin da,
             * sie bekommen nur keinen Belag; ein Baum braucht darunter
             * keinen.
             */
            foreach (int n in CreateVegetationDefinitionsSchritte(layout.GrassForVegetation, heightData))
            { created += n; yield return n; }
            ProtokolliereBauschritt("CreateEntranceArrowDefinitions");
            foreach (int n in CreateEntranceArrowDefinitionsSchritte(layout, heightData))
            { created += n; yield return n; }
            yield break;
        }

        private static void MeasureAreaPreviewGroup(
            float2[][] polygons,
            ref TerrainHeightData heightData,
            ref int sampled,
            ref float minimum,
            ref float maximum)
        {
            if (polygons == null) return;

            for (var polygonIndex = 0; polygonIndex < polygons.Length; polygonIndex++)
            {
                var polygon = polygons[polygonIndex];
                var nodeCount = OpenNodeCount(polygon);
                if (nodeCount < 3) continue;

                for (var nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
                {
                    var point = polygon[nodeIndex];
                    if (!math.all(math.isfinite(point)))
                        throw new InvalidOperationException(
                            "Eine Polygonfläche enthält eine nicht-endliche Koordinate.");

                    var height = TerrainUtils.SampleHeight(
                        ref heightData, new float3(point.x, 0f, point.y));
                    if (!math.isfinite(height))
                        throw new InvalidOperationException(
                            "Die Terrain-Abtastung einer Polygonfläche ist nicht endlich.");

                    minimum = math.min(minimum, height);
                    maximum = math.max(maximum, height);
                    sampled++;
                }
            }
        }

        private int CreateAreaPreviewGroup(
            string kind,
            float2[][] polygons,
            Entity prefab,
            ref TerrainHeightData heightData)
        {
            int n = 0;
            foreach (int teil in CreateAreaPreviewGroupSchritte(kind, polygons, prefab, heightData)) n += teil;
            return n;
        }

        private System.Collections.Generic.IEnumerable<int> CreateAreaPreviewGroupSchritte(string kind, float2[][] polygons, Entity prefab, TerrainHeightData heightData)
        {
            if (polygons == null) yield break;

            ProtokolliereBauschritt("CreateAreaPreviewGroup " + kind);
            var created = 0;
            for (var i = 0; i < polygons.Length; i++)
            {
                yield return 0;
                if (CreateAreaPreviewDefinition(kind, i, polygons[i], prefab, ref heightData))
                { created++; yield return 1; }
            }
            yield break;
        }

        private bool CreateAreaPreviewDefinition(
            string kind,
            int index,
            float2[] polygon,
            Entity prefab,
            ref TerrainHeightData heightData)
        {
            var nodeCount = OpenNodeCount(polygon);
            if (nodeCount < 3) return false;

            for (var i = 0; i < nodeCount; i++)
                if (!math.all(math.isfinite(polygon[i])))
                    throw new InvalidOperationException(
                        "Eine Polygonfläche enthält eine nicht-endliche Koordinate.");

            var definition = EntityManager.CreateEntity();
            EntityManager.AddComponentData(definition, new CreationDefinition
            {
                m_Prefab = prefab,
            });
            EntityManager.AddComponent<Updated>(definition);

            // Wie CS2s AreaToolSystem: beliebig viele, auch konkave Knoten;
            // der letzte Puffereintrag schließt den Ring explizit.
            var nodes = EntityManager.AddBuffer<Game.Areas.Node>(definition);
            nodes.ResizeUninitialized(nodeCount + 1);
            var sentNodes = new float3[nodeCount + 1];
            for (var i = 0; i < nodeCount; i++)
            {
                var point = polygon[i];
                var height = TerrainUtils.SampleHeight(
                    ref heightData, new float3(point.x, 0f, point.y));
                sentNodes[i] = new float3(point.x, height, point.y);
                nodes[i] = new Game.Areas.Node(sentNodes[i], float.MinValue);
            }
            nodes[nodeCount] = nodes[0];
            sentNodes[nodeCount] = sentNodes[0];
            RecordAreaDefinition(kind, index, prefab,
                definition, sentNodes);
            return true;
        }

        private long AreaPreviewSignature(ParkingLayout layout)
        {
            unchecked
            {
                var signature = 1125899906842597L;
                signature = AppendSettings(signature, _areaPreviewSettings);
                // Die gemerkte Reglerbreite bleibt auch bei abgeschaltetem
                // Mittelgruen Teil des Bauzettels. Md allein ist dann immer 0
                // und koennte eine echte Einstellungsänderung nicht erkennen.
                signature = AppendDouble(signature,
                    _uiSystem?.AktuelleMedianbreite ?? _areaPreviewSettings?.Md ?? 0);
                signature = signature * 31
                    + ((_uiSystem?.MittelgruenAn ?? (_areaPreviewSettings?.Md > 0))
                        ? 1 : 0);
                signature = AppendDouble(signature,
                    _uiSystem?.AktuelleQuerbuchten ?? 0);
                signature = AppendPolygonGroup(signature, layout.GrassSurface);
                signature = AppendPolygonGroup(signature, layout.AsphaltSurface);
                signature = AppendPolygonGroup(signature, layout.ZoningRoadSurface);
                signature = AppendPolygonGroup(signature, layout.ZoningSurface);
                signature = AppendPolygonGroup(signature, layout.StrassenbelagSurface);
                signature = AppendPolygonGroup(signature, layout.FahrbahnSurface);
                // Im Bauzettel 23.09. gingen 3 Klicks auf Innen AUS verloren:
                // zweimal meldete Enter "ohne Aenderungen". Die Signatur
                // muss auch die 4 geladenen Handschaltungen vergleichen.
                signature = signature * 31 + _zoningSeitenPlan.Count;
                foreach (var seite in _zoningSeitenPlan)
                {
                    signature = AppendDouble(signature, seite.A.x);
                    signature = AppendDouble(signature, seite.A.y);
                    signature = AppendDouble(signature, seite.B.x);
                    signature = AppendDouble(signature, seite.B.y);
                    signature = signature * 31 + (seite.Links ? 1 : 0);
                    signature = signature * 31 + (seite.Aus ? 1 : 0);
                }
                /*
                 * DIE BUSHALTESTELLEN GEHOEREN DAZU.
                 *
                 * Nutzer, 2026-09-24: im Edit Haltestellen gesetzt, "Bauen"
                 * geklickt - "da passiert nix". Der Log dreimal:
                 * "Ausstieg durch Uebernehmen ohne Aenderungen". Die
                 * Signatur kannte sie nicht, und ein Edit, der NUR
                 * Haltestellen setzt, sah damit unveraendert aus. Derselbe
                 * Fehler wie am 23.09. bei den Zoning-Seitenschaltern.
                 */
                signature = signature * 31 + _busStops.Count;
                foreach (var halt in _busStops)
                {
                    signature = AppendDouble(signature, halt.A.x);
                    signature = AppendDouble(signature, halt.A.y);
                    signature = AppendDouble(signature, halt.B.x);
                    signature = AppendDouble(signature, halt.B.y);
                    signature = AppendDouble(signature, halt.Along);
                    signature = signature * 31 + (halt.Left ? 1 : 0);
                }
                signature = AppendPolygonGroup(signature, _vorflaechen);
                signature = signature * 31 + ((_baukontext?.Zettel.SurfaceApronOn ?? _uiSystem?.VorflaecheAn ?? true) ? 1 : 0);
                signature = AppendText(signature, GrassSurfaceName);
                signature = AppendText(signature, PavementSurfaceName);
                signature = signature * 31 + _vorflaechenPrefab.Index;
                signature = signature * 31 + _vorflaechenPrefab.Version;
                // Ein fertig initialisierter Raeumbelag muss die Vorschau
                // ersetzen; sonst bliebe der erste Vanilla-Rueckfall stehen.
                signature = signature * 31 + _dekoBelagPrefab.Index;
                signature = signature * 31 + _dekoBelagPrefab.Version;
                signature = signature * 31 + _asphaltBelagPrefab.Index;
                signature = signature * 31 + _asphaltBelagPrefab.Version;
                // Ohne diese zwei Zeilen bliebe die Vorschau stehen, wenn nur
                // ein Schalter umgelegt wird: die Geometrie ist ja dieselbe.
                signature = signature * 31 + ((_baukontext?.Zettel.SurfaceRoadOn ?? _uiSystem?.FlaecheStrasseAn ?? true) ? 1 : 0);
                signature = signature * 31 + ((_baukontext?.Zettel.SurfaceDecorationOn ?? _uiSystem?.FlaecheDekoAn ?? true) ? 1 : 0);
                signature = signature * 31 + ((_baukontext?.Zettel.BayIcons ?? _uiSystem?.Buchtsymbole ?? true) ? 1 : 0);
                signature = signature * 31 + (_uiSystem?.VegetationJson.GetHashCode() ?? 0);
                signature = signature * 31 + (_uiSystem?.LaternenJson.GetHashCode() ?? 0);
                signature = signature * 31 + _grassSurfacePrefab.Index;
                signature = signature * 31 + _grassSurfacePrefab.Version;
                signature = signature * 31 + _zoningSurfacePrefab.Index;
                signature = signature * 31 + _zoningSurfacePrefab.Version;
                // Ein Zustand, der das Ergebnis aendert, gehoert in den
                // Schluessel - sonst bliebe die Vorschau nach dem Waehlen
                // einer Bezugslinie stehen.
                signature = signature * 31
                    + (Ausrichtwinkel?.GetHashCode() ?? 0);
                signature = signature * 31 + _pavementSurfacePrefab.Index;
                signature = signature * 31 + _pavementSurfacePrefab.Version;
                // Wege und Decals folgen demselben Layout, haengen aber an
                // eigenen Prefabs. Ohne sie bliebe eine Vorschau stehen, die
                // erzeugt wurde, bevor diese Prefabs aufloesbar waren.
                signature = AppendPolygonGroup(signature, layout.AisleLine);
                signature = AppendPolygonGroup(signature, layout.CrossLine);
                signature = AppendPolygonGroup(signature, layout.PerimeterLine);
                signature = AppendPolygonGroup(signature, layout.EntranceLine);
                signature = AppendPolygonGroup(signature, layout.Bay);
                signature = signature * 31 + _pathPrefabs.Count;
                signature = signature * 31 + _bayDecalPrefab.Index;
                signature = signature * 31 + _bayDecalPrefab.Version;
                signature = signature * 31 + _disabledDecalPrefab.Index;
                signature = signature * 31 + _electricDecalPrefab.Index;
                return signature;
            }
        }

        private static long AppendSettings(long signature, LayoutSettings settings)
        {
            unchecked
            {
                if (settings == null) return signature * 31 - 1;
                signature = AppendDouble(signature, settings.Es);
                signature = AppendDouble(signature, settings.Ai);
                signature = AppendDouble(signature, settings.Cw);
                signature = AppendDouble(signature, settings.Sl);
                signature = AppendDouble(signature, settings.Sw);
                signature = AppendDouble(signature, settings.Md);
                signature = AppendDouble(signature, settings.Cr);
                signature = AppendDouble(signature, settings.Angle);
                signature = AppendDouble(signature, settings.KantenVersatz);
                signature = signature * 31 + (settings.Qk ? 1 : 0);
                signature = signature * 31 + (settings.Randstrassen ? 1 : 0);
                signature = signature * 31 + (settings.Auto ? 1 : 0);
                signature = signature * 31 + (settings.AutomaticEntrances ? 1 : 0);
                signature = signature * 31 + (settings.Zellen ? 1 : 0);
                signature = signature * 31 + (settings.EineFlaeche ? 1 : 0);
                signature = signature * 31 + (settings.NoNotch ? 1 : 0);
                signature = signature * 31 + (settings.Single ? 1 : 0);
                signature = signature * 31 + (settings.NoHalf ? 1 : 0);
                signature = AppendText(signature, settings.AngleMode);
                /*
                 * DIE BAULANDFLAECHEN GEHOEREN IN DIE SIGNATUR.
                 *
                 * Ohne sie rechnet die Vorschau nicht neu, wenn eine Flaeche
                 * gesetzt, verschoben oder gedreht wird - und genau das hat
                 * der Nutzer gemeldet: "die Strassen-Preview aenderte sich
                 * nicht nach dem Erstellen der Flaeche". Die Signatur ist die
                 * Stelle, an der eine neue Einstellung am leisesten
                 * verlorengeht: alles rechnet richtig, nur sieht es niemand.
                 */
                var bauland = settings.Zoningflaechen
                    ?? Array.Empty<ParkingGeometry.Zoningflaeche>();
                signature = signature * 31 + bauland.Length;
                foreach (var flaeche in bauland)
                {
                    if (flaeche == null) continue;
                    signature = AppendDouble(signature, flaeche.Ecke.x);
                    signature = AppendDouble(signature, flaeche.Ecke.y);
                    signature = signature * 31 + flaeche.Spalten;
                    signature = signature * 31 + flaeche.Reihen;
                    signature = AppendDouble(signature, flaeche.Winkel);
                }
                var entrances = settings.Entrances ?? Array.Empty<Entrance>();
                signature = signature * 31 + entrances.Length;
                for (var i = 0; i < entrances.Length; i++)
                {
                    var entrance = entrances[i];
                    if (entrance == null)
                    {
                        signature = signature * 31 - 1;
                        continue;
                    }
                    signature = signature * 31 + entrance.Edge;
                    signature = AppendDouble(signature, entrance.Along);
                    signature = AppendText(signature, entrance.Corner);
                    signature = signature * 31 + (int)entrance.Art;
                }
                var ausrichtungen = settings.TeilflaechenAusrichtungen
                    ?? Array.Empty<TeilflaechenAusrichtung>();
                signature = signature * 31 + ausrichtungen.Length;
                for (var i = 0; i < ausrichtungen.Length; i++)
                {
                    var ausrichtung = ausrichtungen[i];
                    if (ausrichtung == null)
                    {
                        signature = signature * 31 - 1;
                        continue;
                    }
                    signature = AppendDouble(signature, ausrichtung.Anker.x);
                    signature = AppendDouble(signature, ausrichtung.Anker.y);
                    signature = AppendDouble(signature, ausrichtung.Winkel);
                }
                return signature;
            }
        }

        private static long AppendDouble(long signature, double value)
        {
            unchecked
            {
                var bits = BitConverter.DoubleToInt64Bits(value);
                signature = signature * 31 + (int)bits;
                return signature * 31 + (int)(bits >> 32);
            }
        }

        private static long AppendText(long signature, string value)
        {
            unchecked
            {
                if (value == null) return signature * 31 - 1;
                signature = signature * 31 + value.Length;
                for (var i = 0; i < value.Length; i++)
                    signature = signature * 31 + value[i];
                return signature;
            }
        }

        private static long AppendPolygonGroup(long signature, float2[][] polygons)
        {
            unchecked
            {
                if (polygons == null) return signature * 31 - 1;
                signature = signature * 31 + polygons.Length;
                for (var i = 0; i < polygons.Length; i++)
                {
                    var polygon = polygons[i];
                    if (polygon == null)
                    {
                        signature = signature * 31 - 1;
                        continue;
                    }

                    signature = signature * 31 + polygon.Length;
                    for (var j = 0; j < polygon.Length; j++)
                    {
                        signature = signature * 31 + math.asint(polygon[j].x);
                        signature = signature * 31 + math.asint(polygon[j].y);
                    }
                }
                return signature;
            }
        }

        private static bool HasPreviewPolygons(ParkingLayout layout)
        {
            return layout != null
                && (HasPreviewPolygon(layout.ZoningSurface)
                    || HasPreviewPolygon(layout.GrassSurface)
                    || HasPreviewPolygon(layout.AsphaltSurface)
                    || HasPreviewPolygon(layout.ZoningRoadSurface));
        }

        private static bool HasPreviewPolygon(float2[][] polygons)
        {
            if (polygons == null) return false;
            for (var i = 0; i < polygons.Length; i++)
                if (OpenNodeCount(polygons[i]) >= 3) return true;
            return false;
        }

        private static int OpenNodeCount(float2[] polygon)
        {
            if (polygon == null) return 0;
            var count = polygon.Length;
            if (count > 1 && math.all(polygon[0] == polygon[count - 1])) count--;
            return count;
        }
    }
}
