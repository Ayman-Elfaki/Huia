// Live client-side validation for the phone sign-in form on Login.cshtml: the selected country and the
// entered phone number have to agree, checked with libphonenumber-js the same way PhoneNumberValidator
// (server-side, PhoneLoginModel.OnPostAsync) checks them with libphonenumber-csharp. This is UX only — the
// server re-validates independently regardless of what this script does. Progressive enhancement: the plain
// <input type="tel"> and the country selector's hidden input already post as-is if this script (or
// libphonenumber-js itself) never loads.
(() => {
    "use strict";

    const countrySelect = document.getElementById("huia-country-menu");
    const countryValue = document.getElementById("huia-country-value");
    const countryTrigger = document.getElementById("huia-country-trigger");
    const countryError = document.getElementById("huia-country-select-error");
    const phoneInput = document.getElementById("huia-phone-input");
    const phoneError = document.getElementById("huia-phone-input-error");
    const form = document.getElementById("huia-phone-login-form");

    if (!countrySelect || !countryValue || !phoneInput || !form) {
        return;
    }

    function isPhoneValidForCountry() {
        if (!window.libphonenumber || !countryValue.value) {
            return true; // nothing to check it against yet — validateCountry (or the server) catches this case
        }
        try {
            return window.libphonenumber.isValidPhoneNumber(phoneInput.value, countryValue.value);
        } catch {
            return false;
        }
    }

    function validateCountry() {
        const valid = countryValue.value !== "";
        countryTrigger?.setAttribute("aria-invalid", String(!valid));
        if (countryError) {
            countryError.textContent = valid ? "" : countrySelect.dataset.requiredMessage ?? "";
        }
        return valid;
    }

    function validatePhone() {
        const valid = phoneInput.value === "" || isPhoneValidForCountry();
        phoneInput.setCustomValidity(valid ? "" : (phoneInput.dataset.invalidMessage ?? "Enter a valid phone number."));
        phoneInput.setAttribute("aria-invalid", String(!valid));
        if (phoneError) {
            phoneError.textContent = valid ? "" : phoneInput.validationMessage;
        }
        return valid;
    }

    countrySelect.addEventListener("change", () => {
        validateCountry();
        if (phoneInput.value) {
            validatePhone();
        }
    });

    phoneInput.addEventListener("blur", () => {
        if (phoneInput.value) {
            validatePhone();
        }
    });

    form.addEventListener("submit", (event) => {
        const countryOk = validateCountry();
        const phoneOk = phoneInput.value === "" || validatePhone();

        if (!countryOk) {
            event.preventDefault();
            countryTrigger?.focus();
        } else if (!phoneOk) {
            event.preventDefault();
            phoneInput.reportValidity();
        }
    });
})();
