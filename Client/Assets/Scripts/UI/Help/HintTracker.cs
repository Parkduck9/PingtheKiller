namespace PingTheKiller.Client.UI
{
    // HUD_SPEC 4.3a / 6.1 (11/17 R3): the first-time contextual hints. Each fires once per match, on the existing prompt row (a
    // Survivor client) or interact prompt (a Killer client), and stays HelpCopy.HintSeconds. None fires while the intro card is
    // up (the card already says it), none replaces a prompt that has text (the caller passes slotFree; a hint fires, and is
    // used up, only when it can be shown: INTEG-R, R3 QA minor 3), and
    // Survivor hints read only what a Survivor client already has. Pure; ClientApp feeds the facts each frame.
    public sealed class HintTracker
    {
        private bool terminal, lagging, signal, blip;
        private double shownAt = double.NegativeInfinity;
        private string current;

        public void Reset()
        {
            terminal = lagging = signal = blip = false;
            shownAt = double.NegativeInfinity; current = null;
        }

        // The hint to show now, or null. nearTerminal: an unfinished terminal within 6 m. laggingName: the first Lagging ally's
        // prompt name, or null when none is Lagging. A hint's text is fixed when it fires.
        public string Survivor(double now, bool introVisible, bool normal, bool nearTerminal, string laggingName, bool ownSignal, bool slotFree = true)
        {
            if (introVisible) return null;
            if (Active(now)) return current;
            if (!slotFree) return null;
            if (!terminal && normal && nearTerminal) return Fire(now, ref terminal, HelpCopy.HintTerminal);
            if (!lagging && normal && laggingName != null) return Fire(now, ref lagging, HelpCopy.HintLagging(laggingName));
            if (!signal && normal && ownSignal) return Fire(now, ref signal, HelpCopy.HintSignal);
            return null;
        }

        // blipWithin6: a tracked Survivor instance within 6 m (from the tracker, never from a masked entry).
        public string Killer(double now, bool introVisible, bool blipWithin6, bool slotFree = true)
        {
            if (introVisible) return null;
            if (Active(now)) return current;
            if (!slotFree) return null;
            if (!blip && blipWithin6) return Fire(now, ref blip, HelpCopy.HintKillerBlip);
            return null;
        }

        private bool Active(double now) => current != null && now - shownAt < HelpCopy.HintSeconds;

        private string Fire(double now, ref bool flag, string text)
        {
            flag = true; shownAt = now; current = text;
            return text;
        }

        public bool FiredTerminal => terminal;
        public bool FiredLagging => lagging;
        public bool FiredSignal => signal;
        public bool FiredBlip => blip;
    }
}
