using System;
using AIInterviewer.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIInterviewer.UI.Framework
{
    /// <summary>
    /// Creates the UI Toolkit document at runtime (PanelSettings, theme, layers) and tracks the device safe area.
    /// Layers (bottom to top): screens, tab bar, offline banner, overlay (toasts, dialogs, veil).
    /// </summary>
    public sealed class UiRoot : MonoBehaviour
    {
        public const float TabBarHeight = 60f;

        private UIDocument _document;
        private VisualElement _tabBar;
        private VisualElement _offlineBanner;
        private Rect _lastSafeArea;
        private Vector2 _lastSize;
        private Action<TabId> _onTab;

        public VisualElement RootElement { get; private set; }
        public VisualElement ScreensLayer { get; private set; }
        public Navigator Navigator { get; private set; }
        public Overlay Overlay { get; private set; }

        public float SafeTop { get; private set; }
        public float SafeBottom { get; private set; }
        public float SafeLeft { get; private set; }
        public float SafeRight { get; private set; }

        public event Action SafeAreaChanged;

        public void Initialize(Action<TabId> onTabSelected)
        {
            _onTab = onTabSelected;
            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.name = "AIInterviewerPanel";
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(390, 844);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.35f;
            panel.clearColor = false;
            var theme = Resources.Load<ThemeStyleSheet>("UI/AppTheme");
            if (theme != null) panel.themeStyleSheet = theme;
            else Debug.LogWarning("[UI] Resources/UI/AppTheme.tss not found; default controls may be unstyled.");

            // Configure the document while inactive so it attaches to the panel once, fully set up, on enable.
            var docGo = new GameObject("UIDocument");
            docGo.SetActive(false);
            docGo.transform.SetParent(transform, false);
            _document = docGo.AddComponent<UIDocument>();
            _document.panelSettings = panel;
            docGo.SetActive(true);

            RootElement = _document.rootVisualElement;
            var sheet = Resources.Load<StyleSheet>("UI/Theme");
            if (sheet != null) RootElement.styleSheets.Add(sheet);
            else Debug.LogError("[UI] Resources/UI/Theme.uss not found.");
            RootElement.style.flexGrow = 1;

            ScreensLayer = new VisualElement();
            ScreensLayer.AddToClassList("fill");
            ScreensLayer.pickingMode = PickingMode.Ignore;
            RootElement.Add(ScreensLayer);

            _tabBar = BuildTabBar();
            RootElement.Add(_tabBar);

            _offlineBanner = new VisualElement();
            _offlineBanner.AddToClassList("offline-banner");
            _offlineBanner.pickingMode = PickingMode.Ignore;
            _offlineBanner.Add(new Icon(IconKind.WifiOff, 16f, Color.white));
            _offlineBanner.Add(UiKit.Text("You're offline", "offline-banner__text"));
            RootElement.Add(_offlineBanner);

            var overlayLayer = new VisualElement();
            overlayLayer.AddToClassList("fill");
            RootElement.Add(overlayLayer);
            Overlay = new Overlay(overlayLayer);

            Navigator = new Navigator(ScreensLayer);
            Navigator.Prepare = ApplySafeArea;
            Navigator.CurrentChanged += OnScreenChanged;
            RootElement.RegisterCallback<GeometryChangedEvent>(_ => UpdateSafeArea(force: true));
        }

        public void SetOffline(bool offline)
        {
            _offlineBanner.EnableInClassList("offline-banner--visible", offline);
        }

        private void Update()
        {
            if (RootElement == null) return;
            if (Screen.safeArea != _lastSafeArea || new Vector2(Screen.width, Screen.height) != _lastSize)
            {
                UpdateSafeArea(force: false);
            }
        }

        private void UpdateSafeArea(bool force)
        {
            var area = Screen.safeArea;
            var size = new Vector2(Screen.width, Screen.height);
            if (!force && area == _lastSafeArea && size == _lastSize) return;
            float panelWidth = RootElement.layout.width;
            if (float.IsNaN(panelWidth) || panelWidth <= 0f || Screen.width <= 0) return;
            _lastSafeArea = area;
            _lastSize = size;
            float scale = panelWidth / Screen.width; // screen pixels -> panel points
            SafeLeft = area.xMin * scale;
            SafeRight = (Screen.width - area.xMax) * scale;
            SafeBottom = area.yMin * scale;                  // Screen space origin is bottom-left
            SafeTop = (Screen.height - area.yMax) * scale;
            _tabBar.style.paddingBottom = SafeBottom;
            _offlineBanner.style.paddingTop = SafeTop + 4f;
            Overlay.SetSafeTop(SafeTop);
            Overlay.SetSafeBottom(SafeBottom);
            if (Navigator.Current != null) ApplySafeArea(Navigator.Current);
            SafeAreaChanged?.Invoke();
        }

        private void ApplySafeArea(AppScreen screen)
        {
            screen.ApplySafeArea(SafeTop, SafeBottom, SafeLeft, SafeRight, TabBarHeight + 8f);
        }

        private void OnScreenChanged(AppScreen screen)
        {
            bool show = screen != null && screen.Tab != TabId.None;
            _tabBar.EnableInClassList("tab-bar--hidden", !show);
            _tabBar.pickingMode = show ? PickingMode.Position : PickingMode.Ignore;
            foreach (var child in _tabBar.Children())
            {
                if (child.userData is TabId id)
                {
                    bool active = show && id == screen.Tab;
                    child.EnableInClassList("tab--active", active);
                    var icon = child.Q<Icon>();
                    if (icon != null) icon.Color = active ? Palette.Text : Palette.TextMuted;
                }
            }
        }

        private VisualElement BuildTabBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("tab-bar");
            bar.AddToClassList("tab-bar--hidden");
            AddTab(bar, TabId.Home, IconKind.Home, "Home");
            AddTab(bar, TabId.History, IconKind.History, "History");
            AddTab(bar, TabId.Progress, IconKind.Chart, "Progress");
            AddTab(bar, TabId.Profile, IconKind.User, "Profile");
            return bar;
        }

        private void AddTab(VisualElement bar, TabId id, IconKind icon, string label)
        {
            var tab = new VisualElement { userData = id };
            tab.AddToClassList("tab");
            var i = new Icon(icon, 22f, Palette.TextMuted);
            tab.Add(i);
            var l = UiKit.Text(label, "tab__label");
            l.pickingMode = PickingMode.Ignore;
            tab.Add(l);
            tab.RegisterCallback<ClickEvent>(_ => _onTab?.Invoke(id));
            bar.Add(tab);
        }
    }
}
