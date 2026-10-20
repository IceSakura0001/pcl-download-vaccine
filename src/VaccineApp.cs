using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using PclVaccine;

namespace PclVaccine
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string initial = null;
            if (args != null && args.Length > 0)
            {
                string candidate = args[0].Trim().Trim('"');
                if (File.Exists(candidate) || Directory.Exists(candidate)) initial = candidate;
            }

            Application.Run(new MainForm(initial));
        }
    }

    sealed class MainForm : Form
    {
        const string AppTitle = "PCL下载修复疫苗";
        const int WM_DROPFILES = 0x0233;

        [DllImport("shell32.dll")] static extern void DragAcceptFiles(IntPtr hWnd, bool accept);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder name, uint cch);
        [DllImport("shell32.dll")] static extern void DragFinish(IntPtr hDrop);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        readonly string initialPath;
        readonly float uiScale;
        DeltaPatch patch;
        string patchError;
        bool busy;

        DropZone dropZone;
        Button chooseButton;
        Label statusLabel;
        TextBox logBox;
        Label hintLabel;
        ProgressBar progress;
        Font fontMain;
        Font fontSub;
        Font fontSmall;

        internal float UiScale { get { return uiScale; } }
        internal Color VisualBlue { get { return Blue; } }
        internal Font VisualFontMain { get { return fontMain; } }
        internal Font VisualFontSub { get { return fontSub; } }
        internal Font VisualFontSmall { get { return fontSmall; } }

        static readonly Color Blue = Color.FromArgb(31, 95, 214);
        static readonly Color BlueDark = Color.FromArgb(23, 74, 173);
        static readonly Color BlueLight = Color.FromArgb(245, 248, 255);
        static readonly Color TextGray = Color.FromArgb(107, 124, 153);
        static readonly Color Success = Color.FromArgb(26, 127, 55);
        static readonly Color Failure = Color.FromArgb(198, 40, 40);
        static readonly Color InfoBlue = Color.FromArgb(31, 95, 214);

        public MainForm(string initial)
        {
            initialPath = initial;
            uiScale = DetectScale();

            Text = AppTitle;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            fontMain = new Font("Microsoft YaHei UI", 13.5F, FontStyle.Bold, GraphicsUnit.Point);
            fontSub = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
            fontSmall = new Font("Microsoft YaHei UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point);
            ClientSize = new Size(Px(560), Px(540));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            BuildUi();
            AllowDrop = true;
            dropZone.AllowDrop = true;
            DragEnter += OnDragEnterAny;
            DragOver += OnDragEnterAny;
            DragDrop += OnDragDropAny;
            DragLeave += OnDragLeaveAny;
            dropZone.DragEnter += OnDragEnterAny;
            dropZone.DragOver += OnDragEnterAny;
            dropZone.DragDrop += OnDragDropAny;
            dropZone.DragLeave += OnDragLeaveAny;

            Shown += OnShown;
            LoadPatchAsync();
        }

        float DetectScale()
        {
            try
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero)) return Math.Max(1f, g.DpiX / 96f);
            }
            catch { return 1f; }
        }

        int Px(float value) { return (int)Math.Round(value * uiScale); }

        void BuildUi()
        {
            var header = new Panel();
            header.Bounds = new Rectangle(0, 0, ClientSize.Width, Px(96));
            header.BackColor = Blue;
            Controls.Add(header);

            var accent = new Panel();
            accent.Bounds = new Rectangle(0, header.Bottom - Px(3), ClientSize.Width, Px(3));
            accent.BackColor = BlueDark;
            header.Controls.Add(accent);

            var title = new Label();
            title.Text = "PCL 下载修复疫苗";
            title.ForeColor = Color.White;
            title.Font = new Font("Microsoft YaHei UI", 19F, FontStyle.Bold, GraphicsUnit.Point);
            title.AutoSize = true;
            title.Location = new Point(Px(24), Px(16));
            header.Controls.Add(title);

            var subtitle = new Label();
            subtitle.Text = "本地恢复官方 PCL 2.99.0.0 的 Minecraft 下载入口，不联网、不上传";
            subtitle.ForeColor = Color.FromArgb(214, 228, 255);
            subtitle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            subtitle.AutoSize = true;
            subtitle.Location = new Point(Px(26), Px(64));
            header.Controls.Add(subtitle);

            dropZone = new DropZone(this);
            dropZone.Bounds = new Rectangle(Px(20), Px(116), ClientSize.Width - Px(40), Px(160));
            dropZone.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            dropZone.Cursor = Cursors.Hand;
            dropZone.Click += delegate { ChooseFile(); };
            Controls.Add(dropZone);

            chooseButton = new Button();
            chooseButton.Text = "选择 PCL 启动器…";
            chooseButton.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
            chooseButton.Bounds = new Rectangle((ClientSize.Width - Px(170)) / 2, Px(286), Px(170), Px(36));
            chooseButton.FlatStyle = FlatStyle.Flat;
            chooseButton.FlatAppearance.BorderSize = 0;
            chooseButton.BackColor = Blue;
            chooseButton.ForeColor = Color.White;
            chooseButton.Cursor = Cursors.Hand;
            chooseButton.Click += delegate { ChooseFile(); };
            Controls.Add(chooseButton);

            statusLabel = new Label();
            statusLabel.Bounds = new Rectangle(Px(20), Px(332), ClientSize.Width - Px(40), Px(56));
            statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            statusLabel.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold, GraphicsUnit.Point);
            statusLabel.ForeColor = TextGray;
            statusLabel.Text = "等待拖入 PCL 启动器…";
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(statusLabel);

            logBox = new TextBox();
            logBox.Bounds = new Rectangle(Px(20), Px(394), ClientSize.Width - Px(40), Px(100));
            logBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.BorderStyle = BorderStyle.FixedSingle;
            logBox.BackColor = Color.FromArgb(250, 251, 253);
            logBox.ForeColor = Color.FromArgb(72, 84, 104);
            logBox.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
            logBox.Text = "准备就绪。\r\n";
            Controls.Add(logBox);

            hintLabel = new Label();
            hintLabel.Bounds = new Rectangle(Px(20), Px(504), ClientSize.Width - Px(40), Px(24));
            hintLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            hintLabel.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular, GraphicsUnit.Point);
            hintLabel.ForeColor = TextGray;
            hintLabel.Text = "请先完全关闭 PCL · 仅支持官方 2.99.0.0 正式版 · 修改前自动备份 · 全程本地处理";
            hintLabel.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(hintLabel);

            progress = new ProgressBar();
            progress.Bounds = new Rectangle(Px(20), Px(394) - Px(8), ClientSize.Width - Px(40), Px(4));
            progress.Style = ProgressBarStyle.Marquee;
            progress.MarqueeAnimationSpeed = 25;
            progress.Visible = false;
            Controls.Add(progress);

            DragAcceptFiles(Handle, true);
            dropZone.HandleCreated += delegate { DragAcceptFiles(dropZone.Handle, true); };
        }

        void ChooseFile()
        {
            if (busy) return;
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "选择 PCL 启动器（Plain Craft Launcher 2.exe）";
                dialog.Filter = "PCL 启动器 (*.exe)|*.exe|所有文件 (*.*)|*.*";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) == DialogResult.OK) HandlePath(dialog.FileName);
            }
        }

        void OnShown(object sender, EventArgs e)
        {
            if (initialPath != null)
            {
                string p = initialPath;
                BeginInvoke((Action)delegate { HandlePath(p); });
            }
        }

        void LoadPatchAsync()
        {
            statusLabel.Text = "正在准备补丁数据…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    var loaded = VaccineCore.LoadEmbeddedPatch();
                    Post(delegate
                    {
                        patch = loaded;
                        statusLabel.ForeColor = TextGray;
                        statusLabel.Text = "等待拖入 PCL 启动器…";
                        AppendLog("补丁数据已就绪（原始 SHA256 " + DeltaPatch.Hex(loaded.SourceHash).Substring(0, 8)
                            + "… → 修复后 " + loaded.OutputHashHex.Substring(0, 8) + "…）。");
                    });
                }
                catch (Exception ex)
                {
                    Post(delegate
                    {
                        patchError = ex.Message;
                        SetStatus("程序内部错误：补丁数据不可用", Failure);
                        AppendLog("无法载入内置补丁数据：" + ex.Message);
                        AppendLog("请重新获取完整的 " + AppTitle + ".exe。");
                    });
                }
            });
        }

        void HandlePath(string path)
        {
            if (busy) return;
            if (!string.IsNullOrEmpty(patchError)) { SetStatus("补丁数据不可用，无法处理", Failure); return; }
            if (patch == null) { SetStatus("正在准备补丁数据，请稍候…", TextGray); return; }

            if (Directory.Exists(path))
            {
                SetStatus("拖入的是文件夹", Failure);
                AppendLog("请把 PCL 启动器 .exe 文件本身拖进来，而不是文件夹。");
                return;
            }

            busy = true;
            SetBusyUi(true);
            ClearLog();
            AppendLog("开始处理：" + path);
            SetStatus("正在处理，请稍候…", InfoBlue);

            var delta = patch;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    var outcome = VaccineCore.PatchFile(path, delta, delegate(string message) { PostLog(message); });
                    Post(delegate { OnCompleted(outcome); });
                }
                catch (VaccineException ex)
                {
                    Post(delegate { OnFailed(ex.Message); });
                }
                catch (Exception ex)
                {
                    Post(delegate { OnFailed("处理时发生意外错误：" + ex.Message); });
                }
            });
        }

        void OnCompleted(PatchOutcome outcome)
        {
            busy = false;
            SetBusyUi(false);

            if (outcome.AlreadyPatched)
            {
                SetStatus("已修复，无需重复处理", InfoBlue);
                AppendLog("该文件已经是修复版，本次未做任何更改。");
                AppendLog("文件：" + outcome.TargetPath);
            }
            else
            {
                SetStatus("已完成，下载入口已恢复", Success);
                AppendLog("修复成功，最终文件 SHA256 校验通过。");
                AppendLog("文件：" + outcome.TargetPath);
                if (!string.IsNullOrEmpty(outcome.BackupPath)) AppendLog("备份：" + outcome.BackupPath);
                AppendLog("现在可以启动 PCL，在「下载」页选择游戏版本下载。");
            }
            dropZone.ResetVisual();
        }

        void OnFailed(string message)
        {
            busy = false;
            SetBusyUi(false);
            string firstLine = message;
            int nl = firstLine.IndexOf('\n');
            if (nl > 0) firstLine = firstLine.Substring(0, nl);
            SetStatus("未完成：" + firstLine.Trim(), Failure);
            AppendLog("处理失败，原文件保持原样。");
            foreach (var line in message.Replace("\r", "").Split('\n'))
                if (line.Trim().Length > 0) AppendLog(line);
            dropZone.ResetVisual();
        }

        void SetBusyUi(bool working)
        {
            chooseButton.Enabled = !working;
            statusLabel.ForeColor = working ? InfoBlue : statusLabel.ForeColor;
            progress.Visible = working;
            dropZone.Busy = working;
        }

        void SetStatus(string text, Color color)
        {
            statusLabel.ForeColor = color;
            statusLabel.Text = text;
        }

        void ClearLog()
        {
            logBox.Text = "";
        }

        void AppendLog(string line)
        {
            logBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + line + "\r\n");
        }

        void PostLog(string line)
        {
            Post(delegate { AppendLog(line); });
        }

        void Post(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(action); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        // ---------- drag & drop (OLE + WM_DROPFILES) ----------

        void OnDragEnterAny(object sender, DragEventArgs e)
        {
            if (busy || !e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effect = DragDropEffects.None; return; }
            e.Effect = DragDropEffects.Copy;
            if (sender == dropZone) dropZone.Highlight = true;
        }

        void OnDragLeaveAny(object sender, EventArgs e)
        {
            if (sender == dropZone) dropZone.Highlight = false;
        }

        void OnDragDropAny(object sender, DragEventArgs e)
        {
            if (sender == dropZone) dropZone.Highlight = false;
            if (busy) { SetStatus("正在处理上一个文件，请稍候…", InfoBlue); return; }
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            DispatchDroppedFiles(files);
        }

        internal void DispatchDroppedFiles(string[] files)
        {
            MainForm.Diag("DispatchDroppedFiles length=" + (files == null ? -1 : files.Length));
            if (files == null || files.Length == 0) { SetStatus("没有识别到文件", Failure); return; }
            if (files.Length > 1)
            {
                SetStatus("一次只能处理一个文件", Failure);
                AppendLog("收到 " + files.Length.ToString() + " 个文件，请只拖入 PCL 启动器 .exe 一个文件。");
                return;
            }
            dropZone.ResetVisual();
            HandlePath(files[0]);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int light = 0;
                DwmSetWindowAttribute(Handle, 20, ref light, 4); // DWMWA_USE_IMMERSIVE_DARK_MODE = FALSE (light title bar)
                int rounded = 2;
                DwmSetWindowAttribute(Handle, 33, ref rounded, 4); // DWMWA_WINDOW_CORNER_PREFERENCE = ROUND
            }
            catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DROPFILES)
            {
                var files = ReadDroppedFiles(m.WParam);
                Diag("form WM_DROPFILES hdrop=0x" + m.WParam.ToInt64().ToString("X") + " count=" + files.Length);
                DragFinish(m.WParam);
                if (!busy) DispatchDroppedFiles(files);
                return;
            }
            base.WndProc(ref m);
        }

        internal static void Diag(string message)
        {
            try
            {
                if (Environment.GetEnvironmentVariable("PCLVACCINE_DEBUG") != "1") return;
                string path = Path.Combine(Path.GetTempPath(), "pcl-vaccine-debug.log");
                File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + "\r\n");
            }
            catch { }
        }

        static string[] ReadDroppedFiles(IntPtr hDrop)
        {
            uint count = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
            var result = new string[count];
            for (uint i = 0; i < count; i++)
            {
                uint length = DragQueryFile(hDrop, i, null, 0);
                var sb = new StringBuilder((int)length + 1);
                DragQueryFile(hDrop, i, sb, (uint)sb.Capacity);
                result[i] = sb.ToString();
            }
            return result;
        }
    }

    sealed class DropZone : Panel
    {
        readonly MainForm owner;
        bool highlight;
        bool hover;

        public DropZone(MainForm owner)
        {
            this.owner = owner;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
        }

        public bool Highlight
        {
            get { return highlight; }
            set { if (highlight != value) { highlight = value; Invalidate(); } }
        }

        public bool Busy { get; set; }

        public void ResetVisual()
        {
            highlight = false;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        int Px(float v) { return (int)Math.Round(v * (owner == null ? 1f : owner.UiScale)); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color fill;
            Color border;
            if (Busy) { fill = Color.FromArgb(243, 244, 247); border = Color.FromArgb(190, 197, 209); }
            else if (highlight) { fill = Color.FromArgb(232, 241, 255); border = owner.VisualBlue; }
            else if (hover) { fill = Color.FromArgb(240, 246, 255); border = Color.FromArgb(120, 160, 226); }
            else { fill = Color.FromArgb(245, 248, 255); border = Color.FromArgb(157, 187, 239); }

            var outer = new Rectangle(1, 1, Width - 3, Height - 3);
            using (var path = RoundedRect(outer, Px(12)))
            {
                using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
                using (var pen = new Pen(border, Math.Max(1, Px(1.5f))))
                {
                    pen.DashStyle = DashStyle.Dash;
                    pen.DashPattern = new float[] { 5f, 3f };
                    g.DrawPath(pen, path);
                }
            }

            int cx = Width / 2;
            int iconY = Px(34);
            int radius = Px(17);
            using (var brush = new SolidBrush(Busy ? Color.FromArgb(150, 158, 172) : owner.VisualBlue))
                g.FillEllipse(brush, cx - radius, iconY - radius, radius * 2, radius * 2);
            using (var pen = new Pen(Color.White, Math.Max(2f, Px(2f))))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, cx, iconY - Px(7), cx, iconY + Px(4));
                g.DrawLine(pen, cx - Px(6), iconY - Px(1), cx, iconY + Px(5));
                g.DrawLine(pen, cx + Px(6), iconY - Px(1), cx, iconY + Px(5));
                g.DrawLine(pen, cx - Px(8), iconY + Px(9), cx + Px(8), iconY + Px(9));
            }

            string main = Busy ? "正在处理…" : "把 PCL 启动器 .exe 拖到这里";
            TextRenderer.DrawText(g, main, owner.VisualFontMain,
                new Rectangle(0, Px(62), Width, Px(34)), Busy ? Color.FromArgb(120, 128, 142) : owner.VisualBlue,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            TextRenderer.DrawText(g, "或点击此处选择文件 · 修复前会自动备份原文件", owner.VisualFontSub,
                new Rectangle(0, Px(98), Width, Px(26)), Color.FromArgb(122, 138, 168),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            TextRenderer.DrawText(g, "支持：官方 PCL 2.99.0.0 正式版（其他版本会被拒绝）", owner.VisualFontSmall,
                new Rectangle(0, Px(126), Width, Px(22)), Color.FromArgb(154, 167, 191),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0233)
            {
                uint count = RawDragQueryFile(m.WParam, 0xFFFFFFFF, null, 0);
                var files = new string[count];
                for (uint i = 0; i < count; i++)
                {
                    uint length = RawDragQueryFile(m.WParam, i, null, 0);
                    var sb = new StringBuilder((int)length + 1);
                    RawDragQueryFile(m.WParam, i, sb, (uint)sb.Capacity);
                    files[i] = sb.ToString();
                }
                RawDragFinish(m.WParam);
                MainForm.Diag("dropzone WM_DROPFILES count=" + files.Length);
                owner.DispatchDroppedFiles(files);
                return;
            }
            base.WndProc(ref m);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint RawDragQueryFile(IntPtr hDrop, uint iFile, StringBuilder name, uint cch);
        [DllImport("shell32.dll")] static extern void RawDragFinish(IntPtr hDrop);
    }
}
