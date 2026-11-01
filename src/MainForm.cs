using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace PaifuViewer
{
    public class MainForm : Form
    {
        /// <summary>下欄の手牌表示で 1 要素（名前ラベルまたは牌）の配置。</summary>
        class HandItem
        {
            public RectangleF R;
            public string Text;
            public int C, Q;
            public float Fs;
            public bool Drawn, InHand, Rotated, Sharp;
        }

        // ---- 牌譜 ----
        string[] names = new string[0];
        List<object> rounds = new List<object>();
        List<object> K;
        RoundData RD;
        List<Step> steps;
        int i, rc;
        int? view;

        // ---- 分岐 ----
        List<Step> BR;
        int bi;
        bool rii;
        string bn = "";

        readonly System.Windows.Forms.Timer tm = new System.Windows.Forms.Timer();
        readonly Random rnd = new Random();
        bool updating;
        List<HandItem> handItems = new List<HandItem>();
        int hover = -1;

        public string PendingFile;

        // ---- コントロール ----
        Label lblTitle, lblRoundInfo, lblJn, lblTableHeader, lblState, lblResult, lblBranchNote, lblBranchText;
        ComboBox cmbView, cmbSpeed;
        CheckBox chkOthers, chkAll;
        ListBox lstRounds, lstLog;
        TrackBar trk;
        FlowLayoutPanel pnlEvents, pnlBranchButtons;
        Button btnBranchStart, btnBranchEnd, btnPlay;
        Canvas tablePanel, handPanel;
        TableLayoutPanel mainPanel, aside;
        RowStyle tableRow;
        const int MinTableHeight = 420;

        public MainForm()
        {
            Text = "牌譜ビューア";
            Size = new Size(1220, 1000);
            MinimumSize = new Size(820, 640);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Fg;
            Font = Theme.Ui(10f);
            KeyPreview = true;
            BuildUi();
            tm.Tick += (s, e) => { if (Cx() >= CurCount() - 1) Play(false); else Go(Cx() + 1); };
            EnableDrop(this);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            UpdateWidths();
            try { LoadData(MiniJson.Parse(SampleData.Json)); }
            catch (Exception ex) { lblTitle.Text = "読み込めませんでした：" + ex.Message; }
            if (PendingFile != null) ReadFile(PendingFile);
        }

        // =====================================================================
        // UI 構築
        // =====================================================================

        static Label H3(string t)
        {
            return new Label
            {
                Text = t, AutoSize = false, Height = 28, Dock = DockStyle.Top, BackColor = Theme.Hdr, ForeColor = Theme.Acc,
                Font = Theme.Ui(9f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0), Margin = Padding.Empty
            };
        }

        static Button Btn(string t, EventHandler h, float pt = 10f)
        {
            var b = new Button
            {
                Text = t, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Box, ForeColor = Theme.Fg, Font = Theme.Ui(pt), Margin = new Padding(3),
                Padding = new Padding(4, 0, 4, 0), UseVisualStyleBackColor = false, Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = Theme.Line;
            if (h != null) b.Click += h;
            return b;
        }

        static FlowLayoutPanel Bar()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Theme.Hdr,
                Padding = new Padding(8, 3, 8, 3), Margin = Padding.Empty, WrapContents = true
            };
        }

        static Label BarLabel(string t)
        {
            return new Label { Text = t, AutoSize = true, Margin = new Padding(3, 8, 10, 3) };
        }

        static void AddRow(TableLayoutPanel t, Control c, bool fill = false)
        {
            t.RowStyles.Add(fill ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
            t.RowCount = t.RowStyles.Count;
            t.Controls.Add(c, 0, t.RowCount - 1);
        }

        static Panel Column(string title, Control content)
        {
            var p = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            content.Dock = DockStyle.Fill;
            p.Controls.Add(content);
            p.Controls.Add(H3(title));
            return p;
        }

        void BuildUi()
        {
            // ---- 上部バー ----
            var top = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, BackColor = Theme.Hdr, Padding = new Padding(8, 4, 8, 4), WrapContents = true
            };
            top.Controls.Add(new Label { Text = "牌譜ビューア", AutoSize = true, Font = Theme.Ui(12f, FontStyle.Bold), Margin = new Padding(3, 7, 12, 3) });
            top.Controls.Add(Btn("牌譜を読み込む", (s, e) => OpenFile()));
            top.Controls.Add(new Label { Text = "視点", AutoSize = true, Margin = new Padding(16, 9, 0, 3) });
            cmbView = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Margin = new Padding(4, 5, 3, 3) };
            cmbView.SelectedIndexChanged += (s, e) =>
            {
                if (updating || cmbView.SelectedIndex < 0) return;
                view = cmbView.SelectedIndex;
                Render();
            };
            top.Controls.Add(cmbView);
            chkOthers = new CheckBox { Text = "他家の手牌を卓上に表示", Checked = true, AutoSize = true, Margin = new Padding(12, 8, 3, 3) };
            chkOthers.CheckedChanged += (s, e) => Render();
            top.Controls.Add(chkOthers);
            chkAll = new CheckBox { Text = "全員の手牌を下欄に表示", AutoSize = true, Margin = new Padding(12, 8, 3, 3) };
            chkAll.CheckedChanged += (s, e) => Render();
            top.Controls.Add(chkAll);
            lblTitle = new Label { AutoSize = true, Font = Theme.Ui(10.5f, FontStyle.Bold), Margin = new Padding(24, 8, 3, 3) };
            top.Controls.Add(lblTitle);

            // ---- 左サイド ----
            aside = new TableLayoutPanel { Dock = DockStyle.Left, Width = 260, BackColor = Theme.Side, ColumnCount = 1, AutoScroll = true };
            aside.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddRow(aside, H3("局一覧"));
            lstRounds = new ListBox
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Side, ForeColor = Theme.Fg,
                IntegralHeight = false, Height = 60, Margin = new Padding(0, 4, 0, 4), Font = Theme.Ui(10.5f)
            };
            lstRounds.SelectedIndexChanged += (s, e) =>
            {
                if (!updating && lstRounds.SelectedIndex >= 0 && lstRounds.SelectedIndex != rc) SetRound(lstRounds.SelectedIndex);
            };
            AddRow(aside, lstRounds);
            AddRow(aside, H3("局情報"));
            lblRoundInfo = new Label { AutoSize = true, Margin = new Padding(12, 8, 12, 8) };
            AddRow(aside, lblRoundInfo);
            AddRow(aside, H3("巡目選択"));
            var jr = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, AutoSize = true, Margin = new Padding(8, 6, 8, 0) };
            jr.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            jr.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            jr.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            jr.Controls.Add(Btn("◀", (s, e) => Jump(-1)), 0, 0);
            lblJn = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = Theme.Ui(9f) };
            jr.Controls.Add(lblJn, 1, 0);
            jr.Controls.Add(Btn("▶", (s, e) => Jump(1)), 2, 0);
            AddRow(aside, jr);
            trk = new TrackBar { Dock = DockStyle.Fill, TickStyle = TickStyle.None, AutoSize = false, Height = 32, Margin = new Padding(6, 0, 6, 0), BackColor = Theme.Side };
            trk.ValueChanged += (s, e) => { if (!updating) Go(trk.Value); };
            AddRow(aside, trk);
            pnlEvents = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
                Margin = new Padding(8, 0, 8, 8), MaximumSize = new Size(232, 0)
            };
            AddRow(aside, pnlEvents);

            // ---- 中央 ----
            mainPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Theme.Bg, AutoScroll = true };
            mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            lblTableHeader = H3("卓表示");
            lblTableHeader.Dock = DockStyle.Fill;
            AddRow(mainPanel, lblTableHeader);
            tablePanel = new Canvas { Dock = DockStyle.Fill, BackColor = Theme.Felt, Margin = Padding.Empty };
            tablePanel.Paint += (s, e) => PaintTable(e.Graphics);
            AddRow(mainPanel, tablePanel);
            tableRow = mainPanel.RowStyles[mainPanel.RowStyles.Count - 1];
            tableRow.SizeType = SizeType.Absolute;
            tableRow.Height = MinTableHeight;
            var hh = H3("手牌");
            hh.Dock = DockStyle.Fill;
            AddRow(mainPanel, hh);
            handPanel = new Canvas { Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top, Height = 80, BackColor = Theme.Bg, Margin = Padding.Empty };
            handPanel.Paint += (s, e) => PaintHand(e.Graphics);
            handPanel.SizeChanged += (s, e) => RefreshHandLayout();
            handPanel.MouseMove += (s, e) =>
            {
                int h = HitHand(e.Location);
                if (h == hover) return;
                hover = h;
                handPanel.Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
                handPanel.Invalidate();
            };
            handPanel.MouseLeave += (s, e) => { if (hover >= 0) { hover = -1; handPanel.Invalidate(); } };
            handPanel.MouseClick += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                int h = HitHand(e.Location);
                if (h < 0) return;
                var it = handItems[h];
                Disc(it.Q, it.C, it.Drawn);
            };
            AddRow(mainPanel, handPanel);

            var branchBar = Bar();
            branchBar.Controls.Add(BarLabel("分岐"));
            btnBranchStart = Btn("この局面から分岐開始", (s, e) => StartBranch());
            btnBranchEnd = Btn("分岐終了（元牌譜へ戻る）", (s, e) => { BR = null; rii = false; Render(); });
            btnBranchEnd.Visible = false;
            branchBar.Controls.Add(btnBranchStart);
            branchBar.Controls.Add(btnBranchEnd);
            AddRow(mainPanel, branchBar);
            lblBranchNote = new Label { AutoSize = true, ForeColor = Theme.Aka, Font = Theme.Ui(9.5f, FontStyle.Bold), Margin = new Padding(12, 6, 12, 0), Visible = false };
            AddRow(mainPanel, lblBranchNote);
            lblBranchText = new Label { AutoSize = true, ForeColor = Theme.Acc, Font = Theme.Ui(9.5f), Margin = new Padding(12, 6, 12, 4) };
            AddRow(mainPanel, lblBranchText);
            pnlBranchButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = new Padding(8, 0, 8, 6)
            };
            AddRow(mainPanel, pnlBranchButtons);

            var playBar = Bar();
            playBar.Controls.Add(BarLabel("再生"));
            playBar.Controls.Add(Btn("◀", (s, e) => Go(Cx() - 1)));
            btnPlay = Btn("▶", (s, e) => Play(!tm.Enabled));
            playBar.Controls.Add(btnPlay);
            playBar.Controls.Add(Btn("▶|", (s, e) => Go(Cx() + 1)));
            cmbSpeed = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70, Margin = new Padding(6, 5, 3, 3) };
            cmbSpeed.Items.AddRange(new object[] { "0.5×", "1.0×", "2.0×", "4.0×" });
            cmbSpeed.SelectedIndex = 1;
            cmbSpeed.SelectedIndexChanged += (s, e) => { if (tm.Enabled) Play(true); };
            playBar.Controls.Add(cmbSpeed);
            AddRow(mainPanel, playBar);
            mainPanel.Resize += (s, e) => { UpdateWidths(); FitTable(); };
            // 縦スクロールのみ許可する
            mainPanel.AutoScroll = false;
            mainPanel.HorizontalScroll.Enabled = false;
            mainPanel.HorizontalScroll.Visible = false;
            mainPanel.HorizontalScroll.Maximum = 0;
            mainPanel.AutoScroll = true;

            // ---- 下段 3 列 ----
            var cols = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom, Height = 180, ColumnCount = 3, RowCount = 1, BackColor = Theme.Bg,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
            };
            for (int j = 0; j < 3; j++) cols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            cols.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            lblState = new Label { AutoSize = false, Padding = new Padding(12, 8, 12, 8) };
            lstLog = new ListBox { BorderStyle = BorderStyle.None, IntegralHeight = false, BackColor = Theme.Bg, ForeColor = Theme.Fg, Font = Theme.Ui(9.5f) };
            lstLog.SelectedIndexChanged += (s, e) => { if (!updating && lstLog.SelectedIndex >= 0) Go(lstLog.SelectedIndex); };
            lblResult = new Label { AutoSize = false, Padding = new Padding(12, 8, 12, 8) };
            cols.Controls.Add(Column("状態", lblState), 0, 0);
            cols.Controls.Add(Column("ログ", lstLog), 1, 0);
            cols.Controls.Add(Column("局の結果", lblResult), 2, 0);

            var hint = new Label
            {
                Dock = DockStyle.Bottom, AutoSize = false, Height = 26, Padding = new Padding(12, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.Acc, Font = Theme.Ui(9f),
                Text = "操作：← → 1手移動　Space 再生/停止　JSONはドラッグ＆ドロップでも読み込めます"
            };

            // Dock は後から追加したものが先に配置される
            Controls.Add(mainPanel);
            Controls.Add(aside);
            Controls.Add(cols);
            Controls.Add(hint);
            Controls.Add(top);
        }

        void UpdateWidths()
        {
            if (mainPanel == null) return;
            int w = Math.Max(100, mainPanel.ClientSize.Width - 30);
            lblBranchNote.MaximumSize = new Size(w, 0);
            lblBranchText.MaximumSize = new Size(w, 0);
            pnlBranchButtons.MaximumSize = new Size(w + 10, 0);
        }

        /// <summary>卓表示に残りの高さを割り当てる。足りない場合は最小高さを保ち、中央列をスクロールさせる。</summary>
        void FitTable()
        {
            if (tableRow == null) return;
            int other = 0;
            foreach (Control c in mainPanel.Controls)
                if (c != tablePanel && c.Visible) other += c.Height + c.Margin.Vertical;
            int h = Math.Max(MinTableHeight, mainPanel.ClientSize.Height - other - 2);
            if ((int)tableRow.Height != h) tableRow.Height = h;
        }

        void EnableDrop(Control c)
        {
            c.AllowDrop = true;
            c.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            c.DragDrop += (s, e) =>
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0) ReadFile(files[0]);
            };
            foreach (Control ch in c.Controls) EnableDrop(ch);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (K != null)
            {
                if (keyData == Keys.Left) { Go(Cx() - 1); return true; }
                if (keyData == Keys.Right) { Go(Cx() + 1); return true; }
                if (keyData == Keys.Space) { Play(!tm.Enabled); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // =====================================================================
        // 読み込み
        // =====================================================================

        void OpenFile()
        {
            using (var dlg = new OpenFileDialog { Filter = "牌譜 JSON (*.json)|*.json|すべてのファイル (*.*)|*.*" })
                if (dlg.ShowDialog(this) == DialogResult.OK) ReadFile(dlg.FileName);
        }

        void ReadFile(string path)
        {
            try { LoadData(MiniJson.Parse(File.ReadAllText(path, Encoding.UTF8))); }
            catch (Exception ex) { lblTitle.Text = "読み込めませんでした：" + ex.Message; }
        }

        void LoadData(object d)
        {
            var dict = d as Dictionary<string, object>;
            object logO, nameO;
            if (dict == null || !dict.TryGetValue("log", out logO) || J.L(logO).Count == 0)
                throw new FormatException("天鳳6形式の牌譜ではありません");
            names = dict.TryGetValue("name", out nameO) ? J.L(nameO).Select(x => J.S(x)).ToArray() : new string[0];
            rounds = J.L(logO);
            view = null;
            bool prev = updating;
            updating = true;
            try
            {
                lstRounds.Items.Clear();
                foreach (var k in rounds) lstRounds.Items.Add(Rname(J.L(k)));
                lstRounds.Height = Math.Min(260, lstRounds.ItemHeight * rounds.Count + 6);
            }
            finally { updating = prev; }
            SetRound(0);
        }

        void SetRound(int r)
        {
            BR = null;
            rc = r;
            K = J.L(rounds[r]);
            RD = RoundBuilder.Build(K, names);
            steps = RD.Steps;
            i = 0;
            Play(false);
            int n = steps.Count - 1;
            if (!view.HasValue) view = Math.Max(0, Array.FindIndex(names, x => !string.IsNullOrEmpty(x) && x != "AI"));

            pnlEvents.SuspendLayout();
            foreach (var c in pnlEvents.Controls.Cast<Control>().ToList()) c.Dispose();
            for (int j = 0; j < steps.Count; j++)
            {
                var s = steps[j];
                string kind = j == 0 ? "配牌"
                    : j == n ? (s.Msg.StartsWith("和了") ? "和了" : "流局")
                    : s.Msg.Contains("チー") || s.Msg.Contains("ポン") || s.Msg.Contains("槓") ? "鳴き"
                    : s.Msg.Contains("立直") ? "リーチ" : "";
                if (kind == "") continue;
                int jj = j;
                pnlEvents.Controls.Add(Btn(s.Jn + "巡 " + kind, (o, e) => Go(jj), 8.5f));
            }
            pnlEvents.ResumeLayout();

            bool prev = updating;
            updating = true;
            try
            {
                cmbView.Items.Clear();
                for (int q = 0; q < 4; q++) cmbView.Items.Add(Nmq(q));
            }
            finally { updating = prev; }
            Render();
        }

        // =====================================================================
        // 表示ヘルパ
        // =====================================================================

        Step Cs() { return BR != null ? BR[bi] : steps[i]; }
        int Cx() { return BR != null ? bi : i; }
        int CurCount() { return BR != null ? BR.Count : steps.Count; }
        string Nmq(int q) { return J.Name(names, q); }
        static string Wn(int q, Step s) { return Tiles.WD[(q - s.Dealer + 4) % 4] + "家"; }
        string N(int q) { return Wn(q, Cs()) + " " + Nmq(q); }

        static string Rname(List<object> k)
        {
            var k0 = J.L(k[0]);
            int r = J.I(k0[0]);
            return Tiles.WD[(r / 4) % 4].ToString() + (r % 4 + 1) + "局 " + J.I(k0[1]) + "本場";
        }

        string DoraText(Step s) { return string.Join(" ", J.Ints(K[2]).Take(s.Dn).Select(c => Tiles.Tn(c))); }
        int Kyotaku(Step s) { return (J.I(J.L(K[0])[2]) + s.Pl.Count(p => p.Ri)) * 1000; }
        static string FirstLine(string m) { return m.Split('\n')[0]; }

        IEnumerable<int> HandOrder()
        {
            int v = view ?? 0;
            return chkAll.Checked || BR != null ? Enumerable.Range(0, 4).Select(o => (v + o) % 4) : new[] { v };
        }

        // =====================================================================
        // 描画
        // =====================================================================

        void Render()
        {
            if (K == null) return;
            bool prev = updating;
            updating = true;
            try
            {
                var s = Cs();
                int n = steps.Count - 1, kt = Kyotaku(s);
                string dora = DoraText(s);
                if (view.HasValue && view.Value < cmbView.Items.Count) cmbView.SelectedIndex = view.Value;
                trk.Maximum = BR != null ? BR.Count - 1 : n;
                trk.Value = Cx();
                lblTitle.Text = Rname(K) + (BR != null ? "　● 分岐編集中" : "　● 牌譜を閲覧中");
                lblTableHeader.Text = "卓表示｜" + s.Jn + "巡目　" + (s.T >= 0 ? Wn(s.T, s) + "の手番" : "局終了");
                lstRounds.SelectedIndex = rc;
                lblRoundInfo.Text = Rname(K) + "\r\nドラ " + dora + "\r\n供託 " + kt.ToString("N0");
                lblJn.Text = BR != null
                    ? s.Jn + "巡目（分岐 " + bi + "/" + (BR.Count - 1) + "手）"
                    : s.Jn + " / " + steps[n].Jn + "巡目（" + i + "/" + n + "手）";

                tablePanel.Invalidate();
                RefreshHandLayout();

                lblState.Text = "手番　" + (s.T >= 0 ? Wn(s.T, s) : "―") + "\r\n" + s.Jn + "巡目 / 残り" + s.Wall + "枚\r\n"
                    + string.Join("\r\n", Enumerable.Range(0, 4).Select(q => Wn(q, s) + " " + s.Sc[q].ToString("N0") + (s.Pl[q].Ri ? "（立直）" : "")));

                var list = BR ?? steps;
                lstLog.BeginUpdate();
                lstLog.Items.Clear();
                lstLog.Items.AddRange(list.Select(x => (object)(x.Jn + "巡目：" + FirstLine(x.Msg))).ToArray());
                lstLog.SelectedIndex = Cx();
                lstLog.TopIndex = Math.Max(0, Cx() - 3);
                lstLog.EndUpdate();

                lblResult.Text = (BR != null && s.Ph == "e" ? s.Msg : steps[n].Msg).Replace("\n", "\r\n");
                UpdateBranchPanel();
                mainPanel.PerformLayout();
                FitTable();
            }
            finally { updating = prev; }
        }

        void PaintTable(Graphics g)
        {
            g.Clear(Theme.Felt);
            if (K == null) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            var sz = tablePanel.ClientSize;
            float k = Math.Min(sz.Width, sz.Height) / 640f;
            if (k <= 0.05f) return;
            g.TranslateTransform((sz.Width - 640 * k) / 2, (sz.Height - 640 * k) / 2);
            g.ScaleTransform(k, k);

            var s = Cs();
            int v = view ?? 0;
            var fmt = TileRenderer.CenterFormat;
            using (var box = new SolidBrush(Theme.Box))
            using (var line = new Pen(Theme.Line))
            using (var acc = new Pen(Theme.Acc, 2f))
            using (var fg = new SolidBrush(Theme.Fg))
            {
                // 中央の局情報
                g.FillRectangle(box, 255, 255, 130, 130);
                g.DrawRectangle(line, 255, 255, 130, 130);
                string[] lines = { Rname(K), "ドラ　" + DoraText(s), "供託　" + Kyotaku(s).ToString("N0"), "残り " + s.Wall + "枚" };
                float y = 255 + (130 - lines.Length * 21) / 2f;
                for (int j = 0; j < lines.Length; j++)
                {
                    g.DrawString(lines[j], Theme.Px(13, j == 0 ? FontStyle.Bold : FontStyle.Regular), fg, new RectangleF(255, y, 130, 21), fmt);
                    y += 21;
                }

                // 各家の点数ボックス
                for (int q = 0; q < 4; q++)
                {
                    int pos = (q - v + 4) % 4;
                    var p = s.Pl[q];
                    var r = new RectangleF(pos == 0 || pos == 3 ? 4 : 496, pos <= 1 ? 595 : 1, 140, 44);
                    g.FillRectangle(box, r);
                    g.DrawRectangle(s.T == q ? acc : line, r.X, r.Y, r.Width, r.Height);
                    g.DrawString(Wn(q, s) + "　" + Nmq(q), Theme.Px(12), fg, new RectangleF(r.X, r.Y + 3, r.Width, 19), fmt);
                    g.DrawString(s.Sc[q].ToString("N0") + (p.Ri ? "　立直" : "") + (s.T == q ? "　● 手番" : ""),
                        Theme.Px(12), fg, new RectangleF(r.X, r.Y + 22, r.Width, 19), fmt);
                }
            }

            // 各家の河・副露・手牌（自分の位置を基準に回転）
            for (int q = 0; q < 4; q++)
            {
                int pos = (q - v + 4) % 4;
                var p = s.Pl[q];
                var st = g.Save();
                if (pos != 0)
                {
                    g.TranslateTransform(320, 320);
                    g.RotateTransform(pos == 1 ? -90 : pos == 2 ? 180 : 90);
                    g.TranslateTransform(-320, -320);
                }
                for (int j = 0; j < p.Rv.Count; j++)
                {
                    var rv = p.Rv[j];
                    TileRenderer.Draw(g, rv.C, new RectangleF(264 + j % 6 * 19, 392 + j / 6 * 26, 18, 25), 11, rv.X, rv.G, rv.R);
                }
                float x = 60;
                foreach (var m in p.Ml)
                {
                    for (int j = 0; j < m.T.Count; j++)
                    {
                        if (j == m.Ci && m.L != 'a')
                        {
                            TileRenderer.Draw(g, m.T[j], new RectangleF(x + 1, 533 - 22, 31, 22), 12, rotated: true);
                            x += 34;
                        }
                        else
                        {
                            TileRenderer.Draw(g, m.T[j], new RectangleF(x, 502, 22, 31), 12);
                            x += 23;
                        }
                    }
                    x += 8;
                }
                if (chkOthers.Checked && q != v)
                {
                    x = 60;
                    foreach (int c in p.H)
                    {
                        TileRenderer.Draw(g, c, new RectangleF(x, 540, 22, 31), 12);
                        x += 22;
                    }
                    if (p.Dw.HasValue) TileRenderer.Draw(g, p.Dw.Value, new RectangleF(x + 8, 540, 22, 31), 12);
                }
                g.Restore(st);
            }
        }

        /// <summary>下欄の手牌表示のレイアウトを計算し、パネルの高さを合わせる。</summary>
        void RefreshHandLayout()
        {
            if (K == null) return;
            int width = handPanel.ClientSize.Width;
            var items = new List<HandItem>();
            var s = Cs();
            const float TH = 54, LH = 58, X0 = 12;
            float right = width - 12, y = 6;
            foreach (int q in HandOrder())
            {
                var p = s.Pl[q];
                items.Add(new HandItem { Text = Wn(q, s) + "　" + Nmq(q), R = new RectangleF(X0, y, Math.Max(10, width - 24), 18), Q = q });
                y += 22;
                float x = X0, lineTop = y;
                Action<float> fit = w => { if (x + w > right && x > X0) { x = X0; lineTop += LH; } };

                foreach (int c in p.H)
                {
                    fit(38);
                    items.Add(new HandItem { C = c, Q = q, InHand = true, Sharp = true, Fs = 15, R = new RectangleF(x, lineTop, 38, TH) });
                    x += 39;
                }
                if (p.Dw.HasValue)
                {
                    x += 12;
                    fit(38);
                    items.Add(new HandItem { C = p.Dw.Value, Q = q, InHand = true, Drawn = true, Sharp = true, Fs = 15, R = new RectangleF(x, lineTop, 38, TH) });
                    x += 39;
                }
                x += 12;
                foreach (var m in p.Ml)
                {
                    fit(m.T.Count * 31 + (m.L != 'a' ? 24 : 0));
                    for (int j = 0; j < m.T.Count; j++)
                    {
                        if (j == m.Ci && m.L != 'a')
                        {
                            x += 6;
                            items.Add(new HandItem { C = m.T[j], Q = q, Rotated = true, Fs = 12, R = new RectangleF(x, lineTop + TH - 30, 42, 30) });
                            x += 49;
                        }
                        else
                        {
                            items.Add(new HandItem { C = m.T[j], Q = q, Fs = 12, R = new RectangleF(x, lineTop + TH - 42, 30, 42) });
                            x += 31;
                        }
                    }
                    x += 10;
                }
                y = lineTop + LH + 6;
            }
            handItems = items;
            hover = -1;
            int height = (int)Math.Ceiling(y) + 2;
            if (handPanel.Height != height) handPanel.Height = height;
            handPanel.Invalidate();
        }

        bool Clickable(HandItem it)
        {
            if (BR == null || !it.InHand) return false;
            var s = Cs();
            return s.Ph == "d" && s.T == it.Q;
        }

        int HitHand(Point pt)
        {
            for (int j = 0; j < handItems.Count; j++)
                if (handItems[j].Text == null && handItems[j].R.Contains(pt) && Clickable(handItems[j])) return j;
            return -1;
        }

        void PaintHand(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            using (var acc = new SolidBrush(Theme.Acc))
            {
                for (int j = 0; j < handItems.Count; j++)
                {
                    var it = handItems[j];
                    if (it.Text != null) g.DrawString(it.Text, Theme.Ui(9f), acc, it.R.Location);
                    else TileRenderer.Draw(g, it.C, it.R, it.Fs, mark: j == hover, sharp: it.Sharp, rotated: it.Rotated);
                }
            }
        }

        // =====================================================================
        // 移動・再生
        // =====================================================================

        void Go(int n)
        {
            if (K == null) return;
            if (BR != null) bi = Math.Max(0, Math.Min(BR.Count - 1, n));
            else i = Math.Max(0, Math.Min(steps.Count - 1, n));
            Render();
        }

        void Jump(int d)
        {
            if (BR != null || K == null) return;
            int t = steps[i].Jn + d, j = steps.FindIndex(x => x.Jn == t);
            if (j >= 0) Go(j);
        }

        void Play(bool on)
        {
            tm.Stop();
            btnPlay.Text = on ? "⏸" : "▶";
            if (!on) return;
            double speed = new[] { 0.5, 1, 2, 4 }[Math.Max(0, cmbSpeed.SelectedIndex)];
            tm.Interval = (int)(800 / speed);
            tm.Start();
        }

        // =====================================================================
        // 分岐（牌譜と別の展開を手で進める）
        // =====================================================================

        void StartBranch()
        {
            var s = steps[i];
            if (s.Ph == "e") return;
            Play(false);
            BR = new List<Step> { s.Clone() };
            bi = 0;
            rii = false;
            bn = "";
            if (BR[0].Ph == "w") Nxt(); else Render();
        }

        Step Nx(string m, int t, string ph)
        {
            BR.RemoveRange(bi + 1, BR.Count - bi - 1);
            var n = BR[bi].Clone();
            n.Msg = m;
            n.T = t;
            n.Ph = ph;
            bn = "";
            n.Rin = false;
            n.Jn = n.CalcJn();
            BR.Add(n);
            bi++;
            return n;
        }

        void Note(string m) { bn = m; Render(); }
        void Fin(string m) { Nx(m, -1, "e"); Render(); }

        void Disc(int q, int c, bool w)
        {
            var p0 = Cs().Pl[q];
            if (p0.Ri && !w) { Note("リーチ後はツモ切りのみ可能です"); return; }
            bool r = rii;
            if (r)
            {
                var rest = new List<int>(p0.H);
                if (p0.Dw.HasValue) rest.Add(p0.Dw.Value);
                Tiles.Rm(rest, c);
                if (!Tiles.Tenpai(rest)) { Note("その牌を切ってもテンパイになりません"); return; }
            }
            var n = Nx(N(q) + " 打 " + Tiles.Tn(c) + (r ? "（立直）" : ""), q, "c");
            var p = n.Pl[q];
            if (w) p.Dw = null;
            else
            {
                Tiles.Rm(p.H, c);
                if (p.Dw.HasValue) { p.H.Add(p.Dw.Value); Tiles.Sort(p.H); p.Dw = null; }
            }
            p.Rv.Add(new River { C = c, R = r, G = w });
            if (r) { p.Ri = true; n.Sc[q] -= 1000; }
            rii = false;
            Render();
        }

        void DoCall(int q, char l, List<int> use, int dc)
        {
            int t = Cs().T, d = (t - q + 4) % 4;
            int ci = l == 'c' || d == 3 ? 0 : d == 2 ? 1 : l == 'p' ? 2 : 3;
            var tl = new List<int>(use);
            tl.Insert(ci, dc);
            var n = Nx(N(q) + " " + RoundBuilder.CallName(l) + " " + Tiles.Tn(dc), q, l == 'm' ? "w" : "d");
            var p = n.Pl[q];
            foreach (int c in use) Tiles.Rm(p.H, c);
            var rv = n.Pl[t].Rv;
            rv[rv.Count - 1].X = true;
            p.Ml.Add(new Meld { L = l, T = tl, Ci = ci });
            if (l == 'm') { n.Dn++; n.Rin = true; Nxt(); }
            else Render();
        }

        void DoKan(int q, int k)
        {
            var pp = Cs().Pl[q];
            int m = pp.Ml.FindIndex(x => x.L == 'p' && Tiles.Norm(x.T[0]) == k);
            var n = Nx(N(q) + " " + (m >= 0 ? "加槓 " : "暗槓 ") + Tiles.Tn(k), q, "w");
            var p = n.Pl[q];
            if (p.Dw.HasValue) { p.H.Add(p.Dw.Value); p.Dw = null; }
            if (m >= 0)
            {
                int c = p.H.First(x => Tiles.Norm(x) == k);
                Tiles.Rm(p.H, c);
                var t = new List<int>(p.Ml[m].T) { c };
                p.Ml[m] = new Meld { L = 'k', T = t, Ci = p.Ml[m].Ci };
            }
            else
            {
                var u = p.H.Where(x => Tiles.Norm(x) == k).ToList();
                foreach (int c in u) Tiles.Rm(p.H, c);
                p.Ml.Add(new Meld { L = 'a', T = u, Ci = 0 });
            }
            Tiles.Sort(p.H);
            n.Dn++;
            n.Rin = true;
            Nxt();
        }

        void Pass()
        {
            var s = Cs();
            if (s.Wall <= 0) { Fin("流局（海底）"); return; }
            int q = (s.T + 1) % 4;
            Nx("見送り → " + N(q) + " のツモ", q, "w");
            Nxt();
        }

        void DrawFromWall(int c)
        {
            var s0 = Cs();
            int q = s0.T;
            bool rn = s0.Rin;
            var n = Nx(N(q) + " ツモ " + Tiles.Tn(c), q, "d");
            n.Pl[q].Dw = c;
            n.Wall--;
            if (rn) n.Rd++; else n.Ld++;
            Render();
        }

        /// <summary>牌譜の牌山どおりに次のツモを行う。牌山が尽きていれば選択待ちのまま表示する。</summary>
        void Nxt()
        {
            var s = Cs();
            var src = s.Rin ? RD.RW : RD.LW;
            int idx = s.Rin ? s.Rd : s.Ld;
            if (idx < src.Count) DrawFromWall(src[idx]); else Render();
        }

        List<int> Unseen(Step s)
        {
            var a = new int[34];
            var red = new HashSet<int>();
            Action<int> add = c => { a[Tiles.Ix(c)]++; if (c > 50) red.Add(c); };
            foreach (var p in s.Pl)
            {
                p.H.ForEach(add);
                if (p.Dw.HasValue) add(p.Dw.Value);
                foreach (var r in p.Rv) if (!r.X) add(r.C);
                foreach (var m in p.Ml) m.T.ForEach(add);
            }
            foreach (int c in J.Ints(K[2]).Take(s.Dn)) add(c);
            var o = new List<int>();
            for (int j = 0; j < 34; j++)
            {
                if (a[j] >= 4) continue;
                o.Add(Tiles.Cd(j));
                if (j < 27 && j % 9 == 4 && !red.Contains(50 + j / 9 + 1)) o.Add(50 + j / 9 + 1);
            }
            return o;
        }

        /// <summary>分岐欄の説明文と、現在の局面で選べる合法手のボタンを作り直す。</summary>
        void UpdateBranchPanel()
        {
            btnBranchStart.Visible = BR == null;
            btnBranchEnd.Visible = BR != null;
            pnlBranchButtons.SuspendLayout();
            foreach (var c in pnlBranchButtons.Controls.Cast<Control>().ToList()) c.Dispose();
            lblBranchNote.Text = bn;
            lblBranchNote.Visible = BR != null && bn != "";

            Action<string, Action> B = (label, f) =>
            {
                // クリック中のボタン自身を作り直すので、処理はイベント後に回す
                pnlBranchButtons.Controls.Add(Btn(label, (o, e) => BeginInvoke(f), 9.5f));
            };

            string h;
            var s = BR != null ? Cs() : null;
            if (BR == null)
            {
                h = "任意の局面で「分岐開始」を押すと、合法手（打牌・チー・ポン・カン・リーチ・ツモ/ロン）を選んで（ツモは牌譜の牌山どおり）牌譜と別の展開を確認できます。";
            }
            else if (s.Ph == "d")
            {
                int q = s.T;
                var P = s.Pl[q];
                var all = new List<int>(P.H);
                if (P.Dw.HasValue) all.Add(P.Dw.Value);
                var cn = new SortedDictionary<int, int>();
                foreach (int c in all)
                {
                    int nn = Tiles.Norm(c), cur;
                    cn.TryGetValue(nn, out cur);
                    cn[nn] = cur + 1;
                }
                h = N(q) + "の手番：手牌をクリックで打牌" + (rii ? "（リーチ宣言中）" : "");
                if (Tiles.Agari(all)) B("ツモ和了", () => Fin(N(q) + " ツモ和了（役・点数は未計算）"));
                if (!P.Ri && s.Wall > 0)
                {
                    foreach (var kv in cn)
                    {
                        int kk = kv.Key;
                        if (kv.Value == 4 || P.Ml.Any(m => m.L == 'p' && Tiles.Norm(m.T[0]) == kk))
                            B((kv.Value == 4 ? "暗槓 " : "加槓 ") + Tiles.Tn(kk), () => DoKan(q, kk));
                    }
                }
                if (!P.Ri && P.Ml.All(m => m.L == 'a') && s.Sc[q] >= 1000 && s.Wall >= 4)
                    B("リーチ宣言 " + (rii ? "ON" : "OFF"), () => { rii = !rii; Render(); });
            }
            else if (s.Ph == "c")
            {
                int q = s.T;
                int dc = s.Pl[q].Rv[s.Pl[q].Rv.Count - 1].C, nn = Tiles.Norm(dc);
                h = N(q) + "の " + Tiles.Tn(dc) + " に対する操作：";
                for (int k = 1; k < 4; k++)
                {
                    int r = (q + k) % 4;
                    var P = s.Pl[r];
                    string W = Wn(r, s);
                    if (Tiles.Agari(new List<int>(P.H) { dc }))
                        B(W + " ロン", () => Fin(N(r) + " ロン和了（" + N(q) + "から／役・点数は未計算）"));
                    if (P.Ri || s.Wall <= 0) continue;
                    var sm = P.H.Where(c => Tiles.Norm(c) == nn).ToList();
                    if (sm.Count >= 2) { var use = sm.Take(2).ToList(); B(W + " ポン", () => DoCall(r, 'p', use, dc)); }
                    if (sm.Count >= 3) { var use = sm.Take(3).ToList(); B(W + " 大明槓", () => DoCall(r, 'm', use, dc)); }
                    if (k == 1 && nn < 40)
                    {
                        int su = nn / 10, rk = nn % 10;
                        foreach (var ab in new[] { new[] { -2, -1 }, new[] { -1, 1 }, new[] { 1, 2 } })
                        {
                            int x = rk + ab[0], y = rk + ab[1];
                            if (x < 1 || y > 9) continue;
                            int? u = Tiles.Pk(P.H, su * 10 + x), v = Tiles.Pk(P.H, su * 10 + y);
                            if (!u.HasValue || !v.HasValue) continue;
                            var use = new List<int> { u.Value, v.Value };
                            B(W + " チー " + Tiles.Tn(u.Value) + Tiles.Tn(v.Value), () => DoCall(r, 'c', use, dc));
                        }
                    }
                }
                B("鳴かずに進める", Pass);
            }
            else if (s.Ph == "w")
            {
                int q = s.T;
                h = "牌譜の牌山が尽きたため、" + N(q) + "のツモ牌を未使用牌から選択：";
                var u = Unseen(s);
                foreach (int c in u)
                {
                    int cc = c;
                    B(Tiles.Tn(cc) + (cc > 50 ? "(赤)" : ""), () => DrawFromWall(cc));
                }
                if (u.Count > 0) B("ランダム", () => DrawFromWall(u[rnd.Next(u.Count)]));
            }
            else h = "局終了（" + FirstLine(s.Msg) + "）";

            lblBranchText.Text = h;
            pnlBranchButtons.Visible = pnlBranchButtons.Controls.Count > 0;
            pnlBranchButtons.ResumeLayout();
        }
    }
}
