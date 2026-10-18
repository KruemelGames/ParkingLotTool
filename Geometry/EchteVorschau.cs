using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public enum EchteVorschauArt { Aus, Flaechen, Laternen, Vegetation }

    /** Eine Auswahl statt drei gespeicherter Schalter; alte Dateien bleiben lesbar. */
    public static class EchteVorschau
    {
        public const int MaxObjekte = 256;
        public static EchteVorschauArt Lesen(int? wert, bool alterSchalter = false)
            => wert.HasValue ? (wert.Value >= 0 && wert.Value <= 3 ? (EchteVorschauArt)wert.Value : EchteVorschauArt.Aus)
                : alterSchalter ? EchteVorschauArt.Flaechen : EchteVorschauArt.Aus;
        public static EchteVorschauArt Schalten(EchteVorschauArt aktuell, EchteVorschauArt art, bool an)
            => art < EchteVorschauArt.Flaechen || art > EchteVorschauArt.Vegetation ? aktuell
                : an ? art : aktuell == art ? EchteVorschauArt.Aus : aktuell;
        public static bool Enthaelt(OberflaechenMuster.Bereich b, float2 punkt)
        {
            if (!b.Gueltig || !math.all(math.isfinite(punkt))) return false;
            var lokal = b.Lokal(punkt) - b.Mitte;
            return math.all(math.abs(lokal) <= b.Halbseite + .001f);
        }
        public sealed class Auswahl
        {
            public OberflaechenMuster.Bereich Ausschnitt;
            public int[] Indizes = Array.Empty<int>();
            public int Verkleinerungen;
        }
        public static Auswahl Waehle(float2[] lot, float2 mitte, float2 richtung, IReadOnlyList<float2> punkte)
            => Waehle(OberflaechenMuster.Ausschnitt(lot, mitte, richtung),punkte);
        public static Auswahl Waehle(OberflaechenMuster.Bereich b, IReadOnlyList<float2> punkte)
        {
            var mitte=b.Mitte;
            for (int versuch = 0; versuch < 24; versuch++)
            {
                var indizes = new List<int>();
                for (int i=0; i<punkte.Count; i++) if (Enthaelt(b,punkte[i])) indizes.Add(i);
                if (indizes.Count <= MaxObjekte) return new Auswahl {
                    Ausschnitt=b, Indizes=indizes.ToArray(), Verkleinerungen=versuch };
                b = new OberflaechenMuster.Bereich(mitte,b.Halbseite*.7f,b.Richtung);
            }
            // Zu viele deckungsgleiche Standorte: kein halber Satz.
            return new Auswahl { Ausschnitt=b, Verkleinerungen=24 };
        }
        /** ObjectState 1..6 sind die Mesh-Altersstufen, 0 heisst ohne Altersbedingung. */
        public static bool Altersmesh(int zustand, int alter)
            => zustand < 1 || zustand > 6 || alter < 0 || zustand == alter+1;
    }
}
