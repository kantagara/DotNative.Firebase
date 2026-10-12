import FirebaseAnalytics
import FirebaseCore
import FirebaseCrashlytics
import FirebaseMessaging
import FirebaseRemoteConfig
import UserNotifications

@MainActor final class FirebasePlugin {
    private static var instance: FirebasePlugin?
    static func register() {
        if instance == nil { instance = FirebasePlugin() }
    }

    private init() {
        let channel = NativeChannels.channel("dotnative.firebase")
        channel.handle("initialize") { arguments, reply in
            let fields = arguments.fields
            guard let apiKey = fields["apiKey"]?.string, !apiKey.isEmpty,
                let applicationId = fields["applicationId"]?.string, !applicationId.isEmpty,
                let projectId = fields["projectId"]?.string, !projectId.isEmpty,
                let senderId = fields["messagingSenderId"]?.string, !senderId.isEmpty,
                let bucket = fields["storageBucket"]?.string, !bucket.isEmpty
            else {
                reply.failure("invalid_argument", "Firebase options are incomplete")
                return
            }
            if FirebaseApp.app() == nil {
                let options = FirebaseOptions(googleAppID: applicationId, gcmSenderID: senderId)
                options.apiKey = apiKey
                options.projectID = projectId
                options.storageBucket = bucket
                if let bundleId = fields["bundleId"]?.string, !bundleId.isEmpty {
                    options.bundleID = bundleId
                }
                FirebaseApp.configure(options: options)
            }
            reply.success()
        }
        channel.handle("logEvent") { arguments, reply in
            let fields = arguments.fields
            guard let name = fields["name"]?.string, !name.isEmpty else {
                reply.failure("invalid_argument", "An event name is required")
                return
            }
            var parameters: [String: Any] = [:]
            for (key, value) in fields["parameters"]?.fields ?? [:] {
                if let text = value.string { parameters[key] = text }
            }
            Analytics.logEvent(name, parameters: parameters)
            reply.success()
        }
        channel.handle("log") { arguments, reply in
            guard let message = arguments.fields["message"]?.string, !message.isEmpty else {
                reply.failure("invalid_argument", "A message is required")
                return
            }
            Crashlytics.crashlytics().log(message)
            reply.success()
        }
        channel.handle("recordError") { arguments, reply in
            guard let message = arguments.fields["message"]?.string, !message.isEmpty else {
                reply.failure("invalid_argument", "An error message is required")
                return
            }
            Crashlytics.crashlytics().record(
                error: NSError(
                    domain: "DotNative.Firebase", code: 1,
                    userInfo: [NSLocalizedDescriptionKey: message]))
            reply.success()
        }
        channel.handle("setCustomKey") { arguments, reply in
            let fields = arguments.fields
            guard let key = fields["key"]?.string, !key.isEmpty,
                let value = fields["value"]?.string
            else {
                reply.failure("invalid_argument", "A key and a value are required")
                return
            }
            Crashlytics.crashlytics().setCustomValue(value, forKey: key)
            reply.success()
        }
        channel.handle("getToken") { _, reply in
            Messaging.messaging().token { token, error in
                Task { @MainActor in
                    if let token, !token.isEmpty { reply.success(.string(token)) }
                    else {
                        reply.failure("unavailable", error?.localizedDescription ?? "Token request failed")
                    }
                }
            }
        }
        channel.handle("requestPermission") { _, reply in
            UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .badge, .sound]) {
                granted, _ in
                Task { @MainActor in
                    reply.success(.string(granted ? "authorized" : "denied"))
                }
            }
        }
        channel.handle("fetchRemoteConfig") { _, reply in
            RemoteConfig.remoteConfig().fetchAndActivate { _, error in
                Task { @MainActor in
                    if let error {
                        reply.failure("firebase", error.localizedDescription)
                    } else {
                        reply.success()
                    }
                }
            }
        }
        channel.handle("getRemoteString") { arguments, reply in
            guard let key = arguments.fields["key"]?.string, !key.isEmpty else {
                reply.failure("invalid_argument", "A Remote Config key is required")
                return
            }
            reply.success(.string(RemoteConfig.remoteConfig().configValue(forKey: key).stringValue))
        }
        _ = NativeEventChannel(
            "dotnative.firebase.messages",
            onListen: { _, sink in FirebaseMessages.sink = sink },
            onCancel: { _ in FirebaseMessages.sink = nil }
        )
        UNUserNotificationCenter.current().delegate = FirebaseNotificationCenter.shared
    }
}

private final class FirebaseNotificationCenter: NSObject, UNUserNotificationCenterDelegate {
    static let shared = FirebaseNotificationCenter()

    func userNotificationCenter(
        _ center: UNUserNotificationCenter,
        willPresent notification: UNNotification,
        withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void
    ) {
        let title = notification.request.content.title
        let body = notification.request.content.body
        Task { @MainActor in FirebaseMessages.emit(title: title, body: body) }
        completionHandler([.banner, .sound])
    }
}

private enum FirebaseMessages {
    static var sink: NativeEventSink?
    @MainActor static func emit(title: String, body: String) {
        sink?.success(.map(["title": .string(title), "body": .string(body)]))
    }
}
