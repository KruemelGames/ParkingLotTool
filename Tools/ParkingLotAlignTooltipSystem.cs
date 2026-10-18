using Game.UI.Tooltip;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * Der Text am Mauszeiger, solange eine Bezugslinie gewaehlt wird.
     *
     * WARUM AM ZEIGER UND NICHT NUR IM PANEL. Ansage des Nutzers: *"Nach dem
     * Druecken muss dem User per Mouse-Info also Text neben dem Cursor
     * mitgeteilt werden was er tun soll."* Wer gerade auf den Umriss schaut,
     * liest keine Statuszeile am Bildschirmrand - der Blick ist dort, wo der
     * Klick hin soll.
     *
     * Der Weg ist derselbe, den die Vanilla-Werkzeuge nehmen:
     * `TooltipSystemBase.AddMouseTooltip` mit einem `StringTooltip`. Kein
     * eigenes Overlay, keine eigene Positionsrechnerei - damit sitzt der Text
     * dort, wo CS2 seine Werkzeughinweise immer hinsetzt, und folgt dessen
     * Skalierung.
     */
    public sealed partial class ParkingLotAlignTooltipSystem : TooltipSystemBase
    {
        private ParkingLotToolSystem _tool;
        private StringTooltip _hinweis;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _tool = World.GetOrCreateSystemManaged<ParkingLotToolSystem>();
            _hinweis = new StringTooltip { path = "parkingLotToolAlign" };
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (_tool?.MustergriffHinweis==true)
            {
                _hinweis.value=ParkingLotTexte.T("preview.moveArea");
                AddMouseTooltip(_hinweis); return;
            }
            if (_tool == null || !_tool.AusrichtWahlAktiv) return;
            // Der Hinweis muss den SCHRITT nennen, nicht den Vorgang - sonst
            // stuende bei der Flaechenwahl "eine Linie anklicken".
            // Der Ausweg gehoert in den Hinweis, sonst sucht man ihn.
            // Seit es den Knopf gibt, wird er zuerst genannt - er ist der
            // Weg, den man sieht.
            if (_tool.Ausrichtwahl
                == ParkingLotToolSystem.Ausrichtschritt.Trennen)
            {
                _hinweis.value = _tool.TrennAnfang >= 0
                    ? ParkingLotTexte.T("alignTooltipSystem.clickTheSecondOutlinePointRight")
                    : ParkingLotTexte.T("alignTooltipSystem.connectTwoOutlinePointsRightClick");
                AddMouseTooltip(_hinweis);
                return;
            }
            _hinweis.value = _tool.Ausrichtwahl
                    == ParkingLotToolSystem.Ausrichtschritt.Flaeche
                ? ParkingLotTexte.T("alignTooltipSystem.clickASubAreaShiftReveals")
                : ParkingLotTexte.T("alignTooltipSystem.clickALineOfTheOutline");
            AddMouseTooltip(_hinweis);
        }
    }
}
