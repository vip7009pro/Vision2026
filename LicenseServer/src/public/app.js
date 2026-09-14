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
const offlineLicenseSelect = document.getElementById('offline-license-select');
const btnSignOffline = document.getElementById('btn-sign-offline');

// Pending Approvals
const pendingTableBody = document.getElementById('pending-table-body');
const btnRefreshPending = document.getElementById('btn-refresh-pending');
const pendingBadge = document.getElementById('pending-badge');
const kpiPending = document.getElementById('kpi-pending-registrations');
const modalApprove = document.getElementById('modal-approve');
const btnCloseApproveModal = document.getElementById('btn-close-approve-modal');
const btnCancelApprove = document.getElementById('btn-cancel-approve');
const approveForm = document.getElementById('approve-form');
const approveRegId = document.getElementById('approve-reg-id');
const approveMachineName = document.getElementById('approve-machine-name');
const approveFormattedCode = document.getElementById('approve-formatted-code');
const approveCustomerName = document.getElementById('approve-customer-name');
const approveEdition = document.getElementById('approve-edition');
const approveDuration = document.getElementById('approve-duration');

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
    if (target === 'pending') loadPendingRegistrations();
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
      if (kpiPending) {
        kpiPending.textContent = data.stats.pendingRegistrations || 0;
      }
      if (pendingBadge) {
        const count = data.stats.pendingRegistrations || 0;
        pendingBadge.textContent = count;
        pendingBadge.style.display = count > 0 ? 'inline-block' : 'none';
      }
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
              <button class="btn-primary btn-sm" onclick="openChangePlan('${c.machine_fingerprint}', '${escapeHtml(c.machine_name)}', '${escapeHtml(c.edition || 'Enterprise')}', '${escapeHtml(c.license_type || 'Perpetual')}')">✏️ Đổi Gói / Hạn</button>
              ${c.is_revoked === 1 ? `
                <button class="btn-secondary btn-sm" onclick="activateMachine('${c.machine_fingerprint}')">Khôi Phục</button>
              ` : `
                <button class="btn-outline-danger btn-sm" onclick="revokeMachine('${c.machine_fingerprint}', '${escapeHtml(c.machine_name)}')">Thu Hồi</button>
              `}
              <button class="btn-secondary btn-sm" onclick="transferMachine('${c.machine_fingerprint}', '${escapeHtml(c.machine_name)}')">Đổi Máy</button>
              <button class="btn-outline-danger btn-sm" onclick="deleteMachinePermanently('${c.machine_fingerprint}', '${escapeHtml(c.machine_name)}')">🗑️ Xóa</button>
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
  licensesTableBody.innerHTML = '<tr><td colspan="9" class="text-center py-4">Đang tải danh sách license...</td></tr>';
  try {
    const res = await authFetch(`${API_BASE}/api/v1/admin/licenses`);
    const data = await res.json();

    if (data.success && data.licenses) {
      if (data.licenses.length === 0) {
        licensesTableBody.innerHTML = '<tr><td colspan="9" class="text-center py-4 text-muted">Chưa có license nào.</td></tr>';
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
            <td class="text-right">
              <button class="btn-outline-danger btn-sm" onclick="deleteLicense('${l.id}', '${escapeHtml(l.license_key)}', '${escapeHtml(l.customer_name)}')">🗑️ Xóa</button>
            </td>
          </tr>
        `;
      }).join('');

      // Cập nhật dropdown chọn License trong tab Ký Offline
      if (offlineLicenseSelect) {
        const activeLics = data.licenses.filter(l => l.status === 'Active');
        offlineLicenseSelect.innerHTML = '<option value="CUSTOM">[Nhập License Key Mới Tùy Ý]</option>' + 
          activeLics.map(l => 
            `<option value="${l.id}" data-key="${escapeHtml(l.license_key)}" data-customer="${escapeHtml(l.customer_name)}" data-edition="${escapeHtml(l.edition)}" data-type="${escapeHtml(l.license_type)}">${escapeHtml(l.license_key)} - ${escapeHtml(l.customer_name)} (${l.edition})</option>`
          ).join('');

        if (activeLics.length > 0) {
          offlineLicenseSelect.value = activeLics[0].id;
          offlineLicenseKey.value = activeLics[0].license_key;
          offlineCustomerName.value = activeLics[0].customer_name;
          document.getElementById('offline-edition').value = activeLics[0].edition;
          document.getElementById('offline-type').value = activeLics[0].license_type;
        }
      }
    }
  } catch (err) {
    licensesTableBody.innerHTML = `<tr><td colspan="9" class="text-center py-4 text-danger">Lỗi: ${err.message}</td></tr>`;
  }
}

if (offlineLicenseSelect) {
  offlineLicenseSelect.addEventListener('change', () => {
    const opt = offlineLicenseSelect.selectedOptions[0];
    if (opt && opt.value !== 'CUSTOM') {
      offlineLicenseKey.value = opt.getAttribute('data-key') || '';
      offlineCustomerName.value = opt.getAttribute('data-customer') || '';
      const ed = opt.getAttribute('data-edition');
      if (ed) document.getElementById('offline-edition').value = ed;
      const tp = opt.getAttribute('data-type');
      if (tp) document.getElementById('offline-type').value = tp;
    }
  });
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
      const content = (e.target.result || '').trim();
      let reqData;
      try {
        const decoded = atob(content);
        reqData = JSON.parse(decoded);
      } catch {
        reqData = JSON.parse(content);
      }

      const machineFingerprint = (reqData.machineFingerprint || reqData.MachineFingerprint || reqData.fingerprint || reqData.Fingerprint || reqData.machine_fingerprint || '').toString().trim();
      const machineName = (reqData.machineName || reqData.MachineName || reqData.name || reqData.Name || reqData.machine_name || 'Industrial-PC').toString().trim();
      const osVersion = (reqData.osVersion || reqData.OsVersion || reqData.os_version || 'Windows').toString().trim();
      const appVersion = (reqData.appVersion || reqData.AppVersion || reqData.app_version || '2.1.0').toString().trim();

      parsedOfflineReq = {
        rawBase64: btoa(JSON.stringify({
          machineFingerprint,
          machineName,
          osVersion,
          appVersion,
          ...reqData
        })),
        data: {
          machineFingerprint,
          machineName,
          osVersion,
          appVersion
        }
      };

      reqMachineName.textContent = machineName;
      reqFingerprint.textContent = machineFingerprint || '-';
      reqOs.textContent = osVersion;
      reqAppVer.textContent = appVersion;

      parsedReqInfo.style.display = 'block';
      btnSignOffline.disabled = !machineFingerprint;
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

// --- TAB PENDING: PENDING REGISTRATIONS ---

async function loadPendingRegistrations() {
  if (!pendingTableBody) return;
  pendingTableBody.innerHTML = '<tr><td colspan="7" class="text-center py-4">Đang tải danh sách máy chờ duyệt...</td></tr>';

  try {
    const res = await authFetch(`${API_BASE}/api/v1/admin/pending-registrations`);
    const data = await res.json();

    if (!data.success || !data.registrations || data.registrations.length === 0) {
      pendingTableBody.innerHTML = '<tr><td colspan="7" class="text-center py-4 text-muted">🎉 Hiện không có máy trạm nào đang chờ duyệt.</td></tr>';
      if (pendingBadge) pendingBadge.style.display = 'none';
      if (kpiPending) kpiPending.textContent = '0';
      return;
    }

    if (pendingBadge) {
      pendingBadge.textContent = data.registrations.length;
      pendingBadge.style.display = 'inline-block';
    }
    if (kpiPending) {
      kpiPending.textContent = data.registrations.length;
    }

    pendingTableBody.innerHTML = data.registrations.map(r => {
      const formattedCode = r.formattedCode || (r.fingerprint ? r.fingerprint.substring(0, 16) : '-');
      const machineName = escapeHtml(r.machineName || 'Industrial-PC');
      const osVer = escapeHtml(`${r.osVersion || 'Windows'} / ${r.appVersion || '2.1.0'}`);
      const ip = escapeHtml(r.ipAddress || '127.0.0.1');
      const updated = formatDate(r.updatedAt);

      return `
        <tr>
          <td><strong>${machineName}</strong></td>
          <td><span class="mono-code">${formattedCode}</span></td>
          <td>${ip}</td>
          <td>${osVer}</td>
          <td>${updated}</td>
          <td><span class="badge badge-warning">⏳ Chờ duyệt</span></td>
          <td class="text-right">
            <button class="btn-sm btn-quick-approve" onclick="quickApprove('${r.id}', '${escapeHtml(r.machineName || '')}')" title="Kích hoạt ngay gói Enterprise Vĩnh Viễn">⚡ Duyệt Nhanh</button>
            <button class="btn-sm btn-custom-approve" onclick="openCustomApprove('${r.id}', '${escapeHtml(r.machineName || '')}', '${formattedCode}')" title="Tùy chỉnh thời hạn hoặc gói bản quyền">⚙️ Tùy Chỉnh</button>
            <button class="btn-sm btn-reject" onclick="rejectRegistration('${r.id}', '${escapeHtml(r.machineName || '')}')" title="Từ chối yêu cầu">✕</button>
          </td>
        </tr>
      `;
    }).join('');
  } catch (err) {
    pendingTableBody.innerHTML = `<tr><td colspan="7" class="text-center py-4 text-danger">Lỗi tải danh sách: ${err.message}</td></tr>`;
  }
}

if (btnRefreshPending) {
  btnRefreshPending.addEventListener('click', loadPendingRegistrations);
}

// Quick Approve: Enterprise + Perpetual (Ưu tiên gán vào License Key Active có sẵn)
async function quickApprove(id, machineName) {
  if (!confirm(`Xác nhận DUYỆT NHANH máy "${machineName || id}"?`)) {
    return;
  }

  try {
    let licenseId = undefined;
    try {
      const lRes = await authFetch(`${API_BASE}/api/v1/admin/licenses`);
      const lData = await lRes.json();
      if (lData.success && lData.licenses && lData.licenses.length > 0) {
        const activeLics = lData.licenses.filter(l => l.status === 'Active');
        if (activeLics.length > 0) {
          licenseId = activeLics[0].id;
        }
      }
    } catch (e) {}

    const res = await authFetch(`${API_BASE}/api/v1/admin/registration/approve`, {
      method: 'POST',
      body: JSON.stringify({
        registrationId: id,
        licenseId,
        customerName: machineName ? `Khách Hàng (${machineName})` : 'Khách Hàng Vision2026',
        edition: 'Enterprise',
        durationDays: 0
      })
    });

    const data = await res.json();
    if (data.success) {
      alert(`✅ ${data.message || `Đã phê duyệt và ký số bản quyền cho máy "${machineName}"!`}\nClient sẽ tự động kích hoạt bản quyền trong vài giây.`);
      loadPendingRegistrations();
      loadClients();
      loadDashboardData();
    } else {
      alert(`Lỗi phê duyệt: ${data.message}`);
    }
  } catch (err) {
    alert(`Lỗi: ${err.message}`);
  }
}

// Open Custom Approve Modal
async function openCustomApprove(id, machineName, formattedCode) {
  approveRegId.value = id;
  approveMachineName.value = machineName || 'Industrial-PC';
  approveFormattedCode.value = formattedCode || '-';
  approveCustomerName.value = machineName ? `Nhà Máy (${machineName})` : '';
  approveEdition.value = 'Enterprise';
  approveDuration.value = '0';

  // Load danh sách license keys có sẵn
  const licenseSelect = document.getElementById('approve-license-select');
  const licenseHint = document.getElementById('approve-license-hint');
  if (licenseSelect) {
    licenseSelect.innerHTML = '<option value="NEW">✨ Tự động tạo License Key mới theo thông tin dưới</option>';
    try {
      const res = await authFetch(`${API_BASE}/api/v1/admin/licenses`);
      const data = await res.json();
      if (data.success && data.licenses && data.licenses.length > 0) {
        data.licenses.filter(l => l.status === 'Active').forEach(l => {
          const opt = document.createElement('option');
          opt.value = l.id;
          opt.textContent = `🔑 ${l.license_key} — ${l.customer_name} (${l.edition}, tối đa ${l.max_machines} máy)`;
          opt.dataset.customer = l.customer_name;
          opt.dataset.edition = l.edition;
          opt.dataset.type = l.license_type;
          licenseSelect.appendChild(opt);
        });

        // Nếu chỉ có 1 key và là key người dùng vừa tạo, tự động chọn luôn key đó
        if (data.licenses.length === 1 && data.licenses[0].status === 'Active') {
          licenseSelect.value = data.licenses[0].id;
          approveCustomerName.value = data.licenses[0].customer_name;
          approveEdition.value = data.licenses[0].edition;
          if (licenseHint) licenseHint.textContent = `Đang chọn gán trực tiếp vào Khóa ${data.licenses[0].license_key}`;
        }
      }
    } catch (e) {
      console.warn('Could not load licenses list:', e);
    }

    licenseSelect.onchange = () => {
      const selectedOpt = licenseSelect.selectedOptions[0];
      if (licenseSelect.value === 'NEW') {
        approveCustomerName.readOnly = false;
        approveEdition.disabled = false;
        approveDuration.disabled = false;
        if (licenseHint) licenseHint.textContent = 'Hệ thống sẽ tự động tạo một License Key mới.';
      } else if (selectedOpt) {
        approveCustomerName.value = selectedOpt.dataset.customer || approveCustomerName.value;
        approveEdition.value = selectedOpt.dataset.edition || 'Enterprise';
        if (licenseHint) licenseHint.textContent = `Khóa [${selectedOpt.textContent}] sẽ được gán cho máy trạm này.`;
      }
    };
  }

  modalApprove.style.display = 'flex';
}

function closeApproveModal() {
  modalApprove.style.display = 'none';
}

if (btnCloseApproveModal) btnCloseApproveModal.addEventListener('click', closeApproveModal);
if (btnCancelApprove) btnCancelApprove.addEventListener('click', closeApproveModal);

// Submit Custom Approve Form
if (approveForm) {
  approveForm.addEventListener('submit', async (e) => {
    e.preventDefault();
    const id = approveRegId.value;
    const customerName = approveCustomerName.value.trim();
    const edition = approveEdition.value;
    const durationDays = parseInt(approveDuration.value, 10) || 0;
    const licenseSelect = document.getElementById('approve-license-select');
    const licenseId = licenseSelect && licenseSelect.value !== 'NEW' ? licenseSelect.value : undefined;

    try {
      const res = await authFetch(`${API_BASE}/api/v1/admin/registration/approve`, {
        method: 'POST',
        body: JSON.stringify({
          registrationId: id,
          licenseId,
          customerName,
          edition,
          durationDays
        })
      });

      const data = await res.json();
      if (data.success) {
        alert(`✅ ${data.message || 'Đã phê duyệt và ký số bản quyền thành công cho máy!'}\nClient sẽ tự động nhận bản quyền.`);
        closeApproveModal();
        loadPendingRegistrations();
        loadClients();
        loadDashboardData();
      } else {
        alert(`Lỗi: ${data.message}`);
      }
    } catch (err) {
      alert(`Lỗi kết nối: ${err.message}`);
    }
  });
}

// Reject Registration
async function rejectRegistration(id, machineName) {
  const reason = prompt(`Nhập lý do từ chối kích hoạt máy "${machineName || id}":`, 'Yêu cầu không được phê duyệt từ quản trị viên');
  if (reason === null) return;

  try {
    const res = await authFetch(`${API_BASE}/api/v1/admin/registration/reject`, {
      method: 'POST',
      body: JSON.stringify({
        registrationId: id,
        reason
      })
    });

    const data = await res.json();
    if (data.success) {
      alert(`Đã từ chối máy "${machineName}".`);
      loadPendingRegistrations();
      loadDashboardData();
    } else {
      alert(`Lỗi: ${data.message}`);
    }
  } catch (err) {
    alert(`Lỗi: ${err.message}`);
  }
}

// Expose functions globally for onclick in template strings
window.quickApprove = quickApprove;
window.openCustomApprove = openCustomApprove;
window.rejectRegistration = rejectRegistration;

// Delete Machine Permanently
window.deleteMachinePermanently = async function(fingerprint, name) {
  if (!confirm(`Xác nhận XÓA HOÀN TOÀN máy trạm [${name}] khỏi hệ thống?\n\nSau khi xóa, máy tính này có thể tự do đăng ký lại như một máy mới.`)) {
    return;
  }
  try {
    const res = await authFetch(`${API_BASE}/api/v1/admin/machine/delete`, {
      method: 'POST',
      body: JSON.stringify({ machineFingerprint: fingerprint })
    });
    const data = await res.json();
    if (data.success) {
      alert(data.message || 'Đã xóa hoàn toàn máy trạm khỏi hệ thống.');
      loadDashboardData();
      loadClients();
      loadPendingRegistrations();
    } else {
      alert(`Lỗi: ${data.message}`);
    }
  } catch (err) {
    alert(`Lỗi: ${err.message}`);
  }
};

// Delete License Function
window.deleteLicense = async function(id, key, customer) {
  if (!confirm(`CẢNH BÁO NGUY HIỂM:\nBạn có chắc chắn muốn XÓA HOÀN TOÀN License Key [${key}] của khách hàng [${customer}]?\n\nHành động này sẽ xóa license khỏi hệ thống và THU HỒI TẤT CẢ máy trạm đang sử dụng license này!`)) {
    return;
  }

  try {
    const res = await authFetch(`${API_BASE}/api/v1/admin/license/delete`, {
      method: 'POST',
      body: JSON.stringify({ licenseId: id })
    });
    const data = await res.json();
    if (data.success) {
      alert(`Đã xóa thành công License [${key}]!`);
      loadLicenses();
      loadClients();
      loadDashboardData();
    } else {
      alert(`Lỗi: ${data.message}`);
    }
  } catch (err) {
    alert(`Lỗi: ${err.message}`);
  }
};

// Change Machine Plan & Duration Functions
const modalChangePlan = document.getElementById('modal-change-plan');
const changePlanForm = document.getElementById('change-plan-form');
const btnCloseChangePlanModal = document.getElementById('btn-close-change-plan-modal');
const btnCancelChangePlan = document.getElementById('btn-cancel-change-plan');

window.openChangePlan = function(fingerprint, name, currentEdition, currentType) {
  document.getElementById('change-plan-fingerprint').value = fingerprint;
  document.getElementById('change-plan-machine-name').value = name;
  document.getElementById('change-plan-edition').value = currentEdition || 'Enterprise';

  const durationSelect = document.getElementById('change-plan-duration');
  if (currentType === 'Trial') {
    durationSelect.value = '30';
  } else if (currentType === 'Perpetual') {
    durationSelect.value = '0';
  } else {
    durationSelect.value = '365';
  }

  if (modalChangePlan) {
    modalChangePlan.style.display = 'flex';
  }
};

if (btnCloseChangePlanModal) {
  btnCloseChangePlanModal.addEventListener('click', () => {
    modalChangePlan.style.display = 'none';
  });
}

if (btnCancelChangePlan) {
  btnCancelChangePlan.addEventListener('click', () => {
    modalChangePlan.style.display = 'none';
  });
}

if (changePlanForm) {
  changePlanForm.addEventListener('submit', async (e) => {
    e.preventDefault();
    const fingerprint = document.getElementById('change-plan-fingerprint').value;
    const edition = document.getElementById('change-plan-edition').value;
    const durationDays = parseInt(document.getElementById('change-plan-duration').value, 10);

    let licenseType = 'Perpetual';
    if (durationDays === 7 || durationDays === 30) {
      licenseType = 'Trial';
    } else if (durationDays > 0) {
      licenseType = 'Subscription';
    }

    try {
      const res = await authFetch(`${API_BASE}/api/v1/admin/machine/change-plan`, {
        method: 'POST',
        body: JSON.stringify({
          machineFingerprint: fingerprint,
          edition,
          licenseType,
          durationDays
        })
      });

      const data = await res.json();
      if (data.success) {
        alert(`Cập nhật thành công!\nMáy trạm [${document.getElementById('change-plan-machine-name').value}] đã được chuyển sang gói ${edition} (${licenseType}).\nGói bản quyền mới đã được ký số RSA và sẽ tự động cập nhật xuống máy trạm.`);
        modalChangePlan.style.display = 'none';
        loadClients();
        loadDashboardData();
      } else {
        alert(`Lỗi: ${data.message}`);
      }
    } catch (err) {
      alert(`Lỗi kết nối: ${err.message}`);
    }
  });
}

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
