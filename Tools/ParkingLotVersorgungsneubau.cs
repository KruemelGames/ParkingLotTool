using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private Entity _avAbrissTraeger;
        private readonly List<Entity> _avAbrissKanten = new List<Entity>();
        /**
         * Die Endknoten der alten Leitungskanten.
         *
         * Gemerkt wird, SOLANGE DIE KANTEN NOCH DA SIND - danach kommt man an
         * ihre Enden nicht mehr heran. Siehe AvAbrissFertig, dort steht der
         * Grund.
         */
        private readonly List<Entity> _avAbrissKnoten = new List<Entity>();
        /** Zeitpunkt, an dem die alten KANTEN fort waren. -1 = noch nicht. */
        private double _avKantenFreiMs = -1;
        private System.Diagnostics.Stopwatch _avGesamtzeit;

        private bool IstReinerAutomatischerVersorgungsknoten(Entity knoten, Entity lot)
        {
            if (!EntityManager.HasBuffer<ConnectedEdge>(knoten)) return false;
            var eigen = false;
            foreach (var verbunden in EntityManager.GetBuffer<ConnectedEdge>(knoten, true))
            {
                var e = verbunden.m_Edge;
                if (!VersorgungsentityLebt(e)) continue;
                if (!EntityManager.HasComponent<Edge>(e)) return false;
                var edge = EntityManager.GetComponentData<Edge>(e);
                // Eine seitliche Strassenreferenz ist keine manuell gebaute Leitung.
                if (edge.m_Start != knoten && edge.m_End != knoten) continue;
                if (!EntityManager.HasComponent<ParkingLotVersorgungsleitung>(e)
                    || EntityManager.GetComponentData<ParkingLotVersorgungsleitung>(e).Lot != lot)
                    return false;
                eigen = true;
            }
            return eigen;
        }

        private void AvMerkeAbriss(Entity lot, Entity traeger)
        {
            _avAbrissTraeger = traeger;
            _avAbrissKanten.Clear();
            _avAbrissKnoten.Clear();
            _avKantenFreiMs = -1;
            if (lot == Entity.Null) return;
            var query = GetEntityQuery(ComponentType.ReadOnly<ParkingLotVersorgungsleitung>(),
                ComponentType.ReadOnly<Edge>(), ComponentType.Exclude<Temp>());
            using var kanten = query.ToEntityArray(Allocator.Temp);
            foreach (var e in kanten)
            {
                var tag = EntityManager.GetComponentData<ParkingLotVersorgungsleitung>(e);
                if (tag.Lot != lot
                    && (traeger == Entity.Null || tag.Carrier != traeger))
                    continue;
                _avAbrissKanten.Add(e);
                /*
                 * DIE ENDKNOTEN JETZT MERKEN, NICHT SPAETER.
                 *
                 * Gleich sind die Kanten fort, und mit ihnen der einzige Weg
                 * zu ihren Enden. Wer die Knoten erst nach dem Abriss sucht,
                 * sucht ins Leere.
                 */
                if (!EntityManager.HasComponent<Edge>(e)) continue;
                var kante = EntityManager.GetComponentData<Edge>(e);
                if (kante.m_Start != Entity.Null
                    && !_avAbrissKnoten.Contains(kante.m_Start))
                    _avAbrissKnoten.Add(kante.m_Start);
                if (kante.m_End != Entity.Null
                    && !_avAbrissKnoten.Contains(kante.m_End))
                    _avAbrissKnoten.Add(kante.m_End);
                // Auch die Seitenanschluesse - der Abriss merkt sie sich
                // ebenso (`MerkeLeitungsknoten`) und raeumt sie mit ab.
                if (EntityManager.HasBuffer<ConnectedNode>(e))
                    foreach (var n in EntityManager.GetBuffer<ConnectedNode>(e, true))
                        if (n.m_Node != Entity.Null && !_avAbrissKnoten.Contains(n.m_Node))
                            _avAbrissKnoten.Add(n.m_Node);
            }
            Mod.log.Info($"PLT-Autoversorgung NEUBAU: {_avAbrissKanten.Count} alte eigene Leitungskanten "
                + $"mit {_avAbrissKnoten.Count} Endknoten bleiben beim Abriss von Lot {lot}; "
                + $"Neubau wartet auf deren Entfernung und Traeger {traeger}.");
        }

        /**
         * WAS GENAU HAENGT NOCH?
         *
         * Ohne diese Auskunft meldet der Zeitablauf nur "alter Leitungsabriss
         * noch offen" - und die naechste Fehlersuche faengt bei null an. Der
         * entscheidende Unterschied steht in der Klammer:
         *
         *   "Deleted, wartet auf Zerstoerung" - alles in Ordnung, CS2 raeumt
         *     noch ab; die Frist war schlicht zu kurz.
         *   "NICHT als geloescht markiert"    - der Aufraeumer hat diese Kante
         *     gar nicht erwischt. Dann liegt es an der Knotenregel in
         *     `ParkingLotCleanupVersorgung`, nicht am Warten.
         *
         * Zwei voellig verschiedene Fehler, die ohne diese Zeile gleich
         * aussehen. Genau solche Verwechslungen haben am 2026-09-05 einen
         * halben Tag gekostet.
         */
        private string AvAbrissRest()
        {
            var teile = new List<string>();
            if (_avAbrissTraeger != Entity.Null
                && EntityManager.Exists(_avAbrissTraeger))
                teile.Add($"alter Traeger #{_avAbrissTraeger.Index}");

            var offen = 0;
            var beispiele = new List<string>();
            foreach (var e in _avAbrissKanten)
            {
                if (!EntityManager.Exists(e)) continue;
                offen++;
                if (beispiele.Count >= 4) continue;
                beispiele.Add($"#{e.Index} "
                    + (EntityManager.HasComponent<Deleted>(e)
                        ? "(Deleted, wartet auf Zerstoerung)"
                        : "(NICHT als geloescht markiert)"));
            }
            if (offen > 0)
                teile.Add($"{offen} alte Leitungskante(n): "
                    + string.Join(", ", beispiele)
                    + (offen > beispiele.Count ? ", ..." : ""));

            // Ohne diese Zeile sieht "Kanten weg, trotzdem kein Neubau" wie
            // ein Haenger aus, obwohl noch ein Knoten abgeraeumt wird.
            var knoten = AvSterbendeKnoten();
            if (knoten > 0)
                teile.Add($"{knoten} sterbende(r) Endknoten der alten Leitung");

            return teile.Count == 0 ? "nichts mehr offen"
                : string.Join("; ", teile);
        }

        /**
         * DIE KANTEN SIND NICHT DAS LETZTE, WAS VERSCHWINDET.
         *
         * Bis zum 2026-09-14 hat diese Pruefung nur auf die alten KANTEN und
         * den alten Traeger gewartet. Von den KNOTEN wusste sie nichts - und
         * die sind beim Edit genau das Problem.
         *
         * Warum: `ParkingLotCleanupVersorgung` markiert mit voller Absicht nur
         * Kanten. Knoten selbst zu markieren war der Absturz vom 2026-09-10.
         * CS2 raeumt die verwaisten Knoten danach selbst ab - aber ein paar
         * Frames spaeter. In diesem Fenster ist die Kante schon fort und der
         * Knoten steht noch.
         *
         * Und dann greift CS2 danach. `GenerateEdgesSystem.TryGetNode` sucht
         * den Knoten eines Kursendes UEBER DIE POSITION, sobald das Kursende
         * keine Entity nennt - und unsere nennen bewusst keine (`AvCoursePos`
         * setzt `m_Entity = Entity.Null`). Die neue Leitung beginnt an
         * derselben Stelle wie die alte: gleicher Parkplatz, gleiche Strasse,
         * gleiche 10 m Tiefe. CS2 findet also genau den Knoten, der gerade
         * abgeraeumt wird.
         *
         * NACHTRAG, UND DER GEHOERT HIERHER: DAS WAR NICHT DER ABSTURZ.
         *
         * Diese Schranke wurde am 2026-09-14 gegen den Absturz beim Edit
         * gebaut. Sie hat ihn NICHT behoben. Die eingebaute Messung sagte im
         * naechsten Lauf woertlich: *"4 gemerkte Endknoten, davon musste auf 0
         * gewartet werden; 0,0 ms"*. Es stand kein sterbender Knoten im Weg.
         *
         * Die wirkliche Ursache lag woanders und steht in
         * `ParkingLotCleanupVersorgung`: wir haben die alten Leitungskanten
         * aus `Modification3` heraus geloescht, wo `Game.Net.ReferencesSystem`
         * (`Modification2B`) sie nie zu sehen bekam.
         *
         * Warum die Schranke trotzdem bleibt: sie kostet nichts, sie ist
         * sachlich richtig, und ihre Messung steht im Log. Wer sie spaeter
         * anfasst, soll aber wissen, dass sie noch nie etwas aufgehalten hat.
         *
         * WAS ALS GEFAEHRLICH GILT: ein gemerkter Knoten, der noch existiert
         * und entweder `Deleted` traegt oder keine Kante mehr hat. Der zweite
         * Fall ist wichtig - zwischen "Kante fort" und "Knoten als geloescht
         * markiert" liegt ein Fenster, in dem der Knoten voellig unauffaellig
         * aussieht und trotzdem sterben wird.
         *
         * BERICHTIGT 2026-09-25: "ein Knoten mit Kanten ist der Knoten der
         * Stadtstrasse" war falsch. Unser Anschlussknoten sitzt SEITLICH auf
         * dem Stadtrohr und hat dessen Kante im Puffer, ohne ihr Ende zu
         * sein - der Abriss loescht ihn trotzdem. Seitdem gilt hier dieselbe
         * Regel wie dort: `ParkingLotLeitungsabrissSystem.TraegtNichtsMehr`.
         */
        private bool AvAbrissFertig()
        {
            if (_avAbrissTraeger != Entity.Null && EntityManager.Exists(_avAbrissTraeger)) return false;
            foreach (var e in _avAbrissKanten)
                if (EntityManager.Exists(e)) return false;

            var gabEtwas = _avAbrissTraeger != Entity.Null
                || _avAbrissKanten.Count > 0;
            if (gabEtwas && _avKantenFreiMs < 0)
                _avKantenFreiMs = _avGesamtzeit?.Elapsed.TotalMilliseconds ?? 0;

            var sterbende = AvSterbendeKnoten();
            if (sterbende > 0) return false;

            if (gabEtwas)
            {
                var jetzt = _avGesamtzeit?.Elapsed.TotalMilliseconds ?? 0;
                var nachKanten = _avKantenFreiMs < 0 ? 0 : jetzt - _avKantenFreiMs;
                Mod.log.Info($"PLT-Autoversorgung ZEIT: alter Abriss nach "
                    + $"{jetzt:F1} ms abgeschlossen; frischer Anschluss freigegeben. "
                    + $"KNOTENWARTEN: {_avAbrissKnoten.Count} gemerkte Endknoten, "
                    + $"davon musste auf {AvKnotenGewartet} gewartet werden; "
                    + $"{nachKanten:F1} ms davon NACH dem Verschwinden der Kanten. "
                    + "Steht hier 0 und 0,0 ms, war kein sterbender Knoten im Weg.");
            }
            _avAbrissTraeger = Entity.Null;
            _avAbrissKanten.Clear();
            _avAbrissKnoten.Clear();
            _avKantenFreiMs = -1;
            AvKnotenGewartet = 0;
            return true;
        }

        /** Hoechststand der gleichzeitig sterbenden Knoten, nur fuer die Messung. */
        private int AvKnotenGewartet;

        /**
         * Zaehlt die gemerkten Knoten, die noch sterben werden.
         *
         * `Deleted` ist der offensichtliche Fall. Sonst zaehlt genau, was
         * der Abriss als naechstes loescht - seine eigene Regel
         * `TraegtNichtsMehr` (sie deckt "gar keine Kante" mit ab). Was er nie
         * anfasst (`Temp`), zaehlt nicht: darauf zu warten hiesse, bis zur
         * Frist zu haengen und den Neubau ausfallen zu lassen (Codex,
         * 2026-09-25).
         */
        private int AvSterbendeKnoten()
        {
            var zahl = 0;
            foreach (var k in _avAbrissKnoten)
            {
                if (!EntityManager.Exists(k)) continue;
                if (EntityManager.HasComponent<Deleted>(k)) { zahl++; continue; }
                if (EntityManager.HasComponent<Temp>(k)) continue;
                // Dieselbe Regel wie der Abriss selbst - sonst gibt diese
                // Sperre Knoten frei, die der Abriss gleich darauf loescht.
                if (ParkingLotLeitungsabrissSystem.TraegtNichtsMehr(EntityManager, k))
                    zahl++;
            }
            if (zahl > AvKnotenGewartet) AvKnotenGewartet = zahl;
            return zahl;
        }
    }
}
