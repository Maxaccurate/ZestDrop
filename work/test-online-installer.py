"""Test bootstrapper download failures/child exit codes, then the real GitHub download.

Usage: python work/test-online-installer.py ONLINE.exe PAYLOAD.json ISCC.exe OUTPUT_DIR
Refuses to replace an existing EXE installation or Start menu shortcut folder.
"""
from pathlib import Path
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
import shutil
import subprocess
import sys
import threading
import time
import winreg as reg

online, manifest_path, compiler, output = [Path(p).resolve() for p in sys.argv[1:5]]
repo = Path(__file__).resolve().parent.parent
if not output.is_relative_to(repo / 'outputs') or output.exists():
    raise RuntimeError('Use a fresh test folder below the repository outputs folder.')
uninstall_key = r'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8A1799B5-2AA0-45C6-837D-2D87268F1874}_is1'
menu = Path(os.environ['APPDATA']) / 'Microsoft/Windows/Start Menu/Programs/ZestDrop'
def read_value(key, name=''):
    try:
        with reg.OpenKey(reg.HKEY_CURRENT_USER, key, 0, reg.KEY_READ | reg.KEY_WOW64_64KEY) as handle:
            return reg.QueryValueEx(handle, name)
    except FileNotFoundError:
        return None
if read_value(uninstall_key, 'DisplayName') is not None or menu.exists():
    raise RuntimeError('An EXE installation or shortcut folder already exists; test will not overwrite it.')
output.mkdir(parents=True)
manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
version = manifest['version']
checks = []
target = output / 'ZestDrop online 中文 test'
integration_keys = [(r'Software\Microsoft\Windows\CurrentVersion\Run', 'ZestDrop'),
    (r'Software\Classes\*\shell\ZestDrop\command', ''),
    (r'Software\Classes\Directory\shell\ZestDrop\command', '')]
original_integrations = [read_value(*key) for key in integration_keys]
settings = Path(os.environ['LOCALAPPDATA']) / 'ZestDrop/settings.json'
def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest() if path.exists() else None
settings_before = sha(settings)
def check(name, passed):
    checks.append({'name': name, 'passed': bool(passed)})
    print(('PASS ' if passed else 'FAIL ') + name, flush=True)
    if not passed:
        raise AssertionError(name)
def compile_script(script, directory, extra):
    directory.mkdir()
    subprocess.run([str(compiler), '/Q', '/DAppVersion=' + version, '/DOutputPath=' + str(directory),
        '/DAppIcon=' + str(repo / 'work/ZestDrop/assets/zestdrop.ico'), *extra, str(script)], check=True, timeout=60)
    files = list(directory.glob('*.exe'))
    assert len(files) == 1
    return files[0]
def run(exe, log, folder, tasks=''):
    return subprocess.run([str(exe), '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART',
        '/NOCLOSEAPPLICATIONS', '/NORESTARTAPPLICATIONS', '/LANG=chinesesimplified',
        '/DIR=' + str(folder), '/TASKS=' + tasks, '/LOG=' + str(log)], timeout=600)

fixture = output / 'child.iss'
marker = output / 'child-arguments.txt'
fixture.write_text('''[Setup]
AppName=ZestDrop Bootstrap Test Child
AppVersion=1.1.0
CreateAppDir=no
Uninstallable=no
CreateUninstallRegKey=no
PrivilegesRequired=lowest
DisableWelcomePage=yes
DisableReadyPage=yes
DisableFinishedPage=yes
OutputDir={#OutputPath}
OutputBaseFilename=child
[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\\ChineseSimplified.isl"
[Tasks]
Name: "desktopicon"; Description: "Test task"
[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var I: Integer; Args: String;
begin
  if CurStep <> ssInstall then exit;
  Args := '';
  for I := 1 to ParamCount do Args := Args + ParamStr(I) + #13#10;
  SaveStringToFile('{#Marker}', Args, False);
end;
function GetCustomSetupExitCode: Integer;
begin Result := {#ExitCode}; end;
''', encoding='utf-8')
good = compile_script(fixture, output / 'good-child', ['/DMarker=' + str(marker), '/DExitCode=0']).read_bytes()
failed = compile_script(fixture, output / 'failed-child', ['/DMarker=' + str(marker), '/DExitCode=9']).read_bytes()
class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass
    def do_GET(self):
        data = {'/good.exe': good, '/bad.exe': good + b'corrupt-download', '/failed.exe': failed}.get(self.path)
        if data is None:
            self.send_error(404)
            return
        self.send_response(200)
        self.send_header('Content-Length', str(len(data)))
        self.send_header('Content-Type', 'application/octet-stream')
        self.end_headers()
        self.wfile.write(data)
