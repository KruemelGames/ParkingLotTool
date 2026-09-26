using System.Collections.Generic;
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
    /** Ist das eine unserer Zufahrtsgassen? Die eine Stelle fuer diese Frage. */
    internal static class GassenPrefab
    {
        internal static bool Ist(PrefabSystem prefabs, Entity prefab)
            => prefabs != null && prefabs.TryGetPrefab<PrefabBase>(prefab, out var p) && p != null
               && p.name.StartsWith("PLT Zufahrtsgasse", System.StringComparison.Ordinal);
    }

    /**
     * DIE GASSEN KOMMEN IN EINEM ZWEITEN BAUSCHRITT - an die schon stehenden
     * Wegknoten angedockt, so wie man im Spiel eine Strasse an einen
     * vorhandenen Knoten baut.
     *
     * Befund 2026-09-26 (Messung "PLT-Gassenhoehe", Dekompilat, Codex):
     *
     *   - Im gemeinsamen Bau ist der innere Knoten NEU, und sein Prefab kommt
     *     vom zuletzt verarbeiteten Kurs (`GenerateNodesSystem.
     *     CollectUpdatesJob`) - mal die Gasse, mal ein unsichtbarer Weg.
     *     Mit Weg-Prefab legt `GroundHeightSystem` den Knoten aufs Gelaende,
     *     das die Gasse (ClipTerrain) dort beschnitten hat: 0,3-0,5 m tiefer,
     *     und jeder Edit erbte das.
     *   - Beim ersten Bau des Tages trugen zufaellig alle fuenf Knoten die
     *     Gasse - keiner sank. Das Prefab ist also der Hebel.
     *   - Nachtraegliches Ueberbauen (erster Versuch, 5b2f5c6) setzte das
     *     Prefab richtig, kam aber 0,4 s zu spaet; die Hoehe eines
     *     vorhandenen Knotens uebernimmt CS2 dabei unveraendert.
     *
     * Deshalb die Vanilla-Reihenfolge: erst entsteht der Parkplatz OHNE
     * Gassen. Der Wegknoten am inneren Ende liegt dann auf Gelaende, das
     * keine Gasse beschneidet - und weil er ein Weg ist, fuehrt CS2 ihn dem
     * Gelaende nach (beim Edit auch zurueck nach oben, sobald die alte Gasse
     * abgerissen ist). Danach dockt die Gasse mit `CoursePos.m_Entity` an
     * diesen Knoten an; fuer einen VORHANDENEN Knoten waehlt
     * `FindNodePrefab` nach `m_NodePriority`, und die Gasse (2008) schlaegt
     * die Wege (7 und 3). Ab da ebnet der Knoten selbst ein und bleibt, wo er
     * ist.
     */
    public sealed partial class ParkingLotToolSystem
    {
        private sealed class Gassenauftrag
        {
            internal NetSegment Piece;
            internal int Index;
            internal float Vorflaechenbreite;
        }

        private enum GkPhase { Idle, WarteAufRuhe, TempWarten, ApplyWarten }

        private readonly List<Gassenauftrag> _gassenauftraege = new();
        private GkPhase _gkPhase;
        private int _gkFrame;
        private int _gkRuhig;
        private Entity _gkLot;
        private Entity _gkTraeger;
        private readonly List<Entity> _gkDefinitionen = new();
        private int _gkKurse;
        private const int GkFrist = 90;
        private const int GkRuheFrist = 300;

        private bool GassenknotenLaeuft => _gkPhase != GkPhase.Idle;

        /** Aus dem Hauptbau: diese Gasse erst im zweiten Schritt anlegen. */
        private void MerkeGassenauftrag(NetSegment piece, int index, float vorflaechenbreite)
            => _gassenauftraege.Add(new Gassenauftrag
            {
                Piece = piece, Index = index, Vorflaechenbreite = vorflaechenbreite,
            });

        /**
         * Nach dem Apply des Hauptbaus. `true`: der Gassenschritt laeuft, der
         * Gassenbefund folgt erst nach ihm.
         */
        private bool PlaneGassenbau()
        {
            if (_gassenauftraege.Count == 0) return false;
            _gkLot = _lotOwner;
            _gkTraeger = _lotCarrier;
            _gkPhase = GkPhase.WarteAufRuhe;
            _gkFrame = UnityEngine.Time.frameCount;
            _gkRuhig = 0;
            return true;
        }

        /** Gibt `true`, solange dieser Schritt das Bild (und `applyMode`) braucht. */
        private bool PflegeGassenknoten()
        {
            var vergangen = UnityEngine.Time.frameCount - _gkFrame;
            switch (_gkPhase)
            {
                case GkPhase.WarteAufRuhe:
                {
                    _aufraeumer ??= World.GetExistingSystemManaged<ParkingLotCleanupSystem>();
                    var frei = !IsEditing && _buildStage == BuildStage.Idle
                        && GkTempQuery().IsEmptyIgnoreFilter
                        && (_aufraeumer == null || !_aufraeumer.AbrissLaeuft);
                    /*
                     * RUHE HEISST: jeder Wegknoten am inneren Ende liegt auf dem
                     * aktuellen Gelaende. Beim Edit schneidet die alte Gasse dort
                     * noch, bis der Aufraeumer sie abgerissen hat; danach hebt
                     * CS2 das Gelaende und fuehrt den Knoten nach. Erst dann
                     * stimmt die Hoehe, die die Gasse erbt.
                     */
                    if (frei && GkGelaendeRuht() && GkKnotenLiegenAufGelaende()) _gkRuhig++;
                    else _gkRuhig = 0;
                    if (_gkRuhig < 3 && vergangen <= GkRuheFrist) return false;
                    if (_gkRuhig < 3)
                    {
                        if (!frei)
                        {
                            GkAbbruch("kein freier Bauzyklus nach " + vergangen + " Bildern");
                            return false;
                        }
                        Mod.log.Warn($"PLT-Gassenbau: Wegknoten nach {vergangen} Bildern noch nicht "
                            + "auf dem Gelaende; die Gassen werden trotzdem gebaut.");
                    }
                    GkLegeGassenAn(vergangen);
                    return _gkPhase != GkPhase.Idle;
                }
                case GkPhase.TempWarten:
                {
                    var neu = new List<Entity>();
                    using (var temps = GkTempQuery().ToEntityArray(Allocator.Temp))
                        foreach (var t in temps)
                            if (EntityManager.HasComponent<Edge>(t)
                                && EntityManager.GetComponentData<Temp>(t).m_Original == Entity.Null
                                && GassenPrefab.Ist(_prefabSystem, EntityManager.GetComponentData<PrefabRef>(t).m_Prefab))
                                neu.Add(t);
                    if (neu.Count < _gkKurse)
                    {
                        if (vergangen > GkFrist)
                            GkAbbruch($"nur {neu.Count} von {_gkKurse} Gassenkanten erschienen");
                        return true;
                    }
                    // Wie `AttachByPrefab` beim Hauptbau: Besitzer an die Temp-Kante,
                    // Eintrag in die Netzliste des Traegers. Die Kopie wird beim
                    // Apply selbst dauerhaft.
                    var geheftet = 0;
                    if (_gkLot != Entity.Null && EntityManager.Exists(_gkLot))
                        foreach (var kante in neu)
                        {
                            if (EntityManager.HasComponent<Owner>(kante))
                                EntityManager.SetComponentData(kante, new Owner(_gkLot));
                            else EntityManager.AddComponentData(kante, new Owner(_gkLot));
                            if (_gkTraeger != Entity.Null && EntityManager.Exists(_gkTraeger)
                                && EntityManager.HasBuffer<Game.Net.SubNet>(_gkTraeger))
                            {
                                var liste = EntityManager.GetBuffer<Game.Net.SubNet>(_gkTraeger);
                                var drin = false;
                                for (var i = 0; i < liste.Length && !drin; i++)
                                    drin = liste[i].m_SubNet == kante;
                                if (!drin) liste.Add(new Game.Net.SubNet(kante));
                            }
                            geheftet++;
                        }
                    Mod.log.Info($"PLT-Gassenbau: {neu.Count} Gassenkante(n) materialisiert, "
                        + $"{geheftet} mit dem Parkplatz als Besitzer; Apply.");
                    applyMode = ApplyMode.Apply;
                    _gkPhase = GkPhase.ApplyWarten;
                    _gkFrame = UnityEngine.Time.frameCount;
                    return true;
                }
                case GkPhase.ApplyWarten:
                {
                    if (!GkTempQuery().IsEmptyIgnoreFilter && vergangen <= GkFrist) return true;
                    GkEntferneDefinitionen();
                    var gasse = 0;
                    var gesamt = 0;
                    var andere = new List<string>();
                    foreach (var g in _gassenhoehen)
                    {
                        if (KantenAmPunkt(g.Lage, out var knoten, out _) == 0 || knoten == Entity.Null) continue;
                        gesamt++;
                        var prefab = EntityManager.GetComponentData<PrefabRef>(knoten).m_Prefab;
                        if (GassenPrefab.Ist(_prefabSystem, prefab)) gasse++;
                        else andere.Add(_prefabSystem.TryGetPrefab<PrefabBase>(prefab, out var pb) && pb != null
                            ? pb.name : prefab.ToString());
                    }
                    var text = $"PLT-Gassenbau FERTIG: {gasse} von {gesamt} inneren Knoten tragen das Gassen-Prefab.";
                    if (andere.Count > 0) Mod.log.Warn(text + " Andere: " + string.Join(", ", andere));
                    else Mod.log.Info(text);
                    _gkPhase = GkPhase.Idle;
                    MeldeGassenbefundAn();
                    return false;
                }
            }
            return false;
        }

        /**
         * Hat CS2 das Gelaende fertig neu gerechnet? Nach dem Abriss einer
         * einebnenden Gasse setzt `TerrainSystem` erst `heightMapRenderRequired`,
         * rendert dann und liest die CPU-Hoehen asynchron zurueck
         * (`m_HeightMapChanged`, privat). Solange eins davon steht, ist die
         * CPU-Hoehe ein Zwischenstand - ein Knoten "auf dem Gelaende" laege
         * dann noch auf dem von der alten Gasse beschnittenen.
         */
        private static readonly System.Reflection.FieldInfo GkHoehenkarteGeaendert =
            typeof(Game.Simulation.TerrainSystem).GetField("m_HeightMapChanged",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        private bool GkGelaendeRuht()
        {
            if (_terrainSystem.heightMapRenderRequired) return false;
            return GkHoehenkarteGeaendert == null
                || !(GkHoehenkarteGeaendert.GetValue(_terrainSystem) is bool b && b);
        }

        /** Liegt jeder vorhandene Wegknoten an einem inneren Gassenende auf dem aktuellen Gelaende? */
        private bool GkKnotenLiegenAufGelaende()
        {
            var gelaende = _terrainSystem.GetHeightData();
            foreach (var auftrag in _gassenauftraege)
            {
                if (KantenAmPunkt(auftrag.Piece.B, out var knoten, out var versatz) == 0
                    || knoten == Entity.Null || versatz > 0.05f) continue;
                var p = EntityManager.GetComponentData<Node>(knoten).m_Position;
                var boden = Game.Simulation.TerrainUtils.SampleHeight(ref gelaende, p);
                if (!math.isfinite(boden) || math.abs(p.y - boden) > 0.05f) return false;
            }
            return true;
        }

        private void GkLegeGassenAn(int gewartet)
        {
            var gelaende = _terrainSystem.GetHeightData();
            var hoehen = new Dictionary<(long, long), float>();
            var zufall = new Unity.Mathematics.Random((uint)System.Environment.TickCount | 1u);
            var bericht = new List<string>();
            var vorher = _netRecords.Count;
            var angedockt = 0;
            var gebaut = 0;
            _gassenhoehen.Clear();
            foreach (var auftrag in _gassenauftraege)
            {
                var innen = Entity.Null;
                if (KantenAmPunkt(auftrag.Piece.B, out var knoten, out var versatz) > 0
                    && knoten != Entity.Null && versatz <= 0.05f)
                {
                    innen = knoten;
                    // Die Gasse erbt die Hoehe des Wegknotens - dort, wo CS2
                    // ihn dem Gelaende nachgefuehrt hat.
                    MerkeHoehe(auftrag.Piece.B,
                        EntityManager.GetComponentData<Node>(knoten).m_Position.y, hoehen);
                    angedockt++;
                }
                gebaut += CreateGassenstueck(auftrag.Piece, auftrag.Index, auftrag.Vorflaechenbreite,
                    ref gelaende, hoehen, ref zufall, bericht, innen) > 0 ? 1 : 0;
            }
            _gkDefinitionen.Clear();
            for (var i = vorher; i < _netRecords.Count; i++) _gkDefinitionen.Add(_netRecords[i].Definition);
            _gkKurse = _gkDefinitionen.Count;
            Mod.log.Info($"PLT-Gassenbau: nach {gewartet} Bildern {gebaut} von {_gassenauftraege.Count} "
                + $"Gasse(n) angelegt, {angedockt} an einen vorhandenen Wegknoten angedockt. "
                + string.Join("; ", bericht));
            _gassenauftraege.Clear();
            if (_gkKurse == 0)
            {
                _gkPhase = GkPhase.Idle;
                MeldeGassenbefundAn();
                return;
            }
            _gkPhase = GkPhase.TempWarten;
            _gkFrame = UnityEngine.Time.frameCount;
        }

        private EntityQuery GkTempQuery() => GetEntityQuery(
            ComponentType.ReadOnly<Temp>(),
            ComponentType.ReadOnly<PrefabRef>(),
            ComponentType.Exclude<Deleted>());

        private void GkEntferneDefinitionen()
        {
            foreach (var d in _gkDefinitionen)
                if (EntityManager.Exists(d)) EntityManager.DestroyEntity(d);
            _gkDefinitionen.Clear();
        }

        private void GkAbbruch(string grund)
        {
            Mod.log.Warn("PLT-Gassenbau: abgebrochen - " + grund
                + ". Der Parkplatz steht ohne Zufahrtsgassen.");
            GkEntferneDefinitionen();
            if (_gkPhase == GkPhase.TempWarten) applyMode = ApplyMode.Clear;
            _gassenauftraege.Clear();
            _gkPhase = GkPhase.Idle;
        }
    }
}
