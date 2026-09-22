using Cindara.Core.Authentication;
using Cindara.Core.Jellyfin;
using Cindara.Desktop.Localization;

namespace Cindara.Desktop.Tests.Localization;

[Collection(LocalizationTestGroup.Name)]
public sealed class LocalizedErrorsTests
{
    [Fact]
    public void AllTypedErrorsHaveResourcesWithoutExposingRawExceptionMessages()
    {
        using var scope = new CultureScope("en");
        const string rawMessage = "Native diagnostic that must not reach the UI";

        var messages = Enum.GetValues<AuthenticationError>()
            .Select(error => LocalizedErrors.Get(new AuthenticationException(error, rawMessage)))
            .Concat(Enum.GetValues<ServerConnectionError>()
                .Select(error => LocalizedErrors.Get(new ServerConnectionException(error, rawMessage))))
            .Concat(Enum.GetValues<MediaPreviewError>()
                .Select(error => LocalizedErrors.Get(new MediaPreviewException(error, rawMessage))));

        foreach (var message in messages)
        {
            Assert.NotEmpty(message);
            Assert.DoesNotContain(rawMessage, message, StringComparison.Ordinal);
            Assert.DoesNotContain("[Error.", message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void UnknownErrorCodesUseSafeFallback()
    {
        using var scope = new CultureScope("en");
        var expected = Loc.Get("Error.Unknown");

        Assert.Equal(expected, LocalizedErrors.Get(new AuthenticationException((AuthenticationError)999, "raw")));
        Assert.Equal(expected, LocalizedErrors.Get(new ServerConnectionException((ServerConnectionError)999, "raw")));
        Assert.Equal(expected, LocalizedErrors.Get(new MediaPreviewException((MediaPreviewError)999, "raw")));
    }

    [Fact]
    public void ErrorsRemainEnglishForUnsupportedLanguages()
    {
        using var scope = new CultureScope("fr-FR");

        Assert.Equal("Jellyfin denied access to this media.",
            LocalizedErrors.Get(new MediaPreviewException(MediaPreviewError.AccessDenied, "raw")));
    }
}
