using System;
using System.Threading.Tasks;
using AIInterviewer.App;
using AIInterviewer.Core.Api;
using AIInterviewer.UI.Framework;
using AIInterviewer.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIInterviewer.UI.Screens
{
    public sealed class HomeScreen : AppScreen
    {
        private ScrollView _scroll;
        private bool _loading;

        public HomeScreen(AppServices app) : base(app)
        {
        }

        public override TabId Tab => TabId.Home;

        protected override void Build()
        {
            _scroll = UiKit.Scroll();
            Content.Add(_scroll);
        }

        public override void OnShow()
        {
            MainThread.Run(LoadAsync, "Home");
        }

        private async Task LoadAsync()
        {
            if (_loading) return;
            _loading = true;
            if (_scroll.childCount == 0) RenderSkeleton();
            try
            {
                var subTask = App.GetSubscriptionAsync(refresh: true);
                var catalogTask = App.GetCatalogAsync();
                var recentTask = App.Api.ListInterviewsAsync(3);
                await Task.WhenAll(subTask, catalogTask, recentTask);
                Render(subTask.Result, catalogTask.Result, recentTask.Result);
            }
            catch (ApiException ex)
            {
                if (ex.Kind == ApiFailureKind.Unauthorized) return;
                _scroll.Clear();
                _scroll.Add(UiKit.StatusView(IconKind.WifiOff, "Couldn't load your dashboard",
                    ex.Kind == ApiFailureKind.Network ? "Check your internet connection and try again." : ex.Message,
                    "Retry", () => MainThread.Run(LoadAsync, "Home.Retry"), iconColor: Palette.Warning));
            }
            finally
            {
                _loading = false;
            }
        }

        private void RenderSkeleton()
        {
            _scroll.Clear();
            _scroll.Add(UiKit.Spacer(20));
            foreach (var h in new[] { 40f, 190f, 46f, 74f, 74f })
            {
                var s = new VisualElement();
                s.AddToClassList("skeleton");
                s.style.height = h;
                s.style.marginBottom = 14;
                _scroll.Add(s);
            }
        }

        private void Render(SubscriptionResponse sub, Catalog catalog, InterviewList recent)
        {
            _scroll.Clear();
            var user = App.Auth.User;

            // Header
            var greeting = UiKit.Column(T(Greeting() + ",", "caption"), T(user?.FirstName ?? "there", "title"));
            greeting.style.flexGrow = 1;
            bool premium = sub?.Plan?.Id == "premium";
            var planBadge = UiKit.Badge(premium ? "PREMIUM" : "FREE PLAN", premium ? "gold" : "accent",
                premium ? IconKind.Crown : (IconKind?)null);
            planBadge.RegisterCallback<ClickEvent>(_ => App.OpenSubscription());
            var header = UiKit.Row(greeting, planBadge);
            header.style.marginTop = 16;
            header.style.marginBottom = 18;
            _scroll.Add(header);

            if (catalog.DemoMode)
            {
                _scroll.Add(UiKit.Banner("Demo mode: the server has no AI key configured. The interviewer is scripted and " +
                                         "reports contain sample data.", "warning", IconKind.Info));
            }

            // Hero
            var hero = UiKit.Card("card--hero");
            UiKit.HeroGradient(hero);
            var overline = T("PRACTICE NOW", "overline");
            overline.style.color = new Color(1f, 1f, 1f, 0.75f);
            hero.Add(overline);
            hero.Add(UiKit.Spacer(6));
            var heroTitle = T("Start a mock interview", "title");
            heroTitle.style.color = Color.white;
            hero.Add(heroTitle);
            hero.Add(UiKit.Spacer(6));
            var usage = sub?.Usage;
            string usageText = usage == null ? "Practice with a realistic AI interviewer." :
                $"{usage.InterviewsRemaining} of {usage.InterviewsLimit} interviews left this month";
            var heroBody = T(usageText, "body");
            heroBody.style.color = new Color(1f, 1f, 1f, 0.85f);
            hero.Add(heroBody);
            hero.Add(UiKit.Spacer(18));
            bool canStart = usage == null || usage.InterviewsRemaining > 0;
            var start = UiKit.Button(canStart ? "Start interview" : "Get more interviews", () =>
            {
                if (canStart) App.Nav.Push(new SetupScreen(App));
                else App.OpenSubscription();
            }, ButtonStyle.Secondary, canStart ? IconKind.Play : IconKind.Crown);
            start.style.backgroundColor = Color.white;
            start.Q<Label>(className: "btn__label").style.color = (Color)new Color32(0x1B, 0x23, 0x45, 0xFF);
            var startIcon = start.Q<Icon>();
            if (startIcon != null) startIcon.Color = new Color32(0x3F, 0x5E, 0xE8, 0xFF);
            hero.Add(start);
            _scroll.Add(hero);

            // Quick start
            _scroll.Add(SectionTitle("Quick start"));
            var chips = new VisualElement();
            chips.AddToClassList("row");
            chips.AddToClassList("row--wrap");
            foreach (var role in catalog.Roles)
            {
                if (role.Id == "custom") continue;
                var r = role;
                chips.Add(UiKit.Chip(r.Title, false, r.Locked, () =>
                {
                    var cfg = App.Settings.LastConfig;
                    cfg.RoleId = r.Id;
                    App.Settings.LastConfig = cfg;
                    App.Nav.Push(new SetupScreen(App));
                }));
            }
            _scroll.Add(chips);

            // Recent
            var recentHeader = UiKit.Row(SectionTitle("Recent interviews"), UiKit.Spacer(0, grow: true));
            if (recent.Total > 0)
            {
                var all = UiKit.Button("See all", () => App.OpenTab(TabId.History), ButtonStyle.Ghost, small: true);
                recentHeader.Add(all);
            }
            _scroll.Add(recentHeader);
            if (recent.Items.Count == 0)
            {
                var empty = UiKit.Card("card--flat");
                empty.Add(T("No interviews yet", "h3"));
                empty.Add(UiKit.Spacer(4));
                empty.Add(T("Your sessions and feedback reports will appear here.", "body"));
                _scroll.Add(empty);
            }
            else
            {
                foreach (var s in recent.Items) _scroll.Add(HistoryScreen.SessionItem(App, s));
            }

            // Tip
            var tip = UiKit.Card();
            tip.Add(UiKit.Row(new Icon(IconKind.Headphones, 20f, Palette.Accent), UiKit.HSpacer(10), T("Pro tip", "h3")));
            tip.Add(UiKit.Spacer(6));
            tip.Add(T("Use headphones for the most natural conversation — you can interrupt the interviewer just by speaking.",
                "body"));
            _scroll.Add(UiKit.Spacer(8));
            _scroll.Add(tip);
        }

        private static Label SectionTitle(string text)
        {
            var l = T(text, "h2");
            l.AddToClassList("section-title");
            return l;
        }

        private static string Greeting()
        {
            int h = DateTime.Now.Hour;
            return h < 12 ? "Good morning" : h < 18 ? "Good afternoon" : "Good evening";
        }
    }
}
