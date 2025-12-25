// wwwroot/js/orders.js
class OrderManager {
    constructor() {
        this.allOrders = [];
        this.filteredOrders = [];
        this.init();
    }

    init() {
        this.setupEventListeners();
        if (document.getElementById('orders-view') &&
            !document.getElementById('orders-view').classList.contains('hidden')) {
            this.loadOrders();
        }
    }

    setupEventListeners() {
        const refreshBtn = document.getElementById('refresh-orders-btn');
        if (refreshBtn) {
            refreshBtn.addEventListener('click', () => this.loadOrders());
        }

        const typeFilter = document.getElementById('orderTypeFilter');
        if (typeFilter) {
            typeFilter.addEventListener('change', () => this.applyFilters());
        }

        const statusFilter = document.getElementById('orderStatusFilter');
        if (statusFilter) {
            statusFilter.addEventListener('change', () => this.applyFilters());
        }

        const dateFilter = document.getElementById('orderDateFilter');
        if (dateFilter) {
            dateFilter.addEventListener('change', () => this.applyFilters());
        }

        const searchInput = document.getElementById('orderSearch');
        if (searchInput) {
            searchInput.addEventListener('input', () => this.applyFilters());
        }

        const observer = new MutationObserver(() => {
            const ordersView = document.getElementById('orders-view');
            if (ordersView && !ordersView.classList.contains('hidden')) {
                this.loadOrders();
            }
        });

        const ordersView = document.getElementById('orders-view');
        if (ordersView) {
            observer.observe(ordersView, { attributes: true });
        }
    }

    getAntiForgeryToken() {
        return document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    }

