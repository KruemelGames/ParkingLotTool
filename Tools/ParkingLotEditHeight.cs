using System;
using Game.Simulation;
using Unity.Collections;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private TerrainHeightData _editHeightSnapshot;
        private NativeArray<ushort> _editHeightCells;
        private NativeArray<ushort> _editHeightCellsDownscaled;
        private long _editHeightReadTick;
        private long _editNetRemovalTick;
        private long _editHeightWaitMilliseconds;

        /**
         * HOEHEN DER ALTEN KNOTEN, NACH LAGE.
         *
         * Nutzer, 2026-09-24: *"Es wird immer noch leicht die Hoehe der Gasse
         * veraendert."* Gemessen an Zufahrt 17: Bau 372,93 m, Edit ohne
         * Aenderung 372,69 m.
         *
         * Ursache: der Neubau las die Hoehe aus dem Gelaende - und das hat
         * die alte Gasse selbst planiert. CS2 haelt das unveraenderte
         * Grundgelaende nur auf der Grafikkarte (`TerrainSystem.m_Heightmap`);
         * auf der CPU liegt allein die Kopie MIT allen Planierungen
         * (`m_CPUHeights`, 3,5-m-Zellen). Am Ende des planierten Streifens
         * mischt die Abfrage planierte und unplanierte Zellen, und jeder
         * Edit liest ein Stueck tiefer.
         *
         * CS2s eigenes Vorbild ist das Aufwerten einer Strasse: die
         * Geometrie bleibt stehen, es wird nicht neu gemessen. Genau so hier -
         * liegt ein neuer Knoten dort, wo ein alter lag, bekommt er dessen
         * Hoehe. Neu gemessen wird nur, was sich wirklich verschoben hat.
         * Schluessel wie `MerkeHoehe`: 2,5-cm-Raster.
         */
        private readonly System.Collections.Generic.Dictionary<(long, long), float>
            _altknotenhoehen = new();

        private void MerkeAltknotenhoehe(Unity.Entities.Entity knoten)
        {
            if (knoten == Unity.Entities.Entity.Null
                || !EntityManager.Exists(knoten)
                || !EntityManager.HasComponent<Game.Net.Node>(knoten)) return;
            var lage = EntityManager.GetComponentData<Game.Net.Node>(knoten).m_Position;
            if (float.IsNaN(lage.y) || float.IsInfinity(lage.y)) return;
            var schluessel = ((long)Unity.Mathematics.math.round(lage.x * 40f),
                              (long)Unity.Mathematics.math.round(lage.z * 40f));
            _altknotenhoehen[schluessel] = lage.y;
        }

        /**
         * Belegt den Hoehenspeicher des Neubaus vor. Auch die acht
         * Nachbarzellen: CS2 legt Knoten nicht immer auf die exakt
         * angeforderte Stelle, und ein Knoten zwei Zentimeter daneben ist
         * derselbe Knoten. Vorhandene Eintraege (z. B. die Strassenhoehe am
         * Gassenanfang) gehen vor.
         */
        private void BelegeHoehenAusAltbestand(
            System.Collections.Generic.Dictionary<(long, long), float> hoehen,
            ref TerrainHeightData gelaende)
        {
            if (_altknotenhoehen.Count == 0) return;
            var belegt = 0;
            var verworfen = 0;
            foreach (var paar in _altknotenhoehen)
            {
                // Eine verdorbene Hoehe wird nicht weitervererbt - sonst
                // bleibt ein einmal versunkener Knoten fuer immer versunken.
                var lage = new Unity.Mathematics.float2(paar.Key.Item1 / 40f,
                    paar.Key.Item2 / 40f);
                if (!AlthoeheGlaubwuerdig(lage, paar.Value, ref gelaende, out var umgebung))
                {
                    verworfen++;
                    Mod.log.Warn("PLT-Edithoehe: alte Knotenhoehe "
                        + paar.Value.ToString("F2") + " m bei " + lage.x.ToString("F1")
                        + "/" + lage.y.ToString("F1") + " verworfen - unberuehrtes "
                        + "Gelaende daneben " + umgebung.ToString("F2") + " m.");
                    continue;
                }
                for (var dx = -1; dx <= 1; dx++)
                    for (var dz = -1; dz <= 1; dz++)
                    {
                        var k = (paar.Key.Item1 + dx, paar.Key.Item2 + dz);
                        if (hoehen.ContainsKey(k)) continue;
                        hoehen[k] = paar.Value;
                        if (dx == 0 && dz == 0) belegt++;
                    }
            }
            Mod.log.Info("PLT-Edithoehe: " + belegt + " Knotenhoehe(n) des "
                + "alten Parkplatzes uebernommen - an unveraenderten Stellen "
                + "wird nicht neu gemessen; " + verworfen + " verworfen, "
                + _altkanten.Count + " alte Kante(n) als Hoehenquelle.");
        }

        private void ErfasseEdithoehenVorAbriss()
        {
            VerwerfeEdithoehen();
            try
            {
                var start = System.Diagnostics.Stopwatch.GetTimestamp();
                var source = _terrainSystem.GetHeightData(waitForPending: true);
                _editHeightReadTick = System.Diagnostics.Stopwatch.GetTimestamp();
                _editHeightWaitMilliseconds = (long)((_editHeightReadTick - start)
                    * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                _editHeightCells = new NativeArray<ushort>(
                    source.heights.Length, Allocator.Persistent);
                NativeArray<ushort>.Copy(source.heights, _editHeightCells);
                if (source.downscaledHeights.IsCreated)
                {
                    _editHeightCellsDownscaled = new NativeArray<ushort>(
                        source.downscaledHeights.Length, Allocator.Persistent);
                    NativeArray<ushort>.Copy(source.downscaledHeights,
                        _editHeightCellsDownscaled);
                }
                _editHeightSnapshot = new TerrainHeightData(
                    _editHeightCells, _editHeightCellsDownscaled,
                    source.resolution, source.scale, source.offset,
                    source.hasBackdrop);
            }
            catch (Exception exception)
            {
                VerwerfeEdithoehen();
                Mod.log.Warn("PLT-Edithoehe: Hoehenkarte vor Abriss nicht "
                    + "gesichert; Neubau liest die aktuelle Karte: "
                    + exception.Message);
            }
        }

        private void VerwerfeEdithoehen()
        {
            _altknotenhoehen.Clear();
            _altkanten.Clear();
            _verworfeneAlthoehen = 0;
            _editHeightSnapshot = default;
            if (_editHeightCells.IsCreated) _editHeightCells.Dispose();
            if (_editHeightCellsDownscaled.IsCreated)
                _editHeightCellsDownscaled.Dispose();
            _editHeightCells = default;
            _editHeightCellsDownscaled = default;
            _editHeightReadTick = 0;
            _editNetRemovalTick = 0;
            _editHeightWaitMilliseconds = 0;
        }
    }
}
