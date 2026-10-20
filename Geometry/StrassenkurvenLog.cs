using System.Collections.Generic;

namespace ParkingLotTool.Geometry
{
    public sealed class StrassenkurvenLog
    {
        private readonly Dictionary<int,(long,int,long,int,Strassenkurve.Grund,int)> _quellen
            =new Dictionary<int,(long,int,long,int,Strassenkurve.Grund,int)>();

        // Quelle -1: Maus, sonst Index der gesetzten Kante. t und Zeiten sind Messwerte.
        public bool Neu(int quelle,Strassenkurve.Ergebnis e)
        {
            var stand=(e.Von.Kante,e.Von.Seite,e.Bis.Kante,e.Bis.Seite,e.Grund,e.Stuecke);
            if(_quellen.TryGetValue(quelle,out var alt) && alt.Equals(stand))return false;
            _quellen[quelle]=stand;return true;
        }
        public void Leere() => _quellen.Clear();
    }
}
