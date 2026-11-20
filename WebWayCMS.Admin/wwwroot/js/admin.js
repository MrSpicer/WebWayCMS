// Admin JS — shared helpers (window.WebWayAdmin), burger toggle, delete modal, confirm forms

// Shared helpers for every admin script. admin.js is loaded by _AdminLayout before any other admin
// script, so the others can rely on window.WebWayAdmin being defined.
window.WebWayAdmin = (function () {
    const EMPTY_GUID = '00000000-0000-0000-0000-000000000000';

    // The antiforgery request token: the layout's csrf-token meta tag, else a rendered hidden input.
    function token() {
        const meta = document.querySelector('meta[name="csrf-token"]');
        if (meta && meta.content) return meta.content;
        const input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    // fetch() with the antiforgery header. options: method, json (serialized as the JSON body),
    // body (sent as is, e.g. FormData), fallbackError. A non-2xx response throws an Error carrying the
    // server's { error } message, or fallbackError when the body has none (or is not JSON).
    async function request(url, options) {
        const opts = options || {};
        const headers = { 'RequestVerificationToken': token() };
        let body = opts.body;
        if (opts.json !== undefined) {
            headers['Content-Type'] = 'application/json';
            body = JSON.stringify(opts.json);
        }
        const response = await fetch(url, { method: opts.method || 'GET', headers: headers, body: body });
        if (!response.ok) {
            const data = await response.json().catch(function () { return {}; });
            throw new Error((data && data.error) || opts.fallbackError || 'Request failed');
        }
        return response;
    }

    async function fetchJson(url, options) {
        const response = await request(url, options);
        const text = await response.text();
        return text ? JSON.parse(text) : null;
    }

    async function fetchText(url, options) {
        const response = await request(url, options);
        return response.text();
    }

    function ready(fn) {
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', fn);
        } else {
            fn();
        }
    }

    function isEmptyGuid(id) {
        return !id || id === EMPTY_GUID;
    }

    return { token: token, request: request, fetchJson: fetchJson, fetchText: fetchText, ready: ready, isEmptyGuid: isEmptyGuid };
})();

// A form with data-confirm="message" asks before it submits (page history, page zone editor, ...).
document.addEventListener('submit', function (e) {
    const form = e.target;
    if (form instanceof HTMLFormElement && form.dataset.confirm && !window.confirm(form.dataset.confirm)) {
        e.preventDefault();
    }
});

