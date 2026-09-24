using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Newtonsoft.Json;

namespace ParkingLotTool.Tools
{
    /**
     * Das BAUPROTOKOLL: welcher Parkplatz wurde wann woraus gerechnet.
     *
     * WOZU: Eine Markierung sagt "hier stimmt etwas nicht". Damit ich das
     * nachvollziehen kann, brauche ich das REZEPT - das gezogene Polygon und
     * die Einstellungen, aus denen dieser Parkplatz entstanden ist. Am
     * gebauten Parkplatz selbst steht davon nichts: nach dem Bauen ist das
     * Layout aus dem Speicher, und Flaechen in der Welt tragen nur ihre
     * Eckpunkte.
     *
     * WARUM EINE DATEI UND KEINE KOMPONENTE AM BESITZER: eine eigene
     * serialisierte Komponente wandert in den Spielstand. Deinstalliert
     * jemand den Mod, hat er sie im Save stehen - ein Risiko, das ein
     * Meldewerkzeug niemandem aufzwingen darf. Eine Datei daneben kostet
     * nichts.
     *
     * WORAN EIN PARKPLATZ ERKANNT WIRD - drei Kandidaten, zwei davon
     * untauglich:
     *
     *   Entity-Nummer  aendert sich beim Speichern und Laden. Untauglich.
     *   Name           kann der Nutzer aendern, und zwei Parkplaetze duerfen
     *                  gleich heissen. Untauglich.
     *   Das gezogene POLYGON  liegt fest, sobald gebaut wurde. Es kann nicht
     *                  umbenannt werden, und zwei Parkplaetze koennen nicht
     *                  denselben Boden belegen.
     *
     * Also das Polygon. Daraus entsteht eine kurze KENNUNG (Hash der auf den
     * Zentimeter gerundeten, sortierten Ecken) - stabil, ohne dass etwas im
     * Spielstand abgelegt werden muss. Zugeordnet wird eine Markierung ueber
     * die Frage "in welchem gezogenen Polygon liegt sie?", nicht ueber
     * Entfernung: bei zwei angrenzenden Parkplaetzen kann ein Abstandsmass
     * daneben greifen, eine Enthaltenseinspruefung nicht.
     *
     * Der Name wird nur noch ANGEZEIGT, nicht mehr zum Zuordnen benutzt.
     */
    public sealed partial class ParkingLotToolSystem
    {
        private const string JournalFileName = "ParkingLotTool-builds.jsonl";

        /** Wie nah ein Protokolleintrag am Parkplatz liegen muss. */
        private const float JournalMatchRadius = 60f;

        internal sealed class BuildRecord
        {
            internal string Id;
            internal string When;
            internal string OwnerName;
            internal float2 Center;
            internal float2[] Site;
            internal string Settings;
            internal string Result;

            /**
             * Stehen die vollstaendigen Bauwerte drin? Aeltere Zeilen (vor
             * `layoutwerte`/`uiwerte`) haben nur eine Textbeschreibung und
             * die ANZAHL der Zufahrten, nicht ihre Lage - daraus laesst sich
             * kein Bauzettel wiederherstellen.
             */
            internal bool VollerBauplan;
        }

        /**
         * Kennung aus dem gezogenen Polygon.
         *
         * Sortiert und auf den Zentimeter gerundet, damit dieselbe Flaeche
         * immer dieselbe Kennung ergibt - unabhaengig davon, bei welcher Ecke
         * der Nutzer angefangen hat und in welcher Richtung er gezogen hat.
         */
        internal static string LotId(IReadOnlyList<float2> site)
        {
            if (site == null || site.Count == 0) return "PLT-000000";
            var keys = new List<long>(site.Count);
            foreach (var point in site)
                keys.Add((long)math.round(point.x * 100)
                    * 4294967296L + (long)math.round(point.y * 100));
            keys.Sort();
            unchecked
            {
                var hash = 2166136261u;
                foreach (var key in keys)
                    for (var shift = 0; shift < 64; shift += 8)
                        hash = (hash ^ (byte)(key >> shift)) * 16777619u;
                return "PLT-" + hash.ToString("X8");
            }
        }

