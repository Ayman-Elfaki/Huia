// Lightweight client-side validation for `form[data-validate]`. Uses the browser's native constraint
// validation (`required`, `type`, `maxlength`, ...) — no framework. On an invalid submit it blocks the
// post, focuses the first invalid control, and mirrors each control's `validationMessage` into an
// adjacent `[data-val-for="<name>"]` span so the message is styled like a server-side error.
(() => {
  document.querySelectorAll("form[data-validate]").forEach((form) => {
    const messageFor = (control) =>
      control.name ? form.querySelector('[data-val-for="' + CSS.escape(control.name) + '"]') : null;

    const showMessage = (control) => {
      const span = messageFor(control);
      if (span) {
        span.textContent = control.validationMessage;
      }
    };

    const clearMessage = (control) => {
      const span = messageFor(control);
      if (span) {
        span.textContent = "";
      }
    };

    form.setAttribute("novalidate", "");

    form.addEventListener(
      "submit",
      (event) => {
        if (form.checkValidity()) {
          return;
        }

        event.preventDefault();
        let firstInvalid = null;
        for (const control of form.elements) {
          if (typeof control.checkValidity !== "function" || control.disabled) {
            continue;
          }

          if (control.checkValidity()) {
            clearMessage(control);
          } else {
            showMessage(control);
            firstInvalid = firstInvalid || control;
          }
        }

        if (firstInvalid) {
          firstInvalid.focus();
        }
      },
      true,
    );

    form.addEventListener("input", (event) => {
      const control = event.target;
      if (typeof control.checkValidity === "function" && control.checkValidity()) {
        clearMessage(control);
      }
    });
  });
})();
