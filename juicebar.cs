// Juicebar: bateria dos aparelhos sem fio, na barra de tarefas do Windows.
//
// Fontes (todas so de leitura: nao conecta, nao pareia, nao grava nada):
//  1. Windows: a bateria que o Windows ja recebe de qualquer aparelho Bluetooth conectado
//     (mouse, teclado, fone, controle...), agrupada por aparelho (ContainerId).
//  2. Bluetooth LE (GATT): aparelho LE conectado sem bateria no Windows -> le o servico
//     padrao de bateria (0x180F / 0x2A19).
//  3. Fast Pair do Google: fones que anunciam a bateria de cada lado (esquerdo, direito,
//     estojo). So vale para fone conectado a este PC (casado pelo nome).
//
// Uso: juicebar.exe [--demo]
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

// ================================================================ modelo
enum Tipo { Fone, Mouse, Teclado, Controle, Outro }

class Valor
{
    public string Rotulo;       // "", "E", "D", "Estojo"
    public int Nivel;           // 0-100, -1 = sem informacao
    public bool Carregando;
    public bool NaBarra = true; // estojo so na dica e nos detalhes
}

class Aparelho
{
    public string Id, Nome;
    public Tipo Tipo;
    public List<Valor> Valores = new List<Valor>();
    public string Fonte;
    public int Menor { get { var v = Valores.Where(x => x.NaBarra && x.Nivel >= 0).Select(x => x.Nivel).ToList(); return v.Count == 0 ? -1 : v.Min(); } }
}

// ================================================================ fonte 1: Windows (SetupAPI)
static class Windows_
{
    static readonly Guid BAT = new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5");          // pid 2, byte
    static readonly Guid CONT = new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C");         // pid 2, guid
    static readonly Guid CONN = new Guid("83DA6326-97A6-4088-9453-A1923F573B29");         // pid 15, bool
    static readonly Guid NOME = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0");         // 14 friendly, 2 desc

    static readonly Guid C_MOUSE = new Guid("4D36E96F-E325-11CE-BFC1-08002BE10318");
    static readonly Guid C_TECLADO = new Guid("4D36E96B-E325-11CE-BFC1-08002BE10318");
    static readonly Guid C_MEDIA = new Guid("4D36E96C-E325-11CE-BFC1-08002BE10318");
    static readonly Guid C_AUDIO = new Guid("C166523C-FE0C-4A94-A586-F1A80CFBBF3E");

    class Dev { public string Inst, Nome; public Guid Classe, Cont; public int Bat = -1; public bool? Conectado; }

    public class Grupo
    {
        public Guid Cont; public string Nome; public Tipo Tipo; public int Bat = -1; public bool Conectado;
        public ulong EnderecoLE;  // BTHLE\DEV_xxxx conectado
    }

    public static List<Grupo> Ler()
    {
        var devs = new List<Dev>();
        IntPtr h = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, 0x2 | 0x4);   // PRESENT | ALLCLASSES
        if (h == new IntPtr(-1)) return new List<Grupo>();
        try
        {
            var d = new SP_DEVINFO_DATA(); d.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
            for (uint i = 0; SetupDiEnumDeviceInfo(h, i, ref d); i++)
            {
                var dev = new Dev { Classe = d.ClassGuid };
                var sb = new System.Text.StringBuilder(512); uint req;
                if (SetupDiGetDeviceInstanceIdW(h, ref d, sb, 512, out req)) dev.Inst = sb.ToString(); else continue;
                byte[] b;
                b = Prop(h, ref d, CONT, 2); if (b != null && b.Length == 16) dev.Cont = new Guid(b);
                b = Prop(h, ref d, BAT, 2); if (b != null && b.Length >= 1) dev.Bat = b[0];
                b = Prop(h, ref d, CONN, 15); if (b != null && b.Length >= 1) dev.Conectado = b[0] != 0;
                b = Prop(h, ref d, NOME, 14) ?? Prop(h, ref d, NOME, 2); if (b != null) dev.Nome = System.Text.Encoding.Unicode.GetString(b).TrimEnd('\0');
                devs.Add(dev);
            }
        }
        finally { SetupDiDestroyDeviceInfoList(h); }

