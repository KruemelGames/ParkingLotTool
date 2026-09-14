using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static partial class ParkingGeometry
    {
        /** Eine geometrische Basis, entlang der eine gesperrte ZF gleiten kann. */
        public readonly struct ZoningBewegungsachse
        {
            public readonly float2 Richtung;
            public readonly string Name;

            public ZoningBewegungsachse(float2 richtung, string name)
            {
                Richtung = richtung;
                Name = name ?? string.Empty;
            }
        }

        /** Messwerte der reinen Begrenzung; der Werkzeugpfad schreibt daraus ins Log. */
        public sealed class ZoningBegrenzung
        {
            public float2 Versatz;
            public int Pruefungen;
            public string Basis = "keine";
            public float2 Basisachse;
            public float Restquadrat;
            public bool AusgangUngueltig;
            public bool BudgetErschoepft;
            public int Befreiungsring;
        }

        /** Messwerte der reinen Kandidatensuche. */
        public sealed class ZoningRastung
        {
            public float2 Versatz;
            public bool Gefunden;
            public int Achsen;
            public int Nachbar = -1;
            public int NachbarnInReichweite;
            public int Angeboten;
            public int Pruefungen;
            public int ZuWeit;
            public int Verboten;
            public float KleinsteVerworfene = float.MaxValue;
            public float KleinsteVerbotene = float.MaxValue;
            public float2 VerboteneEcke;
            public bool HatVerbotene;
            public float Korrekturquadrat = float.MaxValue;
            public float Mausabstandquadrat = float.MaxValue;
        }

        /**
         * Begrenzt einen absoluten Mauswunsch auf einen erlaubten Bewegungsweg.
         *
         * Die Zulassungsregeln kommen als Pruefer hinein. Damit kennt dieser
         * Rechenweg weder CS2 noch das Werkzeug und derselbe Code laeuft im
         * Matrixtest. Die Achsen werden vorher aus den wirklichen Waenden
         * (Umriss, Randzoning und ZF) konstruiert; hier wird nur noch geloest.
         */
        public static ZoningBegrenzung ZoningVersatzBegrenzen(
            Zoningflaeche flaeche,
            float2 wunsch,
            Func<Zoningflaeche, bool> erlaubt,
            Func<IReadOnlyList<ZoningBewegungsachse>> achsenQuelle,
            bool rasterBefreiung = true,
            int maximaleRasterringe = 50,
            int pruefbudget = 1500)
        {
            var messung = new ZoningBegrenzung
            {
                Versatz = float2.zero,
                Restquadrat = math.lengthsq(wunsch),
            };
            if (math.lengthsq(wunsch) < 1e-12f) return messung;

            bool Geht(float2 versatz)
            {
                messung.Pruefungen++;
                return erlaubt(ZoningVerschoben(flaeche, versatz));
            }

            if (Geht(wunsch))
            {
                messung.Versatz = wunsch;
                messung.Restquadrat = 0f;
                return messung;
            }

            if (!Geht(float2.zero))
            {
                messung.AusgangUngueltig = true;
                if (!rasterBefreiung) return messung;

                var kachel = (float)Zoningparzelle;
                var geprueftVorher = messung.Pruefungen;
                for (var schritt = 1; schritt <= maximaleRasterringe; schritt++)
                {
                    var beste = float2.zero;
                    var bestesQuadrat = float.MaxValue;
                    void Pruefe(int u, int v)
                    {
                        if (messung.Pruefungen - geprueftVorher >= pruefbudget) return;
                        var kandidat = wunsch
                            + ZoningRichtungen(flaeche.Winkel).Laengs * (u * kachel)
                            + ZoningRichtungen(flaeche.Winkel).Quer * (v * kachel);
                        if (!Geht(kandidat)) return;
                        var quadrat = math.lengthsq(kandidat - wunsch);
                        if (quadrat >= bestesQuadrat) return;
                        beste = kandidat;
                        bestesQuadrat = quadrat;
                    }

                    for (var u = -schritt; u <= schritt; u++)
                    {
                        Pruefe(u, -schritt);
                        Pruefe(u, schritt);
                    }
                    for (var v = -schritt + 1; v < schritt; v++)
                    {
                        Pruefe(-schritt, v);
                        Pruefe(schritt, v);
                    }
                    if (bestesQuadrat < float.MaxValue)
                    {
                        messung.Versatz = beste;
                        messung.Restquadrat = bestesQuadrat;
                        messung.Befreiungsring = schritt;
                        return messung;
                    }
                    if (messung.Pruefungen - geprueftVorher >= pruefbudget)
                    {
                        messung.BudgetErschoepft = true;
                        messung.Befreiungsring = schritt;
                        return messung;
                    }
                }
                return messung;
            }

            float2 Groesster(float2 basis, float2 richtung)
            {
                if (math.lengthsq(richtung) < 1e-12f) return float2.zero;
                if (Geht(basis + richtung)) return richtung;
                var unten = 0f;
                var oben = 1f;
                for (var i = 0; i < 20; i++)
                {
                    var mitte = (unten + oben) * 0.5f;
                    if (Geht(basis + richtung * mitte)) unten = mitte;
                    else oben = mitte;
                }
                return richtung * unten;
            }

            void PruefeWeg(float2 weg, ZoningBewegungsachse basis)
            {
                if (!Geht(weg)) return;
                var restquadrat = math.lengthsq(wunsch - weg);
                if (restquadrat > messung.Restquadrat - 1e-7f) return;
                messung.Versatz = weg;
                messung.Basis = basis.Name;
                messung.Basisachse = basis.Richtung;
                messung.Restquadrat = restquadrat;
            }

            // Kanten sammeln und normalisieren kostet bei grossen Polygonen.
            // Der freie Schnellweg oben braucht keine einzige davon, deshalb
            // wird die Liste erst nach dem ersten Verbot angefordert.
            var achsen = achsenQuelle?.Invoke();
            if (achsen != null)
            for (var i = 0; i < achsen.Count; i++)
            {
                var achseA = achsen[i].Richtung;
                if (math.lengthsq(achseA) < 1e-9f) continue;
                achseA = math.normalize(achseA);
                var achseB = new float2(-achseA.y, achseA.x);
                var anteilA = achseA * math.dot(wunsch, achseA);
                var anteilB = achseB * math.dot(wunsch, achseB);
                var basis = new ZoningBewegungsachse(achseA, achsen[i].Name);

                var ersteA = Groesster(float2.zero, anteilA);
                PruefeWeg(ersteA + Groesster(ersteA, anteilB), basis);
                var ersteB = Groesster(float2.zero, anteilB);
                PruefeWeg(ersteB + Groesster(ersteB, anteilA), basis);
            }
            return messung;
        }

        /**
         * Waehlt ein Raster- oder Kontaktziel um die ERREICHBARE Rohposition.
         *
         * Die Nachbarin und die Flaechengroesse definieren die Zielmengen:
         * gemeinsame Rasterpunkte sowie Linien mit 0/8/16/24 m Kantenabstand.
         * Der Mauswunsch bestimmt nur die Rangfolge. Der Fangbereich wird vom
         * erreichbaren Anker gemessen; ein Zeiger ausserhalb des Polygons kann
         * deshalb keine gueltigen Ziele mehr aus dem Kandidatensatz druecken.
         */
        public static ZoningRastung ZoningAnNachbarnRasten(
            Zoningflaeche flaeche,
            float2 mauswunsch,
            float2 erreichbarerVersatz,
            IReadOnlyList<Zoningflaeche> nachbarn,
            Func<Zoningflaeche, bool> erlaubt,
            float2? altesAbsolutesZiel = null,
            float rastreichweite = 24f)
        {
            var ergebnis = new ZoningRastung { Versatz = erreichbarerVersatz };
            if (nachbarn == null || nachbarn.Count == 0) return ergebnis;

            var (laengs, quer) = ZoningRichtungen(flaeche.Winkel);
            var kachel = (float)Zoningparzelle;
            var fangradius = kachel * math.sqrt(0.5f) + 1e-3f;
            var fangquadrat = fangradius * fangradius;
            var ankerEcke = flaeche.Ecke + erreichbarerVersatz;
            var mausEcke = flaeche.Ecke + mauswunsch;
            var ankerFlaeche = ZoningVerschoben(flaeche, erreichbarerVersatz);
            var ankerEcken = ZoningEcken(ankerFlaeche);
            var breite = (float)(flaeche.Spalten * Zoningparzelle);
            var tiefe = (float)(flaeche.Reihen * Zoningparzelle);
            var altesGefunden = false;
            var altesMausquadrat = float.MaxValue;
            var altesKorrekturquadrat = float.MaxValue;
            var alteAchsen = 0;
            var alteNachbarin = -1;

            void Pruefe(float2 ecke, int achsenZahl, int nachbarIndex)
            {
                var korrekturquadrat = math.distancesq(ecke, ankerEcke);
                ergebnis.Angeboten++;
                if (korrekturquadrat > fangquadrat)
                {
                    ergebnis.ZuWeit++;
                    ergebnis.KleinsteVerworfene = math.min(
                        ergebnis.KleinsteVerworfene, korrekturquadrat);
                    return;
                }

                var probe = ZoningVerschoben(flaeche, ecke - flaeche.Ecke);
                ergebnis.Pruefungen++;
                if (!erlaubt(probe))
                {
                    ergebnis.Verboten++;
                    ergebnis.KleinsteVerworfene = math.min(
                        ergebnis.KleinsteVerworfene, korrekturquadrat);
                    if (korrekturquadrat < ergebnis.KleinsteVerbotene)
                    {
                        ergebnis.KleinsteVerbotene = korrekturquadrat;
                        ergebnis.VerboteneEcke = ecke;
                        ergebnis.HatVerbotene = true;
                    }
                    return;
                }

                var mausquadrat = math.distancesq(ecke, mausEcke);
                if (altesAbsolutesZiel.HasValue
                    && math.distancesq(ecke, altesAbsolutesZiel.Value) < 1e-6f)
                {
                    altesGefunden = true;
                    altesMausquadrat = mausquadrat;
                    altesKorrekturquadrat = korrekturquadrat;
                    alteAchsen = achsenZahl;
                    alteNachbarin = nachbarIndex;
                }

                // Ein voller Rasterpunkt gewinnt vor einer Kontaktlinie. Erst
                // innerhalb derselben Art entscheidet die Naehe zum Mausziel.
                if (achsenZahl < ergebnis.Achsen
                    || (achsenZahl == ergebnis.Achsen
                        && mausquadrat >= ergebnis.Mausabstandquadrat - 1e-6f))
                    return;
                ergebnis.Gefunden = true;
                ergebnis.Achsen = achsenZahl;
                ergebnis.Nachbar = nachbarIndex;
                ergebnis.Versatz = ecke - flaeche.Ecke;
                ergebnis.Korrekturquadrat = korrekturquadrat;
                ergebnis.Mausabstandquadrat = mausquadrat;
            }

            var ankerU = math.dot(ankerEcke, laengs);
            var ankerV = math.dot(ankerEcke, quer);
            for (var i = 0; i < nachbarn.Count; i++)
            {
                var andere = nachbarn[i];
                var winkel = Math.Abs(((andere.Winkel - flaeche.Winkel)
                    % 180 + 180) % 180);
                winkel = Math.Min(winkel, 180 - winkel);
                if (winkel > 1e-4) continue;

                var andereEcken = ZoningEcken(andere);
                if (ZoningRechteckAbstand(ankerEcken, andereEcken)
                    > rastreichweite + 1e-3f) continue;
                ergebnis.NachbarnInReichweite++;

                var minU = float.MaxValue;
                var maxU = float.MinValue;
                var minV = float.MaxValue;
                var maxV = float.MinValue;
                foreach (var ecke in andereEcken)
                {
                    var u = math.dot(ecke, laengs);
                    var v = math.dot(ecke, quer);
                    minU = math.min(minU, u);
                    maxU = math.max(maxU, u);
                    minV = math.min(minV, v);
                    maxV = math.max(maxV, v);
                }

                var phaseU = math.dot(andere.Ecke, laengs);
                var phaseV = math.dot(andere.Ecke, quer);
                var u0 = (int)math.floor((ankerU - phaseU) / kachel);
                var v0 = (int)math.floor((ankerV - phaseV) / kachel);
                for (var du = 0; du <= 1; du++)
                for (var dv = 0; dv <= 1; dv++)
                {
                    var u = phaseU + (u0 + du) * kachel;
                    var v = phaseV + (v0 + dv) * kachel;
                    Pruefe(laengs * u + quer * v, 2, i);
                }

                bool UeberdecktQuer(float v) =>
                    v < maxV - 1e-3f && v + tiefe > minV + 1e-3f;
                bool UeberdecktLaengs(float u) =>
                    u < maxU - 1e-3f && u + breite > minU + 1e-3f;

                for (var schritt = 0; schritt <= 3; schritt++)
                {
                    var luecke = schritt * kachel;
                    var links = minU - breite - luecke;
                    var rechts = maxU + luecke;
                    if (UeberdecktQuer(ankerV))
                    {
                        Pruefe(laengs * links + quer * ankerV, 1, i);
                        Pruefe(laengs * rechts + quer * ankerV, 1, i);
                    }

                    var unten = minV - tiefe - luecke;
                    var oben = maxV + luecke;
                    if (UeberdecktLaengs(ankerU))
                    {
                        Pruefe(laengs * ankerU + quer * unten, 1, i);
                        Pruefe(laengs * ankerU + quer * oben, 1, i);
                    }
                }
            }

            // 10 cm liegen ueber den gemessenen 1,7 cm Raycast-Rauschen.
            if (ergebnis.Gefunden && altesGefunden
                && alteAchsen == ergebnis.Achsen
                && math.sqrt(altesMausquadrat)
                    <= math.sqrt(ergebnis.Mausabstandquadrat) + 0.10f)
            {
                ergebnis.Versatz = altesAbsolutesZiel.Value - flaeche.Ecke;
                ergebnis.Nachbar = alteNachbarin;
                ergebnis.Korrekturquadrat = altesKorrekturquadrat;
                ergebnis.Mausabstandquadrat = altesMausquadrat;
            }
            return ergebnis;
        }

        /** Kleinster Abstand zweier gedrehter Rechtecke, 0 bei Beruehrung. */
        public static float ZoningRechteckAbstand(float2[] a, float2[] b)
        {
            if (ZoningRechteckeUeberlappen(a, b)) return 0f;
            var kleinster = float.MaxValue;
            for (var i = 0; i < 4; i++)
            for (var k = 0; k < 4; k++)
                kleinster = math.min(kleinster, ZoningStreckenAbstand(
                    a[i], a[(i + 1) % 4], b[k], b[(k + 1) % 4]));
            return kleinster;
        }

        /** Trennachsentest; Beruehren ist erlaubt, nur echte Flaeche zaehlt. */
        public static bool ZoningRechteckeUeberlappen(float2[] a, float2[] b)
        {
            for (var seite = 0; seite < 4; seite++)
            {
                var ecken = seite < 2 ? a : b;
                var k = seite % 2;
                var kante = ecken[k + 1] - ecken[k];
                if (math.lengthsq(kante) < 1e-9f) continue;
                var achse = math.normalize(new float2(-kante.y, kante.x));
                var aMin = float.MaxValue;
                var aMax = float.MinValue;
                var bMin = float.MaxValue;
                var bMax = float.MinValue;
                for (var i = 0; i < 4; i++)
                {
                    var pa = math.dot(a[i], achse);
                    var pb = math.dot(b[i], achse);
                    aMin = math.min(aMin, pa);
                    aMax = math.max(aMax, pa);
                    bMin = math.min(bMin, pb);
                    bMax = math.max(bMax, pb);
                }
                if (aMax <= bMin + 1e-3f || bMax <= aMin + 1e-3f)
                    return false;
            }
            return true;
        }

        public static float ZoningPunktStrecke(float2 p, float2 a, float2 b)
        {
            var ab = b - a;
            var laenge = math.lengthsq(ab);
            if (laenge < 1e-9f) return math.distance(p, a);
            var t = math.clamp(math.dot(p - a, ab) / laenge, 0f, 1f);
            return math.distance(p, a + ab * t);
        }

        private static float ZoningStreckenAbstand(
            float2 a1, float2 a2, float2 b1, float2 b2) => math.min(
                math.min(ZoningPunktStrecke(a1, b1, b2),
                    ZoningPunktStrecke(a2, b1, b2)),
                math.min(ZoningPunktStrecke(b1, a1, a2),
                    ZoningPunktStrecke(b2, a1, a2)));
    }
}
