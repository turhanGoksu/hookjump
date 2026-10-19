// The hookjump.app launcher. `hookjump install` compiles it on the listener's
// Mac, so the package ships source only, never a binary.
//
// Why native code: macOS asks permission (to control Spotify) in the name of
// the app responsible for a process. Started as this launcher's child, Python
// runs as "hookjump", not as "Python", so the permission is hookjump's alone.
//
//   hookjump              start the menu bar app (Python, as a child)
//   hookjump --login X    X = on, off or status: open at login (SMAppService)

#include <libgen.h>
#include <mach-o/dyld.h>
#include <objc/message.h>
#include <objc/runtime.h>
#include <spawn.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/wait.h>

extern char **environ;

// Shows an alert: a failed start must not be silent.
static void complain(const char *message) {
    char script[2048];
    snprintf(script, sizeof script,
             "display alert \"hookjump could not start\" message \"%s\"", message);
    char *args[] = {"osascript", "-e", script, NULL};
    pid_t pid;
    if (posix_spawn(&pid, "/usr/bin/osascript", NULL, NULL, args, environ) == 0)
        waitpid(pid, NULL, 0);
}

// Open at login, through SMAppService (macOS 13 and later). It must be asked by
// this executable: the service is the main app of whoever asks.
static int login(const char *what) {
    Class cls = objc_getClass("SMAppService");
    if (!cls) {
        fprintf(stderr, "Open at login needs macOS 13 or later.\n");
        return 2;
    }
    id service = ((id (*)(Class, SEL))objc_msgSend)(cls, sel_registerName("mainAppService"));
    if (strcmp(what, "status") == 0) {
        // 0 not registered, 1 enabled, 2 requires approval, 3 not found
        printf("%ld\n", ((long (*)(id, SEL))objc_msgSend)(service, sel_registerName("status")));
        return 0;
    }
    int on = strcmp(what, "on") == 0;
    if (!on && strcmp(what, "off") != 0) {
        fprintf(stderr, "--login takes on, off or status\n");
        return 2;
    }
    SEL change = sel_registerName(on ? "registerAndReturnError:" : "unregisterAndReturnError:");
    id error = NULL;
    if (((BOOL (*)(id, SEL, id *))objc_msgSend)(service, change, &error)) return 0;
    id text = ((id (*)(id, SEL))objc_msgSend)(error, sel_registerName("localizedDescription"));
    fprintf(stderr, "%s\n", ((const char *(*)(id, SEL))objc_msgSend)(text, sel_registerName("UTF8String")));
    return 1;
}

int main(int argc, char *argv[]) {
    if (argc == 3 && strcmp(argv[1], "--login") == 0) return login(argv[2]);

    char exe[4096];
    uint32_t size = sizeof exe;
    if (_NSGetExecutablePath(exe, &size) != 0) return 1;
    char macos[4096], bundle[4096], config[4096];
    snprintf(macos, sizeof macos, "%s", dirname(exe));            // .../Contents/MacOS
    snprintf(config, sizeof config, "%s/../Resources/python-path", macos);
    snprintf(bundle, sizeof bundle, "%s/../..", macos);           // .../hookjump.app

    // The Python that has hookjump installed, written here by `hookjump install`.
    char python[4096];
    FILE *file = fopen(config, "r");
    if (!file || !fgets(python, sizeof python, file)) {
        complain("Its settings are missing. Run hookjump install again.");
        return 1;
    }
    fclose(file);
    python[strcspn(python, "\n")] = 0;

    char resolved[4096];
    setenv("HOOKJUMP_BUNDLE", realpath(bundle, resolved) ? resolved : bundle, 1);
    char *args[] = {python, "-m", "hookjump", "run", NULL};
    pid_t pid;
    if (posix_spawn(&pid, python, NULL, NULL, args, environ) != 0) {
        complain("The Python it was installed with is gone. Install hookjump again, then run hookjump install.");
        return 1;
    }
    int status;
    waitpid(pid, &status, 0);
    return WIFEXITED(status) ? WEXITSTATUS(status) : 1;
}
