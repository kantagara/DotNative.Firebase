
using System.Runtime.CompilerServices;
using DotNative;
using DotNative.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotNative.Firebase;

// ─────────────────────────────────────────────────────────────
// Configuration
// ─────────────────────────────────────────────────────────────

public sealed record FirebaseAppOptions(
    string ApiKey,
    string ApplicationId,
    string ProjectId,
    string MessagingSenderId,
    string StorageBucket,
    string BundleId = ""
);

public sealed record FirebaseStartupOptions(
    bool LogAppOpen = false,
    bool RequestNotificationPermission = false
);

// ─────────────────────────────────────────────────────────────
// Public API
// ─────────────────────────────────────────────────────────────

public interface IFirebaseApp
{
    Task InitializeAsync(
        CancellationToken cancellationToken = default);

    Task InitializeAsync(
        FirebaseAppOptions options,
        CancellationToken cancellationToken = default);
}

public interface IFirebaseAnalytics
{
    Task LogEventAsync(
        string name,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default);
}

public interface IFirebaseCrashlytics
{
    Task LogAsync(
        string message,
        CancellationToken cancellationToken = default);

    Task RecordErrorAsync(
        string message,
        CancellationToken cancellationToken = default);

    Task SetCustomKeyAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default);
}

public interface IFirebaseMessaging
{
    Task<string> GetTokenAsync(
        CancellationToken cancellationToken = default);

    Task<string> RequestNotificationPermissionAsync(
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<IReadOnlyDictionary<string, string>> Messages(
        CancellationToken cancellationToken = default);
}

public interface IFirebaseRemoteConfig
{
    Task FetchRemoteConfigAsync(
        CancellationToken cancellationToken = default);

    Task<string> GetRemoteStringAsync(
        string key,
        CancellationToken cancellationToken = default);
}

// ─────────────────────────────────────────────────────────────
// Dependency Injection
// ─────────────────────────────────────────────────────────────

public static class FirebaseServices
{
    public static IServiceCollection AddFirebase(
        this IServiceCollection services,
        FirebaseAppOptions options,
        FirebaseStartupOptions? startup = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        FirebaseService.ValidateOptions(options);

        return services.AddFirebase(_ => options, startup);
    }

    public static IServiceCollection AddFirebase(
        this IServiceCollection services,
        Func<PresentationTarget, FirebaseAppOptions> configure,
        FirebaseStartupOptions? startup = null)
    {
        ArgumentNullException.ThrowIfNull(configure);

        services.TryAddSingleton(sp =>
        {
            var target = sp.GetRequiredService<PresentationTarget>();

            return new FirebaseService(
                sp.GetRequiredService<IPlatformChannels>(),
                target,
                () => configure(target));
        });

        RegisterProducts(services);

        services.TryAddSingleton(
            startup ?? new FirebaseStartupOptions());

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IApplicationInitializer,
                FirebaseInitializer>());

        return services;
    }

    // Manual initialization compatibility.
    public static IServiceCollection AddFirebase(
        this IServiceCollection services)
    {
        services.TryAddSingleton<FirebaseService>();
        RegisterProducts(services);

        return services;
    }

    private static void RegisterProducts(
        IServiceCollection services)
    {
        services.TryAddSingleton<IFirebaseApp>(
            sp => sp.GetRequiredService<FirebaseService>());

        services.TryAddSingleton<IFirebaseAnalytics>(
            sp => sp.GetRequiredService<FirebaseService>());

        services.TryAddSingleton<IFirebaseCrashlytics>(
            sp => sp.GetRequiredService<FirebaseService>());

        services.TryAddSingleton<IFirebaseMessaging>(
            sp => sp.GetRequiredService<FirebaseService>());

        services.TryAddSingleton<IFirebaseRemoteConfig>(
            sp => sp.GetRequiredService<FirebaseService>());
    }
}

// ─────────────────────────────────────────────────────────────
// Implementation
// ─────────────────────────────────────────────────────────────

