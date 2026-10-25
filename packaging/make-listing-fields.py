"""Write packaging/store-listing-fields.md: every Partner Center listing field, ready to paste, per language.

Usage: python packaging/make-listing-fields.py
The script also checks the Store's limits and fails if one is exceeded. Update the text below for each release.
"""
from pathlib import Path

OUT = Path(__file__).resolve().parent / 'store-listing-fields.md'
VERSION = '1.2.1'

L = {}
L['en'] = dict(
    heading='English (United States)',
    name='ZestDrop',
    short=("Convert files without opening an app. Hold Shift while you drag any file, and a format wheel opens under your "
           "cursor. Release on the format you want. Images, video, audio, PDF, 3D, e-books and more, all processed on your PC."),
    whatsnew="""New since the last Store version:

• Many new file types, converted on your PC with no extra software: 3D models (GLB, glTF, OBJ, STL, 3MF, PLY, FBX), CAD drawings (DWG, DXF), fonts, data files (JSON, YAML, TOML, XML, CSV), e-books and comics (EPUB, MOBI, AZW3, FB2, CBZ), more subtitle formats, emails (EML, MSG), and images traced into SVG. Spreadsheets keep their formulas.
• Customize the wheel visually: drag options into the order you want and watch a live preview of the real wheel.
• Arabic interface with right-to-left layout, next to English and Chinese.
• Light, dark or "Match Windows" appearance, and a clearer, rounded tray menu.
• Optional sticky menu: the wheel stays open after you release the mouse; Tab switches between formats and tools.
• New tools: Location (read, remove or replace a photo's or video's location) and Redact PDF (removes the text and images under marked areas).
• Watched folders: inbox mode, a separate output folder, subfolders and file-name filters.
• "Welcome window…" in the tray menu, and the start-with-Windows option always shows the state Windows reports.""",
    description="""ZestDrop turns the drag you already make into a file converter and a small toolbox.

Select files on the desktop or in File Explorer and start dragging. Press Shift and a format wheel appears under your cursor; release on the format you want. Press Ctrl+Shift instead for tools such as compress, resize, rotate, crop, watermark or redact. For pictures and videos, related tools share one window so the wheel stays simple. The new file is saved next to the original, which is never overwritten.

WHAT IT HANDLES
• Images: JPG, PNG, WebP, HEIC, TIFF, AVIF, BMP, SVG. Convert, resize, rotate by any angle, crop with a draggable frame, compress to a target size, watermark with text or a logo, pixelate or blur areas, adjust exposure and colour, add a background, edit or strip metadata and location, make icons, recognise text (OCR), trace into SVG, or combine pictures into a PDF, collage, GIF or slideshow.
• Video: MP4, MOV, MKV, WebM, AVI, WMV, GIF. Convert, make GIFs, trim and split, join, change speed, rotate, change resolution and frame rate, fade, reverse or loop, burn in subtitles, replace or mix the soundtrack, watermark, redact regions over time, save frames or a contact sheet.
• Audio: MP3, M4A, WAV, FLAC, OGG, Opus, AIFF, WMA. Convert, compress, normalize loudness, change volume, add fades, reduce noise, trim, join, adjust channels, bleep out sections, make a phone ringtone or a waveform video.
• PDF and documents: PDF to Word, images or text; text to PDF; merge, split, reorder and rotate pages; add or remove a password; page numbers, headers and footers; extract images; watermark; redact marked areas and matching text; text recognition for scanned PDFs. Word, Excel, PowerPoint, OpenDocument, Markdown and HTML convert natively, and spreadsheets keep their formulas.
• Office: export Word, Excel and PowerPoint files to PDF or images with the exact layout (uses your installed Microsoft Office).
• 3D and CAD: GLB, glTF, OBJ, STL, 3MF, PLY and FBX models to other 3D formats or a picture; DWG and DXF drawings to SVG, PDF or images, or between DWG and DXF.
• E-books and comics: EPUB, KEPUB, MOBI, AZW3, FB2 and CBZ to EPUB, FB2, Word, Markdown, HTML or text; Markdown and text files to EPUB.
• Fonts, data and more: TTF, OTF, WOFF, WOFF2 and EOT fonts; JSON, JSON Lines, YAML, TOML, XML, CSV, TSV and vCard data; SRT, VTT, ASS, SAMI, LRC, TTML, SUB and SCC subtitles; EML and MSG emails to HTML or text.
• Archives: pack and extract ZIP, TAR, GZIP and RAR, or make ZIP and 7z files with an AES-256 password and split volumes.

WORKS THE WAY YOU DO
• Customize the wheel: drag options into your own order and see the real wheel update as you go. The option you used last can move to the top.
• Presets remember your settings. Flows chain several tools, such as resize, watermark, compress.
• Watched folders process new files automatically, with an inbox mode, a separate output folder and file-name filters.
• Save next to the original or in one folder you choose, and process a copied screenshot or files from the tray menu.
• Each job gets a progress card with Pause, Cancel and Hide. Run several at once.
• Lives in the system tray, with no main window. Optionally starts with Windows.
• English, Simplified Chinese and Arabic interface, with light and dark appearance.

PRIVATE BY DESIGN
• Everything runs on your PC with the engines included in the app (FFmpeg, 7-Zip, Python and open-source .NET libraries). Nothing is uploaded, and it works offline.
• Text recognition uses the recogniser built into Windows.
• No account, ads, analytics or telemetry.

Free and open source under the MIT License.""",
    features=[
        'Hold Shift while dragging a file to pick a new format from a wheel under your cursor',
        'Ctrl+Shift opens tools: compress, resize, rotate, crop, watermark, redact and more',
        'Images, video, audio, PDF, Office documents and archives',
        '3D models, CAD drawings, e-books, fonts, data files, subtitles and emails, converted without extra software',
        'Everything is processed locally: nothing is uploaded',
        'Text recognition (OCR) for pictures and scanned PDFs, using the recogniser built into Windows',
        'Customize the wheel with a live preview: drag options into the order you want',
        'Presets and flows: save your settings and chain tools such as resize, watermark, compress',
        'Watched folders process new files automatically',
        'Pause, resume, cancel or hide any job; run several at once',
        'Originals are never overwritten; save next to the original or in a folder you choose',
        'Password-protected ZIP and 7z (AES-256), split volumes, PDF passwords and PDF redaction',
        'Built-in player with draggable trim handles for audio and video',
        'Light and dark appearance; optional start with Windows',
        'English, Simplified Chinese and Arabic interface',
        'Free and open source',
    ],
    keywords=['file converter', 'video converter', 'image converter', 'audio converter', 'PDF converter', 'compress video', 'drag and drop'],
    copyright='© 2026 陈唐安',
    license=("ZestDrop is licensed under the MIT License. It includes third-party components under their own licenses, including "
             "FFmpeg (GPLv3), 7-Zip (LGPL), PyMuPDF (AGPLv3) and open-source .NET libraries (MIT, BSD and MPL). "
             "See \"Third-party notices\" in the tray menu."),
    developer='陈唐安',
    captions=[
        'Hold Shift while dragging a file and a format wheel opens under your cursor. Release on JPG, PDF, MP3 or any other format.',
        'Press Ctrl+Shift for tools. Related tools share one wheel entry, so the wheel stays simple.',
        'Related tools share one window with tabs, a live preview and draggable trim handles.',
        'Every job gets a progress card with Pause, Cancel and Hide. Run several at once.',
        'Customize the wheel: drag options into the order you want and watch the real wheel update.',
        'New file types: 3D models, CAD drawings, e-books, fonts and data files convert right on your PC.',
    ],
    shots='en',
    art='en',
)
L['zh'] = dict(
    heading='Chinese (China) 中文（中国）',
    name='ZestDrop',
    short=("无需打开应用即可转换文件。拖动任意文件时按住 Shift，光标下方会出现格式轮盘，在想要的格式上松手即可。"
           "图片、视频、音频、PDF、3D 模型、电子书等，全部在你的电脑上处理。"),
    whatsnew="""自上一个商店版本以来的更新：

• 新增大量文件类型，无需额外软件即可在本机转换：3D 模型（GLB、glTF、OBJ、STL、3MF、PLY、FBX）、CAD 图纸（DWG、DXF）、字体、数据文件（JSON、YAML、TOML、XML、CSV）、电子书和漫画（EPUB、MOBI、AZW3、FB2、CBZ）、更多字幕格式、邮件（EML、MSG），以及把图片描摹为 SVG。电子表格转换时保留公式。
• 可视化自定义轮盘：拖动选项调整顺序，并实时预览真实的轮盘。
• 新增阿拉伯语界面（从右到左布局），与英文、中文并列。
• 浅色、深色或“跟随系统”外观，托盘菜单更清晰、更圆润。
• 可选“固定菜单”：松开鼠标后轮盘保持打开；按 Tab 在格式和工具之间切换。
• 新工具：“位置信息”（读取、移除或替换照片和视频的位置）和“PDF 遮盖”（移除标记区域下的文字和图片）。
• 监视文件夹：收件箱模式、单独的输出文件夹、包含子文件夹和文件名筛选。
• 托盘菜单新增“欢迎窗口…”；开机自启选项始终显示 Windows 报告的实际状态。""",
    description="""ZestDrop 把你本来就会做的“拖动文件”变成文件转换器和一套小工具箱。

在桌面或资源管理器中选中文件并开始拖动，按下 Shift，光标下方会出现格式轮盘，在想要的格式上松手即可。按 Ctrl+Shift 则打开工具，例如压缩、缩放、旋转、裁剪、水印或打码。图片和视频的相关工具会合并到同一个窗口，轮盘始终简洁。新文件保存在原文件旁边，原文件永远不会被覆盖。

支持的文件
• 图片：JPG、PNG、WebP、HEIC、TIFF、AVIF、BMP、SVG。转换、缩放、任意角度旋转、用可拖动选框裁剪、压缩到指定大小、文字或图标水印、马赛克或模糊打码、调整曝光和色彩、添加背景、编辑或清除元数据和位置信息、制作图标、文字识别（OCR）、描摹为 SVG，或把多张图片合成 PDF、拼图、GIF 或幻灯片。
• 视频：MP4、MOV、MKV、WebM、AVI、WMV、GIF。转换、制作 GIF、裁剪时段与分段、拼接、变速、旋转、调整分辨率和帧率、淡入淡出/倒放/循环、烧录字幕、替换或混入配乐、水印、按时间段打码、保存帧或缩略图总览。
• 音频：MP3、M4A、WAV、FLAC、OGG、Opus、AIFF、WMA。转换、压缩、标准化响度、调整音量、淡入淡出、降噪、裁剪、拼接、调整声道、消音蜂鸣、制作手机铃声或波形视频。
• PDF 与文档：PDF 转 Word、图片或文本；文本转 PDF；合并、拆分、调整页面顺序和旋转；添加或移除密码；页码、页眉和页脚；提取图片；水印；遮盖标记区域和匹配的文字；扫描版 PDF 的文字识别。Word、Excel、PowerPoint、OpenDocument、Markdown 和 HTML 可直接转换，电子表格保留公式。
• Office：按原版式把 Word、Excel、PowerPoint 导出为 PDF 或图片（调用已安装的 Microsoft Office）。
• 3D 与 CAD：GLB、glTF、OBJ、STL、3MF、PLY、FBX 模型转换为其他 3D 格式或图片；DWG、DXF 图纸转换为 SVG、PDF 或图片，或在 DWG 与 DXF 之间互转。
• 电子书和漫画：EPUB、KEPUB、MOBI、AZW3、FB2、CBZ 转换为 EPUB、FB2、Word、Markdown、HTML 或文本；Markdown 和文本文件可制作成 EPUB。
• 字体、数据及更多：TTF、OTF、WOFF、WOFF2、EOT 字体；JSON、JSON Lines、YAML、TOML、XML、CSV、TSV、vCard 数据；SRT、VTT、ASS、SAMI、LRC、TTML、SUB、SCC 字幕；EML、MSG 邮件转换为 HTML 或文本。
• 压缩包：打包和解压 ZIP、TAR、GZIP、RAR，或制作带 AES-256 密码、可分卷的 ZIP 和 7z。

顺手好用
• 自定义轮盘：拖动选项排成你想要的顺序，真实轮盘随之实时更新；上次使用的选项可以排在最前。
• 预设记住你的设置；流程把多个工具串联起来，例如缩放、水印、压缩。
• 监视文件夹：新文件自动处理，支持收件箱模式、单独的输出文件夹和文件名筛选。
• 可保存在原文件旁边或你选择的文件夹，也可从托盘菜单处理已复制的截图或文件。
• 每个任务都有进度卡片，可暂停、取消或隐藏；可同时运行多个任务。
• 常驻系统托盘，没有主窗口，可选开机自启。
• 简体中文、英文和阿拉伯语界面，支持浅色和深色外观。

注重隐私
• 所有处理都由应用内置的引擎（FFmpeg、7-Zip、Python 和开源 .NET 库）在本机完成，不上传任何内容，断网也能使用。
• 文字识别使用 Windows 内置的识别引擎。
• 无需账号，没有广告、统计或遥测。

以 MIT 许可证免费开源。""",
    features=[
        '拖动文件时按住 Shift，从光标下的轮盘选择新格式',
        'Ctrl+Shift 打开工具：压缩、缩放、旋转、裁剪、水印、打码等',
        '支持图片、视频、音频、PDF、Office 文档和压缩包',
        '3D 模型、CAD 图纸、电子书、字体、数据文件、字幕和邮件，无需额外软件即可转换',
        '全部在本机处理，不上传任何内容',
        '图片和扫描版 PDF 的文字识别（OCR），使用 Windows 内置识别引擎',
        '可视化自定义轮盘：拖动选项排序，实时预览',
        '预设和流程：保存设置，并串联缩放、水印、压缩等工具',
        '监视文件夹：新文件自动处理',
        '任务可暂停、继续、取消或隐藏，可同时运行多个任务',
        '永不覆盖原文件；可保存在原文件旁或你选择的文件夹',
        '带密码的 ZIP 和 7z（AES-256）、分卷压缩、PDF 密码和 PDF 遮盖',
        '音视频工具内置播放器和可拖动的裁剪手柄',
        '浅色和深色外观；可选开机自启',
        '简体中文、英文和阿拉伯语界面',
        '免费开源',
    ],
    keywords=['文件转换', '视频转换', '图片转换', '音频转换', 'PDF 转换', '视频压缩', '格式转换'],
    copyright='© 2026 陈唐安',
    license=("ZestDrop 以 MIT 许可证授权，并包含遵循各自许可证的第三方组件，包括 FFmpeg（GPLv3）、7-Zip（LGPL）、PyMuPDF（AGPLv3）"
             "以及开源 .NET 库（MIT、BSD 和 MPL）。详见托盘菜单中的“第三方组件许可”。"),
    developer='陈唐安',
    captions=[
        '拖动文件时按住 Shift，光标下会出现格式轮盘，在 JPG、PDF、MP3 等格式上松手即可转换。',
        '按 Ctrl+Shift 打开工具。相关工具合并为一个入口，轮盘始终简洁。',
        '相关工具合并在一个带标签页的窗口里，带实时预览和可拖动的裁剪手柄。',
        '每个任务都有进度卡片，可暂停、取消或隐藏，可同时运行多个任务。',
        '自定义轮盘：拖动选项调整顺序，真实轮盘随之实时更新。',
        '新增文件类型：3D 模型、CAD 图纸、电子书、字体和数据文件，都能在本机直接转换。',
    ],
    shots='zh',
    art='zh',
)
L['ar'] = dict(
    heading='Arabic العربية',
    name='ZestDrop',
    short=("حوّل ملفاتك دون فتح أي تطبيق. اضغط Shift أثناء سحب أي ملف، فتظهر عجلة الصيغ تحت المؤشر. "
           "أفلت الملف على الصيغة التي تريدها. صور وفيديو وصوت وPDF ونماذج ثلاثية الأبعاد وكتب إلكترونية، وكل المعالجة على جهازك."),
    whatsnew="""الجديد منذ آخر إصدار في المتجر:

• أنواع ملفات جديدة كثيرة تُحوَّل على جهازك دون برامج إضافية: النماذج ثلاثية الأبعاد (GLB وglTF وOBJ وSTL و3MF وPLY وFBX)، ورسومات CAD ‏(DWG وDXF)، والخطوط، وملفات البيانات (JSON وYAML وTOML وXML وCSV)، والكتب الإلكترونية والقصص المصورة (EPUB وMOBI وAZW3 وFB2 وCBZ)، وصيغ ترجمة إضافية، ورسائل البريد (EML وMSG)، وتحويل الصور إلى SVG. وتحتفظ جداول البيانات بصيغها الحسابية.
• خصّص العجلة بصريًا: اسحب الخيارات بالترتيب الذي تريده وشاهد معاينة حية للعجلة الحقيقية.
• واجهة عربية باتجاه من اليمين إلى اليسار، إلى جانب الإنجليزية والصينية.
• مظهر فاتح أو داكن أو «مطابقة إعدادات النظام»، وقائمة أوضح وبحواف مستديرة في علبة النظام.
• «تثبيت القائمة» اختياريًا: تبقى العجلة مفتوحة بعد إفلات الفأرة، ويبدّل Tab بين الصيغ والأدوات.
• أدوات جديدة: «الموقع الجغرافي» (قراءة موقع الصورة أو الفيديو أو إزالته أو استبداله) و«حجب نصوص PDF» (يزيل النصوص والصور الموجودة تحت المناطق المحددة).
• المجلدات المراقبة: وضع صندوق الوارد، ومجلد إخراج منفصل، والمجلدات الفرعية، وتصفية أسماء الملفات.
• عنصر «نافذة الترحيب…» في قائمة علبة النظام، وخيار التشغيل مع Windows يعرض دائمًا الحالة التي يبلّغ عنها Windows.""",
    description="""يحوّل ZestDrop عملية السحب التي تقوم بها أصلًا إلى محوّل ملفات وصندوق أدوات صغير.

حدّد الملفات على سطح المكتب أو في مستكشف الملفات وابدأ سحبها. اضغط Shift فتظهر عجلة الصيغ تحت المؤشر، ثم أفلت الملف على الصيغة التي تريدها. أو اضغط Ctrl+Shift لفتح الأدوات مثل الضغط وتغيير الحجم والتدوير والقص والعلامة المائية والطمس. تُجمع أدوات الصور والفيديو المتشابهة في نافذة واحدة حتى تبقى العجلة بسيطة. يُحفظ الملف الجديد بجوار الأصلي، ولا يُستبدل الملف الأصلي أبدًا.

ما يتعامل معه
• الصور: JPG وPNG وWebP وHEIC وTIFF وAVIF وBMP وSVG. التحويل وتغيير الحجم والتدوير بأي زاوية والقص بإطار قابل للسحب والضغط إلى حجم محدد وإضافة علامة مائية نصية أو شعار وتنقيط المناطق أو تمويهها وضبط الإضاءة والألوان وإضافة خلفية وتعديل البيانات الوصفية والموقع أو إزالتها وإنشاء الأيقونات والتعرف الضوئي على النصوص (OCR) والتحويل إلى SVG، أو دمج الصور في ملف PDF أو مجمّعة صور أو GIF أو عرض شرائح.
• الفيديو: MP4 وMOV وMKV وWebM وAVI وWMV وGIF. التحويل وإنشاء GIF والقص والتقسيم والدمج وتغيير السرعة والتدوير وتغيير الدقة ومعدل الإطارات والتلاشي والعكس والتكرار ودمج الترجمة واستبدال الصوت أو مزجه والعلامة المائية وطمس المناطق عبر الزمن وحفظ الإطارات أو ورقة مصغرات.
• الصوت: MP3 وM4A وWAV وFLAC وOGG وOpus وAIFF وWMA. التحويل والضغط وتسوية الجهارة وتغيير مستوى الصوت وإضافة التلاشي وتقليل الضوضاء والقص والدمج وضبط القنوات وحجب المقاطع بصفير وإنشاء نغمة هاتف أو فيديو للموجة الصوتية.
• PDF والمستندات: PDF إلى Word أو صور أو نص، ونص إلى PDF، ودمج الصفحات وتقسيمها وإعادة ترتيبها وتدويرها، وإضافة كلمة مرور أو إزالتها، وأرقام الصفحات والرؤوس والتذييلات، واستخراج الصور، والعلامة المائية، وطمس المناطق المحددة والنصوص المطابقة، والتعرف على النصوص في ملفات PDF الممسوحة ضوئيًا. وتُحوَّل ملفات Word وExcel وPowerPoint وOpenDocument وMarkdown وHTML مباشرة، وتحتفظ جداول البيانات بصيغها الحسابية.
• Office: تصدير ملفات Word وExcel وPowerPoint إلى PDF أو صور مع الحفاظ على التنسيق تمامًا (باستخدام Microsoft Office المثبّت لديك).
• ثلاثي الأبعاد وCAD: تحويل نماذج GLB وglTF وOBJ وSTL و3MF وPLY وFBX إلى صيغ ثلاثية الأبعاد أخرى أو إلى صورة، وتحويل رسومات DWG وDXF إلى SVG أو PDF أو صور، أو بين DWG وDXF.
• الكتب الإلكترونية والقصص المصورة: EPUB وKEPUB وMOBI وAZW3 وFB2 وCBZ إلى EPUB أو FB2 أو Word أو Markdown أو HTML أو نص، وتحويل ملفات Markdown والنصوص إلى EPUB.
• الخطوط والبيانات وغيرها: خطوط TTF وOTF وWOFF وWOFF2 وEOT، وبيانات JSON وJSON Lines وYAML وTOML وXML وCSV وTSV وvCard، وترجمات SRT وVTT وASS وSAMI وLRC وTTML وSUB وSCC، ورسائل EML وMSG إلى HTML أو نص.
• الأرشيفات: ضغط وفك ZIP وTAR وGZIP وRAR، أو إنشاء ملفات ZIP و7z بكلمة مرور AES-256 وتقسيمها إلى أجزاء.

يعمل بطريقتك
• خصّص العجلة: اسحب الخيارات بترتيبك الخاص وشاهد العجلة الحقيقية تتحدث أثناء ذلك. ويمكن أن ينتقل آخر خيار استخدمته إلى الأعلى.
• تتذكر الإعدادات المسبقة إعداداتك، وتربط التدفقات عدة أدوات معًا، مثل تغيير الحجم ثم العلامة المائية ثم الضغط.
• تعالج المجلدات المراقبة الملفات الجديدة تلقائيًا، مع وضع صندوق الوارد ومجلد إخراج منفصل وتصفية أسماء الملفات.
• احفظ بجوار الملف الأصلي أو في مجلد واحد تختاره، وعالج لقطة شاشة منسوخة أو ملفات منسوخة من قائمة علبة النظام.
• لكل مهمة بطاقة تقدم فيها إيقاف مؤقت وإلغاء وإخفاء، ويمكن تشغيل عدة مهام معًا.
• يعمل من علبة النظام دون نافذة رئيسية، ويمكن تشغيله مع Windows اختياريًا.
• واجهة باللغات العربية والإنجليزية والصينية المبسطة، بمظهر فاتح وداكن.

الخصوصية أولًا
• تتم كل المعالجة على جهازك باستخدام المحركات المضمّنة في التطبيق (FFmpeg و7-Zip وPython ومكتبات .NET مفتوحة المصدر). لا يُرفع أي شيء، ويعمل دون اتصال بالإنترنت.
• يستخدم التعرف على النصوص المحرّك المدمج في Windows.
• لا حساب ولا إعلانات ولا تحليلات ولا قياس عن بُعد.

مجاني ومفتوح المصدر بموجب ترخيص MIT.""",
    features=[
        'اضغط Shift أثناء سحب ملف لاختيار صيغة جديدة من عجلة تحت المؤشر',
        'يفتح Ctrl+Shift الأدوات: الضغط وتغيير الحجم والتدوير والقص والعلامة المائية والطمس وغيرها',
        'الصور والفيديو والصوت وPDF ومستندات Office والأرشيفات',
        'النماذج ثلاثية الأبعاد ورسومات CAD والكتب الإلكترونية والخطوط وملفات البيانات والترجمات ورسائل البريد، دون برامج إضافية',
        'كل المعالجة على جهازك: لا يُرفع أي شيء',
        'التعرف الضوئي على النصوص (OCR) في الصور وملفات PDF الممسوحة باستخدام محرك Windows المدمج',
        'خصّص العجلة بمعاينة حية: اسحب الخيارات بالترتيب الذي تريده',
        'إعدادات مسبقة وتدفقات: احفظ إعداداتك واربط الأدوات معًا',
        'المجلدات المراقبة تعالج الملفات الجديدة تلقائيًا',
        'إيقاف مؤقت واستئناف وإلغاء وإخفاء لأي مهمة، وتشغيل عدة مهام معًا',
        'لا يُستبدل الملف الأصلي أبدًا؛ احفظ بجواره أو في مجلد تختاره',
        'ملفات ZIP و7z محمية بكلمة مرور (AES-256) وتقسيم إلى أجزاء وكلمات مرور PDF وطمس PDF',
        'مشغّل مدمج بمقابض قص قابلة للسحب للصوت والفيديو',
        'مظهر فاتح وداكن، وتشغيل اختياري مع Windows',
        'واجهة بالعربية والإنجليزية والصينية المبسطة',
        'مجاني ومفتوح المصدر',
    ],
    keywords=['محول ملفات', 'محول فيديو', 'محول صور', 'محول صوت', 'محول PDF', 'ضغط الفيديو', 'سحب وإفلات'],
    copyright='© 2026 陈唐安',
    license=("يُرخَّص ZestDrop بموجب ترخيص MIT، ويتضمن مكونات من جهات خارجية بتراخيصها الخاصة، منها FFmpeg ‏(GPLv3) و7-Zip ‏(LGPL) "
             "وPyMuPDF ‏(AGPLv3) ومكتبات .NET مفتوحة المصدر (MIT وBSD وMPL). راجع «تراخيص الطرف الثالث» في قائمة علبة النظام."),
    developer='陈唐安',
    captions=[
        'اضغط Shift أثناء سحب ملف فتظهر عجلة الصيغ تحت المؤشر. أفلت الملف على JPG أو PDF أو MP3 أو أي صيغة أخرى.',
        'اضغط Ctrl+Shift لفتح الأدوات. تجتمع الأدوات المتشابهة في عنصر واحد حتى تبقى العجلة بسيطة.',
        'تجتمع الأدوات المتشابهة في نافذة واحدة بعلامات تبويب ومعاينة حية ومقابض قص قابلة للسحب.',
        'لكل مهمة بطاقة تقدم فيها إيقاف مؤقت وإلغاء وإخفاء. شغّل عدة مهام معًا.',
        'خصّص العجلة: اسحب الخيارات بالترتيب الذي تريده وشاهد العجلة الحقيقية تتحدث.',
        'أنواع ملفات جديدة: النماذج ثلاثية الأبعاد ورسومات CAD والكتب الإلكترونية والخطوط وملفات البيانات تُحوَّل على جهازك.',
    ],
    shots='ar',
    art='en',
)

