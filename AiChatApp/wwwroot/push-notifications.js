const PushNotifications = {
    vapidPublicKey: null,

    async init() {
        if (!('serviceWorker' in navigator) || !('PushManager' in window)) {
            console.warn('Push messaging is not supported');
            return;
        }

        try {
            const response = await fetch('/api/notifications/vapid-public-key');
            const data = await response.json();
            this.vapidPublicKey = data.publicKey;
            
            // Auto-subscribe if permission is already granted
            if (Notification.permission === 'granted') {
                this.subscribeUser();
            }
        } catch (err) {
            console.error('Failed to get VAPID public key', err);
        }
    },

    async requestPermission() {
        const permission = await Notification.requestPermission();
        if (permission === 'granted') {
            await this.subscribeUser();
        }
        return permission;
    },

    async subscribeUser() {
        try {
            const registration = await navigator.serviceWorker.ready;
            const subscription = await registration.pushManager.subscribe({
                userVisibleOnly: true,
                applicationServerKey: this.urlBase64ToUint8Array(this.vapidPublicKey)
            });

            const subJson = subscription.toJSON();
            await fetch('/api/notifications/subscribe', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    endpoint: subJson.endpoint,
                    p256dh: subJson.keys.p256dh,
                    auth: subJson.keys.auth
                })
            });

            console.log('User is subscribed to Web Push');
        } catch (err) {
            console.error('Failed to subscribe the user: ', err);
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
