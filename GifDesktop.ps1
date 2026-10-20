# GifDesktop - يعرض لحد 10 صور GIF متحركة على سطح المكتب، كل وحدة بمكانها وحجمها
# سحب بالماوس = تحريك | عجلة الماوس = تكبير/تصغير (مع Ctrl خطوة صغيرة) | زر يمين = القائمة

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win32 {
    [DllImport("user32.dll")] public static extern bool ReleaseCapture();
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    // يرجع النافذة فوق كل النوافذ بدون تغيير مكانها أو حجمها أو أخذ التركيز
    public static void KeepOnTop(IntPtr hWnd) { SetWindowPos(hWnd, new IntPtr(-1), 0, 0, 0, 0, 0x0013); }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern uint GetDpiForSystem();
}
"@
# بدون هذا ويندوز يكبّر النوافذ والقائمة كصورة فتطلع مغبشة على شاشات 125% وأكثر
[Win32]::SetProcessDPIAware() | Out-Null
$dpiScale = [Win32]::GetDpiForSystem() / 96.0
# الملفات المنزّلة من الإنترنت (مثلاً ZIP من GitHub) ويندوز يحظرها، وملفات DLL المحظورة ما تنفتح
try { Get-ChildItem $PSScriptRoot -Recurse -File | Unblock-File -ErrorAction Stop } catch {}
Add-Type -AssemblyName PresentationCore, WindowsBase, System.Xaml   # لقراءة WebP
$refs = [System.Windows.Forms.Form].Assembly.Location, [System.Drawing.Bitmap].Assembly.Location, [System.Windows.Media.Imaging.BitmapDecoder].Assembly.Location, [System.Windows.Threading.Dispatcher].Assembly.Location, [System.Xaml.XamlReader].Assembly.Location
$sources = (Join-Path $PSScriptRoot 'GifBox.cs'), (Join-Path $PSScriptRoot 'SvgTools.cs')
# ستيكرات تيليكرام (.tgs و .webm) تحتاج مجلد lib (WebView2). بدونه البرنامج يشتغل عادي بس بدونها
$libDir = Join-Path $PSScriptRoot 'lib'
$webView2 = (Join-Path $libDir 'Microsoft.Web.WebView2.Core.dll'), (Join-Path $libDir 'Microsoft.Web.WebView2.WinForms.dll')
$hasTelegram = -not ($webView2 | Where-Object { -not (Test-Path $_) })
if ($hasTelegram) {
    Add-Type -Path $webView2
    $refs += $webView2
    $sources += Join-Path $PSScriptRoot 'TelegramStickers.cs'
}
Add-Type -Path $sources -ReferencedAssemblies $refs
if ($hasTelegram) {
    $appData = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'GifDesktop'
    [TelegramStickers]::Register($libDir, (Join-Path $appData 'cache'), (Join-Path $appData 'WebView2'))
}
[System.Windows.Forms.Application]::EnableVisualStyles()

$MaxGifs      = 10
$settingsPath = Join-Path $PSScriptRoot 'settings.json'

