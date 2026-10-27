using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PingTheKiller.Client.Net;
using PingTheKiller.ServerCore;
using PingTheKiller.ServerCore.Transport;

namespace PingTheKiller.Client.QA.Tests
{
    // QA brief item 2: the offline path never touches a socket type. Two independent layers:
    //   1. an IL scan of the compiled types (skipped as inconclusive if the runtime cannot read IL), and
    //   2. a scan of the comment-free source text.
    // The orchestrator already proved the runtime side (built player: 0 TCP and 0 UDP endpoints during an offline match).
    public sealed class OfflineStructureQaTests
    {
        private static string Describe(IlScan scan) => string.Join("; ", scan.Violations.ToArray());

        // Negative control: if the scanner cannot see a TcpClient inside NetClient it is not reliable on this runtime.
        private static void RequireReliableScanner()
        {
            IlScan control = IlScan.Of(typeof(NetClient));
            if (control.Bodies == 0 || control.Violations.Count == 0)
                Assert.Inconclusive("IL scan is not reliable on this runtime (no TcpClient found in NetClient); see the source-scan tests and the code-read finding.");
        }

        [Test]
        public void TheScannerFindsTheSocketInNetClientAsANegativeControl()
        {
            IlScan control = IlScan.Of(typeof(NetClient));
            Assert.That(control.Violations, Is.Not.Empty, "NetClient owns a TcpClient, so a working scanner must report it");
        }

        [Test]
        public void OfflineLinkReferencesNoSocketTransportOrNetClientType()
        {
            RequireReliableScanner();
            IlScan scan = IlScan.Of(typeof(OfflineLink));
            Assert.That(scan.Bodies, Is.GreaterThan(3), "the scan read the method bodies");
            Assert.That(scan.References.Exists(line => line.Contains("PingTheKiller.ServerCore.ServerOptions")), Is.True,
                "sanity: the scan reached the CreateTransport call");
            Assert.That(scan.Violations, Is.Empty, "socket-related references in OfflineLink: " + Describe(scan));
        }

        [Test]
        public void TheHostedServerAndTheInMemoryTransportReferenceNoSocketType()
        {
            RequireReliableScanner();
            foreach (Type type in new[] { typeof(GameServer), typeof(InMemoryTransport), typeof(FramedConnection) })
            {
                IlScan scan = IlScan.Of(type);
                Assert.That(scan.Bodies, Is.GreaterThan(0), type.Name);
                Assert.That(scan.Violations, Is.Empty, type.Name + " references: " + Describe(scan));
            }
        }

        [Test]
        public void ServerOptionsHandsOutAnInMemoryTransportForOfflineAndNeverBuildsATcpListenerThere()
        {
            using (ITransportListener offline = new ServerOptions { IsOffline = true }.CreateTransport(() =>
                { throw new InvalidOperationException("the online factory must not run offline"); }))
            {
                Assert.That(offline, Is.TypeOf<InMemoryTransport>());
            }
        }

