using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    /**
     * Fahrradkern, Neubau 02.11.2026 (ersetzt den Codex-Nachtlauf).
     *
     * Festes Raster im Lokalrahmen der Zufahrtskante: +X laeuft entlang der
     * Kante, +Y ins Grundstueck. Ohne Zufahrt gilt die laengste Kante, damit
     * die Vorschau sofort steht.
     *  - A/C: Sammelweg an der Zufahrtsseite, Gaenge senkrecht davon, Bausteine
     *    beidseitig. Reste: kleinerer Baustein (Unterstand -> Staender -> Buegel),
     *    dann Reihe direkt am Sammelweg, dann Gruen mit Baum.
     *  - D: Laufachse von der Zufahrt ins Grundstueck, Buegelreihen parallel
     *    zur Zufahrtskante, an schraegen Kanten treppenfoermig.
     * Alle Wege sind gleich (Nutzer 02.11.: ein Wegtyp, 3 m). Wege stehen
     * fest, BEVOR Objekte gesetzt werden; nichts wird nachtraeglich entfernt.
     * Alles haelt mindestens 2 cm Abstand zur Kante: Beruehrungen gegen
     * float-Weltkoordinaten waren die Bruchstelle des alten Kerns.
     */
    public static partial class FahrradGeometrie
    {
        private sealed class Weg
        {
            internal double2 A, B;
            internal double Breite;
            internal Fahrradwegart Art;
            internal bool Zufahrt;
            // Platzring: Pflaster ueber die Ecke hinaus, sonst bleibt aussen ein Grasdreieck.
            internal bool Kappe;
        }

        private const double Gang = 3;
        private const double MinRand = .02;
        // 5 mm Fuge zwischen Nachbarn: bei 8 km ist eine float-Stufe 0,5 mm,
        // beruehrende Grundrisse ueberlappen in Weltkoordinaten sonst messbar.
        private const double Luft = .005;

        private sealed class Baustein
        {
            internal Fahrradobjektart Art;
            internal string Prefab;
            internal double Laenge, Tiefe;
            internal int Plaetze;
        }

        // sonde-1.log: BicycleShelter 4,0 x 2,48 (8 Raeder), BikeRack03 Spur 1,5 m
        // (4 Raeder), CommercialBikeRack Spur 0,75 m (2 Raeder), Stellplatz 2 m tief.
        private static readonly Baustein Dach = new Baustein { Art = Fahrradobjektart.Unterstand,
            Prefab = "BicycleShelter01", Laenge = 4, Tiefe = 2.5, Plaetze = 8 };
        private static readonly Baustein Staender = new Baustein { Art = Fahrradobjektart.Staender,
            Prefab = "BikeRack03", Laenge = 1.5, Tiefe = 2, Plaetze = 4 };
        private static readonly Baustein Buegel = new Baustein { Art = Fahrradobjektart.Buegel,
            Prefab = "CommercialBikeRack01", Laenge = .8, Tiefe = 2, Plaetze = 2 };

        private sealed class Teil
        {
            internal Baustein B;
            internal double2[] Rect;
            internal double2 Zugang, ZumWeg;
        }

        private sealed class Variante
        {
            internal readonly List<Weg> Wege = new List<Weg>();
            internal readonly List<double2[]> Sperren = new List<double2[]>();
            internal readonly List<Teil> Teile = new List<Teil>();
            internal readonly List<string> Hinweise = new List<string>();
            /** Baumstandorte, die das Layout vorgibt (Mitte des Platzes). */
            internal readonly List<double2> Mitte = new List<double2>();
            /** Platz: Pflasterring zwischen zwei gleich ausgerichteten Vielecken. */
            internal double2[] RingAussen, RingInnen;
            internal int Plaetze => Teile.Sum(t => t.B.Plaetze);
        }

        public static FahrradPlan Baue(float2[] polygon, LayoutSettings settings,
            Func<float2[], bool> flaechenpruefer = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (!Enum.IsDefined(typeof(Fahrradlayout), settings.Fahrradlayout))
                throw new ArgumentOutOfRangeException(nameof(settings.Fahrradlayout));
            var stil = Enum.IsDefined(typeof(Fahrradstil), settings.Fahrradstil) ? settings.Fahrradstil : Fahrradstil.Schlicht;
            var plan = new FahrradPlan { Layout = settings.Fahrradlayout, Stil = stil };
            if (polygon == null || polygon.Length < 3) return plan;
            var flaeche = ParkingGeometry.SignedArea(polygon.Select(p => (double2)p).ToArray());
            if (Math.Abs(flaeche) < .01) return plan;
            // CS2s Flaechenknoten sind gegen den Uhrzeiger. Die Randdaten
            // behalten dagegen ihre Reihenfolge, damit Entrance.Edge stimmt.
            var belag = flaeche < 0 ? polygon.Reverse().ToArray() : polygon.ToArray();
            flaechenpruefer = flaechenpruefer ?? (ring => Cs2Triangulierung.Dreiecke(ring) == ring.Length - 2);
            if (!flaechenpruefer(belag))
            {
                plan.Hinweise.Add("Fahrrad-Belag: CS2 verwirft den Umriss; keine Anlage geplant.");
                return plan;
            }
            var gruen = Gruen(stil);
            var gesetzt = (settings.Entrances ?? Array.Empty<Entrance>())
                .Where(e => e != null && e.Edge >= 0 && e.Edge < polygon.Length).ToList();
            var hatZufahrt = gesetzt.Count > 0 || settings.AutomaticEntrances;
            var (raum, v, rahmen, aussen, gewuenscht, ohneRing) = Plane(polygon, settings, stil, gesetzt);
            var rand = aussen + rahmen;
            plan.Rahmen = rahmen;
            if (ohneRing)
                plan.Hinweise.Add("Fahrrad: fuer den Platzring ist die Flaeche zu klein; gebaut wird der gruene Rahmen.");
            if (gruen && rahmen < gewuenscht - 1e-9 && v.Teile.Count > 0)
                plan.Hinweise.Add($"Fahrrad: Rahmen auf {rahmen:0.##} m verschmaelert, damit die gewaehlte Abstellart passt.");
            plan.Hinweise.AddRange(v.Hinweise);
            // Zwei Flaechen, lueckenlos und ohne Ueberlappung wie beim Autoparkplatz:
            // Pflaster nur, wo es gebraucht wird, der Rest Gras.
            Func<double2, bool> aufPflaster = _ => true;
            if (gruen && Flaechenbau(raum, v, flaechenpruefer, out var pflaster, out var gras, out var pruefe, plan.Hinweise))
            {
                plan.Flaechen.AddRange(pflaster); plan.Gras.AddRange(gras); aufPflaster = pruefe;
            }
            else plan.Flaechen.Add(belag);
            if (!hatZufahrt) plan.Hinweise.Add("Fahrrad: Vorschau ohne Zufahrt; gebaut wird erst mit gesetzter Zufahrt.");

            foreach (var t in v.Teile) plan.Objekte.Add(Objekt(raum, t));
            Netz(raum, v.Wege, plan);
            Licht(raum, rand, aussen, rahmen, gruen && plan.Gras.Count > 0, v, plan, aufPflaster);
            if (plan.Objekte.Count == 0) plan.Hinweise.Add("Fahrrad: kein Stellplatz passt in diese Flaeche.");
            return plan;
        }

        /**
         * Die Anlage ohne Flaechen und Schmuck: Ausrichtung, Rahmenbreite, Bausteine
         * und Wege. Bau und Zufahrtspruefung rechnen damit dieselbe Anlage.
         */
        private static (Raum Raum, Variante V, double Rahmen, double Aussen, double Gewuenscht, bool OhneRing)
            Plane(float2[] polygon, LayoutSettings settings, Fahrradstil stil, List<Entrance> gesetzt)
        {
            var layout = settings.Fahrradlayout;
            var gruen = Gruen(stil);
            // Im Gruen IST der Randabstand der gruene Rahmen, wie der Randstreifen am
            // Autoparkplatz (Nutzer 03.11.: "sollte wie am echten Parkplatz einstellbar sein").
            var gewuenscht = gruen ? Math.Max(0, settings.Es) : 0;
            var aussen = gruen ? MinRand : Math.Max(MinRand, settings.Es);
            // Die Lage kommt aus der Form und dem Reihenwinkel, nie aus der Zufahrt
            // (Nutzer 03.11.: "wenn ich die Einfahrt setze, wird das wieder verworfen ...
            // man kann nicht planen, wie es am Ende aussieht"). Gesetzte Zufahrten
            // schliessen nur an den naechsten Weg an, wie beim Autoparkplatz.
            var raum = Ausrichtung(polygon, settings, out var zugang, out var kanteY);
            var weitere = gesetzt.Select(e => Zufahrtspunkt(raum, polygon, e)).ToList();
            // Ohne gesetzte Zufahrt baut die Automatik den Zugang mittig an der Bezugsseite.
            var mittelzugang = gesetzt.Count == 0 && settings.AutomaticEntrances;
            // Schlicht stopft Reste mit kleineren Bausteinen; im Gruen wird der Rest gruen (Nutzer 03.11.).
            var bausteine = layout == Fahrradlayout.D ? new[] { Buegel }
                : layout == Fahrradlayout.C ? (gruen ? new[] { Dach } : new[] { Dach, Staender, Buegel })
                : gruen ? new[] { Staender } : new[] { Staender, Buegel };
            // Gruener Rahmen: das Feld beginnt hinter dem Rand; offen bleibt er nur, wo eine Zufahrt kreuzt.
            // Die Wahl gilt, die Optik gibt nach (Nutzer 03.11.: "wenn ich Unterstand auswaehle,
            // sollte da auch ein Unterstand sein"): passt mit dem eingestellten Rahmen nichts, wird
            // er schmaler, notfalls entfaellt er. Beim Platz gilt dasselbe fuer den Ring.
            var versuche = new List<(double Breite, Variante V)>();
            foreach (var breite in (gruen ? new[] { gewuenscht, gewuenscht / 2, 0 } : new[] { 0.0 }).Distinct())
            {
                var r = aussen + breite;
                var w = stil == Fahrradstil.Platz ? Ring(raum, zugang, r, mittelzugang, bausteine[0], weitere) : null;
                w ??= layout == Fahrradlayout.D
                    ? Buegelplatz(raum, zugang, kanteY, r, mittelzugang, weitere)
                    : BesteReihen(raum, zugang, kanteY, r, mittelzugang, bausteine, weitere, gruen);
                versuche.Add((breite, w));
                if (w.Mitte.Count > 0 || (stil != Fahrradstil.Platz && w.Teile.Count > 0)) break;
            }
            var ringe = versuche.Where(x => x.V.Mitte.Count > 0).ToList();
            var gesteht = versuche.Where(x => x.V.Teile.Count > 0).ToList();
            var (rahmen, v) = ringe.Count > 0 ? ringe[0] : gesteht.Count > 0 ? gesteht[0] : versuche[0];
            return (raum, v, rahmen, aussen, gewuenscht, stil == Fahrradstil.Platz && ringe.Count == 0);
        }

        /**
         * Reihenwinkel wie beim Autoparkplatz (ParkingGeometry.Reihenwinkel): Bezug ist
         * die gewaehlte Linie, sonst die laengste Kante; Quer +90, Fest + Regler. Ohne
         * Linie und auf "Kante" bleibt es beim bisherigen Rahmen an der laengsten
         * Kante samt Bogenkette. Die Bezugsseite (Sammelweg, Mittelzugang) liegt
         * auf der Seite der laengsten Kante.
         */
        private static Raum Ausrichtung(float2[] polygon, LayoutSettings settings, out double2 zugang, out double kanteY)
        {
            var kante = string.IsNullOrEmpty(settings.AngleMode) || settings.AngleMode == Winkelmodus.Kante;
            if (kante && !settings.Ausrichtwinkel.HasValue && Math.Abs(settings.KantenVersatz) < 1e-9)
                return Rahmen(polygon, null, out zugang, out kanteY);
            var ring = polygon.Select(q => (double2)q).ToArray();
            var winkel = ParkingGeometry.Reihenwinkel(settings, ParkingGeometry.LaengsteKante(ring)) * Math.PI / 180;
            var raum = new Raum(polygon, winkel);
            // Die laengste Kante liegt unten (kleines y): dort ist die Bezugsseite.
            var l = 0;
            for (var i = 1; i < polygon.Length; i++)
                if (math.distance(polygon[i], polygon[(i + 1) % polygon.Length])
                    > math.distance(polygon[l], polygon[(l + 1) % polygon.Length]) + .001f) l = i;
            var mitte = raum.Lokal((polygon[l] + polygon[(l + 1) % polygon.Length]) / 2);
            if (mitte.y > (raum.MinY + raum.MaxY) / 2)
            {
                raum = new Raum(polygon, winkel + Math.PI);
                mitte = raum.Lokal((polygon[l] + polygon[(l + 1) % polygon.Length]) / 2);
            }
            // Bezugslinie: die erste Hoehe, auf der die Form schon breit ist (60 % der groessten
            // Breite). Schraeg gedreht ist die Unterseite sonst nur eine Ecke, und dort hat kein
            // Sammelweg Platz; das Dreieck darunter wird Rest. Der Mittelzugang liegt darunter.
            double Breite(double y, out double mitteX)
            {
                mitteX = 0; var beste = 0.0;
                foreach (var b in raum.Baender(y + Gang / 2, Gang, false))
                    if (b.Bis - b.Von > beste) { beste = b.Bis - b.Von; mitteX = (b.Von + b.Bis) / 2; }
                return beste;
            }
            var groesste = 0.0;
            for (var y = raum.MinY; y < raum.MaxY - Gang; y += .5) groesste = Math.Max(groesste, Breite(y, out _));
            var bezug = raum.MinY; var x = mitte.x;
            for (var y = raum.MinY; y < raum.MaxY - Gang; y += .5)
                if (Breite(y, out var mx) >= .6 * groesste - 1e-6) { bezug = y; x = mx; break; }
            var hoehe = bezug + Gang / 2;
            // Ab hier passt der volle 3-m-Zugang ins Grundstueck (schraege Unterkante).
            var unten = raum.Baender(x, Gang, true).Where(b => b.Von <= hoehe && b.Bis >= hoehe).ToList();
            zugang = new double2(x, unten.Count > 0 ? unten[0].Von : bezug);
            kanteY = bezug;
            return raum;
        }

        /**
         * Lokalrahmen an der Zufahrtskante (sonst der laengsten Kante). Liefert
         * den Zufahrtspunkt lokal; seine y-Koordinate ist die Kantenlinie.
         */
        private static Raum Rahmen(float2[] polygon, Entrance zufahrt, out double2 punkt, out double kanteY)
        {
            int kante; double entlang;
            if (zufahrt != null) { kante = zufahrt.Edge; entlang = zufahrt.Along; }
            else
            {
                kante = 0;
                for (var i = 1; i < polygon.Length; i++)
                    if (math.distance(polygon[i], polygon[(i + 1) % polygon.Length])
                        > math.distance(polygon[kante], polygon[(kante + 1) % polygon.Length]) + .001f) kante = i;
                entlang = math.distance(polygon[kante], polygon[(kante + 1) % polygon.Length]) / 2;
            }
            var n = polygon.Length;
            var a = (double2)polygon[kante];
            var b = (double2)polygon[(kante + 1) % n];
            var laenge = math.length(b - a);
            // Bogenkante: Nachbarkanten bis 40 Grad Abweichung gehoeren dazu; der
            // Rahmen folgt der Sehne, nicht dem kurzen Stueck unter der Zufahrt.
            var richtung = laenge < 1e-9 ? new double2(1, 0) : (b - a) / laenge;
            bool Gleich(int i)
            {
                var d = (double2)polygon[(i + 1) % n] - (double2)polygon[i];
                var l = math.length(d);
                return l > 1e-9 && math.dot(d / l, richtung) > Math.Cos(40 * Math.PI / 180);
            }
            var erste = kante; var letzte = kante;
            for (var i = 1; i < n - 1 && Gleich((kante - i + n) % n); i++) erste = (kante - i + n) % n;
            for (var i = 1; i < n - 1 && Gleich((kante + i) % n) && (kante + i) % n != erste; i++) letzte = (kante + i) % n;
            var sa = (double2)polygon[erste]; var sb = (double2)polygon[(letzte + 1) % n];
            var winkel = math.lengthsq(sb - sa) < 1e-12 ? Math.Atan2(richtung.y, richtung.x) : Math.Atan2(sb.y - sa.y, sb.x - sa.x);
            var raum = new Raum(polygon, winkel);
            var mitte = (raum.Lokal((float2)a) + raum.Lokal((float2)b)) / 2;
            if (!raum.Innen(mitte + new double2(0, .05)))
                raum = new Raum(polygon, winkel + Math.PI);
            var la = raum.Lokal((float2)a); var lb = raum.Lokal((float2)b);
            punkt = math.lerp(la, lb, laenge < 1e-9 ? .5 : math.clamp(entlang / laenge, 0, 1));
            // Innerste Ecke der Kette ist die Bezugslinie fuer Sammelweg und Reihen.
            kanteY = punkt.y;
            for (var i = erste; ; i = (i + 1) % n)
            {
                kanteY = Math.Max(kanteY, raum.Lokal(polygon[i]).y);
                if (i == (letzte + 1) % n) break;
            }
            return raum;
        }

        private static bool Frei(Raum raum, Variante v, double2[] rect, double rand)
        {
            foreach (var s in v.Sperren) if (Schnell(rect, s)) return false;
            foreach (var t in v.Teile) if (Schnell(rect, t.Rect)) return false;
            return raum.Passt(rect, rand);
        }

        /**
         * Ueberlappung mit Huellkasten vorab. Lokal sind fast alle Rechtecke
         * achsparallel; dann ist der Kastentest schon exakt (Beruehrung frei).
         */
        private static bool Schnell(double2[] a, double2[] b)
        {
            Kasten(a, out var ax0, out var ay0, out var ax1, out var ay1, out var aGerade);
            Kasten(b, out var bx0, out var by0, out var bx1, out var by1, out var bGerade);
            if (ax1 <= bx0 + Epsilon || bx1 <= ax0 + Epsilon || ay1 <= by0 + Epsilon || by1 <= ay0 + Epsilon) return false;
            return (aGerade && bGerade) || Ueberlappt(a, b);
        }

        private static void Kasten(double2[] r, out double x0, out double y0, out double x1, out double y1, out bool gerade)
        {
            x0 = y0 = double.MaxValue; x1 = y1 = double.MinValue;
            foreach (var p in r) { x0 = Math.Min(x0, p.x); y0 = Math.Min(y0, p.y); x1 = Math.Max(x1, p.x); y1 = Math.Max(y1, p.y); }
            gerade = r.Length == 4;
            for (var i = 0; gerade && i < 4; i++)
            {
                var d = r[(i + 1) % 4] - r[i];
                gerade = Math.Abs(d.x) < 1e-9 || Math.Abs(d.y) < 1e-9;
            }
        }

        private static (double Von, double Bis)? Intervall(List<(double Von, double Bis)> baender, double wert)
        {
            (double Von, double Bis)? best = null; var abstand = double.MaxValue;
            foreach (var b in baender)
            {
                var d = wert < b.Von ? b.Von - wert : wert > b.Bis ? wert - b.Bis : 0;
                if (d < abstand) { abstand = d; best = b; }
            }
            return best;
        }

        // ------------------------------------------------------------ A und C

        private static Variante BesteReihen(Raum raum, double2 zugang, double kanteY, double rand, bool hatZufahrt,
            Baustein[] bausteine, List<(double2 P, double2 N)> weitere, bool nurRaster = false)
        {
            var haupt = bausteine[0];
            var c0 = kanteY + rand; var c1 = c0 + Gang; var cm = (c0 + c1) / 2;
            var sammel = Intervall(raum.Baender(cm, Gang, false), zugang.x);
            if (sammel == null || sammel.Value.Bis - sammel.Value.Von - 2 * rand < Gang)
                return Randreihe(raum, zugang, kanteY, rand, hatZufahrt, bausteine, weitere);
            var k0 = sammel.Value.Von + rand; var k1 = sammel.Value.Bis - rand;
            var modul = 2 * haupt.Tiefe + Gang + 2 * Luft;
            Variante beste = null;
            for (var phase = 0.0; phase < modul - 1e-9; phase += .25)
            {
                var v = Reihen(raum, zugang, rand, hatZufahrt, bausteine, k0, k1, c0, c1, phase, weitere, nurRaster);
                if (beste == null || v.Plaetze > beste.Plaetze) beste = v;
            }
            // Im Gruen ist auch eine einzige durchgehende Reihe am Sammelweg regelmaessig;
            // in schmalen Feldern ist sie ruhiger und fasst mehr als Gangstummel.
            if (nurRaster)
            {
                var reihe = Reihen(raum, zugang, rand, hatZufahrt, bausteine, k0, k1, c0, c1, 0, weitere, nurRaster, ohneGaenge: true);
                if (reihe.Plaetze > beste.Plaetze) beste = reihe;
            }
            return beste;
        }

        private static Variante Reihen(Raum raum, double2 zugang, double rand, bool hatZufahrt,
            Baustein[] bausteine, double k0, double k1, double c0, double c1, double phase,
            List<(double2 P, double2 N)> weitere, bool nurRaster, bool ohneGaenge = false)
        {
            var v = new Variante();
            var haupt = bausteine[0];
            var cm = (c0 + c1) / 2;
            var zx = math.clamp(zugang.x, k0 + Gang / 2, k1 - Gang / 2);
            v.Sperren.Add(Rechteck(k0, c0, k1 - k0, Gang));
            if (hatZufahrt) v.Sperren.Add(Streifen(zugang, new double2(zx, cm), Gang));

            // Gaenge zuerst: sie sind Sperrflaechen, bevor ein Objekt steht.
            var gaenge = new List<(double X, double Ende)>();
            var gangSperren = new Dictionary<double, double2[]>();
            for (var xa = k0 + phase + haupt.Tiefe + Luft; !ohneGaenge && xa + Gang <= k1 + 1e-9; xa += 2 * haupt.Tiefe + Gang + 2 * Luft)
            {
                var band = raum.Baender(xa + Gang / 2, Gang, true)
                    .Where(b => b.Von <= c1 + 1e-6 && b.Bis > c1 + .8).ToList();
                if (band.Count == 0) continue;
                var ende = band[0].Bis - rand;
                gaenge.Add((xa, ende));
                var sperre = Rechteck(xa, c0, Gang, ende - c0);
                v.Sperren.Add(sperre); gangSperren[xa] = sperre;
            }

            // Weitere Zufahrten: gerade bis zum ersten Weg, ebenfalls VOR den Objekten.
            var linien = new List<(double2 A, double2 B)> { (new double2(k0, cm), new double2(k1, cm)) };
            foreach (var (xa, ende) in gaenge) linien.Add((new double2(xa + Gang / 2, cm), new double2(xa + Gang / 2, ende)));
            var anschluss = new Dictionary<double, double>();
            var sammelAnschluss = new List<double>();
            foreach (var (p, n) in weitere)
            {
                if (!Treffer(raum, p, n, rand, linien, out var welche, out var ziel))
                { v.Hinweise.Add("Fahrrad: eine weitere Zufahrt erreicht keinen Weg."); continue; }
                v.Sperren.Add(Streifen(p, ziel, Gang));
                v.Wege.Add(new Weg { A = p, B = ziel, Breite = Gang, Art = Fahrradwegart.Fahrradweg, Zufahrt = true });
                if (welche == 0) sammelAnschluss.Add(ziel.x);
                else
                {
                    var xa = gaenge[welche - 1].X;
                    anschluss[xa] = Math.Max(anschluss.TryGetValue(xa, out var bisher) ? bisher : c1, ziel.y);
                }
            }

            var genutzt = new List<double>();
            var weitesteJeGang = new Dictionary<double, double>();
            foreach (var (xa, ende) in gaenge)
            {
                var weiteste = double.NaN;
                foreach (var seite in new[] { -1, 1 })
                {
                    var y = c1 + Luft;
                    while (y < ende - .5)
                    {
                        var ok = false;
                        foreach (var b in bausteine)
                        {
                            if (y + b.Laenge > ende + 1e-9) continue;
                            var xr = seite < 0 ? xa - b.Tiefe - Luft : xa + Gang + Luft;
                            var rect = Rechteck(xr, y + Rest(b), b.Tiefe, b.Laenge - 2 * Rest(b));
                            if (!Frei(raum, v, rect, rand)) continue;
                            var zug = new double2(xa + Gang / 2, y + b.Laenge / 2);
                            v.Teile.Add(new Teil { B = b, Rect = rect, Zugang = zug, ZumWeg = new double2(-seite, 0) });
                            weiteste = double.IsNaN(weiteste) ? zug.y : Math.Max(weiteste, zug.y);
                            y += b.Laenge + Luft; ok = true; break;
                        }
                        if (!ok) y += .5;
                    }
                }
                if (anschluss.TryGetValue(xa, out var noetig))
                    weiteste = double.IsNaN(weiteste) ? noetig : Math.Max(weiteste, noetig);
                if (double.IsNaN(weiteste)) continue;
                weitesteJeGang[xa] = weiteste;
                genutzt.Add(xa + Gang / 2);
            }

            // Reste am Sammelweg: Reihe direkt an seiner Oberseite (kleine Lots).
            // Im Gruen nur, wenn sonst gar nichts steht: der Rest wird gruen.
            var reihe = new List<double>();
            var reiheErlaubt = !nurRaster || v.Teile.Count == 0;
            // Steht an keinem Gang etwas, sind seine Reservierungen leer: sonst
            // reissen sie unregelmaessige Luecken in die Reihe am Sammelweg.
            if (v.Teile.Count == 0)
                foreach (var paar in gangSperren)
                    if (!anschluss.ContainsKey(paar.Key)) v.Sperren.Remove(paar.Value);
            for (var x = k0; reiheErlaubt && x < k1 - .5;)
            {
                var ok = false;
                foreach (var b in bausteine)
                {
                    if (x + b.Laenge > k1 + 1e-9) continue;
                    var rect = Rechteck(x + Rest(b), c1 + Luft, b.Laenge - 2 * Rest(b), b.Tiefe);
                    if (!Frei(raum, v, rect, rand)) continue;
                    var zug = new double2(x + b.Laenge / 2, cm);
                    v.Teile.Add(new Teil { B = b, Rect = rect, Zugang = zug, ZumWeg = new double2(0, -1) });
                    reihe.Add(zug.x);
                    x += b.Laenge + Luft; ok = true; break;
                }
                if (!ok) x += .5;
            }

            // Wege nur so weit, wie Stellplaetze sie brauchen.
            foreach (var paar in weitesteJeGang)
                v.Wege.Add(new Weg { A = new double2(paar.Key + Gang / 2, cm),
                    B = new double2(paar.Key + Gang / 2, Math.Max(paar.Value, c1)), Breite = Gang, Art = Fahrradwegart.Fahrradweg });
            var xs = genutzt.Concat(reihe).Concat(sammelAnschluss).ToList();
            if (hatZufahrt) xs.Add(zx);
            if (xs.Count > 0)
            {
                // Bis zum aeussersten Zugang; das Sammelband [k0, k1] ist reserviert.
                var von = Math.Min(xs.Min(), zx);
                var bis = Math.Max(xs.Max(), zx);
                if (bis - von > .01)
                    v.Wege.Add(new Weg { A = new double2(von, cm), B = new double2(bis, cm), Breite = Gang, Art = Fahrradwegart.Fahrradweg });
            }
            if (hatZufahrt)
                v.Wege.Add(new Weg { A = zugang, B = new double2(zx, cm), Breite = Gang, Art = Fahrradwegart.Fahrradweg, Zufahrt = true });
            return v;
        }

        /** Zu flach fuer den Sammelweg: eine Reihe direkt an der Zufahrtskante. */
        private static Variante Randreihe(Raum raum, double2 zugang, double kanteY, double rand, bool hatZufahrt,
            Baustein[] bausteine, List<(double2 P, double2 N)> weitere)
        {
            var v = new Variante();
            foreach (var (p, n) in weitere) Stich(raum, p, n, rand, v);
            var y0 = kanteY + rand;
            if (hatZufahrt)
            {
                v.Sperren.Add(Rechteck(zugang.x - Gang / 2, zugang.y - 1, Gang, y0 - zugang.y + 1 + Gang));
                v.Wege.Add(new Weg { A = zugang, B = new double2(zugang.x, y0 + Gang / 2),
                    Breite = Gang, Art = Fahrradwegart.Fahrradweg, Zufahrt = true });
            }
            for (var x = raum.MinX; x < raum.MaxX - .5;)
            {
                var ok = false;
                foreach (var b in bausteine.Where(b => b != Dach))
                {
                    var rect = Rechteck(x + Rest(b), y0, b.Laenge - 2 * Rest(b), b.Tiefe);
                    if (!Frei(raum, v, rect, rand)) continue;
                    v.Teile.Add(new Teil { B = b, Rect = rect, Zugang = new double2(x + b.Laenge / 2, kanteY),
                        ZumWeg = new double2(0, -1) });
                    x += b.Laenge + Luft; ok = true; break;
                }
                if (!ok) x += .25;
            }
            return v;
        }

        // ------------------------------------------------------------------ D

        private static Variante Buegelplatz(Raum raum, double2 zugang, double kanteY, double rand, bool hatZufahrt,
            List<(double2 P, double2 N)> weitere)
        {
            var v = new Variante();
            var y0 = kanteY + rand;
            var achse = Intervall(raum.Baender(zugang.x, Gang, true), y0 + .1);
            if (achse == null || achse.Value.Bis - rand - y0 < Gang)
            {
                v.Hinweise.Add("Fahrrad D: fuer die Laufachse ist die Flaeche hinter der Zufahrt zu flach.");
                return v;
            }
            var oben = achse.Value.Bis - rand;
            var ax = zugang.x;
            // Kleine Plaetze brauchen keine Laufachse: die Gehflaeche traegt die
            // Raeder (Vanilla-Aufbau). Frei bleibt nur die erste Reihe vor der Zufahrt.
            var mitAchse = oben - y0 >= 10 && raum.MaxX - raum.MinX >= 12;
            v.Sperren.Add(Rechteck(ax - Gang / 2, zugang.y - 1, Gang, (mitAchse ? oben : y0 + 2.4) - zugang.y + 1));
            var achsEnde = y0;
            var anschlussEnde = double.MinValue;
            var achsLinie = new List<(double2 A, double2 B)> { (new double2(ax, y0 + Gang / 2), new double2(ax, oben)) };
            foreach (var (p, n) in weitere)
            {
                if (mitAchse && Treffer(raum, p, n, rand, achsLinie, out _, out var ziel))
                {
                    v.Sperren.Add(Streifen(p, ziel, Gang));
                    v.Wege.Add(new Weg { A = p, B = ziel, Breite = Gang, Art = Fahrradwegart.Fahrradweg, Zufahrt = true });
                    achsEnde = Math.Max(achsEnde, ziel.y);
                    anschlussEnde = Math.Max(anschlussEnde, ziel.y);
                }
                else Stich(raum, p, n, rand, v);
            }
            for (var y = y0; y + 2 <= raum.MaxY - rand + 1e-9; y += 2.4)
            {
                foreach (var seite in mitAchse ? new[] { -1, 1 } : new[] { 1 })
                {
                    // Zellen von der Achse nach aussen (ohne Achse: ein Raster ueber die
                    // ganze Breite); misslingt eine, geht es dahinter weiter (Treppe).
                    var start = !mitAchse ? raum.MinX + rand
                        : seite < 0 ? ax - Gang / 2 - .3 - 1.6 : ax + Gang / 2 + .3;
                    for (var x = start; seite < 0 ? x > raum.MinX - 1.6 : x < raum.MaxX; x += seite * 1.6)
                    {
                        var zelle = Rechteck(x, y, 1.6, 2);
                        if (!raum.Passt(zelle, rand)) continue;
                        var rect = Rechteck(x + .425, y, .75, 2);
                        if (!Frei(raum, v, rect, rand)) continue;
                        v.Teile.Add(new Teil { B = Buegel, Rect = rect, Zugang = new double2(x + .8, y + 2.2), ZumWeg = new double2(0, 1) });
                        if (y + 1 <= oben) achsEnde = Math.Max(achsEnde, y + 1);
                    }
                }
            }
            var a = new double2(ax, y0 + Gang / 2);
            if (hatZufahrt)
                v.Wege.Add(new Weg { A = zugang, B = a, Breite = Gang, Art = Fahrradwegart.Fahrradweg, Zufahrt = true });
            // Die Achse reicht mindestens bis zu jeder Zufahrt, die an ihr anschliesst
            // (hintere Zufahrt traf sie sonst 1,5 m hinter ihrem Ende).
            var achsBis = Math.Max(Math.Min(achsEnde, oben - Gang / 2), anschlussEnde);
            if (mitAchse && achsBis > a.y + .01)
                v.Wege.Add(new Weg { A = a, B = new double2(ax, achsBis), Breite = Gang, Art = Fahrradwegart.Fahrradweg });
            return v;
        }

        // ---------------------------------------------------- weitere Zufahrten

        /** Lokaler Punkt auf der Kante und Richtung ins Grundstueck. */
        private static (double2 P, double2 N) Zufahrtspunkt(Raum raum, float2[] polygon, Entrance e)
        {
            var a = raum.Lokal(polygon[e.Edge]);
            var b = raum.Lokal(polygon[(e.Edge + 1) % polygon.Length]);
            var laenge = math.length(b - a);
            var d = laenge < 1e-9 ? new double2(1, 0) : (b - a) / laenge;
            var p = a + d * math.clamp(e.Along, 0, laenge);
            var n = new double2(-d.y, d.x);
            if (!raum.Innen(p + n * .05)) n = -n;
            return (p, n);
        }

        /** Erster Weg auf dem Strahl ins Grundstueck, wenn der 3-m-Streifen dorthin passt. */
        private static bool Treffer(Raum raum, double2 p, double2 n, double rand,
            List<(double2 A, double2 B)> linien, out int welche, out double2 ziel)
        {
            welche = -1; ziel = default; var bestT = double.MaxValue;
            var amEnde = false;
            for (var i = 0; i < linien.Count; i++)
            {
                var wd = linien[i].B - linien[i].A; var k = Kreuz(n, wd);
                if (Math.Abs(k) < 1e-9)
                {
                    // Weg in der Flucht der Zufahrt (Gang, Laufachse): an sein naechstes
                    // Ende anschliessen, statt ihn zu uebersehen und quer durchs Feld
                    // bis zum naechsten Querweg zu laufen.
                    if (Math.Abs(Kreuz(linien[i].A - p, n)) > Gang / 2) continue;
                    foreach (var ende in new[] { linien[i].A, linien[i].B })
                    {
                        // Auch ein Ende im Randabstand zaehlt: sonst lief die Zufahrt an der
                        // ganzen Achse entlang bis zu deren fernem Ende.
                        var t = math.dot(ende - p, n);
                        if (t <= 1e-6 || t >= bestT) continue;
                        // Unter 1 m baut CS2 keinen Kurs: dann 1 m in den Weg hinein anschliessen.
                        var punkt = t >= ParkingGeometry.NetzMindestlaenge ? ende : ende + n * (ParkingGeometry.NetzMindestlaenge - t);
                        var entlang = math.dot(punkt - linien[i].A, math.normalizesafe(wd));
                        if (entlang < -1e-6 || entlang > math.length(wd) + 1e-6) continue;
                        // Nur wenn der Anschluss selbst ins Grundstueck passt (seitlich versetzt
                        // und sehr kurz ragte er sonst ueber die Kante).
                        if (!raum.Passt(Streifen(p, punkt, Gang - .01))) continue;
                        bestT = t; welche = i; amEnde = true; ziel = punkt;
                    }
                    continue;
                }
                var s = Kreuz(linien[i].A - p, wd) / k;
                var u = Kreuz(linien[i].A - p, n) / k;
                if (s <= rand || u < -1e-6 || u > 1 + 1e-6 || s >= bestT) continue;
                bestT = s; welche = i; amEnde = false;
            }
            if (welche < 0) return false;
            if (!amEnde) ziel = p + n * bestT;
            return bestT - rand < Gang / 2 + 1e-6 || raum.Passt(Streifen(p + n * rand, ziel - n * (Gang / 2), Gang - .01));
        }

        /** Kurzer Anschluss ohne inneren Weg (D ohne Achse, Randreihe): die Gehflaeche traegt. */
        private static void Stich(Raum raum, double2 p, double2 n, double rand, Variante v)
        {
            var ziel = p + n * (rand + Gang / 2);
            v.Sperren.Add(Streifen(p - n, ziel + n * Gang / 2, Gang));
            v.Wege.Add(new Weg { A = p, B = ziel, Breite = Gang, Art = Fahrradwegart.Fahrradweg, Zufahrt = true });
        }

        private static double Rest(Baustein b) => b == Buegel ? .025 : 0;

        // ------------------------------------------------------------- Ausgabe

        private static Fahrradobjekt Objekt(Raum raum, Teil t)
        {
            var pos = (t.Rect[0] + t.Rect[2]) / 2;
            // Codex-Vertrag des Bauteils: Drehung 0 = Zugang in lokal +Y.
            var winkel = Math.Atan2(t.ZumWeg.y, t.ZumWeg.x) - Math.PI / 2;
            var dach = t.B.Art == Fahrradobjektart.Unterstand;
            double2[] boden = null;
            if (dach)
            {
                var laengs = new double2(-t.ZumWeg.y, t.ZumWeg.x);
                boden = new[] { pos - laengs * 2 - t.ZumWeg, pos - laengs * 2 + t.ZumWeg,
                    pos + laengs * 2 + t.ZumWeg, pos + laengs * 2 - t.ZumWeg };
                if (ParkingGeometry.SignedArea(boden) < 0) Array.Reverse(boden);
            }
            return new Fahrradobjekt
            {
                Art = t.B.Art, Prefab = t.B.Prefab, Kapazitaet = t.B.Plaetze,
                Position = raum.Welt(pos), Drehung = raum.Winkel + winkel,
                Grundriss = raum.Welt(t.Rect), Zugang = raum.Welt(t.Zugang),
                BodenPrefab = dach ? "BicycleParkingLotDecal01" : null,
                BodenGrundriss = boden == null ? null : raum.Welt(boden),
            };
        }

        /**
         * Laternen am Wegrand. Nur im Gruen: Baeume und Buesche im Rahmen,
         * Baeume in Restflaechen ab 3 x 3 m und in der Mitte des Platzes.
         * Schlicht bleibt ohne Pflanzen (Nutzer 03.11.: "nur eine Flaeche").
         */
        private static void Licht(Raum raum, double rand, double aussen, double rahmen, bool gruen, Variante v, FahrradPlan plan,
            Func<double2, bool> aufPflaster)
        {
            // Pflanzen meiden, was gebaut wird (Wege, Pflaster, Objekte), nicht die
            // Planungsreserven: ein reservierter, aber leerer Gang ist Gruen.
            var baender = v.Wege.Select(w => Streifen(w.A, w.B, w.Breite, true))
                .Concat(gruen ? Belag(raum, v) : v.Sperren).ToList();
            // Pflanzen auch nie auf dem gebauten Pflaster (Platzring, eingerastete Raender).
            bool Belegt(double2[] rect) => baender.Any(b => Schnell(rect, b)) || v.Teile.Any(t => Schnell(rect, t.Rect))
                || (gruen && rect.Append(Mitte(rect)).Any(aufPflaster));
            // Laternen stehen am Rand des Weges selbst (0,3 m innen): neben dichten
            // Reihen ist kein Platz, und Wege gibt es in jedem Plan.
            var laternen = new List<double2>();
            var wechsel = 1;
            foreach (var w in v.Wege.OrderBy(w => w.Zufahrt))
            {
                var d = w.B - w.A; var l = math.length(d);
                if (l < 1e-6) continue;
                d /= l; var n = new double2(-d.y, d.x);
                for (var s = Math.Min(1.5, l / 2); s <= l + 1e-6; s += 12)
                    foreach (var seite in new[] { wechsel, -wechsel })
                    {
                        var p = w.A + d * s + n * seite * (w.Breite / 2 - .3);
                        var rect = Rechteck(p.x - .2, p.y - .2, .4, .4);
                        // Im Gruen darf die Laterne auch im Rahmen an der Zufahrt stehen.
                        if (!raum.Passt(rect, aussen) || v.Teile.Any(o => Schnell(rect, o.Rect))
                            || laternen.Any(q => math.distance(q, p) < 6)) continue;
                        laternen.Add(p); wechsel = -wechsel; break;
                    }
            }
            plan.Laternen.AddRange(laternen.Select(raum.Welt));
            if (!gruen) return;
            var baeume = new List<double2>(v.Mitte);
            // Ohne Rahmen (ganz aufgeloest) keine Randbepflanzung; Reste bleiben trotzdem gruen.
            var mitteRahmen = aussen + rahmen / 2;
            foreach (var p in rahmen < .5 ? new List<double2>() : Rahmenpunkte(raum, mitteRahmen, BaumabstandRahmen))
                if (!Belegt(Rechteck(p.x - .6, p.y - .6, 1.2, 1.2)) && !laternen.Any(q => math.distance(q, p) < 2))
                    baeume.Add(p);
            var buesche = new List<double2>();
            foreach (var p in rahmen < .5 ? new List<double2>() : Rahmenpunkte(raum, mitteRahmen, BuschabstandRahmen))
                if (!Belegt(Rechteck(p.x - .4, p.y - .4, .8, .8)) && !baeume.Any(q => math.distance(q, p) < 2.5)
                    && !laternen.Any(q => math.distance(q, p) < 1.5))
                    buesche.Add(p);
            for (var y = raum.MinY + rand + 1.5; y <= raum.MaxY - rand - 1.5 + 1e-9; y += 2)
                for (var x = raum.MinX + rand + 1.5; x <= raum.MaxX - rand - 1.5 + 1e-9; x += 2)
                {
                    var p = new double2(x, y);
                    var rect = Rechteck(x - 1.5, y - 1.5, 3, 3);
                    // Platz: grosse Restflaechen sind Wiese mit lockeren Baeumen, kein Obstgarten.
                    if (baeume.Any(q => math.distance(q, p) < (v.Mitte.Count > 0 ? 7 : 4))
                        || laternen.Any(q => math.distance(q, p) < 2)) continue;
                    if (!raum.Passt(rect, rand) || Belegt(rect)) continue;
                    baeume.Add(p);
                }
            plan.Baeume.AddRange(baeume.Select(raum.Welt));
            plan.Buesche.AddRange(buesche.Select(raum.Welt));
        }
    }
}
