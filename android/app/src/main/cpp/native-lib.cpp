// Starts the bundled Node runtime (nodejs-mobile) inside the app process.
// Node may only be started once per process; the Kotlin side guarantees that.

#include <jni.h>
#include <android/log.h>
#include <pthread.h>
#include <unistd.h>

#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

#include "node.h"

namespace {

const char *kTag = "SilkNode";

// Node writes to stdout and stderr; on Android those go nowhere, so they are piped into logcat.
int g_pipe[2];

void *PumpLog(void *) {
    char buffer[2048];
    std::string line;
    ssize_t read_size;
    while ((read_size = read(g_pipe[0], buffer, sizeof buffer)) > 0) {
        line.append(buffer, static_cast<size_t>(read_size));
        size_t newline;
        while ((newline = line.find('\n')) != std::string::npos) {
            __android_log_write(ANDROID_LOG_INFO, kTag, line.substr(0, newline).c_str());
            line.erase(0, newline + 1);
        }
    }
    return nullptr;
}

void RedirectOutput() {
    setvbuf(stdout, nullptr, _IOLBF, 0);
    setvbuf(stderr, nullptr, _IONBF, 0);
    if (pipe(g_pipe) != 0) return;
    dup2(g_pipe[1], STDOUT_FILENO);
    dup2(g_pipe[1], STDERR_FILENO);
    pthread_t thread;
    if (pthread_create(&thread, nullptr, PumpLog, nullptr) == 0) pthread_detach(thread);
}

}  // namespace

extern "C" JNIEXPORT void JNICALL
Java_app_silk_bridge_NodeRuntime_setEnv(JNIEnv *env, jobject, jstring key, jstring value) {
    const char *k = env->GetStringUTFChars(key, nullptr);
    const char *v = env->GetStringUTFChars(value, nullptr);
    setenv(k, v, 1);
    env->ReleaseStringUTFChars(key, k);
    env->ReleaseStringUTFChars(value, v);
}

extern "C" JNIEXPORT jint JNICALL
Java_app_silk_bridge_NodeRuntime_start(JNIEnv *env, jobject, jobjectArray arguments) {
    const jsize count = env->GetArrayLength(arguments);
    std::vector<std::string> values;
    size_t total = 0;
    for (jsize i = 0; i < count; i++) {
        auto item = static_cast<jstring>(env->GetObjectArrayElement(arguments, i));
        const char *chars = env->GetStringUTFChars(item, nullptr);
        values.emplace_back(chars);
        env->ReleaseStringUTFChars(item, chars);
        env->DeleteLocalRef(item);
        total += values.back().size() + 1;
    }

    // Node expects argv to be one contiguous block, and keeps pointers into it for the life of the process.
    auto *block = static_cast<char *>(calloc(total, 1));
    auto **argv = static_cast<char **>(calloc(static_cast<size_t>(count) + 1, sizeof(char *)));
    char *cursor = block;
    for (jsize i = 0; i < count; i++) {
        memcpy(cursor, values[i].c_str(), values[i].size());
        argv[i] = cursor;
        cursor += values[i].size() + 1;
    }

    RedirectOutput();
    return node::Start(count, argv);
}
