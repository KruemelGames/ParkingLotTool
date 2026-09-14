using Game.UI.Tooltip;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * DER TEXT AM ZEIGER, SOLANGE AM ZONING GEARBEITET WIRD.
     *
     * Zwei Ansagen des Nutzers am 2026-09-03 treffen sich hier:
     *
     *   *"Im User-Feedback sollte am besten auch stehen X * Y tiles anstatt
     *   nur im UI oben."*
     *
     *   *"Toggle road sides wird beim Klicken auf Strassenhaelften/in der
     *   Naehe davon nicht erkannt oder per User-Feedback nicht mitgeteilt."*
     *
     * Beide Male geht es um dasselbe: wer auf die Flaeche schaut, liest
     * keine Statuszeile am Bildschirmrand. Die Zahl, um die es geht, muss
     * dort stehen, wo der Blick ohnehin ist.
     *
     * Derselbe Weg wie beim Ausrichten - `AddMouseTooltip` mit einem
     * `StringTooltip`, also die Stelle, an die CS2 seine Werkzeughinweise
     * immer setzt.
     */
    public sealed partial class ParkingLotZoningTooltipSystem : TooltipSystemBase
    {
        private ParkingLotToolSystem _tool;
        private StringTooltip _hinweis;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _tool = World.GetOrCreateSystemManaged<ParkingLotToolSystem>();
            _hinweis = new StringTooltip { path = "parkingLotToolZoning" };
        }

        [Preserve]
        protected override void OnUpdate()
        {
            var text = _tool?.ZoningZeigertext;
            if (string.IsNullOrEmpty(text)) return;
            _hinweis.value = text;
            AddMouseTooltip(_hinweis);
        }
    }
}
