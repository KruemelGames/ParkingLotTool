using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Game.Rendering;
using Game.Simulation;
using ParkingLotTool.Geometry;
using Unity.Mathematics;
using UnityEngine;
using static ParkingLotTool.Tools.ParkingLotPreviewStyle;

namespace ParkingLotTool.Tools
{
    internal sealed partial class ParkingLotOverlay
    {
        // Layout-Baender; Polygonbelaege kommen aus ParkingLotAreaPreview.
        private readonly List<Band> _green = new List<Band>();

        /**
         * Das Flaechennetz, wenn es zeichnen kann. Wird vom Werkzeug gesetzt.
         *
         * Steht hier `null`, faellt die Teilflaechen-Einfaerbung auf das alte
         * Klickrechteck zurueck. Lieber ein zu grobes Rechteck als gar keine
         * Anzeige, welche Teilflaeche gerade gemeint ist.
         */
        internal ParkingLotFlaechennetzSystem Flaechennetz;

        private readonly List<Band> _roads = new List<Band>();
        private readonly List<Band> _bays = new List<Band>();
        private readonly List<(float3 Position, bool Tree)> _vegetation = new List<(float3, bool)>();
        internal void SetVegetation(VegetationPlan plan, VegetationSpecies[] species, TerrainSystem terrain)
        {
            _vegetation.Clear();
            var heights=terrain.GetHeightData();
            foreach(var plant in plan.Plants) {
                var point=new float3(plant.Position.x,0,plant.Position.y);
                point.y=TerrainUtils.SampleHeight(ref heights,point);
                if(math.all(math.isfinite(point))) _vegetation.Add((point,species[plant.Species].Tree));
            }
        }
        private readonly List<float3> _chargers = new List<float3>();

        private readonly struct Band
        {
            internal readonly Line3.Segment Segment;
            internal readonly float Width;
            internal readonly Color Color;

            internal Band(Line3.Segment segment, float width, Color color)
            {
                Segment = segment;
                Width = width;
                Color = color;
            }
        }

        internal void ClearLayout()
        {
            _green.Clear();
            _roads.Clear();
            _bays.Clear();
            _chargers.Clear();
            _vegetation.Clear();
        }

