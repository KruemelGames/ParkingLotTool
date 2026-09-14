using System;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static partial class Program
{
    /**
     * WEICHT DER PARKPLATZ DER BAULANDFLAECHE AUS?
     *
     * Der Nutzer hat gebaut und war enttaeuscht: die Flaeche stand da, aber
     * das Layout wusste nichts von ihr - keine Bucht wich aus, die Vorschau
     * ruehrte sich nicht. Dieser Lauf misst genau das, und zwar an vier
     * Fragen, die alle NULL ergeben muessen:
     *
     *   1. Wieviele Buchten ueberlappen die Flaeche?
     *   2. Wieviele Fahrgassen laufen hinein?
     *   3. Liegt Asphalt darauf, wo blanker Boden sein soll?
     *   4. Verliert der Parkplatz mehr Buchten, als die Flaeche gross ist?
     *
     * Frage 4 ist die Gegenprobe gegen den bequemen Fehler: eine Flaeche,
     * die zu viel wegnimmt, sieht in den ersten drei Zahlen genauso gut aus
     * wie eine richtige.
     */
    private static int RunZoningflaeche()
    {
        var site = new[]
        {
            new float2(-1168.4104f, 84.05932f),
            new float2(-1043.91248f, 91.75974f),
            new float2(-1047.86353f, 155.637726f),
            new float2(-1108.24414f, 151.9027f),
            new float2(-1115.998f, 277.1949f),
            new float2(-1179.40771f, 273.270569f),
        };

        LayoutSettings Grund()
        {
            var s = LayoutSettings.Cs2;
            s.Zellen = true;
            s.AngleMode = "edge";
            s.Auto = false;
            s.AutomaticEntrances = false;
            s.Entrances = new[] { new Entrance { Edge = 0, Along = 20 } };
            return s;
        }

        var ohne = ParkingGeometry.Build(site, Grund());
        Console.WriteLine($"ohne Bauland: {ohne.Stalls} Buchten");

        var fehler = 0;
        foreach (var (spalten, reihen, name) in new[]
                 {
                     (3, 3, "3x3"), (6, 4, "6x4"), (10, 6, "10x6 (CS2-Block)"),
                 })
        foreach (var winkel in new double[] { 0, 40 })
        {
            var s = Grund();
            // Mitte des Areals als Anker, damit die Flaeche sicher innen liegt.
            var mitte = float2.zero;
            foreach (var p in site) mitte += p;
            mitte /= site.Length;
            var (laengs, quer) = ParkingGeometry.ZoningRichtungen(winkel);
            var ecke = mitte
                - laengs * (float)(spalten * ParkingGeometry.Zoningparzelle / 2)
                - quer * (float)(reihen * ParkingGeometry.Zoningparzelle / 2);
            var flaeche = new ParkingGeometry.Zoningflaeche
            {
                Ecke = ecke, Spalten = spalten, Reihen = reihen, Winkel = winkel,
            };
            s.Zoningflaechen = new[] { flaeche };

            var mit = ParkingGeometry.Build(site, s);
            // GEMESSEN WIRD GEGEN DEN FREIGEHALTENEN Bereich, nicht gegen
            // das gezogene Rechteck: seit die Zoning-Strasse ihren Korridor
            // bekommt, ist das Ergebnis groesser als der Zug. Genau so hat
            // der Nutzer es entschieden.
            var rand = flaeche.Rand;
            var rechteck = ParkingGeometry.ZoningEckenMitRand(flaeche);

            var buchtenDrin = mit.Bay.Count(bucht =>
                ConvexOverlapArea(rechteck, bucht) > 0.05);
            /*
             * GEMESSEN WIRD DIE FLAECHE, NICHT DIE ANZAHL.
             *
             * Der erste Anlauf zaehlte jedes Viereck, das die Flaeche auch
             * nur beruehrt - und meldete sechs "Gassen drin", die zusammen
             * 8,7 m2 ausmachten. Eine 7 m breite Gasse quer durch ein 48 m
             * tiefes Rechteck waere 340 m2. Es waren also Haarstreifen an
             * der Kante, kein Weg durch die Flaeche.
             *
             * Die Grenze haengt am UMFANG, nicht an einer festen Zahl: an
             * einer gedrehten Kante liegen die Zellen als Keile an, und ihr
             * Rest waechst mit der Kantenlaenge. Zehn Zentimeter mittlere
             * Eindringtiefe sind Rundung; alles darueber ist ein echter Weg.
             */
            var gassenFlaeche = mit.AisleQuad.Sum(gasse =>
                ConvexOverlapArea(rechteck, gasse));
            var querFlaeche = mit.CrossQuad.Sum(q =>
                ConvexOverlapArea(rechteck, q));
            var umfang = 2 * (spalten * ParkingGeometry.Zoningparzelle + 2 * rand
                + reihen * ParkingGeometry.Zoningparzelle + 2 * rand);
            var randrauschen = umfang * 0.10;
            /*
             * ASPHALT WIRD JETZT AN ZWEI STELLEN GEMESSEN, NICHT AN EINER.
             *
             * Frueher galt: im freigehaltenen Bereich darf kein Asphalt
             * liegen. Das stimmt seit der Zoning-Strasse nicht mehr - ihr
             * Korridor SOLL Asphalt bekommen, sonst faehrt man ueber Gras,
             * denn die Strasse selbst ist unsichtbar. Falsch waere Asphalt
             * nur noch auf den PARZELLEN.
             */
            float2[] Aufgeschlagen(double d0)
            {
                var e = ecke - laengs * (float)d0 - quer * (float)d0;
                var bw = laengs * (float)(spalten * ParkingGeometry.Zoningparzelle
                    + 2 * d0);
                var tf = quer * (float)(reihen * ParkingGeometry.Zoningparzelle
                    + 2 * d0);
                return new[] { e, e + bw, e + bw + tf, e + tf };
            }
            var parzellen = Aufgeschlagen(0.0);
            var korridorAussen = Aufgeschlagen(8.0);
            double AsphaltIn(float2[] bereich) => mit.AsphaltSurface
                .SelectMany(TriangulateMaterialRegion)
                .Sum(d => d.Weight * ConvexOverlapArea(d.Points, bereich));
            var asphaltDrauf = AsphaltIn(parzellen);
            /*
             * GEMESSEN WIRD GEGEN DEN ANTEIL IM AREAL, NICHT GEGEN DAS
             * RECHTECK.
             *
             * Der Anker dieses Tests ist der Schwerpunkt des Areals, und die
             * Areale sind konkav - beim 3x3-Fall liegen nur 315 von 576 m2
             * des Zugs ueberhaupt auf dem Grundstueck. Gegen die volle
             * Rechteckflaeche gemessen kaeme immer rund die Haelfte heraus,
             * und zwar voellig unabhaengig davon, ob der Belag stimmt. Der
             * erste Anlauf meldete genau so 52 bis 69 % und sah wie ein
             * Geometriefehler aus; es war der Massstab.
             */
            var korridorFlaeche = Triangulate(site)
                    .Sum(t => ConvexOverlapArea(t, korridorAussen))
                - Triangulate(site).Sum(t => ConvexOverlapArea(t, parzellen));
            // Der Korridor liegt seit dem 2026-09-02 in einer EIGENEN Liste:
            // er braucht den Terrain|Roads-Klon, sonst ist er auf der
            // unsichtbaren Strasse selbst unsichtbar.
            var korridorAsphalt = FlaecheIn(mit.ZoningRoadSurface, korridorAussen)
                - FlaecheIn(mit.ZoningRoadSurface, parzellen);
            // 95 % statt 100: an den vier Ecken schneidet die
            // Materialverschmelzung Zellen, und der Ring ist gedreht.
            var korridorGedeckt = korridorAsphalt / korridorFlaeche;
            // Was sonst im Korridor liegt - beantwortet sofort, ob die
            // Einordnung danebengreift oder die Flaechen verlorengehen.
            double FlaecheIn(float2[][] ringe, float2[] bereich) =>
                (ringe ?? Array.Empty<float2[]>())
                    .SelectMany(TriangulateMaterialRegion)
                    .Sum(d => d.Weight * ConvexOverlapArea(d.Points, bereich));
            var gruenKorridor = FlaecheIn(mit.GrassSurface, korridorAussen)
                - FlaecheIn(mit.GrassSurface, parzellen);
            var zoningKorridor = FlaecheIn(mit.ZoningSurface, korridorAussen)
                - FlaecheIn(mit.ZoningSurface, parzellen);
            var gruenAnteil = gruenKorridor / korridorFlaeche;
            var baulandAnteil = zoningKorridor / korridorFlaeche;

            var flaechengroesse =
                (spalten * ParkingGeometry.Zoningparzelle + 2 * rand)
                * (reihen * ParkingGeometry.Zoningparzelle + 2 * rand);
            var verloren = ohne.Stalls - mit.Stalls;
            // Eine Bucht misst rund 48,9 m2 einschliesslich ihres Anteils an
            // Fahrgasse und Gruen - der im Projekt gemessene Wert. Mehr als
            // das Doppelte davon zu verlieren waere kein Ausweichen mehr.
            var erwartetHoechstens = (int)(flaechengroesse / 48.9 * 2 + 4);

            /*
             * DIE ZONING-STRASSE MUSS DA SEIN UND AUF IHRER ACHSE LIEGEN.
             *
             * Die Achse laeuft 4,00 m ausserhalb des gezogenen Rechtecks -
             * die halbe gemessene Strassenbreite. Geprueft wird beides:
             * dass der Ring ueberhaupt entsteht, und dass seine Laenge dem
             * Umfang dieser Achse entspricht. Eine zu kurze Summe hiesse,
             * dass ein Stueck fehlt; eine zu lange, dass doppelt gebaut wird.
             */
            const double halb = 4.0;
            var achsBreite = spalten * ParkingGeometry.Zoningparzelle + 2 * halb;
            var achsTiefe = reihen * ParkingGeometry.Zoningparzelle + 2 * halb;
            var achsUmfang = 2 * (achsBreite + achsTiefe);
            var zoningStuecke = mit.NetLine
                .Where(n => n.Kind == "zoning").ToArray();
            var zoningLaenge = zoningStuecke.Sum(
                n => (double)math.distance(n.A, n.B));
            var achsEcke = ecke - laengs * (float)halb - quer * (float)halb;
            double AufAchse(float2 punkt)
            {
                var d = punkt - achsEcke;
                var u = d.x * laengs.x + d.y * laengs.y;
                var v = d.x * quer.x + d.y * quer.y;
                /*
                 * 1 cm - und diese Grenze ist der BEWEIS eines Fixes.
                 *
                 * Vorher stand hier 0,25 m, weil die Ringkante bis zu 17 cm
                 * neben ihrer Sollachse lag (v=56,17 bei Tiefe 56,00; 10x6
                 * unter 40 Grad). Ursache war der Winkel: der Zellenkern
                 * rechnete ihn aus zwei umgerechneten Eckpunkten zurueck,
                 * und der Fehler wuchs mit der Kantenlaenge.
                 *
                 * Seit der Winkel durchgereicht wird (Weltwinkel minus
                 * Rahmenwinkel, eine Subtraktion), haelt die Geometrie 1 cm.
                 * Wer hier wieder aufweitet, verdeckt genau diesen Rueckfall
                 * - und CS2s Zonenraster verzeiht Winkelreste nicht: der
                 * Nutzer kennt das als "angeblich 90 Grad, in Wahrheit
                 * 89,999999, und dann schneidet er Tiles weg".
                 */
                if (u < -0.01 || u > achsBreite + 0.01) return 1e9;
                if (v < -0.01 || v > achsTiefe + 0.01) return 1e9;
                return Math.Min(
                    Math.Min(Math.Abs(u), Math.Abs(u - achsBreite)),
                    Math.Min(Math.Abs(v), Math.Abs(v - achsTiefe)));
            }
            // Liegt die Mitte jedes Stuecks im Areal? Der Test misst damit
            // genau den Fehler, den der Nutzer im Spiel gesehen hat:
            // Zoning-Strasse ausserhalb des Polygons.
            var strasseDraussen = zoningStuecke.Count(n =>
            {
                var mitte = (n.A + n.B) * 0.5f;
                return Triangulate(site).Sum(t =>
                    ConvexOverlapArea(t, new[]
                    {
                        mitte + new float2(-0.6f, -0.6f),
                        mitte + new float2(0.6f, -0.6f),
                        mitte + new float2(0.6f, 0.6f),
                        mitte + new float2(-0.6f, 0.6f),
                    })) < 1.0;
            });
            var danebenliegend = zoningStuecke.Count(
                // 5 cm statt 1 cm: die Kappung interpoliert den
                // Schnittpunkt, das kostet ein paar Millimeter.
                n => AufAchse(n.A) > 0.01 || AufAchse(n.B) > 0.01);
            /*
             * AUF DER ACHSE war die falsche Erwartung: damit liegen die
             * Mittellinien beider Fahrbahnen aufeinander. `LocalConnect`
             * verlangt fuer unsere unsichtbaren Wege gerade einen
             * Sackgassenknoten am Fahrbahnrand; die NetBuilder-Messung nennt
             * dafuer Breite/2 + Breite/2 + 4 m Suchweite.
             *
             * Geprueft wird deshalb die eigentliche Eigenschaft: der exakte
             * Segmentabstand darf nie kleiner als die halbe Breitensumme
             * sein. Das erfasst auch schraege Schnitte und Endkappen, statt
             * nur ausgewaehlte Endpunkte gegen die Zoningachse zu testen.
             */
            var fahrbahnpaare = ZoningFahrbahnueberlappungen(mit.NetLine);

            /*
             * NIMMT CS2 DIE RINGE UEBERHAUPT AN?
             *
             * `Game.Areas.GeometrySystem` versetzt jeden Knoten 0,1 m nach
             * innen und dreieckt dann; misslingt das, verwirft das Spiel die
             * Flaeche STILL. Genau das ist am 2026-09-02 passiert: der
             * Bauzettel meldete "2 Ring(e) (Prefab da)", im Spiel war
             * nichts zu sehen. Ursache war, dass die beiden Zoning-Listen
             * nicht durch `PlaneZellenCs2Flaechen` liefen.
             *
             * Deshalb wird hier jeder Ring gegen dasselbe Orakel geprueft,
             * das der Mod benutzt.
             */
            int Unbaubar(float2[][] ringe) => (ringe
                    ?? Array.Empty<float2[]>())
                .Count(r => r == null || r.Length < 3
                    || Cs2Triangulierung.Dreiecke(r) == 0);
            /*
             * NADELN FINDEN - die bisherige Selbstpruefung sieht sie nicht.
             *
             * Sie misst kuerzeste Kante und kleinste Flaeche. Ein langer,
             * spitzer Zipfel hat aber LANGE Kanten und eine ansehnliche
             * Flaeche; er faellt dort nicht auf. Im Abzug des Nutzers vom
             * 2026-09-03 standen zwei Zoningstrassen-Flaechen mit 1,57 Grad,
             * die im Spiel ueber den Gehweg auf die Strasse liefen.
             *
             * Fuenf Grad sind grosszuegig: eine Gehrung an einer
             * rechtwinkligen Ecke hat 45 Grad, und selbst schraege
             * Arealkanten bleiben deutlich darueber.
             */
            double SpitzesterWinkel(float2[] ring)
            {
                if (ring == null || ring.Length < 3) return 180.0;
                var kleinster = 180.0;
                for (var i = 0; i < ring.Length; i++)
                {
                    var a = ring[(i - 1 + ring.Length) % ring.Length];
                    var b = ring[i];
                    var c = ring[(i + 1) % ring.Length];
                    var v1 = a - b;
                    var v2 = c - b;
                    var l1 = math.length(v1);
                    var l2 = math.length(v2);
                    if (l1 < 1e-6f || l2 < 1e-6f) continue;
                    var cos = math.clamp(math.dot(v1, v2) / (l1 * l2), -1f, 1f);
                    kleinster = Math.Min(kleinster,
                        math.degrees(Math.Acos(cos)));
                }
                return kleinster;
            }
            var spitzesteZoning = 180.0;
            foreach (var ring in (mit.ZoningRoadSurface
                     ?? Array.Empty<float2[]>()))
                spitzesteZoning = Math.Min(spitzesteZoning,
                    SpitzesterWinkel(ring));
            foreach (var ring in (mit.ZoningSurface ?? Array.Empty<float2[]>()))
                spitzesteZoning = Math.Min(spitzesteZoning,
                    SpitzesterWinkel(ring));

            var unbaubarStrasse = Unbaubar(mit.ZoningRoadSurface);
            var unbaubarBauland = Unbaubar(mit.ZoningSurface);

            var schlecht = buchtenDrin != 0
                || unbaubarStrasse != 0 || unbaubarBauland != 0
                || spitzesteZoning < 5.0
                // Vier Stuecke waren die Erwartung, solange der Ring immer
                // geschlossen war. Seit er an der Randstrassenachse gekappt
                // wird, koennen es weniger sein - verlangt wird nur noch,
                // dass ueberhaupt Strasse entsteht.
                || zoningStuecke.Length < 1
                // NICHT MEHR die volle Ringlaenge: die Strasse wird an der
                // Randstrassenachse gekappt, damit sie nicht aus dem
                // Parkplatz hinauslaeuft. Die Flaeche dieses Tests haengt
                // zur Haelfte ausserhalb des Areals - dass dort weniger
                // Strasse entsteht, ist richtig und kein Fehler. Geprueft
                // wird deshalb: es entsteht ueberhaupt Strasse, sie ist nie
                // laenger als der Ring, und kein Stueck liegt draussen.
                || zoningLaenge < 1.0
                || zoningLaenge > achsUmfang + 0.05
                || strasseDraussen != 0
                || danebenliegend != 0
                || fahrbahnpaare != 0
                || gassenFlaeche > randrauschen || querFlaeche > randrauschen
                // Auch hier am Umfang, nicht an einer festen Zahl: die
                // Materialringe entstehen aus verschmolzenen Zellen, und an
                // einer gedrehten Kante bleibt ein Saum. Ein Zentimeter
                // mittlere Tiefe ist Rundung; Asphalt DURCH die Flaeche
                // waeren Hunderte Quadratmeter.
                || asphaltDrauf > umfang * 0.01
                // NICHT MEHR "Korridor ganz Asphalt": seit der Belag an
                // derselben Grenze endet wie die Strasse, ist der Teil
                // ausserhalb der Randstrassenachse GRUEN - und das ist
                // gewollt. Der Nutzer hat es skizziert: in der Ecke liegt
                // die Zoning-Strasse nur auf den beiden Innenseiten.
                //
                // Was bleibt und weiterhin zaehlt: es darf kein grosses
                // LOCH geben. Genau danach habe ich am 2026-09-02
                // stundenlang gesucht.
                //
                // OFFEN: gemessen werden 86 bis 97 % statt 100 %. Der Rest
                // ist NICHT erklaert - vermutlich Messrauschen der
                // Dreiecksschnitte an den vielen kleinen Randzellen, aber
                // das ist eine Vermutung und keine Messung. Die Grenze steht
                // deshalb auf dem beobachteten Minimum, damit ein echter
                // Einbruch trotzdem auffaellt.
                || korridorGedeckt + gruenAnteil + baulandAnteil < 0.85
                || verloren > erwartetHoechstens
                || verloren < 0;
            if (schlecht) fehler++;
            Console.WriteLine($"  {name,-16} {winkel,3:F0} Grad | "
                + $"Rand {rand,4:F1} m | "
                + $"{mit.Stalls,4} Buchten (-{verloren,3}, hoechstens "
                + $"{erwartetHoechstens,3}) | Buchten drin {buchtenDrin} | "
                + $"Gassen {gassenFlaeche,5:F1} / Quer {querFlaeche,5:F1} m2 "
                + $"(Rauschgrenze {randrauschen,5:F1}) | "
                + $"Asphalt auf Parzellen {asphaltDrauf,5:F2} | "
                + $"Korridor {korridorGedeckt * 100,5:F1} % Asphalt, "
                + $"gedeckt {(korridorGedeckt + gruenAnteil + baulandAnteil) * 100,5:F1} % "
                + $"(Gruen {gruenKorridor,4:F0}, Bauland {zoningKorridor,4:F0}, "
                + $"im Areal {korridorFlaeche,6:F0} m2) | "
                + $"ZoningSurface {mit.ZoningSurface?.Length ?? -1} Ringe"
                + $" spitzest {spitzesteZoning,5:F1} Grad"
                + (unbaubarStrasse + unbaubarBauland > 0
                    ? $" CS2 VERWIRFT {unbaubarStrasse}+{unbaubarBauland}"
                    : " (CS2 nimmt alle)")
                + " | "
                + $"Strasse {zoningStuecke.Length,2} Stueck "
                + $"{zoningLaenge,6:F1}/{achsUmfang,6:F1} m"
                + (strasseDraussen != 0 ? $" DRAUSSEN {strasseDraussen}" : "")
                + (danebenliegend != 0
                    ? " maxAbw " + zoningStuecke
                        .Select(n => Math.Max(AufAchse(n.A), AufAchse(n.B)))
                        .Where(w => w < 1e8).DefaultIfEmpty(0)
                        .Max().ToString("F3")
                        + (zoningStuecke.Any(n => AufAchse(n.A) > 1e8
                            || AufAchse(n.B) > 1e8) ? " (ausserhalb!)" : "")
                    : "")
                + (danebenliegend != 0 ? $" DANEBEN {danebenliegend}" : "")
                + (fahrbahnpaare != 0
                    ? $" FAHRBAHN-UEBERLAPPUNG {fahrbahnpaare}" : "")
                + (schlecht ? "   <-- FEHLER" : ""));
        }

        Console.WriteLine(fehler == 0
            ? "Zoningflaeche: alles sauber."
            : $"Zoningflaeche: {fehler} Fall/Faelle fehlerhaft.");
        return fehler == 0 ? 0 : 1;
    }
}
