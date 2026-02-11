using FluentAssertions;
using NSubstitute;
using System.Net;
using System.Net.Http;
using System.Text;

namespace SpeechToText.Core.Tests;

public class TextHttpSenderTests
{
    [Fact]
    public async Task Send_ShouldReturnFalse_AndSkipFactory_WhenUriIsEmpty()
    {
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        var sender = new TextHttpSender("", httpClientFactory);

        var result = await sender.Send(new TextHttpSender.RecognizedText { text = "hello" });

        result.Should().BeFalse();
        sender.IsEnable.Should().BeFalse();
        httpClientFactory.DidNotReceive().CreateClient();
    }

    [Fact]
    public async Task Send_ShouldPostJsonAndReturnTrue_WhenStatusCodeIsOk()
    {
        const string destinationUri = "https://example.local/recognized";
        string? requestBody = null;
        HttpMethod? requestMethod = null;
        Uri? requestUri = null;

        var handler = new DelegateHttpMessageHandler(async (request, _) =>
        {
            requestMethod = request.Method;
            requestUri = request.RequestUri;
            requestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var httpClient = new HttpClient(handler);
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient().Returns(httpClient);

        var sender = new TextHttpSender(destinationUri, httpClientFactory);

        var result = await sender.Send(new TextHttpSender.RecognizedText { text = "hello" });

        result.Should().BeTrue();
        sender.IsEnable.Should().BeTrue();
        requestMethod.Should().Be(HttpMethod.Post);
        requestUri.Should().Be(new Uri(destinationUri));
        requestBody.Should().NotBeNull();
        requestBody.Should().Be("{}");
        httpClientFactory.Received(1).CreateClient();
    }

    [Fact]
    public async Task Send_ShouldReturnFalseAndDisable_WhenHttpRequestExceptionOccurs()
    {
        var handler = new DelegateHttpMessageHandler((_, _) => throw new HttpRequestException("network failure"));
        var httpClient = new HttpClient(handler);
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient().Returns(httpClient);
        var sender = new TextHttpSender("https://example.local/recognized", httpClientFactory);

        var result = await sender.Send(new TextHttpSender.RecognizedText { text = "hello" });

        result.Should().BeFalse();
        sender.IsEnable.Should().BeFalse();
    }

    private sealed class DelegateHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> onSendAsync;

        public DelegateHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> onSendAsync)
        {
            this.onSendAsync = onSendAsync;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return onSendAsync(request, cancellationToken);
        }
    }
}
