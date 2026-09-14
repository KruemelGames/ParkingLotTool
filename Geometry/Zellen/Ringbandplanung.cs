using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingLotTool.Geometry.Zellen
{
    internal static class Ringbandplanung
    {
        // Im Bauzettel 2026-09-09 12:17 stand eine Achse nur 4,9617 m
        // neben der Stufenstrasse. Der globale Zentriervorschlag kennt deren
        // Lage nicht. Hier werden die Module VOR den Teilungslinien zugelassen.
        internal static Bandplan Baender(double minY, double maxY,
            Zelleneinstellungen e, IReadOnlyList<Punkt> ring,
            IReadOnlyList<Randzoningabschnitt> randzoning = null, bool randstrassen = true)
        {
            var vorschlag = Layoutplanung.Baender(minY, maxY, e.Buchttiefe,
                e.Fahrgassenbreite, e.Gruenstreifenbreite, null, !randstrassen);
            var grenzen = new List<(Punkt A, Punkt B, double Reserve)>();
            if (randstrassen)
                for (int k = 0; k < ring.Count; k++)
                    grenzen.Add((ring[k], ring[(k + 1) % ring.Count],
                        e.Fahrgassenbreite + e.Buchttiefe));
            // Nutzerfall: 8,41 m statt 13,40 m. RZ-Achsen begrenzen
            // die Module vor der Rasterteilung, auch ohne Randstrassen.
            foreach (var rz in randzoning ?? Array.Empty<Randzoningabschnitt>())
                grenzen.Add((rz.A, rz.B, e.Fahrgassenbreite / 2 + rz.Halb + e.Buchttiefe));
            var module = new List<Parkmodulabschnitt>();
            double halb = e.Fahrgassenbreite / 2;
            foreach (var alt in vorschlag.Module)
            {
                if (Frei(alt.Fahrgassenmitte))
                {
                    module.Add(alt);
                    continue;
                }
                double links = module.Count == 0 ? minY : module.Last().Ende;
                double rechts = alt.Id + 1 == vorschlag.Module.Count
                    ? maxY : vorschlag.Module[alt.Id + 1].Anfang;
                var kandidaten = new List<double>();
                foreach (var grenze in grenzen)
                {
                    var a = grenze.A; var b = grenze.B;
                    if (!Parallel(a, b)) continue;
                    kandidaten.Add(Math.Min(a.Y, b.Y) - grenze.Reserve - e.Cs2Mindestkante);
                    kandidaten.Add(Math.Max(a.Y, b.Y) + grenze.Reserve + e.Cs2Mindestkante);
                }
                Parkmodulabschnitt bester = null;
                // Zuerst zwei Reihen erhalten. Wenn dafuer kein Platz ist,
                // eine vollstaendige Reihe aufgeben, niemals eine Bucht kuerzen.
                foreach (var seiten in new[] { (true, true), (false, true), (true, false) })
                {
                    double vor = halb + (seiten.Item1 ? e.Buchttiefe : 0);
                    double nach = halb + (seiten.Item2 ? e.Buchttiefe : 0);
                    var moeglich = kandidaten.Concat(new[] { links + vor, rechts - nach })
                        .Where(y => y - vor >= links - 1e-6 && y + nach <= rechts + 1e-6)
                        .Where(y => y - vor - links < 1e-6 || y - vor - links >= e.Cs2Mindestkante - 1e-6)
                        .Where(y => rechts - y - nach < 1e-6 || rechts - y - nach >= e.Cs2Mindestkante - 1e-6)
                        .Where(y => Frei(y))
                        .OrderBy(y => Math.Abs(y - alt.Fahrgassenmitte)).ToArray();
                    if (moeglich.Length == 0) continue;
                    double mitte = moeglich[0];
                    bester = new Parkmodulabschnitt { Id = alt.Id,
                        ErsteReihe = new Bandabschnitt(alt.ErsteReihe.Id,
                            mitte - vor, mitte - halb, Zellart.Bucht, alt.ErsteReihe.ReihenId),
                        Fahrgasse = new Bandabschnitt(alt.Fahrgasse.Id,
                            mitte - halb, mitte + halb, Zellart.Fahrgasse),
                        ZweiteReihe = new Bandabschnitt(alt.ZweiteReihe.Id,
                            mitte + halb, mitte + nach, Zellart.Bucht, alt.ZweiteReihe.ReihenId) };
                    break;
                }
                // Auch beim Wegfall verschwinden beide zugehoerigen Reihen
                // bereits aus dem Plan: es bleibt keine unerreichbare Reihe.
                if (bester != null) module.Add(bester);
            }
            if (module.SequenceEqual(vorschlag.Module)) return vorschlag;
            var baender = new List<Bandabschnitt>();
            int id = vorschlag.Baender.Max(b => b.Id) + 1;
            double cursor = minY;
            foreach (var m in module)
            {
                if (m.Anfang > cursor + 1e-6)
                    baender.Add(new Bandabschnitt(id++, cursor, m.Anfang,
                        cursor == minY ? Zellart.Restgruen : Zellart.Gruenstreifen));
                if (m.ErsteReihe.Ende > m.ErsteReihe.Anfang + 1e-6) baender.Add(m.ErsteReihe);
                baender.Add(m.Fahrgasse);
                if (m.ZweiteReihe.Ende > m.ZweiteReihe.Anfang + 1e-6) baender.Add(m.ZweiteReihe);
                cursor = m.Ende;
            }
            if (maxY > cursor) baender.Add(new Bandabschnitt(id, cursor, maxY, Zellart.Restgruen));
            return new Bandplan { Baender = baender, Module = module };

            bool Frei(double y)
            {
                var schnitte = new List<double>();
                for (int k = 0; k < ring.Count; k++)
                {
                    var a = ring[k]; var b = ring[(k + 1) % ring.Count];
                    if ((a.Y > y) != (b.Y > y))
                        schnitte.Add(a.X + (y - a.Y) * (b.X - a.X) / (b.Y - a.Y));
                }
                schnitte.Sort();
                if (schnitte.Count < 2) return false;
                foreach (var grenze in grenzen)
                {
                    var a = grenze.A; var b = grenze.B;
                    if (!Parallel(a, b)) continue;
                    for (int j = 1; j < schnitte.Count; j += 2)
                    {
                        double von = Math.Max(schnitte[j - 1], Math.Min(a.X, b.X));
                        double bis = Math.Min(schnitte[j], Math.Max(a.X, b.X));
                        if (bis - von < 20 - 1e-6) continue;
                        double ya = a.Y + (von - a.X) * (b.Y - a.Y) / (b.X - a.X);
                        double yb = a.Y + (bis - a.X) * (b.Y - a.Y) / (b.X - a.X);
                        if ((ya - y) * (yb - y) <= 0
                            || Math.Min(Math.Abs(ya - y), Math.Abs(yb - y)) < grenze.Reserve - 1e-6)
                            return false;
                    }
                }
                return true;
            }
        }

        private static bool Parallel(Punkt a, Punkt b) =>
            Geometrie.Laenge(b - a) > 1e-6
            && Math.Abs(b.Y - a.Y) <= 0.01 * Geometrie.Laenge(b - a);
    }
}
