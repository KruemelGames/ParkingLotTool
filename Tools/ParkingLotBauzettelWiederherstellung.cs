using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * Alles, was in einen Bauzettel geht - unabhaengig davon, woher es kommt.
     *
     * Nach einem Bau stammt es aus dem laufenden Werkzeug
     * (`WriteBuildReceipt`), bei einem verwaisten Parkplatz aus dem
     * Bauprotokoll. Geschrieben wird in beiden Faellen von
     * `SchreibeBauzettel` - so kann es kein zweites, abweichendes Format
     * geben.
     */
    internal sealed class Bauzettelquelle
    {
        internal LayoutSettings Settings;
        internal float3[] Punkte;
        internal double MedianWidth;
        internal bool GreenMedian;
        internal double CrossBays;
        internal bool SurfaceRoadOn;
        internal bool SurfaceDecorationOn;
        internal bool SurfaceApronOn;
        internal bool BayIcons;
        internal string ZoningWinkelmodus;
        internal double ZoningReglerwinkel;
        internal int ZoningAussentiefeVorwahl;
        internal double? ZoningAusrichtwinkel;
        internal double? Ausrichtwinkel;
        internal IReadOnlyList<ParkingLotToolSystem.Ausrichtzuweisung> Ausrichtungen;
        internal IReadOnlyList<Teilflaechenschnitt> Trennschnitte;
        internal IReadOnlyList<ParkingGeometry.Zoningflaeche> Zoningflaechen;
        internal IReadOnlyList<(float2 A, float2 B, bool Links, bool Aus)> Seitenplan;
        internal IReadOnlyList<ParkingGeometry.RandzoningLinie> Randzoning;
        internal string FlaecheStrasse;
        internal string FlaecheDekoration;
        internal string FlaecheZoning;

        /** Nur bei der Wiederherstellung: die Vegetationswahl als JSON. */
        internal string VegetationAusProtokoll;
    }

    public sealed partial class ParkingLotToolSystem
    {
        /** Liest `[x, y]` als float2 - so schreibt `SichererWert` sie. */
        private sealed class Float2Leser : JsonConverter
        {
            public override bool CanConvert(Type t)
                => t == typeof(float2) || t == typeof(float2?);

            public override object ReadJson(JsonReader reader, Type t,
                object existing, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null) return null;
                var feld = JArray.Load(reader);
                return new float2((float)feld[0], (float)feld[1]);
            }

            public override bool CanWrite => false;

            public override void WriteJson(JsonWriter writer, object value,
                JsonSerializer serializer) => throw new NotSupportedException();
        }

        private static readonly JsonSerializer ProtokollLeser = JsonSerializer.Create(
            new JsonSerializerSettings
            {
                Converters = { new Float2Leser() },
                MissingMemberHandling = MissingMemberHandling.Ignore,
            });

        /** Die ganze Protokollzeile zu einer Kennung - der juengste Eintrag. */
        internal static string ProtokollzeileZu(string kennung)
        {
            try
            {
                var pfad = JournalPath();
                if (!File.Exists(pfad)) return null;
                string treffer = null;
                var marke = "{\"kennung\":\"" + kennung + "\"";
                foreach (var zeile in File.ReadAllLines(pfad))
                    if (zeile.StartsWith(marke, StringComparison.Ordinal))
                        treffer = zeile;
                return treffer;
            }
            catch (Exception e)
            {
                Mod.log.Warn("PLT-Bauzettel aus Protokoll: Datei nicht lesbar: " + e.Message);
                return null;
            }
        }

        /**
         * Baut aus einer Protokollzeile die Bauzettelquelle.
         *
         * Die Punkte kommen NICHT aus dem Protokoll (dort stehen nur x/z),
         * sondern aus den Knoten der Lot-Flaeche - das ist dasselbe gezogene
         * Polygon, samt Hoehe, und seine Kennung ist genau die, unter der
         * der Eintrag gefunden wurde.
         *
         * WAS DAS PROTOKOLL NICHT HAT, und was deshalb anders ist als beim
         * Original: die beiden Enden der Bezugslinie je Teilflaeche (nur
         * Anker und Winkel sind protokolliert - der Winkel bleibt, die
         * Linie wird beim naechsten Bearbeiten nicht mehr nachgezogen), und
         * die Unterschrift der Vegetation (beim ersten Bearbeiten wird neu
         * gepflanzt statt uebernommen).
         */
        internal static bool TryBauzettelquelleAusProtokoll(string zeile,
            float3[] punkte, out Bauzettelquelle quelle, out string grund)
        {
            quelle = null;
            grund = null;
            try
            {
                var eintrag = JObject.Parse(zeile);
                var layout = eintrag["layoutwerte"] as JObject;
                var ui = eintrag["uiwerte"] as JObject;
                if (layout == null || ui == null)
                {
                    grund = "layoutwerte oder uiwerte fehlen";
                    return false;
                }
                var settings = layout.ToObject<LayoutSettings>(ProtokollLeser);
                // Die internen Werte liest der Deserialisierer nicht; sie
                // entscheiden aber mit, welches Layout herauskommt.
                settings.KantenVersatz = (double?)layout["KantenVersatz"] ?? 0;
                settings.Zellen = (bool?)layout["Zellen"] ?? true;
                settings.EineFlaeche = (bool?)layout["EineFlaeche"] ?? false;
                settings.NoNotch = (bool?)layout["NoNotch"] ?? false;
                settings.Single = (bool?)layout["Single"] ?? false;
                settings.NoHalf = (bool?)layout["NoHalf"] ?? false;

                // Gegenprobe: dieselbe Erfassung wie beim Schreiben muss
                // wieder dasselbe ergeben. Sonst ging beim Lesen etwas
                // verloren, und ein Zettel daraus waere eine stille Luege.
                var zurueck = JToken.FromObject(ParkingSettingsInventory.Erfasse(
                    settings, ParkingSettingsInventory.Layout));
                // Nur die Felder, die der Eintrag HAT: aeltere Zeilen kennen
                // spaetere Werte (etwa BusStops) noch nicht - die bleiben auf
                // ihrem Vorgabewert, und das ist dann auch der alte Stand.
                var abweichend = Unterschied(layout, zurueck);
                if (abweichend != null)
                {
                    grund = "Rueckleseprobe der Layoutwerte weicht ab: " + abweichend;
                    return false;
                }

                var seiten = new List<(float2 A, float2 B, bool Links, bool Aus)>();
                if (eintrag["zoningseiten"] is JArray seitenfeld)
                    foreach (var s in seitenfeld)
                        seiten.Add((new float2((float)s["A"][0], (float)s["A"][1]),
                            new float2((float)s["B"][0], (float)s["B"][1]),
                            (bool)s["Links"], (bool)s["Aus"]));

                var ausrichtungen = (settings.TeilflaechenAusrichtungen
                        ?? Array.Empty<TeilflaechenAusrichtung>())
                    .Select(a => new Ausrichtzuweisung
                    {
                        Anker = a.Anker,
                        // Die Linienenden sind nicht protokolliert. Gleiche
                        // Enden heissen "keine Linie": AktualisiereAusrichtungen
                        // steigt dann aus, und der Winkel bleibt.
                        LinieA = a.Anker,
                        LinieB = a.Anker,
                        Winkel = a.Winkel,
                    }).ToList();

                var vegetation = eintrag["vegetation"];
                quelle = new Bauzettelquelle
                {
                    Settings = settings,
                    Punkte = punkte,
                    MedianWidth = (double?)ui["MedianWidth"] ?? settings.Md,
                    GreenMedian = (bool?)ui["GreenMedian"] ?? settings.Md > 0,
                    CrossBays = (double?)ui["CrossBays"]
                        ?? (settings.Cr / settings.Sw - (settings.Qk ? 2 : 0)),
                    SurfaceRoadOn = (bool?)ui["SurfaceRoadOn"] ?? true,
                    SurfaceDecorationOn = (bool?)ui["SurfaceDecorationOn"] ?? true,
                    SurfaceApronOn = (bool?)ui["SurfaceApronOn"] ?? true,
                    BayIcons = (bool?)ui["BayIcons"] ?? true,
                    ZoningWinkelmodus = (string)ui["ZoningWinkelmodus"] ?? "edge",
                    ZoningReglerwinkel = (double?)ui["ZoningWinkel"] ?? 0,
                    ZoningAussentiefeVorwahl = (int?)ui["ZoningAussentiefe"] ?? 2,
                    ZoningAusrichtwinkel = (double?)ui["ZoningAusrichtwinkel"],
                    Ausrichtwinkel = settings.Ausrichtwinkel,
                    Ausrichtungen = ausrichtungen,
                    Trennschnitte = settings.Teilflaechenschnitte
                        ?? Array.Empty<Teilflaechenschnitt>(),
                    Zoningflaechen = settings.Zoningflaechen
                        ?? Array.Empty<ParkingGeometry.Zoningflaeche>(),
                    Seitenplan = seiten,
                    Randzoning = settings.Randzoning
                        ?? Array.Empty<ParkingGeometry.RandzoningLinie>(),
                    FlaecheStrasse = (string)ui["SurfaceRoad"] ?? string.Empty,
                    FlaecheDekoration = (string)ui["SurfaceDecoration"] ?? string.Empty,
                    FlaecheZoning = (string)ui["SurfaceZoning"] ?? string.Empty,
                    VegetationAusProtokoll = vegetation == null
                        || vegetation.Type == JTokenType.Null
                        ? JsonConvert.SerializeObject(new VegetationOptions())
                        : vegetation.ToString(Formatting.None),
                };
                return true;
            }
            catch (Exception e)
            {
                grund = "Protokollzeile nicht lesbar: " + e.Message;
                return false;
            }
        }

        /**
         * Zahlen auf FLOAT-Genauigkeit vergleichen: 5 und 5.0 sind derselbe
         * Wert, und ein float, der als double zurueckkommt, darf sich in den
         * Stellen jenseits von float unterscheiden. Mehr Spiel gibt es nicht
         * (vorher: 5 Nachkommastellen - zu grob, Codex 2026-09-25).
         */
        private static JToken Normalisiere(JToken t)
        {
            switch (t.Type)
            {
                case JTokenType.Integer:
                case JTokenType.Float:
                    return new JValue((double)(float)(double)t);
                case JTokenType.Array:
                    return new JArray(t.Select(Normalisiere));
                case JTokenType.Object:
                    var o = new JObject();
                    foreach (var p in ((JObject)t).Properties().OrderBy(p => p.Name))
                        o[p.Name] = Normalisiere(p.Value);
                    return o;
                default:
                    return t.DeepClone();
            }
        }

        private static string Unterschied(JObject soll, JToken ist)
        {
            var namen = new List<string>();
            foreach (var p in soll.Properties())
                if (!JToken.DeepEquals(Normalisiere(p.Value), Normalisiere(ist[p.Name])))
                    namen.Add(p.Name);
            return namen.Count == 0 ? null : string.Join(", ", namen);
        }

        /**
         * Vegetationszettel ohne Unterschrift: die Wahl ist bekannt, die
         * Gruenflaechen, aus denen die Unterschrift faellt, nicht.
         */
        private void SchreibeVegetationAusProtokoll(Entity lot, string optionen)
        {
            var zettel = new VegetationReceipt { Options = optionen, Signature = string.Empty };
            var text = JsonConvert.SerializeObject(zettel);
            AddBuildText(EntityManager.GetBuffer<ParkingLotBuildText>(lot), 4, text);
            var puffer = EntityManager.HasBuffer<ParkingLotVegetationReceipt>(lot)
                ? EntityManager.GetBuffer<ParkingLotVegetationReceipt>(lot)
                : EntityManager.AddBuffer<ParkingLotVegetationReceipt>(lot);
            puffer.Clear();
            foreach (var b in Encoding.UTF8.GetBytes(text))
                puffer.Add(new ParkingLotVegetationReceipt { Value = b });
        }

        /**
         * Stellt den Bauzettel eines reparierten Parkplatzes aus dem
         * Bauprotokoll wieder her. Danach ist er wieder bearbeitbar.
         *
         * Nur mit exakter Kennung (aus den Knoten der Lot-Flaeche) und nur,
         * wenn der geschriebene Zettel sich danach auch wieder LESEN laesst
         * - sonst wird er wieder entfernt, damit kein halber Zettel stehen
         * bleibt, der Bearbeiten in die Irre fuehrt.
         */
        internal bool StelleBauzettelWiederHer(Entity lot, out string grund)
        {
            grund = null;
            if (lot == Entity.Null || !EntityManager.Exists(lot)
                || !EntityManager.HasBuffer<Game.Areas.Node>(lot))
            {
                grund = "Flaeche fehlt";
                return false;
            }
            if (EntityManager.HasComponent<ParkingLotBuildReceipt>(lot))
            {
                grund = "hat schon einen Bauzettel";
                return false;
            }
            var knoten = EntityManager.GetBuffer<Game.Areas.Node>(lot, true);
            var punkte = new float3[knoten.Length];
            var xz = new float2[knoten.Length];
            for (var i = 0; i < knoten.Length; i++)
            {
                punkte[i] = knoten[i].m_Position;
                xz[i] = knoten[i].m_Position.xz;
            }
            var kennung = LotId(xz);
            var zeile = ProtokollzeileZu(kennung);
            if (zeile == null)
            {
                grund = "kein Protokolleintrag " + kennung;
                return false;
            }
            if (!TryBauzettelquelleAusProtokoll(zeile, punkte, out var quelle, out grund))
                return false;
            if (!SchreibeBauzettel(lot, quelle))
            {
                grund = "Schreiben fehlgeschlagen";
                EntferneBauzettel(lot);
                return false;
            }
            if (!TryReadBuildReceiptFuerProbe(lot, out var lesegrund))
            {
                grund = "geschriebener Zettel nicht lesbar: " + lesegrund;
                EntferneBauzettel(lot);
                return false;
            }
            Mod.log.Info("PLT-Bauzettel aus Protokoll wiederhergestellt: Lot "
                + lot.Index + " " + kennung + ", " + punkte.Length + " Ecken, "
                + (quelle.Settings.Entrances?.Length ?? 0) + " Zufahrt(en), "
                + quelle.Zoningflaechen.Count + " Zoningflaeche(n), "
                + (quelle.Settings.BusStops?.Length ?? 0) + " Bushalt(e).");
            return true;
        }

        /** Laesst sich der Zettel so lesen, wie Bearbeiten ihn liest? */
        private bool TryReadBuildReceiptFuerProbe(Entity lot, out string grund)
            => TryReadBuildReceipt(lot, out _, out _, out _, out _, out _,
                out _, out _, out _, out _, out grund);

        private void EntferneBauzettel(Entity lot)
        {
            if (EntityManager.HasComponent<ParkingLotBuildReceipt>(lot))
                EntityManager.RemoveComponent<ParkingLotBuildReceipt>(lot);
        }
    }
}
