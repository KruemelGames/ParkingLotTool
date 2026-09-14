using UnityEngine;

namespace ParkingLotTool.Tools
{
    // Gemeinsame Darstellung fuer alle PLT-Werkzeugzustaende.
    // Meter im Weltraum, keine Pixel. Siehe PREVIEW-DESIGN.md fuer Abnahme.
    internal static class ParkingLotPreviewStyle
    {
        internal static readonly Color VegetationColor = new Color(.49f,.83f,.33f,.75f);
        internal const float VegetationTreeDiameter = 2f;
        internal const float VegetationShrubDiameter = .9f;
        internal const float GridLineWidth = 0.18f;
        internal const float PolygonLineWidth = 0.35f;
        internal const float PreviewLineWidth = 0.25f;
        internal const float HoverLineWidth = 0.55f;
        internal const float SelectedLineWidth = 0.8f;
        internal const float PointDiameter = 2.4f;
        internal const float ActivePointDiameter = 3.6f;
        internal const float SnapDiameter = 4.8f;
        internal const float HandleOutlineWidth = 0.24f;
        internal const float EdgeArrowGap = 2.4f;
        internal const float EdgeArrowReach = 7f;
        internal const float ChargerDiameter = 1.8f;
        internal const float MarkerDiameter = 4.8f;
        internal const float EntranceHandleDiameter = PointDiameter;
        internal const float EntranceActiveDiameter = ActivePointDiameter;
        internal const float EntranceSpacingWidth = HoverLineWidth;
        internal const float SnapGuideWidth = PreviewLineWidth;
        internal const float DashLength = 3f;
        internal const float DashGap = 2f;
        internal const float FillQuiet = 0.08f;
        internal const float FillSelected = 0.18f;
        internal const float FillHover = 0.24f;
        internal const float ZoningFuellungRuhe = FillQuiet;
        internal const float ZoningFuellung = FillSelected;
        internal const float ZoningFuellungHover = FillHover;
        // Bereits im Spiel abgestimmte Teilflaechenstufen bleiben erhalten.
        internal const float TeilflaecheFuellung = 0.15f;
        internal const float TeilflaecheFuellungBenutzt = 0.05f;

        internal static Color Alpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        internal static readonly Color Accent = new Color(0.31f, 0.765f, 0.969f, 0.92f);
        internal static readonly Color Positive = new Color(0.49f, 0.85f, 0.55f, 0.92f);
        internal static readonly Color Negative = new Color(0.94f, 0.32f, 0.32f, 0.94f);
        internal static readonly Color Warning = new Color(0.95f, 0.76f, 0.31f, 0.92f);
        internal static readonly Color Muted = new Color(0.66f, 0.73f, 0.79f, 0.55f);
        internal static readonly Color PolygonColor = Alpha(Accent, 0.78f);
        internal static readonly Color PreviewColor = Alpha(Accent, 0.55f);
        internal static readonly Color PointColor = new Color(0.88f, 0.94f, 0.98f, 0.8f);
        internal static readonly Color ActivePointColor = Accent;
        internal static readonly Color ClosePointColor = Positive;
        internal static readonly Color ExtrudeColor = Accent;
        internal static readonly Color ShiftColor = Accent;
        internal static readonly Color TrennschnittColor = Accent;
        internal static readonly Color EntranceHandleColor = Accent;
        internal static readonly Color EntranceCandidateColor = Accent;
        internal static readonly Color EntranceBlockedColor = Negative;
        internal static readonly Color MarkerColor = new Color(0.91f, 0.40f, 0.72f, 0.92f);
        internal static readonly Color NormalBayColor = new Color(0.79f, 0.84f, 0.88f, 0.24f);
        internal static readonly Color DisabledBayColor = Alpha(Accent, 0.34f);
        internal static readonly Color ElectricBayColor = Alpha(Positive, 0.34f);
        internal static readonly Color ChargerColor = Positive;
        internal static readonly Color GreenColor = new Color(0.55f, 0.72f, 0.47f, 0.20f);
        internal static readonly Color PerimeterRoadColor = new Color(0.46f, 0.57f, 0.66f, 0.16f);
        internal static readonly Color CrossRoadColor = PerimeterRoadColor;
        internal static readonly Color AisleRoadColor = PerimeterRoadColor;
        // RGB entspricht unveraendert den vier art*-Klassen im Panel.
        internal static readonly Color[] EntranceArtColors =
        {
            new Color(0.878f, 0.353f, 0.169f, 0.32f),
            new Color(0.310f, 0.765f, 0.969f, 0.32f),
            new Color(0.949f, 0.757f, 0.306f, 0.32f),
            new Color(0.494f, 0.851f, 0.341f, 0.32f),
        };
        internal static readonly Color EntranceRoadColor = EntranceArtColors[0];
        // Fangarten bleiben unterscheidbar; nur der aktive Fang wird gezeigt.
        internal static readonly Color RoadSnapColor = Accent;
        internal static readonly Color ObjectSnapColor = new Color(0.75f, 0.65f, 0.91f, 0.92f);
        internal static readonly Color AreaSnapColor = Positive;
        internal static readonly Color AngleSnapColor = Accent;
        internal static readonly Color CrossSnapColor = PointColor;
        internal static readonly Color GuideSnapColor = Alpha(Accent, 0.65f);
        internal static readonly Color ZoneGridSnapColor = new Color(0.40f, 0.83f, 0.80f, 0.92f);
        // Bauland ist eine Kategorie, kein Warnzustand: ruhiges Blauviolett.
        internal static readonly Color ZoningColor = new Color(0.62f, 0.69f, 0.91f, 0.8f);
    }
}
