class ReportsManager {
    constructor() {
        this.init();
    }

    init() {
        this.setupEventListeners();
        this.loadReports();
    }

    setupEventListeners() {
        const refreshBtn = document.querySelector('#reports-view button[onclick*="loadReports"]');
        if (refreshBtn) {
            refreshBtn.addEventListener('click', () => this.loadReports());
        }
    }

    async loadReports() {
        try {
            const response = await fetch('/POS/GetReports', {
                method: 'GET',
                headers: { 'Accept': 'application/json', 'Cache-Control': 'no-cache' }
            });

            if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);

            const data = await response.json();
            this.renderReports(data);
        } catch (error) {
            console.error('Error loading reports:', error);
        }
    }

    renderReports(data) {
        document.querySelector('.reports-today-sales').textContent = `$${data.todaySales.toFixed(2)}`;
        document.querySelector('.reports-completed').textContent = data.completed;
        document.querySelector('.reports-pending').textContent = data.pending;
        document.querySelector('.reports-total').textContent = data.total;

        const tbody = document.querySelector('#reports-view table tbody');
        if (tbody) {
            tbody.innerHTML = data.recent.map(order => `
                <tr class="border-b hover:bg-gray-50">
                    <td class="py-3 px-4">${order.id}</td>
                    <td class="py-3 px-4">${order.customer}</td>
                    <td class="py-3 px-4">${order.type}</td>
                    <td class="py-3 px-4">$${order.total.toFixed(2)}</td>
                    <td class="py-3 px-4"><span class="px-2 py-1 bg-gray-200 rounded">${order.status}</span></td>
                    <td class="py-3 px-4">${order.time}</td>
                </tr>
            `).join('');
        }
    }
}

function loadReports() {
    if (window.reportsManager) {
        window.reportsManager.loadReports();
    }
}

document.addEventListener('DOMContentLoaded', () => {
    const reportsView = document.getElementById('reports-view');
    if (reportsView) {
        window.reportsManager = new ReportsManager();
    }
});