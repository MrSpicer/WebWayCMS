// Reorder for a parent's children panel (_RelationChildren.cshtml): Up/Down move a row, then the
// whole order is posted to the panel's data-reorder-url. A failure reloads the page so the list shows
// what is stored. Copied to wwwroot/js/relation-children.js by the CopyViewScripts build target.
(function () {
    'use strict';

    function token() {
        const meta = document.querySelector('meta[name="csrf-token"]');
        return meta ? meta.content : '';
    }

    async function save(panel) {
        const order = Array.from(panel.querySelectorAll('tr[data-child-id]')).map(function (row) {
            return row.dataset.childId;
        });
        const error = panel.querySelector('[data-reorder-error]');
        try {
            const response = await fetch(panel.dataset.reorderUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() },
                body: JSON.stringify(order)
            });
            if (!response.ok) {
                const body = await response.json().catch(function () { return {}; });
                throw new Error(body.error || 'The new order could not be saved.');
            }
        } catch (e) {
            if (error) {
                error.textContent = e.message + ' Reloading…';
                error.classList.remove('is-hidden');
            }
            window.setTimeout(function () { window.location.reload(); }, 1500);
        }
    }

    document.addEventListener('click', function (e) {
        const button = e.target.closest('[data-relation-children] [data-move]');
        if (!button) return;
        const panel = button.closest('[data-relation-children]');
        const row = button.closest('tr[data-child-id]');
        if (!panel || !panel.dataset.reorderUrl || !row) return;

        const sibling = button.dataset.move === 'up' ? row.previousElementSibling : row.nextElementSibling;
        if (!sibling) return;
        if (button.dataset.move === 'up') sibling.before(row); else sibling.after(row);
        button.focus();
        save(panel);
    });
})();
