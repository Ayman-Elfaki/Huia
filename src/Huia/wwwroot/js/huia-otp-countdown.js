// Drives the Resend-code countdown on PhoneLoginVerify.cshtml. The starting second count comes from the
// server (PhoneLoginVerifyModel.SecondsUntilResendAllowed, derived from the pending cookie's own IssuedUtc)
// so it's correct even after a page reload or back-navigation; this script only ticks it down and flips the
// button over once it reaches zero — it never decides on its own whether a resend is actually allowed
// (IPhoneOtpRateLimiter is still the real enforcement, server-side).
(() => {
    "use strict";

    const countdown = document.getElementById("huia-otp-countdown");
    const button = document.getElementById("huia-otp-resend-button");

    if (!countdown || !button) {
        return;
    }

    let seconds = Number.parseInt(countdown.dataset.seconds, 10) || 0;
    const template = countdown.dataset.template || "{0}";

    function render() {
        if (seconds <= 0) {
            countdown.hidden = true;
            button.disabled = false;
            return;
        }

        countdown.hidden = false;
        countdown.textContent = template.replace("{0}", String(seconds));
        button.disabled = true;
    }

    render();

    if (seconds <= 0) {
        return;
    }

    const timer = setInterval(() => {
        seconds -= 1;
        render();
        if (seconds <= 0) {
            clearInterval(timer);
        }
    }, 1000);
})();
