using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Test-only helper: a drag source used to exercise the vaccine's real OLE drag-and-drop path.
// Not part of the delivered product.
class DragSource
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main(string[] args)
    {
        try { SetProcessDPIAware(); } catch { }
        Application.EnableVisualStyles();
        var files = new string[args.Length];
        Array.Copy(args, files, args.Length);
        string geombase = Path.Combine(Path.GetTempPath(), "dragsource");
        try { File.Delete(geombase + ".log"); } catch { }
        try { File.Delete(geombase + ".geom"); } catch { }

        var form = new Form();
        form.Text = "DragSource";
        form.FormBorderStyle = FormBorderStyle.FixedToolWindow;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(80, 240);
        form.TopMost = true;
        form.Size = new Size(360, 200);

        var label = new Label();
        label.Dock = DockStyle.Fill;
        label.TextAlign = ContentAlignment.MiddleCenter;
        label.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
        label.Cursor = Cursors.Hand;
        label.Text = "按住这里拖到疫苗窗口\r\n\r\n" + (files.Length == 1 ? Path.GetFileName(files[0]) : (files.Length + " 个文件"));
        label.MouseDown += delegate
        {
            if (files.Length == 0) return;
            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, files);
            var effect = form.DoDragDrop(data, DragDropEffects.Copy);
            try
            {
                File.AppendAllText(geombase + ".log",
                    DateTime.Now.ToString("HH:mm:ss.fff") + " drag finished effect=" + effect + "\r\n");
            }
            catch { }
        };
        form.Controls.Add(label);
        form.Shown += delegate
        {
            try
            {
                var center = label.PointToScreen(new Point(label.Width / 2, label.Height / 2));
                File.WriteAllText(geombase + ".geom", center.X + "," + center.Y + "\r\n" + label.Width + "x" + label.Height);
            }
            catch { }
        };
        Application.Run(form);
    }
}
