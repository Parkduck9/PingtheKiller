using System.Collections.Generic;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.ServerCore
{
    public sealed partial class GameServer
    {
        private readonly List<KeyValuePair<Player, byte>> _skillRequests = new List<KeyValuePair<Player, byte>>();

        private static byte SkillCount(Role role) => role == Role.Killer ? SkillIds.KillerSkillCount : SkillIds.SurvivorSkillCount;

        // Requests are validated again when they execute, after this tick's movement.
        private void RequestSkill(Player player, byte skillId)
        {
            if (_sandbox) { RejectSkill(player, skillId, SkillRejectReason.Sandbox); return; }
            if (skillId >= SkillCount(player.Role)) { RejectSkill(player, skillId, SkillRejectReason.NotYourSkill); return; }
            _skillRequests.Add(new KeyValuePair<Player, byte>(player, skillId));
        }

        private void ExecuteSkills()
        {
            for (int i = 0; i < _skillRequests.Count; i++) TryUseSkill(_skillRequests[i].Key, _skillRequests[i].Value);
            _skillRequests.Clear();
        }

        private bool SkillReady(Player player) => _now >= player.SkillReadyAt - TimeEpsilon;

        private void TryUseSkill(Player caster, byte skillId)
        {
            SkillRejectReason reason;
            if (!_matchStarted) reason = SkillRejectReason.NotInMatch;
            else if (skillId != caster.Skill) reason = SkillRejectReason.NotYourSkill;
            // GDD v2 section 8: a stunned killer cannot use its skill (reported as NotNormal; no new reason on the wire).
            else if (caster.Status != PlayerStatus.Normal || Stunned(caster) || GettingUp(caster)) reason = SkillRejectReason.NotNormal;
            else if (!SkillReady(caster)) reason = SkillRejectReason.Cooldown;
            else if (caster.Role == Role.Killer ? UseKillerSkill(caster, out reason) : UseSurvivorSkill(caster, out reason))
            {
                caster.SkillReadyAt = _now + (caster.Role == Role.Killer
                    ? GameConstants.KillerSkillCooldownSeconds : GameConstants.SurvivorSkillCooldownSeconds);
                // A use answers the press: later requests of this tick (refused by the cooldown) send no rejection.
                caster.SkillAnsweredTick = _tick;
                WriteLog($"skill player={caster.Id} role={caster.Role} id={skillId}");
                return;
            }
            RejectSkill(caster, skillId, reason);
        }

        public const double SkillRejectLogSeconds = 1;

        // Q3: a refused C2S_UseSkill gets one S2C_SkillRejected, to the caster only, through its session's send queue on
        // the game-loop thread (like SendEffect). At most one answer (use or rejection) per player per tick, so a client
        // spamming C2S_UseSkill cannot make the server send more than one rejection per tick. Bots have no connection and
        // get none. Every reason is derived from state the caster's own stream already carries (HUD_SPEC 14.3).
        private void RejectSkill(Player player, byte skillId, SkillRejectReason reason)
        {
            if (player.IsBot || player.SkillAnsweredTick == _tick) return;
            if (player.ConnectionId == null || !_connections.TryGetValue(player.ConnectionId, out var binding)) return;
            player.SkillAnsweredTick = _tick;
            Send(binding.Connection, new S2C_SkillRejected { SkillId = skillId, Reason = (byte)reason });
            // The first rejection per player per second is logged; the others of that second are counted on the next line.
            if (_now - player.SkillRejectLoggedAt < SkillRejectLogSeconds - TimeEpsilon) { player.SkillRejectsUnlogged++; return; }
            string unlogged = player.SkillRejectsUnlogged > 0 ? " unlogged=" + player.SkillRejectsUnlogged : string.Empty;
            player.SkillRejectLoggedAt = _now;
            player.SkillRejectsUnlogged = 0;
            WriteLog($"skill rejected player={player.Id} id={skillId} reason={reason}{unlogged}");
        }

        private bool UseKillerSkill(Player killer, out SkillRejectReason reason)
        {
            reason = SkillRejectReason.NotYourSkill;
            switch (killer.Skill)
            {
                case SkillIds.PingScanner:
                {
                    Player target = null;
                    float best = float.MaxValue;
                    foreach (var survivor in _players)
                    {
                        if (!survivor.IsActiveSurvivor) continue;
                        float distance = DistanceSquared(killer.X, killer.Z, survivor.X, survivor.Z);
                        if (distance < best) { best = distance; target = survivor; }
                    }
                    // Only when no Survivor is active at all: never depends on where a (Silent) Survivor is.
                    if (target == null) { reason = SkillRejectReason.NoTarget; return false; }
                    target.ExposedUntil = Max(target.ExposedUntil, _now + SkillIds.PingScannerExposeSeconds);
                    SendEffect(killer, killer, SkillIds.PingScanner, killer.X, killer.Z, SkillIds.PingScannerExposeSeconds);
                    // Survivor-bound effects of killer skills never carry the killer's coordinates.
                    SendEffect(target, killer, SkillIds.PingScanner, 0, 0, SkillIds.PingScannerExposeSeconds);
                    return true;
                }
                case SkillIds.Overclock:
                    // R1 (11/17): the tail after the boost is RecoverySeconds at 60 % speed (GameServer.Simulation).
                    killer.OverclockUntil = _now + SkillIds.OverclockSeconds;
                    killer.RecoveryUntil = killer.OverclockUntil + SkillIds.OverclockRecoverySeconds;
                    SendEffect(killer, killer, SkillIds.Overclock, killer.X, killer.Z, SkillIds.OverclockSeconds);
                    return true;
                case SkillIds.FirewallBlock:
                {
                    // GDD v2 §3: locks the switch of the unopened exit with the most progress for 5 s. R1 (11/17): without
                    // one, it locks the unfinished terminal with the most progress for 8 s; it fails only when neither
                    // exists. Every Survivor sees exact progress; the Killer learns only which terminal the server picked.
                    int exit = FirewallTarget();
                    if (exit >= 0)
                    {
                        _exitLockedUntil[exit] = _now + SkillIds.FirewallBlockSeconds;
                        foreach (var player in _players)
                            SendEffect(player, killer, SkillIds.FirewallBlock, Map.Exits[exit].X, Map.Exits[exit].Z, SkillIds.FirewallBlockSeconds);
                        return true;
                    }
                    int terminal = FirewallTerminalTarget();
                    if (terminal < 0) { reason = SkillRejectReason.NoSwitchInProgress; return false; }
                    LockTerminal(terminal);
                    foreach (var player in _players)
                        SendEffect(player, killer, SkillIds.FirewallBlock, _terminals[terminal].X, _terminals[terminal].Z, SkillIds.FirewallTerminalSeconds);
                    return true;
                }
                case SkillIds.PacketTrace:
                {
                    // R1 (11/17): every active Survivor within PacketTraceRadius is forced Signal (the Ping exposure). It
                    // never fails, so a press says nothing about where a Silent Survivor is.
                    SendEffect(killer, killer, SkillIds.PacketTrace, killer.X, killer.Z, SkillIds.PacketTraceSeconds);
                    foreach (var survivor in _players)
                    {
                        if (!survivor.IsActiveSurvivor || !Within(survivor, killer.X, killer.Z, SkillIds.PacketTraceRadius)) continue;
                        survivor.ExposedUntil = Max(survivor.ExposedUntil, _now + SkillIds.PacketTraceSeconds);
                        // Survivor-bound effects of killer skills never carry the killer's coordinates.
                        SendEffect(survivor, killer, SkillIds.PacketTrace, 0, 0, SkillIds.PacketTraceSeconds);
                    }
                    return true;
                }
                default:
                    return false;
            }
        }

        private bool UseSurvivorSkill(Player caster, out SkillRejectReason reason)
        {
            reason = SkillRejectReason.NotYourSkill;
            switch (caster.Skill)
            {
                case SkillIds.DummyPacket:
                    CastDummy(caster);
                    SendSurvivorEffect(caster, SkillIds.DummyPacket, caster.X, caster.Z, SkillIds.DummyPacketSeconds);
                    return true;
                case SkillIds.StealthRelay:
                    foreach (var ally in _players)
                        if (ally.Role == Role.Survivor && ally.Status == PlayerStatus.Normal &&
                            Within(ally, caster.X, caster.Z, SkillIds.StealthRelayRadius))
                            ally.StealthUntil = Max(ally.StealthUntil, _now + SkillIds.StealthRelaySeconds);
                    SendSurvivorEffect(caster, SkillIds.StealthRelay, caster.X, caster.Z, SkillIds.StealthRelaySeconds);
                    return true;
                case SkillIds.TrafficOverload:
                    caster.ExposedUntil = Max(caster.ExposedUntil, _now + SkillIds.TrafficOverloadSeconds);
                    if (_killer != null)
                    {
                        _killer.SlowedUntil = Max(_killer.SlowedUntil, _now + SkillIds.TrafficOverloadSeconds);
                        // The killer finds the caster through its forced Signal; the effect carries no position and,
                        // like every killer-bound packet, no survivor Id.
                        SendEffect(_killer, 0, SkillIds.TrafficOverload, 0, 0, SkillIds.TrafficOverloadSeconds);
                    }
                    SendSurvivorEffect(caster, SkillIds.TrafficOverload, caster.X, caster.Z, SkillIds.TrafficOverloadSeconds);
                    return true;
                case SkillIds.EmergencyRestore:
                {
                    Player target = null;
                    float best = float.MaxValue;
                    foreach (var ally in _players)
                    {
                        if (ally == caster || ally.Role != Role.Survivor || ally.Status != PlayerStatus.Lagging ||
                            !Within(ally, caster.X, caster.Z, SkillIds.EmergencyRestoreRadius)) continue;
                        float distance = DistanceSquared(caster.X, caster.Z, ally.X, ally.Z);
                        if (distance < best) { best = distance; target = ally; }
                    }
                    if (target == null) { reason = SkillRejectReason.NoTarget; return false; }
                    SetStatus(target, PlayerStatus.Normal, "emergency restore");
                    SendSurvivorEffect(caster, SkillIds.EmergencyRestore, target.X, target.Z, 0);
                    return true;
                }
                case SkillIds.WhiteNoise:
                    CastWhiteNoise();
                    SendSurvivorEffect(caster, SkillIds.WhiteNoise, caster.X, caster.Z, SkillIds.WhiteNoiseSeconds);
                    return true;
                default:
                    return false;
            }
        }

        private static double Max(double a, double b) => a > b ? a : b;

        private void SendSurvivorEffect(Player caster, byte skillId, float x, float z, float duration)
        {
            // Survivor skills are never announced to the killer (TrafficOverload sends its own killer copy).
            foreach (var player in _players)
                if (player.Role == Role.Survivor) SendEffect(player, caster, skillId, x, z, duration);
        }

        private void SendEffect(Player recipient, Player caster, byte skillId, float x, float z, float duration) =>
            SendEffect(recipient, caster.Id, skillId, x, z, duration);

        private void SendEffect(Player recipient, int casterId, byte skillId, float x, float z, float duration)
        {
            if (recipient.ConnectionId == null || !_connections.TryGetValue(recipient.ConnectionId, out var binding)) return;
            Send(binding.Connection, new S2C_SkillEffect { CasterId = casterId, SkillId = skillId, X = x, Z = z, Duration = duration });
        }
    }
}