        var saida = new List<Grupo>();
        foreach (var g in devs.Where(x => x.Cont != Guid.Empty).GroupBy(x => x.Cont))
        {
            var lista = g.ToList();
            var raiz = lista.FirstOrDefault(x => x.Inst.StartsWith("BTHENUM\\DEV_", StringComparison.OrdinalIgnoreCase) || x.Inst.StartsWith("BTHLE\\DEV_", StringComparison.OrdinalIgnoreCase));
            var comBat = lista.FirstOrDefault(x => x.Bat >= 0 && x.Bat <= 100);
            bool ehLE = raiz != null && raiz.Inst.StartsWith("BTHLE\\", StringComparison.OrdinalIgnoreCase);
            if (comBat == null && !ehLE) continue;
            if (comBat == null && raiz == null) continue;

            var gr = new Grupo { Cont = g.Key };
            gr.Nome = raiz != null && !string.IsNullOrEmpty(raiz.Nome) ? raiz.Nome : (comBat != null ? comBat.Nome : "Aparelho");
            gr.Bat = comBat != null ? comBat.Bat : -1;
            var con = lista.Where(x => x.Conectado.HasValue).ToList();
            gr.Conectado = con.Count == 0 || con.Any(x => x.Conectado.Value);
            if (lista.Any(x => x.Classe == C_MOUSE)) gr.Tipo = Tipo.Mouse;
            else if (lista.Any(x => x.Classe == C_TECLADO)) gr.Tipo = Tipo.Teclado;
            else if (lista.Any(x => x.Classe == C_MEDIA || x.Classe == C_AUDIO)) gr.Tipo = Tipo.Fone;
            else if (System.Text.RegularExpressions.Regex.IsMatch(gr.Nome ?? "", "controller|gamepad|controle|xbox|dualsense|dualshock", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) gr.Tipo = Tipo.Controle;
            else gr.Tipo = Tipo.Outro;
            if (ehLE && gr.Conectado)
            {
                var m = System.Text.RegularExpressions.Regex.Match(raiz.Inst, "DEV_([0-9A-F]{12})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success) gr.EnderecoLE = Convert.ToUInt64(m.Groups[1].Value, 16);
            }
            if (comBat == null && gr.EnderecoLE == 0) continue;
            saida.Add(gr);
        }
        return saida;
    }

    static byte[] Prop(IntPtr h, ref SP_DEVINFO_DATA d, Guid fmt, uint pid)
    {
        var k = new DEVPROPKEY { fmtid = fmt, pid = pid };
        uint tipo, req;
        var buf = new byte[1024];
        if (!SetupDiGetDevicePropertyW(h, ref d, ref k, out tipo, buf, (uint)buf.Length, out req, 0)) return null;
        var r = new byte[req]; Array.Copy(buf, r, (int)req); return r;
    }

    [StructLayout(LayoutKind.Sequential)] struct SP_DEVINFO_DATA { public uint cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] struct DEVPROPKEY { public Guid fmtid; public uint pid; }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevsW(IntPtr cls, string en, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInfo(IntPtr h, uint i, ref SP_DEVINFO_DATA d);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInstanceIdW(IntPtr h, ref SP_DEVINFO_DATA d, System.Text.StringBuilder s, uint n, out uint req);
    [DllImport("setupapi.dll")] static extern bool SetupDiGetDevicePropertyW(IntPtr h, ref SP_DEVINFO_DATA d, ref DEVPROPKEY k, out uint tipo, byte[] buf, uint n, out uint req, uint flags);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr h);
}

// ================================================================ fonte 2: GATT (Bluetooth LE)
static class Gatt
{
    // espera uma operacao WinRT sem async/await (o csc do .NET Framework nao acha o GetAwaiter)
    static T Esperar<T>(Windows.Foundation.IAsyncOperation<T> op, int ms)
    {
        var fim = DateTime.Now.AddMilliseconds(ms);
        while (op.Status == Windows.Foundation.AsyncStatus.Started)
        {
            if (DateTime.Now > fim) { try { op.Cancel(); } catch { } return default(T); }
            Thread.Sleep(50);
        }
        return op.Status == Windows.Foundation.AsyncStatus.Completed ? op.GetResults() : default(T);
    }

