using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.SceneFlow;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    public struct ParkingLotOffenerErsatz : IComponentData, ISerializable
    {
        public Entity Lot, Traeger;
        public void Serialize<TWriter>(TWriter w) where TWriter : IWriter { w.Write(Lot); w.Write(Traeger); }
        public void Deserialize<TReader>(TReader r) where TReader : IReader { r.Read(out Lot); r.Read(out Traeger); }
    }

    /** Ein Auftrag, ein Gate, dieselbe Queue auch fuer den Edit-Rueckweg.
     *  Getickt nach ToolOutputBarrier, also vor jedem Vanilla-Generator und
     *  vor Net.References in Modification2B. Kein Werkzeugupdate. */
    [DisableAutoCreation]
    internal sealed partial class ParkingLotHintergrundSystem : GameSystemBase
    {
        private enum Phase { Planung, Terrain, Besitzer, Ausgabe, Kinder, Fehlstellen, Anmeldung, Anschluesse, Nachpruefung, Nebenarbeit, Ruecknahme, Rueckweg, Rueckabschluss, Ruhe }
        private sealed class Auftrag
        {
            internal Entity Lot, Neu, Traeger;
            internal Auftragskennung Kennung;
            internal bool Fortsetzen, Erfolg;
            internal int Id, Seit, Versuch, Rueckwegpruefungen, PrefabSeit = -1;
            internal int Fruehestens, NaechstePruefung, Kinderpruefungen;
            internal bool NurRueckweg, GateOffen, AnschlussAngestossen, RueckwegAusgegeben;
            internal Phase Phase;
            internal Task<ParkingLayout> Rechnung;
            internal ParkingLotToolSystem Bauer;
            internal int RueckwegIndex;
            internal int RueckIst, RueckSoll;
            internal ParkingLotNetzRueckweg.Prueflauf Rueckpruefung;
            internal string Kindermessung;
            internal readonly Dictionary<Phase,HintergrundZeitmessung> Zeiten = new Dictionary<Phase,HintergrundZeitmessung>();
            internal readonly HintergrundTempo Tempo = new HintergrundTempo();
            internal double BildMs;
            internal bool Beendet;
            internal HintergrundPortion Ersatzabriss;
        }
        private readonly List<Auftrag> _queue = new List<Auftrag>();
        private readonly HashSet<Entity> _angehalten = new HashSet<Entity>();
        private readonly List<(ParkingLotToolSystem Bauer, long Generation)> _flussbeobachter = new List<(ParkingLotToolSystem, long)>();
        private HintergrundAuftragsregel<Entity> _regel = new HintergrundAuftragsregel<Entity>();
        private Auftrag _aktiv;
        private int _nummer;
        private int _naechsterStart, _letztesArbeitsbild = -1;
        private ParkingLotExklusivesBildSystem _gate;
        internal int Offen => _queue.Count + (_aktiv != null ? 1 : 0) + _angehalten.Count;
        /** Arbeitet gerade etwas? Ohne angehaltene Rueckwege - die warten auf den Spieler, nicht auf Zeit. */
        internal bool Laeuft => _queue.Count > 0 || _aktiv != null;
        internal bool IstErsatz(Entity lot) => _aktiv != null && _aktiv.Neu == lot
            || _queue.Any(a => a.Neu == lot);
        internal string Fortschrittshinweis
        {
            get
            {
                if (_angehalten.Count > 0) return RueckwegHinweis;
                if (_aktiv == null && _queue.Count > 0
                    && World.GetExistingSystemManaged<ParkingLotToolSystem>()?.BearbeitetLot(_queue[0].Lot) == true)
                    return ParkingLotTexte.T("hintergrundSystem.synchronizationIsWaitingFinishOrCancel");
                return _gate.Wartehinweis;
            }
        }
        private static string RueckwegHinweis => ParkingLotTexte.T("hintergrundSystem.restorationStoppedTheParkingLotRemains");

        /** Angehaltene Rueckwege: gesperrt, bis ein Spieler es meldet (Sync-Bericht). */
        internal int Angehalten => _angehalten.Count;

        /**
         * Aendert sich, sobald irgendein Auftrag weiterkommt - fuer die
         * Stillstandserkennung im Sync. Phase, Portionen und Messfenster
         * zaehlen alle als Fortschritt.
         */
        internal string Fortschrittsmarke
            => _aktiv == null
                ? "q" + _queue.Count + "|h" + _angehalten.Count
                : _aktiv.Id + "|" + _aktiv.Phase + "|" + _aktiv.RueckwegIndex + "|"
                  + _aktiv.Rueckwegpruefungen + "|" + _aktiv.Kinderpruefungen + "|"
                  + _aktiv.Versuch + "|q" + _queue.Count + "|h" + _angehalten.Count;

        /** Klartext fuer den Sync-Bericht: jeder Auftrag, seine Phase, seine Sicherung. */
        internal string Zustandsbericht()
        {
            var text = new System.Text.StringBuilder();
            void Zeile(string art, Auftrag a)
                => text.AppendLine("  " + art + " job " + a.Id + ": lot " + a.Lot.Index
                    + ", phase " + a.Phase + ", attempt " + a.Versuch + "/3"
                    + ", rollback only " + a.NurRueckweg
                    + ", frames since start " + (UnityEngine.Time.frameCount - a.Seit)
                    + ", rollback checks " + a.Rueckwegpruefungen
                    + Sicherung(a.Lot));
            text.AppendLine("Background jobs: active " + (_aktiv != null ? 1 : 0)
                + ", queued " + _queue.Count + ", stopped " + _angehalten.Count);
            if (_aktiv != null) Zeile("active", _aktiv);
            foreach (var a in _queue) Zeile("queued", a);
            foreach (var lot in _angehalten)
                text.AppendLine("  stopped lot " + lot.Index + Sicherung(lot));
            return text.ToString();
        }

        /** Was zum Rueckweg gesichert ist, und ob Besitzer und Bautypen noch leben. */
        private string Sicherung(Entity lot)
        {
            if (!EntityManager.Exists(lot)) return ", lot gone";
            if (!EntityManager.HasBuffer<ParkingLotRueckwegkurs>(lot)) return ", no rollback snapshot";
            var kurse = EntityManager.GetBuffer<ParkingLotRueckwegkurs>(lot, true);
            int ohneBesitzer = 0, ohnePrefab = 0;
            foreach (var k in kurse)
            {
                if (!EntityManager.Exists(k.Besitzer) || EntityManager.HasComponent<Deleted>(k.Besitzer)) ohneBesitzer++;
                if (!EntityManager.Exists(k.Prefab)) ohnePrefab++;
            }
            return ", snapshot " + kurse.Length + " edges (owner missing " + ohneBesitzer
                + ", prefab missing " + ohnePrefab + ")";
        }
        internal bool Gesperrt(Entity lot) => Gesperrt(lot,out _);
        private bool Gesperrt(Entity lot, out bool angehalten)
        {
            angehalten = false;
            for (int i = 0; i < 8 && lot != Entity.Null && EntityManager.Exists(lot); i++)
            {
                if (_angehalten.Contains(lot)) { angehalten = true; return true; }
                if (_regel.Gesperrt(lot) || _aktiv != null && (_aktiv.Neu == lot || _aktiv.Traeger == lot)
                    || _queue.Any(a => a.Neu == lot || a.Traeger == lot)) return true;
                if (EntityManager.HasComponent<ParkingLotPartRelation>(lot))
                    lot = EntityManager.GetComponentData<ParkingLotPartRelation>(lot).Lot;
                else if (EntityManager.HasComponent<Owner>(lot))
                    lot = EntityManager.GetComponentData<Owner>(lot).m_Owner;
                else return false;
            }
            return false;
        }
        internal string Sperrgrund(Entity lot)
            => !Gesperrt(lot, out bool angehalten) ? string.Empty
                : angehalten ? RueckwegHinweis : ParkingLotTexte.T("hintergrundSystem.thisParkingLotIsBeingRebuilt");

        internal bool Sperrmeldung(Entity lot)
        {
            var text = Sperrgrund(lot);
            if (string.IsNullOrEmpty(text)) return false;
            World.GetOrCreateSystemManaged<ParkingLotUISystem>().SetStatus(text);
            ParkingLotNetzRueckweg.Melde($"Lotsperre {lot.Index}: {text}; offene Auftraege {Offen}.");
            return true;
        }

        [Preserve]
        protected override void OnCreate()
        { base.OnCreate(); _gate = World.GetOrCreateSystemManaged<ParkingLotExklusivesBildSystem>(); }

        [Preserve]
        protected override void OnUpdate()
        {
            var spiel = GameManager.instance;
            if (spiel == null || spiel.isGameLoading || !spiel.gameMode.IsGame()) return;
            int bild = UnityEngine.Time.frameCount;
            if (bild == _letztesArbeitsbild) return;
            _letztesArbeitsbild = bild;
            for (int i = _flussbeobachter.Count-1; i >= 0; i--)
            {
                var (b, generation) = _flussbeobachter[i];
                if (!Auftraege.Register.Bereit || generation != Auftraege.Register.Generation) continue;
                try { b.HintergrundPflegeFluss(); }
                catch (Exception e)
                {
                    ParkingLotNetzRueckweg.Melde("Flussbeobachtung beendet: " + e.Message + "; keine Flussabnahme behauptet.");
                    World.DestroySystemManaged(b); _flussbeobachter.RemoveAt(i); continue;
                }
                if (b.HintergrundBeobachtetFluss) continue;
                World.DestroySystemManaged(b); _flussbeobachter.RemoveAt(i);
            }
            if (_aktiv == null)
            {
                if (_queue.Count == 0) return;
                if (!HintergrundTakt.Faellig(bild,_naechsterStart)) return;
                var next = _queue.Find(a => HintergrundTakt.Faellig(bild,a.Fruehestens));
                if (next == null) return;
                // Dasselbe Lot darf seinen bereits begonnenen Edit beenden.
                // Andere Lots bleiben frei; 0/0 regelt nur Definitionsbilder.
                if (World.GetOrCreateSystemManaged<ParkingLotToolSystem>().BearbeitetLot(next.Lot)) return;
                _queue.Remove(next);
                if (!Beginne(next)) return;
                if (!next.NurRueckweg && !KannNeubauen(next.Lot,out var grund))
                {
                    _regel.Ende(next.Lot);
                    try { World.GetOrCreateSystemManaged<ParkingLotSyncSystem>().MeldeBauplanFehlt(next.Lot,grund); }
                    finally
                    {
                        _naechsterStart = bild + HintergrundTakt.Auftragspause;
                        Auftraege.Beende(next.Kennung, Entity.Null, false);
                    }
                    return;
                }
                _aktiv = next;
                next.Seit = UnityEngine.Time.frameCount;
                next.Versuch = next.NurRueckweg ? _regel.StartRueckweg(next.Lot) : _regel.Start(next.Lot);
                next.Phase = next.NurRueckweg ? (EntityManager.HasComponent<ParkingLotOffenerErsatz>(next.Lot)
                    || EntityManager.HasBuffer<ParkingLotStufeAKnoten>(next.Lot) ? Phase.Ruecknahme : Phase.Rueckweg) : Phase.Planung;
                Diagnose(() => ParkingLotNetzRueckweg.Melde($"Start Lot {next.Lot.Index}, Versuch {next.Versuch}/3; Auftrag {next.Id}; ohne Werkzeugeinstieg."));
                if (!next.NurRueckweg)
                {
                    try { Messe(next,"Start",() => { next.Bauer = ParkingLotToolSystem.Bauarbeiter(World); next.Rechnung = next.Bauer.BereiteHintergrund(next.Lot,next.Id); }); }
                    catch (Exception e) { Fehler(next,e); }
                }
                // Kein Start + Prefabvorbereitung/Abriss im selben Bild.
                return;
            }
            var a = _aktiv;
            if (!Aktuell(a) || a.GateOffen) return;
            if (a.BildMs > 0)
            {
                a.Bauer?.HintergrundBildzeit(a.BildMs); a.Tempo.MeldeBild(a.BildMs); a.BildMs = 0;
            }
            if (!HintergrundTakt.Faellig(bild,a.NaechstePruefung)) return;
            a.NaechstePruefung = bild + (a.Phase == Phase.Planung || a.Phase == Phase.Ausgabe
                || a.Phase == Phase.Kinder || a.Phase == Phase.Fehlstellen || a.Phase == Phase.Anmeldung || a.Phase == Phase.Nachpruefung || a.Phase == Phase.Nebenarbeit
                || a.Phase == Phase.Ruecknahme || a.Phase == Phase.Rueckweg || a.Phase == Phase.Rueckabschluss
                ? 1 : HintergrundTakt.Pruefabstand);
            try { Messe(a,"Pruefung",() => Pflege(a)); }
            catch (Exception e) { Fehler(a,e); }
        }

        private void Pflege(Auftrag a)
        {
            if (!Aktuell(a)) return;
            int bild = UnityEngine.Time.frameCount;
            switch (a.Phase)
            {
                case Phase.Planung:
                    if (!a.Rechnung.IsCompleted) return;
                    if (!a.Bauer.HintergrundPrefabs(a.Rechnung.GetAwaiter().GetResult()))
                    {
                        if (!a.Bauer.HintergrundVorbereitungFertig) return;
                        if (a.PrefabSeit < 0) a.PrefabSeit = bild;
                        if (bild-a.PrefabSeit > 120) throw new InvalidOperationException("Bauprefabs nach 120 Bildern nicht bereit; kein eigener Abriss.");
                        return;
                    }
                    Exklusiv(a, () =>
                    {
                        int n = a.Bauer.HintergrundAbriss();
                        if (a.Bauer.HintergrundAbrissFertig) { a.Phase = Phase.Terrain; a.Seit = UnityEngine.Time.frameCount; }
                        return n;
                    });
                    break;
                case Phase.Terrain:
                    if (!a.Bauer.HintergrundTerrainFertig()) return;
                    Exklusiv(a, () =>
                    {
                        int n = a.Bauer.HintergrundStufeA();
                        if (n != 1) throw new InvalidOperationException($"Stufe A: {n}/1 Lot-Definitionen.");
                        a.Phase = Phase.Besitzer; a.Seit = UnityEngine.Time.frameCount; return n;
                    });
                    break;
                case Phase.Besitzer:
                    if (!a.Bauer.HintergrundBesitzerDa())
                    { if (bild-a.Seit > 90) throw new InvalidOperationException("Stufe A nach 90 Bildern nicht materialisiert."); return; }
                    a.Neu = a.Bauer.HintergrundLot; a.Traeger = a.Bauer.HintergrundTraeger;
                    a.Phase = Phase.Ausgabe;
                    a.Seit = bild;
                    break;
                case Phase.Ausgabe:
                    Exklusiv(a, () =>
                    {
                        int n = a.Bauer.HintergrundBauportion();
                        if (a.Bauer.HintergrundAusgabeFertig) { a.Phase = Phase.Kinder; a.Seit = UnityEngine.Time.frameCount; }
                        return n;
                    });
                    break;
                case Phase.Kinder:
                    if (!a.Bauer.HintergrundKinderDa(out var messung))
                    {
                        if (!a.Bauer.HintergrundKinderpruefungFertig) return;
                        if (++a.Kinderpruefungen >= 3)
                        { a.Kindermessung = messung; a.Phase = Phase.Fehlstellen; }
                        return;
                    }
                    ParkingLotNetzRueckweg.Melde(messung);
                    a.Phase = Phase.Anmeldung;
                    break;
                case Phase.Anmeldung:
                    Exklusiv(a, () =>
                    {
                        if (a.Bauer.HintergrundMeldeAnschluesse()) { a.Phase = Phase.Anschluesse; a.Seit = UnityEngine.Time.frameCount; }
                        return 0;
                    });
                    break;
                case Phase.Fehlstellen:
                    if (!a.Bauer.HintergrundFehlstellen()) return;
                    throw new InvalidOperationException(a.Kindermessung);
                case Phase.Anschluesse:
                    if (!a.Bauer.HintergrundAnschluesseDa(out int angeschlossen,out int erfasst))
                    { if (bild-a.Seit > 90) throw new InvalidOperationException($"Versorgungsanschluesse {angeschlossen}/{erfasst} nach 90 Bildern."); return; }
                    ParkingLotNetzRueckweg.Melde($"Versorgungsanschluesse beidseitig geprueft: {angeschlossen}/{erfasst}; 0 direkte physische Netzeintraege.");
                    a.Phase = Phase.Nachpruefung; a.Seit = UnityEngine.Time.frameCount;
                    break;
                case Phase.Nachpruefung:
                    if (bild-a.Seit < 30) return;
                    if (!a.Bauer.HintergrundWirtschaftBereit())
                    { if (bild-a.Seit > 120) throw new InvalidOperationException("Gebuehr/Begleiter nach 120 Bildern nicht uebernommen."); return; }
                    bool spuren = a.Bauer.HintergrundFahrwegePortion();
                    if (!a.Bauer.HintergrundSpurpruefungFertig) return;
                    if (Mod.Aus("hintergrund-spurpruefung") || !spuren)
                        throw new InvalidOperationException("Fahrwege-Nachpruefung fehlgeschlagen.");
                    ParkingLotNetzRueckweg.Melde("Fahrwege-Nachpruefung: echte Fahrspuren vorhanden, Fahrwert-/Kostenabweichungen 0.");
                    // Letzter exklusiver Abschluss: keine fremden Definitionen
                    // laufen in die Uebernahme/Nacharbeit hinein.
                    Exklusiv(a, () =>
                    {
                        ParkingLotSchrittmarke.Setze($"Hintergrund: Abschluss Lot {a.Lot.Index} -> {a.Neu.Index}");
                        a.Bauer.HintergrundUebernahme();
                        if (!ParkingLotHintergrundAbschluss.MerkeNebenarbeit(EntityManager, a.Neu, a.Traeger))
                            throw new InvalidOperationException("Neubau nach der Uebernahme nicht mehr gueltig; kein Datenstand geschrieben.");
                        ParkingLotSchrittmarke.Setze($"Hintergrund: Datenstand und Nebenarbeit fuer Lot {a.Neu.Index} gespeichert");
                        a.Phase = Phase.Nebenarbeit; a.Seit = UnityEngine.Time.frameCount; return 0;
                    });
                    break;
                case Phase.Nebenarbeit:
                    if (World.GetOrCreateSystemManaged<ParkingLotCleanupSystem>().AbrissLaeuft) return;
                    int nebenstand = a.Bauer.HintergrundNebenstand();
                    if (nebenstand == 0) return;
                    if (nebenstand == 1) { Exklusiv(a, () => a.Bauer.HintergrundNebenbild()); return; }
                    if (nebenstand == 3)
                    {
                        Exklusiv(a, () => { int n = a.Bauer.HintergrundVerwerfeNebenbau(); a.Seit = UnityEngine.Time.frameCount; return n; });
                        return;
                    }
                    if (nebenstand == 4) { Exklusiv(a, () => a.Bauer.HintergrundMeldeNebenanschluesse()); return; }
                    a.Erfolg = true;
                    EntityManager.RemoveComponent<ParkingLotNebenarbeitOffen>(a.Neu);
                    ParkingLotNetzRueckweg.Melde($"Abschluss Lot {a.Neu.Index}, Stand 8; offene Auftraege {Offen-1}.");
                    Ende(a);
                    break;
                case Phase.Ruecknahme:
                    Exklusiv(a, () =>
                    {
                        a.Bauer?.HintergrundRuecknahme();
                        if (a.Bauer != null && !a.Bauer.HintergrundRuecknahmeFertig) return 0;
                        RaumeGespeichertesA(a.Lot);
                        if (EntityManager.HasComponent<ParkingLotOffenerErsatz>(a.Lot))
                        {
                            var ersatz = EntityManager.GetComponentData<ParkingLotOffenerErsatz>(a.Lot);
                            ParkingLotNetzerhalt.EntferneGelieheneVerweise(EntityManager,a.Lot,ersatz.Lot,ersatz.Traeger);
                            if (a.Bauer == null)
                            {
                                a.Ersatzabriss ??= new HintergrundPortion(ParkingLotErsatzabriss.Schritte(EntityManager,a.Lot,ersatz.Lot,ersatz.Traeger),a.Tempo);
                                var uhr = Stopwatch.StartNew();
                                a.Ersatzabriss.Weiter(() => uhr.Elapsed.TotalMilliseconds);
                                if (!a.Ersatzabriss.Fertig) return 0;
                            }
                        }
                        a.Phase = Phase.Ruhe; a.Seit = UnityEngine.Time.frameCount; return 0;
                    });
                    break;
                case Phase.Ruhe:
                    if (bild-a.Seit < 3 || World.GetOrCreateSystemManaged<ParkingLotCleanupSystem>().AbrissLaeuft) return;
                    if (EntityManager.HasComponent<ParkingLotOffenerErsatz>(a.Lot))
                    {
                        var ersatz = EntityManager.GetComponentData<ParkingLotOffenerErsatz>(a.Lot);
                        int rest = ParkingLotErsatzabriss.Eigene(EntityManager,a.Lot,ersatz.Lot,ersatz.Traeger).Count;
                        if (rest != 0) throw new InvalidOperationException($"Ruecknahme unvollstaendig: {rest} eigene Stage-B-Teile leben; 0 Rueckwegdefinitionen.");
                        ParkingLotNetzRueckweg.Melde("Ruecknahme vollstaendig: 0 eigene Stage-B-Netzteile/Kinder leben; Rueckweg freigegeben.");
                    }
                    a.Phase = Phase.Rueckweg; a.Seit = UnityEngine.Time.frameCount;
                    break;
                case Phase.Rueckweg:
                    if (!EntityManager.HasBuffer<ParkingLotRueckwegkurs>(a.Lot)) { RueckwegFertig(a,0,0); return; }
                    // Erst wenn der Aufraeumer fertig ist (16.10.): nach F5 hinter dem
                    // Uebernehmen reisst er noch den neuen Parkplatz ab, und dessen
                    // Anschluesse an Stadtstrassen sind dann noch nicht wieder frei.
                    if (a.RueckwegIndex == 0 && !a.RueckwegAusgegeben
                        && World.GetOrCreateSystemManaged<ParkingLotCleanupSystem>().AbrissLaeuft) return;
                    if (a.RueckwegIndex == 0 && !a.RueckwegAusgegeben && VerwirfUnbaubarenRueckweg(a)) return;
                    if (!a.RueckwegAusgegeben)
                    {
                        Exklusiv(a, () =>
                        {
                            int portion = Math.Min(64,a.Tempo.Einheiten);
                            int n = ParkingLotNetzRueckweg.Erzeuge(EntityManager,a.Lot,a.Id,a.RueckwegIndex,portion,a.Tempo.BudgetMs,out int weiter);
                            a.RueckwegIndex = weiter;
                            a.RueckwegAusgegeben = a.RueckwegIndex >= EntityManager.GetBuffer<ParkingLotRueckwegkurs>(a.Lot,true).Length;
                            if (a.RueckwegAusgegeben) ParkingLotNetzRueckweg.Melde($"Rueckwegausgabe Lot {a.Lot.Index}: Planindex {a.RueckwegIndex}; Ausgabe fertig.");
                            a.Seit = UnityEngine.Time.frameCount;
                            return n;
                        });
                        return;
                    }
                    if (!a.AnschlussAngestossen)
                    {
                        Exklusiv(a, () => { ParkingLotNetzRueckweg.MeldeAnschluesse(EntityManager,a.Lot); a.AnschlussAngestossen = true; return 0; });
                        return;
                    }
                    if (a.Rueckpruefung == null)
                        a.Rueckpruefung = new ParkingLotNetzRueckweg.Prueflauf(EntityManager,a.Lot,diagnose:true,tempo:a.Tempo);
                    a.Rueckpruefung.Weiter();
                    if (!a.Rueckpruefung.Fertig) return;
                    int ist = a.Rueckpruefung.Ist, soll = a.Rueckpruefung.Soll;
                    bool richtig = a.Rueckpruefung.Richtig;
                    a.Rueckpruefung.Dispose(); a.Rueckpruefung = null;
                    if (richtig) { RueckwegFertig(a,ist,soll); return; }
                    a.Rueckwegpruefungen++;
                    ParkingLotNetzRueckweg.Melde($"Rueckweg OFFEN: Kanten {ist}/{soll}; Pruefung {a.Rueckwegpruefungen}/{HintergrundRueckwegfrist.MaxPruefungen}; Lot bleibt gesperrt, 0 weitere Definitionen.");
                    if (!HintergrundRueckwegfrist.Weiter(a.Rueckwegpruefungen)) { HalteRueckwegAn(a); return; }
                    a.NaechstePruefung = bild + HintergrundRueckwegfrist.Fenster;
                    break;
                case Phase.Rueckabschluss:
                    RueckwegFertig(a,a.RueckIst,a.RueckSoll);
                    break;
            }
        }

        internal bool KannNeubauen(Entity lot, out string grund)
        {
            grund = "Lot fehlt oder ist kein dauerhafter Parkplatz; Reparatur/Bauplan erforderlich";
            return ParkingLotNetzRueckweg.Lebt(EntityManager,lot)
                && EntityManager.HasComponent<Game.Areas.Area>(lot) && !EntityManager.HasComponent<Game.Tools.Temp>(lot)
                && ParkingLotBaukontextLeser.TryRead(EntityManager,lot,out _,out grund,melden:false);
        }

        private static void Messe(Auftrag a, string arbeit, Action schritt)
        {
            var phase = a.Phase;
            var uhr = Stopwatch.StartNew();
            try { schritt(); }
            finally
            {
                double ms = uhr.Elapsed.TotalMilliseconds;
                a.BildMs += ms;
                if (!a.Zeiten.TryGetValue(phase,out var zeit)) a.Zeiten[phase] = zeit = new HintergrundZeitmessung();
                zeit.Fuege(UnityEngine.Time.frameCount,ms);
                if (ms > 50) ParkingLotHintergrundDiagnose.Sicher(() => ParkingLotNetzRueckweg.Melde(FormattableString.Invariant(
                    $"Bildzeit Ausreisser Auftrag {a.Id}, Phase {phase}, {arbeit}: Spielthread {ms:F3} ms, Bild {UnityEngine.Time.frameCount}.")));
                foreach (var p in a.Zeiten.Keys.ToArray())
                    if (p != a.Phase || a.Beendet)
                    {
                        var z = a.Zeiten[p];
                        ParkingLotHintergrundDiagnose.Sicher(() => ParkingLotNetzRueckweg.Melde(FormattableString.Invariant(
                            $"Phasenzeit Auftrag {a.Id}, Versuch {a.Versuch}, Phase {p}: Summe {z.Summe:F3} ms, Max {z.Max:F3} ms, Bilder {z.Bilder}.")));
                        a.Zeiten.Remove(p);
                    }
            }
        }

        private bool _wiederaufnahmeOffen;

        protected override void OnGameLoaded(Context context)
        {
            base.OnGameLoaded(context);
            _gate.Verwerfe(_aktiv?.Lot ?? Entity.Null);
            _aktiv?.Ersatzabriss?.Dispose();
            _aktiv?.Rueckpruefung?.Dispose();
            if (_aktiv?.Bauer != null) World.DestroySystemManaged(_aktiv.Bauer);
            foreach (var b in _flussbeobachter) World.DestroySystemManaged(b.Bauer);
            _flussbeobachter.Clear();
            _aktiv = null; _queue.Clear(); _angehalten.Clear(); _neubauStattSperre.Clear(); _regel = new HintergrundAuftragsregel<Entity>();
            _naechsterStart = 0; _letztesArbeitsbild = -1;
            _wiederaufnahmeOffen = true;
        }

        // BayObjects ruft erst nach den nativen Traeger-Puffern auf.
        internal void NimmNachLadenWiederAuf()
        {
            if (!_wiederaufnahmeOffen) return;
            _wiederaufnahmeOffen = false;
            using (var offenQuery = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ParkingLotRueckwegsicherungOffen>()))
            using (var offen = offenQuery.ToEntityArray(Allocator.Temp))
                foreach (var lot in offen) ParkingLotNetzRueckweg.VerwerfeOffeneSicherung(EntityManager,lot);
            using var query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ParkingLotRueckwegkurs>());
            using var lots = query.ToEntityArray(Allocator.Temp);
            foreach (var lot in lots)
            {
                Einreihen(lot, true, grund: Auftragsgrund.Wiederaufnahme);
                var a = _queue.Find(x => x.Lot == lot);
                if (a != null && EntityManager.HasComponent<ParkingLotOffenerErsatz>(lot)) a.Phase = Phase.Ruecknahme;
            }
            // Issue #17: verworfener Rueckweg, Neubau noch nicht gesichert.
            using var faelligQuery = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ParkingLotNeubauFaellig>(),
                ComponentType.Exclude<ParkingLotRueckwegkurs>(), ComponentType.Exclude<Deleted>());
            using var faellig = faelligQuery.ToEntityArray(Allocator.Temp);
            foreach (var lot in faellig)
            {
                ParkingLotNetzRueckweg.Melde($"Lot {lot.Index}: Neubau nach verworfenem Rueckweg war offen; wird eingereiht.");
                Einreihen(lot, fruehestens: UnityEngine.Time.frameCount + HintergrundTakt.Versuchspause, grund: Auftragsgrund.Wiederaufnahme);
            }
        }

        private void RaumeGespeichertesA(Entity lot)
        {
            if (EntityManager.HasComponent<ParkingLotOffenerErsatz>(lot)
                || !EntityManager.HasBuffer<ParkingLotStufeAKnoten>(lot)
                || (!EntityManager.HasComponent<ParkingLotStufeAPrefab>(lot)
                    && !EntityManager.HasComponent<ParkingLotStufeAPrefabID>(lot))) return;
            var stufeA = EntityManager.GetBuffer<ParkingLotStufeAKnoten>(lot,true);
            var punkte = new Unity.Mathematics.float3[stufeA.Length];
            for (var i = 0; i < stufeA.Length; i++) punkte[i] = stufeA[i].Position;
            var prefab = ParkingLotStufeA.Prefab(EntityManager, lot);
            if (prefab == Entity.Null)
            {
                ParkingLotNetzRueckweg.Melde($"Stufe A Lot {lot.Index}: Prefab nicht aufloesbar; keine Flaeche auf Verdacht geloescht.");
                return;
            }
            using var q = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Game.Areas.Area>(),
                ComponentType.ReadOnly<Game.Areas.Node>(), ComponentType.ReadOnly<Game.Prefabs.PrefabRef>(),
                ComponentType.Exclude<Game.Tools.Temp>(),ComponentType.Exclude<Deleted>());
            using var areas = q.ToEntityArray(Allocator.Temp);
            foreach (var e in areas)
                if (e != lot && !EntityManager.HasComponent<ParkingLotBuildReceipt>(e)
                    && EntityManager.GetComponentData<Game.Prefabs.PrefabRef>(e).m_Prefab == prefab
                    && ParkingLotToolSystem.AreaNodesMatch(punkte,EntityManager.GetBuffer<Game.Areas.Node>(e,true)))
                { EntityManager.AddComponent<Deleted>(e); ParkingLotNetzRueckweg.Melde($"Gespeicherte leere Stufe-A-Flaeche {e.Index} zum Abriss angemeldet."); }
        }
    }
}
