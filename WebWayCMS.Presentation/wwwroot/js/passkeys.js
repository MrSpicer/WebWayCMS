(function () {
  'use strict';

  // --- Base64 helper + PublicKeyCredential.toJSON polyfill -------------------------
  //
  // PublicKeyCredential.toJSON() is not yet universally supported (notably Safari and some
  // password managers). This polyfill — straight from the ASP.NET Core WebAuthn docs — serialises
  // the credential the way the server's PerformPasskeyAttestationAsync / PasskeySignInAsync expect.

  function convertToBase64(arrayBuffer) {
    var bytes = new Uint8Array(arrayBuffer);
    var binary = '';
    var chunkSize = 0x8000;
    for (var i = 0; i < bytes.length; i += chunkSize) {
      binary += String.fromCharCode.apply(null, bytes.subarray(i, i + chunkSize));
    }
    return btoa(binary);
  }

  if (typeof PublicKeyCredential !== 'undefined' && !PublicKeyCredential.prototype.toJSON) {
    PublicKeyCredential.prototype.toJSON = function () {
      var json = {
        id: this.id,
        rawId: convertToBase64(this.rawId),
        type: this.type
      };

      if (this.response) {
        var response = this.response;
        json.response = {
          clientDataJSON: convertToBase64(response.clientDataJSON)
        };

        if (response instanceof AuthenticatorAttestationResponse) {
          json.response.attestationObject = convertToBase64(response.attestationObject);
          json.response.transports = response.getTransports();
        } else if (response instanceof AuthenticatorAssertionResponse) {
          json.response.authenticatorData = convertToBase64(response.authenticatorData);
          json.response.signature = convertToBase64(response.signature);
          json.response.userHandle = response.userHandle ? convertToBase64(response.userHandle) : null;
        }
      }

      if (this.getClientExtensionResults) {
        json.clientExtensionResults = this.getClientExtensionResults();
      }

      return json;
    };
  }

  // --- Framework-agnostic credential helpers ---------------------------------------

  function createCredential(options) {
    return navigator.credentials
      .create({ publicKey: PublicKeyCredential.parseCreationOptionsFromJSON(options) })
      .then(function (credential) { return credential.toJSON(); });
  }

  function requestCredential(options) {
    return navigator.credentials
      .get({ publicKey: PublicKeyCredential.parseRequestOptionsFromJSON(options) })
      .then(function (credential) { return credential.toJSON(); });
  }

  function postJson(url, body) {
    return fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    });
  }

  function isUserCancellation(err) {
    // NotAllowedError (and AbortError on some browsers) is thrown when the user cancels the
    // credential prompt — treat that as a deliberate dismissal, not a failure to surface.
    return err && (err.name === 'NotAllowedError' || err.name === 'AbortError');
  }

  // Only accept an app-relative URL (starts with '/', but not '//' or '/\') so an attacker-supplied
  // returnUrl can never redirect the browser off-site. Falls back to the site root.
  function safeReturnUrl(value) {
    if (typeof value === 'string' && value.charAt(0) === '/' && value.charAt(1) !== '/' && value.charAt(1) !== '\\') {
      return value;
    }
    return '/';
  }

  // --- Login page: "Sign in with a passkey" ----------------------------------------

  function initPasskeyLogin() {
    var button = document.getElementById('passkey-login-button');
    if (!button) return;

    button.addEventListener('click', function () {
      var returnUrl = safeReturnUrl(button.getAttribute('data-return-url'));
      var status = document.getElementById('passkey-login-status');

      fetch('/Identity/Account/PasskeyRequestOptions', { method: 'POST' })
        .then(function (response) {
          if (!response.ok) throw new Error('Could not begin passkey sign-in.');
          return response.text();
        })
        .then(function (text) { return JSON.parse(text); })
        .then(requestCredential)
        .then(function (credential) { return postJson('/Identity/Account/PasskeyAssertion', credential); })
        .then(function (response) {
          return response.text().then(function (text) {
            return { ok: response.ok, body: text ? JSON.parse(text) : {} };
          });
        })
        .then(function (result) {
          if (result.ok) {
            if (result.body && result.body.requiresTwoFactor) {
              window.location.href = '/Identity/Account/LoginWith2fa?ReturnUrl=' + encodeURIComponent(returnUrl) + '&RememberMe=false';
            } else {
              window.location.href = returnUrl;
            }
          } else if (status) {
            status.textContent = (result.body && result.body.title) ? result.body.title : 'Passkey sign-in failed.';
          }
        })
        .catch(function (err) {
          if (isUserCancellation(err)) return;
          if (status) status.textContent = 'Passkey sign-in failed.';
        });
    });
  }

  // --- Manage page: add a passkey ---------------------------------------------------

  function initPasskeyManage() {
    var button = document.getElementById('passkey-add-button');
    if (!button) return;

    button.addEventListener('click', function () {
      var status = document.getElementById('passkey-status');

      fetch('/Identity/Account/PasskeyCreationOptions', { method: 'POST' })
        .then(function (response) {
          if (!response.ok) throw new Error('Could not begin passkey creation.');
          return response.text();
        })
        .then(function (text) { return JSON.parse(text); })
        .then(createCredential)
        .then(function (credential) { return postJson('/Identity/Account/PasskeyRegistration', credential); })
        .then(function (response) {
          if (response.ok) {
            window.location.reload();
          } else if (status) {
            status.textContent = 'Failed to add passkey.';
          }
        })
        .catch(function (err) {
          if (isUserCancellation(err)) return;
          if (status) status.textContent = 'Failed to add passkey.';
        });
    });
  }

  document.addEventListener('DOMContentLoaded', function () {
    initPasskeyLogin();
    initPasskeyManage();
  });
}());
