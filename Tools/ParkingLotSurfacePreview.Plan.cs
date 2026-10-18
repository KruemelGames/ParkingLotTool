using System;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        /** Derselbe Zuschnitt vor dem Bauen und vor dem Musterschnitt. Keine Definitionen. */
        private void GeplanteBauflaechen(ParkingLayout layout, bool strasseAn, bool dekoAn, bool vorbild,
            out float2[][] gras, out float2[][] asphalt, out float2[][] verschmolzen, out float2[][] einzeln)
        {
            layout.SurfacesForPlacement(strasseAn, dekoAn, out gras, out asphalt);
            verschmolzen = Array.Empty<float2[]>();
            einzeln = vorbild ? _vorflaechen : Array.Empty<float2[]>();
            if (einzeln.Length > 0)
            {
                VerschmelzeVorflaechen(asphalt, out verschmolzen, out var rest, out einzeln, out _);
                asphalt = rest;
            }
        }

        private OberflaechenMuster.Plan BerechneSurfacePreview()
        {
            GeplanteBauflaechen(_areaPreviewLayout, _uiSystem.FlaecheStrasseAn, _uiSystem.FlaecheDekoAn, true,
                out var gras, out var asphalt, out var verschmolzen, out var einzeln);
            var lot = _points.ToArray();
            var richtung = OberflaechenMuster.Richtung(_areaPreviewLayout.AisleLine, lot, lot[0]);
            return OberflaechenMuster.Berechne(_musterGriff.Startbereich(lot,richtung), new[] {
                gras, asphalt, ZoningFlaecheAus ? Array.Empty<float2[]>() : _areaPreviewLayout.ZoningSurface,
                _areaPreviewLayout.ZoningRoadSurface,
                _uiSystem.FlaecheStrasseAn ? verschmolzen : Array.Empty<float2[]>(),
                _uiSystem.FlaecheStrasseAn ? einzeln : Array.Empty<float2[]>(),
            });
        }
    }
}
