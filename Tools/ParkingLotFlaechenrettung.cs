using System;
using System.Collections.Generic;
using System.Linq;
using Game;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * DAMIT EIN GELADENER PARKPLATZ SEIN MATERIAL BEHAELT.
     *
     * Jede Flaeche eines Parkplatzes verweist auf einen unserer Klone, nicht
     * auf das Vorbild. Ein Spielstand merkt sich diesen Verweis ueber den
     * Namen. Gibt es den Klon beim Laden nicht, wird das Teil obsolet: Form
     * da, Material weg. Der Tester vom 2026-09-23 schrieb dazu *"the grass
     * disappears and it color is way lighter"*; sein Abzug zeigte 124 von
     * 124 Teilen auf zwei nicht aufloesbaren Prefabs.
     *
     * Welche Klone beim Start entstehen, stand bisher NUR in der Liste in
     * den Mod-Einstellungen (`optionen.coc`), nicht im Spielstand. Seit dem
     * Baumfix vom 2026-09-23 laufen auch Vanilla-Gras und -Asphalt ueber
     * Klone. Ein weitergegebener Spielstand, eine Neuinstallation oder
     * zurueckgesetzte Einstellungen haetten damit auch reine
     * Vanilla-Parkplaetze blass gemacht - vorher waren die immun.
     *
     * Zwei Schutzschichten:
     *
     * 1. VANILLA-KLONE BEI JEDEM START, ohne Liste. Die Vorbilder sind im
     *    Hauptmenue da, die Varianten stehen in
     *    `Flaechenklonname.Bauvarianten`. Damit ist Vanilla wieder so sicher
     *    wie vor dem 23.09.
     *
     * 2. REPARATUR NACH DEM LADEN. Ein totes Teil verraet ueber
     *    `PrefabSystem.GetPrefabName` den Namen, den es gesucht hat
     *    (Game.dll: PrefabSystem.GetObsoleteID, gesetzt von
     *    ResolvePrefabsSystem). Daraus liest `Flaechenklonname.TryLese`
     *    Vorbild, Prioritaet und Raeumwirkung. Ist das Vorbild jetzt da,
     *    entsteht der Klon, und das Teil wird umgehaengt. Ist es nicht da,
     *    steht der Name im Log - dann fehlt ein Mod, und retten kann das
     *    niemand.
     *
     * Beim Nutzer selbst lieferte der Start am 2026-09-24 00:15:57 genau
     * diesen zweiten Fall: 7 von 19 gemerkten Vorbildern fehlten, alle aus
     * Mods (G87 Vanilla Asphalt, G87 Road Markings, Urban Decay Pack 2).
     * Die Mods waren laut Modding.log geladen, ihre Flaechen aber in keiner
     * Flaechenliste dieser Sitzung vorhanden - auch nicht im laufenden Spiel.
     */
    public sealed partial class ParkingLotApronPrefabSystem
    {
        private bool _vanillaGesaet;
        private bool _reparaturFaellig;
        private int _reparaturZyklen;
        private EntityQuery _eigeneFlaechen;
        private readonly HashSet<string> _unrettbarGemeldet = new();

        private const int ReparaturZyklenMax = 600;

        private bool _eigeneFlaechenAngelegt;

        /**
         * ALLE Flaechen, nicht nur die mit unserer Teilrelation.
         *
         * Welche Teile die Relation tragen, haengt an unserem Bau; ob ein
         * Teil auf einen toten PLT-Klon zeigt, haengt nur am Namen. Den Klon
         * benutzt niemand ausser uns - eine Flaeche mit totem "PLT ..."
         * Prefab ist also sicher unsere, auch ohne Relation.
         */
        private EntityQuery EigeneFlaechen()
        {
            if (!_eigeneFlaechenAngelegt)
            {
                _eigeneFlaechen = GetEntityQuery(
                    ComponentType.ReadOnly<Game.Areas.Surface>(),
                    ComponentType.ReadOnly<Area>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.Exclude<Deleted>(),
                    ComponentType.Exclude<Temp>());
                _eigeneFlaechenAngelegt = true;
            }
            return _eigeneFlaechen;
        }

        private void SaeheVanillaKlone()
        {
            if (_vanillaGesaet || _flaechenprefabs.IsEmptyIgnoreFilter) return;
            _vanillaGesaet = true;

            var vorbilder = 0;
            using var kandidaten = _flaechenprefabs.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < kandidaten.Length; i++)
            {
                if (!_prefabSystem.TryGetPrefab<SurfacePrefab>(kandidaten[i],
                        out var vorbild) || vorbild == null) continue;
                if (!vorbild.isBuiltin) continue;
                if (!Flaechenklonname.IstMaterial(vorbild.name)) continue;
                vorbilder++;
                foreach (var (aufschlag, raeumt) in Flaechenklonname.Bauvarianten)
                    FordereAn(kandidaten[i], aufschlag, out _, out _, raeumt,
                        merken: false);
            }
            Mod.log.Info("PLT-Flaechenrettung: Vanilla-Klone fuer " + vorbilder
                + " Vorbilder x " + Flaechenklonname.Bauvarianten.Length
                + " Varianten angefordert - unabhaengig von der gemerkten "
                + "Liste, damit ein Vanilla-Parkplatz nie blass laedt.");
        }

        [Preserve]
        protected override void OnGameLoadingComplete(
            Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (mode != GameMode.Game) return;
            _reparaturFaellig = true;
            _reparaturZyklen = 0;
            _unrettbarGemeldet.Clear();
        }

        /**
         * Laeuft nach dem Laden, bis nichts mehr offen ist. Ein Durchgang
         * reicht nicht immer: ein frisch angeforderter Klon ist erst ein paar
         * Zyklen spaeter benutzbar.
         */
        private void RetteToteFlaechen()
        {
            if (!_reparaturFaellig || !_gesaet) return;

            var abfrage = EigeneFlaechen();
            using var teile = abfrage.ToEntityArray(Allocator.Temp);

            // Totes Prefab -> seine Teile. Ein Klon stirbt immer als Ganzes,
            // also genuegt der Blick auf die Prefab-Entity.
            var tot = new Dictionary<Entity, List<Entity>>();
            for (var i = 0; i < teile.Length; i++)
            {
                var prefab = EntityManager.GetComponentData<PrefabRef>(teile[i]).m_Prefab;
                if (!IstTot(prefab)) continue;
                if (!tot.TryGetValue(prefab, out var liste))
                    tot[prefab] = liste = new List<Entity>();
                liste.Add(teile[i]);
            }

            if (tot.Count == 0)
            {
                if (_reparaturZyklen > 0)
                    Mod.log.Info("PLT-Flaechenrettung: alle Flaechen haben "
                        + "wieder ein Material (nach " + _reparaturZyklen
                        + " Zyklen).");
                _reparaturFaellig = false;
                return;
            }

            var umgehaengt = 0;
            var offen = 0;
            foreach (var paar in tot)
            {
                var gesucht = _prefabSystem.GetPrefabName(paar.Key);
                // Fremde tote Flaechen gehoeren nicht uns; die meldet CS2 selbst.
                if (!Flaechenklonname.IstEigener(gesucht)) continue;
                if (!Flaechenklonname.TryLese(gesucht, out var vorbildname,
                        out var aufschlag, out var raeumt))
                {
                    MeldeUnrettbar(gesucht, paar.Value.Count,
                        "Name nicht lesbar");
                    continue;
                }
                var vorbild = SucheVorbild(vorbildname);
                if (vorbild == Entity.Null)
                {
                    MeldeUnrettbar(gesucht, paar.Value.Count,
                        "Vorbild '" + vorbildname + "' gibt es in diesem Spiel "
                        + "nicht - der Mod, der es liefert, ist nicht geladen "
                        + "oder nennt es inzwischen anders");
                    continue;
                }
                var klon = FordereAn(vorbild, aufschlag, out var fehler,
                    out var aufgegeben, raeumt);
                if (fehler || aufgegeben)
                {
                    MeldeUnrettbar(gesucht, paar.Value.Count,
                        "Klon liess sich nicht anlegen");
                    continue;
                }
                if (klon == Entity.Null) { offen += paar.Value.Count; continue; }

                foreach (var teil in paar.Value)
                {
                    EntityManager.SetComponentData(teil, new PrefabRef { m_Prefab = klon });
                    if (!EntityManager.HasComponent<Updated>(teil))
                        EntityManager.AddComponent<Updated>(teil);
                    if (!EntityManager.HasComponent<BatchesUpdated>(teil))
                        EntityManager.AddComponent<BatchesUpdated>(teil);
                }
                umgehaengt += paar.Value.Count;
                Mod.log.Info("PLT-Flaechenrettung: " + paar.Value.Count
                    + " Teil(e) von totem '" + gesucht + "' auf den neuen Klon "
                    + "umgehaengt.");
            }

            _reparaturZyklen++;
            if (offen == 0 || _reparaturZyklen >= ReparaturZyklenMax)
            {
                _reparaturFaellig = false;
                if (offen > 0)
                    Mod.log.Error("PLT-Flaechenrettung: " + offen + " Teil(e) "
                        + "warten nach " + _reparaturZyklen + " Zyklen noch auf "
                        + "ihren Klon und bleiben ohne Material.");
            }
        }

        /**
         * Tot heisst: die Entity ist ein obsoletes Prefab. CS2 legt fuer
         * jede unbekannte Kennung eines Spielstands eine solche Entity an
         * und gibt ihr einen NEGATIVEN Index (PrefabSystem.GetObsoleteID
         * rechnet `-1 - m_Index`).
         */
        private bool IstTot(Entity prefab)
        {
            if (prefab == Entity.Null || !EntityManager.Exists(prefab)) return false;
            if (!EntityManager.HasComponent<PrefabData>(prefab)) return false;
            return EntityManager.GetComponentData<PrefabData>(prefab).m_Index < 0;
        }

        private Entity SucheVorbild(string name)
        {
            using var kandidaten = _flaechenprefabs.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < kandidaten.Length; i++)
                if (_prefabSystem.TryGetPrefab<SurfacePrefab>(kandidaten[i],
                        out var vorbild) && vorbild != null
                    && string.Equals(vorbild.name, name, StringComparison.Ordinal))
                    return kandidaten[i];
            return Entity.Null;
        }

        private void MeldeUnrettbar(string gesucht, int teile, string grund)
        {
            if (!_unrettbarGemeldet.Add(gesucht)) return;
            Mod.log.Error("PLT-Flaechenrettung: " + teile + " Teil(e) suchen '"
                + gesucht + "' und bleiben ohne Material: " + grund + ".");
        }
    }
}