# ---------- اللغة ----------
$Texts = @{
    ar = @{
        Running   = 'البرنامج شغّال بالفعل. اضغط زر يمين على أي GIF واختر "إضافة GIF".'
        PickTitle = 'اختر صور GIF (لحد {0})'
        Filter    = 'صور GIF (*.gif)|*.gif|كل الصور (*.gif;*.png;*.jpg;*.jpeg;*.bmp)|*.gif;*.png;*.jpg;*.jpeg;*.bmp'
        TooMany   = 'الحد الأقصى {0} صور. راح أضيف أول {1} بس.'
        Bounds    = 'المكان والحجم'
        X         = 'X (من اليسار)'
        Y         = 'Y (من الأعلى)'
        Width     = 'العرض'
        Height    = 'الارتفاع'
        KeepRatio = 'حافظ على النسبة'
        Apply     = 'تطبيق'
        Cancel    = 'إلغاء'
        Add       = 'إضافة GIF... ({0}/{1})'
        Change    = 'تغيير هذي الصورة...'
        SetBounds = 'تحديد المكان والحجم...'
        Original  = 'الحجم الأصلي'
        OnTop     = 'دائماً فوق النوافذ'
        Lock      = 'قفل المكان والحجم'
        RemoveBg  = 'إزالة الخلفية تلقائياً'
        Startup   = 'التشغيل مع ويندوز'
        Language  = 'English'
        Remove    = 'حذف هذي الصورة'
        AddSticker = 'إضافة ستيكر... ({0}/{1})'
        PickStickers = 'اختر ستيكرات (لحد {0})'
        StickerFilter = 'ستيكرات (*.webp;*.tgs;*.webm;*.png;*.svg;*.gif;*.jpg;*.jpeg;*.bmp)|*.webp;*.tgs;*.webm;*.png;*.svg;*.gif;*.jpg;*.jpeg;*.bmp'
        ToSvg     = 'تحويل صورة إلى SVG...'
        SvgPick   = 'اختر صورة ثابتة حتى تتحول إلى SVG'
        SvgSave   = 'وين تحفظ ملف الـ SVG؟'
        SvgDone   = 'تم التحويل وانحفظ هنا:{0}{1}{0}{0}تبي تضيفه كستيكر على الشاشة؟'
        SvgSaved  = 'تم التحويل وانحفظ هنا:{0}{1}'
        SvgFail   = 'ما قدرت أحوّل الصورة:{0}{1}'
        ImageFilter = 'صور (*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.tif;*.tiff'
        SvgFilter = 'ملف SVG (*.svg)|*.svg'
        LoadFail  = 'ما قدرت أفتح الملف:{0}{1}{0}{0}{2}'
        CloseAll  = 'إغلاق الكل'
    }
    en = @{
        Running   = 'GifDesktop is already running. Right-click any GIF and choose "Add GIF".'
        PickTitle = 'Choose GIF images (up to {0})'
        Filter    = 'GIF images (*.gif)|*.gif|All images (*.gif;*.png;*.jpg;*.jpeg;*.bmp)|*.gif;*.png;*.jpg;*.jpeg;*.bmp'
        TooMany   = 'The maximum is {0} images. Only the first {1} will be added.'
        Bounds    = 'Position and size'
        X         = 'X (from left)'
        Y         = 'Y (from top)'
        Width     = 'Width'
        Height    = 'Height'
        KeepRatio = 'Keep aspect ratio'
        Apply     = 'Apply'
        Cancel    = 'Cancel'
        Add       = 'Add GIF... ({0}/{1})'
        Change    = 'Change this image...'
        SetBounds = 'Set position and size...'
        Original  = 'Original size'
        OnTop     = 'Always on top'
        Lock      = 'Lock position and size'
        RemoveBg  = 'Remove background automatically'
        Startup   = 'Start with Windows'
        Language  = 'العربية'
        Remove    = 'Remove this image'
        AddSticker = 'Add sticker... ({0}/{1})'
        PickStickers = 'Choose stickers (up to {0})'
        StickerFilter = 'Stickers (*.webp;*.tgs;*.webm;*.png;*.svg;*.gif;*.jpg;*.jpeg;*.bmp)|*.webp;*.tgs;*.webm;*.png;*.svg;*.gif;*.jpg;*.jpeg;*.bmp'
        ToSvg     = 'Convert image to SVG...'
        SvgPick   = 'Choose a still image to convert to SVG'
        SvgSave   = 'Where should the SVG be saved?'
        SvgDone   = 'Converted and saved to:{0}{1}{0}{0}Add it to the screen as a sticker?'
        SvgSaved  = 'Converted and saved to:{0}{1}'
        SvgFail   = 'Could not convert the image:{0}{1}'
        ImageFilter = 'Images (*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.tif;*.tiff'
        SvgFilter = 'SVG file (*.svg)|*.svg'
        LoadFail  = 'Could not open the file:{0}{1}{0}{0}{2}'
        CloseAll  = 'Close all'
    }
}
$script:lang = 'ar'
try { if ((Get-Content $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json).Lang -eq 'en') { $script:lang = 'en' } } catch {}
function L([string]$key) { $Texts[$script:lang][$key] }
function Is-Rtl { $script:lang -eq 'ar' }

# نسخة وحدة بس من البرنامج تشتغل بنفس الوقت
$created = $false
$mutex = New-Object System.Threading.Mutex($true, 'Local\GifDesktop', [ref]$created)
if (-not $created) {
    [System.Windows.Forms.MessageBox]::Show((L Running), 'GifDesktop') | Out-Null
    exit
}

