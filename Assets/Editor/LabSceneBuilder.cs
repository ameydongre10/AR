using System;
using System.IO;
using System.Linq;
using ARLab.Laboratory;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
// The settings container is editor-side, the settings object itself is runtime, and the
// loader metadata helpers sit under their own namespace.
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine.XR.Management;

namespace ARLab.EditorTools
{
    /// <summary>
    /// Generates the playable scene. The scene only ever contains a single GameObject with a
    /// GameManager: everything else is assembled at runtime by the same code path, so there is
    /// no second, editor-only version of the scene to keep in sync.
    /// </summary>
    public static class LabSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("AR Lab/Rebuild Main Scene", priority = 0)]
        public static void RebuildMainScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("ARLab");
            root.AddComponent<GameManager>();
            var manager = root.GetComponent<GameManager>();

            // Building here is the validation step: it proves the graph assembles from an
            // empty scene, and it is the same call play mode makes from Awake.
            manager.BuildIfNeeded();
            VerifyGraphIsComplete(manager);

            // Everything just built is regenerated at runtime, so it must not be serialized.
            // Keeping it would bloat the scene to megabytes and freeze a stale snapshot of
            // the hierarchy that no longer matches the code.
            foreach (Transform child in root.transform.Cast<Transform>().ToList())
                LabMaterials.Discard(child.gameObject);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log($"[LabSceneBuilder] Wrote {ScenePath} with a single GameManager entry point.");
        }

        /// <summary>
        /// Fails loudly if assembly did not produce a usable graph. A scene generator that
        /// silently saves a broken scene is worse than no generator at all.
        /// </summary>
        private static void VerifyGraphIsComplete(GameManager manager)
        {
            if (manager.Laboratory == null)
                throw new InvalidOperationException("Scene assembly produced no LaboratoryManager.");
            if (manager.Laboratory.Breadboard == null)
                throw new InvalidOperationException("Scene assembly produced no Breadboard.");
            if (manager.Laboratory.LabRoot == null)
                throw new InvalidOperationException("Scene assembly produced no LabRoot.");
            if (manager.Placement == null)
                throw new InvalidOperationException("Scene assembly produced no ARPlacementManager.");

            var hud = manager.GetComponentInChildren<ARLab.UI.LabHud>(true);
            if (hud == null || !hud.BuiltUi)
                throw new InvalidOperationException("Scene assembly did not build the HUD.");
        }

        /// <summary>
        /// Makes the generated scene the one that opens with the project and the one the play
        /// button uses, so a fresh clone is runnable with no manual steps.
        /// </summary>
        [MenuItem("AR Lab/Set As Startup Scene", priority = 1)]
        public static void SetAsStartupScene()
        {
            if (!File.Exists(ScenePath))
            {
                Debug.LogWarning($"[LabSceneBuilder] {ScenePath} does not exist yet. Rebuild the scene first.");
                return;
            }

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log($"[LabSceneBuilder] {ScenePath} is now the startup scene.");
        }

        /// <summary>
        /// Assigns the ARCore loader for Android. Without this the app builds and installs but
        /// no session subsystem is ever created, so the AR layer stays dead on the device.
        /// </summary>
        [MenuItem("AR Lab/Configure ARCore Loader", priority = 10)]
        public static void ConfigureArCoreLoader()
        {
            // A fresh clone has no per-build-target XR settings asset at all, so it is created
            // before the loader can be assigned to it.
            EnsureXrSettingsAssets();

            var settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(
                BuildTargetGroup.Android);
            if (settings == null)
            {
                Debug.LogError("[LabSceneBuilder] Could not obtain XR settings for Android.");
                return;
            }

            bool assigned = XRPackageMetadataStore.AssignLoader(settings.Manager,
                "Unity.XR.ARCore.ARCoreLoader", BuildTargetGroup.Android);
            settings.InitManagerOnStart = true;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            Debug.Log(assigned
                ? "[LabSceneBuilder] ARCore loader assigned for Android."
                : "[LabSceneBuilder] ARCore loader was already assigned for Android.");
        }

        /// <summary>
        /// Creates the per-build-target XR settings container if the project has none. These
        /// assets are normally written by the XR Plug-in Management UI, so a clone that has
        /// never been opened through that UI would otherwise build with no loader at all.
        /// </summary>
        private static void EnsureXrSettingsAssets()
        {
            const string folder = "Assets/XR/Settings";
            const string containerPath = folder + "/XRGeneralSettingsPerBuildTarget.asset";

            if (!AssetDatabase.IsValidFolder("Assets/XR"))
                AssetDatabase.CreateFolder("Assets", "XR");
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets/XR", "Settings");

            // The lookup goes through the config object first, then an asset search, so the
            // asset has to exist and be registered in both places.
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,
                    out XRGeneralSettingsPerBuildTarget container)
                || container == null)
            {
                container = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(containerPath);
                if (container == null)
                {
                    container = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                    AssetDatabase.CreateAsset(container, containerPath);
                }
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, container, true);
            }

            // Standalone is included so the same project also runs in the editor simulator.
            foreach (BuildTargetGroup group in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
            {
                if (!container.HasSettingsForBuildTarget(group))
                    container.CreateDefaultSettingsForBuildTarget(group);

                var settings = container.SettingsForBuildTarget(group);
                if (settings == null) continue;

                // CreateDefaultSettingsForBuildTarget leaves the manager unassigned, and every
                // loader call dereferences it, so the manager is created here.
                if (settings.Manager == null)
                {
                    var manager = ScriptableObject.CreateInstance<XRManagerSettings>();
                    manager.name = $"{group} Providers";
                    manager.automaticLoading = true;
                    manager.automaticRunning = true;
                    AssetDatabase.AddObjectToAsset(manager, containerPath);
                    settings.Manager = manager;
                }

                EditorUtility.SetDirty(settings);
            }

            EditorUtility.SetDirty(container);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [MenuItem("AR Lab/Configure Android Player Settings", priority = 20)]
        public static void ConfigureAndroidPlayerSettings()
        {
            PlayerSettings.companyName = "ARLab";
            PlayerSettings.productName = "AR Digital Electronics Lab";
            PlayerSettings.bundleVersion = "1.0.0";

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Low);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                new[] { UnityEngine.Rendering.GraphicsDeviceType.Vulkan, UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });

            // ARCore needs API 25+, and Unity 6 refuses anything lower.
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.forceInternetPermission = false;
            PlayerSettings.Android.startInFullscreen = true;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.arlab.digitalelectronics");

            // Release builds must not ship a debug keystore, so the project's own signing
            // setup (or CI) is what signs the APK.
            PlayerSettings.Android.blitType = AndroidBlitType.Never;
            PlayerSettings.SetMobileMTRendering(NamedBuildTarget.Android, true);

            // The ARCore AARs - including Google's own com.google.ar.core client - do not
            // declare the camera permission, so the app supplies it through an .androidlib
            // module, which Gradle merges without needing a Player Settings toggle. Warn if it
            // is missing, because the failure mode is a black camera on a real device with no
            // error anywhere in the editor.
            const string arManifest = "Assets/Plugins/Android/ARPermissions.androidlib/AndroidManifest.xml";
            if (!File.Exists(arManifest))
            {
                Debug.LogWarning(
                    $"[LabSceneBuilder] {arManifest} is missing; the APK would ship without the " +
                    "CAMERA permission and AR would not start on a real device.");
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[LabSceneBuilder] Android player settings applied (ARM64, IL2CPP, API 25+).");
        }
    }
}
