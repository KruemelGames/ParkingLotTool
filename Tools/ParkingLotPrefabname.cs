using System;
using System.Text;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    /**
     * PREFAB ALS ID STATT ALS ROHE ENTITY (Issue #17, 2026-10-15).
     *
     * Der Rueckweg-Schnappschuss speichert Prefabs als Entity. Das ueberlebt
     * das Laden nur, wenn CS2 das Prefab selbst in den Spielstand schreibt
     * und `ParkingLotPrefabVerweiseSystem` den Platzhalter umbiegt. Ein mit
     * 1.0.10 gespeicherter Spielstand kam mit LEEREN Verweisen zurueck ("0
     * geprueft"), umzubiegen gab es nichts, der Rueckweg blieb fuer immer
     * gesperrt. Die PrefabID ist dagegen vom Speichern und von Entity-
     * Indizes unabhaengig; beim Laden wird zuerst sie aufgeloest.
     *
     * Gespeichert wird `PrefabID.ToUrlSegment()` = "Typ/Name[/Hash]", jeder
     * Teil URL-kodiert. Der Hash gehoert dazu: `PrefabSystem` registriert
     * Prefabs mit Asset unter Typ+Name+Hash, eine Suche ohne Hash findet sie
     * nicht (`PrefabID.Equals` vergleicht alle drei).
     */
    internal static class ParkingLotPrefabname
    {
        internal static FixedString512Bytes Von(EntityManager em, Entity prefab)
        {
            var prefabs = em.World.GetExistingSystemManaged<PrefabSystem>();
            if (prefabs == null || prefab == Entity.Null || !em.Exists(prefab)
                || !em.HasComponent<PrefabData>(prefab)
                || !prefabs.TryGetPrefab<PrefabBase>(prefab, out var p) || p == null)
                return default;
            return Fest(p.GetPrefabID().ToUrlSegment());
        }

        /** Entity.Null, wenn keine ID gespeichert ist oder das Prefab fehlt. */
        internal static Entity Aufloesen(EntityManager em, FixedString512Bytes id)
        {
            if (id.IsEmpty) return Entity.Null;
            var prefabs = em.World.GetExistingSystemManaged<PrefabSystem>();
            if (prefabs == null) return Entity.Null;
            var teile = id.ToString().Split('/');
            if (teile.Length < 2 || teile.Length > 3) return Entity.Null;
            var typ = Uri.UnescapeDataString(teile[0]);
            var name = Uri.UnescapeDataString(teile[1]);
            var hash = default(Colossal.Hash128);
            if (teile.Length == 3 && !Colossal.Hash128.TryParse(Uri.UnescapeDataString(teile[2]), out hash))
                return Entity.Null;
            return prefabs.TryGetPrefab(new PrefabID(typ, name, hash), out var p) && p != null
                && prefabs.TryGetEntity(p, out var e) ? e : Entity.Null;
        }

        /** Zu lange IDs bleiben leer statt abgeschnitten: eine falsche waere schlimmer als keine. */
        internal static FixedString512Bytes Fest(string text)
        {
            if (string.IsNullOrEmpty(text)
                || Encoding.UTF8.GetByteCount(text) > FixedString512Bytes.UTF8MaxLengthInBytes)
                return default;
            return new FixedString512Bytes(text);
        }
    }
}
