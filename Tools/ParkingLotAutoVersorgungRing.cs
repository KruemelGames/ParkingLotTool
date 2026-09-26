using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private readonly List<Entity> _avAlleEigenen = new List<Entity>();

        /**
         * EINE GEBAUTE LEITUNG - ALS ZWEI PUNKTE, NICHT ALS ZWEI ENTITIES.
         *
         * Ein Apply kann eine Kante teilen: liegt der Startpunkt mitten auf
         * einer unserer Kanten, werden aus einer Entity zwei neue, und jede
         * Merkliste aus Entities zeigt ins Leere. Die Geometrie bleibt, wo sie
         * war - deshalb merken wir uns Orte.
         *
         * `ZielIstStadt` haelt fest, dass das andere Ende an einer fremden
         * Strasse hing. Diese Leitung bringt echten Strom herein; alle anderen
         * reichen ihn nur weiter.
         */
        private struct AvVerbindung
        {
            internal float3 Start, Ziel;
            internal bool ZielIstStadt;
        }

        private readonly List<AvVerbindung> _avVerbindungen = new List<AvVerbindung>();

        /**
         * Was mit EINEM Netz in diesem Lauf schon passiert ist.
         *
         * Alle Kanten eines Netzes zeigen auf dasselbe Objekt - damit ist es
         * gleich, ueber welche Kante gefragt wird.
         */
        private sealed class AvNetzstand
        {
            internal int Versuche;
            internal readonly HashSet<Entity> GesperrteZiele = new HashSet<Entity>();
        }

        private readonly Dictionary<Entity, AvNetzstand> _avNetzstand =
            new Dictionary<Entity, AvNetzstand>();
        private HashSet<int> _avStadtStrom, _avStadtWasser;

        /** Die Leitungen und Rohre im Umkreis, die uns nicht gehoeren. */
        private readonly List<Entity> _avFremdleitungen = new List<Entity>();

        private void AvErfasseStadtpfade()
        {
            _avAlleEigenen.Clear();
            var query = GetEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>(),
                ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using var kanten = query.ToEntityArray(Allocator.Temp);
            var stromWurzeln = new HashSet<int>(); var wasserWurzeln = new HashSet<int>();
            var stromKnoten = new HashSet<int>(); var wasserKnoten = new HashSet<int>();
            void Merke(Entity e)
            {
                var s = AvFlussknoten(e, true); var w = AvFlussknoten(e, false);
                if (s != Entity.Null) stromKnoten.Add(s.Index);
                if (w != Entity.Null) wasserKnoten.Add(w.Index);
            }
            var eigeneTraegerkanten = new HashSet<Entity>(SammleUnsereKanten(_avTraeger));
            foreach (var e in kanten)
            {
                var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                var road = EntityManager.HasComponent<RoadData>(prefab);
                var eigen = EntityManager.HasComponent<Owner>(e);
                if ((eigen && road) || eigeneTraegerkanten.Contains(e)) _avAlleEigenen.Add(e);
                if (!road && !EntityManager.HasComponent<ElectricityConnectionData>(prefab)
                    && !EntityManager.HasComponent<WaterPipeConnectionData>(prefab)) continue;
                Merke(e);
                var edge = EntityManager.GetComponentData<Edge>(e);
                Merke(edge.m_Start); Merke(edge.m_End);
                if (EntityManager.HasBuffer<ConnectedNode>(e))
                    foreach (var n in EntityManager.GetBuffer<ConnectedNode>(e, true)) Merke(n.m_Node);
                if (!road || eigen || !KanteNimmtVersorgung(e)) continue;
                var s = AvFlussknoten(e, true); var w = AvFlussknoten(e, false);
                if (s != Entity.Null) stromWurzeln.Add(s.Index);
                if (w != Entity.Null) wasserWurzeln.Add(w.Index);
            }
            // Globale Simulationsquellen/-senken sind keine Strassenverbindung.
            // Deshalb nur Flussknoten dauerhafter Netzkanten und ihrer Knoten.
            var sk = new List<int2>(); var wk = new List<int2>();
            using var strom = GetEntityQuery(ComponentType.ReadOnly<Game.Simulation.ElectricityFlowEdge>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()).ToEntityArray(Allocator.Temp);
            foreach (var e in strom)
            {
                var k = EntityManager.GetComponentData<Game.Simulation.ElectricityFlowEdge>(e);
                if (k.m_Capacity > 0 && !k.isDisconnected
                    && stromKnoten.Contains(k.m_Start.Index) && stromKnoten.Contains(k.m_End.Index))
                    sk.Add(new int2(k.m_Start.Index, k.m_End.Index));
            }
            using var wasser = GetEntityQuery(ComponentType.ReadOnly<Game.Simulation.WaterPipeEdge>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()).ToEntityArray(Allocator.Temp);
            foreach (var e in wasser)
            {
                var k = EntityManager.GetComponentData<Game.Simulation.WaterPipeEdge>(e);
                if (k.m_FreshCapacity > 0 && k.m_SewageCapacity > 0
                    && (k.m_Flags & (Game.Simulation.WaterPipeEdgeFlags.WaterDisconnected
                        | Game.Simulation.WaterPipeEdgeFlags.SewageDisconnected)) == 0
                    && wasserKnoten.Contains(k.m_Start.Index) && wasserKnoten.Contains(k.m_End.Index))
                    wk.Add(new int2(k.m_Start.Index, k.m_End.Index));
            }
            _avStadtStrom = Versorgungsnetz.Erreichbar(stromWurzeln, sk);
            _avStadtWasser = Versorgungsnetz.Erreichbar(wasserWurzeln, wk);
            Mod.log.Info($"PLT-Autoversorgung STADTPFAD: {stromWurzeln.Count}/{wasserWurzeln.Count} Stadtwurzeln, "
                + $"{_avStadtStrom.Count}/{_avStadtWasser.Count} erreichbare Strom-/Wasserknoten; 0 geplante Kanten als Quelle.");
        }

        private bool AvZielHatStadtpfad(Entity e)
        {
            if (!EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e)
                || EntityManager.HasComponent<Temp>(e) || !KanteNimmtVersorgung(e)) return false;
            // Eine Stadtstrasse ist die verlangte Wurzel. Ob dort Versorgung
            // fliesst, entscheidet weiterhin ausschliesslich die Flussabnahme.
            if (!EntityManager.HasComponent<Owner>(e)) return true;
            var s = AvFlussknoten(e, true); var w = AvFlussknoten(e, false);
            return s != Entity.Null && w != Entity.Null && _avStadtStrom.Contains(s.Index)
                && _avStadtWasser.Contains(w.Index);
        }

        /**
         * Wieviele unserer Netze fuehrt CS2 gerade am Stadtnetz?
         *
         * Setzt einen frischen `AvErfasseStadtpfade` voraus - die Antwort ist
         * nur so aktuell wie der Graph, aus dem sie kommt.
         */
        private int AvGedeckteNetze(out int gesamt)
        {
            var gruppen = SammleVersorgungsgruppen(
                SammleUnsereKanten(_avTraeger).FindAll(KanteNimmtVersorgung));
            var erreicht = 0;
            foreach (var gruppe in gruppen)
                if (gruppe.TrueForAll(AvZielHatStadtpfad)) erreicht++;
            gesamt = gruppen.Count;
            return erreicht;
        }

        private bool AvMesseNetzabdeckung()
        {
            var erreicht = AvGedeckteNetze(out var gesamt);
            var text = $"PLT-Autoversorgung NETZABDECKUNG: {erreicht}/{gesamt} versorgungsfaehige Netze "
                + "mit Stadtpfad fuer Strom UND Wasser/Abwasser; Flussabnahme separat.";
            if (erreicht == gesamt) Mod.log.Info(text); else Mod.log.Warn(text + " Anschlussnachweis unvollstaendig.");
            return erreicht == gesamt;
        }

        /**
         * WER MIT WEM ZUSAMMENHAENGT - UND WER DIE STADT ERREICHT.
         *
         * Jedes unserer versorgungsfaehigen Netze ist ein Teil, die Stadt ist
         * ein weiteres. Zusammengelegt wird nach zwei Quellen:
         *
         *   - was CS2 selbst schon am Stadtnetz fuehrt (`AvZielHatStadtpfad`),
         *   - was wir in diesem Lauf gebaut haben (`_avVerbindungen`).
         *
         * Die zweite Quelle ist der Grund, warum nicht mehr gewartet werden
         * muss: eine Leitung, die wir eben angewandt haben, verbindet, auch
         * wenn CS2s Flussgraph noch Sekunden braucht, um es zu bestaetigen.
         * Bestaetigt wird es trotzdem - in der Netzabdeckung am Ende.
         */
        private sealed class AvTeile
        {
            internal readonly List<List<Entity>> Gruppen;
            internal readonly int Stadt;
            private readonly int[] _wurzel;
            private readonly Dictionary<Entity, int> _kanteZuGruppe =
                new Dictionary<Entity, int>();

            internal AvTeile(List<List<Entity>> gruppen)
            {
                Gruppen = gruppen;
                Stadt = gruppen.Count;
                _wurzel = new int[gruppen.Count + 1];
                for (var i = 0; i < _wurzel.Length; i++) _wurzel[i] = i;
                for (var i = 0; i < gruppen.Count; i++)
                    foreach (var e in gruppen[i]) _kanteZuGruppe[e] = i;
            }

            internal int Finde(int i)
            {
                while (_wurzel[i] != i) { _wurzel[i] = _wurzel[_wurzel[i]]; i = _wurzel[i]; }
                return i;
            }

            internal void Verbinde(int a, int b)
            {
                a = Finde(a); b = Finde(b);
                if (a == b) return;
                // Die Stadt bleibt immer die Wurzel - dann heisst "haengt an
                // der Stadt" genau `Finde(i) == Finde(Stadt)`.
                if (b == Finde(Stadt)) _wurzel[a] = b; else _wurzel[b] = a;
            }

            internal bool AnDerStadt(int gruppe) => Finde(gruppe) == Finde(Stadt);

            internal int GruppeVon(Entity kante)
                => _kanteZuGruppe.TryGetValue(kante, out var i) ? i : -1;

            internal int OffeneTeile()
            {
                var teile = new HashSet<int>();
                for (var i = 0; i < Gruppen.Count; i++)
                    if (!AnDerStadt(i)) teile.Add(Finde(i));
                return teile.Count;
            }
        }

        private int _avPerKnotenAnStadt;

        private AvTeile AvErmittleTeile(List<List<Entity>> gruppen)
        {
            var teile = new AvTeile(gruppen);
            _avPerKnotenAnStadt = 0;
            for (var i = 0; i < gruppen.Count; i++)
            {
                if (AvHaengtAnStadtstrasse(gruppen[i])) _avPerKnotenAnStadt++;
                else if (!gruppen[i].TrueForAll(AvZielHatStadtpfad)) continue;
                teile.Verbinde(i, teile.Stadt);
            }
            foreach (var v in _avVerbindungen)
            {
                var a = AvGruppeAmPunkt(gruppen, v.Start);
                if (a < 0) continue;
                var b = v.ZielIstStadt ? teile.Stadt : AvGruppeAmPunkt(gruppen, v.Ziel);
                if (b < 0) continue;
                teile.Verbinde(a, b);
            }
            return teile;
        }

        /**
         * HAENGT DAS NETZ SCHON PER KNOTEN AN EINER STADTSTRASSE?
         *
         * Eine Gasse am Strassenknoten bekommt Strom und Wasser ueber die
         * eingebauten Leitungen beider Strassen - so wie jede Vanilla-Strasse,
         * ab dem Moment des Baus. CS2s Flussgraph kennt frische Kanten aber
         * erst Sekunden spaeter. Nach einem Edit sind alle Gassen frisch: am
         * 2026-09-26 hielt die Planung deshalb 10 von 10 Gassen fuer
         * unangeschlossen und legte 23 s lang je Anlauf eine Vorschau-Leitung
         * an die Stadtstrasse - die CS2 dafuer jedes Mal teilte, sichtbar als
         * flackernde Ueberwege. Alle 30 Anlaeufe scheiterten.
         *
         * Dieselbe Regel wie `AvZielHatStadtpfad` fuer eine Stadtstrasse
         * selbst: sie ist die Wurzel. Gefragt wird nach dem Aufbau, nicht nach
         * dem Graphen; bestaetigt wird weiterhin in der Netzabdeckung.
         */
        private bool AvHaengtAnStadtstrasse(List<Entity> gruppe)
        {
            foreach (var e in gruppe)
            {
                if (!EntityManager.Exists(e) || !EntityManager.HasComponent<Edge>(e)
                    || !KanteNimmtVersorgung(e)) continue;
                var kante = EntityManager.GetComponentData<Edge>(e);
                if (KnotenHatStadtstrasse(kante.m_Start) || KnotenHatStadtstrasse(kante.m_End))
                    return true;
            }
            return false;
        }

        private bool KnotenHatStadtstrasse(Entity knoten)
        {
            if (!EntityManager.Exists(knoten) || !EntityManager.HasBuffer<ConnectedEdge>(knoten))
                return false;
            var angehaengt = EntityManager.GetBuffer<ConnectedEdge>(knoten, true);
            for (var i = 0; i < angehaengt.Length; i++)
            {
                var k = angehaengt[i].m_Edge;
                if (EntityManager.HasComponent<Owner>(k) || EntityManager.HasComponent<Temp>(k)
                    || EntityManager.HasComponent<Deleted>(k)) continue;
                if (KanteNimmtVersorgung(k)) return true;
            }
            return false;
        }

        /** Welches Netz laeuft durch diesen Punkt? -1, wenn keins. */
        private int AvGruppeAmPunkt(List<List<Entity>> gruppen, float3 punkt)
        {
            for (var i = 0; i < gruppen.Count; i++)
                foreach (var e in gruppen[i])
                {
                    if (!EntityManager.HasComponent<Curve>(e)) continue;
                    var bogen = EntityManager.GetComponentData<Curve>(e).m_Bezier;
                    if (MathUtils.Distance(bogen.xz, punkt.xz, out _) <= 0.1f) return i;
                }
            return -1;
        }

        /**
         * DARF DIESE KANTE ZIEL SEIN?
         *
         * Eine fremde Strasse immer - sie bringt den Strom herein. Eine eigene
         * nur, wenn sie zu einem ANDEREN Teil gehoert: sonst entstuende ein
         * Ring, der an keiner Stadt haengt. Genau daran haengt die Forderung,
         * dass am Ende eine Zone nach draussen verbunden sein muss.
         */
        private bool AvZielErlaubt(Entity ziel, int meinTeil, AvTeile teile)
        {
            if (!EntityManager.Exists(ziel) || EntityManager.HasComponent<Deleted>(ziel)
                || EntityManager.HasComponent<Temp>(ziel)) return false;
            if (!KanteNimmtVersorgung(ziel)) return false;
            if (!EntityManager.HasComponent<Owner>(ziel)) return true;
            var g = teile.GruppeVon(ziel);
            if (g < 0) return false;
            return teile.Finde(g) != meinTeil;
        }

        /** Der Stand dieses Netzes, auf Wunsch neu angelegt. */
        private AvNetzstand AvStandFuer(List<Entity> gruppe, bool anlegen)
        {
            foreach (var e in gruppe)
                if (_avNetzstand.TryGetValue(e, out var vorhanden)) return vorhanden;
            if (!anlegen) return null;
            var stand = new AvNetzstand();
            foreach (var e in gruppe) _avNetzstand[e] = stand;
            return stand;
        }

        private void AvMerkeAngewandt(Versorgungstrasse trasse)
        {
            // Ein Anschluss an eine weitere eigene Zone macht zuvor
            // gescheiterte Stadtziele nicht wieder gueltig. Historie behalten.
            var zurStadt = trasse.Zielkante != Entity.Null
                && EntityManager.Exists(trasse.Zielkante)
                && !EntityManager.HasComponent<Owner>(trasse.Zielkante);
            _avVerbindungen.Add(new AvVerbindung {
                Start = trasse.Start, Ziel = trasse.Ziel, ZielIstStadt = zurStadt });
            Mod.log.Info("PLT-Autoversorgung VERBUNDEN: "
                + $"({trasse.Start.x:F1}/{trasse.Start.z:F1}) nach "
                + $"({trasse.Ziel.x:F1}/{trasse.Ziel.z:F1}), "
                + (zurStadt ? "Ziel ist eine Stadtstrasse - ab hier kommt der Strom herein."
                    : "Ziel ist ein eigenes Netz - dieses Teil braucht noch einen Weg nach draussen."));
        }

        /**
         * EIN FEHLSCHLAG SPERRT DAS ZIEL, NICHT DAS NETZ.
         *
         * Am 2026-09-16 standen 14 Ziele zur Wahl und genau eines wurde
         * versucht. Scheitert es, ist das eine Auskunft ueber DIESEN Weg -
         * nicht darueber, ob das Netz ueberhaupt anschliessbar ist. Also
         * faellt dieses Ziel weg und der naechstbeste kommt dran.
         *
         * Warum eine Obergrenze: der Weg zur Zielliste kostet jedes Mal einen
         * vollen Bauzyklus. Drei Anlaeufe decken den Fall "zufaellig lag da
         * etwas" ab; wer danach immer noch nicht durchkommt, hat ein anderes
         * Problem, und das soll im Log stehen statt in einer Schleife.
         */
        private void AvMerkeFehlschlag(Versorgungstrasse trasse)
        {
            if (trasse.Startnetz == null) return;
            var stand = AvStandFuer(trasse.Startnetz, true);
            stand.Versuche++;
            if (trasse.Zielkante != Entity.Null)
                stand.GesperrteZiele.Add(trasse.Zielkante);
            /*
             * ZURUECK IN DEN OFFENEN TOPF.
             *
             * Beim Planen wurde dieses Netz als erledigt abgezogen. Es hat
             * aber keine Leitung bekommen, also ist es weiter offen - und nur
             * solange die Zahl ueber null steht, plant `AvNaechsteTrasse`
             * ueberhaupt noch eine Runde. Ohne diese Zeile endet der Lauf nach
             * dem ersten Fehlschlag und der zweite Weg kaeme nie dran.
             */
            var weitere = stand.Versuche < AutoVersorgungHoechstversuche;
            if (weitere) _avNochOffeneNetze++;
            Mod.log.Info($"PLT-Autoversorgung: Ziel {trasse.Zielkante} fuer dieses Netz "
                + $"gesperrt, {stand.Versuche}/{AutoVersorgungHoechstversuche} Anlaeufe "
                + "verbraucht. " + (weitere
                    ? "Der naechste nimmt den naechstbesten Weg."
                    : "Damit ist dieses Netz fuer diesen Lauf durch."));
        }

        private HashSet<int> AvZielstrassen(Entity ziel)
            => EntityManager.HasComponent<Owner>(ziel) ? new HashSet<int> { ziel.Index } : null;

        private bool AvStarttor(HashSet<int> strassen, float2 p, bool strom)
        {
            foreach (var e in _avAlleEigenen)
                if (strassen.Contains(e.Index) && AvZieltor(e, strom ? _avStromprefab : _avWasserprefab,
                    p, out _, out _)) return true;
            return false;
        }

        private List<float3> AvKantenpunkte(List<Entity> gruppe, List<(Entity Kante, Bezier4x3 Bogen)> ziele)
        {
            var r = new List<float3>();
            void Merke(Bezier4x3 b, float2 p)
            {
                MathUtils.Distance(b.xz, p, out var t);
                var punkt = MathUtils.Position(b, t);
                foreach (var alt in r) if (math.distance(alt, punkt) < 0.01f) return;
                r.Add(punkt);
            }
            foreach (var e in gruppe)
            {
                if (!KanteNimmtVersorgung(e) || !EntityManager.HasComponent<Curve>(e)) continue;
                var b = EntityManager.GetComponentData<Curve>(e).m_Bezier;
                Merke(b, MathUtils.Position(b, 0.5f).xz);
                foreach (var ziel in ziele)
                {
                    float2 Projektion(Bezier4x3 kurve, float2 p)
                    {
                        MathUtils.Distance(kurve.xz, p, out var t);
                        return MathUtils.Position(kurve, t).xz;
                    }
                    foreach (var p in Versorgungsnetz.Kantenpunkte(t => MathUtils.Position(b, t).xz,
                        p => Projektion(b, p), t => MathUtils.Position(ziel.Bogen, t).xz,
                        p => Projektion(ziel.Bogen, p))) Merke(b, p);
                }
            }
            return r;
        }
    }
}
