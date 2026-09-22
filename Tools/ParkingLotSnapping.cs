using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Simulation;
using Game.Tools;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * Einrasten beim Zeichnen des Polygons.
     *
     * WOZU FANGEN UEBERHAUPT GUT IST
     *
     * Nicht dazu, den Cursor irgendwo festzukleben. Sondern dazu, eine
     * BEDINGUNG durchzusetzen, die man von Hand nicht trifft: "genau am
     * Bordstein", "genau rechtwinklig", "genau an der Nachbarkante".
     *
     * Und der entscheidende Punkt, den ich beim ersten Mal uebersehen habe:
     * an einer Ecke gelten meistens ZWEI Bedingungen gleichzeitig. Die
     * vierte Ecke eines Parkplatzes an der Strasse muss auf dem Bordstein
     * liegen UND im rechten Winkel zur dritten Kante stehen. Wer da nur eine
     * von beiden erfuellt, bekommt kein Rechteck, sondern ein Trapez - und
     * genau das ist passiert: der Fahrbahnfang hat gewonnen, der Winkel war
     * weg, und die letzte Ecke musste nach Augenmass gesetzt werden.
     *
     * ZWEI BEDINGUNGEN GLEICHZEITIG heisst geometrisch: der SCHNITTPUNKT
     * zweier Geraden. Vanilla macht genau das, in `ToolUtils.AddSnapLine` -
     * jede Fangart legt nicht nur einen Punkt vor, sondern auch ihre GERADE,
     * und jedes Paar sich schneidender Geraden ergibt einen zusaetzlichen
     * Vorschlag. Der bekommt die hoehere der beiden Stufen und das doppelte
     * Gewicht (`priority 2f` statt `1f`), schlaegt also jede der beiden
     * einzelnen Bedingungen. Beim ersten Anlauf hatte ich nur die eine
     * Haelfte portiert (`AddSnapPosition`) und diese hier weggelassen.
     *
     * VIER FANGARTEN, dieselben, die das Vanilla-Arealwerkzeug fuer
     * Lot-Flaechen anbietet (`AreaToolSystem.GetAvailableSnapMask`, Fall
     * `AreaType.Lot`, ausserhalb des Editors):
     *
     *   NetSide           - Fahrbahnkante
     *   ObjectSide        - Gebaeudekante
     *   ExistingGeometry  - Kanten und Ecken bestehender Grundstuecke
     *   StraightDirection - Achsenkreuz: laengs UND quer zu einer Bezugsachse
     *
     * Die Bezugsachse eines Punktes ist das, woran er selbst eingerastet ist
     * (`_pointAxes`). Wer den ersten Punkt an den Bordstein setzt, rechnet
     * ab dem zweiten mit der Fahrtrichtung der Strasse als Achse.
     */
    public sealed partial class ParkingLotToolSystem
    {
        /** Wie nah der Cursor an ein Fangziel muss. Wie Vanilla: 8 m. */
        private const float SnapDistance = 8f;

        /**
         * STUFEN. Erste Zahl der Rangfolge; sie schlaegt das Gewicht immer.
         * Aus dem Vanilla-Arealwerkzeug uebernommen, damit sich der Fang
         * anfuehlt wie ueberall sonst im Spiel:
         *   Kanten bestehender Grundstuecke   2   (AreaIterator.CheckLine)
         *   Fahrbahn- und Gebaeudekante       1   (SnapEdgeCurve, CheckLine)
         *   Achsenkreuz                       0   (FindControlPoint)
         * Ein Schnittpunkt erbt die hoehere Stufe seiner beiden Geraden.
         */
        private const float LevelDirection = 0f;
        // Hilfslinien und Zellenraster sind gedachte Linien wie das
        // Achsenkreuz - dieselbe Stufe, eine echte Kante schlaegt sie.
        private const float LevelGuide = 0f;
        private const float LevelZoneGrid = 0f;
        private const float LevelNet = 1f;
        private const float LevelObject = 1f;
        private const float LevelArea = 2f;

        /** Gewicht einer einzelnen Bedingung und eines Schnittpunkts. */
        private const float WeightSingle = 1f;
        private const float WeightCrossing = 2f;

        /** Laenge der eingeblendeten Hilfslinie je Seite. */
        private const float GuideLength = 40f;

        internal enum SnapKind
        {
            None, RoadEdge, ObjectSide, AreaEdge, Direction, Guide, ZoneGrid,
            Crossing,
        }

        private struct SnapCandidate
        {
            public float3 Position;
            /** Tangente am Fangziel - wird die Bezugsachse des Punktes. */
            public float2 Direction;
            /** (Stufe, Guete) - genau wie `ControlPoint.m_SnapPriority`. */
            public float2 Priority;
            public SnapKind Kind;
            /** Gedachte Achse im Spiel? Dann braucht sie eine Hilfslinie. */
            public bool HasGuide;
            public float2 GuideDirection;
        }

        /**
         * Eine Bedingung als Gerade: "der Punkt liegt irgendwo auf dieser
         * Linie". Erst das Schneiden zweier solcher Geraden macht aus zwei
         * halben Bedingungen eine ganze Ecke.
         */
        private struct SnapLine
        {
            public float3 Position;
            public float2 Direction;
            public float Level;
            public bool IsGuide;
        }

        /** Im Feld gehalten, damit nicht jeder Frame eine Liste anlegt. */
        private readonly List<SnapLine> _snapLines = new List<SnapLine>();

        /**
         * Die Bezugsachse je gesetztem Punkt, parallel zu `_points`.
         * Null-Vektor heisst: dieser Punkt hat an nichts eingerastet.
         */
        private readonly List<float2> _pointAxes = new List<float2>();
        private float2 _hoverAxis;
        private float2 _dragStartAxis;

        /** Zuletzt benutzte Fangart - fuer den Cursorring im Overlay. */
        internal SnapKind LastSnap { get; private set; }
        /** Hilfslinie des Achsenfangs; ohne sie sieht man die Achse nicht. */
        internal bool HasSnapGuide { get; private set; }
        internal Line3.Segment SnapGuide { get; private set; }

        /**
         * Meldet dem Spiel, welche Fangarten wir koennen - und bekommt dafuer
         * das MAGNET-PANEL der Vanilla-Werkzeugleiste geschenkt.
         *
         * `Game.UI.InGame.ToolUISystem` ruft genau diese Methode am aktiven
         * Werkzeug auf und baut daraus die Schalter; umgekehrt schreibt es die
         * Auswahl nach `selectedSnap` zurueck. Es braucht also kein eigenes
         * UI, nur diese Ueberschreibung.
         *
         * Jedes Flag steht in ON und in OFF: nur was in BEIDEN steht, wird als
         * abschaltbarer Schalter angezeigt (`GetActualSnap` rechnet
         * `(selectedSnap | ~offMask) & onMask`).
         */
        private Snap _fangwahl = Snap.All;

        /**
         * GESPEICHERT WIRD NUR, SOLANGE DAS WERKZEUG LAEUFT.
         *
         * Das ist der Kern der Sache. `selectedSnap` wird nicht nur vom
         * Magnet-Panel geschrieben, sondern auch vom Spiel selbst - einmal
         * in `ToolBaseSystem.OnCreate` und noch einmal kurz danach, beide
         * Male mit `Snap.All`. Beim zweiten Mal waren die Einstellungen
         * schon geladen, und dieser Griff hat die gemerkte Auswahl
         * ueberschrieben. Im Log stand es klar:
         *
         *   19:52:12,412  Fangauswahl geladen: noch nie gesetzt
         *   19:52:12,429  Fangauswahl gespeichert: -1 (All)
         *
         * Beides passierte beim Modstart, lange bevor der Nutzer das
         * Werkzeug ueberhaupt geoeffnet hatte.
         *
         * Ein Griff des Nutzers kommt dagegen immer ueber das Magnet-Panel,
         * und das gibt es nur am AKTIVEN Werkzeug. Damit ist die Trennung
         * eindeutig - ohne raten zu muessen, wer gerade geschrieben hat.
         */
        private bool _fangAktiv;

        /**
         * DIE FANGAUSWAHL UEBERLEBT DEN SPIELSTART.
         *
         * `ToolBaseSystem` setzt `selectedSnap` in seinem `OnCreate` fest auf
         * `Snap.All`. Wer also eine Fangart abschaltet, findet sie beim
         * naechsten Start wieder an - der Nutzer: *"Das resetet sich immer,
         * wenn ich wieder ins Spiel gehe, und das nervt voll."*
         *
         * Deshalb liegt der Wert in unseren Einstellungen und nicht im
         * Spielstand: er ist eine Vorliebe, kein Merkmal einer Stadt.
         *
         * `_fangGeladen` ist der Schutz gegen genau das Zuruecksetzen: bis
         * die Auswahl einmal geladen wurde, wird NICHT gespeichert. Ohne
         * diese Sperre wuerde die Zuweisung aus `OnCreate` den gemerkten
         * Wert bei jedem Start mit `All` ueberschreiben - der Fehler waere
         * derselbe, nur von uns selbst verursacht.
         */
        public override Snap selectedSnap
        {
            get => _fangwahl;
            set
            {
                _fangwahl = value;
                if (!_fangAktiv || Mod.Optionen == null) return;
                if (Mod.Optionen.FangauswahlGesetzt
                    && Mod.Optionen.Fangauswahl == (int)value) return;
                Mod.Optionen.Fangauswahl = (int)value;
                Mod.Optionen.FangauswahlGesetzt = true;
                Mod.Optionen.ApplyAndSave();
                Mod.log.Info($"PLT-Fangauswahl gespeichert: {(int)value} "
                    + $"({value}).");
            }
        }

        /**
         * Holt die gemerkte Auswahl - bei JEDEM Werkzeugstart.
         *
         * Nicht nur einmal: zwischen zwei Starts kann das Spiel
         * `selectedSnap` auf `All` zurueckgesetzt haben, und dann muss der
         * gemerkte Wert wieder darueber. Erst danach wird gespeichert.
         */
        private void LadeFangauswahl()
        {
            _fangAktiv = false;
            if (Mod.Optionen == null) return;
            if (Mod.Optionen.FangauswahlGesetzt)
            {
                _fangwahl = (Snap)Mod.Optionen.Fangauswahl;
                Mod.log.Info($"PLT-Fangauswahl geladen: "
                    + $"{Mod.Optionen.Fangauswahl} ({_fangwahl}).");
            }
            else
            {
                Mod.log.Info("PLT-Fangauswahl: noch nie gesetzt, alles an.");
            }
            // ERST JETZT scharfstellen - die Zeile davor haette sich sonst
            // selbst gespeichert.
            _fangAktiv = true;
            _fangNachschauFrames = 0;
        }

        /** Beim Verlassen des Werkzeugs aufrufen. */
        private void SperreFangauswahl() => _fangAktiv = false;

        /**
         * SCHAUT KURZ NACH DEM START NOCH EINMAL HIN.
         *
         * Die Kette ist an allen vier Gliedern gemessen: der Setter schlaegt
         * an, der Wert steht in `optionen.coc`, er wird beim Werkzeugstart
         * geladen, und die Anzeige liest ihn jeden Frame frisch. Der Nutzer
         * sieht den Knopf am 2026-09-22 trotzdem wieder an.
         *
         * Also die einzige Stelle, die keine Messung hat: was passiert NACH
         * dem Laden. Schreibt jemand `selectedSnap` ohne unseren Setter -
         * etwa CS2 selbst ueber ein Feld -, faellt es hier auf. Stimmt der
         * Wert dagegen noch, liegt es nicht an uns, und die Suche geht in
         * der Oberflaeche weiter.
         */
        private int _fangNachschauFrames = -1;

        private void PflegeFangnachschau()
        {
            if (_fangNachschauFrames < 0) return;
            if (++_fangNachschauFrames < 60) return;
            _fangNachschauFrames = -1;
            var jetzt = (int)selectedSnap;
            var erwartet = Mod.Optionen?.FangauswahlGesetzt == true
                ? Mod.Optionen.Fangauswahl : unchecked((int)Snap.All);
            if (jetzt == erwartet)
            {
                Mod.log.Info("PLT-Fangnachschau: nach 60 Bildern unveraendert "
                    + jetzt + " - der gemerkte Wert haelt.");
                return;
            }
            Mod.log.Warn("PLT-Fangnachschau: der Wert wurde nach dem Laden "
                + "ueberschrieben. Erwartet " + erwartet + ", jetzt " + jetzt
                + ". Jemand schreibt an unserem Setter vorbei.");
        }

        /**
         * Die Fangarten, die dieses Werkzeug wirklich kann.
         *
         * Steht getrennt von `GetAvailableSnapMask`, weil die Methode dem
         * SPIEL antwortet und diese Eigenschaft UNS.
         */
        internal static Snap Fangarten
            => Snap.ExistingGeometry | Snap.StraightDirection
                | Snap.NetSide | Snap.ObjectSide
                | Snap.GuideLines | Snap.ZoneGrid;

        /**
         * MELDET DEM SPIEL ABSICHTLICH NICHTS.
         *
         * Bis zum 2026-09-17 stand hier die echte Maske, und dafuer schenkte
         * uns CS2 sein Magnet-Panel unten links. Seit die Fangschalter in
         * unserer eigenen Kopfleiste stehen, waeren es zwei Fenster fuer
         * dieselbe Sache - und das zweite lag ausserdem hinter unserem.
         *
         * Eine leere Maske laesst `ToolUISystem` das Fenster gar nicht erst
         * bauen. Der Fang selbst haengt nicht daran: `GetActualSnap()`
         * rechnet mit den Feldern `m_SnapOnMask`/`m_SnapOffMask`, und die
         * bekommen in `OnCreate` weiterhin `Fangarten`.
         *
         * Das ist kein Ausblenden fremder Oberflaeche: dieses Fenster gibt
         * es nur, weil unser Werkzeug danach fragt.
         */
        public override void GetAvailableSnapMask(out Snap onMask, out Snap offMask)
        {
            onMask = Snap.None;
            offMask = Snap.None;
        }

        /**
         * Setzt Cursorring und Hilfslinie zurueck.
         *
         * Noetig, weil am fertigen Polygon gar nicht mehr gefangen wird - die
         * Anzeige bliebe sonst auf dem letzten Zustand stehen und zeigte einen
         * Fang an, den es nicht mehr gibt.
         */
        private void ClearSnapFeedback()
        {
            LastSnap = SnapKind.None;
            HasSnapGuide = false;
            _hoverAxis = float2.zero;
        }

        /**
         * Rastet den Cursor ein, falls etwas in Reichweite ist.
         *
         * Keine Reihenfolge, sondern ein Wettbewerb: jede eingeschaltete
         * Fangart legt Punkt UND Gerade vor, aus je zwei Geraden entsteht
         * zusaetzlich ihr Schnittpunkt, und der beste Vorschlag gewinnt.
         */
        /**
         * `nurWelt` laesst die beiden Quellen aus, die unser EIGENES Polygon
         * lesen: `CollectGuideLines` und `CollectDirections` rechnen gegen
         * `_points`. Waehrend ein Kantenzug diese Punkte gerade bewegt, ergibt
         * das eine Rueckkopplung - Kante wandert, Fangziel wandert mit - und
         * im Spiel sah man ein Schwingen bei stillstehendem Zeiger.
         *
         * Strasse, Objektkante und vorhandene Flaechen sind gefahrlos: die
         * schliessen `Temp` ausdruecklich aus, sehen unsere Vorschau also
         * ohnehin nicht.
         */
        private float3 ApplySnapping(float3 raw, bool nurWelt = false)
        {
            ClearSnapFeedback();
            _snapLines.Clear();

            /**
             * Der Polygonabschluss hat Vorrang vor allem. Zieht ein Fang den
             * Cursor aus dem Umkreis des ersten Punktes heraus, laesst sich
             * das Polygon nicht mehr schliessen - `CanCloseAtCursor` misst
             * naemlich die bereits eingerastete Lage.
             */
            if (!_closed && _points.Count >= MinPolygonPoints
                && math.distance(raw.xz, _points[0]) <= CloseDistance) return raw;

            var snap = GetActualSnap();
            var best = new SnapCandidate
            {
                Position = raw,
                Priority = new float2(float.MinValue, float.MinValue),
                Kind = SnapKind.None,
            };

            if ((snap & Snap.NetSide) != 0) CollectRoadEdges(raw, ref best);
            if ((snap & Snap.ObjectSide) != 0) CollectObjectSides(raw, ref best);
            if ((snap & Snap.ExistingGeometry) != 0) CollectAreaEdges(raw, ref best);
            if ((snap & Snap.ZoneGrid) != 0) CollectZoneGrid(raw, ref best);
            if (!nurWelt)
            {
                if ((snap & Snap.GuideLines) != 0) CollectGuideLines(raw, ref best);
                if ((snap & Snap.StraightDirection) != 0) CollectDirections(raw, ref best);
            }

            if (best.Kind == SnapKind.None) return raw;

            LastSnap = best.Kind;
            _hoverAxis = best.Direction;
            if (best.HasGuide)
            {
                var from = best.Position;
                var to = best.Position;
                from.xz -= best.GuideDirection * GuideLength;
                to.xz += best.GuideDirection * GuideLength;
                SnapGuide = new Line3.Segment(from, to);
                HasSnapGuide = true;
            }
            return best.Position;
        }

        /**
         * Meldet eine erfuellte Bedingung an: als Punkt UND als Gerade.
         *
         * Beides gehoert zusammen. Nur den Punkt zu melden hiesse, dass diese
         * Bedingung sich nie mit einer anderen zu einer Ecke verbinden kann.
         */
        private void RegisterSnap(ref SnapCandidate best, float3 hit, float level,
            SnapKind kind, float3 position, float2 direction, bool isGuide = false)
        {
            if (!math.all(math.isfinite(position))
                || math.lengthsq(direction) < 0.5f) return;

            Offer(ref best, hit, level, WeightSingle, new SnapCandidate
            {
                Position = position,
                Direction = direction,
                Kind = kind,
                HasGuide = isGuide,
                GuideDirection = direction,
            });

            AddSnapLine(ref best, hit, new SnapLine
            {
                Position = position,
                Direction = direction,
                Level = level,
                IsGuide = isGuide,
            });
        }

        /**
         * Nachbau von `ToolUtils.AddSnapLine`.
         *
         * Schneidet die neue Gerade mit allen bisherigen. Jeder Schnittpunkt
         * erfuellt zwei Bedingungen auf einmal und ist deshalb der Vorschlag,
         * den man eigentlich meint: die Ecke, die sowohl am Bordstein liegt
         * als auch rechtwinklig steht.
         */
        private void AddSnapLine(ref SnapCandidate best, float3 hit, SnapLine line)
        {
            for (var i = 0; i < _snapLines.Count; i++)
            {
                var other = _snapLines[i];
                // Parallele Geraden schneiden sich nicht - oder ueberall,
                // was dasselbe Problem ist.
                if (math.abs(math.dot(line.Direction, other.Direction)) > 0.999999f)
                    continue;

                var a = new Line2(line.Position.xz, line.Position.xz + line.Direction);
                var b = new Line2(other.Position.xz, other.Position.xz + other.Direction);
                if (!MathUtils.Intersect(a, b, out var t)) continue;

                // Die hoehere Stufe gibt Hoehe und Achse vor - eine echte
                // Kante weiss besser, wo der Boden liegt, als eine gedachte.
                var leading = line.Level >= other.Level;
                var source = leading ? line : other;
                var position = source.Position;
                position.xz += source.Direction * (leading ? t.x : t.y);

                var guide = line.IsGuide || other.IsGuide;
                Offer(ref best, hit, math.max(line.Level, other.Level),
                    WeightCrossing, new SnapCandidate
                    {
                        Position = position,
                        Direction = source.Direction,
                        Kind = SnapKind.Crossing,
                        HasGuide = guide,
                        GuideDirection = line.IsGuide
                            ? line.Direction : other.Direction,
                    });
            }
            _snapLines.Add(line);
        }

        /** Uebernimmt den Vorschlag, wenn er den bisher besten schlaegt. */
        private static void Offer(ref SnapCandidate best, float3 hit, float level,
            float weight, SnapCandidate proposal)
        {
            proposal.Priority = SnapPriority(level, weight, hit,
                proposal.Position, proposal.Direction);
            if (Better(proposal.Priority, best.Priority)) best = proposal;
        }

        /**
         * Die Rangfolge eines Vorschlags - Nachbau von
         * `ToolUtils.CalculateSnapPriority`.
         *
         * Der Versatz wird in die Achse des Fangziels gedreht, durch 8
         * geteilt und quadriert. Ergebnis: je weiter der Vorschlag den Cursor
         * verschiebt, desto schlechter die Guete - und ein Versatz LAENGS der
         * Kante wiegt genauso wie einer quer dazu.
         *
         * `weight` ist Vanillas `priority`: 1 fuer eine einzelne Bedingung,
         * 2 fuer einen Schnittpunkt. Daher schlaegt eine Ecke, die zwei
         * Bedingungen erfuellt, immer jede einzelne davon.
         *
         * Der Hoehenanteil faellt weg: Vanilla gewichtet ihn nur fuer
         * `AreaType.Space`, ein Parkplatz ist `Lot`.
         */
        private static float2 SnapPriority(float level, float weight, float3 hit,
            float3 snapped, float2 direction)
        {
            var delta = snapped - hit;
            var offset = new float3(
                math.dot(delta.xz, direction),
                delta.y,
                math.dot(delta.xz, MathUtils.Right(direction)));
            offset /= 8f;
            offset *= offset;
            var sum = math.min(1f, offset.x + offset.z);
            var worst = math.max(offset.x, offset.z)
                + math.min(offset.x, offset.z) * 0.001f;
            return new float2(level, weight * (2f - sum - worst));
        }

        /** Nachbau von `ToolUtils.CompareSnapPriority`: Stufe vor Guete. */
        private static bool Better(float2 candidate, float2 current)
            => candidate.x > current.x
                || (candidate.y > current.y && candidate.x == current.x);

        /**
         * Das Achsenkreuz.
         *
         * Bezugsachsen, in der Reihenfolge von `AreaToolSystem`:
         *   1. die Achse, an der der letzte Punkt eingerastet ist - bei einem
         *      Punkt am Bordstein also die Fahrtrichtung der Strasse.
         *   2. die zuletzt gezogene Polygonkante.
         * Beide haengen am letzten gesetzten Punkt. Geprueft wird jeweils
         * laengs und quer; der insgesamt naechste Treffer gewinnt.
         *
         * Gemeldet wird immer, auch wenn dieser Vorschlag fuer sich genommen
         * verliert: als Gerade wird er noch gebraucht: mit dem Bordstein
         * zusammen ergibt er die rechtwinklige Ecke an der Strasse.
         */
        private void CollectDirections(float3 raw, ref SnapCandidate best)
        {
            SyncSnapAxes();
            var count = _points.Count;
            if (count == 0) return;

            var distance = float.MaxValue;
            var position = raw;
            var direction = float2.zero;

            if (_dragPoint >= 0)
            {
                /**
                 * Beim Ziehen zaehlen die beiden Nachbarkanten - der Punkt
                 * hat ja keine "letzte" Kante, sondern zwei angrenzende.
                 * Ebenso in `AreaToolSystem`, Zustand `Modify`.
                 */
                if (count < 3) return;
                var previous = (_dragPoint - 1 + count) % count;
                var beforeThat = (_dragPoint - 2 + count) % count;
                var next = (_dragPoint + 1) % count;
                var afterThat = (_dragPoint + 2) % count;
                DirectionSnap(ref distance, ref position, ref direction, raw,
                    _points[previous],
                    math.normalizesafe(_points[beforeThat] - _points[previous]));
                DirectionSnap(ref distance, ref position, ref direction, raw,
                    _points[next],
                    math.normalizesafe(_points[afterThat] - _points[next]));
            }
            else
            {
                var anchor = _points[count - 1];
                DirectionSnap(ref distance, ref position, ref direction, raw,
                    anchor, _pointAxes[count - 1]);
                if (count >= 2)
                    DirectionSnap(ref distance, ref position, ref direction, raw,
                        anchor, math.normalizesafe(_points[count - 2] - anchor));
            }

            if (math.lengthsq(direction) < 0.5f) return;

            var heightData = _terrainSystem.GetHeightData();
            position.y = TerrainUtils.SampleHeight(ref heightData,
                new float3(position.x, raw.y, position.z));

            RegisterSnap(ref best, raw, LevelDirection, SnapKind.Direction,
                position, direction, isGuide: true);
        }

        /**
         * Nachbau von `ToolUtils.DirectionSnap`.
         *
         * Prueft die Achse UND ihre Senkrechte und behaelt den naeheren der
         * beiden. `Line2` misst zur UNENDLICHEN Geraden (`Line2.Segment`
         * waere gekappt) - deshalb darf `t` negativ werden, und genau daran
         * erkennt man, dass die Achse umgedreht gehoert.
         */
        private static void DirectionSnap(ref float bestDistance,
            ref float3 resultPosition, ref float2 resultDirection,
            float3 raw, float2 origin, float2 axis)
        {
            if (math.lengthsq(axis) < 0.5f) return;

            var across = MathUtils.Right(axis);
            var along = new Line2(origin, origin + axis);
            var side = new Line2(origin, origin + across);
            var alongDistance = MathUtils.Distance(along, raw.xz, out var alongT);
            var sideDistance = MathUtils.Distance(side, raw.xz, out var sideT);

            if (alongDistance < bestDistance)
            {
                bestDistance = alongDistance;
                if (alongDistance < SnapDistance)
                {
                    resultDirection = alongT < 0f ? -axis : axis;
                    resultPosition.xz = MathUtils.Position(along, alongT);
                }
            }
            if (sideDistance < bestDistance)
            {
                bestDistance = sideDistance;
                if (sideDistance < SnapDistance)
                {
                    resultDirection = sideT < 0f ? -across : across;
                    resultPosition.xz = MathUtils.Position(side, sideT);
                }
            }
        }

        /**
         * HILFSLINIEN: das Achsenkreuz JEDER schon gesetzten Ecke, nicht nur
         * der letzten.
         *
         * Der Achsenfang kennt nur den zuletzt gesetzten Punkt. Damit laesst
         * sich die fuenfte Ecke nicht auf die zweite ausrichten - und genau
         * das braucht man bei jedem Grundstueck, das nicht einfach ein
         * Rechteck ist.
         *
         * Vanilla meint mit `Snap.GuideLines` dasselbe: `NetToolSystem`
         * verlaengert von einem nahen Knoten aus die Kantenrichtung und ihre
         * beiden Senkrechten als `SnapLine` mit `SnapLineFlags.GuideLine`.
         * Hier sind die Bezugspunkte die eigenen Ecken - beim Zeichnen eines
         * Grundstuecks sind sie das, woran man sich ausrichten will.
         *
         * Je Ecke wird HOECHSTENS EINE Linie gemeldet, naemlich die naechste
         * ihres Achsenkreuzes. Sonst haette ein Achteck sechzehn Hilfslinien,
         * und der Cursor haenge an jeder zweiten Mausbewegung woanders.
         */
        private void CollectGuideLines(float3 raw, ref SnapCandidate best)
        {
            SyncSnapAxes();
            var count = _points.Count;
            if (count < 2) return;

            var heightData = _terrainSystem.GetHeightData();
            for (var i = 0; i < count; i++)
            {
                // Die letzte Ecke gehoert dem Achsenfang, die gezogene sich
                // selbst - doppelt gemeldet gaebe es doppelte Geraden.
                if (i == _dragPoint) continue;
                if (_dragPoint < 0 && i == count - 1) continue;

                var anchor = _points[i];
                var distance = float.MaxValue;
                var position = raw;
                var direction = float2.zero;
                DirectionSnap(ref distance, ref position, ref direction, raw,
                    anchor, _pointAxes[i]);
                DirectionSnap(ref distance, ref position, ref direction, raw,
                    anchor, math.normalizesafe(
                        _points[(i + 1) % count] - anchor));
                if (math.lengthsq(direction) < 0.5f) continue;

                position.y = TerrainUtils.SampleHeight(ref heightData,
                    new float3(position.x, raw.y, position.z));
                RegisterSnap(ref best, raw, LevelGuide, SnapKind.Guide,
                    position, direction, isGuide: true);
            }
        }

        /**
         * Haelt `_pointAxes` auf der Laenge von `_points`.
         *
         * Selbstheilend statt an jeder Stelle mitgepflegt: `_points` wird an
         * sechs Stellen veraendert (setzen, ziehen, zuruecknehmen, zweimal
         * leeren), und eine vergessene Stelle waere ein Indexfehler mitten im
         * Zeichnen.
         */
        private void SyncSnapAxes()
        {
            while (_pointAxes.Count > _points.Count)
                _pointAxes.RemoveAt(_pointAxes.Count - 1);
            while (_pointAxes.Count < _points.Count)
                _pointAxes.Add(float2.zero);
        }

        /** Uebernimmt die Achse des aktuellen Fangs fuer einen Punkt. */
        private void StoreSnapAxis(int index)
        {
            SyncSnapAxes();
            if (index >= 0 && index < _pointAxes.Count)
                _pointAxes[index] = _hoverAxis;
        }

        /** Stellt die Achse wieder her, wenn ein Ziehen verworfen wird. */
        private void RestoreSnapAxis(int index, float2 axis)
        {
            SyncSnapAxes();
            if (index >= 0 && index < _pointAxes.Count) _pointAxes[index] = axis;
        }

        /** Die Achse eines Punktes - fuer das Merken vor dem Ziehen. */
        private float2 GetSnapAxis(int index)
        {
            SyncSnapAxes();
            return index >= 0 && index < _pointAxes.Count
                ? _pointAxes[index] : float2.zero;
        }
    }
}
