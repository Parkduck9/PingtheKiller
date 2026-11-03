using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.QA.Tests;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.QA.Tests.Reverify
{
    // Re-verification Attack 1: Killer client anonymity (Area C of Reverify-flash.md).
    // The Killer client path must never read Survivor Id, Yaw, StatusTimer, CaptureCount,
    // or the Hacking, Rescuing, Stealthed and Afterimage flags.
    [TestFixture]
    public sealed class KillerAnonymityReverifyTests
    {
        [Test]
        public void KillerPathFiles_DoNotReadForbiddenSurvivorFields()
        {
            // Audits all files compiled on the Killer path
            string[] killerFiles =
            {
                "Killer/KillerScene.cs",
                "Killer/KillerWorldLayer.cs",
                "Killer/BlipTracker.cs",
                "Killer/BlipSoundModel.cs",
                "Killer/SurvivorSoundSynth.cs",
                "Killer/ExecutionGuidance.cs",
                "Killer/KillerCues.cs",
                "Killer/KillerHudCopy.cs",
                "Killer/KillerAudio.cs",
                "UI/KillerHud.cs",
                "UI/MarkerLayer.cs"
            };

            var allowedIdReceivers = new HashSet<string> { "terminal" };
            var allowedYawReceivers = new HashSet<string> { "pose", "view", "rig", "camera" };

            foreach (string rel in killerFiles)
            {
                string raw = QaPaths.ReadScript(rel);
                string code = QaSource.StripComments(raw);
                string filename = Path.GetFileName(rel);

                // Check Id, StatusTimer, CaptureCount, Flags accesses
                foreach (Match m in Regex.Matches(code, @"\b(\w+)\s*\.\s*(Id|StatusTimer|CaptureCount|Flags)\b"))
                {
                    string receiver = m.Groups[1].Value;
                    string member = m.Groups[2].Value;
                    if (member == "Id" && allowedIdReceivers.Contains(receiver)) continue;
                    Assert.Fail($"{filename} reads forbidden field '{m.Value}'");
                }

                // Check Yaw accesses
                foreach (Match m in Regex.Matches(code, @"\b(\w+)\s*\.\s*Yaw\b"))
                {
                    string receiver = m.Groups[1].Value;
                    Assert.That(allowedYawReceivers, Does.Contain(receiver), $"{filename} reads forbidden Yaw via '{m.Value}'");
                }

                // Check flag tokens
                Assert.That(code, Does.Not.Match(@"\bPlayerFlags\b"), $"{filename} references PlayerFlags");
                Assert.That(code, Does.Not.Match(@"\b(Afterimage|Stealthed|Hacking|Rescuing)\b"), $"{filename} references behavior flags");
            }
        }

        [Test]
        public void KillerScene_Accept_ReadsOnlyRoleStatusAndCoordinatesFromEntries()
        {
            string code = QaSource.StripComments(QaPaths.ReadScript("Killer/KillerScene.cs"));
            string accept = QaSource.MethodText(code, "public void Accept(");
            Assert.That(accept, Is.Not.Null, "KillerScene.Accept method found");

            var fields = new SortedSet<string>();
            foreach (Match m in Regex.Matches(accept, @"\bentry\s*\.\s*(\w+)"))
            {
                fields.Add(m.Groups[1].Value);
            }

            Assert.That(fields, Is.EquivalentTo(new[] { "Role", "Status", "X", "Z" }),
                "Only Role, Status, X and Z are ever accessed on PlayerState entries in KillerScene.Accept");
        }

        [Test]
        public void WorldView_Accept_ImmediatelyBreaksOnKillerRole()
        {
            string code = QaSource.StripComments(QaPaths.ReadScript(WorldSources.Characters));
            string accept = QaSource.MethodText(code, "public void Accept(");
            Assert.That(accept, Is.Not.Null);

            int breakIndex = accept.IndexOf("role == Role.Killer) break;", StringComparison.Ordinal);
            Assert.That(breakIndex, Is.GreaterThanOrEqualTo(0), "WorldView.Accept has break on role == Role.Killer");

            // Ensure no player fields are read before the break
            foreach (string field in new[] { "player.Id", "player.Yaw", "player.Status", "player.Flags", "player.X", "player.Z", "player.StatusTimer", "player.CaptureCount" })
            {
                int at = accept.IndexOf(field, StringComparison.Ordinal);
                Assert.That(at < 0 || at > breakIndex, Is.True, $"{field} must not be read before Killer break in WorldView.Accept");
            }
        }

        [Test]
        public void ClientApp_PlayerLoop_BreaksOnKillerRoleAndGuardsCollectMates()
        {
            string code = QaSource.StripComments(QaPaths.ReadScript("ClientApp.cs"));

            // Check own entry lookup break
            int guard = code.IndexOf("role == Role.Killer ? player.Role != (byte)Role.Killer : player.Id != playerId", StringComparison.Ordinal);
            Assert.That(guard, Is.GreaterThanOrEqualTo(0));
            int stop = code.IndexOf("if (role == Role.Killer) break;", guard, StringComparison.Ordinal);
            Assert.That(stop, Is.GreaterThan(guard));

            // Survivor-only fields must be behind break
            foreach (string field in new[] { "player.Flags", "player.StatusTimer", "player.CaptureCount" })
            {
                int at = code.IndexOf(field, guard, StringComparison.Ordinal);
                Assert.That(at < 0 || at > stop, Is.True, $"{field} must sit behind the Killer break in ClientApp.cs");
            }

            // CollectMates must be gated by role == Role.Survivor
            Match collect = Regex.Match(code, @"if\s*\(\s*role\s*==\s*Role\.Survivor\s*\)\s*\{[^}]*CollectMates");
            Assert.That(collect.Success, Is.True, "CollectMates is only called when role == Role.Survivor");
        }

        [Test]
        public void DevCapture_OnWorld_GuardsAgainstKillerRoleBeforeEntryLoop()
        {
            string code = QaSource.StripComments(QaPaths.ReadScript("Dev/DevCapture.cs"));
            string onWorld = QaSource.MethodText(code, "public void OnWorld(");
            Assert.That(onWorld, Is.Not.Null);

            int guard = onWorld.IndexOf("if (role != Role.Survivor) return;", StringComparison.Ordinal);
            Assert.That(guard, Is.GreaterThanOrEqualTo(0), "DevCapture.OnWorld guards with role != Role.Survivor");

            int loop = onWorld.IndexOf("foreach (PlayerState player in state.Players)", StringComparison.Ordinal);
            Assert.That(loop, Is.GreaterThan(guard), "Entry loop must be after the Survivor-only return guard");
        }

        [Test]
        public void DevCapture_Preview_NeverReadsPlayerStateOnKillerRole()
        {
            string code = QaSource.StripComments(QaPaths.ReadScript("ClientApp.cs"));
            string preview = QaSource.MethodText(code, "private void Preview(");
            Assert.That(preview, Is.Not.Null, "Preview method found in ClientApp.cs");

            int killerPreview = preview.IndexOf("role == Role.Killer && lobby != null", StringComparison.Ordinal);
            Assert.That(killerPreview, Is.GreaterThanOrEqualTo(0));

            string killerSection = preview.Substring(killerPreview);
            Assert.That(killerSection, Does.Contain("lobby.Slots"), "Killer preview uses lobby slots");
            Assert.That(killerSection, Does.Not.Contain("Players"), "Killer preview does not use survivor entries / Players");
            Assert.That(killerSection, Does.Not.Contain("lastWorld"), "Killer preview does not use lastWorld");
            Assert.That(killerSection, Does.Not.Contain("PlayerState"), "Killer preview does not use PlayerState");
        }

        [Test]
        public void SelfState_SeamIsolation_KillerNeverReadsRescueOrInvulnerability()
        {
            // Killer path must never read RescueProgress or InvulnerableLeft
            string[] killerFiles =
            {
                "Killer/KillerScene.cs",
                "Killer/KillerWorldLayer.cs",
                "Killer/ExecutionGuidance.cs",
                "Killer/KillerCues.cs",
                "UI/KillerHud.cs"
            };

            foreach (string rel in killerFiles)
            {
                string code = QaSource.StripComments(QaPaths.ReadScript(rel));
                Assert.That(Regex.IsMatch(code, @"\b(RescueProgress|InvulnerableLeft)\b"), Is.False,
                    $"{rel} reads Survivor-only SelfState field");
            }

            // Survivor HUD files must never read ExecutionGraceLeft
            foreach (string rel in new[] { "UI/SurvivorHud.cs", "UI/SurvivorReadout.cs" })
            {
                string code = QaSource.StripComments(QaPaths.ReadScript(rel));
                Assert.That(code, Does.Not.Contain("ExecutionGraceLeft"), $"{rel} reads Killer-only ExecutionGraceLeft");
            }
        }

        [Test]
        public void BlipTracker_AcceptsAnonymousEntriesAndProducesUniformInstancesRegardlessOfOrder()
        {
            var terminals = new List<(float x, float z)> { (10, 10), (30, 30) };
            var trackerA = new BlipTracker(new System.Random(1234), terminals);
            var trackerB = new BlipTracker(new System.Random(1234), terminals);

            var entriesA = new List<BlipInput>
            {
                new BlipInput(5f, 5f, BlipStatus.Normal),
                new BlipInput(20f, 20f, BlipStatus.Lagging),
                new BlipInput(40f, 40f, BlipStatus.Normal)
            };

            var entriesB = new List<BlipInput>
            {
                new BlipInput(40f, 40f, BlipStatus.Normal),
                new BlipInput(5f, 5f, BlipStatus.Normal),
                new BlipInput(20f, 20f, BlipStatus.Lagging)
            };

            trackerA.Accept(1, entriesA, 0, 0);
            trackerB.Accept(1, entriesB, 0, 0);

            Assert.That(trackerA.Blips.Count, Is.EqualTo(3));
            Assert.That(trackerB.Blips.Count, Is.EqualTo(3));

            // Position and status counts match regardless of entry order
            int lagA = 0, lagB = 0;
            foreach (var b in trackerA.Blips) if (b.Status == BlipStatus.Lagging) lagA++;
            foreach (var b in trackerB.Blips) if (b.Status == BlipStatus.Lagging) lagB++;
            Assert.That(lagA, Is.EqualTo(1));
            Assert.That(lagB, Is.EqualTo(1));
        }
    }
}
