// Progressive enhancement for passkey (WebAuthn) sign-in, step-up and management. Talks to the
// endpoints under identity/account/passkey/* (login + 2fa) or the Passkeys page handlers (manage).
// No bundler: plain browser APIs only.
(() => {
  const supported =
    typeof window.PublicKeyCredential === "function" &&
    typeof navigator.credentials?.get === "function";

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

  // The server sends challenge / user.id / *.id as base64url strings; the browser wants ArrayBuffers.
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
        errorBox.textContent = message;
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

    // --- discoverable primary sign-in (login page) ------------------------------------------------
    if (mode === "signin" || mode === "signin-page") {
      const returnUrl = container.dataset.returnUrl || "/";

      const assert = async (mediation) => {
        const optionsJson = await post("assertion-options", {});
        const controller = mediation === "conditional" ? new AbortController() : null;
        const credential = await navigator.credentials.get({
          publicKey: toRequestOptions(optionsJson),
          mediation,
          signal: controller ? controller.signal : undefined,
        });
        if (!credential) {
          return;
        }
        const result = await post("assertion", {
          credential: serializeCredential(credential),
          returnUrl,
          rememberMe: false,
        });
        window.location.assign(result.redirectUrl || returnUrl);
      };

      const button = container.querySelector("[data-huia-passkey-signin]");
      if (button) {
        if (!supported) {
          button.disabled = true;
        }
        button.addEventListener("click", async () => {
          clearError();
          button.disabled = true;
          try {
            await assert("optional");
          } catch (error) {
            if (error.name !== "NotAllowedError" && error.name !== "AbortError") {
              showError(container.dataset.errorText || "That passkey could not be used.");
            }
          } finally {
            button.disabled = !supported ? true : false;
          }
        });
      }

      // Conditional UI: offer passkeys in the username field's autofill, when the browser supports it.
      if (supported && window.PublicKeyCredential.isConditionalMediationAvailable) {
        window.PublicKeyCredential.isConditionalMediationAvailable().then((available) => {
          if (available && document.querySelector('input[autocomplete~="webauthn"]')) {
            assert("conditional").catch(() => {});
          }
        });
      }
      return;
    }

    // --- passkey as a second factor (LoginWith2fa page) -----------------------------------------
    if (mode === "2fa") {
      const flow = container.dataset.flow || "";
      const rememberSelector = container.dataset.rememberMachineSelector;
      const button = container.querySelector("[data-huia-passkey-2fa]");
      if (!button) {
        return;
      }
      if (!supported) {
        button.disabled = true;
      }
      button.addEventListener("click", async () => {
        clearError();
        button.disabled = true;
        try {
          const optionsJson = await post("2fa-options", { flow });
          const credential = await navigator.credentials.get({ publicKey: toRequestOptions(optionsJson) });
          const rememberMachine = rememberSelector ? !!document.querySelector(rememberSelector)?.checked : false;
          const result = await post("2fa", {
            credential: serializeCredential(credential),
            flow,
            rememberMachine,
          });
          window.location.assign(result.redirectUrl || "/");
        } catch (error) {
          if (error.name !== "NotAllowedError") {
            showError("That passkey could not be used.");
          }
        } finally {
          button.disabled = !supported;
        }
      });
      return;
    }

    // --- credential management (Passkeys page) -------------------------------------------------
    if (mode === "manage") {
      const button = container.querySelector("[data-huia-passkey-register]");
      if (!button) {
        return;
      }
      if (!supported || typeof navigator.credentials.create !== "function") {
        button.disabled = true;
      }
      button.addEventListener("click", async () => {
        clearError();
        const name = window.prompt(container.dataset.namePrompt || "Name this passkey");
        if (name === null) {
          return;
        }
        button.disabled = true;
        try {
          const optionsJson = await post("?handler=CreationOptions", {});
          const credential = await navigator.credentials.create({ publicKey: toCreationOptions(optionsJson) });
          await post("?handler=Register", { credential: serializeCredential(credential), name });
          window.location.reload();
        } catch (error) {
          if (error.name !== "NotAllowedError") {
            showError(error.message || "That passkey could not be registered.");
          }
          button.disabled = false;
        }
      });
    }
  });
})();
