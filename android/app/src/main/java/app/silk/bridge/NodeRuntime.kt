package app.silk.bridge

/** JNI entry points into nodejs-mobile. Node can be started once per process. */
internal object NodeRuntime {
    init {
        System.loadLibrary("node")
        System.loadLibrary("silkbridge")
    }

    external fun setEnv(key: String, value: String)

    /** Runs Node with these arguments on the calling thread; returns only if Node exits. */
    external fun start(arguments: Array<String>): Int
}