document.addEventListener('DOMContentLoaded', function () {

    // ── Navbar burger ──────────────────────────────────────────
    const burger = document.getElementById('adminBurger');
    const navMenu = document.getElementById('adminNav');

    if (burger && navMenu) {
        burger.addEventListener('click', function () {
            const isActive = navMenu.classList.toggle('is-active');
            burger.classList.toggle('is-active', isActive);
            burger.setAttribute('aria-expanded', isActive.toString());
        });
    }

    // ── Navbar "Advanced" dropdown ────────────────────────────
    const advancedDropdown = document.getElementById('advancedDropdown');
    const advancedTrigger = document.getElementById('advancedDropdownTrigger');

    if (advancedDropdown && advancedTrigger) {
        function setDropdown(open) {
            advancedDropdown.classList.toggle('is-active', open);
            advancedTrigger.setAttribute('aria-expanded', open.toString());
        }

        advancedTrigger.addEventListener('click', function (e) {
            e.preventDefault();
            setDropdown(!advancedDropdown.classList.contains('is-active'));
        });

        document.addEventListener('click', function (e) {
            if (!advancedDropdown.contains(e.target)) {
                setDropdown(false);
            }
        });

        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && advancedDropdown.classList.contains('is-active')) {
                setDropdown(false);
                advancedTrigger.focus();
            }
        });
    }

    // ── List row "⋯" menus ────────────────────────────────────
    // One open at a time; closes on outside click / Escape; arrow keys move between items.
    (function () {
        let openMenu = null;

        function menuItems(dropdown) {
            return Array.from(dropdown.querySelectorAll('.dropdown-item'));
        }

        function closeMenu(returnFocus) {
            if (!openMenu) return;
            const trigger = openMenu.querySelector('[data-row-menu-trigger]');
            openMenu.classList.remove('is-active');
            trigger.setAttribute('aria-expanded', 'false');
            if (returnFocus) trigger.focus();
            openMenu = null;
        }

        function openRowMenu(dropdown, focusFirst) {
            closeMenu(false);
            dropdown.classList.add('is-active');
            dropdown.querySelector('[data-row-menu-trigger]').setAttribute('aria-expanded', 'true');
            openMenu = dropdown;
            // Flip upward when the menu would run past the bottom of the viewport and fits above.
            dropdown.classList.remove('is-up');
            const rect = dropdown.querySelector('.dropdown-menu').getBoundingClientRect();
            const triggerTop = dropdown.getBoundingClientRect().top;
            if (rect.bottom > window.innerHeight && triggerTop > rect.height) {
                dropdown.classList.add('is-up');
            }
            if (focusFirst) {
                const items = menuItems(dropdown);
                if (items[0]) items[0].focus();
            }
        }

        document.addEventListener('click', function (e) {
            const trigger = e.target.closest('[data-row-menu-trigger]');
            if (trigger) {
                e.preventDefault();
                const dropdown = trigger.closest('.dropdown');
                if (dropdown === openMenu) {
                    closeMenu(false);
                } else {
                    openRowMenu(dropdown, false);
                }
                return;
            }
            if (openMenu && !openMenu.contains(e.target)) {
                closeMenu(false);
            } else if (openMenu && e.target.closest('.dropdown-item')) {
                // Refocus the trigger first so the delete modal (registered below) restores focus to it.
                closeMenu(true);
            }
        });

        document.addEventListener('keydown', function (e) {
            const trigger = e.target.closest && e.target.closest('[data-row-menu-trigger]');
            if (trigger && e.key === 'ArrowDown') {
                e.preventDefault();
                openRowMenu(trigger.closest('.dropdown'), true);
                return;
            }
            if (!openMenu) return;
            if (e.key === 'Escape') {
                closeMenu(true);
                return;
            }
            if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp') return;
            const items = menuItems(openMenu);
            const index = items.indexOf(document.activeElement);
            if (index === -1) return;
            e.preventDefault();
            const next = e.key === 'ArrowDown'
                ? items[(index + 1) % items.length]
                : items[(index - 1 + items.length) % items.length];
            next.focus();
        });
    })();

    // ── Delete Confirmation Modal ──────────────────────────────
    (function () {
        const modal = document.getElementById('deleteConfirmModal');
        if (!modal) return;

        const form = document.getElementById('deleteConfirmForm');
        const msgEl = document.getElementById('deleteModalMessage');
        const titleEl = document.getElementById('deleteModalTitle');
        const passwordField = document.getElementById('deleteModalPasswordField');
        const passwordInput = document.getElementById('deleteModalPassword');
        let triggerEl = null;

        function getFocusable() {
            return Array.from(modal.querySelectorAll(
                'button:not([disabled]), [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'
            ));
        }

        // A trigger may carry data-delete-descendants (a count, all depths) and
        // data-delete-descendant-label (singular noun, e.g. "child page"): the
        // confirm then says what goes with the item.
        function deleteMessage(name, count, label) {
            const n = parseInt(count, 10);
            if (!(n > 0)) return 'Delete "' + name + '"? This cannot be undone.';
            const noun = (label || 'child item') + (n === 1 ? '' : 's');
            return 'Delete "' + name + '" and its ' + n + ' ' + noun + '? This cannot be undone.';
        }

        // data-delete-message replaces the generated message; data-delete-password asks for the
        // acting user's password (posted as CurrentPassword) before a destructive action.
        function openModal(name, actionUrl, count, label, message, askPassword) {
            triggerEl = document.activeElement;
            form.action = actionUrl;
            msgEl.textContent = message || deleteMessage(name, count, label);
            titleEl.textContent = 'Confirm Delete';
            if (passwordField) {
                passwordField.hidden = !askPassword;
                passwordInput.required = askPassword;
                passwordInput.value = '';
            }
            modal.style.display = '';
            modal.classList.add('is-active');
            if (askPassword && passwordInput) { passwordInput.focus(); return; }
            const focusable = getFocusable();
            if (focusable[0]) focusable[0].focus();
        }

        function closeModal() {
            modal.classList.remove('is-active');
            modal.style.display = 'none';
            if (triggerEl) triggerEl.focus();
        }

        // Focus trap
        modal.addEventListener('keydown', function (e) {
            if (!modal.classList.contains('is-active')) return;
            if (e.key === 'Escape') { closeModal(); return; }
            if (e.key !== 'Tab') return;
            const focusable = getFocusable();
            const first = focusable[0];
            const last = focusable[focusable.length - 1];
            if (e.shiftKey && document.activeElement === first) {
                e.preventDefault();
                last.focus();
            } else if (!e.shiftKey && document.activeElement === last) {
                e.preventDefault();
                first.focus();
            }
        });

        document.getElementById('deleteModalClose').addEventListener('click', closeModal);
        document.getElementById('deleteModalCancel').addEventListener('click', closeModal);
        document.getElementById('deleteModalBg').addEventListener('click', closeModal);

        // Wire delete trigger buttons
        document.addEventListener('click', function (e) {
            const btn = e.target.closest('[data-delete-trigger]');
            if (!btn) return;
            e.preventDefault();
            openModal(btn.dataset.deleteName || 'this item', btn.dataset.deleteUrl,
                btn.dataset.deleteDescendants, btn.dataset.deleteDescendantLabel,
                btn.dataset.deleteMessage, btn.dataset.deletePassword === 'true');
        });
    })();

});

