using static ParkingLotTool.Tools.ParkingLotTexte;
using System.Collections.Generic;
using Unity.Mathematics;
using Kurven = ParkingLotTool.Geometry.Strassenkurve;

namespace ParkingLotTool.Tools
{
    /**
     * KANTE NEBEN EINER KURVE VERSCHIEBEN (Nutzer, 19.10.).
     *
     * Ohne das wanderte das gemeinsame Ende von gerader Kante und Kurve
     * starr mit der Kante und verlor die Strasse. Jetzt loest sich die Kurve
     * beim Anfassen (wie beim Ziehen eines Kurvenendes), das Ende gleitet
     * waehrend des Zugs dorthin, wo die verschobene Kante den Bordstein
     * schneidet, und beim Loslassen wird die Kurve vom anderen Ende her neu
     * verfolgt. Findet sich kein Schnitt auf demselben Bordstein, bleibt es
     * beim alten starren Weg und die Kante wird gerade.
     *
     * OHNE BORDSTEIN (versetzte Kurve, Edit, Schalter aus - Nutzer 19.10.:
     * "die Kurve wird verworfen"): das Ende gleitet auf der Kurve SELBST,
     * dorthin, wo die verschobene Kante sie schneidet. Nach aussen wird die
     * erste Sehne laenger, nach innen faellt weg, was abgeschnitten ist.
     */
    public sealed partial class ParkingLotToolSystem
    {
        private sealed class Kantenzugkurve
        {
            internal float2 Start;
            internal Kurven.Fang Ende, Anders;
            internal Dictionary<long, Kurven.Kante> Netz;
            internal float LetzterVersatz = float.NaN;
            internal bool Getroffen;
            internal Kurven.Fang Neu;
            internal float3[] Vorschau;
            internal bool Gemeldet;
            internal List<Kurven.Fang> Kandidaten;
            // Die geloeste Kurve selbst. Liegt der Schnitt auf der Abrundung einer
            // Ecke, gehoert die zum KNOTEN und nicht zu den Bordsteinen der Strassen
            // (Log 19.10. 22:05: 0 Kandidaten, Bordstein 0,2 m daneben) - dann
            // gleitet das Ende auf der schon verfolgten Kurve.
            internal Eigenkurvenende Ersatz;
            internal float2? ErsatzEnde;
            // Lagen auf dem ganzen Verlauf (Bogenmeter ab seinem Anfang am gemeinsamen Ende).
            internal float ErsatzBezug, ErsatzFern;
            internal float2 FernPunkt;
            internal List<float2> ErsatzRest;
            // Letzter Versatz mit Platz fuer dieses Kurvenende; dahinter bleibt der Zug stehen.
            internal float GuterVersatz;
            // Nur der gemerkte Verlauf, kein Strassenfang (Ende sitzt auf einer Abrundung):
            // Netz ist dann null, und ein Klick ohne Zug legt diese Zwischenpunkte zurueck.
            internal List<float2> AlteZwischen;
        }

        // Je Ende der gezogenen Kante (0 = Anfang, 1 = Ende); null = keine Kurve dort.
        private readonly Kantenzugkurve[] _kantenzugKurven = new Kantenzugkurve[2];

        private sealed class Eigenkurvenende
        {
            internal Kurvenkante Kurve;
            internal bool AmVon;
            // Punktnummern und Startlagen vom gemeinsamen Ende nach aussen.
            internal int[] Kette;
            internal float2[] Start;
            internal int Weg;
            internal float2? Ende;
            internal float GuterVersatz;
        }

