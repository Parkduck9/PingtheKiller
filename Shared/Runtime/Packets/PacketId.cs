namespace PingTheKiller.Shared.Packets
{
    public enum PacketId : ushort
    {
        C2S_Join = 0x0001,
        S2C_JoinResult = 0x0002,
        C2S_MoveInput = 0x0003,
        C2S_UseSkill = 0x0004,
        S2C_WorldState = 0x0005,
        S2C_SkillEffect = 0x0006,
        S2C_GameResult = 0x0007,
        C2S_Ping = 0x0008,
        S2C_Pong = 0x0009,
        S2C_LobbyState = 0x000A,
        C2S_StartMatch = 0x000B,
        S2C_MatchStart = 0x000C,
        C2S_Interact = 0x000D,
        C2S_SetBots = 0x000E,
        C2S_SetRole = 0x000F,
        S2C_SelfState = 0x0010,
        // 0x0011-0x0013 (the v0.4 sync check: S2C_SyncCheck, C2S_SyncPress, S2C_SyncResult) are retired (11/17, R2).
        S2C_RttProbe = 0x0014,
        C2S_RttEcho = 0x0015,
        S2C_SkillRejected = 0x0016,
        // 11/17 (R2): one packet-routing key press of the hacking Survivor (0x0017-0x0018 are allocated to R1).
        C2S_HackInput = 0x0019
    }
}