    public static int Ler(ulong endereco)
    {
        try
        {
            using (var dev = Esperar(BluetoothLEDevice.FromBluetoothAddressAsync(endereco), 5000))
            {
                if (dev == null) return -1;
                var sv = Esperar(dev.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Cached), 5000);
                if (sv == null || sv.Status != GattCommunicationStatus.Success || sv.Services.Count == 0) return -1;
                var s = sv.Services[0];
                try
                {
                    var cs = Esperar(s.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Cached), 5000);
                    if (cs == null || cs.Status != GattCommunicationStatus.Success || cs.Characteristics.Count == 0) return -1;
                    var r = Esperar(cs.Characteristics[0].ReadValueAsync(BluetoothCacheMode.Uncached), 5000);
                    if (r == null || r.Status != GattCommunicationStatus.Success || r.Value == null || r.Value.Length < 1) return -1;
                    var b = new byte[1]; DataReader.FromBuffer(r.Value).ReadBytes(b);
                    return b[0] <= 100 ? (int)b[0] : -1;
                }
                finally { s.Dispose(); }
            }
        }
        catch { return -1; }
    }
}

// ================================================================ fonte 3: Fast Pair (anuncios LE)
class FastPair
{
    public class Leitura { public string Nome; public byte[] Bat; public DateTime Quando; }
    readonly BluetoothLEAdvertisementWatcher w = new BluetoothLEAdvertisementWatcher();
    readonly Dictionary<ulong, string> nomes = new Dictionary<ulong, string>();
    readonly Dictionary<ulong, byte[]> bats = new Dictionary<ulong, byte[]>();
    readonly Dictionary<string, Leitura> porNome = new Dictionary<string, Leitura>();
    public event Action Mudou;

    public FastPair() { w.ScanningMode = BluetoothLEScanningMode.Active; w.Received += Recebido; }
    public string Iniciar() { try { w.Start(); return null; } catch (Exception ex) { return ex.Message; } }
    public void Parar() { try { w.Stop(); } catch { } }

    public List<Leitura> Recentes()
    {
        lock (porNome) return porNome.Values.Where(x => (DateTime.Now - x.Quando).TotalMinutes < 10).ToList();
    }

    void Recebido(BluetoothLEAdvertisementWatcher s, BluetoothLEAdvertisementReceivedEventArgs e)
    {
        ulong end = e.BluetoothAddress;
        string nome = e.Advertisement.LocalName ?? "";
        byte[] bat = null;
        foreach (var d in e.Advertisement.DataSections)
        {
            if (d.DataType != 0x16 || d.Data.Length < 3) continue;
            var b = new byte[d.Data.Length];
            DataReader.FromBuffer(d.Data).ReadBytes(b);
            if (b[0] == 0x2C && b[1] == 0xFE) bat = Decodificar(b) ?? bat;
        }
        bool mudou = false;
        lock (porNome)
        {
            if (nome.Length > 0) nomes[end] = nome;
            if (bat != null) bats[end] = bat;
            string n; byte[] bb;
            if (nomes.TryGetValue(end, out n) && bats.TryGetValue(end, out bb))
            {
                Leitura l;
                mudou = !porNome.TryGetValue(n, out l) || !l.Bat.SequenceEqual(bb);
                porNome[n] = new Leitura { Nome = n, Bat = bb, Quando = DateTime.Now };
            }
        }
        if (mudou && Mudou != null) Mudou();
    }

    // 2C FE | flags | campos [cabecalho = tamanho<<4 | tipo]; tipo 3/4 = bateria E, D, estojo.
    // Cada byte: bit 7 = carregando; bits 0-6 = nivel (0x7F = desconhecido).
    static byte[] Decodificar(byte[] b)
    {
        if (b.Length == 5) return null;
        int i = 3;
        while (i < b.Length)
        {
            int tam = b[i] >> 4, tipo = b[i] & 0x0F;
            i++;
            if (i + tam > b.Length) return null;
            if ((tipo == 3 || tipo == 4) && tam >= 1)
            {
                var r = new byte[] { 0x7F, 0x7F, 0x7F };
                for (int k = 0; k < Math.Min(tam, 3); k++) r[k] = b[i + k];
                return r;
            }
            i += tam;
        }
        return null;
    }

    public static string Normal(string s)
    {
        s = (s ?? "").ToUpperInvariant();
        foreach (var suf in new[] { "-LE", " LE", "(LE)" }) if (s.EndsWith(suf)) s = s.Substring(0, s.Length - suf.Length);
        return new string(s.Where(char.IsLetterOrDigit).ToArray());
    }
}

