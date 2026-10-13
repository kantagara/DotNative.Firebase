using System;
using Microsoft.Extensions.DependencyInjection;

namespace DotNative.Firebase;

public static class FirebaseServiceProviderExtensions
{
#if NET10_0_OR_GREATER
    extension(IServiceProvider services)
    {
        /// <summary>Resolves the shared Firebase app using the provider's DI lifetime.</summary>
        public IFirebaseApp FirebaseApp => services.GetRequiredService<IFirebaseApp>();

        /// <summary>Resolves Firebase Analytics using the provider's DI lifetime.</summary>
        public IFirebaseAnalytics FirebaseAnalytics => services.GetRequiredService<IFirebaseAnalytics>();

        /// <summary>Resolves Firebase Crashlytics using the provider's DI lifetime.</summary>
        public IFirebaseCrashlytics FirebaseCrashlytics =>
            services.GetRequiredService<IFirebaseCrashlytics>();

        /// <summary>Resolves Firebase Cloud Messaging using the provider's DI lifetime.</summary>
        public IFirebaseMessaging FirebaseMessaging => services.GetRequiredService<IFirebaseMessaging>();

        /// <summary>Resolves Firebase Remote Config using the provider's DI lifetime.</summary>
        public IFirebaseRemoteConfig FirebaseRemoteConfig =>
            services.GetRequiredService<IFirebaseRemoteConfig>();
    }
#else
    /// <summary>Resolves the shared Firebase app using the provider's DI lifetime.</summary>
    public static IFirebaseApp FirebaseApp(this IServiceProvider services) =>
        services.GetRequiredService<IFirebaseApp>();

    /// <summary>Resolves Firebase Analytics using the provider's DI lifetime.</summary>
    public static IFirebaseAnalytics FirebaseAnalytics(this IServiceProvider services) =>
        services.GetRequiredService<IFirebaseAnalytics>();

    /// <summary>Resolves Firebase Crashlytics using the provider's DI lifetime.</summary>
    public static IFirebaseCrashlytics FirebaseCrashlytics(this IServiceProvider services) =>
        services.GetRequiredService<IFirebaseCrashlytics>();

    /// <summary>Resolves Firebase Cloud Messaging using the provider's DI lifetime.</summary>
    public static IFirebaseMessaging FirebaseMessaging(this IServiceProvider services) =>
        services.GetRequiredService<IFirebaseMessaging>();

    /// <summary>Resolves Firebase Remote Config using the provider's DI lifetime.</summary>
    public static IFirebaseRemoteConfig FirebaseRemoteConfig(this IServiceProvider services) =>
        services.GetRequiredService<IFirebaseRemoteConfig>();
#endif
}
