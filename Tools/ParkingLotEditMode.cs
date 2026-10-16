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

        /**
         * DIE BEARBEITUNG UEBERLEBT EIN AUTOMATISCHES SPEICHERN.
         *
         * Ansage des Nutzers am 2026-10-03: die Auswahl verschwand
         * "irgendwann", und einen Moment davor ruckelte es. Die Schrittspur
         * zeigte den Grund:
         *
         *     Bearbeiten laeuft Lot 125560:1
         *     Bearbeiten Abbruch: Speichern von 03-October-19-41-52
         *     Auswahl: verloren (... existiert=True; geloescht=False)
         *
         * Das Speichern bricht die Bearbeitung ab - richtig, denn ein
         * Spielstand darf keine ausgeblendeten alten Teile enthalten. Nur
         * war es danach vorbei: die Auswahl fiel mit dem Speichervorgang,
         * und der Nutzer musste das Werkzeug verlassen und neu bearbeiten.
         *
         * Deshalb wird die Flaeche gemerkt und nach dem Speichern wieder
         * geoeffnet. Der Spielstand bleibt sauber (der Abbruch kommt vor dem
         * Schreiben), und der Nutzer merkt vom Autospeichern nur ein
         * kurzes Ruckeln.
         */
        private Entity _editNachSpeichern = Entity.Null;
        private bool _speicherLaeuft;
        private float _speicherSeit;

        internal bool IsEditing => _editLot != Entity.Null;

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
            if (!_bauarbeiter && GameManager.instance != null)
                GameManager.instance.onGameSaveLoad += OnEditGameSaveLoad;
        }

        internal void RequestEdit(Entity lot)
        {
            // Kein Bearbeiten, solange Sync oder Reparatur laufen (1.0.6) -
            // die Meldung pulsiert stattdessen.
            if (World.GetOrCreateSystemManaged<ParkingLotSyncSystem>().SperrtWegenArbeit("Bearbeiten")) return;
            if (World.GetOrCreateSystemManaged<ParkingLotHintergrundSystem>().Sperrmeldung(lot)) return;
            if (lot == Entity.Null || !EntityManager.Exists(lot)
                || EntityManager.HasComponent<Deleted>(lot)
                || EntityManager.HasComponent<Temp>(lot)
                || !HasCompleteBuildReceipt(lot))
            {
                ParkingLotSchrittmarke.Setze("Bearbeiten-Anfrage " + lot
                    + " abgelehnt: " + Ablehnungsgrund(lot));
                _uiSystem?.SetStatus(ParkingLotTexte.T("editMode.thisParkingLotHasNoComplete"));
                return;
            }
            if (IsEditing)
            {
                ParkingLotSchrittmarke.Setze("Bearbeiten-Anfrage " + lot
                    + " abgelehnt: es laeuft bereits " + _editLot);
                _uiSystem?.SetStatus(ParkingLotTexte.T("editMode.aParkingLotIsAlreadyBeing"));
                return;
            }
            /*
             * EIN HAENGENDER AUFTRAG DARF DEN NAECHSTEN NICHT BLOCKIEREN.
             *
             * Vorher galt `_pendingEditLot != Entity.Null` als "es wird
             * bereits bearbeitet". Blieb der Auftrag aber haengen, weil CS2
             * die Werkzeugumschaltung verworfen hatte, kam man aus diesem
             * Zustand nur noch durch Verlassen des Werkzeugs heraus - genau
             * das hat der Nutzer als "die Auswahl geht verloren" gesehen.
             *
             * Jetzt ersetzt ein neuer Klick den alten Auftrag. Der letzte
             * Wunsch des Nutzers gewinnt, und `OnUpdateGemessen` holt das
             * Werkzeug so lange zurueck, bis der Auftrag verbraucht ist.
             */
            if (_pendingEditLot != Entity.Null && _pendingEditLot != lot)
                Mod.log.Info("PLT-Bearbeiten: offener Auftrag Lot "
                    + _pendingEditLot.Index + " wird durch Lot " + lot.Index
                    + " ersetzt.");

            _pendingEditLot = lot;
            if (m_ToolSystem.activeTool != this)
                m_ToolSystem.activeTool = this;
        }

        /** Warum `RequestEdit` eine Flaeche nicht annehmen kann - fuer die Spur. */
        private string Ablehnungsgrund(Entity lot)
        {
            if (lot == Entity.Null) return "keine Flaeche";
            if (!EntityManager.Exists(lot)) return "existiert nicht mehr";
            if (EntityManager.HasComponent<Deleted>(lot)) return "ist geloescht";
            if (EntityManager.HasComponent<Temp>(lot)) return "ist temporaer";
            return "kein vollstaendiger Bauzettel";
        }

        private bool HasCompleteBuildReceipt(Entity lot)
            => ParkingLotBaukontextLeser.Vollstaendig(EntityManager, lot);

        /**
         * Der Seitenplan aus dem gelesenen Bauzettel - Zwischenlager, bis
         * `ResetSelection` durch ist. Siehe `TryReadBuildReceipt`.
         */
        private List<(float2 A, float2 B, bool Links, bool Aus)>
            _zoningSeitenplanAusZettel;
        private List<ParkingGeometry.RandzoningLinie> _randzoningAusZettel;
        private List<BusStopPlacement> _busStopsAusZettel;

        /** Wird im ersten Werkzeugframe nach dem UI-Klick ausgefuehrt. */
        private bool TryBeginPendingEdit()
        {
            if (_pendingEditLot == Entity.Null) return false;
            var lot = _pendingEditLot;
            _pendingEditLot = Entity.Null;
            ParkingLotSchrittmarke.Setze("Bearbeiten beginnt Lot " + lot);
            if (!TryReadBuildReceipt(lot, out var receipt, out var points,
                    out var entrances, out var alignments, out var cuts,
                    out var zonen,
                    out var surfaceRoad,
                    out var surfaceDecoration, out var surfaceZoning,
                    out var reason))
            {
                ParkingLotSchrittmarke.Setze("Bearbeiten Lot " + lot
                    + " abgebrochen: " + reason);
                _uiSystem?.SetStatus(ParkingLotTexte.T("editMode.cannotEdit", ("reason", reason)));
                ResetSelection();
                return true;
            }

            ResetSelection();
            LadeZoningBedienwerte(receipt);
            _uiSystem?.LoadBuildReceipt(receipt, surfaceRoad, surfaceDecoration,
                surfaceZoning);
            if (TryReadBuildText(EntityManager.GetBuffer<ParkingLotBuildText>(
                    lot, true), 5, out var zoningRoad))
                _uiSystem?.SetBuildReceiptZoningstrasse(zoningRoad);
            LoadVegetation(lot);
            LoadLaternen(lot);
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
            _busStops.AddRange(_busStopsAusZettel ?? new List<BusStopPlacement>());
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
                _uiSystem?.SetStatus(ParkingLotTexte.T("editMode.editingParkingLotCancelRestoresThe"));
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
                _uiSystem?.SetStatus(ParkingLotTexte.T("editMode.editCancelledVisiblePartsCouldNot"));
            }
            return true;
        }

        private bool ProcessEditLifecycle()
        {
            PruefeUeberlebendeAusgeblendete();
            if (!IsEditing) return false;
            if (!EntityManager.Exists(_editLot))
            {
                ExitEdit("Abbruch: altes Lot existiert nicht mehr", false,
                    resetSelection: true);
                _uiSystem?.SetStatus(ParkingLotTexte.T("editMode.editEndedTheOldParkingLot"));
                return true;
            }
            // Bulldozer-Hover ist Deleted + Temp. Erst Deleted OHNE Temp ist
            // der wirkliche Abriss, genau wie im PLT-Aufraeumer.
            if (EntityManager.HasComponent<Deleted>(_editLot)
                && !EntityManager.HasComponent<Temp>(_editLot))
            {
                ExitEdit("Abbruch: altes Lot wurde während der Bearbeitung gelöscht",
                    false, resetSelection: true);
                _uiSystem?.SetStatus(ParkingLotTexte.T("editMode.editEndedTheOldParkingLot2"));
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
                    "editAbbruch.rebuildFailedTheOldParkingLot");
                return true;
            }
            if (EntityManager.HasComponent<Temp>(_replacementNewLot))
            {
                if (elapsed <= MaterializationTimeoutFrames) return false;
                AbortEdit("Neubau blieb temporär", "editAbbruch.rebuildFailedTheOldParkingLot");
                return true;
            }
            if (!EntityManager.HasComponent<ParkingLotCarrierReference>(
                    _replacementNewLot)
                || !HasCompleteBuildReceipt(_replacementNewLot))
            {
                AbortEdit("Neubau ist unvollständig", "editAbbruch.rebuildFailedTheOldParkingLot");
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
                    "editAbbruch.rebuildFailedTheOldParkingLot");
                return true;
            }

            if (_editParkingFeeKnown && !_replacementEconomyTransferred
                && !_editEconomySystem.TryInitializeReplacement(
                    _replacementNewLot, _editParkingFee))
            {
                if (elapsed <= MaterializationTimeoutFrames) return false;
                AbortEdit("Wirtschaft des Neubaus konnte nicht geeicht werden",
                    "editAbbruch.rebuildFailedTheParkingFeeOr");
                return true;
            }
            if (_editParkingFeeKnown) _replacementEconomyTransferred = true;
            if (_editBuildingEconomyEnabled
                && !HasEnabledCompanion(_replacementNewLot))
            {
                if (elapsed <= MaterializationTimeoutFrames) return false;
                AbortEdit("Wirtschaftsbegleiter des Neubaus fehlt",
                    "editAbbruch.rebuildFailedTheOldParkingLot");
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
            UebertrageZoningbestand(_editLot, _replacementNewLot, carrier);
            if (!_bauarbeiter) StelleVersorgungsanschluesseWiederHer(carrier);
            else
            {
                UebertrageAutoVersorgungsbestand(_editLot, _replacementNewLot, carrier);
                UebertrageBushaltbestand(_editLot, _replacementNewLot, carrier);
            }
            // Unveraenderte eigene Leitungen und Halte behalten ihre IDs.
            // Die Nacharbeit baut ausschliesslich fehlende Anschluesse/Halte.
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
            var abrissLot = _bauarbeiter && _erhalteneNetzteile.Count > 0 ? HintergrundBesitzanker(old,next,alterTraeger) : old;
            ParkingLotAbriss.Sichern(EntityManager, abrissLot);
            EntityManager.AddComponent<Deleted>(abrissLot);
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
            if (EntityManager.HasBuffer<ParkingLotErhaltenerKurs>(old)) EntityManager.RemoveComponent<ParkingLotErhaltenerKurs>(old);
            if (EntityManager.HasBuffer<ParkingLotRueckwegkurs>(old)) EntityManager.RemoveComponent<ParkingLotRueckwegkurs>(old);
            if (EntityManager.HasComponent<ParkingLotOffenerErsatz>(old)) EntityManager.RemoveComponent<ParkingLotOffenerErsatz>(old);
            if (EntityManager.HasBuffer<ParkingLotRueckweganschluss>(old)) EntityManager.RemoveComponent<ParkingLotRueckweganschluss>(old);
            if (EntityManager.HasBuffer<ParkingLotStufeAKnoten>(old)) EntityManager.RemoveComponent<ParkingLotStufeAKnoten>(old);
            ParkingLotStufeA.VergissPrefab(EntityManager, old);
            // Der Hintergrund meldet sie selbst im exklusiven Bild an.
            if (!_bauarbeiter && _erhalteneNetzteile.Count > 0)
                Mod.log.Info("PLT-Bearbeiten: " + MeldeErhalteneNetzteileAn()
                    + " erhaltene Netzteile zum Auffrischen der Fahrspuren angemeldet.");
            ClearEditState();
            if (!_bauarbeiter) MerkeAutoVersorgung(carrier, old, alterTraeger);
            Mod.log.Info("PLT-Bearbeiten: Ausstieg durch Übernehmen; neues Lot "
                + next.Index + " steht vollständig, altes Lot " + old.Index
                + " dem PLT-Aufräumer übergeben.");
            _uiSystem?.SetStatus(ParkingLotTexte.T("editMode.changesApplied"));
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
            MerkeZoningbestand(layout);
            _editBaselinePending = false;
        }

        private bool TryFinishUnchangedEdit()
        {
            if (!IsEditing || _editBaselinePending || _areaPreviewLayout == null)
                return false;
            if (EntityManager.HasBuffer<ParkingLotRueckwegkurs>(_editLot)) return false;
            // Gleicher Bauzettel heisst nicht gleicher Bestand: fehlen Wege
            // (Abbruch-Schaden 2026-10-02) oder fahren sie noch mit altem
            // Prefabstand, muss gebaut werden.
            var traeger = EntityManager.HasComponent<ParkingLotCarrierReference>(_editLot)
                ? EntityManager.GetComponentData<ParkingLotCarrierReference>(_editLot).Carrier
                : Entity.Null;
            if (!HatGebauteFahrspuren(traeger) || BrauchtFahrwegeNeubau(traeger)) return false;
            if (AreaPreviewSignature(_areaPreviewLayout) != _editBaselineSignature)
                return false;
            RestoreHiddenParts();
            var lot = _editLot;
            ClearEditState();
            ResetSelection();
            Mod.log.Info("PLT-Bearbeiten: Ausstieg durch Übernehmen ohne Änderungen; "
                + "Lot " + lot.Index + " unverändert wieder eingeblendet, nichts gebaut.");
            _uiSystem?.SetStatus(ParkingLotTexte.T("editMode.noChangesNothingWasRebuilt"));
            return true;
        }

        private void AbortEdit(string reason, string statusSchluessel)
        {
            if (!IsEditing) return;
            ParkingLotSchrittmarke.Setze("Bearbeiten Abbruch: " + reason);
            RollBackReplacement();
            RestoreHiddenParts();
            var lot = _editLot;
            bool rueckweg = EntityManager.HasBuffer<ParkingLotRueckwegkurs>(lot);
            if (rueckweg && !_bauarbeiter)
                World.GetOrCreateSystemManaged<ParkingLotHintergrundSystem>().Einreihen(lot, true);
            if (_bauarbeiter) throw new InvalidOperationException("Ersatzuebernahme abgebrochen: " + reason);
            ClearEditState();
            ResetSelection();
            Mod.log.Info("PLT-Bearbeiten: Ausstieg durch Abbruch (" + reason
                + "); altes Lot " + lot.Index + (rueckweg ? ": Rueckweg eingereiht, Nachpruefung ausstehend." : " wieder eingeblendet."));
            _uiSystem?.SetStatus(rueckweg ? ParkingLotTexte.T("editMode.editCancelledTheOldPathsAre") : ParkingLotTexte.T(statusSchluessel));
        }

        private void ExitEdit(string reason, bool restoreOld, bool resetSelection)
        {
            if (!IsEditing) return;
            RollBackReplacement();
            if (restoreOld) RestoreHiddenParts();
            else _hiddenByEdit.Clear();
            var lot = _editLot;
            if (restoreOld && EntityManager.HasBuffer<ParkingLotRueckwegkurs>(lot))
                World.GetOrCreateSystemManaged<ParkingLotHintergrundSystem>().Einreihen(lot, true);
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
                else
                {
                    ParkingLotAbriss.Sichern(EntityManager, _replacementNewLot);
                    EntityManager.AddComponent<Deleted>(_replacementNewLot);
                }
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
                Zahl("Gassenbreite", gespeichert.Gassenbreite,
                    wirksam.Gassenbreite, 1e-6);
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
                        + "uebernommen, 20 Werte geprueft, keine Abweichung.");
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
            VerwerfeEdithoehen();
            _alteZoningkurse = null;
            _erhalteneNetzteile.Clear();
            _erhalteneKursketten.Clear(); _hintergrundGassenkurse.Clear();
            _zoningErhalten = false;
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
            _editNachSpeichern = Entity.Null;
            _speicherLaeuft = false;
            if (IsEditing)
                AbortEdit("Mod oder Spiel wird beendet",
                    "editAbbruch.editCancelled");
        }

        private void OnEditGameSaveLoad(string saveName, string previewUri,
                                        bool start, bool success)
        {
            if (start)
            {
                _speicherLaeuft = true;
                _speicherSeit = UnityEngine.Time.realtimeSinceStartup;
                if (!IsEditing) return;
                _editNachSpeichern = _editLot;
                AbortEdit("Speichern von " + saveName,
                    "editAbbruch.editWasAutomaticallyCancelledBeforeSaving");
                return;
            }

            // Ende des Speicherns: die Bearbeitung wieder aufnehmen.
            _speicherLaeuft = false;
            NimmBearbeitungNachSpeichernWiederAuf(success);
        }

        /**
         * Oeffnet die beim Speichern abgebrochene Bearbeitung wieder.
         *
         * Der Schluessel ist die FLaeche selbst, nicht der Name: der
         * Spielstand ist derselbe, die Entity hat dieselbe Gueltigkeit. Fehlt
         * sie inzwischen oder wurde sie geloescht, passiert nichts.
         */
        private void NimmBearbeitungNachSpeichernWiederAuf(bool erfolg)
        {
            if (_editNachSpeichern == Entity.Null) return;
            var lot = _editNachSpeichern;
            _editNachSpeichern = Entity.Null;
            if (!erfolg) return;
            if (!EntityManager.Exists(lot)
                || EntityManager.HasComponent<Deleted>(lot)) return;
            ParkingLotSchrittmarke.Setze(
                "Bearbeiten wird nach dem Speichern fortgesetzt: Lot " + lot);
            RequestEdit(lot);
        }

        [Preserve]
        protected override void OnGamePreload(
            Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            if (_bauarbeiter) return;
            base.OnGamePreload(purpose, mode);
            _pendingEditLot = Entity.Null;
            _editNachSpeichern = Entity.Null;
            _speicherLaeuft = false;
            if (IsEditing)
                AbortEdit("Spielstandwechsel", "editAbbruch.editCancelled");
        }

        [Preserve]
        protected override void OnDestroy()
        {
            if (GameManager.instance != null)
                GameManager.instance.onGameSaveLoad -= OnEditGameSaveLoad;
            if (!_bauarbeiter) CancelEditingForShutdown();
            if (_bauarbeiter) HintergrundPortionenBeenden();
            // Die Hoehenkarten-Kopie ist Allocator.Persistent und so gross
            // wie die ganze Karte. Endet das Spiel mitten in einem Edit-Bau,
            // gibt sie sonst niemand frei. Der Aufruf ist wiederholbar.
            VerwerfeEdithoehen();
            base.OnDestroy();
        }

        /**
         * Der Bauzettel aus dem LAUFENDEN Werkzeugzustand - der Normalfall
         * nach jedem Bau. Sammelt nur ein und gibt an `SchreibeBauzettel`
         * weiter; dieselbe Schreibfunktion stellt verwaiste Parkplaetze aus
         * dem Bauprotokoll wieder her. Eine Wahrheit ueber das Format.
         */
        private bool WriteBuildReceipt(Entity lot, LayoutSettings settings,
                                       float3[] worldPoints)
        {
            if (settings == null) return false;
            var quelle = new Bauzettelquelle
            {
                Settings = settings,
                Punkte = worldPoints,
                MedianWidth = _uiSystem?.AktuelleMedianbreite ?? settings.Md,
                GreenMedian = _uiSystem?.MittelgruenAn ?? settings.Md > 0,
                CrossBays = _uiSystem?.AktuelleQuerbuchten
                    ?? (System.Math.Abs(settings.Cr) / settings.Sw - (settings.Qk ? 2 : 0)),
                SurfaceRoadOn = _uiSystem?.FlaecheStrasseAn ?? true,
                SurfaceDecorationOn = _uiSystem?.FlaecheDekoAn ?? true,
                SurfaceApronOn = _uiSystem?.VorflaecheAn ?? true,
                BayIcons = _uiSystem?.Buchtsymbole ?? true,
                ZoningWinkelmodus = ZoningWinkelmodus,
                ZoningReglerwinkel = ZoningReglerwinkel,
                ZoningAussentiefeVorwahl = ZoningTiefeVorwahl,
                ZoningAusrichtwinkel = ZoningAusrichtwinkel,
                Ausrichtwinkel = Ausrichtwinkel,
                Ausrichtungen = Ausrichtungen,
                Trennschnitte = Trennschnitte,
                Zoningflaechen = Zoningflaechen,
                Seitenplan = _zoningSeitenPlan,
                Randzoning = Randzoninglinien,
                FlaecheStrasse = _uiSystem?.FlaecheStrasse ?? string.Empty,
                FlaecheDekoration = _uiSystem?.FlaecheDekoration ?? string.Empty,
                FlaecheZoning = _uiSystem?.FlaecheZoning ?? string.Empty,
                VegetationAusProtokoll = null,
            };
            return SchreibeBauzettel(lot, quelle);
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
            var gelesen = ParkingLotBaukontextLeser.TryRead(EntityManager, lot,
                out var kontext, out reason);
            receipt = kontext?.Zettel ?? default;
            points = kontext?.Punkte ?? Array.Empty<float3>();
            entrances = kontext?.Zugaenge ?? Array.Empty<Entrance>();
            alignments = kontext?.Ausrichtungen;
            cuts = kontext?.Schnitte;
            zonen = kontext?.Zonen;
            surfaceRoad = kontext?.FlaecheStrasse ?? string.Empty;
            surfaceDecoration = kontext?.FlaecheDekoration ?? string.Empty;
            surfaceZoning = kontext?.FlaecheZoning ?? string.Empty;
            if (gelesen)
            {
                // Nur dieser Werkzeugadapter uebernimmt die Bedienlisten.
                // Der gemeinsame Leser selbst hat keinerlei Seiteneffekte.
                _zoningSeitenplanAusZettel = kontext.Seitenplan;
                _randzoningAusZettel = kontext.Randzoning;
                _busStopsAusZettel = kontext.Bushalte;
            }
            return gelesen;
        }

        private static bool TryReadBuildText(
            DynamicBuffer<ParkingLotBuildText> buffer, int kind, out string value)
            => ParkingLotBaukontextLeser.TryReadBuildText(buffer, kind, out value);
    }
}
