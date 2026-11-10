using System;
using System.Collections.Generic;
using System.Globalization;
using Game;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * MISST DIE LATERNEN-KANDIDATEN, BEVOR SIE GESETZT WERDEN (2026-10-04).
     *
     * Der Nutzer hat die Auswahl festgelegt: Einzel- und Doppelstrassenlaterne
     * 01-03, Gewerbe-Lichtmast 01-04 (01-02 einseitig, 03-04 rundum) und
     * Industrie-Lichtmast 01-04 (wie Gewerbe, hoeher). Bevor die Platzierung
     * Hoehe, Drehung und Lichtabstand benutzt, liefert diese Inventur die
     * echten Werte: Abmessungen, Kollisionsflags, Strassenlaternen-Ebene und
     * je Lichteffekt Lage, Reichweite und die Bedingungen, unter denen er
     * brennt. Aus der Lage der Lichteffekte relativ zum Mast ergibt sich, in
     * welche Richtung die Arme zeigen.
     *
     * Hintergrund zum Licht (Dekompilat): `EffectControlData.CheckTrigger`
     * prueft `Night` zeitgesteuert, `Operational` ohne Gebaeude immer wahr und
     * `LightsOff` ueber `StreetLight.m_State`. Diesen Zustand schaltet
     * `StreetLightSystem` nur fuer Unterobjekte von Strassen, Gebaeuden und
     * Schiffen - an unserem Traeger nie. Ob eine Laterne bei uns tagsueber
     * brennt, haengt also an den Bedingungen ihres Effekts.
     *
     * Nur lesen, einmal je geladenem Spielstand.
     */
    public sealed partial class ParkingLotLaternenInventurSystem : GameSystemBase
    {
        internal static readonly string[] Kandidaten =
        {
            "StreetlightSingle01", "StreetlightSingle02", "StreetlightSingle03",
            "StreetlightDouble01", "StreetlightDouble02", "StreetlightDouble03",
            "LightpoleCommercial01", "LightpoleCommercial02", "LightpoleCommercial03", "LightpoleCommercial04",
            "LightpoleIndustrial01", "LightpoleIndustrial02", "LightpoleIndustrial03", "LightpoleIndustrial04",
            // Fahrradplatz (03.11.2026): Hoehe/Reichweite im Katalog sind bis zur Messung vorlaeufig.
            "LightpolePark02",
        };

        private PrefabSystem _prefabs;
        private EntityQuery _objektPrefabs;
        private bool _faellig;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _objektPrefabs = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(),
                ComponentType.ReadOnly<ObjectGeometryData>());
        }

        [Preserve]
        protected override void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (mode == GameMode.Game) _faellig = true;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (!_faellig) return;
            _faellig = false;
            try { Messe(); }
            catch (Exception ausnahme) { Mod.log.Warn("PLT-Laternen-Inventur fehlgeschlagen: " + ausnahme); }
        }

        private void Messe()
        {
            var gesucht = new HashSet<string>(Kandidaten, StringComparer.Ordinal);
            var gefunden = new SortedDictionary<string, string>(StringComparer.Ordinal);
            using var prefabs = _objektPrefabs.ToEntityArray(Allocator.Temp);
            foreach (var e in prefabs)
            {
                if (!_prefabs.TryGetPrefab<PrefabBase>(e, out var asset) || asset == null || !gesucht.Contains(asset.name)) continue;
                gefunden[asset.name] = Beschreibe(e, asset);
            }
            var zeilen = new List<string>();
            foreach (var name in Kandidaten)
                zeilen.Add(gefunden.TryGetValue(name, out var text) ? text : "'" + name + "': NICHT GELADEN");
            Mod.log.Info("PLT-Laternen-Inventur: " + gefunden.Count + " von " + Kandidaten.Length
                + " Kandidaten geladen.\n  " + string.Join("\n  ", zeilen));
        }

        private static string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);
        private static string F3(float3 v) => "(" + F(v.x) + "|" + F(v.y) + "|" + F(v.z) + ")";

        private string Beschreibe(Entity e, PrefabBase asset)
        {
            var g = EntityManager.GetComponentData<ObjectGeometryData>(e);
            var text = "'" + asset.name + "' (" + asset.GetType().Name + "): Bounds " + F3(g.m_Bounds.min) + ".." + F3(g.m_Bounds.max)
                + ", Hoehe " + F(g.m_Bounds.max.y - g.m_Bounds.min.y) + " m, Pivot " + F3(g.m_Pivot)
                + ", Flags " + g.m_Flags + ", Groesse " + F3(g.m_Size) + ", Bein " + F3(g.m_LegSize)
                + " Versatz (" + F(g.m_LegOffset.x) + "|" + F(g.m_LegOffset.y) + ")";
            text += ", Platzierbar " + (EntityManager.HasComponent<PlaceableObjectData>(e) ? "ja" : "nein");
            text += ", UIObject " + (EntityManager.HasComponent<UIObjectData>(e) ? "ja" : "nein");
            if (EntityManager.HasComponent<StreetLightData>(e))
                text += ", Strassenlaterne Ebene " + EntityManager.GetComponentData<StreetLightData>(e).m_Layer;
            else text += ", KEINE StreetLightData";
            if (EntityManager.HasBuffer<SubMesh>(e)) text += ", Meshes " + EntityManager.GetBuffer<SubMesh>(e, true).Length;
            if (!EntityManager.HasBuffer<Effect>(e)) return text + ", keine Effekte";
            var effekte = EntityManager.GetBuffer<Effect>(e, true);
            var teile = new List<string>();
            for (var i = 0; i < effekte.Length; i++)
            {
                var ef = effekte[i];
                var name = _prefabs.TryGetPrefab<PrefabBase>(ef.m_Effect, out var efAsset) && efAsset != null ? efAsset.name : "?";
                var t = name + " @" + F3(ef.m_Position);
                if (EntityManager.HasComponent<LightEffectData>(ef.m_Effect))
                {
                    var l = EntityManager.GetComponentData<LightEffectData>(ef.m_Effect);
                    t += " Licht Reichweite " + F(l.m_Range) + " m, " + F(l.m_ColorTemperature) + " K";
                }
                if (EntityManager.HasComponent<EffectData>(ef.m_Effect))
                {
                    var b = EntityManager.GetComponentData<EffectData>(ef.m_Effect).m_Flags;
                    t += " [braucht " + b.m_RequiredFlags + ", verboten " + b.m_ForbiddenFlags + ", Staerke " + b.m_IntensityFlags + "]";
                }
                teile.Add(t);
            }
            return text + ", Effekte " + effekte.Length + ": " + string.Join("; ", teile);
        }
    }
}