        /**
         * `vorflaechen` sind die Belagstuecke VOR dem Polygon, bis an die
         * Strasse.
         *
         * Sie muessen hier gezeichnet werden und nicht nur als CS2-Vorschau:
         * Vorschauflaechen zeigt CS2 ausschliesslich als Rand, und der wird
         * wie die Flaeche selbst aufs Gelaende projiziert - auf dem Gehweg
         * einer Strasse also unsichtbar. Genau das hat der Nutzer am
         * 2026-08-27 gemeldet: gebaut sichtbar, in der Vorschau nicht. Das
         * Overlay zeichnet dagegen ueber allem.
         */
        internal void SetLayout(ParkingLayout layout, LayoutSettings settings,
            TerrainSystem terrainSystem, float2[][] vorflaechen = null,
            int[] vorflaechenArt = null, bool gruenAlsNetz = false)
        {
            ClearLayout();
            if (layout == null || settings == null || terrainSystem == null) return;

            var heightData = terrainSystem.GetHeightData();

            float3 World(float2 point)
            {
                var position = new float3(point.x, 0f, point.y);
                position.y = TerrainUtils.SampleHeight(ref heightData, position);
                return position;
            }

            /**
             * Ein Rechteck wird zu einer Linie mit Breite - der hier verwendete Weg,
             * rechteckige Layoutbauteile im Overlay zu zeigen. Die Mittellinie laeuft
             * entlang der LAENGEREN Seite, damit aus einem 3 x 5,9-Rechteck
             * eine 5,9 m lange Linie von 3 m Breite wird und nicht umgekehrt.
             */
            void AddQuad(List<Band> target, float2[] quad, Color color)
            {
                if (quad == null || quad.Length != 4) return;

                var edge0 = math.distance(quad[0], quad[1]);
                var edge1 = math.distance(quad[1], quad[2]);
                float2 start;
                float2 end;
                float width;

                if (edge0 >= edge1)
                {
                    start = (quad[0] + quad[3]) * 0.5f;
                    end = (quad[1] + quad[2]) * 0.5f;
                    width = (math.distance(quad[0], quad[3])
                        + math.distance(quad[1], quad[2])) * 0.5f;
                }
                else
                {
                    start = (quad[0] + quad[1]) * 0.5f;
                    end = (quad[3] + quad[2]) * 0.5f;
                    width = (math.distance(quad[0], quad[1])
                        + math.distance(quad[3], quad[2])) * 0.5f;
                }

                if (width < 0.01f || math.lengthsq(end - start) < 0.0001f) return;
                target.Add(new Band(new Line3.Segment(World(start), World(end)),
                    width, color));
            }

            void AddQuads(List<Band> target, float2[][] quads, Color color)
            {
                if (quads == null) return;
                for (var i = 0; i < quads.Length; i++) AddQuad(target, quads[i], color);
            }

            /**
             * GRUEN aus den ENTWURFS-Rechtecken, nicht aus den verschmolzenen
             * Ringen.
             *
             * Gemessen: Median, Kappen und Gruenflaechen sind zu 100 % echte
             * Vierecke, der Rest zur Haelfte. Ein Viereck deckt ein Rechteck
             * mit EINEM Streifen exakt ab. Ueber die verschmolzenen Ringe zu
             * gehen kostete im selben Fall 154 Streifen und bis zu 21 %
             * Flaechenfehler, weil die Abtastzeilen an schraegen Kanten
             * ueber- und unterschiessen. Gedeckt wird derselbe Boden: die
             * Ringe SIND die Vereinigung dieser Teile.
             */
            void AddStrips(List<Band> target, float2[][] rings, Color color)
            {
                if (rings == null) return;
                foreach (var strip in ParkingSurfaceStrips.Fill(
                             rings, ParkingSurfaceStrips.PreferredWidth))
                {
                    if (strip.Width < 0.01) continue;
                    var from = World(new float2((float)strip.From.x, (float)strip.From.y));
                    var to = World(new float2((float)strip.To.x, (float)strip.To.y));
                    if (math.distancesq(from, to) < 0.0001f) continue;
                    target.Add(new Band(new Line3.Segment(from, to),
                        (float)strip.Width, color));
                }
            }

            bool LogicalInsideRing(float2[] polygon)
            {
                if (polygon == null || polygon.Length == 0
                    || layout.Ring == null || layout.Ring.Length < 3) return false;
                var middle = polygon.Aggregate(float2.zero, (sum, point) => sum + point)
                    / polygon.Length;
                var inside = false;
                for (int i = 0, j = layout.Ring.Length - 1;
                     i < layout.Ring.Length; j = i++)
                {
                    var a = layout.Ring[i];
                    var b = layout.Ring[j];
                    if ((a.y > middle.y) != (b.y > middle.y)
                        && middle.x < (b.x - a.x) * (middle.y - a.y)
                            / (b.y - a.y) + a.x)
                        inside = !inside;
                }
                return inside;
            }
            /*
             * ZWEI WEGE, EINE AUSWAHL.
             *
             * `gruenAlsNetz` heisst: das Dreiecksnetz hat ein Material
             * gefunden und uebernimmt die Fuellung - dann aus den
             * VERSCHMOLZENEN Grasringen, nicht aus diesen Entwurfsteilen.
             * Die Streifen bleiben dann weg, sonst laege beides uebereinander.
             *
             * Findet sich KEIN Shader, fallen wir auf die Streifen zurueck.
             * Sie haben Naehte, aber sie sind da - ein Werkzeug, das gar
             * nichts anzeigt, waere schlimmer als eines mit einem sichtbaren
             * Mangel.
             */
            void Gruen(float2[][] polygons)
            {
                if (polygons == null || gruenAlsNetz) return;
                AddStrips(_green, polygons, GreenColor);
            }

            void AddMaterialGreen(float2[][] polygons, bool insideGreen)
            {
                if (polygons == null) return;
                Gruen(insideGreen
                    ? polygons
                    : polygons.Where(polygon => !LogicalInsideRing(polygon)).ToArray());
            }

            // Mittelstreifen sind immer gruen, es gibt dafuer keinen Schalter.
            Gruen(layout.Median);
            AddMaterialGreen(layout.Cap, settings.Qk);
            Gruen(layout.Green);
            AddMaterialGreen(layout.Fill, settings.Qk);
            // qk=false erzeugt den Rest zwischen erster Bucht und
            // Querstrasse als Belag. Nur ein theoretischer Anteil ausserhalb
            // der Randstrasse duerfte weiterhin gruen erscheinen.
            AddMaterialGreen(layout.CrossPavement, false);

            // Die Fahrwege liegen als fertige Rechtecke im Layout - dieselben,
            // aus denen der Netzbauer spaeter die unsichtbaren Strassen macht.
            // Asphalt braucht keine eigene Fuellung: er IST Buchten plus
            // Fahrwege, genau wie im Band-Modell.
            AddQuads(_roads, layout.PerimeterQuad, PerimeterRoadColor);
            AddQuads(_roads, layout.AisleQuad, AisleRoadColor);
            AddQuads(_roads, layout.CrossQuad, CrossRoadColor);
            /*
             * Ohne Artenliste (klassischer Rechenkern) faellt alles auf die
             * bisherige eine Farbe zurueck. Eine fehlende Zuordnung darf die
             * Vorschau nicht verfaerben, sondern nur nicht einfaerben.
             */
            var arten = layout.EntranceQuadArt;
            for (var i = 0; i < layout.EntranceQuad.Length; i++)
            {
                var farbe = arten != null && i < arten.Length
                    && arten[i] >= 0 && arten[i] < EntranceArtColors.Length
                    ? EntranceArtColors[arten[i]]
                    : EntranceRoadColor;
                AddQuads(_roads, new[] { layout.EntranceQuad[i] }, farbe);
            }

            /*
             * Dieselbe Farbe wie die zugehoerige Zufahrt, nur blasser: die
             * Vorflaeche ist deren Fortsetzung, kein eigenes Bauteil.
             */
            if (vorflaechen != null)
            {
                for (var i = 0; i < vorflaechen.Length; i++)
                {
                    var art = vorflaechenArt != null && i < vorflaechenArt.Length
                        ? vorflaechenArt[i] : 0;
                    var basis = art >= 0 && art < EntranceArtColors.Length
                        ? EntranceArtColors[art] : EntranceRoadColor;
                    var farbe = Alpha(basis, FillSelected);
                    AddQuads(_roads, new[] { vorflaechen[i] }, farbe);
                }
            }

            foreach (var run in ParkingBayRuns.Merge(layout, settings))
                AddQuad(_bays, run.Quad, ColorFor(run.Role));

            foreach (var charger in ParkingBayDecals.Plan(layout, settings).Chargers)
                _chargers.Add(World(new float2(
                    (float)charger.Center.x, (float)charger.Center.y)));
        }

        private static Color ColorFor(BayRole role)
        {
            switch (role)
            {
                case BayRole.Disabled: return DisabledBayColor;
                case BayRole.Electric: return ElectricBayColor;
                default: return NormalBayColor;
            }
        }

        private static void DrawBands(ParkingLotPreviewBuffer buffer, List<Band> bands, bool outline = false)
        {
            for (var i = 0; i < bands.Count; i++)
            {
                var band = bands[i];
                buffer.DrawLine(Alpha(band.Color, outline ? 0.45f : band.Color.a),
                    band.Color, outline ? GridLineWidth : 0f,
                    OverlayRenderSystem.StyleFlags.Projected,
                    band.Segment, band.Width, default);
            }
        }
    }
}
