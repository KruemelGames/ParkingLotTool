using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private static string SerializeDebugDocument(DebugDumpDocument document)
        {
            var serializer = new JsonSerializer
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Include,
            };
            serializer.Converters.Add(new FullPrecisionNumberConverter());
            serializer.Converters.Add(new StringEnumConverter());

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