internal sealed class FirebaseService(
    IPlatformChannels channels,
    PresentationTarget target,
    Func<FirebaseAppOptions>? configuration = null
) :
    IFirebaseApp,
    IFirebaseAnalytics,
    IFirebaseCrashlytics,
    IFirebaseMessaging,
    IFirebaseRemoteConfig
{
    private const string ChannelName = "dotnative.firebase";
    private const string MessagesChannelName = "dotnative.firebase.messages";

    private bool IsMobile =>
        target.Platform is NativePlatform.IOS or NativePlatform.Android;

    private Task<object?> InvokeAsync(
        string method,
        Dictionary<string, object?>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        return channels.Get(ChannelName)
            .InvokeAsync(method, arguments, cancellationToken);
    }

    // ─────────────────────────────────────────────────────────
    // App
    // ─────────────────────────────────────────────────────────

    public Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        if (!IsMobile)
            return Task.CompletedTask;

        var options = configuration?.Invoke()
            ?? throw new InvalidOperationException(
                "Configure Firebase with AddFirebase(options) " +
                "before calling InitializeAsync().");

        return InitializeAsync(options, cancellationToken);
    }

    public Task InitializeAsync(
        FirebaseAppOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);

        if (!IsMobile)
            return Task.CompletedTask;

        return InvokeAsync(
            "initialize",
            new()
            {
                ["apiKey"] = options.ApiKey,
                ["applicationId"] = options.ApplicationId,
                ["projectId"] = options.ProjectId,
                ["messagingSenderId"] = options.MessagingSenderId,
                ["storageBucket"] = options.StorageBucket,
                ["bundleId"] = options.BundleId
            },
            cancellationToken);
    }

    // ─────────────────────────────────────────────────────────
    // Analytics
    // ─────────────────────────────────────────────────────────

    public Task LogEventAsync(
        string name,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default)
    {
        ValidateName(name);
        ArgumentNullException.ThrowIfNull(parameters);

        if (!IsMobile)
            return Task.CompletedTask;

        return InvokeAsync(
            "logEvent",
            new()
            {
                ["name"] = name,
                ["parameters"] = parameters.ToDictionary(
                    pair => pair.Key,
                    pair => (object?)pair.Value)
            },
            cancellationToken);
    }

    // ─────────────────────────────────────────────────────────
    // Crashlytics
    // ─────────────────────────────────────────────────────────

    public Task LogAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        ValidateText(message);

        return IsMobile
            ? InvokeAsync(
                "log",
                new() { ["message"] = message },
                cancellationToken)
            : Task.CompletedTask;
    }

    public Task RecordErrorAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        ValidateText(message);

        return IsMobile
            ? InvokeAsync(
                "recordError",
                new() { ["message"] = message },
                cancellationToken)
            : Task.CompletedTask;
    }

    public Task SetCustomKeyAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        ValidateName(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return IsMobile
            ? InvokeAsync(
                "setCustomKey",
                new()
                {
                    ["key"] = key,
                    ["value"] = value
                },
                cancellationToken)
            : Task.CompletedTask;
    }

    // ─────────────────────────────────────────────────────────
    // Messaging
    // ─────────────────────────────────────────────────────────

    public async Task<string> GetTokenAsync(
        CancellationToken cancellationToken = default)
    {
        if (!IsMobile)
            return "";

        var value = await InvokeAsync(
            "getToken",
            cancellationToken: cancellationToken
        ).ConfigureAwait(false);

        return value is string token &&
               token.Length is > 0 and <= 4096
            ? token
            : throw new InvalidDataException(
                "Invalid Firebase token.");
    }

    public async Task<string> RequestNotificationPermissionAsync(
        CancellationToken cancellationToken = default)
    {
        if (!IsMobile)
            return "notSupported";

        var value = await InvokeAsync(
            "requestNotificationPermission",
            cancellationToken: cancellationToken
        ).ConfigureAwait(false);

        return value is string status &&
               status.Length is > 0 and <= 32
            ? status
            : throw new InvalidDataException(
                "Invalid notification permission.");
    }

    public async IAsyncEnumerable<
        IReadOnlyDictionary<string, string>> Messages(
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        if (!IsMobile)
            yield break;

        var channel = new EventChannel(
            channels,
            MessagesChannelName);

        await foreach (var value in channel.ReadAllAsync(
            cancellationToken: cancellationToken))
        {
            if (value is not IReadOnlyDictionary<string, object?> fields)
                continue;

            var message = new Dictionary<string, string>(
                StringComparer.Ordinal);

            foreach (var (key, fieldValue) in fields)
            {
                if (fieldValue is string text)
                    message[key] = text;
            }

            yield return message;
        }
    }

    // ─────────────────────────────────────────────────────────
    // Remote Config
    // ─────────────────────────────────────────────────────────

    public Task FetchRemoteConfigAsync(
        CancellationToken cancellationToken = default)
    {
        return IsMobile
            ? InvokeAsync(
                "fetchRemoteConfig",
                cancellationToken: cancellationToken)
            : Task.CompletedTask;
    }

    public async Task<string> GetRemoteStringAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ValidateName(key);

        if (!IsMobile)
            return "";

        var value = await InvokeAsync(
            "getRemoteString",
            new() { ["key"] = key },
            cancellationToken
        ).ConfigureAwait(false);

        return value as string ?? "";
    }

    // ─────────────────────────────────────────────────────────
    // Validation
    // ─────────────────────────────────────────────────────────

    internal static void ValidateOptions(
        FirebaseAppOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

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

// ─────────────────────────────────────────────────────────────
// Application Lifecycle
// ─────────────────────────────────────────────────────────────

internal sealed class FirebaseInitializer(
    IFirebaseApp app,
    IFirebaseAnalytics analytics,
    IFirebaseMessaging messaging,
    PresentationTarget target,
    FirebaseStartupOptions options
) : IApplicationInitializer
{
    public async Task InitializeAsync(
        CancellationToken stopping)
    {
        if (target.Platform is not (
            NativePlatform.IOS or NativePlatform.Android))
            return;

        await app.InitializeAsync(stopping);

        if (options.LogAppOpen)
        {
            await analytics.LogEventAsync(
                "app_open",
                new Dictionary<string, string>(),
                stopping);
        }

        if (options.RequestNotificationPermission)
        {
            await messaging.RequestNotificationPermissionAsync(stopping);
        }
    }
}
