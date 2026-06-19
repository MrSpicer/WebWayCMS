(function () {
  'use strict';

  // --- Validators ---

  var validators = {
    required: function (value, input) {
      // Checkboxes always have a value via ASP.NET's hidden companion input — never block on unchecked
      if (input.type === 'checkbox') return true;
      return value.trim() !== '';
    },

    email: function (value) {
      if (value.trim() === '') return true;
      return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value.trim());
    },

    length: function (value, input) {
      var min = parseInt(input.getAttribute('data-val-length-min') || '0', 10);
      var max = parseInt(input.getAttribute('data-val-length-max') || '0', 10);
      var len = value.length;
      if (min > 0 && len < min) return false;
      if (max > 0 && len > max) return false;
      return true;
    },

    equalto: function (value, input) {
      var otherRef = input.getAttribute('data-val-equalto-other') || '';
      var parts = otherRef.split('.');
      var otherName = parts[parts.length - 1];
      var form = input.form;
      if (!form || !otherName) return true;
      // Find input whose name ends with the referenced property name
      var inputs = form.querySelectorAll('input[name]');
      var other = null;
      for (var i = 0; i < inputs.length; i++) {
        var n = inputs[i].name;
        if (n === otherName || n.endsWith('.' + otherName)) {
          other = inputs[i];
          break;
        }
      }
      if (!other) return true;
      return value === other.value;
    },

    phone: function (value) {
      if (value.trim() === '') return true;
      return /^[\d\s+\-()\\.ext]+$/i.test(value.trim());
    },

    nativeRequired: function (value, input) {
      if (input.type === 'checkbox') return true;
      if (input.tagName === 'SELECT') return value !== '';
      return value.trim() !== '';
    },

    nativeMinlength: function (value, input) {
      var min = parseInt(input.getAttribute('minlength') || '0', 10);
      if (min > 0 && value.length < min) return false;
      return true;
    },

    nativeMaxlength: function (value, input) {
      var max = parseInt(input.getAttribute('maxlength') || '0', 10);
      if (max > 0 && value.length > max) return false;
      return true;
    },

    nativePattern: function (value, input) {
      if (value.trim() === '') return true;
      var pattern = input.getAttribute('pattern');
      if (!pattern) return true;
      return new RegExp('^(?:' + pattern + ')$').test(value);
    },

    nativeMin: function (value, input) {
      var min = input.getAttribute('min');
      if (min === null || min === '' || value === '') return true;
      return parseFloat(value) >= parseFloat(min);
    },

    nativeMax: function (value, input) {
      var max = input.getAttribute('max');
      if (max === null || max === '' || value === '') return true;
      return parseFloat(value) <= parseFloat(max);
    }
  };

  // --- Rule extraction ---

  function getRulesForInput(input) {
    var rules = [];

    if (input.getAttribute('data-val') === 'true') {
      if (input.getAttribute('data-val-required') !== null) {
        rules.push({ name: 'required', message: input.getAttribute('data-val-required') });
      }
      if (input.getAttribute('data-val-email') !== null) {
        rules.push({ name: 'email', message: input.getAttribute('data-val-email') });
      }
      if (input.getAttribute('data-val-length') !== null) {
        rules.push({ name: 'length', message: input.getAttribute('data-val-length') });
      }
      if (input.getAttribute('data-val-equalto') !== null) {
        rules.push({ name: 'equalto', message: input.getAttribute('data-val-equalto') });
      }
      if (input.getAttribute('data-val-phone') !== null) {
        rules.push({ name: 'phone', message: input.getAttribute('data-val-phone') });
      }
      return rules;
    }

    // Native HTML5 rules for admin forms (input has a data-valmsg-for span but no data-val="true")
    if (input.hasAttribute('required') || input.required) {
      rules.push({ name: 'nativeRequired', message: 'This field is required.' });
    }
    if (input.getAttribute('minlength')) {
      rules.push({ name: 'nativeMinlength', message: 'Value is too short.' });
    }
    if (input.getAttribute('maxlength')) {
      rules.push({ name: 'nativeMaxlength', message: 'Value is too long.' });
    }
    if (input.getAttribute('pattern')) {
      rules.push({ name: 'nativePattern', message: 'Please enter a valid value.' });
    }
    if (input.getAttribute('min') !== null && input.getAttribute('min') !== '') {
      rules.push({ name: 'nativeMin', message: 'Value is below the minimum.' });
    }
    if (input.getAttribute('max') !== null && input.getAttribute('max') !== '') {
      rules.push({ name: 'nativeMax', message: 'Value exceeds the maximum.' });
    }

    return rules;
  }

  // --- Error span lookup ---

  function getErrorSpan(input) {
    var form = input.form;
    if (!form || !input.name) return null;
    return form.querySelector('[data-valmsg-for="' + CSS.escape(input.name) + '"]');
  }

  function showError(input, message) {
    input.classList.add('is-danger');
    var span = getErrorSpan(input);
    if (span) {
      span.textContent = message;
    }
  }

  function clearError(input) {
    input.classList.remove('is-danger');
    var span = getErrorSpan(input);
    if (span) {
      span.textContent = '';
    }
  }

  // --- Validate a single input ---

  function validateInput(input) {
    var rules = getRulesForInput(input);
    if (rules.length === 0) return true;

    var value = input.value || '';

    for (var i = 0; i < rules.length; i++) {
      var rule = rules[i];
      var fn = validators[rule.name];
      if (!fn) continue;
      if (!fn(value, input)) {
        showError(input, rule.message);
        return false;
      }
    }

    clearError(input);
    return true;
  }

  // --- Collect validated inputs for a form ---

  function getValidatedInputs(form) {
    var all = Array.from(form.querySelectorAll('input, textarea, select'));
    return all.filter(function (input) {
      if (input.type === 'hidden') return false;
      if (input.getAttribute('data-val') === 'true') return true;
      // Admin form fields: has a data-valmsg-for span and has at least one native constraint
      if (input.name && form.querySelector('[data-valmsg-for="' + CSS.escape(input.name) + '"]')) {
        return (
          input.hasAttribute('required') ||
          input.getAttribute('minlength') ||
          input.getAttribute('maxlength') ||
          input.getAttribute('pattern') ||
          (input.getAttribute('min') !== null && input.getAttribute('min') !== '') ||
          (input.getAttribute('max') !== null && input.getAttribute('max') !== '')
        );
      }
      return false;
    });
  }

  // --- Init a single form ---

  function initForm(form) {
    var inputs = getValidatedInputs(form);
    if (inputs.length === 0) return;

    inputs.forEach(function (input) {
      input.addEventListener('blur', function () {
        validateInput(input);
      });

      if (input.type === 'checkbox' || input.tagName === 'SELECT') {
        input.addEventListener('change', function () {
          validateInput(input);
        });
      } else {
        input.addEventListener('input', function () {
          // Re-validate while typing only if already showing an error
          if (input.classList.contains('is-danger')) {
            validateInput(input);
          }
        });
      }
    });

    form.addEventListener('submit', function (e) {
      var valid = true;
      inputs.forEach(function (input) {
        if (!validateInput(input)) {
          valid = false;
        }
      });

      if (!valid) {
        e.preventDefault();
        var firstInvalid = inputs.find(function (i) { return i.classList.contains('is-danger'); });
        if (firstInvalid) firstInvalid.focus();
      }
    });
  }

  // --- Init all forms on the page ---

  function init() {
    Array.from(document.querySelectorAll('form')).forEach(function (form) {
      var hasDataVal = form.querySelector('[data-val="true"]');
      var hasValmsgSpan = form.querySelector('[data-valmsg-for]');
      if (hasDataVal || hasValmsgSpan) {
        initForm(form);
      }
    });
  }

  document.addEventListener('DOMContentLoaded', init);

}());
