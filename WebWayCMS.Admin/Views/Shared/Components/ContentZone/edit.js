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
        const displayModeField = form.querySelector('.cz-display-mode-field');
        const displayModeButtons = displayModeField
            ? Array.from(displayModeField.querySelectorAll('[data-display-mode]'))
            : [];

        let editingItemId = null;
        // 'Single' or 'List'. Only list-capable widgets can be List; set from the toggle when adding
        // and from the item's metadata when editing (where the settings dialog owns the choice).
        let displayMode = 'Single';
        // The configurator only renders the current display mode's fields, so its serialized
        // values are merged over these stored/previous values: switching Single <-> List (or saving
        // in one mode) keeps the other mode's settings instead of dropping them.
        let baseProps = {};

        function parseProps(json) {
            try {
                const parsed = JSON.parse(json || '{}');
                return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? parsed : {};
            } catch (e) {
                return {};
            }
        }

        function selectedSupportsList() {
            const option = componentSelector.options[componentSelector.selectedIndex];
            return !!option && option.dataset.supportsList === 'true';
        }

        function setDisplayMode(mode) {
            displayMode = mode === 'List' ? 'List' : 'Single';
            displayModeButtons.forEach(function (btn) {
                const active = btn.dataset.displayMode === displayMode;
                btn.classList.toggle('is-primary', active);
                btn.classList.toggle('is-selected', active);
                btn.setAttribute('aria-pressed', active ? 'true' : 'false');
            });
        }

        function updateDisplayModeField() {
            if (!displayModeField) return;
            const supportsList = selectedSupportsList();
            if (!supportsList) setDisplayMode('Single');
            // When editing, the mode is shown but changed through the settings dialog.
            displayModeField.style.display = supportsList ? '' : 'none';
            displayModeButtons.forEach(function (btn) { btn.disabled = editingItemId !== null; });
        }

        function parseDisplayMode(metadataJson) {
            try {
                const metadata = JSON.parse(metadataJson || '{}');
                const mode = metadata.DisplayMode ?? metadata.displayMode;
                return mode === 'List' || mode === 2 ? 'List' : 'Single';
            } catch (e) {
                return 'Single';
            }
        }

        displayModeButtons.forEach(function (btn) {
            btn.addEventListener('click', function () {
                if (btn.dataset.displayMode === displayMode) return;
                updatePropertiesJson();
                baseProps = parseProps(propsJsonInput ? propsJsonInput.value : '{}');
                setDisplayMode(btn.dataset.displayMode);
                // Re-render the configurator for the new mode; current values carry over.
                componentSelector.dispatchEvent(new Event('change'));
            });
        });

        // Open modal for adding new item
        czOwnElements(container, '.zone-add-btn').forEach(function (btn) {
            btn.addEventListener('click', function () {
                form.reset();
                editingItemId = null;
                baseProps = {};
                setDisplayMode('Single');
                updateDisplayModeField();
                componentSelector.disabled = false;
                modal.querySelector('.modal-card-title').textContent = 'Add Widget';
                dynamicContainer.style.display = 'none';
                dynamicProperties.innerHTML = '';
                if (propsJsonInput) propsJsonInput.value = '{}';
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
                    setDisplayMode(parseDisplayMode(data.metadataJson));
                    baseProps = parseProps(data.componentPropertiesJson);
                    componentSelector.value = data.componentName;
                    componentSelector.disabled = true;
                    if (propsJsonInput) propsJsonInput.value = data.componentPropertiesJson || '{}';
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
        componentSelector.addEventListener('change', async function (event) {
            const componentName = this.value;

            // A user picking a different widget starts from a clean configuration; programmatic
            // re-renders (edit load, Single/List toggle) keep the carried-over values.
            if (event.isTrusted) {
                baseProps = {};
                if (propsJsonInput) propsJsonInput.value = '{}';
            }
            const selectedOption = this.options[this.selectedIndex];

            componentDescription.textContent = selectedOption.dataset.description || 'Select a component to configure.';
            updateDisplayModeField();

            if (!componentName) {
                dynamicContainer.style.display = 'none';
                dynamicProperties.innerHTML = '';
                return;
            }

            try {
                // Single and List have different configuration fields.
                const formUrl = '/wadmin/widgets/registry/' + encodeURIComponent(componentName) + '/form'
                    + (selectedSupportsList() ? '?displayMode=' + encodeURIComponent(displayMode.toLowerCase()) : '');
                const response = await fetch(formUrl, {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'RequestVerificationToken': getAntiForgeryToken()
                    },
                    body: JSON.stringify(propsJsonInput ? propsJsonInput.value : '{}')
                });
                if (!response.ok) throw new Error('Failed to load form');

                const html = await response.text();
                dynamicProperties.innerHTML = html;
                dynamicContainer.style.display = 'block';

                // Mode-based field visibility (Article widget specific) – run synchronously
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
                    updateModeVisibility();
                }

                updatePropertiesJson();

            } catch (error) {
                console.error('Error loading component form:', error);
                dynamicProperties.innerHTML = '<div class="notification is-danger">Failed to load component configuration form.</div>';
                dynamicContainer.style.display = 'block';
            }
        });

        function updatePropertiesJson() {
            if (propsJsonInput) {
                const rendered = parseProps(window.WebWayFormComponents
                    ? window.WebWayFormComponents.serializeDataProps(dynamicProperties)
                    : '{}');
                propsJsonInput.value = JSON.stringify(Object.assign({}, baseProps, rendered));
            }
        }

        dynamicProperties.addEventListener('change', updatePropertiesJson);
        dynamicProperties.addEventListener('input', updatePropertiesJson);

        // ── Widget settings (metadata) modal ─────────────────────────────────
        const settingsModalId = container.dataset.settingsModalId;
        const settingsModal = document.getElementById(settingsModalId);
        if (settingsModal) {
            const settingsFields = settingsModal.querySelector('.cz-settings-fields');
            const settingsError = settingsModal.querySelector('.cz-modal-error');
            const settingsTitle = settingsModal.querySelector('.modal-card-title');
            let settingsItemId = null;
            let settingsComponentName = null;

            function showSettingsError(message) {
                settingsError.textContent = message;
                settingsError.style.display = '';
            }

            function hideSettingsError() {
                settingsError.textContent = '';
                settingsError.style.display = 'none';
            }

            function closeSettings() {
                settingsModal.classList.remove('is-active');
                settingsItemId = null;
                settingsComponentName = null;
            }

            settingsModal.querySelector('.modal-background').addEventListener('click', closeSettings);
            settingsModal.querySelector('.modal-card-head .delete').addEventListener('click', closeSettings);
            settingsModal.querySelectorAll('.cancel-btn').forEach(function (btn) {
                btn.addEventListener('click', closeSettings);
            });

            // Open the settings (metadata) editor for an item
            czOwnElements(container, '.zone-settings-item').forEach(function (btn) {
                btn.addEventListener('click', async function () {
                    const zoneObject = this.closest('.zone-object-edit');
                    settingsItemId = zoneObject.dataset.itemId;
                    settingsComponentName = zoneObject.dataset.componentName || 'widget';

                    if (!settingsItemId || settingsItemId === '00000000-0000-0000-0000-000000000000') {
                        alert('Cannot open settings: item ID not found.');
                        return;
                    }

                    settingsTitle.textContent = 'Widget Settings — ' + settingsComponentName;
                    settingsFields.innerHTML = '<p class="has-text-grey">Loading settings…</p>';
                    hideSettingsError();
                    settingsModal.classList.add('is-active');

                    try {
                        const response = await fetch('/api/contentzones/items/' + encodeURIComponent(settingsItemId));
                        if (!response.ok) {
                            throw new Error('Failed to load item');
                        }
                        const data = await response.json();

                        const formResponse = await fetch('/wadmin/widgets/registry/' + encodeURIComponent(settingsComponentName) + '/metadata-form', {
                            method: 'POST',
                            headers: {
                                'Content-Type': 'application/json',
                                'RequestVerificationToken': getAntiForgeryToken()
                            },
                            body: JSON.stringify(data.metadataJson || '{}')
                        });
                        if (!formResponse.ok) {
                            throw new Error('Failed to load the settings form');
                        }

                        settingsFields.innerHTML = await formResponse.text();
                    } catch (error) {
                        console.error('Error loading widget settings:', error);
                        settingsFields.innerHTML = '';
                        showSettingsError(error.message);
                    }
                });
            });

            // Save the item's metadata
            settingsModal.querySelector('.cz-settings-save').addEventListener('click', async function () {
                if (!settingsItemId) return;

                try {
                    const metadataJson = window.WebWayFormComponents
                        ? window.WebWayFormComponents.serializeDataProps(settingsModal)
                        : '{}';

                    const response = await fetch('/api/contentzones/items/' + encodeURIComponent(settingsItemId) + '/metadata', {
                        method: 'PUT',
                        headers: {
                            'Content-Type': 'application/json',
                            'RequestVerificationToken': getAntiForgeryToken()
                        },
                        body: JSON.stringify({ metadataJson: metadataJson })
                    });

                    if (!response.ok) {
                        const errorData = await response.json().catch(function () { return {}; });
                        throw new Error(errorData.error || 'Failed to save settings');
                    }

                    window.location.reload();
                } catch (error) {
                    console.error('Error saving widget settings:', error);
                    showSettingsError(error.message);
                }
            });
        }

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
            const parentPageNodeIdField = form.querySelector('[name="parentPageNodeId"]');
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
                        parentPageNodeId: parentPageNodeIdField ? parentPageNodeIdField.value : null,
                        slotName: slotNameField ? slotNameField.value : null,
                        itemId: editingItemId,
                        componentName: componentName,
                        componentPropertiesJson: propertiesJson,
                        // New items carry the Single/List choice; updates keep the stored metadata.
                        metadataJson: !editingItemId && selectedSupportsList()
                            ? JSON.stringify({ DisplayMode: displayMode })
                            : null
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
    });
}

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initContentZones);
} else {
    initContentZones();
}