// ── CKEditor (rich text editor) ────────────────────────────
// Loaded on every admin page; the CKEditor CDN script is only present when a
// view defines the "CKEditor" section, so guard on the CKEDITOR global.
function initRichTextEditor() {
	if (typeof CKEDITOR === 'undefined') {
		return; // CKEditor CDN not loaded on this page.
	}

	const editorElement = document.querySelector('.rich-text-editor');
	if (!editorElement) {
		return; // No editor on this page.
	}

	const {
		ClassicEditor,
		Essentials,
		Bold,
		Italic,
		Underline,
		Strikethrough,
		Code,
		BlockQuote,
		Heading,
		Link,
		List,
		Indent,
		IndentBlock,
		Paragraph,
		Font,
		FontSize,
		FontFamily,
		FontColor,
		FontBackgroundColor,
		Alignment,
		Table,
		TableToolbar,
		CodeBlock,
		HorizontalLine,
		SpecialCharacters,
		RemoveFormat,
		Highlight,
		SourceEditing,
		HtmlEmbed,
		GeneralHtmlSupport
	} = CKEDITOR;

	// License key injected server-side via the ckeditor-license-key meta tag (see _AdminLayout).
	// window.__APP_CONFIG__ is still honored if a host chooses to set it.
	const ckLicenseKey = (window.__APP_CONFIG__ && window.__APP_CONFIG__.ckEditorLicenseKey)
		|| (document.querySelector('meta[name="ckeditor-license-key"]')?.content)
		|| '';

	if (!ckLicenseKey) {
		console.warn('CKEditor license key not configured. Proceeding without it.');
		return;
	}

	ClassicEditor
		.create(editorElement, {
			licenseKey: ckLicenseKey,
			plugins: [
				Essentials, Bold, Italic, Underline, Strikethrough, Code, BlockQuote,
				Heading, Link, List, Indent, IndentBlock, Paragraph,
				Font, FontSize, FontFamily, FontColor, FontBackgroundColor, Alignment,
				Table, TableToolbar, CodeBlock, HorizontalLine, SpecialCharacters,
				RemoveFormat, Highlight, SourceEditing, HtmlEmbed, GeneralHtmlSupport
			],
			toolbar: [
				'htmlEmbed',
				'undo', 'redo', '|',
				'heading', '|',
				'bold', 'italic', 'underline', 'strikethrough', 'removeFormat', '|',
				'fontSize', 'fontFamily', 'fontColor', 'fontBackgroundColor', 'highlight', '|',
				'link', 'bulletedList', 'numberedList', 'indent', '|',
				'blockQuote', 'code', 'codeBlock', '|',
				'insertTable', 'horizontalLine', '|',
				'specialCharacters', '|',
				'alignment', '|',
				'undo', 'redo', '|',
				'sourceEditing'
			],
			table: {
				contentToolbar: ['tableColumn', 'tableRow', 'mergeTableCells']
			},
			htmlSupport: {
				allow: [
					{
						name: /.*/,
						attributes: true,
						classes: true,
						styles: true
					}
				]
			}
		})
		.then(editor => {
			const form = editorElement.closest('form');
			if (form) {
				form.addEventListener('submit', () => {
					editor.updateSourceElement();
				});
			}
		})
		.catch(error => console.log(error));
}

window.WebWayAdmin.ready(initRichTextEditor);