// ================================================================ juncao das fontes
class Coletor
{
    public List<Aparelho> Aparelhos = new List<Aparelho>();
    public DateTime Atualizado;
    public event Action Mudou;
    readonly FastPair fp = new FastPair();
    readonly Control ui;
    List<Windows_.Grupo> grupos = new List<Windows_.Grupo>();
    readonly Dictionary<ulong, int> gattCache = new Dictionary<ulong, int>();
    int ciclo;

    public Coletor(Control ui)
    {
        this.ui = ui;
        fp.Mudou += () => Postar(Montar);
    }

    // --demo: aparelhos de exemplo, sem ler nada (para ver o visual e tirar a foto do README)
    public static bool Demo;

    void MontarDemo()
    {
        var fone = new Aparelho { Id = "demo-fone", Nome = "Fone Bluetooth", Tipo = Tipo.Fone, Fonte = "Fast Pair" };
        fone.Valores.Add(new Valor { Rotulo = "E", Nivel = 90 });
        fone.Valores.Add(new Valor { Rotulo = "D", Nivel = 35 });
        fone.Valores.Add(new Valor { Rotulo = "Estojo", Nivel = 60, Carregando = true, NaBarra = false });
        var mouse = new Aparelho { Id = "demo-mouse", Nome = "Mouse Bluetooth", Tipo = Tipo.Mouse, Fonte = "Windows" };
        mouse.Valores.Add(new Valor { Rotulo = "", Nivel = 72 });
        var teclado = new Aparelho { Id = "demo-teclado", Nome = "Teclado Bluetooth", Tipo = Tipo.Teclado, Fonte = "Windows" };
        teclado.Valores.Add(new Valor { Rotulo = "", Nivel = 15 });
        Aparelhos = new List<Aparelho> { fone, mouse, teclado };
        Atualizado = DateTime.Now;
        if (Mudou != null) Mudou();
    }

    public string Iniciar()
    {
        if (Demo) { Postar(MontarDemo); return null; }
        var erro = fp.Iniciar();
        var t = new Thread(Laco) { IsBackground = true, Name = "coletor" };
        t.Start();
        return erro;
    }

    public void Parar() { fp.Parar(); }

    void Laco()
    {
        while (true)
        {
            try
            {
                var g = Windows_.Ler();
                bool lerGatt = ciclo++ % 6 == 0;                   // LE: a cada ~2 min
                foreach (var x in g.Where(x => x.Bat < 0 && x.EnderecoLE != 0))
                {
                    if (lerGatt || !gattCache.ContainsKey(x.EnderecoLE)) gattCache[x.EnderecoLE] = Gatt.Ler(x.EnderecoLE);
                    x.Bat = gattCache[x.EnderecoLE];
                }
                Postar(() => { grupos = g; Montar(); });
            }
            catch { }
            Thread.Sleep(20000);
        }
    }

    void Postar(Action a)
    {
        try { if (ui.IsHandleCreated) ui.BeginInvoke(a); } catch { }
    }

    void Montar()
    {
        var lista = new List<Aparelho>();
        var fps = fp.Recentes();
        foreach (var g in grupos.Where(x => x.Conectado && x.Bat >= 0))
        {
            var a = new Aparelho { Id = g.Cont.ToString(), Nome = Limpar(g.Nome), Tipo = g.Tipo, Fonte = "Windows" };
            string n = FastPair.Normal(g.Nome);
            var casado = n.Length >= 4 ? fps.FirstOrDefault(f => { var m = FastPair.Normal(f.Nome); return m == n || m.StartsWith(n) || n.StartsWith(m); }) : null;
            if (casado != null)
            {
                a.Fonte = "Fast Pair";
                a.Tipo = Tipo.Fone;
                a.Valores.Add(V("E", casado.Bat[0], true));
                a.Valores.Add(V("D", casado.Bat[1], true));
                a.Valores.Add(V("Estojo", casado.Bat[2], false));
            }
            else a.Valores.Add(new Valor { Rotulo = "", Nivel = g.Bat });
            lista.Add(a);
        }
        Aparelhos = lista.OrderBy(x => x.Tipo).ThenBy(x => x.Nome).ToList();
        Atualizado = DateTime.Now;
        if (Mudou != null) Mudou();
    }

    static Valor V(string r, byte b, bool naBarra)
    {
        int n = b & 0x7F;
        return new Valor { Rotulo = r, Nivel = n > 100 ? -1 : n, Carregando = (b & 0x80) != 0 && n <= 100, NaBarra = naBarra };
    }

