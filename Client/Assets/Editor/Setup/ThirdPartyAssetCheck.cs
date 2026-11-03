using System;
using UnityEditor;
using UnityEngine;

namespace PingTheKiller.Editor.Setup
{
    public static class ThirdPartyAssetCheck
    {
        static readonly string[] Roots = { "Assets/ThirdParty", "Assets/Plugins" };
        static int problems;

        public static void Run()
        {
            try
            {
                problems = 0;
                int materials = 0, prefabs = 0, models = 0, upgraded = 0;

                var lit = Shader.Find("Universal Render Pipeline/Lit");
                if (lit == null)
                {
                    throw new InvalidOperationException("Shader 'Universal Render Pipeline/Lit' not found");
                }

                foreach (string guid in AssetDatabase.FindAssets("t:Material", Roots))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)) continue;
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material != null && material.shader != null && IsBuiltIn(material.shader.name))
                    {
                        Upgrade(material, lit);
                        EditorUtility.SetDirty(material);
                        upgraded++;
                        Debug.Log("[ThirdPartyAssetCheck] upgraded " + path);
                    }
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                foreach (string guid in AssetDatabase.FindAssets("t:Material", Roots))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null) continue;
                    materials++;
                    CheckMaterial(path, material);
                }

                foreach (string guid in AssetDatabase.FindAssets("t:GameObject", Roots))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (root == null) continue;

                    if (AssetImporter.GetAtPath(path) is ModelImporter)
                    {
                        models++;
                        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                        {
                            foreach (var material in renderer.sharedMaterials)
                            {
                                CheckMaterial(path + " [" + renderer.name + "]", material);
                            }
                        }
                    }
                    else
                    {
                        prefabs++;
                        foreach (var child in root.GetComponentsInChildren<Transform>(true))
                        {
                            int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject);
                            if (missing > 0)
                            {
                                Warn(path, missing + " missing script(s) on '" + child.name + "'");
                            }
                        }
                    }
                }

                Debug.Log("[ThirdPartyAssetCheck] materials=" + materials + ", prefabs=" + prefabs
                    + ", models=" + models + ", upgraded=" + upgraded + ", problems=" + problems);

                if (problems > 0 && Application.isBatchMode)
                {
                    EditorApplication.Exit(2);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[ThirdPartyAssetCheck] Failed: " + e);
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }
            }
        }

        static void CheckMaterial(string where, Material material)
        {
            if (material == null)
            {
                Warn(where, "missing material reference");
                return;
            }

            var shader = material.shader;
            if (shader == null)
            {
                Warn(where, "material '" + material.name + "' has a null shader");
                return;
            }

            string name = shader.name;
            if (!shader.isSupported)
                Warn(where, "material '" + material.name + "' shader '" + name + "' is not supported");
            if (name == "Hidden/InternalErrorShader")
                Warn(where, "material '" + material.name + "' uses the error shader");
            else if (IsBuiltIn(name))
                Warn(where, "material '" + material.name + "' still uses Built-in shader '" + name + "'");
        }

        static bool IsBuiltIn(string shaderName)
        {
            return shaderName == "Standard" || shaderName == "Standard (Specular setup)"
                || shaderName.StartsWith("Legacy Shaders/", StringComparison.Ordinal);
        }

        static void Upgrade(Material m, Shader lit)
        {
            Texture mainTex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
            Vector2 scale = m.HasProperty("_MainTex") ? m.GetTextureScale("_MainTex") : Vector2.one;
            Vector2 offset = m.HasProperty("_MainTex") ? m.GetTextureOffset("_MainTex") : Vector2.zero;
            bool hasColor = m.HasProperty("_Color");
            Color color = hasColor ? m.GetColor("_Color") : Color.white;
            Texture bump = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;
            bool hasBumpScale = m.HasProperty("_BumpScale");
            float bumpScale = hasBumpScale ? m.GetFloat("_BumpScale") : 1f;
            Texture metalMap = m.HasProperty("_MetallicGlossMap") ? m.GetTexture("_MetallicGlossMap") : null;
            bool hasMetallic = m.HasProperty("_Metallic");
            float metallic = hasMetallic ? m.GetFloat("_Metallic") : 0f;
            bool hasGloss = m.HasProperty("_Glossiness");
            float gloss = hasGloss ? m.GetFloat("_Glossiness") : 0.5f;
            Texture emissionMap = m.HasProperty("_EmissionMap") ? m.GetTexture("_EmissionMap") : null;
            bool hasEmissionColor = m.HasProperty("_EmissionColor");
            Color emission = hasEmissionColor ? m.GetColor("_EmissionColor") : Color.black;
            bool emissionOn = m.IsKeywordEnabled("_EMISSION");
            int mode = m.HasProperty("_Mode") ? Mathf.RoundToInt(m.GetFloat("_Mode")) : 0;
            bool hasCutoff = m.HasProperty("_Cutoff");
            float cutoff = hasCutoff ? m.GetFloat("_Cutoff") : 0.5f;

            m.shader = lit;
            m.shaderKeywords = null;

            if (mainTex != null) m.SetTexture("_BaseMap", mainTex);
            m.SetTextureScale("_BaseMap", scale);
            m.SetTextureOffset("_BaseMap", offset);
            if (hasColor) m.SetColor("_BaseColor", color);
            if (bump != null)
            {
                m.SetTexture("_BumpMap", bump);
                m.EnableKeyword("_NORMALMAP");
            }
            if (hasBumpScale) m.SetFloat("_BumpScale", bumpScale);
            if (metalMap != null)
            {
                m.SetTexture("_MetallicGlossMap", metalMap);
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            if (hasMetallic) m.SetFloat("_Metallic", metallic);
            if (hasGloss) m.SetFloat("_Smoothness", gloss);
            if (emissionMap != null) m.SetTexture("_EmissionMap", emissionMap);
            if (hasEmissionColor) m.SetColor("_EmissionColor", emission);
            if (emissionOn) m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = emissionOn ? MaterialGlobalIlluminationFlags.BakedEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;

            if (mode == 1)
            {
                m.SetFloat("_AlphaClip", 1f);
                if (hasCutoff) m.SetFloat("_Cutoff", cutoff);
                m.EnableKeyword("_ALPHATEST_ON");
                m.renderQueue = 2450;
            }
            else if (mode == 2 || mode == 3)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetShaderPassEnabled("ShadowCaster", false);
                m.renderQueue = 3000;
            }
        }

        static void Warn(string path, string problem)
        {
            problems++;
            Debug.LogWarning("[ThirdPartyAssetCheck] " + path + ": " + problem);
        }
    }
}
