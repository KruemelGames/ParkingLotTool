using System;
using System.Collections.Generic;
using System.Text;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;
using Ausrichtzuweisung = ParkingLotTool.Tools.ParkingLotToolSystem.Ausrichtzuweisung;

namespace ParkingLotTool.Tools
{
    /** Gemeinsamer Leser: nur der uebergebene Bauzettel, kein Panel/Werkzeugzustand.
     *  Die bisherigen Zulassungsregeln wurden verschoben, nicht neu erfunden.
     *  Alle Ergebnislisten sind Kopien; DynamicBuffer verlassen den Leser nie. */
    internal static class ParkingLotBaukontextLeser
    {
        internal static bool Vollstaendig(EntityManager em, Entity lot)
            => lot != Entity.Null && em.Exists(lot)
               && em.HasComponent<ParkingLotCarrierReference>(lot)
               && em.HasComponent<ParkingLotBuildReceipt>(lot)
               && em.HasBuffer<ParkingLotBuildPoint>(lot)
               && em.HasBuffer<ParkingLotBuildEntrance>(lot)
               && em.HasBuffer<ParkingLotBuildText>(lot);

        internal static bool TryRead(EntityManager em, Entity lot,
            out ParkingLotBaukontext kontext, out string grund, bool melden = true)
        {
            kontext = null;
            if (!TryReadWerte(em, lot, out var zettel, out var punkte,
                    out var zugaenge, out var ausrichtungen, out var schnitte,
                    out var zonen, out var strasse, out var deko, out var zoning,
                    out var seiten, out var rand, out var bus, out grund))
                return false;
            var texte = em.GetBuffer<ParkingLotBuildText>(lot, true);
            TryReadBuildText(texte, 4, out var vegetation);
            TryReadBuildText(texte, 5, out var zoningstrasse);
            kontext = new ParkingLotBaukontext(lot, zettel, punkte, zugaenge,
                ausrichtungen, schnitte, zonen, strasse, deko, zoning,
                vegetation, zoningstrasse, seiten, rand, bus)
            {
                Kurven = ParkingLotKurvenzettelPuffer.Lies(em, lot),
            };
            var messung = $"PLT-Hintergrund: Bauzettelkopie Lot {lot.Index}; "
                + $"Punkte {punkte.Length}, Zugaenge {zugaenge.Length}, "
                + $"Zoning {zonen?.Length ?? 0}, Seiten {seiten.Count}, "
                + $"Bushalte {bus.Count}; nur gelesen, kein Neubau.";
            if (melden) { Mod.log.Info(messung); ParkingGeometry.Live(messung); }
            return true;
        }

