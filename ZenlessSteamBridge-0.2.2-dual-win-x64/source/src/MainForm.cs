using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZenlessSteamBridge
{
    internal sealed class MainForm : Form
    {
        private readonly Color ink = Color.FromArgb(236, 238, 231), accent = Color.FromArgb(210, 242, 78), muted = Color.FromArgb(165, 171, 164);
        private Label account, client, starRailStatus, installed, status;
        private TextBox game, starRail;
        private Button enable, restore, refresh, choose, chooseStarRail, preview;
        private DetectionResult current;
        private bool busy, previewRendering;
        public MainForm()
        {
            SuspendLayout();
            DoubleBuffered = true;
            Text = "Zenless Steam Bridge · 绝区零 + 崩坏：星穹铁道";
            Icon = SystemIcons.Application;
            Font = new Font("Microsoft YaHei UI", 9.5f);
            ForeColor = ink; BackColor = Color.FromArgb(24, 27, 25);
            StartPosition = FormStartPosition.CenterScreen;
            var viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var layout = Rows(8); layout.Dock = DockStyle.Top; layout.Padding = new Padding(28, 22, 28, 20);
            viewport.Controls.Add(layout); Controls.Add(viewport);
            var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.Controls.Add(Label("ZENLESS + STAR RAIL  /  STEAM BRIDGE", 9, accent), 0, 0);
            header.Controls.Add(Label("v" + App.Version, 9, accent), 1, 0); layout.Controls.Add(header, 0, 0);
            var title = Label("绝区零 / 星穹铁道", 24, ink); title.Margin = new Padding(0, 8, 0, 6); layout.Controls.Add(title, 0, 1);
            var introduction = Label("从 Steam《绝区零》进入卡片界面，选择要游玩的游戏。两款游戏的 Steam 时长均记录在绝区零名下。", 10, muted);
            introduction.Margin = new Padding(0, 0, 0, 16); layout.Controls.Add(introduction, 0, 2);
            var card = Rows(10); card.BackColor = Color.FromArgb(37, 42, 38); card.Padding = new Padding(18, 12, 18, 16);
            layout.Controls.Add(card, 0, 3);
            card.Controls.Add(Label("01   STEAM 账号", 9, muted), 0, 0);
            account = Label("正在检测…", 11, ink); account.Margin = new Padding(0, 2, 0, 12); card.Controls.Add(account, 0, 1);
            card.Controls.Add(Label("02   绝区零 · ZenlessZoneZero.exe", 9, muted), 0, 2);
            card.Controls.Add(PathRow(out game, out choose), 0, 3);
            client = Label("正在查找绝区零客户端…", 9, muted); client.Margin = new Padding(0, 0, 0, 12); card.Controls.Add(client, 0, 4);
            card.Controls.Add(Label("03   崩坏：星穹铁道 · StarRail.exe", 9, muted), 0, 5);
            card.Controls.Add(PathRow(out starRail, out chooseStarRail), 0, 6);
            starRailStatus = Label("正在查找星穹铁道客户端…", 9, muted); starRailStatus.Margin = new Padding(0, 0, 0, 12); card.Controls.Add(starRailStatus, 0, 7);
            card.Controls.Add(Label("04   启动入口", 9, muted), 0, 8);
            installed = Label("尚未载入", 10, ink); installed.Margin = new Padding(0, 4, 0, 0); card.Controls.Add(installed, 0, 9);
            choose.Click += async delegate { await Choose(GameTarget.Zenless, game); };
            chooseStarRail.Click += async delegate { await Choose(GameTarget.StarRail, starRail); };
            var actions = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 16, 0, 10) };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            actions.RowStyles.Add(new RowStyle(SizeType.AutoSize)); actions.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            enable = Button("载入双游戏", true); restore = Button("撤回载入", false); refresh = Button("重新检测", false); preview = Button("预览选择界面", false);
            actions.Controls.Add(enable, 0, 0); actions.Controls.Add(restore, 1, 0); actions.Controls.Add(refresh, 0, 1); actions.Controls.Add(preview, 1, 1);
            layout.Controls.Add(actions, 0, 4);
            enable.Click += async delegate { await Mutate(false); }; restore.Click += async delegate { await Mutate(true); }; refresh.Click += async delegate { await RefreshDetection(); };
            preview.Click += delegate
            {
                using (var picker = new GameSelectionForm(new RuntimeConfig { Game = game.Text, StarRailGame = starRail.Text })) picker.ShowDialog(this);
                status.Text = "这是选择界面预览。正式游玩请从 Steam《绝区零》开始游戏。";
            };
            status = Label("至少设置一款已安装的游戏。首次使用请先将《绝区零》添加到 Steam 库。", 9, muted);
            status.Margin = new Padding(0, 0, 0, 12); layout.Controls.Add(status, 0, 5);
            var links = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = new Padding(0) };
            AddLink(links, "Steam 绝区零", delegate { Process.Start("steam://nav/games/details/" + App.AppId); });
            AddLink(links, "导出诊断", ExportDiagnostics);
            AddLink(links, "复制 Steam 启动项", delegate { Clipboard.SetText(SteamLaunchRoute.Command(App.FrontendExe)); status.Text = "已复制。可在 Steam → 绝区零 → 属性 → 通用 → 启动选项中核对或粘贴。"; });
            layout.Controls.Add(links, 0, 6);
            var more = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = new Padding(0, 6, 0, 0) };
            AddLink(more, "备份与状态", delegate { Directory.CreateDirectory(App.Home); Process.Start("explorer.exe", Arguments.Quote(App.Home)); });
            AddLink(more, "原项目 / MIT", delegate { Process.Start(new ProcessStartInfo("https://github.com/SoledadAki/endfield-steam-bridge") { UseShellExecute = true }); });
            layout.Controls.Add(more, 0, 7);
            // All dimensions above are designed at 96 DPI. Missing this baseline
            // made the old fixed rows clip their larger Windows fonts.
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(860, 790); MinimumSize = new Size(640, 480);
            Shown += async delegate {
                var area = Screen.FromControl(this).WorkingArea;
                MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width - 32), Math.Min(MinimumSize.Height, area.Height - 32));
                Size = new Size(Math.Min(Width, area.Width - 32), Math.Min(Height, area.Height - 32));
                Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
                if (!previewRendering) await RefreshDetection();
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) { e.Cancel = true; status.Text = "正在处理，请等待当前操作完成。"; } };
            ResumeLayout(true); SetBusy(true);
        }
        private static TableLayoutPanel Rows(int count)
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = count, Margin = new Padding(0) };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < count; i++) panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            return panel;
        }
        private Control PathRow(out TextBox field, out Button browse)
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = Color.Transparent, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 5, 0, 5) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            field = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(24, 27, 25), ForeColor = ink, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 5, 10, 0) };
            browse = Button("选择", false); browse.Margin = new Padding(0); browse.Dock = DockStyle.Top; browse.MinimumSize = Size.Empty;
            row.Controls.Add(field, 0, 0); row.Controls.Add(browse, 1, 0); return row;
        }
        private async Task Choose(GameTarget target, TextBox field)
        {
            using (var dialog = new OpenFileDialog { Filter = target.Name + " (" + target.Executable + ")|" + target.Executable, Title = "选择游戏目录中的 " + target.Executable, CheckFileExists = true })
                if (dialog.ShowDialog(this) == DialogResult.OK) { field.Text = dialog.FileName; await RefreshDetection(); }
        }
        private Label Label(string text, float size, Color color)
        { return new Label { Text = text, Font = new Font("Microsoft YaHei UI", size), ForeColor = color, BackColor = Color.Transparent, Dock = DockStyle.Top, TextAlign = ContentAlignment.MiddleLeft, AutoSize = true, Margin = new Padding(0) }; }
        private Button Button(string text, bool primary)
        {
            var value = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, MinimumSize = new Size(0, 40), Padding = new Padding(14, 7, 14, 7), FlatStyle = FlatStyle.Flat, BackColor = primary ? accent : Color.FromArgb(37, 42, 38), ForeColor = primary ? Color.FromArgb(24, 27, 25) : ink, Margin = new Padding(0, 0, 8, 8), Cursor = Cursors.Hand };
            value.FlatAppearance.BorderColor = primary ? accent : Color.FromArgb(77, 85, 77); return value;
        }
        private void ExportDiagnostics()
        {
            if (busy || current == null) return;
            using (var dialog = new SaveFileDialog { Filter = "诊断文件 (*.json)|*.json", FileName = "ZenlessBridge-diagnostic.json" })
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    try { App.Save(dialog.FileName, Diagnostics.Capture(current)); status.Text = "诊断已导出，包含启动项、组件版本和最近一次卡片程序启动记录。"; }
                    catch (Exception error) { MessageBox.Show(this, error.Message, "导出失败"); }
                }
        }
        private void AddLink(FlowLayoutPanel panel, string text, Action open)
        {
            var link = new LinkLabel { Text = text, AutoSize = true, LinkColor = accent, ActiveLinkColor = ink, Margin = new Padding(0, 0, 26, 0) };
            link.LinkClicked += delegate { open(); }; panel.Controls.Add(link);
        }
        private void SetBusy(bool value)
        {
            busy = value; refresh.Enabled = choose.Enabled = chooseStarRail.Enabled = preview.Enabled = !value;
            bool state = File.Exists(App.StatePath);
            enable.Text = state ? "修复启动入口" : "载入双游戏";
            enable.Enabled = !value && current != null && current.Account != null && current.Account.Known && current.Account.Owned && (current.ClientReady || current.StarRailReady) && current.Error == null;
            restore.Enabled = !value && state;
        }
        private async Task RefreshDetection()
        {
            SetBusy(true); status.Text = "正在检测 Steam、绝区零与星穹铁道…";
            string z = String.IsNullOrWhiteSpace(game.Text) ? null : game.Text, s = String.IsNullOrWhiteSpace(starRail.Text) ? null : starRail.Text;
            try { current = await Task.Run(() => Detection.Scan(z, s)); Populate(); }
            finally { SetBusy(false); }
        }
        private void Populate()
        {
            var a = current.Account;
            account.Text = a == null || !a.Known ? (a == null ? "Steam 未就绪" : a.Error) : (a.Owned ? "已拥有绝区零  ·  " : "尚未拥有绝区零  ·  ") + (a.Name ?? a.SteamId.ToString());
            account.ForeColor = a != null && a.Known && a.Owned ? accent : Color.FromArgb(239, 175, 102);
            game.Text = current.Game ?? game.Text; starRail.Text = current.StarRailGame ?? starRail.Text;
            client.Text = current.GameStatus ?? "请选择 ZenlessZoneZero.exe。"; starRailStatus.Text = current.StarRailStatus ?? "请选择 StarRail.exe。";
            installed.Text = current.Installation == null ? "尚未载入；未设置的游戏卡片不可点击。" : current.RouteError ?? "启动入口与组件文件校验通过。";
            installed.ForeColor = current.RouteError == null ? ink : Color.FromArgb(239, 175, 102);
            status.Text = current.Error ?? (a == null || !a.Known ? "请确认 Steam 已在线登录，再重新检测。" : !a.Owned ? "请先通过 Steam 商店将绝区零添加入库，再重新检测。" : "载入或撤回时会重启 Steam。两款游戏仍通过米哈游启动器更新。");
        }
        private async Task Mutate(bool uninstall)
        {
            SetBusy(true); string outcome = null;
            Action<string> report = message => { if (!IsDisposed) BeginInvoke((Action)(() => status.Text = message)); };
            try
            {
                if (uninstall) await Task.Run(() => InstallService.Restore(report));
                else if (File.Exists(App.StatePath)) await Task.Run(() => InstallService.Repair(current, report));
                else await Task.Run(() => InstallService.Enable(current, report));
                outcome = uninstall ? "已撤回载入，恢复原有 Steam 安装状态。" : "Steam 启动项和组件校验通过。等待 Steam 重启后，从绝区零进入游戏卡片。";
            }
            catch (Exception error) { outcome = error.Message; MessageBox.Show(this, error.Message, "操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            finally
            {
                if (current == null) current = new DetectionResult();
                try { current.Installation = File.Exists(App.StatePath) ? App.Load<Installation>(App.StatePath) : null;
                    current.RouteError = SteamLaunchRoute.Check(current.Installation, current.Account == null ? 0 : current.Account.SteamId, App.FrontendExe, App.RuntimeExe); Populate(); }
                catch (Exception error) { outcome = error.Message; }
                status.Text = outcome; SetBusy(false);
            }
        }
        internal void SavePreview(string path)
        {
            previewRendering = true;
            current = new DetectionResult { Account = new Ownership { Known = true, Owned = true, Name = "Steam 玩家" }, Game = @"D:\Games\ZenlessZoneZero Game\ZenlessZoneZero.exe", StarRailGame = @"D:\Games\Star Rail Games\StarRail.exe", ClientReady = true, StarRailReady = true, GameStatus = "已识别绝区零 x64 客户端。", StarRailStatus = "已识别崩坏：星穹铁道 x64 客户端。" };
            Populate(); SetBusy(false); Show(); PerformLayout(); Refresh(); Application.DoEvents();
            System.Threading.Thread.Sleep(150); Application.DoEvents();
            using (var image = new Bitmap(ClientSize.Width, ClientSize.Height))
            {
                using (var graphics = Graphics.FromImage(image)) graphics.CopyFromScreen(PointToScreen(Point.Empty), Point.Empty, ClientSize);
                image.Save(path);
            }
            Hide();
        }
    }
}
