// Keeps optional browser-only conveniences outside markup so the restrictive content-security policy remains intact.
document.querySelectorAll('[data-print-page]').forEach(button => button.addEventListener('click', () => window.print()));
