using System.Threading.Tasks;
using AIInterviewer.App;
using AIInterviewer.Core.Api;
using AIInterviewer.UI.Kit;
using UnityEngine.UIElements;

namespace AIInterviewer.UI.Framework
{
    public enum TabId
    {
        None,
        Home,
        History,
        Progress,
        Profile
    }

    /// <summary>
    /// Base class for screens. A screen builds its visual tree once (<see cref="Build"/>) and receives
    /// lifecycle callbacks from the <see cref="Navigator"/>. Content is padded for the device safe area.
    /// </summary>
    public abstract class AppScreen
    {
        private bool _built;

        /// <summary>Bumped by the <see cref="Navigator"/> on every show/hide; stale hide callbacks compare it.</summary>
        internal int NavVersion;

        protected AppScreen(AppServices app)
        {
            App = app;
            Root = new VisualElement();
            Root.AddToClassList("screen");
            Root.name = GetType().Name;
            Content = new VisualElement();
            Content.AddToClassList("screen-content");
            Root.Add(Content);
        }

        protected AppServices App { get; }

        public VisualElement Root { get; }

        /// <summary>Safe-area padded container for the screen's content.</summary>
        protected VisualElement Content { get; }

        /// <summary>Show the 3D interview room behind this screen.</summary>
        public virtual bool ShowsInterviewRoom => false;

        public virtual TabId Tab => TabId.None;

        internal void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
            if (ShowsInterviewRoom) Root.AddToClassList("screen--transparent");
            Build();
        }

        protected abstract void Build();

        public virtual void OnShow()
        {
        }

        public virtual void OnHide()
        {
        }

        /// <summary>Android back / edge swipe. Return true if handled.</summary>
        public virtual bool OnBack() => false;

        internal void ApplySafeArea(float top, float bottom, float left, float right, float tabBarHeight)
        {
            Content.style.paddingTop = top;
            Content.style.paddingBottom = bottom + (Tab != TabId.None ? tabBarHeight : 0f);
            Content.style.paddingLeft = 20f + left;
            Content.style.paddingRight = 20f + right;
            OnSafeAreaChanged(top, bottom);
        }

        protected virtual void OnSafeAreaChanged(float top, float bottom)
        {
        }

        /// <summary>Runs an API call with standard error presentation. Returns default on failure.</summary>
        protected async Task<T> Guard<T>(Task<T> task, string context = null)
        {
            try
            {
                return await task;
            }
            catch (ApiException ex)
            {
                App.HandleApiError(ex, context);
                return default;
            }
        }

        protected async Task<bool> Guard(Task task, string context = null)
        {
            try
            {
                await task;
                return true;
            }
            catch (ApiException ex)
            {
                App.HandleApiError(ex, context);
                return false;
            }
        }

        protected static Label T(string text, string classes = "body") => UiKit.Text(text, classes);
    }
}
