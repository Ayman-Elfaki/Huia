// Boxed one-time-code entry on PhoneLoginVerify.cshtml: six individually-focusable Basecoat .input boxes
// standing in for the single bound Input.Code field (kept in sync via a hidden input) that ASP.NET Core
// Identity actually validates server-side. Handles auto-advance, backspace-to-previous, arrow-key navigation,
// and distributing a pasted or autofilled (WebOTP/iOS) full code across all six boxes at once.
(() => {
    "use strict";

    const boxes = Array.from(document.querySelectorAll("[data-otp-box]"));
    const hidden = document.getElementById("huia-otp-value");

    if (boxes.length === 0 || !hidden) {
        return;
    }

    function sync() {
        hidden.value = boxes.map((box) => box.value).join("");
    }

    function focusBox(index) {
        boxes[Math.max(0, Math.min(index, boxes.length - 1))]?.focus();
    }

    function fillFrom(index, digits) {
        const chars = digits.replace(/\D/g, "").split("");
        chars.forEach((char, offset) => {
            const target = boxes[index + offset];
            if (target) {
                target.value = char;
            }
        });
        sync();
        focusBox(Math.min(index + chars.length, boxes.length - 1));
    }

    boxes.forEach((box, index) => {
        box.addEventListener("input", () => {
            const digits = box.value.replace(/\D/g, "");
            if (digits.length > 1) {
                // A full (or partial) code landed here at once — WebOTP/iOS autofill, or a paste the browser
                // routed through "input" instead of a "paste" event. Always distribute from the first box: an
                // autofilled code is the complete code, never a fragment mid-sequence.
                fillFrom(0, digits);
                return;
            }

            box.value = digits;
            sync();
            if (digits) {
                focusBox(index + 1);
            }
        });

        box.addEventListener("keydown", (event) => {
            if (event.key === "Backspace" && !box.value && index > 0) {
                event.preventDefault();
                boxes[index - 1].value = "";
                sync();
                focusBox(index - 1);
            } else if (event.key === "ArrowLeft") {
                event.preventDefault();
                focusBox(index - 1);
            } else if (event.key === "ArrowRight") {
                event.preventDefault();
                focusBox(index + 1);
            }
        });

        box.addEventListener("paste", (event) => {
            const text = event.clipboardData?.getData("text") ?? "";
            if (text) {
                event.preventDefault();
                fillFrom(0, text);
            }
        });

        box.addEventListener("focus", () => box.select());
    });

    // A failed verification re-renders this page with the previously-submitted code still bound to
    // Input.Code — restore it into the boxes instead of leaving them blank while the hidden field disagrees.
    if (hidden.value) {
        fillFrom(0, hidden.value);
    }

    (boxes.find((box) => !box.value) ?? boxes[0]).focus();
})();
