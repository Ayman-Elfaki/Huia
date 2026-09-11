// Progressive enhancement for the segmented one-time-code input (VerifyOtp.cshtml). The real form
// field stays a single hidden `Input.Code` text input; this only keeps it in sync with the boxes.
(() => {
  document.querySelectorAll("[data-huia-otp]").forEach((group) => {
    const target = document.getElementById(group.dataset.huiaOtpFor);
    if (!target) {
      return;
    }

    const boxes = Array.from(group.querySelectorAll("input"));

    const sync = () => {
      target.value = boxes.map((box) => box.value).join("");
    };

    boxes.forEach((box, index) => {
      box.addEventListener("input", () => {
        box.value = box.value.replace(/[^0-9]/g, "").slice(-1);
        sync();
        if (box.value && index < boxes.length - 1) {
          boxes[index + 1].focus();
        }
      });

      box.addEventListener("keydown", (event) => {
        if (event.key === "Backspace" && !box.value && index > 0) {
          boxes[index - 1].focus();
        }
      });

      box.addEventListener("paste", (event) => {
        const pasted = (event.clipboardData?.getData("text") ?? "").replace(/[^0-9]/g, "");
        if (!pasted) {
          return;
        }

        event.preventDefault();
        pasted.split("").slice(0, boxes.length - index).forEach((digit, offset) => {
          boxes[index + offset].value = digit;
        });
        sync();
        boxes[Math.min(index + pasted.length, boxes.length - 1)].focus();
      });
    });

    // Prefill from any value the server already put in the field (e.g. a validation round-trip).
    if (target.value) {
      target.value.split("").slice(0, boxes.length).forEach((digit, index) => {
        boxes[index].value = digit;
      });
    }
  });
})();

// "Send a new code" cooldown counter. The button starts disabled on arrival (a code was just sent),
// counts down, then re-enables. The deadline is kept in sessionStorage so a refresh keeps counting.
(() => {
  document.querySelectorAll("[data-cooldown]").forEach((button) => {
    const seconds = parseInt(button.dataset.cooldown, 10);
    if (!Number.isFinite(seconds) || seconds <= 0) {
      return;
    }

    const form = button.closest("form");
    const label = button.dataset.label || button.textContent;
    const countdownLabel = button.dataset.countdownLabel || "{0}";
    const flow = (form && form.querySelector('[name="Flow"]') && form.querySelector('[name="Flow"]').value) || "";
    const storeKey = "huia-otp-cooldown:" + flow.slice(0, 40);

    const read = () => {
      try {
        return parseInt(sessionStorage.getItem(storeKey) || "0", 10) || 0;
      } catch {
        return 0;
      }
    };
    const write = (value) => {
      try {
        sessionStorage.setItem(storeKey, String(value));
      } catch {
        /* private mode */
      }
    };

    let deadline = read();
    if (!deadline || deadline < Date.now()) {
      deadline = Date.now() + seconds * 1000;
      write(deadline);
    }

    let timer;
    const tick = () => {
      const remaining = Math.ceil((deadline - Date.now()) / 1000);
      if (remaining <= 0) {
        button.disabled = false;
        button.textContent = label;
        window.clearInterval(timer);
        return;
      }
      button.disabled = true;
      button.textContent = countdownLabel.replace("{0}", String(remaining));
    };

    tick();
    timer = window.setInterval(tick, 1000);

    if (form) {
      form.addEventListener("submit", () => write(Date.now() + seconds * 1000));
    }
  });
})();
