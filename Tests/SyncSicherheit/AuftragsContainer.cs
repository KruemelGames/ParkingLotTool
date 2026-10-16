using System;
using System.Collections.Generic;
using ParkingLotTool.Geometry;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotSyncSystem
    {
        private bool _spielGeladen = true, _neuAufnehmen;
        private readonly List<Entity> _offen = new();
        private readonly HashSet<Entity> _offenMenge = new(), _gescheitert = new();
        public Action BeiMeldung;
        internal void NeuAufnehmen() => _neuAufnehmen = true;
        public int TestStand(Entity lot) => StandVon(lot);
        public bool TestAntwortDirekt(Bauauftrag<Entity> a, Entity neu, bool ok) => AuftragsAntwort(a, neu, ok);
        public bool TestWartet(Entity lot) => _wartetAuf.ContainsKey(lot);
        public bool TestGescheitert(Entity lot) => _gescheitert.Contains(lot);
        public void TestPflege() => PflegeAuftraege();
        public Auftragskennung TestMigration(Entity lot, int schritt, Auftragsarbeiter arbeiter,
            Func<Auftragskennung, bool> uebergabe)
        {
            Auftragskennung kennung = default;
            AsynchronenSchrittStarten(lot, schritt, arbeiter, (e, id) => { kennung = id; return uebergabe(id); });
            return kennung;
        }
        public void TestNutzerFertig(Entity lot)
        {
            _fortsetzen.Remove(lot); PflegeAuftraege();
        }
        public void TestNeueLadung()
        {
            _spielGeladen = true;
            _wartetAuf.Clear(); _fortsetzen.Clear(); _auftragsWarten.Clear(); _nutzerAuftraege.Clear();
            _warteschlange.Clear(); _offen.Clear(); _offenMenge.Clear(); _gescheitert.Clear();
        }
    }
    public sealed partial class ParkingLotBuildingEconomySystem
    {
        public Auftragskennung TestNormal(Entity lot) => Normalanschluss(lot).Auftrag;
        public bool TestAnlauf(Entity lot)
            => _anschluesse.TryGetValue(lot, out var a) && AnschlussDarfArbeiten(lot, a);
        public void TestAntwort(Entity lot, Auftragskennung id, bool ok) => AnschlussBeendet(lot, id, ok, "Test mit Kennung");
    }
    public sealed partial class ParkingLotFahrwegTauschWerkzeug
    {
        public bool TestVeraltetenVerwerfen() => VerwirfVeraltetenAuftrag();
    }
}
