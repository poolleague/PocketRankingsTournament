// Keeps optional browser-only conveniences outside markup so the restrictive content-security policy remains intact.
document.querySelectorAll('[data-print-page]').forEach(button => button.addEventListener('click', () => window.print()));

// Keeps intentional venue-link copying user-initiated and avoids browser storage or third-party scripts.
document.querySelectorAll('[data-copy-target]').forEach(button => button.addEventListener('click', async () => {
    const input = document.getElementById(button.dataset.copyTarget);
    if (!input) return;
    try {
        await navigator.clipboard.writeText(input.value);
        button.textContent = 'Copied';
    } catch {
        input.select();
        document.execCommand('copy');
        button.textContent = 'Copied';
    }
}));
