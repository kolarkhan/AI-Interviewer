using AIInterviewer.App;
using AIInterviewer.UI.Framework;
using AIInterviewer.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIInterviewer.UI.Screens
{
    public sealed class OnboardingScreen : AppScreen
    {
        private static readonly (IconKind icon, string title, string body)[] Pages =
        {
            (IconKind.Mic, "Practice out loud with a realistic interviewer",
                "Speak naturally. Alex asks role-specific questions and follows up on your answers, just like a real interview."),
            (IconKind.Chart, "Get clear, actionable feedback",
                "After each session you receive a structured report: scores, strengths, areas to improve and better answer structures."),
            (IconKind.Shield, "Private by default",
                "Your voice is never recorded or stored. Transcripts are deleted after your report unless you choose to keep them.")
        };

        private int _page;
        private VisualElement _pageHost;
        private VisualElement _dotsHost;
        private Button _next;
        private Button _secondary;
        private Vector2 _pointerStart;

        public OnboardingScreen(AppServices app) : base(app)
        {
        }

        protected override void Build()
        {
            var skip = UiKit.Button("Skip", Finish, ButtonStyle.Ghost, small: true);
            skip.style.alignSelf = Align.FlexEnd;
            skip.style.marginTop = 8;
            Content.Add(skip);

            _pageHost = new VisualElement();
            _pageHost.style.flexGrow = 1;
            _pageHost.style.justifyContent = Justify.Center;
            Content.Add(_pageHost);

            _dotsHost = new VisualElement();
            Content.Add(_dotsHost);
            Content.Add(UiKit.Spacer(24));

            _next = UiKit.Button("Next", Next);
            _next.AddToClassList("btn--block");
            Content.Add(_next);
            _secondary = UiKit.Button("I already have an account", () =>
            {
                App.Settings.OnboardingSeen = true;
                App.Nav.ResetTo(new LoginScreen(App));
            }, ButtonStyle.Ghost);
            _secondary.style.marginTop = 6;
            _secondary.style.marginBottom = 16;
            Content.Add(_secondary);

            // Horizontal swipe between pages.
            Root.RegisterCallback<PointerDownEvent>(e => _pointerStart = e.position);
            Root.RegisterCallback<PointerUpEvent>(e =>
            {
                float dx = e.position.x - _pointerStart.x;
                if (Mathf.Abs(dx) < 60f) return;
                if (dx < 0) Next();
                else if (_page > 0) Show(_page - 1);
            });
            Show(0);
        }

        private void Next()
        {
            if (_page < Pages.Length - 1) Show(_page + 1);
            else Finish();
        }

        private void Finish()
        {
            App.Settings.OnboardingSeen = true;
            App.Nav.ResetTo(new SignUpScreen(App), Transition.Slide);
        }

        private void Show(int page)
        {
            _page = page;
            var (icon, title, body) = Pages[page];
            _pageHost.Clear();
            var col = new VisualElement();
            col.AddToClassList("splash-fade");
            col.style.alignItems = Align.Center;

            var halo = new VisualElement();
            halo.style.width = 168;
            halo.style.height = 168;
            halo.style.borderTopLeftRadius = 84;
            halo.style.borderTopRightRadius = 84;
            halo.style.borderBottomLeftRadius = 84;
            halo.style.borderBottomRightRadius = 84;
            halo.style.backgroundColor = new Color(0.36f, 0.55f, 1f, 0.12f);
            halo.style.alignItems = Align.Center;
            halo.style.justifyContent = Justify.Center;
            halo.Add(Brand.IconBadge(104, icon));
            col.Add(halo);
            col.Add(UiKit.Spacer(36));
            var t = T(title, "title");
            t.style.unityTextAlign = TextAnchor.MiddleCenter;
            col.Add(t);
            col.Add(UiKit.Spacer(12));
            var b = T(body, "body");
            b.style.unityTextAlign = TextAnchor.MiddleCenter;
            col.Add(b);
            _pageHost.Add(col);
            col.schedule.Execute(() => col.AddToClassList("splash-fade--in")).StartingIn(30);

            _dotsHost.Clear();
            _dotsHost.Add(UiKit.Dots(Pages.Length, page));
            bool last = page == Pages.Length - 1;
            UiKit.SetButtonText(_next, last ? "Create free account" : "Next");
        }
    }
}