        private static bool TryReadWerte(EntityManager EntityManager, Entity lot,
            out ParkingLotBuildReceipt receipt, out float3[] points,
            out Entrance[] entrances, out Ausrichtzuweisung[] alignments,
            out Teilflaechenschnitt[] cuts,
            out ParkingGeometry.Zoningflaeche[] zonen,
            out string surfaceRoad,
            out string surfaceDecoration, out string surfaceZoning,
            out List<(float2 A, float2 B, bool Links, bool Aus)> seitenplan,
            out List<ParkingGeometry.RandzoningLinie> randplan,
            out List<BusStopPlacement> busStops,
            out string reason)
        {
            seitenplan = new List<(float2 A, float2 B, bool Links, bool Aus)>();
            randplan = new List<ParkingGeometry.RandzoningLinie>();
            busStops = new List<BusStopPlacement>();
            receipt = default;
            points = Array.Empty<float3>();
            entrances = Array.Empty<Entrance>();
            alignments = null;
            cuts = null;
            zonen = null;
            surfaceRoad = string.Empty;
            surfaceDecoration = string.Empty;
            surfaceZoning = string.Empty;
            reason = ParkingLotTexte.T("baukontextLeser.buildReceiptMissing");
            if (lot == Entity.Null || !EntityManager.Exists(lot)
                || !Vollstaendig(EntityManager, lot)) return false;

            receipt = EntityManager.GetComponentData<ParkingLotBuildReceipt>(lot);
            if (receipt.Version < 3
                || receipt.Version > ParkingLotBuildReceipt.CurrentVersion)
            {
                reason = ParkingLotTexte.T("baukontextLeser.unknownBuildReceiptVersion", ("receiptVersion", receipt.Version));
                return false;
            }
            if (!ValidReceiptSettings(receipt))
            {
                reason = ParkingLotTexte.T("baukontextLeser.invalidSettingsInTheBuildReceipt");
                return false;
            }

            var pointBuffer = EntityManager
                .GetBuffer<ParkingLotBuildPoint>(lot, true);
            if (pointBuffer.Length < 3)
            {
                reason = ParkingLotTexte.T("baukontextLeser.fewerThanThreeOutlinePoints");
                return false;
            }
            points = new float3[pointBuffer.Length];
            for (var i = 0; i < pointBuffer.Length; i++)
            {
                var point = pointBuffer[i];
                if (point.Version != ParkingLotBuildPoint.CurrentVersion
                    || !math.all(math.isfinite(point.Position)))
                {
                    reason = ParkingLotTexte.T("baukontextLeser.invalidOutlinePoint", ("i", i));
                    return false;
                }
                points[i] = point.Position;
            }

            var entranceBuffer = EntityManager
                .GetBuffer<ParkingLotBuildEntrance>(lot, true);
            entrances = new Entrance[entranceBuffer.Length];
            for (var i = 0; i < entranceBuffer.Length; i++)
            {
                var entrance = entranceBuffer[i];
                if (entrance.Version < 1
                    || entrance.Version > ParkingLotBuildEntrance.CurrentVersion
                    || entrance.Edge < 0 || entrance.Edge >= points.Length
                    || entrance.Corner < 0 || entrance.Corner > 2
                    || double.IsNaN(entrance.Along)
                    || double.IsInfinity(entrance.Along)
                    || !Enum.IsDefined(typeof(Zufahrtsart), entrance.Art)
                    || (entrance.HasAxis
                        && (!math.all(math.isfinite(entrance.AxisDirection))
                            || math.lengthsq(entrance.AxisDirection) <= 0f
                            || double.IsNaN(entrance.AxisLength)
                            || double.IsInfinity(entrance.AxisLength)
                            || entrance.AxisLength <= 0)))
                {
                    reason = ParkingLotTexte.T("baukontextLeser.invalidEntrance", ("i", i));
                    return false;
                }
                entrances[i] = new Entrance
                {
                    Edge = entrance.Edge,
                    Along = entrance.Along,
                    Corner = entrance.Corner == 1 ? "start" : entrance.Corner == 2 ? "end" : null,
                    Art = entrance.Art,
                    AxisDirection = entrance.HasAxis
                        ? entrance.AxisDirection : (float2?)null,
                    AxisLength = entrance.HasAxis
                        ? entrance.AxisLength : (double?)null,
                };
            }
            if (EntityManager.HasBuffer<ParkingLotBuildAlignment>(lot))
            {
                var alignmentBuffer = EntityManager
                    .GetBuffer<ParkingLotBuildAlignment>(lot, true);
                alignments = new Ausrichtzuweisung[alignmentBuffer.Length];
                for (var i = 0; i < alignmentBuffer.Length; i++)
                {
                    var alignment = alignmentBuffer[i];
                    if (alignment.Version != ParkingLotBuildAlignment.CurrentVersion
                        || !math.all(math.isfinite(alignment.Anchor))
                        || !math.all(math.isfinite(alignment.LineA))
                        || !math.all(math.isfinite(alignment.LineB))
                        || double.IsNaN(alignment.Angle)
                        || double.IsInfinity(alignment.Angle))
                    {
                        reason = ParkingLotTexte.T("baukontextLeser.invalidSubAreaAlignment", ("i", i));
                        return false;
                    }
                    alignments[i] = new Ausrichtzuweisung
                    {
                        Anker = alignment.Anchor,
                        LinieA = alignment.LineA,
                        LinieB = alignment.LineB,
                        Winkel = alignment.Angle,
                    };
                }
            }
            if (EntityManager.HasBuffer<ParkingLotBuildCut>(lot))
            {
                var cutBuffer = EntityManager
                    .GetBuffer<ParkingLotBuildCut>(lot, true);
                cuts = new Teilflaechenschnitt[cutBuffer.Length];
                for (var i = 0; i < cutBuffer.Length; i++)
                {
                    var cut = cutBuffer[i];
                    if (cut.Version != ParkingLotBuildCut.CurrentVersion
                        || !math.all(math.isfinite(cut.A))
                        || !math.all(math.isfinite(cut.B)))
                    {
                        reason = ParkingLotTexte.T("baukontextLeser.invalidCut", ("i", i));
                        return false;
                    }
                    cuts[i] = new Teilflaechenschnitt { A = cut.A, B = cut.B };
                }
            }
            if (EntityManager.HasBuffer<ParkingLotBuildZoning>(lot))
            {
                var zonePuffer = EntityManager
                    .GetBuffer<ParkingLotBuildZoning>(lot, true);
                zonen = new ParkingGeometry.Zoningflaeche[zonePuffer.Length];
                for (var i = 0; i < zonePuffer.Length; i++)
                {
                    var z = zonePuffer[i];
                    // Dieselbe Strenge wie bei den Schnitten: ein Bauzettel,
                    // dem man nicht trauen kann, wird abgelehnt statt halb
                    // benutzt. Die Grenzen kommen aus dem Spiel und koennen
                    // sich nicht geaendert haben - eine Zahl ausserhalb ist
                    // also ein kaputter Zettel, kein alter.
                    // Fassung 1 wird angenommen: ihr fehlt nur der Rand, und
                    // der war damals immer die Strassenbreite. Einen alten
                    // Zettel deshalb abzulehnen hiesse, dem Nutzer einen
                    // funktionierenden Parkplatz zu nehmen.
                    if (z.Version < 1
                        || z.Version > ParkingLotBuildZoning.CurrentVersion
                        || !math.all(math.isfinite(z.Ecke))
                        || !double.IsFinite(z.Winkel)
                        || z.Spalten < 1
                        || z.Spalten > ParkingGeometry.ZoningMaxBreite
                        || z.Reihen < 1
                        || z.Reihen > ParkingGeometry.ZoningMaxTiefe)
                    {
                        reason = ParkingLotTexte.T("baukontextLeser.invalidZoningPatch", ("i", i));
                        return false;
                    }
                    zonen[i] = new ParkingGeometry.Zoningflaeche
                    {
                        Rand = z.Rand,
                        Ecke = z.Ecke,
                        Spalten = z.Spalten,
                        Reihen = z.Reihen,
                        Winkel = z.Winkel,
                        // `Deserialize` hat bei alten Zetteln die frueher
                        // ringsum gleiche Tiefe schon auf die vier Seiten
                        // verteilt; hier steht sie nur noch durch.
                        Aussentiefen = new[]
                            { z.Aussen0, z.Aussen1, z.Aussen2, z.Aussen3 },
                    };
                }
                // Gegenstueck zur Zeile beim Schreiben. Stehen hier andere
                // Zahlen als dort, ist der Zettel schuld; stehen dieselben,
                // liegt es an dem, was danach mit ihnen passiert.
                var gelesen = new System.Text.StringBuilder();
                for (var i = 0; i < zonen.Length; i++)
                {
                    if (i > 0) gelesen.Append(" | ");
                    for (var s = 0; s < 4; s++)
                    {
                        if (s > 0) gelesen.Append('/');
                        gelesen.Append(ParkingGeometry
                            .ZoningAussentiefe(zonen[i], s)
                            .ToString("0.##",
                                System.Globalization.CultureInfo.InvariantCulture));
                    }
                }
                Mod.log.Info("PLT-Zoningzettel GELESEN: " + zonen.Length
                    + " Flaeche(n), Aussentiefen " + gelesen
                    + " (je Flaeche Seite 0/1/2/3).");
            }

            /*
             * Und die handgeschalteten Seiten dazu - sonst waeren sie beim
             * Bearbeiten weg, obwohl sie im Zettel stehen.
             *
             * Ein unbrauchbarer Eintrag wird UEBERGANGEN, nicht abgelehnt:
             * eine fehlende Umschaltung kostet den Nutzer einen Klick, ein
             * abgelehnter Zettel den ganzen Parkplatz.
             */
            if (EntityManager.HasBuffer<ParkingLotBuildZoningSeite>(lot))
            {
                var seitenPuffer = EntityManager
                    .GetBuffer<ParkingLotBuildZoningSeite>(lot, true);
                for (var i = 0; i < seitenPuffer.Length; i++)
                {
                    var s = seitenPuffer[i];
                    if (s.Version < 1
                        || s.Version > ParkingLotBuildZoningSeite.CurrentVersion
                        || !math.all(math.isfinite(s.A))
                        || !math.all(math.isfinite(s.B))) continue;
                    seitenplan.Add((s.A, s.B, s.Links, s.Aus));
                }
            }
            /*
             * NICHT HIER AUFTRAGEN, NUR MERKEN.
             *
             * Diese Methode laeuft VOR `ResetSelection`, und das raeumt den
             * Seitenplan mit den Zoningflaechen weg. Genau daran ist die
             * Umschaltung verlorengegangen: *"Nachdem ich den Standard
             * entfernt habe, gebaut habe und wieder editiert habe, war der
             * Standard wieder da."* Aufgetragen wird deshalb erst beim
             * Aufrufer, gleich hinter den Flaechen, zu denen der Plan gehoert.
             */
            var seitenGelesen = new System.Text.StringBuilder();
            for (var i = 0; i < seitenplan.Count; i++)
            {
                if (i > 0) seitenGelesen.Append(", ");
                seitenGelesen.Append(seitenplan[i].Links ? "links " : "rechts ")
                    .Append(seitenplan[i].Aus ? "AUS" : "an");
            }
            Mod.log.Info("PLT-Zoningseitenplan GELESEN: " + seitenplan.Count
                + " Handschaltung(en)"
                + (seitenplan.Count > 0 ? " - " + seitenGelesen : string.Empty)
                + ". Der Leser veraendert keine Panelwahl.");
            if (EntityManager.HasBuffer<ParkingLotBuildBusStop>(lot))
            {
                var busPuffer = EntityManager.GetBuffer<ParkingLotBuildBusStop>(lot,
                    true);
                for (var i = 0; i < busPuffer.Length; i++)
                {
                    var stop = busPuffer[i];
                    if (stop.Version != ParkingLotBuildBusStop.CurrentVersion
                        || !math.all(math.isfinite(stop.A))
                        || !math.all(math.isfinite(stop.B))
                        || !math.isfinite(stop.Along)
                        || stop.Along < 0f || stop.Along > 1f) continue;
                    busStops.Add(new BusStopPlacement
                    {
                        A = stop.A, B = stop.B, Along = stop.Along,
                        Left = stop.Left,
                    });
                }
            }
            Mod.log.Info("PLT-Bauzettel Bushalte GELESEN: "
                + busStops.Count + " von " + receipt.BusStopCount);

            // Das Randzoning aus demselben Zettel, mit derselben Nachsicht:
            // ein unbrauchbarer Eintrag wird uebergangen, nicht abgelehnt.
            if (EntityManager.HasBuffer<ParkingLotBuildRandzoning>(lot))
            {
                var randPuffer = EntityManager
                    .GetBuffer<ParkingLotBuildRandzoning>(lot, true);
                for (var i = 0; i < randPuffer.Length; i++)
                {
                    var r = randPuffer[i];
                    if (r.Version < 1
                        || r.Version > ParkingLotBuildRandzoning.CurrentVersion
                        || !math.all(math.isfinite(r.A))
                        || !math.all(math.isfinite(r.B))) continue;
                    randplan.Add(new ParkingGeometry.RandzoningLinie
                    {
                        A = r.A,
                        B = r.B,
                    });
                }
            }
            var textBuffer = EntityManager.GetBuffer<ParkingLotBuildText>(lot, true);
            // Eintrag 3 ist freiwillig: Bauzettel von vor dem 2026-09-02
            // haben ihn nicht, und leer heisst ohnehin "nimm die
            // Dekoflaeche". Ein fehlender Eintrag darf den Zettel also
            // nicht ungueltig machen.
            if (!TryReadBuildText(textBuffer, 3, out surfaceZoning))
                surfaceZoning = string.Empty;
            if (!TryReadBuildText(textBuffer, 1, out surfaceRoad)
                || !TryReadBuildText(textBuffer, 2, out surfaceDecoration)
                || string.IsNullOrEmpty(surfaceRoad)
                || string.IsNullOrEmpty(surfaceDecoration))
            {
                reason = ParkingLotTexte.T("baukontextLeser.invalidSurfaceNamesInTheBuild");
                return false;
            }
            return true;
        }

