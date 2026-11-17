using System;
using System.Collections.Generic;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Shared.Game
{
    // GDD v2 §8 (protocol v0.7): a roll-down shutter is Up (passable), Down (blocks movement, not sight) or Broken
    // (passable, permanent for the match). Sent in S2C_WorldState, one byte per shutter, the same to every recipient.
    public enum ShutterState : byte { Up = 0, Down = 1, Broken = 2 }

    // GDD v2 §8 (protocol v0.7): what a player is doing with a shutter or rail, for animation. PlayerState.Chase.
    [Flags]
    // R1 (11/17): Swinging = 16 is set on the Killer during a swing's wind-up and recovery.
    public enum ChaseFlags : byte { None = 0, Pulling = 1, Vaulting = 2, Breaking = 4, Stunned = 8, Swinging = 16 }

    // A thin axis-aligned prop box (a shutter across an alley or a low rail). It blocks movement only, never sight.
    // The long side runs along X when AlongX, otherwise along Z; it is crossed along (CrossX, CrossZ).
    public readonly struct ChaseProp
    {
        public ChaseProp(byte id, float minX, float minZ, float maxX, float maxZ)
        {
            Id = id; MinX = minX; MinZ = minZ; MaxX = maxX; MaxZ = maxZ;
        }

        public byte Id { get; }
        public float MinX { get; }
        public float MinZ { get; }
        public float MaxX { get; }
        public float MaxZ { get; }
        public float CenterX => (MinX + MaxX) * 0.5f;
        public float CenterZ => (MinZ + MaxZ) * 0.5f;
        public bool AlongX => MaxX - MinX >= MaxZ - MinZ;
        public float CrossX => AlongX ? 0f : 1f;
        public float CrossZ => AlongX ? 1f : 0f;
        public float HalfLength => AlongX ? (MaxX - MinX) * 0.5f : (MaxZ - MinZ) * 0.5f;
        public float HalfThickness => AlongX ? (MaxZ - MinZ) * 0.5f : (MaxX - MinX) * 0.5f;

        // Distance from a point to the box; 0 inside.
        public float Distance(float x, float z)
        {
            float dx = Math.Max(Math.Max(MinX - x, 0f), x - MaxX);
            float dz = Math.Max(Math.Max(MinZ - z, 0f), z - MaxZ);
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        // The same strict test as the server's walls: a circle merely touching the box does not intersect it.
        public bool IntersectsCircle(float x, float z, float radius)
        {
            float dx = x - Math.Max(MinX, Math.Min(MaxX, x));
            float dz = z - Math.Max(MinZ, Math.Min(MaxZ, z));
            return dx * dx + dz * dz < radius * radius - 0.000001f;
        }

        // Signed offset of a point from the prop's centre line along the crossing axis (positive on the +Cross side).
        public float Side(float x, float z) => (x - CenterX) * CrossX + (z - CenterZ) * CrossZ;
    }

    // GDD v2 §8: the static shutter and rail boxes of FixedMap v2.1. Server and client read these same values.
    // A shutter spans an alley wall to wall; a rail's vault point is its centre and it is crossed along its Cross axis.
    // The west road (x < 12, z < 39), the south edge street and the centre crossroads stay free: they are test lanes.
    public static class WorldChaseProps
    {
        public const int ShutterCount = 10;
        public const int RailCount = 8;

        public static IReadOnlyList<ChaseProp> Shutters { get; } = Array.AsReadOnly(new[]
        {
            new ChaseProp(0, 19f, 19.35f, 23f, 19.65f),     // SW alley, north mouth
            new ChaseProp(1, 67.85f, 56f, 68.15f, 59f),     // east street to the NE loops
            new ChaseProp(2, 27.85f, 21f, 28.15f, 24f),     // south band E-W alley under the single-box loop
            new ChaseProp(3, 39f, 18.35f, 42f, 18.65f),     // south alley beside the big middle block
            new ChaseProp(4, 47.35f, 12f, 47.65f, 16f),     // south E-W alley
            new ChaseProp(5, 18f, 33.35f, 21f, 33.65f),     // west loop alley
            new ChaseProp(6, 56f, 43.85f, 60f, 44.15f),     // east middle alley
            new ChaseProp(7, 13f, 65.85f, 17f, 66.15f),     // NW alley
            new ChaseProp(8, 52f, 66.85f, 56f, 67.15f),     // north alley
            new ChaseProp(9, 63f, 61.85f, 67f, 62.15f)      // NE alley
        });

        public static IReadOnlyList<ChaseProp> Rails { get; } = Array.AsReadOnly(new[]
        {
            new ChaseProp(0, 5f, 56.85f, 8f, 57.15f),       // west plaza, north edge
            new ChaseProp(1, 6.85f, 52f, 7.15f, 55f),       // west plaza, north-west
            new ChaseProp(2, 67f, 23.85f, 70f, 24.15f),     // east plaza, south edge
            new ChaseProp(3, 70.85f, 28f, 71.15f, 31f),     // east plaza, east edge
            new ChaseProp(4, 55.85f, 32f, 56.15f, 35f),     // east plaza, west edge
            new ChaseProp(5, 36.35f, 25f, 36.65f, 28f),     // loop corner east of the single-box loop
            new ChaseProp(6, 39f, 55.85f, 42f, 56.15f),     // north street
            new ChaseProp(7, 56f, 19.85f, 59f, 20.15f)       // south of the east plaza
        });

        public static bool Has(PlayerState player, ChaseFlags flag) => (player.Chase & (byte)flag) != 0;

        // The state of shutter id in a WorldState; Up when the snapshot carries no shutter list.
        public static ShutterState ShutterOf(S2C_WorldState world, int id)
        {
            if (world?.Shutters == null || id < 0 || id >= world.Shutters.Count) return ShutterState.Up;
            return (ShutterState)world.Shutters[id];
        }
    }
}
