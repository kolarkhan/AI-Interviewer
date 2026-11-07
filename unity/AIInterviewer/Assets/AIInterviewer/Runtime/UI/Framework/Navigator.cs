using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace AIInterviewer.UI.Framework
{
    public enum Transition
    {
        Slide,
        Up,
        Fade,
        None
    }

    /// <summary>Stack-based navigation with lightweight USS transitions.</summary>
    public sealed class Navigator
    {
        private const long ExitDurationMs = 280;
        private readonly VisualElement _layer;
        private readonly List<AppScreen> _stack = new List<AppScreen>();
        private static readonly string[] TransitionClasses =
            { "screen--enter", "screen--enter-up", "screen--enter-fade", "screen--exit" };

        public Navigator(VisualElement layer)
        {
            _layer = layer;
        }

        public AppScreen Current => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;
        public int Depth => _stack.Count;

        /// <summary>Raised after the visible screen changes.</summary>
        public event Action<AppScreen> CurrentChanged;

        /// <summary>Applied to each screen when shown (safe area etc.).</summary>
        public Action<AppScreen> Prepare;

        public void Push(AppScreen screen, Transition transition = Transition.Slide)
        {
            var previous = Current;
            _stack.Add(screen);
            Show(screen, transition);
            if (previous != null) Hide(previous, removeAfter: false);
            CurrentChanged?.Invoke(screen);
        }

        public void Replace(AppScreen screen, Transition transition = Transition.Fade)
        {
            var previous = Current;
            if (_stack.Count > 0) _stack.RemoveAt(_stack.Count - 1);
            _stack.Add(screen);
            Show(screen, transition);
            if (previous != null) Hide(previous, removeAfter: true);
            CurrentChanged?.Invoke(screen);
        }

        /// <summary>Clears the stack and shows <paramref name="screen"/> as the root.</summary>
        public void ResetTo(AppScreen screen, Transition transition = Transition.Fade)
        {
            var old = new List<AppScreen>(_stack);
            _stack.Clear();
            _stack.Add(screen);
            Show(screen, transition);
            foreach (var s in old)
            {
                if (s != screen) Hide(s, removeAfter: true);
            }
            CurrentChanged?.Invoke(screen);
        }

        public bool Pop()
        {
            if (_stack.Count <= 1) return false;
            var top = Current;
            _stack.RemoveAt(_stack.Count - 1);
            var below = Current;
            Show(below, Transition.Fade, reveal: true);
            Hide(top, removeAfter: true, slideOut: true);
            CurrentChanged?.Invoke(below);
            return true;
        }

        /// <summary>Pops screens until one of type T is on top (or the root is reached).</summary>
        public void PopTo<T>() where T : AppScreen
        {
            while (_stack.Count > 1 && !(Current is T))
            {
                var top = Current;
                _stack.RemoveAt(_stack.Count - 1);
                Hide(top, removeAfter: true);
            }
            Show(Current, Transition.Fade, reveal: true);
            CurrentChanged?.Invoke(Current);
        }

        public bool HandleBack()
        {
            var top = Current;
            if (top == null) return false;
            if (top.OnBack()) return true;
            return Pop();
        }

        private void Show(AppScreen screen, Transition transition, bool reveal = false)
        {
            screen.EnsureBuilt();
            Prepare?.Invoke(screen);
            // Invalidates any hide still pending for this screen (e.g. the user came back to a cached tab
            // before its exit animation finished), so it can't remove or hide the now-visible screen.
            screen.NavVersion++;
            var root = screen.Root;
            root.style.display = DisplayStyle.Flex;
            root.pickingMode = PickingMode.Position;
            foreach (var c in TransitionClasses) root.RemoveFromClassList(c);
            if (root.parent != _layer) _layer.Add(root);
            root.BringToFront();
            string cls = transition == Transition.Slide ? "screen--enter" :
                transition == Transition.Up ? "screen--enter-up" :
                transition == Transition.Fade ? "screen--enter-fade" : null;
            if (cls != null && !reveal)
            {
                root.AddToClassList(cls);
                // Remove next frame so the USS transition animates to the resting state.
                root.schedule.Execute(() => root.RemoveFromClassList(cls)).StartingIn(16);
            }
            screen.OnShow();
        }

        private void Hide(AppScreen screen, bool removeAfter, bool slideOut = false)
        {
            screen.OnHide();
            int version = ++screen.NavVersion;
            var root = screen.Root;
            root.pickingMode = PickingMode.Ignore;
            root.AddToClassList(slideOut ? "screen--enter" : "screen--exit");
            // Runs after the exit transition. The exit class stays on while the screen is hidden: removing it
            // here would start a new transition right before the element is detached, and a transition
            // interrupted by detaching can leave the screen invisible (opacity 0) when it is shown again.
            // Show() clears the classes instead.
            root.schedule.Execute(() =>
            {
                if (screen.NavVersion != version || _stack.Contains(screen) && Current == screen) return;
                if (removeAfter) root.RemoveFromHierarchy();
                else root.style.display = DisplayStyle.None;
            }).StartingIn(ExitDurationMs + 40);
        }
    }
}
