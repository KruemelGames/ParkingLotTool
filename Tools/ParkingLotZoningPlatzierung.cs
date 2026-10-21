using System;
using System.Collections.Generic;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * WIE ZONINGFLAECHEN GESETZT WERDEN (Nutzer 21.10.).
     *
     * Zwei Werkzeugmodi fuer dieselbe Flaeche: "ziehen" (so gross, wie
     * gezogen) und "fest" (die eingetragene Breite x Tiefe haengt am Zeiger).
     * Art, feste Groesse und gemerkte Groessen stehen in den Optionen und
     * ueberleben damit einen Neustart, bis der Spieler sie aendert. Die
     * Obergrenze ist immer die aus den Mod-Einstellungen (ZonBreite/ZonTiefe),
     * die Untergrenze 1 - beides wird hier erzwungen, nicht nur im Panel.
     */
    public sealed partial class ParkingLotToolSystem
    {
        private const int ZoningVorlagenHoechstzahl = 6;

        internal bool ZoningFesteGroesse => Mod.Optionen?.ZoningPlatzierart == "fest";

        internal int ZoningFestBreite => math.clamp(
            Mod.Optionen?.ZoningFestBreite ?? 3, 1, ParkingGeometry.ZoningMaxBreite);

        internal int ZoningFestTiefe => math.clamp(
            Mod.Optionen?.ZoningFestTiefe ?? 2, 1, ParkingGeometry.ZoningMaxTiefe);

        internal void SetzeZoningPlatzierart(string art)
        {
            var optionen = Mod.Optionen;
            if (optionen == null) return;
            var neu = art == "fest" ? "fest" : "ziehen";
            if (optionen.ZoningPlatzierart == neu) return;
            optionen.ZoningPlatzierart = neu;
            optionen.ApplyAndSave();
            if (ZoningModus) _uiSystem?.SetStatus(ZoningSetzstatus());
            Mod.log.Info("PLT-Zoning: Werkzeugmodus " + neu + ".");
        }

        internal void SetzeZoningFestGroesse(int breite, int tiefe)
        {
            var optionen = Mod.Optionen;
            if (optionen == null) return;
            breite = math.clamp(breite, 1, ParkingGeometry.ZoningMaxBreite);
            tiefe = math.clamp(tiefe, 1, ParkingGeometry.ZoningMaxTiefe);
            if (optionen.ZoningFestBreite == breite && optionen.ZoningFestTiefe == tiefe) return;
            optionen.ZoningFestBreite = breite;
            optionen.ZoningFestTiefe = tiefe;
            optionen.ApplyAndSave();
        }

        /** Gemerkte Groessen, je "BxT", ohne Doppelte und ohne ungueltige. */
        internal List<(int Breite, int Tiefe)> ZoningGroessenVorlagen()
        {
            var liste = new List<(int, int)>();
            foreach (var teil in (Mod.Optionen?.ZoningGroessenVorlagen ?? "").Split(';'))
            {
                var zahlen = teil.Split('x');
                if (zahlen.Length != 2 || !int.TryParse(zahlen[0], out var b)
                    || !int.TryParse(zahlen[1], out var t) || b < 1 || t < 1) continue;
                if (!liste.Contains((b, t))) liste.Add((b, t));
            }
            return liste;
        }

        internal void SpeichereZoningGroessenVorlage()
        {
            var liste = ZoningGroessenVorlagen();
            var neu = (ZoningFestBreite, ZoningFestTiefe);
            if (liste.Contains(neu)) return;
            liste.Add(neu);
            while (liste.Count > ZoningVorlagenHoechstzahl) liste.RemoveAt(0);
            SchreibeZoningVorlagen(liste);
        }

        internal void EntferneZoningGroessenVorlage(string vorlage)
        {
            var liste = ZoningGroessenVorlagen();
            if (liste.RemoveAll(v => v.Breite + "x" + v.Tiefe == vorlage) > 0)
                SchreibeZoningVorlagen(liste);
        }

        private static void SchreibeZoningVorlagen(List<(int Breite, int Tiefe)> liste)
        {
            var optionen = Mod.Optionen;
            if (optionen == null) return;
            optionen.ZoningGroessenVorlagen = string.Join(";", liste.Select(v => v.Breite + "x" + v.Tiefe));
            optionen.ApplyAndSave();
        }

        /**
         * Je Bild ans Panel; die Bindungen melden nur Aenderungen weiter.
         * Die Vorlagen gehen roh hinaus (geschrieben wird nur bereinigt), damit
         * hier je Bild nichts zerlegt und nichts angelegt wird.
         */
        private void MeldeZoningPlatzierung()
        {
            _uiSystem?.SetZoningPlatzierung(ZoningFesteGroesse ? "fest" : "ziehen",
                ZoningFestBreite, ZoningFestTiefe,
                Mod.Optionen?.ZoningGroessenVorlagen ?? string.Empty,
                ParkingGeometry.ZoningMaxBreite, ParkingGeometry.ZoningMaxTiefe);
        }
    }
}
