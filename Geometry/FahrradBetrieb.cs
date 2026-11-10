using System;

namespace ParkingLotTool.Geometry
{
    /** Sonde 29.10.2026: 6 / 12,5 / 23 je Rad, Komfort 0,2 / 0,3 / 0,5. */
    public static class FahrradBetrieb
    {
        public static float Komfort(Fahrradlayout layout) => layout switch
        {
            Fahrradlayout.A => .2f,
            Fahrradlayout.C => .3f,
            Fahrradlayout.D => .5f,
            _ => throw new ArgumentOutOfRangeException(nameof(layout)),
        };

        public static int Unterhalt(Fahrradlayout layout, int kapazitaet)
        {
            var satz = layout switch
            {
                Fahrradlayout.A => 6.0,
                Fahrradlayout.C => 12.5,
                Fahrradlayout.D => 23.0,
                _ => throw new ArgumentOutOfRangeException(nameof(layout)),
            };
            return checked((int)Math.Round(Math.Max(0, kapazitaet) * satz));
        }
    }
}
