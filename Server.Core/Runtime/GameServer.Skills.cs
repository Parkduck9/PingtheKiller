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
            if (_sandbox || skillId >= SkillCount(player.Role)) return;
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
            if (!_matchStarted || skillId != caster.Skill || caster.Status != PlayerStatus.Normal || !SkillReady(caster)) return;
            bool used = caster.Role == Role.Killer ? UseKillerSkill(caster) : UseSurvivorSkill(caster);
            if (!used) return;
            caster.SkillReadyAt = _now + (caster.Role == Role.Killer
                ? GameConstants.KillerSkillCooldownSeconds : GameConstants.SurvivorSkillCooldownSeconds);
            WriteLog($"skill player={caster.Id} role={caster.Role} id={skillId}");
        }

        private bool UseKillerSkill(Player killer)
        {
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
                    if (target == null) return false;
                    target.ExposedUntil = Max(target.ExposedUntil, _now + SkillIds.PingScannerExposeSeconds);
                    SendEffect(killer, killer, SkillIds.PingScanner, killer.X, killer.Z, SkillIds.PingScannerExposeSeconds);
                    // Survivor-bound effects of killer skills never carry the killer's coordinates.
                    SendEffect(target, killer, SkillIds.PingScanner, 0, 0, SkillIds.PingScannerExposeSeconds);
                    return true;
                }
                case SkillIds.Overclock:
                    killer.OverclockUntil = _now + SkillIds.OverclockSeconds;
                    killer.RecoveryUntil = killer.OverclockUntil + SkillIds.OverclockRecoverySeconds;
                    SendEffect(killer, killer, SkillIds.Overclock, killer.X, killer.Z, SkillIds.OverclockSeconds);
                    return true;
                case SkillIds.FirewallBlock:
                    if (!_exitOpen) return false;
                    _exitLockedUntil = _now + SkillIds.FirewallBlockSeconds;
                    foreach (var player in _players)
                        SendEffect(player, killer, SkillIds.FirewallBlock, Map.Exit.X, Map.Exit.Z, SkillIds.FirewallBlockSeconds);
                    return true;
                default:
                    // PacketAfterimage is passive and cannot be activated.
                    return false;
            }
        }

        private bool UseSurvivorSkill(Player caster)
        {
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
                    if (target == null) return false;
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
