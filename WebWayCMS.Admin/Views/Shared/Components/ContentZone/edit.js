// Content Zone Edit - Widget CRUD
// Each zone container has data-modal-id pointing to its modal

function czOwnElements(container, selector) {
    return Array.from(container.querySelectorAll(selector)).filter(function (el) {
        return el.closest('.content-zone-edit') === container;
    });
}

function getAntiForgeryToken() {
    const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
    return tokenInput ? tokenInput.value : '';
}

function initContentZones() {
    document.querySelectorAll('.content-zone-edit').forEach(function (container) {
        if (container.dataset.czInitialized) return;
        container.dataset.czInitialized = 'true';

        const modalId = container.dataset.modalId;
        const modal = document.getElementById(modalId);
        if (!modal) return;

        const form = modal.querySelector('.cz-widget-form');
        const componentSelector = form.querySelector('.component-selector');
        const componentDescription = form.querySelector('.component-description');
        const dynamicContainer = form.querySelector('.dynamic-properties-container');
        const dynamicProperties = form.querySelector('.dynamic-properties');
        const propsJsonInput = form.querySelector('.component-props-json');

        let currentProperties = [];
        let existingProperties = {};
        let editingItemId = null;

        // Open modal for adding new item
        czOwnElements(container, '.zone-add-btn').forEach(function (btn) {
            btn.addEventListener('click', function () {
                form.reset();
                existingProperties = {};
                editingItemId = null;
                componentSelector.disabled = false;
                modal.querySelector('.modal-card-title').textContent = 'Add Widget';
                dynamicContainer.style.display = 'none';
                dynamicProperties.innerHTML = '';
                modal.classList.add('is-active');
            });
        });

        // Close modal
        modal.querySelector('.modal-background').addEventListener('click', closeModal);
        modal.querySelector('.delete').addEventListener('click', closeModal);
        modal.querySelector('.cancel-btn').addEventListener('click', closeModal);

        function closeModal() {
            modal.classList.remove('is-active');
            editingItemId = null;
            componentSelector.disabled = false;
        }

        // Delete item handler
        czOwnElements(container, '.zone-delete-item').forEach(function (btn) {
            btn.addEventListener('click', async function () {
                const zoneObject = this.closest('.zone-object-edit');
                const itemId = zoneObject.dataset.itemId;
                const componentName = zoneObject.querySelector('.zone-object-label')?.textContent || 'this widget';

                if (!itemId || itemId === '00000000-0000-0000-0000-000000000000') {
                    alert('Cannot delete: Item ID not found.');
                    return;
                }

                if (!confirm('Are you sure you want to permanently delete "' + componentName + '"? This action cannot be undone.')) {
                    return;
                }

                try {
                    const response = await fetch('/api/contentzones/items/' + encodeURIComponent(itemId), {
                        method: 'DELETE',
                        headers: {
                            'RequestVerificationToken': getAntiForgeryToken()
                        }
                    });

                    if (!response.ok) {
                        const errorData = await response.json();
                        throw new Error(errorData.error || 'Failed to delete');
                    }

                    zoneObject.remove();
                } catch (error) {
                    console.error('Error deleting widget:', error);
                    alert('Failed to delete widget: ' + error.message);
                }
            });
        });

        // Edit item handler
        czOwnElements(container, '.zone-edit-item').forEach(function (btn) {
            btn.addEventListener('click', async function () {
                const zoneObject = this.closest('.zone-object-edit');
                const itemId = zoneObject.dataset.itemId;

                if (!itemId || itemId === '00000000-0000-0000-0000-000000000000') {
                    alert('Cannot edit: Item ID not found.');
                    return;
                }

                try {
                    const response = await fetch('/api/contentzones/items/' + encodeURIComponent(itemId));
                    if (!response.ok) {
                        throw new Error('Failed to load item data');
                    }

                    const data = await response.json();

                    editingItemId = itemId;
                    componentSelector.value = data.componentName;
                    componentSelector.disabled = true;
                    existingProperties = JSON.parse(data.componentPropertiesJson || '{}');
                    modal.querySelector('.modal-card-title').textContent = 'Edit Widget';
                    componentSelector.dispatchEvent(new Event('change'));
                    modal.classList.add('is-active');
                } catch (error) {
                    console.error('Error loading widget data:', error);
                    alert('Failed to load widget data: ' + error.message);
                }
            });
        });

        // Component selection change
        componentSelector.addEventListener('change', async function () {
            const componentName = this.value;
            const selectedOption = this.options[this.selectedIndex];

            componentDescription.textContent = selectedOption.dataset.description || 'Select a component to configure.';

            if (!componentName) {
                dynamicContainer.style.display = 'none';
                dynamicProperties.innerHTML = '';
                currentProperties = [];
                return;
            }

            try {
                const response = await fetch('/admin/contentzones/registry/' + encodeURIComponent(componentName) + '/properties');
                if (!response.ok) throw new Error('Failed to load properties');

                const data = await response.json();
                currentProperties = data.properties || [];

                if (currentProperties.length === 0) {
                    dynamicContainer.style.display = 'none';
                    dynamicProperties.innerHTML = '';
                    return;
                }

                renderPropertyFields(currentProperties);
                dynamicContainer.style.display = 'block';
            } catch (error) {
                console.error('Error loading component properties:', error);
                dynamicProperties.innerHTML = '<div class="notification is-danger">Failed to load component properties.</div>';
                dynamicContainer.style.display = 'block';
            }
        });

        function renderPropertyFields(properties) {
            const groups = {};
            properties.forEach(function (prop) {
                const group = prop.group || 'General';
                if (!groups[group]) groups[group] = [];
                groups[group].push(prop);
            });

            let html = '';
            for (const [groupName, groupProps] of Object.entries(groups)) {
                if (Object.keys(groups).length > 1) {
                    html += '<h4 class="is-size-6 has-text-weight-bold mt-3 mb-2">' + escapeHtml(groupName) + '</h4>';
                }
                groupProps.sort(function (a, b) { return (a.order || 0) - (b.order || 0); });
                for (const prop of groupProps) {
                    html += renderPropertyField(prop);
                }
            }

            dynamicProperties.innerHTML = html;

            dynamicProperties.querySelectorAll('input, select, textarea').forEach(function (input) {
                input.addEventListener('change', updatePropertiesJson);
                input.addEventListener('input', updatePropertiesJson);
            });

            // Mode-based field visibility
            var modeSelect = dynamicProperties.querySelector('[data-prop="Mode"]');
            if (modeSelect) {
                function updateModeVisibility() {
                    var mode = modeSelect.value;
                    var idField = dynamicProperties.querySelector('[data-prop="Id"]');
                    var listField = dynamicProperties.querySelector('[data-prop="ArticleListId"]');
                    var idContainer = idField ? idField.closest('.field') : null;
                    var listContainer = listField ? listField.closest('.field') : null;
                    if (idContainer) idContainer.style.display = (mode === 'List') ? 'none' : '';
                    if (listContainer) listContainer.style.display = (mode === 'Single') ? 'none' : '';
                }
                modeSelect.addEventListener('change', updateModeVisibility);
                setTimeout(updateModeVisibility, 100);
            }

            updatePropertiesJson();
        }

        function renderPropertyField(prop) {
            const existingValue = existingProperties[prop.name];
            const value = existingValue !== undefined ? existingValue : (prop.defaultValue || '');
            const required = prop.isRequired ? 'required' : '';
            const placeholder = prop.placeholder || '';
            const helpText = prop.helpText || '';

            let fieldHtml = '<div class="field">';
            fieldHtml += '<label class="label">' + escapeHtml(prop.label) + (prop.isRequired ? ' <span class="has-text-danger">*</span>' : '') + '</label>';
            fieldHtml += '<div class="control">';

            switch (prop.editorType) {
                case 'checkbox':
                    var checked = value === true || value === 'true' || value === 'True' ? 'checked' : '';
                    fieldHtml += '<label class="checkbox"><input type="checkbox" data-prop="' + prop.name + '" ' + checked + ' /></label>';
                    break;
                case 'number':
                    var min = prop.min !== null && !isNaN(prop.min) ? 'min="' + prop.min + '"' : '';
                    var max = prop.max !== null && !isNaN(prop.max) ? 'max="' + prop.max + '"' : '';
                    fieldHtml += '<input class="input" type="number" data-prop="' + prop.name + '" value="' + escapeHtml(String(value)) + '" ' + min + ' ' +
                        max + ' ' + required + ' style="max-width: 200px;" />';
                    break;
                case 'textarea':
                    fieldHtml += '<textarea class="textarea" data-prop="' + prop.name + '" rows="3" ' + required + '>' + escapeHtml(String(value)) +
                        '</textarea>';
                    break;
                case 'viewpicker':
                case 'dropdown':
                    fieldHtml += '<div class="select"><select data-prop="' + prop.name + '" ' + required + '>';
                    if (prop.dropdownOptions) {
                        for (const [optValue, optLabel] of Object.entries(prop.dropdownOptions)) {
                            var selected = String(value) === optValue ? 'selected' : '';
                            fieldHtml += '<option value="' + escapeHtml(optValue) + '" ' + selected + '>' + escapeHtml(optLabel) + '</option>';
                        }
                    }
                    fieldHtml += '</select></div>';
                    break;
                case 'guid':
                    if (prop.entityType) {
                        fieldHtml += '<div class="select is-fullwidth"><select data-prop="' + prop.name + '" class="entity-picker" data-entity-type="' +
                            prop.entityType + '" ' + required + '>';
                        fieldHtml += '<option value="">-- Loading ' + escapeHtml(prop.entityType) + 's... --</option>';
                        fieldHtml += '</select></div>';
                        setTimeout(function () { loadEntities(prop.name, prop.entityType, value); }, 0);
                    } else {
                        fieldHtml = '<input type="hidden" data-prop="' + prop.name + '" value="' + escapeHtml(String(value)) + '" />';
                        return fieldHtml;
                    }
                    break;
                default:
                    fieldHtml += '<input class="input" type="text" data-prop="' + prop.name + '" value="' + escapeHtml(String(value)) + '" placeholder="' +
                        escapeHtml(placeholder) + '" ' + required + ' />';
                    break;
            }

            fieldHtml += '</div>';
            if (helpText) fieldHtml += '<p class="help">' + escapeHtml(helpText) + '</p>';
            fieldHtml += '</div>';

            return fieldHtml;
        }

        async function loadEntities(propName, entityType, selectedValue) {
            const select = dynamicProperties.querySelector('select[data-prop="' + propName + '"]');
            if (!select) return;

            try {
                const endpoints = {
                    'ContentBlock': '/admin/contentblocks/api/list',
                    'Article': '/admin/articles/api/list',
                    'ArticleList': '/admin/articles/api/articlelists',
                    'ContentZone': '/admin/contentzones/api/list'
                };

                const endpoint = endpoints[entityType];
                if (!endpoint) {
                    select.innerHTML = '<option value="">-- Unknown entity type: ' + escapeHtml(entityType) + ' --</option>';
                    return;
                }

                const response = await fetch(endpoint);
                if (!response.ok) throw new Error('Failed to load entities');

                const entities = await response.json();

                let options = '<option value="">-- Select --</option>';
                for (const entity of entities) {
                    const id = entity.id || entity.Id;
                    const title = entity.title || entity.Title || entity.name || entity.Name || id;
                    const selected = String(id) === String(selectedValue) ? 'selected' : '';
                    options += '<option value="' + escapeHtml(id) + '" ' + selected + '>' + escapeHtml(title) + '</option>';
                }
                select.innerHTML = options;

                select.addEventListener('change', updatePropertiesJson);
                updatePropertiesJson();
            } catch (error) {
                console.error('Error loading entities:', error);
                select.innerHTML = '<option value="">-- Failed to load ' + escapeHtml(entityType) + 's --</option>';
            }
        }

        function updatePropertiesJson() {
            const properties = {};
            dynamicProperties.querySelectorAll('[data-prop]').forEach(function (input) {
                const propName = input.dataset.prop;
                if (input.type === 'checkbox') {
                    properties[propName] = input.checked;
                } else if (input.type === 'number') {
                    properties[propName] = input.value ? Number(input.value) : null;
                } else {
                    properties[propName] = input.value || null;
                }
            });
            propsJsonInput.value = JSON.stringify(properties);
        }

        // GUID generation
        dynamicProperties.addEventListener('click', function (e) {
            if (e.target.classList.contains('guid-gen-btn')) {
                const input = e.target.closest('.has-addons').querySelector('input[data-prop]');
                if (input) {
                    input.value = crypto.randomUUID();
                    updatePropertiesJson();
                }
            }
        });

        // Save widget
        modal.querySelector('.save-widget-btn').addEventListener('click', async function () {
            updatePropertiesJson();

            const componentName = componentSelector.value;
            if (!componentName) {
                alert('Please select a component.');
                return;
            }

            const zoneName = form.querySelector('[name="zoneName"]').value;
            const zoneIdField = czOwnElements(container, '.zone-id-field')[0];
            const zoneId = zoneIdField ? zoneIdField.value : null;
            const propertiesJson = propsJsonInput.value;
            const parentPageMasterIdField = form.querySelector('[name="parentPageMasterId"]');
            const slotNameField = form.querySelector('[name="slotName"]');

            try {
                const response = await fetch('/api/contentzones/items', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'RequestVerificationToken': getAntiForgeryToken()
                    },
                    body: JSON.stringify({
                        zoneName: zoneName,
                        zoneId: zoneId && zoneId !== '00000000-0000-0000-0000-000000000000' ? zoneId : null,
                        parentPageMasterId: parentPageMasterIdField ? parentPageMasterIdField.value : null,
                        slotName: slotNameField ? slotNameField.value : null,
                        itemId: editingItemId,
                        componentName: componentName,
                        componentPropertiesJson: propertiesJson
                    })
                });

                if (!response.ok) {
                    const errorData = await response.json();
                    throw new Error(errorData.error || 'Failed to save');
                }

                window.location.reload();
            } catch (error) {
                console.error('Error saving widget:', error);
                alert('Failed to save widget: ' + error.message);
            }
        });

        function escapeHtml(text) {
            if (text === null || text === undefined) return '';
            const div = document.createElement('div');
            div.textContent = String(text);
            return div.innerHTML;
        }
    });
}

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initContentZones);
} else {
    initContentZones();
}
