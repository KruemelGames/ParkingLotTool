using System.Collections.Generic;
using Game.Common;
using Game.Tools;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    /**
     * NEUBAU BESTEHENDER PARKPLAETZE UEBER DEN BEARBEITEN-WEG.
     *
     * Manche Korrekturen stecken in der Art, wie Netze GEBAUT werden - an
     * fertigen Kanten nachtraeglich Prefab oder Hoehe umzuschreiben ist genau
     * das Direktschreiben, das CS2 am 2026-09-25 sechsmal nativ abstuerzen
     * liess. Der Sync baut solche Parkplaetze deshalb so neu, wie es der
     * Nutzer von Hand taete: Bearbeiten, Uebernehmen. Das nimmt jeden
     * erprobten Weg mit - Bauzettel, Abriss der alten Teile, Nacharbeit,
     * Leitungen - und setzt alle aktuellen Bauregeln um.
     *
     * Einer nach dem anderen. Gestartet wird nur, wenn nichts anderes laeuft
     * (kein Bearbeiten, kein Bau, kein Abriss, keine Leitungen).
     */
    public sealed partial class ParkingLotToolSystem
    {
        private enum NachbauPhase { Frei, WarteAufBearbeiten, WarteAufBau }

        private readonly List<Entity> _nachbauAuftraege = new();
        private NachbauPhase _nachbauPhase;
        private Entity _nachbauLot;
        private int _nachbauSeit;
        private int _nachbauFertig;
        private const int NachbauFrist = 1800;

        /** Vom Sync: diesen Parkplatz ueber den Bearbeiten-Weg neu bauen. */
        internal void PlaneNachbau(Entity lot)
        {
            if (lot == Entity.Null || _nachbauAuftraege.Contains(lot) || _nachbauLot == lot)
                return;
            _nachbauAuftraege.Add(lot);
            Mod.log.Info($"PLT-Nachbau: Parkplatz {lot} vorgemerkt "
                + $"({_nachbauAuftraege.Count} in der Warteschlange).");
        }

        internal int NachbauOffen => _nachbauAuftraege.Count + (_nachbauLot != Entity.Null ? 1 : 0);

        /** Je Bild, mit offenem und geschlossenem Werkzeug. */
        private void PflegeSyncNachbau()
        {
            var bild = UnityEngine.Time.frameCount;
            switch (_nachbauPhase)
            {
                case NachbauPhase.Frei:
                {
                    if (_nachbauAuftraege.Count == 0) return;
                    if (IsEditing || _pendingEditLot != Entity.Null || _buildTask != null
                        || _buildStage != BuildStage.Idle || _avPhase != AvPhase.Idle) return;
                    // Die Nacharbeit des vorigen Baus (Seiten, Namen, Haltestellen)
                    // muss durch sein - sonst trifft sie den naechsten Umbau.
                    if (_zoningSeitenFrames > 0 || _zoningBlockFrames > 0
                        || _pendingBusStops.Length > 0) return;
                    _aufraeumer ??= World.GetExistingSystemManaged<ParkingLotCleanupSystem>();
                    if (_aufraeumer != null && _aufraeumer.AbrissLaeuft) return;

                    var lot = _nachbauAuftraege[0];
                    _nachbauAuftraege.RemoveAt(0);
                    if (!EntityManager.Exists(lot) || EntityManager.HasComponent<Deleted>(lot)
                        || !HasCompleteBuildReceipt(lot))
                    {
                        Mod.log.Warn($"PLT-Nachbau: Parkplatz {lot} uebersprungen - "
                            + "nicht mehr da oder ohne vollstaendigen Bauzettel.");
                        return;
                    }
                    _nachbauLot = lot;
                    _nachbauSeit = bild;
                    _nachbauPhase = NachbauPhase.WarteAufBearbeiten;
                    RequestEdit(lot);
                    ParkingLotSchrittmarke.Setze("Nachbau: Bearbeiten " + lot);
                    return;
                }
                case NachbauPhase.WarteAufBearbeiten:
                    if (IsEditing)
                    {
                        // Derselbe Weg wie der Bauknopf: der Bau wartet selbst
                        // auf die fertige Vorschau.
                        RequestBuildFromPanel();
                        _nachbauPhase = NachbauPhase.WarteAufBau;
                        _nachbauSeit = bild;
                        ParkingLotSchrittmarke.Setze("Nachbau: Uebernehmen " + _nachbauLot);
                        return;
                    }
                    if (bild - _nachbauSeit > NachbauFrist) BrichNachbauAb("Bearbeiten begann nicht");
                    return;
                case NachbauPhase.WarteAufBau:
                    if (!IsEditing)
                    {
                        _nachbauFertig++;
                        Mod.log.Info($"PLT-Nachbau: Parkplatz {_nachbauLot} neu gebaut "
                            + $"({_nachbauFertig} fertig, {_nachbauAuftraege.Count} offen).");
                        _nachbauLot = Entity.Null;
                        _nachbauPhase = NachbauPhase.Frei;
                        if (_nachbauAuftraege.Count == 0
                            && m_ToolSystem.activeTool == this)
                            m_ToolSystem.activeTool = m_DefaultToolSystem;
                        return;
                    }
                    if (bild - _nachbauSeit > NachbauFrist) BrichNachbauAb("Uebernehmen lief nicht durch");
                    return;
            }
        }

        private void BrichNachbauAb(string grund)
        {
            Mod.log.Warn($"PLT-Nachbau: Parkplatz {_nachbauLot} abgebrochen - {grund} "
                + $"nach {NachbauFrist} Bildern. Er bleibt, wie er war.");
            // Abbruch stellt das alte Lot wieder her - wie Esc im Bearbeiten.
            AbortEdit("Nachbau: " + grund,
                "Automatischer Neubau abgebrochen; der Parkplatz bleibt, wie er war.",
                "Automatic rebuild cancelled; the parking lot stays as it was.");
            _nachbauLot = Entity.Null;
            _nachbauPhase = NachbauPhase.Frei;
        }
    }
}