server = ThreadingHTTPServer(('127.0.0.1', 0), Handler)
threading.Thread(target=server.serve_forever, daemon=True).start()
try:
    for name, route, required_hash, expected_exit in [
        ('valid', 'good', hashlib.sha256(good).hexdigest(), 0),
        ('bad-hash', 'bad', hashlib.sha256(good).hexdigest(), None),
        ('http-error', 'missing', hashlib.sha256(good).hexdigest(), None),
        ('child-error', 'failed', hashlib.sha256(failed).hexdigest(), 9),
    ]:
        marker.unlink(missing_ok=True)
        exe = compile_script(repo / 'packaging/ZestDrop-online.iss', output / name,
            ['/DInstallerURL=http://127.0.0.1:' + str(server.server_port) + '/' + route + '.exe',
             '/DInstallerSHA256=' + required_hash])
        result = run(exe, output / (name + '.log'), output / 'fixture destination 中文', 'desktopicon')
        if expected_exit is None:
            check('Rejects ' + name + ' before executing the downloaded file', result.returncode != 0 and not marker.exists())
        else:
            check('Propagates child exit code ' + str(expected_exit), result.returncode == expected_exit and marker.exists())
            if expected_exit == 0:
                args = marker.read_text(encoding='utf-8-sig')
                check('Forwards Unicode destination, language, silent mode and task choice',
                    '/DIR=' + str(output / 'fixture destination 中文') in args and
                    '/LANG=chinesesimplified' in args and '/VERYSILENT' in args and '/TASKS=desktopicon' in args)
                check('Keeps outer log parameters out of the child command line', str(output / 'valid.log') not in args)
    check('Bootstrap fixtures create no installed-app entry or Start menu folder',
        read_value(uninstall_key, 'DisplayName') is None and not menu.exists())
finally:
    server.shutdown()
    server.server_close()

try:
    real_log = output / 'real-download.log'
    result = run(online, real_log, target)
    check('Real GitHub download hands off to a successful per-user installation', result.returncode == 0)
    log = real_log.read_text(encoding='utf-8-sig')
    check('Downloaded offline installer passes the embedded integrity check', 'Verified installer download completed.' in log)
    check('Installs into the forwarded Unicode path', (target / 'ZestDrop.exe').exists())
    for item in manifest['files']:
        if sha(target / item['path']) != item['sha256']:
            raise AssertionError('Installed payload differs: ' + item['path'])
    check('All application files match the validated offline installer', True)
    check('Only the real application is registered in Installed apps',
        read_value(uninstall_key, 'DisplayVersion')[0] == version and
        read_value(r'Software\Microsoft\Windows\CurrentVersion\Uninstall\ZestDropOnlineSetup_is1', 'DisplayName') is None)
    fixtures = output / 'real-fixtures'
    fixtures.mkdir()
    image = fixtures / 'image.png'
    shutil.copy2(repo / 'work/full-fixtures/image.png', image)
    request, response = fixtures / 'request.json', fixtures / 'result.json'
    before = sha(image)
    request.write_text(json.dumps({'Paths': [str(image)], 'Action': 'convert:jpg'}), encoding='utf-8')
    subprocess.run([str(target / 'ZestDrop.exe'), '--worker', str(request), str(response)], check=True, timeout=90)
    value = json.loads(response.read_text(encoding='utf-8-sig'))['Files'][0]
    check('Online-installed application converts an image and preserves the original',
        not value.get('Error') and Path(value['Output']).is_file() and sha(image) == before)
finally:
    if (target / 'unins000.exe').exists():
        cleanup = subprocess.run([str(target / 'unins000.exe'), '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART'], timeout=120)
        check('Online-installed application uninstalls successfully', cleanup.returncode == 0)
        # The uninstall launcher can return before its temporary helper has
        # finished deleting the launcher itself and the last empty directories.
        for _ in range(100):
            if not target.exists() and not menu.exists() and read_value(uninstall_key, 'DisplayName') is None:
                break
            time.sleep(.1)
    check('Online test leaves no application files or installed-app entry', not target.exists() and
        not menu.exists() and read_value(uninstall_key, 'DisplayName') is None)
    check('Preserves existing settings, startup and Explorer registrations', sha(settings) == settings_before and
        [read_value(*key) for key in integration_keys] == original_integrations)
    report = {'passed': sum(item['passed'] for item in checks), 'failed': sum(not item['passed'] for item in checks),
        'payload_files': len(manifest['files']), 'checks': checks}
    (output / 'checks.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report, indent=2), flush=True)
