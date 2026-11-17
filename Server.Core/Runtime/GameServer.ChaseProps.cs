using System;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.ServerCore
{
    // GDD v2 section 8 (unit Q5a): roll-down shutters and vault rails, decided only here on the game-loop thread.
    //
    // Interact priority (deterministic):
    //   Survivor, on a fresh press: a survivor in rescue contact this tick starts nothing (rescue first); otherwise the
    //     nearest of {Up shutter within 1.5 m of its box -> pull, Down shutter within 1.0 m of its box -> vault,
    //     rail within 1.0 m of its vault point -> vault} wins (ties: shutters before rails, lower id first). Only when
    //     none is in range does the press fall through to the exit switch / terminal hold (GameServer.Terminals).
    //   Killer: a fresh press within 1.0 m of a rail vault point vaults; otherwise holding Interact still within
    //     1.5 m of a Down shutter breaks it (nearest). Either one suppresses the hatch close and the kick that tick.
    // A pull, vault or break freezes the actor (move input ignored); a survivor is forced Signal during a pull or
    // vault and for ChaseSignalTailSeconds after. A stunned killer cannot move, capture, interact or use skills.
    public sealed partial class GameServer
    {
        private const float PushClearance = FixedMap.PlayerRadius + 0.05f;

        private void ResetChaseProps()
        {
            Map.ResetShutters();
            Nav.ResetShutters();
        }

        internal ShutterState ShutterStateForTests(int id) => Map.ShutterAt(id);

        // Test hook: whether a player is mid-vault at this exact position (a vaulting body crosses over the prop).
        internal bool VaultingAtForTests(float x, float z)
        {
            lock (_tickGate)
                foreach (var player in _players)
                    if (player.ChaseKind == (byte)ActionKind.Vault && Math.Abs(player.X - x) < 0.001f && Math.Abs(player.Z - z) < 0.001f)
                        return true;
            return false;
        }

        // Test hook: set a shutter state between ticks (keeps the navigation grid in sync).
        internal void SetShutterForTests(int id, ShutterState state)
        {
            lock (_tickGate) SetShutter(id, state);
        }

        private void SetShutter(int id, ShutterState state)
        {
            Map.SetShutter(id, state);
            Nav.SetShutterDown(id, state == ShutterState.Down);
        }

        private bool Stunned(Player player) => Active(player.StunnedUntil);

        private bool ChaseFrozen(Player player) => player.InChaseAction || Stunned(player) || player.BreakShutter >= 0;

        private void ChaseProps(float dt)
        {
            foreach (var player in _players)
            {
                if (player.InChaseAction) AdvanceChase(player);
                else if (player.InteractPressed) StartChase(player);
                player.InteractPressed = false;
            }
            KillerBreak(dt);
        }

        private void StartChase(Player player)
        {
            if (player.Role == Role.Survivor)
            {
                // E priority: a Lagging ally in reach takes the press (rescue) before any shutter or rail.
                if (player.Status != PlayerStatus.Normal || player.Rescuing || NearestLaggingAlly(player) != null) return;
            }
            else if (Stunned(player) || Swinging(player)) return;
            var shutters = WorldChaseProps.Shutters;
            var rails = WorldChaseProps.Rails;
            int pick = -1;
            bool rail = false;
            float best = float.MaxValue;
            if (player.Role == Role.Survivor)
                for (int i = 0; i < shutters.Count; i++)
                {
                    var state = Map.ShutterAt(i);
                    float distance = shutters[i].Distance(player.X, player.Z);
                    float range = state == ShutterState.Up ? GameConstants.ShutterPullRadius
                        : state == ShutterState.Down ? GameConstants.ShutterVaultRadius : -1;
                    if (distance <= range + 0.000001f && distance < best) { best = distance; pick = i; }
                }
            for (int i = 0; i < rails.Count; i++)
            {
                float distance = (float)Math.Sqrt(DistanceSquared(player.X, player.Z, rails[i].CenterX, rails[i].CenterZ));
                if (distance <= GameConstants.RailVaultRadius + 0.000001f && distance < best) { best = distance; pick = i; rail = true; }
            }
            if (pick < 0) return;
            if (!rail && Map.ShutterAt(pick) == ShutterState.Up) BeginPull(player, pick);
            else BeginVault(player, rail ? rails[pick] : shutters[pick], rail);
        }

        private void BeginPull(Player player, int id)
        {
            var shutter = WorldChaseProps.Shutters[id];
            player.ChaseKind = (byte)ActionKind.Pull;
            player.ChaseProp = id;
            player.ChaseOnRail = false;
            player.ChaseStart = _now;
            player.ChaseEnd = _now + GameConstants.ShutterPullSeconds;
            player.ChaseSide = shutter.Side(player.X, player.Z) >= 0 ? 1 : -1;
            player.Hacking = false;
            player.HackTerminal = player.SwitchExit = -1;
            player.DirX = player.DirZ = 0;
            WriteLog($"shutter {id} pull by player={player.Id}");
        }

        private void BeginVault(Player player, ChaseProp prop, bool rail)
        {
            float side = prop.Side(player.X, player.Z) >= 0 ? 1 : -1;
            float along = (player.X - prop.CenterX) * prop.CrossZ + (player.Z - prop.CenterZ) * prop.CrossX;
            if (rail) along = Math.Max(-0.5f, Math.Min(0.5f, along));
            float depth = Math.Max(Math.Abs(prop.Side(player.X, player.Z)), prop.HalfThickness + PushClearance + 0.1f);
            float tx = prop.CenterX + prop.CrossZ * along - prop.CrossX * side * depth;
            float tz = prop.CenterZ + prop.CrossX * along - prop.CrossZ * side * depth;
            if (!Map.CanStand(tx, tz))
            {
                // Straight across the vault point when the mirrored spot is taken by a wall or obstacle.
                tx = prop.CenterX - prop.CrossX * side * depth;
                tz = prop.CenterZ - prop.CrossZ * side * depth;
                if (!Map.CanStand(tx, tz)) return;
            }
            double seconds = !rail ? GameConstants.ShutterVaultSeconds
                : player.Role == Role.Killer ? GameConstants.RailVaultKillerSeconds : GameConstants.RailVaultSurvivorSeconds;
            player.ChaseKind = (byte)ActionKind.Vault;
            player.ChaseProp = prop.Id;
            player.ChaseOnRail = rail;
            player.ChaseStart = _now;
            player.ChaseEnd = _now + seconds;
            player.ChaseFromX = player.X;
            player.ChaseFromZ = player.Z;
            player.ChaseToX = tx;
            player.ChaseToZ = tz;
            player.ChaseSide = side;
            player.Hacking = false;
            player.HackTerminal = player.SwitchExit = -1;
            player.DirX = player.DirZ = 0;
            WriteLog($"{(rail ? "rail" : "shutter")} {prop.Id} vault by player={player.Id} role={player.Role}");
        }

        private void AdvanceChase(Player player)
        {
            bool done = _now >= player.ChaseEnd - TimeEpsilon;
            if (player.ChaseKind == (byte)ActionKind.Vault)
            {
                double t = done ? 1 : Math.Max(0, Math.Min(1, (_now - player.ChaseStart) / (player.ChaseEnd - player.ChaseStart)));
                player.X = (float)(player.ChaseFromX + (player.ChaseToX - player.ChaseFromX) * t);
                player.Z = (float)(player.ChaseFromZ + (player.ChaseToZ - player.ChaseFromZ) * t);
                if (done) FinishChase(player);
                return;
            }
            if (!done) return;
            int id = player.ChaseProp;
            FinishChase(player);
            if (Map.ShutterAt(id) == ShutterState.Up) DropShutter(id, player);
        }

        private void FinishChase(Player player)
        {
            if (player.Role == Role.Survivor) player.ChaseSignalUntil = _now + GameConstants.ChaseSignalTailSeconds;
            player.ChaseKind = 0;
            player.ChaseProp = -1;
        }

        // A capture (or any status change) interrupts a pull or a vault. A vaulting body lands on the nearer side.
        private void CancelChase(Player player)
        {
            if (player.ChaseKind == (byte)ActionKind.Vault)
            {
                double t = (_now - player.ChaseStart) / Math.Max(TimeEpsilon, player.ChaseEnd - player.ChaseStart);
                bool from = t < 0.5 && Map.CanStand(player.ChaseFromX, player.ChaseFromZ);
                player.X = from ? player.ChaseFromX : player.ChaseToX;
                player.Z = from ? player.ChaseFromZ : player.ChaseToZ;
            }
            FinishChase(player);
        }

        private void DropShutter(int id, Player puller)
        {
            var shutter = WorldChaseProps.Shutters[id];
            SetShutter(id, ShutterState.Down);
            bool stun = false;
            var killer = _killer;
            if (killer != null && killer.Status == PlayerStatus.Normal &&
                shutter.Distance(killer.X, killer.Z) < FixedMap.PlayerRadius + GameConstants.ShutterStunInflate)
            {
                stun = true;
                killer.StunnedUntil = _now + GameConstants.ShutterStunSeconds;
                killer.BreakShutter = -1;
                killer.BreakHeld = killer.KickHeld = killer.HatchHeld = 0;
                if (killer.InChaseAction) CancelChase(killer);
                // The killer always ends on the far side from the puller.
                if (shutter.Side(killer.X, killer.Z) * puller.ChaseSide >= 0 || shutter.IntersectsCircle(killer.X, killer.Z, FixedMap.PlayerRadius))
                    PushOut(killer, shutter, -puller.ChaseSide);
            }
            foreach (var player in _players)
            {
                if (player == killer && stun) continue;
                if (!shutter.IntersectsCircle(player.X, player.Z, FixedMap.PlayerRadius)) continue;
                float side = player == puller ? puller.ChaseSide : shutter.Side(player.X, player.Z) >= 0 ? 1 : -1;
                PushOut(player, shutter, side);
            }
            PushPhantomsOut(shutter);
            WriteLog($"shutter {id} down by player={puller.Id} stun={(stun ? "killer" : "none")}");
            if (stun) WriteLog($"stun player={killer.Id} seconds={GameConstants.ShutterStunSeconds} shutter={id}");
        }

        // Moves a body to the given side of a shutter, just clear of it, keeping its position along the shutter.
        private void PushOut(Player player, ChaseProp shutter, float side)
        {
            if (TryPushPoint(shutter, player.X, player.Z, side, out float x, out float z) ||
                TryPushPoint(shutter, player.X, player.Z, -side, out x, out z))
            {
                player.X = x;
                player.Z = z;
                WriteLog($"shutter {shutter.Id} push player={player.Id}");
            }
        }

        private bool TryPushPoint(ChaseProp shutter, float x, float z, float side, out float px, out float pz)
        {
            float along = (x - shutter.CenterX) * shutter.CrossZ + (z - shutter.CenterZ) * shutter.CrossX;
            float depth = Math.Max(shutter.HalfThickness + PushClearance, side * shutter.Side(x, z));
            for (int attempt = 0; attempt < 4; attempt++, depth += 0.25f)
            {
                px = shutter.CenterX + shutter.CrossZ * along + shutter.CrossX * side * depth;
                pz = shutter.CenterZ + shutter.CrossX * along + shutter.CrossZ * side * depth;
                if (Map.CanStand(px, pz)) return true;
            }
            px = x;
            pz = z;
            return false;
        }

        // GDD v2 section 8: the killer holds Interact standing still within 1.5 m of a Down shutter for 2 s; moving,
        // releasing, a stun or a vault cancels. The result is Broken for the rest of the match.
        private void KillerBreak(float dt)
        {
            var killer = _killer;
            if (killer == null) return;
            if (!killer.Interacting || killer.InputHeld(_now) || Stunned(killer) || killer.InChaseAction || Swinging(killer))
            {
                killer.BreakShutter = -1;
                killer.BreakHeld = 0;
                return;
            }
            int target = -1;
            float best = float.MaxValue;
            var shutters = WorldChaseProps.Shutters;
            for (int i = 0; i < shutters.Count; i++)
            {
                if (Map.ShutterAt(i) != ShutterState.Down) continue;
                float distance = shutters[i].Distance(killer.X, killer.Z);
                if (distance <= GameConstants.ShutterBreakRadius + 0.000001f && distance < best) { best = distance; target = i; }
            }
            if (target != killer.BreakShutter) killer.BreakHeld = 0;
            killer.BreakShutter = target;
            if (target < 0) return;
            killer.BreakHeld += dt;
            if (killer.BreakHeld < GameConstants.ShutterBreakSeconds - TimeEpsilon) return;
            killer.BreakHeld = 0;
            killer.BreakShutter = -1;
            SetShutter(target, ShutterState.Broken);
            WriteLog($"shutter {target} broken by player={killer.Id}");
        }

        // A phantom inside a shutter that just came down would never move again (Map.Move refuses every step).
        private void PushPhantomsOut(ChaseProp shutter)
        {
            foreach (var phantom in _decoys)
            {
                if (!shutter.IntersectsCircle(phantom.X, phantom.Z, FixedMap.PlayerRadius)) continue;
                float side = shutter.Side(phantom.X, phantom.Z) >= 0 ? 1 : -1;
                if (TryPushPoint(shutter, phantom.X, phantom.Z, side, out float x, out float z) ||
                    TryPushPoint(shutter, phantom.X, phantom.Z, -side, out x, out z))
                {
                    phantom.X = x;
                    phantom.Z = z;
                }
            }
        }

        // PlayerState.Chase for one player.
        private byte ChaseFlagsOf(Player player)
        {
            var flags = ChaseFlags.None;
            if (player.ChaseKind == (byte)ActionKind.Pull) flags |= ChaseFlags.Pulling;
            if (player.ChaseKind == (byte)ActionKind.Vault) flags |= ChaseFlags.Vaulting;
            if (player.BreakShutter >= 0) flags |= ChaseFlags.Breaking;
            if (Stunned(player)) flags |= ChaseFlags.Stunned;
            // R1 (11/17): wind-up and recovery of a Killer swing.
            if (Swinging(player)) flags |= ChaseFlags.Swinging;
            return (byte)flags;
        }

        // GDD v2 section 8: pulls and vaults are loud; the survivor shows for their length and 1 s after.
        private bool ChaseForcesSignal(Player survivor) => survivor.InChaseAction || Active(survivor.ChaseSignalUntil);
    }
}