# Limits from Partner Center and Microsoft's docs.
for code, d in L.items():
    assert len(d['short']) <= 270, (code, 'short description', len(d['short']))
    assert len(d['description']) <= 10000, (code, 'description')
    assert len(d['whatsnew']) <= 1500, (code, "what's new", len(d['whatsnew']))
    assert 'http' not in d['description'] and 'http' not in d['whatsnew'], (code, 'no URLs in the description or what\'s new')
    assert len(d['features']) <= 20 and all(len(f) <= 200 for f in d['features']), (code, 'features')
    assert len(d['keywords']) <= 7 and all(len(k) <= 40 for k in d['keywords']), (code, 'keywords')
    assert sum(len(k.split()) for k in d['keywords']) <= 21, (code, 'keyword words')
    assert all(len(c) <= 200 for c in d['captions']), (code, 'captions')


def block(text):
    return '```text\n' + text + '\n```\n'


lines = [f'# Store listing fields for ZestDrop {VERSION}: ready to paste',
         '',
         'Fill in each language on its own listing page (Store listings > click the language). Leave every field that is not listed here empty.',
         'Images are in `packaging/store-art/` (logos, hero) and `packaging/screenshots/` (screenshots). Regenerate with `make-store-art.py`, `make-screenshots.py` and this script.',
         '']
