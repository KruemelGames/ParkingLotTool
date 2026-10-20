using System;
using System.Text;
using ParkingLotTool.Geometry;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private bool SchreibeBauzettel(Entity lot, Bauzettelquelle q)
        {
            var settings = q.Settings;
            var worldPoints = q.Punkte;
            if (lot == Entity.Null || !EntityManager.Exists(lot)
                || settings == null || worldPoints == null
                || worldPoints.Length < MinPolygonPoints) return false;
            // Auftrag 4 im Lauf 13:01 brach vor dem ersten ECS-Schreiben ab:
            // fehlende optionale Ausrichtungs-/Schnitt-/Zonenpuffer sind null.
            // Auch Normalbau und Protokollreparatur schreiben dieselbe Quelle.
            q.Ausrichtungen = HintergrundTakt.Liste(q.Ausrichtungen);
            q.Trennschnitte = HintergrundTakt.Liste(q.Trennschnitte);
            q.Zoningflaechen = HintergrundTakt.Liste(q.Zoningflaechen);
            q.Seitenplan = HintergrundTakt.Liste(q.Seitenplan);
            q.Randzoning = HintergrundTakt.Liste(q.Randzoning);
            try
            {
                var receipt = new ParkingLotBuildReceipt
                {
                    Version = ParkingLotBuildReceipt.CurrentVersion,
                    Es = settings.Es,
                    Ai = settings.Ai,
                    Cw = settings.Cw,
                    Sl = settings.Sl,
                    Sw = settings.Sw,
                    Md = settings.Md,
                    Cr = settings.Cr,
                    Gassenbreite = settings.Gassenbreite,
                    Angle = settings.Angle,
                    KantenVersatz = settings.KantenVersatz,
                    Qk = settings.Qk,
                    Randstrassen = settings.Randstrassen,
                    Auto = settings.Auto,
                    AutomaticEntrances = settings.AutomaticEntrances,
                    Zellen = settings.Zellen,
                    EineFlaeche = settings.EineFlaeche,
                    NoNotch = settings.NoNotch,
                    Single = settings.Single,
                    NoHalf = settings.NoHalf,
                    AngleMode = ParkingLotBuildReceipt
                        .EncodeAngleMode(settings.AngleMode),
                    MedianWidth = q.MedianWidth,
                    GreenMedian = q.GreenMedian,
                    CrossBays = q.CrossBays,
                    SurfaceRoadOn = q.SurfaceRoadOn,
                    SurfaceDecorationOn = q.SurfaceDecorationOn,
                    SurfaceApronOn = q.SurfaceApronOn,
                    BayIcons = q.BayIcons,
                    ZoningWinkelmodus = Winkelmodus.Kodiere(q.ZoningWinkelmodus),
                    ZoningReglerwinkel = q.ZoningReglerwinkel,
                    ZoningAussentiefeVorwahl = q.ZoningAussentiefeVorwahl,
                    ZoningAusrichtwinkel = q.ZoningAusrichtwinkel ?? double.NaN,
                    BusStopCount = settings.BusStops?.Length ?? 0,
                    // NaN heisst "keine Bezugslinie" - so bleibt der Wert
                    // auch ohne Ausrichtung eindeutig.
                    Ausrichtwinkel = q.Ausrichtwinkel ?? double.NaN,
                    AusrichtAx = q.Ausrichtungen.Count > 0 ? q.Ausrichtungen[0].LinieA.x : 0f,
                    AusrichtAz = q.Ausrichtungen.Count > 0 ? q.Ausrichtungen[0].LinieA.y : 0f,
                    AusrichtBx = q.Ausrichtungen.Count > 0 ? q.Ausrichtungen[0].LinieB.x : 0f,
                    AusrichtBz = q.Ausrichtungen.Count > 0 ? q.Ausrichtungen[0].LinieB.y : 0f,
                };
                if (EntityManager.HasComponent<ParkingLotBuildReceipt>(lot))
                    EntityManager.SetComponentData(lot, receipt);
                else EntityManager.AddComponentData(lot, receipt);

                var pointBuffer = EntityManager.HasBuffer<ParkingLotBuildPoint>(lot)
                    ? EntityManager.GetBuffer<ParkingLotBuildPoint>(lot)
                    : EntityManager.AddBuffer<ParkingLotBuildPoint>(lot);
                pointBuffer.Clear();
                for (var i = 0; i < worldPoints.Length; i++)
                    pointBuffer.Add(new ParkingLotBuildPoint
                    {
                        Version = ParkingLotBuildPoint.CurrentVersion,
                        Position = worldPoints[i],
                    });

                var entranceBuffer = EntityManager
                    .HasBuffer<ParkingLotBuildEntrance>(lot)
                    ? EntityManager.GetBuffer<ParkingLotBuildEntrance>(lot)
                    : EntityManager.AddBuffer<ParkingLotBuildEntrance>(lot);
                entranceBuffer.Clear();
                var entrances = settings.Entrances ?? Array.Empty<Entrance>();
                for (var i = 0; i < entrances.Length; i++)
                {
                    var entrance = entrances[i];
                    if (entrance == null) continue;
                    entranceBuffer.Add(new ParkingLotBuildEntrance
                    {
                        Version = ParkingLotBuildEntrance.CurrentVersion,
                        Edge = entrance.Edge,
                        Along = entrance.Along,
                        Corner = EncodeCorner(entrance.Corner),
                        Art = entrance.Art,
                        HasAxis = entrance.AxisDirection.HasValue
                            && entrance.AxisLength.HasValue,
                        AxisDirection = entrance.AxisDirection ?? default,
                        AxisLength = entrance.AxisLength ?? 0,
                    });
                }
                /*
                 * EIGENER OPTIONALER PUFFER. Der Bauzettel selbst bleibt in
                 * Fassung 2 lesbar; alte Spielstaende besitzen diesen Typ
                 * schlicht nicht und fallen beim Lesen auf ihren einen
                 * gespeicherten Winkel zurueck.
                 */
                if (q.Ausrichtungen.Count > 0)
                {
                    var alignmentBuffer = EntityManager
                        .HasBuffer<ParkingLotBuildAlignment>(lot)
                        ? EntityManager.GetBuffer<ParkingLotBuildAlignment>(lot)
                        : EntityManager.AddBuffer<ParkingLotBuildAlignment>(lot);
                    alignmentBuffer.Clear();
                    for (var i = 0; i < q.Ausrichtungen.Count; i++)
                    {
                        var alignment = q.Ausrichtungen[i];
                        alignmentBuffer.Add(new ParkingLotBuildAlignment
                        {
                            Version = ParkingLotBuildAlignment.CurrentVersion,
                            Anchor = alignment.Anker,
                            LineA = alignment.LinieA,
                            LineB = alignment.LinieB,
                            Angle = alignment.Winkel,
                        });
                    }
                }
                else if (EntityManager.HasBuffer<ParkingLotBuildAlignment>(lot))
                {
                    EntityManager.RemoveComponent<ParkingLotBuildAlignment>(lot);
                }

                // Strassenkurven: Formel, Zusammenfassung, gebaute Stationen.
                ParkingLotKurvenzettelPuffer.Schreibe(EntityManager, lot, q.Kurven);
                if (q.Kurven != null)
                    foreach (var kurve in q.Kurven)
                        if (kurve != null)
                            Mod.log.Info("PLT-Kurve gebaut: " + kurve.Kurzzeile() + ".");

                // Die Handschnitte gehoeren zu den Zuweisungen: sie legen
                // fest, WELCHE Teilflaechen es ueberhaupt gibt.
                if (q.Trennschnitte.Count > 0)
                {
                    var cutBuffer = EntityManager
                        .HasBuffer<ParkingLotBuildCut>(lot)
                        ? EntityManager.GetBuffer<ParkingLotBuildCut>(lot)
                        : EntityManager.AddBuffer<ParkingLotBuildCut>(lot);
                    cutBuffer.Clear();
                    for (var i = 0; i < q.Trennschnitte.Count; i++)
                        cutBuffer.Add(new ParkingLotBuildCut
                        {
                            Version = ParkingLotBuildCut.CurrentVersion,
                            A = q.Trennschnitte[i].A,
                            B = q.Trennschnitte[i].B,
                        });
                }
                else if (EntityManager.HasBuffer<ParkingLotBuildCut>(lot))
                {
                    EntityManager.RemoveComponent<ParkingLotBuildCut>(lot);
                }

                // Die Zoning-Flaechen aus demselben Grund wie die Schnitte:
                // ohne sie waere ein bearbeiteter Parkplatz um seine
                // Parzellen aermer.
                if (q.Zoningflaechen.Count > 0)
                {
                    var zonePuffer = EntityManager
                        .HasBuffer<ParkingLotBuildZoning>(lot)
                        ? EntityManager.GetBuffer<ParkingLotBuildZoning>(lot)
                        : EntityManager.AddBuffer<ParkingLotBuildZoning>(lot);
                    zonePuffer.Clear();
                    for (var i = 0; i < q.Zoningflaechen.Count; i++)
                        zonePuffer.Add(new ParkingLotBuildZoning
                        {
                            Version = ParkingLotBuildZoning.CurrentVersion,
                            Ecke = q.Zoningflaechen[i].Ecke,
                            Spalten = q.Zoningflaechen[i].Spalten,
                            Reihen = q.Zoningflaechen[i].Reihen,
                            Winkel = q.Zoningflaechen[i].Winkel,
                            Rand = q.Zoningflaechen[i].Rand,
                            // Ohne diese vier verloere der Parkplatz seine
                            // Aussenbaender beim naechsten Laden.
                            Aussen0 = ParkingGeometry.ZoningAussentiefe(
                                q.Zoningflaechen[i], 0),
                            Aussen1 = ParkingGeometry.ZoningAussentiefe(
                                q.Zoningflaechen[i], 1),
                            Aussen2 = ParkingGeometry.ZoningAussentiefe(
                                q.Zoningflaechen[i], 2),
                            Aussen3 = ParkingGeometry.ZoningAussentiefe(
                                q.Zoningflaechen[i], 3),
                        });
                    /*
                     * ZAEHLER STATT THEORIE.
                     *
                     * Der Nutzer am 2026-09-23: *"wenn ich im Edit bei der
                     * Zoningflaeche aussen das Tiling/Zoning aktiviere/
                     * deaktiviere und baue wird das nicht gespeichert."*
                     * Die Kette liest sich korrekt - Schalter, Schreiben,
                     * Zurueckholen -, also sagt nur eine Messung, an welcher
                     * Stelle die Tiefen verlorengehen. Die Zeile steht
                     * bewusst bei jedem Bau, nicht nur beim Umbau: damit
                     * beantwortet derselbe Log auch seine zweite Frage, ob
                     * es nur den Umbau betrifft.
                     */
                    var tiefen = new System.Text.StringBuilder();
                    for (var i = 0; i < q.Zoningflaechen.Count; i++)
                    {
                        if (i > 0) tiefen.Append(" | ");
                        for (var s = 0; s < 4; s++)
                        {
                            if (s > 0) tiefen.Append('/');
                            tiefen.Append(ParkingGeometry
                                .ZoningAussentiefe(q.Zoningflaechen[i], s)
                                .ToString("0.##",
                                    System.Globalization.CultureInfo.InvariantCulture));
                        }
                    }
                    Mod.log.Info("PLT-Zoningzettel GESCHRIEBEN ("
                        + (IsEditing ? "Umbau" : "Neubau") + "): "
                        + q.Zoningflaechen.Count + " Flaeche(n), Aussentiefen "
                        + tiefen + " (je Flaeche Seite 0/1/2/3).");
                }
                else if (EntityManager.HasBuffer<ParkingLotBuildZoning>(lot))
                {
                    EntityManager.RemoveComponent<ParkingLotBuildZoning>(lot);
                }

                /*
                 * DIE HANDGESCHALTETEN SEITEN GEHOEREN DAZU.
                 *
                 * Sie entstehen jetzt schon in der Vorschau, also lange vor
                 * der Kante, an der sie am Ende haengen. Ohne diesen Puffer
                 * waeren sie mit dem Bauen weg - und der Nutzer haette die
                 * Arbeit umsonst gemacht.
                 */
                var seitenPuffer = EntityManager
                    .HasBuffer<ParkingLotBuildZoningSeite>(lot)
                    ? EntityManager.GetBuffer<ParkingLotBuildZoningSeite>(lot)
                    : EntityManager.AddBuffer<ParkingLotBuildZoningSeite>(lot);
                seitenPuffer.Clear();
                foreach (var seite in q.Seitenplan)
                    seitenPuffer.Add(new ParkingLotBuildZoningSeite
                    {
                        Version = ParkingLotBuildZoningSeite.CurrentVersion,
                        A = seite.A,
                        B = seite.B,
                        Links = seite.Links,
                        Aus = seite.Aus,
                    });
                var busPuffer = EntityManager.HasBuffer<ParkingLotBuildBusStop>(lot)
                    ? EntityManager.GetBuffer<ParkingLotBuildBusStop>(lot)
                    : EntityManager.AddBuffer<ParkingLotBuildBusStop>(lot);
                busPuffer.Clear();
                foreach (var stop in settings.BusStops ?? Array.Empty<BusStopPlacement>())
                    busPuffer.Add(new ParkingLotBuildBusStop
                    {
                        Version = ParkingLotBuildBusStop.CurrentVersion,
                        A = stop.A, B = stop.B, Along = stop.Along,
                        Left = stop.Left,
                    });
                Mod.log.Info("PLT-Bauzettel Bushalte GESCHRIEBEN: "
                    + busPuffer.Length + " Platzierung(en).");
                /*
                 * ZAEHLER AN DIE ZWEITE HAELFTE DER KETTE.
                 *
                 * Aussen haelt sie nachweislich (Log vom 2026-09-23,
                 * 16/0/16/16 geschrieben und identisch zurueckgelesen).
                 * Innen meldet der Nutzer, dass eine abgeschaltete Seite
                 * nach dem Bauen wieder da ist. Die Handschaltungen sind
                 * das einzige, was davon im Zettel steht - die PANELWAHL
                 * (Innen/Aussen/Beides) steht NIRGENDS, weder hier noch in
                 * `LayoutSettings`. Beides gehoert gemessen, bevor daran
                 * etwas geaendert wird.
                 */
                // AddBuffer<BusStop> kann die zuvor geliehene Seitensicht
                // invalidieren. Nach dieser Strukturveraenderung neu beziehen.
                seitenPuffer = EntityManager.GetBuffer<ParkingLotBuildZoningSeite>(lot);
                var seiten = new System.Text.StringBuilder();
                for (var i = 0; i < seitenPuffer.Length; i++)
                {
                    if (i > 0) seiten.Append(", ");
                    var e = seitenPuffer[i];
                    seiten.Append(e.Links ? "links " : "rechts ")
                        .Append(e.Aus ? "AUS" : "an");
                }
                Mod.log.Info("PLT-Zoningseitenplan GESCHRIEBEN ("
                    + (IsEditing ? "Umbau" : "Neubau") + "): "
                    + seitenPuffer.Length + " Handschaltung(en)"
                    + (seitenPuffer.Length > 0 ? " - " + seiten : string.Empty)
                    + ". Panelwahl " + ZoningSeite
                    + " - die steht NICHT im Zettel.");

                // Und das Randzoning, aus demselben Grund.
                var randPuffer = EntityManager
                    .HasBuffer<ParkingLotBuildRandzoning>(lot)
                    ? EntityManager.GetBuffer<ParkingLotBuildRandzoning>(lot)
                    : EntityManager.AddBuffer<ParkingLotBuildRandzoning>(lot);
                randPuffer.Clear();
                foreach (var linie in q.Randzoning)
                    randPuffer.Add(new ParkingLotBuildRandzoning
                    {
                        Version = ParkingLotBuildRandzoning.CurrentVersion,
                        A = linie.A,
                        B = linie.B,
                    });
                var textBuffer = EntityManager.HasBuffer<ParkingLotBuildText>(lot)
                    ? EntityManager.GetBuffer<ParkingLotBuildText>(lot)
                    : EntityManager.AddBuffer<ParkingLotBuildText>(lot);
                textBuffer.Clear();
                AddBuildText(textBuffer, 1, q.FlaecheStrasse ?? string.Empty);
                AddBuildText(textBuffer, 2, q.FlaecheDekoration ?? string.Empty);
                /*
                 * DIE DRITTE FLAECHE GEHOERT DAZU.
                 *
                 * Ohne sie saehe ein bearbeiteter Parkplatz anders aus als
                 * der gebaute: die Parzellen fielen still auf die
                 * Dekoflaeche zurueck. Leer ist ein gueltiger Wert und
                 * heisst genau das - "nimm die Dekoflaeche"; ein alter
                 * Bauzettel ohne Eintrag verhaelt sich damit richtig.
                 */
                AddBuildText(textBuffer, 3, q.FlaecheZoning ?? string.Empty);
                AddBuildText(textBuffer, 5, settings.Zoningstrasse);
                // Die Laternenwahl - beim Bearbeiten kommt sie ins Fenster zurueck.
                // Nicht beim Wiederherstellen aus dem Protokoll (siehe Bauzettelquelle).
                if (!q.OhneLaternenzettel)
                    AddBuildText(textBuffer, 6, Newtonsoft.Json.JsonConvert.SerializeObject(AktuelleLaternen));
                if (q.VegetationAusProtokoll == null) WriteVegetation(lot);
                else SchreibeVegetationAusProtokoll(lot, q.VegetationAusProtokoll);
                return true;
            }
            catch (Exception exception)
            {
                Mod.log.Error(exception,
                    "PLT-Bauzettel konnte nicht am neuen Lot gespeichert werden.");
                return false;
            }
        }

        private static int EncodeCorner(string corner)
            => corner == "start" ? 1 : corner == "end" ? 2 : 0;

        private static void AddBuildText(DynamicBuffer<ParkingLotBuildText> buffer,
                                         int kind, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            for (var i = 0; i < bytes.Length; i++)
                buffer.Add(new ParkingLotBuildText
                {
                    Version = ParkingLotBuildText.CurrentVersion,
                    Kind = kind,
                    Index = i,
                    Value = bytes[i],
                });
        }

    }
}