        /**
         * KEIN KANTENZUG ZERSTOERT EINE KURVE (Nutzer 20.10.: "hier wuerde ich
         * mir sogar eine Sperre wuenschen").
         *
         * Die Schliesskante eines U verbindet beide Enden DERSELBEN Kurve. Zog
         * man sie, wanderten beide Enden starr mit, die Kurve loeste sich auf
         * und es blieben zwei Punkte - "The cell engine needs a polygon with
         * at least three corners", danach war keine Form mehr zu bauen. Solche
         * Kanten ziehen jetzt beide Enden auf der Kurve mit (ParkingLotKurvenSchlusszug).
         * Und wo ein Kurvenende weder am Bordstein noch auf der Kurve selbst Platz
         * findet, bleibt der Zug am letzten gueltigen Versatz stehen.
         */
        private bool _kantenzugZurueck;
        // Schliesskante, fuer die kein Schlusszug zustande kam: lieber stehen als zerfallen.
        private bool _kantenzugGesperrt;

        private static float? Enger(float? bisher, float neu)
            => bisher.HasValue && math.abs(bisher.Value) <= math.abs(neu) ? bisher : neu;

        private readonly Eigenkurvenende[] _eigenKurven = new Eigenkurvenende[2];
        private const double KantenzugReichweite = 64;

        /** Vor dem Merken der Startpunkte: Kurven an beiden Enden loesen, `_dragEdge` nachfuehren. */
        private void LoeseKurvenFuerKantenzug()
        {
            _kantenzugKurven[0] = _kantenzugKurven[1] = null;
            _eigenKurven[0] = _eigenKurven[1] = null;
            _schlusszug = null;
            _kantenzugGesperrt = false;
            if (_extrudeStart >= 0 || _dragEdge < 0 || _kurvenkanten.Count == 0) return;
            var a = _points[_dragEdge];
            var b = _points[(_dragEdge + 1) % _points.Count];
            for (int e = 0; e < 2; e++)
            {
                var p = e == 0 ? a : b;
                var andererEndpunkt = e == 0 ? b : a;
                foreach (var k in _kurvenkanten)
                {
                    bool von = math.all(k.Von == p), bis = math.all(k.Bis == p);
                    if (!von && !bis) continue;
                    var anders = von ? k.Bis : k.Von;
                    // Laeuft die Kurve zum anderen Ende derselben Kante, wuerden beide wandern
                    // und die Kurve zerfallen: diese Kante bleibt, wo sie ist.
                    if (math.all(anders == andererEndpunkt))
                    {
                        _schlusszug = BeginneSchlusszug(k);
                        _kantenzugGesperrt = _schlusszug == null;
                        return;
                    }
                    if (!KurveFolgenAn || !_kurvenPunkte.TryGetValue(p, out var fe)
                        || !_kurvenPunkte.TryGetValue(anders, out var fa))
                    {
                        // Gekuerzte Kurve, Ende auf der Abrundung (Berichte 19.10. 22:21-22:22):
                        // ohne Strassenfang gleitet es auf dem gemerkten ganzen Verlauf. Sonst
                        // verlaengerte der Weg ohne Bordstein die letzte Sehne ueber die Strasse.
                        if (k.Ganz != null)
                        {
                            var verlauf = GanzeKette(k);
                            if (!von) System.Array.Reverse(verlauf);
                            var alte = new List<float2>(k.Zwischen);
                            if (!von) alte.Reverse();
                            _kantenzugKurven[e] = new Kantenzugkurve
                            {
                                Start = p,
                                Ersatz = new Eigenkurvenende { Kurve = k, AmVon = von, Start = verlauf },
                                ErsatzBezug = LageAufKette(verlauf, p), ErsatzFern = LageAufKette(verlauf, anders),
                                FernPunkt = anders, AlteZwischen = alte,
                            };
                            break;
                        }
                        _eigenKurven[e] = Eigenkurve(k, von);
                        break;
                    }
                    LadeKurvennetz(fe, fa);
                    var netz = new Dictionary<long, Kurven.Kante>(_kurvenNetz);
                    ErweitereKantenzugnetz(netz, 2);
                    var ganz = GanzeKette(k);
                    if (!von) System.Array.Reverse(ganz);
                    _kantenzugKurven[e] = new Kantenzugkurve
                    {
                        Start = p, Ende = fe, Anders = fa, Netz = netz,
                        Ersatz = new Eigenkurvenende { Kurve = k, AmVon = von, Start = ganz },
                        ErsatzBezug = LageAufKette(ganz, p), ErsatzFern = LageAufKette(ganz, anders),
                        FernPunkt = anders,
                    };
                    break;
                }
            }
            for (int e = 0; e < 2; e++)
                if (_kantenzugKurven[e] != null) LoeseKurvenAn(_kantenzugKurven[e].Start);
            _dragEdge = _points.IndexOf(a);
            _hoverEdge = _dragEdge;
        }

