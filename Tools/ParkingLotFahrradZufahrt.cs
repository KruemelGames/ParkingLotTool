using System.Globalization;
using System.Linq;
using System.Text;
using ParkingLotTool.Geometry;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private string _fahrradZufahrtSchluessel;
        private FahrradGeometrie.Zufahrtspruefung _fahrradZufahrtPruefung;

        /**
         * FAHRRADZUFAHRT VORAB (Nutzer 03.11.2026): rot und nicht setzbar, wenn ihr Weg
         * keinen Gang, Sammelweg, keine Achse und keinen Ring erreicht, ohne Bausteine zu
         * queren. Die Anlage ohne diese Zufahrt wird einmal je Stand gerechnet, nicht je
         * Bild: der Schluessel aendert sich nur mit Umriss, Einstellungen und den anderen
         * Zufahrten.
         */
        private bool FahrradZufahrtMoeglich(Entrance kandidat, int ignoredIndex, LayoutSettings settings)
        {
            if (settings == null || settings.Fahrzeugart != Fahrzeugart.Fahrrad || _points.Count < 3) return true;
            var andere = _entrances.Where((e, i) => i != ignoredIndex && e != null).Select(e => e.Clone()).ToArray();
            var s = new StringBuilder();
            foreach (var p in _points) s.Append(p.x.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(p.y.ToString("R", CultureInfo.InvariantCulture)).Append(';');
            s.Append('|').Append((int)settings.Fahrradlayout).Append('|').Append((int)settings.Fahrradstil)
                .Append('|').Append(settings.Es.ToString("R", CultureInfo.InvariantCulture))
                .Append('|').Append(settings.AngleMode).Append('|').Append(settings.Angle.ToString("R", CultureInfo.InvariantCulture))
                .Append('|').Append(settings.Ausrichtwinkel?.ToString("R", CultureInfo.InvariantCulture))
                .Append('|').Append(settings.KantenVersatz.ToString("R", CultureInfo.InvariantCulture));
            foreach (var e in andere) s.Append('|').Append(e.Edge).Append(':').Append(e.Along.ToString("R", CultureInfo.InvariantCulture));
            var schluessel = s.ToString();
            if (schluessel != _fahrradZufahrtSchluessel)
            {
                var pruefsettings = settings.Clone();
                pruefsettings.Entrances = andere;
                pruefsettings.AutomaticEntrances = false;
                _fahrradZufahrtPruefung = FahrradGeometrie.Pruefung(_points.ToArray(), pruefsettings);
                _fahrradZufahrtSchluessel = schluessel;
            }
            return _fahrradZufahrtPruefung?.Moeglich(kandidat) ?? true;
        }
    }
}
