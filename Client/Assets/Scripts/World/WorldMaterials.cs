using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PingTheKiller.Client.World
{
    // The one material path of the world view (every primitive, ring and character part goes through MakeMaterial) and the
    // owner of the materials it creates. Shared by CharacterViews and WorldProps; kept frozen so that the two can change
    // independently.
    public sealed class WorldMaterials
    {
        // The Survivor team colour (`ally`): the Survivor characters, done terminals and the open Exit on a Survivor client.
        public static readonly Color Survivor = new Color32(63, 184, 175, 255);

        private readonly Transform parent;
        private readonly List<Material> materials = new List<Material>();

        public WorldMaterials(Transform parent)
        {
            this.parent = parent;
        }

        public Transform Parent => parent;

        public Material MakeMaterial(Color color, bool transparent = false)
        {
            var template = Resources.Load<Material>(transparent ? "WorldLitFade" : "WorldLit");
            var material = template != null ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.color = color;
            if (transparent)
            {
                material.SetFloat("_Surface", 1);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            materials.Add(material);
            return material;
        }

        public GameObject Primitive(string label, PrimitiveType type, Vector3 position, Vector3 scale, Color color, bool transparent = false)
        {
            var item = GameObject.CreatePrimitive(type);
            item.name = label;
            item.transform.SetParent(parent, false);
            item.transform.position = position;
            item.transform.localScale = scale;
            item.GetComponent<Renderer>().sharedMaterial = MakeMaterial(color, transparent);
            Object.Destroy(item.GetComponent<Collider>()); // Positions and collisions are server-owned.
            return item;
        }

        public void Dispose()
        {
            foreach (Material material in materials) Object.Destroy(material);
            materials.Clear();
        }
    }
}
