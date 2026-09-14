using System.Collections.Generic;
using System.Globalization;
using Colossal.Mathematics;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /** Kleine Text- und Vergleichshelfer der Ueberlappungsdiagnose. */
    public sealed partial class ParkingLotToolSystem
    {
        private static string UeEntstehung(UeObjekt a, UeObjekt b, bool frueh,
                                           UeBasis basis)
        {
            if (frueh) return "schon beim fruehen Lauf nach dem Bau vorhanden";
            if (basis == null) return "Zeitpunkt unbelegt: kein frueher "
                + "Vergleichsstand in dieser Spielsitzung";
            var seit = (System.DateTime.UtcNow - basis.Zeitpunkt).TotalMinutes;
            var vergleich = " (" + UeZahl(seit, 1)
                + " min seit dem fruehen Lauf)";
            if (b == null)
            {
                if (basis.Objekte.Contains(a.Entity))
                    return "war beim fruehen Lauf schon vorhanden" + vergleich;
                return a.Gebaeude
                    ? "neues Gebaeude nach dem fruehen Lauf" + vergleich
                        + "; automatisches Wachstum ist zeitlich passend, der "
                        + "Erzeugungsweg selbst wird von CS2 nicht protokolliert"
                    : "nach dem fruehen Lauf neu entstanden" + vergleich;
            }
            var key = UePaarschluessel(a, b);
            if (basis.Paare.Contains(key))
                return "Paar bestand schon beim fruehen Lauf" + vergleich;
            var neuA = !basis.Objekte.Contains(a.Entity);
            var neuB = !basis.Objekte.Contains(b.Entity);
            if ((neuA && a.Gebaeude) || (neuB && b.Gebaeude))
                return "Paar erst nach dem fruehen Lauf" + vergleich
                    + "; ein beteiligtes Gebaeude ist neu, automatisches "
                    + "Wachstum ist zeitlich passend aber nicht direkt protokolliert";
            return "Paar erst nach dem fruehen Lauf festgestellt" + vergleich;
        }

        private static string UePaarschluessel(UePaar paar)
            => UePaarschluessel(paar.A, paar.B);

        private static string UePaarschluessel(UeObjekt a, UeObjekt b)
        {
            var first = a.Entity.Index < b.Entity.Index
                || a.Entity.Index == b.Entity.Index
                    && a.Entity.Version <= b.Entity.Version ? a.Entity : b.Entity;
            var second = first == a.Entity ? b.Entity : a.Entity;
            return first.Index + "." + first.Version + "/"
                + second.Index + "." + second.Version;
        }

        private static void UeZaehle(Dictionary<Entity, int> werte, Entity entity)
            => werte[entity] = werte.TryGetValue(entity, out var alt) ? alt + 1 : 1;

        private static string UeZahl(double wert, int stellen = 1)
            => wert.ToString("F" + stellen, CultureInfo.InvariantCulture);

        private static string UePunkt(float2 p)
            => UeZahl(p.x, 2) + "/" + UeZahl(p.y, 2);

        private static string UeFloat3(float3 p)
            => UeZahl(p.x, 2) + "/" + UeZahl(p.y, 2) + "/" + UeZahl(p.z, 2);

        private static string UeBounds(Bounds3 b)
            => "[" + UeFloat3(b.min) + "] bis [" + UeFloat3(b.max) + "]";
    }
}