$launcherPath = Join-Path $PSScriptRoot 'GifDesktop.vbs'
$startupLink  = Join-Path ([Environment]::GetFolderPath('Startup')) 'GifDesktop.lnk'
$transparent  = [GifBox]::Key
$forms        = New-Object System.Collections.ArrayList

# ---------- الإعدادات ----------
# Kind: 'gif' أو 'sticker'، ولكل نوع حد $MaxGifs لحاله
function New-GifItem([string]$gif, [int]$x, [int]$y, [string]$kind = 'gif') {
    @{ Kind = $kind; Gif = $gif; X = $x; Y = $y; Width = 0; Height = 0; OnTop = $true; Locked = $false; RemoveBg = $true }
}

function Read-Settings {
    $items = @()
    if (-not (Test-Path $settingsPath)) { return $items }
    try { $loaded = Get-Content $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json } catch { return $items }
    # الملف القديم كان فيه GIF واحد بدون Items
    $raw = if ($null -ne $loaded.Items) { @($loaded.Items) } elseif ($loaded.Gif) { @($loaded) } else { @() }
    foreach ($r in $raw) {
        $it = New-GifItem '' 100 100
        foreach ($k in @($it.Keys)) { if ($null -ne $r.$k) { $it[$k] = $r.$k } }
        # الإعدادات القديمة انحفظت قبل دعم الدقة العالية، فنحوّلها لبكسلات حقيقية حتى يبقى نفس المكان والحجم
        if (-not $loaded.PhysicalPixels) {
            foreach ($k in 'X', 'Y', 'Width', 'Height') { $it[$k] = [int]($it[$k] * $dpiScale) }
        }
        if ($it.Gif -and (Test-Path $it.Gif)) { $items += $it }
    }
    return @($items | Where-Object { $_.Kind -eq 'gif' } | Select-Object -First $MaxGifs) +
           @($items | Where-Object { $_.Kind -eq 'sticker' } | Select-Object -First $MaxGifs)
}

function Save-Settings {
    $items = @(foreach ($f in $forms) {
        $t = $f.Tag
        [ordered]@{ Kind = $t.Kind; Gif = $t.Gif; X = $f.Left; Y = $f.Top; Width = $f.Width; Height = $f.Height; OnTop = $f.TopMost; Locked = [bool]$t.Locked; RemoveBg = [bool]$t.RemoveBg }
    })
    [ordered]@{ Lang = $script:lang; PhysicalPixels = $true; Items = $items } | ConvertTo-Json -Depth 4 | Set-Content $settingsPath -Encoding UTF8
}

function Count-Kind([string]$kind) { @($forms | Where-Object { $_.Tag.Kind -eq $kind }).Count }

function Select-Gifs([int]$max, [string]$kind = 'gif') {
    $d = New-Object System.Windows.Forms.OpenFileDialog
    $d.Title = (L $(if ($kind -eq 'sticker') { 'PickStickers' } else { 'PickTitle' })) -f $max
    $d.Multiselect = $true
    $d.Filter = L $(if ($kind -eq 'sticker') { 'StickerFilter' } else { 'Filter' })
    if ($d.ShowDialog() -ne 'OK') { return @() }
    $files = @($d.FileNames)
    if ($files.Count -gt $max) {
        [System.Windows.Forms.MessageBox]::Show(((L TooMany) -f $MaxGifs, $max), 'GifDesktop') | Out-Null
        $files = $files[0..($max - 1)]
    }
    return $files
}

# يضيف صور جديدة جنب الصورة اللي انضغط عليها، بحدود المتبقي من نوعها
function Add-Images($src, [string]$kind) {
    $files = Select-Gifs ($MaxGifs - (Count-Kind $kind)) $kind
    $i = 1
    foreach ($p in $files) {
        New-GifWindow (New-GifItem $p ($src.Left + 30 * $i) ($src.Top + 30 * $i) $kind)
        $i++
    }
    Save-Settings
}

