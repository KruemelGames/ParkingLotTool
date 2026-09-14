using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using Game;
using Game.Areas;
using Game.Common;
using Game.Rendering;
using Game.SceneFlow;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /** Lebenszyklus des Bearbeiten-Modus und Speicherung seines Bauzettels. */
    public sealed partial class ParkingLotToolSystem
    {
        private EntityQuery _editOwnerParts;
        private EntityQuery _editRelatedParts;
        private EntityQuery _editEnabledCompanions;
        private ParkingLotEconomySystem _editEconomySystem;

        private Entity _pendingEditLot = Entity.Null;
        private Entity _editLot = Entity.Null;
        private Entity _replacementNewLot = Entity.Null;
        private Entity _replacementNewCarrier = Entity.Null;
        private int _replacementStartedFrame;
        private readonly HashSet<Entity> _hiddenByEdit = new HashSet<Entity>();
        /**
         * Teile, die beim Uebernehmen ausgeblendet waren und mit dem alten
         * Lot verschwinden SOLLEN. Wer den Abriss ueberlebt, wird wieder
         * sichtbar gemacht - siehe `PruefeUeberlebendeAusgeblendete`.
         */
        private readonly HashSet<Entity> _hiddenNachUebernahme =
            new HashSet<Entity>();
        private readonly List<Entity> _hiddenPruefliste = new List<Entity>();

        private bool _editBaselinePending;
        private long _editBaselineSignature;
        private bool _editParkingFeeKnown;
        private int _editParkingFee;
        private bool _editBuildingEconomyEnabled;
        private bool _replacementEconomyTransferred;

        private bool IsEditing => _editLot != Entity.Null;

        private void InitializeEditing()
        {
            _editOwnerParts = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Owner>() },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            _editRelatedParts = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ParkingLotPartRelation>() },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            _editEnabledCompanions = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ParkingLotPartRelation>(),
                    ComponentType.ReadOnly<ParkingLotBuildingEconomyEnabled>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            _editEconomySystem = World
                .GetOrCreateSystemManaged<ParkingLotEconomySystem>();
            if (GameManager.instance != null)
                GameManager.instance.onGameSaveLoad += OnEditGameSaveLoad;
        }

        internal void RequestEdit(Entity lot)
        {
            if (lot == Entity.Null || !EntityManager.Exists(lot)
                || EntityManager.HasComponent<Deleted>(lot)
                || EntityManager.HasComponent<Temp>(lot)
                || !HasCompleteBuildReceipt(lot))
            {
                _uiSystem?.SetStatus(ParkingLotTexte.T(
                    "Dieser Parkplatz hat keinen vollständigen Bauzettel.",
                    "This parking lot has no complete build receipt."));
                return;
            }
            if (IsEditing || _pendingEditLot != Entity.Null)
            {
                _uiSystem?.SetStatus(ParkingLotTexte.T(
                    "Es wird bereits ein Parkplatz bearbeitet.",
                    "A parking lot is already being edited."));
                return;
            }

            _pendingEditLot = lot;
            if (m_ToolSystem.activeTool != this)
                m_ToolSystem.activeTool = this;
        }

        private bool HasCompleteBuildReceipt(Entity lot)
            => EntityManager.HasComponent<ParkingLotCarrierReference>(lot)
               && EntityManager.HasComponent<ParkingLotBuildReceipt>(lot)
               && EntityManager.HasBuffer<ParkingLotBuildPoint>(lot)
               && EntityManager.HasBuffer<ParkingLotBuildEntrance>(lot)
               && EntityManager.HasBuffer<ParkingLotBuildText>(lot);

        /**
         * Der Seitenplan aus dem gelesenen Bauzettel - Zwischenlager, bis
         * `ResetSelection` durch ist. Siehe `TryReadBuildReceipt`.
         */
        private List<(float2 A, float2 B, bool Links, bool Aus)>
            _zoningSeitenplanAusZettel;
        private List<ParkingGeometry.RandzoningLinie> _randzoningAusZettel;

        /** Wird im ersten Werkzeugframe nach dem UI-Klick ausgefuehrt. */
        private bool TryBeginPendingEdit()
        {
            if (_pendingEditLot == Entity.Null) return false;
            var lot = _pendingEditLot;
            _pendingEditLot = Entity.Null;
            if (!TryReadBuildReceipt(lot, out var receipt, out var points,
                    out var entrances, out var alignments, out var cuts,
                    out var zonen,
                    out var surfaceRoad,
                    out var surfaceDecoration, out var surfaceZoning,
                    out var reason))
            {
                _uiSystem?.SetStatus(ParkingLotTexte.T(
                    "Bearbeiten nicht möglich: " + reason,
                    "Cannot edit: " + reason));
                ResetSelection();
                return true;
            }

            ResetSelection();
            _uiSystem?.LoadBuildReceipt(receipt, surfaceRoad, surfaceDecoration,
                surfaceZoning);
            LoadVegetation(lot);
            /*
             * Die Bezugslinie gehoert zum Bauzettel, nicht zu den Reglern -
             * deshalb hier und nicht in `LoadBuildReceipt`. `NaN` und
             * Bauzettel der Fassung 1 bedeuten beide: keine gewaehlt.
             */
            // Erst die Schnitte, dann die Zuweisungen: die Zuweisungen
            // suchen ihre Teilflaeche, und die entsteht aus den Schnitten.
            SetzeTrennschnitte(cuts);
            // Die Parzellen haengen an keiner Teilflaeche und koennen
            // deshalb hier stehen, wo sie hingehoeren: gleich neben dem, was
            // sie im Bauzettel begleitet.
            SetzeZoningflaechen(zonen);
            // Der Seitenplan gehoert zu diesen Flaechen und muss NACH
            // `ResetSelection` kommen - das raeumt ihn sonst gleich wieder weg.
            LadeZoningSeitenplan(_zoningSeitenplanAusZettel);
            SetzeRandzoning(_randzoningAusZettel);
            _uiSystem?.SetZoningZahlen(_zoningflaechen.Count,
                _zoningflaechen.Sum(f => f.Parzellen));
            if (alignments != null)
                SetzeAusrichtungen(alignments);
            else
                SetzeAusrichtwinkel(double.IsNaN(receipt.Ausrichtwinkel)
                    ? (double?)null : receipt.Ausrichtwinkel,
                    new float2(receipt.AusrichtAx, receipt.AusrichtAz),
                    new float2(receipt.AusrichtBx, receipt.AusrichtBz));
            _seenSettingsRevision = _uiSystem?.Revision ?? 0;
            for (var i = 0; i < points.Length; i++)
            {
                _worldPoints.Add(points[i]);
                _points.Add(points[i].xz);
            }
            _entrances.AddRange(entrances);
            _closed = true;
            _polygonTouched = true;
            _layoutDirty = true;
            _geometryRevision++;
            _editBaselinePending = true;
            _editBaselineSignature = long.MinValue;
            _editLot = lot;
            _editParkingFeeKnown = EntityManager
                .HasComponent<ParkingLotEconomyData>(lot);
            if (_editParkingFeeKnown)
                _editParkingFee = EntityManager
                    .GetComponentData<ParkingLotEconomyData>(lot).ParkingFee;
            _editBuildingEconomyEnabled = HasEnabledCompanion(lot);
            PruefeBauzettelUebernahme(receipt, entrances);

            try
            {
                var hidden = HideVisibleParts(lot);
                PublishEntranceState();
                _uiSystem?.SetStatus(ParkingLotTexte.T(
                    "Parkplatz bearbeiten. Abbrechen stellt den alten Stand wieder her.",
                    "Editing parking lot. Cancel restores the old version."));
                Mod.log.Info("PLT-Bearbeiten: Einstieg Lot " + lot.Index
                    + " über Infofenster; " + hidden.Areas + " Flächen und "
                    + hidden.Objects + " sichtbare Objekte ausgeblendet, Netze "
                    + "bleiben aktiv.");
            }
            catch (Exception exception)
            {
                RestoreHiddenParts();
                _editLot = Entity.Null;
                _uiSystem?.ClearBuildReceiptTemplate();
                ResetSelection();
                Mod.log.Error(exception,
                    "PLT konnte die sichtbaren Teile zum Bearbeiten nicht ausblenden.");
                _uiSystem?.SetStatus(ParkingLotTexte.T(
                    "Bearbeiten abgebrochen: sichtbare Teile konnten nicht ausgeblendet werden.",
                    "Edit cancelled: visible parts could not be hidden."));
            }
            return true;
        }

        private (int Areas, int Objects) HideVisibleParts(Entity lot)
        {
            var areas = 0;
            var objects = 0;
            using (var parts = _editOwnerParts.ToEntityArray(Allocator.Temp))
            {
                for (var i = 0; i < parts.Length; i++)
                {
                    var part = parts[i];
                    if (EntityManager.GetComponentData<Owner>(part).m_Owner != lot
                        || !EntityManager.HasComponent<Area>(part)) continue;
                    if (HidePart(part)) areas++;
                }
            }
            using (var parts = _editRelatedParts.ToEntityArray(Allocator.Temp))
            {
                for (var i = 0; i < parts.Length; i++)
                {
                    var part = parts[i];
                    if (EntityManager
                            .GetComponentData<ParkingLotPartRelation>(part).Lot != lot
                        || !EntityManager.HasComponent<Game.Objects.Object>(part))
                        continue;
                    if (HidePart(part)) objects++;
                }
            }
            return (areas, objects);
        }

        private bool HidePart(Entity part)
        {
            if (EntityManager.HasComponent<Hidden>(part)) return false;
            EntityManager.AddComponent<Hidden>(part);
            if (!EntityManager.HasComponent<BatchesUpdated>(part))
                EntityManager.AddComponent<BatchesUpdated>(part);
            _hiddenByEdit.Add(part);
            return true;
        }

        private void RestoreHiddenParts()
        {
            foreach (var part in _hiddenByEdit)
            {
                if (!EntityManager.Exists(part)
                    || !EntityManager.HasComponent<Hidden>(part)) continue;
                EntityManager.RemoveComponent<Hidden>(part);
                if (!EntityManager.HasComponent<BatchesUpdated>(part))
                    EntityManager.AddComponent<BatchesUpdated>(part);
            }
            _hiddenByEdit.Clear();
        }

        /** Prueft Lot-Loeschung und den zweiten Schritt eines Umbaus. */
        /**
         * DIE ALTEN WEGE MUESSEN WEG, BEVOR DIE NEUEN ENTSTEHEN.
         *
         * GEMESSEN am 2026-08-31, Log des Nutzers:
         *
         *     PLT-Bearbeiten: Einstieg ...; NETZE BLEIBEN AKTIV.
         *     PLT-Besitzer: nur 54 Wegteile aus 72 Kursen haben einen
         *                   Besitzer bekommen.
         *
         * Ein `NetCourse` ergibt normalerweise eine Kante UND ihre Knoten,
         * also MEHR Entities als Kurse. 54 aus 72 heisst: fuer die Mehrzahl
         * der Kurse entstand gar nichts Neues.
         *
         * Der Grund: unsere Kurse geben zwar keinen Verbindungspunkt vor
         * (`m_Entity = Entity.Null` in `ParkingLotNetBuilder`), aber CS2
         * verbindet ueber IDENTISCHE ENDPUNKTE. Wo sich am Layout nichts
         * geaendert hat, liegt der neue Kurs exakt auf der alten Kante; CS2
         * legt dort nichts an, die Stelle gehoert weiter dem ALTEN Traeger -
         * und faellt mit ihm. Beide Haelften des Nutzerbefundes: einige Wege
         * werden nicht gesetzt, andere verschwinden.
         *
         * Der Aufruf sitzt genau im Uebergang `Idle -> CreateDefinitions`:
         * Enter ist angenommen, die Definitionen entstehen erst im naechsten
         * Durchgang. Damit liegt ein Frame zwischen Loeschen und Anlegen, und
         * die neuen Kurse finden nichts mehr, woran sie haengenbleiben.
         *
         * Nur die NETZE. Flaechen, Aufkleber, Lot und Traeger bleiben stehen,
         * bis der Neubau vollstaendig ist.
         */
        private void EntferneAlteNetzeVorDemNeubau()
        {
            if (!IsEditing) return;
            /*
             * ZUERST ERFASSEN, DANN LOESCHEN.
             *
             * Was der Nutzer selbst an unsere Strassen gelegt hat - Strom
             * unterirdisch, Wasser und Abwasser als Doppelrohr - haengt an
             * genau den Kanten, die gleich verschwinden. Danach ist nicht mehr
             * feststellbar, was dort hing.
             *
             * Sein Befund am 2026-09-05: *"Dann sind die ehemaligen
             * Verbindungen unterbrochen weil neue Strasse."*
             *
             * Diese Zeile repariert noch nichts. Sie schreibt auf, was
             * verlorengeht - und das ist die Voraussetzung fuer jede Abhilfe.
             * Ohne die Liste waere sie geraten.
             */
            ErfasseVersorgungsanschluesse(_editLot);
            /*
             * UEBER DEN BESITZER, NICHT UEBER DIE TEILRELATION.
             *
             * Der erste Anlauf am 2026-08-31 durchsuchte
             * `ParkingLotPartRelation` - und fand NICHTS: das Log meldete
             * "0 alte Wegteile ... entfernt", waehrend im Spiel weiterhin
             * Fahrgassen fehlten. Die Relation tragen die Aufkleber und
             * Ladesaeulen; die WEGE bekommen in `AttachPartsToLotOwner` einen
             * `Owner` und landen im SubNet der Lot-Flaeche. Zwei verschiedene
             * Zugehoerigkeiten, und ich hatte die falsche genommen.
             *
             * Ein Zaehler, der stumm 0 meldet, sieht aus wie "nichts zu tun".
             * Deshalb steht die Zahl jetzt in derselben Zeile neben der Zahl
             * der ueberhaupt betrachteten Teile.
             */
            using var teile = _editOwnerParts.ToEntityArray(Allocator.Temp);
            var entfernt = 0;
            var besessen = 0;
            for (var i = 0; i < teile.Length; i++)
            {
                var teil = teile[i];
                if (EntityManager.GetComponentData<Owner>(teil).m_Owner
                        != _editLot) continue;
                besessen++;
                if (!EntityManager.HasComponent<Game.Net.Edge>(teil)
                    && !EntityManager.HasComponent<Game.Net.Node>(teil))
                    continue;
                if (EntityManager.HasComponent<Deleted>(teil)) continue;
                EntityManager.AddComponent<Deleted>(teil);
                entfernt++;
            }

            Mod.log.Info("PLT-Bearbeiten: " + entfernt + " alte Wegteile von "
                + "Lot " + _editLot.Index + " vor dem Neubau entfernt (von "
                + besessen + " Teilen dieses Lots, " + teile.Length
                + " mit Besitzer insgesamt). Ohne das wuerden die neuen Kurse "
                + "auf den alten Kanten liegen und mit ihnen verschwinden.");
        }

        /**
         * Nach dem Uebernehmen: wer noch da ist, wird wieder sichtbar.
         *
         * Laeuft auch dann, wenn gerade nicht bearbeitet wird - der Abriss
         * des alten Lots zieht sich ueber mehrere Durchgaenge, das Ende der
         * Bearbeitung ist also nicht das Ende des Aufraeumens.
         *
         * Grenze, die ich offen benenne: der Aufruf haengt am ToolUpdate,
         * laeuft also nur, solange das PLT-Werkzeug aktiv ist. Uebernommen
         * wird IM Werkzeug, und der Abriss braucht nur wenige Durchgaenge -
         * der Normalfall ist damit gedeckt. Verlaesst jemand das Werkzeug
         * genau in diesen Frames, wird der Ueberlebende beim naechsten
         * Oeffnen sichtbar gemacht, nicht frueher.
         */
        private void PruefeUeberlebendeAusgeblendete()
        {
            if (_hiddenNachUebernahme.Count == 0) return;

            _hiddenPruefliste.Clear();
            foreach (var teil in _hiddenNachUebernahme)
                if (!EntityManager.Exists(teil)
                    || EntityManager.HasComponent<Deleted>(teil))
                    _hiddenPruefliste.Add(teil);
            for (var i = 0; i < _hiddenPruefliste.Count; i++)
                _hiddenNachUebernahme.Remove(_hiddenPruefliste[i]);
            if (_hiddenNachUebernahme.Count == 0) return;

            /*
             * Was noch existiert und NICHT mehr zum Abriss vorgemerkt ist,
             * hat den Abriss ueberlebt. Es gehoert keinem alten Lot mehr an,
             * also darf es auch nicht unsichtbar bleiben.
             */
            _hiddenPruefliste.Clear();
            foreach (var teil in _hiddenNachUebernahme)
            {
                if (!EntityManager.HasComponent<Hidden>(teil))
                {
                    _hiddenPruefliste.Add(teil);
                    continue;
                }
                if (EntityManager.HasComponent<Temp>(teil)) continue;
                EntityManager.RemoveComponent<Hidden>(teil);
                if (!EntityManager.HasComponent<BatchesUpdated>(teil))
                    EntityManager.AddComponent<BatchesUpdated>(teil);
                _hiddenPruefliste.Add(teil);
                Mod.log.Info("PLT-Bearbeiten: Teil " + teil.Index
                    + " hat den Abriss des alten Lots ueberlebt und war noch "
                    + "ausgeblendet - wieder sichtbar gemacht.");
            }
            for (var i = 0; i < _hiddenPruefliste.Count; i++)
                _hiddenNachUebernahme.Remove(_hiddenPruefliste[i]);
        }

        private bool ProcessEditLifecycle()
        {
            PruefeUeberlebendeAusgeblendete();
            if (!IsEditing) return false;
            if (!EntityManager.Exists(_editLot))
            {
                ExitEdit("Abbruch: altes Lot existiert nicht mehr", false,
                    resetSelection: true);
                _uiSystem?.SetStatus(ParkingLotTexte.T(
                    "Bearbeitung beendet: der alte Parkplatz existiert nicht mehr.",
                    "Edit ended: the old parking lot no longer exists."));
                return true;
            }
            // Bulldozer-Hover ist Deleted + Temp. Erst Deleted OHNE Temp ist
            // der wirkliche Abriss, genau wie im PLT-Aufraeumer.
            if (EntityManager.HasComponent<Deleted>(_editLot)
                && !EntityManager.HasComponent<Temp>(_editLot))
            {
                ExitEdit("Abbruch: altes Lot wurde während der Bearbeitung gelöscht",
                    false, resetSelection: true);
                _uiSystem?.SetStatus(ParkingLotTexte.T(
                    "Bearbeitung beendet: der alte Parkplatz wurde gelöscht.",
                    "Edit ended: the old parking lot was deleted."));
                return true;
            }
            if (_replacementNewLot == Entity.Null) return false;
            return PollReplacementCommit();
        }

        private bool PollReplacementCommit()
        {
            var elapsed = UnityEngine.Time.frameCount - _replacementStartedFrame;
            if (!EntityManager.Exists(_replacementNewLot)
                || EntityManager.HasComponent<Deleted>(_replacementNewLot))
            {
                AbortEdit("Neubau ist vor der Übernahme verschwunden",
                    "Umbau fehlgeschlagen; der alte Parkplatz wurde wiederhergestellt.",
                    "Rebuild failed; the old parking lot was restored.");
                return true;
            }
            if (EntityManager.HasComponent<Temp>(_replacementNewLot))
            {
                if (elapsed <= MaterializationTimeoutFrames) return false;
                AbortEdit("Neubau blieb temporär", "Umbau fehlgeschlagen; "
                    + "der alte Parkplatz wurde wiederhergestellt.",
                    "Rebuild failed; the old parking lot was restored.");
                return true;
            }
            if (!EntityManager.HasComponent<ParkingLotCarrierReference>(
                    _replacementNewLot)
                || !HasCompleteBuildReceipt(_replacementNewLot))
            {
                AbortEdit("Neubau ist unvollständig", "Umbau fehlgeschlagen; "
                    + "der alte Parkplatz wurde wiederhergestellt.",
                    "Rebuild failed; the old parking lot was restored.");
                return true;
            }
            var carrier = EntityManager.GetComponentData<
                ParkingLotCarrierReference>(_replacementNewLot).Carrier;
            if (carrier == Entity.Null || carrier != _replacementNewCarrier
                || !EntityManager.Exists(carrier)
                || EntityManager.HasComponent<Deleted>(carrier)
                || EntityManager.HasComponent<Temp>(carrier))
            {
                AbortEdit("Träger des Neubaus ist unvollständig",
                    "Umbau fehlgeschlagen; der alte Parkplatz wurde wiederhergestellt.",
                    "Rebuild failed; the old parking lot was restored.");
                return true;
            }

            if (_editParkingFeeKnown && !_replacementEconomyTransferred
                && !_editEconomySystem.TryInitializeReplacement(
                    _replacementNewLot, _editParkingFee))
            {
                if (elapsed <= MaterializationTimeoutFrames) return false;
                AbortEdit("Wirtschaft des Neubaus konnte nicht geeicht werden",
                    "Umbau fehlgeschlagen; Parkgebühr oder Kapazität konnten "
                    + "nicht übernommen werden.",
                    "Rebuild failed; the parking fee or capacity could not be transferred.");
                return true;
            }
            if (_editParkingFeeKnown) _replacementEconomyTransferred = true;
            if (_editBuildingEconomyEnabled
                && !HasEnabledCompanion(_replacementNewLot))
            {
                if (elapsed <= MaterializationTimeoutFrames) return false;
                AbortEdit("Wirtschaftsbegleiter des Neubaus fehlt",
                    "Umbau fehlgeschlagen; der alte Parkplatz wurde wiederhergestellt.",
                    "Rebuild failed; the old parking lot was restored.");
                return true;
            }

            /*
             * JETZT IST DER RICHTIGE ZEITPUNKT FUER DIE LEITUNGEN.
             *
             * Frueher geht es nicht: bis hierher wurde gerade erst geprueft,
             * dass Lot UND Traeger dauerhaft sind. Auf einer temporaeren
             * Kante laeuft `UpdateNodeConnections` nicht, ein Updated waere
             * also folgenlos verpufft.
             *
             * Spaeter geht es auch nicht: gleich faellt das alte Lot, und
             * damit endet der Bearbeiten-Zustand samt der erfassten Liste.
             */
            StelleVersorgungsanschluesseWiederHer(carrier);
            // Eigene automatische Leitungen bleiben beim alten Lot: derselbe
            // Aufraeumer wie beim Abriss entfernt sie samt eigenen Endknoten.
            // Sie werden nach Abschluss dieses Abrisses frisch gebaut.
            /*
             * NOCH NUR DIE WAHL, NICHT DER BAU.
             *
             * Der automatische Anschluss braucht zuerst zwei gemessene Punkte:
             * wo an unserem Parkplatz die Leitung ansetzt und an welcher
             * Stadtstrasse sie endet. Die Rangfolge dafuer hat der Nutzer
             * vorgegeben (Sackgasse, sonst Ecke, sonst naechster Punkt), und
             * sie steht jetzt im Bauzettel - nachpruefbar, bevor irgendetwas
             * in der Welt entsteht.
             */


            var old = _editLot;
            var next = _replacementNewLot;
            var alterTraeger = EntityManager.HasComponent<ParkingLotCarrierReference>(old)
                ? EntityManager.GetComponentData<ParkingLotCarrierReference>(old).Carrier
                : Entity.Null;
            TransferVegetation(old, next, carrier);
            EntityManager.AddComponent<Deleted>(old);
            /*
             * `Hidden` DARF NICHT EINFACH VERGESSEN WERDEN.
             *
             * Hier stand `_hiddenByEdit.Clear()`. Die Annahme dahinter: die
             * ausgeblendeten Teile verschwinden ohnehin mit dem alten Lot,
             * also ist das Vergessen folgenlos.
             *
             * Die Annahme haelt nicht. Der Aufraeumer raeumt in Haeppchen von
             * 32 je Durchgang und ueberspringt dabei ausdruecklich Teile mit
             * `Temp` ("Temp bleibt hier sichtbar, damit ein temporaeres Teil
             * das Entfernen des Traegers aufschiebt"). Ueberlebt auch nur ein
             * ausgeblendetes Teil den Abriss, traegt es unser `Hidden` fuer
             * immer weiter - unsichtbar, aber vorhanden. Der Nutzer meldete
             * am 2026-08-31 einen fehlenden gruenen Rand nach dem Bearbeiten,
             * waehrend das Log "28 von 28 Grasflaechen gesetzt" sagte: gebaut
             * und trotzdem nicht zu sehen.
             *
             * Die Liste wird deshalb weitergefuehrt statt geleert. Was
             * wirklich verschwindet, faellt von selbst heraus; was den Abriss
             * ueberlebt, bekommt sein `Hidden` zurueckgenommen und ist wieder
             * sichtbar. Kein geratener Zeitwert - beobachtet wird, bis nichts
             * mehr uebrig ist.
             */
            _hiddenNachUebernahme.Clear();
            foreach (var teil in _hiddenByEdit) _hiddenNachUebernahme.Add(teil);
            _hiddenByEdit.Clear();
            ClearEditState();
            MerkeAutoVersorgung(carrier, old, alterTraeger);
            Mod.log.Info("PLT-Bearbeiten: Ausstieg durch Übernehmen; neues Lot "
                + next.Index + " steht vollständig, altes Lot " + old.Index
                + " dem PLT-Aufräumer übergeben.");
            _uiSystem?.SetStatus(ParkingLotTexte.T(
                "Änderungen übernommen.", "Changes applied."));
            return true;
        }

        private bool HasEnabledCompanion(Entity lot)
        {
            if (_editEnabledCompanions.IsEmptyIgnoreFilter) return false;
            using var companions = _editEnabledCompanions
                .ToEntityArray(Allocator.Temp);
            for (var i = 0; i < companions.Length; i++)
                if (EntityManager
                        .GetComponentData<ParkingLotPartRelation>(companions[i]).Lot
                    == lot)
                    return true;
            return false;
        }

        private void BeginReplacementCommit(Entity newLot, Entity newCarrier)
        {
            if (!IsEditing) return;
            _replacementNewLot = newLot;
            _replacementNewCarrier = newCarrier;
            _replacementStartedFrame = UnityEngine.Time.frameCount;
            _replacementEconomyTransferred = false;
        }

        private void CaptureEditBaselineIfNeeded(ParkingLayout layout)
        {
            if (!IsEditing || !_editBaselinePending || layout == null) return;
            _editBaselineSignature = AreaPreviewSignature(layout);
            _editBaselinePending = false;
        }

        private bool TryFinishUnchangedEdit()
        {
            if (!IsEditing || _editBaselinePending || _areaPreviewLayout == null)
                return false;
            if (AreaPreviewSignature(_areaPreviewLayout) != _editBaselineSignature)
                return false;
            RestoreHiddenParts();
            var lot = _editLot;
            ClearEditState();
            ResetSelection();
            Mod.log.Info("PLT-Bearbeiten: Ausstieg durch Übernehmen ohne Änderungen; "
                + "Lot " + lot.Index + " unverändert wieder eingeblendet, nichts gebaut.");
            _uiSystem?.SetStatus(ParkingLotTexte.T(
                "Keine Änderungen; nichts neu gebaut.",
                "No changes; nothing was rebuilt."));
            return true;
        }

        private void AbortEdit(string reason, string statusDe, string statusEn)
        {
            if (!IsEditing) return;
            RollBackReplacement();
            RestoreHiddenParts();
            var lot = _editLot;
            ClearEditState();
            ResetSelection();
            Mod.log.Info("PLT-Bearbeiten: Ausstieg durch Abbruch (" + reason
                + "); altes Lot " + lot.Index + " wiederhergestellt.");
            _uiSystem?.SetStatus(ParkingLotTexte.T(statusDe, statusEn));
        }

        private void ExitEdit(string reason, bool restoreOld, bool resetSelection)
        {
            if (!IsEditing) return;
            RollBackReplacement();
            if (restoreOld) RestoreHiddenParts();
            else _hiddenByEdit.Clear();
            var lot = _editLot;
            ClearEditState();
            if (resetSelection) ResetSelection();
            Mod.log.Info("PLT-Bearbeiten: Ausstieg durch " + reason + "; Lot "
                + lot.Index + ".");
        }

        private void RollBackReplacement()
        {
            if (_replacementNewLot != Entity.Null
                && EntityManager.Exists(_replacementNewLot)
                && !EntityManager.HasComponent<Deleted>(_replacementNewLot))
            {
                if (EntityManager.HasComponent<Temp>(_replacementNewLot))
                    applyMode = ApplyMode.Clear;
                else EntityManager.AddComponent<Deleted>(_replacementNewLot);
            }
            if (_replacementNewCarrier != Entity.Null
                && EntityManager.Exists(_replacementNewCarrier)
                && (_replacementNewLot == Entity.Null
                    || !EntityManager.Exists(_replacementNewLot)
                    || EntityManager.HasComponent<Temp>(_replacementNewLot))
                && !EntityManager.HasComponent<Deleted>(_replacementNewCarrier))
                EntityManager.AddComponent<Deleted>(_replacementNewCarrier);
        }

        /**
         * KOMMT AUS DEM BAUZETTEL WIRKLICH DAS AN, WAS DRINSTAND?
         *
         * Der Nutzer am 2026-09-14: *"Ich will, dass Edit absolut stabil
         * funktioniert."* Der Abgleich von Hand hat gezeigt, dass alles
         * Gezeichnete eine eigene Komponente hat - Polygon, Zufahrten,
         * Ausrichtungen, Zoningflaechen, Randzoning, Seitenplan, Schnitte,
         * Flaechenwahlen, Vegetation. Dort ist keine Luecke.
         *
         * EINE UNSICHTBARE KETTE BLEIBT. Sieben Werte haben keinen Regler:
         * `Sl`, `Sw`, `NoNotch`, `Single`, `NoHalf`, `KantenVersatz` und
         * `AutomaticEntrances`. `CurrentSettings` holt sie aus
         * `_loadedBuildReceiptTemplate` und ueberschreibt alles uebrige mit
         * den Panelwerten. Solange das Template steht, stimmt das - faellt es
         * weg, springen genau diese sieben still auf `LayoutSettings.Cs2`,
         * und der bearbeitete Parkplatz kaeme anders heraus als der gebaute.
         *
         * Gegen "still" hilft nur Nachzaehlen. Diese Pruefung vergleicht, was
         * im Bauzettel steht, mit dem, was der Bau gleich benutzen wuerde -
         * Feld fuer Feld. Sie aendert nichts und verhindert nichts; sie sagt
         * nur Bescheid. Eine Mod, die beim Bearbeiten von selbst etwas
         * zurechtrueckt, waere genau der Reparaturpass, den dieses Projekt
         * nicht will.
         *
         * `Cr` bekommt Spielraum: der Regler zaehlt BUCHTEN, der Bauzettel
         * speichert METER, und die Ruecknrechnung trifft nicht auf den
         * Millimeter.
         */
        private void PruefeBauzettelUebernahme(
            ParkingLotBuildReceipt receipt, Entrance[] entrances)
        {
            try
            {
                var gespeichert = receipt.ToLayoutSettings(
                    entrances ?? Array.Empty<Entrance>());
                var wirksam = _uiSystem?.CurrentSettings();
                if (wirksam == null) return;

                var abweichungen = new List<string>();
                void Zahl(string name, double a, double b, double schranke)
                {
                    if (Math.Abs(a - b) > schranke)
                        abweichungen.Add($"{name} {a:0.####} -> {b:0.####}");
                }
                void Schalter(string name, bool a, bool b)
                {
                    if (a != b) abweichungen.Add($"{name} {a} -> {b}");
                }

                Zahl("Es", gespeichert.Es, wirksam.Es, 1e-6);
                Zahl("Ai", gespeichert.Ai, wirksam.Ai, 1e-6);
                Zahl("Cw", gespeichert.Cw, wirksam.Cw, 1e-6);
                Zahl("Sl", gespeichert.Sl, wirksam.Sl, 1e-6);
                Zahl("Sw", gespeichert.Sw, wirksam.Sw, 1e-6);
                Zahl("Md", gespeichert.Md, wirksam.Md, 1e-6);
                Zahl("Cr", gespeichert.Cr, wirksam.Cr, 0.05);
                Zahl("Angle", gespeichert.Angle, wirksam.Angle, 1e-6);
                Zahl("KantenVersatz", gespeichert.KantenVersatz,
                    wirksam.KantenVersatz, 1e-6);
                Schalter("Qk", gespeichert.Qk, wirksam.Qk);
                Schalter("Randstrassen", gespeichert.Randstrassen,
                    wirksam.Randstrassen);
                Schalter("Auto", gespeichert.Auto, wirksam.Auto);
                Schalter("AutomaticEntrances", gespeichert.AutomaticEntrances,
                    wirksam.AutomaticEntrances);
                Schalter("Zellen", gespeichert.Zellen, wirksam.Zellen);
                Schalter("EineFlaeche", gespeichert.EineFlaeche,
                    wirksam.EineFlaeche);
                Schalter("NoNotch", gespeichert.NoNotch, wirksam.NoNotch);
                Schalter("Single", gespeichert.Single, wirksam.Single);
                Schalter("NoHalf", gespeichert.NoHalf, wirksam.NoHalf);
                if (!string.Equals(gespeichert.AngleMode ?? string.Empty,
                        wirksam.AngleMode ?? string.Empty,
                        StringComparison.Ordinal))
                    abweichungen.Add($"AngleMode {gespeichert.AngleMode} -> "
                        + wirksam.AngleMode);

                if (abweichungen.Count == 0)
                {
                    Mod.log.Info("PLT-Bearbeiten: Bauzettel vollstaendig "
                        + "uebernommen, 19 Werte geprueft, keine Abweichung.");
                    return;
                }

                Mod.log.Warn("PLT-Bearbeiten: der Bau wuerde ANDERE Werte "
                    + "benutzen als im Bauzettel stehen - "
                    + string.Join("; ", abweichungen)
                    + ". Wer jetzt uebernimmt, baut etwas anderes als vorher. "
                    + "Bei Sl, Sw, NoNotch, Single, NoHalf, KantenVersatz oder "
                    + "AutomaticEntrances heisst das: das Bauzettel-Template "
                    + "fehlt, diese Werte haben keinen Regler.");
            }
            catch (Exception ausnahme)
            {
                // Eine Pruefung darf das Bearbeiten nie verhindern.
                Mod.log.Warn("PLT-Bearbeiten: Bauzettel-Abgleich nicht "
                    + "moeglich: " + ausnahme.Message);
            }
        }

        private void ClearEditState()
        {
            _uiSystem?.ClearBuildReceiptTemplate();
            _editLot = Entity.Null;
            _replacementNewLot = Entity.Null;
            _replacementNewCarrier = Entity.Null;
            _editBaselinePending = false;
            _editBaselineSignature = long.MinValue;
            _editParkingFeeKnown = false;
            _editBuildingEconomyEnabled = false;
            _replacementEconomyTransferred = false;
        }

        internal void CancelEditingForShutdown()
        {
            _pendingEditLot = Entity.Null;
            if (IsEditing)
                AbortEdit("Mod oder Spiel wird beendet",
                    "Bearbeitung abgebrochen.", "Edit cancelled.");
        }

        private void OnEditGameSaveLoad(string saveName, string previewUri,
                                        bool start, bool success)
        {
            if (!start || !IsEditing) return;
            AbortEdit("Speichern von " + saveName,
                "Bearbeitung vor dem Speichern automatisch abgebrochen.",
                "Edit was automatically cancelled before saving.");
        }

        [Preserve]
        protected override void OnGamePreload(
            Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            _pendingEditLot = Entity.Null;
            if (IsEditing)
                AbortEdit("Spielstandwechsel", "Bearbeitung abgebrochen.",
                    "Edit cancelled.");
        }

        [Preserve]
        protected override void OnDestroy()
        {
            if (GameManager.instance != null)
                GameManager.instance.onGameSaveLoad -= OnEditGameSaveLoad;
            CancelEditingForShutdown();
            base.OnDestroy();
        }

        private bool WriteBuildReceipt(Entity lot, LayoutSettings settings,
                                       float3[] worldPoints)
        {
            if (lot == Entity.Null || !EntityManager.Exists(lot)
                || settings == null || worldPoints == null
                || worldPoints.Length < MinPolygonPoints) return false;
            try
            {
                var receipt = new ParkingLotBuildReceipt
                {
                    Version = ParkingLotBuildReceipt.CurrentVersion,
                    Es = settings.Es,
                    Ai = settings.Ai,
                    Cw = settings.Cw,
                    Sl = settings.Sl,
                    Sw = settings.Sw,
                    Md = settings.Md,
                    Cr = settings.Cr,
                    Angle = settings.Angle,
                    KantenVersatz = settings.KantenVersatz,
                    Qk = settings.Qk,
                    Randstrassen = settings.Randstrassen,
                    Auto = settings.Auto,
                    AutomaticEntrances = settings.AutomaticEntrances,
                    Zellen = settings.Zellen,
                    EineFlaeche = settings.EineFlaeche,
                    NoNotch = settings.NoNotch,
                    Single = settings.Single,
                    NoHalf = settings.NoHalf,
                    AngleMode = ParkingLotBuildReceipt
                        .EncodeAngleMode(settings.AngleMode),
                    MedianWidth = _uiSystem?.AktuelleMedianbreite ?? settings.Md,
                    GreenMedian = _uiSystem?.MittelgruenAn ?? settings.Md > 0,
                    CrossBays = _uiSystem?.AktuelleQuerbuchten
                        ?? (settings.Cr / settings.Sw - (settings.Qk ? 2 : 0)),
                    SurfaceRoadOn = _uiSystem?.FlaecheStrasseAn ?? true,
                    SurfaceDecorationOn = _uiSystem?.FlaecheDekoAn ?? true,
                    SurfaceApronOn = _uiSystem?.VorflaecheAn ?? true,
                    BayIcons = _uiSystem?.Buchtsymbole ?? true,
                    // NaN heisst "keine Bezugslinie" - so bleibt der Wert
                    // auch ohne Ausrichtung eindeutig.
                    Ausrichtwinkel = Ausrichtwinkel ?? double.NaN,
                    AusrichtAx = Ausrichtungen.Count > 0 ? Ausrichtungen[0].LinieA.x : 0f,
                    AusrichtAz = Ausrichtungen.Count > 0 ? Ausrichtungen[0].LinieA.y : 0f,
                    AusrichtBx = Ausrichtungen.Count > 0 ? Ausrichtungen[0].LinieB.x : 0f,
                    AusrichtBz = Ausrichtungen.Count > 0 ? Ausrichtungen[0].LinieB.y : 0f,
                };
                if (EntityManager.HasComponent<ParkingLotBuildReceipt>(lot))
                    EntityManager.SetComponentData(lot, receipt);
                else EntityManager.AddComponentData(lot, receipt);

                var pointBuffer = EntityManager.HasBuffer<ParkingLotBuildPoint>(lot)
                    ? EntityManager.GetBuffer<ParkingLotBuildPoint>(lot)
                    : EntityManager.AddBuffer<ParkingLotBuildPoint>(lot);
                pointBuffer.Clear();
                for (var i = 0; i < worldPoints.Length; i++)
                    pointBuffer.Add(new ParkingLotBuildPoint
                    {
                        Version = ParkingLotBuildPoint.CurrentVersion,
                        Position = worldPoints[i],
                    });

                var entranceBuffer = EntityManager
                    .HasBuffer<ParkingLotBuildEntrance>(lot)
                    ? EntityManager.GetBuffer<ParkingLotBuildEntrance>(lot)
                    : EntityManager.AddBuffer<ParkingLotBuildEntrance>(lot);
                entranceBuffer.Clear();
                var entrances = settings.Entrances ?? Array.Empty<Entrance>();
                for (var i = 0; i < entrances.Length; i++)
                {
                    var entrance = entrances[i];
                    if (entrance == null) continue;
                    entranceBuffer.Add(new ParkingLotBuildEntrance
                    {
                        Version = ParkingLotBuildEntrance.CurrentVersion,
                        Edge = entrance.Edge,
                        Along = entrance.Along,
                        Corner = EncodeCorner(entrance.Corner),
                        Art = entrance.Art,
                    });
                }
                /*
                 * EIGENER OPTIONALER PUFFER. Der Bauzettel selbst bleibt in
                 * Fassung 2 lesbar; alte Spielstaende besitzen diesen Typ
                 * schlicht nicht und fallen beim Lesen auf ihren einen
                 * gespeicherten Winkel zurueck.
                 */
                if (Ausrichtungen.Count > 0)
                {
                    var alignmentBuffer = EntityManager
                        .HasBuffer<ParkingLotBuildAlignment>(lot)
                        ? EntityManager.GetBuffer<ParkingLotBuildAlignment>(lot)
                        : EntityManager.AddBuffer<ParkingLotBuildAlignment>(lot);
                    alignmentBuffer.Clear();
                    for (var i = 0; i < Ausrichtungen.Count; i++)
                    {
                        var alignment = Ausrichtungen[i];
                        alignmentBuffer.Add(new ParkingLotBuildAlignment
                        {
                            Version = ParkingLotBuildAlignment.CurrentVersion,
                            Anchor = alignment.Anker,
                            LineA = alignment.LinieA,
                            LineB = alignment.LinieB,
                            Angle = alignment.Winkel,
                        });
                    }
                }
                else if (EntityManager.HasBuffer<ParkingLotBuildAlignment>(lot))
                {
                    EntityManager.RemoveComponent<ParkingLotBuildAlignment>(lot);
                }

                // Die Handschnitte gehoeren zu den Zuweisungen: sie legen
                // fest, WELCHE Teilflaechen es ueberhaupt gibt.
                if (Trennschnitte.Count > 0)
                {
                    var cutBuffer = EntityManager
                        .HasBuffer<ParkingLotBuildCut>(lot)
                        ? EntityManager.GetBuffer<ParkingLotBuildCut>(lot)
                        : EntityManager.AddBuffer<ParkingLotBuildCut>(lot);
                    cutBuffer.Clear();
                    for (var i = 0; i < Trennschnitte.Count; i++)
                        cutBuffer.Add(new ParkingLotBuildCut
                        {
                            Version = ParkingLotBuildCut.CurrentVersion,
                            A = Trennschnitte[i].A,
                            B = Trennschnitte[i].B,
                        });
                }
                else if (EntityManager.HasBuffer<ParkingLotBuildCut>(lot))
                {
                    EntityManager.RemoveComponent<ParkingLotBuildCut>(lot);
                }

                // Die Zoning-Flaechen aus demselben Grund wie die Schnitte:
                // ohne sie waere ein bearbeiteter Parkplatz um seine
                // Parzellen aermer.
                if (Zoningflaechen.Count > 0)
                {
                    var zonePuffer = EntityManager
                        .HasBuffer<ParkingLotBuildZoning>(lot)
                        ? EntityManager.GetBuffer<ParkingLotBuildZoning>(lot)
                        : EntityManager.AddBuffer<ParkingLotBuildZoning>(lot);
                    zonePuffer.Clear();
                    for (var i = 0; i < Zoningflaechen.Count; i++)
                        zonePuffer.Add(new ParkingLotBuildZoning
                        {
                            Version = ParkingLotBuildZoning.CurrentVersion,
                            Ecke = Zoningflaechen[i].Ecke,
                            Spalten = Zoningflaechen[i].Spalten,
                            Reihen = Zoningflaechen[i].Reihen,
                            Winkel = Zoningflaechen[i].Winkel,
                            Rand = Zoningflaechen[i].Rand,
                        });
                }
                else if (EntityManager.HasBuffer<ParkingLotBuildZoning>(lot))
                {
                    EntityManager.RemoveComponent<ParkingLotBuildZoning>(lot);
                }

                /*
                 * DIE HANDGESCHALTETEN SEITEN GEHOEREN DAZU.
                 *
                 * Sie entstehen jetzt schon in der Vorschau, also lange vor
                 * der Kante, an der sie am Ende haengen. Ohne diesen Puffer
                 * waeren sie mit dem Bauen weg - und der Nutzer haette die
                 * Arbeit umsonst gemacht.
                 */
                var seitenPuffer = EntityManager
                    .HasBuffer<ParkingLotBuildZoningSeite>(lot)
                    ? EntityManager.GetBuffer<ParkingLotBuildZoningSeite>(lot)
                    : EntityManager.AddBuffer<ParkingLotBuildZoningSeite>(lot);
                SchreibeZoningSeitenplan(seitenPuffer);

                // Und das Randzoning, aus demselben Grund.
                var randPuffer = EntityManager
                    .HasBuffer<ParkingLotBuildRandzoning>(lot)
                    ? EntityManager.GetBuffer<ParkingLotBuildRandzoning>(lot)
                    : EntityManager.AddBuffer<ParkingLotBuildRandzoning>(lot);
                randPuffer.Clear();
                foreach (var linie in Randzoninglinien)
                    randPuffer.Add(new ParkingLotBuildRandzoning
                    {
                        Version = ParkingLotBuildRandzoning.CurrentVersion,
                        A = linie.A,
                        B = linie.B,
                    });
                var textBuffer = EntityManager.HasBuffer<ParkingLotBuildText>(lot)
                    ? EntityManager.GetBuffer<ParkingLotBuildText>(lot)
                    : EntityManager.AddBuffer<ParkingLotBuildText>(lot);
                textBuffer.Clear();
                AddBuildText(textBuffer, 1,
                    _uiSystem?.FlaecheStrasse ?? string.Empty);
                AddBuildText(textBuffer, 2,
                    _uiSystem?.FlaecheDekoration ?? string.Empty);
                /*
                 * DIE DRITTE FLAECHE GEHOERT DAZU.
                 *
                 * Ohne sie saehe ein bearbeiteter Parkplatz anders aus als
                 * der gebaute: die Parzellen fielen still auf die
                 * Dekoflaeche zurueck. Leer ist ein gueltiger Wert und
                 * heisst genau das - "nimm die Dekoflaeche"; ein alter
                 * Bauzettel ohne Eintrag verhaelt sich damit richtig.
                 */
                AddBuildText(textBuffer, 3,
                    _uiSystem?.FlaecheZoning ?? string.Empty);
                WriteVegetation(lot);
                return true;
            }
            catch (Exception exception)
            {
                Mod.log.Error(exception,
                    "PLT-Bauzettel konnte nicht am neuen Lot gespeichert werden.");
                return false;
            }
        }

        private bool TryReadBuildReceipt(Entity lot,
            out ParkingLotBuildReceipt receipt, out float3[] points,
            out Entrance[] entrances, out Ausrichtzuweisung[] alignments,
            out Teilflaechenschnitt[] cuts,
            out ParkingGeometry.Zoningflaeche[] zonen,
            out string surfaceRoad,
            out string surfaceDecoration, out string surfaceZoning,
            out string reason)
        {
            receipt = default;
            points = Array.Empty<float3>();
            entrances = Array.Empty<Entrance>();
            alignments = null;
            cuts = null;
            zonen = null;
            surfaceRoad = string.Empty;
            surfaceDecoration = string.Empty;
            surfaceZoning = string.Empty;
            reason = "Bauzettel fehlt";
            if (lot == Entity.Null || !EntityManager.Exists(lot)
                || !HasCompleteBuildReceipt(lot)) return false;

            receipt = EntityManager.GetComponentData<ParkingLotBuildReceipt>(lot);
            if (receipt.Version != ParkingLotBuildReceipt.CurrentVersion)
            {
                reason = "unbekannte Bauzettel-Version " + receipt.Version;
                return false;
            }
            if (!ValidReceiptSettings(receipt))
            {
                reason = "ungültige Einstellungen im Bauzettel";
                return false;
            }

            var pointBuffer = EntityManager
                .GetBuffer<ParkingLotBuildPoint>(lot, true);
            if (pointBuffer.Length < MinPolygonPoints)
            {
                reason = "weniger als drei Polygonpunkte";
                return false;
            }
            points = new float3[pointBuffer.Length];
            for (var i = 0; i < pointBuffer.Length; i++)
            {
                var point = pointBuffer[i];
                if (point.Version != ParkingLotBuildPoint.CurrentVersion
                    || !math.all(math.isfinite(point.Position)))
                {
                    reason = "ungültiger Polygonpunkt " + i;
                    return false;
                }
                points[i] = point.Position;
            }

            var entranceBuffer = EntityManager
                .GetBuffer<ParkingLotBuildEntrance>(lot, true);
            entrances = new Entrance[entranceBuffer.Length];
            for (var i = 0; i < entranceBuffer.Length; i++)
            {
                var entrance = entranceBuffer[i];
                if (entrance.Version != ParkingLotBuildEntrance.CurrentVersion
                    || entrance.Edge < 0 || entrance.Edge >= points.Length
                    || entrance.Corner < 0 || entrance.Corner > 2
                    || double.IsNaN(entrance.Along)
                    || double.IsInfinity(entrance.Along)
                    || (int)entrance.Art < (int)Zufahrtsart.Zufahrt
                    || (int)entrance.Art > (int)Zufahrtsart.Fussweg)
                {
                    reason = "ungültiger Zugang " + i;
                    return false;
                }
                entrances[i] = new Entrance
                {
                    Edge = entrance.Edge,
                    Along = entrance.Along,
                    Corner = DecodeCorner(entrance.Corner),
                    Art = entrance.Art,
                };
            }
            if (EntityManager.HasBuffer<ParkingLotBuildAlignment>(lot))
            {
                var alignmentBuffer = EntityManager
                    .GetBuffer<ParkingLotBuildAlignment>(lot, true);
                alignments = new Ausrichtzuweisung[alignmentBuffer.Length];
                for (var i = 0; i < alignmentBuffer.Length; i++)
                {
                    var alignment = alignmentBuffer[i];
                    if (alignment.Version != ParkingLotBuildAlignment.CurrentVersion
                        || !math.all(math.isfinite(alignment.Anchor))
                        || !math.all(math.isfinite(alignment.LineA))
                        || !math.all(math.isfinite(alignment.LineB))
                        || double.IsNaN(alignment.Angle)
                        || double.IsInfinity(alignment.Angle))
                    {
                        reason = "ungültige Teilflächenausrichtung " + i;
                        return false;
                    }
                    alignments[i] = new Ausrichtzuweisung
                    {
                        Anker = alignment.Anchor,
                        LinieA = alignment.LineA,
                        LinieB = alignment.LineB,
                        Winkel = alignment.Angle,
                    };
                }
            }
            if (EntityManager.HasBuffer<ParkingLotBuildCut>(lot))
            {
                var cutBuffer = EntityManager
                    .GetBuffer<ParkingLotBuildCut>(lot, true);
                cuts = new Teilflaechenschnitt[cutBuffer.Length];
                for (var i = 0; i < cutBuffer.Length; i++)
                {
                    var cut = cutBuffer[i];
                    if (cut.Version != ParkingLotBuildCut.CurrentVersion
                        || !math.all(math.isfinite(cut.A))
                        || !math.all(math.isfinite(cut.B)))
                    {
                        reason = "ungültiger Trennschnitt " + i;
                        return false;
                    }
                    cuts[i] = new Teilflaechenschnitt { A = cut.A, B = cut.B };
                }
            }
            if (EntityManager.HasBuffer<ParkingLotBuildZoning>(lot))
            {
                var zonePuffer = EntityManager
                    .GetBuffer<ParkingLotBuildZoning>(lot, true);
                zonen = new ParkingGeometry.Zoningflaeche[zonePuffer.Length];
                for (var i = 0; i < zonePuffer.Length; i++)
                {
                    var z = zonePuffer[i];
                    // Dieselbe Strenge wie bei den Schnitten: ein Bauzettel,
                    // dem man nicht trauen kann, wird abgelehnt statt halb
                    // benutzt. Die Grenzen kommen aus dem Spiel und koennen
                    // sich nicht geaendert haben - eine Zahl ausserhalb ist
                    // also ein kaputter Zettel, kein alter.
                    // Fassung 1 wird angenommen: ihr fehlt nur der Rand, und
                    // der war damals immer die Strassenbreite. Einen alten
                    // Zettel deshalb abzulehnen hiesse, dem Nutzer einen
                    // funktionierenden Parkplatz zu nehmen.
                    if (z.Version < 1
                        || z.Version > ParkingLotBuildZoning.CurrentVersion
                        || !math.all(math.isfinite(z.Ecke))
                        || !double.IsFinite(z.Winkel)
                        || z.Spalten < 1
                        || z.Spalten > ParkingGeometry.ZoningMaxBreite
                        || z.Reihen < 1
                        || z.Reihen > ParkingGeometry.ZoningMaxTiefe)
                    {
                        reason = "ungültige Zoning-Fläche " + i;
                        return false;
                    }
                    zonen[i] = new ParkingGeometry.Zoningflaeche
                    {
                        Rand = z.Rand,
                        Ecke = z.Ecke,
                        Spalten = z.Spalten,
                        Reihen = z.Reihen,
                        Winkel = z.Winkel,
                    };
                }
            }

            /*
             * Und die handgeschalteten Seiten dazu - sonst waeren sie beim
             * Bearbeiten weg, obwohl sie im Zettel stehen.
             *
             * Ein unbrauchbarer Eintrag wird UEBERGANGEN, nicht abgelehnt:
             * eine fehlende Umschaltung kostet den Nutzer einen Klick, ein
             * abgelehnter Zettel den ganzen Parkplatz.
             */
            var seitenplan =
                new List<(float2 A, float2 B, bool Links, bool Aus)>();
            if (EntityManager.HasBuffer<ParkingLotBuildZoningSeite>(lot))
            {
                var seitenPuffer = EntityManager
                    .GetBuffer<ParkingLotBuildZoningSeite>(lot, true);
                for (var i = 0; i < seitenPuffer.Length; i++)
                {
                    var s = seitenPuffer[i];
                    if (s.Version < 1
                        || s.Version > ParkingLotBuildZoningSeite.CurrentVersion
                        || !math.all(math.isfinite(s.A))
                        || !math.all(math.isfinite(s.B))) continue;
                    seitenplan.Add((s.A, s.B, s.Links, s.Aus));
                }
            }
            /*
             * NICHT HIER AUFTRAGEN, NUR MERKEN.
             *
             * Diese Methode laeuft VOR `ResetSelection`, und das raeumt den
             * Seitenplan mit den Zoningflaechen weg. Genau daran ist die
             * Umschaltung verlorengegangen: *"Nachdem ich den Standard
             * entfernt habe, gebaut habe und wieder editiert habe, war der
             * Standard wieder da."* Aufgetragen wird deshalb erst beim
             * Aufrufer, gleich hinter den Flaechen, zu denen der Plan gehoert.
             */
            _zoningSeitenplanAusZettel = seitenplan;

            // Das Randzoning aus demselben Zettel, mit derselben Nachsicht:
            // ein unbrauchbarer Eintrag wird uebergangen, nicht abgelehnt.
            var randplan = new List<ParkingGeometry.RandzoningLinie>();
            if (EntityManager.HasBuffer<ParkingLotBuildRandzoning>(lot))
            {
                var randPuffer = EntityManager
                    .GetBuffer<ParkingLotBuildRandzoning>(lot, true);
                for (var i = 0; i < randPuffer.Length; i++)
                {
                    var r = randPuffer[i];
                    if (r.Version < 1
                        || r.Version > ParkingLotBuildRandzoning.CurrentVersion
                        || !math.all(math.isfinite(r.A))
                        || !math.all(math.isfinite(r.B))) continue;
                    randplan.Add(new ParkingGeometry.RandzoningLinie
                    {
                        A = r.A,
                        B = r.B,
                    });
                }
            }
            _randzoningAusZettel = randplan;
            var textBuffer = EntityManager.GetBuffer<ParkingLotBuildText>(lot, true);
            // Eintrag 3 ist freiwillig: Bauzettel von vor dem 2026-09-02
            // haben ihn nicht, und leer heisst ohnehin "nimm die
            // Dekoflaeche". Ein fehlender Eintrag darf den Zettel also
            // nicht ungueltig machen.
            if (!TryReadBuildText(textBuffer, 3, out surfaceZoning))
                surfaceZoning = string.Empty;
            if (!TryReadBuildText(textBuffer, 1, out surfaceRoad)
                || !TryReadBuildText(textBuffer, 2, out surfaceDecoration)
                || string.IsNullOrEmpty(surfaceRoad)
                || string.IsNullOrEmpty(surfaceDecoration))
            {
                reason = "ungültige Flächennamen im Bauzettel";
                return false;
            }
            return true;
        }

        private static bool ValidReceiptSettings(ParkingLotBuildReceipt r)
        {
            return Finite(r.Es) && Finite(r.Ai) && Finite(r.Cw)
                && Finite(r.Sl) && Finite(r.Sw) && Finite(r.Md)
                && Finite(r.Cr) && Finite(r.Angle) && Finite(r.KantenVersatz)
                && Finite(r.MedianWidth) && Finite(r.CrossBays)
                && r.AngleMode >= 0
                && r.AngleMode <= Winkelmodus.GroessteZahl;
        }

        private static bool Finite(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value);

        private static int EncodeCorner(string corner)
            => corner == "start" ? 1 : corner == "end" ? 2 : 0;

        private static string DecodeCorner(int corner)
            => corner == 1 ? "start" : corner == 2 ? "end" : null;

        private static void AddBuildText(DynamicBuffer<ParkingLotBuildText> buffer,
                                         int kind, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            for (var i = 0; i < bytes.Length; i++)
                buffer.Add(new ParkingLotBuildText
                {
                    Version = ParkingLotBuildText.CurrentVersion,
                    Kind = kind,
                    Index = i,
                    Value = bytes[i],
                });
        }

        private static bool TryReadBuildText(
            DynamicBuffer<ParkingLotBuildText> buffer, int kind,
            out string value)
        {
            var count = 0;
            for (var i = 0; i < buffer.Length; i++)
            {
                var item = buffer[i];
                if (item.Version != ParkingLotBuildText.CurrentVersion)
                {
                    value = string.Empty;
                    return false;
                }
                if (item.Kind == kind) count++;
            }
            var bytes = new byte[count];
            var seen = new bool[count];
            for (var i = 0; i < buffer.Length; i++)
            {
                var item = buffer[i];
                if (item.Kind != kind) continue;
                if (item.Index < 0 || item.Index >= bytes.Length)
                {
                    value = string.Empty;
                    return false;
                }
                if (seen[item.Index])
                {
                    value = string.Empty;
                    return false;
                }
                seen[item.Index] = true;
                bytes[item.Index] = item.Value;
            }
            value = Encoding.UTF8.GetString(bytes);
            return true;
        }
    }
}
