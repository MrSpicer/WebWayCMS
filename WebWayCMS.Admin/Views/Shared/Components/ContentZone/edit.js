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

        let editingItemId = null;

        // Open modal for adding new item
        czOwnElements(container, '.zone-add-btn').forEach(function (btn) {
            btn.addEventListener('click', function () {
                form.reset();
                editingItemId = null;
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
        componentSelector.addEventListener('change', async function () {
            const componentName = this.value;
            const selectedOption = this.options[this.selectedIndex];

            componentDescription.textContent = selectedOption.dataset.description || 'Select a component to configure.';

            if (!componentName) {
                dynamicContainer.style.display = 'none';
                dynamicProperties.innerHTML = '';
                return;
            }

            try {
                const response = await fetch('/admin/widgets/registry/' + encodeURIComponent(componentName) + '/form', {
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
                propsJsonInput.value = window.WebWayFormComponents
                    ? window.WebWayFormComponents.serializeDataProps(dynamicProperties)
                    : '{}';
            }
        }

        dynamicProperties.addEventListener('change', updatePropertiesJson);
        dynamicProperties.addEventListener('input', updatePropertiesJson);

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
    });
}

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initContentZones);
} else {
    initContentZones();
}
