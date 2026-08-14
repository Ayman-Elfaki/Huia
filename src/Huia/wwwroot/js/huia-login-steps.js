// Drives Login.cshtml's two-step email -> password flow. Both fields still live in, and submit as, a single
// <form> post (LoginModel.OnPostAsync is unchanged) — this only toggles which step is visible, so no extra
// request round trip is needed between steps. Progressive enhancement: Login.cshtml renders both steps open
// by default, so a page that never runs this script (or fails the early-return checks below) still works as
// a classic single-step form with both fields visible at once.
(() => {
    "use strict";

    const form = document.getElementById("huia-login-form");
    const emailStep = document.getElementById("huia-login-step-email");
    const passwordStep = document.getElementById("huia-login-step-password");
    const nextButton = document.getElementById("huia-login-next");
    const changeEmailButton = document.getElementById("huia-login-change-email");
    const emailInput = document.getElementById("Input_Email");
    const passwordInput = document.getElementById("Input_Password");
    const identityEmail = document.getElementById("huia-login-identity-email");
    const altOptions = document.getElementById("huia-login-alt-options");

    if (!form || !emailStep || !passwordStep || !nextButton || !emailInput) {
        return;
    }

    function showPasswordStep() {
        if (identityEmail) {
            identityEmail.textContent = emailInput.value;
        }
        emailStep.hidden = true;
        passwordStep.hidden = false;
        if (altOptions) {
            altOptions.hidden = true;
        }
    }

    function showEmailStep() {
        passwordStep.hidden = true;
        emailStep.hidden = false;
        if (altOptions) {
            altOptions.hidden = false;
        }
        emailInput.focus();
    }

    nextButton.addEventListener("click", () => {
        if (!emailInput.checkValidity()) {
            emailInput.reportValidity();
            return;
        }
        showPasswordStep();
        passwordInput?.focus();
    });

    // The email field is a plain <input>, not a submit button, so Enter would otherwise do nothing.
    emailInput.addEventListener("keydown", (event) => {
        if (event.key === "Enter") {
            event.preventDefault();
            nextButton.click();
        }
    });

    changeEmailButton?.addEventListener("click", showEmailStep);

    // A page-level error (ModelState.AddModelError(string.Empty, ...) in LoginModel.OnPostAsync — e.g.
    // "Invalid email or password.") means the email step already passed server-side validation on this
    // postback, so land back on the password step instead of collapsing to step one and losing that
    // context. Anything else (a fresh load, or a property-level error on Email itself) starts at step one.
    const hasPageLevelError = document.querySelector(".huia-validation-summary li") !== null;
    if (hasPageLevelError) {
        showPasswordStep();
    } else {
        passwordStep.hidden = true;
    }
})();