        /**
         * Ueber die Kurvenenden hinaus: dieselbe Strasse `stufen` Kanten weiter,
         * damit das Ende auch beim Herausziehen auf dem Bordstein bleibt. Nur
         * die Fortsetzung ohne Knick - an Kreuzungen nicht in Querstrassen.
         */
        private void ErweitereKantenzugnetz(Dictionary<long, Kurven.Kante> netz, int stufen)
        {
            for (int s = 0; s < stufen; s++)
            {
                var neu = new List<long>();
                foreach (var k in netz.Values)
                    foreach (var node in new[] { k.Start, k.Ende })
                    {
                        var n = LeseKurvenknoten(KurvenEntity(node));
                        if (n == null || n.Kanten.Length < 2) continue;
                        long weiter = n.Kanten.Length == 2
                            ? (n.Kanten[0] == k.Id ? n.Kanten[1] : n.Kanten[0])
                            : Kurven.Weiterfuehrung(n, k.Id);
                        if (weiter != 0 && !netz.ContainsKey(weiter) && !neu.Contains(weiter)) neu.Add(weiter);
                    }
                if (neu.Count == 0) return;
                foreach (var id in neu)
                {
                    var k = LeseKurvenkante(KurvenEntity(id));
                    if (k != null) netz[id] = k;
                }
            }
        }

        private Eigenkurvenende Eigenkurve(Kurvenkante k, bool amVon)
        {
            var kette = KurvenKette(k);
            if (kette == null || kette.Length < 3) return null;
            if (!amVon) System.Array.Reverse(kette);
            var start = new float2[kette.Length];
            for (int i = 0; i < kette.Length; i++) start[i] = _points[kette[i]];
            return new Eigenkurvenende { Kurve = k, AmVon = amVon, Kette = kette, Start = start };
        }

        /**
         * Wo schneidet die Gerade `q + s*d` die Kurve, vom gemeinsamen Ende aus
         * gesehen? Vor dem Ende zaehlt die Verlaengerung der ersten Sehne.
         * `weg` = wie viele Zwischenpunkte dabei abgeschnitten werden; einer
         * bleibt immer, sonst waere es keine Kurve mehr.
         */
        private static bool SchneideEigenkurve(float2[] k, float2 q, float2 d, out float2 punkt, out int weg)
            => SchneideKette(k, q, d, 0f, out punkt, out weg, out _) && weg <= k.Length - 3;

