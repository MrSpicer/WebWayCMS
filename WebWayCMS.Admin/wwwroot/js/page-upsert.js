// Page Upsert - Controller picker and dynamic config fields
(function () {
    var form = document.getElementById('pageForm');
    var controllerSelect = document.getElementById('ControllerName');
    var configArea = document.getElementById('configurationArea');
    var configFields = document.getElementById('configFields');
    var configJsonInput = document.getElementById('ConfigurationJson');

    controllerSelect.addEventListener('change', function () {
        var name = this.value;
        if (name) {
            if (configJsonInput) configJsonInput.value = '{}';
            loadControllerForm(name);
        } else {
            configArea.classList.add('is-hidden');
            configFields.innerHTML = '';
            if (configJsonInput) configJsonInput.value = '{}';
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
        }, 0);
    }
})();
