using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AIInterviewer.App
{
    /// <summary>Runs callbacks from background threads (WebSocket, tasks) on Unity's main thread.</summary>
    public sealed class MainThread : MonoBehaviour
    {
        private static MainThread _instance;
        private static int _mainThreadId;
        private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();

        public static bool IsMainThread => Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        public static void Initialize(GameObject host)
        {
            if (_instance != null) return;
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            _instance = host.AddComponent<MainThread>();
        }

        public static void Post(Action action)
        {
            if (action == null) return;
            if (_instance == null)
            {
                Debug.LogWarning("[MainThread] Not initialised; dropping callback.");
                return;
            }
            _instance._queue.Enqueue(action);
        }

        /// <summary>Fire-and-forget a task, logging exceptions instead of losing them.</summary>
        public static async void Run(Func<Task> work, string context = null)
        {
            try
            {
                await work();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{context ?? "Task"}] {ex}");
            }
        }

        private void Update()
        {
            int budget = 256; // keep frames smooth even if a burst arrives
            while (budget-- > 0 && _queue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }
    }
}
