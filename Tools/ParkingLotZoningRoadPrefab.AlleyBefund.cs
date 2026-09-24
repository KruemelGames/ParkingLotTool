using System;
using System.Collections.Generic;
using System.Text;
using Colossal.Serialization.Entities;
using Game;
using Game.Prefabs;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotZoningRoadPrefabSystem
    {
        /*
         * Die Sitzung vom 2026-09-24 meldete 2 entfernte SubObjects je
         * Alley-Klon, aber keinen Zustand der Vanilla-Alley. Darum werden
         * Original und Klon an vier Stellen desselben Startlaufs gemessen.
         * Der Ladeabschluss ist ein zweiter Messpunkt fuer denselben Befund.
         */
        [Preserve]
        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (mode == GameMode.Game)
            {
                MesseVanillaAlley("Laden abgeschlossen");
                foreach (var eintrag in _eintraege.Values)
                    MesseAlleyKlonMeshes(eintrag, "Laden abgeschlossen");
            }
        }

        private void MesseAlleyKlonMeshes(Eintrag eintrag, string zeitpunkt)
        {
            if (!eintrag.Name.EndsWith("(Alley)", StringComparison.Ordinal))
                return;
            try
            {
                var entity = eintrag.KlonEntity;
                if (entity == Entity.Null || !EntityManager.Exists(entity)
                    || !EntityManager.HasBuffer<NetGeometryComposition>(entity))
                    return;
                var referenzen = EntityManager.GetBuffer<NetGeometryComposition>(
                    entity, true);
                var details = new List<string>();
                var enden = 0;
                foreach (var referenz in referenzen)
                {
                    var komposition = referenz.m_Composition;
                    if (komposition == Entity.Null
                        || !EntityManager.Exists(komposition)
                        || !EntityManager.HasComponent<NetCompositionData>(komposition))
                        continue;
                    var flags = EntityManager.GetComponentData<NetCompositionData>(
                        komposition).m_Flags.m_General;
                    if ((flags & (CompositionFlags.General.DeadEnd
                            | CompositionFlags.General.Intersection)) == 0)
                        continue;
                    enden++;
                    var mesh = EntityManager.HasComponent<NetCompositionMeshRef>(
                        komposition)
                        ? EntityManager.GetComponentData<NetCompositionMeshRef>(
                            komposition).m_Mesh : Entity.Null;
                    if (details.Count < 12)
                        details.Add(komposition + "/" + flags + "/Mesh=" + mesh);
                    if (zeitpunkt == "Laden abgeschlossen"
                        && (flags & CompositionFlags.General.DeadEnd) != 0)
                        MesseAlleyMeshPieces(komposition, eintrag.Name);
                }
                Mod.log.Info("PLT-Alley-Klonmeshes " + zeitpunkt + " '"
                    + eintrag.Name + "': Endkompositionen=" + enden
                    + " [" + string.Join(" | ", details) + "].");
            }
            catch (Exception fehler)
            {
                Mod.log.Warn("PLT-Alley-Klonmeshes " + zeitpunkt + " '"
                    + eintrag.Name + "': Messung fehlgeschlagen: " + fehler);
            }
        }

        private void MesseKlonabstand(RoadPrefab original, RoadPrefab klon,
            string zeitpunkt)
        {
            try { MesseKlonabstandKern(original, klon, zeitpunkt); }
            catch (Exception fehler)
            {
                Mod.log.Warn("PLT-Alley-Klonabstand " + zeitpunkt
                    + ": Messung fehlgeschlagen: " + fehler);
            }
        }

        private void MesseKlonabstandKern(RoadPrefab original, RoadPrefab klon,
            string zeitpunkt)
        {
            if (original == null || original.name != "Alley" || klon == null)
                return;
            var a = original.GetComponent<NetSubObjects>();
            var b = klon.GetComponent<NetSubObjects>();
            var aListe = a?.m_SubObjects;
            var bListe = b?.m_SubObjects;
            var aSekt = original.m_Sections;
            var bSekt = klon.m_Sections;
            Mod.log.Info("PLT-Alley-Klonabstand " + zeitpunkt + " '" + klon.name
                + "': Bauteil gleich=" + ReferenceEquals(a, b)
                + ", SubObject-Array gleich=" + ReferenceEquals(aListe, bListe)
                + ", erstes SubObject gleich="
                + (aListe != null && bListe != null && aListe.Length > 0
                    && bListe.Length > 0 && ReferenceEquals(aListe[0], bListe[0]))
                + ", Sections-Array gleich=" + ReferenceEquals(aSekt, bSekt)
                + ", erster Section-Eintrag gleich="
                + (aSekt != null && bSekt != null && aSekt.Length > 0
                    && bSekt.Length > 0 && ReferenceEquals(aSekt[0], bSekt[0]))
                + ", Section-Prefab gleich="
                + (aSekt != null && bSekt != null && aSekt.Length > 0
                    && bSekt.Length > 0
                    && ReferenceEquals(aSekt[0]?.m_Section,
                        bSekt[0]?.m_Section)) + ".");
        }

        private void MesseVanillaAlley(string zeitpunkt, RoadPrefab original = null)
        {
            try { MesseVanillaAlleyKern(zeitpunkt, original); }
            catch (Exception fehler)
            {
                Mod.log.Warn("PLT-Alley-Befund " + zeitpunkt
                    + ": Messung fehlgeschlagen: " + fehler);
            }
        }

        private void MesseVanillaAlleyKern(string zeitpunkt,
            RoadPrefab original)
        {
            if (original == null)
            {
                if (!_prefabSystem.TryGetPrefab(
                        new PrefabID(nameof(RoadPrefab), "Alley"), out var gefunden)
                    || !(gefunden is RoadPrefab road))
                {
                    Mod.log.Info("PLT-Alley-Befund " + zeitpunkt
                        + ": Vanilla-Prefab noch nicht vorhanden.");
                    return;
                }
                original = road;
            }
            if (original.name != "Alley") return;

            var entity = _prefabSystem.GetEntity(original);
            var sub = original.GetComponent<NetSubObjects>()?.m_SubObjects;
            var sektionen = original.m_Sections;
            var subText = new List<string>();
            if (sub != null)
                for (var i = 0; i < sub.Length; i++)
                {
                    var info = sub[i];
                    subText.Add(i + ":" + (info?.m_Object?.name ?? "null")
                        + "/" + info?.m_Placement
                        + "/DeadEnd=" + info?.m_RequireDeadEnd
                        + "/Orphan=" + info?.m_RequireOrphan);
                }

            var versteckt = 0;
            if (sektionen != null)
                for (var i = 0; i < sektionen.Length; i++)
                    if (sektionen[i] != null && sektionen[i].m_HiddenLayers != 0)
                        versteckt++;
            var geo = entity != Entity.Null && EntityManager.Exists(entity)
                && EntityManager.HasComponent<NetGeometryData>(entity)
                ? EntityManager.GetComponentData<NetGeometryData>(entity)
                : default;
            var ecsSub = entity != Entity.Null && EntityManager.Exists(entity)
                && EntityManager.HasBuffer<SubObject>(entity)
                ? EntityManager.GetBuffer<SubObject>(entity, true).Length : -1;
            var ecsSekt = entity != Entity.Null && EntityManager.Exists(entity)
                && EntityManager.HasBuffer<NetGeometrySection>(entity)
                ? EntityManager.GetBuffer<NetGeometrySection>(entity, true).Length : -1;
            var kompositionen = entity != Entity.Null && EntityManager.Exists(entity)
                && EntityManager.HasBuffer<NetGeometryComposition>(entity)
                ? EntityManager.GetBuffer<NetGeometryComposition>(entity, true).Length : -1;

            Mod.log.Info("PLT-Alley-Befund " + zeitpunkt + ": Prefab=" + entity
                + ", SubObjects=" + (sub?.Length ?? -1) + " ["
                + string.Join(", ", subText) + "]"
                + ", ECS-SubObjects=" + ecsSub
                + ", Sections=" + (sektionen?.Length ?? -1)
                + ", versteckt=" + versteckt
                + ", ECS-Sections=" + ecsSekt
                + ", Kompositionen=" + kompositionen
                + ", Breite=" + geo.m_DefaultWidth.ToString("F2")
                + ", Flags=" + geo.m_Flags + ".");

            var teile = new StringBuilder();
            var gesehen = new HashSet<NetPiecePrefab>();
            if (sektionen != null)
                foreach (var info in sektionen)
                {
                    var stuecke = info?.m_Section?.m_Pieces;
                    if (stuecke == null) continue;
                    foreach (var stueck in stuecke)
                    {
                        var piece = stueck?.m_Piece;
                        if (piece == null || !gesehen.Add(piece)) continue;
                        if (!piece.name.Contains("Intersection")
                            && !piece.name.Contains("Middle")
                            && !piece.name.Contains("Ending")) continue;
                        /* 21:39:03: 9 fruehe GetEntity-Aufrufe warfen vor der
                         * Piece-Anmeldung; TryGetEntity laesst die Messung laufen. */
                        _prefabSystem.TryGetEntity(piece, out var e);
                        if (teile.Length != 0) teile.Append(" | ");
                        teile.Append(piece.name).Append(':').Append(e);
                        if (e == Entity.Null || !EntityManager.Exists(e)) continue;
                        if (EntityManager.HasComponent<NetPieceData>(e))
                        {
                            var p = EntityManager.GetComponentData<NetPieceData>(e);
                            teile.Append(" W=").Append(p.m_Width.ToString("F2"))
                                .Append(" H=").Append(p.m_SurfaceHeights);
                        }
                        if (EntityManager.HasComponent<NetTerrainData>(e))
                        {
                            var t = EntityManager.GetComponentData<NetTerrainData>(e);
                            teile.Append(" Clip=").Append(t.m_ClipHeightOffset);
                        }
                    }
                }
            Mod.log.Info("PLT-Alley-Endstuecke " + zeitpunkt + ": "
                + gesehen.Count + " verschiedene Pieces, davon Ende/Mitte ["
                + teile + "].");

            var endKnoten = 0;
            var einmuendungen = 0;
            var endTeile = 0;
            var endMeshTeile = 0;
            var endVersteckt = 0;
            var endOhneMesh = 0;
            var endDetails = new List<string>();
            if (entity != Entity.Null && EntityManager.Exists(entity)
                && EntityManager.HasBuffer<NetGeometryComposition>(entity))
            {
                var referenzen = EntityManager.GetBuffer<NetGeometryComposition>(
                    entity, true);
                foreach (var referenz in referenzen)
                {
                    var komposition = referenz.m_Composition;
                    if (komposition == Entity.Null
                        || !EntityManager.Exists(komposition)
                        || !EntityManager.HasComponent<NetCompositionData>(komposition))
                        continue;
                    var flags = EntityManager.GetComponentData<NetCompositionData>(
                        komposition).m_Flags.m_General;
                    var sackgasse = (flags & CompositionFlags.General.DeadEnd) != 0;
                    var einmuendung = (flags & CompositionFlags.General.Intersection) != 0;
                    if (!sackgasse && !einmuendung) continue;
                    if (sackgasse) endKnoten++;
                    if (einmuendung) einmuendungen++;
                    var mesh = EntityManager.HasComponent<NetCompositionMeshRef>(
                        komposition)
                        ? EntityManager.GetComponentData<NetCompositionMeshRef>(
                            komposition).m_Mesh : Entity.Null;
                    if (mesh == Entity.Null) endOhneMesh++;
                    var sichtbar = 0;
                    if (EntityManager.HasBuffer<NetCompositionPiece>(komposition))
                    {
                        var puffer = EntityManager.GetBuffer<NetCompositionPiece>(
                            komposition, true);
                        endTeile += puffer.Length;
                        foreach (var p in puffer)
                        {
                            if ((p.m_SectionFlags & NetSectionFlags.Hidden) != 0)
                            {
                                endVersteckt++;
                                continue;
                            }
                            if ((p.m_PieceFlags & NetPieceFlags.HasMesh) != 0)
                            {
                                sichtbar++;
                                endMeshTeile++;
                            }
                        }
                    }
                    if (endDetails.Count < 12)
                        endDetails.Add(komposition + "/" + flags
                            + "/Mesh=" + mesh + "/sichtbar=" + sichtbar);
                    if (zeitpunkt == "Laden abgeschlossen" && sackgasse)
                        MesseAlleyMeshPieces(komposition, "Vanilla Alley");
                }
            }
            Mod.log.Info("PLT-Alley-Endkompositionen " + zeitpunkt
                + ": DeadEnd=" + endKnoten + ", Intersection=" + einmuendungen
                + ", Pieces=" + endTeile + ", sichtbare Mesh-Pieces="
                + endMeshTeile + ", Hidden=" + endVersteckt
                + ", ohne Mesh-Ref=" + endOhneMesh
                + " [" + string.Join(" | ", endDetails) + "].");
        }

        private void MesseAlleyMeshPieces(Entity komposition, string herkunft)
        {
            /*
             * Im kaputten Lauf teilten 2 Vanilla-DeadEnds und 7 Klon-DeadEnds
             * Mesh 400077; die bisherigen 6 sichtbaren Pieces hatten keine
             * Namen und Flags. Beide Puffer muessen getrennt ins Protokoll.
             */
            var referenz = EntityManager.HasComponent<NetCompositionMeshRef>(
                komposition)
                ? EntityManager.GetComponentData<NetCompositionMeshRef>(
                    komposition) : default;
            var mesh = referenz.m_Mesh;
            Mod.log.Info("PLT-Alley-Meshpieces '" + herkunft + "' " + komposition
                + ": Mesh=" + mesh + ", Rotate=" + referenz.m_Rotate
                + ", Komposition=" + BeschreibeAlleyMeshPieces(komposition)
                + ", Modell=" + BeschreibeAlleyMeshPieces(mesh) + ".");
        }

        private string BeschreibeAlleyMeshPieces(Entity entity)
        {
            if (entity == Entity.Null || !EntityManager.Exists(entity)
                || !EntityManager.HasBuffer<NetCompositionPiece>(entity))
                return "kein Puffer";
            var puffer = EntityManager.GetBuffer<NetCompositionPiece>(entity,
                true);
            var details = new List<string>();
            foreach (var piece in puffer)
            {
                if ((piece.m_PieceFlags & NetPieceFlags.HasMesh) == 0
                    || (piece.m_SectionFlags & NetSectionFlags.Hidden) != 0)
                    continue;
                var name = _prefabSystem.TryGetPrefab<NetPiecePrefab>(
                    piece.m_Piece, out var prefab)
                    ? prefab.name : piece.m_Piece.ToString();
                details.Add(name + "/" + piece.m_Piece
                    + "/Sektion=" + piece.m_SectionFlags
                    + "/Piece=" + piece.m_PieceFlags
                    + "/Offset=" + piece.m_Offset);
            }
            return details.Count + " [" + string.Join(" | ", details) + "]";
        }
    }
}
