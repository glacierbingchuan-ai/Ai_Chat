// Text Particle Animation - Happy New Year 2026 (Optimized)
(function() {
    'use strict';

    let canvas = null;
    let ctx = null;
    let animationId = null;
    let particles = [];
    let isRunning = false;
    let phase = 'form';
    let phaseStartTime = 0;

    const colors = [
        '#ff0000', '#ffd700', '#ff69b4', '#ff4500', '#ffff00',
        '#00ff00', '#00ffff', '#0080ff', '#8000ff', '#ff00ff',
        '#ff80ff', '#80ff00', '#00ff80', '#ff8000', '#8000ff',
        '#40c4ff', '#18ffff', '#64ffda', '#b2ff59', '#eeff41',
        '#ff4081', '#e040fb', '#7c4dff', '#536dfe', '#448aff'
    ];
    const TEXT = 'Happy New Year 2026';
    const FONT_SIZE = 100;

    function initCanvas() {
        if (!canvas) {
            canvas = document.createElement('canvas');
            canvas.style.cssText = 'position:fixed;top:0;left:0;width:100%;height:100%;pointer-events:none;z-index:9999;';
            document.body.appendChild(canvas);
        }
        canvas.width = window.innerWidth;
        canvas.height = window.innerHeight;
        ctx = canvas.getContext('2d');
    }

    function getTextPositions() {
        const tempCanvas = document.createElement('canvas');
        const tempCtx = tempCanvas.getContext('2d');
        tempCanvas.width = canvas.width;
        tempCanvas.height = canvas.height;

        tempCtx.font = `bold ${FONT_SIZE}px "Microsoft YaHei", sans-serif`;
        tempCtx.fillStyle = 'white';
        tempCtx.textAlign = 'center';
        tempCtx.textBaseline = 'middle';
        tempCtx.fillText(TEXT, canvas.width / 2, canvas.height / 2);

        const imageData = tempCtx.getImageData(0, 0, canvas.width, canvas.height);
        const data = imageData.data;
        const positions = [];
        const step = 6; // Larger step = fewer particles

        for (let y = 0; y < canvas.height; y += step) {
            for (let x = 0; x < canvas.width; x += step) {
                if (data[(y * canvas.width + x) * 4 + 3] > 128) {
                    positions.push({
                        x: x,
                        y: y,
                        color: colors[Math.floor(Math.random() * colors.length)]
                    });
                }
            }
        }
        return positions;
    }

    function createParticles() {
        const positions = getTextPositions();
        particles = [];
        
        // Limit to 600 particles max
        const maxParticles = 600;
        const ratio = Math.ceil(positions.length / maxParticles);
        
        for (let i = 0; i < positions.length; i += ratio) {
            const pos = positions[i];
            particles.push({
                tx: pos.x,
                ty: pos.y,
                x: Math.random() * canvas.width,
                y: Math.random() * canvas.height,
                vx: 0,
                vy: 0,
                color: pos.color,
                size: 3 + Math.random() * 2,
                alpha: 0,
                speed: 0.03 + Math.random() * 0.02
            });
        }
    }

    function animate() {
        if (!isRunning) return;

        ctx.clearRect(0, 0, canvas.width, canvas.height);
        
        const now = Date.now();
        const elapsed = now - phaseStartTime;
        let activeCount = 0;

        if (phase === 'form') {
            let allFormed = true;
            
            for (let i = 0; i < particles.length; i++) {
                const p = particles[i];
                
                // Move to target
                p.x += (p.tx - p.x) * p.speed;
                p.y += (p.ty - p.y) * p.speed;
                
                if (p.alpha < 1) p.alpha += 0.03;
                
                // Check if formed
                if (Math.abs(p.tx - p.x) > 2 || Math.abs(p.ty - p.y) > 2) {
                    allFormed = false;
                }
                
                // Draw
                ctx.globalAlpha = p.alpha;
                ctx.fillStyle = p.color;
                ctx.beginPath();
                ctx.arc(p.x, p.y, p.size, 0, Math.PI * 2);
                ctx.fill();
                activeCount++;
            }
            
            if (allFormed && elapsed > 2000) {
                phase = 'wait';
                phaseStartTime = now;
            }
        } else if (phase === 'wait') {
            for (let i = 0; i < particles.length; i++) {
                const p = particles[i];
                ctx.globalAlpha = p.alpha;
                ctx.fillStyle = p.color;
                ctx.beginPath();
                ctx.arc(p.tx, p.ty, p.size, 0, Math.PI * 2);
                ctx.fill();
            }
            
            if (elapsed > 2000) {
                phase = 'fall';
                phaseStartTime = now;
            }
        } else if (phase === 'fall') {
            for (let i = particles.length - 1; i >= 0; i--) {
                const p = particles[i];
                
                // Initialize velocity on first fall frame
                if (p.vx === 0 && p.vy === 0) {
                    const angle = Math.random() * Math.PI * 2;
                    const speed = 3 + Math.random() * 4;
                    p.vx = Math.cos(angle) * speed;
                    p.vy = Math.sin(angle) * speed;
                }
                
                p.x += p.vx;
                p.y += p.vy;
                p.alpha -= 0.015;
                p.size *= 0.98;
                
                if (p.alpha > 0 && p.size > 0.1) {
                    ctx.globalAlpha = p.alpha;
                    ctx.fillStyle = p.color;
                    ctx.beginPath();
                    ctx.arc(p.x, p.y, p.size, 0, Math.PI * 2);
                    ctx.fill();
                    activeCount++;
                } else {
                    particles.splice(i, 1);
                }
            }
            
            if (particles.length === 0) {
                stop();
                return;
            }
        }

        animationId = requestAnimationFrame(animate);
    }

    function start() {
        if (isRunning) return;
        isRunning = true;
        phase = 'form';
        phaseStartTime = Date.now();

        initCanvas();
        createParticles();
        animate();
    }

    function stop() {
        isRunning = false;
        if (animationId) {
            cancelAnimationFrame(animationId);
            animationId = null;
        }
        particles = [];
        
        if (canvas) {
            canvas.style.opacity = '0';
            setTimeout(() => {
                if (canvas && canvas.parentNode) {
                    canvas.parentNode.removeChild(canvas);
                }
                canvas = null;
                ctx = null;
            }, 300);
        }
    }

    window.addEventListener('resize', () => {
        if (canvas) {
            canvas.width = window.innerWidth;
            canvas.height = window.innerHeight;
        }
    });

    window.Fireworks = {
        start: start,
        stop: stop
    };
})();
