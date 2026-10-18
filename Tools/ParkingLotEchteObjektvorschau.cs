using System;
using System.Collections.Generic;
using System.Linq;
using Game.Objects;
using Game.Prefabs;
using Game.Simulation;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private VegetationPlan _musterVegetation;
        private VegetationAsset[] _musterPflanzen;
        private VegetationOptions _musterVegetationswahl;
        private LaternenPlan _musterLaternen;
        private LaternenOptionen _musterLaternenwahl;
        private ParkingLayout _musterObjektLayout;
        private long _musterObjektStand;

        private ParkingLotMusterSystem.Objektplan BerechneObjektvorschau(EchteVorschauArt art)
        {
            float2[] standorte = art == EchteVorschauArt.Laternen
                ? _musterLaternen.Laternen.Select(p=>p.Position).ToArray()
                : _musterVegetation.Plants.Select(p=>p.Position).ToArray();
            var lot=_points.ToArray();
            var auswahl=EchteVorschau.Waehle(_musterGriff.Startbereich(lot,
                OberflaechenMuster.Richtung(_areaPreviewLayout.AisleLine,lot,_points[0])),standorte);
            var objekte = new List<ParkingLotMusterSystem.Objekt>();
            foreach (int index in auswahl.Indizes)
            {
                if (art == EchteVorschauArt.Laternen)
                {
                    var p=_musterLaternen.Laternen[index]; var optionen=_musterLaternenwahl;
                    var prefab=LaternenPrefab(optionen.ModellFuer(p));
                    if (prefab == Unity.Entities.Entity.Null) throw new InvalidOperationException("Gewaehltes Laternenmodell fehlt.");
                    objekte.Add(new ParkingLotMusterSystem.Objekt { Position=p.Position, Vorbild=prefab,
                        Erzeuge=(ref TerrainHeightData h)=>LaternenDefinition(p,optionen,ref h,out _,out _,out _) });
                }
                else
                {
                    var p=_musterVegetation.Plants[index]; var asset=_musterPflanzen[p.Species]; var optionen=_musterVegetationswahl;
                    int alter=asset.Tree ? ParkingVegetation.SelectAge(optionen.Ages,p.Seed) : -1;
                    // Derselbe Stumpf-Rueckfall wie StumpfNurMitMesh beim echten Bau.
                    if (alter==5 && (!EntityManager.HasBuffer<SubMesh>(asset.Prefab)
                        || EntityManager.GetBuffer<SubMesh>(asset.Prefab,true).Length<=5)) alter=4;
                    objekte.Add(new ParkingLotMusterSystem.Objekt { Position=p.Position,Vorbild=asset.Prefab,Alter=alter,
                        Erzeuge=(ref TerrainHeightData h)=>PflanzenDefinition(asset,p,optionen,ref h,out _,out _,out _) });
                }
            }
            return new ParkingLotMusterSystem.Objektplan { Ausschnitt=auswahl.Ausschnitt,
                Objekte=objekte.ToArray(),Verkleinerungen=auswahl.Verkleinerungen };
        }
    }
}
