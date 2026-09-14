using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * Laufzeitmessung fuer die Materialgrenze einer Stadtstrasse.
     *
     * WARUM EINE EIGENE DATEI: `ParkingLotRoadSurvey` liegt bereits nahe an
     * der Projektgrenze von 800 Zeilen. Ausserdem darf dieses Messgeraet die
     * Ergebniswahl nicht versehentlich veraendern. Es liest dieselben aktiven
     * ECS-Daten, aus denen CS2 die konkrete Kante gebaut hat, und schreibt sie
     * mit dem Praefix `PLT-Kantenmessung` ins Log.
     *
     * Hier ist kein fremder Code enthalten. Die Feldbedeutungen wurden am
     * bereitgestellten Spielddekompilat nachvollzogen; implementiert ist nur
     * eine eigenstaendige Auflistung der Laufzeitkomponenten.
     */
    public sealed partial class ParkingLotToolSystem
    {
        private readonly HashSet<string> _gemeldeteFahrbahnkanten =
            new HashSet<string>();

        private struct ParkbauteilKandidat
        {
            internal bool Gefunden;
            internal string Bauteil;
            internal string Spur;
            internal float BauteilMin;
            internal float BauteilMax;
            internal float Spurposition;
            internal float Spurbreite;
            internal float Spurhuelle;
            internal float Bauteilrand;
            internal float Mittenabweichung;
            internal bool HatFahrspur;
            internal bool HatParkspur;
            internal bool HatFussspur;
            internal string Parkdaten;
        }

        /**
         * Meldet nur das am Ende wirklich ausgewaehlte Strassensegment.
         * Kante, aktive Composition und Seite bilden den Schluessel; mehrere
         * Zufahrten an derselben geraden Kante erzeugen deshalb keinen
         * Logsturm.
         */
        private void MeldeFahrbahnkantenDetails(Entity kante,
            Entity compositionPrefab, bool obenIstUnsere, float2 randpunkt,
            float2 innenrichtung, Weltspuraufmass weltspur,
            float querschnittRandbreite, float verwendeteRandbreite)
        {
            if (kante == Entity.Null || compositionPrefab == Entity.Null)
                return;

            var schluessel = kante.Index + ":" + kante.Version + "/"
                + compositionPrefab.Index + ":" + compositionPrefab.Version
                + "/" + (obenIstUnsere ? "oben" : "unten");
            if (!_gemeldeteFahrbahnkanten.Add(schluessel)) return;

            var strassenname = StrassenPrefabname(kante);
            var compositionName = KantenPrefabname(compositionPrefab);
            Mod.log.Info($"PLT-Kantenmessung: START Strasse '{strassenname}', "
                + $"Kante {kante.Index}:{kante.Version}, Composition "
                + $"'{compositionName}' {compositionPrefab.Index}:"
                + $"{compositionPrefab.Version}, unsere Seite "
                + $"{(obenIstUnsere ? "oben" : "unten")}, Aussenpunkt "
                + $"{randpunkt.x:F3}/{randpunkt.y:F3}, innen "
                + $"{innenrichtung.x:F4}/{innenrichtung.y:F4}.");

            MeldeCompositionDaten(compositionPrefab);

            var alleMin = float.MaxValue;
            var alleMax = float.MinValue;
            if (EntityManager.HasBuffer<NetCompositionPiece>(compositionPrefab))
            {
                var teile = EntityManager.GetBuffer<NetCompositionPiece>(
                    compositionPrefab, isReadOnly: true);
                for (var i = 0; i < teile.Length; i++)
                {
                    var teil = teile[i];
                    if (teil.m_Size.x <= 0f) continue;
                    alleMin = math.min(alleMin,
                        teil.m_Offset.x - teil.m_Size.x * 0.5f);
                    alleMax = math.max(alleMax,
                        teil.m_Offset.x + teil.m_Size.x * 0.5f);
                }
            }
            if (alleMax <= alleMin
                && EntityManager.HasComponent<NetCompositionData>(compositionPrefab))
            {
                var daten = EntityManager.GetComponentData<NetCompositionData>(
                    compositionPrefab);
                alleMin = -daten.m_Width * 0.5f;
                alleMax = daten.m_Width * 0.5f;
            }

            var gewaehltesSpurPrefab = Entity.Null;
            if (weltspur.Spur != Entity.Null
                && EntityManager.HasComponent<PrefabRef>(weltspur.Spur))
            {
                gewaehltesSpurPrefab = EntityManager
                    .GetComponentData<PrefabRef>(weltspur.Spur).m_Prefab;
            }

            var kandidat = new ParkbauteilKandidat
            {
                Mittenabweichung = float.MaxValue,
            };
            MeldeAktiveBauteile(compositionPrefab, obenIstUnsere,
                alleMin, alleMax, gewaehltesSpurPrefab,
                weltspur.IstParkspur, weltspur.RandspurMittenabstand,
                ref kandidat);
            MeldeCompositionFlaechen(compositionPrefab);
            MeldeCompositionSpuren(compositionPrefab);
            MeldeWeltspuren(kante, randpunkt, innenrichtung);
            MeldeKantenantwort(weltspur, querschnittRandbreite,
                verwendeteRandbreite, kandidat);
        }

        /**
         * Der Gesamtbereich zeigt, ob der Querschnitt mehrere
         * Oberflaechenhoehen enthaelt. Eine Querposition steckt in Bounds1
         * ausdruecklich nicht; deshalb wird daraus keine Kante errechnet.
         */
        private void MeldeCompositionDaten(Entity compositionPrefab)
        {
            if (!EntityManager.HasComponent<NetCompositionData>(compositionPrefab))
            {
                Mod.log.Info("PLT-Kantenmessung: COMPOSITION-DATEN fehlen.");
                return;
            }

            var daten = EntityManager.GetComponentData<NetCompositionData>(
                compositionPrefab);
            var randhoehen = daten.m_EdgeHeights;
            Mod.log.Info("PLT-Kantenmessung: COMPOSITION-DATEN "
                + $"Breite {daten.m_Width:F3} m, Mittelversatz "
                + $"{daten.m_MiddleOffset:F3} m, WidthOffset "
                + $"{daten.m_WidthOffset:F3} m, Hoehenspanne "
                + $"{daten.m_HeightRange.min:F3}.."
                + $"{daten.m_HeightRange.max:F3} m, Oberflaechenhoehe "
                + $"{daten.m_SurfaceHeight.min:F3}.."
                + $"{daten.m_SurfaceHeight.max:F3} m, Randhoehen "
                + $"{randhoehen.x:F3}/{randhoehen.y:F3}/"
                + $"{randhoehen.z:F3}/{randhoehen.w:F3} m.");
        }

        /**
         * Diese Bereiche stammen aus den BuildableNetPiece-Angaben der
         * tatsaechlich aktiven Bauteile. Sie werden roh gemeldet: Die Flags
         * benennen keine Materialien, also waere eine automatische Deutung
         * als Bordstein vor dem Fuenf-Faelle-Lauf wieder nur eine Annahme.
         */
        private void MeldeCompositionFlaechen(Entity compositionPrefab)
        {
            if (!EntityManager.HasBuffer<NetCompositionArea>(compositionPrefab))
            {
                Mod.log.Info("PLT-Kantenmessung: COMPOSITION-FLAECHEN fehlen.");
                return;
            }

            var flaechen = EntityManager.GetBuffer<NetCompositionArea>(
                compositionPrefab, isReadOnly: true);
            if (flaechen.Length == 0)
            {
                Mod.log.Info("PLT-Kantenmessung: COMPOSITION-FLAECHEN leer.");
                return;
            }

            for (var i = 0; i < flaechen.Length; i++)
            {
                var flaeche = flaechen[i];
                var min = flaeche.m_Position.x - flaeche.m_Width * 0.5f;
                var max = flaeche.m_Position.x + flaeche.m_Width * 0.5f;
                var snapMin = flaeche.m_SnapPosition.x
                    - flaeche.m_SnapWidth * 0.5f;
                var snapMax = flaeche.m_SnapPosition.x
                    + flaeche.m_SnapWidth * 0.5f;
                Mod.log.Info($"PLT-Kantenmessung: COMPOSITION-FLAECHE {i} "
                    + $"Bereich {min:F3}..{max:F3} m, Position "
                    + $"{flaeche.m_Position.x:F3}/"
                    + $"{flaeche.m_Position.y:F3}/"
                    + $"{flaeche.m_Position.z:F3}, Breite "
                    + $"{flaeche.m_Width:F3} m, Snapbereich "
                    + $"{snapMin:F3}..{snapMax:F3} m, SnapPosition "
                    + $"{flaeche.m_SnapPosition.x:F3}/"
                    + $"{flaeche.m_SnapPosition.y:F3}/"
                    + $"{flaeche.m_SnapPosition.z:F3}, SnapBreite "
                    + $"{flaeche.m_SnapWidth:F3} m, Flags "
                    + $"[{KantenFlags(flaeche.m_Flags)}].");
            }
        }

        /** Hoehenprofil und Hoehenspanne eines Querschnittsbauteils. */
        private string HoehenprofilVon(Entity piece, float offsetY)
        {
            if (piece == Entity.Null
                || !EntityManager.HasComponent<NetPieceData>(piece))
                return ", Hoehen unbekannt";
            var daten = EntityManager.GetComponentData<NetPieceData>(piece);
            var h = daten.m_SurfaceHeights;
            var compositionHoehen = h + offsetY;
            return $", Oberflaechenhoehen {h.x:F3}/{h.y:F3}/{h.z:F3}/{h.w:F3} m"
                + $", mit OffsetY {compositionHoehen.x:F3}/"
                + $"{compositionHoehen.y:F3}/{compositionHoehen.z:F3}/"
                + $"{compositionHoehen.w:F3} m"
                + $", Hoehenspanne {daten.m_HeightRange.min:F3}..{daten.m_HeightRange.max:F3} m"
                + $", PieceBreite {daten.m_Width:F2} m"
                + $", WidthOffset {daten.m_WidthOffset:F3} m";
        }

        private void MeldeAktiveBauteile(Entity compositionPrefab,
            bool obenIstUnsere, float alleMin, float alleMax,
            Entity gewaehltesSpurPrefab, bool gewaehlteIstParkspur,
            float weltMittenabstand, ref ParkbauteilKandidat kandidat)
        {
            if (!EntityManager.HasBuffer<NetCompositionPiece>(compositionPrefab))
            {
                Mod.log.Info("PLT-Kantenmessung: BAUTEILE fehlen.");
                return;
            }

            var teile = EntityManager.GetBuffer<NetCompositionPiece>(
                compositionPrefab, isReadOnly: true);
            for (var i = 0; i < teile.Length; i++)
            {
                var teil = teile[i];
                var teilMin = teil.m_Offset.x - teil.m_Size.x * 0.5f;
                var teilMax = teil.m_Offset.x + teil.m_Size.x * 0.5f;
                Mod.log.Info($"PLT-Kantenmessung: BAUTEIL {i} "
                    + $"'{KantenPrefabname(teil.m_Piece)}' "
                    + $"{teilMin:F2}..{teilMax:F2} m (Mitte "
                    + $"{teil.m_Offset.x:F2}, Breite {teil.m_Size.x:F2}), "
                    + $"OffsetY/Z {teil.m_Offset.y:F3}/"
                    + $"{teil.m_Offset.z:F3} m, "
                    + $"Section {teil.m_SectionIndex}, "
                    + $"SectionFlags [{KantenFlags(teil.m_SectionFlags)}], "
                    + $"PieceFlags [{KantenFlags(teil.m_PieceFlags)}]"
                    /*
                     * DAS HOEHENPROFIL IST DIE ERSTE NOCH UNGEPRUEFTE
                     * DIREKTE SPUR ZUM BORDSTEIN.
                     *
                     * Bis hierher wurde die Kante immer aus Spuren und
                     * Bauteilbreiten GESCHLOSSEN - mal richtig, mal nicht.
                     * Ein Bordstein ist aber eine HOEHENSTUFE: Gehweg
                     * erhoeht, Fahrbahn auf 0. `NetPieceData.m_SurfaceHeights`
                     * nennt vier Oberflaechenhoehen. Dass
                     * `NetCompositionHelpers` sie bei umgekehrten Abschnitten
                     * als `.yxwz` spiegelt, belegt eine Abhaengigkeit von der
                     * Querausrichtung.
                     *
                     * Noch NICHT belegt ist, dass jede sichtbare
                     * Bordsteinstufe in diesen vier Werten enthalten ist. Das
                     * entscheidet erst der Lauf ueber alle fuenf Strassen.
                     */
                    + HoehenprofilVon(teil.m_Piece, teil.m_Offset.y) + ".");

                if (!EntityManager.HasBuffer<NetPieceLane>(teil.m_Piece))
                    continue;

                var spuren = EntityManager.GetBuffer<NetPieceLane>(
                    teil.m_Piece, isReadOnly: true);
                var hatFahrspur = false;
                var hatParkspur = false;
                var hatFussspur = false;
                var passendePosition = 0f;
                var passendeBreite = 0f;
                var passendeSpur = string.Empty;
                var passendeParkdaten = string.Empty;
                var passendeAbweichung = float.MaxValue;

                for (var j = 0; j < spuren.Length; j++)
                {
                    var spur = spuren[j];
                    var position = spur.m_Position.x;
                    if ((teil.m_SectionFlags & NetSectionFlags.Invert) != 0)
                        position = -position;
                    position += teil.m_Offset.x;

                    var flags = spur.m_ExtraFlags;
                    var breite = 0f;
                    var hatDaten = spur.m_Lane != Entity.Null
                        && EntityManager.HasComponent<NetLaneData>(spur.m_Lane);
                    if (hatDaten)
                    {
                        var daten = EntityManager
                            .GetComponentData<NetLaneData>(spur.m_Lane);
                        flags |= daten.m_Flags;
                        breite = daten.m_Width;
                    }

                    hatFahrspur |= (flags & LaneFlags.Road) != 0;
                    hatParkspur |= (flags & LaneFlags.Parking) != 0;
                    hatFussspur |= (flags & LaneFlags.Pedestrian) != 0;
                    var parkdaten = KantenParkdaten(spur.m_Lane);
                    Mod.log.Info($"PLT-Kantenmessung: BAUTEILSPUR {i}.{j} "
                        + $"'{KantenPrefabname(spur.m_Lane)}', Position "
                        + $"{position:F2} m (im Piece {spur.m_Position.x:F2}), "
                        + (hatDaten ? $"Breite {breite:F2} m" : "Breite FEHLT")
                        + $", Flags [{KantenFlags(flags)}], ExtraFlags "
                        + $"[{KantenFlags(spur.m_ExtraFlags)}]{parkdaten}.");

                    /*
                     * LaneSystem darf fuer die Weltspur eine kompatible
                     * Prefab-Variante einsetzen. Deshalb ist Entity-Gleichheit
                     * der beste Treffer, aber keine harte Schranke. Bei einer
                     * abweichenden Variante entscheidet derselbe Spurtyp plus
                     * der gemessene Mittenabstand, nicht der Prefabname.
                     */
                    var gleicherPrefab = gewaehltesSpurPrefab != Entity.Null
                        && spur.m_Lane == gewaehltesSpurPrefab;
                    var gleicherTyp = gewaehlteIstParkspur
                        ? (flags & LaneFlags.Parking) != 0
                        : (flags & LaneFlags.Road) != 0;
                    if (!gleicherPrefab && !gleicherTyp) continue;
                    var lokalerMittenabstand = obenIstUnsere
                        ? alleMax - position : position - alleMin;
                    var abweichung = math.abs(
                        lokalerMittenabstand - weltMittenabstand);
                    if (abweichung >= passendeAbweichung) continue;
                    passendeAbweichung = abweichung;
                    passendePosition = position;
                    passendeBreite = breite;
                    passendeSpur = KantenPrefabname(spur.m_Lane);
                    passendeParkdaten = parkdaten;
                }

                if (passendeAbweichung >= kandidat.Mittenabweichung) continue;
                var spurhuelle = float.NaN;
                if (passendeBreite > 0f)
                {
                    spurhuelle = obenIstUnsere
                        ? alleMax - (passendePosition + passendeBreite * 0.5f)
                        : (passendePosition - passendeBreite * 0.5f) - alleMin;
                }
                kandidat.Gefunden = true;
                kandidat.Bauteil = KantenPrefabname(teil.m_Piece);
                kandidat.Spur = passendeSpur;
                kandidat.BauteilMin = teilMin;
                kandidat.BauteilMax = teilMax;
                kandidat.Spurposition = passendePosition;
                kandidat.Spurbreite = passendeBreite;
                kandidat.Spurhuelle = spurhuelle;
                kandidat.Bauteilrand = obenIstUnsere
                    ? alleMax - teilMax : teilMin - alleMin;
                kandidat.Mittenabweichung = passendeAbweichung;
                kandidat.HatFahrspur = hatFahrspur;
                kandidat.HatParkspur = hatParkspur;
                kandidat.HatFussspur = hatFussspur;
                kandidat.Parkdaten = passendeParkdaten;
            }
        }

        private void MeldeCompositionSpuren(Entity compositionPrefab)
        {
            if (!EntityManager.HasBuffer<NetCompositionLane>(compositionPrefab))
            {
                Mod.log.Info("PLT-Kantenmessung: COMPOSITION-SPUREN fehlen.");
                return;
            }

            var spuren = EntityManager.GetBuffer<NetCompositionLane>(
                compositionPrefab, isReadOnly: true);
            for (var i = 0; i < spuren.Length; i++)
            {
                var spur = spuren[i];
                var hatDaten = spur.m_Lane != Entity.Null
                    && EntityManager.HasComponent<NetLaneData>(spur.m_Lane);
                var breite = hatDaten
                    ? EntityManager.GetComponentData<NetLaneData>(spur.m_Lane).m_Width
                    : 0f;
                Mod.log.Info($"PLT-Kantenmessung: COMPOSITION-SPUR {i} "
                    + $"'{KantenPrefabname(spur.m_Lane)}', Position "
                    + $"{spur.m_Position.x:F2}/{spur.m_Position.y:F2}/"
                    + $"{spur.m_Position.z:F2}, "
                    + (hatDaten ? $"Breite {breite:F2} m" : "Breite FEHLT")
                    + $", Flags [{KantenFlags(spur.m_Flags)}], Carriageway "
                    + $"{spur.m_Carriageway}, Group {spur.m_Group}, "
                    + $"Index {spur.m_Index}{KantenParkdaten(spur.m_Lane)}.");
            }
        }

        private void MeldeWeltspuren(Entity kante, float2 randpunkt,
            float2 innenrichtung)
        {
            if (!EntityManager.HasBuffer<Game.Net.SubLane>(kante))
            {
                Mod.log.Info("PLT-Kantenmessung: WELTSPUREN fehlen.");
                return;
            }

            var spuren = EntityManager.GetBuffer<Game.Net.SubLane>(
                kante, isReadOnly: true);
            var sonde = new float3(randpunkt.x, 0f, randpunkt.y);
            for (var i = 0; i < spuren.Length; i++)
            {
                var entity = spuren[i].m_SubLane;
                var prefab = Entity.Null;
                if (entity != Entity.Null && EntityManager.Exists(entity)
                    && EntityManager.HasComponent<PrefabRef>(entity))
                {
                    prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                }
                var hatDaten = prefab != Entity.Null
                    && EntityManager.HasComponent<NetLaneData>(prefab);
                var flags = hatDaten
                    ? EntityManager.GetComponentData<NetLaneData>(prefab).m_Flags
                    : 0;
                var breite = hatDaten
                    ? EntityManager.GetComponentData<NetLaneData>(prefab).m_Width
                    : 0f;

                var lage = default(float2);
                var abstand = float.NaN;
                var t = float.NaN;
                var innen = false;
                if (entity != Entity.Null && EntityManager.Exists(entity)
                    && EntityManager.HasComponent<Game.Net.Curve>(entity))
                {
                    var kurve = EntityManager
                        .GetComponentData<Game.Net.Curve>(entity).m_Bezier;
                    kurve.a.y = 0f; kurve.b.y = 0f;
                    kurve.c.y = 0f; kurve.d.y = 0f;
                    abstand = MathUtils.Distance(kurve, sonde, out t);
                    var lage3 = MathUtils.Position(kurve, t);
                    lage = new float2(lage3.x, lage3.z);
                    innen = math.dot(lage - randpunkt, innenrichtung) > 0f;
                }

                var runtimeParken = string.Empty;
                if (entity != Entity.Null && EntityManager.Exists(entity)
                    && EntityManager.HasComponent<Game.Net.ParkingLane>(entity))
                {
                    var parking = EntityManager
                        .GetComponentData<Game.Net.ParkingLane>(entity);
                    runtimeParken = $", ParkingLaneFlags [{parking.m_Flags}]";
                }
                Mod.log.Info($"PLT-Kantenmessung: WELTSPUR {i} "
                    + $"{entity.Index}:{entity.Version} "
                    + $"'{KantenPrefabname(prefab)}', Mitte "
                    + $"{lage.x:F3}/{lage.y:F3}, Abstand {abstand:F3} m, "
                    + $"t {t:F4}, innen {innen}, "
                    + (hatDaten ? $"Breite {breite:F2} m" : "Breite FEHLT")
                    + $", Flags [{KantenFlags(flags)}]{runtimeParken}"
                    + $"{KantenParkdaten(prefab)}.");
            }
        }

        private void MeldeKantenantwort(Weltspuraufmass weltspur,
            float querschnittRandbreite, float verwendeteRandbreite,
            ParkbauteilKandidat kandidat)
        {
            if (!weltspur.HatRand)
            {
                Mod.log.Info("PLT-Kantenmessung: ANTWORT keine Weltspur mit "
                    + $"echter Breite; verwendet {verwendeteRandbreite:F2} m "
                    + $"aus dem Querschnitt.");
                return;
            }

            var art = weltspur.IstParkspur ? "Parkspur" : "Fahrspur";
            var grund = $"PLT-Kantenmessung: ANTWORT aktuell "
                + $"{verwendeteRandbreite:F2} m aus {art}-Mitte "
                + $"{weltspur.RandspurMittenabstand:F2} minus halbe "
                + $"NetLaneData-Breite {weltspur.Breite:F2}; "
                + $"Querschnitt {querschnittRandbreite:F2} m";
            if (!weltspur.IstParkspur || !kandidat.Gefunden)
            {
                Mod.log.Info(grund + (weltspur.IstParkspur
                    ? "; zu dieser Parkspur wurde kein aktives Bauteil gefunden."
                    : "; die ausgewaehlte Weltspur ist keine Parkspur."));
                return;
            }

            var belegung = kandidat.HatFussspur
                ? "Parken+Fussweg im selben Bauteil"
                : (kandidat.HatFahrspur
                    ? "Parken+Fahrspur im selben Bauteil"
                    : "eigenes Parkbauteil ohne Fuss-/Fahrspur");
            Mod.log.Info(grund + $"; Host '{kandidat.Bauteil}' "
                + $"{kandidat.BauteilMin:F2}..{kandidat.BauteilMax:F2} m, "
                + $"{belegung}; lokale Spurhuelle {kandidat.Spurhuelle:F2} m, "
                + $"lokaler Bauteilrand {kandidat.Bauteilrand:F2} m, "
                + $"Differenz Bauteilrand-aktuell "
                + $"{kandidat.Bauteilrand - verwendeteRandbreite:+0.00;-0.00;0.00} m"
                + $"{kandidat.Parkdaten}.");
        }

        private string KantenParkdaten(Entity lanePrefab)
        {
            if (lanePrefab == Entity.Null
                || !EntityManager.HasComponent<ParkingLaneData>(lanePrefab))
                return string.Empty;
            var daten = EntityManager.GetComponentData<ParkingLaneData>(lanePrefab);
            return $", ParkingData Slot {daten.m_SlotSize.x:F2} x "
                + $"{daten.m_SlotSize.y:F2} m, Winkel "
                + $"{daten.m_SlotAngle:F4} rad/{math.degrees(daten.m_SlotAngle):F2} Grad, "
                + $"Intervall {daten.m_SlotInterval:F4} m, "
                + $"MaxCar {daten.m_MaxCarLength:F2} m, "
                + $"RoadTypes [{daten.m_RoadTypes}]";
        }

        private string KantenPrefabname(Entity prefab)
        {
            if (prefab == Entity.Null) return "null";
            try
            {
                var system = World.GetExistingSystemManaged<PrefabSystem>();
                return system != null
                    ? system.GetPrefabName(prefab)
                    : prefab.Index + ":" + prefab.Version;
            }
            catch (Exception)
            {
                return prefab.Index + ":" + prefab.Version;
            }
        }

        private static string KantenFlags<T>(T flags) where T : struct
        {
            var text = flags.ToString();
            return string.IsNullOrEmpty(text) || text == "0" ? "keine" : text;
        }
    }
}
