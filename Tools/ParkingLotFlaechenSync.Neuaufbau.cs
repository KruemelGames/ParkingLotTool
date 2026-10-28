using System.Collections.Generic;
using System.Linq;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * DIE FLAECHEN EINES ALTEN PARKPLATZES NEU AUFBAUEN (Sync-Schritt 16).
     *
     * Ein alter Parkplatz soll danach GENAU die Flaechen eines Neubaus haben:
     * Alley-Belag, Fahrbahn mit eingesetzter Vorflaeche, frisch gemessene
     * Vorflaeche bis zur Strasse, nichts unter Bruecken. Zwei Irrwege davor:
     * Zusatzflaechen ueber den Bestand kleben (Nutzer: "einfach ne neue Flaeche
     * hinzugekommen und das ist falsch"), und der volle Hintergrund-Neubau
     * (scheiterte an den Wegen, Lot 499405 gesperrt).
     *
     * Hier nur Flaechen, nach dem Muster von Schritt 13: der isolierte
     * Bauarbeiter plant (ParkingLotBauarbeiter.Flaechen.cs), dieses System legt
     * feste Definitionen am Besitzer an - ohne Werkzeug, ohne Panel. ERST WENN
     * CS2 alle neuen Flaechen angenommen hat (Dreiecke), fallen die alten weg.
     * Sonst werden die neuen wieder entfernt und der Parkplatz bleibt, wie er
     * war. Wege, Objekte und Bauzettel bleiben unberuehrt.
     */
    public sealed partial class ParkingLotFlaechenSyncSystem
    {
        private ParkingLotToolSystem _flaechenplaner;

        private void StarteNeuaufbau(Entity lot)
        {
            if (!LaufGueltig()) return;
            var traeger = EntityManager.Exists(lot) && EntityManager.HasComponent<ParkingLotCarrierReference>(lot)
                ? EntityManager.GetComponentData<ParkingLotCarrierReference>(lot).Carrier : Entity.Null;
            if (!ParkingLotSyncGueltigkeit.Bestand(EntityManager, lot, traeger))
            { Beende(false, "Bestand vor dem Start verschwunden"); return; }
            _lauf.Traeger = traeger;
            var unter = EntityManager.GetBuffer<Game.Areas.SubArea>(lot, true);
            for (var i = 0; i < unter.Length; i++)
            {
                var f = unter[i].m_Area;
                _lauf.Vorher.Add(f);
                if (!IstEigeneFlaeche(f)) continue;
                _lauf.Alt.Add(f);
                if (_lauf.Besitzer == Entity.Null && EntityManager.HasComponent<Owner>(f))
                    _lauf.Besitzer = EntityManager.GetComponentData<Owner>(f).m_Owner;
            }
            if (_lauf.Besitzer == Entity.Null) _lauf.Besitzer = lot;
            _flaechenplaner ??= ParkingLotToolSystem.Bauarbeiter(World);
            _lauf.Rechnung = _flaechenplaner.BereiteFlaechen(lot);
            Mod.log.Info($"PLT-Flaechensync: Lot {lot.Index}: Flaechen werden neu geplant "
                + $"({_lauf.Alt.Count} alte Flaeche(n)).");
        }

        private void PflegeNeuaufbau(int bild)
        {
            var lot = _lauf.Lot;
            if (!ParkingLotSyncGueltigkeit.Bestand(EntityManager, lot, _lauf.Traeger))
            { Beende(false, "Bestand verschwunden"); return; }
            if (!_lauf.Angelegt) { LegeNeuaufbauAn(bild); return; }
            if (bild - _lauf.Seit < Pruefabstand) return;
            // CS2s eigene Antwort: sind alle neuen Flaechen da, mit Dreiecken?
            var neue = new List<Entity>();
            var ohneDreiecke = 0;
            var unter = EntityManager.GetBuffer<Game.Areas.SubArea>(lot, true);
            for (var i = 0; i < unter.Length; i++)
            {
                var f = unter[i].m_Area;
                if (_lauf.Vorher.Contains(f) || !IstEigeneFlaeche(f)) continue;
                neue.Add(f);
                if (!EntityManager.HasBuffer<Triangle>(f) || EntityManager.GetBuffer<Triangle>(f, true).Length == 0)
                    ohneDreiecke++;
            }
            if (neue.Count == _lauf.Neu && ohneDreiecke == 0)
            {
                // Jetzt erst: die alten weg.
                foreach (var alt in _lauf.Alt)
                    if (EntityManager.Exists(alt) && !EntityManager.HasComponent<Deleted>(alt))
                        EntityManager.AddComponent<Deleted>(alt);
                Beende(true, $"{_lauf.Alt.Count} alte Flaeche(n) durch {neue.Count} neu geplante ersetzt, alle mit Dreiecken");
                return;
            }
            if (bild - _lauf.Seit < Hoechstwartezeit) return;
            // Nicht vollstaendig: die neuen wieder weg, der alte Bestand bleibt.
            foreach (var f in neue)
                if (EntityManager.Exists(f) && !EntityManager.HasComponent<Deleted>(f))
                    EntityManager.AddComponent<Deleted>(f);
            Beende(false, $"neue Flaechen {neue.Count}/{_lauf.Neu}, {ohneDreiecke} ohne Dreiecke - "
                + "wieder entfernt, alter Bestand unveraendert");
        }

        private void LegeNeuaufbauAn(int bild)
        {
            var r = _lauf.Rechnung;
            if (r == null) { Beende(false, "keine Rechnung"); return; }
            if (!r.IsCompleted)
            {
                if (bild - _lauf.Seit > 3 * Hoechstwartezeit) Beende(false, "Rechnung dauert zu lange");
                return;
            }
            if (r.IsFaulted || r.IsCanceled)
            { Beende(false, "Rechnung gescheitert: " + r.Exception?.GetBaseException().Message); return; }
            // Wie beim Start: nicht in einen offenen Entwurf des Spielers hinein.
            var plt = World.GetOrCreateSystemManaged<ParkingLotToolSystem>();
            if (World.GetOrCreateSystemManaged<ToolSystem>().activeTool == plt && plt.ArbeitetGerade) return;
            var gruppen = _flaechenplaner.PlaneFlaechen(r.Result, out var grund);
            if (gruppen == null)
            {
                if (bild - _lauf.Seit > Hoechstwartezeit) Beende(false, "Flaechenprefabs nicht bereit: " + grund);
                return;
            }
            var hoehen = World.GetOrCreateSystemManaged<TerrainSystem>().GetHeightData();
            var angelegt = 0;
            var zeilen = new List<string>();
            foreach (var (art, ringe, prefab) in gruppen)
            {
                if (ringe == null || ringe.Length == 0) continue;
                if (prefab == Entity.Null || !EntityManager.Exists(prefab))
                { Beende(false, $"Gruppe {art}: kein Prefab - nichts angelegt, alter Bestand unveraendert"); return; }
                var n = 0;
                foreach (var ring in ringe)
                {
                    var knotenzahl = ParkingLotToolSystem.OpenNodeCount(ring);
                    if (knotenzahl < 3) continue;
                    var d = EntityManager.CreateEntity();
                    EntityManager.AddComponentData(d, new CreationDefinition
                    {
                        m_Prefab = prefab,
                        m_Owner = _lauf.Besitzer,
                        m_Flags = CreationFlags.Permanent,
                    });
                    EntityManager.AddComponent<Updated>(d);
                    var puffer = EntityManager.AddBuffer<Node>(d);
                    puffer.ResizeUninitialized(knotenzahl + 1);
                    for (var i = 0; i < knotenzahl; i++)
                    {
                        var h = TerrainUtils.SampleHeight(ref hoehen, new float3(ring[i].x, 0f, ring[i].y));
                        puffer[i] = new Node(new float3(ring[i].x, h, ring[i].y), float.MinValue);
                    }
                    // Ohne Original macht erst der wiederholte erste Knoten die
                    // Flaeche "Complete" (siehe LegeAn).
                    puffer[knotenzahl] = puffer[0];
                    ParkingLotToolSystem.NurDiesesBild(EntityManager, d);
                    n++;
                }
                angelegt += n;
                zeilen.Add(art + " " + n);
            }
            _lauf.Neu = angelegt;
            _lauf.Angelegt = true;
            _lauf.Seit = bild;
            Mod.log.Info($"PLT-Flaechensync: Lot {_lauf.Lot.Index}: {angelegt} Flaeche(n) wie beim Neubau "
                + $"angelegt ({string.Join(", ", zeilen)}); die {_lauf.Alt.Count} alten fallen erst, "
                + "wenn CS2 alle neuen angenommen hat.");
        }
    }
}
