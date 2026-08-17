// Page Upsert - Controller picker and dynamic config fields
(function () {
    var form = document.getElementById('pageForm');
    var controllerSelect = document.getElementById('ControllerName');
    var viewNameSelect = document.getElementById('ViewName');
    var configArea = document.getElementById('configurationArea');
    var configFields = document.getElementById('configFields');
    var configJsonInput = document.getElementById('ConfigurationJson');

    controllerSelect.addEventListener('change', function () {
        var name = this.value;
        if (name) {
            if (configJsonInput) configJsonInput.value = '{}';
            loadControllerForm(name);
            loadControllerViews(name);
        } else {
            configArea.classList.add('is-hidden');
            configFields.innerHTML = '';
            if (configJsonInput) configJsonInput.value = '{}';
            resetViewNameOptions();
        }
    });

    function loadControllerForm(name) {
        fetch('/wadmin/pages/registry/' + encodeURIComponent(name) + '/form', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getAntiForgeryToken()
            },
            body: JSON.stringify(configJsonInput ? configJsonInput.value : '{}')
        })
            .then(function (r) {
                if (!r.ok) throw new Error('Failed to load configuration form');
                return r.text();
            })
            .then(function (html) {
                configFields.innerHTML = html;
                configArea.classList.remove('is-hidden');
                var heading = document.getElementById('configurationHeading');
                if (heading) {
                    heading.textContent = name + ' Settings';
                }
            })
            .catch(function (err) {
                console.error(err);
                configArea.classList.add('is-hidden');
                configFields.innerHTML = '';
                if (configJsonInput) configJsonInput.value = '{}';
            });
    }

    function loadControllerViews(name) {
        fetch('/wadmin/pages/registry/' + encodeURIComponent(name) + '/properties')
            .then(function (r) {
                if (!r.ok) throw new Error('Failed to load available views');
                return r.json();
            })
            .then(function (data) {
                var availableViews = data.availableViews || [];
                rebuildViewNameOptions(availableViews);
            })
            .catch(function (err) {
                console.error(err);
                resetViewNameOptions();
            });
    }

    function rebuildViewNameOptions(availableViews) {
        if (!viewNameSelect) return;

        var current = viewNameSelect.value;
        viewNameSelect.innerHTML = '';

        var defaultOption = document.createElement('option');
        defaultOption.value = '';
        defaultOption.textContent = '-- Default --';
        viewNameSelect.appendChild(defaultOption);

        availableViews.forEach(function (viewName) {
            var option = document.createElement('option');
            option.value = viewName;
            option.textContent = viewName;
            viewNameSelect.appendChild(option);
        });

        // Preserve the selection if it is still offered; switching page type legitimately
        // invalidates a view name, in which case fall back to the default.
        var values = Array.prototype.map.call(viewNameSelect.options, function (o) { return o.value; });
        viewNameSelect.value = values.indexOf(current) !== -1 ? current : '';
    }

    function resetViewNameOptions() {
        if (!viewNameSelect) return;
        viewNameSelect.innerHTML = '';
        var defaultOption = document.createElement('option');
        defaultOption.value = '';
        defaultOption.textContent = '-- Default --';
        viewNameSelect.appendChild(defaultOption);
        viewNameSelect.value = '';
    }

    function getAntiForgeryToken() {
        var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
        return tokenInput ? tokenInput.value : '';
    }

    // Collect config values before form submission
    form.addEventListener('submit', function () {
        if (configJsonInput && window.WebWayFormComponents) {
            configJsonInput.value = window.WebWayFormComponents.serializeDataProps(configFields);
        }
    });

    // Load initial form if controller is pre-selected
    if (controllerSelect.value) {
        setTimeout(function () {
            loadControllerForm(controllerSelect.value);
            loadControllerViews(controllerSelect.value);
        }, 0);
    }
})();
