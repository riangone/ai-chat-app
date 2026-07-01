const W = 800, H = 600;

class BootScene extends Phaser.Scene {
    constructor() { super('BootScene'); }

    preload() {
        const cx = W / 2, cy = H / 2;
        this.add.text(cx, cy - 40, 'Loading Vocab...', {
            fontSize: '28px', color: '#94a3b8', fontFamily: 'system-ui'
        }).setOrigin(0.5);

        const bar = this.add.graphics();
        this.load.on('progress', v => {
            bar.clear();
            bar.fillStyle(0x1e293b);
            bar.fillRoundedRect(cx - 150, cy + 20, 300, 16, 8);
            bar.fillStyle(0x6366f1);
            bar.fillRoundedRect(cx - 150, cy + 20, 300 * v, 16, 8);
        });
    }

    async create() {
        try {
            const res = await fetch('/api/vocab/review');
            const cards = await res.json();
            if (!cards.length) {
                this.scene.start('EmptyScene');
                return;
            }
            this.scene.start('GameScene', { cards });
        } catch {
            this.scene.start('EmptyScene');
        }
    }
}

class EmptyScene extends Phaser.Scene {
    constructor() { super('EmptyScene'); }

    create() {
        const cx = W / 2, cy = H / 2;
        this.add.text(cx, cy - 60, 'All caught up!', {
            fontSize: '36px', color: '#22c55e', fontFamily: 'system-ui'
        }).setOrigin(0.5);

        this.add.text(cx, cy, 'No words due for review right now.', {
            fontSize: '16px', color: '#64748b', fontFamily: 'system-ui'
        }).setOrigin(0.5);

        const btn = this.add.text(cx, cy + 80, '[  Retry  ]', {
            fontSize: '20px', color: '#6366f1', fontFamily: 'system-ui', backgroundColor: '#1e293b',
            padding: { x: 24, y: 12 }
        }).setOrigin(0.5).setInteractive({ useHandCursor: true });

        btn.on('pointerdown', () => this.scene.start('BootScene'));
        btn.on('pointerover', () => btn.setColor('#a5b4fc'));
        btn.on('pointerout', () => btn.setColor('#6366f1'));
    }
}

class SummaryScene extends Phaser.Scene {
    constructor() { super('SummaryScene'); }

    create(data) {
        const { correct, wrong, total, combo } = data;
        const cx = W / 2, cy = H / 2;
        const pct = total ? Math.round(correct / total * 100) : 0;

        this.add.text(cx, cy - 120, 'Session Complete', {
            fontSize: '34px', color: '#e2e8f0', fontFamily: 'system-ui'
        }).setOrigin(0.5);

        const stats = [
            { label: 'Cards', value: total, color: '#94a3b8' },
            { label: 'Correct', value: correct, color: '#22c55e' },
            { label: 'Wrong', value: wrong, color: '#ef4444' },
            { label: 'Best Combo', value: combo, color: '#f59e0b' },
            { label: 'Accuracy', value: pct + '%', color: pct >= 80 ? '#22c55e' : '#f59e0b' },
        ];

        stats.forEach((s, i) => {
            const y = cy - 50 + i * 40;
            this.add.text(cx - 100, y, s.label, {
                fontSize: '18px', color: '#64748b', fontFamily: 'system-ui'
            }).setOrigin(0, 0.5);
            this.add.text(cx + 100, y, String(s.value), {
                fontSize: '20px', color: s.color, fontFamily: 'system-ui', fontStyle: 'bold'
            }).setOrigin(1, 0.5);
        });

        const grade = pct >= 90 ? 'Excellent!' : pct >= 70 ? 'Good job!' : pct >= 50 ? 'Keep practicing!' : 'More review needed!';
        this.add.text(cx, cy + 130, grade, {
            fontSize: '22px', color: '#e2e8f0', fontFamily: 'system-ui'
        }).setOrigin(0.5);

        const btn = this.add.text(cx, cy + 180, '[  Play Again  ]', {
            fontSize: '20px', color: '#6366f1', fontFamily: 'system-ui', backgroundColor: '#1e293b',
            padding: { x: 24, y: 12 }
        }).setOrigin(0.5).setInteractive({ useHandCursor: true });

        btn.on('pointerdown', () => this.scene.start('BootScene'));
        btn.on('pointerover', () => btn.setColor('#a5b4fc'));
        btn.on('pointerout', () => btn.setColor('#6366f1'));

        for (let i = 0; i < 30; i++) {
            const x = Phaser.Math.Between(0, W);
            const y = Phaser.Math.Between(0, H);
            const r = Phaser.Math.Between(2, 6);
            const c = Phaser.Display.Color.GetColor(
                Phaser.Math.Between(100, 255), Phaser.Math.Between(100, 255), Phaser.Math.Between(100, 255)
            );
            const dot = this.add.circle(x, y, r, c, 0.3);
            this.tweens.add({
                targets: dot, alpha: 0, duration: Phaser.Math.Between(1000, 3000),
                ease: 'Sine.easeIn', repeat: -1, yoyo: true
            });
        }
    }
}

