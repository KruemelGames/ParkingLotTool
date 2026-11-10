using System.Collections.Generic;
using Game.Common;
using Game.Prefabs;
using ParkingLotTool.Geometry;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    internal static partial class ParkingLotFahrradBetrieb
    {
        /** Bauzettel sichert auch das Ladefenster vor der Prefab-Freigabe ab.
         *  Auto-Migrationen kennen weder 2,5-m-Fahrradgaenge noch Geh-Flaeche;
         *  sie duerfen einen Fahrradbestand nicht anfassen oder hochstufen. */
        internal static bool IstBestand(EntityManager em, Entity lot)
            => Ist(em, lot) || (lot != Entity.Null && em.Exists(lot)
                && em.HasComponent<ParkingLotBuildReceipt>(lot)
                && em.GetComponentData<ParkingLotBuildReceipt>(lot).Fahrzeugart == Fahrzeugart.Fahrrad);

        internal static bool PasstZettel(EntityManager em, Entity lot, ParkingLotBuildReceipt zettel)
        {
            var fahrrad=TryLayout(em,lot,out var layout);
            return zettel.Fahrzeugart==(fahrrad?Fahrzeugart.Fahrrad:Fahrzeugart.Auto)
                && (!fahrrad || zettel.Fahrradlayout==layout);
        }

        /** Fuer den Abriss nach Verlust der Mod-Relation: nur identischer
         *  PrefabRef UND nativer Owner, genau 1 Kandidat, keine Ortssuche. */
        internal static Entity AbrissTraeger(EntityManager em, Entity lot, IEnumerable<Entity> kandidaten, out int anzahl)
        {
            anzahl=0;
            if(!IstBestand(em,lot) || !em.HasComponent<PrefabRef>(lot)) return Entity.Null;
            var gefunden=Entity.Null;
            foreach(var kandidat in kandidaten)
            {
                if(!TraegerPasst(em,lot,kandidat)) continue;
                anzahl++;gefunden=kandidat;
            }
            return anzahl==1?gefunden:Entity.Null;
        }

        internal static bool TraegerPasst(EntityManager em, Entity lot, Entity kandidat)
            => kandidat!=lot && kandidat!=Entity.Null && em.Exists(kandidat)
                && em.HasBuffer<Game.Objects.SubObject>(kandidat)
                && em.HasComponent<Owner>(kandidat) && em.GetComponentData<Owner>(kandidat).m_Owner==lot
                && em.HasComponent<PrefabRef>(lot) && em.HasComponent<PrefabRef>(kandidat)
                && em.GetComponentData<PrefabRef>(kandidat).m_Prefab==em.GetComponentData<PrefabRef>(lot).m_Prefab;

        internal static int LadeNetze(EntityManager em, Entity lot, Entity traeger)
        {
            if(!IstBestand(em,lot) || lot==traeger || traeger==Entity.Null || !em.Exists(traeger)
                || !em.HasBuffer<Game.Net.SubNet>(lot)) return 0;
            var quelle=em.GetBuffer<Game.Net.SubNet>(lot);
            var ziel=em.HasBuffer<Game.Net.SubNet>(traeger)?em.GetBuffer<Game.Net.SubNet>(traeger)
                :em.AddBuffer<Game.Net.SubNet>(traeger);
            ziel.Clear();
            for(var i=0;i<quelle.Length;i++) ziel.Add(quelle[i]);
            return ziel.Length;
        }

        /** Game.Areas.SubArea ist IEmptySerializable: nach Laden 0 Verweise.
         *  Rekonstruktion aus echtem Owner/Prefab, keine neue Geometrie. */
        internal static int LadeGehflaechen(EntityManager em, Entity lot, Entity traeger, IEnumerable<Entity> gehen)
        {
            if (!IstBestand(em, lot) || traeger == lot || traeger == Entity.Null || !em.Exists(traeger)) return 0;
            var root = em.HasBuffer<Game.Areas.SubArea>(lot) ? em.GetBuffer<Game.Areas.SubArea>(lot)
                : em.AddBuffer<Game.Areas.SubArea>(lot);
            var ziel = em.HasBuffer<Game.Areas.SubArea>(traeger) ? em.GetBuffer<Game.Areas.SubArea>(traeger)
                : em.AddBuffer<Game.Areas.SubArea>(traeger);
            ziel.Clear();
            foreach (var flaeche in gehen)
            {
                if (flaeche == lot || flaeche == traeger || flaeche == Entity.Null || !em.Exists(flaeche)) continue;
                var schon = false;
                for (var i=0;i<ziel.Length;i++) if(ziel[i].m_Area==flaeche) { schon=true; break; }
                if(schon) continue;
                ziel.Add(new Game.Areas.SubArea { m_Area=flaeche });
                schon=false;
                for(var i=0;i<root.Length;i++) if(root[i].m_Area==flaeche) { schon=true; break; }
                if(!schon) root.Add(new Game.Areas.SubArea { m_Area=flaeche });
            }
            return ziel.Length;
        }
    }
}
