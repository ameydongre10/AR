using System;
using System.Collections.Generic;
using UnityEngine;

namespace ARLab.Core
{
    /// <summary>
    /// Process-wide, allocation-free publish/subscribe hub.
    /// Handlers are stored per event type in a single slot so subscribe/unsubscribe
    /// never allocate and dispatch never boxes.
    /// </summary>
    public static class EventBus
    {
        private static readonly Dictionary<Type, Delegate> Handlers = new Dictionary<Type, Delegate>(32);

        public static void Subscribe<T>(Action<T> handler)
        {
            if (handler == null) return;
            Type t = typeof(T);
            if (Handlers.TryGetValue(t, out Delegate existing) && existing != null)
                Handlers[t] = Delegate.Combine(existing, handler);
            else
                Handlers[t] = handler;
        }

        public static void Unsubscribe<T>(Action<T> handler)
        {
            if (handler == null) return;
            Type t = typeof(T);
            if (Handlers.TryGetValue(t, out Delegate existing) && existing != null)
                Handlers[t] = Delegate.Remove(existing, handler);
        }

        /// <summary>Dispatches to subscribers. Exceptions are caught per-subscriber chain so one
        /// bad listener cannot abort the remaining listeners.</summary>
        public static void Publish<T>(T evt)
        {
            Type t = typeof(T);
            if (!Handlers.TryGetValue(t, out Delegate d) || d == null) return;
            try
            {
                ((Action<T>)d).Invoke(evt);
            }
            catch (Exception e)
            {
                Debug.LogError($"[EventBus] Handler for {t.Name} threw: {e}");
            }
        }

        /// <summary>Drops every subscription. Used by tests and on full teardown.</summary>
        public static void Clear()
        {
            Handlers.Clear();
        }
    }
}