    static string Limpar(string s)
    {
        s = s ?? "";
        foreach (var suf in new[] { " Hands-Free AG Audio", " Hands-Free AG", " Hands-Free", " Stereo", " Avrcp Transport" })
            if (s.EndsWith(suf, StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - suf.Length);
        return s.Trim();
    }
}

// ================================================================ desenho comum
static class Visual
{
    public static Color Cor(int n)
    {
        if (n < 0) return Color.FromArgb(140, 140, 140);
        if (n <= 20) return Color.FromArgb(255, 99, 88);
        if (n <= 40) return Color.FromArgb(255, 185, 64);
        return Color.FromArgb(255, 255, 255);
    }

    public static string Glifo(Tipo t)
    {
        switch (t)
        {
            case Tipo.Fone: return "";
            case Tipo.Mouse: return "";
            case Tipo.Teclado: return "";
            case Tipo.Controle: return "";
            default: return "";
        }
    }

    // pilha pequena: contorno, polo e preenchimento proporcional
    public static void Pilha(Graphics g, float x, float y, float w, float h, int nivel, bool carregando, Color contorno)
    {
        using (var p = new Pen(contorno, 1f))
        using (var path = Arredondado(new RectangleF(x, y, w, h), 2f))
        {
            g.DrawPath(p, path);
            using (var polo = new SolidBrush(contorno)) g.FillRectangle(polo, x + w + 0.5f, y + h * 0.3f, 1.6f, h * 0.4f);
            if (nivel > 0)
            {
                float iw = (w - 3f) * Math.Min(nivel, 100) / 100f;
                var cor = carregando ? Color.FromArgb(110, 220, 130) : Cor(nivel);
                using (var b = new SolidBrush(cor))
                using (var ip = Arredondado(new RectangleF(x + 1.5f, y + 1.5f, Math.Max(iw, 1.5f), h - 3f), 1f))
                    g.FillPath(b, ip);
            }
        }
    }

    static GraphicsPath Arredondado(RectangleF r, float raio)
    {
        var p = new GraphicsPath(); float d = raio * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }

    public static string Texto(Valor v) { return v.Nivel < 0 ? "--" : v.Nivel + "%"; }
}

// ================================================================ barra de tarefas
class Widget : Form
{
    readonly Coletor col;
    readonly Detalhes detalhes;
    readonly System.Windows.Forms.Timer relogio = new System.Windows.Forms.Timer();
    readonly ToolTip dica = new ToolTip();
    readonly ContextMenuStrip menu = new ContextMenuStrip();
    IntPtr barra = IntPtr.Zero;
    Rectangle onde;
    int largura;
    bool sobre;

    public Widget()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-3000, -3000);
        Size = new Size(10, 40);
        Text = "Juicebar (barra)";
        col = new Coletor(this);
        detalhes = new Detalhes(col);
        col.Mudou += () => { Encaixar(); Desenhar(); AtualizarDica(); };

        menu.Items.Add("Detalhes", null, (s, e) => AbrirDetalhes());
        menu.Items.Add("Sair", null, (s, e) => { col.Parar(); Application.Exit(); });
        dica.InitialDelay = 300;

        MouseUp += (s, e) =>
        {
            if (e.Button == MouseButtons.Left) AbrirDetalhes();
            else if (e.Button == MouseButtons.Right) menu.Show(Cursor.Position);
        };
        MouseEnter += (s, e) => { sobre = true; Desenhar(); };
        MouseLeave += (s, e) => { sobre = false; Desenhar(); };

