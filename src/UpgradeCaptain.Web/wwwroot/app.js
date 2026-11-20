// Upgrade Captain · cockpit · lo mínimo en JavaScript: atajos de teclado, autoscroll, foco y ventanas.
(() => {
    const isTyping = (el) => el && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.tagName === 'SELECT' || el.isContentEditable);

    // ← → cambian de paso · T arranca/pausa el reloj · L cambia de idioma · P abre el lanzador ⚡ (Esc lo cierra)
    // · Enter envía en el cuadro de mensaje
    document.addEventListener('keydown', (e) => {
        const target = e.target;
        if (target && target.id === 'composer' && e.key === 'Enter' && !e.shiftKey && !e.isComposing) {
            e.preventDefault();
            document.getElementById('composer-send')?.click();
            return;
        }
        if (e.key === 'Escape') {
            document.querySelector('.popover-backdrop')?.click();
            return;
        }
        if (isTyping(target) || e.metaKey || e.ctrlKey || e.altKey) {
            return;
        }
        switch (e.key) {
            case 'ArrowLeft': document.getElementById('nav-prev')?.click(); break;
            case 'ArrowRight': document.getElementById('nav-next')?.click(); break;
            case 't': case 'T': document.getElementById('clock-toggle')?.click(); break;
            case 'p': case 'P': document.getElementById('launcher-toggle')?.click(); break;
            case 'l': case 'L': {
                const off = document.querySelector('.lang-toggle button:not(.on)');
                off?.click();
                break;
            }
        }
    });

    // Autoscroll: los paneles con data-autoscroll se quedan abajo mientras llega texto,
    // salvo que la persona haya subido para leer.
    const stick = new WeakMap();
    document.addEventListener('scroll', (e) => {
        const el = e.target;
        if (el instanceof HTMLElement && el.hasAttribute('data-autoscroll')) {
            stick.set(el, el.scrollHeight - el.scrollTop - el.clientHeight < 90);
        }
    }, true);
    new MutationObserver(() => {
        document.querySelectorAll('[data-autoscroll]').forEach((el) => {
            if (stick.get(el) !== false) {
                el.scrollTop = el.scrollHeight;
            }
        });
    }).observe(document.body, { childList: true, subtree: true, characterData: true });

    window.uc = {
        focus: (id) => document.getElementById(id)?.focus(),
        open: (url, name) => window.open(url, name, name === 'maria' ? 'width=1000,height=1050' : undefined),
    };
})();