        /**
         * Schnitt der Geraden mit dem Punktzug `k`, der Lage `bezug` (Bogenmeter)
         * am naechsten. Vor dem Anfang zaehlt die Verlaengerung der ersten Sehne
         * mit negativer Lage. `weg` = Zahl der Zwischenpunkte vor dem Schnitt.
         */
        private static bool SchneideKette(float2[] k, float2 q, float2 d, float bezug,
            out float2 punkt, out int weg, out float lage)
        {
            punkt = default; weg = 0; lage = 0f;
            float bester = float.PositiveInfinity, bogen = 0f;
            for (int j = -1; j < k.Length - 1; j++)
            {
                var a = j < 0 ? k[0] : k[j];
                var b = j < 0 ? k[0] + (k[0] - k[1]) : k[j + 1];
                var r = b - a;
                float laenge = math.length(r);
                float nenner = r.x * d.y - r.y * d.x;
                if (math.abs(nenner) < 1e-9f) { if (j >= 0) bogen += laenge; continue; }
                var w = q - a;
                float u = (w.x * d.y - w.y * d.x) / nenner;
                bool trifft = j < 0 ? u >= 0f : u >= 0f && u <= 1f;
                if (trifft)
                {
                    float hier = j < 0 ? -u * laenge : bogen + u * laenge;
                    if (math.abs(hier - bezug) < bester)
                    {
                        bester = math.abs(hier - bezug);
                        lage = hier;
                        punkt = a + r * u;
                        weg = j < 0 ? 0 : (u >= 1f ? j + 1 : j);
                    }
                }
                if (j >= 0) bogen += laenge;
            }
            return !float.IsInfinity(bester);
        }

        /** Bogenmeter des Lotpunkts von `p` auf dem Punktzug. */
        private static float LageAufKette(float2[] k, float2 p)
        {
            float beste = float.PositiveInfinity, lage = 0f, bogen = 0f;
            for (int j = 0; j + 1 < k.Length; j++)
            {
                var r = k[j + 1] - k[j];
                float laenge = math.length(r);
                float t = laenge > 0f ? math.clamp(math.dot(p - k[j], r) / (laenge * laenge), 0f, 1f) : 0f;
                float abstand = math.distance(p, k[j] + r * t);
                if (abstand < beste) { beste = abstand; lage = bogen + t * laenge; }
                bogen += laenge;
            }
            return lage;
        }

        private static float2[] GanzeKette(Kurvenkante k)
        {
            if (k.Ganz != null) return (float2[])k.Ganz.Clone();
            var kette = new float2[k.Zwischen.Length + 2];
            kette[0] = k.Von;
            for (int i = 0; i < k.Zwischen.Length; i++) kette[i + 1] = k.Zwischen[i];
            kette[kette.Length - 1] = k.Bis;
            return kette;
        }

        /** Nach dem starren Versatz: Kurvenenden auf den Bordstein setzen. */
        private void KantenzugAufBordstein(int aIndex, int bIndex, float offset)
        {
            if (_schlusszug != null) { SchlusszugAufKurve(aIndex, bIndex, offset); return; }
            if (_kantenzugGesperrt)
            {
                if (!offset.Equals(0f) && !_kantenzugZurueck) ZurueckAufVersatz(0f);
                return;
            }
            float? zurueck = EigenkurvenAufKante(aIndex, bIndex, offset);
            var startA = _dragEdgeStartPoints[aIndex];
            var startB = _dragEdgeStartPoints[bIndex];
            var shift = _dragEdgeNormal * offset;
            for (int e = 0; e < 2; e++)
            {
                var z = _kantenzugKurven[e];
                if (z == null) continue;
                int index = e == 0 ? aIndex : bIndex;
                if (!offset.Equals(z.LetzterVersatz))
                {
                    z.LetzterVersatz = offset;
                    z.Getroffen = z.Netz != null && SucheKantenzugSchnitt(z, startA + shift, startB - startA,
                        (e == 0 ? startA : startB) + shift);
                    if (z.Getroffen) z.GuterVersatz = offset;
                    z.ErsatzEnde = null;
                    // Auf dem GANZEN verfolgten Verlauf, nicht nur dem Rest: so kommt
                    // ein schon abgeschnittenes Stueck der Abrundung beim Herausziehen zurueck.
                    if (!z.Getroffen && z.Ersatz != null
                        && SchneideKette(z.Ersatz.Start, startA + shift, startB - startA, z.ErsatzBezug,
                            out var schnitt, out _, out var lage)
                        && lage < z.ErsatzFern - (float)Kurven.Mindestsehne)
                    {
                        z.ErsatzEnde = AufGitter(schnitt);
                        z.ErsatzRest = new List<float2>();
                        var y = _dragEdgeStartWorld[index].y;
                        var vorschau = new List<float3> { new float3(z.ErsatzEnde.Value.x, y, z.ErsatzEnde.Value.y) };
                        float bogen = 0f;
                        for (int i = 0; i < z.Ersatz.Start.Length; i++)
                        {
                            if (i > 0) bogen += math.distance(z.Ersatz.Start[i - 1], z.Ersatz.Start[i]);
                            if (bogen <= lage + 0.01f || bogen >= z.ErsatzFern - 0.01f) continue;
                            z.ErsatzRest.Add(z.Ersatz.Start[i]);
                            vorschau.Add(new float3(z.Ersatz.Start[i].x, y, z.Ersatz.Start[i].y));
                        }
                        vorschau.Add(new float3(z.FernPunkt.x, y, z.FernPunkt.y));
                        z.Vorschau = vorschau.ToArray();
                        z.GuterVersatz = offset;
                    }
                    else if (!z.Getroffen && z.Netz != null)
                        MeldeKantenzugOhneSchnitt(z, startA + shift, startB - startA);
                }
                if (z.Getroffen)
                {
                    _points[index] = AufGitter(z.Neu.Punkt.xz);
                    _worldPoints[index] = z.Neu.Punkt;
                }
                else if (z.ErsatzEnde.HasValue)
                {
                    var p = z.ErsatzEnde.Value;
                    _points[index] = p;
                    _worldPoints[index] = new float3(p.x, _dragEdgeStartWorld[index].y, p.y);
                }
                else zurueck = Enger(zurueck, z.GuterVersatz);
            }
            if (zurueck.HasValue && !_kantenzugZurueck && !zurueck.Value.Equals(offset)) ZurueckAufVersatz(zurueck.Value);
        }

