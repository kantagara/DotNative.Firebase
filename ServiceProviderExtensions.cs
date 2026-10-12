using System;
using Microsoft.Extensions.DependencyInjection;

namespace DotNative.Firebase;

public static class FirebaseServiceProviderExtensions
{
#if NET10_0_OR_GREATER
    extension(IServiceProvider services)
    {
        /// <summary>Resolves the registered plugin using the provider's DI lifetime.</summary>
        public IFirebase Firebase => services.GetRequiredService<IFirebase>();
    }
#else
    /// <summary>Resolves the registered plugin using the provider's DI lifetime.</summary>
    public static IFirebase Firebase(this IServiceProvider services) =>
        services.GetRequiredService<IFirebase>();
#endif
}
