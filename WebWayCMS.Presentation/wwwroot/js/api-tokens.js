// Copy buttons on Identity → Manage → API tokens: a button with data-copy-target copies the value (an
// input) or text (anything else) of the element with that id.
(function () {
    'use strict';

    document.addEventListener('click', async function (event) {
        const button = event.target.closest('[data-copy-target]');
        if (!button) return;

        const target = document.getElementById(button.dataset.copyTarget);
        if (!target) return;

        const text = 'value' in target ? target.value : target.textContent;
        const label = button.textContent;
        try {
            await navigator.clipboard.writeText(text);
            button.textContent = 'Copied';
        } catch {
            // Clipboard access can be refused (e.g. an insecure context); leave the value selectable.
            if (target.select) target.select();
            button.textContent = 'Press Ctrl+C';
        }
        setTimeout(function () { button.textContent = label; }, 2000);
    });
})();
