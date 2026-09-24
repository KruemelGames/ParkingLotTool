using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /**
     * Gemeinsame Feldliste fuer Baujournal und beide JSON-Abzuege.
     * Gemessen: LayoutSettings hat 22 oeffentliche und 6 interne Werte;
     * VegetationOptions hat 6 oeffentliche Felder. Der Geometrietest
     * zaehlt die Typen unabhaengig nach.
     */
    public static class ParkingSettingsInventory
    {
        public static readonly string[] Layout =
        {
            "Es", "Ai", "Cw", "Sl", "Sw", "Md", "Gassenbreite", "Cr",
            "Qk", "Randstrassen", "Auto", "Angle", "AngleMode",
            "Ausrichtwinkel", "TeilflaechenAusrichtungen", "Zoningflaechen",
            "Randzoning", "Zoningstrasse", "BusStops", "Teilflaechenschnitte",
            "Entrances", "AutomaticEntrances", "KantenVersatz", "Zellen",
            "EineFlaeche", "NoNotch", "Single", "NoHalf",
        };

        public static readonly string[] Vegetation =
        {
            "Enabled", "Line", "Density", "Ages", "Seed", "Species",
        };

        public static readonly string[] Panelwerte =
        {
            "MedianWidth", "GreenMedian", "CrossBays", "SurfaceRoad",
            "SurfaceDecoration", "SurfaceZoning", "SurfaceRoadOn",
            "SurfaceDecorationOn", "SurfaceApronOn", "BayIcons",
            "ZoningWinkelmodus", "ZoningWinkel", "ZoningAussentiefe",
            "ZoningAusrichtwinkel",
        };

        // Die Set-Trigger aus bindings.ts sind bewusst eingeteilt:
        // ein neuer Trigger muss im Geometrietest neu zugeordnet werden.
        public static readonly string[] BauTrigger =
        {
            "SetAisleWidth", "SetAngleMode", "SetBayIcons", "SetCrossBays",
            "SetCrossCaps", "SetCrossWidth", "SetEdgeSetback", "SetEngine",
            "SetGreenMedian", "SetMedianWidth", "SetRandstrassen",
            "SetRowAngle", "SetSurfaceApronOn", "SetSurfaceDecoration",
            "SetSurfaceDecorationOn", "SetSurfaceRoad", "SetSurfaceRoadOn",
            "SetSurfaceZoning", "SetZoningAussentiefe", "SetZoningWinkel",
            "SetZoningWinkelmodus",
        };

        public static readonly string[] AndereTrigger =
        {
            "SetAltEngineOhneWarnung", "SetAsDefault", "SetAutoEntryMode",
            "SetEntranceKind", "SetEntranceMode", "SetBusStopMode", "SetMarkerMode",
            "SetPanelOpen", "SetPanelPosition", "SetPanelStil",
            "SetSelectedParkingFee", "SetTab", "SetZoningLinienwahl",
            "SetZoningModus", "SetZoningSeitenModus",
        };

        public static Dictionary<string, object> Erfasse<T>(T source,
            IEnumerable<string> names)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            if (source == null) return result;
            var type = typeof(T);
            foreach (var name in names)
            {
                var member = type.GetMember(name, BindingFlags.Public
                    | BindingFlags.NonPublic | BindingFlags.Instance).Single();
                result.Add(name, member is PropertyInfo property
                    ? SichererWert(property.GetValue(source))
                    : SichererWert(((FieldInfo)member).GetValue(source)));
            }
            return result;
        }

        public static object SichererWert(object value)
        {
            if (value == null) return null;
            if (value is float2 point) return new[] { point.x, point.y };
            if (value is double2 doublePoint)
                return new[] { doublePoint.x, doublePoint.y };
            if (value is float3 world)
                return new[] { world.x, world.y, world.z };
            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is string
                || value is decimal) return value;
            if (value is Array array)
            {
                var items = new object[array.Length];
                for (var i = 0; i < array.Length; i++)
                    items[i] = SichererWert(array.GetValue(i));
                return items;
            }
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            var properties = type.GetProperties(BindingFlags.Public
                | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0);
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var field in fields)
                result[field.Name] = SichererWert(field.GetValue(value));
            foreach (var property in properties)
                result[property.Name] = SichererWert(property.GetValue(value));
            return result;
        }
    }
}
