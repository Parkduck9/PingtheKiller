using System;
using System.Collections.Generic;
using UnityEngine;

namespace PingTheKiller.Client.World
{
    // One drawn character body for VisualSeparation: its id (any stable key; ties between coincident bodies break by it),
    // its ground position (x, z) and whether it must not move (the own avatar, the Killer model).
    public readonly struct SeparationBody
    {
        public readonly int Id;
        public readonly float X, Z;
        public readonly bool Pinned;

        public SeparationBody(int id, float x, float z, bool pinned)
        {
            Id = id; X = x; Z = z; Pinned = pinned;
        }
    }

    // Client-only draw offsets that keep character bodies from standing inside each other (play check 11/05, finding 4).
    // Pure: the offsets depend only on the current positions, so they change continuously as the bodies move. Nothing here
    // touches an authoritative position, a snapshot, input or anything sent to the server; CharacterViews adds the offsets to
    // the drawn transforms only.
    public static class VisualSeparation
    {
        // About one body width (CharacterLook.Width, 0.8 m).
        public const float MinDistance = CharacterLook.Width;
        // A few Jacobi passes settle groups of three or more; one pass settles a pair exactly.
        public const int Passes = 4;
        // Exponential ease of the drawn offset towards Resolve's target, per second (time constant 1/12 s, about 83 ms).
        public const float EaseRate = 12f;
        private const float Coincident = 1e-5f;

        // offsets[i] (x, z) for bodies[i]. A pair closer than minDistance is pushed apart along the line between them until
        // it is minDistance apart: half each, or all of it on the unpinned body when the other is pinned (two pinned bodies
        // stay). Exactly coincident bodies separate along +x, the lower id to -x. Each offset is at most minDistance long.
        // The result does not depend on the order of `bodies`.
        public static void Resolve(IReadOnlyList<SeparationBody> bodies, float minDistance, Vector2[] offsets)
        {
            int count = bodies?.Count ?? 0;
            if (offsets == null || offsets.Length < count) throw new ArgumentException("offsets must hold one entry per body", nameof(offsets));
            for (int i = 0; i < count; i++) offsets[i] = Vector2.zero;
            if (count < 2 || !(minDistance > 0)) return;
            Span<int> order = stackalloc int[count];
            Span<Vector2> delta = stackalloc Vector2[count];
            for (int i = 0; i < count; i++) order[i] = i;
            for (int i = 1; i < count; i++) // insertion sort by id: a fixed pair order, whatever the input order
                for (int j = i; j > 0 && bodies[order[j]].Id < bodies[order[j - 1]].Id; j--)
                    (order[j], order[j - 1]) = (order[j - 1], order[j]);
            for (int pass = 0; pass < Passes; pass++)
            {
                bool moved = false;
                for (int i = 0; i < count; i++) delta[i] = Vector2.zero;
                for (int i = 0; i < count; i++)
                    for (int j = i + 1; j < count; j++)
                    {
                        int a = order[i], b = order[j];
                        SeparationBody first = bodies[a], second = bodies[b];
                        if (first.Pinned && second.Pinned) continue;
                        Vector2 from = new Vector2(first.X, first.Z) + offsets[a], to = new Vector2(second.X, second.Z) + offsets[b];
                        Vector2 between = to - from;
                        float distance = between.magnitude;
                        if (float.IsNaN(distance) || distance >= minDistance) continue;
                        Vector2 direction = distance > Coincident ? between / distance : Vector2.right; // first has the lower id
                        float overlap = minDistance - distance;
                        float firstShare = first.Pinned ? 0 : second.Pinned ? 1 : .5f;
                        delta[a] -= direction * (overlap * firstShare);
                        delta[b] += direction * (overlap * (1 - firstShare));
                        moved = true;
                    }
                if (!moved) break;
                for (int i = 0; i < count; i++) offsets[i] += delta[i];
            }
            for (int i = 0; i < count; i++)
                if (offsets[i].magnitude > minDistance) offsets[i] = offsets[i].normalized * minDistance;
        }

        // The drawn offset eased towards the target over dt seconds. The result lies between current and target, so it is as
        // bounded as they are; dt <= 0 (or NaN) keeps current, a long gap reaches the target.
        public static Vector2 Ease(Vector2 current, Vector2 target, float dt, float rate = EaseRate)
        {
            if (!(dt > 0) || !(rate > 0)) return current;
            float t = 1 - Mathf.Exp(-rate * dt);
            return current + (target - current) * t;
        }
    }
}
