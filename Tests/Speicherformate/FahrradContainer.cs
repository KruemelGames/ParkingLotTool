// Neue Anzeige-Abhaengigkeiten, kein zusaetzliches Speicherformat.
namespace ParkingLotTool.Tools
{
    public struct ParkingLotEconomyData { public int Capacity; }
    public static class ParkingLotEconomySystem { public static int CalculateUpkeep(int n)=>48*n+438; }
}
namespace Unity.Entities
{
    public readonly struct DynamicBuffer<T>
    {
        private readonly System.Collections.Generic.List<T> _liste;
        public DynamicBuffer(System.Collections.Generic.List<T> liste) { _liste=liste; }
        public int Length=>_liste.Count;
        public T this[int i]=>_liste[i];
        public void Add(T wert)=>_liste.Add(wert);
        public void Clear()=>_liste.Clear();
    }
}
namespace Game.Areas { public struct SubArea { public Unity.Entities.Entity m_Area; } }
namespace Game.Objects { public struct SubObject { public Unity.Entities.Entity m_SubObject; } }
namespace Game.Net { public struct SubNet { public Unity.Entities.Entity m_SubNet; } }
