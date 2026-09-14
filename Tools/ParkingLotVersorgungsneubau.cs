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
            if (lot == Entity.Null) return;
            var query = GetEntityQuery(ComponentType.ReadOnly<ParkingLotVersorgungsleitung>(),
                ComponentType.ReadOnly<Edge>(), ComponentType.Exclude<Temp>());
            using var kanten = query.ToEntityArray(Allocator.Temp);
            foreach (var e in kanten)
            {
                var tag = EntityManager.GetComponentData<ParkingLotVersorgungsleitung>(e);
                if (tag.Lot == lot || (traeger != Entity.Null && tag.Carrier == traeger))
                    _avAbrissKanten.Add(e);
            }
            Mod.log.Info($"PLT-Autoversorgung NEUBAU: {_avAbrissKanten.Count} alte eigene Leitungskanten "
                + $"bleiben beim Abriss von Lot {lot}; Neubau wartet auf deren Entfernung und Traeger {traeger}.");
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

            return teile.Count == 0 ? "nichts mehr offen"
                : string.Join("; ", teile);
        }

        private bool AvAbrissFertig()
        {
            if (_avAbrissTraeger != Entity.Null && EntityManager.Exists(_avAbrissTraeger)) return false;
            foreach (var e in _avAbrissKanten)
                if (EntityManager.Exists(e)) return false;
            if (_avAbrissTraeger != Entity.Null || _avAbrissKanten.Count > 0)
                Mod.log.Info($"PLT-Autoversorgung ZEIT: alter Abriss nach "
                    + $"{_avGesamtzeit?.Elapsed.TotalMilliseconds:F1} ms abgeschlossen; frischer Anschluss freigegeben.");
            _avAbrissTraeger = Entity.Null;
            _avAbrissKanten.Clear();
            return true;
        }
    }
}
