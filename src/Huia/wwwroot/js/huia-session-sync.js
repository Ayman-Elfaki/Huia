// Cross-tab sign-in/out sync for Huia's own Identity UI pages. If a user signs in or out in one browser
// tab that has a Huia page open, this notifies any other Huia tab so a stale Login/Register form doesn't
// sit there after the user is already (or no longer) authenticated elsewhere.
//
// All of Huia's pages are same-origin with each other, so BroadcastChannel — same-origin only, no iframe
// needed — is enough. The "storage" event, which fires in every tab but the one that wrote the key, covers
// the handful of browsers (older Safari) without BroadcastChannel.
(() => {
    "use strict";

    const CHANNEL_NAME = "huia:session";
    const STORAGE_KEY = "huia:authenticated";

    const body = document.body;
    const isAuthenticated = body.dataset.huiaAuthenticated === "true";
    const reactToSignIn = body.dataset.huiaReactToSignIn === "true";

    function handleAuthenticatedChange(authenticated) {
        if (authenticated && reactToSignIn) {
            // A stale Login/Register form — the user signed in elsewhere. Reload so the server-side
            // already-authenticated check (see Login/Register OnGet) redirects past it.
            location.reload();
        }
    }

    let lastKnown;
    try {
        lastKnown = localStorage.getItem(STORAGE_KEY);
        localStorage.setItem(STORAGE_KEY, String(isAuthenticated));
    } catch (_) {
        // Storage can throw in some privacy modes; cross-tab sync just won't happen — sign-in/out still work.
        return;
    }

    const changed = lastKnown !== null && lastKnown !== String(isAuthenticated);

    if ("BroadcastChannel" in window) {
        const channel = new BroadcastChannel(CHANNEL_NAME);
        if (changed) {
            channel.postMessage(isAuthenticated);
        }
        channel.addEventListener("message", (event) => handleAuthenticatedChange(event.data));
    } else {
        window.addEventListener("storage", (event) => {
            if (event.key === STORAGE_KEY) {
                handleAuthenticatedChange(event.newValue === "true");
            }
        });
    }
})();
