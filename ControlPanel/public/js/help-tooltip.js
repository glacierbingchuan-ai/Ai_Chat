let activeTooltip = null;

function initHelpTooltips() {
    document.querySelectorAll('.help-tooltip').forEach(tooltip => {
        const helpIcon = tooltip.querySelector('.help-icon');
        const tooltipText = tooltip.querySelector('.tooltip-text');
        
        if (helpIcon && tooltipText) {
            tooltipText.style.display = 'none';
            
            const text = tooltipText.textContent.trim();
            const hasBottomClass = tooltip.classList.contains('bottom');
            
            helpIcon.addEventListener('mouseenter', function(e) {
                showTooltip(e, text, hasBottomClass);
            });
            
            helpIcon.addEventListener('mouseleave', function() {
                hideTooltip();
            });
        }
    });
}

function showTooltip(event, text, isBottom = false) {
    hideTooltip();
    
    const tooltip = document.createElement('div');
    tooltip.className = 'fixed-tooltip';
    tooltip.textContent = text;
    
    document.body.appendChild(tooltip);
    
    const iconRect = event.target.getBoundingClientRect();
    const tooltipRect = tooltip.getBoundingClientRect();
    
    let left = iconRect.left + iconRect.width / 2 - tooltipRect.width / 2;
    let top;
    
    if (isBottom) {
        top = iconRect.bottom + 10;
    } else {
        top = iconRect.top - tooltipRect.height - 10;
    }
    
    if (left < 10) {
        left = 10;
    }
    
    if (left + tooltipRect.width > window.innerWidth - 10) {
        left = window.innerWidth - tooltipRect.width - 10;
    }
    
    if (top < 10) {
        top = iconRect.bottom + 10;
    }
    
    if (top + tooltipRect.height > window.innerHeight - 10) {
        top = iconRect.top - tooltipRect.height - 10;
    }
    
    tooltip.style.left = left + 'px';
    tooltip.style.top = top + 'px';
    tooltip.style.visibility = 'visible';
    tooltip.style.opacity = '1';
    
    activeTooltip = tooltip;
}

function hideTooltip() {
    if (activeTooltip) {
        activeTooltip.remove();
        activeTooltip = null;
    }
}

document.addEventListener('DOMContentLoaded', function() {
    const style = document.createElement('style');
    style.textContent = `
        .fixed-tooltip {
            position: fixed;
            z-index: 99999;
            width: 250px;
            background-color: var(--text-color, #e2e8f0);
            color: var(--background-color, #0a0a0a);
            text-align: center;
            border-radius: 8px;
            padding: 10px 15px;
            font-size: 14px;
            line-height: 1.4;
            box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);
            pointer-events: none;
            visibility: hidden;
            opacity: 0;
            transition: opacity 0.3s ease, visibility 0.3s ease;
        }
    `;
    document.head.appendChild(style);
    
    initHelpTooltips();
});

window.initHelpTooltips = initHelpTooltips;