function Convert-ToSvg($src) {
    $open = New-Object System.Windows.Forms.OpenFileDialog
    $open.Title = L SvgPick
    $open.Filter = L ImageFilter
    if ($open.ShowDialog() -ne 'OK') { return }

    $save = New-Object System.Windows.Forms.SaveFileDialog
    $save.Title = L SvgSave
    $save.Filter = L SvgFilter
    $save.InitialDirectory = Split-Path $open.FileName
    $save.FileName = [System.IO.Path]::GetFileNameWithoutExtension($open.FileName) + '.svg'
    if ($save.ShowDialog() -ne 'OK') { return }

    [System.Windows.Forms.Cursor]::Current = [System.Windows.Forms.Cursors]::WaitCursor
    try {
        [void][SvgTracer]::ConvertFile($open.FileName, $save.FileName, $true)
    } catch {
        $err = if ($_.Exception.InnerException) { $_.Exception.InnerException.Message } else { $_.Exception.Message }
        [System.Windows.Forms.MessageBox]::Show(((L SvgFail) -f [Environment]::NewLine, $err), 'GifDesktop') | Out-Null
        return
    } finally {
        [System.Windows.Forms.Cursor]::Current = [System.Windows.Forms.Cursors]::Default
    }

    if ((Count-Kind 'sticker') -ge $MaxGifs) {
        [System.Windows.Forms.MessageBox]::Show(((L SvgSaved) -f [Environment]::NewLine, $save.FileName), 'GifDesktop') | Out-Null
        return
    }
    $answer = [System.Windows.Forms.MessageBox]::Show(((L SvgDone) -f [Environment]::NewLine, $save.FileName), 'GifDesktop', 'YesNo')
    if ($answer -eq 'Yes') {
        New-GifWindow (New-GifItem $save.FileName ($src.Left + 30) ($src.Top + 30) 'sticker')
        Save-Settings
    }
}

# ---------- نوافذ الـ GIF ----------
# الحجم الأصلي بنفس شكله على الشاشة (مكبّر حسب دقة الشاشة مثل باقي ويندوز)
function Get-OriginalSize($box) {
    New-Object System.Drawing.Size([int]($box.ImageSize.Width * $dpiScale), [int]($box.ImageSize.Height * $dpiScale))
}

function Set-GifImage($f, [string]$path, [bool]$keepWidth) {
    $box = $f.Controls[0]
    $box.LoadImage($path, [bool]$f.Tag.RemoveBg)
    $f.Tag.Gif = $path
    $f.Tag.Ratio = $box.ImageSize.Width / $box.ImageSize.Height
    if ($keepWidth -and $f.Width -gt 10) {
        $f.Size = New-Object System.Drawing.Size($f.Width, [int]($f.Width / $f.Tag.Ratio))
    } else {
        $f.Size = Get-OriginalSize $box
    }
}

# مثل Set-GifImage، بس إذا الملف ما انفتح يطلع رسالة ويرجع false بدل ما يوقف البرنامج
function Try-SetGifImage($f, [string]$path, [bool]$keepWidth) {
    [System.Windows.Forms.Cursor]::Current = [System.Windows.Forms.Cursors]::WaitCursor
    try {
        Set-GifImage $f $path $keepWidth
        return $true
    } catch {
        $ex = $_.Exception
        while ($ex.InnerException) { $ex = $ex.InnerException }
        [System.Windows.Forms.MessageBox]::Show(((L LoadFail) -f [Environment]::NewLine, $path, $ex.Message), 'GifDesktop') | Out-Null
        return $false
    } finally {
        [System.Windows.Forms.Cursor]::Current = [System.Windows.Forms.Cursors]::Default
    }
}

function New-GifWindow($item) {
    $f = New-Object System.Windows.Forms.Form
    $f.Text = 'GifDesktop'
    $f.FormBorderStyle = 'None'
    $f.ShowInTaskbar = $false
    $f.StartPosition = 'Manual'
    $f.BackColor = $transparent
    $f.TransparencyKey = $transparent
    $f.MinimumSize = New-Object System.Drawing.Size(10, 10)
    $f.TopMost = [bool]$item.OnTop
    $f.Tag = @{ Kind = $item.Kind; Gif = ''; Ratio = 1.0; Locked = [bool]$item.Locked; RemoveBg = [bool]$item.RemoveBg }

    $pic = New-Object GifBox
    $pic.Dock = 'Fill'
    $pic.ContextMenuStrip = $menu
    $pic.Add_MouseDown($onMouseDown)
    $pic.Add_MouseWheel($onWheel)
    $f.Controls.Add($pic)

    if (-not (Try-SetGifImage $f $item.Gif $false)) { $f.Dispose(); return }
    if ($item.Width -gt 0 -and $item.Height -gt 0) {
        $f.Bounds = New-Object System.Drawing.Rectangle([int]$item.X, [int]$item.Y, [int]$item.Width, [int]$item.Height)
    } else {
        $f.Location = New-Object System.Drawing.Point([int]$item.X, [int]$item.Y)
    }

    $f.Add_FormClosed({
        [void]$forms.Remove($this)
        if ($forms.Count -eq 0) { [System.Windows.Forms.Application]::ExitThread() }
    })
    [void]$forms.Add($f)
    $f.Show()
}

