using System.Collections.Generic;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;
using Ausrichtzuweisung = ParkingLotTool.Tools.ParkingLotToolSystem.Ausrichtzuweisung;

namespace ParkingLotTool.Tools
{
    /** Eingabekopie eines Bauzettels, ohne lebende UI-Bindings oder Temp-Entities.
     *  Noch keine Bau-Transaktion: Hoehenschnappschuss, Wirtschaft und erzeugte
     *  Entities muessen erst nach gesicherter Materialisierung hinzukommen. */
    internal sealed class ParkingLotBaukontext
    {
        internal readonly Entity AltesLot;
        internal readonly ParkingLotBuildReceipt Zettel;
        internal readonly float3[] Punkte;
        internal readonly Entrance[] Zugaenge;
        internal readonly Ausrichtzuweisung[] Ausrichtungen;
        internal readonly Teilflaechenschnitt[] Schnitte;
        internal readonly ParkingGeometry.Zoningflaeche[] Zonen;
        internal readonly string FlaecheStrasse, FlaecheDekoration, FlaecheZoning;
        internal readonly string Vegetationszettel, Zoningstrasse;
        internal readonly List<(float2 A, float2 B, bool Links, bool Aus)> Seitenplan;
        internal readonly List<ParkingGeometry.RandzoningLinie> Randzoning;
        internal readonly List<BusStopPlacement> Bushalte;
        /** Strassenkurven des alten Lots (optional, nur Nachweis). */
        internal Kurvenzettel[] Kurven;

        internal ParkingLotBaukontext(Entity lot, ParkingLotBuildReceipt zettel,
            float3[] punkte, Entrance[] zugaenge, Ausrichtzuweisung[] ausrichtungen,
            Teilflaechenschnitt[] schnitte, ParkingGeometry.Zoningflaeche[] zonen,
            string strasse, string deko, string zoning, string vegetation,
            string zoningstrasse,
            List<(float2 A, float2 B, bool Links, bool Aus)> seitenplan,
            List<ParkingGeometry.RandzoningLinie> randzoning,
            List<BusStopPlacement> bushalte)
        {
            AltesLot = lot;
            Zettel = zettel;
            Punkte = punkte;
            Zugaenge = zugaenge;
            Ausrichtungen = ausrichtungen;
            Schnitte = schnitte;
            Zonen = zonen;
            FlaecheStrasse = strasse;
            FlaecheDekoration = deko;
            FlaecheZoning = zoning;
            Vegetationszettel = vegetation;
            Zoningstrasse = zoningstrasse;
            Seitenplan = seitenplan;
            Randzoning = randzoning;
            Bushalte = bushalte;
        }

        /** Dieselbe Kopie mit dem Namen der Zoningstrasse - fuer Bauzettel ohne ihn. */
        internal ParkingLotBaukontext MitZoningstrasse(string name)
            => new ParkingLotBaukontext(AltesLot, Zettel, Punkte, Zugaenge, Ausrichtungen,
                Schnitte, Zonen, FlaecheStrasse, FlaecheDekoration, FlaecheZoning, Vegetationszettel, name,
                Seitenplan, Randzoning, Bushalte) { Kurven = Kurven };

        /** Dieselbe Kopie mit anderen Flaechen - fuer die Reparatur fehlender Assets. */
        internal ParkingLotBaukontext MitFlaechen(string strasse, string deko, string zoning)
            => new ParkingLotBaukontext(AltesLot, Zettel, Punkte, Zugaenge, Ausrichtungen,
                Schnitte, Zonen, strasse, deko, zoning, Vegetationszettel, Zoningstrasse,
                Seitenplan, Randzoning, Bushalte) { Kurven = Kurven };
    }
}
