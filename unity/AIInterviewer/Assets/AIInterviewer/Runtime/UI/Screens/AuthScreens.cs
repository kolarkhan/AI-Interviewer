using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AIInterviewer.App;
using AIInterviewer.Core.Api;
using AIInterviewer.UI.Framework;
using AIInterviewer.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIInterviewer.UI.Screens
{
    internal static class AuthValidation
    {
        private static readonly Regex Email = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");

        public static string EmailError(string email) =>
            string.IsNullOrWhiteSpace(email) ? "Enter your email" : !Email.IsMatch(email.Trim()) ? "Enter a valid email" : null;

        public static string NewPasswordError(string password)
        {
            if (string.IsNullOrEmpty(password) || password.Length < 10) return "Use at least 10 characters";
            bool letter = false, other = false;
            foreach (char c in password)
            {
                if (char.IsLetter(c)) letter = true;
                else other = true;
            }
            return letter && other ? null : "Include letters and at least one number or symbol";
        }
    }

    public sealed class LoginScreen : AppScreen
    {
        private TextField _email, _password;
        private Label _emailErr, _passwordErr;
        private VisualElement _errorHost;
        private Button _submit;

        public LoginScreen(AppServices app) : base(app)
        {
        }

        protected override void Build()
        {
            var scroll = UiKit.Scroll();
            Content.Add(scroll);
            scroll.Add(UiKit.Spacer(48));
            var logo = Brand.LogoMark(64);
            scroll.Add(logo);
            scroll.Add(UiKit.Spacer(24));
            scroll.Add(T("Welcome back", "display"));
            scroll.Add(UiKit.Spacer(6));
            scroll.Add(T("Sign in to continue your interview practice.", "body"));
            scroll.Add(UiKit.Spacer(28));

            _errorHost = new VisualElement();
            scroll.Add(_errorHost);

            var email = UiKit.Field("Email", "you@example.com", keyboard: TouchScreenKeyboardType.EmailAddress);
            (_email, _emailErr) = (email.input, email.error);
            scroll.Add(email.container);
            var pass = UiKit.Field("Password", "Your password", password: true);
            (_password, _passwordErr) = (pass.input, pass.error);
            scroll.Add(pass.container);

            scroll.Add(UiKit.Spacer(8));
            _submit = UiKit.Button("Sign in", () => MainThread.Run(SubmitAsync, "Login"));
            _submit.AddToClassList("btn--block");
            scroll.Add(_submit);
            scroll.Add(UiKit.Spacer(12));
            var create = UiKit.Button("New here? Create an account", () => App.Nav.ResetTo(new SignUpScreen(App), Transition.Slide),
                ButtonStyle.Ghost);
            scroll.Add(create);
        }

        private async Task SubmitAsync()
        {
            _errorHost.Clear();
            string emailErr = AuthValidation.EmailError(_email.value);
            string passErr = string.IsNullOrEmpty(_password.value) ? "Enter your password" : null;
            UiKit.SetFieldError(_email, _emailErr, emailErr);
            UiKit.SetFieldError(_password, _passwordErr, passErr);
            if (emailErr != null || passErr != null) return;

            var restore = UiKit.SetLoading(_submit, "Signing in…");
            try
            {
                await App.Auth.LoginAsync(_email.value.Trim(), _password.value);
                App.OnSignedIn();
            }
            catch (ApiException ex)
            {
                restore();
                if (ex.Kind == ApiFailureKind.Unauthorized || ex.Kind == ApiFailureKind.RateLimited ||
                    ex.Kind == ApiFailureKind.Validation)
                {
                    _errorHost.Add(UiKit.Banner(ex.Kind == ApiFailureKind.Validation ? "Check your email and password." : ex.Message,
                        "danger", IconKind.Warning));
                }
                else
                {
                    App.HandleApiError(ex, "login");
                }
            }
        }
    }

    public sealed class SignUpScreen : AppScreen
    {
        private TextField _name, _email, _password;
        private Label _nameErr, _emailErr, _passwordErr;
        private Switch _terms;
        private VisualElement _errorHost;
        private Button _submit;

        public SignUpScreen(AppServices app) : base(app)
        {
        }

        protected override void Build()
        {
            var scroll = UiKit.Scroll();
            Content.Add(scroll);
            scroll.Add(UiKit.Spacer(40));
            scroll.Add(Brand.LogoMark(64));
            scroll.Add(UiKit.Spacer(24));
            scroll.Add(T("Create your account", "display"));
            scroll.Add(UiKit.Spacer(6));
            scroll.Add(T("Start with 3 free practice interviews every month.", "body"));
            scroll.Add(UiKit.Spacer(24));

            _errorHost = new VisualElement();
            scroll.Add(_errorHost);

            var name = UiKit.Field("Name", "How should the interviewer call you?");
            (_name, _nameErr) = (name.input, name.error);
            _name.maxLength = 60;
            scroll.Add(name.container);
            var email = UiKit.Field("Email", "you@example.com", keyboard: TouchScreenKeyboardType.EmailAddress);
            (_email, _emailErr) = (email.input, email.error);
            scroll.Add(email.container);
            var pass = UiKit.Field("Password", "At least 10 characters", password: true);
            (_password, _passwordErr) = (pass.input, pass.error);
            scroll.Add(pass.container);

            _terms = new Switch(false, null);
            var termsText = T("I agree to the Terms of Service and Privacy Policy", "body");
            termsText.style.flexShrink = 1;
            termsText.style.marginLeft = 12;
            termsText.style.fontSize = 13;
            var termsRow = UiKit.Row(_terms, termsText);
            termsRow.style.marginTop = 4;
            scroll.Add(termsRow);
            var links = UiKit.Row(
                UiKit.Button("Terms", () => Application.OpenURL(App.Config.TermsUrl), ButtonStyle.Ghost, small: true),
                UiKit.Button("Privacy", () => Application.OpenURL(App.Config.PrivacyPolicyUrl), ButtonStyle.Ghost, small: true));
            scroll.Add(links);

            scroll.Add(UiKit.Spacer(12));
            _submit = UiKit.Button("Create account", () => MainThread.Run(SubmitAsync, "SignUp"));
            _submit.AddToClassList("btn--block");
            scroll.Add(_submit);
            scroll.Add(UiKit.Spacer(12));
            scroll.Add(UiKit.Button("Already have an account? Sign in", () => App.Nav.ResetTo(new LoginScreen(App), Transition.Slide),
                ButtonStyle.Ghost));
        }

        private async Task SubmitAsync()
        {
            _errorHost.Clear();
            string nameErr = string.IsNullOrWhiteSpace(_name.value) ? "Enter your name" : null;
            string emailErr = AuthValidation.EmailError(_email.value);
            string passErr = AuthValidation.NewPasswordError(_password.value);
            UiKit.SetFieldError(_name, _nameErr, nameErr);
            UiKit.SetFieldError(_email, _emailErr, emailErr);
            UiKit.SetFieldError(_password, _passwordErr, passErr);
            if (nameErr != null || emailErr != null || passErr != null) return;
            if (!_terms.Value)
            {
                _errorHost.Add(UiKit.Banner("Please accept the Terms and Privacy Policy to continue.", "warning", IconKind.Warning));
                return;
            }

            var restore = UiKit.SetLoading(_submit, "Creating account…");
            try
            {
                await App.Auth.RegisterAsync(_email.value.Trim(), _password.value, _name.value.Trim());
                App.OnSignedIn();
                App.Overlay.Success("Welcome! Your account is ready.");
            }
            catch (ApiException ex)
            {
                restore();
                if (ex.Code == "email_taken")
                {
                    UiKit.SetFieldError(_email, _emailErr, "An account with this email already exists");
                }
                else if (ex.Kind == ApiFailureKind.Validation || ex.Kind == ApiFailureKind.RateLimited)
                {
                    _errorHost.Add(UiKit.Banner(ex.Kind == ApiFailureKind.RateLimited ? ex.Message :
                        "Please check the highlighted details.", "danger", IconKind.Warning));
                }
                else
                {
                    App.HandleApiError(ex, "register");
                }
            }
        }
    }
}