        private void ZurueckAufVersatz(float versatz)
        {
            _kantenzugZurueck = true;
            try { ApplyEdgeOffset(versatz); }
            finally { _kantenzugZurueck = false; }
        }

        private bool SucheKantenzugSchnitt(Kantenzugkurve z, float2 q, float2 d, float2 erwartet)
        {
            z.Vorschau = null;
            var kandidaten = Kurven.Bordsteinschnitte(z.Netz.Values, q, d, erwartet, KantenzugReichweite);
            foreach (var f in kandidaten)
            {
                // Derselbe Bordstein wie die Kurve: vom anderen Ende her verfolgbar.
                var e = VerfolgeKurvenpaar(z.Anders, f, 0);
                // Gerade: der Rest des Bordsteins ist gerade - dann wird es eine gerade Kante.
                if (e == null || !(e.Folgt || e.Grund == Kurven.Grund.ZuKurz || e.Grund == Kurven.Grund.Nullsehne
                    || e.Grund == Kurven.Grund.Gerade))
                    continue;
                z.Neu = f;
                if (e.Grund == Kurven.Grund.Keiner && e.Punkte != null && e.Punkte.Length >= 3) z.Vorschau = e.Punkte;
                return true;
            }
            z.Kandidaten = kandidaten;
            return false;
        }

