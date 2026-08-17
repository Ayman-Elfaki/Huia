// Drives the country picker on Login.cshtml's phone form — two alternative widgets for the same value (see
// _PhoneLoginForm.cshtml's own comment: a Basecoat dropdown-menu on wider viewports, a native <select> on
// mobile, CSS alone choosing which is visible), both funneled through the one shared hidden Input.CountryCode
// field below. Basecoat's dropdown-menu component handles the popover open/close and each
// [role=menuitemradio] row's own exclusive aria-checked state on its own, but — unlike its select component —
// has no built-in hidden-input, trigger-label, or change-event mechanism, so this file supplies all three for
// it (the native <select> needs none of that; its own value/label are already exactly what's wanted).
(() => {
    "use strict";

    const menu = document.getElementById("huia-country-menu");
    const trigger = document.getElementById("huia-country-trigger");
    const list = document.getElementById("huia-country-menu-list");
    const hidden = document.getElementById("huia-country-value");
    const flag = document.getElementById("huia-country-flag");
    const label = document.getElementById("huia-country-trigger-label");
    const nativeSelect = document.getElementById("huia-country-native-select");

    if (!menu || !trigger || !list || !hidden || !flag || !label) {
        return;
    }

    function selectCountry(item) {
        const value = item.dataset.value ?? "";

        hidden.value = value;
        flag.className = value ? `fi fi-${value.toLowerCase()}` : "fi";
        label.textContent = item.dataset.label ?? "";

        if (nativeSelect) {
            nativeSelect.value = value;
        }

        menu.dispatchEvent(new CustomEvent("change", { detail: { value } }));
    }

    // Marks item as the sole checked row (Basecoat's own click handling does this itself for a real click —
    // see the "click" listener below — but the native-<select> and typeahead paths never fire a real click on
    // a row, so they call this directly instead).
    function markChecked(item) {
        list.querySelectorAll('[aria-checked="true"]').forEach((el) => el.setAttribute("aria-checked", "false"));
        item.setAttribute("aria-checked", "true");
    }

    list.addEventListener("click", (event) => {
        const item = event.target.closest("[role='menuitemradio']");
        if (!item || item.getAttribute("aria-disabled") === "true") {
            return;
        }

        selectCountry(item);
    });

    nativeSelect?.addEventListener("change", () => {
        const option = nativeSelect.selectedOptions[0];
        const item = option && list.querySelector(`[data-value="${CSS.escape(option.value)}"]`);
        if (item) {
            markChecked(item);
            selectCountry(item);
        }
    });

    // Typeahead: typing while the popover is open jumps straight to (and selects) the first country whose
    // name starts with what's been typed so far — repeating the same character cycles through its matches,
    // and pausing briefly resets the search, both matching how a native <select> already behaves for free
    // (which is exactly what mobile gets instead of this widget). Basecoat's own arrow-key navigation tracks
    // its "active" row separately (a plain .active class on the trigger's own keydown, not a public API this
    // file can reach) — rather than fight to stay in sync with that private state, typeahead here commits the
    // match immediately via the same selectCountry() a click uses, exactly like the native <select> it
    // mirrors, and never closes the popover on its own so repeated typing keeps working.
    const items = Array.from(list.querySelectorAll("[role='menuitemradio']"));
    let searchBuffer = "";
    let searchResetTimer = null;
    let lastMatch = null;

    function itemName(item) {
        return item.querySelector(".huia-country-name")?.textContent ?? "";
    }

    trigger.addEventListener("keydown", (event) => {
        if (trigger.getAttribute("aria-expanded") !== "true") {
            return;
        }

        if (event.key.length !== 1 || event.ctrlKey || event.altKey || event.metaKey) {
            return;
        }

        clearTimeout(searchResetTimer);
        searchResetTimer = setTimeout(() => {
            searchBuffer = "";
        }, 600);

        // Repeating the same character (within the reset window) re-searches for that single character from
        // just past the last match, cycling through its matches one at a time — e.g. "s", "s", "s" walks
        // through every S country. Any other character extends the buffer instead, narrowing the search
        // (e.g. "f" then "r" searches for "fr", not "r") — the standard ARIA APG listbox typeahead algorithm.
        const repeatingSameKey = searchBuffer.length > 0 && [...searchBuffer].every((c) => c === event.key);
        searchBuffer = repeatingSameKey ? event.key : searchBuffer + event.key;

        const query = searchBuffer.toLocaleLowerCase();
        const startIndex = repeatingSameKey && lastMatch ? items.indexOf(lastMatch) + 1 : 0;

        for (let i = 0; i < items.length; i++) {
            const item = items[(startIndex + i) % items.length];
            if (itemName(item).toLocaleLowerCase().startsWith(query)) {
                event.preventDefault();
                lastMatch?.classList.remove("huia-typeahead-match");
                item.classList.add("huia-typeahead-match");
                lastMatch = item;
                item.scrollIntoView({ block: "nearest" });
                markChecked(item);
                selectCountry(item);
                break;
            }
        }
    });

    // Clears the typeahead highlight once the popover actually closes — aria-expanded is the one reliable
    // signal for that (Basecoat's dropdown-menu doesn't emit its own open/close event).
    new MutationObserver(() => {
        if (trigger.getAttribute("aria-expanded") !== "true") {
            lastMatch?.classList.remove("huia-typeahead-match");
            lastMatch = null;
            searchBuffer = "";
        }
    }).observe(trigger, { attributes: true, attributeFilter: ["aria-expanded"] });
})();
