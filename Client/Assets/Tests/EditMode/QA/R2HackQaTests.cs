using NUnit.Framework;
using PingTheKiller.Client.UI.RulesV2;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Client.QA.Tests
{
    // R2-hack QA attempt 1 (Fable). Findings F1 and F2 of docs/qa/R2-hack-qa-a1.md, written as the expected behaviour:
    // they fail on attempt 1 and are the acceptance tests of the fix.
    public sealed class R2HackQaTests
    {
        // Up, Down, Left, Right.
        private static readonly byte Dirs = HackRoute.WithDirection(HackRoute.WithDirection(HackRoute.WithDirection(
            HackRoute.WithDirection(0, 0, RouteDirection.Up), 1, RouteDirection.Down), 2, RouteDirection.Left), 3, RouteDirection.Right);

        // F1: the server keeps advertising an abandoned route (Server.Tests R2HackQaTests.F1). Holding E anywhere else
        // (exit switch, vault, pull, a Locked terminal) while not Hacking must leave W A S D to movement.
        [Test]
        public void F1_AnAbandonedRouteDoesNotCaptureTheMoveKeysWhileTheOwnEntryIsNotHacking()
        {
            var route = new RouteTracker();
            route.OnSelf(5, HackRoute.MaxLength, Dirs, 1, 0);
            Assert.That(route.Held, Is.True, "the server still holds the abandoned route");
            Assert.That(RouteKeys.Capturing(interactHeld: true, ownHacking: false, routeHeld: route.Held), Is.False,
                "E held away from the terminal: W A S D must still move");
        }

        // F2: a key the server dropped (not Hacking yet, rate limit, F1) must not leave the client ahead for good.
        [Test]
        public void F2_ARightKeyTheServerDroppedIsRolledBackByTheNextSelfState()
        {
            var route = new RouteTracker();
            route.OnSelf(5, HackRoute.MaxLength, Dirs, 0, 0);
            Assert.That(route.TryPress(RouteDirection.Up, .1, out C2S_HackInput _), Is.True);
            Assert.That(route.Index, Is.EqualTo(1));
            // The server dropped the key: one second later it still reports index 0 of the same route.
            route.OnSelf(5, HackRoute.MaxLength, Dirs, 0, 1.1);
            Assert.That(route.Index, Is.EqualTo(0), "the shown cell must follow the server once the key is known lost");
        }

        [Test]
        public void F2_AWrongKeyTheServerDroppedDoesNotLockTheRouteForGood()
        {
            var route = new RouteTracker();
            route.OnSelf(5, HackRoute.MaxLength, Dirs, 0, 0);
            Assert.That(route.TryPress(RouteDirection.Down, .1, out C2S_HackInput _), Is.True);
            Assert.That(route.Present, Is.False, "predicted error: locked until the server answers");
            // The server dropped the key: the same route is still there long after the error line ended.
            route.OnSelf(5, HackRoute.MaxLength, Dirs, 0, .1 + RouteTracker.ErrorSeconds + 1);
            Assert.That(route.Present, Is.True, "the same route must become answerable again");
        }

        // QA attempt 2, finding N1: in the gap between two routes (the server sends RouteId 0 for 0.7 s or more while the
        // own entry is still Hacking and E is held) W A S D must not fall through to movement, or the key still down from
        // the last cell, or pressed early for the next route, walks the character off the terminal.
        [Test]
        public void N1_TheGapBetweenRoutesDoesNotHandTheMoveKeysBackWhileEIsHeldAndTheOwnEntryIsHacking()
        {
            var route = new RouteTracker();
            route.OnSelf(5, HackRoute.MaxLength, Dirs, 3, 0);
            Assert.That(route.TryPress(RouteDirection.Right, .1, out C2S_HackInput _), Is.True);
            route.OnSelf(0, 0, 0, 0, .15); // the server's answer to the completed route: the gap
            Assert.That(route.Held, Is.False);
            Assert.That(RouteKeys.Capturing(interactHeld: true, ownHacking: true, routeHeld: route.Held), Is.True,
                "E held at the terminal between two routes: W A S D must stay off movement");
        }
    }
}
