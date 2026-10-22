// Page zone editor: Edit zones / Preview tabs, draft preview iframe, and toolbar confirmations.
// No inline script (the CSP forbids it) and no jQuery.
(function () {
    'use strict';

    const zonesPanel = document.getElementById('page-zones-panel-zones');
    const previewPanel = document.getElementById('page-zones-panel-preview');
    if (!zonesPanel || !previewPanel) return;

    const iframe = previewPanel.querySelector('iframe.page-zones-preview');
    const tabs = Array.from(document.querySelectorAll('.tabs [data-tab]'));
    const isEmpty = zonesPanel.dataset.empty === 'true';
    const reloadGuardKey = 'wwcms:page-zones-discovery:' + window.location.pathname;

    // (Re)load the draft preview. Always via the admin preview action, which redirects to
    // /_preview/{id}; the route transformer marks that one request as a draft read.
    function loadPreview() {
        if (iframe) iframe.src = iframe.dataset.src;
    }

    function activate(name, updateHash) {
        tabs.forEach(function (tab) {
            const active = tab.dataset.tab === name;
            tab.classList.toggle('is-active', active);
            const link = tab.querySelector('a');
            if (link) link.setAttribute('aria-selected', active ? 'true' : 'false');
        });
        zonesPanel.hidden = name !== 'zones';
        previewPanel.hidden = name !== 'preview';

        if (name === 'preview') loadPreview();
        if (updateHash) history.replaceState(null, '', name === 'preview' ? '#preview' : window.location.pathname + window.location.search);
    }

    tabs.forEach(function (tab) {
        tab.addEventListener('click', function (e) {
            e.preventDefault();
            activate(tab.dataset.tab, true);
        });
    });

    // The hash remembers the tab across the reload every zone edit triggers.
    activate(window.location.hash === '#preview' ? 'preview' : 'zones', false);

    // A page's zone slots are created the first time its template renders. With none yet, render the
    // preview in the background and reload once so the slots it created appear.
    let discoveryPending = false;
    try {
        discoveryPending = isEmpty && sessionStorage.getItem(reloadGuardKey) !== '1';
        if (!isEmpty) sessionStorage.removeItem(reloadGuardKey);
    } catch (err) {
        discoveryPending = false;
    }

    if (discoveryPending && iframe) {
        iframe.addEventListener('load', function () {
            try { sessionStorage.setItem(reloadGuardKey, '1'); } catch (err) { return; }
            window.location.reload();
        }, { once: true });
        if (previewPanel.hidden) loadPreview();
    }

    // Toolbar actions that cannot be undone ask first.
    document.querySelectorAll('form[data-confirm]').forEach(function (form) {
        form.addEventListener('submit', function (e) {
            if (!window.confirm(form.dataset.confirm)) e.preventDefault();
        });
    });
})();
