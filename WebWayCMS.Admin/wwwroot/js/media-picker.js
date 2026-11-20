// Image picker for the "ImagePicker" form component.
//
// Uploads the chosen file to the media API on its own and writes only the returned content hash
// into a hidden input. The surrounding admin form therefore stays url-encoded and never carries
// image bytes -- which is what keeps the same view model usable from MCP and content seeding.
(function () {
    'use strict';

    var UPLOAD_URL = '/api/media/upload';

    function showError(picker, message) {
        var error = picker.querySelector('[data-image-picker-error]');
        if (!error) return;
        error.textContent = message || '';
        error.classList.toggle('is-hidden', !message);
    }

    function setPreview(picker, url) {
        var preview = picker.querySelector('[data-image-picker-preview]');
        if (!preview) return;
        preview.innerHTML = '';
        if (!url) return;
        var img = document.createElement('img');
        img.src = url;
        img.alt = '';
        preview.appendChild(img);
    }

    async function upload(picker, file) {
        var hidden = picker.querySelector('.image-picker-value');
        var nameLabel = picker.querySelector('[data-image-picker-name]');

        showError(picker, '');
        if (nameLabel) nameLabel.textContent = file.name;

        var body = new FormData();
        body.append('file', file);

        try {
            var payload = await window.WebWayAdmin.fetchJson(UPLOAD_URL, {
                method: 'POST',
                body: body,
                fallbackError: 'The image could not be uploaded.'
            });

            if (hidden) {
                hidden.value = payload.hash;
                // Let any form-level change tracking notice the new value.
                hidden.dispatchEvent(new Event('change', { bubbles: true }));
            }
            setPreview(picker, payload.url);
        } catch (e) {
            showError(picker, e.message || 'The image could not be uploaded.');
        }
    }

    function wire(picker) {
        if (picker.dataset.imagePickerWired) return;
        picker.dataset.imagePickerWired = 'true';

        var input = picker.querySelector('[data-image-picker-input]');
        if (!input) return;

        input.addEventListener('change', function () {
            if (input.files && input.files.length > 0) {
                upload(picker, input.files[0]);
            }
        });
    }

    function wireAll(root) {
        (root || document).querySelectorAll('[data-image-picker]').forEach(wire);
    }

    document.addEventListener('DOMContentLoaded', function () {
        wireAll(document);

        // Widget configuration forms are injected after load, so pick those up too.
        var observer = new MutationObserver(function (mutations) {
            mutations.forEach(function (m) {
                m.addedNodes.forEach(function (node) {
                    if (node.nodeType === 1) wireAll(node);
                });
            });
        });
        observer.observe(document.body, { childList: true, subtree: true });
    });
})();
