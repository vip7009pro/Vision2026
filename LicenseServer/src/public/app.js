// Vision2026 Standalone License Server Dashboard Frontend

const API_BASE = '';
let authToken = localStorage.getItem('v26_license_token') || null;
let parsedOfflineReq = null;

// DOM Elements
const loginContainer = document.getElementById('login-container');
const dashboardContainer = document.getElementById('dashboard-container');
const loginForm = document.getElementById('login-form');
const loginError = document.getElementById('login-error');
const btnLogout = document.getElementById('btn-logout');
const currentUserLabel = document.getElementById('current-user');

// Tabs
const navTabs = document.querySelectorAll('.nav-tab');
const tabPanes = document.querySelectorAll('.tab-pane');

// Clients table
const clientsTableBody = document.getElementById('clients-table-body');
const btnRefreshClients = document.getElementById('btn-refresh-clients');

// Licenses table
const licensesTableBody = document.getElementById('licenses-table-body');
const btnOpenCreateLicense = document.getElementById('btn-open-create-license');
const createLicenseModal = document.getElementById('create-license-modal');
const btnCloseModal = document.getElementById('btn-close-modal');
const btnCancelModal = document.getElementById('btn-cancel-modal');
const createLicenseForm = document.getElementById('create-license-form');
const createTypeSelect = document.getElementById('create-type');
const expiryGroup = document.getElementById('expiry-group');

// Offline Signing
const dropZone = document.getElementById('drop-zone');
const fileReqInput = document.getElementById('file-req-input');
const parsedReqInfo = document.getElementById('parsed-req-info');
const reqMachineName = document.getElementById('req-machine-name');
const reqFingerprint = document.getElementById('req-fingerprint');
const reqOs = document.getElementById('req-os');
const reqAppVer = document.getElementById('req-app-ver');
const offlineSignForm = document.getElementById('offline-sign-form');
const offlineLicenseKey = document.getElementById('offline-license-key');
const offlineCustomerName = document.getElementById('offline-customer-name');
const btnSignOffline = document.getElementById('btn-sign-offline');

// --- AUTHENTICATION ---

function checkAuth() {
  if (authToken) {
    loginContainer.style.display = 'none';
    dashboardContainer.style.display = 'block';
    currentUserLabel.textContent = localStorage.getItem('v26_license_username') || 'admin';
    loadDashboardData();
  } else {
    loginContainer.style.display = 'flex';
    dashboardContainer.style.display = 'none';
  }
}