class GameScene extends Phaser.Scene {
    constructor() { super('GameScene'); }

    init(data) {
        this.cards = data.cards;
        this.index = 0;
        this.score = 0;
        this.correctCount = 0;
        this.wrongCount = 0;
        this.combo = 0;
        this.bestCombo = 0;
        this.answered = false;
    }

    create() {
        this.drawBackground();
        this.createUI();
        this.showCard();
    }

    drawBackground() {
        const bg = this.add.graphics();
        bg.fillGradientStyle(0x0f172a, 0x0f172a, 0x1e1b4b, 0x1e1b4b, 1);
        bg.fillRect(0, 0, W, H);

        for (let i = 0; i < 40; i++) {
            const x = Phaser.Math.Between(0, W);
            const y = Phaser.Math.Between(0, H);
            const r = Phaser.Math.FloatBetween(0.5, 2);
            const dot = this.add.circle(x, y, r, 0x6366f1, Phaser.Math.FloatBetween(0.1, 0.4));
            this.tweens.add({
                targets: dot, alpha: 0, y: y - Phaser.Math.Between(20, 60),
                duration: Phaser.Math.Between(2000, 5000), repeat: -1, delay: Phaser.Math.Between(0, 3000)
            });
        }
    }

    createUI() {
        this.scoreText = this.add.text(16, 16, 'Score: 0', {
            fontSize: '18px', color: '#e2e8f0', fontFamily: 'system-ui', fontStyle: 'bold'
        });

        this.comboText = this.add.text(16, 42, '', {
            fontSize: '14px', color: '#f59e0b', fontFamily: 'system-ui'
        });

        this.progressText = this.add.text(W - 16, 16, '', {
            fontSize: '14px', color: '#94a3b8', fontFamily: 'system-ui'
        }).setOrigin(1, 0);

        this.progressBar = this.add.graphics();
        this.cardBg = this.add.graphics();
    }

    showCard() {
        if (this.index >= this.cards.length) {
            this.time.delayedCall(600, () => {
                this.scene.start('SummaryScene', {
                    correct: this.correctCount, wrong: this.wrongCount,
                    total: this.cards.length, combo: this.bestCombo
                });
            });
            return;
        }

        this.answered = false;
        const card = this.cards[this.index];
        const cx = W / 2;

        this.progressText.setText(`${this.index + 1} / ${this.cards.length}`);

        this.progressBar.clear();
        this.progressBar.fillStyle(0x1e293b);
        this.progressBar.fillRoundedRect(16, 68, W - 32, 8, 4);
        this.progressBar.fillStyle(0x6366f1);
        this.progressBar.fillRoundedRect(16, 68, (W - 32) * (this.index / this.cards.length), 8, 4);

        this.cardBg.clear();
        this.cardBg.fillStyle(0x1e293b, 0.8);
        this.cardBg.fillRoundedRect(cx - 220, 100, 440, 200, 20);
        this.cardBg.lineStyle(2, 0x334155);
        this.cardBg.strokeRoundedRect(cx - 220, 100, 440, 200, 20);

        if (card.reading) {
            this.rebuildText('wordText', cx, 150, card.word, '42px', '#f1f5f9', 'bold');
            this.rebuildText('readingText', cx, 200, card.reading, '20px', '#64748b');
        } else {
            this.rebuildText('wordText', cx, 170, card.word, '42px', '#f1f5f9', 'bold');
            this.rebuildText('readingText');
        }

        if (card.example) {
            this.rebuildText('exampleText', cx, 245, `"${card.example}"`, '14px', '#475569', 'italic');
        } else {
            this.rebuildText('exampleText');
        }
        this.rebuildText('exampleTransText');

        const options = this.getOptions(card);
        this.createOptionButtons(options, card);

        this.tweens.add({
            targets: this.cardBg, alpha: { from: 0, to: 1 }, duration: 300, ease: 'Back.easeOut'
        });
    }

