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
`RequestPermissionAsync` asks for `POST_NOTIFICATIONS` on Android 13+.

iOS `RequestPermissionAsync` asks for alert, sound and badge, then calls
`registerForRemoteNotifications`. The device token is handed to Firebase even
when the application delegate does not implement the APNs callbacks.
`GetTokenAsync` waits for that token before returning the FCM token. The app
needs the Push Notifications entitlement (`aps-environment`) and
`remote-notification` in `UIBackgroundModes`. Foreground messages are shown
and also published on `Messages`; a tap publishes the same title and body.

iOS publish downloads Firebase 12.12.0 and links the device or simulator
slice. The first iOS publish needs the network and about 350 MB of cache under
`.firebase-ios`, which is not part of the package.

Windows, Linux and macOS skip the native SDK and return empty results.

## Service access

`services.Firebase` is the C# 14 property on `net10.0`.
`services.Firebase()` is the method on `net9.0`.
