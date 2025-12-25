class KitchenManager {
    constructor() {
        this.orders = [];
        this.refreshInterval = null;
        this.init();
    }

    init() {
        this.setupEventListeners();
        this.loadOrders();
        this.startAutoRefresh();
    }

    setupEventListeners() {
        const refreshBtn = document.querySelector('#kitchen-view button[onclick*="loadKitchenOrders"]');
        if (refreshBtn) {
            refreshBtn.addEventListener('click', () => this.loadOrders());
        }
    }

    startAutoRefresh() {
        this.refreshInterval = setInterval(() => this.loadOrders(), 15000); // Refresh every 15s
    }

    stopAutoRefresh() {
        if (this.refreshInterval) {
            clearInterval(this.refreshInterval);
        }
    }

    async loadOrders() {
        try {
            const response = await fetch('/POS/GetKitchenOrders', {
                method: 'GET',
                headers: { 'Accept': 'application/json', 'Cache-Control': 'no-cache' }
            });

            if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);

            const orders = await response.json();
            this.orders = orders;
            this.renderOrders();
        } catch (error) {
            console.error('Error loading kitchen orders:', error);
        }
    }

    renderOrders() {
        const container = document.querySelector('#kitchen-view .grid.grid-cols-1');
        if (!container) return;

        container.innerHTML = '';

        if (this.orders.length === 0) {
            container.innerHTML = `
                <div class="col-span-full text-center py-12">
                    <div class="text-6xl mb-4">👨‍🍳</div>
                    <h3 class="text-2xl font-bold text-gray-600 mb-2">No Orders</h3>
                    <p class="text-gray-500">All orders have been completed!</p>
                </div>
            `;
            return;
        }

        this.orders.forEach(order => {
            container.appendChild(this.createOrderTicket(order));
        });
    }

    createOrderTicket(order) {
        const card = document.createElement('div');
        const isPending = order.status === 'pending';
        card.className = `${isPending ? 'bg-red-50 border-red-200' : 'bg-blue-50 border-blue-200'} border-2 rounded-lg p-6 shadow-lg`;

        card.innerHTML = `
            <div class="flex justify-between items-start mb-4">
                <div>
                    <h3 class="text-2xl font-bold text-gray-800">${order.ticket}</h3>
                    <p class="text-sm ${isPending ? 'text-red-600' : 'text-blue-600'} font-bold">
                        ${isPending ? '🚨 PENDING' : '⏳ PREPARING'}
                    </p>
                </div>
                <div class="text-right">
                    <p class="text-sm text-gray-600">🕐 ${order.time}</p>
                </div>
            </div>
            <div class="border-t pt-4">
                <button onclick="markOrderReady('${order.id}')" 
                        class="w-full bg-green-600 text-white py-2 rounded font-bold hover:bg-green-700 transition-all">
                    ✅ Mark Ready
                </button>
            </div>
        `;

        return card;
    }
}

function loadKitchenOrders() {
    if (window.kitchenManager) {
        window.kitchenManager.loadOrders();
    }
}

async function markOrderReady(orderId) {
    try {
        const response = await fetch('/POS/UpdateOrderStatus', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]')?.value || ''
            },
            body: JSON.stringify({ orderId: parseInt(orderId), status: 'ready' })
        });

        const result = await response.json();
        if (result.success) {
            if (window.kitchenManager) window.kitchenManager.loadOrders();
        }
    } catch (error) {
        console.error('Error:', error);
    }
}

document.addEventListener('DOMContentLoaded', () => {
    const kitchenView = document.getElementById('kitchen-view');
    if (kitchenView) {
        window.kitchenManager = new KitchenManager();
    }
});