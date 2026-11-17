using System.Collections.Generic;
using PingTheKiller.Client.Audio;
using PingTheKiller.Client.Net;
using PingTheKiller.Client.UI;
using PingTheKiller.Client.UI.RulesV2;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.World
{
    // HUD_SPEC 15 (Q5): everything the Shutters and Rails add to a match, on the "Match world" object next to WorldView:
    // the props (ChasePropsView), the positional cues (ChasePropAudio), the pose overlay on drawn characters
    // (ChasePoseRig), the stun burst and the stun timing (StunFeedback), and the prompt / hold / effect-line additions the
    // HUDs need (ClientApp calls the Survivor and Killer helpers after the existing HUD code ran, so the existing prompts
    // keep their order and only an empty slot takes a Shutter or Rail prompt).
    public sealed class ChasePropsLayer : MonoBehaviour
    {
        public const string KillerModelName = "Killer (line of sight)";
        private const int KillerKey = int.MinValue;

        private sealed class Actor
        {
            public readonly ChasePoseClock Clock = new ChasePoseClock();
            public ChasePoseRig Rig;
            public Transform Root;
        }

        private WorldMaterials materials;
        private ChasePropsView view;
        private ChasePropAudio audioCues;
        private StunBurst burst;
        private readonly StunFeedback stun = new StunFeedback();
        private readonly Dictionary<int, Actor> actors = new Dictionary<int, Actor>();
        private readonly List<int> stale = new List<int>();
        private readonly HashSet<int> present = new HashSet<int>();
        private IReadOnlyList<byte> shutters;
        private Role role;
        private int ownId;

        public ChasePropsView View => view;
        public ChasePropAudio Audio => audioCues;
        public StunFeedback Stun => stun;
        public StunBurst Burst => burst;
        public bool Reduced { get; set; }
        public IReadOnlyList<byte> Shutters => shutters;
        // Dev capture / tests: the pose of a character (Survivor id, or KillerKey for the Killer model).
        public ChasePose PoseOf(int id) => actors.TryGetValue(id, out Actor actor) ? actor.Clock.Pose : ChasePose.None;
        public ChasePose KillerPose => PoseOf(KillerKey);

        public void Initialize(int playerId, Role localRole, bool reduced)
        {
            ownId = playerId; role = localRole; Reduced = reduced;
            materials = new WorldMaterials(transform);
            view = new ChasePropsView(materials) { ReducedMotionOverride = reduced };
            view.Initialize();
            audioCues = gameObject.AddComponent<ChasePropAudio>();
            burst = new StunBurst(transform);
        }

        public void Accept(S2C_WorldState state, double now)
        {
            if (state == null || view == null) return;
            shutters = state.Shutters;
            view.Accept(state.Shutters, now);
            audioCues.OnWorld(state);
            present.Clear();
            bool killerStunned = false;
            foreach (PlayerState player in state.Players)
            {
                bool killer = player.Role == (byte)Role.Killer;
                if (killer) killerStunned = (player.Chase & (byte)ChaseFlags.Stunned) != 0;
                // The Killer client draws no characters (HUD_SPEC 6.0); a Survivor draws every Survivor and the Killer model.
                if (role != Role.Survivor) continue;
                int key = killer ? KillerKey : player.Id;
                present.Add(key);
                if (!actors.TryGetValue(key, out Actor actor)) actors.Add(key, actor = new Actor());
                ChasePose pose = ChaseActionMap.Pose(player.Chase);
                actor.Clock.Observe(pose, ChaseActionMap.Seconds(pose, killer ? Role.Killer : Role.Survivor, player.X, player.Z, state.Shutters), now);
            }
            // An entry missing from a snapshot (the Killer out of sight) ends its pose; it starts again with the next one.
            stale.Clear();
            foreach (var pair in actors) if (!present.Contains(pair.Key)) stale.Add(pair.Key);
            foreach (int key in stale) actors[key].Clock.Reset();
            if (stun.Observe(killerStunned, now)) Debug.Log("[chase] killer stunned (" + (role == Role.Killer ? "own" : "seen") + ")");
        }

        // Each S2C_SelfState, raw: SelfStateHolder keeps only the v0.4 hold kinds, so the chase kinds are tracked here.
        // Progress advances at the server's rate between packets (SelfState is sent when the value departs from that).
        public void OnSelf(byte actionKind, float progress, double now)
        {
            ChaseAction = ChasePropPrompt.IsChaseAction(actionKind) ? actionKind : (byte)0;
            actionProgress = float.IsNaN(progress) ? 0 : Mathf.Clamp01(progress);
            actionAt = now;
        }

        public byte ChaseAction { get; private set; }
        private float actionProgress;
        private double actionAt;

        public float ActionProgress(double now)
        {
            if (ChaseAction == 0) return 0;
            float seconds = ChaseAction == (byte)Shared.Game.ActionKind.Break ? GameConstants.ShutterBreakSeconds
                : ChaseAction == (byte)Shared.Game.ActionKind.Pull ? GameConstants.ShutterPullSeconds
                : role == Role.Killer ? GameConstants.RailVaultKillerSeconds : GameConstants.RailVaultSurvivorSeconds;
            return Mathf.Clamp01(actionProgress + (float)((now - actionAt) / seconds));
        }

        public void Frame(double now) => view?.Render(now);

        // After the Animator (LateUpdate): the pose overlay and the stun burst over the drawn Killer model.
        private void LateUpdate() => ApplyPoses(LinkClock.Now);

        // Public for the editor capture (no player loop there).
        public void ApplyPoses(double now)
        {
            if (view == null || role != Role.Survivor) return;
            Transform killerHead = null;
            foreach (var pair in actors)
            {
                Actor actor = pair.Value;
                if (actor.Clock.Pose == ChasePose.None) continue;
                if (actor.Root == null)
                {
                    actor.Root = transform.Find(pair.Key == KillerKey ? KillerModelName : "Player " + pair.Key);
                    actor.Rig = actor.Root != null ? new ChasePoseRig(actor.Root) : null;
                }
                if (actor.Rig == null || !actor.Root.gameObject.activeInHierarchy) continue;
                actor.Rig.Apply(actor.Clock, now, Reduced);
                if (pair.Key == KillerKey) killerHead = actor.Root;
            }
            bool showBurst = killerHead != null && stun.Showing(now);
            burst.Set(showBurst, showBurst ? killerHead.position + Vector3.up * 1.9f : Vector3.zero, Reduced);
        }

        // ---- HUD helpers (called by ClientApp after the existing HUD code) ------------------------------------------------

        // Survivor: a chase action of the own SelfState replaces the hold row by its action line (no bar: 0.3-1.0 s); with
        // no action, an empty (or muted) prompt slot takes the nearest Shutter or Rail prompt while the own status is Normal.
        public static void SurvivorPrompt(ref PromptState prompt, ref HoldActionView hold, byte actionKind, bool normal, bool blocked, float x, float z, IReadOnlyList<byte> shutters)
        {
            if (ChasePropPrompt.IsChaseAction(actionKind))
            {
                hold = default;
                prompt = new PromptState { Text = actionKind == (byte)Shared.Game.ActionKind.Pull ? ChasePropCopy.Pulling : ChasePropCopy.Vaulting };
                return;
            }
            if (hold.Kind != HoldKind.None || !normal || blocked || (!string.IsNullOrEmpty(prompt.Text) && !prompt.Muted)) return;
            string text = ChasePropPrompt.SurvivorText(ChasePropPrompt.Survivor(x, z, shutters));
            if (text != null) prompt = new PromptState { Text = text };
        }

        // Killer: the Break / Vault hold block (the generic block has no copy for these kinds), the Shutter / Rail prompt in
        // an empty prompt slot, and the `Stunned` effect line while the server reports it.
        public void ApplyKillerHud(KillerHud hud, Vector3 position, bool menuOpen, double now)
        {
            if (hud == null) return;
            string line = stun.Line(now);
            if (line != null) hud.SetEffect(line, false);
            if (menuOpen) return;
            byte kind = ChaseAction;
            if (ChasePropPrompt.IsChaseAction(kind))
            {
                float progress = ActionProgress(now);
                hud.SetPrompt(default, default); // no kick / hatch prompt next to the block
                bool force = kind == (byte)Shared.Game.ActionKind.Break;
                hud.Hold.Set(force ? ChasePropCopy.ForcingLabel(HoldCopy.Percent(progress)) : ChasePropCopy.KillerVaulting, progress);
                Label hint = hud.Hold.Q<Label>(className: "v2-hold-hint");
                if (hint != null) hint.text = force ? ChasePropCopy.ForcingHint : ChasePropCopy.KillerVaultingHint;
                return;
            }
            if (hud.PromptText != null || stun.Stunned) return;
            InteractPromptText prompt = ChasePropPrompt.Killer(ChasePropPrompt.Killer(position.x, position.z, shutters));
            if (prompt.Text != null) hud.SetPrompt(new InteractPrompt { Text = prompt.Text, Hint = prompt.Hint }, default);
        }

        private void OnDestroy()
        {
            view?.Dispose();
            burst?.Dispose();
            materials?.Dispose();
        }
    }
}
