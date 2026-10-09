using System.Collections.Generic;
using System.Linq;
using Game;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * FEHLENDE FLAECHEN IM BESTAND ERSETZEN (Sync-Schritt 13, 2026-10-08).
     *
     * Bis zur Ausgangsgarantie (`ParkingGeometry.BaubareStuecke`) gingen
     * Flaechen an CS2, die das Spiel verwirft: CS2 rueckt jede Flaeche 0,1 m
     * ein, und ein Detail unter etwa 0,2 m - schon eine einzelne Kante
     * unter 10 cm - laesst die GANZE Flaeche fallen. Die Entity bleibt,
     * aber ohne Dreiecke: im Spiel nackter Boden, wo Gras oder Belag sein
     * sollte. Nutzer am 2026-10-08: "Flaechensync und evtl repair wenn User
     * eine Flaeche haben die sozusagen fehlt aber da sein sollte."
     *
     * MINIMAL: nur die verworfene Flaeche selbst wird ersetzt - durch die
     * Stuecke, die `BaubareStuecke` aus IHREN EIGENEN Ecken baut. Kein
     * Neubau, keine Neuberechnung des Parkplatzes, keine neuen Punkte; jede
     * Ecke eines Stuecks bekommt den gespeicherten Knoten (mit Hoehe) der
     * alten Flaeche. Prefab und Besitzer bleiben.
     *
     * DER WEG IST DERSELBE wie `ParkingLotApronPrefabSystem.LegeNeuAn`, der
     * seit 1.0.4 tote Flaechen nach dem Laden ersetzt: eine feste Definition
     * mit Knoten und Besitzer, die alte Flaeche bekommt `Deleted` - so wie
     * Vanilla ein Flaechenprefab tauscht (nie umhaengen, siehe Absturz #6).
     * Dieselbe Phase (PrefabUpdate, vor Modification1), damit
     * GenerateAreasSystem die Definitionen im selben Bild liest.
     *
     * ERFOLG erst nach der Nachpruefung: nach 30 Bildern muessen die alten
     * Flaechen weg sein und jede neue Flaeche von CS2 Dreiecke bekommen
     * haben - das ist CS2s eigene Antwort, nicht unsere Vorhersage.
     */
    public sealed partial class ParkingLotFlaechenSyncSystem : GameSystemBase
    {
        private const int Pruefabstand = 30;
        private const int Hoechstwartezeit = 600;

        private sealed class Lauf
        {
            internal Entity Lot;
            internal readonly List<Entity> Alt = new();
            internal int Neu;
            internal int Seit;
            internal double Verloren;
        }

        private readonly List<Entity> _warteschlange = new();
        private Lauf _lauf;

        internal int Offen => _warteschlange.Count + (_lauf != null ? 1 : 0);

        internal void Einreihen(Entity lot)
        {
            if (_warteschlange.Contains(lot) || _lauf?.Lot == lot) return;
            _warteschlange.Add(lot);
            Mod.log.Info($"PLT-Flaechensync: Lot {lot.Index} eingereiht; offen {Offen}.");
        }

        /**
         * Hat dieser Parkplatz eine Flaeche, die CS2 verwirft? Gerechnet mit
         * dem bitgenauen Nachbau (`Cs2Triangulierung`) auf den gespeicherten
         * Knoten - unabhaengig davon, ob CS2s Dreieckspuffer nach dem Laden
         * schon gefuellt ist.
         */
        internal bool Braucht(Entity lot) => Verworfene(lot).Count > 0;

        private List<Entity> Verworfene(Entity lot)
        {
            var treffer = new List<Entity>();
            if (lot == Entity.Null || !EntityManager.Exists(lot)
                || !EntityManager.HasBuffer<Game.Areas.SubArea>(lot)) return treffer;
            var unter = EntityManager.GetBuffer<Game.Areas.SubArea>(lot, true);
            for (var i = 0; i < unter.Length; i++)
            {
                var flaeche = unter[i].m_Area;
                if (!IstEigeneFlaeche(flaeche)) continue;
                var ring = Ring(flaeche);
                if (ring != null && Cs2Triangulierung.Dreiecke(ring) == 0) treffer.Add(flaeche);
            }
            return treffer;
        }

        private bool IstEigeneFlaeche(Entity e)
            => e != Entity.Null && EntityManager.Exists(e)
               && EntityManager.HasComponent<Game.Areas.Surface>(e)
               && EntityManager.HasComponent<PrefabRef>(e)
               && EntityManager.HasBuffer<Node>(e)
               && !EntityManager.HasComponent<Deleted>(e)
               && !EntityManager.HasComponent<Temp>(e);

        /** Der Ring in xz, gegen den Uhrzeigersinn - wie der Bau ihn schickt. */
        private float2[] Ring(Entity flaeche)
        {
            var knoten = EntityManager.GetBuffer<Node>(flaeche, true);
            if (knoten.Length < 3) return null;
            var ring = new float2[knoten.Length];
            for (var i = 0; i < knoten.Length; i++) ring[i] = knoten[i].m_Position.xz;
            var doppelt = 0.0;
            for (var i = 0; i < ring.Length; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Length];
                doppelt += (double)a.x * b.y - (double)b.x * a.y;
            }
            if (doppelt < 0) System.Array.Reverse(ring);
            return ring;
        }

        [Preserve]
        protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            // Wie der Bestandstausch: Entities der alten Welt nicht mitnehmen.
            _warteschlange.Clear();
            _lauf = null;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            var spiel = GameManager.instance;
            if (spiel == null || spiel.isGameLoading || !spiel.gameMode.IsGame()) return;
            var bild = UnityEngine.Time.frameCount;

            if (_lauf != null)
            {
                if (bild - _lauf.Seit < Pruefabstand) return;
                Pruefe(bild);
                return;
            }
            if (_warteschlange.Count == 0) return;
            // Ein laufender Entwurf des Werkzeugs ersetzt die Flaechen
            // ohnehin; nicht mitten hinein tauschen.
            if (World.GetOrCreateSystemManaged<ParkingLotToolSystem>().ArbeitetGerade) return;
            var lot = _warteschlange[0];
            _warteschlange.RemoveAt(0);
            Starte(lot, bild);
        }

        private void Starte(Entity lot, int bild)
        {
            var alt = Verworfene(lot);
            _lauf = new Lauf { Lot = lot, Seit = bild };
            foreach (var flaeche in alt)
            {
                var ring = Ring(flaeche);
                var unbaubar = new List<float2[]>();
                var stuecke = ParkingGeometry.BaubareStuecke(ring, unbaubar);
                foreach (var r in unbaubar) _lauf.Verloren += math.abs(Flaeche(r));
                _lauf.Neu += LegeAn(flaeche, stuecke);
                // Auch ohne Stuecke weg: die Flaeche ist ohnehin unsichtbar.
                EntityManager.AddComponent<Deleted>(flaeche);
                _lauf.Alt.Add(flaeche);
            }
            Mod.log.Info($"PLT-Flaechensync: Lot {lot.Index}: {alt.Count} verworfene Flaeche(n) "
                + $"durch {_lauf.Neu} baubare ersetzt (nicht baubar, schmaler als CS2 bauen kann: "
                + $"{_lauf.Verloren:F2} m2).");
            if (alt.Count == 0) Beende(true, "nichts zu tun");
        }

        /** Eine feste Definition je Stueck - wie `LegeNeuAn`. */
        private int LegeAn(Entity alt, List<float2[]> stuecke)
        {
            if (stuecke.Count == 0) return 0;
            var prefab = EntityManager.GetComponentData<PrefabRef>(alt).m_Prefab;
            var besitzer = EntityManager.HasComponent<Owner>(alt)
                ? EntityManager.GetComponentData<Owner>(alt).m_Owner : Entity.Null;
            var knoten = EntityManager.GetBuffer<Node>(alt, true).ToNativeArray(Allocator.Temp);
            try
            {
                var nachLage = new Dictionary<float2, Node>();
                foreach (var k in knoten) nachLage[k.m_Position.xz] = k;
                var angelegt = 0;
                foreach (var stueck in stuecke)
                {
                    // Nur vorhandene Ecken - fehlt eine, stimmt etwas nicht;
                    // dann lieber dieses Stueck nicht als eins in falscher Hoehe.
                    if (!stueck.All(nachLage.ContainsKey)) continue;
                    var d = EntityManager.CreateEntity();
                    EntityManager.AddComponentData(d, new CreationDefinition
                    {
                        m_Prefab = prefab,
                        m_Owner = besitzer,
                        m_Flags = CreationFlags.Permanent,
                    });
                    EntityManager.AddComponent<Updated>(d);
                    var puffer = EntityManager.AddBuffer<Node>(d);
                    puffer.ResizeUninitialized(stueck.Length + 1);
                    for (var i = 0; i < stueck.Length; i++) puffer[i] = nachLage[stueck[i]];
                    // Ohne Original macht erst der wiederholte erste Knoten die
                    // Flaeche "Complete" (siehe LegeNeuAn).
                    puffer[stueck.Length] = puffer[0];
                    ParkingLotToolSystem.NurDiesesBild(EntityManager, d);
                    angelegt++;
                }
                return angelegt;
            }
            finally { knoten.Dispose(); }
        }

        private void Pruefe(int bild)
        {
            var lot = _lauf.Lot;
            var altWeg = _lauf.Alt.All(a => !EntityManager.Exists(a) || EntityManager.HasComponent<Deleted>(a));
            var nochVerworfen = Verworfene(lot).Count;
            // CS2s eigene Antwort: hat jede Flaeche des Parkplatzes Dreiecke?
            var ohneDreiecke = 0;
            if (EntityManager.Exists(lot) && EntityManager.HasBuffer<Game.Areas.SubArea>(lot))
            {
                var unter = EntityManager.GetBuffer<Game.Areas.SubArea>(lot, true);
                for (var i = 0; i < unter.Length; i++)
                {
                    var f = unter[i].m_Area;
                    if (!IstEigeneFlaeche(f) || !EntityManager.HasBuffer<Triangle>(f)) continue;
                    if (EntityManager.GetBuffer<Triangle>(f, true).Length == 0) ohneDreiecke++;
                }
            }
            if (altWeg && nochVerworfen == 0 && ohneDreiecke == 0)
            {
                Beende(true, $"{_lauf.Alt.Count} ersetzt durch {_lauf.Neu}, alle Flaechen mit Dreiecken");
                return;
            }
            if (bild - _lauf.Seit < Hoechstwartezeit) return;
            Beende(false, $"alte weg {altWeg}, noch verworfen {nochVerworfen}, ohne Dreiecke {ohneDreiecke}");
        }

        private void Beende(bool ok, string text)
        {
            var lot = _lauf?.Lot ?? Entity.Null;
            _lauf = null;
            Mod.log.Info($"PLT-Flaechensync: Lot {lot.Index}: {(ok ? "fertig" : "nicht abgeschlossen")} - {text}.");
            World.GetOrCreateSystemManaged<ParkingLotSyncSystem>().MeldeHintergrundEnde(lot, lot, ok);
        }

        private static double Flaeche(float2[] ring)
        {
            var summe = 0.0;
            for (var i = 0; i < ring.Length; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Length];
                summe += (double)a.x * b.y - (double)b.x * a.y;
            }
            return summe / 2;
        }
    }
}
