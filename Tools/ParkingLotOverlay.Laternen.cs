using Colossal.Mathematics;
using ParkingLotTool.Geometry;
using Unity.Mathematics;
using static ParkingLotTool.Tools.ParkingLotPreviewStyle;

namespace ParkingLotTool.Tools
{
    internal sealed partial class ParkingLotOverlay
    {
        /**
         * LATERNEN IN DER VORSCHAU (Nutzer 2026-10-04: "an die Preview hast
         * du auch gedacht?").
         *
         * Wie im Design: Mastpunkt, Arme in Leuchtrichtung des gewaehlten
         * Modells (einseitig ein Arm, doppelt zwei, rundum keiner) und ein
         * zarter Lichtkreis in der gemessenen Reichweite. Farbe nach Ort:
         * Kappe, innen (Mittelstreifen und Kopf-an-Kopf), Rand.
         */
        /** Lichtkreise zeigen - Schalter im Laternenfenster, Standard aus. */
        internal bool LichtkreiseZeigen;
        internal EchteVorschauArt EchteMusterArt;
        internal OberflaechenMuster.Bereich? EchteMusterBereich;
        private bool ImLaternenmuster(float3 pos) => EchteMusterArt == EchteVorschauArt.Laternen
            && EchteMusterBereich.HasValue && EchteVorschau.Enthaelt(EchteMusterBereich.Value,pos.xz);

        private void ZeichneLaternen(ParkingLotPreviewBuffer buffer)
        {
            if (_laternen.Count == 0) return;
            ParkingLotMessung.Zaehle(ParkingLotMessung.Zaehler.Sonstige, _laternen.Count);
            if (LichtkreiseZeigen)
                foreach (var (pos, platz, bauart, reichweite) in _laternen)
                    if (!ImLaternenmuster(pos)) buffer.DrawRing(LaterneLichtColor, pos, 2f * reichweite, .3f);
            foreach (var (pos, platz, bauart, _) in _laternen)
            {
                if (ImLaternenmuster(pos)) continue;
                var vorn = LaternenKatalog.Vorwaerts(platz, bauart);
                if (bauart == LaternenBauart.Einseitig)
                    Arm(buffer, pos, vorn);
                else if (bauart == LaternenBauart.Doppelt)
                {
                    Arm(buffer, pos, platz.Richtung);
                    Arm(buffer, pos, -platz.Richtung);
                }
                var farbe = platz.Art == LaternenArt.Kappe ? LaterneKappeColor
                    : platz.Art == LaternenArt.Rand ? LaterneRandColor : LaterneInnenColor;
                buffer.DrawCircle(farbe, pos, bauart == LaternenBauart.Rundum ? LaterneMastDiameter * 1.6f : LaterneMastDiameter);
            }
        }

        private static void Arm(ParkingLotPreviewBuffer buffer, float3 pos, float2 richtung)
        {
            var ende = pos + new float3(richtung.x, 0f, richtung.y) * LaterneArmLaenge;
            buffer.DrawLine(LaterneArmColor, new Line3.Segment(pos, ende), LaterneArmBreite);
        }
    }
}
