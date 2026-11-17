using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.World
{
    // The static map plus, on a Survivor client, the Survivor characters (Id-based SnapshotBuffer interpolation) and the
    // Killer model. The Killer model is drawn only from the Killer entry of the snapshots (SnapshotBuffer.SampleKiller),
    // which the server sends to a Survivor only while its line of sight to the Killer is clear (HUD_SPEC 4.10 (10/30)): no
    // marker, outline or through-wall indicator, and nothing else (threat level, effects) ever places it.
    // The Killer client draws no character here: it has no body, and Survivors reach it only as anonymous blips
    // (Killer/KillerWorldLayer). Its own position is read by Role.
    // The MonoBehaviour that owns the snapshots and delegates to CharacterViews (characters, Killer model) and WorldProps
    // (floor, walls, terminals, Exit); both draw through the shared WorldMaterials.
    public sealed class WorldView : MonoBehaviour
    {
        public static readonly Vector3 KillerBodyScale = CharacterViews.KillerBodyScale;
        // Alpha the Killer model was drawn with in the last Render (0 = hidden); read by the dev capture.
        public float KillerAlpha => characters != null ? characters.KillerAlpha : 0;
        // R1 (11/17): Killer swings whose wind-up this Survivor client started on the drawn model.
        public int KillerSwings => characters != null ? characters.KillerSwings : 0;
        // Alpha of the Killer's visor material after the last Render; it fades with the body (KillerAlpha).
        public float KillerVisorAlpha => characters != null ? characters.KillerVisorAlpha : 0;

        // The escape hatch (both roles): drawn only while the latest snapshot carries one; snaps to its position.
        public bool HatchShown => hatch != null && hatch.Shown;
        public Vector3 HatchPosition => hatch != null ? hatch.Position : Vector3.zero;
        public HatchView Hatch => hatch;

        // A spectator (own status Eliminated or Escaped) never sees the Killer model, even if an entry arrives (U4e Q-04b).
        public static bool KillerDrawable(byte ownStatus) => CharacterViews.KillerDrawable(ownStatus);

        private readonly SnapshotBuffer snapshots = new SnapshotBuffer();
        private WorldMaterials materials;
        private CharacterViews characters;
        private WorldProps props;
        private HatchView hatch;
        private int localId;
        private Role role;

        public void Initialize(int id, Role localRole)
        {
            localId = id; role = localRole;
            materials = new WorldMaterials(transform);
            props = new WorldProps(materials, role);
            characters = new CharacterViews(materials, localId, role);
            hatch = new HatchView(materials);
            props.Initialize();
            characters.Initialize();
        }

        public void Accept(S2C_WorldState state, double time)
        {
            snapshots.Add(state, time);
            characters.Accept(state);
            props.Accept(state);
            hatch.Set(state.HatchVisible == 1, state.HatchX, state.HatchZ);
        }

        // Survivor view: the own character faces the local facing (HUD_SPEC 4.10) and is dithered while the boom is short.
        public void SetLocalLook(float facing, bool dither) => characters.SetLocalLook(facing, dither);

        // U10: the own hold action (S2C_SelfState.ActionKind) for the own exit switch pose.
        public void SetOwnAction(byte kind) => characters?.SetOwnAction(kind);

        // 11/17 (R2) packet routing: the own holo panel pulses on real route keys while routing (HoloPropFx.KeyDriven).
        public void SetOwnRouting(bool routing) => characters?.SetOwnRouting(routing);
        public void OwnKeyPulse(double now) => characters?.OwnKeyPulse(now);

        public void Render(double now)
        {
            characters.Render(now, snapshots);
            hatch.Render(now);
        }

        // Survivor: the own entry by Id. Killer: the own entry by Role (HUD_SPEC 6.2).
        public bool LocalPosition(double now, out Vector3 position)
        {
            return snapshots.Sample(role == Role.Killer ? SnapshotBuffer.KillerKey : localId, now, out position, out _, out _);
        }

        private void OnDestroy()
        {
            materials?.Dispose();
            characters?.Dispose();
            props?.Dispose();
            hatch?.Dispose();
        }
    }
}
