using System;
using System.Linq;
using System.Collections.Generic;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    internal sealed class AreaTransferRecord
    {
        internal string Kind;
        internal int Index;
        internal Entity Prefab;
        internal Entity Definition;
        internal float3[] SentNodes = Array.Empty<float3>();
        internal int CreatedFrame;
        internal bool DefinitionExists;
        internal bool? DefinitionHasCreationDefinition;
        internal bool? DefinitionHasNodeBuffer;
        internal bool? DefinitionHasUpdated;
        internal bool? DefinitionMaterialized;
        internal Entity MaterializedEntity;
        internal bool? GeometryAccepted;
        internal string Status;
        internal string Reason;
        internal string AreaFlags;
        internal int? AreaFlagsValue;
        internal string TempFlags;
        internal int? TempFlagsValue;
        internal int? TriangleCount;
        internal bool? TriangleIndicesValid;
        internal float3[] MaterializedNodes;
        internal float[] MaterializedElevations;
    }

    internal sealed class TerrainTransferRecord
    {
        internal int SampleCount;
        internal float? Minimum;
        internal float? Maximum;
        internal float? Span;
        internal bool FiftyMeterLimitTriggered;
        internal float LimitMeters;
        internal string Note;
    }

    internal sealed class PreviewDiagnosticRecord
    {
        internal DateTime TimestampUtc;
        internal string Severity;
        internal string Message;
        internal string ExceptionType;
        internal string ExceptionMessage;
        internal string StackTrace;
    }

    internal sealed class DebugDumpDocument
    {
        public int SchemaVersion { get; set; }
        public Dictionary<string, object> ZettelUIWerte { get; set; }
        public object[] Zoningseiten { get; set; }
        public object[] Bushaltestellen { get; set; }
        public ParkingLotTool.Geometry.VegetationOptions Vegetation { get; set; }
        public Dictionary<string, object> Vegetationswerte { get; set; }
        public DebugFramework Framework { get; set; }
        public DebugInput Input { get; set; }
        public DebugPreview Preview { get; set; }
        public DebugAreaTransfer Cs2AreaTransfer { get; set; }
        public DebugBuilt Built { get; set; }
        public DebugWorld ExistingWorld { get; set; }
        public DebugAreaPrefabs AreaPrefabs { get; set; }
        public DebugDiagnostics Diagnostics { get; set; }
    }

    /**
     * Alle Flaechen-Prefabs mit ihrer Art.
     *
     * Gebraucht fuer die Frage, warum man den Parkplatz nur auf den Decals
     * anklicken kann: `DefaultToolSystem` setzt `AreaTypeMask.Lots`, nie
     * `Surfaces`, und die Art kommt aus `AreaData.m_Type` am Prefab
     * (`AreaUtils.GetTypeMask` = 1 << (int)m_Type). Eine Flaeche vom Typ
     * Lot waere im ganzen Umriss anklickbar - dazu muss man aber erst
     * wissen, welche es ueberhaupt gibt.
     */
    internal sealed class DebugAreaPrefabs
    {
        public int Total { get; set; }
        public string[] ByType { get; set; }
        public DebugAreaPrefab[] Lots { get; set; }

        /** Alle Belag- und Gruenflaechen, aus denen der Nutzer waehlen kann. */
        public DebugAreaPrefab[] Surfaces { get; set; }
        public string Note { get; set; }
    }

    internal sealed class DebugAreaPrefab
    {
        public string Name { get; set; }
        public string PrefabClass { get; set; }
        public string AreaType { get; set; }
        public bool Builtin { get; set; }
        public float? MaxRadius { get; set; }
        public bool? AllowOverlap { get; set; }
        public bool? AllowEditing { get; set; }
        public bool? OnWater { get; set; }
    }

    internal sealed class DebugFramework
    {
        public string CapturedAtUtc { get; set; }
        public string CapturedAtLocal { get; set; }
        /*
         * DIE DREI HEISSEN "Path", ENTHALTEN ABER NUR DEN DATEINAMEN.
         *
         * Seit dem 2026-09-15 mit Absicht: der volle Pfad faengt mit
         * C:\Users\<Name> an, und dieser Abzug geht mit jeder Meldung nach
         * aussen. Die Namen bleiben, damit aeltere Abzuege weiter lesbar
         * sind und die Schemaversion nicht steigen muss.
         */
        public string OutputPath { get; set; }
        public string LatestPath { get; set; }
        public string DllPath { get; set; }
        public string DllBuiltAtUtc { get; set; }
        public string DllBuiltAtLocal { get; set; }
        public string DllTimestampSource { get; set; }
        public string Cs2Version { get; set; }
        public string GameMode { get; set; }
        public string CityName { get; set; }
        public string MapName { get; set; }
        public string MapNameNote { get; set; }
        public string CoordinatePrecision { get; set; }
    }

    internal sealed class DebugInput
    {
        public string PolygonSource { get; set; }
        public DebugPoint3[] PolygonWorld { get; set; }
        public DebugPoint2[] PolygonXZ { get; set; }
        public DebugLayoutSettings LayoutSettings { get; set; }
        public Dictionary<string, object> Layoutwerte { get; set; }
        public Dictionary<string, object> UIWerte { get; set; }
        public object[] Zoningseiten { get; set; }
        public int CurrentGeometryRevision { get; set; }
        public int? ResultGeometryRevision { get; set; }
        public bool MatchesResult { get; set; }
        public string Note { get; set; }
    }

    /// <summary>Ein Wegstueck aus `ParkingLayout.NetLine`.</summary>
    internal sealed class DebugNetPiece
    {
        public string Kind { get; set; }
        public DebugPoint2 A { get; set; }
        public DebugPoint2 B { get; set; }
    }

    internal sealed class DebugLayoutSettings
    {
        public Dictionary<string, object> AlleWerte { get; set; }
        public double Es { get; set; }
        public double Ai { get; set; }
        public double Cw { get; set; }
        public double Sl { get; set; }
        public double Sw { get; set; }
        public double Md { get; set; }
        public double Cr { get; set; }
        public bool Qk { get; set; }
        public bool Auto { get; set; }
        public double Angle { get; set; }
        public string AngleMode { get; set; }
        public DebugAlignment[] TeilflaechenAusrichtungen { get; set; }
        // Ohne dieses Feld ist ein Abzug nicht nachrechenbar: alter und
        // Zellenweg brechen an voellig verschiedenen Stellen ab, und aus den
        // uebrigen Werten laesst sich nicht ablesen, welcher gelaufen ist.
        public bool Zellen { get; set; }
        /*
         * OHNE DEN SCHALTER IST EIN ABZUG NICHT ZUZUORDNEN.
         *
         * Am 2026-09-08 meldete der Nutzer zwei Fehler "bei Randstrassen
         * aus". Der Bauzettel schrieb den Schalter nicht mit - aus dem JSON
         * war nicht abzulesen, in welcher Betriebsart gebaut wurde, und die
         * beiden Wege bauen voellig verschieden.
         */
        public bool Randstrassen { get; set; }
        public DebugEntrance[] Entrances { get; set; }
        public bool NoNotch { get; set; }
        public bool Single { get; set; }
        // Siehe DebugStrecke: ohne diese Felder ist ein Abzug nicht
        // nachrechenbar. Die Handschnitte teilen das Grundstueck und
        // aendern die Buchtenzahl um Hunderte.
        public double? Ausrichtwinkel { get; set; }
        public bool AutomaticEntrances { get; set; }
        public string Zoningstrasse { get; set; }
        public DebugStrecke[] Teilflaechenschnitte { get; set; }
        public DebugZoningflaeche[] Zoningflaechen { get; set; }
        public DebugStrecke[] Randzoning { get; set; }

        internal static DebugLayoutSettings From(LayoutSettings settings)
        {
            if (settings == null) return null;
            DebugEntrance[] resultEntrances = null;
            if (settings.Entrances != null)
            {
                resultEntrances = new DebugEntrance[settings.Entrances.Length];
                for (var i = 0; i < settings.Entrances.Length; i++)
                    resultEntrances[i] = DebugEntrance.From(settings.Entrances[i]);
            }
            var resultAlignments = (settings.TeilflaechenAusrichtungen
                    ?? Array.Empty<TeilflaechenAusrichtung>())
                .Where(value => value != null)
                .Select(value => new DebugAlignment
                {
                    Anchor = DebugPoint2.From(value.Anker),
                    Angle = value.Winkel,
                }).ToArray();
            return new DebugLayoutSettings
            {
                AlleWerte = ParkingSettingsInventory.Erfasse(settings,
                    ParkingSettingsInventory.Layout),
                Es = settings.Es,
                Ai = settings.Ai,
                Cw = settings.Cw,
                Sl = settings.Sl,
                Sw = settings.Sw,
                Md = settings.Md,
                Cr = settings.Cr,
                Qk = settings.Qk,
                Auto = settings.Auto,
                Angle = settings.Angle,
                AngleMode = settings.AngleMode,
                TeilflaechenAusrichtungen = resultAlignments,
                Zellen = settings.Zellen,
                Entrances = resultEntrances,
                Randstrassen = settings.Randstrassen,
                Ausrichtwinkel = settings.Ausrichtwinkel,
                AutomaticEntrances = settings.AutomaticEntrances,
                Zoningstrasse = settings.Zoningstrasse,
                Teilflaechenschnitte = (settings.Teilflaechenschnitte
                        ?? Array.Empty<Teilflaechenschnitt>())
                    .Select(schnitt => new DebugStrecke
                    {
                        A = DebugPoint2.From(schnitt.A),
                        B = DebugPoint2.From(schnitt.B),
                    }).ToArray(),
                Zoningflaechen = (settings.Zoningflaechen
                        ?? Array.Empty<ParkingGeometry.Zoningflaeche>())
                    .Select(flaeche => new DebugZoningflaeche
                    {
                        Ecke = DebugPoint2.From(flaeche.Ecke),
                        Spalten = flaeche.Spalten,
                        Reihen = flaeche.Reihen,
                        Winkel = flaeche.Winkel,
                        Rand = flaeche.Rand,
                        Aussentiefen = flaeche.Aussentiefen,
                    }).ToArray(),
                Randzoning = (settings.Randzoning
                        ?? Array.Empty<ParkingGeometry.RandzoningLinie>())
                    .Select(linie => new DebugStrecke
                    {
                        A = DebugPoint2.From(linie.A),
                        B = DebugPoint2.From(linie.B),
                    }).ToArray(),
                NoNotch = settings.NoNotch,
                Single = settings.Single,
            };
        }
    }

    internal sealed class DebugAlignment
    {
        public DebugPoint2 Anchor { get; set; }
        public double Angle { get; set; }
    }

    /**
     * WAS DER BAUZETTEL BISHER VERSCHWIEGEN HAT.
     *
     * Am 2026-09-09 liess sich ein Absturzfall nicht nachrechnen: mit den
     * mitgeschriebenen Reglern kamen 527 Buchten heraus, im Abzug standen
     * 407. Der Grund waren fehlende Felder - vor allem die Handschnitte, die
     * das Grundstueck teilen. Ein Bauzettel, den man nicht nachrechnen kann,
     * ist nur ein halber Bauzettel.
     */
    internal sealed class DebugStrecke
    {
        public DebugPoint2 A { get; set; }
        public DebugPoint2 B { get; set; }
    }

    internal sealed class DebugZoningflaeche
    {
        public DebugPoint2 Ecke { get; set; }
        public int Spalten { get; set; }
        public int Reihen { get; set; }
        public double Winkel { get; set; }
        public double Rand { get; set; }
        /* Vier Tiefen im Uhrzeigersinn. Ohne sie faende man im Abzug nicht
           wieder, warum der Parkplatz an einer Seite Platz laesst. */
        public double[] Aussentiefen { get; set; }
    }

    internal sealed class DebugPreview
    {
        public bool Available { get; set; }
        public string StartedAtUtc { get; set; }
        public string CompletedAtUtc { get; set; }
        public double? BuildMilliseconds { get; set; }
        public DebugPoint3[] PolygonWorldUsed { get; set; }
        public DebugLayout Layout { get; set; }
        public string Note { get; set; }
    }

    internal sealed class DebugLayout
    {
        public DebugBay[] Bay { get; set; }
        public DebugPoint2[][] Cap { get; set; }
        public string[] CapKind { get; set; }
        public DebugPoint2[][] Median { get; set; }
        public DebugPoint2[][] Green { get; set; }
        public DebugPoint2[][] Fill { get; set; }
        public DebugPoint2[][] FillHole { get; set; }
        public DebugPoint2[][] CrossPavement { get; set; }
        public DebugPoint2[][] GrassSurface { get; set; }
        public DebugPoint2[][] AsphaltSurface { get; set; }
        public DebugPoint2[][] PerimeterQuad { get; set; }
        public DebugPoint2[][] EntranceQuad { get; set; }
        public DebugPoint2[][] AisleLine { get; set; }
        public DebugPoint2[][] AisleQuad { get; set; }
        public DebugPoint2[][] CrossLine { get; set; }
        public DebugPoint2[][] CrossQuad { get; set; }
        public DebugPoint2[][] PerimeterLine { get; set; }

        /**
         * DIE WEGSTUECKE, AUS DENEN DIE ECHTEN STRASSEN ENTSTEHEN.
         *
         * Fehlten bis zum 2026-08-21 im Abzug - es standen nur Zaehler drin
         * ("23 Kurse geplant"). Als der Nutzer meldete, an der Einfahrt gehe
         * es nur in eine Richtung, war der Abzug deshalb blind: welche Stuecke
         * wo enden und ob sie sich einen Knoten teilen, stand nirgends. Genau
         * daran entscheidet CS2 aber, ob eine Kreuzung entsteht.
         */
        public DebugNetPiece[] NetLine { get; set; }
        public DebugPoint2[][] EntranceLine { get; set; }
        public DebugEntrance[] Entrances { get; set; }
        public DebugPoint2[] Ring { get; set; }
        public DebugLayoutSection[] Sections { get; set; }
        public int Stalls { get; set; }
        public int PerimeterStalls { get; set; }
        public int InnerPerimeterStalls { get; set; }
        public int InnerStalls { get; set; }
        public int ExtraStalls { get; set; }
        public DebugSpecialStalls SpecialStalls { get; set; }
        public double Angle { get; set; }
        public int Aisles { get; set; }
        public int Parts { get; set; }
        public int NotchAisles { get; set; }
        public string[] Warnings { get; set; }

        internal static DebugLayout From(ParkingLayout layout)
        {
            if (layout == null) return null;
            DebugBay[] debugBays = null;
            if (layout.Bay != null)
            {
                debugBays = new DebugBay[layout.Bay.Length];
                for (var i = 0; i < layout.Bay.Length; i++)
                {
                    debugBays[i] = new DebugBay
                    {
                        BayKind = layout.BayKind != null && i < layout.BayKind.Length
                            ? layout.BayKind[i].ToString() : null,
                        BayRole = layout.BayRole != null && i < layout.BayRole.Length
                            ? layout.BayRole[i].ToString() : null,
                        Points = DebugPoint2.From(layout.Bay[i]),
                    };
                }
            }

            DebugEntrance[] debugEntrances = null;
            if (layout.Entrances != null)
            {
                debugEntrances = new DebugEntrance[layout.Entrances.Length];
                for (var i = 0; i < layout.Entrances.Length; i++)
                    debugEntrances[i] = DebugEntrance.From(layout.Entrances[i]);
            }

            DebugLayoutSection[] debugSections = null;
            if (layout.Sections != null)
            {
                debugSections = new DebugLayoutSection[layout.Sections.Length];
                for (var i = 0; i < layout.Sections.Length; i++)
                {
                    var section = layout.Sections[i];
                    debugSections[i] = section == null ? null : new DebugLayoutSection
                    {
                        L = section.L,
                        Bays = section.Bays,
                        Cap0 = section.Cap0,
                        Cap1 = section.Cap1,
                        End0 = section.End0,
                        End1 = section.End1,
                    };
                }
            }

            return new DebugLayout
            {
                Bay = debugBays,
                Cap = DebugPoint2.From(layout.Cap),
                CapKind = layout.CapKind,
                Median = DebugPoint2.From(layout.Median),
                Green = DebugPoint2.From(layout.Green),
                Fill = DebugPoint2.From(layout.Fill),
                FillHole = DebugPoint2.From(layout.FillHole),
                CrossPavement = DebugPoint2.From(layout.CrossPavement),
                GrassSurface = DebugPoint2.From(layout.GrassSurface),
                AsphaltSurface = DebugPoint2.From(layout.AsphaltSurface),
                PerimeterQuad = DebugPoint2.From(layout.PerimeterQuad),
                EntranceQuad = DebugPoint2.From(layout.EntranceQuad),
                AisleLine = DebugPoint2.From(layout.AisleLine),
                AisleQuad = DebugPoint2.From(layout.AisleQuad),
                CrossLine = DebugPoint2.From(layout.CrossLine),
                CrossQuad = DebugPoint2.From(layout.CrossQuad),
                PerimeterLine = DebugPoint2.From(layout.PerimeterLine),
                NetLine = (layout.NetLine ?? Array.Empty<NetSegment>())
                    .Select(stueck => new DebugNetPiece
                    {
                        Kind = stueck.Kind,
                        A = DebugPoint2.From(stueck.A),
                        B = DebugPoint2.From(stueck.B),
                    }).ToArray(),
                EntranceLine = DebugPoint2.From(layout.EntranceLine),
                Entrances = debugEntrances,
                Ring = DebugPoint2.From(layout.Ring),
                Sections = debugSections,
                Stalls = layout.Stalls,
                PerimeterStalls = layout.PerimeterStalls,
                InnerPerimeterStalls = layout.InnerPerimeterStalls,
                InnerStalls = layout.InnerStalls,
                ExtraStalls = layout.ExtraStalls,
                SpecialStalls = layout.SpecialStalls == null
                    ? null
                    : new DebugSpecialStalls
                    {
                        Behindert = layout.SpecialStalls.Behindert,
                        Elektro = layout.SpecialStalls.Elektro,
                    },
                Angle = layout.Angle,
                Aisles = layout.Aisles,
                Parts = layout.Parts,
                NotchAisles = layout.NotchAisles,
                Warnings = layout.Warnings,
            };
        }
    }

    internal sealed class DebugBay
    {
        public string BayKind { get; set; }
        public string BayRole { get; set; }
        public DebugPoint2[] Points { get; set; }
    }

    internal sealed class DebugEntrance
    {
        public int? Edge { get; set; }
        public double? Along { get; set; }
        public string Corner { get; set; }
        /**
         * DIE ART GEHOERT IN DEN ABZUG.
         *
         * Am 2026-08-27 meldete der Nutzer, die Sonderplaetze landeten nicht
         * am Fussweg. Sein Abzug hielt die Zufahrt fest - aber nicht, WELCHE
         * ART sie war. Genau die Angabe entschied die Frage, und sie fehlte.
         * Ein Abzug, der die entscheidende Groesse nicht enthaelt, kostet
         * eine ganze Runde.
         */
        public string Art { get; set; }
        // Im Abzug vom 22.09. fehlten bei 2/2 gesetzten Zufahrten die
        // Achsfelder; ohne sie liess sich der Hindernistreffer nicht nachbauen.
        public DebugPoint2 AxisDirection { get; set; }
        public double? AxisLength { get; set; }

        internal static DebugEntrance From(Entrance entrance) => entrance == null
            ? null
            : new DebugEntrance
            {
                Edge = entrance.Edge,
                Along = entrance.Along,
                Corner = entrance.Corner,
                Art = entrance.Art.ToString(),
                AxisDirection = entrance.AxisDirection.HasValue
                    ? DebugPoint2.From(entrance.AxisDirection.Value) : null,
                AxisLength = entrance.AxisLength,
            };
    }

    internal sealed class DebugLayoutSection
    {
        public double L { get; set; }
        public int Bays { get; set; }
        public double Cap0 { get; set; }
        public double Cap1 { get; set; }
        public string End0 { get; set; }
        public string End1 { get; set; }
    }

    internal sealed class DebugSpecialStalls
    {
        public int Behindert { get; set; }
        public int Elektro { get; set; }
    }

    internal sealed class DebugAreaTransfer
    {
        public string PrefabName { get; set; }
        public DebugPrefab Prefab { get; set; }
        public int? GeometryRevision { get; set; }
        public bool PrefabEntityExists { get; set; }
        public bool HasSurfaceData { get; set; }
        public bool HasAreaData { get; set; }
        public bool HasAreaGeometryData { get; set; }
        public bool AreaArchetypeValid { get; set; }
        public int PlannedSurfaceCount { get; set; }
        public int SentSurfaceCount { get; set; }
        public DebugTerrainSamples TerrainSamples { get; set; }
        public DebugGeneratedSurface[] Surfaces { get; set; }
        public string AcceptanceMeaning { get; set; }
        public string Note { get; set; }
    }

    internal sealed class DebugTerrainSamples
    {
        public int SampleCount { get; set; }
        public float? Minimum { get; set; }
        public float? Maximum { get; set; }
        public float? Span { get; set; }
        public float LimitMeters { get; set; }
        public bool FiftyMeterLimitTriggered { get; set; }
        public string Note { get; set; }
    }

    internal sealed class DebugGeneratedSurface
    {
        public string Kind { get; set; }
        public int Index { get; set; }
        public DebugPrefab Prefab { get; set; }
        public DebugEntity DefinitionEntity { get; set; }
        public bool DefinitionStillExists { get; set; }
        public bool? DefinitionHasCreationDefinition { get; set; }
        public bool? DefinitionHasNodeBuffer { get; set; }
        public bool? DefinitionHasUpdated { get; set; }
        public DebugAreaNode[] NodesSentToCs2 { get; set; }
        public int CreatedFrame { get; set; }
        public bool? DefinitionMaterialized { get; set; }
        public DebugEntity MaterializedEntity { get; set; }
        public bool? GeometryAccepted { get; set; }
        public string Status { get; set; }
        public string Reason { get; set; }
        public string AreaFlags { get; set; }
        public int? AreaFlagsValue { get; set; }
        public string TempFlags { get; set; }
        public int? TempFlagsValue { get; set; }
        public int? TriangleCount { get; set; }
        public bool? TriangleIndicesValid { get; set; }
        public DebugAreaNode[] MaterializedNodes { get; set; }
    }

    internal sealed class DebugWorld
    {
        public bool Captured { get; set; }
        public string SelectionMethod { get; set; }
        public int CandidateLimitPerSpatialSearch { get; set; }
        public int GlobalAreaScanLimit { get; set; }
        public int CurveSubdivisions { get; set; }
        public int NetCandidateMatches { get; set; }
        public int NetCandidatesStored { get; set; }
        public bool NetsTruncated { get; set; }
        public int NetsIncluded { get; set; }
        public int AreaEntityCount { get; set; }
        public int AreasScanned { get; set; }
        public int AreaCandidateMatches { get; set; }
        public int AreaCandidatesStored { get; set; }
        public bool AreasTruncated { get; set; }
        public int AreasIncluded { get; set; }
        public double CaptureMilliseconds { get; set; }
        public DebugNet[] Nets { get; set; }
        public DebugExistingArea[] Areas { get; set; }
        public string Note { get; set; }
        public string Error { get; set; }
    }

    internal sealed class DebugNet
    {
        public DebugEntity Entity { get; set; }
        public DebugPrefab Prefab { get; set; }
        public string Kind { get; set; }
        public string IntersectionEvidence { get; set; }
        public DebugEntity Owner { get; set; }
        public DebugEntity StartNode { get; set; }
        public DebugPoint3 StartNodePosition { get; set; }
        public DebugEntity EndNode { get; set; }
        public DebugPoint3 EndNodePosition { get; set; }
        public DebugBezier Curve { get; set; }
        public float CurveLength { get; set; }
        public float? CompositionWidth { get; set; }
        public float? PrefabDefaultWidth { get; set; }
        public DebugBounds3 GeometryBounds { get; set; }
    }

    internal sealed class DebugExistingArea
    {
        public DebugEntity Entity { get; set; }
        public DebugPrefab Prefab { get; set; }
        public string Kind { get; set; }
        public string IntersectionEvidence { get; set; }
        public DebugEntity Owner { get; set; }
        public DebugAreaNode[] Nodes { get; set; }
        public string AreaFlags { get; set; }
        public int AreaFlagsValue { get; set; }
        public int? TriangleCount { get; set; }
        public DebugBounds3 GeometryBounds { get; set; }
        public DebugPoint3 GeometryCenter { get; set; }
        public float? SurfaceArea { get; set; }
    }

    /**
     * Bilanz eines Bauvorgangs: was geschickt wurde gegen das, was danach
     * wirklich als dauerhafte Flaeche in der Welt steht. Ohne diese
     * Gegenueberstellung sagt ein Abzug nur, was geplant war - nicht, was
     * davon angekommen ist.
     */
    internal sealed class DebugBuilt
    {
        public string Trigger { get; set; }
        public string BuiltAtLocal { get; set; }
        public int BuiltAtFrame { get; set; }
        public int PlannedGrass { get; set; }
        public int PlannedAsphalt { get; set; }
        public int InWorldGrass { get; set; }
        public int InWorldAsphalt { get; set; }
        public int InWorldWithoutTriangles { get; set; }
        public DebugEntity OwnerEntity { get; set; }
        public bool OwnerExists { get; set; }
        public int OwnerSubAreaCount { get; set; }
        public int OwnerSubNetCount { get; set; }
        public int OwnerSubObjectCount { get; set; }
        public int ChildrenWithOwner { get; set; }
        public int PlannedCourses { get; set; }
        public int PlannedBayDecals { get; set; }
        public int AttachedNets { get; set; }
        public int AttachedObjects { get; set; }
        /** Versuch mit eigener Lot-Besitzerflaeche (Shift+P), Standard aus. */
        public bool LotAreaOwner { get; set; }
        public string OwnerPrefabName { get; set; }
        public string GroupNote { get; set; }
        public double InWorldGrassArea { get; set; }
        public double InWorldAsphaltArea { get; set; }
        public string Note { get; set; }
    }

    internal sealed class DebugDiagnostics
    {
        public PreviewDiagnosticRecordDto[] PreviewMessages { get; set; }
        public string[] LayoutWarnings { get; set; }
        public object Cs2LogMessages { get; set; }
        public string Cs2LogMessagesNote { get; set; }
        public string[] Notes { get; set; }
    }

    internal sealed class PreviewDiagnosticRecordDto
    {
        public string TimestampUtc { get; set; }
        public string Severity { get; set; }
        public string Message { get; set; }
        public string ExceptionType { get; set; }
        public string ExceptionMessage { get; set; }
        public string StackTrace { get; set; }
    }

    internal sealed class DebugPrefab
    {
        public DebugEntity Entity { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public bool Resolved { get; set; }
        public string Note { get; set; }
    }

    internal sealed class DebugEntity
    {
        public int Index { get; set; }
        public int Version { get; set; }

        internal static DebugEntity From(Entity entity) => entity == Entity.Null
            ? null
            : new DebugEntity { Index = entity.Index, Version = entity.Version };
    }

    internal sealed class DebugPoint2
    {
        public float X { get; set; }
        public float Z { get; set; }

        internal static DebugPoint2 From(float2 point) => new DebugPoint2
            { X = point.x, Z = point.y };

        internal static DebugPoint2[] From(float2[] points)
        {
            if (points == null) return null;
            var result = new DebugPoint2[points.Length];
            for (var i = 0; i < points.Length; i++) result[i] = From(points[i]);
            return result;
        }

        internal static DebugPoint2[][] From(float2[][] polygons)
        {
            if (polygons == null) return null;
            var result = new DebugPoint2[polygons.Length][];
            for (var i = 0; i < polygons.Length; i++) result[i] = From(polygons[i]);
            return result;
        }
    }

    internal sealed class DebugPoint3
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        internal static DebugPoint3 From(float3 point) => new DebugPoint3
            { X = point.x, Y = point.y, Z = point.z };

        internal static DebugPoint3[] From(float3[] points)
        {
            if (points == null) return null;
            var result = new DebugPoint3[points.Length];
            for (var i = 0; i < points.Length; i++) result[i] = From(points[i]);
            return result;
        }
    }

    internal sealed class DebugAreaNode
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float? Elevation { get; set; }

        internal static DebugAreaNode[] From(float3[] points, float elevation)
        {
            if (points == null) return null;
            var result = new DebugAreaNode[points.Length];
            for (var i = 0; i < points.Length; i++)
            {
                result[i] = new DebugAreaNode
                {
                    X = points[i].x,
                    Y = points[i].y,
                    Z = points[i].z,
                    Elevation = elevation,
                };
            }
            return result;
        }

        internal static DebugAreaNode[] From(float3[] points, float[] elevations)
        {
            if (points == null) return null;
            var result = new DebugAreaNode[points.Length];
            for (var i = 0; i < points.Length; i++)
            {
                result[i] = new DebugAreaNode
                {
                    X = points[i].x,
                    Y = points[i].y,
                    Z = points[i].z,
                    Elevation = elevations != null && i < elevations.Length
                        ? (float?)elevations[i] : null,
                };
            }
            return result;
        }
    }

    internal sealed class DebugBezier
    {
        public DebugPoint3 A { get; set; }
        public DebugPoint3 B { get; set; }
        public DebugPoint3 C { get; set; }
        public DebugPoint3 D { get; set; }
    }

    internal sealed class DebugBounds3
    {
        public DebugPoint3 Minimum { get; set; }
        public DebugPoint3 Maximum { get; set; }
    }
}
