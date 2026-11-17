using NUnit.Framework;
using PingTheKiller.Client.World;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.Tests
{
    public sealed class InteractInputTests
    {
        [Test]
        public void PressSendsOneAndHoldingSendsNothingMore()
        {
            var input = new InteractInput();
            Assert.That(input.TryCreate(false, out _), Is.False, "idle sends nothing");
            Assert.That(input.TryCreate(true, out C2S_Interact press), Is.True);
            Assert.That(press.Active, Is.EqualTo(1));
            for (int i = 0; i < 100; i++) Assert.That(input.TryCreate(true, out _), Is.False, "no repeats while held");
            Assert.That(input.TryCreate(false, out C2S_Interact release), Is.True);
            Assert.That(release.Active, Is.EqualTo(0));
            Assert.That(input.TryCreate(false, out _), Is.False, "release is sent once");
        }

        [Test]
        public void ForcedReleaseSendsZeroOnlyAfterAPress()
        {
            var input = new InteractInput();
            Assert.That(input.TryRelease(out _), Is.False);
            input.TryCreate(true, out _);
            Assert.That(input.TryRelease(out C2S_Interact release), Is.True);
            Assert.That(release.Active, Is.EqualTo(0));
            Assert.That(input.TryRelease(out _), Is.False);
            Assert.That(input.TryCreate(true, out _), Is.True, "a new press after a forced release is sent again");
        }

        [Test]
        public void ResetForgetsWithoutSending()
        {
            var input = new InteractInput();
            input.TryCreate(true, out _);
            input.Reset();
            Assert.That(input.Held, Is.False);
            Assert.That(input.TryRelease(out _), Is.False);
        }
    }

    public sealed class SkillInputTests
    {
        [Test]
        public void ActiveSkillIsSentOnPressWhileNormal()
        {
            Assert.That(SkillInput.TryCreate(true, Role.Survivor, 3, PlayerStatus.Normal, out C2S_UseSkill packet), Is.True);
            Assert.That(packet.SkillId, Is.EqualTo(3));
            Assert.That(SkillInput.TryCreate(true, Role.Killer, 0, PlayerStatus.Normal, out _), Is.True);
            Assert.That(SkillInput.TryCreate(false, Role.Killer, 0, PlayerStatus.Normal, out _), Is.False, "press edge only");
        }

        [Test]
        public void PacketTraceIsSentSinceR1()
        {
            // R1 (11/17): the former passive Packet afterimage (id 2) is the active Packet trace.
            Assert.That(SkillInput.TryCreate(true, Role.Killer, 2, PlayerStatus.Normal, out C2S_UseSkill packet), Is.True);
            Assert.That(packet.SkillId, Is.EqualTo(2));
        }

        [TestCase(PlayerStatus.Lagging)]
        [TestCase(PlayerStatus.Escaped)]
        [TestCase(PlayerStatus.Eliminated)]
        public void NoSkillUnlessNormal(PlayerStatus status)
        {
            Assert.That(SkillInput.TryCreate(true, Role.Survivor, 0, status, out _), Is.False);
        }
    }

    public sealed class MatchEndInputTests
    {
        [Test]
        public void FinalZeroUsesTheNextSeqAndStopsTheHold()
        {
            var movement = new MovementInput();
            Assert.That(movement.TryCreate(true, 0, 1, 0, MovementInput.Interval, out C2S_MoveInput held), Is.True);
            C2S_MoveInput final = movement.Final(90);
            Assert.That(final.Seq, Is.EqualTo(held.Seq + 1));
            Assert.That(final.DirX, Is.EqualTo(0f));
            Assert.That(final.DirZ, Is.EqualTo(0f));
            Assert.That(final.Yaw, Is.EqualTo(90f));
            Assert.That(movement.TryCreate(false, 0, 0, 0, MovementInput.Interval, out _), Is.False, "no backup zeros after the final zero");
        }
    }

    public sealed class SharedMapTests
    {
        [Test]
        public void LowObstaclesBlockMovementAndTheLowCameraButNeverSight()
        {
            Assert.That(MapGeometry.ObstacleHeight,Is.EqualTo(1.2f));
            var map=SharedMap.Map;
            Assert.That(map.Exits.Count,Is.EqualTo(2));
            Assert.That(SharedMap.ExitAt(1).x,Is.EqualTo(map.Exits[1].X));
            foreach(var o in map.Obstacles)
            {
                float z=(o.MinZ+o.MaxZ)*.5f;
                Assert.That(map.CanStand((o.MinX+o.MaxX)*.5f,z),Is.False);
                Assert.That(MapGeometry.SegmentBlocked(map,o.MinX-1,z,o.MaxX+1,z),Is.False);
                Assert.That(MapGeometry.SphereCast(map,new Vector3(o.MinX-1,.6f,z),Vector3.right,4,.2f),Is.EqualTo(.8f).Within(.0001f));
                Assert.That(MapGeometry.SphereCast(map,new Vector3(o.MinX-1,1.6f,z),Vector3.right,4,.2f),Is.EqualTo(4));
            }
        }
        [Test]
        public void WallBoxesAreFixedMapMinMaxBoxes()
        {
            FixedMap map = SharedMap.Map;
            Assert.That(map.Walls.Count, Is.GreaterThan(4));
            foreach (Wall wall in map.Walls)
            {
                SharedMap.WallBox(wall, out Vector3 center, out Vector3 size);
                Assert.That(center.x - size.x / 2, Is.EqualTo(wall.MinX).Within(1e-5f));
                Assert.That(center.x + size.x / 2, Is.EqualTo(wall.MaxX).Within(1e-5f));
                Assert.That(center.z - size.z / 2, Is.EqualTo(wall.MinZ).Within(1e-5f));
                Assert.That(center.z + size.z / 2, Is.EqualTo(wall.MaxZ).Within(1e-5f));
            }
            Assert.That(SharedMap.Center, Is.EqualTo(new Vector3(FixedMap.Size / 2, 0, FixedMap.Size / 2)));
            Assert.That(SharedMap.Exit.x, Is.EqualTo(map.Exit.X));
            Assert.That(SharedMap.Exit.z, Is.EqualTo(map.Exit.Z));
        }
    }
}