loginForm.addEventListener('submit', async (e) => {
  e.preventDefault();
  loginError.style.display = 'none';

  const username = document.getElementById('username').value.trim();
  const password = document.getElementById('password').value.trim();

  try {
    const res = await fetch(`${API_BASE}/api/v1/admin/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password })
    });

    const data = await res.json();
    if (data.success && data.token) {
      authToken = data.token;
      localStorage.setItem('v26_license_token', authToken);
      localStorage.setItem('v26_license_username', data.username || username);
      checkAuth();
    } else {
      loginError.textContent = data.message || 'Đăng nhập thất bại.';
      loginError.style.display = 'block';
    }
  } catch (err) {
    loginError.textContent = `Lỗi kết nối máy chủ: ${err.message}`;
    loginError.style.display = 'block';
  }
});

btnLogout.addEventListener('click', () => {
  authToken = null;
  localStorage.removeItem('v26_license_token');
  localStorage.removeItem('v26_license_username');
  checkAuth();
});

// Helper fetch with Auth
async function authFetch(url, options = {}) {
  options.headers = {
    ...options.headers,
    'Authorization': `Bearer ${authToken}`,
    'Content-Type': 'application/json'
  };

  const res = await fetch(url, options);
  if (res.status === 401) {
    btnLogout.click();
    throw new Error('Phiên đăng nhập đã hết hạn.');
  }
  return res;
}

// --- TABS NAVIGATION ---

navTabs.forEach(tab => {
  tab.addEventListener('click', () => {
    navTabs.forEach(t => t.classList.remove('active'));
    tabPanes.forEach(p => p.classList.remove('active'));

    tab.classList.add('active');
    const target = tab.dataset.tab;
    document.getElementById(`tab-${target}`).classList.add('active');

    if (target === 'clients') loadClients();
    if (target === 'licenses') loadLicenses();
  });
});

// --- LOAD DASHBOARD & KPI ---

async function loadDashboardData() {
  try {
    const res = await authFetch(`${API_BASE}/api/v1/admin/dashboard`);
    const data = await res.json();
    if (data.success && data.stats) {
      document.getElementById('kpi-total-licenses').textContent = data.stats.totalLicenses;
      document.getElementById('kpi-active-machines').textContent = data.stats.activeOnlineMachines;
      document.getElementById('kpi-revoked-machines').textContent = data.stats.revokedMachines;
      document.getElementById('kpi-total-machines').textContent = data.stats.totalMachines;
    }
  } catch (err) {
    console.error('Error loading KPI:', err);
  }

  loadClients();
}

// --- TAB 1: CLIENTS TABLE ---

async function loadClients() {
  clientsTableBody.innerHTML = '<tr><td colspan="8" class="text-center py-4">Đang tải dữ liệu máy trạm...</td></tr>';
  try {
    const res = await authFetch(`${API_BASE}/api/v1/admin/clients`);
    const data = await res.json();

    if (data.success && data.clients) {
      if (data.clients.length === 0) {
        clientsTableBody.innerHTML = '<tr><td colspan="8" class="text-center py-4 text-muted">Chưa có máy trạm nào kết nối bản quyền.</td></tr>';
        return;
      }

      const now = Date.now();
      clientsTableBody.innerHTML = data.clients.map(c => {
        const lastHb = new Date(c.last_heartbeat).getTime();
        const isOnline = (now - lastHb) < (12 * 3600 * 1000); // 12h

        let statusBadge = '<span class="badge badge-online">● Online</span>';
        if (c.is_revoked === 1) {
          statusBadge = '<span class="badge badge-revoked">⛔ Thu Hồi</span>';
        } else if (c.is_suspended === 1) {
          statusBadge = '<span class="badge badge-suspended">⏸ Tạm Khóa</span>';
        } else if (!isOnline) {
          statusBadge = '<span class="badge badge-offline">○ Offline</span>';
        }

        const editionBadge = `<span class="badge badge-${(c.edition || 'ent').toLowerCase().substring(0, 3)}">${c.edition || 'Enterprise'}</span>`;

        return `
          <tr>
            <td>${statusBadge}</td>
            <td>
              <strong>${escapeHtml(c.machine_name)}</strong><br/>
              <small class="text-muted">${escapeHtml(c.os_version || 'Windows')}</small>
            </td>
            <td>
              <div>${escapeHtml(c.customer_name || 'Khách Hàng')} ${editionBadge}</div>
              <code>${escapeHtml(c.license_key || '-')}</code>
            </td>
            <td>
              <code>${escapeHtml(c.machine_fingerprint.substring(0, 16))}...</code>
            </td>
            <td>
              <span>LAN: ${escapeHtml(c.local_ip || '-')}</span><br/>
              <small class="text-muted">Public: ${escapeHtml(c.public_ip || '-')}</small>
            </td>
            <td>v${escapeHtml(c.app_version || '2.1.0')}</td>
            <td>${formatDate(c.last_heartbeat)}</td>
            <td class="text-right">
              ${c.is_revoked === 1 ? `
                <button class="btn-secondary btn-sm" onclick="activateMachine('${c.machine_fingerprint}')">Khôi Phục</button>
              ` : `
                <button class="btn-outline-danger btn-sm" onclick="revokeMachine('${c.machine_fingerprint}', '${escapeHtml(c.machine_name)}')">Thu Hồi</button>
              `}
              <button class="btn-secondary btn-sm" onclick="transferMachine('${c.machine_fingerprint}', '${escapeHtml(c.machine_name)}')">Đổi Máy</button>
            </td>
          </tr>
        `;
      }).join('');
    }
  } catch (err) {
    clientsTableBody.innerHTML = `<tr><td colspan="8" class="text-center py-4 text-danger">Lỗi tải dữ liệu: ${err.message}</td></tr>`;
  }
}

btnRefreshClients.addEventListener('click', () => {
  loadDashboardData();
  loadClients();
});

// Client Actions
window.revokeMachine = async function(fingerprint, name) {
  const reason = prompt(`Xác nhận THU HỒI BẢN QUYỀN máy [${name}]?\nNhập lý do thu hồi:`, 'Hết hạn hợp đồng hoặc thanh lý máy');
  if (reason === null) return;

  try {
    const res = await authFetch(`${API_BASE}/api/v1/admin/machine/revoke`, {
      method: 'POST',
      body: JSON.stringify({ machineFingerprint: fingerprint, reason })
    });
    const data = await res.json();
    alert(data.message);
    loadDashboardData();
    loadClients();
  } catch (err) {
    alert(`Lỗi: ${err.message}`);
  }
};

window.activateMachine = async function(fingerprint) {
  if (!confirm('Khôi phục trạng thái hoạt động cho máy tính này?')) return;

  try {
    const res = await authFetch(`${API_BASE}/api/v1/admin/machine/activate`, {
      method: 'POST',
      body: JSON.stringify({ machineFingerprint: fingerprint })
    });
    const data = await res.json();
    alert(data.message);
    loadDashboardData();
    loadClients();
  } catch (err) {
    alert(`Lỗi: ${err.message}`);
  }
};

window.transferMachine = async function(fingerprint, name) {
  if (!confirm(`Hủy liên kết máy [${name}] để khách hàng có thể kích hoạt máy tính mới?\n(Hành động này sẽ giải phóng 1 slot bản quyền)`)) return;

  try {
    const res = await authFetch(`${API_BASE}/api/v1/admin/machine/transfer`, {
      method: 'POST',
      body: JSON.stringify({ machineFingerprint: fingerprint })
    });
    const data = await res.json();
    alert(data.message);
    loadDashboardData();
    loadClients();
  } catch (err) {
    alert(`Lỗi: ${err.message}`);
  }
};

// --- TAB 2: LICENSES TABLE & CREATION ---

async function loadLicenses() {
  licensesTableBody.innerHTML = '<tr><td colspan="8" class="text-center py-4">Đang tải danh sách license...</td></tr>';
  try {
    const res = await authFetch(`${API_BASE}/api/v1/admin/licenses`);
    const data = await res.json();

    if (data.success && data.licenses) {
      if (data.licenses.length === 0) {
        licensesTableBody.innerHTML = '<tr><td colspan="8" class="text-center py-4 text-muted">Chưa có license nào.</td></tr>';
        return;
      }

      licensesTableBody.innerHTML = data.licenses.map(l => {
        const editionBadge = `<span class="badge badge-${l.edition.toLowerCase().substring(0, 3)}">${l.edition}</span>`;
        return `
          <tr>
            <td><code><strong>${escapeHtml(l.license_key)}</strong></code></td>
            <td><strong>${escapeHtml(l.customer_name)}</strong></td>
            <td>${editionBadge}</td>
            <td>${escapeHtml(l.license_type)}</td>
            <td>${l.max_machines} máy</td>
            <td>${formatDate(l.issued_at)}</td>
            <td>${l.expires_at ? formatDate(l.expires_at) : '<span class="text-success">Vĩnh viễn</span>'}</td>
            <td><span class="badge badge-online">${escapeHtml(l.status)}</span></td>
          </tr>
        `;
      }).join('');
    }
  } catch (err) {
    licensesTableBody.innerHTML = `<tr><td colspan="8" class="text-center py-4 text-danger">Lỗi: ${err.message}</td></tr>`;
  }
}

// Modal Create License
btnOpenCreateLicense.addEventListener('click', () => {
  createLicenseModal.style.display = 'flex';
});

btnCloseModal.addEventListener('click', () => {
  createLicenseModal.style.display = 'none';
});

btnCancelModal.addEventListener('click', () => {
  createLicenseModal.style.display = 'none';
});

createTypeSelect.addEventListener('change', () => {
  expiryGroup.style.display = createTypeSelect.value === 'Perpetual' ? 'none' : 'block';
});

createLicenseForm.addEventListener('submit', async (e) => {
  e.preventDefault();

  const payload = {
    customerName: document.getElementById('create-customer').value.trim(),
    edition: document.getElementById('create-edition').value,
    maxMachines: document.getElementById('create-max-machines').value,
    licenseType: document.getElementById('create-type').value,
    expiresAt: document.getElementById('create-type').value !== 'Perpetual' ? document.getElementById('create-expiry').value : null,
    customKey: document.getElementById('create-custom-key').value.trim()
  };

  try {
    const res = await authFetch(`${API_BASE}/api/v1/admin/license/create`, {
      method: 'POST',
      body: JSON.stringify(payload)
    });

    const data = await res.json();
    if (data.success) {
      alert(`Tạo khóa thành công!\nLicense Key: ${data.license.license_key}`);
      createLicenseModal.style.display = 'none';
      createLicenseForm.reset();
      loadLicenses();
      loadDashboardData();
    } else {
      alert(`Lỗi: ${data.message}`);
    }
  } catch (err) {
    alert(`Lỗi: ${err.message}`);
  }
});

// --- TAB 3: OFFLINE SIGNING ---

dropZone.addEventListener('click', () => fileReqInput.click());

dropZone.addEventListener('dragover', (e) => {
  e.preventDefault();
  dropZone.classList.add('dragover');
});

dropZone.addEventListener('dragleave', () => dropZone.classList.remove('dragover'));

dropZone.addEventListener('drop', (e) => {
  e.preventDefault();
  dropZone.classList.remove('dragover');
  if (e.dataTransfer.files.length > 0) {
    handleReqFile(e.dataTransfer.files[0]);
  }
});

fileReqInput.addEventListener('change', () => {
  if (fileReqInput.files.length > 0) {
    handleReqFile(fileReqInput.files[0]);
  }
});

function handleReqFile(file) {
  const reader = new FileReader();
  reader.onload = (e) => {
    try {
      const content = e.target.result;
      let reqData;
      try {
        const decoded = atob(content);
        reqData = JSON.parse(decoded);
      } catch {
        reqData = JSON.parse(content);
      }

      parsedOfflineReq = {
        rawBase64: btoa(JSON.stringify(reqData)),
        data: reqData
      };

      reqMachineName.textContent = reqData.machineName || 'Industrial-PC';
      reqFingerprint.textContent = reqData.machineFingerprint || '-';
      reqOs.textContent = reqData.osVersion || 'Windows';
      reqAppVer.textContent = reqData.appVersion || '2.1.0';

      parsedReqInfo.style.display = 'block';
      btnSignOffline.disabled = false;
    } catch (err) {
      alert(`Tệp không hợp lệ! Vui lòng chọn đúng file .req do ứng dụng Vision xuất ra. (${err.message})`);
    }
  };
  reader.readAsText(file);
}

offlineSignForm.addEventListener('submit', async (e) => {
  e.preventDefault();
  if (!parsedOfflineReq) {
    alert('Vui lòng nạp file yêu cầu .req trước.');
    return;
  }

  const payload = {
    requestCode: parsedOfflineReq.rawBase64,
    licenseKey: offlineLicenseKey.value.trim(),
    customerName: offlineCustomerName.value.trim(),
    edition: document.getElementById('offline-edition').value,
    licenseType: document.getElementById('offline-type').value
  };

  try {
    const res = await fetch(`${API_BASE}/api/v1/license/offline-sign`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });

    const data = await res.json();
    if (data.success && data.licenseFileBase64) {
      // Tự động tải file .lic về máy tính
      const fileBlob = new Blob([atob(data.licenseFileBase64)], { type: 'application/octet-stream' });
      const downloadUrl = URL.createObjectURL(fileBlob);
      const a = document.createElement('a');
      a.href = downloadUrl;
      a.download = data.licenseFileName || 'LicenseFile.lic';
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(downloadUrl);

      alert(`✅ Ký số thành công! Đã tự động tải file: ${a.download}\nHãy sao chép file này vào USB và mang vào nạp cho máy trạm.`);
    } else {
      alert(`Lỗi ký số: ${data.message}`);
    }
  } catch (err) {
    alert(`Lỗi: ${err.message}`);
  }
});

// Utilities
function escapeHtml(str) {
  if (!str) return '';
  return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

function formatDate(isoStr) {
  if (!isoStr) return '-';
  try {
    const d = new Date(isoStr);
    return d.toLocaleString('vi-VN', {
      year: 'numeric', month: '2-digit', day: '2-digit',
      hour: '2-digit', minute: '2-digit'
    });
  } catch {
    return isoStr;
  }
}

// Initial Run
checkAuth();