# ---------- نافذة تحديد المكان والحجم ----------
function Show-BoundsDialog($f) {
    $dlg = New-Object System.Windows.Forms.Form
    $dlg.Text = L Bounds
    $dlg.AutoScaleDimensions = New-Object System.Drawing.SizeF(96, 96)
    $dlg.AutoScaleMode = 'Dpi'   # المقاسات تحت مكتوبة لشاشة 100%، وتتكبر حسب الشاشة
    $dlg.FormBorderStyle = 'FixedDialog'
    $dlg.MaximizeBox = $false; $dlg.MinimizeBox = $false
    $dlg.StartPosition = 'CenterScreen'
    $dlg.ClientSize = New-Object System.Drawing.Size(270, 215)
    if (Is-Rtl) { $dlg.RightToLeft = 'Yes'; $dlg.RightToLeftLayout = $true }
    $dlg.TopMost = $true

    $labels = (L X), (L Y), (L Width), (L Height)
    $values = $f.Left, $f.Top, $f.Width, $f.Height
    $nums = @()
    for ($i = 0; $i -lt 4; $i++) {
        $l = New-Object System.Windows.Forms.Label
        $l.Text = $labels[$i]; $l.AutoSize = $true
        $l.Location = New-Object System.Drawing.Point(15, (16 + $i * 32))
        $n = New-Object System.Windows.Forms.NumericUpDown
        $n.Minimum = $(if ($i -lt 2) { -20000 } else { 10 })
        $n.Maximum = 20000
        $n.Value = $values[$i]
        $n.Location = New-Object System.Drawing.Point(140, (12 + $i * 32))
        $n.Width = 110
        $dlg.Controls.AddRange(@($l, $n))
        $nums += $n
    }

    $keep = New-Object System.Windows.Forms.CheckBox
    $keep.Text = L KeepRatio; $keep.Checked = $true; $keep.AutoSize = $true
    $keep.Location = New-Object System.Drawing.Point(15, 142)
    $dlg.Controls.Add($keep)

    $ratio = $f.Tag.Ratio
    $st = @{ busy = $false }
    $nums[2].Add_ValueChanged({
        if ($keep.Checked -and -not $st.busy) {
            $st.busy = $true; $nums[3].Value = [Math]::Max(10, [int]($nums[2].Value / $ratio)); $st.busy = $false
        }
    }.GetNewClosure())
    $nums[3].Add_ValueChanged({
        if ($keep.Checked -and -not $st.busy) {
            $st.busy = $true; $nums[2].Value = [Math]::Max(10, [int]($nums[3].Value * $ratio)); $st.busy = $false
        }
    }.GetNewClosure())

    $ok = New-Object System.Windows.Forms.Button
    $ok.Text = L Apply; $ok.DialogResult = 'OK'
    $ok.Location = New-Object System.Drawing.Point(15, 175)
    $cancel = New-Object System.Windows.Forms.Button
    $cancel.Text = L Cancel; $cancel.DialogResult = 'Cancel'
    $cancel.Location = New-Object System.Drawing.Point(100, 175)
    $dlg.Controls.AddRange(@($ok, $cancel))
    $dlg.AcceptButton = $ok; $dlg.CancelButton = $cancel

    if ($dlg.ShowDialog() -eq 'OK') {
        $f.Bounds = New-Object System.Drawing.Rectangle([int]$nums[0].Value, [int]$nums[1].Value, [int]$nums[2].Value, [int]$nums[3].Value)
        Save-Settings
    }
    $dlg.Dispose()
}

