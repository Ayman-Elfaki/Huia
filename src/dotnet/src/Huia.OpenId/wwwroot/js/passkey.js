// Progressive enhancement for passkey (WebAuthn) discoverable sign-in, the post-sign-up enrollment
// prompt and credential management. Feature detection happens pre-paint in the layout
// (html[data-webauthn]); this script only wires behaviour. No bundler: plain browser APIs.
(() => {
  const supported =
    typeof window.PublicKeyCredential === "function" &&
    typeof navigator.credentials?.get === "function";

  const strings = (() => {
    try {
      return JSON.parse(document.querySelector("[data-huia-passkey-strings]")?.textContent || "{}");
    } catch {
      return {};
    }
  })();

  const b64urlToBytes = (value) => {
    const pad = value.length % 4 === 0 ? "" : "=".repeat(4 - (value.length % 4));
    const binary = atob(value.replace(/-/g, "+").replace(/_/g, "/") + pad);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i += 1) {
      bytes[i] = binary.charCodeAt(i);
    }
    return bytes.buffer;
  };

  const bytesToB64url = (buffer) => {
    const bytes = new Uint8Array(buffer);
    let binary = "";
    for (let i = 0; i < bytes.length; i += 1) {
      binary += String.fromCharCode(bytes[i]);
    }
    return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
  };

  const toCreationOptions = (options) => {
    options.challenge = b64urlToBytes(options.challenge);
    options.user.id = b64urlToBytes(options.user.id);
    (options.excludeCredentials || []).forEach((c) => {
      c.id = b64urlToBytes(c.id);
    });
    return options;
  };

  const toRequestOptions = (options) => {
    options.challenge = b64urlToBytes(options.challenge);
    (options.allowCredentials || []).forEach((c) => {
      c.id = b64urlToBytes(c.id);
    });
    return options;
  };

  const serializeCredential = (credential) => {
    const response = credential.response;
    const json = {
      id: credential.id,
      rawId: bytesToB64url(credential.rawId),
      type: credential.type,
      clientExtensionResults: credential.getClientExtensionResults ? credential.getClientExtensionResults() : {},
      response: {
        clientDataJSON: bytesToB64url(response.clientDataJSON),
      },
    };
    if (response.attestationObject) {
      json.response.attestationObject = bytesToB64url(response.attestationObject);
      if (typeof response.getTransports === "function") {
        json.response.transports = response.getTransports();
      }
    }
    if (response.authenticatorData) {
      json.response.authenticatorData = bytesToB64url(response.authenticatorData);
      json.response.signature = bytesToB64url(response.signature);
      json.response.userHandle = response.userHandle ? bytesToB64url(response.userHandle) : null;
    }
    return json;
  };

  // A friendly default name for a freshly created credential.
  const guessDeviceName = (credential) => {
    const device = strings.device || {};
    const transports = credential.response?.getTransports?.() || [];
    const attachment = credential.authenticatorAttachment;
    if (transports.includes("hybrid")) {
      return device.phone || "Phone";
    }
    if (transports.some((t) => t === "usb" || t === "nfc" || t === "ble")) {
      return device.securityKey || "Security key";
    }
    if (transports.includes("internal") || attachment === "platform") {
      return device.platform || "This device";
    }
    return device.fallback || "Passkey";
  };

  document.querySelectorAll("[data-huia-passkey]").forEach((container) => {
    const mode = container.dataset.mode || "signin";
    const endpoint =
      mode === "manage"
        ? window.location.pathname
        : (container.dataset.endpoint || ".").replace(/\/$/, "");
    const token = container.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
    const errorBox = container.querySelector("[data-huia-passkey-error]");

    const showError = (message) => {
      if (errorBox) {
        if (message) {
          errorBox.textContent = message;
        }
        errorBox.hidden = false;
      }
    };
    const clearError = () => {
      if (errorBox) {
        errorBox.hidden = true;
      }
    };

    const post = async (path, body) => {
      const url = path.startsWith("?") ? endpoint + path : `${endpoint}/${path}`;
      const response = await fetch(url, {
        method: "POST",
        headers: { "Content-Type": "application/json", "X-Huia-CSRF": token, "RequestVerificationToken": token },
        body: JSON.stringify(body || {}),
      });
      const text = await response.text();
      const data = text ? JSON.parse(text) : {};
      if (!response.ok) {
        throw new Error(data.error || data.title || "request_failed");
      }
      return data;
    };

    // --- discoverable sign-in (login page): understated fast path -------------------------------
    if (mode === "signin") {
      if (!supported) {
        return;
      }
      const returnUrl = container.dataset.returnUrl || "/";
      const button = container.querySelector("[data-huia-passkey-signin]");

      const focusForm = () => {
        const active = document.querySelector('[role="tabpanel"]:not([hidden])') || document;
        const field = active.querySelector('input[name="Input.Email"], input[name="Input.PhoneNumber"]')
          || document.querySelector('input[name="Input.Email"], input[name="Input.PhoneNumber"]');
        field?.focus();
      };

      const assert = async (mediation) => {
        const optionsJson = await post("assertion-options", {});
        const credential = await navigator.credentials.get({
          publicKey: toRequestOptions(optionsJson),
          mediation,
        });
        if (!credential) {
          if (mediation !== "conditional") {
            focusForm();
          }
          return;
        }
        const result = await post("assertion", {
          credential: serializeCredential(credential),
          returnUrl,
          rememberMe: false,
        });
        window.location.assign(result.redirectUrl || returnUrl);
      };

      if (button) {
        button.addEventListener("click", async () => {
          button.disabled = true;
          try {
            await assert("optional");
          } catch {
            focusForm();
          } finally {
            button.disabled = false;
          }
        });
      }

      if (window.PublicKeyCredential.isConditionalMediationAvailable) {
        window.PublicKeyCredential.isConditionalMediationAvailable().then((available) => {
          if (available && document.querySelector('input[autocomplete~="webauthn"]')) {
            assert("conditional").catch(() => {});
          }
        });
      }
      return;
    }

    // --- post-sign-up enrollment interstitial -------------------------------------------------
    if (mode === "enroll") {
      const returnUrl = container.dataset.returnUrl || "/";
      const button = container.querySelector("[data-huia-passkey-enroll]");
      const skipForm = container.querySelector("[data-huia-passkey-skip-form]");

      if (!supported || typeof navigator.credentials.create !== "function") {
        skipForm?.submit(); // auto-skip via the server so the "prompted once" flag is recorded
        return;
      }

      button?.addEventListener("click", async () => {
        clearError();
        button.disabled = true;
        try {
          const optionsJson = await post("?handler=CreationOptions", {});
          const credential = await navigator.credentials.create({ publicKey: toCreationOptions(optionsJson) });
          const result = await post("?handler=Register", {
            credential: serializeCredential(credential),
            name: guessDeviceName(credential),
          });
          window.location.assign(result.redirectUrl || returnUrl);
        } catch {
          showError();
          button.disabled = false;
        }
      });
      return;
    }

    // --- credential management (Passkeys page) ------------------------------------------------
    if (mode === "manage") {
      wireManage(container, { supported, endpoint, token, post, toCreationOptions, serializeCredential, guessDeviceName, strings });
    }
  });

  function wireManage(container, ctx) {
    const addButton = container.querySelector("[data-huia-passkey-register]");
    const errorBox = container.querySelector("[data-huia-passkey-error]");

    if (addButton) {
      if (!ctx.supported || typeof navigator.credentials.create !== "function") {
        addButton.disabled = true;
      }
      addButton.addEventListener("click", async () => {
        if (errorBox) errorBox.hidden = true;
        addButton.disabled = true;
        try {
          const optionsJson = await ctx.post("?handler=CreationOptions", {});
          const credential = await navigator.credentials.create({ publicKey: ctx.toCreationOptions(optionsJson) });
          await ctx.post("?handler=Register", {
            credential: ctx.serializeCredential(credential),
            name: ctx.guessDeviceName(credential),
          });
          window.location.reload();
        } catch (error) {
          if (error.name !== "NotAllowedError" && errorBox) {
            errorBox.textContent = error.message || ctx.strings.registerFailed || "That passkey could not be registered.";
            errorBox.hidden = false;
          }
          addButton.disabled = false;
        }
      });
    }

    // Inline rename: click the name → editable input; blur / Enter saves; Esc reverts.
    container.querySelectorAll("[data-huia-passkey-name]").forEach((nameEl) => {
      const id = nameEl.dataset.huiaPasskeyName;
      nameEl.addEventListener("click", () => {
        if (nameEl.querySelector("input")) {
          return;
        }
        const current = nameEl.textContent.trim();
        const input = document.createElement("input");
        input.className = "input";
        input.value = current;
        input.setAttribute("aria-label", ctx.strings.renameLabel || "Passkey name");
        nameEl.textContent = "";
        nameEl.appendChild(input);
        input.focus();
        input.select();

        let done = false;
        const finish = async (save) => {
          if (done) return;
          done = true;
          const value = save ? input.value.trim() : current;
          nameEl.textContent = value || (ctx.strings.unnamed || "Passkey");
          if (save && value && value !== current) {
            try {
              await ctx.post("?handler=Rename", { id, name: value });
            } catch {
              nameEl.textContent = current;
            }
          }
        };
        input.addEventListener("keydown", (e) => {
          if (e.key === "Enter") { e.preventDefault(); finish(true); }
          else if (e.key === "Escape") { e.preventDefault(); finish(false); }
        });
        input.addEventListener("blur", () => finish(true));
      });
    });

    // Two-step remove: first click reveals "Remove? Yes / Cancel" in the row.
    container.querySelectorAll("[data-huia-passkey-remove]").forEach((form) => {
      const trigger = form.querySelector("button[type=submit]");
      if (!trigger) {
        return;
      }
      trigger.addEventListener("click", (e) => {
        if (form.dataset.confirming === "1") {
          return; // second click submits the form for real
        }
        e.preventDefault();
        form.dataset.confirming = "1";
        const label = trigger.textContent;

        const confirm = document.createElement("span");
        confirm.className = "huia-passkey-confirm";
        const yes = document.createElement("button");
        yes.type = "submit";
        yes.className = "huia-linkbtn huia-linkbtn-danger";
        yes.textContent = ctx.strings.removeConfirmYes || "Remove";
        const no = document.createElement("button");
        no.type = "button";
        no.className = "huia-linkbtn";
        no.textContent = ctx.strings.cancel || "Cancel";
        confirm.append(document.createTextNode((ctx.strings.removeConfirm || "Remove?") + " "), yes, no);

        trigger.hidden = true;
        form.appendChild(confirm);
        yes.focus();
        no.addEventListener("click", () => {
          confirm.remove();
          trigger.hidden = false;
          delete form.dataset.confirming;
          trigger.textContent = label;
        });
      });
    });
  }
})();