        private static string JournalPath()
        {
            var folder = Path.Combine(Application.persistentDataPath, "Logs");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, JournalFileName);
        }

        private static string Num(double value)
            => value.ToString("F2", CultureInfo.InvariantCulture);

        /**
         * Koordinaten mit voller Genauigkeit. Auf zwei Stellen gerundet ist
         * ein Polygon nicht mehr dasselbe - beim Bericht vom 2026-08-12
         * wichen dadurch die Buchtenzahlen ab.
         */
        private static string Exact(double value)
            => value.ToString("R", CultureInfo.InvariantCulture);

        /**
         * Haengt einen Eintrag an. Wird beim erfolgreichen Bauen gerufen.
         *
         * Eine Zeile je Bau, damit ein Absturz mittendrin hoechstens die
         * letzte Zeile beschaedigt und nicht die ganze Datei.
         */
        /**
         * `grassPlaced` und `asphaltPlaced` sind die TATSAECHLICH gesetzten
         * Flaechen, nicht die geplanten aus dem Layout. Seit den
         * Flaechenschaltern faellt beides auseinander, und das Protokoll ist
         * genau die Datei, mit der sich ein Bau spaeter nachstellen laesst -
         * eine geschoente Zahl darin macht es wertlos.
         */
        private void AppendBuildJournal(ParkingLayout layout,
            LayoutSettings settings, float3[] worldSite, string ownerName,
            int nets, int objects, int grassPlaced, int asphaltPlaced)
        {
            try
            {
                if (layout == null || settings == null) return;
                Mod.log.Info("PLT-Bauzettel: Randstrassen " + (settings.Randstrassen ? "an" : "aus")
                    + "; " + layout.Stalls + " Buchten.");
                Mod.log.Info("PLT-Bauzettel Vegetation: " + (_vegetationReceipt?.Options ?? "aus") + "; neu=" + _vegetationCount + "; uebernehmen=" + _vegetationPreserve);
                foreach (var warnung in layout.Warnings) Mod.log.Warn("PLT-Bauzettel: " + warnung);
                var site = worldSite ?? Array.Empty<float3>();
                var center = float2.zero;
                for (var i = 0; i < site.Length; i++) center += site[i].xz;
                if (site.Length > 0) center /= site.Length;

                var points = new StringBuilder("[");
                for (var i = 0; i < site.Length; i++)
                {
                    if (i > 0) points.Append(',');
                    points.Append('[').Append(Exact(site[i].x)).Append(',')
                        .Append(Exact(site[i].z)).Append(']');
                }
                points.Append(']');

                var siteXZ = new float2[site.Length];
                for (var i = 0; i < site.Length; i++) siteXZ[i] = site[i].xz;

                var line = new StringBuilder();
                line.Append("{\"kennung\":\"").Append(LotId(siteXZ))
                    .Append("\",\"zeit\":\"")
                    .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                    .Append("\",\"besitzer\":\"").Append(Escape(ownerName))
                    .Append("\",\"mitte\":[").Append(Num(center.x)).Append(',')
                    .Append(Num(center.y)).Append("],\"polygon\":").Append(points)
                    .Append(",\"vegetation\":").Append(_vegetationReceipt?.Options ?? "null")
                    .Append(",\"vegetationswerte\":")
                    .Append(JsonConvert.SerializeObject(ParkingSettingsInventory
                        .Erfasse(JsonConvert.DeserializeObject<VegetationOptions>(
                            _vegetationReceipt?.Options ?? "{}"),
                            ParkingSettingsInventory.Vegetation)))
                    .Append(",\"layoutwerte\":")
                    .Append(JsonConvert.SerializeObject(ParkingSettingsInventory
                        .Erfasse(settings, ParkingSettingsInventory.Layout)))
                    .Append(",\"uiwerte\":")
                    .Append(JsonConvert.SerializeObject(ZettelUIWerte()))
                    .Append(",\"zoningseiten\":")
                    .Append(JsonConvert.SerializeObject(_zoningSeitenPlan.Select(seite => new
                    {
                        A = new[] { seite.A.x, seite.A.y },
                        B = new[] { seite.B.x, seite.B.y },
                        seite.Links, seite.Aus,
                    }).ToArray()))
                    .Append(",\"bushaltestellen\":")
                    .Append(JsonConvert.SerializeObject((settings.BusStops
                        ?? Array.Empty<BusStopPlacement>()).Select(stop => new
                    {
                        A = new[] { stop.A.x, stop.A.y },
                        B = new[] { stop.B.x, stop.B.y },
                        stop.Along, stop.Left,
                    }).ToArray()))
                    .Append(",\"zoning\":").Append(Zoningfelder(settings))
                    .Append(",\"randzoning\":").Append(Randzoningfelder(settings))
                    .Append(",\"einstellungen\":\"").Append(Escape(Describe(settings)))
                    .Append("\",\"ergebnis\":\"")
                    .Append(Escape($"{layout.Stalls} stalls, "
                        + $"{layout.Aisles} aisles, angle {Num(layout.Angle)} deg, "
                        + $"{grassPlaced} grass and "
                        + $"{asphaltPlaced} pavement surfaces, "
                        + $"{nets} net pieces, {objects} objects"))
                    .Append("\"}");
                File.AppendAllText(JournalPath(), line + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                // Ein fehlgeschlagenes Protokoll darf den Bau nicht kippen.
                Mod.log.Warn("PLT-Bauprotokoll konnte nicht geschrieben werden: "
                    + exception.Message);
            }
        }

        private Dictionary<string, object> ZettelUIWerte()
        {
            // Vierzehn Bau- und Vorwahlwerte ausserhalb von LayoutSettings.
            return new Dictionary<string, object>
            {
                ["MedianWidth"] = _uiSystem?.AktuelleMedianbreite,
                ["GreenMedian"] = _uiSystem?.MittelgruenAn,
                ["CrossBays"] = _uiSystem?.AktuelleQuerbuchten,
                ["SurfaceRoad"] = _uiSystem?.FlaecheStrasse,
                ["SurfaceDecoration"] = _uiSystem?.FlaecheDekoration,
                ["SurfaceZoning"] = _uiSystem?.FlaecheZoning,
                ["SurfaceRoadOn"] = _uiSystem?.FlaecheStrasseAn,
                ["SurfaceDecorationOn"] = _uiSystem?.FlaecheDekoAn,
                ["SurfaceApronOn"] = _uiSystem?.VorflaecheAn,
                ["BayIcons"] = _uiSystem?.Buchtsymbole,
                ["ZoningWinkelmodus"] = ZoningWinkelmodus,
                ["ZoningWinkel"] = ZoningReglerwinkel,
                ["ZoningAussentiefe"] = ZoningTiefeVorwahl,
                ["ZoningAusrichtwinkel"] = ZoningAusrichtwinkel,
            };
        }

        private static Dictionary<string, object> ZettelUIWerte(
            ParkingLotBuildReceipt receipt, string road, string decoration,
            string zoning)
        {
            return new Dictionary<string, object>
            {
                ["MedianWidth"] = receipt.MedianWidth,
                ["GreenMedian"] = receipt.GreenMedian,
                ["CrossBays"] = receipt.CrossBays,
                ["SurfaceRoad"] = road,
                ["SurfaceDecoration"] = decoration,
                ["SurfaceZoning"] = zoning,
                ["SurfaceRoadOn"] = receipt.SurfaceRoadOn,
                ["SurfaceDecorationOn"] = receipt.SurfaceDecorationOn,
                ["SurfaceApronOn"] = receipt.SurfaceApronOn,
                ["BayIcons"] = receipt.BayIcons,
                ["ZoningWinkelmodus"] = Winkelmodus.Dekodiere(
                    receipt.ZoningWinkelmodus),
                ["ZoningWinkel"] = receipt.ZoningReglerwinkel,
                ["ZoningAussentiefe"] = receipt.ZoningAussentiefeVorwahl,
                ["ZoningAusrichtwinkel"] = double.IsNaN(
                    receipt.ZoningAusrichtwinkel) ? null : receipt.ZoningAusrichtwinkel,
            };
        }

        /**
         * DIE ZONING-FLAECHEN GEHOEREN INS PROTOKOLL.
         *
         * Am 2026-09-03 meldete der Nutzer eine Luecke im Strassennetz
         * zwischen zwei Zoningflaechen. Das Protokoll hatte Umriss und
         * Einstellungen - aber nicht die Flaechen, und ohne die liess sich
         * sein Fall nicht nachstellen. Ich habe daraufhin sechs Anordnungen
         * geraten; alle sechs hingen zusammen, seine nicht.
         *
         * Ein Rezept, das den Fall nicht reproduziert, ist schlimmer als
         * keins - dieselbe Lehre wie damals bei `AngleMode`, nur eine Ebene
         * weiter.
         */
        private static string Zoningfelder(LayoutSettings s)
        {
            var flaechen = s.Zoningflaechen;
            if (flaechen == null || flaechen.Length == 0) return "[]";
            var text = new StringBuilder("[");
            for (var i = 0; i < flaechen.Length; i++)
            {
                var f = flaechen[i];
                if (f == null) continue;
                if (i > 0) text.Append(',');
                text.Append("{\"ecke\":[").Append(Exact(f.Ecke.x)).Append(',')
                    .Append(Exact(f.Ecke.y))
                    .Append("],\"spalten\":").Append(f.Spalten)
                    .Append(",\"reihen\":").Append(f.Reihen)
                    .Append(",\"winkel\":").Append(Num(f.Winkel))
                    .Append(",\"rand\":").Append(Num(f.Rand))
                    // Die vier Aussentiefen gehoeren ins Protokoll: sie
                    // erklaeren, warum an einer Seite Platz frei blieb.
                    .Append(",\"aussen\":[")
                    .Append(Num(ParkingGeometry.ZoningAussentiefe(f, 0)))
                    .Append(',')
                    .Append(Num(ParkingGeometry.ZoningAussentiefe(f, 1)))
                    .Append(',')
                    .Append(Num(ParkingGeometry.ZoningAussentiefe(f, 2)))
                    .Append(',')
                    .Append(Num(ParkingGeometry.ZoningAussentiefe(f, 3)))
                    .Append(']')
                    .Append('}');
            }
            return text.Append(']').ToString();
        }

        /**
         * DAS RANDZONING GEHOERT AUCH INS PROTOKOLL.
         *
         * Es stand im Bauzettel der Flaeche - der ueberlebt das Laden -, aber
         * nicht in dieser Datei. Und die ist es, aus der ich einen Fall
         * nachstelle. Am 2026-09-04 meldete der Nutzer einen Fehler an einem
         * Parkplatz MIT Randzoning und schickte den Zettel; darin stand
         * davon nichts, und der Fall liess sich wieder nicht abschreiben.
         * Dieselbe Luecke wie bei den Zoningflaechen einen Tag zuvor.
         */
        private static string Randzoningfelder(LayoutSettings s)
        {
            var linien = s.Randzoning;
            if (linien == null || linien.Length == 0) return "[]";
            var text = new StringBuilder("[");
            for (var i = 0; i < linien.Length; i++)
            {
                var l = linien[i];
                if (l == null) continue;
                if (i > 0) text.Append(',');
                text.Append("{\"a\":[").Append(Exact(l.A.x)).Append(',')
                    .Append(Exact(l.A.y)).Append("],\"b\":[")
                    .Append(Exact(l.B.x)).Append(',')
                    .Append(Exact(l.B.y)).Append("]}");
            }
            return text.Append(']').ToString();
        }

        /**
         * ALLE Einstellungen, nicht die halben.
         *
         * Hier fehlten `AngleMode` und `Auto` - und damit war das Rezept
         * WERTLOS. Nachgestellt mit dem Bericht vom 2026-08-12: ohne Modus
         * ergab dasselbe Polygon 291 Buchten und 16 Grasringe, mit
         * `AngleMode = "edge"` dagegen 313 Buchten, Winkel 93 und 86
         * Grasringe. Ich haette daraus fast geschlossen, die Geometrie sei
         * schon sauber. Ein Rezept, das den Fall nicht reproduziert, ist
         * schlimmer als keins.
         */
        private static string Describe(LayoutSettings s)
            => $"edge setback {Num(s.Es)} m, aisle {Num(s.Ai)} m, "
                + $"cross road {Num(s.Cw)} m, cross spacing {Num(s.Cr)} m, "
                + $"median {Num(s.Md)} m, bay {Num(s.Sw)} x {Num(s.Sl)} m, "
                + $"angle mode {s.AngleMode ?? "(none)"}, angle {Num(s.Angle)} deg, "
                + $"Randstrassen {(s.Randstrassen ? "an" : "aus")}, "
                + $"caps at cross roads {(s.Qk ? "on" : "off")}, "
                + $"auto {(s.Auto ? "on" : "off")}, "
                + $"{s.TeilflaechenAusrichtungen?.Length ?? 0} sub-area alignment(s), "
                // OHNE DIESE ZEILE IST EIN BEFUND NICHT NACHRECHENBAR: alter
                // und Zellenweg bauen verschiedene Layouts, und aus den
                // uebrigen Werten laesst sich nicht ablesen, welcher lief.
                + $"engine {(s.Zellen ? "new" : "old")}, "
                + $"{s.Entrances?.Length ?? 0} entrance(s)";

        private static string Escape(string value)
            => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");

        /**
         * Sucht den Protokolleintrag, der zu dieser Stelle gehoert.
         *
         * ENTHALTENSEIN SCHLAEGT ENTFERNUNG. Liegt die Markierung im
         * gezogenen Polygon eines Eintrags, ist die Sache entschieden - zwei
         * Parkplaetze koennen nicht denselben Boden belegen. Ein Abstandsmass
         * koennte bei zwei angrenzenden Parkplaetzen daneben greifen.
         *
         * Liegen mehrere Polygone um den Punkt (derselbe Boden wurde neu
         * bebaut), gewinnt der JUENGSTE Eintrag - das ist der Parkplatz, der
         * jetzt dort steht.
         *
         * Nur wenn gar kein Polygon den Punkt enthaelt, faellt es auf die
         * naechste Mitte zurueck: die Markierung sitzt dann etwas neben dem
         * Grundstueck, etwa auf einem ueberstehenden Aufkleber.
         */
        /**
         * Alle Eintraege, juengster je Kennung gewinnt.
         *
         * Fuer die Waisensuche: dort ist die Lot-Flaeche noch da, der Bauzettel
         * aber weg. Die Kennung stammt aus dem gezogenen Polygon, und genau das
         * sind die Knoten der Lot-Flaeche - also dieselbe Kennung.
         */
        internal static Dictionary<string, BuildRecord> LeseBauprotokoll()
        {
            var ergebnis = new Dictionary<string, BuildRecord>();
            try
            {
                var path = JournalPath();
                if (!File.Exists(path)) return ergebnis;
                foreach (var line in File.ReadAllLines(path))
                {
                    var record = ParseRecord(line);
                    if (record?.Id != null) ergebnis[record.Id] = record;
                }
            }
            catch (Exception exception)
            {
                Mod.log.Warn("PLT-Bauprotokoll konnte nicht gelesen werden: "
                    + exception.Message);
            }
            return ergebnis;
        }

        private BuildRecord FindBuildRecord(float2 position, string ownerName)
        {
            try
            {
                var path = JournalPath();
                if (!File.Exists(path)) return null;
                BuildRecord contained = null;
                BuildRecord nearest = null;
                var bestDistance = JournalMatchRadius;
                foreach (var line in File.ReadAllLines(path))
                {
                    var record = ParseRecord(line);
                    if (record == null) continue;
                    // Spaetere Zeilen ueberschreiben fruehere - die Datei ist
                    // chronologisch, also gewinnt der juengste Bau.
                    if (record.Site != null && record.Site.Length >= 3
                        && PointInPolygon(position, record.Site))
                    {
                        contained = record;
                        continue;
                    }
                    var distance = math.distance(record.Center, position);
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    nearest = record;
                }
                return contained ?? nearest;
            }
            catch (Exception exception)
            {
                Mod.log.Warn("PLT-Bauprotokoll konnte nicht gelesen werden: "
                    + exception.Message);
                return null;
            }
        }

        /** Winziger Leser fuer genau unser Format - keine JSON-Bibliothek. */
        private static BuildRecord ParseRecord(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("{")) return null;
            string Field(string key)
            {
                var marker = "\"" + key + "\":\"";
                var start = line.IndexOf(marker, StringComparison.Ordinal);
                if (start < 0) return null;
                start += marker.Length;
                var end = start;
                while (end < line.Length && (line[end] != '"' || line[end - 1] == '\\'))
                    end++;
                return line.Substring(start, end - start).Replace("\\\"", "\"");
            }
            float2 Pair(string key)
            {
                var marker = "\"" + key + "\":[";
                var start = line.IndexOf(marker, StringComparison.Ordinal);
                if (start < 0) return float2.zero;
                start += marker.Length;
                var end = line.IndexOf(']', start);
                var parts = line.Substring(start, end - start).Split(',');
                return parts.Length < 2 ? float2.zero : new float2(
                    float.Parse(parts[0], CultureInfo.InvariantCulture),
                    float.Parse(parts[1], CultureInfo.InvariantCulture));
            }

            var when = Field("zeit");
            if (when == null) return null;
            return new BuildRecord
            {
                Id = Field("kennung") ?? "unbekannt",
                When = when,
                OwnerName = Field("besitzer"),
                Center = Pair("mitte"),
                Site = ParseSite(line),
                Settings = Field("einstellungen"),
                Result = Field("ergebnis"),
                VollerBauplan = line.Contains("\"layoutwerte\":")
                    && line.Contains("\"uiwerte\":"),
            };
        }

