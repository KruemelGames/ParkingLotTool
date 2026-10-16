using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game;
using ParkingLotTool.Geometry;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    // Nur native Umgebung und Baugeometrie sind ersetzt; Annahme, Identitaet,
    // Task-Lesen, Gate-Rueckruf und Abschluss kommen aus den Produktionsdateien.
    internal enum Tauschart { Pflanzen, Laternen }

    public sealed partial class ParkingLotBestandsTauschSystem
    {
        private sealed class Ergebnis { }
        private sealed class Vorbereitung
        {
            internal Entity Lot;
            internal Tauschart Art;
            internal Auftragskennung Kennung;
            internal Task<Ergebnis> Rechnung;
        }
        private readonly List<(Entity Lot, Tauschart Art, Auftragskennung Kennung)> _warteschlange = new();
        private Vorbereitung _laeuft;
        private (Entity Lot, Tauschart Art)? _imWerkzeug;
        public int Offen => _warteschlange.Count + (_laeuft != null ? 1 : 0) + (_imWerkzeug != null ? 1 : 0);
        public int Freigaben;
        public bool Freigabefehler;
        private void Freigeben(bool ok)
        {
            Freigaben++; _imWerkzeug = null; _laeuft = null;
            if (Freigabefehler) throw new InvalidOperationException("Freigabe");
        }
        public bool TestStart(Entity lot, Auftragskennung id)
        {
            _warteschlange.RemoveAll(a => a.Kennung == id);
            if (!Auftraege.Starte(id)) return false;
            _kennung = id;
            _imWerkzeug = (lot, Tauschart.Pflanzen);
            return true;
        }
        public void TestAntwort(Entity lot, Auftragskennung id, bool ok) => Fertig(lot, id, ok, "Test");
        public Func<bool> TestRechnung(Entity lot, Auftragskennung id, bool fehler = false)
        {
            var quelle = new TaskCompletionSource<Ergebnis>();
            var v = new Vorbereitung { Lot = lot, Kennung = id, Rechnung = quelle.Task };
            _laeuft = v;
            if (fehler) quelle.SetException(new InvalidOperationException("Rechnung")); else quelle.SetResult(new Ergebnis());
            return () => RechnungLesen(v, out _);
        }
        public void TestLaden() { _warteschlange.Clear(); _laeuft = null; _imWerkzeug = null; _kennung = default; }
    }

    public sealed partial class ParkingLotBestandsTauschWerkzeug
    {
        private enum Stufe { Frei, Anlegen }
        private sealed class Auftrag { internal Entity Lot; internal string Name; internal Auftragskennung Kennung; }
        private Auftrag _a;
        private Stufe _stufe;
        private readonly List<Entity> _definitionen = new();
        internal Action<Entity, Auftragskennung, bool, string> Fertig;
        public bool Beschaeftigt => _stufe != Stufe.Frei;
        internal void Zuruecksetzen() { _a = null; _stufe = Stufe.Frei; _definitionen.Clear(); }
        public void TestStart(Entity lot, Auftragskennung id, Entity definition)
        {
            _a = new Auftrag { Lot = lot, Name = "Test", Kennung = id };
            _stufe = Stufe.Anlegen; _definitionen.Add(definition);
        }
        public void TestEnde(bool ok) => Beende(ok, "Test");
    }

    public sealed partial class ParkingLotFlaechenSyncSystem
    {
        public bool TestDarfArbeiten() => LaufGueltig();
        public void TestBelegung(bool ja) => _lauf = ja ? new Lauf() : null;
        public bool TestStart(Entity lot, Auftragskennung id)
        { _warteschlange.RemoveAll(a => a.Kennung == id); return Beginne(lot, id, 0); }
        public Action TestAntwort(bool ok)
        {
            var a = _lauf;
            return () => { if (ReferenceEquals(_lauf, a)) Beende(ok, "Test"); };
        }
        public void TestEnde(bool ok) => Beende(ok, "Test");
        public void TestFehler() => BeendeNachAusnahme("Test", new InvalidOperationException("Pruefung"));
        public void TestLaden() { _lauf = null; _warteschlange.Clear(); }
    }

    public struct ParkingLotOffenerErsatz { public Entity Lot, Traeger; }
    public struct ParkingLotRueckwegkurs { }
    public struct ParkingLotErhaltenerKurs { }
    public struct ParkingLotRueckweganschluss { }
    public struct ParkingLotStufeAKnoten { }
    public struct ParkingLotNeubauFaellig { }
    public static class ParkingLotStufeA { public static void VergissPrefab(EntityManager em, Entity lot) { } }
    public static class ParkingLotNetzRueckweg
    {
        public static int Fehlend;
        public static void Melde(string s) => Mod.log.Info(s);
        public static bool Lebt(EntityManager em, Entity e) => em.Exists(e) && !em.HasComponent<Game.Common.Deleted>(e);
        public static void VerwerfeOffeneSicherung(EntityManager em, Entity e) { }
        public static int FehlendePrefabs(EntityManager em, Entity e) => Fehlend;
    }
    public class ParkingLotUISystem : GameSystemBase { public void SetStatus(string s) { } }
    public static class ParkingLotTexte { public static string T(string s) => s; }
    public class ParkingLotExklusivesBildSystem : GameSystemBase
    {
        public readonly List<Func<int, int>> Arbeit = new();
        public void Erwarte(Entity lot, Func<int, int> arbeit) => Arbeit.Add(arbeit);
    }
    public sealed partial class ParkingLotToolSystem
    {
        public static ParkingLotToolSystem Bauarbeiter(World world) => new() { World = world };
        internal bool HintergrundBeobachtetFluss;
        internal bool HintergrundRueckwegFlussPortion(Entity e) => true;
        internal void HintergrundPortionenBeenden() { }
    }
    public sealed partial class ParkingLotSyncSystem
    {
        public int RueckwegeGestartet, RueckwegeBeendet;
        internal void MeldeBauplanFehlt(Entity lot, string s) { }
        internal void MeldeRueckwegStart(bool erster) => RueckwegeGestartet++;
        internal void MeldeRueckwegEnde() => RueckwegeBeendet++;
    }

    internal sealed partial class ParkingLotHintergrundSystem : GameSystemBase
    {
        private enum Phase { Planung, Nachpruefung, Nebenarbeit, Ruecknahme, Ruhe, Rueckweg, Rueckabschluss }
        private sealed class Auftrag
        {
            internal Entity Lot, Neu, Traeger;
            internal Auftragskennung Kennung;
            internal bool NurRueckweg, Fortsetzen, Erfolg, Beendet, GateOffen;
            internal int Id, Fruehestens, Seit, Versuch, Rueckwegpruefungen, RueckIst, RueckSoll;
            internal Phase Phase;
            internal ParkingLotToolSystem Bauer;
            internal IDisposable Ersatzabriss, Rueckpruefung;
        }
        private readonly List<Auftrag> _queue = new();
        private readonly HashSet<Entity> _angehalten = new();
        private readonly List<(ParkingLotToolSystem Bauer, long Generation)> _flussbeobachter = new();
        private HintergrundAuftragsregel<Entity> _regel = new();
        private Auftrag _aktiv;
        private int _nummer, _naechsterStart;
        private ParkingLotExklusivesBildSystem _gate => World.GetOrCreateSystemManaged<ParkingLotExklusivesBildSystem>();
        internal int Offen => _queue.Count + (_aktiv != null ? 1 : 0) + _angehalten.Count;
        private string RueckwegHinweis => "Test";
        private bool Sperrmeldung(Entity lot) => true;
        public bool BauplanFehlt;
        internal bool KannNeubauen(Entity lot, out string grund) { grund = "Test"; return !BauplanFehlt; }
        private static void Messe(Auftrag a, string s, Action arbeit) => arbeit();
        public bool TestStart()
        {
            if (_queue.Count == 0) return false;
            var a = _queue[0]; _queue.RemoveAt(0);
            if (!Beginne(a)) return false;
            _aktiv = a;
            a.Versuch = a.NurRueckweg ? _regel.StartRueckweg(a.Lot) : _regel.Start(a.Lot);
            a.Phase = a.NurRueckweg ? Phase.Rueckweg : Phase.Planung;
            return true;
        }
        public Auftragskennung TestKennung => _aktiv?.Kennung ?? (_queue.Count > 0 ? _queue[0].Kennung : default);
        public int TestVersuch => _aktiv?.Versuch ?? 0;
        public int TestFruehestens => _queue.Count > 0 ? _queue[0].Fruehestens : -1;
        public bool TestGesperrt(Entity lot) => _regel.Gesperrt(lot) || _angehalten.Contains(lot);
        public void TestGate(Func<int> arbeit) => Exklusiv(_aktiv, arbeit);
        private sealed class DefektePortion : IDisposable
        { public void Dispose() => throw new InvalidOperationException("Dispose"); }
        public void TestDefekteFreigabe() => _aktiv.Ersatzabriss = new DefektePortion();
        public Action TestAntwort(bool ok, Entity neu = default)
        {
            var a = _aktiv;
            return () => { if (!Aktuell(a)) return; a.Erfolg = ok; a.Neu = neu; Ende(a); };
        }
        public void TestFehler(bool uebernommen = false, Entity neu = default, Entity traeger = default)
        {
            _aktiv.Neu = neu; _aktiv.Traeger = traeger;
            if (uebernommen) _aktiv.Phase = Phase.Nebenarbeit;
            Fehler(_aktiv, new InvalidOperationException("Test"));
        }
        public void TestRueckweg() { _aktiv.Phase = Phase.Rueckweg; RueckwegFertig(_aktiv, 1, 1); }
        public bool TestFehlendePrefabs() => VerwirfUnbaubarenRueckweg(_aktiv);
        public bool TestInRuecknahme => _aktiv?.Phase == Phase.Ruecknahme;
        public void TestRueckwegFehler()
        { _aktiv.Phase = Phase.Rueckweg; Fehler(_aktiv, new InvalidOperationException("Rueckweg")); }
        public void TestLaden() { _aktiv = null; _queue.Clear(); _angehalten.Clear(); _regel = new(); }
    }
}