    rebuildText(key, x, y, content, size, color, style) {
        if (this[key]) this[key].destroy();
        if (!content) { this[key] = null; return; }
        const opts = { fontSize: size || '18px', color: color || '#e2e8f0', fontFamily: 'system-ui' };
        if (style === 'bold') opts.fontStyle = 'bold';
        if (style === 'italic') opts.fontStyle = 'italic';
        this[key] = this.add.text(x, y, content, opts).setOrigin(0.5);
        this[key].setAlpha(0);
        this.tweens.add({ targets: this[key], alpha: 1, duration: 300, ease: 'Back.easeOut' });
    }

    getOptions(card) {
        const correct = card.translation;
        const wrong = this.cards
            .filter(c => c.id !== card.id && c.translation !== correct)
            .map(c => c.translation)
            .filter((v, i, a) => a.indexOf(v) === i);

        Phaser.Utils.Array.Shuffle(wrong);
        const choices = [correct, ...wrong.slice(0, 3)];
        while (choices.length < 4) choices.push('---');
        Phaser.Utils.Array.Shuffle(choices);
        return { correct, choices };
    }

    createOptionButtons(options, card) {
        if (this.optionBtns) this.optionBtns.forEach(o => o.destroy());
        if (this.feedbackText) this.feedbackText.destroy();

        this.optionBtns = [];
        const cx = W / 2;
        const positions = [
            { x: cx - 160, y: 340 }, { x: cx + 160, y: 340 },
            { x: cx - 160, y: 420 }, { x: cx + 160, y: 420 },
        ];
        const btnW = 280, btnH = 56;

        options.choices.forEach((text, i) => {
            const pos = positions[i];
            const bg = this.add.graphics();
            this.drawBtn(bg, pos.x, pos.y, btnW, btnH, 0x1e293b, 0x334155);

            const label = this.add.text(pos.x, pos.y, text, {
                fontSize: '18px', color: '#e2e8f0', fontFamily: 'system-ui'
            }).setOrigin(0.5);

            const zone = this.add.zone(pos.x, pos.y, btnW, btnH).setInteractive({ useHandCursor: true });
            const obj = { bg, label, zone, text, btnW, btnH, pos };
            this.optionBtns.push(obj);

            zone.on('pointerover', () => {
                if (!this.answered) this.drawBtn(bg, pos.x, pos.y, btnW, btnH, 0x334155, 0x6366f1);
            });
            zone.on('pointerout', () => {
                if (!this.answered) this.drawBtn(bg, pos.x, pos.y, btnW, btnH, 0x1e293b, 0x334155);
            });
            zone.on('pointerdown', () => {
                if (this.answered) return;
                this.handleAnswer(text, options.correct, card, obj);
            });

            bg.setAlpha(0);
            label.setAlpha(0);
            this.tweens.add({
                targets: [bg, label], alpha: 1, y: pos.y,
                duration: 300, delay: i * 80, ease: 'Back.easeOut'
            });
        });
    }

