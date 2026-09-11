// Progressive enhancement: client-side phone validation with libphonenumber-js, ahead of the
// server-side check (Huia.AspNetCore.Services.IPhoneNumberService, which stays authoritative).
(() => {
  const lib = window.libphonenumber;
  if (!lib) {
    return;
  }

  document.querySelectorAll("form.huia-country").forEach((form) => {
    const number = form.querySelector('input[type="tel"]');
    if (!number) {
      return;
    }

    // The combined login page uses a Basecoat listbox (its value lives on a hidden input); the
    // standalone page uses a native <select>. `[name="Input.Country"]` covers both.
    const countryField = form.querySelector('[name="Input.Country"]');
    const nativeSelect = form.querySelector("[data-huia-country-select]");
    const flag = form.querySelector("[data-huia-country-flag]");

    if (nativeSelect && flag) {
      const syncFlag = () => {
        flag.className = nativeSelect.value
          ? `fi huia-country-flag fi-${nativeSelect.value.toLowerCase()}`
          : "fi huia-country-flag";
      };
      nativeSelect.addEventListener("change", syncFlag);
      syncFlag();
    }

    const hint = form.querySelector('[data-testid="phone-number-hint"]');

    const validate = () => {
      const value = number.value.trim();
      if (!value) {
        number.setCustomValidity("");
        if (hint) {
          hint.textContent = "";
        }
        return;
      }

      const region = (countryField && countryField.value) || undefined;
      const ok = lib.isValidNumber(value, region) || (value.startsWith("+") && lib.isValidNumber(value));
      const message = number.dataset.invalidMessage || "Enter a valid phone number.";
      number.setCustomValidity(ok ? "" : message);
      if (hint) {
        hint.textContent = ok ? "" : message;
      }
    };

    number.addEventListener("input", validate);
    form.addEventListener("change", validate);
    form.addEventListener("submit", validate);
  });
})();
