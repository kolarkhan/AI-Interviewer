using System;
using UnityEngine;

namespace AIInterviewer.Platform
{
    /// <summary>
    /// Polls OS reachability. Note: reachability only says a network interface is up; real
    /// connectivity is confirmed by API calls (which surface <c>ApiFailureKind.Network</c>).
    /// </summary>
    public sealed class NetworkMonitor : MonoBehaviour
    {
        private float _next;

        public bool IsOnline { get; private set; } = true;
        public bool IsCellular { get; private set; }

        public event Action<bool> OnlineChanged;

        private void Awake()
        {
            Sample(force: true);
        }

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;
            Sample(force: false);
        }

        private void Sample(bool force)
        {
            var reach = Application.internetReachability;
            bool online = reach != NetworkReachability.NotReachable;
            IsCellular = reach == NetworkReachability.ReachableViaCarrierDataNetwork;
            if (online != IsOnline || force)
            {
                IsOnline = online;
                if (!force) OnlineChanged?.Invoke(online);
            }
        }
    }
}