    drawBtn(g, x, y, w, h, fill, stroke) {
        g.clear();
        g.fillStyle(fill, 0.9);
        g.fillRoundedRect(x - w / 2, y - h / 2, w, h, 12);
        g.lineStyle(2, stroke);
        g.strokeRoundedRect(x - w / 2, y - h / 2, w, h, 12);
    }

    async handleAnswer(selected, correct, card, btnObj) {
        this.answered = true;
        this.optionBtns.forEach(o => o.zone.disableInteractive());

        if (selected === correct) {
            this.correctCount++;
            this.combo++;
            this.bestCombo = Math.max(this.bestCombo, this.combo);
            const points = 10 + Math.min(this.combo * 2, 20);
            this.score += points;
            this.scoreText.setText(`Score: ${this.score}`);
            this.comboText.setText(this.combo > 1 ? `${this.combo}x Combo!` : '');
            this.fadeOptions(null, correct);
            this.showFeedback(this.combo >= 5 ? 'AMAZING!' : this.combo >= 3 ? 'Great!' : 'Correct!', '#22c55e');
            this.particleBurst(btnObj.pos.x, btnObj.pos.y, 0x22c55e);
        } else {
            this.wrongCount++;
            this.combo = 0;
            this.comboText.setText('');
            this.fadeOptions(btnObj, correct);
            this.showFeedback('Incorrect', '#ef4444');
            this.cameras.main.shake(200, 0.005);
        }

        await this.submitResult(card.id, selected === correct ? 'correct' : 'wrong');

        this.time.delayedCall(selected === correct ? 800 : 1500, () => {
            this.index++;
            this.showCard();
        });
    }

    fadeOptions(wrongBtn, correctText) {
        this.optionBtns.forEach(o => {
            if (o.text === correctText) {
                this.drawBtn(o.bg, o.pos.x, o.pos.y, o.btnW, o.btnH, 0x166534, 0x22c55e);
                o.label.setColor('#22c55e');
            } else if (wrongBtn && o === wrongBtn) {
                this.drawBtn(o.bg, o.pos.x, o.pos.y, o.btnW, o.btnH, 0x7f1d1d, 0xef4444);
                o.label.setColor('#ef4444');
            } else {
                o.bg.setAlpha(0.3);
                o.label.setAlpha(0.3);
            }
        });
    }

    showFeedback(text, color) {
        const cx = W / 2;
        if (this.feedbackText) this.feedbackText.destroy();
        this.feedbackText = this.add.text(cx, 500, text, {
            fontSize: '26px', color, fontFamily: 'system-ui', fontStyle: 'bold'
        }).setOrigin(0.5).setAlpha(0);

        this.tweens.add({ targets: this.feedbackText, alpha: 1, y: 480, duration: 400, ease: 'Back.easeOut' });
        this.tweens.add({ targets: this.feedbackText, alpha: 0, duration: 300, delay: 600, ease: 'Sine.easeIn' });
    }

    particleBurst(x, y, color) {
        for (let i = 0; i < 12; i++) {
            const angle = (i / 12) * Math.PI * 2;
            const dist = Phaser.Math.Between(40, 80);
            const p = this.add.circle(x, y, Phaser.Math.Between(3, 6), color, 1);
            this.tweens.add({
                targets: p, x: x + Math.cos(angle) * dist, y: y + Math.sin(angle) * dist,
                alpha: 0, duration: 500, ease: 'Sine.easeOut',
                onComplete: () => p.destroy()
            });
        }
    }

    async submitResult(cardId, result) {
        try {
            const fd = new URLSearchParams();
            fd.append('result', result);
            await fetch(`/api/vocab/${cardId}/result`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                body: fd
            });
        } catch { }
    }
}

const config = {
    type: Phaser.AUTO,
    width: W,
    height: H,
    parent: 'game-container',
    backgroundColor: '#0f172a',
    scene: [BootScene, GameScene, SummaryScene, EmptyScene],
    scale: { mode: Phaser.Scale.FIT, autoCenter: Phaser.Scale.CENTER_BOTH }
};

new Phaser.Game(config);