        private static float2[] ParseSite(string line)
        {
            var marker = "\"polygon\":[";
            var start = line.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return Array.Empty<float2>();
            start += marker.Length;
            var end = line.IndexOf("]]", start, StringComparison.Ordinal);
            if (end < 0) return Array.Empty<float2>();
            var body = line.Substring(start, end - start + 1);
            var points = new List<float2>();
            foreach (var chunk in body.Split(new[] { "],[" }, StringSplitOptions.None))
            {
                var parts = chunk.Trim('[', ']').Split(',');
                if (parts.Length < 2) continue;
                if (float.TryParse(parts[0], NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var x)
                    && float.TryParse(parts[1], NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var z))
                    points.Add(new float2(x, z));
            }
            return points.ToArray();
        }

        /** Der Name, unter dem ein Parkplatz im Spiel steht. */
        private string OwnerDisplayName(Entity owner)
        {
            if (owner == Entity.Null || !EntityManager.Exists(owner)) return null;
            try
            {
                var nameSystem = World.GetOrCreateSystemManaged<Game.UI.NameSystem>();
                if (nameSystem != null
                    && nameSystem.TryGetCustomName(owner, out var custom)
                    && !string.IsNullOrEmpty(custom)) return custom;
            }
            catch (Exception)
            {
                // Ein fehlender Name ist kein Grund, den Bericht abzubrechen.
            }
            return PrefabNameOf(owner);
        }

        /** Laeuft die Besitzerkette bis zur Wurzel hoch. */
        private Entity RootOwnerOf(Entity entity)
        {
            var current = entity;
            for (var guard = 0; guard < 8; guard++)
            {
                if (!EntityManager.HasComponent<Game.Common.Owner>(current)) break;
                var next = EntityManager
                    .GetComponentData<Game.Common.Owner>(current).m_Owner;
                if (next == Entity.Null || next == current) break;
                current = next;
            }
            return current;
        }
    }
}
