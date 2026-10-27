using System;
using System.IO;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PingTheKiller.Client.Net;

namespace PingTheKiller.Client.QA.Tests
{
    // Written from docs/briefs/U4b-a1.md section 3.1: connection errors are our own fixed sentences, never raw OS/exception
    // text, and the mapping is a pure function.
    public sealed class ConnectionMessagesQaTests
    {
        private const string Endpoint = "192.168.0.10:7777";
        private const string Refused = "Server not reachable at 192.168.0.10:7777. Check that the host is running and the port is right.";
        private const string TimedOut = "No answer from 192.168.0.10:7777 within 5 s. Check the IP and that both PCs are on the same network.";
        private const string Lost = "Connection to 192.168.0.10:7777 lost. Rejoin when the host is back.";

        private static bool HasNonAscii(string text)
        {
            foreach (char c in text) if (c > 127) return true;
            return false;
        }

        // ---- the three fixed sentences ----------------------------------------------------------------------------------

        [TestCase(SocketError.ConnectionRefused)]
        [TestCase(SocketError.HostUnreachable)]
        [TestCase(SocketError.NetworkUnreachable)]
        [TestCase(SocketError.HostNotFound)]
        [TestCase(SocketError.NetworkDown)]
        [TestCase(SocketError.AddressNotAvailable)]
        [TestCase(SocketError.AccessDenied)]
        public void RefusedAndUnreachableSocketErrorsGiveTheNotReachableSentence(SocketError code)
        {
            Assert.That(ConnectionMessages.Describe(new SocketException((int)code), false, Endpoint), Is.EqualTo(Refused));
        }

        [Test]
        public void ATimedOutSocketErrorGivesTheTimeoutSentence()
        {
            Assert.That(ConnectionMessages.Describe(new SocketException((int)SocketError.TimedOut), false, Endpoint), Is.EqualTo(TimedOut));
        }

