using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Prefabs;
using UnityEngine;

namespace ParkingLotTool.Tools
{
    /**
     * Eigene Section/Piece/Lane/Car-Pathfind-Kette je Netz. Auch LOD-Pieces
     * klonen: MeshRef.Equals vergleicht m_Piece-Entities (Game.Prefabs.
     * NetCompositionMeshRefSystem:381), ebenfalls nach LOD-Ersetzung:238.
     * Die unveraenderten Geometry-/Surface-Assets bleiben nur lesbare Quellen.
     * Keine PrefabBase-/ComponentBase-Buchhaltung oder Render-Laufzeitcaches
     * kopieren (Speicherabsturz 2026-09-02 durch fremden prefab-Rueckverweis).
     */
    internal sealed class ParkingLotFahrprefabKopie
    {
        private readonly string _netz;
        private readonly Action<PrefabBase, PrefabBase> _anmelden;
        private readonly Dictionary<PrefabBase, PrefabBase> _prefabs = new();
        private readonly Dictionary<object, object> _werte = new();
        private readonly bool _autoregeln;
        private readonly Action<PrefabBase> _konfiguriere;

        internal ParkingLotFahrprefabKopie(string netz,
            Action<PrefabBase, PrefabBase> anmelden, bool autoregeln = true,
            Action<PrefabBase> konfiguriere = null)
        {
            _netz = netz;
            _anmelden = anmelden;
            _autoregeln = autoregeln;
            _konfiguriere = konfiguriere;
        }

        /** Nur vor AddPrefab des Wurzelnetzes aufrufen, in PrefabUpdate. */
        internal void Isoliere(NetGeometryPrefab netz)
        {
            KopiereFelder(netz, netz, typeof(PrefabBase));
            foreach (var komponente in netz.components)
                KopiereFelder(komponente, komponente, typeof(ComponentBase));
            Verstecke(netz);
            if (_autoregeln && netz is RoadPrefab road) road.m_SpeedLimit = ParkingLotFahrregeln.TempoKmh;
            if (_autoregeln && netz is PathwayPrefab path) path.m_SpeedLimit = ParkingLotFahrregeln.TempoKmh;
            _konfiguriere?.Invoke(netz);
        }

        private PrefabBase Klone(PrefabBase quelle)
        {
            if (_prefabs.TryGetValue(quelle, out var vorhanden)) return vorhanden;
            var klon = (PrefabBase)ScriptableObject.CreateInstance(quelle.GetType());
            klon.name = ParkingLotFahrregeln.Teilname(_netz, quelle);
            _prefabs.Add(quelle, klon); // Vor Rekursion: Sektionen/SecondaryLanes koennen Zyklen haben.
            KopiereFelder(quelle, klon, typeof(PrefabBase));
            foreach (var komponente in quelle.components)
            {
                if (!Erben(komponente)) continue;
                var kopie = klon.AddComponentFrom(komponente);
                KopiereFelder(komponente, kopie, typeof(ComponentBase));
            }
            var auto = klon.GetComponent<CarPathfind>();
            if (_autoregeln && auto != null) auto.m_DrivingCost = ParkingLotFahrregeln.Fahrkosten;
            Verstecke(klon);
            _konfiguriere?.Invoke(klon);
            _anmelden(quelle, klon);
            return klon;
        }

        internal static bool Erben(ComponentBase komponente)
            => ParkingLotKlonregel.Erben(komponente)
               && !(komponente is SpawnableArea)
               && !(komponente is SpawnableObject)
               && !(komponente is SpawnableLane)
               && !(komponente is SpawnableBuilding)
               && !(komponente is PlaceholderArea)
               && !(komponente is PlaceholderObject)
               && !(komponente is PlaceholderLane)
               && !(komponente is PlaceholderBuilding)
               && !(komponente is AssetPackItem)
               && !(komponente is EditorAssetCategoryOverride);

        /**
         * Find It 77240_58 indiziert Road/Pathway OHNE UIObject. Sein Indexer
         * (307-315) liest TryGet und verlangt eine nichtleere Include-Liste,
         * bevor er Exclude auswertet. CS2 PrefabBase.TryGet ignoriert active;
         * GetComponents/TryGet(List) dagegen filtert active (340). Daher NUR
         * inaktive Metadaten: Find It sieht den Ausschluss, CS2 erzeugt KEINE
         * EditorAssetCategoryOverrideData und keine Editor-Kategorieeintraege.
         */
        internal static void Verstecke(PrefabBase klon)
        {
            var ausschluss = klon.AddOrGetComponent<EditorAssetCategoryOverride>();
            ausschluss.active = false;
            ausschluss.m_IncludeCategories = new[] { "FindIt" };
            ausschluss.m_ExcludeCategories = new[] { "FindIt" };
        }

        private void KopiereFelder(object quelle, object ziel, Type grenze)
        {
            foreach (var feld in Felder(quelle.GetType(), grenze))
                feld.SetValue(ziel, KopiereWert(feld.GetValue(quelle)));
        }

        private object KopiereWert(object wert)
        {
            if (wert == null) return null;
            if (wert is PrefabBase prefab)
            {
                // Nicht nur direkte Fahrspuren: SecondaryLanes, NetLanes und
                // LOD-RenderPrefabs koennen weitere Spuren/Pieces referenzieren.
                if (prefab is NetSectionPrefab || prefab is RenderPrefab
                    || prefab is NetLanePrefab
                    || prefab is PathfindPrefab && prefab.GetComponent<CarPathfind>() != null)
                    return Klone(prefab);
                return prefab; // Aggregate, Zonenblock, Fuss-Pathfind: nur lesen.
            }
            if (wert is UnityEngine.Object) return wert;
            var typ = wert.GetType();
            if (typ.IsPrimitive || typ.IsEnum || wert is string) return wert;
            if (_werte.TryGetValue(wert, out var vorher)) return vorher;
            if (wert is Array array)
            {
                var kopie = (Array)array.Clone();
                _werte.Add(wert, kopie);
                for (var i = 0; i < array.Length; i++) kopie.SetValue(KopiereWert(array.GetValue(i)), i);
                return kopie;
            }
            // Nur serialisierte Game-Infoobjekte kopieren, keine AssetDatabase-
            // Handles. Diese werden weder hier noch spaeter veraendert.
            if (typ.Namespace != "Game.Prefabs") return wert;
            var neu = Activator.CreateInstance(typ);
            _werte.Add(wert, neu);
            KopiereFelder(wert, neu, typeof(object));
            return neu;
        }

        private static IEnumerable<FieldInfo> Felder(Type typ, Type grenze)
        {
            for (; typ != null && typ != grenze; typ = typ.BaseType)
                foreach (var feld in typ.GetFields(BindingFlags.Instance | BindingFlags.Public
                    | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (!feld.IsInitOnly && !feld.IsLiteral && !feld.IsNotSerialized
                        && (feld.IsPublic || feld.IsDefined(typeof(SerializeField), false)))
                        yield return feld;
        }
    }
}