        private static bool ValidReceiptSettings(ParkingLotBuildReceipt r)
        {
            return Finite(r.Es) && Finite(r.Ai) && Finite(r.Cw)
                && Finite(r.Sl) && Finite(r.Sw) && Finite(r.Md)
                && Finite(r.Cr) && Finite(r.Angle) && Finite(r.KantenVersatz)
                && Finite(r.MedianWidth) && Finite(r.CrossBays)
                && Finite(r.Gassenbreite)
                && r.ZoningWinkelmodus >= 0
                && r.ZoningWinkelmodus <= Winkelmodus.GroessteZahl
                && Finite(r.ZoningReglerwinkel)
                && r.ZoningAussentiefeVorwahl >= 1
                && r.ZoningAussentiefeVorwahl <= 6
                && (double.IsNaN(r.ZoningAusrichtwinkel)
                    || Finite(r.ZoningAusrichtwinkel))
                && r.AngleMode >= 0
                && r.AngleMode <= Winkelmodus.GroessteZahl;
        }

        private static bool Finite(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value);

        internal static bool TryReadBuildText(
            DynamicBuffer<ParkingLotBuildText> buffer, int kind,
            out string value)
        {
            var count = 0;
            for (var i = 0; i < buffer.Length; i++)
            {
                var item = buffer[i];
                if (item.Version != ParkingLotBuildText.CurrentVersion)
                {
                    value = string.Empty;
                    return false;
                }
                if (item.Kind == kind) count++;
            }
            var bytes = new byte[count];
            var seen = new bool[count];
            for (var i = 0; i < buffer.Length; i++)
            {
                var item = buffer[i];
                if (item.Kind != kind) continue;
                if (item.Index < 0 || item.Index >= bytes.Length)
                {
                    value = string.Empty;
                    return false;
                }
                if (seen[item.Index])
                {
                    value = string.Empty;
                    return false;
                }
                seen[item.Index] = true;
                bytes[item.Index] = item.Value;
            }
            value = Encoding.UTF8.GetString(bytes);
            return true;
        }
    }
}