        [Test]
        public void ATimeoutExceptionGivesTheTimeoutSentenceAlsoThroughAnAggregate()
        {
            Assert.That(ConnectionMessages.Describe(new TimeoutException("Connect timed out after 5 s."), false, Endpoint), Is.EqualTo(TimedOut));
            var wrapped = new AggregateException(new TimeoutException("x"));
            Assert.That(ConnectionMessages.Describe(wrapped, false, Endpoint), Is.EqualTo(TimedOut));
            var nested = new InvalidOperationException("outer", new AggregateException(new SocketException((int)SocketError.TimedOut)));
            Assert.That(ConnectionMessages.Describe(nested, false, Endpoint), Is.EqualTo(TimedOut));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AnErrorAfterConnectingIsAlwaysTheLostSentence(bool timeoutShape)
        {
            Exception error = timeoutShape ? (Exception)new TimeoutException("t") : new IOException("The server closed the connection.");
            Assert.That(ConnectionMessages.Describe(error, true, Endpoint), Is.EqualTo(Lost));
            Assert.That(ConnectionMessages.Describe(new SocketException((int)SocketError.ConnectionReset), true, Endpoint), Is.EqualTo(Lost));
        }

        [Test]
        public void AnUnknownExceptionBeforeConnectingFallsBackToNotReachable()
        {
            Assert.That(ConnectionMessages.Describe(new InvalidOperationException("boom"), false, Endpoint), Is.EqualTo(Refused));
            Assert.That(ConnectionMessages.Describe(new ObjectDisposedException("socket"), false, Endpoint), Is.EqualTo(Refused));
        }

        [Test]
        public void ANullExceptionDoesNotThrowAndDescribesAFailureInPlainWords()
        {
            string text = null;
            Assert.DoesNotThrow(() => text = ConnectionMessages.Describe((Exception)null, false, Endpoint));
            Assert.That(text, Is.EqualTo(Refused));
            Assert.DoesNotThrow(() => text = ConnectionMessages.Describe((Exception)null, true, Endpoint));
            Assert.That(text, Is.EqualTo(Lost));
        }

        [Test]
        public void ClassifyAndDescribeByFailureAgreeWithTheExceptionOverload()
        {
            Assert.That(ConnectionMessages.Classify(new TimeoutException(), false), Is.EqualTo(LinkFailure.Timeout));
            Assert.That(ConnectionMessages.Classify(new IOException(), true), Is.EqualTo(LinkFailure.Lost));
            Assert.That(ConnectionMessages.Classify(new SocketException((int)SocketError.ConnectionRefused), false), Is.EqualTo(LinkFailure.Unreachable));
            Assert.That(ConnectionMessages.Describe(LinkFailure.Unreachable, Endpoint), Is.EqualTo(Refused));
            Assert.That(ConnectionMessages.Describe(LinkFailure.Timeout, Endpoint), Is.EqualTo(TimedOut));
            Assert.That(ConnectionMessages.Describe(LinkFailure.Lost, Endpoint), Is.EqualTo(Lost));
        }

        // ---- raw (localized) text never reaches the screen --------------------------------------------------------------

        private static readonly string[] LocalizedMessages =
        {
            "대상 컴퓨터에서 연결을 거부했으므로 연결하지 못했습니다.",
            "Die Verbindung wurde vom Zielcomputer abgelehnt.",
            "対象のコンピューターによって拒否されました",
            "No se puede establecer una conexión ya que el equipo de destino denegó expresamente dicha conexión.",
            "Невозможно подключиться к удалённому серверу"
        };

        [Test]
        public void LocalizedExceptionTextNeverAppearsInTheOutput()
        {
            foreach (string raw in LocalizedMessages)
            {
                var errors = new Exception[]
                {
                    new Exception(raw), new IOException(raw), new TimeoutException(raw), new InvalidOperationException(raw),
                    new AggregateException(raw, new Exception(raw)), new InvalidOperationException("outer", new IOException(raw)),
                    new AggregateException(new TimeoutException(raw))
                };
                foreach (Exception error in errors)
                {
                    foreach (bool connected in new[] { false, true })
                    {
                        string text = ConnectionMessages.Describe(error, connected, Endpoint);
                        Assert.That(text, Does.Not.Contain(raw), error.GetType().Name + " connected=" + connected);
                        Assert.That(HasNonAscii(text), Is.False, "no localized characters on screen: " + text);
                        Assert.That(text, Is.EqualTo(Refused).Or.EqualTo(TimedOut).Or.EqualTo(Lost), "only the three fixed sentences");
                    }
                }
            }
        }

        [Test]
        public void ARealSocketExceptionWhoseMessageIsLocalizedByTheOsIsStillAFixedSentence()
        {
            var real = new SocketException((int)SocketError.ConnectionRefused);
            string raw = real.Message; // Korean on a Korean Windows, English elsewhere
            string text = ConnectionMessages.Describe(real, false, Endpoint);
            Assert.That(text, Is.EqualTo(Refused));
            if (raw.Length > 0) Assert.That(text, Does.Not.Contain(raw));
        }

        // ---- other fixed messages ---------------------------------------------------------------------------------------

        [Test]
        public void TheLobbyTimeoutNamesNoProtocolVersionAndAsksForTheSameVersion()
        {
            string text = ConnectionMessages.LobbyTimeout;
            Assert.That(text, Does.Not.Contain("protocol"));
            Assert.That(Regex.IsMatch(text, @"v\d"), Is.False, "no version string");
            Assert.That(text, Does.Contain("lobby"));
            Assert.That(text, Does.Contain("version"));
            Assert.That(text, Does.Not.Contain("!"));
        }

        [Test]
        public void EveryFixedMessageIsPlainAsciiWithoutInternalNamesOrExclamationMarks()
        {
            string[] all =
            {
                ConnectionMessages.LobbyTimeout, ConnectionMessages.Left, ConnectionMessages.OfflineStopped,
                ConnectionMessages.Rejected(0, Endpoint), ConnectionMessages.Rejected(1, Endpoint), ConnectionMessages.Rejected(2, Endpoint),
                ConnectionMessages.Rejected(255, Endpoint), Refused, TimedOut, Lost
            };
            foreach (string text in all)
            {
                Assert.That(text, Is.Not.Null.And.Not.Empty);
                Assert.That(HasNonAscii(text), Is.False, text);
                Assert.That(Regex.IsMatch(text, @"\bU\d+[a-z]?\b"), Is.False, "internal unit name in: " + text);
                Assert.That(Regex.IsMatch(text, @"\bv\d+\.\d+"), Is.False, "protocol version in: " + text);
                Assert.That(text, Does.Not.Contain("!"), text);
                Assert.That(text, Does.Not.Contain("Exception"), text);
                Assert.That(text, Does.Not.Contain("Something went wrong"), text);
            }
        }

        [Test]
        public void RejectedMessagesNameTheEndpointAndTheNextStep()
        {
            Assert.That(ConnectionMessages.Rejected(1, Endpoint), Does.Contain(Endpoint));
            Assert.That(ConnectionMessages.Rejected(1, Endpoint), Does.Contain("full"));
            Assert.That(ConnectionMessages.Rejected(2, Endpoint), Does.Contain(Endpoint));
            Assert.That(ConnectionMessages.Rejected(1, Endpoint), Is.Not.EqualTo(ConnectionMessages.Rejected(2, Endpoint)));
        }

        // ---- source layer: nothing routes raw exception text to the screen ----------------------------------------------

        [Test]
        public void NetClientPassesOnlyFixedMessagesAsTheFailureNotice()
        {
            string source = QaSource.StripComments(QaPaths.ReadScript("Net/NetClient.cs"));
            foreach (Match call in Regex.Matches(source, @"\bStop\(([^;]*)\)\s*;"))
            {
                string arguments = call.Groups[1].Value.Trim();
                if (arguments.StartsWith("string ")) continue;
                Assert.That(arguments, Does.StartWith("ConnectionMessages."), "Stop(" + arguments + ") must pass a fixed message first");
            }
            Assert.That(source, Does.Not.Contain("notices.Enqueue(exception"), "raw exception text must not be queued for the screen");
        }

        [Test]
        public void NoScreenCallReceivesAnExceptionMessage()
        {
            foreach (string name in new[] { "ClientApp.cs", "UI/ClientScreens.cs", "Net/OfflineLink.cs", "Net/NetClient.cs" })
            {
                string source = QaSource.StripComments(QaPaths.ReadScript(name));
                Assert.That(Regex.IsMatch(source, @"(screens\.(Error|ShowMenu)|Disconnected\?\.Invoke|notices\.Enqueue)\([^;]*\.(Message|ToString\(\)|GetBaseException)"),
                    Is.False, name + " passes raw exception text toward the screen");
            }
        }

        [Test]
        public void RawFailureDetailGoesToTheLogOnly()
        {
            string app = QaSource.StripComments(QaPaths.ReadScript("ClientApp.cs"));
            Assert.That(app, Does.Contain("Debug.Log(\"[net] \" + endpointText + \": \" + client.FailureDetail)"));
            string net = QaSource.StripComments(QaPaths.ReadScript("Net/NetClient.cs"));
            Assert.That(net, Does.Contain("public string FailureDetail"));
        }
    }
}
