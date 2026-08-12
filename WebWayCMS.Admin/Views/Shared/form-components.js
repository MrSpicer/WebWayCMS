// Shared form component behaviours loaded by both static admin forms
// and dynamically-rendered config sub-forms.
(function () {
    document.addEventListener('DOMContentLoaded', function () {
        initAllEntityPickers();
    });

    function initAllEntityPickers() {
        document.querySelectorAll('select.entity-picker').forEach(function (select) {
            if (select.dataset.entityPickerInitialized) return;
            select.dataset.entityPickerInitialized = 'true';

            var entityType = select.dataset.entityType;
            if (!entityType) return;

            loadEntityPickerOptions(select, entityType);
        });

        var observer = new MutationObserver(function (mutations) {
            mutations.forEach(function (mutation) {
                mutation.addedNodes.forEach(function (node) {
                    if (node.nodeType === 1) {
                        if (node.matches && node.matches('select.entity-picker')) {
                            loadEntityPickerOptions(node, node.dataset.entityType);
                        }
                        if (node.querySelectorAll) {
                            node.querySelectorAll('select.entity-picker').forEach(function (s) {
                                loadEntityPickerOptions(s, s.dataset.entityType);
                            });
                        }
                    }
                });
            });
        });

        observer.observe(document.body, { childList: true, subtree: true });
    }

    function loadEntityPickerOptions(select, entityType) {
        if (select.dataset.entityPickerLoading) return;
        select.dataset.entityPickerLoading = 'true';

        var endpoints = {
            'ContentBlock': '/admin/contentblocks/api/list',
            'Article': '/admin/articles/api/list',
            'ArticleList': '/admin/articles/api/articlelists',
            'ContentZone': '/admin/contentzones/api/list'
        };

        var endpoint = endpoints[entityType];
        if (!endpoint) {
            select.innerHTML = '<option value="">-- Unknown entity type: ' + escapeHtml(entityType) + ' --</option>';
            select.style.display = '';
            return;
        }

        var currentValue = select.dataset.currentValue || '';
        select.innerHTML = '<option value="">-- Loading ' + escapeHtml(entityType) + 's... --</option>';
        select.style.display = '';

        fetch(endpoint)
            .then(function (r) {
                if (!r.ok) throw new Error('Failed to load entities');
                return r.json();
            })
            .then(function (entities) {
                var options = '<option value="">-- Select --</option>';
                entities.forEach(function (entity) {
                    var id = entity.id || entity.Id;
                    var title = entity.title || entity.Title || entity.name || entity.Name || id;
                    var selected = String(id) === String(currentValue) ? ' selected' : '';
                    options += '<option value="' + escapeHtml(id) + '"' + selected + '>' + escapeHtml(title) + '</option>';
                });
                select.innerHTML = options;
                select.dispatchEvent(new Event('change', { bubbles: true }));
            })
            .catch(function (error) {
                console.error('Error loading entities for ' + entityType + ':', error);
                select.innerHTML = '<option value="">-- Failed to load ' + escapeHtml(entityType) + 's --</option>';
            });
    }

    function serializeDataProps(container) {
        var config = {};
        container.querySelectorAll('[data-prop]').forEach(function (el) {
            var name = el.dataset.prop;
            if (!name) return;

            if (el.type === 'checkbox') {
                config[name] = el.checked;
            } else if (el.type === 'number') {
                config[name] = el.value !== '' ? parseFloat(el.value) : null;
            } else if (el.matches('select.entity-picker') && el.dataset.entityPickerLoading === 'true' && !el.value) {
                config[name] = el.dataset.currentValue || null;
            } else {
                config[name] = el.value || null;
            }
        });
        return JSON.stringify(config);
    }

    function escapeHtml(text) {
        if (text === null || text === undefined) return '';
        var div = document.createElement('div');
        div.textContent = String(text);
        return div.innerHTML;
    }

    window.WebWayFormComponents = {
        loadEntityPickerOptions: loadEntityPickerOptions,
        serializeDataProps: serializeDataProps
    };
})();
