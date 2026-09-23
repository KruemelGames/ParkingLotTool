using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private string SerializeDebugDocument(DebugDumpDocument document)
        {
            ErgaenzeZettelwerte(document);
            var serializer = new JsonSerializer
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Include,
            };
            serializer.Converters.Add(new FullPrecisionNumberConverter());
            serializer.Converters.Add(new StringEnumConverter());

            document.Vegetationswerte = ParkingSettingsInventory.Erfasse(
                document.Vegetation ?? new VegetationOptions(),
                ParkingSettingsInventory.Vegetation);
            using var text = new StringWriter(CultureInfo.InvariantCulture);
            using (var writer = new JsonTextWriter(text)
            {
                Formatting = Formatting.Indented,
                Indentation = 2,
                IndentChar = ' ',
                Culture = CultureInfo.InvariantCulture,
            })
            {
                serializer.Serialize(writer, document);
            }
            return text.ToString();
        }

        private void ErgaenzeZettelwerte(DebugDumpDocument document)
        {
            // Zwei Abzuege benutzen denselben Serializer; 27 Layoutwerte und
            // 14 UI-Werte werden hier bei beiden Wegen nachgezaehlt.
            document.ZettelUIWerte = ZettelUIWerte();
            document.Zoningseiten = _zoningSeitenPlan.Select(seite => (object)new
                {
                    A = new[] { seite.A.x, seite.A.y },
                    B = new[] { seite.B.x, seite.B.y },
                    seite.Links, seite.Aus,
                }).ToArray();
            var quelle = document.Input?.PolygonSource;
            const string prefix = "gebauter Parkplatz ";
            if (quelle == null || !quelle.StartsWith(prefix,
                StringComparison.Ordinal)) return;
            var ende = quelle.IndexOf(' ', prefix.Length);
            if (ende < 0 || !int.TryParse(quelle.Substring(prefix.Length,
                ende - prefix.Length), out var index)) return;
            using var query = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ParkingLotBuildReceipt>());
            using var lots = query.ToEntityArray(Allocator.Temp);
            foreach (var lot in lots)
            {
                if (lot.Index != index || !TryReadBuildReceipt(lot,
                    out var receipt, out _, out _, out var alignments,
                    out var cuts, out var zones, out var road,
                    out var decoration, out var zoning, out _)) continue;
                document.ZettelUIWerte = ZettelUIWerte(receipt, road,
                    decoration, zoning);
                document.Vegetation = JsonConvert.DeserializeObject<
                    VegetationOptions>(ReadVegetation(lot)?.Options ?? "{}");
                var values = document.Input.LayoutSettings.AlleWerte;
                values["Ausrichtwinkel"] = double.IsNaN(receipt.Ausrichtwinkel)
                    ? null : receipt.Ausrichtwinkel;
                document.Input.LayoutSettings.Ausrichtwinkel =
                    double.IsNaN(receipt.Ausrichtwinkel)
                        ? null : receipt.Ausrichtwinkel;
                values["TeilflaechenAusrichtungen"] =
                    alignments?.Select(a => (object)new
                    {
                        Anker = new[] { a.Anker.x, a.Anker.y },
                        a.Winkel,
                        LinieA = new[] { a.LinieA.x, a.LinieA.y },
                        LinieB = new[] { a.LinieB.x, a.LinieB.y },
                    }).ToArray();
                document.Input.LayoutSettings.TeilflaechenAusrichtungen =
                    alignments?.Select(a => new DebugAlignment
                    {
                        Anchor = DebugPoint2.From(a.Anker),
                        Angle = a.Winkel,
                    }).ToArray();
                values["Teilflaechenschnitte"] =
                    ParkingSettingsInventory.SichererWert(cuts);
                document.Input.LayoutSettings.Teilflaechenschnitte =
                    cuts?.Select(c => new DebugStrecke
                    {
                        A = DebugPoint2.From(c.A),
                        B = DebugPoint2.From(c.B),
                    }).ToArray();
                values["Zoningflaechen"] =
                    ParkingSettingsInventory.SichererWert(zones);
                document.Input.LayoutSettings.Zoningflaechen =
                    zones?.Select(z => new DebugZoningflaeche
                    {
                        Ecke = DebugPoint2.From(z.Ecke),
                        Spalten = z.Spalten,
                        Reihen = z.Reihen,
                        Winkel = z.Winkel,
                        Rand = z.Rand,
                        Aussentiefen = z.Aussentiefen,
                    }).ToArray();
                values["Randzoning"] = ParkingSettingsInventory.SichererWert(
                    _randzoningAusZettel?.ToArray());
                document.Input.LayoutSettings.Randzoning =
                    _randzoningAusZettel?.Select(r => new DebugStrecke
                    {
                        A = DebugPoint2.From(r.A),
                        B = DebugPoint2.From(r.B),
                    }).ToArray();
                if (EntityManager.HasBuffer<ParkingLotBuildText>(lot)
                    && TryReadBuildText(EntityManager.GetBuffer<ParkingLotBuildText>(
                        lot, true), 5, out var zoningRoad)
                    && !string.IsNullOrEmpty(zoningRoad))
                {
                    values["Zoningstrasse"] = zoningRoad;
                    document.Input.LayoutSettings.Zoningstrasse = zoningRoad;
                }
                if (EntityManager.HasBuffer<ParkingLotBuildZoningSeite>(lot))
                {
                    var buffer = EntityManager.GetBuffer<ParkingLotBuildZoningSeite>(
                        lot, true);
                    var seiten = new object[buffer.Length];
                    for (var i = 0; i < buffer.Length; i++)
                    {
                        var s = buffer[i];
                        seiten[i] = new
                        {
                            A = new[] { s.A.x, s.A.y },
                            B = new[] { s.B.x, s.B.y },
                            s.Links, s.Aus,
                        };
                    }
                    document.Zoningseiten = seiten;
                }
                return;
            }
        }

        /// <summary>
        /// Json.NETs Rundlauf-Format ist bereits verlustfrei. G17 wird hier
        /// trotzdem ausdrücklich erzwungen, damit der Abzug nicht von einer
        /// Bibliotheksvoreinstellung oder der Rechnerkultur abhängt.
        /// </summary>
        private sealed class FullPrecisionNumberConverter : JsonConverter
        {
            public override bool CanRead => false;

            public override bool CanConvert(Type objectType)
            {
                var type = Nullable.GetUnderlyingType(objectType) ?? objectType;
                return type == typeof(float) || type == typeof(double);
            }

            public override void WriteJson(
                JsonWriter writer,
                object value,
                JsonSerializer serializer)
            {
                if (value == null)
                {
                    writer.WriteNull();
                    return;
                }

                var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (double.IsNaN(number) || double.IsInfinity(number))
                {
                    writer.WriteNull();
                    return;
                }
                writer.WriteRawValue(number.ToString("G17", CultureInfo.InvariantCulture));
            }

            public override object ReadJson(
                JsonReader reader,
                Type objectType,
                object existingValue,
                JsonSerializer serializer)
            {
                throw new NotSupportedException();
            }
        }
    }
}
