using Game.UI.Localization;
using Game.UI.Tooltip;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /// <summary>Zeigt Debug-Bestaetigungen und dauerhafte Zufahrts-Hinweise.</summary>
    public sealed partial class ParkingLotDebugTooltipSystem : TooltipSystemBase
    {
        internal const float VisibleSeconds = 4f;

        private StringTooltip _message;
        private StringTooltip _entranceHint;
        private StringTooltip _vorschauHinweis;
        private string _vorschauHinweisText;
        private Game.Tools.ToolSystem _toolSystem;
        private float _visibleUntil;
        private string _entranceHintText;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _message = new StringTooltip
            {
                path = "parkingLotDebugDump",
                // StringTooltip und der CS2-Renderer unterstützen null ausdrücklich;
                // dadurch wird kein potenziell abstürzender Icon-Pfad aufgelöst.
                icon = null,
                color = TooltipColor.Success,
            };
            _entranceHint = new StringTooltip
            {
                path = "parkingLotEntranceHint",
                icon = null,
                // Warning ist die eigene, sonst unbenutzte Farbklasse. Das UI
                // gibt ihr nur waehrend des Zufahrt-Modus den festgelegten
                // #8A4300-Hintergrund; die Debug-Bestaetigung bleibt Success.
                color = TooltipColor.Warning,
            };
            // Zu klein fuer eine Buchtreihe (2026-10-13): steht am Cursor, weil
            // die Statuszeile nicht jeder liest; ausfuehrlicher als der Bauknopf.
            _vorschauHinweis = new StringTooltip
            {
                path = "parkingLotPreviewHint",
                icon = null,
                color = TooltipColor.Warning,
            };
            _toolSystem = World.GetOrCreateSystemManaged<Game.Tools.ToolSystem>();
        }

        internal void SetVorschauHinweis(string text)
        {
            text ??= string.Empty;
            if (text == _vorschauHinweisText) return;
            _vorschauHinweisText = text;
            _vorschauHinweis.value = LocalizedString.Value(text);
        }

        internal void Show(string text)
        {
            _message.value = LocalizedString.Value(text);
            _visibleUntil = UnityEngine.Time.realtimeSinceStartup + VisibleSeconds;
        }

        internal void SetEntranceHint(string text)
        {
            text ??= string.Empty;
            if (text == _entranceHintText) return;
            _entranceHintText = text;
            _entranceHint.value = LocalizedString.Value(text);
        }

        internal void ClearEntranceHint() => SetEntranceHint(string.Empty);

        [Preserve]
        protected override void OnUpdate()
        {
            if (_message != null
                && UnityEngine.Time.realtimeSinceStartup < _visibleUntil)
                AddMouseTooltip(_message);
            // Kein Zeitfenster: der Hinweis bleibt bei jedem UI-Update am
            // Cursor, bis das Werkzeug den Zustand ausdruecklich beendet.
            if (_entranceHint != null && !string.IsNullOrEmpty(_entranceHintText))
                AddMouseTooltip(_entranceHint);
            // Nur solange das PLT-Werkzeug offen ist - auch wenn ein anderer Weg
            // den Zustand einmal nicht leert, bleibt er nie an fremden Werkzeugen.
            if (_vorschauHinweis != null && !string.IsNullOrEmpty(_vorschauHinweisText)
                && _toolSystem?.activeTool is ParkingLotToolSystem)
                AddMouseTooltip(_vorschauHinweis);
        }
    }
}