for code, d in L.items():
    n, art = d['shots'], d['art']
    lines += [f"## {d['heading']}", '',
              '| Field on the page | What to enter |', '|---|---|',
              f"| Product name | {d['name']} (pick it in the drop-down) |",
              "| What's new in this version | the text under \"What's new\" below |",
              '| Short title, Voice title | **leave empty** (Xbox only) |',
              f"| Copyright and trademark info | {d['copyright']} |",
              f"| Developed by | {d['developer']} |",
              f"| Screenshots > Desktop | `screenshots/{n}-1.png` … `{n}-{len(d['captions'])}.png`, in that order, with the captions below |",
              f"| Store logos > Poster art (720 x 1080) | `store-art/poster-720x1080-{art}.png` |",
              f"| Store logos > Box art (1080 x 1080) | `store-art/box-art-1080-{art}.png` |",
              '| Store display images > App tile icon 300 x 300 / 150 x 150 / 71 x 71 | `store-art/tile-300.png`, `tile-150.png`, `tile-71.png` |',
              '| Windows and Xbox image > Super hero art (1920 x 1080) | `store-art/hero-1920x1080.png` (the same for every language) |',
              '| Xbox images, Trailers | **leave empty** (Windows only) |',
              '', f"### What's new in this version ({len(d['whatsnew'])} of 1,500 characters)", '', block(d['whatsnew']),
              f"### Description ({len(d['description'])} of 10,000 characters)", '', block(d['description']),
              f"### Short description ({len(d['short'])} of 270 recommended)", '', block(d['short']),
              f"### Product features (one per box, {len(d['features'])} of 20; press \"Add more\" for each)", '']
    lines += [f'{i}. `{f}`' for i, f in enumerate(d['features'], 1)]
    lines += ['', '### Keywords (press Enter after each; ' + f"{len(d['keywords'])} of 7, {sum(len(k.split()) for k in d['keywords'])} of 21 words)", '']
    lines += [f'- `{k}`' for k in d['keywords']]
    lines += ['', '### Additional license terms', '', block(d['license']), '### Screenshot captions (200 characters or fewer)', '']
    lines += [f'{i}. {c}' for i, c in enumerate(d['captions'], 1)]
    lines += ['', '---', '']
OUT.write_text('\n'.join(lines), encoding='utf8')
print('wrote', OUT.name, '| short descriptions:', {c: len(d['short']) for c, d in L.items()},
      '| descriptions:', {c: len(d['description']) for c, d in L.items()},
      "| what's new:", {c: len(d['whatsnew']) for c, d in L.items()})
