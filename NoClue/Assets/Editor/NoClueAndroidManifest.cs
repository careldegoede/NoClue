#if UNITY_ANDROID
using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace NoClue.EditorTools
{
    /// <summary>
    /// Adds the network permissions NoClue needs to the generated Android manifest:
    /// INTERNET + network/Wi-Fi state, and CHANGE_WIFI_MULTICAST_STATE so LAN discovery
    /// can hold a multicast lock (see AndroidLan). Runs automatically on every Android build.
    /// </summary>
    public sealed class NoClueAndroidManifest : IPostGenerateGradleAndroidProject
    {
        private const string AndroidNs = "http://schemas.android.com/apk/res/android";

        private static readonly string[] Permissions =
        {
            "android.permission.INTERNET",
            "android.permission.ACCESS_NETWORK_STATE",
            "android.permission.ACCESS_WIFI_STATE",
            "android.permission.CHANGE_WIFI_MULTICAST_STATE",
        };

        public int callbackOrder => 0;

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            string path = Path.Combine(unityLibraryPath, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(path))
            {
                Debug.LogWarning("[NoClue] AndroidManifest.xml not found at " + path);
                return;
            }

            var doc = new XmlDocument();
            doc.Load(path);
            var manifest = doc.DocumentElement;
            if (manifest == null) return;

            foreach (var permission in Permissions)
            {
                bool present = false;
                foreach (XmlNode node in manifest.SelectNodes("uses-permission"))
                    if (node.Attributes?["name", AndroidNs]?.Value == permission) { present = true; break; }
                if (present) continue;

                var el = doc.CreateElement("uses-permission");
                el.SetAttribute("name", AndroidNs, permission);
                manifest.PrependChild(el);
            }
            doc.Save(path);
        }
    }
}
#endif
