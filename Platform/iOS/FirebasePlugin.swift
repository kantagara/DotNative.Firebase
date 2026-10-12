import FirebaseAnalytics
import FirebaseCore
import FirebaseCrashlytics
import FirebaseMessaging
import FirebaseRemoteConfig
import ObjectiveC
import UIKit
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
            ApnsRegistration.prepare { message in
                if let message {
                    reply.failure("unavailable", message)
                    return
                }
                Messaging.messaging().token { token, error in
                        Task { @MainActor in
                            if let token, !token.isEmpty { reply.success(.string(token)) }
                            else {
                                reply.failure(
                                    "unavailable",
                                    error?.localizedDescription ?? "Token request failed")
                            }
                        }
                    }
                }
        }
        channel.handle("requestPermission") { _, reply in
            UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .badge, .sound]) {
                granted, _ in
                Task { @MainActor in
                    if granted { ApnsRegistration.register() }
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

    func userNotificationCenter(
        _ center: UNUserNotificationCenter,
        didReceive response: UNNotificationResponse,
        withCompletionHandler completionHandler: @escaping () -> Void
    ) {
        let content = response.notification.request.content
        Task { @MainActor in FirebaseMessages.emit(title: content.title, body: content.body) }
        completionHandler()
    }
}

/// The host AppDelegate does not implement the APNs callbacks. Forward the device
/// token into Firebase, then let an existing implementation run.
private enum ApnsRegistration {
    private static var installed = false
    private static var registerOriginal: IMP?
    private static var failureOriginal: IMP?
    private static var waiters: [Waiter] = []

    private final class Waiter {
        var finished = false
        let finish: (String?) -> Void
        init(_ finish: @escaping (String?) -> Void) { self.finish = finish }
        func complete(_ result: String?) {
            guard !finished else { return }
            finished = true
            finish(result)
        }
    }

    static func register() {
        install()
        UIApplication.shared.registerForRemoteNotifications()
    }

    static func prepare(_ done: @escaping (String?) -> Void) {
        Task { @MainActor in
            install()
            if Messaging.messaging().apnsToken != nil {
                done(nil)
                return
            }
            let waiter = Waiter(done)
            waiters.append(waiter)
            UIApplication.shared.registerForRemoteNotifications()
            Task { @MainActor in
                try? await Task.sleep(nanoseconds: 12_000_000_000)
                guard !waiter.finished else { return }
                waiters.removeAll { $0 === waiter }
                waiter.complete("APNs registration did not complete")
            }
        }
    }

    private static func install() {
        guard !installed, let delegate = UIApplication.shared.delegate else { return }
        let cls: AnyClass = object_getClass(delegate)!
        hook(
            cls, "application:didRegisterForRemoteNotificationsWithDeviceToken:", "v@:@@",
            { original in registerOriginal = original },
            { (obj: AnyObject, app: UIApplication, token: Data) in
                let previous = registerOriginal
                let sel = sel_registerName(
                    "application:didRegisterForRemoteNotificationsWithDeviceToken:")
                Task { @MainActor in
                    Messaging.messaging().apnsToken = token
                    finish(nil)
                }
                if let previous {
                    unsafeBitCast(
                        previous,
                        to: (@convention(c) (AnyObject, Selector, UIApplication, Data) -> Void).self
                    )(obj, sel, app, token)
                }
            } as @convention(block) (AnyObject, UIApplication, Data) -> Void as AnyObject)
        hook(
            cls, "application:didFailToRegisterForRemoteNotificationsWithError:", "v@:@@",
            { original in failureOriginal = original },
            { (obj: AnyObject, app: UIApplication, error: NSError) in
                let previous = failureOriginal
                let sel = sel_registerName(
                    "application:didFailToRegisterForRemoteNotificationsWithError:")
                let message = error.localizedDescription
                Task { @MainActor in finish(message) }
                if let previous {
                    unsafeBitCast(
                        previous,
                        to: (@convention(c) (AnyObject, Selector, UIApplication, NSError) -> Void)
                            .self
                    )(obj, sel, app, error)
                }
            } as @convention(block) (AnyObject, UIApplication, NSError) -> Void as AnyObject)
        installed = true
    }

    private static func hook(
        _ cls: AnyClass, _ name: String, _ encoding: String,
        _ remember: (IMP?) -> Void,
        _ block: AnyObject
    ) {
        let sel = sel_registerName(name)
        let imp = imp_implementationWithBlock(block)
        let added = encoding.withCString { class_addMethod(cls, sel, imp, $0) }
        if added {
            remember(nil)
        } else if let method = class_getInstanceMethod(cls, sel) {
            remember(method_getImplementation(method))
            method_setImplementation(method, imp)
        }
    }

    private static func finish(_ result: String?) {
        let pending = waiters
        waiters.removeAll()
        pending.forEach { $0.complete(result) }
    }
}

private enum FirebaseMessages {
    static var sink: NativeEventSink?
    @MainActor static func emit(title: String, body: String) {
        sink?.success(.map(["title": .string(title), "body": .string(body)]))
    }
}
