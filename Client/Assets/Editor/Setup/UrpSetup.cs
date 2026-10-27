using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PingTheKiller.Editor.Setup
{
    public static class UrpSetup
    {
        const string SettingsFolder = "Assets/Settings";
        const string RendererPath = SettingsFolder + "/PC_Renderer.asset";
        const string PipelinePath = SettingsFolder + "/PC_RPAsset.asset";

        public static void Run()
        {
            try
            {
                if (!AssetDatabase.IsValidFolder(SettingsFolder))
                {
                    AssetDatabase.CreateFolder("Assets", "Settings");
                }

                var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
                bool rendererCreated = rendererData == null;
                if (rendererCreated)
                {
                    rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                    AssetDatabase.CreateAsset(rendererData, RendererPath);
                    ResourceReloader.ReloadAllNullIn(rendererData, UniversalRenderPipelineAsset.packagePath);
                }

                if (rendererData.renderingMode != RenderingMode.ForwardPlus)
                {
                    rendererData.renderingMode = RenderingMode.ForwardPlus;
                    EditorUtility.SetDirty(rendererData);
                }
                else if (rendererCreated)
                {
                    EditorUtility.SetDirty(rendererData);
                }

                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
                bool pipelineCreated = pipeline == null;
                if (pipelineCreated)
                {
                    pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                    AssetDatabase.CreateAsset(pipeline, PipelinePath);
                }

                if (!pipeline.supportsCameraDepthTexture)
                {
                    pipeline.supportsCameraDepthTexture = true;
                    EditorUtility.SetDirty(pipeline);
                }
                else if (pipelineCreated)
                {
                    EditorUtility.SetDirty(pipeline);
                }

                if (GraphicsSettings.defaultRenderPipeline != pipeline)
                {
                    GraphicsSettings.defaultRenderPipeline = pipeline;
                }

                int originalLevel = QualitySettings.GetQualityLevel();
                int levelCount = QualitySettings.names.Length;
                try
                {
                    for (int i = 0; i < levelCount; i++)
                    {
                        QualitySettings.SetQualityLevel(i, false);
                        if (QualitySettings.renderPipeline != pipeline)
                        {
                            QualitySettings.renderPipeline = pipeline;
                        }
                    }
                }
                finally
                {
                    QualitySettings.SetQualityLevel(originalLevel, false);
                }

                if (PlayerSettings.colorSpace != ColorSpace.Linear)
                {
                    PlayerSettings.colorSpace = ColorSpace.Linear;
                }

                AssetDatabase.SaveAssets();
                Debug.Log("[UrpSetup] URP configured: renderer=" + RendererPath
                    + " (Forward+), asset=" + PipelinePath
                    + ", qualityLevels=" + levelCount
                    + ", colorSpace=" + PlayerSettings.colorSpace
                    + ", created=" + (rendererCreated || pipelineCreated));
            }
            catch (Exception e)
            {
                Debug.LogError("[UrpSetup] Failed: " + e);
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }
            }
        }
    }
}
