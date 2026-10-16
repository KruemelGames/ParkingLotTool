using System.Collections.Generic;
using ParkingLotTool.Geometry;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotBuildingEconomySystem
    {
        private sealed class Anschluss
        {
            internal Auftragskennung Auftrag;
            internal int Seit = UnityEngine.Time.frameCount;
            internal int NaechstePruefung;
            internal bool Sync, Platziert;
            internal int Versuche, NaechstePlatzierung;
        }
        private readonly Dictionary<Entity, Anschluss> _anschluesse = new();
        private ParkingLotAuftragsSystem Auftraege => World.GetOrCreateSystemManaged<ParkingLotAuftragsSystem>();

        internal bool StrassensyncEinreihen(Entity lot, Auftragskennung id)
        {
            if (_anschluesse.TryGetValue(lot, out var vorher) && vorher.Sync && Auftraege.Gilt(vorher.Auftrag))
                return false;
            if (!Auftraege.Annehmen(id, lot, Auftragsarbeiter.Strasse)) return false;
            if (vorher != null) Auftraege.Beende(vorher.Auftrag, lot, false);
            _anschluesse[lot] = new Anschluss { Auftrag = id, Sync = true };
            return true;
        }

        /** null, wenn das Register den Auftrag nicht angenommen hat (etwa waehrend des Ladens). */
        private Anschluss Normalanschluss(Entity lot, Auftragsgrund grund = Auftragsgrund.Normalbau)
        {
            if (!_anschluesse.TryGetValue(lot, out var a) || !Auftraege.Gilt(a.Auftrag))
            {
                var id = Auftraege.Merke(lot, Auftragsarbeiter.Strasse, grund);
                Auftraege.Uebergib(id, kennung =>
                {
                    if (!Auftraege.Annehmen(kennung, lot, Auftragsarbeiter.Strasse)) return false;
                    _anschluesse[lot] = new Anschluss { Auftrag = kennung };
                    return true;
                });
                if (!_anschluesse.TryGetValue(lot, out a) || !Auftraege.Gilt(a.Auftrag)) return null;
            }
            Auftraege.Starte(a.Auftrag);
            return a;
        }

        private bool AnschlussDarfArbeiten(Entity lot, Anschluss a)
        {
            if (!_anschluesse.TryGetValue(lot, out var aktuell) || !ReferenceEquals(aktuell, a)) return false;
            if (!Auftraege.Gilt(a.Auftrag))
            { _anschluesse.Remove(lot); return false; }
            Auftraege.Starte(a.Auftrag);
            return Auftraege.DarfAnwenden(a.Auftrag);
        }
    }
}
