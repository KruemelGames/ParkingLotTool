using System.Collections.Generic;
using Game.Common;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * Reparatur verwaister Parkplaetze.
     *
     * WAS VERLOREN IST (gemessen 2026-09-24 an vier Waisen): der Verweis der
     * Flaeche auf ihren Traeger, der Bauzettel, die Teilrelationen an den
     * Aufklebern und die Marken am Wirtschaftsbegleiter. Flaeche, Wege,
     * Flaechen, Traeger und Aufkleber sind alle noch da und haengen an ihren
     * Besitzern.
     *
     * WAS DIE REPARATUR TUT: nur Verweise und Relationen wiederherstellen.
     * Keine Geometrie, kein Loeschen. Die einzige Verbindung, die ueber den
     * Ort hergestellt wird, ist Traeger -> Flaeche, und die nur, wenn ALLE
     * Objekte des Traegers im Umriss GENAU EINER Waise liegen und diese Waise
     * genau einen solchen Traeger hat. Sonst bleibt der Parkplatz verwaist
     * und wird als nicht reparierbar gezeigt - lieber nicht reparieren als
     * falsch verbinden.
     *
     * Der Bauzettel kommt hier NICHT zurueck. Bearbeiten bleibt gesperrt,
     * bis er aus dem Bauprotokoll wiederhergestellt oder neu gebaut ist.
     */
    public sealed partial class ParkingLotWaisenSystem
    {
        /** So weit darf ein Aufkleber ueber den Umriss ragen (Randaufkleber). */
        private const float Randtoleranz = 3f;

        /** Waise -> eindeutig zugeordneter Traeger. */
        private readonly Dictionary<Entity, Entity> _traegerVon =
            new Dictionary<Entity, Entity>();

        /** Traeger -> sein Wirtschaftsbegleiter (ueber Attached). */
        private readonly Dictionary<Entity, Entity> _begleiterVon =
            new Dictionary<Entity, Entity>();

        /** Waise -> warum sie nicht reparierbar ist. */
        private readonly Dictionary<Entity, string> _grund =
            new Dictionary<Entity, string>();

        private bool _autoOffen;

        /** 0 = nicht verwaist, 1 = verwaist und reparierbar, 2 = verwaist, nicht reparierbar. */
        internal int Zustand(Entity lot)
        {
            if (!Waisen.Contains(lot)) return 0;
            return _traegerVon.ContainsKey(lot) ? 1 : 2;
        }

        internal string Grund(Entity lot)
            => _grund.TryGetValue(lot, out var g) ? g : string.Empty;

        /** Die noch offenen Waisen, fuer die Liste. */
        internal IReadOnlyList<Entity> OffeneWaisen => Waisen;

        private void Zuordnen()
        {
            _traegerVon.Clear();
            _begleiterVon.Clear();
            _grund.Clear();
            if (Waisen.Count == 0) return;

            var umrisse = new Dictionary<Entity, float2[]>();
            foreach (var lot in Waisen)
                umrisse[lot] = Umriss(lot);

            // Traeger -> die Waisen, in deren Umriss ALLE seine Objekte liegen.
            var kandidaten = new Dictionary<Entity, List<Entity>>();
            foreach (var traeger in HerrenloseTraeger)
            {
                var positionen = ObjektpositionenVon(traeger);
                if (positionen.Count == 0) continue;
                foreach (var paar in umrisse)
                {
                    var alle = true;
                    foreach (var p in positionen)
                        if (!ImUmriss(p, paar.Value)) { alle = false; break; }
                    if (!alle) continue;
                    if (!kandidaten.TryGetValue(paar.Key, out var liste))
                        kandidaten[paar.Key] = liste = new List<Entity>();
                    liste.Add(traeger);
                }
            }
            var traegerTreffer = new Dictionary<Entity, int>();
            foreach (var liste in kandidaten.Values)
                foreach (var t in liste)
                    traegerTreffer[t] = traegerTreffer.TryGetValue(t, out var n) ? n + 1 : 1;

            foreach (var lot in Waisen)
            {
                if (!kandidaten.TryGetValue(lot, out var liste) || liste.Count == 0)
                {
                    _grund[lot] = "kein Traeger liegt vollstaendig in diesem Umriss";
                    continue;
                }
                if (liste.Count > 1)
                {
                    _grund[lot] = liste.Count + " Traeger passen - nicht eindeutig";
                    continue;
                }
                if (traegerTreffer[liste[0]] > 1)
                {
                    _grund[lot] = "der Traeger passt auch zu einem anderen Parkplatz";
                    continue;
                }
                _traegerVon[lot] = liste[0];
            }

            SucheBegleiter();

            foreach (var lot in Waisen)
                Mod.log.Info("PLT-Waisen: Zuordnung " + lot.Index + ": "
                    + (_traegerVon.TryGetValue(lot, out var t)
                        ? "Traeger " + t.Index + ", Begleiter "
                          + (_begleiterVon.TryGetValue(t, out var b)
                              ? b.Index.ToString() : "keiner")
                          + " - reparierbar"
                        : "NICHT reparierbar (" + Grund(lot) + ")") + ".");
        }

        /** Begleiter ohne Relation, deren Attached auf einen herrenlosen Traeger zeigt. */
        private void SucheBegleiter()
        {
            var gesucht = new HashSet<Entity>(HerrenloseTraeger);
            var query = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Buildings.Building>(),
                    ComponentType.ReadOnly<Game.Objects.Attached>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<ParkingLotPartRelation>(),
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Game.Tools.Temp>(),
                },
            });
            using var gebaeude = query.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < gebaeude.Length; i++)
            {
                var g = gebaeude[i];
                var eltern = EntityManager.GetComponentData<
                    Game.Objects.Attached>(g).m_Parent;
                if (!gesucht.Contains(eltern)) continue;
                var prefab = EntityManager.GetComponentData<PrefabRef>(g).m_Prefab;
                if (PrefabName(prefab) != ParkingLotBuildingEconomySystem
                        .CompanionPrefabName) continue;
                if (_begleiterVon.ContainsKey(eltern))
                {
                    Mod.log.Warn("PLT-Waisen: Traeger " + eltern.Index
                        + " hat mehr als einen Begleiter; nur der erste wird "
                        + "wieder angehaengt.");
                    continue;
                }
                _begleiterVon[eltern] = g;
            }
        }

        /**
         * Verbindet eine Waise wieder. Liefert false, wenn sie nicht (mehr)
         * reparierbar ist.
         */
        internal bool Reparieren(Entity lot)
        {
            if (!_traegerVon.TryGetValue(lot, out var traeger)) return false;
            if (!EntityManager.Exists(lot) || !EntityManager.Exists(traeger)
                || EntityManager.HasComponent<Deleted>(lot)
                || EntityManager.HasComponent<ParkingLotCarrierReference>(lot))
            {
                Vergessen(lot, traeger);
                return false;
            }
            var uhr = System.Diagnostics.Stopwatch.StartNew();

            // Nur Objekte, deren Owner der Traeger ist - genau die, die beim
            // Bau eine Relation bekommen haben (HefteObjekteAnTraeger).
            var objekte = new List<Entity>();
            var puffer = EntityManager.GetBuffer<Game.Objects.SubObject>(traeger, true);
            for (var i = 0; i < puffer.Length; i++)
            {
                var o = puffer[i].m_SubObject;
                if (!EntityManager.Exists(o)
                    || !EntityManager.HasComponent<Owner>(o)
                    || EntityManager.GetComponentData<Owner>(o).m_Owner != traeger)
                    continue;
                objekte.Add(o);
            }
            // Eine Strukturaenderung fuer alle statt tausend einzelne.
            using (var feld = new NativeArray<Entity>(objekte.ToArray(), Allocator.Temp))
                EntityManager.AddComponent<ParkingLotPartRelation>(feld);
            var relation = new ParkingLotPartRelation { Lot = lot, Carrier = traeger };
            foreach (var o in objekte)
                EntityManager.SetComponentData(o, relation);

            var begleiter = "keiner";
            if (_begleiterVon.TryGetValue(traeger, out var b) && EntityManager.Exists(b))
            {
                if (!EntityManager.HasComponent<ParkingLotPartRelation>(b))
                    EntityManager.AddComponentData(b, relation);
                if (!EntityManager.HasComponent<ParkingLotBuildingEconomyEnabled>(b))
                    EntityManager.AddComponent<ParkingLotBuildingEconomyEnabled>(b);
                begleiter = b.Index.ToString();
            }

            // Zuletzt der Verweis: ab hier sehen Liste, Wirtschaft und
            // Aufraeumer den Parkplatz. Vorher waere er halb verbunden.
            EntityManager.AddComponentData(lot,
                new ParkingLotCarrierReference { Carrier = traeger });

            Mod.log.Info("PLT-Waisen: Lot " + lot.Index + " repariert: Traeger "
                + traeger.Index + ", " + objekte.Count + " Relationen, Begleiter "
                + begleiter + ", " + uhr.ElapsedMilliseconds + " ms. Bauzettel "
                + "fehlt weiterhin.");
            Vergessen(lot, traeger);
            return true;
        }

        internal int ReparierenAlle()
        {
            var n = 0;
            foreach (var lot in new List<Entity>(Waisen))
                if (Reparieren(lot)) n++;
            return n;
        }

        private void Vergessen(Entity lot, Entity traeger)
        {
            Waisen.Remove(lot);
            HerrenloseTraeger.Remove(traeger);
            _traegerVon.Remove(lot);
            _grund.Remove(lot);
            Bauplaene.Remove(lot);
        }

        /**
         * Automatische Reparatur, EIN Parkplatz je Durchlauf - damit auch
         * eine grosse Waise das Spiel nicht spuerbar anhaelt.
         */
        private void AutomatischWeiter()
        {
            if (!_autoOffen) return;
            if (Mod.Optionen == null || !Mod.Optionen.WaisenAutomatischReparieren)
            {
                _autoOffen = false;
                return;
            }
            foreach (var lot in Waisen)
            {
                if (!_traegerVon.ContainsKey(lot)) continue;
                Reparieren(lot);
                return;
            }
            _autoOffen = false;
        }

        /** Der Schalter wurde eingeschaltet - offene Waisen jetzt angehen. */
        internal void StarteAutomatik() => _autoOffen = Waisen.Count > 0;

        private float2[] Umriss(Entity lot)
        {
            if (!EntityManager.HasBuffer<Game.Areas.Node>(lot)) return new float2[0];
            var knoten = EntityManager.GetBuffer<Game.Areas.Node>(lot, true);
            var umriss = new float2[knoten.Length];
            for (var i = 0; i < knoten.Length; i++) umriss[i] = knoten[i].m_Position.xz;
            return umriss;
        }

        private List<float2> ObjektpositionenVon(Entity traeger)
        {
            var positionen = new List<float2>();
            if (!EntityManager.HasBuffer<Game.Objects.SubObject>(traeger)) return positionen;
            var puffer = EntityManager.GetBuffer<Game.Objects.SubObject>(traeger, true);
            for (var i = 0; i < puffer.Length; i++)
            {
                var o = puffer[i].m_SubObject;
                if (!EntityManager.Exists(o)
                    || !EntityManager.HasComponent<Game.Objects.Transform>(o)
                    || !EntityManager.HasComponent<Owner>(o)
                    || EntityManager.GetComponentData<Owner>(o).m_Owner != traeger)
                    continue;
                positionen.Add(EntityManager.GetComponentData<
                    Game.Objects.Transform>(o).m_Position.xz);
            }
            return positionen;
        }

        private static bool ImUmriss(float2 p, float2[] umriss)
        {
            if (umriss.Length < 3) return false;
            var innen = false;
            for (int i = 0, j = umriss.Length - 1; i < umriss.Length; j = i++)
            {
                var a = umriss[i];
                var b = umriss[j];
                if ((a.y > p.y) != (b.y > p.y)
                    && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    innen = !innen;
            }
            if (innen) return true;
            for (int i = 0, j = umriss.Length - 1; i < umriss.Length; j = i++)
            {
                var a = umriss[j];
                var d = umriss[i] - a;
                var l2 = math.lengthsq(d);
                var t = l2 > 0f ? math.saturate(math.dot(p - a, d) / l2) : 0f;
                if (math.distance(p, a + t * d) <= Randtoleranz) return true;
            }
            return false;
        }

        private string PrefabName(Entity prefab)
        {
            try
            {
                if (_prefabSystem.TryGetPrefab<PrefabBase>(prefab, out var p) && p != null)
                    return p.name;
                return _prefabSystem.GetPrefabName(prefab);
            }
            catch { return null; }
        }
    }
}
