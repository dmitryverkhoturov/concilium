// Concilium desktop: runs the local server inside Electron and shows it in a real window.
const { app, BrowserWindow, shell } = require('electron');
const path = require('node:path');
const fs = require('node:fs');
const { pathToFileURL } = require('node:url');

if (!app.requestSingleInstanceLock()) app.quit();

const PORT = 4757; // not the dev server's 4747, so both can run side by side

// Portable: data sits next to the program (the portable .exe's folder, or the install folder).
// A macOS .app bundle is read-only and gets moved around, so there it goes to the user's app-data folder.
function dataDir() {
  if (process.env.CONCILIUM_DATA) return process.env.CONCILIUM_DATA;
  if (!app.isPackaged) return path.join(app.getPath('appData'), 'Concilium');
  if (process.platform === 'darwin') return app.getPath('userData');
  const dir = process.env.PORTABLE_EXECUTABLE_DIR || path.dirname(process.execPath);
  return path.join(dir, 'Concilium data');
}

async function up() {
  try { return (await fetch(`http://localhost:${PORT}/api/people`)).ok; } catch { return false; }
}

let win;
app.on('second-instance', () => { if (win) { if (win.isMinimized()) win.restore(); win.focus(); } });

app.whenReady().then(async () => {
  process.env.PORT = String(PORT);
  process.env.CONCILIUM_DATA = dataDir();
  process.env.CONCILIUM_TEMPLATE = app.isPackaged
    ? path.join(process.resourcesPath, 'concilium-markdown')
    : path.join(__dirname, '..', 'concilium-markdown');
  fs.mkdirSync(process.env.CONCILIUM_DATA, { recursive: true });
  await import(pathToFileURL(path.join(__dirname, 'server.mjs')).href);
  for (let i = 0; i < 200 && !(await up()); i++) await new Promise((r) => setTimeout(r, 100));

  win = new BrowserWindow({
    width: 1400, height: 900, minWidth: 900, minHeight: 600,
    title: 'Concilium', icon: path.join(__dirname, 'icon.ico'),
    autoHideMenuBar: true, backgroundColor: '#c8d9d2',
  });
  // Links (sources, sign-in page) open in the normal browser, not inside the app.
  win.webContents.setWindowOpenHandler(({ url }) => { shell.openExternal(url); return { action: 'deny' }; });
  win.webContents.on('will-navigate', (e, url) => {
    if (!url.startsWith(`http://localhost:${PORT}`)) { e.preventDefault(); shell.openExternal(url); }
  });
  win.loadURL(`http://localhost:${PORT}`);
});

app.on('window-all-closed', () => app.quit());
