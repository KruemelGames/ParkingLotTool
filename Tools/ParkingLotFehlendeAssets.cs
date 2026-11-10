using System;
using System.Collections.Generic;
using System.Linq;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /** Was ein Parkplatz statt seiner fehlenden Assets bekommt. */
    internal sealed class AssetErsatz
    {
        internal string Strasse, Deko, Zoning;
        /** `null` heisst: die Pflanzen bleiben, wie sie sind. */
        internal VegetationOptions Vegetation;
    }

    /**
     * PARKPLAETZE MIT ASSETS AUS FEHLENDEN MODS (Nutzer 2026-10-05).
     *
     * Ein Parkplatz merkt sich seine Flaechen und Pflanzenarten ueber den
     * Namen. Nimmt der Spieler den Asset-Mod heraus, laedt der Spielstand
     * trotzdem - GEMESSEN am 2026-10-05 mit Herringbone Pavers und European
     * Beech, mit und ohne Neustart: kein Absturz, kein PLT-Fehler. Aber die
     * Flaeche ist weiss oder ein CS2-Ersatz, und der Spieler erfaehrt nur aus
     * unserem Log, warum. Bearbeiten und Synchronisieren laufen ausserdem
     * ins Leere, weil die Vorschau das Prefab nicht findet.
     *
     * Dieses System erkennt solche Parkplaetze, zeigt sie in der Liste und
     * repariert sie auf Klick (oder mit "Automatisch reparieren"): Neubau im
     * Hintergrund wie beim Synchronisieren, nur mit dem gespeicherten
     * Standard des Spielers statt der fehlenden Flaeche bzw. Art. Ohne Klick
     * bleibt alles, wie es ist - wer den Mod wieder einschaltet, hat seinen
     * Parkplatz unveraendert zurueck.
     *
     * Erkannt wird nach dem Laden und mitten in der Sitzung: CS2 meldet die
     * Prefabs eines Asset-Packs beim Entfernen aus dem Playset sofort ab
     * (`GameManager.OnEntryIsInActivePlaysetChanged` -> `RemovePrefab`,
     * das Prefab bekommt `Deleted`). Deshalb zaehlen hier nur lebende,
     * aktivierte Prefabs ohne `Deleted`.
     */
    public sealed partial class ParkingLotFehlendeAssetsSystem : GameSystemBase
    {
        private const string Werksbelag = "Pavement Surface 01";
        private const string Werksgras = "Grass Surface 01";
        private const int Takt = 120;

        private static readonly Dictionary<Entity, AssetErsatz> Ersatzliste = new();

        /** Fuer den Hintergrund-Neubau: der Ersatz, falls dieser Parkplatz repariert wird. */
        internal static AssetErsatz ErsatzFuer(Entity lot)
            => Ersatzliste.TryGetValue(lot, out var e) ? e : null;

        private sealed class Befund
        {
            internal readonly List<string> Flaechen = new();
            internal readonly List<string> Arten = new();
            internal string Text;
        }

        private EntityQuery _lots, _flaechen, _pflanzen;
        private PrefabSystem _prefabs;
        private readonly Dictionary<Entity, Befund> _befunde = new();
        private readonly HashSet<Entity> _automatischVersucht = new();
        private int _bilder, _stand = -1;
        private string _letzterBericht;

        internal int Anzahl => _befunde.Count;

        internal bool Betrifft(Entity lot) => _befunde.ContainsKey(lot);

        /** Die fehlenden Namen fuer die Liste, leer wenn alles da ist. */
        internal string Text(Entity lot)
            => _befunde.TryGetValue(lot, out var b) ? b.Text : string.Empty;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _lots = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ParkingLotCarrierReference>(),
                    ComponentType.ReadOnly<ParkingLotBuildReceipt>(),
                    ComponentType.ReadOnly<ParkingLotBuildText>(),
                },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            _flaechen = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<SurfaceData>(), ComponentType.ReadOnly<AreaData>(),
                    ComponentType.ReadOnly<AreaGeometryData>(), ComponentType.ReadOnly<PrefabData>(),
                },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<PlaceholderObjectElement>() },
            });
            _pflanzen = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<PlantData>(), ComponentType.ReadOnly<ObjectGeometryData>(),
                    ComponentType.ReadOnly<PrefabData>(),
                },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
        }

        [Preserve]
        protected override void OnGameLoadingComplete(
            Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            _befunde.Clear();
            _automatischVersucht.Clear();
            Ersatzliste.Clear();
            _stand = -1;
            _letzterBericht = null;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            var spiel = GameManager.instance;
            if (spiel == null || spiel.isGameLoading || !spiel.gameMode.IsGame()) return;
            // Sofort, wenn sich die Zahl der Prefabs oder Parkplaetze aendert
            // (Mod mitten in der Sitzung entfernt, Parkplatz neu gebaut);
            // sonst im Takt, weil ein Neubau die Zahl gleich laesst.
            var stand = _flaechen.CalculateEntityCount() * 7919
                + _pflanzen.CalculateEntityCount() * 31 + _lots.CalculateEntityCount();
            if (stand == _stand && ++_bilder < Takt) return;
            _stand = stand;
            _bilder = 0;
            Erfasse();
            if (Mod.Optionen?.WaisenAutomatischReparieren == true) RepariereAutomatisch();
        }

        /** Fuer die Sync-Aufnahme: jetzt erfassen, nicht erst im naechsten Takt. */
        internal void ErfasseJetzt()
        {
            Erfasse();
            _bilder = 0;
        }

        private void Erfasse()
        {
            var flaechen = VerfuegbareFlaechen();
            var arten = VerfuegbareArten();
            var werkzeug = World.GetOrCreateSystemManaged<ParkingLotToolSystem>();
            _befunde.Clear();
            using var lots = _lots.ToEntityArray(Allocator.Temp);
            foreach (var lot in lots)
            {
                var befund = new Befund();
                foreach (var name in Flaechennamen(lot))
                    if (!flaechen.Contains(name) && !befund.Flaechen.Contains(name)) befund.Flaechen.Add(name);
                var vegetation = werkzeug.VegetationVon(lot);
                if (vegetation.Enabled && vegetation.Species != null)
                    foreach (var art in vegetation.Species)
                        if (!arten.Contains(art)) befund.Arten.Add(art);
                if (befund.Flaechen.Count == 0 && befund.Arten.Count == 0) continue;
                befund.Text = string.Join(", ", befund.Flaechen.Concat(befund.Arten.Select(Artname)));
                _befunde[lot] = befund;
            }
            var bericht = string.Join("; ", _befunde.Select(p => "Lot " + p.Key.Index + ": " + p.Value.Text)
                .OrderBy(s => s, StringComparer.Ordinal));
            if (bericht == _letzterBericht) return;
            _letzterBericht = bericht;
            Mod.log.Info(_befunde.Count == 0
                ? "PLT-Fehlende Assets: alle Parkplaetze haben ihre Flaechen und Pflanzen."
                : "PLT-Fehlende Assets: " + _befunde.Count + " Parkplatz/Parkplaetze nutzen Assets, die nicht geladen sind - " + bericht);
        }

        /** Fahrflaeche, Dekoflaeche und - falls gewaehlt - Baulandflaeche aus dem Bauzettel. */
        private IEnumerable<string> Flaechennamen(Entity lot)
        {
            var texte = EntityManager.GetBuffer<ParkingLotBuildText>(lot, true);
            for (var art = 1; art <= 3; art++)
                if (ParkingLotBaukontextLeser.TryReadBuildText(texte, art, out var name)
                    && !string.IsNullOrEmpty(name))
                    yield return name;
        }

        private HashSet<string> VerfuegbareFlaechen()
        {
            var namen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var prefabs = _flaechen.ToEntityArray(Allocator.Temp);
            foreach (var e in prefabs)
                if (_prefabs.TryGetPrefab<PrefabBase>(e, out var p) && p != null
                    && EntityManager.GetComponentData<AreaData>(e).m_Archetype.Valid)
                    namen.Add(p.name);
            return namen;
        }

        private HashSet<string> VerfuegbareArten()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            using var prefabs = _pflanzen.ToEntityArray(Allocator.Temp);
            foreach (var e in prefabs)
                if (_prefabs.TryGetPrefab<PrefabBase>(e, out var p) && p is StaticObjectPrefab)
                    ids.Add(p.GetPrefabID().ToString());
            return ids;
        }

        /** "StaticObjectPrefab:European Beech Tree (f23d...)" -> "European Beech Tree". */
        private static string Artname(string id)
        {
            var name = id.StartsWith("StaticObjectPrefab:", StringComparison.Ordinal)
                ? id.Substring("StaticObjectPrefab:".Length) : id;
            var klammer = name.LastIndexOf(" (", StringComparison.Ordinal);
            return klammer > 0 && name.EndsWith(")", StringComparison.Ordinal) ? name.Substring(0, klammer) : name;
        }

        internal void ReparierenAlle()
        {
            // Nie waehrend eines Sync-Durchgangs: der Neubau reisst den Parkplatz ab,
            // den ein Tausch vielleicht gerade anfasst (1.0.6). Die Meldung pulsiert.
            var sync = World.GetOrCreateSystemManaged<ParkingLotSyncSystem>();
            if (sync.SyncLaeuft) { sync.SperrtWegenArbeit("Reparieren"); return; }
            foreach (var lot in _befunde.Keys.ToArray()) Reparieren(lot);
        }

        private void RepariereAutomatisch()
        {
            // Laeuft ein Sync-Durchgang, wartet die Automatik - nichts als "versucht" merken.
            if (World.GetOrCreateSystemManaged<ParkingLotSyncSystem>().SyncLaeuft) return;
            foreach (var lot in _befunde.Keys.ToArray())
                if (_automatischVersucht.Add(lot)) Reparieren(lot);
        }

        /**
         * Baut den Parkplatz im Hintergrund neu - mit dem gespeicherten
         * Standard des Spielers fuer jede fehlende Flaeche und Art. Ist auch
         * der Standard nicht geladen, gilt der Werkswert (Vanilla).
         */
        internal void Reparieren(Entity lot)
        {
            if (ParkingLotFahrradBetrieb.IstBestand(EntityManager, lot))
            {
                Mod.log.Warn("PLT-Bauzettel Fahrrad: Auto-Assetreparatur ausgelassen, Lot " + lot.Index);
                World.GetOrCreateSystemManaged<ParkingLotUISystem>().SetStatus(ParkingLotTexte.T("fahrrad.automatikAus"));
                return;
            }
            if (!_befunde.TryGetValue(lot, out var befund)) return;
            var hintergrund = World.GetOrCreateSystemManaged<ParkingLotHintergrundSystem>();
            if (hintergrund.Gesperrt(lot)) return;
            var sync = World.GetOrCreateSystemManaged<ParkingLotSyncSystem>();
            if (sync.SyncLaeuft) { sync.SperrtWegenArbeit("Reparieren"); return; }
            if (!ParkingLotBaukontextLeser.TryRead(EntityManager, lot, out var kontext, out var grund, melden: false))
            {
                Mod.log.Warn("PLT-Fehlende Assets: Lot " + lot.Index + " nicht reparierbar, Bauzettel unlesbar: " + grund);
                return;
            }
            var ui = World.GetOrCreateSystemManaged<ParkingLotUISystem>();
            var flaechen = VerfuegbareFlaechen();
            string Waehle(string ist, string standard, string werk)
            {
                if (string.IsNullOrEmpty(ist) || flaechen.Contains(ist)) return ist;
                return !string.IsNullOrEmpty(standard) && flaechen.Contains(standard) ? standard : werk;
            }
            var ersatz = new AssetErsatz
            {
                Strasse = Waehle(kontext.FlaecheStrasse, ui.StandardFlaecheStrasse, Werksbelag),
                Deko = Waehle(kontext.FlaecheDekoration, ui.StandardFlaecheDeko, Werksgras),
                // Fuer die Baulandflaeche gibt es keinen gespeicherten Standard; leer heisst "keine".
                Zoning = Waehle(kontext.FlaecheZoning, string.Empty, string.Empty),
            };
            if (befund.Arten.Count > 0)
            {
                var arten = VerfuegbareArten();
                var vegetation = World.GetOrCreateSystemManaged<ParkingLotToolSystem>().VegetationVon(lot);
                var bleiben = vegetation.Species.Where(arten.Contains).ToArray();
                if (bleiben.Length == 0)
                    bleiben = (ui.StandardVegetation.Species ?? Array.Empty<string>()).Where(arten.Contains).ToArray();
                vegetation.Species = bleiben;
                ersatz.Vegetation = vegetation;
                // Der Neubau pflanzt aus dem Katalog; der kann vom Sitzungsbeginn stammen.
                ui.AktualisiereVegetationskatalog();
            }
            Ersatzliste[lot] = ersatz;
            Mod.log.Info("PLT-Fehlende Assets: Lot " + lot.Index + " wird repariert. Fahrflaeche '"
                + kontext.FlaecheStrasse + "' -> '" + ersatz.Strasse + "', Dekoflaeche '" + kontext.FlaecheDekoration
                + "' -> '" + ersatz.Deko + "', Baulandflaeche '" + kontext.FlaecheZoning + "' -> '" + ersatz.Zoning + "'"
                + (ersatz.Vegetation == null ? "" : ", Pflanzenarten jetzt: "
                    + (ersatz.Vegetation.Species.Length == 0 ? "keine" : string.Join(", ", ersatz.Vegetation.Species.Select(Artname)))) + ".");
            hintergrund.Einreihen(lot);
        }
    }
}