        /** Einmal je Zug, nur wenn weder Bordstein noch Kurve getroffen wurde. */
        private void MeldeKantenzugOhneSchnitt(Kantenzugkurve z, float2 q, float2 d)
        {
            if (!z.Gemeldet)
            {
                // Was geladen war und wo das Ende sitzt (19.10.: 0 Kandidaten schon am Start).
                z.Gemeldet = true;
                var b = new System.Text.StringBuilder();
                b.Append($"PLT-Kurve-Kantenzug: Ende {KurvenEntity(z.Ende.Kante)}/{z.Ende.Seite}/{z.Ende.Teil}@{z.Ende.T:F3}, ")
                 .Append($"anders {KurvenEntity(z.Anders.Kante)}/{z.Anders.Seite}/{z.Anders.Teil}@{z.Anders.T:F3}, ")
                 .Append($"Gerade q-Ende {math.distance(q, z.Ende.Punkt.xz):F2} m, Richtung {math.normalizesafe(d).x:F3}/{math.normalizesafe(d).y:F3}; Netz {z.Netz.Count}:");
                foreach (var k in z.Netz.Values)
                {
                    b.Append($" {KurvenEntity(k.Id)}");
                    for (int seite = -1; seite <= 1; seite += 2)
                    {
                        var teile = k.Seite(seite);
                        float naechster = float.PositiveInfinity;
                        if (teile != null)
                            foreach (var t in teile)
                                for (int i = 0; i <= 16; i++)
                                    naechster = math.min(naechster, math.distance(t.Kurve.Punkt(i / 16f).xz, z.Ende.Punkt.xz));
                        b.Append($" s{seite}:{teile?.Length ?? 0}/{naechster:F1}m");
                    }
                    if (k.Hoehe) b.Append(" Hoehe");
                }
                foreach (var f in z.Kandidaten ?? new List<Kurven.Fang>())
                {
                    var e = VerfolgeKurvenpaar(z.Anders, f, 0);
                    b.Append($"; Kandidat {KurvenEntity(f.Kante)}/{f.Seite}/{f.Teil}@{f.T:F2} {e?.Grund}");
                }
                Mod.log.Info(b.ToString());
            }
        }

        private float? EigenkurvenAufKante(int aIndex, int bIndex, float offset)
        {
            float? zurueck = null;
            if (_eigenKurven[0] == null && _eigenKurven[1] == null) return zurueck;
            var startA = _dragEdgeStartPoints[aIndex];
            var startB = _dragEdgeStartPoints[bIndex];
            var shift = _dragEdgeNormal * offset;
            for (int e = 0; e < 2; e++)
            {
                var z = _eigenKurven[e];
                if (z == null) continue;
                // Jedes Bild vom Start aus: der Schnitt darf auch zurueckwandern.
                for (int i = 1; i < z.Kette.Length; i++)
                {
                    _points[z.Kette[i]] = z.Start[i];
                    _worldPoints[z.Kette[i]] = _dragEdgeStartWorld[z.Kette[i]];
                }
                z.Weg = 0;
                z.Ende = null;
                if (!SchneideEigenkurve(z.Start, (e == 0 ? startA : startB) + shift, startB - startA,
                        out var schnitt, out var weg))
                {
                    zurueck = Enger(zurueck, z.GuterVersatz);
                    continue;
                }
                schnitt = AufGitter(schnitt);
                // Abgeschnittene Zwischenpunkte liegen bis zum Loslassen auf dem Ende.
                for (int i = 0; i <= weg; i++)
                {
                    int index = z.Kette[i];
                    _points[index] = schnitt;
                    _worldPoints[index] = new float3(schnitt.x, _dragEdgeStartWorld[index].y, schnitt.y);
                }
                z.Weg = weg;
                z.Ende = schnitt;
                z.GuterVersatz = offset;
            }
            return zurueck;
        }

        /** Loslassen: abgeschnittene Punkte entfernen, Kurve auf ihr neues Ende setzen. */
        private void UebernimmEigenkurven()
        {
            for (int e = 0; e < 2; e++)
            {
                var z = _eigenKurven[e];
                _eigenKurven[e] = null;
                if (z == null) continue;
                var k = z.Kurve;
                // Kein Schnitt mit der Kurve: das Ende ist gerade gewandert, die Kurve faellt weg.
                if (!z.Ende.HasValue) continue;
                var ende = z.Ende.Value;
                var rest = new List<float2>();
                for (int i = z.Weg + 1; i < z.Kette.Length - 1; i++) rest.Add(z.Start[i]);
                // Nach Lage, nicht nach Nummer: eine am anderen Ende neu verfolgte
                // Kurve hat die Nummern inzwischen verschoben. Von den Weg+1
                // gleichen Punkten bleibt einer.
                for (int j = _points.Count - 1, weg = z.Weg; j >= 0 && weg > 0; j--)
                {
                    if (!math.all(_points[j] == ende)) continue;
                    EntfernePunkt(j);
                    weg--;
                }
                if (!z.AmVon) rest.Reverse();
                var von = z.AmVon ? ende : k.Von;
                var bis = z.AmVon ? k.Bis : ende;
                if (k.Zettel != null)
                    k.Zettel = k.Zettel.Gekuerzt(von, bis, z.AmVon ? z.Weg : 0, z.AmVon ? 0 : z.Weg);
                SetzeKurvenpunkte(k, von, bis, rest.ToArray());
            }
        }

