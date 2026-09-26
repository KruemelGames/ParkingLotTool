using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * DER INNERE GASSENKNOTEN BEKOMMT DAS GASSEN-PREFAB - so, wie CS2 es
     * beim Anbauen an einen vorhandenen Knoten selbst entscheidet.
     *
     * Befund 2026-09-26 (siehe "DAS INNERE GASSENENDE" in
     * ParkingLotNetBuilder.cs): im Bau ist der Knoten neu, und das Prefab
     * kommt vom zuletzt verarbeiteten Kurs. Traegt er einen unsichtbaren Weg,
     * legt `GroundHeightSystem` ihn aufs von der Gasse beschnittene Gelaende.
     *
     * Vanilla haengt einen Weg an eine STEHENDE Strasse; dann waehlt
     * `GenerateNodesSystem.FindNodePrefab` das Prefab mit der hoechsten
     * `m_NodePriority`, und die Strasse (+2000) gewinnt. Ein Knoten mit
     * FlattenTerrain-Prefab laesst `GroundHeightSystem.NetIterator` aus.
     *
     * Genau diese Entscheidung holen wir nach dem Apply nach: jede Gasse wird
     * einmal so "ueberbaut", wie es CS2s Aufwertungswerkzeug tut
     * (`NetToolSystem.CreateUpgrade`: Definition mit `m_Original` = Kante,
     * gleiches Prefab, gleiches `Upgraded`, Kurs von Knoten zu Knoten). Dabei
     * entstehen beide Endknoten mit Original neu, und `FindNodePrefab`
     * entscheidet nach Prioritaet. Kein Direktschreiben in fertige Netze.
     *
     * Einziger Eingriff an den Temp-Kopien: `Owner`. Unsere Definitionen sind
     * besitzerlos (ParkingLotNetBuilder.cs, Hoehenweg in CourseSplitSystem),
     * also tragen die Kopien keinen - und `ApplyNetSystem` wuerde den
     * `Owner` des Originals dann entfernen (`UpdateComponent`, Zweig
     * "Original hat, Temp nicht"). Wir geben den Kopien den Besitzer ihres
     * Originals, wie beim Hauptbau.
     */
    /** Ist das eine unserer Zufahrtsgassen? Die eine Stelle fuer diese Frage. */
    internal static class GassenPrefab
    {
        internal static bool Ist(PrefabSystem prefabs, Entity prefab)
            => prefabs != null && prefabs.TryGetPrefab<PrefabBase>(prefab, out var p) && p != null
               && p.name.StartsWith("PLT Zufahrtsgasse", System.StringComparison.Ordinal);
    }

    public sealed partial class ParkingLotToolSystem
    {
        private enum GkPhase { Idle, Definieren, TempWarten, ApplyWarten }

        private GkPhase _gkPhase;
        private int _gkFrame;
        private readonly List<float2> _gkEnden = new();
        private readonly List<Entity> _gkDefinitionen = new();
        private readonly List<Entity> _gkKanten = new();
        private const int GkFrist = 90;

        /** Nach einem erfolgreichen Bau: die inneren Gassenenden vormerken. */
        private void PlaneGassenknoten()
        {
            _gkEnden.Clear();
            foreach (var g in _gassenhoehen) _gkEnden.Add(g.Lage);
            if (_gkEnden.Count == 0) return;
            _gkPhase = GkPhase.Definieren;
            _gkFrame = UnityEngine.Time.frameCount;
        }

        private bool GassenknotenLaeuft => _gkPhase != GkPhase.Idle;

        /** Gibt `true`, solange dieser Schritt das Bild (und `applyMode`) braucht. */
        private bool PflegeGassenknoten()
        {
            var vergangen = UnityEngine.Time.frameCount - _gkFrame;
            switch (_gkPhase)
            {
                case GkPhase.Definieren:
                {
                    if (IsEditing || _buildStage != BuildStage.Idle || !GkTempQuery().IsEmptyIgnoreFilter)
                    {
                        if (vergangen > GkFrist) GkAbbruch("kein freier Bauzyklus");
                        return false;
                    }
                    _gkKanten.Clear();
                    var schonGasse = 0;
                    var ohneKnoten = 0;
                    foreach (var ende in _gkEnden)
                    {
                        if (KantenAmPunkt(ende, out var knoten, out _) == 0 || knoten == Entity.Null)
                        {
                            ohneKnoten++;
                            continue;
                        }
                        if (GassenPrefab.Ist(_prefabSystem, EntityManager.GetComponentData<PrefabRef>(knoten).m_Prefab))
                        {
                            schonGasse++;
                            continue;
                        }
                        var puffer = EntityManager.GetBuffer<ConnectedEdge>(knoten, true);
                        for (var i = 0; i < puffer.Length; i++)
                        {
                            var kante = puffer[i].m_Edge;
                            if (EntityManager.HasComponent<Temp>(kante)
                                || EntityManager.HasComponent<Deleted>(kante)) continue;
                            var e = EntityManager.GetComponentData<Edge>(kante);
                            if (e.m_Start != knoten && e.m_End != knoten) continue;
                            if (!GassenPrefab.Ist(_prefabSystem, EntityManager.GetComponentData<PrefabRef>(kante).m_Prefab)) continue;
                            if (!_gkKanten.Contains(kante)) _gkKanten.Add(kante);
                        }
                    }
                    if (ohneKnoten > 0 && vergangen <= GkFrist && _gkKanten.Count == 0 && schonGasse == 0)
                        return false;
                    Mod.log.Info($"PLT-Gassenknoten: {_gkEnden.Count} innere Ende(n) - {schonGasse} tragen "
                        + $"schon das Gassen-Prefab, {_gkKanten.Count} Gasse(n) werden ueberbaut, "
                        + $"{ohneKnoten} ohne Knoten gefunden.");
                    if (_gkKanten.Count == 0)
                    {
                        _gkPhase = GkPhase.Idle;
                        return false;
                    }
                    foreach (var kante in _gkKanten) _gkDefinitionen.Add(GkUeberbaue(kante));
                    _gkPhase = GkPhase.TempWarten;
                    _gkFrame = UnityEngine.Time.frameCount;
                    return true;
                }
                case GkPhase.TempWarten:
                {
                    var angepasst = 0;
                    var gefunden = 0;
                    using var temps = GkTempQuery().ToEntityArray(Allocator.Temp);
                    foreach (var t in temps)
                    {
                        var original = EntityManager.GetComponentData<Temp>(t).m_Original;
                        if (_gkKanten.Contains(original)) gefunden++;
                        // Nur Knoten und Kanten: Spuren und Unterobjekte der Kopie
                        // gehoeren der Temp-Kante, nicht dem Parkplatz.
                        if (!EntityManager.HasComponent<Node>(t) && !EntityManager.HasComponent<Edge>(t)) continue;
                        if (original == Entity.Null || !EntityManager.Exists(original)
                            || !EntityManager.HasComponent<Owner>(original)) continue;
                        var soll = EntityManager.GetComponentData<Owner>(original);
                        if (EntityManager.HasComponent<Owner>(t))
                        {
                            if (EntityManager.GetComponentData<Owner>(t).m_Owner == soll.m_Owner) continue;
                            EntityManager.SetComponentData(t, soll);
                        }
                        else EntityManager.AddComponentData(t, soll);
                        angepasst++;
                    }
                    if (gefunden < _gkKanten.Count)
                    {
                        if (vergangen > GkFrist) GkAbbruch($"nur {gefunden} von {_gkKanten.Count} Temp-Kopien erschienen");
                        return true;
                    }
                    Mod.log.Info($"PLT-Gassenknoten: {temps.Length} Temp-Kopien, {angepasst} davon mit dem "
                        + "Besitzer ihres Originals versehen; Apply.");
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
                    var andere = new List<string>();
                    foreach (var ende in _gkEnden)
                    {
                        if (KantenAmPunkt(ende, out var knoten, out _) == 0 || knoten == Entity.Null) continue;
                        var prefab = EntityManager.GetComponentData<PrefabRef>(knoten).m_Prefab;
                        if (GassenPrefab.Ist(_prefabSystem, prefab)) gasse++;
                        else andere.Add(_prefabSystem.TryGetPrefab<PrefabBase>(prefab, out var pb) && pb != null
                            ? pb.name : prefab.ToString());
                    }
                    var ohneBesitzer = 0;
                    foreach (var kante in _gkKanten)
                        if (EntityManager.Exists(kante) && !EntityManager.HasComponent<Owner>(kante)) ohneBesitzer++;
                    var text = $"PLT-Gassenknoten NACH DEM UEBERBAUEN: {gasse} von {_gkEnden.Count} inneren "
                        + $"Knoten tragen das Gassen-Prefab; {ohneBesitzer} Gasse(n) ohne Besitzer.";
                    if (andere.Count > 0 || ohneBesitzer > 0)
                        Mod.log.Warn(text + (andere.Count > 0 ? " Andere Prefabs: " + string.Join(", ", andere) : ""));
                    else Mod.log.Info(text);
                    _gkPhase = GkPhase.Idle;
                    return false;
                }
            }
            return false;
        }

        /** Wie `NetToolSystem.CreateUpgrade` fuer EINE Kante, ohne Aenderung. */
        private Entity GkUeberbaue(Entity kante)
        {
            var definition = EntityManager.CreateEntity();
            EntityManager.AddComponentData(definition, new CreationDefinition
            {
                m_Original = kante,
                m_Prefab = EntityManager.GetComponentData<PrefabRef>(kante).m_Prefab,
                m_Flags = CreationFlags.Align | CreationFlags.SubElevation,
            });
            // Die Aufwertungen der Kante (z. B. RemoveCrosswalk an der Gasse)
            // gehoeren unveraendert mit, sonst nimmt das Ueberbauen sie weg.
            EntityManager.AddComponentData(definition,
                EntityManager.HasComponent<Upgraded>(kante)
                    ? EntityManager.GetComponentData<Upgraded>(kante)
                    : default);
            var ersatz = EntityManager.AddBuffer<SubReplacement>(definition);
            if (EntityManager.HasBuffer<SubReplacement>(kante))
            {
                var kopie = EntityManager.GetBuffer<SubReplacement>(kante, true).ToNativeArray(Allocator.Temp);
                ersatz = EntityManager.GetBuffer<SubReplacement>(definition);
                ersatz.AddRange(kopie);
                kopie.Dispose();
            }
            EntityManager.AddComponent<Updated>(definition);
            var edge = EntityManager.GetComponentData<Edge>(kante);
            var kurve = EntityManager.GetComponentData<Curve>(kante).m_Bezier;
            var kurs = new NetCourse
            {
                m_Curve = kurve,
                m_Length = MathUtils.Length(kurve),
                m_FixedIndex = EntityManager.HasComponent<Fixed>(kante)
                    ? EntityManager.GetComponentData<Fixed>(kante).m_Index : -1,
                m_StartPosition = new CoursePos
                {
                    m_Entity = edge.m_Start,
                    m_Position = kurve.a,
                    m_Rotation = NetUtils.GetNodeRotation(MathUtils.StartTangent(kurve)),
                    m_CourseDelta = 0f,
                    m_Flags = CoursePosFlags.IsFirst,
                    m_ParentMesh = -1,
                },
                m_EndPosition = new CoursePos
                {
                    m_Entity = edge.m_End,
                    m_Position = kurve.d,
                    m_Rotation = NetUtils.GetNodeRotation(MathUtils.EndTangent(kurve)),
                    m_CourseDelta = 1f,
                    m_Flags = CoursePosFlags.IsLast,
                    m_ParentMesh = -1,
                },
            };
            EntityManager.AddComponentData(definition, kurs);
            return definition;
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
            Mod.log.Warn("PLT-Gassenknoten: Ueberbauen abgebrochen - " + grund
                + ". Der Parkplatz bleibt, wie er gebaut wurde.");
            GkEntferneDefinitionen();
            if (_gkPhase == GkPhase.TempWarten) applyMode = ApplyMode.Clear;
            _gkPhase = GkPhase.Idle;
        }
    }
}
