// Admin chrome behaviours (vanilla, no dependencies).
//
// Navbar burger toggle for the admin navbar. Uses a delegated document-level
// listener so it keeps working across Blazor enhanced-navigation DOM swaps
// (the listener is attached once and never needs re-binding).
(function () {
  document.addEventListener('click', function (e) {
    var burger = e.target.closest && e.target.closest('.navbar-burger');
    if (!burger) {
      return;
    }
    var targetId = burger.dataset.target || burger.getAttribute('aria-controls');
    var menu = targetId ? document.getElementById(targetId) : null;
    burger.classList.toggle('is-active');
    if (menu) {
      menu.classList.toggle('is-active');
      burger.setAttribute('aria-expanded', menu.classList.contains('is-active') ? 'true' : 'false');
    }
  });
})();
