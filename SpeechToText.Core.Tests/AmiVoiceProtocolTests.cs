using FluentAssertions;

namespace SpeechToText.Core.Tests;

public class AmiVoiceProtocolTests
{
    [Fact]
    public void BuildAuthCommand_ShouldIncludeAuthorizationAndQuoteWhitespaceValues()
    {
        var command = AmiVoiceProtocol.BuildAuthCommand(
            "-a-general",
            "apikey",
            new Dictionary<string, string>
            {
                ["profileId"] = "profile with spaces",
                ["keepFillerToken"] = "1"
            });

        command.Should().StartWith("s ");
        command.Should().Contain("lsb16k -a-general");
        command.Should().Contain("authorization=apikey");
        command.Should().Contain("profileId=\"profile with spaces\"");
        command.Should().Contain("keepFillerToken=1");
    }

    [Theory]
    [InlineData("G trace", "Trace")]
    [InlineData("s", "Auth")]
    [InlineData("s invalid app key", "Auth")]
    [InlineData("p timeout", "Timeout")]
    [InlineData("e", "End")]
    [InlineData("S 123", "VoiceStart")]
    [InlineData("E 456", "VoiceEnd")]
    [InlineData("C", "RecognizeStart")]
    [InlineData("U {\"text\":\"x\"}", "Recognizing")]
    [InlineData("A {\"text\":\"x\"}", "Recognized")]
    [InlineData("R {\"text\":\"x\"}", "Recognized")]
    public void ParseServerPacket_ShouldMapPacketType(string rawPacket, string expectedType)
    {
        var packet = AmiVoiceProtocol.ParseServerPacket(rawPacket);

        packet.Type.ToString().Should().Be(expectedType);
        packet.Raw.Should().NotBeNullOrWhiteSpace();
    }
}
