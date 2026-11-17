using System;
using System.IO;
using PingTheKiller.Client.UI;
using PingTheKiller.Client.World;
using UnityEditor;
using UnityEngine;

namespace PingTheKiller.Editor.Setup
{
    // HUD_SPEC 14 skill feedback assets. Batch: -executeMethod PingTheKiller.Editor.Setup.SkillFxSetup.Run
    // Builds Assets/Resources/SkillFxArt.asset: the Hovl one-shot prefabs (FxPrefab order), the Sci-Fi pack clips (FxClip
    // order), and two materials under Assets/Art/SkillFx: a copy of the vendor marker material with a low emission (the
    // vendor 18 burns tints to white and the bloom threshold is 0.9) and the unlit ring material (it also keeps the URP
    // Unlit shader in the player build). Vendor files are only read.
    public static class SkillFxSetup
    {
        public const string Root = "Assets/Art/SkillFx";
        public const string Catalog = "Assets/Resources/SkillFxArt.asset";
        public const string PrefabRoot = "Assets/ThirdParty/Hovl Studio/Map track markers VFX/Prefabs/";
        public const string AudioRoot = "Assets/ThirdParty/Sci-Fi Weapons-Bullet Hell Sound Effects Pack/AUDIO/";
        public const string VendorMarker = "Assets/ThirdParty/Hovl Studio/HSFiles/Materials/MarkersAtlas1bcg.mat";
        public const float MarkerEmission = .8f;

        [MenuItem("Ping the Killer/Setup/Skill FX")]
        public static void Run()
        {
            Directory.CreateDirectory(Root);
            AssetDatabase.Refresh();
            var catalog = AssetDatabase.LoadAssetAtPath<SkillFxArt>(Catalog);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<SkillFxArt>();
                AssetDatabase.CreateAsset(catalog, Catalog);
            }
            catalog.Prefabs = new GameObject[SkillFeedback.PrefabNames.Length];
            for (int i = 1; i < catalog.Prefabs.Length; i++)
            {
                string path = PrefabRoot + SkillFeedback.PrefabNames[i] + ".prefab";
                catalog.Prefabs[i] = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (catalog.Prefabs[i] == null) throw new InvalidOperationException("Missing prefab: " + path);
            }
            catalog.Clips = new AudioClip[SkillFeedback.ClipPaths.Length];
            for (int i = 1; i < catalog.Clips.Length; i++)
            {
                string path = AudioRoot + SkillFeedback.ClipPaths[i];
                catalog.Clips[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (catalog.Clips[i] == null) throw new InvalidOperationException("Missing clip: " + path);
            }
            catalog.Marker = Marker();
            catalog.Ring = RingMaterial();
            catalog.Glyph = GlyphMaterial();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("[setup] SkillFxArt: " + (catalog.Prefabs.Length - 1) + " prefabs, " + (catalog.Clips.Length - 1) + " clips, marker emission " + MarkerEmission + ", glyph material");
        }

        private static Material Marker()
        {
            var vendor = AssetDatabase.LoadAssetAtPath<Material>(VendorMarker);
            if (vendor == null) throw new InvalidOperationException("Missing material: " + VendorMarker);
            string path = Root + "/SkillFx marker.mat";
            var copy = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (copy == null)
            {
                copy = new Material(vendor);
                AssetDatabase.CreateAsset(copy, path);
            }
            else copy.CopyPropertiesFromMaterial(vendor);
            if (copy.HasProperty("_Emission")) copy.SetFloat("_Emission", MarkerEmission);
            EditorUtility.SetDirty(copy);
            return copy;
        }

        // U9 world glyph: URP Unlit, alpha clip at 0.5 (no blending, HUD_SPEC 14.1 World effects), opaque-cutout queue.
        private static Material GlyphMaterial()
        {
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null) throw new InvalidOperationException("Missing shader: Universal Render Pipeline/Unlit");
            string path = Root + "/SkillFx glyph.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(unlit);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = unlit;
            material.color = Color.white;
            material.SetFloat("_Surface", 0);
            material.SetFloat("_AlphaClip", 1);
            material.SetFloat("_Cutoff", .5f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material RingMaterial()
        {
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null) throw new InvalidOperationException("Missing shader: Universal Render Pipeline/Unlit");
            string path = Root + "/SkillFx ring.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(unlit);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = unlit;
            material.color = Color.white;
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
