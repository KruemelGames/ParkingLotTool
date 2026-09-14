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
                    ? ParkingLotTexte.T(
                        "Zweiten Polygonpunkt anklicken · Rechtsklick verwirft",
                        "Click the second outline point · right click discards")
                    : ParkingLotTexte.T(
                        "Zwei Polygonpunkte verbinden · Rechtsklick nimmt zurück "
                            + "· „Trennung fertig“, wenn es passt",
                        "Connect two outline points · right click undoes "
                            + "· press Done splitting when it fits");
                AddMouseTooltip(_hinweis);
                return;
            }
            _hinweis.value = _tool.Ausrichtwahl
                    == ParkingLotToolSystem.Ausrichtschritt.Flaeche
                ? ParkingLotTexte.T(
                    "Eine Teilfläche anklicken · Umschalt zeigt eine schon "
                        + "zugewiesene · „Fertig“, Rechtsklick oder Esc beendet",
                    "Click a sub-area · Shift reveals an assigned one · "
                        + "Done, right click or Esc finishes")
                : ParkingLotTexte.T(
                    "Eine Linie des Umrisses anklicken · „Fertig“, Rechtsklick oder Esc beendet",
                    "Click a line of the outline · Done, right click or Esc finishes");
            AddMouseTooltip(_hinweis);
        }
    }
}
