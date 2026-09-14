using System;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    // Reine Zettelmessung: keine Zulassung, keine Reparatur im Bauweg.
    public static class ZoningGassenabstand
    {
        public static string Beschreibe(ParkingLayout layout, LayoutSettings settings)
        {
            double engste = double.PositiveInfinity;
            int gasse = -1, kurs = -1;
            var gassen = layout?.AisleLine ?? Array.Empty<float2[]>();
            var netz = layout?.NetLine ?? Array.Empty<NetSegment>();
            for (int g = 0; g < gassen.Length; g++)
            {
                var linie = gassen[g];
                if (linie == null || linie.Length < 2) continue;
                var a = linie[0]; var b = linie[linie.Length - 1];
                var gd = b - a;
                for (int n = 0; n < netz.Length; n++)
                {
                    var r = netz[n];
                    if (r.Kind != "zoning") continue;
                    var d = r.B - r.A;
                    double len = math.length(d), gl = math.length(gd);
                    if (len < 1e-6 || gl < 1e-6) continue;
                    if (Math.Abs(d.x * gd.y - d.y * gd.x) > 0.01 * len * gl) continue;
                    double ta = math.dot(a - r.A, d) / len;
                    double tb = math.dot(b - r.A, d) / len;
                    double von = Math.Max(0, Math.Min(ta, tb));
                    double bis = Math.Min(len, Math.Max(ta, tb));
                    if (bis - von < 1e-6) continue;
                    double da = (d.x * (a.y - r.A.y) - d.y * (a.x - r.A.x)) / len;
                    double db = (d.x * (b.y - r.A.y) - d.y * (b.x - r.A.x)) / len;
                    double dv = da + (db - da) * (von - ta) / (tb - ta);
                    double de = da + (db - da) * (bis - ta) / (tb - ta);
                    double abstand = dv * de <= 0 ? 0 : Math.Min(Math.Abs(dv), Math.Abs(de));
                    if (abstand >= engste) continue;
                    engste = abstand; gasse = g; kurs = n;
                }
            }
            if (gasse < 0) return "Zoning-/RZ-Gassenabstand: 0 parallele, längs überdeckte Paare.";
            double halb = settings.Ai / 2 + ParkingGeometry.ZoningStrassenbreite / 2;
            return FormattableString.Invariant($"Zoning-/RZ-Gassenabstand: Gasse {gasse}, Netzkurs {kurs}; Achsabstand {engste:F2} m, gefordert {halb + settings.Sl:F2} m; freier Randabstand {engste - halb:F2} m. Gemessen über parallele, längs überdeckte Zoningkurse einschließlich Randzoning.");
        }
    }
}
