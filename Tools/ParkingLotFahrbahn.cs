using System;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * Die Fahrbahn bekommt die Texturlage des Asphaltrings darunter.
     *
     * Die Fahrbahn ist die unsichtbare Strasse, an der eine Alley endet
     * (meist die Randstrasse), als eigene durchgaengige Flaeche mit
     * Strassen-Ebene - nur so liegt sie ueber dem Halbkreis des
     * Alley-Knotens (Issue #22). Sie liegt UEBER dem Asphalt, statt ihn zu
     * zerschneiden, je Asphaltring ein Stueck.
     *
     * WARUM DIE TEXTURLAGE GEHT. CS2 legt die Textur einer Flaeche ohne
     * Transform in der Besitzerkette an ihren ersten Knoten und dreht sie
     * nach der Richtung zum zweiten (AreaBatchSystem.GenerateTriangleData:
     * offsetDir.xy = nodes[0], offsetDir.zw = Left(nodes[1] - nodes[0])).
     * Unsere Flaechen haben keinen Transform-Besitzer. Beginnen Fahrbahn und
     * Asphaltring am selben Punkt und laufen von dort in dieselbe Richtung,
     * liegt die Textur deckungsgleich. Beide sind positiv umlaufend und
     * liegen auf derselben Seite ihrer gemeinsamen Kante (am Aussenrand der
     * Randstrasse), laufen dort also gleich herum.
     *
     * Der gemeinsame Punkt ist oft kein Knoten: der Asphaltring laeuft dort
     * gerade weiter, gerade Zwischenpunkte entfallen. Dann wird er auf der
     * Kante eingesetzt - der Umriss bleibt gleich. Bevorzugt wird eine
     * Kante in der Richtung, mit der der Ring schon beginnt: der Kern richtet
     * jeden Ring nach der Reihenrichtung aus (ZellenAusrichten), und die
     * soll dem Asphalt erhalten bleiben.
     *
     * Ein Ring hat nur einen ersten Knoten: liegen zwei Fahrbahnstuecke auf
     * demselben Asphaltring, passt nur das erste. Die Meldung sagt es.
     */
    public sealed partial class ParkingLotToolSystem
    {
        private const float KnotenGleich = 0.02f;

        private float2[][] RichteFahrbahnAus(float2[][] fahrbahnen,
            ref float2[][] asphalt, ref float2[][] verschmolzen)
        {
            if (fahrbahnen == null || fahrbahnen.Length == 0) return Array.Empty<float2[]>();
            // Kopien: die Ringe des Layouts selbst bleiben unveraendert.
            asphalt = (float2[][])(asphalt ?? Array.Empty<float2[]>()).Clone();
            verschmolzen = (float2[][])(verschmolzen ?? Array.Empty<float2[]>()).Clone();
            var gruppen = new[] { asphalt, verschmolzen };
            var benutzt = new bool[2][] { new bool[asphalt.Length], new bool[verschmolzen.Length] };
            var ergebnis = new float2[fahrbahnen.Length][];
            var ausgerichtet = 0;
            var richtungBehalten = 0;
            for (var a = 0; a < fahrbahnen.Length; a++)
            {
                var p = fahrbahnen[a];
                ergebnis[a] = p;
                if (p == null || p.Length < 3) continue;
                // Erst nur Kanten in der Anfangsrichtung des Rings, dann jede.
                var gefunden = false;
                for (var durchgang = 0; durchgang < 2 && !gefunden; durchgang++)
                for (var g = 0; g < 2 && !gefunden; g++)
                for (var r = 0; r < gruppen[g].Length && !gefunden; r++)
                {
                    var s = gruppen[g][r];
                    if (benutzt[g][r] || s == null || s.Length < 3) continue;
                    var anfang = math.normalizesafe(s[1] - s[0]);
                    for (var i = 0; i < s.Length && !gefunden; i++)
                    for (var j = 0; j < p.Length && !gefunden; j++)
                    {
                        var von = s[i];
                        var nach = s[(i + 1) % s.Length];
                        var rs = math.normalizesafe(nach - von);
                        if (durchgang == 0 && math.dot(rs, anfang) < 0.9999f) continue;
                        var pa = p[j];
                        var pb = p[(j + 1) % p.Length];
                        if (math.dot(rs, math.normalizesafe(pb - pa)) < 0.9999f) continue;
                        // Beide Kanten auf derselben Linie?
                        var quer = new float2(-rs.y, rs.x);
                        if (math.abs(math.dot(pa - von, quer)) > KnotenGleich) continue;
                        // Und ueberlappend? Gemeinsamer Abschnitt auf der Linie.
                        var kante = math.distance(von, nach);
                        var ta = math.dot(pa - von, rs);
                        var tb = math.dot(pb - von, rs);
                        if (math.min(kante, tb) - math.max(0f, ta) < 0.05f) continue;
                        // Ursprung: die Fahrbahnecke, wenn sie auf der Ringkante
                        // liegt, sonst der Ringknoten auf der Fahrbahnkante.
                        var ursprung = ta >= -KnotenGleich ? von + rs * math.max(0f, ta) : von;
                        // Liegt er auf einem Ringknoten, genau den nehmen: der
                        // Asphaltring wird nie verschoben, nur die Fahrbahn.
                        if (math.distance(ursprung, von) <= KnotenGleich) ursprung = von;
                        else if (math.distance(ursprung, nach) <= KnotenGleich) ursprung = nach;
                        gruppen[g][r] = BeginntBei(s, i, ursprung);
                        ergebnis[a] = BeginntBei(p, j, ursprung);
                        benutzt[g][r] = true;
                        gefunden = true;
                        ausgerichtet++;
                        if (durchgang == 0) richtungBehalten++;
                    }
                }
            }
            var text = "PLT-Fahrbahn: " + fahrbahnen.Length + " Stueck(e), " + ausgerichtet
                + " mit der Texturlage ihres Asphaltrings (" + richtungBehalten
                + " ohne Richtungswechsel)"
                + (ausgerichtet < fahrbahnen.Length
                    ? " - die uebrigen haben eine eigene Lage."
                    : ".");
            if (text != _letzteFahrbahnmeldung)
            {
                _letzteFahrbahnmeldung = text;
                Mod.log.Info(text);
            }
            return ergebnis;
        }

        private string _letzteFahrbahnmeldung = string.Empty;

        /** Ab hier gilt ein Netzstueck als Bruecke ueber dem Parkplatz. */
        private const float BrueckeUeberGelaende = 3f;
        /** Rand um den Brueckengrundriss: weicher Flaechenrand, float-Rest. */
        private const float BrueckenRand = 0.3f;

        /**
         * Die Grundrisse der Bruecken ueber dem Parkplatz (Issue #22).
         *
         * Unter ihnen bekommt die Strasse keine Fahrbahn mit Strassen-Ebene:
         * der Flaechenshader prueft keine Hoehe und malte sie auf die
         * Brueckenfahrbahn. Gelesen wird jede Kante im Umkreis, abgetastet
         * in Vierteln ihrer Segmente; ein Viertel zaehlt, wenn es mehr als
         * BrueckeUeberGelaende ueber dem Gelaende liegt. Nur auf dem
         * Spielfaden aufrufen.
         *
         * Eine Bruecke, die erst NACH dem Parkplatz entsteht, kennt er erst
         * nach Edit oder Neubau.
         */
        private float2[][] Brueckengrundrisse(float2[] umriss)
        {
            if (umriss == null || umriss.Length < 3 || _netSearchSystem == null)
                return Array.Empty<float2[]>();
            var min = new float2(float.MaxValue);
            var max = new float2(float.MinValue);
            foreach (var p in umriss) { min = math.min(min, p); max = math.max(max, p); }
            var grundrisse = new System.Collections.Generic.List<float2[]>();
            var baum = _netSearchSystem.GetNetSearchTree(true, out var deps);
            deps.Complete();
            using (var treffer = new Unity.Collections.NativeList<Unity.Entities.Entity>(64,
                Unity.Collections.Allocator.Temp))
            {
                var it = new EntityIterator
                {
                    Bounds = new Colossal.Mathematics.Bounds2(min - 2f, max + 2f),
                    Results = treffer,
                };
                baum.Iterate(ref it);
                var hoehen = _terrainSystem.GetHeightData();
                var gesehen = new System.Collections.Generic.HashSet<Unity.Entities.Entity>();
                foreach (var e in treffer)
                {
                    if (!gesehen.Add(e) || !EntityManager.HasComponent<Game.Net.EdgeGeometry>(e)
                        || EntityManager.HasComponent<Game.Common.Deleted>(e)
                        || EntityManager.HasComponent<Game.Tools.Temp>(e)) continue;
                    var g = EntityManager.GetComponentData<Game.Net.EdgeGeometry>(e);
                    foreach (var segment in new[] { g.m_Start, g.m_End })
                    for (var i = 0; i < 4; i++)
                    {
                        float t0 = i * 0.25f, t1 = (i + 1) * 0.25f;
                        var l0 = Colossal.Mathematics.MathUtils.Position(segment.m_Left, t0);
                        var l1 = Colossal.Mathematics.MathUtils.Position(segment.m_Left, t1);
                        var r0 = Colossal.Mathematics.MathUtils.Position(segment.m_Right, t0);
                        var r1 = Colossal.Mathematics.MathUtils.Position(segment.m_Right, t1);
                        var mitte = (l0 + l1 + r0 + r1) * 0.25f;
                        var gelaende = Game.Simulation.TerrainUtils.SampleHeight(ref hoehen, mitte);
                        if (!math.isfinite(gelaende) || mitte.y - gelaende < BrueckeUeberGelaende) continue;
                        // Quer nach aussen um den Rand erweitern.
                        var q0 = math.normalizesafe(l0.xz - r0.xz) * BrueckenRand;
                        var q1 = math.normalizesafe(l1.xz - r1.xz) * BrueckenRand;
                        var viereck = new[] { l0.xz + q0, l1.xz + q1, r1.xz - q1, r0.xz - q0 };
                        var vmin = math.min(math.min(viereck[0], viereck[1]), math.min(viereck[2], viereck[3]));
                        var vmax = math.max(math.max(viereck[0], viereck[1]), math.max(viereck[2], viereck[3]));
                        if (math.any(vmax < min) || math.any(vmin > max)) continue;
                        grundrisse.Add(viereck);
                    }
                }
            }
            var text = "PLT-Bruecken ueber dem Parkplatz: " + grundrisse.Count
                + " Grundrissstueck(e); darunter keine Fahrbahn mit Strassen-Ebene.";
            if (text != _letzteBrueckenmeldung)
            {
                _letzteBrueckenmeldung = text;
                Mod.log.Info(text);
            }
            return grundrisse.ToArray();
        }

        private string _letzteBrueckenmeldung = string.Empty;

        /**
         * Der Ring, beginnend bei `punkt` auf seiner Kante `i` -> `i+1`.
         * Faellt der Punkt mit einem Endknoten zusammen, wird nur gedreht;
         * sonst wird er als Knoten eingesetzt (der Umriss bleibt gleich),
         * und zwar exakt mit diesem Wert, damit beide Ringe denselben
         * Ursprung haben.
         */
        private static float2[] BeginntBei(float2[] ring, int i, float2 punkt)
        {
            var n = ring.Length;
            if (math.distance(ring[i], punkt) <= KnotenGleich)
            {
                var gedreht = new float2[n];
                for (var k = 0; k < n; k++) gedreht[k] = ring[(i + k) % n];
                gedreht[0] = punkt;
                return gedreht;
            }
            if (math.distance(ring[(i + 1) % n], punkt) <= KnotenGleich)
            {
                var gedreht = new float2[n];
                for (var k = 0; k < n; k++) gedreht[k] = ring[(i + 1 + k) % n];
                gedreht[0] = punkt;
                return gedreht;
            }
            var neu = new float2[n + 1];
            neu[0] = punkt;
            for (var k = 0; k < n; k++) neu[k + 1] = ring[(i + 1 + k) % n];
            return neu;
        }
    }
}
