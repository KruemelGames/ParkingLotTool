using System;
using System.Collections.Generic;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    // Nur ECS-Zugriffe und Umgebung sind Stubs; Abschluss/Dispatcher sind Produktionscode.
    public sealed partial class ParkingLotFahrwegTauschWerkzeug
    {
        private enum Stufe { Frei, Anlegen }
        private Stufe _stufe;
        private Entity _lot;
        private readonly List<(Entity Kante, Entity Ziel, Entity Besitzer)> _plan = new();
        private readonly List<Entity> _definitionen = new();
        public void PruefeJetzt() => PruefeUndBeende();
        public void BeendeJetzt(bool ok) => Beende(ok, "Test");
        public void MerkeDefinition(Entity e) => _definitionen.Add(e);
    }

    public sealed partial class ParkingLotSyncSystem
    {
        private const double BudgetMs = 2.0;
        private readonly List<Entity> _warteschlange = new();
        private readonly Dictionary<Entity, int> _wartetAuf = new();
        private readonly HashSet<Entity> _fortsetzen = new();
        private readonly ParkingLotToolSystem _werkzeug = new();
        private Dictionary<Entity, List<Entity>> _index;
        private int _erledigt, _syncErfolge, _meldungSync;
        public readonly List<Entity> Fehlerlots = new(), Synclots = new();
        public Func<Dictionary<Entity, List<Entity>>> IndexBauen = () => new();
        public Action BeimScheitern;
        public int DispatcherOffen => _warteschlange.Count;
        public int DispatcherErledigt => _erledigt;
        public void StarteDispatcher(params Entity[] lots) => _warteschlange.AddRange(lots);
        public void DispatcherBild() => Abarbeiten();
        private Dictionary<Entity, List<Entity>> TeileJeLot() => IndexBauen();
        private bool Synchronisiere(Entity lot, Dictionary<Entity, List<Entity>> index) { Synclots.Add(lot); return true; }
        private void Scheitern(Entity lot, int stand, string wo, string grund) { Fehlerlots.Add(lot); BeimScheitern?.Invoke(); }
        private void VeroeffentlicheMeldung() { BeiMeldung?.Invoke(); }
    }

    public sealed partial class ParkingLotBuildingEconomySystem : Game.GameSystemBase
    {
        public Func<List<Entity>> Kandidaten = () => new();
        public int AnschluesseOffen => _anschluesse.Count;
        public void StarteAnschluss(Entity lot, bool sync)
        {
            if (!sync) { Normalanschluss(lot); return; }
            var id = Auftraege.Merke(lot, ParkingLotTool.Geometry.Auftragsarbeiter.Strasse,
                ParkingLotTool.Geometry.Auftragsgrund.Migration, 14,
                antwort: World.GetOrCreateSystemManaged<ParkingLotSyncSystem>().TestAntwort);
            Auftraege.Uebergib(id, token => StrassensyncEinreihen(lot, token));
            AnschlussDarfArbeiten(lot, _anschluesse[lot]);
        }
        public void BeendeAnschluss(Entity lot, bool ok)
        { if (_anschluesse.TryGetValue(lot, out var a)) AnschlussBeendet(lot, a.Auftrag, ok, "Test"); }
        private bool Lebt(Entity lot) => EntityManager.Exists(lot);
        private List<Entity> Stadtstrassen(Entity lot) => Kandidaten();
    }

    public static class ParkingLotSchrittmarke
    {
        public static bool Fehler;
        public static void Aenderung(string text) { if (Fehler) throw new InvalidOperationException("Schrittspur"); }
    }

    internal static partial class ParkingLotMeldepaket
    {
        public static string TestLog(string pfad, bool sync) => sync ? Syncauszug(pfad) : Logende(pfad);
        public static string TestPlayer(string pfad) => Auszug(pfad);
        public static string TestArchiv(string text, bool logauszug)
        {
            using var daten = new System.IO.MemoryStream();
            using (var archiv = new System.IO.Compression.ZipArchive(daten, System.IO.Compression.ZipArchiveMode.Create, true))
                LegeText(archiv, "test.txt", text, logauszug);
            daten.Position = 0;
            using var lesen = new System.IO.Compression.ZipArchive(daten, System.IO.Compression.ZipArchiveMode.Read);
            using var leser = new System.IO.StreamReader(lesen.GetEntry("test.txt").Open(), new System.Text.UTF8Encoding(false, true));
            return leser.ReadToEnd();
        }
    }
}
