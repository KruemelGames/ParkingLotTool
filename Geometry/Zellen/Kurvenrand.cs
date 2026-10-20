using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry.Zellen
{
    /**
     * Plant Rechtecke, ihre Zufahrtsstreifen und die Fahrbahn gemeinsam.
     * Der Fahrbahnversatz entsteht aus den ganzen Rechtecken. Die Sehnen
     * bestimmen nicht mehr die Tiefe einer radial gedrehten Bucht.
     */
    internal sealed partial class Kurvenrandplan
    {
        // Unter einem Meter bildet der Keil keine erkennbare Gruenflaeche.
        // Diese Materialgrenze gilt vor dem Raster, nicht erst beim Export.
        internal const double GraskeilMindestbreite = 1.0;
        internal static bool IstGruenkeil(double drehung, double breite)
            => drehung > 0 && breite >= GraskeilMindestbreite;
        internal readonly bool[] Kanten;
        internal readonly List<Randbuchtplan> Buchten = new List<Randbuchtplan>();
        internal readonly List<Punkt[]> Anschluesse = new List<Punkt[]>();
        internal readonly List<Punkt[]> Graskeile = new List<Punkt[]>();
        internal readonly List<Punkt[]> Materialsektoren = new List<Punkt[]>();
        internal readonly List<(Punkt A, Punkt B)> Bodenstoesse = new List<(Punkt, Punkt)>();
        internal readonly List<KurvenRandreihe> Laternen = new List<KurvenRandreihe>();
        private readonly IReadOnlyList<Punkt> _areal;
        private readonly Zelleneinstellungen _s;
        private readonly Rahmen _rahmen;
        private readonly double[] _zugabe;
        private readonly bool[] _versatzKanten;
        private readonly List<(Randbuchtplan Bucht, bool AnschlussVorher, bool GrasVorher)> _eintraege
            = new List<(Randbuchtplan, bool, bool)>();
        private readonly Dictionary<(Punkt A, Punkt B), Linie> _linien = new Dictionary<(Punkt, Punkt), Linie>();
        internal double[] Zugaben => _zugabe;

        internal Kurvenrandplan(IReadOnlyList<Punkt> areal, Rahmen rahmen,
            Zelleneinstellungen s, IReadOnlyList<Zufahrtsvorgabe> zufahrten, Linienregister linien,
            double[] festeZugaben = null, int planlauf = 0)
        {
            _areal = areal; _s = s; _rahmen = rahmen;
            Kanten = new bool[areal.Count]; _zugabe = new double[areal.Count];
            _versatzKanten = new bool[areal.Count];
            if (festeZugaben != null) Array.Copy(festeZugaben, _zugabe, _zugabe.Length);
            if (!s.Randstrassen) return;
            foreach (var lauf in s.Kurvenlaeufe)
            {
                var kontrollen = lauf.Kontrollpunkte.Select(p => rahmen.NachLokal(new Punkt(p.x, p.z))).ToArray();
                var spur = new Kurvenrandspur(kontrollen);
                var kanten = Enumerable.Range(0, lauf.Kanten).Select(i => (lauf.Von + i) % areal.Count).ToArray();
                foreach (int k in kanten) { Kanten[k] = true; _versatzKanten[k] = true; }
                // Der Fahrbahnversatz muss am tangentialen Uebergang in die
                // Nachbargerade weiterlaufen; sonst schneidet deren Achse
                // die letzte radiale Bucht, unabhaengig vom Kurvenversatz.
                foreach (int ende in new[] { 0, 1 })
                {
                    int k = ende == 0 ? kanten[0] : kanten[kanten.Length - 1];
                    int schritt = ende == 0 ? -1 : 1;
                    for (int i = 0; i < areal.Count; i++)
                    {
                        int nachbar = Geometrie.Mod(k + schritt, areal.Count);
                        var a = areal[(k + 1) % areal.Count] - areal[k];
                        var b = areal[(nachbar + 1) % areal.Count] - areal[nachbar];
                        if (Geometrie.Skalar(a, b) / (Geometrie.Laenge(a) * Geometrie.Laenge(b))
                            < Math.Cos(15 * Math.PI / 180)) break;
                        _versatzKanten[nachbar] = true;
                        k = nachbar;
                    }
                }
                var kantenstationen = kanten.ToDictionary(k => k,
                    k => spur.NaechsteStation((areal[k] + areal[(k + 1) % areal.Count]) * 0.5));
                var nominaleAchse = Layoutplanung.Innenrand(areal, s.Randstrassenmittellinientiefe,
                    festeZugaben?.Select(z => s.Randstrassenmittellinientiefe + z).ToArray());
                bool ImLauf(Punkt[] ecken)
                {
                    if (!ImAreal(ecken)) return false;
                    if (festeZugaben == null) return true;
                    var links = ecken[3] - ecken[0]; links *= 1 / Geometrie.Laenge(links);
                    var rechts = ecken[2] - ecken[1]; rechts *= 1 / Geometrie.Laenge(rechts);
                    return !double.IsPositiveInfinity(Achstreffer(nominaleAchse, ecken[2], rechts))
                        && !double.IsPositiveInfinity(Achstreffer(nominaleAchse, ecken[3], links));
                }
                var roh = new List<(double Station, Punkt[] Ecken)>();
                double station = s.Buchtbreite / 2;
                // Wie an der Geraden begrenzen die beiden Gehrungen den
                // Lauf. Der Bogen wird einmal gepackt, nicht je Sehne.
                while (station < spur.Laenge && !ImLauf(Rechteck(spur, station)))
                    station += s.Buchtbreite * 0.25;
                Punkt[] vorher = null;
                double vorigeStation = 0;
                while (station <= spur.Laenge - s.Buchtbreite / 2)
                {
                    bool geradeFolge = vorher != null && spur.Geradenabschnitt(vorigeStation) >= 0
                        && spur.Geradenabschnitt(vorigeStation + s.Buchtbreite) == spur.Geradenabschnitt(vorigeStation);
                    if (geradeFolge) station = vorigeStation + s.Buchtbreite;
                    var ecken = Rechteck(spur, station);
                    if (geradeFolge)
                    {
                        // Dieselbe native Kontaktkante, keine zwei separat
                        // gerundeten Versionen einer gemeinsamen Geraden.
                        ecken[0] = vorher[1]; ecken[3] = vorher[2];
                    }
                    if (vorher != null && Kurvenrandspur.Ueberlappt(vorher, ecken))
                    {
                        double lo = station, hi = station + s.Buchtbreite * 8;
                        if (hi > spur.Laenge) hi = spur.Laenge;
                        if (Kurvenrandspur.Ueberlappt(vorher, Rechteck(spur, hi))) break;
                        for (int i = 0; i < 40; i++)
                        {
                            double mitte = (lo + hi) / 2;
                            if (Kurvenrandspur.Ueberlappt(vorher, Rechteck(spur, mitte))) lo = mitte;
                            else hi = mitte;
                        }
                        station = hi;
                        ecken = Rechteck(spur, station);
                    }
                    if (station > spur.Laenge - s.Buchtbreite / 2) break;
                    if (vorher != null
                        && Geometrie.Laenge(ecken[0] - vorher[1]) <= ParkingGeometry.ZellenEingangsgitter
                        && Geometrie.Laenge(ecken[3] - vorher[2]) <= ParkingGeometry.ZellenEingangsgitter)
                    {
                        // Innerhalb eines Eingabegitterfelds ist es derselbe
                        // Kontakt. Beide Buchten benutzen seine nativen Ecken,
                        // bevor Raster und Materialstuecke entstehen.
                        ecken[0] = vorher[1]; ecken[3] = vorher[2];
                    }
                    if (!ImLauf(ecken)) break;
                    if (ImAreal(ecken)) roh.Add((station, ecken));
                    vorher = ecken; vorigeStation = station;
                    // Die folgende Suche startet vor der naechsten Beruehrung.
                    station += s.Buchtbreite * 0.25;
                    if (roh.Count > 10000) throw new InvalidOperationException("Kurvenreihe ueberschreitet das Buchtenbudget.");
                }

                if (roh.Count == 0)
                    throw new InvalidOperationException("In den Kurvenlauf passt keine ganze Randbucht mit Fahrbahn.");
                // Stuetzhuelle der Fronten, vor Raster und Netz: ein gemeinsamer
                // Tiefenzuschlag fuer den Lauf vermeidet Stufen an Sehnennahtstellen.
                double zugabe = 0;
                foreach (var b in roh)
                    foreach (int k in kanten)
                    {
                        var a = areal[k]; var d = areal[(k + 1) % areal.Count] - a;
                        double l = Geometrie.Laenge(d); var u = d * (1 / l);
                        if (Math.Abs(b.Station - kantenstationen[k]) > l / 2 + s.Buchtbreite) continue;
                        var n = new Punkt(-u.Y, u.X);
                        foreach (var p in b.Ecken)
                        {
                            double t = Geometrie.Skalar(p - a, u);
                            if (t < -s.Buchtbreite || t > l + s.Buchtbreite) continue;
                            zugabe = Math.Max(zugabe, Geometrie.Skalar(p - a, n) - s.Zufahrtstiefe);
                        }
                    }
                if (festeZugaben == null)
                    for (int k = 0; k < _zugabe.Length; k++)
                        if (_versatzKanten[k]) _zugabe[k] = Math.Max(_zugabe[k], zugabe);

                var sperren = new List<double2>();
                foreach (var z in zufahrten)
                {
                    if (!kanten.Contains(z.Kante)) continue;
                    var a = areal[z.Kante]; var d = areal[(z.Kante + 1) % areal.Count] - a;
                    var t = d * (1 / Geometrie.Laenge(d));
                    var n = new Punkt(-t.Y, t.X);
                    var achse = z.AchsrichtungLokal ?? n;
                    double projektion = Geometrie.Skalar(achse, n) / Geometrie.Laenge(achse);
                    if (projektion <= 0.3) continue;
                    double mitte = spur.NaechsteStation(a + t * z.Along);
                    double halb = z.Breite / (2 * projektion)
                        + (z.Art == Zufahrtsart.Fussweg ? 0 : s.Buchtbreite);
                    sperren.Add(new double2(mitte - halb, mitte + halb));
                }
                int anfang = Buchten.Count;
                (double Station, Punkt[] Ecken)? letzter = null;
                foreach (var b in roh)
                {
                    if (sperren.Any(v => b.Station + s.Buchtbreite / 2 > v.x
                        && b.Station - s.Buchtbreite / 2 < v.y)) { letzter = null; continue; }
                    if (Layoutplanung.BeruehrtBauland(b.Ecken, s.Zoningflaechen)) continue;
                    var plan = new Randbuchtplan { Randkante = lauf.Von, Ecken = b.Ecken };
                    bool anschluss = letzter.HasValue && b.Station - letzter.Value.Station < s.Buchtbreite * 4;
                    bool gras = anschluss && IstGruenkeil(
                        Geometrie.Kreuz(letzter.Value.Ecken[3] - letzter.Value.Ecken[0], b.Ecken[3] - b.Ecken[0]),
                        Geometrie.Laenge(letzter.Value.Ecken[1] - b.Ecken[0]));
                    _eintraege.Add((plan, anschluss, gras));
                    letzter = b;
                    Buchten.Add(plan);
                }
                // Standlinie hinter der gesamten Reihe, unabhaengig von den
                // Sehnen und den Winkelschluesseln der geraden Innenreihen.
                int proben = Math.Max(2, (int)Math.Ceiling(spur.Laenge / 0.25));
                var linie = new float2[proben + 1]; var richtungen = new float2[proben + 1];
                var stationen = new double[proben + 1];
                for (int i = 0; i <= proben; i++)
                {
                    var ort = spur.Bei(spur.Laenge * i / proben);
                    var n = new Punkt(-ort.Tangente.Y, ort.Tangente.X);
                    var p = rahmen.NachWelt(ort.Punkt + n * (s.Randabstand / 2));
                    var v = rahmen.NachWelt(ort.Punkt + n) - rahmen.NachWelt(ort.Punkt);
                    linie[i] = new float2((float)p.X, (float)p.Y);
                    richtungen[i] = new float2((float)v.X, (float)v.Y);
                    if (i > 0) stationen[i] = stationen[i - 1] + math.distance(linie[i - 1], linie[i]);
                }
                double AufStandlinie(double sBogen)
                {
                    double index = Math.Max(0, Math.Min(proben, sBogen / spur.Laenge * proben));
                    int k = Math.Min(proben - 1, (int)index);
                    return stationen[k] + (stationen[k + 1] - stationen[k]) * (index - k);
                }
                Laternen.Add(new KurvenRandreihe
                {
                    Buchten = Enumerable.Range(anfang, Buchten.Count - anfang).ToArray(),
                    Standlinie = linie, Innenrichtungen = richtungen, Stationen = stationen,
                    Sperren = sperren.Select(v => new double2(AufStandlinie(v.x), AufStandlinie(v.y))).ToArray(),
                });
            }
            PlaneFahrbahnabstand();
            // Erst die gemeinsame Fahrbahn, dann das endgueltige Raster
            // zwischen ihren Gehrungen. Noch keine Zelle und kein Material
            // existiert; der endliche Plan laeuft nicht als Reparaturpass.
            if (festeZugaben == null || !_zugabe.SequenceEqual(festeZugaben))
            {
                if (planlauf >= 4)
                    throw new InvalidOperationException("Kurvenreihe und Fahrbahn haben keinen gemeinsamen Plan.");
                var fertig = new Kurvenrandplan(areal, rahmen, s, zufahrten, linien, _zugabe.ToArray(), planlauf + 1);
                Buchten = fertig.Buchten; Anschluesse = fertig.Anschluesse; Laternen = fertig.Laternen;
                Materialsektoren = fertig.Materialsektoren; Bodenstoesse = fertig.Bodenstoesse; Graskeile = fertig.Graskeile;
                _zugabe = fertig._zugabe; _linien = fertig._linien;
                return;
            }
            PlaneAnschluesse(linien);
        }

        private void PlaneAnschluesse(Linienregister linien)
        {
            if (Buchten.Count == 0) return;
            var achse = Innenrand(_s.Randstrassenmittellinientiefe,
                d => Layoutplanung.Innenrand(_areal, d));
            var achspunkte = Enumerable.Range(0, achse.Count)
                .Select(i => new List<Punkt> { achse[i], achse[(i + 1) % achse.Count] }).ToArray();
            (Punkt P, int Kante, double Along) BisAchse(Punkt p, Punkt n)
            {
                double naechster = Achstreffer(achse, p, n, out int kante);
                if (double.IsPositiveInfinity(naechster))
                    throw new InvalidOperationException("Kurvenbucht erreicht die geplante Fahrbahn nicht.");
                var treffer = p + n * naechster;
                achspunkte[kante].Add(treffer);
                var v = achse[(kante + 1) % achse.Count] - achse[kante];
                return (treffer, kante, Geometrie.Skalar(treffer - achse[kante], v) / Geometrie.Skalar(v, v));
            }
            var ruecken = new List<Punkt>();
            (Punkt P, int Kante, double Along) anfang = default, ende = default;
            void BandAbschliessen()
            {
                if (ruecken.Count == 0) return;
                // Ein durchgehender Aussenrand und die echte Achskontur,
                // keine Stosslinie und keine Anschlussflaeche je Bucht.
                var ring = new List<Punkt>(ruecken) { ende.P };
                int kanten = (ende.Kante - anfang.Kante + achse.Count) % achse.Count;
                if (kanten == 0 && ende.Along < anfang.Along) kanten = achse.Count;
                for (int i = kanten; i > 0; i--) ring.Add(achse[(anfang.Kante + i) % achse.Count]);
                ring.Add(anfang.P);
                Anschluesse.Add(ring.ToArray());
                ruecken.Clear();
            }
            Punkt[] vorher = null;
            foreach (var eintrag in _eintraege)
            {
                var e = eintrag.Bucht.Ecken;
                var nl = e[3] - e[0]; nl *= 1 / Geometrie.Laenge(nl);
                var nr = e[2] - e[1]; nr *= 1 / Geometrie.Laenge(nr);
                var links = BisAchse(e[3], nl); var rechts = BisAchse(e[2], nr);
                MerkeGerade(new[] { e[0], e[3], links.P }, linien);
                MerkeGerade(new[] { e[1], e[2], rechts.P }, linien);
                MerkeGerade(new[] { e[0], e[1] }, linien);
                MerkeGerade(new[] { e[2], e[3] }, linien);
                if (!eintrag.AnschlussVorher) BandAbschliessen();
                if (ruecken.Count == 0) anfang = links;
                if (ruecken.Count == 0 || !ruecken[ruecken.Count - 1].Equals(e[0])) ruecken.Add(e[0]);
                ruecken.Add(e[1]); ende = rechts;
                if (eintrag.GrasVorher && vorher != null)
                {
                    var a = vorher[0]; var d = vorher[1] - a;
                    var b = e[0]; var v = e[3] - b;
                    double nenner = Geometrie.Kreuz(d, v);
                    if (nenner != 0)
                    {
                        var spitze = a + d * (Geometrie.Kreuz(b - a, v) / nenner);
                        Graskeile.Add(new[] { a, b, spitze });
                        MerkeGerade(new[] { a, vorher[1], spitze }, linien);
                        MerkeGerade(new[] { b, e[3], spitze }, linien);
                    }
                }
                vorher = new[] { e[1], e[2] };
            }
            BandAbschliessen();
            foreach (var punkte in achspunkte)
                if (Geometrie.Laenge(punkte[1] - punkte[0]) > 0)
                    MerkeGerade(punkte.Distinct().ToArray(), linien);
        }

        private static double Achstreffer(IReadOnlyList<Punkt> achse, Punkt p, Punkt n)
            => Achstreffer(achse, p, n, out _);

        private static double Achstreffer(IReadOnlyList<Punkt> achse, Punkt p, Punkt n, out int kante)
        {
            kante = -1;
            double naechster = double.PositiveInfinity;
            for (int i = 0; i < achse.Count; i++)
            {
                var a = achse[i]; var d = achse[(i + 1) % achse.Count] - a;
                double nenner = Geometrie.Kreuz(n, d);
                if (nenner == 0) continue;
                double t = Geometrie.Kreuz(a - p, d) / nenner;
                double u = Geometrie.Kreuz(a - p, n) / nenner;
                if (t >= 0 && u >= 0 && u <= 1 && t < naechster)
                {
                    naechster = t;
                    kante = i;
                }
            }
            return naechster;
        }

        private void PlaneFahrbahnabstand()
        {
            if (Buchten.Count == 0) return;
            var grund = _zugabe.ToArray();
            bool Passt(double zusatz)
            {
                for (int i = 0; i < _zugabe.Length; i++)
                    if (_versatzKanten[i]) _zugabe[i] = grund[i] + zusatz;
                var achse = Innenrand(_s.Randstrassenmittellinientiefe,
                    d => Layoutplanung.Innenrand(_areal, d));
                foreach (var bucht in Buchten)
                    for (int i = 0; i < achse.Count; i++)
                    {
                        var a = achse[i]; var b = achse[(i + 1) % achse.Count];
                        for (int k = 0; k < 4; k++)
                            if (Segmentabstand(a, b, bucht.Ecken[k], bucht.Ecken[(k + 1) % 4])
                                < _s.Fahrgassenbreite / 2 - 1e-7)
                                return false;
                    }
                return true;
            }
            if (Passt(0)) return;
            double lo = 0, hi = _s.Buchtbreite;
            if (!Passt(hi)) throw new InvalidOperationException("Kurvenreihe laesst keine durchgehende Fahrbahn frei.");
            // Die Fahrbahnachse ist eine Sehnenkette. Ihre volle Breite wird
            // gegen jede Rechteckkante konstruiert, nicht nur gegen die Frontmitte.
            for (int i = 0; i < 32; i++)
            {
                double mitte = (lo + hi) / 2;
                if (Passt(mitte)) hi = mitte; else lo = mitte;
            }
            Passt(hi);
        }

        private static double Segmentabstand(Punkt a, Punkt b, Punkt c, Punkt d)
        {
            double ax = Geometrie.Kreuz(b - a, c - a), bx = Geometrie.Kreuz(b - a, d - a);
            double cx = Geometrie.Kreuz(d - c, a - c), dx = Geometrie.Kreuz(d - c, b - c);
            if (ax * bx < 0 && cx * dx < 0) return 0;
            double Abstand(Punkt p, Punkt von, Punkt bis)
            {
                var v = bis - von; double q = Geometrie.Skalar(v, v);
                double t = q == 0 ? 0 : Math.Max(0, Math.Min(1, Geometrie.Skalar(p - von, v) / q));
                return Geometrie.Laenge(p - von - v * t);
            }
            return Math.Min(Math.Min(Abstand(a, c, d), Abstand(b, c, d)),
                Math.Min(Abstand(c, a, b), Abstand(d, a, b)));
        }

        private void MerkeGerade(Punkt[] punkte, Linienregister register)
        {
            Linie linie = null;
            for (int i = 0; i < punkte.Length && linie == null; i++)
                for (int j = i + 1; j < punkte.Length && linie == null; j++)
                    _linien.TryGetValue((punkte[i], punkte[j]), out linie);
            if (linie == null)
                linie = register.FreieGerade(punkte[0], punkte[1], Linienart.Randbuchtgrenze, "Kurvenband");
            for (int i = 0; i < punkte.Length; i++)
                for (int j = i + 1; j < punkte.Length; j++)
                {
                    _linien[(punkte[i], punkte[j])] = linie;
                    _linien[(punkte[j], punkte[i])] = linie;
                }
        }

        internal Linie Grenze(Punkt a, Punkt b, Linienregister register)
        {
            if (_linien.TryGetValue((a, b), out var linie)) return linie;
            MerkeGerade(new[] { a, b }, register);
            return _linien[(a, b)];
        }

        private Punkt[] Rechteck(Kurvenrandspur spur, double station)
        {
            var ort = spur.Bei(station); var u = ort.Tangente;
            var n = new Punkt(-u.Y, u.X); var q = u * (_s.Buchtbreite / 2);
            double tiefe = _s.Randabstand;
            double kappa = spur.Kruemmung(station);
            if (kappa > 0)
            {
                double radius = 1 / kappa;
                double r = radius - _s.Randabstand;
                double halb = _s.Buchtbreite / 2;
                if (r <= halb) throw new InvalidOperationException("Kurve ist fuer eine ganze Randbucht zu eng.");
                tiefe = radius - Math.Sqrt(r * r - halb * halb);
            }
            var hinten = ort.Punkt + n * tiefe;
            return new[] { hinten - q, hinten + q,
                hinten + q + n * _s.Buchttiefe, hinten - q + n * _s.Buchttiefe }
                // CS2 speichert Welt-float, keine lokalen doubles. Kontakte
                // muessen VOR der Zellteilung in denselben Koordinaten liegen:
                // Lot201905 verlor sonst 9,9 m2 beim spaeten float-Uebergang.
                .Select(p =>
                {
                    var welt = _rahmen.NachWelt(p);
                    return _rahmen.NachLokal(new Punkt((float)welt.X, (float)welt.Y));
                }).ToArray();
        }

        private bool ImAreal(Punkt[] ecken)
        {
            if (ecken.Any(p => !Geometrie.EnthaeltOderRand(_areal, p))) return false;
            for (int i = 0; i < ecken.Length; i++)
            {
                var a = ecken[i]; var d = ecken[(i + 1) % ecken.Length] - a;
                var stellen = new List<double> { 0, 1 };
                for (int k = 0; k < _areal.Count; k++)
                {
                    var p = _areal[k]; var v = _areal[(k + 1) % _areal.Count] - p;
                    double nenner = Geometrie.Kreuz(d, v);
                    if (nenner == 0) continue;
                    double t = Geometrie.Kreuz(p - a, v) / nenner;
                    double u = Geometrie.Kreuz(p - a, d) / nenner;
                    if (t > 0 && t < 1 && u >= 0 && u <= 1) stellen.Add(t);
                }
                stellen.Sort();
                for (int k = 1; k < stellen.Count; k++)
                    if (!Geometrie.EnthaeltOderRand(_areal, a + d * ((stellen[k - 1] + stellen[k]) / 2))) return false;
            }
            return true;
        }

        internal IReadOnlyList<Punkt> Innenrand(double tiefe, Func<double, IReadOnlyList<Punkt>> bisher)
        {
            if (tiefe < _s.Zufahrtstiefe || _zugabe.All(z => z == 0)) return bisher(tiefe);
            var abstaende = _zugabe.Select(z => tiefe + z).ToArray();
            return Layoutplanung.Innenrand(_areal, tiefe, abstaende);
        }
    }
}
