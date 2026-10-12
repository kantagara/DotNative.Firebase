using System.Runtime.CompilerServices;
using DotNative;
using DotNative.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotNative.Firebase;

public sealed record FirebaseAppOptions(
    string ApiKey,
    string ApplicationId,
    string ProjectId,
    string MessagingSenderId,
    string StorageBucket,
    string BundleId = ""
);

public interface IFirebase
{
    Task InitializeAsync(FirebaseAppOptions options, CancellationToken cancellationToken = default);

    Task LogEventAsync(
        string name,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default
    );

    Task LogAsync(string message, CancellationToken cancellationToken = default);

    Task RecordErrorAsync(string message, CancellationToken cancellationToken = default);

    Task SetCustomKeyAsync(string key, string value, CancellationToken cancellationToken = default);

    Task<string> GetTokenAsync(CancellationToken cancellationToken = default);

    Task<string> RequestPermissionAsync(CancellationToken cancellationToken = default);

    Task FetchRemoteConfigAsync(CancellationToken cancellationToken = default);

    Task<string> GetRemoteStringAsync(string key, CancellationToken cancellationToken = default);

    IAsyncEnumerable<IReadOnlyDictionary<string, string>> Messages(
        CancellationToken cancellationToken = default
    );
}

public static class FirebaseServices
{
    public static IServiceCollection AddFirebase(this IServiceCollection services)
    {
        services.TryAddSingleton<IFirebase, RoutingFirebase>();
        return services;
    }
}

internal sealed class RoutingFirebase(IPlatformChannels channels, PresentationTarget target)
    : IFirebase
{
    private bool Mobile => target.Platform is NativePlatform.IOS or NativePlatform.Android;

    public Task InitializeAsync(
        FirebaseAppOptions options,
        CancellationToken cancellationToken = default
    )
    {
        ValidateOptions(options);
        return Mobile
            ? new ChannelFirebase(channels).InitializeAsync(options, cancellationToken)
            : Task.CompletedTask;
    }

    public Task LogEventAsync(
        string name,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default
    )
    {
        ValidateName(name);
        return Mobile
            ? new ChannelFirebase(channels).LogEventAsync(name, parameters, cancellationToken)
            : Task.CompletedTask;
    }

    public Task LogAsync(string message, CancellationToken cancellationToken = default)
    {
        ValidateText(message);
        return Mobile
            ? new ChannelFirebase(channels).LogAsync(message, cancellationToken)
            : Task.CompletedTask;
    }

    public Task RecordErrorAsync(string message, CancellationToken cancellationToken = default)
    {
        ValidateText(message);
        return Mobile
            ? new ChannelFirebase(channels).RecordErrorAsync(message, cancellationToken)
            : Task.CompletedTask;
    }

    public Task SetCustomKeyAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default
    )
    {
        ValidateName(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Mobile
            ? new ChannelFirebase(channels).SetCustomKeyAsync(key, value, cancellationToken)
            : Task.CompletedTask;
    }

    public Task<string> GetTokenAsync(CancellationToken cancellationToken = default) =>
        Mobile
            ? new ChannelFirebase(channels).GetTokenAsync(cancellationToken)
            : Task.FromResult("");

    public Task<string> RequestPermissionAsync(CancellationToken cancellationToken = default) =>
        Mobile
            ? new ChannelFirebase(channels).RequestPermissionAsync(cancellationToken)
            : Task.FromResult("notSupported");

    public Task FetchRemoteConfigAsync(CancellationToken cancellationToken = default) =>
        Mobile
            ? new ChannelFirebase(channels).FetchRemoteConfigAsync(cancellationToken)
            : Task.CompletedTask;

    public Task<string> GetRemoteStringAsync(
        string key,
        CancellationToken cancellationToken = default
    )
    {
        ValidateName(key);
        return Mobile
            ? new ChannelFirebase(channels).GetRemoteStringAsync(key, cancellationToken)
            : Task.FromResult("");
    }

    public async IAsyncEnumerable<IReadOnlyDictionary<string, string>> Messages(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        if (!Mobile)
            yield break;
        await foreach (
            var value in new EventChannel(channels, "dotnative.firebase.messages").ReadAllAsync(
                cancellationToken: cancellationToken
            )
        )
        {
            if (value is not IReadOnlyDictionary<string, object?> fields)
                continue;
            var message = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in fields)
                if (entry.Value is string text)
                    message[entry.Key] = text;
            yield return message;
        }
    }

    private static void ValidateOptions(FirebaseAppOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ApiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ApplicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ProjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.MessagingSenderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.StorageBucket);
    }

    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 40)
            throw new ArgumentOutOfRangeException(nameof(name));
    }

    private static void ValidateText(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (message.Length > 2000)
            throw new ArgumentOutOfRangeException(nameof(message));
    }
}

internal sealed class ChannelFirebase(IPlatformChannels channels)
{
    public Task InitializeAsync(FirebaseAppOptions options, CancellationToken cancellationToken) =>
        Call(
            "initialize",
            new Dictionary<string, object?>
            {
                ["apiKey"] = options.ApiKey,
                ["applicationId"] = options.ApplicationId,
                ["projectId"] = options.ProjectId,
                ["messagingSenderId"] = options.MessagingSenderId,
                ["storageBucket"] = options.StorageBucket,
                ["bundleId"] = options.BundleId,
            },
            cancellationToken
        );

    public Task LogEventAsync(
        string name,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken
    ) =>
        Call(
            "logEvent",
            new Dictionary<string, object?>
            {
                ["name"] = name,
                ["parameters"] = parameters.ToDictionary(pair => pair.Key, pair => (object?)pair.Value),
            },
            cancellationToken
        );

    public Task LogAsync(string message, CancellationToken cancellationToken) =>
        Call("log", new Dictionary<string, object?> { ["message"] = message }, cancellationToken);

    public Task RecordErrorAsync(string message, CancellationToken cancellationToken) =>
        Call(
            "recordError",
            new Dictionary<string, object?> { ["message"] = message },
            cancellationToken
        );

    public Task SetCustomKeyAsync(string key, string value, CancellationToken cancellationToken) =>
        Call(
            "setCustomKey",
            new Dictionary<string, object?> { ["key"] = key, ["value"] = value },
            cancellationToken
        );

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        var value = await Call("getToken", null, cancellationToken).ConfigureAwait(false);
        if (value is not string token || token.Length is 0 or > 4096)
            throw new InvalidDataException("Invalid Firebase token.");
        return token;
    }

    public async Task<string> RequestPermissionAsync(CancellationToken cancellationToken)
    {
        var value = await Call("requestPermission", null, cancellationToken).ConfigureAwait(false);
        if (value is not string status || status.Length is 0 or > 32)
            throw new InvalidDataException("Invalid notification permission.");
        return status;
    }

    public Task FetchRemoteConfigAsync(CancellationToken cancellationToken) =>
        Call("fetchRemoteConfig", null, cancellationToken);

    public async Task<string> GetRemoteStringAsync(string key, CancellationToken cancellationToken)
    {
        var value = await Call(
                "getRemoteString",
                new Dictionary<string, object?> { ["key"] = key },
                cancellationToken
            )
            .ConfigureAwait(false);
        return value as string ?? "";
    }

    private Task<object?> Call(
        string method,
        Dictionary<string, object?>? arguments,
        CancellationToken cancellationToken
    ) =>
        channels
            .Get("dotnative.firebase")
            .InvokeAsync(method, arguments, cancellationToken);
}
