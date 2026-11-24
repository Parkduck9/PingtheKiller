using PingTheKiller.Client.UI;
using UnityEngine;

namespace PingTheKiller.Client.World
{
    // HUD_SPEC 14 skill feedback assets, built by Editor/Setup/SkillFxSetup (-executeMethod
    // PingTheKiller.Editor.Setup.SkillFxSetup.Run): the Hovl one-shot prefabs (indexed by FxPrefab), the Sci-Fi pack clips
    // (indexed by FxClip), the low-emission copy of the vendor marker material and the unlit ring material
    // (both under Assets/Art/SkillFx). The vendor files stay untouched.
    public sealed class SkillFxArt : ScriptableObject
    {
        public GameObject[] Prefabs = new GameObject[0];
        public AudioClip[] Clips = new AudioClip[0];
        public Material Marker;
        public Material Ring;

        public static SkillFxArt Load() => Resources.Load<SkillFxArt>("SkillFxArt");

        public GameObject Prefab(FxPrefab prefab) => Prefabs != null && (int)prefab < Prefabs.Length ? Prefabs[(int)prefab] : null;
        public AudioClip Clip(FxClip clip) => Clips != null && (int)clip < Clips.Length ? Clips[(int)clip] : null;
    }
}