# ---------- التحريك والتكبير ----------
$onMouseDown = {
    param($src, $e)
    $f = $this.FindForm()
    if ($e.Button -eq 'Left' -and -not $f.Tag.Locked) {
        [Win32]::ReleaseCapture() | Out-Null
        [Win32]::SendMessage($f.Handle, 0xA1, 2, 0) | Out-Null   # WM_NCLBUTTONDOWN / HTCAPTION
        Save-Settings
    }
}

$onWheel = {
    param($src, $e)
    $f = $this.FindForm()
    if ($f.Tag.Locked) { return }
    $step = if ([System.Windows.Forms.Control]::ModifierKeys -band [System.Windows.Forms.Keys]::Control) { 2 } else { 15 }
    $w = [Math]::Max(10, $f.Width + [Math]::Sign($e.Delta) * $step)
    $h = [Math]::Max(10, [int]($w / $f.Tag.Ratio))
    # التكبير من المنتصف
    $cx = $f.Left + $f.Width / 2; $cy = $f.Top + $f.Height / 2
    $f.Bounds = New-Object System.Drawing.Rectangle([int]($cx - $w / 2), [int]($cy - $h / 2), $w, $h)
    Save-Settings
}

# ---------- قائمة الزر الأيمن (وحدة مشتركة، تشتغل على الـ GIF اللي ضغطت عليه) ----------
$menu = New-Object System.Windows.Forms.ContextMenuStrip
$menu.RightToLeft = if (Is-Rtl) { 'Yes' } else { 'No' }
$menu.Renderer = New-Object System.Windows.Forms.ToolStripProfessionalRenderer((New-Object MenuColors))
$menu.Font = New-Object System.Drawing.Font('Segoe UI', 10)
$menu.ShowImageMargin = $false
$menu.ShowCheckMargin = $true
function Get-MenuForm { $menu.SourceControl.FindForm() }
# الـ GIF نفسه "فوق النوافذ"، فلازم نرفع القائمة فوقه وإلا يغطيها
$menu.Add_Opened({ [Win32]::KeepOnTop($menu.Handle) })

$miAdd = $menu.Items.Add('')
$miAdd.Add_Click({ Add-Images (Get-MenuForm) 'gif' })

$miAddSticker = $menu.Items.Add('')
$miAddSticker.Add_Click({ Add-Images (Get-MenuForm) 'sticker' })

# تحويل صورة ثابتة إلى SVG: يبقى حاد مهما كبّرته أو صغّرته
$miSvg = $menu.Items.Add((L ToSvg))
$miSvg.Add_Click({ Convert-ToSvg (Get-MenuForm) })

$miPick = $menu.Items.Add((L Change))
$miPick.Add_Click({
    $f = Get-MenuForm
    $p = @(Select-Gifs 1 $f.Tag.Kind)
    if ($p.Count -gt 0 -and (Try-SetGifImage $f $p[0] $true)) { Save-Settings }
})

$miBounds = $menu.Items.Add((L SetBounds))
$miBounds.Add_Click({ Show-BoundsDialog (Get-MenuForm) })

$miOrig = $menu.Items.Add((L Original))
$miOrig.Add_Click({ $f = Get-MenuForm; $f.Size = Get-OriginalSize $f.Controls[0]; Save-Settings })

[void]$menu.Items.Add('-')

$miTop = New-Object System.Windows.Forms.ToolStripMenuItem((L OnTop))
$miTop.CheckOnClick = $true
$miTop.Add_Click({ (Get-MenuForm).TopMost = $miTop.Checked; Save-Settings })
[void]$menu.Items.Add($miTop)

$miLock = New-Object System.Windows.Forms.ToolStripMenuItem((L Lock))
$miLock.CheckOnClick = $true
$miLock.Add_Click({ (Get-MenuForm).Tag.Locked = $miLock.Checked; Save-Settings })
[void]$menu.Items.Add($miLock)

$miBg = New-Object System.Windows.Forms.ToolStripMenuItem((L RemoveBg))
$miBg.CheckOnClick = $true
$miBg.Add_Click({
    $f = Get-MenuForm
    $f.Tag.RemoveBg = $miBg.Checked
    Set-GifImage $f $f.Tag.Gif $true
    Save-Settings
})
[void]$menu.Items.Add($miBg)

