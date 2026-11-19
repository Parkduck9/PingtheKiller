using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

// Global class name is intentional: Unity -executeMethod BuildScript.BuildWindows.
// Assets are created only on build or from the menu, never on editor load.
public static class BuildScript
{
    private const string ScenePath = "Assets/Scenes/Client.unity";

    [MenuItem("Ping the Killer/Ensure Build Assets")]
    public static void EnsureAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Directory.CreateDirectory("Assets/Resources");
        if (AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/WorldLit.mat") == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP/Lit is required. Keep the existing URP 17.3 pipeline.");
            AssetDatabase.CreateAsset(new Material(shader), "Assets/Resources/WorldLit.mat");
        }
        if (AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/WorldLitFade.mat") == null)
        {
            var fade = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/WorldLit.mat"));
            fade.SetFloat("_Surface", 1);
            fade.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            fade.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            fade.SetFloat("_ZWrite", 0);
            fade.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            fade.renderQueue = (int)RenderQueue.Transparent;
            AssetDatabase.CreateAsset(fade, "Assets/Resources/WorldLitFade.mat");
        }
        if (!File.Exists(ScenePath))
        {
            Directory.CreateDirectory("Assets/Scenes");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.CloseScene(scene, true);
        }
        var configured = EditorBuildSettings.scenes;
        if (configured.Length != 1 || configured[0].path != ScenePath || !configured[0].enabled)
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
    }

    // Local test build (Builds/Win64). Not a development build either; kept for the existing QA flow.
    public static void BuildWindows()
    {
        Build("Builds/Win64/PingTheKiller.exe", "[U4] Windows build succeeded: ");
    }

    // Store/MSIX input (Builds/StoreWin64): release player (no Development Build, no script debugging, no profiler
    // autoconnect), Mono scripting backend (IL2CPP Windows support is not installed on this machine), x64.
    public static void BuildWindowsRelease()
    {
        Build("Builds/StoreWin64/PingTheKiller.exe", "[S1] Windows release build succeeded: ");
    }

    private static void Build(string relativeOutput, string successMessage)
    {
        EnsureAssets();
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        EditorUserBuildSettings.development = false;
        EditorUserBuildSettings.allowDebugging = false;
        EditorUserBuildSettings.connectProfiler = false;
        // Game mark as the exe/title-bar icon (source made by Packaging/make-logos.ps1).
        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Icon/AppIcon.png");
        if (icon == null) throw new InvalidOperationException("Assets/Art/Icon/AppIcon.png is missing. Run Packaging/make-logos.ps1.");
        // One slot per size Unity asks for; every slot gets the same source texture (Unity downsamples it).
        var iconSizes = PlayerSettings.GetIconSizes(UnityEditor.Build.NamedBuildTarget.Standalone, UnityEditor.IconKind.Application);
        var icons = new Texture2D[iconSizes.Length];
        for (int i = 0; i < icons.Length; i++) icons[i] = icon;
        PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Standalone, icons, UnityEditor.IconKind.Application);
        string output = Path.GetFullPath(relativeOutput);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath }, locationPathName = output,
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.StrictMode
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("Windows build failed: " + report.summary.result + ", errors=" + report.summary.totalErrors);
        Debug.Log(successMessage + output);
    }
}