        [Test]
        public void ClientAppStartOfflineConstructsNoSocketOrNetClient()
        {
            Type app = typeof(OfflineLink).Assembly.GetType("PingTheKiller.Client.ClientApp");
            if (app == null) Assert.Ignore("ClientApp is not part of this runner.");
            MethodInfo start = app.GetMethod("StartOffline", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(start, Is.Not.Null, "ClientApp.StartOffline");
            RequireReliableScanner();
            IlScan scan = IlScan.Of(start);
            Assert.That(scan.Violations, Is.Empty, Describe(scan));
        }

        // ---- source layer (code-read evidence, comment-free) ----------------------------------------------------------

        private static readonly string[] SocketTokens =
        {
            "NetClient", "TcpClient", "TcpListener", "TcpTransport", "System.Net.Sockets", "Socket", "UdpClient", "NetworkStream"
        };

        private static void AssertNoSocketToken(string source, string what)
        {
            string stripped = QaSource.StripComments(source);
            foreach (string token in SocketTokens)
                Assert.That(Regex.IsMatch(stripped, @"\b" + Regex.Escape(token) + @"\b"), Is.False, what + " mentions " + token);
        }

        [Test]
        public void OfflineLinkSourceMentionsNoSocketTypeOutsideComments()
        {
            AssertNoSocketToken(QaPaths.ReadScript("Net/OfflineLink.cs"), "OfflineLink.cs");
        }

        [Test]
        public void IGameLinkSourceMentionsNoSocketTypeOutsideComments()
        {
            AssertNoSocketToken(QaPaths.ReadScript("Net/IGameLink.cs"), "IGameLink.cs");
        }

        [Test]
        public void ClientAppConstructsNetClientOnlyInsideJoin()
        {
            string source = QaSource.StripComments(QaPaths.ReadScript("ClientApp.cs"));
            Assert.That(Regex.Matches(source, @"new\s+NetClient\s*\(").Count, Is.EqualTo(1), "exactly one NetClient construction in ClientApp");
            string join = QaSource.MethodText(source, "private void Join(string value)");
            Assert.That(join, Is.Not.Null, "ClientApp.Join");
            Assert.That(join, Does.Contain("new NetClient("), "the construction is inside Join");
            string offline = QaSource.MethodText(source, "private void StartOffline()");
            Assert.That(offline, Is.Not.Null, "ClientApp.StartOffline");
            foreach (string token in SocketTokens)
                Assert.That(Regex.IsMatch(offline, @"\b" + Regex.Escape(token) + @"\b"), Is.False, "StartOffline mentions " + token);
            Assert.That(offline, Does.Contain("new OfflineLink("));
        }

        [Test]
        public void OnlyTheDevAndOfflineEntryPointsStartAnOfflineLink()
        {
            string source = QaSource.StripComments(QaPaths.ReadScript("ClientApp.cs"));
            Assert.That(Regex.Matches(source, @"new\s+OfflineLink\s*\(").Count, Is.EqualTo(1));
            Assert.That(source, Does.Contain("screens.Offline += StartOffline"), "the menu Offline button is wired");
            Assert.That(source, Does.Contain("dev.Offline += StartOffline"), "-ptk-offline is wired");
        }

        // ---- link lifecycle (code-read evidence) ----------------------------------------------------------------------

        [Test]
        public void EveryNewLinkReleasesThePreviousOneFirst()
        {
            string source = QaSource.StripComments(QaPaths.ReadScript("ClientApp.cs"));
            string attach = QaSource.MethodText(source, "private void Attach(");
            Assert.That(attach, Is.Not.Null);
            Assert.That(Regex.IsMatch(attach, @"^\{\s*Release\(\);"), Is.True, "Attach starts with Release(): second Offline and Join-after-Offline dispose the old link");
            string release = QaSource.MethodText(source, "private void Release()");
            Assert.That(release, Is.Not.Null);
            Assert.That(release, Does.Contain("previous.Dispose()"));
            Assert.That(release, Does.Contain("PacketReceived -= Receive"), "the old link is detached before it is disposed");
            Assert.That(release, Does.Contain("Disconnected -= Disconnect"));
        }

        [Test]
        public void LeaveAndDisconnectAndDestroyAllDisposeTheLink()
        {
            string source = QaSource.StripComments(QaPaths.ReadScript("ClientApp.cs"));
            string disconnect = QaSource.MethodText(source, "private void Disconnect(string notice)");
            Assert.That(disconnect, Is.Not.Null);
            Assert.That(disconnect, Does.Contain("Release()"), "Disconnect (Leave, timeout, lost) releases the link");
            Assert.That(source, Does.Contain("screens.Leave += () => Disconnect("), "Leave goes through Disconnect");
            string destroy = QaSource.MethodText(source, "private void OnDestroy()");
            Assert.That(destroy, Is.Not.Null);
            Assert.That(destroy, Does.Contain("link?.Dispose()"), "quitting disposes the link, which stops the game thread");
        }

        [Test]
        public void OfflineLinkStopsTheServerOffTheCallingThread()
        {
            string source = QaSource.StripComments(QaPaths.ReadScript("Net/OfflineLink.cs"));
            string stop = QaSource.MethodText(source, "private void Stop(string notice)");
            Assert.That(stop, Is.Not.Null);
            Assert.That(stop, Does.Contain("new Thread("), "server.Stop (which joins the game thread) runs on a helper thread");
            Assert.That(stop, Does.Contain("IsBackground = true"));
            // The only server.Stop call must sit inside that helper thread's lambda, never directly in Stop's own statements.
            int helper = stop.IndexOf("new Thread(", StringComparison.Ordinal);
            int call = stop.IndexOf("server.Stop()", StringComparison.Ordinal);
            Assert.That(call, Is.GreaterThan(helper), "server.Stop() is inside the helper thread");
            Assert.That(stop.IndexOf("server.Stop()", call + 1, StringComparison.Ordinal), Is.EqualTo(-1), "server.Stop() appears once");
            string dispose = QaSource.MethodText(source, "public void Dispose()");
            Assert.That(dispose == null || !dispose.Contains(".Join("), Is.True, "Dispose never joins");
        }
    }
}
