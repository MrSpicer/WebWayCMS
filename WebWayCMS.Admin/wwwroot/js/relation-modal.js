// Reference picker modal: "New"/"Edit" open the target type's form in an iframe with its own save.
// The iframe posts its form with fetch, then messages the parent with { id, title }; the parent adds or
// updates the picker entry and closes. Loaded on every admin page (no-op outside a picker/modal).
(function () {
    var messageType = 'webwaycms:relation-saved';

    document.addEventListener('DOMContentLoaded', function () {
        if (document.body && document.body.dataset.adminModal === 'true') {
            initModalDocument();
        } else {
            initParentDocument();
        }
    });

    // ─── Inside the modal iframe ────────────────────────────────────────────
    function initModalDocument() {
        var form = document.querySelector('main form[method="post"]') || document.querySelector('form[method="post"]');
        if (!form) return;

        // The POST must carry ?modal=1 (the server answers JSON on it); a form rendered from the
        // modal URL does not necessarily include the query in its action.
        ensureModalQuery(form);

        form.addEventListener('submit', function (event) {
            event.preventDefault();
            submitModalForm(form);
        });
    }

    function ensureModalQuery(form) {
        var url = new URL(form.getAttribute('action') || window.location.href, window.location.origin);
        url.searchParams.set('modal', '1');
        form.setAttribute('action', url.toString());
    }

    async function submitModalForm(form) {
        try {
            var response = await fetch(form.getAttribute('action') || window.location.href, {
                method: 'POST',
                body: new FormData(form),
                credentials: 'same-origin',
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });

            if (response.status === 422) {
                var html = await response.text();
                document.open();
                document.write(html);
                document.close();
                return;
            }

            var contentType = response.headers.get('content-type') || '';
            if (response.ok && contentType.indexOf('application/json') >= 0) {
                var saved = await response.json();
                window.parent.postMessage(
                    { type: messageType, id: saved.id, title: saved.title },
                    window.location.origin);
                return;
            }

            // Unexpected response: let the browser show it rather than silently losing the save.
            window.location.href = response.url;
        } catch (error) {
            console.error('Relation modal save failed:', error);
        }
    }

    // ─── On the source form ────────────────────────────────────────────────
    function initParentDocument() {
        document.addEventListener('click', function (event) {
            var newButton = event.target.closest('.entity-picker-new');
            if (newButton) {
                event.preventDefault();
                var newTarget = newButton.closest('.entity-picker-multi')
                    || newButton.closest('.entity-picker-actions')?.querySelector('select.entity-picker');
                openModal(newButton.dataset.modalUrl, newTarget);
                return;
            }

            var editButton = event.target.closest('.entity-picker-edit, .entity-picker-value-edit');
            if (editButton) {
                event.preventDefault();
                var target = editButton.closest('.entity-picker-multi')
                    || editButton.closest('.entity-picker-actions')?.querySelector('select.entity-picker');
                var id = editButton.dataset.id || (target && target.value) || '';
                if (!id) return;
                openModal(editButton.dataset.modalEditBase + encodeURIComponent(id) + '?modal=1', target);
                return;
            }
        });

        // Enable the single-value Edit button once a value is selected.
        document.addEventListener('change', function (event) {
            if (!event.target.matches || !event.target.matches('select.entity-picker')) return;
            var actions = event.target.closest('.entity-picker-actions');
            var edit = actions && actions.querySelector('.entity-picker-edit');
            if (edit) edit.disabled = !event.target.value;
        });

        window.addEventListener('message', function (event) {
            if (event.origin !== window.location.origin) return;
            if (!event.data || event.data.type !== messageType) return;

            var modal = document.querySelector('[data-relation-modal]');
            var target = modal && modal._pickerTarget;
            closeModal(modal);
            if (target) updatePicker(target, event.data.id, event.data.title);
        });
    }

    function openModal(url, target) {
        if (!url) return;
        closeModal(document.querySelector('[data-relation-modal]'));

        var modal = document.createElement('div');
        modal.className = 'modal is-active';
        modal.setAttribute('data-relation-modal', '');
        modal._pickerTarget = target;

        var background = document.createElement('div');
        background.className = 'modal-background';

        var content = document.createElement('div');
        content.className = 'modal-content relation-modal-content';

        var frame = document.createElement('iframe');
        frame.className = 'relation-modal-frame';
        frame.title = 'Create or edit related content';
        frame.src = url;

        var close = document.createElement('button');
        close.type = 'button';
        close.className = 'modal-close is-large';
        close.setAttribute('aria-label', 'Close');
        close.addEventListener('click', function () { closeModal(modal); });

        background.addEventListener('click', function () { closeModal(modal); });
        content.appendChild(frame);
        modal.appendChild(background);
        modal.appendChild(content);
        modal.appendChild(close);
        document.body.appendChild(modal);

        modal._keyHandler = function (event) {
            if (event.key === 'Escape') closeModal(modal);
        };
        document.addEventListener('keydown', modal._keyHandler);
    }

    function closeModal(modal) {
        if (!modal) return;
        if (modal._keyHandler) document.removeEventListener('keydown', modal._keyHandler);
        modal.remove();
    }

    function updatePicker(target, id, title) {
        if (!id) return;
        id = String(id);

        if (target.classList.contains('entity-picker-multi')) {
            var container = target;
            var values = readValues(container);
            if (values.indexOf(id) < 0) values.push(id);
            container.dataset.values = JSON.stringify(values);
            if (window.WebWayFormComponents && window.WebWayFormComponents.setMultiValues) {
                window.WebWayFormComponents.setMultiValues(container, values);
            }
            if (title && window.WebWayFormComponents && window.WebWayFormComponents.setMultiTitle) {
                window.WebWayFormComponents.setMultiTitle(container, id, title);
            }
            return;
        }

        if (target.tagName === 'SELECT') {
            var option = Array.prototype.find.call(target.options, function (o) { return o.value === id; });
            if (!option) {
                option = document.createElement('option');
                option.value = id;
                target.appendChild(option);
            }
            option.textContent = title || id;
            target.value = id;
            target.dataset.currentValue = id;
            target.dispatchEvent(new Event('change', { bubbles: true }));
            var actions = target.closest('.entity-picker-actions');
            var edit = actions && actions.querySelector('.entity-picker-edit');
            if (edit) edit.disabled = false;
        }
    }

    function readValues(container) {
        try {
            var parsed = JSON.parse(container.dataset.values || '[]');
            return Array.isArray(parsed) ? parsed.map(String) : [];
        } catch (e) {
            return [];
        }
    }
})();