        relogio.Interval = 1500;
        relogio.Tick += (s, e) => { Encaixar(); };
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x00080000 | 0x08000000 | 0x00000080; return cp; }
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var erro = col.Iniciar();
        Encaixar(); Desenhar(); AtualizarDica();
        if (erro != null) dica.SetToolTip(this, "Bluetooth indisponível: " + erro);
        relogio.Start();
    }

    void AbrirDetalhes()
    {
        detalhes.Show();
        if (detalhes.WindowState == FormWindowState.Minimized) detalhes.WindowState = FormWindowState.Normal;
        detalhes.Activate();
    }

    // largura de cada aparelho na barra
    static int LarguraDe(Aparelho a) { return a.Valores.Count(v => v.NaBarra) > 1 ? 94 : 72; }   // inclui 6 px de folga entre aparelhos

    int LarguraTotal()
    {
        if (col.Aparelhos.Count == 0) return 30;
        return col.Aparelhos.Sum(a => LarguraDe(a)) + 4;
    }

    // Filho da barra de tarefas, logo a esquerda do clima (ou da bandeja); encolhe a area
    // dos botoes para abrir espaco. Refaz sozinho se o Explorer reiniciar.
    void Encaixar()
    {
        IntPtr tray = N.FindWindow("Shell_TrayWnd", null);
        if (tray == IntPtr.Zero) return;
        if (tray != barra || N.GetParent(Handle) != tray)
        {
            N.SetParent(Handle, tray);
            long st = N.GetWindowLongPtr(Handle, -16).ToInt64();
            st = (st & ~0x80000000L) | 0x40000000L | 0x10000000L;
            N.SetWindowLongPtr(Handle, -16, new IntPtr(st));
            barra = tray;
            onde = Rectangle.Empty;
        }

        N.RECT rt; N.GetWindowRect(tray, out rt);
        int ancora = int.MaxValue;
        foreach (var cls in new[] { "DynamicContent2", "TrayNotifyWnd" })
        {
            IntPtr h = N.FindWindowEx(tray, IntPtr.Zero, cls, null);
            if (h == IntPtr.Zero || !N.IsWindowVisible(h)) continue;
            N.RECT r; N.GetWindowRect(h, out r);
            if (r.Right > r.Left) ancora = Math.Min(ancora, r.Left);
        }
        if (ancora == int.MaxValue) ancora = rt.Right - 300;

        largura = LarguraTotal();
        int alt = rt.Bottom - rt.Top;
        int x = ancora - largura - 4 - rt.Left;
        var novo = new Rectangle(x, 0, largura, alt);
        if (novo != onde)
        {
            N.SetWindowPos(Handle, IntPtr.Zero, novo.X, novo.Y, novo.Width, novo.Height, 0x0010 | 0x0004 | 0x0040);
            onde = novo;
            Desenhar();
        }

        IntPtr rebar = N.FindWindowEx(tray, IntPtr.Zero, "ReBarWindow32", null);
        if (rebar != IntPtr.Zero)
        {
            N.RECT rr; N.GetWindowRect(rebar, out rr);
            int limite = rt.Left + x - 2;
            if (rr.Right > limite)
                N.SetWindowPos(rebar, IntPtr.Zero, rr.Left - rt.Left, rr.Top - rt.Top, limite - rr.Left, rr.Bottom - rr.Top, 0x0010 | 0x0004);
        }
    }

    void Desenhar()
    {
        if (onde.Width <= 0) return;
        int W = onde.Width, H = onde.Height;
        using (var bmp = new Bitmap(W, H, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(1, 0, 0, 0));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                if (sobre) using (var hb = new SolidBrush(Color.FromArgb(28, 255, 255, 255))) g.FillRectangle(hb, 0, 0, W, H);

                using (var fIcone = new Font("Segoe MDL2 Assets", 16f, GraphicsUnit.Pixel))
                using (var fGrande = new Font("Segoe UI", 12.5f, GraphicsUnit.Pixel))
                using (var fPeq = new Font("Segoe UI", 11.5f, GraphicsUnit.Pixel))
                using (var fRot = new Font("Segoe UI Semibold", 10f, GraphicsUnit.Pixel))
                {
                    if (col.Aparelhos.Count == 0)
                    {
                        using (var b = new SolidBrush(Color.FromArgb(120, 120, 120)))
                        {
                            var t = g.MeasureString("", fIcone);
                            g.DrawString("", fIcone, b, (W - t.Width) / 2f, (H - t.Height) / 2f);
                        }
                    }
                    float x = 2;
                    foreach (var a in col.Aparelhos)
                    {
                        int w = LarguraDe(a);
                        Bloco(g, a, x, w, H, fIcone, fGrande, fPeq, fRot);
                        x += w;
                    }
                }
            }
            Aplicar(bmp);
        }
    }

    static void Bloco(Graphics g, Aparelho a, float x, int w, int H, Font fIcone, Font fGrande, Font fPeq, Font fRot)
    {
        var naBarra = a.Valores.Where(v => v.NaBarra).ToList();
        using (var bIcone = new SolidBrush(Visual.Cor(a.Menor) == Color.FromArgb(255, 255, 255) ? Color.FromArgb(230, 230, 230) : Visual.Cor(a.Menor)))
        {
            var t = g.MeasureString(Visual.Glifo(a.Tipo), fIcone);
            g.DrawString(Visual.Glifo(a.Tipo), fIcone, bIcone, x + 2, (H - t.Height) / 2f + 1);
        }
        var contorno = Color.FromArgb(200, 200, 200, 200);
        float cx = x + 24;
        if (naBarra.Count == 1)
        {
            var v = naBarra[0];
            Visual.Pilha(g, cx, H / 2f - 4.5f, 15, 9, v.Nivel, v.Carregando, contorno);
            using (var b = new SolidBrush(Visual.Cor(v.Nivel)))
            {
                var t = g.MeasureString(Visual.Texto(v), fGrande);
                g.DrawString(Visual.Texto(v), fGrande, b, cx + 19, (H - t.Height) / 2f);
            }
        }
        else
        {
            for (int i = 0; i < Math.Min(2, naBarra.Count); i++)
            {
                var v = naBarra[i];
                float cy = H / 2f + (i == 0 ? -8.5f : 8.5f);
                using (var cinza = new SolidBrush(Color.FromArgb(175, 175, 175)))
                {
                    var tr = g.MeasureString(v.Rotulo, fRot);
                    g.DrawString(v.Rotulo, fRot, cinza, cx, cy - tr.Height / 2f);
                }
                Visual.Pilha(g, cx + 13, cy - 3.5f, 13, 7, v.Nivel, v.Carregando, contorno);
                using (var b = new SolidBrush(Visual.Cor(v.Nivel)))
                {
                    var t = g.MeasureString(Visual.Texto(v), fPeq);
                    g.DrawString(Visual.Texto(v), fPeq, b, cx + 30, cy - t.Height / 2f);
                }
            }
        }
    }

    void Aplicar(Bitmap bmp)
    {
        IntPtr tela = N.GetDC(IntPtr.Zero), mem = N.CreateCompatibleDC(tela);
        IntPtr hbmp = bmp.GetHbitmap(Color.FromArgb(0)), velho = N.SelectObject(mem, hbmp);
        try
        {
            var tam = new N.SIZE { cx = bmp.Width, cy = bmp.Height };
            var orig = new N.POINT();
            var bl = new N.BLEND { Op = 0, Flags = 0, Alpha = 255, Format = 1 };
            N.UpdateLayeredWindow(Handle, tela, IntPtr.Zero, ref tam, mem, ref orig, 0, ref bl, 2);
        }
        finally { N.SelectObject(mem, velho); N.DeleteObject(hbmp); N.DeleteDC(mem); N.ReleaseDC(IntPtr.Zero, tela); }
    }

    void AtualizarDica()
    {
        if (col.Aparelhos.Count == 0) { dica.SetToolTip(this, "Nenhum aparelho sem fio com bateria conectado"); return; }
        var linhas = new List<string>();
        foreach (var a in col.Aparelhos)
        {
            linhas.Add(a.Nome);
            foreach (var v in a.Valores)
            {
                string r = v.Rotulo == "E" ? "Esquerdo" : v.Rotulo == "D" ? "Direito" : v.Rotulo == "" ? "Bateria" : v.Rotulo;
                linhas.Add("   " + r + ": " + (v.Nivel < 0 ? "sem informação" : v.Nivel + "%") + (v.Carregando ? " (carregando)" : ""));
            }
        }
        linhas.Add("Atualizado às " + col.Atualizado.ToString("HH:mm:ss"));
        dica.SetToolTip(this, string.Join("\n", linhas));
    }
}

