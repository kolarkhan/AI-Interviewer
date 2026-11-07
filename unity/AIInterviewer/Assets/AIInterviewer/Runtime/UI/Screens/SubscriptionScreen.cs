using System.Threading.Tasks;
using AIInterviewer.App;
using AIInterviewer.Core.Api;
using AIInterviewer.UI.Framework;
using AIInterviewer.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIInterviewer.UI.Screens
{
    /// <summary>
    /// Plans and usage. In-app purchases are NOT integrated yet: the upgrade button explains this honestly and,
    /// in development builds only, offers a test grant. See docs/MONETIZATION.md for the Play/App Store plan.
    /// </summary>
    public sealed class SubscriptionScreen : AppScreen
    {
        private ScrollView _scroll;

        public SubscriptionScreen(AppServices app) : base(app)
        {
        }

        protected override void Build()
        {
            Content.Add(UiKit.TopBar("Plans", () => App.Nav.Pop()));
            _scroll = UiKit.Scroll();
            Content.Add(_scroll);
            _scroll.Add(UiKit.Spacer(40));
            _scroll.Add(new Spinner(30f));
        }

        public override void OnShow()
        {
            MainThread.Run(LoadAsync, "Plans");
        }

        private async Task LoadAsync()
        {
            PlanList plans;
            SubscriptionResponse sub;
            try
            {
                var plansTask = App.Api.GetPlansAsync();
                var subTask = App.GetSubscriptionAsync(refresh: true);
                await Task.WhenAll(plansTask, subTask);
                plans = plansTask.Result;
                sub = subTask.Result;
            }
            catch (ApiException ex)
            {
                _scroll.Clear();
                _scroll.Add(UiKit.StatusView(IconKind.WifiOff, "Couldn't load plans", ex.Message, "Retry",
                    () => MainThread.Run(LoadAsync, "Plans.Retry"), iconColor: Palette.Warning));
                return;
            }
            Render(plans, sub);
        }

        private void Render(PlanList plans, SubscriptionResponse sub)
        {
            _scroll.Clear();
            var hero = UiKit.Column(new Icon(IconKind.Crown, 40f, Palette.Gold), UiKit.Spacer(10),
                T("Prepare like it's the real thing", "title"), UiKit.Spacer(6),
                T("Unlimited-feeling practice, deeper feedback and progress tracking.", "body"));
            hero.style.alignItems = Align.Center;
            foreach (var child in hero.Children()) child.style.unityTextAlign = TextAnchor.MiddleCenter;
            hero.style.marginBottom = 20;
            _scroll.Add(hero);

            if (sub?.Usage != null)
            {
                var usage = UiKit.Card();
                usage.Add(T("This month", "overline"));
                usage.Add(UiKit.Spacer(8));
                usage.Add(UiKit.Row(T("Interviews", "body"), UiKit.Spacer(0, grow: true),
                    T($"{sub.Usage.InterviewsUsed} / {sub.Usage.InterviewsLimit}", "h3")));
                usage.Add(UiKit.Spacer(6));
                usage.Add(new ProgressTrack(sub.Usage.InterviewsLimit == 0 ? 0 : sub.Usage.InterviewsUsed / (float)sub.Usage.InterviewsLimit));
                usage.Add(UiKit.Spacer(12));
                usage.Add(UiKit.Row(T("Voice minutes", "body"), UiKit.Spacer(0, grow: true),
                    T($"{Mathf.RoundToInt(sub.Usage.RealtimeMinutesUsed)} / {sub.Usage.RealtimeMinutesLimit}", "h3")));
                usage.Add(UiKit.Spacer(6));
                usage.Add(new ProgressTrack(sub.Usage.RealtimeMinutesLimit == 0 ? 0 :
                    sub.Usage.RealtimeMinutesUsed / sub.Usage.RealtimeMinutesLimit));
                _scroll.Add(usage);
            }

            string current = sub?.Plan?.Id ?? "free";
            foreach (var plan in plans.Plans)
            {
                bool isPremium = plan.Id == "premium";
                bool isCurrent = plan.Id == current;
                var card = UiKit.Card(isPremium ? "card--accent" : null);
                var head = UiKit.Row(T(plan.Title, "h2"), UiKit.Spacer(0, grow: true));
                if (isCurrent) head.Add(UiKit.Badge("CURRENT", "success"));
                else if (isPremium) head.Add(UiKit.Badge("RECOMMENDED", "gold", IconKind.Crown));
                card.Add(head);
                card.Add(UiKit.Spacer(10));
                foreach (var f in plan.Features) card.Add(UiKit.Bullet(f, IconKind.Check, isPremium ? Palette.Gold : Palette.Success));
                if (isPremium && !isCurrent)
                {
                    card.Add(UiKit.Spacer(8));
                    var buy = UiKit.Button("Upgrade to Premium", Upgrade, ButtonStyle.Gold, IconKind.Crown);
                    buy.AddToClassList("btn--block");
                    card.Add(buy);
                }
                if (isPremium && isCurrent && sub?.Subscription?.IsTestGrant == true)
                {
                    card.Add(UiKit.Spacer(6));
                    card.Add(T("Test grant (development) — no payment was taken.", "caption"));
                }
                _scroll.Add(card);
            }

            var legal = T("Subscriptions will be billed through Google Play or the App Store and renew automatically until " +
                          "cancelled in your store account settings.", "caption");
            legal.style.marginTop = 8;
            _scroll.Add(legal);
        }

        private void Upgrade()
        {
            var actions = new System.Collections.Generic.List<DialogAction>();
            if (!App.Config.IsProduction)
            {
                actions.Add(new DialogAction("Activate test Premium (dev)", ButtonStyle.Gold,
                    () => MainThread.Run(GrantTestAsync, "Plans.DevGrant")));
            }
            actions.Add(new DialogAction("OK", ButtonStyle.Secondary));
            App.Overlay.Dialog("In-app purchases coming soon",
                "Google Play Billing and Apple In-App Purchase are not yet integrated in this build, so no payment can be taken.",
                actions);
        }

        private async Task GrantTestAsync()
        {
            if (await Guard(App.Api.DevGrantPremiumAsync(), "dev premium") == null) return;
            App.InvalidatePlanCaches();
            App.Overlay.Success("Test Premium activated for 30 days");
            await LoadAsync();
        }
    }
}