    async loadOrders() {
        try {
            console.log('Loading orders...');

            const response = await fetch('/POS/GetAllOrders', {
                method: 'GET',
                headers: {
                    'Accept': 'application/json',
                    'Cache-Control': 'no-cache'
                }
            });

            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status}`);
            }

            const result = await response.json();

            if (result.success && result.orders) {
                this.allOrders = result.orders;
                this.updateOrderStatistics();
                this.applyFilters();
                this.showNotification('Orders loaded successfully!', 'success');
            } else {
                throw new Error(result.message || 'Failed to load orders');
            }
        } catch (error) {
            console.error('Error loading orders:', error);
            this.showNotification(`Error loading orders: ${error.message}`, 'error');
            this.showNoOrdersMessage();
        }
    }

    updateOrderStatistics() {
        const allOrdersCount = this.allOrders.length;
        const dineInOrdersCount = this.allOrders.filter(o => o.orderType === 'dine-in').length;
        const deliveryOrdersCount = this.allOrders.filter(o => o.orderType === 'delivery').length;
        const takeawayOrdersCount = this.allOrders.filter(o => o.orderType === 'takeaway').length;

        this.updateElementText('totalOrdersCount', allOrdersCount);
        this.updateElementText('allOrdersCount', allOrdersCount);
        this.updateElementText('dineInOrdersCount', dineInOrdersCount);
        this.updateElementText('deliveryOrdersCount', deliveryOrdersCount);
        this.updateElementText('takeawayOrdersCount', takeawayOrdersCount);
    }

    updateElementText(elementId, text) {
        const element = document.getElementById(elementId);
        if (element) {
            element.textContent = text;
        }
    }

    applyFilters() {
        let filtered = [...this.allOrders];

        const typeFilter = document.getElementById('orderTypeFilter')?.value;
        if (typeFilter && typeFilter !== 'all') {
            filtered = filtered.filter(o => o.orderType === typeFilter);
        }

        const statusFilter = document.getElementById('orderStatusFilter')?.value;
        if (statusFilter && statusFilter !== 'all') {
            filtered = filtered.filter(o => o.status === statusFilter);
        }

        const dateFilter = document.getElementById('orderDateFilter')?.value;
        if (dateFilter) {
            const filterDate = new Date(dateFilter);
            filtered = filtered.filter(o => {
                const orderDate = new Date(o.createdAt);
                return orderDate.toDateString() === filterDate.toDateString();
            });
        }

        const searchTerm = document.getElementById('orderSearch')?.value.toLowerCase();
        if (searchTerm) {
            filtered = filtered.filter(o =>
                o.orderNumber.toLowerCase().includes(searchTerm) ||
                o.customerName.toLowerCase().includes(searchTerm) ||
                (o.customerPhone && o.customerPhone.toLowerCase().includes(searchTerm))
            );
        }

        this.filteredOrders = filtered;
        this.renderOrders();
    }

    renderOrders() {
        const container = document.getElementById('orders-container');
        const noOrdersMessage = document.getElementById('no-orders-message');

        if (!container) return;

        container.innerHTML = '';

        if (this.filteredOrders.length === 0) {
            if (noOrdersMessage) {
                noOrdersMessage.classList.remove('hidden');
            }
            return;
        }

        if (noOrdersMessage) {
            noOrdersMessage.classList.add('hidden');
        }

        this.filteredOrders.forEach(order => {
            const orderCard = this.createOrderCard(order);
            container.appendChild(orderCard);
        });
    }

    createOrderCard(order) {
        const card = document.createElement('div');
        card.className = 'bg-white rounded-lg shadow-lg p-4 border border-gray-200 hover:shadow-xl transition-all';

        const orderDate = new Date(order.createdAt);
        const timeString = orderDate.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
        const dateString = orderDate.toLocaleDateString();

        const statusConfig = this.getStatusConfig(order.status);
        const typeConfig = this.getTypeConfig(order.orderType);

        card.innerHTML = `
            <div class="flex justify-between items-start mb-3">
                <div>
                    <h3 class="text-lg font-bold text-gray-800">${order.orderNumber}</h3>
                    <div class="flex items-center space-x-2 mt-1">
                        <span class="text-sm ${statusConfig.color} px-2 py-1 rounded-full flex items-center">
                            ${statusConfig.icon} ${statusConfig.text}
                        </span>
                        <span class="text-sm bg-gray-100 px-2 py-1 rounded-full flex items-center">
                            ${typeConfig.icon} ${typeConfig.text}
                        </span>
                    </div>
                </div>
                <div class="text-right">
                    <div class="text-xl font-bold text-green-600">$${order.total.toFixed(2)}</div>
                    <div class="text-sm text-gray-500">${timeString}</div>
                    <div class="text-xs text-gray-400">${dateString}</div>
                </div>
            </div>
            
            <div class="mb-3">
                <div class="flex items-center mb-1">
                    <span class="text-gray-600 mr-2">👤</span>
                    <span class="font-medium">${order.customerName}</span>
                </div>
                ${order.customerPhone ? `
                    <div class="flex items-center mb-1">
                        <span class="text-gray-600 mr-2">📞</span>
                        <span class="text-sm">${order.customerPhone}</span>
                    </div>
                ` : ''}
                ${order.tableId ? `
                    <div class="flex items-center mb-1">
                        <span class="text-gray-600 mr-2">🪑</span>
                        <span class="text-sm">Table ${order.tableId}</span>
                    </div>
                ` : ''}
            </div>
            
            <div class="flex space-x-2">
                ${this.getActionButtons(order)}
            </div>
        `;

        setTimeout(() => {
            this.addCardEventListeners(card, order.id);
        }, 0);

        return card;
    }

    getStatusConfig(status) {
        const configs = {
            'pending': { icon: '⏳', text: 'Pending', color: 'bg-yellow-100 text-yellow-800' },
            'preparing': { icon: '👨‍🍳', text: 'Preparing', color: 'bg-blue-100 text-blue-800' },
            'ready': { icon: '✅', text: 'Ready', color: 'bg-green-100 text-green-800' },
            'completed': { icon: '📦', text: 'Completed', color: 'bg-gray-100 text-gray-800' },
            'cancelled': { icon: '❌', text: 'Cancelled', color: 'bg-red-100 text-red-800' }
        };

        return configs[status] || { icon: '❓', text: 'Unknown', color: 'bg-gray-100 text-gray-800' };
    }

    getTypeConfig(type) {
        const configs = {
            'dine-in': { icon: '🍽️', text: 'Dine In' },
            'delivery': { icon: '🚚', text: 'Delivery' },
            'takeaway': { icon: '🥡', text: 'Take Away' }
        };

        return configs[type] || { icon: '📋', text: 'Order' };
    }

    getActionButtons(order) {
        let buttons = '';

        switch (order.status) {
            case 'pending':
                buttons = `
                    <button data-action="prepare" data-order-id="${order.id}" 
                            class="flex-1 bg-blue-600 text-white py-2 rounded hover:bg-blue-700 text-sm font-medium">
                        👨‍🍳 Start Preparing
                    </button>
                `;
                break;

            case 'preparing':
                buttons = `
                    <button data-action="ready" data-order-id="${order.id}" 
                            class="flex-1 bg-green-600 text-white py-2 rounded hover:bg-green-700 text-sm font-medium">
                        ✅ Mark Ready
                    </button>
                `;
                break;

            case 'ready':
                buttons = `
                    <button data-action="pay" data-order-id="${order.id}" 
                            class="flex-1 bg-purple-600 text-white py-2 rounded hover:bg-purple-700 text-sm font-medium">
                        💳 Complete Payment
                    </button>
                `;
                break;
        }

        if (['pending', 'preparing'].includes(order.status)) {
            buttons += `
                <button data-action="cancel" data-order-id="${order.id}" 
                        class="px-3 bg-red-600 text-white py-2 rounded hover:bg-red-700 text-sm font-medium">
                    ❌
                </button>
            `;
        }

        return buttons || '<div class="text-sm text-gray-500 py-2">No actions available</div>';
    }

    addCardEventListeners(card, orderId) {
        const buttons = card.querySelectorAll('button[data-action]');
        buttons.forEach(button => {
            button.addEventListener('click', (e) => {
                const action = e.target.getAttribute('data-action');
                this.handleOrderAction(orderId, action);
            });
        });
    }

    async handleOrderAction(orderId, action) {
        let status = action;
        let message = '';

        switch (action) {
            case 'prepare':
                status = 'preparing';
                message = 'Are you sure you want to start preparing this order?';
                break;
            case 'ready':
                status = 'ready';
                message = 'Mark this order as ready for pickup/delivery?';
                break;
            case 'cancel':
                status = 'cancelled';
                message = 'Are you sure you want to cancel this order?';
                break;
            case 'pay':
                this.showPaymentModal(orderId);
                return;
        }

        if (!confirm(message)) return;

        await this.updateOrderStatus(orderId, status);
    }

    async updateOrderStatus(orderId, status) {
        try {
            const token = this.getAntiForgeryToken();

            const response = await fetch('/POS/UpdateOrderStatus', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': token
                },
                body: JSON.stringify({ orderId, status })
            });

            const result = await response.json();

            if (result.success) {
                this.showNotification(result.message, 'success');
                this.loadOrders();
            } else {
                this.showNotification(result.message, 'error');
            }
        } catch (error) {
            console.error('Error updating order status:', error);
            this.showNotification('Error updating order status: ' + error.message, 'error');
        }
    }

    showPaymentModal(orderId) {
        const modal = document.getElementById('paymentModal');
        if (modal) {
            document.getElementById('paymentOrderId').value = orderId;
            modal.classList.remove('hidden');
        }
    }

    showNotification(message, type) {
        const notification = document.createElement('div');
        notification.className = `notification ${type}`;
        notification.textContent = message;
        document.body.appendChild(notification);

        setTimeout(() => {
            notification.remove();
        }, 3000);
    }

    showNoOrdersMessage() {
        const container = document.getElementById('orders-container');
        const noOrdersMessage = document.getElementById('no-orders-message');

        if (container) container.innerHTML = '';
        if (noOrdersMessage) noOrdersMessage.classList.remove('hidden');
    }
}

document.addEventListener('DOMContentLoaded', function () {
    window.orderManager = new OrderManager();
});

function refreshOrders() {
    if (window.orderManager) {
        window.orderManager.loadOrders();
    }
}

function filterOrders(filterType) {
    if (window.orderManager) {
        document.getElementById('orderTypeFilter').value = filterType;
        window.orderManager.applyFilters();
    }
}

function filterOrdersByStatus() {
    if (window.orderManager) {
        window.orderManager.applyFilters();
    }
}

function filterOrdersByDate() {
    if (window.orderManager) {
        window.orderManager.applyFilters();
    }
}

function searchOrders() {
    if (window.orderManager) {
        window.orderManager.applyFilters();
    }
}