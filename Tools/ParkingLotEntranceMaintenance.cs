using System.Collections.Generic;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /** Haelt Zufahrten bei Polygon- und Massänderungen auf ihrer bisherigen Seite. */
    public sealed partial class ParkingLotToolSystem
    {
        /** Vor einer Punktbewegung bleibt die Weltlage jeder Zufahrt erhalten. */
        private void CaptureEntrancesForPointDrag()
        {
            _entrancePositionsBeforePointDrag = CaptureEntrancePositions();
        }

        private float2[] CaptureEntrancePositions()
        {
            var positions = new float2[_entrances.Count];
            for (var i = 0; i < _entrances.Count; i++)
                positions[i] = TryEntranceBoundary(_entrances[i], out var gate)
                    ? gate.Point : new float2(float.NaN, float.NaN);
            return positions;
        }

        private void ReprojectEntrancesAfterPointDrag()
        {
            var previous = _entrancePositionsBeforePointDrag;
            _entrancePositionsBeforePointDrag = null;
            if (previous == null || previous.Length == 0 || _entrances.Count == 0)
                return;

            if (!RebuildEntrancesAtPositions(previous, CurrentEntranceSettings(),
                out var removed)) return;
            if (removed > 0)
                Mod.log.Info($"PLT-Polygonaenderung: {removed} Zufahrt(en) "
                    + "passten auf ihrer bisherigen Seite nicht mehr und wurden entfernt.");
            PublishEntranceState();
        }

        /**
         * Ein geaendertes Fahrgassenmass aendert auch die 13,0-m-Spanne aus
         * 7,0 m Zufahrt und zwei 3,0-m-Kapseln. Bereits gesetzte Zufahrten
         * werden deshalb sofort neu geprueft; spaetere Konflikte verschwinden
         * in Setzreihenfolge, statt erst beim Bauen zu ueberlappen.
         */
        private bool RevalidateEntrancesAfterSettingsChange()
        {
            if (_entrances.Count == 0) return false;
            var changed = RebuildEntrancesAtPositions(CaptureEntrancePositions(),
                CurrentEntranceSettings(), out var removed);
            if (!changed) return false;
            if (removed > 0)
                Mod.log.Info($"PLT-Massaenderung: {removed} Zufahrt(en) wegen "
                    + "Kapselabstand oder zu kurzer Seite entfernt.");
            PublishEntranceState();
            return true;
        }

        private bool RebuildEntrancesAtPositions(float2[] previous,
            LayoutSettings settings, out int removed)
        {
            removed = 0;
            var rebuilt = new List<Entrance>(_entrances.Count);
            for (var i = 0; i < _entrances.Count; i++)
            {
                if (i >= previous.Length || !math.all(math.isfinite(previous[i]))
                    || !TryReprojectEntrance(_entrances[i], previous[i], settings,
                        out var next)
                    || !HasEntranceSpacingAgainst(next, rebuilt, -1, settings, out _))
                {
                    removed++;
                    continue;
                }
                rebuilt.Add(next);
            }

            var changed = rebuilt.Count != _entrances.Count;
            if (!changed)
                for (var i = 0; i < rebuilt.Count; i++)
                    if (!SameEntrance(rebuilt[i], _entrances[i]))
                    {
                        changed = true;
                        break;
                    }
            if (!changed) return false;
            _entrances.Clear();
            _entrances.AddRange(rebuilt);
            return true;
        }

        private bool TryReprojectEntrance(Entrance old, float2 previous,
            LayoutSettings settings, out Entrance next)
        {
            next = null;
            if (old == null || _points.Count < 2) return false;

            /*
             * DIE SEITE WIRD GESUCHT, NICHT ERINNERT.
             *
             * Bis zum 2026-08-31 stand hier `old.Edge` - der Kantenindex von
             * VOR der Aenderung. Solange nur ein Punkt gezogen wird, stimmt
             * er. Sobald aber ein Punkt oder eine Kante WEGFAELLT, rutschen
             * alle folgenden Indizes nach, und Kante 5 ist danach eine ganz
             * andere Seite des Polygons. Die Zufahrt wurde dann auf diese
             * fremde Seite geklemmt - der Nutzer meldete am 2026-08-31, die
             * Zu-, Ein- und Ausfahrten wuerden beim Bearbeiten "enorm
             * verschoben". Seit eine geloeschte Kante ZWEI Punkte entfernt,
             * verschiebt sich der Index sogar um zwei, was es deutlich
             * sichtbarer macht.
             *
             * Der Index ist die falsche Identitaet. Erinnert wird die
             * WELTPOSITION - die hat sich durch das Umnummerieren nicht
             * bewegt. Gesucht wird deshalb die Kante, die dieser Position am
             * naechsten liegt, ueber ALLE Kanten des aktuellen Polygons.
             *
             * Es gibt hier bewusst KEINE Entfernungsschranke. Ob eine Zufahrt
             * an ihrem neuen Platz ueberhaupt zulaessig ist, entscheiden
             * weiterhin die vorhandenen Pruefungen (Randmasse und
             * Kapselabstand); wer dort durchfaellt, wird entfernt. Eine
             * zusaetzliche Metergrenze waere eine geratene Zahl.
             */
            var edge = -1;
            var besterAbstand = float.PositiveInfinity;
            for (var i = 0; i < _points.Count; i++)
            {
                var von = _points[i];
                var nach = _points[(i + 1) % _points.Count];
                var richtung = nach - von;
                var laenge = math.length(richtung);
                if (laenge < EntranceLineEpsilon) continue;
                var t = math.clamp(math.dot(previous - von, richtung / laenge),
                    0f, laenge);
                var abstand = math.distance(previous, von + richtung / laenge * t);
                if (abstand >= besterAbstand) continue;
                besterAbstand = abstand;
                edge = i;
            }

            if (edge < 0) return false;
            if (edge != old.Edge)
                Mod.log.Info("PLT-Zugang: " + old.Art + " von Kante "
                    + old.Edge + " auf Kante " + edge + " uebernommen, "
                    + besterAbstand.ToString("F2",
                        System.Globalization.CultureInfo.InvariantCulture)
                    + " m von ihrer bisherigen Stelle.");

            var a = _points[edge];
            var b = _points[(edge + 1) % _points.Count];
            var vector = b - a;
            var length = math.length(vector);
            if (length < EntranceLineEpsilon) return false;

            var tangent = vector / length;
            /*
             * KOPIEREN, NICHT NEU BAUEN.
             *
             * Hier stand `new Entrance { Edge, Corner }`. Damit fiel die ART
             * jedes Mal auf ihren Standardwert zurueck - und diese Funktion
             * laeuft nach JEDER Einstellungsaenderung. Der Nutzer meldete am
             * 2026-08-28: nach dem Umlegen eines Schalters sahen alle
             * Zugaenge wie Zufahrten aus, in Form und Farbe. Ein Fussweg war
             * damit lautlos eine Zufahrt geworden.
             *
             * `Clone` kopiert alle Felder. Ein spaeter hinzugefuegtes Feld
             * kann hier deshalb nicht mehr verlorengehen - genau daran ist
             * die Art gescheitert.
             */
            next = old.Clone();
            next.Edge = edge;
            var atStart = old.Corner == "start";
            if (old.Corner != null
                && ParkingGeometry.TryEntranceCornerPlacement(_points.ToArray(),
                    settings, edge, atStart, out var fit))
            {
                next.Along = fit.Along;
                return true;
            }
            if (!TryNormalEntranceBounds(edge, settings,
                out var minimum, out var maximum)) return false;
            next.Corner = null;
            next.Along = math.clamp(math.dot(previous - a, tangent), minimum, maximum);
            return true;
        }

        /** Beim Entfernen der letzten Ecke verschwinden nur ihre beiden Seiten. */
        private void DropEntrancesAtRemovedLastPoint(int oldPointCount)
        {
            if (oldPointCount < 2 || _entrances.Count == 0) return;
            var firstRemovedEdge = oldPointCount - 2;
            var removed = _entrances.RemoveAll(entrance =>
                entrance == null || entrance.Edge >= firstRemovedEdge);
            if (removed > 0)
                Mod.log.Info($"PLT-Polygonecke entfernt: {removed} Zufahrt(en) "
                    + "der weggefallenen Seiten entfernt, keine neu zugeordnet.");
            PublishEntranceState();
        }
    }
}
