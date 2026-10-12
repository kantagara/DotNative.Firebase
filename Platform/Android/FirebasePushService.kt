package com.dotnative.plugins

import com.google.firebase.messaging.FirebaseMessagingService
import com.google.firebase.messaging.RemoteMessage

class FirebasePushService : FirebaseMessagingService() {
    override fun onMessageReceived(message: RemoteMessage) {
        FirebaseMessages.sink?.success(
            mapOf(
                "title" to (message.notification?.title ?: message.data["title"].orEmpty()),
                "body" to (message.notification?.body ?: message.data["body"].orEmpty()),
            )
        )
    }
}

internal object FirebaseMessages {
    var sink: NativeEventSink? = null
}