// ================================================================ janela de detalhes
class Detalhes : Form
{
    readonly Coletor col;
    readonly Panel corpo = new Panel();
    readonly Label rodape = new Label();

    public Detalhes(Coletor col)
    {
        this.col = col;
        Text = "Juicebar";
        Font = new Font("Segoe UI", 10f);
        BackColor = Color.FromArgb(32, 32, 32);
        ForeColor = Color.White;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(400, 200);
        corpo.SetBounds(0, 0, 400, 170);
        corpo.Paint += Pintar;
        typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(corpo, true, null);
        Controls.Add(corpo);
        rodape.ForeColor = Color.FromArgb(150, 150, 150);
        rodape.Font = new Font("Segoe UI", 9f);
        Controls.Add(rodape);
        col.Mudou += () => { if (Visible) Refazer(); };
        VisibleChanged += (s, e) => { if (Visible) Refazer(); };
        FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int sim = 1;
        if (DwmSetWindowAttribute(Handle, 20, ref sim, 4) != 0) DwmSetWindowAttribute(Handle, 19, ref sim, 4);   // barra de titulo escura
    }

    void Refazer()
    {
        int h = 20;
        foreach (var a in col.Aparelhos) h += 44 + a.Valores.Count * 34 + 10;
        if (col.Aparelhos.Count == 0) h = 80;
        ClientSize = new Size(400, h + 34);
        corpo.SetBounds(0, 0, 400, h);
        rodape.SetBounds(20, h + 6, 360, 22);
        rodape.Text = "Atualizado às " + col.Atualizado.ToString("HH:mm:ss");
        corpo.Invalidate();
    }

