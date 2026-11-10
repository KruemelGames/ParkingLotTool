using System;
using System.Reflection;
using Game.Economy;
using Game.Prefabs;
using ParkingLotTool.Geometry;
using UnityEngine;

namespace ParkingLotTool.Tools
{
    /** Nur eigene, noch nicht angemeldete Prefabs. Keine Instanz-Netzdaten. */
    internal static class ParkingLotFahrradPrefabBau
    {
        internal const string Lotname = "PLT Fahrradparkplatzflaeche";
        internal const string LotC = "PLT Fahrradparkplatzflaeche C";
        internal const string LotD = "PLT Fahrradparkplatzflaeche D";
        internal static bool IstLotname(string name) => name == Lotname || name == LotC || name == LotD;
        internal static string LotnameFuer(Fahrradlayout layout) => layout switch
        {
            Fahrradlayout.A => Lotname, Fahrradlayout.C => LotC, Fahrradlayout.D => LotD,
            _ => throw new ArgumentOutOfRangeException(nameof(layout)),
        };
        internal const string Zugang = "PLT Fahrradweg";
        internal const string Innen = "PLT Fahrradweg intern";
        internal const string Schmal = "PLT Fahrradweg 2.5m intern";
        internal const string Gehname = "PLT Fahrrad Gehfläche";

        internal static PathwayPrefab Weg(PathwayPrefab quelle, string name, float breite,
            Action<PrefabBase, PrefabBase> anmelden)
        {
            var klon = ScriptableObject.CreateInstance<PathwayPrefab>();
            klon.name = name;
            for (var typ = quelle.GetType(); typ != null && typ != typeof(PrefabBase); typ = typ.BaseType)
                foreach (var feld in typ.GetFields(BindingFlags.Instance | BindingFlags.Public
                    | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (!feld.IsInitOnly && !feld.IsLiteral
                        && (feld.IsPublic || feld.IsDefined(typeof(SerializeField), false)))
                        feld.SetValue(klon, feld.GetValue(quelle));
            foreach (var c in quelle.components)
                if (ParkingLotFahrprefabKopie.Erben(c)) klon.AddComponentFrom(c);
            var faktor = breite / 3f;
            new ParkingLotFahrprefabKopie(name, anmelden, autoregeln: false,
                konfiguriere: p => Skaliere(p, faktor)).Isoliere(klon);
            anmelden(quelle, klon);
            return klon;
        }

        private static void Skaliere(PrefabBase p, float faktor)
        {
            if (p is NetPiecePrefab piece)
            {
                piece.m_Width *= faktor;
                piece.m_WidthOffset *= faktor;
            }
            var car = p.GetComponent<Game.Prefabs.CarLane>();
            if (car != null) car.m_Width *= faktor;
            var fuss = p.GetComponent<Game.Prefabs.PedestrianLane>();
            if (fuss != null) fuss.m_Width *= faktor;
            var lanes = p.GetComponent<NetPieceLanes>();
            if (lanes?.m_Lanes != null)
                foreach (var lane in lanes.m_Lanes) lane.m_Position.x *= faktor;
        }

        internal static LotPrefab Lot(Fahrradlayout layout = Fahrradlayout.A)
        {
            var lot = ScriptableObject.CreateInstance<LotPrefab>();
            lot.name = LotnameFuer(layout);
            lot.m_MaxRadius = 500;
            lot.m_AllowOverlap = true;
            lot.m_AllowEditing = true;
            lot.m_Color = lot.m_EdgeColor = new Color(0, 0, 0, 0);
            lot.m_SelectionColor = new Color(1, 1, 1, .15f);
            lot.m_SelectionEdgeColor = new Color(1, 1, 1, .6f);
            var parking = lot.AddComponent<Game.Prefabs.ParkingFacility>();
            parking.m_RoadTypes = Game.Net.RoadTypes.Bicycle;
            parking.m_GarageMarkerCapacity = 0;
            parking.m_ComfortFactor = FahrradBetrieb.Komfort(layout);
            // Die Flaeche zahlt 0. Der vorhandene Gebaeudebegleiter rechnet
            // die Instanzkapazitaet ab; UpdateFrame an Areas waere unzulaessig.
            var wirtschaft = lot.AddComponent<CityServiceBuilding>();
            wirtschaft.m_Upkeeps = new[] { new ServiceUpkeepItem
            {
                m_Resources = new ResourceStackInEditor { m_Resource = ResourceInEditor.Money, m_Amount = 0 },
                m_ScaleWithUsage = true,
            } };
            // Vanilla AreaConnectionSystem:513-518 erzeugt fuer Pedestrian
            // SecondaryStart/End und RoadTypes.Bicycle. D benoetigt deshalb
            // keine 0,38-m-Netze und keinen nachtraeglichen Spurflag-Eingriff.
            return lot;
        }

        internal static SpacePrefab Gehflaeche()
        {
            var lot = ScriptableObject.CreateInstance<SpacePrefab>();
            lot.name = Gehname;
            lot.m_Color = lot.m_EdgeColor = new Color(0, 0, 0, 0);
            var navigation = lot.AddComponent<NavigationArea>();
            // Nur Pedestrian wie die Vanilla-Walking-Area: AreaConnectionSystem:514-517
            // gibt diesen Spuren SecondaryStart/End und RoadTypes.Bicycle. Ein
            // m_SecondaryType = Road erzeugte eine Road+Area-Spur = PathMethod.Offroad
            // (PathUtils:1385-1390), die Raeder nicht befahren (PersonalCarAISystem:776).
            // Die Strasse erreicht die Flaeche ueber Zugangsmarker (FahrradZugaenge).
            navigation.m_ConnectionType = RouteConnectionType.Pedestrian;
            navigation.m_RoadTypes = Game.Net.RoadTypes.Bicycle;
            return lot;
        }
    }
}
