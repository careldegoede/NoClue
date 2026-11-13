using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace NoClue.EditorTools
{
    /// <summary>
    /// One-click Android setup and build for NoClue.
    /// Menu: NoClue > Android > Apply Settings / Build APK.
    /// </summary>
    public static class NoClueAndroidSetup
    {
        public const string PackageId = "com.careldegoede.noclue";
        public const string ApkPath = "Builds/Android/NoClue.apk";

        [MenuItem("NoClue/Android/Apply Settings")]
        public static void ApplySettings()
        {
            var android = NamedBuildTarget.Android;

            PlayerSettings.productName = "NoClue";
            PlayerSettings.SetApplicationIdentifier(android, PackageId);
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;   // required by Google Play
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26; // Android 8.0+
            PlayerSettings.Android.forceInternetPermission = true;                     // TCP/UDP on the LAN

            // portrait only: held in one hand like any phone game
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;

            PlayerSettings.runInBackground = true;
            AssetDatabase.SaveAssets();
            Debug.Log($"[NoClue] Android settings applied ({PackageId}, IL2CPP, ARM64, API 26+, portrait).");
        }

        [MenuItem("NoClue/Android/Build APK")]
        public static void BuildApk()
        {
            ApplySettings();
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0) scenes = new[] { "Assets/Scenes/SampleScene.unity" };
            Directory.CreateDirectory(Path.GetDirectoryName(ApkPath)!);

            EditorUserBuildSettings.buildAppBundle = false;   // a plain .apk we can sideload
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });
            Debug.Log($"[NoClue] Android build: {report.summary.result}, {report.summary.totalSize / (1024 * 1024)} MB, " +
                      $"{report.summary.totalErrors} errors -> {Path.GetFullPath(ApkPath)}");
        }
    }
}
