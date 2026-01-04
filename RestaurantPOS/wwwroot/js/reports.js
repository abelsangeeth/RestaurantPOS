class ReportsManager {
    constructor() {
        this.isLoaded = false;
        this.init();
    }

    init() {
        this.setupEventListeners();
        // Listen for the custom event dispatched by showView
        document.addEventListener('viewchanged', (e) => {
            if (e.detail.viewName === 'reports' && !this.isLoaded) {
                this.loadReports();
            }
        });
    }

    setupEventListeners() {
        const refreshBtn = document.querySelector('#reports-view button[onclick*="loadReports"]');
        if (refreshBtn) {
            // Replace onclick with a proper event listener
            refreshBtn.removeAttribute('onclick');
            refreshBtn.addEventListener('click', () => this.loadReports());
        }
    }

    async loadReports() {
        console.log('ReportsManager is loading data...');
        try {
            const response = await fetch('/POS/GetReports', {
                method: 'GET',
                headers: { 'Accept': 'application/json', 'Cache-Control': 'no-cache' }
            });

            if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);

            const data = await response.json();
            this.renderReports(data);
            this.isLoaded = true; // Mark as loaded
        } catch (error) {
            console.error('Error loading reports:', error);
            const tbody = document.querySelector('#reports-view table tbody');
            if(tbody) tbody.innerHTML = '<tr><td colspan="6" class="text-center py-8 text-red-500">Failed to load reports.</td></tr>';
        }
    }

    renderReports(data) {
        // Use more specific selectors to avoid conflicts
        document.querySelector('#reports-view .reports-today-sales').textContent = `$${(data.todaySales || 0).toFixed(2)}`;
        document.querySelector('#reports-view .reports-completed').textContent = data.completed || 0;
        document.querySelector('#reports-view .reports-pending').textContent = data.pending || 0;
        document.querySelector('#reports-view .reports-total').textContent = data.total || 0;

        const tbody = document.querySelector('#reports-view table tbody');
        if (tbody) {
            if (data.recent && data.recent.length > 0) {
                tbody.innerHTML = data.recent.map(order => `
                    <tr class="border-b hover:bg-gray-50">
                        <td class="py-3 px-4">${order.id}</td>
                        <td class="py-3 px-4">${order.customer}</td>
                        <td class="py-3 px-4">${order.type}</td>
                        <td class="py-3 px-4">$${(order.total || 0).toFixed(2)}</td>
                        <td class="py-3 px-4"><span class="px-2 py-1 bg-gray-200 rounded">${order.status}</span></td>
                        <td class="py-3 px-4">${order.time}</td>
                    </tr>
                `).join('');
            } else {
                tbody.innerHTML = '<tr><td colspan="6" class="text-center py-8 text-gray-500">No recent orders to display.</td></tr>';
            }
        }
    }
}

// Initialize the manager once the DOM is loaded
document.addEventListener('DOMContentLoaded', () => {
    const reportsView = document.getElementById('reports-view');
    if (reportsView) {
        window.reportsManager = new ReportsManager();
    }
});