    void Pintar(object s, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using (var fIcone = new Font("Segoe MDL2 Assets", 20f, GraphicsUnit.Pixel))
        using (var fNome = new Font("Segoe UI Semibold", 15f, GraphicsUnit.Pixel))
        using (var fTxt = new Font("Segoe UI", 13.5f, GraphicsUnit.Pixel))
        using (var fNum = new Font("Segoe UI Semibold", 15f, GraphicsUnit.Pixel))
        using (var fPeq = new Font("Segoe UI", 11.5f, GraphicsUnit.Pixel))
        using (var branco = new SolidBrush(Color.White))
        using (var cinza = new SolidBrush(Color.FromArgb(160, 160, 160)))
        using (var trilho = new SolidBrush(Color.FromArgb(60, 60, 60)))
        {
            if (col.Aparelhos.Count == 0)
            {
                g.DrawString("Nenhum aparelho sem fio com bateria conectado.", fTxt, cinza, 20, 28);
                return;
            }
            float y = 20;
            foreach (var a in col.Aparelhos)
            {
                g.DrawString(Visual.Glifo(a.Tipo), fIcone, branco, 18, y + 2);
                g.DrawString(a.Nome, fNome, branco, 50, y);
                g.DrawString(a.Fonte == "Fast Pair" ? "cada lado pelo Fast Pair" : "pelo Windows", fPeq, cinza, 50, y + 20);
                y += 44;
                foreach (var v in a.Valores)
                {
                    string r = v.Rotulo == "E" ? "Esquerdo" : v.Rotulo == "D" ? "Direito" : v.Rotulo == "" ? "Bateria" : v.Rotulo;
                    g.DrawString(r, fTxt, branco, 50, y);
                    g.FillRectangle(trilho, 140, y + 7, 170, 8);
                    if (v.Nivel >= 0)
                        using (var b = new SolidBrush(v.Carregando ? Color.FromArgb(110, 220, 130) : (v.Nivel > 40 ? Color.FromArgb(90, 200, 120) : Visual.Cor(v.Nivel))))
                            g.FillRectangle(b, 140, y + 7, 170f * v.Nivel / 100f, 8);
                    string t = Visual.Texto(v);
                    var sz = g.MeasureString(t, fNum);
                    g.DrawString(t, fNum, branco, 380 - sz.Width, y - 1);
                    if (v.Carregando) g.DrawString("carregando", fPeq, cinza, 140, y + 16);
                    else if (v.Nivel < 0) g.DrawString("sem informação (estojo fechado ou longe)", fPeq, cinza, 140, y + 16);
                    y += 34;
                }
                y += 10;
            }
        }
    }
}

// ================================================================ Win32
static class N
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLEND { public byte Op, Flags, Alpha, Format; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr after, string c, string t);
    [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int ht, uint f);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, IntPtr pDst, ref SIZE sz, IntPtr src, ref POINT pSrc, int key, ref BLEND bl, int flags);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}

static class Programa
{
    [STAThread]
    static void Main(string[] args)
    {
        bool novo;
        using (var m = new Mutex(true, "Juicebar", out novo))
        {
            if (!novo) return;
            Coletor.Demo = args.Any(a => a.Equals("--demo", StringComparison.OrdinalIgnoreCase));
            N.SetProcessDPIAware();
            Application.EnableVisualStyles();
            var w = new Widget();
            w.Show();
            Application.Run(w);
        }
    }
}
