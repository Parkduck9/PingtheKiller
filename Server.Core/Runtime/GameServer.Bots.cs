using System;
using System.Collections.Generic;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.ServerCore
{
    // Bots act only on what a human in the same role receives (U2d fair-information rule). Each bot decision goes through
    // a per-bot query object (SurvivorBotView / KillerBotView) filled here from the same data the role's WorldState carries,
    // and a brain class that has no reference to the server or any Player. The brain's intent is applied through the same
    // paths as client packets: MoveInput, Interact and UseSkill.
    public sealed partial class GameServer
    {
        // Survivor-bot route and pacing, shared with the phantoms (GameServer.Phantoms) so fake tracks keep the same shape.
        // Phantoms stop where a survivor bot hacks (and at landmarks); see SurvivorBrain.HackStopDistance.
        private const float BotStopDistance = SurvivorBrain.HackStopDistance;
        private const float BotStuckFraction = BotBrain.StuckFraction;
        private const double BotWalkBurstMin = SurvivorBrain.WalkBurstMin, BotWalkBurstMax = SurvivorBrain.WalkBurstMax;
        private const double BotWalkPauseMin = SurvivorBrain.WalkPauseMin, BotWalkPauseMax = SurvivorBrain.WalkPauseMax;
        private const double BotHackBurstMin = SurvivorBrain.HackBurstMin, BotHackBurstMax = SurvivorBrain.HackBurstMax;
        private const double BotHackPauseMin = SurvivorBrain.HackPauseMin, BotHackPauseMax = SurvivorBrain.HackPauseMax;
        // White Noise mixes workers with travellers, just as the uncapped survivor team does.
        private const int WhiteNoiseMinTerminalPoints = 2;
        private const int WhiteNoiseTerminalPointChoices = 3;

        // GDD v2 section 8: A* vault-link cost in metres on top of the crossing (about vault seconds x speed, plus a margin).
        internal const float SurvivorVaultCostMeters = 2f;
        internal const float KillerVaultCostMeters = 6f;

        private readonly Dictionary<Player, BotBrain> _brains = new Dictionary<Player, BotBrain>();
        private readonly SurvivorBotView _survivorBotView = new SurvivorBotView();
        private readonly KillerBotView _killerBotView = new KillerBotView();

        // Test hook (U2d fair-information test, Server.Tests only; internal, so the console host and the offline client
        // cannot set it): called with (bot Id, true) right before that bot's view is filled and with (bot Id, false) right
        // after its brain decided, so a test can perturb state the bot must not know and restore it.
        internal Action<int, bool> BotProbeForTests { get; set; }

        private void UpdateBots()
        {
            foreach (var bot in _players)
            {
                if (!bot.IsBot) continue;
                if (_sandbox) { UpdateSandboxBot(bot); continue; }
                BotProbeForTests?.Invoke(bot.Id, true);
                BotIntent intent;
                try
                {
                    if (bot.Role == Role.Killer)
                    {
                        FillKillerView(bot, _killerBotView);
                        var brain = BrainFor<KillerBrain>(bot);
                        brain.VaultWanted = false;
                        brain.VaultCostMeters = KillerVaultCostMeters;
                        intent = brain.Update(_killerBotView, Nav, Map, _random);
                        if (brain.VaultWanted) intent.Interact = !bot.Interacting;
                        brain.Tactics.Killer(_killerBotView, Nav, ref intent);
                    }
                    else
                    {
                        FillSurvivorView(bot, _survivorBotView);
                        var brain = BrainFor<SurvivorBrain>(bot);
                        brain.VaultWanted = false;
                        brain.VaultCostMeters = SurvivorVaultCostMeters;
                        intent = brain.Update(_survivorBotView, Nav, Map, _random);
                        if (brain.VaultWanted) intent.Interact = !bot.Interacting;
                        brain.Tactics.Survivor(_survivorBotView, Nav, ref intent);
                    }
                }
                finally { BotProbeForTests?.Invoke(bot.Id, false); }
                ApplyMove(bot, new C2S_MoveInput { DirX = intent.DirX, DirZ = intent.DirZ, Yaw = intent.Yaw, Seq = bot.Seq + 1 });
                SetInteract(bot, (byte)(intent.Interact ? 1 : 0));
                if (intent.UseSkill) RequestSkill(bot, bot.Skill);
            }
        }

        // A fresh brain at the start of every match and whenever the slot was not a bot of this role on the previous tick
        // (a human took it over, or a disconnected human was just replaced).
        private T BrainFor<T>(Player bot) where T : BotBrain, new()
        {
            if (!_brains.TryGetValue(bot, out var brain) || !(brain is T) || _matchTime <= 0 || brain.LastTick + 1 != _tick)
            {
                brain = new T();
                _brains[bot] = brain;
            }
            brain.LastTick = _tick;
            return (T)brain;
        }

        // The survivor client's knowledge: its own state, the survivor entries of the last WorldState, exact terminals, the
        // exit, ThreatLevel, and the killer only while KillerInSightOf (the U2e rule) holds for this bot. Positions do not
        // change between the last broadcast and this point of the tick, so threat and sight equal what that frame carried.
        private void FillSurvivorView(Player bot, SurvivorBotView view)
        {
            view.Time = _matchTime;
            view.SelfId = bot.Id;
            view.X = bot.X;
            view.Z = bot.Z;
            view.Speed = SpeedAt(bot, _now);
            view.Status = bot.Status;
            view.Skill = bot.Skill;
            view.SkillReady = SkillReady(bot);
            view.Team.Clear();
            if (_matchTime > 0) view.Team.AddRange(_survivorView);
            if (view.Terminals.Length < _terminals.Length) view.Terminals = new TerminalState[_terminals.Length];
            Array.Copy(_terminals, view.Terminals, _terminals.Length);
            view.TerminalCount = _terminals.Length;
            view.TerminalsRequired = _terminalsRequired;
            view.Exits = FillExits(view.Exits, out view.ExitCount);
            view.HatchVisible = HatchVisibleTo(bot);
            view.HatchX = view.HatchVisible ? _hatchX : 0;
            view.HatchZ = view.HatchVisible ? _hatchZ : 0;
            view.Threat = ThreatFor(bot);
            view.CollapseLeft = _collapseRunning ? (float)bot.CollapseLeft : -1;
            view.KillerVisible = KillerInSightOf(bot);
            view.KillerX = view.KillerVisible ? _killer.X : 0;
            view.KillerZ = view.KillerVisible ? _killer.Z : 0;
            view.KillerYaw = view.KillerVisible ? _killer.Yaw : 0;
            FillShutters(view.Shutters);
        }

        private void FillShutters(byte[] states)
        {
            for (int i = 0; i < states.Length; i++) states[i] = (byte)Map.ShutterAt(i);
        }

        // The killer client's knowledge: its own state, the anonymous killer view of the last WorldState (position and
        // status of each survivor-shaped entry, nothing else) and terminals as done or not done.
        private void FillKillerView(Player bot, KillerBotView view)
        {
            view.Time = _matchTime;
            view.X = bot.X;
            view.Z = bot.Z;
            view.Yaw = bot.Yaw;
            view.Speed = SpeedAt(bot, _now);
            view.Skill = bot.Skill;
            view.SkillReady = SkillReady(bot);
            view.Blips.Clear();
            foreach (var entry in _killerView)
                if (entry.Role == (byte)Role.Survivor)
                    view.Blips.Add(new KillerBotView.Blip { X = entry.X, Z = entry.Z, Lagging = entry.Status == (byte)PlayerStatus.Lagging });
            if (view.Terminals.Length < _terminals.Length) view.Terminals = new KillerBotView.Terminal[_terminals.Length];
            for (int i = 0; i < _terminals.Length; i++)
                view.Terminals[i] = new KillerBotView.Terminal { X = _terminals[i].X, Z = _terminals[i].Z, Done = _terminals[i].Progress >= 100 };
            view.TerminalCount = _terminals.Length;
            view.Exits = FillExits(view.Exits, out view.ExitCount);
            view.HatchVisible = HatchVisibleTo(bot);
            view.HatchX = view.HatchVisible ? _hatchX : 0;
            view.HatchZ = view.HatchVisible ? _hatchZ : 0;
            FillShutters(view.Shutters);
        }

        // The exits exactly as the WorldState carries them (both roles get the same), with the static switch points.
        private readonly List<ExitState> _exitScratch = new List<ExitState>();
        private BotExit[] FillExits(BotExit[] exits, out int count)
        {
            _exitScratch.Clear();
            AddExitStates(_exitScratch);
            count = _exitScratch.Count;
            if (exits.Length < count) exits = new BotExit[count];
            for (int i = 0; i < count; i++)
            {
                var state = _exitScratch[i];
                var flags = (ExitFlags)state.Flags;
                exits[i] = new BotExit
                {
                    X = state.X, Z = state.Z, SwitchX = Map.ExitSwitches[i].X, SwitchZ = Map.ExitSwitches[i].Z,
                    Powered = (flags & ExitFlags.Powered) != 0, Open = (flags & ExitFlags.Open) != 0,
                    Locked = (flags & ExitFlags.Locked) != 0, Progress = state.SwitchProgress
                };
            }
            return exits;
        }

        private int NearestIncompleteTerminal(float x, float z)
        {
            int nearest = -1;
            float best = float.MaxValue;
            for (int i = 0; i < _terminals.Length; i++)
            {
                if (_terminals[i].Progress >= 100) continue;
                float distance = DistanceSquared(x, z, _terminals[i].X, _terminals[i].Z);
                if (distance < best) { best = distance; nearest = i; }
            }
            return nearest;
        }
    }
}
