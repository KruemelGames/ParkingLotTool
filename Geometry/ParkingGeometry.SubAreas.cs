using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public static partial class ParkingGeometry
    {
        private const double TeilflaechenEps = 1e-6;

        /** Ein formabhaengiger Teil samt fertig abgeleitetem Reihenwinkel. */
        private sealed class TeilflaechenPlan
        {
            internal int Index;
            internal double2[] Polygon;
            internal double Winkel;
            internal bool EigeneZuweisung;
        }

        /** Gemeinsame Kante zweier Teile; die Linie ist ihre volle Ueberdeckung. */
        private sealed class TeilflaechenNaht
        {
            internal int ErstesTeil;
            internal int ZweitesTeil;
            internal Line2 Linie;
        }

        private static double Achsenwinkel(double winkel)
        {
            var normiert = winkel % 180.0;
            return normiert < 0 ? normiert + 180.0 : normiert;
        }

        private static bool VerschiedeneAchsen(double a, double b)
        {
            var differenz = Math.Abs(Achsenwinkel(a) - Achsenwinkel(b));
            differenz = Math.Min(differenz, 180.0 - differenz);
            return differenz > 1e-6;
        }

        private static bool EnthaeltAnker(double2[] polygon, double2 anker)
            => PointIn(anker, polygon)
               || DistToBoundary(anker, polygon) <= 0.01;

        /**
         * Leitet jeden wirksamen Winkel ueber genau dieselbe Funktion ab wie
         * der Einwinkelbau. Der erste Eintrag liefert den Bezug fuer alle
         * Teile; ein Anker im Teil ersetzt ihn dort.
         */
        private static List<TeilflaechenPlan> PlaneTeilflaechen(
            double2[] site,
            LayoutSettings settings)
        {
            /*
             * HANDSCHNITTE SCHLAGEN DIE AUTOMATIK - vollstaendig.
             *
             * Der Nutzer hat sich am 2026-09-01 dafuer entschieden, den
             * Trennschnitt selbst zu ziehen, nachdem die automatische
             * Zerlegung an seiner schiefen T-Form vorbeischnitt und dabei
             * vier Teilflaechen ohne Flaeche erzeugte (siehe `--zerlegung`).
             * Liegt ein Schnitt vor, wird die Automatik nicht befragt.
             */
            var polygone = settings?.Teilflaechenschnitte != null
                && settings.Teilflaechenschnitte.Length != 0
                ? TeilflaechenAusSchnitten(site, settings.Teilflaechenschnitte)
                : Teilflaechen(site);
            var zuweisungen = settings?.TeilflaechenAusrichtungen?
                .Where(zuweisung => zuweisung != null
                    && !double.IsNaN(zuweisung.Winkel)
                    && !double.IsInfinity(zuweisung.Winkel))
                .ToArray() ?? Array.Empty<TeilflaechenAusrichtung>();
            var ausgabe = new List<TeilflaechenPlan>(polygone.Length);

            for (var index = 0; index < polygone.Length; index++)
            {
                var polygon = polygone[index];
                var eigene = -1;
                if (zuweisungen.Length != 0)
                    for (var z = 0; z < zuweisungen.Length; z++)
                    {
                        var anker = new double2(
                            zuweisungen[z].Anker.x, zuweisungen[z].Anker.y);
                        if (!EnthaeltAnker(polygon, anker)) continue;
                        eigene = z;
                        break;
                    }

                var teilSettings = settings.Clone();
                if (zuweisungen.Length != 0)
                    teilSettings.Ausrichtwinkel = zuweisungen[
                        eigene >= 0 ? eigene : 0].Winkel;
                var winkel = Reihenwinkel(
                    teilSettings, LongestEdgeAngle(polygon));
                ausgabe.Add(new TeilflaechenPlan
                {
                    Index = index,
                    Polygon = polygon,
                    Winkel = Achsenwinkel(winkel),
                    EigeneZuweisung = eigene >= 0,
                });
            }
            return ausgabe;
        }

        private static bool HatTeilflaechenVorgaben(LayoutSettings settings)
            => settings?.TeilflaechenAusrichtungen != null
               && settings.TeilflaechenAusrichtungen.Any(x => x != null);

        private static List<TeilflaechenNaht> PlaneTeilflaechenNaehte(
            IReadOnlyList<TeilflaechenPlan> teile,
            bool nurVerschiedeneWinkel)
        {
            var ausgabe = new List<TeilflaechenNaht>();
            var gesehen = new HashSet<string>(StringComparer.Ordinal);
            for (var erstes = 0; erstes < teile.Count; erstes++)
                for (var zweites = erstes + 1; zweites < teile.Count; zweites++)
                {
                    if (nurVerschiedeneWinkel
                        && !VerschiedeneAchsen(
                            teile[erstes].Winkel, teile[zweites].Winkel))
                        continue;
                    foreach (var linie in GemeinsameKanten(
                        teile[erstes].Polygon, teile[zweites].Polygon))
                    {
                        var a = linie.A;
                        var b = linie.B;
                        if (a.x > b.x || (Math.Abs(a.x - b.x) <= TeilflaechenEps
                            && a.y > b.y)) (a, b) = (b, a);
                        var schluessel = Math.Round(a.x, 6) + ":"
                            + Math.Round(a.y, 6) + ":"
                            + Math.Round(b.x, 6) + ":"
                            + Math.Round(b.y, 6);
                        if (!gesehen.Add(schluessel)) continue;
                        ausgabe.Add(new TeilflaechenNaht
                        {
                            ErstesTeil = erstes,
                            ZweitesTeil = zweites,
                            Linie = new Line2(a, b),
                        });
                    }
                }
            return ausgabe;
        }

        /** Auch eine in mehrere Teilkanten zerlegte gemeinsame Gerade zaehlt. */
        private static IEnumerable<Line2> GemeinsameKanten(
            double2[] erstes,
            double2[] zweites)
        {
            for (var i = 0; i < erstes.Length; i++)
            {
                var a = erstes[i];
                var b = erstes[(i + 1) % erstes.Length];
                var ab = b - a;
                var laenge = Len(ab);
                if (laenge <= TeilflaechenEps) continue;
                var u = ab / laenge;
                for (var j = 0; j < zweites.Length; j++)
                {
                    var c = zweites[j];
                    var d = zweites[(j + 1) % zweites.Length];
                    var cd = d - c;
                    var zweiteLaenge = Len(cd);
                    if (zweiteLaenge <= TeilflaechenEps) continue;
                    var parallel = Math.Abs(u.x * cd.y - u.y * cd.x)
                        / zweiteLaenge;
                    var abstandC = Math.Abs(u.x * (c.y - a.y)
                        - u.y * (c.x - a.x));
                    var abstandD = Math.Abs(u.x * (d.y - a.y)
                        - u.y * (d.x - a.x));
                    if (parallel > TeilflaechenEps
                        || abstandC > TeilflaechenEps
                        || abstandD > TeilflaechenEps) continue;
                    var tc = (c.x - a.x) * u.x + (c.y - a.y) * u.y;
                    var td = (d.x - a.x) * u.x + (d.y - a.y) * u.y;
                    var lo = Math.Max(0, Math.Min(tc, td));
                    var hi = Math.Min(laenge, Math.Max(tc, td));
                    if (hi - lo <= TeilflaechenEps) continue;
                    yield return new Line2(a + u * lo, a + u * hi);
                }
            }
        }

        /**
         * Zaehlt nur Innenbuchten: Aussenring und Randreihen sind absichtlich
         * ein gemeinsamer Bau und gehoeren deshalb keinem Teilraster.
         */
        private static void SetzeTeilflaechenStatistik(
            ParkingLayout layout,
            double2[] site,
            LayoutSettings settings,
            IReadOnlyList<TeilflaechenPlan> geplanterWinkel = null)
        {
            if (layout == null || site == null || site.Length < 3) return;
            var plaene = geplanterWinkel?.ToList()
                ?? PlaneTeilflaechen(site, settings);
            var zaehler = new int[plaene.Count];
            for (var bucht = 0; bucht < layout.Bay.Length; bucht++)
            {
                if (bucht >= layout.BayKind.Length
                    || layout.BayKind[bucht] != BayKind.Inner) continue;
                var polygon = layout.Bay[bucht];
                if (polygon == null || polygon.Length == 0) continue;
                var mitte = double2.zero;
                foreach (var punkt in polygon)
                    mitte += new double2(punkt.x, punkt.y);
                mitte /= polygon.Length;
                for (var teil = 0; teil < plaene.Count; teil++)
                    if (EnthaeltAnker(plaene[teil].Polygon, mitte))
                    {
                        zaehler[teil]++;
                        break;
                    }
            }

            layout.Teilflaechen = plaene.Select((plan, index) =>
                new TeilflaechenBauInfo
                {
                    Index = plan.Index,
                    Winkel = plan.Winkel,
                    Innenbuchten = zaehler[index],
                    EigeneZuweisung = plan.EigeneZuweisung,
                }).ToArray();
        }
    }
}
