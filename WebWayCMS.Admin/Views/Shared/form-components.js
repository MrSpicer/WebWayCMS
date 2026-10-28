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

        document.querySelectorAll('.entity-picker-multi').forEach(initMultiEntityPicker);

        var observer = new MutationObserver(function (mutations) {
            mutations.forEach(function (mutation) {
                mutation.addedNodes.forEach(function (node) {
                    if (node.nodeType === 1) {
                        if (node.matches && node.matches('select.entity-picker')) {
                            loadEntityPickerOptions(node, node.dataset.entityType);
                        }
                        if (node.matches && node.matches('.entity-picker-multi')) {
                            initMultiEntityPicker(node);
                        }
                        if (node.querySelectorAll) {
                            node.querySelectorAll('select.entity-picker').forEach(function (s) {
                                loadEntityPickerOptions(s, s.dataset.entityType);
                            });
                            node.querySelectorAll('.entity-picker-multi').forEach(initMultiEntityPicker);
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

        // The endpoint is resolved server-side from the handler registry (FormEntityPicker), so
        // host content types work without a client-side map.
        var endpoint = select.dataset.endpoint;
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
                    var id = entityId(entity);
                    var title = entityTitle(entity);
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

    // ─── Multi-select picker ────────────────────────────────────────────────
    function initMultiEntityPicker(container) {
        if (container.dataset.entityPickerInitialized) return;
        container.dataset.entityPickerInitialized = 'true';

        var entityType = container.dataset.entityType;
        var endpoint = container.dataset.endpoint;
        var addSelect = container.querySelector('select.entity-picker-add');
        if (!endpoint) {
            if (addSelect) addSelect.innerHTML = '<option value="">-- Unknown entity type: ' + escapeHtml(entityType) + ' --</option>';
            return;
        }

        container._entityTitles = {};
        if (addSelect) addSelect.innerHTML = '<option value="">-- Loading ' + escapeHtml(entityType) + 's... --</option>';

        fetch(endpoint)
            .then(function (r) {
                if (!r.ok) throw new Error('Failed to load entities');
                return r.json();
            })
            .then(function (entities) {
                entities.forEach(function (entity) {
                    container._entityTitles[String(entityId(entity))] = entityTitle(entity);
                });

                var options = '<option value="">-- Add --</option>';
                entities.forEach(function (entity) {
                    options += '<option value="' + escapeHtml(entityId(entity)) + '">' + escapeHtml(entityTitle(entity)) + '</option>';
                });
                if (addSelect) addSelect.innerHTML = options;
                renderMultiValues(container);

                if (addSelect) addSelect.addEventListener('change', function () {
                    var value = addSelect.value;
                    if (!value) return;
                    var values = readMultiValues(container);
                    if (values.indexOf(value) < 0) values.push(value);
                    writeMultiValues(container, values);
                    addSelect.value = '';
                });
            })
            .catch(function (error) {
                console.error('Error loading entities for ' + entityType + ':', error);
                if (addSelect) addSelect.innerHTML = '<option value="">-- Failed to load ' + escapeHtml(entityType) + 's --</option>';
            });
    }

    function renderMultiValues(container) {
        var list = container.querySelector('.entity-picker-values');
        if (!list) return;

        var values = readMultiValues(container);
        var titles = container._entityTitles || {};
        list.innerHTML = '';

        values.forEach(function (value, index) {
            var item = document.createElement('li');
            item.className = 'entity-picker-value';

            var up = button('↑', 'Move up');
            up.disabled = index === 0;
            up.addEventListener('click', function () { move(container, index, -1); });

            var down = button('↓', 'Move down');
            down.disabled = index === values.length - 1;
            down.addEventListener('click', function () { move(container, index, 1); });

            var label = document.createElement('span');
            label.textContent = titles[String(value)] || String(value);

            var editBase = container.dataset.modalEditBase;
            if (editBase) {
                var edit = button('Edit', 'Edit');
                edit.className = 'button is-small is-white entity-picker-value-edit';
                edit.dataset.id = value;
                edit.dataset.modalEditBase = editBase;
                item.appendChild(edit);
            }

            var remove = button('×', 'Remove');
            remove.addEventListener('click', function () {
                var current = readMultiValues(container);
                current.splice(index, 1);
                writeMultiValues(container, current);
            });

            item.appendChild(up);
            item.appendChild(down);
            item.appendChild(label);
            item.appendChild(remove);
            list.appendChild(item);
        });

        // Model-bound forms post repeated inputs; JSON-bound forms read the single data-prop container.
        container.querySelectorAll('input.entity-picker-value-input').forEach(function (input) { input.remove(); });
        var name = container.dataset.name;
        if (name) {
            values.forEach(function (value, index) {
                var hidden = document.createElement('input');
                hidden.type = 'hidden';
                hidden.className = 'entity-picker-value-input';
                hidden.name = name + '[' + index + ']';
                hidden.value = value;
                container.appendChild(hidden);
            });
        }
    }

    function move(container, from, delta) {
        var values = readMultiValues(container);
        var to = from + delta;
        if (to < 0 || to >= values.length) return;
        var moved = values.splice(from, 1)[0];
        values.splice(to, 0, moved);
        writeMultiValues(container, values);
    }

    function readMultiValues(container) {
        try {
            var parsed = JSON.parse(container.dataset.values || '[]');
            return Array.isArray(parsed) ? parsed.map(String).filter(function (v) { return v; }) : [];
        } catch (e) {
            return [];
        }
    }

    function writeMultiValues(container, values) {
        container.dataset.values = JSON.stringify(values);
        renderMultiValues(container);
    }

    function button(text, label) {
        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'button is-small is-white entity-picker-value-action';
        btn.textContent = text;
        btn.setAttribute('aria-label', label);
        return btn;
    }

    function entityId(entity) {
        return entity.id || entity.Id || '';
    }

    function entityTitle(entity) {
        var id = entityId(entity);
        return entity.title || entity.Title || entity.name || entity.Name || id;
    }

    function serializeDataProps(container) {
        var config = {};
        container.querySelectorAll('[data-prop]').forEach(function (el) {
            var name = el.dataset.prop;
            if (!name) return;

            if (el.classList.contains('entity-picker-multi')) {
                config[name] = readMultiValues(el);
            } else if (el.type === 'checkbox') {
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
        serializeDataProps: serializeDataProps,
        setMultiValues: function (container, values) {
            writeMultiValues(container, values.map(String));
        },
        setMultiTitle: function (container, id, title) {
            container._entityTitles = container._entityTitles || {};
            container._entityTitles[String(id)] = title;
            renderMultiValues(container);
        }
    };
})();
