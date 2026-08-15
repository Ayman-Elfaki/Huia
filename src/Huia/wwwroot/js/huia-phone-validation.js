// Live client-side formatting/validation for the phone sign-in form on Login.cshtml, using libphonenumber-js.
// This is UX only — PhoneNumberValidator (server-side, PhoneLoginModel.OnPostAsync) is the real security
// boundary and re-validates independently regardless of what this script does. Progressive enhancement: the
// plain <input type="tel"> already posts as-is if this script (or libphonenumber-js itself) never loads.
(() => {
    "use strict";

    const input = document.getElementById("huia-phone-input");
    const error = document.getElementById("huia-phone-input-error");
    const form = document.getElementById("huia-phone-login-form");

    if (!input || !form || !window.libphonenumber) {
        return;
    }

    function isValid() {
        try {
            return window.libphonenumber.isValidPhoneNumber(input.value);
        } catch {
            return false;
        }
    }

    input.addEventListener("blur", () => {
        if (input.value && !isValid()) {
            input.setCustomValidity("Enter a valid phone number, including country code.");
            if (error) {
                error.textContent = input.validationMessage;
            }
        } else {
            input.setCustomValidity("");
            if (error) {
                error.textContent = "";
            }
        }
    });

    form.addEventListener("submit", (event) => {
        if (input.value && !isValid()) {
            event.preventDefault();
            input.reportValidity();
        }
    });
})();
