using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.UI;
using PingTheKiller.Client.World;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.Tests
{
    // U9 skill signatures: the skill -> signature table, the glyph mapping, what each viewer (above all the Killer) is
    // shown, the warm-up pool and the own-cast flash timing.
    public sealed class SkillSignatureTests
    {
        private const int Own = 3, Mina = 4, Rio = 5, KillerId = 1;
        private static MapPoint Exit(int i) => SharedMapPoints.Exits[i];
        private static MapPoint Switch(int i) => SharedMapPoints.Switches[i];

        private static SurvivorFrame Frame(PlayerStatus status = PlayerStatus.Normal, float x = 40, float z = 40, byte action = 0, params Mate[] mates)
            => new SurvivorFrame { OwnId = Own, Own = new OwnEntry { Status = status, X = x, Z = z }, Mates = mates, Names = id => "N" + id, ActionKind = action };

        private static Mate Peer(int id, float x, float z, PlayerStatus status = PlayerStatus.Normal) => new Mate { Id = id, X = x, Z = z, Status = status };

        // Every record a client can play for a cast: Killer own skills, the Survivor-bound Killer skills and every Survivor
        // skill as caster and as ally.
        private static List<FxRecord> CastRecords()
        {
            var records = new List<FxRecord>
            {
                SkillFeedback.ForKiller(KillerId, KillerId, SkillIds.PingScanner, 7, 9, 1.5f),
                SkillFeedback.ForKiller(KillerId, KillerId, SkillIds.Overclock, 7, 9, 3),
                SkillFeedback.ForKiller(KillerId, KillerId, SkillIds.FirewallBlock, Exit(0).X, Exit(0).Z, 5),
                SkillFeedback.ForSurvivor(Frame(), KillerId, SkillIds.PingScanner, 0, 0, 1.5f),
                SkillFeedback.ForSurvivor(Frame(action: 1), KillerId, SkillIds.FirewallBlock, Exit(1).X, Exit(1).Z, 5),
            };
            foreach (byte skill in new[] { SkillIds.DummyPacket, SkillIds.StealthRelay, SkillIds.TrafficOverload, SkillIds.EmergencyRestore, SkillIds.WhiteNoise })
            {
                records.Add(SkillFeedback.ForSurvivor(Frame(mates: Peer(Mina, 30, 33, PlayerStatus.Lagging)), Own, skill, 30, 33, 2));
                records.Add(SkillFeedback.ForSurvivor(Frame(PlayerStatus.Normal, 40, 40, 0, Peer(Mina, 20, 21), Peer(Rio, 20.2f, 21, PlayerStatus.Lagging)), Mina, skill, 20, 21, 2));
            }
            return records;
        }

        // ---- The table ----

        [Test]
        public void AllNineSkillsHaveDistinctSignatures()
        {
            var all = new List<(bool, byte)>(SkillSignature.All);
            Assert.That(all.Count, Is.EqualTo(9));
            var shapes = new HashSet<FxShape>();
            var motions = new HashSet<FxMotion>();
            foreach ((bool killer, byte skill) in all)
            {
                Signature s = SkillSignature.For(killer, skill);
                Assert.That(s.Shape, Is.Not.EqualTo(FxShape.None), (killer ? "K" : "S") + skill);
                Assert.That(s.Motion, Is.Not.EqualTo(FxMotion.None), (killer ? "K" : "S") + skill);
                shapes.Add(s.Shape); motions.Add(s.Motion);
            }
            Assert.That(shapes.Count, Is.EqualTo(9), "one shape per skill");
            Assert.That(motions.Count, Is.EqualTo(9), "one motion per skill");
            for (int i = 0; i < all.Count; i++)
                for (int j = i + 1; j < all.Count; j++)
                {
                    Signature a = SkillSignature.For(all[i].Item1, all[i].Item2), b = SkillSignature.For(all[j].Item1, all[j].Item2);
                    int differ = (a.Shape != b.Shape ? 1 : 0) + (a.Motion != b.Motion ? 1 : 0) + (a.Tint != b.Tint ? 1 : 0);
                    Assert.That(differ, Is.GreaterThanOrEqualTo(2), all[i] + " vs " + all[j]);
                }
            Assert.That(SkillSignature.For(true, SkillIds.PacketAfterimage).Played, Is.False, "the passive skill has no effect to play");
            Assert.That(SkillSignature.For(true, 9).Shape, Is.EqualTo(FxShape.None), "unknown id");
            Assert.That(SkillSignature.For(false, SkillIds.WhiteNoise).Tint, Is.EqualTo(FxTint.Text), "White noise: nothing transmits at the caster");
        }

        [Test]
        public void EveryWorldEffectLastsAtMostTwoSecondsOrTheEffect()
        {
            foreach (FxRecord r in CastRecords())
            {
                FxPlan plan = SkillSignature.Plan(r, false);
                if (!plan.Shown) continue;
                if (plan.FollowCamera) Assert.That(plan.Seconds, Is.EqualTo(3f), "Overclock: exactly the effect");
                else Assert.That(plan.Seconds, Is.GreaterThan(0f).And.LessThanOrEqualTo(2f), plan.Shape.ToString());
            }
        }

        // ---- Glyphs ----

        [Test]
        public void EachSkillHasItsOwnGlyph()
        {
            var masks = new List<float[]>();
            foreach ((bool killer, byte skill) in SkillSignature.All)
            {
                SkillGlyph.Prim[] glyph = SkillGlyph.For(killer, skill);
                Assert.That(glyph.Length, Is.GreaterThan(0), (killer ? "K" : "S") + skill);
                float[] mask = SkillGlyph.Rasterize(glyph, 32, 2);
                float ink = 0; foreach (float v in mask) ink += v;
                Assert.That(ink, Is.GreaterThan(10f), "the glyph draws something");
                foreach (float[] other in masks)
                {
                    float diff = 0;
                    for (int i = 0; i < mask.Length; i++) diff += Mathf.Abs(mask[i] - other[i]);
                    Assert.That(diff, Is.GreaterThan(10f), "glyphs differ");
                }
                masks.Add(mask);
            }
            Assert.That(SkillGlyph.For(true, 0), Is.Not.SameAs(SkillGlyph.For(false, 0)), "role-local ids map to different glyphs");
            Assert.That(SkillGlyph.For(false, 9).Length, Is.EqualTo(0));
        }

        [Test]
        public void TheHudRingAndTheWorldGlyphUseTheSameSource()
        {
            // SkillRing.Icon paints SkillGlyph.For(!SurvivorSkills, skill): the HUD_SPEC 8 icon descriptions hold.
            Assert.That(SkillGlyph.For(false, SkillIds.WhiteNoise).Length, Is.EqualTo(5), "five scattered dots");
            Assert.That(SkillGlyph.For(false, SkillIds.DummyPacket).Length, Is.EqualTo(8), "a dashed-outline dot");
            Assert.That(SkillGlyph.For(true, SkillIds.FirewallBlock)[0].Kind, Is.EqualTo(SkillGlyph.Kind.Arc), "a ring with a bar");
            int arcs = 0; foreach (SkillGlyph.Prim p in SkillGlyph.For(true, SkillIds.PingScanner)) if (p.Kind == SkillGlyph.Kind.Arc) arcs++;
            Assert.That(arcs, Is.EqualTo(3), "three concentric arcs");
        }

        // ---- Viewers and safety ----

        [Test]
        public void TheKillerIsNeverShownASurvivorSkill()
        {
            foreach (byte skill in new[] { SkillIds.DummyPacket, SkillIds.StealthRelay, SkillIds.TrafficOverload, SkillIds.EmergencyRestore, SkillIds.WhiteNoise })
            {
                foreach (int caster in new[] { Mina, 0, 77 })
                {
                    FxRecord r = SkillFeedback.ForKiller(KillerId, caster, skill, 12, 14, 2);
                    FxPlan plan = SkillSignature.Plan(r, false);
                    Assert.That(plan.Shown, Is.False, "skill " + skill + " caster " + caster);
                    Assert.That(SkillSignature.IsOwnCast(r), Is.False);
                }
            }
            // The Killer-bound Traffic overload has no position: a line and a sound, never a world effect or glyph.
            FxRecord slowed = SkillFeedback.ForKiller(KillerId, 0, SkillIds.TrafficOverload, 0, 0, 2);
            Assert.That(slowed.Viewer, Is.EqualTo(FxViewer.Killer));
            Assert.That(SkillSignature.Plan(slowed, false).Shown, Is.False);
            // Killer own skills: the own point or the static switch, and no glyph above the first-person own point.
            FxPlan ping = SkillSignature.Plan(SkillFeedback.ForKiller(KillerId, KillerId, SkillIds.PingScanner, 7, 9, 1.5f), false);
            Assert.That(ping.Shape, Is.EqualTo(FxShape.PulseRings)); Assert.That(ping.Glyph, Is.False); Assert.That(ping.Tint, Is.EqualTo(FxTint.Text));
            FxPlan overclock = SkillSignature.Plan(SkillFeedback.ForKiller(KillerId, KillerId, SkillIds.Overclock, 7, 9, 3), false);
            Assert.That(overclock.Shape, Is.EqualTo(FxShape.Streaks)); Assert.That(overclock.FollowCamera, Is.True); Assert.That(overclock.Glyph, Is.False);
            FxPlan wall = SkillSignature.Plan(SkillFeedback.ForKiller(KillerId, KillerId, SkillIds.FirewallBlock, Exit(1).X, Exit(1).Z, 5), false);
            Assert.That(wall.Shape, Is.EqualTo(FxShape.HexWall)); Assert.That(wall.Glyph, Is.True); Assert.That(wall.Tint, Is.EqualTo(FxTint.Text), "Killer view: text");
            Assert.That(SkillSignature.Plan(SkillFeedback.RecoverCue(), false).Shown, Is.False, "the Recovering cue draws nothing");
        }

        [Test]
        public void SurvivorViewsKeepTheirMeaningColours()
        {
            FxPlan scanned = SkillSignature.Plan(SkillFeedback.ForSurvivor(Frame(), KillerId, SkillIds.PingScanner, 0, 0, 1.5f), false);
            Assert.That(scanned.Shape, Is.EqualTo(FxShape.PulseRings)); Assert.That(scanned.Inward, Is.True); Assert.That(scanned.Tint, Is.EqualTo(FxTint.Signal));
            FxPlan locked = SkillSignature.Plan(SkillFeedback.ForSurvivor(Frame(action: 1), KillerId, SkillIds.FirewallBlock, Exit(0).X, Exit(0).Z, 5), false);
            Assert.That(locked.Tint, Is.EqualTo(FxTint.Danger));
            Assert.That(SkillSignature.Plan(SkillFeedback.ForSurvivor(Frame(), KillerId, SkillIds.FirewallBlock, 500, 500, 5), false).Shown, Is.False, "no exit near: nothing placed");
            Assert.That(SkillSignature.Plan(SkillFeedback.ForSurvivor(Frame(), Own, SkillIds.StealthRelay, 30, 30, 2), false).Tint, Is.EqualTo(FxTint.Ally));
            Assert.That(SkillSignature.Plan(SkillFeedback.ForSurvivor(Frame(), Own, SkillIds.DummyPacket, 30, 30, 3), false).Tint, Is.EqualTo(FxTint.Signal));
            Assert.That(SkillSignature.Plan(SkillFeedback.ForSurvivor(Frame(), Own, SkillIds.WhiteNoise, 30, 30, 1.5f), false).Tint, Is.EqualTo(FxTint.Text));
            // Spectators keep the world effect (14.1 Viewers).
            FxRecord spectator = SkillFeedback.ForSpectator(SkillFeedback.ForSurvivor(Frame(mates: Peer(Mina, 30, 30)), Mina, SkillIds.DummyPacket, 30, 30, 3));
            Assert.That(SkillSignature.Plan(spectator, false).Shape, Is.EqualTo(FxShape.WireDouble));
        }

        [Test]
        public void ReducedMotionIsStillAndDropsTheSweepAndStreaks()
        {
            Assert.That(SkillSignature.Plan(SkillFeedback.ForKiller(KillerId, KillerId, SkillIds.PingScanner, 7, 9, 1.5f), true).Shown, Is.False);
            Assert.That(SkillSignature.Plan(SkillFeedback.ForKiller(KillerId, KillerId, SkillIds.Overclock, 7, 9, 3), true).Shown, Is.False);
            FxPlan dummy = SkillSignature.Plan(SkillFeedback.ForSurvivor(Frame(), Own, SkillIds.DummyPacket, 30, 30, 3), true);
            Assert.That(dummy.Shape, Is.EqualTo(FxShape.WireDouble)); Assert.That(dummy.Still, Is.True);
        }

        [Test]
        public void SkillFxDrawsOnlyAtTheRecordsOwnPoint()
        {
            var host = new GameObject("skill signature anchor test");
            try
            {
                var fx = host.AddComponent<SkillFx>();
                fx.Warm(SkillFxArt.Load());
                var own = new Vector3(5, 0, 6);
                double now = 0;
                foreach (FxRecord r in CastRecords())
                {
                    FxPlan plan = SkillSignature.Plan(r, false);
                    fx.Play(r, own, now); now += 3;
                    if (!plan.Shown) continue;
                    SkillFeedback.VisualPoint(r, own.x, own.z, out float x, out float z);
                    Vector3 expected = plan.FollowCamera ? own : new Vector3(x, 0, z);
                    Assert.That(fx.LastPoint, Is.EqualTo(expected), plan.Shape.ToString());
                    if (r.KillerSkill && r.SkillId == SkillIds.FirewallBlock)
                        Assert.That(new Vector2(x, z), Is.EqualTo(new Vector2(r.Viewer == FxViewer.Caster ? Switch(0).X : Switch(1).X, r.Viewer == FxViewer.Caster ? Switch(0).Z : Switch(1).Z)), "the static switch point");
                    if (r.KillerSkill && r.Viewer == FxViewer.Affected && r.SkillId == SkillIds.PingScanner)
                        Assert.That(fx.LastPoint, Is.EqualTo(own), "the scanned Survivor's own point, never the effect's 0, 0");
                    if (fx.LastGlyph)
                    {
                        Assert.That(fx.LastGlyphPoint.x, Is.EqualTo(expected.x).Within(1e-4)); Assert.That(fx.LastGlyphPoint.z, Is.EqualTo(expected.z).Within(1e-4));
                    }
                    fx.Tick(now);
                }
            }
            finally { Object.DestroyImmediate(host); }
        }

        // ---- Pool ----

        [Test]
        public void WarmBuildsEveryRigAndThePlaysInstantiateNothing()
        {
            var host = new GameObject("skill signature pool test");
            try
            {
                var fx = host.AddComponent<SkillFx>();
                SkillFxArt art = SkillFxArt.Load();
                fx.Warm(art);
                foreach ((bool killer, byte skill) in SkillSignature.All)
                {
                    Signature s = SkillSignature.For(killer, skill);
                    if (s.Played) Assert.That(fx.RigCount(s.Shape), Is.GreaterThanOrEqualTo(1), s.Shape.ToString());
                }
                if (art != null && art.Glyph != null) Assert.That(fx.GlyphTextureCount, Is.EqualTo(9), "every glyph rasterized at warm-up");
                int created = fx.Created, children = CountAll(host.transform);
                int meshCount = Resources.FindObjectsOfTypeAll<Mesh>().Length, materialCount = Resources.FindObjectsOfTypeAll<Material>().Length;
                int textureCount = Resources.FindObjectsOfTypeAll<Texture2D>().Length;
                double now = 0;
                for (int round = 0; round < 3; round++)
                    foreach (FxRecord r in CastRecords()) { fx.Play(r, new Vector3(5, 0, 5), now); now += .07; fx.Tick(now); }
                fx.Reduced = true;
                foreach (FxRecord r in CastRecords()) fx.Play(r, new Vector3(5, 0, 5), now);
                Assert.That(fx.Created, Is.EqualTo(created), "no GameObject per effect after warm-up");
                Assert.That(CountAll(host.transform), Is.EqualTo(children));
                Assert.That(Resources.FindObjectsOfTypeAll<Mesh>().Length, Is.EqualTo(meshCount), "no mesh per effect");
                Assert.That(Resources.FindObjectsOfTypeAll<Material>().Length, Is.EqualTo(materialCount), "no material per effect");
                Assert.That(Resources.FindObjectsOfTypeAll<Texture2D>().Length, Is.EqualTo(textureCount), "no texture per effect");
                fx.Tick(now + 3.5);
                Assert.That(fx.BusyRigs + fx.BusyGlyphs, Is.EqualTo(0), "everything ends at its time");
                Assert.That(fx.WarmMilliseconds, Is.GreaterThan(0));
            }
            finally { Object.DestroyImmediate(host); }
        }

        private static int CountAll(Transform root)
        {
            int n = 1;
            foreach (Transform child in root) n += CountAll(child);
            return n;
        }

        // ---- Own-cast flash ----

        [Test]
        public void TheOwnCastFlashShowsForSixTenthsOfASecond()
        {
            var timer = new CastFlashTimer();
            Assert.That(timer.Visible(0), Is.False);
            timer.Show(false, SkillIds.DummyPacket, false, 10);
            Assert.That(timer.Visible(10), Is.True);
            Assert.That(timer.Visible(10.59), Is.True);
            Assert.That(timer.Visible(10.6), Is.False);
            Assert.That(timer.Offset(10), Is.EqualTo(CastFlashTimer.RiseUnits).Within(1e-4), "starts 4 u low");
            Assert.That(timer.Offset(10.05), Is.GreaterThan(0f).And.LessThan(CastFlashTimer.RiseUnits));
            Assert.That(timer.Offset(10 + CastFlashTimer.RiseSeconds), Is.EqualTo(0f), "in place within 0.1 s (SLOP 5: <= 150 ms)");
            timer.Show(true, SkillIds.FirewallBlock, true, 20);
            Assert.That(timer.Offset(20), Is.EqualTo(0f), "reduced motion: still");
            Assert.That(timer.Visible(20.3), Is.True);
            Assert.That(timer.KillerSkill, Is.True); Assert.That(timer.SkillId, Is.EqualTo(SkillIds.FirewallBlock));
            timer.Reset();
            Assert.That(timer.Visible(20.3), Is.False);
        }

        [Test]
        public void OnlyTheCastersOwnEffectFlashes()
        {
            Assert.That(SkillSignature.IsOwnCast(SkillFeedback.ForSurvivor(Frame(), Own, SkillIds.DummyPacket, 30, 30, 3)), Is.True);
            Assert.That(SkillSignature.IsOwnCast(SkillFeedback.ForSurvivor(Frame(), Own, SkillIds.StealthRelay, 30, 30, 2)), Is.True);
            Assert.That(SkillSignature.IsOwnCast(SkillFeedback.ForSurvivor(Frame(mates: Peer(Rio, 30, 30, PlayerStatus.Lagging)), Own, SkillIds.EmergencyRestore, 30, 30, 0)), Is.True);
            Assert.That(SkillSignature.IsOwnCast(SkillFeedback.ForKiller(KillerId, KillerId, SkillIds.Overclock, 7, 9, 3)), Is.True);
            Assert.That(SkillSignature.IsOwnCast(SkillFeedback.ForSurvivor(Frame(mates: Peer(Mina, 20, 20)), Mina, SkillIds.DummyPacket, 20, 20, 3)), Is.False, "ally");
            Assert.That(SkillSignature.IsOwnCast(SkillFeedback.RelayResolved(Frame(mates: Peer(Mina, 31, 30)), Own, 30, 30, 2)), Is.False, "the relay count line is not a second cast");
            Assert.That(SkillSignature.IsOwnCast(SkillFeedback.RecoverCue()), Is.False, "Recovering is not a cast");
            Assert.That(SkillSignature.IsOwnCast(SkillFeedback.ForSurvivor(Frame(), KillerId, SkillIds.PingScanner, 0, 0, 1.5f)), Is.False, "affected");
        }

        [Test]
        public void TheFlashElementShowsTheGlyphOnly()
        {
            var flash = new SkillCastFlash(true, 16);
            flash.Show(false, SkillIds.TrafficOverload, false, 5);
            Assert.That(flash.ShownSkill, Is.EqualTo("S" + SkillIds.TrafficOverload));
            Assert.That(flash.Query<Label>().ToList().Count, Is.EqualTo(0), "attempt 2 (F7): no name; the skill block names the skill");
            flash.Tick(5.61);
            Assert.That(flash.ShownSkill, Is.Null, "removed after 0.6 s");
            flash.Show(true, SkillIds.PingScanner, false, 6);
            Assert.That(flash.ShownSkill, Is.EqualTo("K" + SkillIds.PingScanner));
        }

        [Test]
        public void ThePingPulseLastsOneSecondAndTheGlyphSitsLower()
        {
            FxPlan ping = SkillSignature.Plan(SkillFeedback.ForKiller(KillerId, KillerId, SkillIds.PingScanner, 7, 9, 1.5f), false);
            Assert.That(ping.Seconds, Is.EqualTo(1f), "F1: 1.0 s");
            Assert.That(SkillFx.BandBottom, Is.GreaterThanOrEqualTo(.5f)); Assert.That(SkillFx.BandTop, Is.LessThanOrEqualTo(.9f));
            Assert.That(SkillFx.BandTop - SkillFx.BandBottom, Is.InRange(.3f, .4f), "a 0.3-0.4 m band");
            Assert.That(SkillFx.PulseStart, Is.EqualTo(2f));
            Assert.That(SkillFx.PulseStagger * 2 + SkillFx.PulseSeconds, Is.LessThanOrEqualTo(ping.Seconds + 1e-4f), "the last band ends in time");
            Assert.That(SkillSignature.GlyphHeight + SkillSignature.GlyphRise, Is.LessThanOrEqualTo(2.05f), "F3");
        }

        [Test]
        public void SkillWhenAbleIsADevFlag()
        {
            Assert.That(PingTheKiller.Client.Dev.DevArgs.Parse(new[] { "-ptk-skill-at", "3", "-ptk-skill-when-able" }).SkillWhenAble, Is.True);
            Assert.That(PingTheKiller.Client.Dev.DevArgs.Parse(new[] { "-ptk-skill-at", "3" }).SkillWhenAble, Is.False);
        }
    }
}
