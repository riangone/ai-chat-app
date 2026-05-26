const PushNotifications = {
    vapidPublicKey: null,

    async init() {
        console.log('[Push] Initializing...');
        if (!('serviceWorker' in navigator) || !('PushManager' in window)) {
            console.warn('[Push] Push messaging is not supported');
            this.updateUI('Unsupported');
            return;
        }

        try {
            const response = await fetch('/api/notifications/vapid-public-key');
            if (!response.ok) throw new Error('Failed to fetch VAPID key');
            const data = await response.json();
            this.vapidPublicKey = data.publicKey;
            console.log('[Push] VAPID Key loaded');
            
            // Check current subscription status
            const registration = await navigator.serviceWorker.ready;
            const subscription = await registration.pushManager.getSubscription();
            
            if (subscription) {
                console.log('[Push] User is already subscribed');
                this.updateUI('Subscribed');
                // Sync with server just in case
                this.sendSubscriptionToServer(subscription);
            } else if (Notification.permission === 'granted') {
                console.log('[Push] Permission granted but no subscription found, subscribing...');
                this.subscribeUser();
            } else {
                this.updateUI(Notification.permission);
            }
        } catch (err) {
            console.error('[Push] Initialization failed:', err);
        }
    },

    async requestPermission() {
        console.log('[Push] Requesting permission...');
        const permission = await Notification.requestPermission();
        this.updateUI(permission);
        if (permission === 'granted') {
            await this.subscribeUser();
        }
        return permission;
    },

    async subscribeUser() {
        try {
            console.log('[Push] Subscribing user...');
            const registration = await navigator.serviceWorker.ready;
            const subscription = await registration.pushManager.subscribe({
                userVisibleOnly: true,
                applicationServerKey: this.urlBase64ToUint8Array(this.vapidPublicKey)
            });

            await this.sendSubscriptionToServer(subscription);
            console.log('[Push] User is subscribed to Web Push');
            this.updateUI('Subscribed');
        } catch (err) {
            console.error('[Push] Failed to subscribe the user: ', err);
            this.updateUI('Failed');
        }
    },

    async sendSubscriptionToServer(subscription) {
        const subJson = subscription.toJSON();
        const response = await fetch('/api/notifications/subscribe', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                endpoint: subJson.endpoint,
                p256dh: subJson.keys.p256dh,
                auth: subJson.keys.auth
            })
        });
        if (!response.ok) throw new Error('Failed to send subscription to server');
    },

    updateUI(status) {
        console.log('[Push] Status update:', status);
        const btn = document.getElementById('push-toggle-btn');
        if (btn) {
            if (status === 'Subscribed' || status === 'granted') {
                btn.innerText = 'Enabled';
                btn.classList.remove('btn-primary');
                btn.classList.add('btn-success');
                btn.disabled = true;
            } else if (status === 'denied') {
                btn.innerText = 'Blocked';
                btn.classList.add('btn-error');
                btn.disabled = true;
            } else if (status === 'Unsupported') {
                btn.innerText = 'Unsupported';
                btn.classList.add('btn-ghost');
                btn.disabled = true;
            } else if (status === 'Failed') {
                btn.innerText = 'Retry';
            }
        }
    },

    async unsubscribeUser() {
        try {
            const registration = await navigator.serviceWorker.ready;
            const subscription = await registration.pushManager.getSubscription();
            if (subscription) {
                const endpoint = subscription.endpoint;
                await subscription.unsubscribe();
                await fetch('/api/notifications/unsubscribe', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(endpoint)
                });
                console.log('User is unsubscribed');
            }
        } catch (err) {
            console.error('Error unsubscribing', err);
        }
    },

    urlBase64ToUint8Array(base64String) {
        const padding = '='.repeat((4 - base64String.length % 4) % 4);
        const base64 = (base64String + padding)
            .replace(/\-/g, '+')
            .replace(/_/g, '/');

        const rawData = window.atob(base64);
        const outputArray = new Uint8Array(rawData.length);

        for (let i = 0; i < rawData.length; ++i) {
            outputArray[i] = rawData.charCodeAt(i);
        }
        return outputArray;
    }
};

window.PushNotifications = PushNotifications;
