using System;
using System.Collections.Generic;

namespace ChordPlayer.Core
{
    /// <summary>
    /// Lightweight service registry used instead of GameObject.Find /
    /// FindObjectOfType at runtime. Services register themselves in
    /// OnEnable and unregister in OnDisable; everything else resolves
    /// through TryGet / Get.
    /// </summary>
    public static class ServiceLocator
    {
        static readonly Dictionary<Type, object> Services = new();

        public static void Register<T>(T service) where T : class
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            Services[typeof(T)] = service;
        }

        public static void Unregister<T>() where T : class
        {
            Services.Remove(typeof(T));
        }

        public static bool TryGet<T>(out T service) where T : class
        {
            if (Services.TryGetValue(typeof(T), out var obj))
            {
                service = (T)obj;
                return true;
            }
            service = null;
            return false;
        }

        public static T Get<T>() where T : class
        {
            if (TryGet<T>(out var service)) return service;
            throw new InvalidOperationException(
                $"ServiceLocator: no service registered for '{typeof(T).Name}'.");
        }

        /// <summary>Clears everything. Call when leaving play mode if domain reload is disabled.</summary>
        public static void Clear() => Services.Clear();
    }
}
