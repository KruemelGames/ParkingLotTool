using System;
using System.Linq;

namespace ParkingLotTool.Geometry.Zellen
{
    internal sealed partial class Teilflaechenlayout
    {
        internal bool GehoertTeilungZuPolygon(
            Rasterteilung teilung,
            Polygon polygon)
        {
            /*
             * DIE LINIE ENTSCHEIDET, NICHT DER FRAGMENTSCHWERPUNKT.
             *
             * Ein Fragment kann eine Teilflaeche schneiden, obwohl sein
             * Schwerpunkt jenseits ihrer Grenze liegt. Die alte Pruefung liess
             * dann genau die Rasterlinie aus, die das Fragment an einer
             * Buchtkante haette teilen muessen. Spaeter klassifizierte der
             * Schwerpunkt das ungeteilte Fragment als Gruen: gemessen lagen
             * beim Nutzerbau dadurch 12,39 / 5,49 / 0,66 m2 Gras unter
             * fertigen Buchten.
             *
             * Gemeint ist eine Teilung genau dann, wenn ihr vorab begrenzter
             * Linienabschnitt sowohl im Fragment als auch in der Teilflaeche
             * verlaeuft. Die beiden eindimensionalen Innenintervalle auf
             * demselben Segment beantworten diese Frage ohne nachtraegliche
             * Geometriekorrektur.
             */
            var hatMinus = false;
            var hatPlus = false;
            foreach (var punkt in polygon.Punkte)
            {
                var seite = teilung.Linie.Seite(punkt);
                if (seite < 0) hatMinus = true;
                if (seite > 0) hatPlus = true;
            }
            if (!hatMinus || !hatPlus) return false;

            var fragmentintervalle = SegmentparameterImPolygon(
                teilung.Anfang, teilung.Ende, polygon.Punkte.ToArray());
            var teilintervalle = _teilungsintervalle[teilung.Linie.Id];
            var exakt = fragmentintervalle.Any(fragment =>
                teilintervalle.Any(teilintervall =>
                    Math.Min(fragment.Ende, teilintervall.Ende)
                        > Math.Max(fragment.Anfang, teilintervall.Anfang)
                            + 1e-9));

            if (ParkingGeometry.LiveAn)
            {
                var mitte = Geometrie.Mittelwert(polygon);
                var alt = teilung.Teilindizes.Any(index =>
                    _teileNachIndex.TryGetValue(index, out var teil)
                        && mitte.X >= teil.MinX - 1e-9
                        && mitte.X <= teil.MaxX + 1e-9
                        && mitte.Y >= teil.MinY - 1e-9
                        && mitte.Y <= teil.MaxY + 1e-9
                        && Geometrie.EnthaeltOderRand(
                            teil.PolygonWurzel, mitte));
                if (exakt && !alt) _durchLinienlageErgaenzt++;
                if (alt && !exakt) _durchLinienlageVerworfen++;
            }
            return exakt;
        }

        internal void ProtokolliereRasterzuordnung()
        {
            if (!ParkingGeometry.LiveAn) return;
            ParkingGeometry.Live("  rasterzuordnung durch Linienlage +"
                + _durchLinienlageErgaenzt + " / -"
                + _durchLinienlageVerworfen
                + " gegenueber Fragmentschwerpunkt");
        }
    }
}
