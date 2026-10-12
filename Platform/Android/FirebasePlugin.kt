package com.dotnative.plugins

import android.app.Activity
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import com.google.firebase.FirebaseApp
import com.google.firebase.FirebaseOptions
import com.google.firebase.analytics.FirebaseAnalytics
import com.google.firebase.crashlytics.FirebaseCrashlytics
import com.google.firebase.messaging.FirebaseMessaging
import com.google.firebase.remoteconfig.FirebaseRemoteConfig

class FirebasePlugin(private val activity: Activity) {
    init {
        val channel = NativeChannels.channel("dotnative.firebase")
        channel.handle("initialize") { args, reply ->
            activity.runOnUiThread {
                try {
                    val fields = args as? Map<*, *>
                    val apiKey = fields?.get("apiKey") as? String
                    val applicationId = fields?.get("applicationId") as? String
                    val projectId = fields?.get("projectId") as? String
                    val senderId = fields?.get("messagingSenderId") as? String
                    val bucket = fields?.get("storageBucket") as? String
                    if (apiKey.isNullOrBlank() || applicationId.isNullOrBlank() ||
                        projectId.isNullOrBlank() || senderId.isNullOrBlank() || bucket.isNullOrBlank()
                    ) {
                        reply.failure("invalid_argument", "Firebase options are incomplete")
                        return@runOnUiThread
                    }
                    if (FirebaseApp.getApps(activity).isEmpty()) {
                        val options =
                            FirebaseOptions.Builder()
                                .setApiKey(apiKey)
                                .setApplicationId(applicationId)
                                .setProjectId(projectId)
                                .setGcmSenderId(senderId)
                                .setStorageBucket(bucket)
                                .build()
                        FirebaseApp.initializeApp(activity, options)
                    }
                    reply.success()
                } catch (error: Exception) {
                    reply.failure("firebase", error.message ?: "Firebase could not start")
                }
            }
        }
        channel.handle("logEvent") { args, reply ->
            val fields = args as? Map<*, *>
            val name = fields?.get("name") as? String
            if (name.isNullOrBlank()) {
                reply.failure("invalid_argument", "An event name is required")
                return@handle
            }
            val bundle = Bundle()
            (fields?.get("parameters") as? Map<*, *>)?.forEach { (key, value) ->
                if (key is String && value is String) bundle.putString(key, value)
            }
            FirebaseAnalytics.getInstance(activity).logEvent(name, bundle)
            reply.success()
        }
        channel.handle("log") { args, reply ->
            val message = (args as? Map<*, *>)?.get("message") as? String
            if (message.isNullOrBlank()) {
                reply.failure("invalid_argument", "A message is required")
                return@handle
            }
            FirebaseCrashlytics.getInstance().log(message)
            reply.success()
        }
        channel.handle("recordError") { args, reply ->
            val message = (args as? Map<*, *>)?.get("message") as? String
            if (message.isNullOrBlank()) {
                reply.failure("invalid_argument", "An error message is required")
                return@handle
            }
            FirebaseCrashlytics.getInstance().recordException(IllegalStateException(message))
            reply.success()
        }
        channel.handle("setCustomKey") { args, reply ->
            val fields = args as? Map<*, *>
            val key = fields?.get("key") as? String
            val value = fields?.get("value") as? String
            if (key.isNullOrBlank() || value == null) {
                reply.failure("invalid_argument", "A key and a value are required")
                return@handle
            }
            FirebaseCrashlytics.getInstance().setCustomKey(key, value)
            reply.success()
        }
        channel.handle("getToken") { _, reply ->
            FirebaseMessaging.getInstance().token.addOnCompleteListener { task ->
                activity.runOnUiThread {
                    if (task.isSuccessful && !task.result.isNullOrBlank()) reply.success(task.result)
                    else reply.failure("unavailable", task.exception?.message ?: "Token request failed")
                }
            }
        }
        channel.handle("requestPermission") { _, reply ->
            val granted =
                Build.VERSION.SDK_INT < 33 ||
                    activity.checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS) ==
                        PackageManager.PERMISSION_GRANTED
            reply.success(if (granted) "authorized" else "denied")
        }
        channel.handle("fetchRemoteConfig") { _, reply ->
            FirebaseRemoteConfig.getInstance().fetchAndActivate().addOnCompleteListener { task ->
                activity.runOnUiThread {
                    if (task.isSuccessful) reply.success()
                    else reply.failure("firebase", task.exception?.message ?: "Remote Config fetch failed")
                }
            }
        }
        channel.handle("getRemoteString") { args, reply ->
            val key = (args as? Map<*, *>)?.get("key") as? String
            if (key.isNullOrBlank()) {
                reply.failure("invalid_argument", "A Remote Config key is required")
                return@handle
            }
            reply.success(FirebaseRemoteConfig.getInstance().getString(key))
        }
        NativeEventChannel("dotnative.firebase.messages", { _, sink -> FirebaseMessages.sink = sink }, { _ ->
            FirebaseMessages.sink = null
        })
    }
}
