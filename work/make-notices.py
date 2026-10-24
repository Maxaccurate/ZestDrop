"""Write THIRD-PARTY-NOTICES.txt for the app bundle from docs/dependencies.json.

Usage: python work/make-notices.py <output-file>
build.ps1 runs this so every bundle (zip or Store package) ships the notices file the tray menu opens.
"""
from pathlib import Path
import json, re, sys

ROOT = Path(__file__).resolve().parent.parent
deps = json.loads((ROOT / 'docs' / 'dependencies.json').read_text(encoding='utf8'))
ffmpeg = re.search(r'version (\d+(?:\.\d+)+)', deps['ffmpeg']).group(1)
python = deps['python'].split()[0]
dotnet = deps['dotnet_runtime'].split()[0]
pymupdf = next(p['version'] for p in deps['python_packages'] if p['name'] == 'pymupdf')

lines = [
    'ZestDrop - third-party notices',
    '=' * 30,
    '',
    'ZestDrop itself is open source under the MIT License: https://github.com/Maxaccurate/ZestDrop',
    'It bundles the components below. Each keeps its own license; the MIT License does not replace them.',
    'License texts ship inside this app folder at the paths shown.',
    '',
    f'FFmpeg {ffmpeg} (ffmpeg.exe, ffprobe.exe) - GNU General Public License v3 (GPL build, no non-free parts)',
    '  ZestDrop runs FFmpeg as a separate program; it is not linked into ZestDrop.',
    '  License: runtime/ffmpeg/LICENSE',
    '  Binary build: https://www.gyan.dev/ffmpeg/builds/ (configuration: run "ffmpeg.exe -version")',
    f'  Corresponding source: https://ffmpeg.org/releases/ffmpeg-{ffmpeg}.tar.xz',
    f'  Also attached to our release: https://github.com/Maxaccurate/ZestDrop/releases/download/v0.1.0/ffmpeg-{ffmpeg}.tar.xz',
    '',
    '7-Zip 25.01 (7z.exe, 7z.dll) - GNU LGPL 2.1, BSD 3-clause, and the unRAR license restriction',
    '  The unRAR code may not be used to develop a RAR-compatible archiver.',
    '  License: runtime/7zip/License.txt   Source: https://www.7-zip.org/a/7z2501-src.7z',
    '',
    f'Python {python} (embedded) - Python Software Foundation License',
    '  License: runtime/python/LICENSE.txt   Source: https://www.python.org/downloads/source/',
    '',
    f'.NET {dotnet} runtime and WPF - MIT License',
    '  https://github.com/dotnet/runtime  https://github.com/dotnet/wpf',
    '',
    'Source Han Sans CN Medium 2.005 - SIL Open Font License 1.1',
    '  Copyright 2014-2025 Adobe. Embedded in the application for Chinese interface text.',
    '  License: assets/fonts/OFL.txt',
    '  Source: https://github.com/adobe-fonts/source-han-sans',
    '',
    'Python packages (license files: runtime/python/Lib/site-packages/<package>-<version>.dist-info/):',
]
for package in sorted(deps['python_packages'], key=lambda p: p['name'].lower()):
    lines.append(f"  {package['name']} {package['version']} - {package['license']}")
lines += [
    '',
    f'PyMuPDF {pymupdf} is used under the GNU Affero General Public License v3.',
    '  Because ZestDrop is distributed together with it, ZestDrop\'s complete source code is published at',
    '  https://github.com/Maxaccurate/ZestDrop, and PyMuPDF\'s source is available at',
    f'  https://github.com/pymupdf/PyMuPDF/archive/refs/tags/{pymupdf}.tar.gz',
    '',
    'Python package sources: https://pypi.org/ (each package page links its source repository).',
    '',
    '.NET libraries (NuGet packages, built into the application folder as DLLs):',
]
for package in sorted(deps.get('dotnet_packages', []), key=lambda p: p['name'].lower()):
    lines.append(f"  {package['name']} {package['version']} - {package['license']}")
    lines.append(f"    {package['copyright']}. Source: {package['source']}")
lines += [
    '',
    'OpenMcdf (MPL-2.0) and UTF.Unknown (MPL-1.1) are used unmodified; their source code is available at the links above.',
    'Third-party code inside SkiaSharp and Microsoft.Maui.Graphics is listed in licenses/ in this folder.',
    '',
    'MIT License (applies to each package above marked MIT, with its own copyright line)',
    '',
    'Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated',
    'documentation files (the "Software"), to deal in the Software without restriction, including without limitation',
    'the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to',
    'permit persons to whom the Software is furnished to do so, subject to the following conditions:',
    '',
    'The above copyright notice and this permission notice shall be included in all copies or substantial portions of',
    'the Software.',
    '',
    'THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE',
    'WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR',
    'COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR',
    'OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.',
    '',
    'BSD 2-Clause License (applies to Markdig and Tomlyn, with their copyright lines above)',
    '',
    'Redistribution and use in source and binary forms, with or without modification, are permitted provided that the',
    'following conditions are met:',
    '1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following',
    '   disclaimer.',
    '2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the',
    '   following disclaimer in the documentation and/or other materials provided with the distribution.',
    '',
    'THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES,',
    'INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE',
    'DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,',
    'SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR',
    'SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY,',
    'WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE',
    'OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.',
    '',
]
out = Path(sys.argv[1])
out.write_text('\n'.join(lines), encoding='utf-8-sig')
print(f'wrote {out} ({len(lines)} lines)')
