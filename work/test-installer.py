"""Exercise installation, repair and uninstall in an isolated per-user test folder.

Temporarily tests ZestDrop's registry integration; restores the exact previous
entries in finally. Refuses to run if a regular EXE installation already exists.
Usage: python work/test-installer.py SETUP.exe installer-payload.json OUTPUT_DIR
"""
from pathlib import Path
import hashlib
import json
import os
import shutil
import subprocess
import sys
import time
import uuid
import winreg as reg

installer, manifest_path, output = map(lambda x: Path(x).resolve(), sys.argv[1:4])
repo = Path(__file__).resolve().parent.parent
if not output.is_relative_to(repo / 'outputs') or output.exists():
    raise RuntimeError('Use a fresh test directory below this repository outputs folder.')
output.mkdir(parents=True)
target = output / 'ZestDrop 中文 test'
manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
uid = uuid.uuid4().hex[:12]
# The wizard hides its group page, so Inno deliberately uses the default group
# even if /GROUP is passed. Refuse to touch any existing shortcut folder.
group = 'ZestDrop'
start_menu = Path(os.environ['APPDATA']) / 'Microsoft/Windows/Start Menu/Programs' / group
uninstall_key = r'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8A1799B5-2AA0-45C6-837D-2D87268F1874}_is1'
run_key = r'Software\Microsoft\Windows\CurrentVersion\Run'
explorer_keys = [r'Software\Classes\*\shell\ZestDrop', r'Software\Classes\Directory\shell\ZestDrop']
user_data = Path(os.environ['LOCALAPPDATA']) / 'ZestDrop'
sentinel = user_data / ('installer-test-' + uid + '.txt')
settings = user_data / 'settings.json'
checks = []

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest() if path.exists() else None

def open_key(key, access=reg.KEY_READ):
    return reg.OpenKey(reg.HKEY_CURRENT_USER, key, 0, access | reg.KEY_WOW64_64KEY)

def tree(key):
    try:
        with open_key(key) as handle:
            children, values = {}, {}
            sub_count, value_count, _ = reg.QueryInfoKey(handle)
            for i in range(value_count):
                name, value, kind = reg.EnumValue(handle, i)
                values[name] = (value, kind)
            for i in range(sub_count):
                name = reg.EnumKey(handle, i)
                children[name] = tree(key + '\\' + name)
            return {'values': values, 'children': children}
    except FileNotFoundError:
        return None

def delete_tree(key):
    saved = tree(key)
    if saved is None:
        return
    for name in saved['children']:
        delete_tree(key + '\\' + name)
    reg.DeleteKeyEx(reg.HKEY_CURRENT_USER, key, reg.KEY_WOW64_64KEY)

def set_value(key, name, value, kind=reg.REG_SZ):
    with reg.CreateKeyEx(reg.HKEY_CURRENT_USER, key, 0, reg.KEY_WRITE | reg.KEY_WOW64_64KEY) as handle:
        reg.SetValueEx(handle, name, 0, kind, value)

def restore_tree(key, saved):
    delete_tree(key)
    if saved is not None:
        with reg.CreateKeyEx(reg.HKEY_CURRENT_USER, key, 0, reg.KEY_WRITE | reg.KEY_WOW64_64KEY):
            pass
        for name, (value, kind) in saved['values'].items():
            set_value(key, name, value, kind)
        for name, child in saved['children'].items():
            restore_tree(key + '\\' + name, child)

def run_value():
    try:
        with open_key(run_key) as handle:
            return reg.QueryValueEx(handle, 'ZestDrop')
    except FileNotFoundError:
        return None

def check(name, passed):
    checks.append({'name': name, 'passed': bool(passed)})
    if not passed:
        raise AssertionError(name)

def setup(folder, name):
    return subprocess.run([str(installer), '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART',
        '/NOCLOSEAPPLICATIONS', '/NORESTARTAPPLICATIONS', '/LANG=chinesesimplified', '/TASKS=',
        '/DIR=' + str(folder), '/GROUP=' + group, '/LOG=' + str(output / (name + '.log'))], timeout=240)

