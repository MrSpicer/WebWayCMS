// Content Zone Drag - move widgets within a zone and between the zones of one page.
// Pointer events (mouse, touch, pen) on each item's drag handle, plus ArrowUp/ArrowDown on the
// focused handle. Every move is saved through the move API and the page reloads, like every other
// zone edit. Loaded after content-zone-edit.js (uses getAntiForgeryToken, czItemVersion). No jQuery, no inline script.
(function () {
    'use strict';

    const EMPTY_GUID = '00000000-0000-0000-0000-000000000000';
    const DRAG_THRESHOLD = 4;
    const SCROLL_EDGE = 40;
    const SCROLL_STEP = 14;
    const FOCUS_KEY = 'wwcms:cz-focus-item';
    const PLACEHOLDER_MAX_HEIGHT = 64;

    let drag = null;
    let saving = false;

    function zoneIdOf(zone) {
        const field = zone.querySelector(':scope > .zone-id-field');
        return field ? field.value : '';
    }

    function isPersistedZone(zone) {
        const id = zoneIdOf(zone);
        return !!id && id !== EMPTY_GUID;
    }

    // A zone's own items are its direct children (nested zones' items live deeper).
    function ownItems(zone, exclude) {
        return Array.from(zone.children).filter(function (el) {
            return el.classList.contains('zone-object-edit') && el !== exclude;
        });
    }

    function indexAmongItems(zone, node, exclude) {
        let index = 0;
        for (const el of zone.children) {
            if (el === node) return index;
            if (el.classList.contains('zone-object-edit') && el !== exclude) index++;
        }
        return index;
    }

    // Same zone, or another saved zone of the same page that is not inside the dragged item.
    function canDropInto(zone) {
        if (!zone || !isPersistedZone(zone) || drag.item.contains(zone)) return false;
        if (zone === drag.sourceZone) return true;
        const owner = zone.dataset.pageNodeId;
        return !!owner && owner === drag.sourceZone.dataset.pageNodeId;
    }

    async function moveItem(item, zoneId, position) {
        const itemId = item.dataset.itemId;
        if (saving) return;
        saving = true;
        document.body.classList.add('cz-saving');
        try {
            const response = await fetch('/api/contentzones/items/' + encodeURIComponent(itemId) + '/move', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': getAntiForgeryToken()
                },
                // The version the page rendered the item with; the API rejects the move when the
                // item changed since.
                body: JSON.stringify({
                    targetZoneId: zoneId,
                    position: position,
                    expectedVersionNumber: czItemVersion(item)
                })
            });
            if (!response.ok) {
                const errorData = await response.json().catch(function () { return {}; });
                throw new Error(errorData.error || 'Failed to move widget');
            }
        } catch (error) {
            console.error('Error moving widget:', error);
            alert('Failed to move widget: ' + error.message);
        } finally {
            saving = false;
        }
        // Reload either way so the editor (and the page's Pending status) matches what was saved.
        window.location.reload();
    }

    // ── Pointer drag ──────────────────────────────────────────────────────────

    function setTargetZone(zone) {
        if (drag.targetZone === zone) return;
        if (drag.targetZone) drag.targetZone.classList.remove('cz-drop-target', 'cz-drop-invalid');
        drag.targetZone = zone;
        if (zone) zone.classList.add(canDropInto(zone) ? 'cz-drop-target' : 'cz-drop-invalid');
    }

    function updatePlaceholder() {
        const x = drag.lastX;
        const y = drag.lastY;
        const hit = document.elementFromPoint(x, y);
        const zone = hit ? hit.closest('.content-zone-edit') : null;
        setTargetZone(zone);
        // Over nothing droppable: the placeholder stays where it was.
        if (!zone || !canDropInto(zone)) return;

        let before = null;
        for (const el of ownItems(zone, drag.item)) {
            const rect = el.getBoundingClientRect();
            if (y < rect.top + rect.height / 2) {
                before = el;
                break;
            }
        }
        const anchor = before || zone.querySelector(':scope > .zone-add-widget');
        if (drag.placeholder.parentNode === zone && drag.placeholder.nextElementSibling === anchor) return;
        if (anchor) zone.insertBefore(drag.placeholder, anchor);
        else zone.appendChild(drag.placeholder);
    }

    function moveGhost() {
        drag.ghost.style.transform = 'translate(' + (drag.lastX + 12) + 'px, ' + (drag.lastY + 12) + 'px)';
    }

    function autoScroll() {
        if (!drag || !drag.started) return;
        let delta = 0;
        if (drag.lastY < SCROLL_EDGE) delta = -SCROLL_STEP;
        else if (drag.lastY > window.innerHeight - SCROLL_EDGE) delta = SCROLL_STEP;
        if (delta !== 0) {
            window.scrollBy(0, delta);
            updatePlaceholder();
        }
        drag.scrollFrame = window.requestAnimationFrame(autoScroll);
    }

    function startDrag() {
        const item = drag.item;
        const rect = item.getBoundingClientRect();
        drag.started = true;
        drag.startIndex = indexAmongItems(drag.sourceZone, item, null);

        drag.placeholder = document.createElement('div');
        drag.placeholder.className = 'zone-drop-placeholder';
        drag.placeholder.setAttribute('aria-hidden', 'true');
        // Capped, so a tall widget's slot doesn't shove the drop targets away from the pointer.
        drag.placeholder.style.height = Math.min(rect.height, PLACEHOLDER_MAX_HEIGHT) + 'px';
        item.parentNode.insertBefore(drag.placeholder, item);
        // Hiding the item hides its nested zones too, so it can never be dropped into itself.
        item.classList.add('is-drag-source');

        drag.ghost = document.createElement('div');
        drag.ghost.className = 'zone-drag-ghost';
        drag.ghost.setAttribute('aria-hidden', 'true');
        const label = item.querySelector(':scope > .zone-object-toolbar > .zone-object-label');
        drag.ghost.textContent = label ? label.textContent.trim() : (item.dataset.componentName || 'Widget');
        document.body.appendChild(drag.ghost);
        document.body.classList.add('cz-dragging');

        moveGhost();
        setTargetZone(drag.sourceZone);
        drag.scrollFrame = window.requestAnimationFrame(autoScroll);
    }

    function endDrag(commit) {
        const current = drag;
        drag = null;
        document.removeEventListener('pointermove', onPointerMove);
        document.removeEventListener('pointerup', onPointerUp);
        document.removeEventListener('pointercancel', onPointerCancel);
        if (current.handle.hasPointerCapture(current.pointerId)) current.handle.releasePointerCapture(current.pointerId);
        if (!current.started) return;

        window.cancelAnimationFrame(current.scrollFrame);
        if (current.targetZone) current.targetZone.classList.remove('cz-drop-target', 'cz-drop-invalid');
        document.body.classList.remove('cz-dragging');
        current.ghost.remove();

        const zone = current.placeholder.parentNode;
        if (!zone) {
            // The placeholder's zone disappeared mid-drag (a nested re-render, another tab): put
            // the item back where it started instead of crashing on the drop.
            current.placeholder.remove();
            current.item.classList.remove('is-drag-source');
            return;
        }
        const position = indexAmongItems(zone, current.placeholder, current.item);
        const changed = zone !== current.sourceZone || position !== current.startIndex;

        // Show the item where it was dropped while the move saves; a cancel puts it back.
        if (commit && changed) zone.insertBefore(current.item, current.placeholder);
        current.placeholder.remove();
        current.item.classList.remove('is-drag-source');

        if (commit && changed) moveItem(current.item, zoneIdOf(zone), position);
    }

    function onPointerMove(e) {
        if (!drag || e.pointerId !== drag.pointerId) return;
        drag.lastX = e.clientX;
        drag.lastY = e.clientY;
        if (!drag.started) {
            if (Math.abs(e.clientX - drag.startX) < DRAG_THRESHOLD && Math.abs(e.clientY - drag.startY) < DRAG_THRESHOLD) return;
            startDrag();
        }
        moveGhost();
        updatePlaceholder();
    }

    function onPointerUp(e) {
        if (drag && e.pointerId === drag.pointerId) endDrag(true);
    }

    function onPointerCancel(e) {
        if (drag && e.pointerId === drag.pointerId) endDrag(false);
    }

    document.addEventListener('pointerdown', function (e) {
        const handle = e.target.closest('.zone-drag-handle');
        if (!handle || e.button !== 0 || drag || saving) return;
        const item = handle.closest('.zone-object-edit');
        const zone = item ? item.parentNode : null;
        if (!zone || !zone.classList.contains('content-zone-edit')) return;

        e.preventDefault();
        // Capture keeps events flowing while the item (and its handle) is hidden mid-drag; the
        // listeners sit on document, so tracking still works if capture is refused.
        try { handle.setPointerCapture(e.pointerId); } catch (err) { /* pointer no longer active */ }
        drag = {
            handle: handle,
            item: item,
            sourceZone: zone,
            pointerId: e.pointerId,
            startX: e.clientX,
            startY: e.clientY,
            lastX: e.clientX,
            lastY: e.clientY,
            started: false,
            startIndex: 0,
            placeholder: null,
            ghost: null,
            targetZone: null,
            scrollFrame: 0
        };
        document.addEventListener('pointermove', onPointerMove);
        document.addEventListener('pointerup', onPointerUp);
        document.addEventListener('pointercancel', onPointerCancel);
    });

    // ── Keyboard: Escape cancels a drag; arrows on a focused handle reorder ────

    document.addEventListener('keydown', function (e) {
        if (drag) {
            if (e.key === 'Escape') {
                e.preventDefault();
                endDrag(false);
            }
            return;
        }

        if (e.key !== 'ArrowUp' && e.key !== 'ArrowDown') return;
        const handle = e.target.closest ? e.target.closest('.zone-drag-handle') : null;
        if (!handle || saving) return;
        const item = handle.closest('.zone-object-edit');
        const zone = item ? item.parentNode : null;
        if (!zone || !zone.classList.contains('content-zone-edit') || !isPersistedZone(zone)) return;

        e.preventDefault();
        const index = indexAmongItems(zone, item, null);
        const target = e.key === 'ArrowUp' ? index - 1 : index + 1;
        if (target < 0 || target >= ownItems(zone, item).length + 1) return;

        try { sessionStorage.setItem(FOCUS_KEY, item.dataset.itemId); } catch (err) { /* focus restore is a convenience */ }
        moveItem(item, zoneIdOf(zone), target);
    });

    // After a keyboard move reloads the page, put focus back on the moved item's handle.
    function restoreFocus() {
        let itemId = null;
        try {
            itemId = sessionStorage.getItem(FOCUS_KEY);
            sessionStorage.removeItem(FOCUS_KEY);
        } catch (err) {
            return;
        }
        if (!itemId) return;
        const item = document.querySelector('.zone-object-edit[data-item-id="' + CSS.escape(itemId) + '"]');
        const handle = item ? item.querySelector(':scope > .zone-object-toolbar > .zone-drag-handle') : null;
        if (handle) {
            handle.focus();
            item.scrollIntoView({ block: 'nearest' });
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', restoreFocus);
    } else {
        restoreFocus();
    }
})();
