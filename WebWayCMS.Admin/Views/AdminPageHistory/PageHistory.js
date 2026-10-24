// Page history screens: confirm the actions that change the page before they post.
// No inline script (the CSP forbids it) and no jQuery.
(function () {
    'use strict';

    document.querySelectorAll('form[data-confirm]').forEach(function (form) {
        form.addEventListener('submit', function (e) {
            if (!window.confirm(form.dataset.confirm)) e.preventDefault();
        });
    });
})();
