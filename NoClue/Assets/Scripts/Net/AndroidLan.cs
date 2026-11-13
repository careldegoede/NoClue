using UnityEngine;

namespace NoClue.Net
{
    /// <summary>
    /// Android Wi-Fi drops UDP broadcast/multicast packets unless the app holds a WifiManager.MulticastLock
    /// (needs CHANGE_WIFI_MULTICAST_STATE, added by our AndroidManifest). Without it, JOIN never hears any
    /// hosted games. Hold the lock only while it's needed (browsing or hosting) to save battery.
    /// No-op on every other platform.
    /// </summary>
    public static class AndroidLan
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaObject s_Lock;
        private static int s_Users;
#endif

        public static void Acquire()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            s_Users++;
            if (s_Lock != null) return;
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var wifi = activity.Call<AndroidJavaObject>("getSystemService", "wifi");
                s_Lock = wifi.Call<AndroidJavaObject>("createMulticastLock", "NoClueLan");
                s_Lock.Call("setReferenceCounted", false);
                s_Lock.Call("acquire");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[NoClue] Could not take the Wi-Fi multicast lock; LAN discovery may not work: " + e.Message);
                s_Lock = null;
            }
#endif
        }

        public static void Release()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (s_Users > 0) s_Users--;
            if (s_Users > 0 || s_Lock == null) return;
            try { s_Lock.Call("release"); } catch { /* already released */ }
            s_Lock.Dispose();
            s_Lock = null;
#endif
        }
    }
}
