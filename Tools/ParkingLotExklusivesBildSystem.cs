using System;
using Game;
using Game.SceneFlow;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    internal enum ParkingLotDefinitionsmodus { Temp, Permanent }

    /** Einmaliger Erzeugungspunkt fuer ein exklusives Bild.
     *  Vorgesehene Anmeldung: UpdateAfter<..., ToolOutputBarrier>(ToolUpdate).
     *  ToolOutputBarrier ist dann abgespielt; CourseSplit folgt in PostTool,
     *  GenerateNodes/Objects/Areas in Modification1, Edges in Modification2.
     *  Angemeldet hinter dem Auftragsupdate. Runde 4 benutzt getrennte
     *  exklusive Bilder fuer Besitzer, Kinder, Rueckweg und Nebenarbeit.
     *  Globale Queries sind ausschliesslich lesende E1-Zaehler. */
    [DisableAutoCreation]
    internal sealed partial class ParkingLotExklusivesBildSystem : GameSystemBase
    {
        private EntityQuery _definitionen;
        private EntityQuery _temps;
        private Entity _lot;
        private Func<ParkingLotDefinitionsmodus, int> _anlegen;
        private int _wartendeBilder;
        private int _letztesWartebild = -1;
        private int _letzteDefinitionen = -1, _letzteTemps = -1;
        private bool _letzteBereitschaft;
        internal string Wartehinweis { get; private set; } = string.Empty;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            // Kein Updated-/Deleted-Filter: E1 fordert die ganzen Mengen.
            var alle = EntityQueryOptions.IncludeDisabledEntities
                | EntityQueryOptions.IncludePrefab;
            _definitionen = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<CreationDefinition>() },
                Options = alle,
            });
            _temps = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Temp>() },
                Options = alle,
            });
        }

        /** Der Aufruf muss synchron ALLE Definitionen anlegen. Kein eigener
         *  CommandBuffer darf sie hinter die Vanilla-Phasen verschieben.
         *  Nur ein Erzeuger: die spaetere Auftragsmaschine bleibt die Queue. */
        internal void Erwarte(Entity lot, Func<ParkingLotDefinitionsmodus, int> anlegen)
        {
            if (_anlegen != null) throw new InvalidOperationException(
                "Der exklusive Erzeugungspunkt ist bereits belegt.");
            if (lot == Entity.Null) throw new ArgumentException("Lot fehlt.", nameof(lot));
            World.GetExistingSystemManaged<ParkingLotToolSystem>()?.EntferneSurfacePreview("exklusiver Auftrag");
            _anlegen = anlegen ?? throw new ArgumentNullException(nameof(anlegen));
            _lot = lot;
            _wartendeBilder = 0;
            _letztesWartebild = -1;
            _letzteDefinitionen = _letzteTemps = -1;
            Wartehinweis = string.Empty;
        }

        internal void Verwerfe(Entity lot)
        {
            if (_lot != lot) return;
            _anlegen = null;
            _lot = Entity.Null;
            Wartehinweis = string.Empty;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (_anlegen == null) return;
            var spiel = GameManager.instance;
            var bereit = spiel != null && !spiel.isGameLoading && spiel.gameMode.IsGame();
            // ToEntityArray synchronisiert die Query-Abhaengigkeiten.
            // Ausstehende Tool-Schreiber sind am vorgesehenen Wiedergabepunkt
            // bereits in der Welt; EndFrame-Schreiber laufen vor ToolSystem.
            ZaehleEntwuerfe(out var definitionen, out var temps);
            if (!ExklusivesBaubild.DarfAnlegen(definitionen, temps, bereit))
            {
                var werkzeug = World.GetExistingSystemManaged<ParkingLotToolSystem>();
                bool pltZeichnet = World.GetExistingSystemManaged<ToolSystem>()?.activeTool == werkzeug
                    && werkzeug?.HatAktivenEntwurf == true;
                Wartehinweis = pltZeichnet ? ParkingLotTexte.T("exklusivesBildSystem.synchronizationIsWaitingFinishTheDraft")
                    : ParkingLotTexte.T("exklusivesBildSystem.synchronizationIsWaitingForAFree");
                var bild = UnityEngine.Time.frameCount;
                var neuesBild = bild != _letztesWartebild;
                if (neuesBild) _wartendeBilder++;
                _letztesWartebild = bild;
                if (definitionen != _letzteDefinitionen || temps != _letzteTemps
                    || bereit != _letzteBereitschaft || (neuesBild && _wartendeBilder % 120 == 0))
                {
                    Melde($"wartet Lot {_lot.Index}: "
                        + ExklusivesBaubild.Wartegrund(definitionen, temps, bereit)
                        + "; " + Wartehinweis
                        + $"; {_wartendeBilder} wartende Bilder.");
                    // Der Lauf 13:01 enthaelt 0 Tor-Wartezeilen und keine
                    // Blockierer-ID. Beim naechsten Befund Herkunft lesen,
                    // ohne eine fremde Definition/Temp zu veraendern.
                    ParkingLotHintergrundDiagnose.Sicher(() => MeldeDefinitionen(werkzeug?.HatAktivenEntwurf == true));
                }
                _letzteDefinitionen = definitionen;
                _letzteTemps = temps;
                _letzteBereitschaft = bereit;
                return;
            }
            var anlegen = _anlegen;
            var lot = _lot;
            // Vor dem Callback austragen: ein Fehler darf keinen zweiten,
            // ueberlappenden Neubau beim naechsten Update erzeugen.
            Verwerfe(lot);
            var gemeldet = anlegen(ParkingLotDefinitionsmodus.Permanent);
            ZaehleEntwuerfe(out var anzahl, out _);
            if (gemeldet != anzahl) Melde($"Definitionszaehler: Erzeuger {gemeldet}, ECS {anzahl}; ECS ist massgeblich.");
            if (_wartendeBilder > 0) Melde($"exklusives Bild Lot {lot.Index}: fremde Definitionen 0, "
                + $"Temps 0; {anzahl} Definitionen angelegt; "
                + $"{_wartendeBilder} Bilder gewartet.");
        }

        protected override void OnGameLoaded(Colossal.Serialization.Entities.Context context)
        {
            base.OnGameLoaded(context);
            // Delegates enthalten alte Entity-IDs und werden nie geladen.
            Verwerfe(_lot);
        }

        /*
         * EINE AUSWAHL-HERVORHEBUNG IST KEIN ENTWURF (Sync haengt, 28.10.).
         *
         * Steht die Maus mit dem Standardwerkzeug ueber einem Parkplatz, legt
         * Vanilla in jedem Bild eine Definition mit nur `Select`, ohne Prefab,
         * fuer die Hervorhebung an - samt Temp mit `TempFlags.Select`. Sie baut
         * nichts und wird nie uebernommen. Das Tor zaehlte sie als fremden
         * Entwurf: Lot 499407 wartete ueber 2000 Bilder ("Torbelegung ... Flags
         * Select, Prefab (kein)"), weil man beim Sync auf seine Parkplaetze
         * schaut. Unsere Definitionen sind fest (Permanent) und brauchen kein
         * Apply, das sich mit ihr mischen koennte. Jeder echte Entwurf zaehlt
         * weiter: mit Prefab, oder ein Temp mit Erzeugen/Loeschen/Aendern.
         */
        private void ZaehleEntwuerfe(out int definitionen, out int temps)
        {
            definitionen = temps = 0;
            using (var alle = _definitionen.ToEntityArray(Allocator.Temp))
                foreach (var e in alle)
                {
                    var d = EntityManager.GetComponentData<CreationDefinition>(e);
                    if (d.m_Flags == CreationFlags.Select && d.m_Prefab == Entity.Null) continue;
                    definitionen++;
                }
            const TempFlags aenderung = TempFlags.Create | TempFlags.Delete | TempFlags.Modify
                | TempFlags.Replace | TempFlags.Upgrade | TempFlags.Duplicate | TempFlags.Combine;
            using (var alle = _temps.ToEntityArray(Allocator.Temp))
                foreach (var e in alle)
                {
                    var f = EntityManager.GetComponentData<Temp>(e).m_Flags;
                    if ((f & TempFlags.Select) != 0 && (f & aenderung) == 0) continue;
                    temps++;
                }
        }

        private static void Melde(string text)
        {
            var zeile = "PLT-Hintergrund: " + text;
            Mod.log.Info(zeile);
            ParkingGeometry.Live(zeile);
        }

        private void MeldeDefinitionen(bool entwurf)
        {
            var prefabs = World.GetExistingSystemManaged<Game.Prefabs.PrefabSystem>();
            using var eingaben = _definitionen.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < eingaben.Length && i < 12; i++)
            {
                var e = eingaben[i]; var d = EntityManager.GetComponentData<CreationDefinition>(e);
                string prefab = prefabs != null ? ParkingLotHintergrundDiagnose.Name(prefabs,d.m_Prefab) : d.m_Prefab.ToString();
                string auftrag = EntityManager.HasComponent<ParkingLotAuftragsdefinition>(e)
                    ? EntityManager.GetComponentData<ParkingLotAuftragsdefinition>(e).Auftrag.ToString() : "fremd/unmarkiert";
                Melde($"Torbelegung: Definition {e}, Prefab {prefab}, Flags {d.m_Flags}, Owner {d.m_Owner}, Original {d.m_Original}, "
                    + $"Auftrag {auftrag}, Updated={EntityManager.HasComponent<Game.Common.Updated>(e)}, "
                    + $"NetCourse={EntityManager.HasComponent<NetCourse>(e)}, Objekt={EntityManager.HasComponent<ObjectDefinition>(e)}, "
                    + $"Flaechenpuffer={EntityManager.HasBuffer<Game.Areas.Node>(e)}, PLT-Entwurf={entwurf}.");
            }
            if (eingaben.Length > 12) Melde($"Torbelegung: weitere {eingaben.Length-12} Definitionen nur gezaehlt.");
        }
    }
}