$miStartup = New-Object System.Windows.Forms.ToolStripMenuItem((L Startup))
$miStartup.CheckOnClick = $true
$miStartup.Add_Click({
    if ($miStartup.Checked) {
        $sh = New-Object -ComObject WScript.Shell
        $lnk = $sh.CreateShortcut($startupLink)
        $lnk.TargetPath = 'wscript.exe'
        $lnk.Arguments = '"' + $launcherPath + '"'
        $lnk.WorkingDirectory = $PSScriptRoot
        $lnk.Save()
    } elseif (Test-Path $startupLink) {
        Remove-Item $startupLink -Force
    }
})
[void]$menu.Items.Add($miStartup)

# يبدّل بين العربي والإنكليزي ويحدّث نصوص القائمة
function Update-MenuTexts {
    $menu.RightToLeft = if (Is-Rtl) { 'Yes' } else { 'No' }
    $miPick.Text = L Change; $miBounds.Text = L SetBounds; $miOrig.Text = L Original
    $miTop.Text = L OnTop; $miLock.Text = L Lock; $miBg.Text = L RemoveBg; $miStartup.Text = L Startup
    $miSvg.Text = L ToSvg
    $miLang.Text = L Language; $miRemove.Text = L Remove; $miExit.Text = L CloseAll
}
$miLang = $menu.Items.Add((L Language))
$miLang.Add_Click({
    $script:lang = if (Is-Rtl) { 'en' } else { 'ar' }
    Update-MenuTexts
    Save-Settings
})

[void]$menu.Items.Add('-')

$miRemove = $menu.Items.Add((L Remove))
$miRemove.Add_Click({
    $f = Get-MenuForm
    [void]$forms.Remove($f)
    if ($forms.Count -gt 0) { Save-Settings }   # آخر واحد: نخلي الإعدادات حتى يرجع المرة الجاية
    $f.Close()
})

$miExit = $menu.Items.Add((L CloseAll))
$miExit.Add_Click({
    Save-Settings
    foreach ($f in @($forms)) { $f.Close() }
})

# قبل ما تنفتح القائمة: حدّث العلامات حسب الـ GIF اللي تحت الماوس
$menu.Add_Opening({
    $f = Get-MenuForm
    $miTop.Checked = $f.TopMost
    $miLock.Checked = [bool]$f.Tag.Locked
    $miBg.Checked = [bool]$f.Tag.RemoveBg
    $miStartup.Checked = Test-Path $startupLink
    $gifs = Count-Kind 'gif'; $stickers = Count-Kind 'sticker'
    $miAdd.Text = (L Add) -f $gifs, $MaxGifs
    $miAdd.Enabled = $gifs -lt $MaxGifs
    $miAddSticker.Text = (L AddSticker) -f $stickers, $MaxGifs
    $miAddSticker.Enabled = $stickers -lt $MaxGifs
})

# مسافة أكبر بين عناصر القائمة
foreach ($mi in $menu.Items) { if ($mi -is [System.Windows.Forms.ToolStripMenuItem]) { $mi.Padding = New-Object System.Windows.Forms.Padding(4, 5, 4, 5) } }

# ---------- التشغيل ----------
$items = @(Read-Settings)
if ($items.Count -eq 0) {
    $i = 0
    $items = @(foreach ($p in (Select-Gifs $MaxGifs)) { New-GifItem $p (100 + 30 * $i) (100 + 30 * $i); $i++ })
    if ($items.Count -eq 0) { exit }
}
foreach ($it in $items) { New-GifWindow $it }
if ($forms.Count -eq 0) { exit }   # ولا صورة انفتحت: نطلع بدون ما نمسح الإعدادات
Save-Settings

# بعض البرامج (مثل الشاشة الكاملة أو شريط المهام) تغطي النوافذ، فنرجعها فوق كل ثانيتين
$topTimer = New-Object System.Windows.Forms.Timer
$topTimer.Interval = 2000
$topTimer.Add_Tick({
    if ($menu.Visible) { return }
    foreach ($f in $forms) { if ($f.TopMost) { [Win32]::KeepOnTop($f.Handle) } }
})
$topTimer.Start()

[System.Windows.Forms.Application]::Run()
$mutex.ReleaseMutex()