        /** Loslassen nach einem Zug: Fang des neuen Endes merken, Kurve neu verfolgen. */
        private void UebernimmKantenzugKurven(int aIndex, int bIndex)
        {
            UebernimmSchlusszug();
            // Erst beide Punkte merken: das Einsetzen der ersten Kurve verschiebt die Nummern.
            var punkte = new float2?[2];
            var ersatz = new float2?[2];
            for (int e = 0; e < 2; e++)
            {
                var z = _kantenzugKurven[e];
                int index = e == 0 ? aIndex : bIndex;
                if (z == null || index < 0 || index >= _points.Count) continue;
                if (z.Getroffen && math.all(_points[index] == AufGitter(z.Neu.Punkt.xz))) punkte[e] = _points[index];
                else if (!z.Getroffen && z.ErsatzEnde.HasValue && math.all(_points[index] == z.ErsatzEnde.Value))
                    ersatz[e] = _points[index];
            }
            for (int e = 0; e < 2; e++)
            {
                var z = _kantenzugKurven[e];
                _kantenzugKurven[e] = null;
                if (z != null && ersatz[e] != null) SetzeErsatzkurve(z, ersatz[e].Value);
                if (z == null || punkte[e] == null) continue;
                _kurvenPunkte.Remove(z.Start);
                _kurvenPunkte[punkte[e].Value] = z.Neu;
                int index = _points.IndexOf(punkte[e].Value);
                if (index >= 0) UebernimmKurvenAn(index);
                if (z.Ersatz != null) BehalteGanzenVerlauf(z, punkte[e].Value);
            }
            UebernimmEigenkurven();
        }

        /**
         * Neu am Bordstein verfolgt: liegt die neue Kurve ganz auf dem alten
         * Verlauf (gekuerzt), merkt sie sich ihn. Reicht sie darueber hinaus,
         * ist sie selbst der ganze Verlauf.
         */
        private void BehalteGanzenVerlauf(Kantenzugkurve z, float2 ende)
        {
            var ganz = GanzeKette(z.Ersatz.Kurve);
            foreach (var k in _kurvenkanten)
            {
                bool amVon = math.all(k.Von == ende), amBis = math.all(k.Bis == ende);
                if (!amVon && !amBis) continue;
                if (!math.all((amVon ? k.Bis : k.Von) == z.FernPunkt)) continue;
                // Ragt ein Ende ueber den alten Verlauf hinaus (Bordstein der geraden
                // Strasse), ist die neue Kurve selbst der ganze Verlauf.
                if (DistanzZurKette(ganz, k.Von) > .5f || DistanzZurKette(ganz, k.Bis) > .5f) return;
                float lageVon = LageAufKette(ganz, k.Von), lageBis = LageAufKette(ganz, k.Bis);
                if (lageVon > lageBis) System.Array.Reverse(ganz);
                k.Ganz = ganz;
                return;
            }
        }

