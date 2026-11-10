using System;
using Game.Prefabs;
using ParkingLotTool.Geometry;
using ParkingLotTool.Tools;
using Unity.Entities;
using Unity.Mathematics;

internal static partial class Program
{
    private static void Betrieb()
    {
        var em = new EntityManager();
        var auto = new Entity(201);
        em.AddComponentData(auto, new PrefabRef { m_Prefab = new Entity(301) });
        Pruefe(!ParkingLotFahrradBetrieb.Ist(em, auto), "Auto bleibt Auto");
        Pruefe(!ParkingLotFahrradBetrieb.Ist(em, Entity.Null), "Null ist kein Fahrrad-Lot");
        var namen = new System.Collections.Generic.HashSet<string>();
        foreach (var (layout, satz, komfort, kapazitaet, unterhalt) in new[]
        {
            (Fahrradlayout.A, 6.0, .2f, 336, 2016),
            (Fahrradlayout.C, 12.5, .3f, 192, 2400),
            (Fahrradlayout.D, 23.0, .5f, 168, 3864),
        })
        {
            var lot = new Entity(210+(int)layout);
            var prefab = new Entity(310+(int)layout);
            em.AddComponentData(lot, new PrefabRef { m_Prefab = prefab });
            em.AddComponentData(prefab, new ParkingLotFahrradLayout { Layout = layout });
            Pruefe(ParkingLotFahrradBetrieb.TryLayout(em, lot, out var gelesen) && gelesen==layout,
                $"{layout}: Prefab-Vertrag erreicht gebaute Instanz");
            var p = ParkingLotFahrradPrefabBau.Lot(layout);
            Pruefe(namen.Add(p.name) && ParkingLotFahrradPrefabBau.IstLotname(p.name), $"{layout}: eindeutiger eigener Speichername");
            Pruefe(p.GetComponent<ParkingFacility>().m_ComfortFactor==komfort, $"{layout}: nativer Komfort");
            var kosten = p.GetComponent<CityServiceBuilding>().m_Upkeeps;
            Pruefe(kosten.Length==1 && kosten[0].m_Resources.m_Amount==0 && kosten[0].m_ScaleWithUsage,
                $"{layout}: Lot zahlt keine zweite Unterhaltsbasis");
            Pruefe(FahrradBetrieb.Komfort(layout)==komfort, $"{layout}: Spurkomfort entspricht Vanilla");
            Pruefe(FahrradBetrieb.Unterhalt(layout,kapazitaet)==unterhalt, $"{layout}: Rechteck-Unterhalt");
            Pruefe(FahrradBetrieb.Unterhalt(layout,-1)==0, $"{layout}: negative Kapazitaet kostet 0");
            for(var n=0;n<=1000;n++)
            {
                var erwartet=(int)Math.Round(n*satz);
                Pruefe(ParkingLotFahrradBetrieb.Unterhalt(em,lot,n)==erwartet,$"{layout}: {n} wirkliche Plaetze");
                Pruefe(ParkingLotFahrradBetrieb.Unterhalt(em,auto,n)==48*n+438,$"Auto unveraendert bei {n} Plaetzen");
            }
        }
        Pruefe(!ParkingLotFahrradPrefabBau.IstLotname("ParkingLot01"),"Vanilla-Prefabs ausgeschlossen");
        Pruefe(!ParkingLotFahrradPrefabBau.IstLotname("PLT Fahrradparkplatzflaeche fremd"),"nur exakte eigene Namen");
        var abgelehnt=false;
        try { FahrradBetrieb.Komfort((Fahrradlayout)1); } catch(ArgumentOutOfRangeException){abgelehnt=true;}
        Pruefe(abgelehnt,"entfallenes B nicht wieder als Standard angenommen");
        var original=new FahrradPlan { Layout=Fahrradlayout.D };
        original.Baeume.Add(new float2(1,2));original.Baeume.Add(new float2(20,10));
        var kopie=new FahrradPlan { Layout=Fahrradlayout.D };
        kopie.Baeume.AddRange(original.Baeume);
        Pruefe(FahrradDekoration.Speicherkennung(original)==FahrradDekoration.Speicherkennung(kopie),"unveraenderter D-Edit erhaelt Baeume");
        kopie.Baeume[0]+=new float2(2,0);
        Pruefe(FahrradDekoration.Speicherkennung(original)!=FahrradDekoration.Speicherkennung(kopie),"versetzter D-Baum darf nicht als unveraendert uebernommen werden");
        Pruefe(FahrradDekoration.Speicherkennung(new FahrradPlan {Layout=Fahrradlayout.A})!=FahrradDekoration.Speicherkennung(new FahrradPlan {Layout=Fahrradlayout.D}),"Layoutwechsel hat eigene Pflanzenkennung");
    }
}
