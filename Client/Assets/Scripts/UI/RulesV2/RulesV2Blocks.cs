using PingTheKiller.Shared.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.UI.RulesV2
{
    // HUD_SPEC 13.3: one hold action component. Survivor: the 672x40 u prompt row (M label, 656x4 u bar, fill `text`).
    // Killer: a 320x72 u block in the cluster's prompt slot (M 600 label, 304x4 u bar, S `muted` hint). Hidden for
    // HoldKind.None.
    public sealed class HoldActionBar : VisualElement
    {
        private readonly Label label, hint;
        private readonly VisualElement fill;
        private readonly Role role;

        public HoldActionBar(Role viewer, Font font)
        {
            role = viewer;
            pickingMode = PickingMode.Ignore;
            AddToClassList("v2-hold");
            AddToClassList(viewer == Role.Killer ? "v2-hold-killer" : "v2-hold-survivor");
            label = V2Ui.Text(this, string.Empty, font, "v2-m", viewer == Role.Killer ? "v2-bold" : "v2-plain");
            VisualElement track = V2Ui.Box(this, "v2-hold-track");
            fill = V2Ui.Box(track, "v2-fill", "v2-fill-text");
            hint = V2Ui.Text(this, string.Empty, font, "v2-s", "v2-muted", "v2-hold-hint");
            V2Ui.Show(hint, viewer == Role.Killer);
            V2Ui.Show(this, false);
        }

        public string Text => label.text;

        public void Set(in HoldActionView view, string interactGlyph)
        {
            V2Ui.Show(this, view.Kind != HoldKind.None);
            if (view.Kind == HoldKind.None) return;
            Set(HoldCopy.Label(view), view.Progress01);
            if (role == Role.Killer) V2Ui.SetText(hint, HoldCopy.Hint(view.Kind, interactGlyph));
        }

        // Any prompt row with a bar (HUD_SPEC 4.3 hacking uses the same row).
        public void Set(string text, float progress01)
        {
            V2Ui.Show(this, true);
            V2Ui.SetText(label, text);
            V2Ui.Fill(fill, HoldCopy.Fill(progress01));
        }

        public void Hide() => V2Ui.Show(this, false);
    }

    // HUD_SPEC 13.4: one Survivor exit cell, 192x46 u: line 1 `Exit 1` (M 600) left and the state word (M) right; line 2
    // a 176x6 u switch track (1 u `muted` outline) with a 4 u fill. Fill colour by phase: Switching `text`, Locked
    // `danger`, Open `ally` (full). No power: name and state `muted`, track only.
    public sealed class ExitCell : VisualElement
    {
        private readonly Label name, state;
        private readonly VisualElement fill;

        public ExitCell(Font font)
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("v2-exit");
            VisualElement line = V2Ui.Box(this, "v2-line");
            name = V2Ui.Text(line, string.Empty, font, "v2-m", "v2-bold");
            state = V2Ui.Text(line, string.Empty, font, "v2-m", "v2-right");
            VisualElement track = V2Ui.Box(this, "v2-exit-track");
            fill = V2Ui.Box(track, "v2-fill");
        }

        public string Text => name.text + " " + state.text;

        public void Set(in ExitView exit)
        {
            ExitPhase phase = ExitCopy.Phase(exit);
            V2Ui.SetText(name, ExitCopy.Name(exit.Id));
            V2Ui.SetText(state, ExitCopy.State(exit));
            bool muted = phase == ExitPhase.NoPower;
            name.EnableInClassList("v2-muted", muted); state.EnableInClassList("v2-muted", muted);
            fill.EnableInClassList("v2-fill-text", phase == ExitPhase.Switching || phase == ExitPhase.Powered);
            fill.EnableInClassList("v2-fill-danger", phase == ExitPhase.Locked);
            fill.EnableInClassList("v2-fill-ally", phase == ExitPhase.Open);
            V2Ui.Fill(fill, phase == ExitPhase.Open ? 1 : phase == ExitPhase.NoPower ? 0 : HoldCopy.Fill(exit.SwitchProgress01));
        }
    }

    // HUD_SPEC 13.5: the collapse clock. A 4x18 u bar, then `Collapse 1:52` (M 600 `text`). Urgent (shown value 0:30 or
    // less) changes only the bar colour: `muted` -> `danger` (Survivor) or `text` (Killer). The text never changes colour
    // and nothing flashes. Survivor: line 2 (S `text`) `Lagging: 2x faster · you 0:48` / `You 0:48` while the personal
    // clock shows a different value.
    public sealed class CollapseTimer : VisualElement
    {
        private readonly Label main, personal;
        private readonly VisualElement bar;
        private readonly Role role;

        public CollapseTimer(Role viewer, Font font)
        {
            role = viewer;
            pickingMode = PickingMode.Ignore;
            AddToClassList("v2-collapse");
            AddToClassList(viewer == Role.Killer ? "v2-collapse-killer" : "v2-collapse-survivor");
            VisualElement line = V2Ui.Box(this, "v2-line");
            bar = V2Ui.Box(line, "v2-bar");
            main = V2Ui.Text(line, string.Empty, font, "v2-m", "v2-bold");
            personal = V2Ui.Text(this, string.Empty, font, "v2-s", "v2-personal");
            V2Ui.Show(this, false);
        }

        public bool Urgent { get; private set; }
        public string Main => main.text;
        public string Personal => personal.text;

        public void Set(in CollapseView view)
        {
            V2Ui.Show(this, view.Running);
            if (!view.Running) return;
            CollapseView shown = view;
            if (role == Role.Killer) shown.PersonalLeft = -1;
            Urgent = CollapseCopy.Urgent(shown);
            V2Ui.SetText(main, CollapseCopy.Main(shown));
            string second = role == Role.Survivor ? CollapseCopy.Personal(shown) : string.Empty;
            V2Ui.SetText(personal, second);
            V2Ui.Show(personal, second.Length > 0);
            bar.EnableInClassList("v2-urgent-danger", Urgent && role == Role.Survivor);
            bar.EnableInClassList("v2-urgent-text", Urgent && role == Role.Killer);
        }
    }

    // HUD_SPEC 13.1: the Survivor objective row, 672x48 u, 8 u above the cluster: Exit 1 | Exit 2 | collapse.
    // Shown once any exit is Powered or a collapse clock runs.
    public sealed class ObjectiveRow : VisualElement
    {
        public readonly ExitCell[] Exits = new ExitCell[2];
        public readonly CollapseTimer Collapse;

        public ObjectiveRow(Font font)
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("v2-objective");
            for (int i = 0; i < Exits.Length; i++) { Exits[i] = new ExitCell(font); Add(Exits[i]); }
            Collapse = new CollapseTimer(Role.Survivor, font);
            Add(Collapse);
            V2Ui.Show(this, false);
        }

        public static bool Applies(ExitView[] exits, in CollapseView collapse)
        {
            if (collapse.Running) return true;
            if (exits != null) foreach (ExitView exit in exits) if (exit.Powered || exit.Open) return true;
            return false;
        }

        public void Set(ExitView[] exits, in CollapseView collapse)
        {
            bool shown = Applies(exits, collapse);
            V2Ui.Show(this, shown);
            if (!shown) return;
            for (int i = 0; i < Exits.Length; i++)
            {
                bool has = exits != null && i < exits.Length;
                V2Ui.Show(Exits[i], has);
                if (has) Exits[i].Set(exits[i]);
            }
            Collapse.Set(collapse);
        }
    }
}
