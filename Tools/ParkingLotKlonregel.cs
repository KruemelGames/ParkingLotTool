using Game.Prefabs;

namespace ParkingLotTool.Tools
{
    /**
     * WAS EIN PREFAB-KLON VON SEINEM VANILLA-VORBILD ERBEN DARF - an einer
     * Stelle fuer alle Klone.
     *
     * `ObsoleteIdentifiers` NIE. Damit meldet ein Prefab "ich ersetze diese
     * alten Prefab-Kennungen" an (`PrefabSystem.AddPrefab` traegt jede davon
     * in `m_PrefabIndices` ein, wenn sie noch frei ist). Unsere Klone erbten
     * die Liste bis 2026-09-26 mit - zwoelf "Duplicate prefab ID"-Warnungen
     * im Player.log, z. B. "Invisible Cargo Loading Path (PLT Invisible
     * Pedestrian Path)". Weil das Vorbild immer zuerst angemeldet ist, gewann
     * bisher Vanilla; ein Klon, der vor seinem Vorbild entstuende, wuerde
     * alte Spielstaende aber auf UNS aufloesen. Der Nutzer hat genau davor
     * gewarnt: "dass CS2 unser Prefab laden koennte als Vanilla".
     *
     * `UIObject` nur, wenn der Klon ausdruecklich waehlbar sein soll - sonst
     * steht er in CS2s Menues (Strassenmenue, Flaechenliste).
     */
    internal static class ParkingLotKlonregel
    {
        internal static bool Erben(ComponentBase bauteil, bool mitUI = false)
            => bauteil != null
               && !(bauteil is ObsoleteIdentifiers)
               && (mitUI || !(bauteil is UIObject));
    }
}