        private static float DistanzZurKette(float2[] k, float2 p)
        {
            float beste = float.PositiveInfinity;
            for (int j = 0; j + 1 < k.Length; j++)
            {
                var r = k[j + 1] - k[j];
                float l2 = math.lengthsq(r);
                float t = l2 > 0f ? math.clamp(math.dot(p - k[j], r) / l2, 0f, 1f) : 0f;
                beste = math.min(beste, math.distance(p, k[j] + r * t));
            }
            return beste;
        }

        /** Der Rest der geloesten Kurve kommt zurueck, ab dem neuen Ende. */
        private void SetzeErsatzkurve(Kantenzugkurve z, float2 ende)
        {
            var ez = z.Ersatz;
            var k = ez.Kurve;
            var rest = z.ErsatzRest;
            int n = _points.Count;
            int index = _points.IndexOf(ende);
            if (rest == null || rest.Count == 0 || index < 0) return;
            var fern = z.FernPunkt;
            bool vorwaerts = math.all(_points[(index + 1) % n] == fern);
            if (!vorwaerts && !math.all(_points[(index - 1 + n) % n] == fern)) return;
            var einfuegen = new List<float2>(rest);
            if (!vorwaerts) einfuegen.Reverse();
            int stelle = vorwaerts ? index + 1 : index;
            var y = _worldPoints[index].y;
            SyncSnapAxes();
            for (int i = 0; i < einfuegen.Count; i++)
            {
                _points.Insert(stelle + i, einfuegen[i]);
                _worldPoints.Insert(stelle + i, new float3(einfuegen[i].x, y, einfuegen[i].y));
                _pointAxes.Insert(stelle + i, float2.zero);
            }
            var zwischen = new List<float2>(rest);
            if (!ez.AmVon) zwischen.Reverse();
            var von = ez.AmVon ? ende : k.Von;
            var bis = ez.AmVon ? k.Bis : ende;
            var bogenlagen = new float[zwischen.Count];
            float bogen = 0f;
            for (int i = 0; i < zwischen.Count; i++)
            {
                bogen += math.distance(i == 0 ? von : zwischen[i - 1], zwischen[i]);
                bogenlagen[i] = bogen;
            }
            _kurvenkanten.Add(new Kurvenkante
            {
                Von = von, Bis = bis, Zwischen = zwischen.ToArray(),
                Zettel = k.Zettel?.MitPunkten(von, bis, bogenlagen),
                Ganz = GanzeKette(k),
            });
            foreach (var p in zwischen) _kurvenZwischen.Add(p);
            _kurvenformRevision = -1;
            Mod.log.Info($"PLT-Kurve: Ende auf der Kurve selbst gesetzt, {zwischen.Count} Zwischenpunkte bleiben.");
        }

        /** Klick ohne Zug: die geloesten Kurven unveraendert wieder anlegen. */
        private void StelleKantenzugKurvenWieder()
        {
            _eigenKurven[0] = _eigenKurven[1] = null;
            _schlusszug = null;
            for (int e = 0; e < 2; e++)
            {
                var v = _kantenzugKurven[e];
                if (v == null || v.Netz != null) continue;
                _kantenzugKurven[e] = null;
                v.ErsatzRest = v.AlteZwischen;
                SetzeErsatzkurve(v, v.Start);
            }
            for (int e = 0; e < 2; e++)
            {
                var z = _kantenzugKurven[e];
                _kantenzugKurven[e] = null;
                if (z == null) continue;
                int index = _points.IndexOf(z.Start);
                if (index >= 0) UebernimmKurvenAn(index);
            }
        }

        /** Strichlinie entlang der Kurve, die beim Loslassen entstuende. */
        private void ZeichneKantenzugkurven(ParkingLotPreviewBuffer buffer)
        {
            if (_dragEdge < 0) return;
            if (_schlusszug != null) ZeichneStrichkurve(buffer, _schlusszug.Vorschau);
            foreach (var z in _kantenzugKurven)
                if (z != null && (z.Getroffen || z.ErsatzEnde.HasValue)) ZeichneStrichkurve(buffer, z.Vorschau);
        }
    }
}
