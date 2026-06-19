// Page Upsert - Controller picker and dynamic config fields                                                                                                                                                                                                                                                                                  
(function () {
    const form = document.getElementById('pageForm');
    const controllerSelect = document.querySelector('[data-page-controller-picker]') || document.getElementById('ControllerName');
    const configArea = document.getElementById('configurationArea');
    const configFields = document.getElementById('configFields');
    const configJsonInput = document.getElementById('ConfigurationJson');
    const viewNameSelect = document.getElementById('ViewName');
    const currentControllerName = form.dataset.controller || '';
    const currentViewName = form.dataset.viewName || '';
    let currentConfig = {};

    // Try to parse existing configuration                                                                                                                                                                                                                                                                                                    
    try {
        const raw = form.dataset.config;
        if (raw && raw !== '{}') {
            currentConfig = JSON.parse(raw);
        }
    } catch (e) { }

    // Load controllers list
    fetch('/admin/pages/registry')
        .then(function (r) { return r.json(); })
        .then(function (controllers) {
            controllers.forEach(function (c) {
                const opt = document.createElement('option');
                opt.value = c.name;
                opt.textContent = c.displayName + (c.description ? ' - ' + c.description : '');
                if (c.name === currentControllerName) {
                    opt.selected = true;
                }
                controllerSelect.appendChild(opt);
            });

            if (currentControllerName) {
                loadControllerProperties(currentControllerName);
            }
        });

    function populateViewNames(availableViews) {
        if (!viewNameSelect) return;
        const current = currentViewName || viewNameSelect.value;
        viewNameSelect.innerHTML = '<option value="">-- Default --</option>';
        (availableViews || []).forEach(function (v) {
            const opt = document.createElement('option');
            opt.value = v;
            opt.textContent = v;
            if (v === current) opt.selected = true;
            viewNameSelect.appendChild(opt);
        });
    }

    controllerSelect.addEventListener('change', function () {
        const name = this.value;
        if (name) {
            currentConfig = {};
            loadControllerProperties(name);
        } else {
            configArea.style.display = 'none';
            configFields.innerHTML = '';
            if (configJsonInput) configJsonInput.value = '{}';
            populateViewNames([]);
        }
    });

    function loadControllerProperties(name) {
        fetch('/admin/pages/registry/' + encodeURIComponent(name) + '/properties')
            .then(function (r) { return r.json(); })
            .then(function (data) {
                populateViewNames(data.availableViews);
                if (data.properties && data.properties.length > 0) {
                    configArea.style.display = '';
                    renderFields(data.properties);
                } else {
                    configArea.style.display = 'none';
                    configFields.innerHTML = '';
                    if (configJsonInput) configJsonInput.value = '{}';
                }
            })
            .catch(function () {
                configArea.style.display = 'none';
                configFields.innerHTML = '';
                populateViewNames([]);
            });
    }

    function renderFields(properties) {
        configFields.innerHTML = '';

        properties.forEach(function (prop) {
            const fieldDiv = document.createElement('div');
            fieldDiv.className = 'field';

            const label = document.createElement('label');
            label.className = 'label';
            label.textContent = prop.label;
            fieldDiv.appendChild(label);

            const controlDiv = document.createElement('div');
            controlDiv.className = 'control';

            let input;
            const existingValue = currentConfig[prop.name];

            switch (prop.editorType) {
                case 'textarea':
                case 'richtext':
                case 'html':
                case 'markdown':
                case 'code':
                    input = document.createElement('textarea');
                    input.className = 'textarea';
                    input.rows = 4;
                    input.value = existingValue ?? prop.defaultValue ?? '';
                    break;

                case 'checkbox':
                    input = document.createElement('input');
                    input.type = 'checkbox';
                    input.checked = existingValue ?? prop.defaultValue ?? false;
                    break;

                case 'number':
                    input = document.createElement('input');
                    input.type = 'number';
                    input.className = 'input';
                    input.value = existingValue ?? prop.defaultValue ?? '';
                    if (prop.min !== null && prop.min !== undefined) input.min = prop.min;
                    if (prop.max !== null && prop.max !== undefined) input.max = prop.max;
                    break;

                case 'dropdown':
                    input = document.createElement('select');
                    input.className = 'select';
                    const emptyOpt = document.createElement('option');
                    emptyOpt.value = '';
                    emptyOpt.textContent = '-- Select --';
                    input.appendChild(emptyOpt);
                    if (prop.dropdownOptions) {
                        Object.entries(prop.dropdownOptions).forEach(function ([val, lbl]) {
                            const opt = document.createElement('option');
                            opt.value = val;
                            opt.textContent = lbl;
                            if (val === (existingValue ?? '').toString()) opt.selected = true;
                            input.appendChild(opt);
                        });
                    }
                    break;

                default:
                    input = document.createElement('input');
                    input.type = 'text';
                    input.className = 'input';
                    input.value = existingValue ?? prop.defaultValue ?? '';
                    if (prop.placeholder) input.placeholder = prop.placeholder;
                    if (prop.maxLength) input.maxLength = prop.maxLength;
                    break;
            }

            input.dataset.propName = prop.name;
            input.dataset.editorType = prop.editorType;
            if (prop.isRequired) input.required = true;

            controlDiv.appendChild(input);
            fieldDiv.appendChild(controlDiv);

            if (prop.helpText) {
                const help = document.createElement('p');
                help.className = 'help';
                help.textContent = prop.helpText;
                fieldDiv.appendChild(help);
            }

            configFields.appendChild(fieldDiv);
        });
    }

    // Collect config values before form submission
    form.addEventListener('submit', function () {
        const config = {};
        configFields.querySelectorAll('[data-prop-name]').forEach(function (el) {
            const name = el.dataset.propName;
            const editorType = el.dataset.editorType;

            if (editorType === 'checkbox') {
                config[name] = el.checked;
            } else if (editorType === 'number') {
                config[name] = el.value !== '' ? parseFloat(el.value) : null;
            } else {
                config[name] = el.value;
            }
        });
        if (configJsonInput) configJsonInput.value = JSON.stringify(config);
    });
})();
