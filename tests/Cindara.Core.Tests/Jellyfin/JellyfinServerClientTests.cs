using System.Net;
using System.Text;
using Cindara.Core.Jellyfin;

namespace Cindara.Core.Tests.Jellyfin;

public sealed class JellyfinServerClientTests
{
    [Fact]
    public async Task ConnectAsyncReturnsIdentityFromPublicSystemInfo()
    {
        var handler = new StubHttpMessageHandler(
            HttpStatusCode.OK,
            """
            {
              "Id": "server-123",
              "ServerName": "Living Room",
              "Version": "10.10.7",
              "OperatingSystem": "Linux"
            }
            """);
        var client = new JellyfinServerClient(new HttpClient(handler));

        var server = await client.ConnectAsync("media.example.com/jellyfin");

        Assert.Equal("server-123", server.Id);
        Assert.Equal("Living Room", server.DisplayName);
        Assert.Equal("10.10.7", server.Version);
        Assert.Equal("Linux", server.OperatingSystem);
        Assert.Equal(
            new Uri("https://media.example.com/jellyfin/System/Info/Public"),
            handler.RequestUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ftp://media.example.com")]
    [InlineData("https://media.example.com?token=secret")]
    public async Task ConnectAsyncRejectsInvalidAddress(string address)
    {
        var client = new JellyfinServerClient(
            new HttpClient(new StubHttpMessageHandler(HttpStatusCode.OK, "{}")));

        var exception = await Assert.ThrowsAsync<ServerConnectionException>(
            () => client.ConnectAsync(address));

        Assert.Equal(ServerConnectionError.InvalidAddress, exception.Error);
    }

    [Fact]
    public async Task ConnectAsyncReportsUnexpectedStatus()
    {
        var client = new JellyfinServerClient(
            new HttpClient(new StubHttpMessageHandler(HttpStatusCode.BadGateway, "offline")));

        var exception = await Assert.ThrowsAsync<ServerConnectionException>(
            () => client.ConnectAsync("https://media.example.com"));

        Assert.Equal(ServerConnectionError.UnexpectedStatus, exception.Error);
        Assert.Contains("502", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ConnectAsyncReportsAccessDenied(HttpStatusCode statusCode)
    {
        var client = new JellyfinServerClient(
            new HttpClient(new StubHttpMessageHandler(statusCode, "denied")));

        var exception = await Assert.ThrowsAsync<ServerConnectionException>(
            () => client.ConnectAsync("https://media.example.com"));

        Assert.Equal(ServerConnectionError.AccessDenied, exception.Error);
    }

    [Fact]
    public async Task ConnectAsyncRejectsNonJellyfinResponse()
    {
        var client = new JellyfinServerClient(
            new HttpClient(new StubHttpMessageHandler(HttpStatusCode.OK, """{"name":"not Jellyfin"}""")));

        var exception = await Assert.ThrowsAsync<ServerConnectionException>(
            () => client.ConnectAsync("https://media.example.com"));

        Assert.Equal(ServerConnectionError.InvalidResponse, exception.Error);
    }

    [Fact]
    public async Task ConnectAsyncReportsNetworkFailure()
    {
        var client = new JellyfinServerClient(
            new HttpClient(new ThrowingHttpMessageHandler()));

        var exception = await Assert.ThrowsAsync<ServerConnectionException>(
            () => client.ConnectAsync("https://media.example.com"));

        Assert.Equal(ServerConnectionError.Unreachable, exception.Error);
    }

    [Fact]
    public async Task ConnectAsyncReportsTimeout()
    {
        var client = new JellyfinServerClient(
            new HttpClient(new TimeoutHttpMessageHandler()));

        var exception = await Assert.ThrowsAsync<ServerConnectionException>(
            () => client.ConnectAsync("https://media.example.com"));

        Assert.Equal(ServerConnectionError.TimedOut, exception.Error);
    }

    private sealed class StubHttpMessageHandler(HttpStatusCode statusCode, string content)
        : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;

            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
                RequestMessage = request,
            });
        }
    }

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("No route to host.");
    }

    private sealed class TimeoutHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new TaskCanceledException("The request timed out.");
    }
}
