using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingLotTool.Geometry.Zellen
{
    internal sealed partial class Ringlosplan
    {
        // Vor Registrierung der Rasterlinien: Der sichtbare Gassenkorridor
        // ersetzt den Endweg. Im 150x120-m-edge-Fall kreuzte ein Endweg zwei
        // Zufahrten und erzeugte daraus 4 statt 2 Gassenkurse.
        private void PlaneEndwegeNebenZufahrtsgassen()
        {
            var gassen = Zufahrten.Where(z => Zufahrtsarten.IstGasse(z.Art)).ToArray();
            if (gassen.Length == 0) return;
            var neben = new List<Weg>();
            foreach (var fuss in Fusswege)
            {
                var intervalle = new List<(double A, double B)> { (0, 1) };
                foreach (var gasse in gassen)
                {
                    var d = gasse.B - gasse.A;
                    var laenge = Geometrie.Laenge(d);
                    if (laenge < 1e-9) continue;
                    var u = d * (1 / laenge);
                    var n = new Punkt(-u.Y, u.X);
                    var v = fuss.B - fuss.A;
                    var weglaenge = Geometrie.Laenge(v);
                    if (weglaenge < 1e-9) continue;
                    var fn = new Punkt(-v.Y, v.X) * (fuss.Breite / (2 * weglaenge));
                    // Die ganze Fusswegbreite muss ausserhalb liegen, nicht
                    // nur ihre Achse. Die Projektion gilt auch schraeg.
                    var quer = gasse.Breite / 2 + Math.Abs(Geometrie.Skalar(fn, n));
                    var laengs = Math.Abs(Geometrie.Skalar(fn, u));
                    var a = fuss.A - gasse.A;
                    var lo = 0.0; var hi = 1.0;
                    bool Band(double start, double delta, double min, double max)
                    {
                        if (Math.Abs(delta) < 1e-9) return start > min + 1e-6 && start < max - 1e-6;
                        var t0 = (min - start) / delta; var t1 = (max - start) / delta;
                        lo = Math.Max(lo, Math.Min(t0, t1)); hi = Math.Min(hi, Math.Max(t0, t1));
                        return hi > lo + 1e-9;
                    }
                    if (!Band(Geometrie.Skalar(a, u), Geometrie.Skalar(v, u), -laengs, laenge + laengs)
                        || !Band(Geometrie.Skalar(a, n), Geometrie.Skalar(v, n), -quer, quer)) continue;
                    intervalle = intervalle.SelectMany(t => new[] {
                        (t.A, Math.Min(t.B, lo)), (Math.Max(t.A, hi), t.B) })
                        .Where(t => t.Item2 > t.Item1 + 1e-9).ToList();
                }
                if (intervalle.Count == 1 && intervalle[0].A == 0 && intervalle[0].B == 1)
                {
                    neben.Add(fuss);
                    continue;
                }
                /*
                 * DER GESCHNITTENE FUSSWEG BLEIBT ANGESCHLOSSEN.
                 *
                 * Bericht ALCU (2026-09-25): eine Gasse kreuzte den Endweg am
                 * rechten Rand. Er wurde herausgeschnitten - richtig, unter
                 * einer sichtbaren Strasse liegt kein Fussweg -, aber die
                 * Enden hingen danach an nichts, und unten blieb ein 0,83 m
                 * langer Rest. Nutzer: *"an sich soll schon gerne der Fussweg
                 * an den Eingang angeschlossen werden."*
                 *
                 * Jetzt: Reste kuerzer als eine Fusswegbreite fallen weg, und
                 * jedes abgeschnittene Ende bekommt eine Netzverbindung (ohne
                 * Belag) zum INNEREN Ende der Gasse. Dort teilt sie den
                 * Endpunkt mit Gasse und Fahrgasse - CS2 verbindet nur
                 * identische Endpunkte zu einem Knoten.
                 */
                var weglaengeGesamt = Geometrie.Laenge(fuss.B - fuss.A);
                foreach (var t in intervalle)
                {
                    var stueckA = fuss.A + (fuss.B - fuss.A) * t.A;
                    var stueckB = fuss.A + (fuss.B - fuss.A) * t.B;
                    if ((t.B - t.A) * weglaengeGesamt < fuss.Breite) continue;
                    neben.Add(new Weg { A = stueckA, B = stueckB, Breite = fuss.Breite,
                        Fuss = fuss.Fuss, Art = fuss.Art, Band = fuss.Band, Zufahrt = fuss.Zufahrt,
                        SchraegARechts = t.A == 0 ? fuss.SchraegARechts : 0,
                        SchraegALinks = t.A == 0 ? fuss.SchraegALinks : 0,
                        SchraegBRechts = t.B == 1 ? fuss.SchraegBRechts : 0,
                        SchraegBLinks = t.B == 1 ? fuss.SchraegBLinks : 0 });
                    if (t.A > 0) SchliesseAn(stueckA);
                    if (t.B < 1) SchliesseAn(stueckB);
                }
                void SchliesseAn(Punkt ende)
                {
                    // Die Gasse, deren Achse dem Schnittende am naechsten ist.
                    Weg naechste = null;
                    var bester = double.PositiveInfinity;
                    foreach (var gasse in gassen)
                    {
                        var abstand = Geometrie.AbstandPunktStrecke(ende, gasse.A, gasse.B);
                        if (abstand >= bester) continue;
                        bester = abstand;
                        naechste = gasse;
                    }
                    if (naechste == null
                        || Geometrie.Laenge(naechste.B - ende) < 1e-6) return;
                    Fussanschluesse.Add(new Weg { A = ende, B = naechste.B,
                        Breite = fuss.Breite, Fuss = true, Art = Zufahrtsart.Fussweg });
                }
            }
            Fusswege.Clear(); Fusswege.AddRange(neben);
        }
    }
}
