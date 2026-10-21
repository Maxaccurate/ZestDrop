"""Release regressions for location edits and real PDF content redaction, using generated files only."""
import sys
sys.dont_write_bytecode = True
from pathlib import Path
import hashlib
import json
from PIL import Image
import pymupdf as fitz

ROOT = Path(__file__).resolve().parent.parent
APP = ROOT / 'outputs' / 'ZestDrop'
sys.path.insert(0, str(APP / 'backend'))
import location
import pdftools

fixtures = ROOT / 'work' / 'location-redaction-fixtures'
fixtures.mkdir(exist_ok=True)
checks = []


def check(name, action):
    try:
        action()
        checks.append({'test': name, 'passed': True})
    except Exception as error:
        checks.append({'test': name, 'passed': False, 'error': str(error)})


def require(value):
    if not value:
        raise AssertionError('Unexpected release regression result')


photo = fixtures / 'original.jpg'
exif = Image.Exif()
exif[315] = 'Keep fixture artist'
Image.new('RGB', (96, 64), (40, 100, 180)).save(photo, quality=90, exif=exif)
pdf = fixtures / 'original.pdf'
with fitz.open() as document:
    page = document.new_page()
    page.insert_text((36, 50), 'PUBLIC KEEP', fontsize=14)
    page.insert_text((36, 100), 'SECRET ACCOUNT 12345', fontsize=14)
    document.save(pdf)
originals = {path: hashlib.sha256(path.read_bytes()).hexdigest() for path in [photo, pdf]}

set_photo = Path(location.run(photo, {'mode': 'set', 'coordinates': '25.0330, 121.5654'}))


def location_matches(path):
    value = location.read(path)
    return value is not None and abs(value[0] - 25.0330) < 1e-5 and abs(value[1] - 121.5654) < 1e-5


def scan(path):
    data = path.read_bytes()
    return data[data.index(b'\xff\xda'):]


check('JPEG location round-trips the entered coordinates', lambda: require(location_matches(set_photo)))
check('JPEG location editing preserves image scan data and other EXIF', lambda: require(
    scan(photo) == scan(set_photo) and Image.open(set_photo).getexif()[315] == 'Keep fixture artist'))


def remove_location():
    output = Path(location.run(set_photo, {'mode': 'strip'}))
    require(location.read(output) is None)
    require(scan(photo) == scan(output))
    require(Image.open(output).getexif()[315] == 'Keep fixture artist')


check('removing GPS keeps unrelated EXIF and compressed JPEG data', remove_location)
check('location can be copied from another generated photo', lambda: require(location_matches(
    Path(location.run(photo, {'mode': 'set', 'locationSource': str(set_photo)})))))


def reject_invalid_coordinates():
    try:
        location.run(photo, {'mode': 'set', 'coordinates': '91, 121'})
    except ValueError:
        return
    raise AssertionError('Invalid latitude was accepted')


check('invalid coordinates are rejected', reject_invalid_coordinates)


def redact_text():
    output = Path(pdftools.redact(pdf, {'findText': 'SECRET ACCOUNT 12345'}))
    with fitz.open(output) as document:
        text = document[0].get_text()
    require('SECRET ACCOUNT' not in text and 'PUBLIC KEEP' in text)
    require(b'SECRET ACCOUNT' not in output.read_bytes())


check('PDF text redaction removes secret content and retains other text', redact_text)


def redact_box():
    with fitz.open(pdf) as document:
        box = document[0].search_for('SECRET ACCOUNT 12345')[0]
    output = Path(pdftools.redact(pdf, {'regions': json.dumps([
        {'page': 1, 'x': box.x0, 'y': box.y0, 'width': box.width, 'height': box.height}])}))
    with fitz.open(output) as document:
        require('SECRET ACCOUNT' not in document[0].get_text() and 'PUBLIC KEEP' in document[0].get_text())


check('PDF box redaction removes the selected text', redact_box)
check('all original fixtures remain unchanged', lambda: require(all(
    hashlib.sha256(path.read_bytes()).hexdigest() == digest for path, digest in originals.items())))
report = {'passed': sum(row['passed'] for row in checks), 'failed': sum(not row['passed'] for row in checks), 'checks': checks}
(ROOT / 'outputs' / 'location-redaction-tests.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf8')
print(json.dumps(report, ensure_ascii=False), flush=True)
raise SystemExit(bool(report['failed']))
