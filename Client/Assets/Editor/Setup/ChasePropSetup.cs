using System.Collections.Generic;
using System.IO;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using UnityEditor;
using UnityEngine;

namespace PingTheKiller.Editor.Setup
{
    // HUD_SPEC 15.1 (Q5): builds the Shutter and Rail prefabs and the ChaseProps catalog. A sibling of WorldArtSetup that
    // never touches WorldArt (its catalog and Art/World belong to their own setup): it writes Assets/Art/ChaseProps/**
    // and Assets/Resources/ChaseProps.asset only. One prefab per WorldChaseProps box, built from ChasePropShapes (the same
    // parts the runtime primitive fallback uses) with toon materials in the WorldArt palette (night graded like the city).
    // Idempotent: rerunning overwrites the same assets in place.
    //   Unity.exe -batchmode -quit -projectPath Client -executeMethod PingTheKiller.Editor.Setup.ChasePropSetup.Run
    public static class ChasePropSetup
    {
        public const string Root = "Assets/Art/ChaseProps";
        public const string Catalog = "Assets/Resources/" + ChasePropArt.ResourceName + ".asset";

        [MenuItem("Ping the Killer/Setup Chase Props")]
        public static void Run()
        {
            foreach (string folder in new[] { Root, Root + "/Materials", Root + "/Prefabs" }) Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            var materials = new Dictionary<ChasePropShapes.Paint, Material>();
            var catalog = AssetDatabase.LoadAssetAtPath<ChasePropArt>(Catalog);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ChasePropArt>();
                AssetDatabase.CreateAsset(catalog, Catalog);
            }
            IReadOnlyList<ChaseProp> shutters = WorldChaseProps.Shutters, rails = WorldChaseProps.Rails;
            catalog.Shutters = new ChasePropArt.Piece[shutters.Count];
            for (int i = 0; i < shutters.Count; i++)
                catalog.Shutters[i] = Piece(ChasePropShapes.ShutterParts(shutters[i]), shutters[i], "Shutter " + i, materials);
            catalog.Rails = new ChasePropArt.Piece[rails.Count];
            for (int i = 0; i < rails.Count; i++)
                catalog.Rails[i] = Piece(ChasePropShapes.RailParts(rails[i]), rails[i], "Rail " + i, materials);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("[ChasePropSetup] ChaseProps catalog ready: " + shutters.Count + " shutters, " + rails.Count + " rails, " + materials.Count + " materials; matches = " + catalog.Matches());
        }

        private static ChasePropArt.Piece Piece(List<ChasePropShapes.Part> parts, ChaseProp prop, string name, Dictionary<ChasePropShapes.Paint, Material> materials)
        {
            // Built in the prop's local frame (x along the prop); the runtime places the root at the box centre on the floor
            // with ChasePropShapes.Yaw, exactly like the primitive fallback.
            var root = new GameObject(name);
            var groups = new Dictionary<string, Transform>();
            foreach (ChasePropShapes.Part part in parts)
            {
                if (!groups.TryGetValue(part.Group, out Transform group))
                {
                    group = new GameObject(part.Group).transform;
                    group.SetParent(root.transform, false);
                    groups.Add(part.Group, group);
                }
                GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
                piece.name = part.Name;
                Object.DestroyImmediate(piece.GetComponent<Collider>());
                Renderer renderer = piece.GetComponent<Renderer>();
                renderer.sharedMaterial = Paint(part.Paint, materials);
                piece.transform.SetParent(group, false);
                piece.transform.localPosition = part.Center;
                piece.transform.localRotation = Quaternion.Euler(part.Euler);
                piece.transform.localScale = part.Size;
                piece.isStatic = false; // the Down panel moves (the drop)
            }
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/" + name + ".prefab");
            Object.DestroyImmediate(root);
            return new ChasePropArt.Piece { Prefab = prefab, Box = new Vector4(prop.MinX, prop.MinZ, prop.MaxX, prop.MaxZ) };
        }

        private static Material Paint(ChasePropShapes.Paint paint, Dictionary<ChasePropShapes.Paint, Material> made)
        {
            if (made.TryGetValue(paint, out Material cached)) return cached;
            string path = Root + "/Materials/" + paint + ".mat";
            Shader toon = ToonShading.Find();
            Shader shader = toon != null ? toon : Shader.Find("Universal Render Pipeline/Lit");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            if (material.shader != shader) { material.shader = shader; material.shaderKeywords = new string[0]; }
            Color colour = ChasePropShapes.Colour(paint);
            material.SetColor("_BaseColor", ChasePropShapes.Graded(paint) ? ToonShading.NightGrade(colour) : colour);
            if (toon != null) ToonShading.Configure(material, ToonShading.Kind.World);
            else { material.SetFloat("_Smoothness", .3f); material.SetFloat("_Metallic", 0); }
            EditorUtility.SetDirty(material);
            made[paint] = material;
            return material;
        }
    }
}
