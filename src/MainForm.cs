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
        /// <summary>卓上のクリックできる領域（画面座標）。</summary>
        class Hit
        {
            public RectangleF R;
            public string Key;
            public Action Act;
        }

        class BoardButton
        {
            public int Q;          // ボタンを置く家（-1 は卓中央）
            public string Label;
            public Action Act;
        }

        /// <summary>分岐中の局面で選べる操作。</summary>
        class BranchActions
        {
            public string Text = "";
            public int DiscardQ = -1;     // 打牌できる家
            public bool[] HandOk;         // 手牌の各牌を切れるか
            public bool DrawnOk;          // ツモ牌を切れるか
            public int TargetQ = -1;      // 鳴き・ロン対象の打牌をした家
            public List<BoardButton> Buttons = new List<BoardButton>();
            public List<int> Unseen;      // 牌山が尽きたときのツモ候補
        }

        // ---- 卓の寸法（仮想座標 1000×1000。自分の席を下にして描き、各家は回転させる）----
        const float S = 1000;
        static readonly RectangleF CenterBox = new RectangleF(380, 380, 240, 240);
        const float HandX = 90, HandY = 934, TW = 40, TH = 54;            // 手牌
        const float RiverX = 390, RiverY = 630, RW = 34, RH = 46;         // 河
        const float ButtonY = 880, ButtonH = 44;                          // 操作ボタン

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
        BranchActions acts = new BranchActions();

        readonly System.Windows.Forms.Timer tm = new System.Windows.Forms.Timer();
        readonly Random rnd = new Random();
        bool updating;
        readonly List<Hit> hits = new List<Hit>();
        string hoverKey;

        public string PendingFile;

        // ---- コントロール ----
        Label lblTitle, lblRoundInfo, lblJn, lblBoardHeader, lblState, lblResult, lblBranchNote, lblBranchText;
        ComboBox cmbView, cmbSpeed;
        CheckBox chkOthers;
        ListBox lstRounds, lstLog;
        TrackBar trk;
        FlowLayoutPanel pnlEvents;
        Button btnBranchStart, btnBranchEnd, btnPlay;
        Canvas board;

        public MainForm()
        {
            Text = "牌譜ビューア";
            Size = new Size(1500, 1000);
            MinimumSize = new Size(1000, 700);
            StartPosition = FormStartPosition.CenterScreen;
            WindowState = FormWindowState.Maximized;
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
                Text = t, AutoSize = false, Height = 30, Dock = DockStyle.Fill, BackColor = Theme.Hdr, ForeColor = Theme.Acc,
                Font = Theme.Ui(10f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0), Margin = Padding.Empty
            };
        }

        static Button Btn(string t, EventHandler h, float pt = 10.5f)
        {
            var b = new Button
            {
                Text = t, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Box, ForeColor = Theme.Fg, Font = Theme.Ui(pt), Margin = new Padding(3),
                Padding = new Padding(6, 2, 6, 2), UseVisualStyleBackColor = false, Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = Theme.Line;
            if (h != null) b.Click += h;
            return b;
        }

        static FlowLayoutPanel Flow(Padding margin)
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = margin
            };
        }

        static TableLayoutPanel Column(DockStyle dock, int width)
        {
            var t = new TableLayoutPanel { Dock = dock, Width = width, BackColor = Theme.Side, ColumnCount = 1, AutoScroll = true };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return t;
        }

        static void AddRow(TableLayoutPanel t, Control c, bool fill = false)
        {
            t.RowStyles.Add(fill ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
            t.RowCount = t.RowStyles.Count;
            t.Controls.Add(c, 0, t.RowCount - 1);
        }

        void BuildUi()
        {
            // ---- 上部バー ----
            var top = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, BackColor = Theme.Hdr, Padding = new Padding(8, 4, 8, 4), WrapContents = true
            };
            top.Controls.Add(new Label { Text = "牌譜ビューア", AutoSize = true, Font = Theme.Ui(13f, FontStyle.Bold), Margin = new Padding(3, 7, 12, 3) });
            top.Controls.Add(Btn("牌譜を読み込む", (s, e) => OpenFile()));
            top.Controls.Add(new Label { Text = "視点", AutoSize = true, Margin = new Padding(16, 10, 0, 3) });
            cmbView = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Margin = new Padding(4, 6, 3, 3) };
            cmbView.SelectedIndexChanged += (s, e) =>
            {
                if (updating || cmbView.SelectedIndex < 0) return;
                view = cmbView.SelectedIndex;
                Render();
            };
            top.Controls.Add(cmbView);
            chkOthers = new CheckBox { Text = "他家の手牌を表向きに表示", Checked = true, AutoSize = true, Margin = new Padding(12, 9, 3, 3) };
            chkOthers.CheckedChanged += (s, e) => Render();
            top.Controls.Add(chkOthers);
            lblTitle = new Label { AutoSize = true, Font = Theme.Ui(11f, FontStyle.Bold), Margin = new Padding(24, 9, 3, 3) };
            top.Controls.Add(lblTitle);

            // ---- 左：局・巡目・再生 ----
            var left = Column(DockStyle.Left, 250);
            AddRow(left, H3("局一覧"));
            lstRounds = new ListBox
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Side, ForeColor = Theme.Fg,
                IntegralHeight = false, Height = 60, Margin = new Padding(0, 4, 0, 4), Font = Theme.Ui(11f)
            };
            lstRounds.SelectedIndexChanged += (s, e) =>
            {
                if (!updating && lstRounds.SelectedIndex >= 0 && lstRounds.SelectedIndex != rc) SetRound(lstRounds.SelectedIndex);
            };
            AddRow(left, lstRounds);
            AddRow(left, H3("局情報"));
            lblRoundInfo = new Label { AutoSize = true, Margin = new Padding(12, 8, 12, 8), Font = Theme.Ui(10.5f) };
            AddRow(left, lblRoundInfo);
            AddRow(left, H3("巡目選択"));
            var jr = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, AutoSize = true, Margin = new Padding(8, 6, 8, 0) };
            jr.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            jr.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            jr.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            jr.Controls.Add(Btn("◀", (s, e) => Jump(-1)), 0, 0);
            lblJn = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = Theme.Ui(9.5f) };
            jr.Controls.Add(lblJn, 1, 0);
            jr.Controls.Add(Btn("▶", (s, e) => Jump(1)), 2, 0);
            AddRow(left, jr);
            trk = new TrackBar { Dock = DockStyle.Fill, TickStyle = TickStyle.None, AutoSize = false, Height = 32, Margin = new Padding(6, 0, 6, 0), BackColor = Theme.Side };
            trk.ValueChanged += (s, e) => { if (!updating) Go(trk.Value); };
            AddRow(left, trk);
            pnlEvents = Flow(new Padding(8, 0, 8, 8));
            pnlEvents.MaximumSize = new Size(225, 0);
            AddRow(left, pnlEvents);
            AddRow(left, H3("再生"));
            var play = Flow(new Padding(8, 6, 8, 8));
            play.Controls.Add(Btn("◀", (s, e) => Go(Cx() - 1)));
            btnPlay = Btn("▶", (s, e) => Play(!tm.Enabled));
            play.Controls.Add(btnPlay);
            play.Controls.Add(Btn("▶|", (s, e) => Go(Cx() + 1)));
            cmbSpeed = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70, Margin = new Padding(6, 6, 3, 3) };
            cmbSpeed.Items.AddRange(new object[] { "0.5×", "1.0×", "2.0×", "4.0×" });
            cmbSpeed.SelectedIndex = 1;
            cmbSpeed.SelectedIndexChanged += (s, e) => { if (tm.Enabled) Play(true); };
            play.Controls.Add(cmbSpeed);
            AddRow(left, play);
            AddRow(left, new Panel { Height = 1, Margin = Padding.Empty }, true);

            // ---- 右：分岐・状態・ログ・結果 ----
            var right = Column(DockStyle.Right, 330);
            AddRow(right, H3("分岐"));
            var bb = Flow(new Padding(8, 6, 8, 0));
            btnBranchStart = Btn("この局面から分岐開始", (s, e) => StartBranch());
            btnBranchEnd = Btn("分岐終了（元牌譜へ戻る）", (s, e) => { BR = null; rii = false; Render(); });
            btnBranchEnd.Visible = false;
            bb.Controls.Add(btnBranchStart);
            bb.Controls.Add(btnBranchEnd);
            AddRow(right, bb);
            lblBranchNote = new Label { AutoSize = true, ForeColor = Theme.Aka, Font = Theme.Ui(10f, FontStyle.Bold), Margin = new Padding(12, 6, 12, 0), Visible = false, MaximumSize = new Size(300, 0) };
            AddRow(right, lblBranchNote);
            lblBranchText = new Label { AutoSize = true, ForeColor = Theme.Acc, Font = Theme.Ui(10f), Margin = new Padding(12, 6, 12, 10), MaximumSize = new Size(300, 0) };
            AddRow(right, lblBranchText);
            AddRow(right, H3("状態"));
            lblState = new Label { AutoSize = true, Margin = new Padding(12, 8, 12, 8), Font = Theme.Ui(10.5f) };
            AddRow(right, lblState);
            AddRow(right, H3("ログ"));
            lstLog = new ListBox
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, IntegralHeight = false, BackColor = Theme.Side, ForeColor = Theme.Fg,
                Font = Theme.Ui(10f), Margin = Padding.Empty, MinimumSize = new Size(0, 160)
            };
            lstLog.SelectedIndexChanged += (s, e) => { if (!updating && lstLog.SelectedIndex >= 0) Go(lstLog.SelectedIndex); };
            AddRow(right, lstLog, true);
            AddRow(right, H3("局の結果"));
            lblResult = new Label { AutoSize = true, Margin = new Padding(12, 8, 12, 10), Font = Theme.Ui(10f), MaximumSize = new Size(300, 0) };
            AddRow(right, lblResult);

            // ---- 中央：卓 ----
            var center = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Felt };
            board = new Canvas { Dock = DockStyle.Fill, BackColor = Theme.Felt };
            board.Paint += (s, e) => PaintBoard(e.Graphics);
            board.MouseMove += (s, e) =>
            {
                var h = HitAt(e.Location);
                string key = h != null ? h.Key : null;
                if (key == hoverKey) return;
                hoverKey = key;
                board.Cursor = h != null ? Cursors.Hand : Cursors.Default;
                board.Invalidate();
            };
            board.MouseLeave += (s, e) => { if (hoverKey != null) { hoverKey = null; board.Invalidate(); } };
            board.MouseClick += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                var h = HitAt(e.Location);
                if (h == null) return;
                hoverKey = null;
                h.Act();
            };
            lblBoardHeader = H3("卓表示");
            lblBoardHeader.Dock = DockStyle.Top;
            center.Controls.Add(board);
            center.Controls.Add(lblBoardHeader);

            var hint = new Label
            {
                Dock = DockStyle.Bottom, AutoSize = false, Height = 26, Padding = new Padding(12, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.Acc, Font = Theme.Ui(9f), BackColor = Theme.Hdr,
                Text = "操作：← → 1手移動　Space 再生/停止　JSONはドラッグ＆ドロップでも読み込めます　｜　牌画像：麻雀王国 麻雀素材 (mj-king.net/sozai)"
            };

            // Dock は後から追加したものが先に配置される
            Controls.Add(center);
            Controls.Add(left);
            Controls.Add(right);
            Controls.Add(hint);
            Controls.Add(top);
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
                pnlEvents.Controls.Add(Btn(s.Jn + "巡 " + kind, (o, e) => Go(jj), 9f));
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

        List<int> Dora(Step s) { return J.Ints(K[2]).Take(s.Dn).ToList(); }
        int Kyotaku(Step s) { return (J.I(J.L(K[0])[2]) + s.Pl.Count(p => p.Ri)) * 1000; }
        static string FirstLine(string m) { return m.Split('\n')[0]; }

        // =====================================================================
        // 画面更新
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
                acts = ComputeActions(s);
                if (view.HasValue && view.Value < cmbView.Items.Count) cmbView.SelectedIndex = view.Value;
                trk.Maximum = BR != null ? BR.Count - 1 : n;
                trk.Value = Cx();
                lblTitle.Text = Rname(K) + (BR != null ? "　● 分岐編集中" : "　● 牌譜を閲覧中");
                lblBoardHeader.Text = "卓表示｜" + s.Jn + "巡目　" + (s.T >= 0 ? Wn(s.T, s) + " " + Nmq(s.T) + "の手番" : "局終了")
                    + "　｜　" + FirstLine(s.Msg);
                lstRounds.SelectedIndex = rc;
                lblRoundInfo.Text = Rname(K) + "\r\nドラ " + string.Join(" ", Dora(s).Select(c => Tiles.Tn(c))) + "\r\n供託 " + kt.ToString("N0");
                lblJn.Text = BR != null
                    ? s.Jn + "巡目（分岐 " + bi + "/" + (BR.Count - 1) + "手）"
                    : s.Jn + " / " + steps[n].Jn + "巡目（" + i + "/" + n + "手）";

                lblState.Text = "手番　" + (s.T >= 0 ? Wn(s.T, s) : "―") + "\r\n" + s.Jn + "巡目 / 残り" + s.Wall + "枚\r\n"
                    + string.Join("\r\n", Enumerable.Range(0, 4).Select(q => Wn(q, s) + " " + Nmq(q) + "  " + s.Sc[q].ToString("N0") + (s.Pl[q].Ri ? "（立直）" : "")));

                var list = BR ?? steps;
                lstLog.BeginUpdate();
                lstLog.Items.Clear();
                lstLog.Items.AddRange(list.Select(x => (object)(x.Jn + "巡目：" + FirstLine(x.Msg))).ToArray());
                lstLog.SelectedIndex = Cx();
                lstLog.TopIndex = Math.Max(0, Cx() - 5);
                lstLog.EndUpdate();

                lblResult.Text = (BR != null && s.Ph == "e" ? s.Msg : steps[n].Msg).Replace("\n", "\r\n");

                btnBranchStart.Visible = BR == null;
                btnBranchEnd.Visible = BR != null;
                lblBranchNote.Text = bn;
                lblBranchNote.Visible = BR != null && bn != "";
                lblBranchText.Text = acts.Text;

                hoverKey = null;
                board.Invalidate();
            }
            finally { updating = prev; }
        }

        // =====================================================================
        // 卓の描画
        // =====================================================================

        Hit HitAt(Point pt)
        {
            for (int j = hits.Count - 1; j >= 0; j--)
                if (hits[j].R.Contains(pt)) return hits[j];
            return null;
        }

        /// <summary>現在の変換を適用した画面上の矩形をクリック領域として登録する。</summary>
        void AddHit(Graphics g, RectangleF r, string key, Action act)
        {
            var pts = new[] { r.Location, new PointF(r.Right, r.Bottom) };
            using (var m = g.Transform) m.TransformPoints(pts);
            hits.Add(new Hit
            {
                R = RectangleF.FromLTRB(Math.Min(pts[0].X, pts[1].X), Math.Min(pts[0].Y, pts[1].Y), Math.Max(pts[0].X, pts[1].X), Math.Max(pts[0].Y, pts[1].Y)),
                Key = key, Act = act
            });
        }

        void DrawButton(Graphics g, RectangleF r, string label, string key, Action act)
        {
            bool hov = key == hoverKey;
            using (var path = Theme.Rounded(r, 8))
            using (var b = new SolidBrush(hov ? Theme.AccHover : Theme.Acc))
            using (var pen = new Pen(Color.White, 2f))
            {
                g.FillPath(b, path);
                g.DrawPath(pen, path);
            }
            g.DrawString(label, Theme.Px(r.Height * 0.42f, FontStyle.Bold), Brushes.White, r, TileRenderer.CenterFormat);
            AddHit(g, r, key, act);
        }

        float ButtonWidth(Graphics g, string label, float h)
        {
            return g.MeasureString(label, Theme.Px(h * 0.42f, FontStyle.Bold)).Width + 26;
        }

        /// <summary>牌を描く。クリックできる場合はホバー時に持ち上げて枠を付ける。</summary>
        void DrawTile(Graphics g, int c, RectangleF r, string key, Action act, bool dim = false, bool grey = false, bool mark = false, bool side = false)
        {
            if (act != null)
            {
                AddHit(g, r, key, act);
                if (key == hoverKey) { r.Offset(0, -10); mark = true; }
            }
            TileRenderer.Draw(g, c, r, dim, grey, mark, side);
        }

        void PaintBoard(Graphics g)
        {
            g.Clear(Theme.Felt);
            hits.Clear();
            if (K == null) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            var sz = board.ClientSize;
            float k = Math.Min(sz.Width, sz.Height) / S;
            if (k <= 0.05f) return;
            g.TranslateTransform((sz.Width - S * k) / 2, (sz.Height - S * k) / 2);
            g.ScaleTransform(k, k);

            using (var path = Theme.Rounded(new RectangleF(2, 2, S - 4, S - 4), 18))
            using (var b = new SolidBrush(Theme.Table))
            using (var pen = new Pen(Theme.TableEdge, 4f))
            {
                g.FillPath(b, path);
                g.DrawPath(pen, path);
            }

            var s = Cs();
            int v = view ?? 0;
            PaintCenter(g, s);
            for (int q = 0; q < 4; q++)
            {
                int pos = (q - v + 4) % 4;
                var st = g.Save();
                if (pos != 0)
                {
                    g.TranslateTransform(S / 2, S / 2);
                    g.RotateTransform(pos == 1 ? -90 : pos == 2 ? 180 : 90);
                    g.TranslateTransform(-S / 2, -S / 2);
                }
                PaintSeat(g, s, q, q == v);
                g.Restore(st);
            }
            if (acts.Unseen != null) PaintPicker(g, s);
        }

        void PaintCenter(Graphics g, Step s)
        {
            var cb = CenterBox;
            using (var path = Theme.Rounded(cb, 10))
            using (var b = new SolidBrush(Theme.Box))
            using (var pen = new Pen(Theme.Line, 2f))
            {
                g.FillPath(b, path);
                g.DrawPath(pen, path);
            }
            var fmt = TileRenderer.CenterFormat;
            using (var fg = new SolidBrush(Theme.Fg))
            {
                g.DrawString(Rname(K), Theme.Px(20, FontStyle.Bold), fg, new RectangleF(cb.X, 428, cb.Width, 28), fmt);
                var dora = Dora(s);
                float dw = 28, dh = 37, x = cb.X + (cb.Width - dora.Count * (dw + 2)) / 2;
                foreach (int c in dora) { TileRenderer.Draw(g, c, new RectangleF(x, 458, dw, dh)); x += dw + 2; }
                g.DrawString("供託 " + Kyotaku(s).ToString("N0") + "　残り " + s.Wall + "枚", Theme.Px(15), fg, new RectangleF(cb.X, 498, cb.Width, 22), fmt);
            }
            var center = acts.Buttons.Where(b => b.Q < 0).ToList();
            if (center.Count > 0)
            {
                float h = 36, total = center.Sum(b => ButtonWidth(g, b.Label, h)) + (center.Count - 1) * 6, x = cb.X + (cb.Width - total) / 2;
                for (int j = 0; j < center.Count; j++)
                {
                    float w = ButtonWidth(g, center[j].Label, h);
                    DrawButton(g, new RectangleF(x, 528, w, h), center[j].Label, "c:" + j, center[j].Act);
                    x += w + 6;
                }
            }
        }

        /// <summary>1 人分（自分の席を下にした座標系）：名札・河・手牌・副露・操作ボタン。</summary>
        void PaintSeat(Graphics g, Step s, int q, bool self)
        {
            var p = s.Pl[q];
            bool turn = s.T == q;

            // 名札（中央の枠の手前側）
            var tag = new RectangleF(CenterBox.X + 6, CenterBox.Bottom - 44, CenterBox.Width - 12, 40);
            if (turn)
                using (var path = Theme.Rounded(tag, 6))
                using (var b = new SolidBrush(Theme.AccSoft))
                    g.FillPath(b, path);
            using (var fg = new SolidBrush(turn ? Theme.Acc : Theme.Fg))
            {
                g.DrawString(Wn(q, s) + "　" + Nmq(q), Theme.Px(14, turn ? FontStyle.Bold : FontStyle.Regular), fg,
                    new RectangleF(tag.X, tag.Y + 1, tag.Width, 19), TileRenderer.CenterFormat);
                g.DrawString(s.Sc[q].ToString("N0") + (p.Ri ? "　立直" : ""), Theme.Px(15, FontStyle.Bold), fg,
                    new RectangleF(tag.X, tag.Y + 19, tag.Width, 20), TileRenderer.CenterFormat);
            }

            // 河（6 枚ごとに改行、立直宣言牌は横向き）
            float x = RiverX;
            for (int j = 0; j < p.Rv.Count; j++)
            {
                if (j > 0 && j % 6 == 0) x = RiverX;
                float y = RiverY + (j / 6) * (RH + 2);
                var rv = p.Rv[j];
                bool target = acts.TargetQ == q && j == p.Rv.Count - 1;
                if (rv.R)
                {
                    TileRenderer.Draw(g, rv.C, new RectangleF(x, y + RH - RW, RH, RW), rv.X, rv.G, target, true);
                    x += RH + 1;
                }
                else
                {
                    TileRenderer.Draw(g, rv.C, new RectangleF(x, y, RW, RH), rv.X, rv.G, target);
                    x += RW + 1;
                }
            }

            // 手牌
            bool open = self || chkOthers.Checked || BR != null;
            bool canDiscard = acts.DiscardQ == q;
            x = HandX;
            for (int j = 0; j < p.H.Count; j++)
            {
                var r = new RectangleF(x, HandY, TW, TH);
                if (!open) TileRenderer.DrawBack(g, r);
                else
                {
                    int c = p.H[j];
                    bool ok = canDiscard && acts.HandOk[j];
                    DrawTile(g, c, r, "h:" + q + ":" + j, ok ? (Action)(() => Disc(q, c, false)) : null, dim: canDiscard && !ok);
                }
                x += TW + 1;
            }
            if (p.Dw.HasValue)
            {
                x += 12;
                var r = new RectangleF(x, HandY, TW, TH);
                if (!open) TileRenderer.DrawBack(g, r);
                else
                {
                    int c = p.Dw.Value;
                    bool ok = canDiscard && acts.DrawnOk;
                    DrawTile(g, c, r, "h:" + q + ":d", ok ? (Action)(() => Disc(q, c, true)) : null, dim: canDiscard && !ok);
                }
                x += TW + 1;
            }

            // 副露（鳴いた牌は横向き）
            x += 20;
            foreach (var m in p.Ml)
            {
                for (int j = 0; j < m.T.Count; j++)
                {
                    if (j == m.Ci && m.L != 'a')
                    {
                        TileRenderer.Draw(g, m.T[j], new RectangleF(x, HandY + TH - TW, TH, TW), side: true);
                        x += TH + 1;
                    }
                    else
                    {
                        var r = new RectangleF(x, HandY, TW, TH);
                        if (m.L == 'a' && (j == 0 || j == 3)) TileRenderer.DrawBack(g, r);
                        else TileRenderer.Draw(g, m.T[j], r);
                        x += TW + 1;
                    }
                }
                x += 10;
            }

            // この家の操作ボタン（手牌の上）
            float bx = HandX;
            int idx = 0;
            foreach (var b in acts.Buttons.Where(bt => bt.Q == q))
            {
                float w = ButtonWidth(g, b.Label, ButtonH);
                DrawButton(g, new RectangleF(bx, ButtonY, w, ButtonH), b.Label, "b:" + q + ":" + idx++, b.Act);
                bx += w + 8;
            }
        }

        /// <summary>牌山が尽きたときのツモ牌選択（卓中央に重ねて表示）。</summary>
        void PaintPicker(Graphics g, Step s)
        {
            var u = acts.Unseen;
            const int perRow = 10;
            const float cw = 46, ch = 62;
            int rows = Math.Max(1, (u.Count + perRow - 1) / perRow);
            float w = perRow * cw + 40, h = 50 + rows * ch + 64, x0 = (S - w) / 2, y0 = (S - h) / 2;
            var box = new RectangleF(x0, y0, w, h);
            using (var path = Theme.Rounded(box, 12))
            using (var b = new SolidBrush(Color.FromArgb(245, Theme.Box)))
            using (var pen = new Pen(Theme.Acc, 3f))
            {
                g.FillPath(b, path);
                g.DrawPath(pen, path);
            }
            using (var fg = new SolidBrush(Theme.Acc))
                g.DrawString(N(s.T) + " のツモ牌を選択（未使用牌）", Theme.Px(18, FontStyle.Bold), fg,
                    new RectangleF(x0, y0 + 10, w, 30), TileRenderer.CenterFormat);
            for (int j = 0; j < u.Count; j++)
            {
                int c = u[j];
                var r = new RectangleF(x0 + 20 + (j % perRow) * cw + 3, y0 + 50 + (j / perRow) * ch + 4, cw - 6, ch - 8);
                DrawTile(g, c, r, "u:" + j, () => DrawFromWall(c));
            }
            if (u.Count > 0)
            {
                float bw = ButtonWidth(g, "ランダム", 40);
                DrawButton(g, new RectangleF((S - bw) / 2, y0 + h - 54, bw, 40), "ランダム", "u:rand", () => DrawFromWall(u[rnd.Next(u.Count)]));
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
        // 分岐（牌譜と別の展開を卓上で操作して進める）
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
            foreach (int c in Dora(s)) add(c);
            var o = new List<int>();
            for (int j = 0; j < 34; j++)
            {
                if (a[j] >= 4) continue;
                o.Add(Tiles.Cd(j));
                if (j < 27 && j % 9 == 4 && !red.Contains(50 + j / 9 + 1)) o.Add(50 + j / 9 + 1);
            }
            return o;
        }

        static bool TenpaiAfter(List<int> all, int c)
        {
            var rest = new List<int>(all);
            Tiles.Rm(rest, c);
            return Tiles.Tenpai(rest);
        }

        /// <summary>現在の局面で選べる合法手（打牌できる牌・卓上に出すボタン）を求める。</summary>
        BranchActions ComputeActions(Step s)
        {
            var A = new BranchActions();
            if (BR == null)
            {
                A.Text = "任意の局面で「分岐開始」を押すと、卓上の牌やボタンを直接操作して牌譜と別の展開を確認できます（ツモは牌譜の牌山どおり）。";
                return A;
            }
            Action<int, string, Action> B = (bq, label, f) => A.Buttons.Add(new BoardButton { Q = bq, Label = label, Act = f });

            if (s.Ph == "d")
            {
                int q = s.T;
                var P = s.Pl[q];
                var all = new List<int>(P.H);
                if (P.Dw.HasValue) all.Add(P.Dw.Value);
                A.DiscardQ = q;
                A.HandOk = P.H.Select(c => !P.Ri && (!rii || TenpaiAfter(all, c))).ToArray();
                A.DrawnOk = P.Dw.HasValue && (!rii || TenpaiAfter(all, P.Dw.Value));
                A.Text = N(q) + "の手番：卓上の手牌をクリックして打牌"
                    + (rii ? "（リーチ宣言中：テンパイを保てる牌のみ選べます）" : P.Ri ? "（リーチ中のためツモ切りのみ）" : "");

                if (Tiles.Agari(all)) B(q, "ツモ", () => Fin(N(q) + " ツモ和了（役・点数は未計算）"));
                if (!P.Ri && s.Wall > 0)
                {
                    var cn = new SortedDictionary<int, int>();
                    foreach (int c in all)
                    {
                        int nn = Tiles.Norm(c), cur;
                        cn.TryGetValue(nn, out cur);
                        cn[nn] = cur + 1;
                    }
                    foreach (var kv in cn)
                    {
                        int kk = kv.Key;
                        if (kv.Value == 4 || P.Ml.Any(m => m.L == 'p' && Tiles.Norm(m.T[0]) == kk))
                            B(q, (kv.Value == 4 ? "暗槓 " : "加槓 ") + Tiles.Tn(kk), () => DoKan(q, kk));
                    }
                }
                if (!P.Ri && P.Ml.All(m => m.L == 'a') && s.Sc[q] >= 1000 && s.Wall >= 4 && all.Any(c => TenpaiAfter(all, c)))
                    B(q, rii ? "リーチ取消" : "リーチ", () => { rii = !rii; Render(); });
            }
            else if (s.Ph == "c")
            {
                int q = s.T;
                int dc = s.Pl[q].Rv[s.Pl[q].Rv.Count - 1].C, nn = Tiles.Norm(dc);
                A.TargetQ = q;
                A.Text = N(q) + "の " + Tiles.Tn(dc) + " に対する操作：鳴く家の手牌の上のボタンを押すか、中央の「スルー」で次へ";
                for (int k = 1; k < 4; k++)
                {
                    int r = (q + k) % 4;
                    var P = s.Pl[r];
                    if (Tiles.Agari(new List<int>(P.H) { dc }))
                        B(r, "ロン", () => Fin(N(r) + " ロン和了（" + N(q) + "から／役・点数は未計算）"));
                    if (P.Ri || s.Wall <= 0) continue;
                    var sm = P.H.Where(c => Tiles.Norm(c) == nn).ToList();
                    if (sm.Count >= 2) { var use = sm.Take(2).ToList(); B(r, "ポン", () => DoCall(r, 'p', use, dc)); }
                    if (sm.Count >= 3) { var use = sm.Take(3).ToList(); B(r, "カン", () => DoCall(r, 'm', use, dc)); }
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
                            B(r, "チー " + Tiles.Tn(u.Value) + Tiles.Tn(v.Value), () => DoCall(r, 'c', use, dc));
                        }
                    }
                }
                B(-1, "スルー", Pass);
            }
            else if (s.Ph == "w")
            {
                A.Unseen = Unseen(s);
                A.Text = "牌譜の牌山が尽きたため、" + N(s.T) + "のツモ牌を卓上の一覧から選んでください";
            }
            else A.Text = "局終了（" + FirstLine(s.Msg) + "）";
            return A;
        }
    }
}