def uninstall():
    proc = subprocess.run([str(target / 'unins000.exe'), '/VERYSILENT', '/SUPPRESSMSGBOXES',
        '/NORESTART', '/LOG=' + str(output / 'uninstall.log')], timeout=120)
    check('Uninstaller exits successfully', proc.returncode == 0)
    for _ in range(30):
        if not (target / 'unins000.exe').exists():
            break
        time.sleep(.2)

def verify_payload():
    for item in manifest['files']:
        path = target / item['path']
        if digest(path) != item['sha256']:
            raise AssertionError('Installed payload differs: ' + item['path'])

if tree(uninstall_key) is not None:
    raise RuntimeError('A per-user ZestDrop installation already exists; test will not overwrite it.')
if start_menu.exists():
    raise RuntimeError('A ZestDrop Start menu folder already exists; test will not overwrite it.')
original_run = run_value()
original_explorer = {key: tree(key) for key in explorer_keys}
settings_before = digest(settings)
user_data.mkdir(exist_ok=True)
sentinel.write_text('User data must survive uninstall.\n', encoding='utf-8')
try:
    occupied = output / 'unrelated-folder'
    occupied.mkdir()
    (occupied / 'keep.txt').write_text('unrelated data', encoding='utf-8')
    rejected = setup(occupied, 'rejected-folder')
    check('Refuses a nonempty unrelated installation folder', rejected.returncode != 0 and
        (occupied / 'keep.txt').read_text(encoding='utf-8') == 'unrelated data' and
        not (occupied / 'ZestDrop.exe').exists() and tree(uninstall_key) is None)
    check('Installs without administrator privileges into a Unicode path', setup(target, 'install').returncode == 0)
    verify_payload()
    check('All installed payload files match the packaged manifest', True)
    with open_key(uninstall_key) as key:
        check('Registers version in per-user Installed apps', reg.QueryValueEx(key, 'DisplayVersion')[0] == manifest['version'])
    check('Creates a Start menu shortcut', (start_menu / 'ZestDrop.lnk').is_file())
    check('Installation preserves existing startup and Explorer preferences', run_value() == original_run and
        all(tree(key) == original_explorer[key] for key in explorer_keys))

    fixtures = output / 'fixtures'
    fixtures.mkdir()
    for filename, action in [('image.png', 'convert:jpg'), ('sound.wav', 'convert:mp3')]:
        source = fixtures / filename
        shutil.copy2(repo / 'work/full-fixtures' / filename, source)
        before = digest(source)
        request, result = fixtures / (filename + '.request.json'), fixtures / (filename + '.result.json')
        request.write_text(json.dumps({'Paths': [str(source)], 'Action': action}), encoding='utf-8')
        subprocess.run([str(target / 'ZestDrop.exe'), '--worker', str(request), str(result)], check=True, timeout=90)
        response = json.loads(result.read_text(encoding='utf-8-sig'))
        check('Installed application executes ' + action, len(response['Files']) == 1 and
            not response['Files'][0].get('Error') and Path(response['Files'][0]['Output']).stat().st_size > 0 and
            digest(source) == before)

    # A real image job keeps the installed executable alive while both guards run.
    from PIL import Image
    busy_image = fixtures / 'busy.png'
    Image.new('RGB', (1024, 1024), (41, 72, 98)).save(busy_image)
    busy_request, busy_result = fixtures / 'busy-request.json', fixtures / 'busy-result.json'
    busy_request.write_text(json.dumps({'Paths': [str(busy_image)] * 20, 'Action': 'editImage',
        'Parameters': {'denoise': '10'}}), encoding='utf-8')
    worker = subprocess.Popen([str(target / 'ZestDrop.exe'), '--worker', str(busy_request), str(busy_result)],
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    try:
        for _ in range(100):
            if Path(str(busy_result) + '.progress').exists() or worker.poll() is not None:
                break
            time.sleep(.1)
        check('Background fixture job is active', worker.poll() is None)
        blocked_uninstall = subprocess.run([str(target / 'unins000.exe'), '/VERYSILENT', '/SUPPRESSMSGBOXES',
            '/NORESTART', '/LOG=' + str(output / 'blocked-uninstall.log')], timeout=30)
        check('Refuses uninstall while the installed app is processing', blocked_uninstall.returncode != 0 and
            worker.poll() is None and (target / 'ZestDrop.exe').exists())
        blocked_setup = setup(target, 'blocked-upgrade')
        check('Refuses upgrade while the installed app is processing', blocked_setup.returncode != 0 and worker.poll() is None)
    finally:
        worker.wait(timeout=180)
    busy_response = json.loads(busy_result.read_text(encoding='utf-8-sig'))
    check('Blocked upgrade and uninstall preserve the running job', worker.returncode == 0 and
        len(busy_response['Files']) == 20 and all(not item.get('Error') for item in busy_response['Files']))

    # Repair must replace even files without Windows version resources.
    (target / 'backend/worker.py').write_text('broken test file\n', encoding='utf-8')
    (target / 'ZestDrop.dll').unlink()
    retained = target / 'keep-user-file.txt'
    retained.write_text('Untracked user-created file.\n', encoding='utf-8')
    check('Same-version repair completes successfully', setup(target, 'repair').returncode == 0)
    verify_payload()
    check('Repair restores missing and modified files while preserving user-created files', retained.exists())

    installed_command = '"' + str(target / 'ZestDrop.exe') + '"'
    foreign_command = '"C:\\Other Portable Copy\\ZestDrop.exe" --open "%1"'
    set_value(run_key, 'ZestDrop', installed_command + ' --startup')
    set_value(explorer_keys[0] + '\\command', '', installed_command + ' --open "%1"')
    set_value(explorer_keys[1] + '\\command', '', foreign_command)
    foreign_before = tree(explorer_keys[1])
    uninstall()
    check('Uninstall removes its files, shortcut and Installed apps entry', not (target / 'ZestDrop.exe').exists() and
        not (target / 'ZestDrop.dll').exists() and not start_menu.exists() and tree(uninstall_key) is None)
    check('Uninstall removes Python bytecode caches', not list(target.rglob('*.pyc')))
    check('Uninstall removes only integrations pointing to this installation', run_value() is None and
        tree(explorer_keys[0]) is None and tree(explorer_keys[1]) == foreign_before)
    check('Uninstall preserves settings and user-created files', sentinel.exists() and
        digest(settings) == settings_before and retained.exists())
    # Deleting only this fixture file leaves a clean destination for reinstall.
    retained.unlink()
    if target.exists():
        target.rmdir()
    check('Reinstalls cleanly after uninstall', setup(target, 'reinstall').returncode == 0)
    uninstall()
    check('A clean installation uninstalls without leaving application files', not target.exists())
except Exception as error:
    checks.append({'name': 'Unexpected failure', 'passed': False, 'error': str(error)})
    raise
finally:
    if (target / 'unins000.exe').exists():
        subprocess.run([str(target / 'unins000.exe'), '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART'], timeout=120)
    if original_run is None:
        try:
            with open_key(run_key, reg.KEY_SET_VALUE) as key:
                reg.DeleteValue(key, 'ZestDrop')
        except FileNotFoundError:
            pass
    else:
        set_value(run_key, 'ZestDrop', *original_run)
    for key, saved in original_explorer.items():
        restore_tree(key, saved)
    sentinel.unlink(missing_ok=True)
    report = {'passed': sum(c['passed'] for c in checks), 'failed': sum(not c['passed'] for c in checks),
        'payload_files': len(manifest['files']), 'checks': checks}
    (output / 'checks.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report, indent=2), flush=True)
