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

    public static void BuildWindows()
    {
        EnsureAssets();
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        string output = Path.GetFullPath("Builds/Win64/PingTheKiller.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath }, locationPathName = output,
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.StrictMode
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("Windows build failed: " + report.summary.result + ", errors=" + report.summary.totalErrors);
        Debug.Log("[U4] Windows build succeeded: " + output);
    }
}
