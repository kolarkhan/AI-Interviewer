using System.Threading.Tasks;
using AIInterviewer.App;
using AIInterviewer.Core.Api;
using AIInterviewer.UI.Framework;
using AIInterviewer.UI.Branding;
using AIInterviewer.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIInterviewer.UI.Screens
{
    public static class Brand
    {
        public const string AppName = "Avintera";
        public const string Tagline = "Practice real interviews with an AI coach";

        /// <summary>The Avintera mark (head + voice bars) on the brand's dark tile.</summary>
        public static VisualElement LogoMark(float size = 88f)
        {
            var tile = RoundedTile(size);
            tile.style.backgroundColor = AvinteraArtwork.Background;
            tile.style.borderTopWidth = 1;
            tile.style.borderBottomWidth = 1;
            tile.style.borderLeftWidth = 1;
            tile.style.borderRightWidth = 1;
            var edge = new Color(0.21f, 0.84f, 0.91f, 0.25f);
            tile.style.borderTopColor = edge;
            tile.style.borderBottomColor = edge;
            tile.style.borderLeftColor = edge;
            tile.style.borderRightColor = edge;
            tile.Add(new AvinteraMark(size));
            return tile;
        }

        /// <summary>Gradient tile for feature icons (onboarding).</summary>
        public static VisualElement IconBadge(float size, IconKind icon)
        {
            var tile = RoundedTile(size);
            UiKit.HeroGradient(tile);
            tile.Add(new Icon(icon, size * 0.48f, Color.white));
            return tile;
        }

        private static VisualElement RoundedTile(float size)
        {
            var tile = new VisualElement();
            tile.style.width = size;
            tile.style.height = size;
            float radius = size * 0.28f;
            tile.style.borderTopLeftRadius = radius;
            tile.style.borderTopRightRadius = radius;
            tile.style.borderBottomLeftRadius = radius;
            tile.style.borderBottomRightRadius = radius;
            tile.style.overflow = Overflow.Hidden;
            tile.style.alignItems = Align.Center;
            tile.style.justifyContent = Justify.Center;
            tile.style.flexShrink = 0;
            return tile;
        }
    }

    /// <summary>Animated Avintera splash (vector port of Branding/avintera-splash-animated.svg).</summary>
    public sealed class SplashScreen : AppScreen
    {
        private AvinteraSplashArt _art;

        public SplashScreen(AppServices app) : base(app)
        {
        }

        protected override void Build()
        {
            Root.style.backgroundColor = AvinteraArtwork.Background;
            _art = new AvinteraSplashArt();
            Root.Insert(0, _art);
        }

        public override void OnShow()
        {
            _art.Play();
            MainThread.Run(RouteAsync, "Splash");
        }

        private async Task RouteAsync()
        {
            float started = Time.realtimeSinceStartup;
            bool restored = false;
            try
            {
                restored = await App.Auth.TryRestoreAsync();
            }
            catch (ApiException ex) when (ex.Kind == ApiFailureKind.Network || ex.Kind == ApiFailureKind.Server)
            {
                // Server unreachable: keep the stored session and show an offline-friendly screen.
                Debug.LogWarning("[Splash] Could not restore session: " + ex.Message);
            }
            // Let the brand animation finish (plus a short hold) before moving on.
            float remaining = AvinteraSplashArt.Duration + 0.45f - (Time.realtimeSinceStartup - started);
            if (remaining > 0f) await Task.Delay((int)(remaining * 1000));

            if (restored)
            {
                App.OnSignedIn();
            }
            else if (App.Auth.HasStoredSession)
            {
                App.Nav.ResetTo(new ConnectionProblemScreen(App), Transition.Fade);
            }
            else if (!App.Settings.OnboardingSeen)
            {
                App.Nav.ResetTo(new OnboardingScreen(App), Transition.Fade);
            }
            else
            {
                App.Nav.ResetTo(new LoginScreen(App), Transition.Fade);
            }
        }
    }

    /// <summary>Shown when a stored session exists but the server cannot be reached at launch.</summary>
    public sealed class ConnectionProblemScreen : AppScreen
    {
        public ConnectionProblemScreen(AppServices app) : base(app)
        {
        }

        protected override void Build()
        {
            Content.AddToClassList("center");
            Content.Add(UiKit.StatusView(IconKind.WifiOff, "Can't reach " + Brand.AppName,
                App.Network.IsOnline
                    ? "Our servers aren't responding right now. Please try again in a moment."
                    : "You're offline. Connect to the internet to continue practising.",
                "Try again", () => App.Nav.ResetTo(new SplashScreen(App), Transition.Fade),
                iconColor: Palette.Warning));
        }
    }
}
