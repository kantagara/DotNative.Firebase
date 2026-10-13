# DotNative.Firebase

Calls the Firebase Android and iOS SDKs from the native plugin. C# talks to the
channel `dotnative.firebase`.

```csharp
builder.Services.AddFirebase(target => new FirebaseAppOptions(
    ApiKey: "...", ApplicationId: "...", ProjectId: "...",
    MessagingSenderId: "...", StorageBucket: "...", BundleId: "..."));
builder.Build().Run<App>(); // Firebase initializes automatically after mounting the UI.
```

Android resolves `com.google.firebase:firebase-bom:34.12.0` through the app
Gradle file. Pass `FirebaseAppOptions` from the application. The app manifest
still needs a `FirebasePushService` entry so background messages arrive.

iOS hands the device token to Firebase even when the application delegate does
not implement the APNs callbacks. `GetTokenAsync` waits for that token before
returning the FCM token. The app needs the Push Notifications entitlement
(`aps-environment`) and `remote-notification` in `UIBackgroundModes`. Foreground
messages are shown and also published on `Messages`; a tap publishes the same
title and body. Notification permission is a separate call, described below.

iOS publish downloads Firebase 12.12.0 and links the device or simulator
slice. The first iOS publish needs the network and about 350 MB of cache under
`.firebase-ios`, which is not part of the package.

Windows, Linux and macOS skip the native SDK and return empty results.

## Products

Registration creates one Firebase app and exposes each SDK product separately.
All five services are the same instance, so initialization happens once.

| Service | SDK product | Calls |
| --- | --- | --- |
| `IFirebaseApp` | `FirebaseApp` | `InitializeAsync` |
| `IFirebaseAnalytics` | Analytics | `LogEventAsync` |
| `IFirebaseCrashlytics` | Crashlytics | `LogAsync`, `RecordErrorAsync`, `SetCustomKeyAsync` |
| `IFirebaseMessaging` | Cloud Messaging | `RequestNotificationPermissionAsync`, `GetTokenAsync`, `Messages` |
| `IFirebaseRemoteConfig` | Remote Config | `FetchRemoteConfigAsync`, `GetRemoteStringAsync` |

`LogEventAsync` is an Analytics event. `LogAsync` is a Crashlytics breadcrumb
attached to a later crash. A screen injects only the product it uses.

## Service access

On `net10.0` the C# 14 properties are `services.FirebaseApp`,
`services.FirebaseAnalytics`, `services.FirebaseCrashlytics`,
`services.FirebaseMessaging`, and `services.FirebaseRemoteConfig`. On `net9.0`
those names are methods.

Configuration belongs at registration. The target factory allows iOS/Android
options to be selected in the application's composition root, and is not invoked
on desktop. A single `FirebaseAppOptions` can also be passed directly. The plugin
does not read environment variables. `InitializeAsync()` uses the registered
configuration; the older explicit-options overload remains available.

Configured registration installs an `IApplicationInitializer`; no manual service
resolution or InitializeAsync call is needed. `FirebaseStartupOptions` optionally
enables app-open logging and the notification permission request (both default off).
Initialization observes application shutdown. The framework exposes completion
and failures through `DotNativeApplication.Initialization`.

Feature code that needs Firebase immediately after startup can await
`application.Initialization` before requesting a token or fetching remote config.
This is a readiness wait, not a second initialization call.

## Notification permission

`RequestNotificationPermissionAsync` shows the system notification dialog. Firebase
initialization, analytics, crash reporting, and Remote Config do not need it.
`MessagingSenderId` in `FirebaseAppOptions` is the FCM sender id used while
initializing the app. It stays required whether or not the app asks for
notification permission.

Ask during startup with the flag. Both flags default to false. When
`RequestNotificationPermission` is true, the initializer makes this same call
once, after `InitializeAsync`:

```csharp
services.AddFirebase(
    options,
    startup: new FirebaseStartupOptions(RequestNotificationPermission: true)
);
```

The method is public on `IFirebaseMessaging`, so feature code can ask at the
moment notifications are needed:

```csharp
var status = await messaging.RequestNotificationPermissionAsync();
```

Leave the startup flag false when the prompt belongs later. A later call after
the user has already answered does not show the dialog again.

Android 13+ requests `POST_NOTIFICATIONS`. Older Android versions, and a
permission that is already granted, return `"authorized"` without a dialog.
iOS requests alert, sound, and badge, then calls `registerForRemoteNotifications`
when the user allows it. A refusal returns `"denied"`. Windows, Linux, and
macOS return `"notSupported"` and do not call the native SDK.

The status is not an FCM token. After the user allows notifications,
`GetTokenAsync` returns the token. Code that does that immediately after
startup should await `application.Initialization` first.

Notification taps on iOS forward their string data payload to the optional
`dotnative_open_activation` runtime export. Frameworks with
`IApplicationActivations` receive these as notification activations; older hosts
continue receiving the existing title/body message stream. Foreground delivery
does not trigger navigation. Applications must validate destinations and access.
