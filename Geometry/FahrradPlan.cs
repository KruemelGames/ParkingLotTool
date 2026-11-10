using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace ParkingLotTool.Geometry
{
    public enum Fahrzeugart { Auto, Fahrrad }
    // Kennung 1 bleibt frei: Layout B wurde am 29.10.2026 gestrichen.
    public enum Fahrradlayout { A = 0, C = 2, D = 3 }
    /** Gestaltung, gilt fuer jedes Layout (Nutzer 03.11.2026). 0 = Zettel vor Fassung 8. */
    public enum Fahrradstil { Schlicht = 0, Rahmen = 1, Platz = 2 }
    public enum Fahrradobjektart { Staender, Unterstand, Buegel }
    public enum Fahrradwegart { Fahrradweg, Fussweg, Platz }

    public sealed class Fahrradobjekt
    {
        // sonde-1.log: BikeRack03 1,5/0,375 = 4, Shelter 3,6/0,45 = 8,
        // CommercialRack 0,75/0,375 = 2. Reservierungstiefe 2 m, nicht
        // die 2,4 m MaxCarLength (darin steckt Vanilla-Rangierreserve).
        public Fahrradobjektart Art { get; internal set; }
        public string Prefab { get; internal set; }
        public float2 Position { get; internal set; }
        /** Drehung der lokalen +X-Achse in der XZ-Ebene, Radiant. */
        public double Drehung { get; internal set; }
        public int Kapazitaet { get; internal set; }
        /** Reservierung einschliesslich abgestellter Raeder, nicht nur Mesh. */
        public float2[] Grundriss { get; internal set; }
        public float2 Zugang { get; internal set; }
        public string BodenPrefab { get; internal set; }
        public float2[] BodenGrundriss { get; internal set; }
    }

    public sealed class Fahrradweg
    {
        public Fahrradwegart Art { get; internal set; }
        public float2 A { get; internal set; }
        public float2 B { get; internal set; }
        public double Breite { get; internal set; }
        public bool Zufahrt { get; internal set; }
        // Fusswegvertrag fuer D: LaneConnectionSystem:590 verlangt AllowBicycle;
        // eine Geh-Flaeche alleine erzeugt noch keine Fahrradverbindung.
        public bool ErlaubtFahrrad { get; internal set; } = true;
    }

    public sealed class FahrradPlan
    {
        public Fahrradlayout Layout { get; internal set; }
        public Fahrradstil Stil { get; internal set; }
        /** Gebaute Rahmenbreite in Metern (= Randabstand); schmaler nur, wenn die Abstellart sonst nicht passt. */
        public double Rahmen { get; internal set; }
        public readonly List<Fahrradobjekt> Objekte = new List<Fahrradobjekt>();
        public readonly List<Fahrradweg> Wege = new List<Fahrradweg>();
        /** Hauptflaeche (Pflaster). Schlicht: der ganze Umriss; sonst nur unter Wegen und Staendern. */
        public readonly List<float2[]> Flaechen = new List<float2[]>();
        /** Dekoflaeche (Gras) unter dem Pflaster; leer bei Schlicht. */
        public readonly List<float2[]> Gras = new List<float2[]>();
        public readonly List<float2> Laternen = new List<float2>();
        public readonly List<float2> Baeume = new List<float2>();
        public readonly List<float2> Buesche = new List<float2>();
        public readonly List<string> Hinweise = new List<string>();
        public int Kapazitaet => Objekte.Sum(o => o.Kapazitaet);
        public double UnterhaltJeRad => Layout == Fahrradlayout.C ? 12.5
            : Layout == Fahrradlayout.D ? 23 : 6;
        public double Komfort => Layout == Fahrradlayout.C ? .3
            : Layout == Fahrradlayout.D ? .5 : .2;
    }
}
