# DotNative.Firebase

Calls the Firebase Android and iOS SDKs from the native plugin. C# talks to the
channel `dotnative.firebase`.

```csharp
builder.Services.AddFirebase();
await services.Firebase.InitializeAsync(options);
await services.Firebase.LogEventAsync("app_open", new Dictionary<string, string>());
var token = await services.Firebase.GetTokenAsync();
await services.Firebase.FetchRemoteConfigAsync();
var ads = await services.Firebase.GetRemoteStringAsync("InGamePartnershipAds");
```

Android resolves `com.google.firebase:firebase-bom:34.12.0` through the app
Gradle file. Pass `FirebaseAppOptions` from the application. The app manifest
still needs a `FirebasePushService` entry so background messages arrive.

iOS publish downloads Firebase 12.12.0 and links the device or simulator
slice. The first iOS publish needs the network and about 350 MB of cache under
`.firebase-ios`, which is not part of the package.

Windows, Linux and macOS skip the native SDK and return empty results.

## Service access

`services.Firebase` is the C# 14 property on `net10.0`.
`services.Firebase()` is the method on `net9.0`.
