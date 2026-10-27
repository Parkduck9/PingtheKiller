using NUnit.Framework;
using PingTheKiller.Client.Net;

namespace PingTheKiller.Client.QA.Tests
{
    public sealed class EndpointQaTests
    {
        [TestCase("127.0.0.1:7777", "127.0.0.1", 7777)]
        [TestCase("192.168.0.10:1", "192.168.0.10", 1)]
        [TestCase("10.0.0.1:65535", "10.0.0.1", 65535)]
        public void ValidEndpointsParse(string text, string ip, int port)
        {
            Assert.That(EndpointParser.TryParse(text, out var endpoint, out string error), Is.True, error);
            Assert.That(endpoint, Is.Not.Null);
            Assert.That(endpoint.Address.ToString(), Is.EqualTo(ip));
            Assert.That(endpoint.Port, Is.EqualTo(port));
            Assert.That(error, Is.Empty);
        }

        // Host/format problems: the message must tell the player to enter an IP address and port.
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("127.0.0.1")]          // missing port
        [TestCase(":7777")]              // missing host
        [TestCase("256.1.1.1:7777")]     // octet out of range
        [TestCase("1.2.3:7777")]         // three octets
        [TestCase("1.2.3.4.5:7777")]
        [TestCase("a.b.c.d:7777")]
        [TestCase("127.0.0.1 :7777")]    // space inside
        [TestCase("127.0.0.1:7777:1")]   // two ports
        public void MalformedHostOrFormatIsRejectedWithAFixHint(string text)
        {
            Assert.That(EndpointParser.TryParse(text, out var endpoint, out string error), Is.False);
            Assert.That(endpoint, Is.Null);
            AssertUsefulMessage(error);
            bool namesWhatToFix = error.IndexOf("IP", System.StringComparison.OrdinalIgnoreCase) >= 0
                || error.IndexOf("port", System.StringComparison.OrdinalIgnoreCase) >= 0;
            Assert.That(namesWhatToFix, Is.True, "Message must say what to enter. Got: " + error);
        }

        // Port problems: the message must name the valid range.
        [TestCase("127.0.0.1:0")]
        [TestCase("127.0.0.1:65536")]
        [TestCase("127.0.0.1:-1")]
        [TestCase("127.0.0.1:abc")]
        [TestCase("127.0.0.1:")]
        [TestCase("127.0.0.1:99999999999999")]
        [TestCase("127.0.0.1:77 77")]
        public void BadPortIsRejectedAndMessageNamesTheValidRange(string text)
        {
            Assert.That(EndpointParser.TryParse(text, out var endpoint, out string error), Is.False);
            Assert.That(endpoint, Is.Null);
            AssertUsefulMessage(error);
            Assert.That(error, Does.Contain("65535"), "Port errors should state the allowed range. Got: " + error);
        }

        [TestCase("127.0.0.1:7777 ")]
        [TestCase(" 127.0.0.1:7777")]
        [TestCase("\t127.0.0.1:7777  ")]
        public void SurroundingWhitespaceIsEitherTrimmedOrRejectedWithAMessage(string text)
        {
            // The spec does not say which. The implementation's choice (Trim, then accept) is documented in
            // docs/qa/U4-qa-a1.md. Both are acceptable; an exception or a silent wrong endpoint is not.
            bool ok = EndpointParser.TryParse(text, out var endpoint, out string error);
            if (ok)
            {
                Assert.That(endpoint.Address.ToString(), Is.EqualTo("127.0.0.1"));
                Assert.That(endpoint.Port, Is.EqualTo(7777));
            }
            else
            {
                Assert.That(endpoint, Is.Null);
                AssertUsefulMessage(error);
            }
        }

        private static void AssertUsefulMessage(string error)
        {
            // SLOP_DEFINITION section 4: say what and what to do, no filler, no exclamation marks.
            Assert.That(error, Is.Not.Null.And.Not.Empty);
            Assert.That(error, Does.Not.Contain("!"));
            Assert.That(error, Does.Not.Contain("went wrong").IgnoreCase);
            Assert.That(error, Does.Not.Contain("invalid input").IgnoreCase);
            Assert.That(error.Length, Is.GreaterThan(15));
        }
    }
}
