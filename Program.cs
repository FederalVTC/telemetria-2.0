using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Telemetria2
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }

    public class MainForm : Form
    {
        private ComboBox cmbInterfaces;
        private Label lblStatusValue;
        private Label lblIpValue;
        private LinkLabel lnkAppUrl;
        private LinkLabel lnkApiUrl;
        private Button btnOpenBoard;
        private Button btnMinimizeTray;
        private NotifyIcon trayIcon;
        private System.Windows.Forms.Timer uiTimer;

        private HttpListener httpListener;
        private Thread serverThread;
        private volatile bool running = false;
        private const int Port = 25555;

        // Colores Identidad Argentina
        private readonly Color ColorFondo = Color.FromArgb(10, 19, 30);
        private readonly Color ColorPanel = Color.FromArgb(16, 32, 50);
        private readonly Color ColorCeleste = Color.FromArgb(117, 170, 219);
        private readonly Color ColorDorado = Color.FromArgb(244, 196, 48);
        private readonly Color ColorBlanco = Color.White;

        public MainForm()
        {
            ConfigurarVentana();
            InicializarComponentes();
            CargarInterfacesRed();
            IniciarServidorWeb();

            uiTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            uiTimer.Tick += (s, e) => ActualizarEstadoSimulador();
            uiTimer.Start();
        }

        private void ConfigurarVentana()
        {
            this.Text = "FederalVTC - Telemetría 2.0";
            this.Size = new Size(540, 410);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = ColorFondo;
            this.ForeColor = ColorBlanco;
            this.DoubleBuffered = true;

            trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "FederalVTC - Telemetría 2.0",
                Visible = true
            };
            trayIcon.DoubleClick += (s, e) => { this.Show(); this.WindowState = FormWindowState.Normal; };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (Pen penCeleste = new Pen(ColorCeleste, 3))
            {
                g.DrawRectangle(penCeleste, 8, 8, this.ClientSize.Width - 17, this.ClientSize.Height - 17);
            }

            using (Pen penDorado = new Pen(ColorDorado, 1))
            {
                g.DrawRectangle(penDorado, 14, 14, this.ClientSize.Width - 29, this.ClientSize.Height - 29);
            }
        }

        private void InicializarComponentes()
        {
            Label lblTitulo = new Label
            {
                Text = "FEDERAL VTC • TELEMETRÍA 2.0",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = ColorBlanco,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Bounds = new Rectangle(20, 22, 485, 32)
            };

            Label lblSubtitulo = new Label
            {
                Text = "❖ SERVIDOR DE TELEMETRÍA EDICIÓN ARGENTINA ❖",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = ColorDorado,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Bounds = new Rectangle(20, 52, 485, 20)
            };

            Panel pnlDatos = new Panel
            {
                BackColor = ColorPanel,
                Bounds = new Rectangle(28, 82, 470, 215),
                BorderStyle = BorderStyle.FixedSingle
            };

            Font lblFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            Font valFont = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            Label lbl1 = new Label { Text = "Estado del Simulador:", Font = lblFont, ForeColor = ColorCeleste, Location = new Point(15, 18), AutoSize = true };
            lblStatusValue = new Label { Text = "Buscando simulador...", Font = lblFont, ForeColor = Color.Coral, Location = new Point(175, 18), AutoSize = true };

            Label lbl2 = new Label { Text = "Interfaz de Red:", Font = lblFont, ForeColor = ColorCeleste, Location = new Point(15, 55), AutoSize = true };
            cmbInterfaces = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(175, 52),
                Width = 270,
                BackColor = ColorFondo,
                ForeColor = ColorBlanco
            };
            cmbInterfaces.SelectedIndexChanged += (s, e) => ActualizarUrls();

            Label lbl3 = new Label { Text = "IP del Servidor:", Font = lblFont, ForeColor = ColorCeleste, Location = new Point(15, 95), AutoSize = true };
            lblIpValue = new Label { Text = "127.0.0.1", Font = lblFont, ForeColor = ColorDorado, Location = new Point(175, 95), AutoSize = true };

            Label lbl4 = new Label { Text = "Tablero Web (URL):", Font = lblFont, ForeColor = ColorCeleste, Location = new Point(15, 132), AutoSize = true };
            lnkAppUrl = new LinkLabel { Text = $"http://127.0.0.1:{Port}/", Font = valFont, LinkColor = ColorBlanco, Location = new Point(175, 132), AutoSize = true };
            lnkAppUrl.LinkClicked += (s, e) => AbrirUrl(lnkAppUrl.Text);

            Label lbl5 = new Label { Text = "API JSON (URL):", Font = lblFont, ForeColor = ColorCeleste, Location = new Point(15, 168), AutoSize = true };
            lnkApiUrl = new LinkLabel { Text = $"http://127.0.0.1:{Port}/api/ets2/telemetry", Font = valFont, LinkColor = ColorCeleste, Location = new Point(175, 168), AutoSize = true };
            lnkApiUrl.LinkClicked += (s, e) => AbrirUrl(lnkApiUrl.Text);

            pnlDatos.Controls.AddRange(new Control[] { lbl1, lblStatusValue, lbl2, cmbInterfaces, lbl3, lblIpValue, lbl4, lnkAppUrl, lbl5, lnkApiUrl });

            btnOpenBoard = new Button
            {
                Text = "ABRIR TABLERO EN NAVEGADOR",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                BackColor = ColorCeleste,
                ForeColor = ColorFondo,
                FlatStyle = FlatStyle.Flat,
                Bounds = new Rectangle(28, 310, 265, 38),
                Cursor = Cursors.Hand
            };
            btnOpenBoard.Click += (s, e) => AbrirUrl(lnkAppUrl.Text);

            btnMinimizeTray = new Button
            {
                Text = "MINIMIZAR AL RELOJ",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                BackColor = ColorPanel,
                ForeColor = ColorBlanco,
                FlatStyle = FlatStyle.Flat,
                Bounds = new Rectangle(305, 310, 193, 38),
                Cursor = Cursors.Hand
            };
            btnMinimizeTray.Click += (s, e) => this.Hide();

            this.Controls.AddRange(new Control[] { lblTitulo, lblSubtitulo, pnlDatos, btnOpenBoard, btnMinimizeTray });
        }

        private void CargarInterfacesRed()
        {
            cmbInterfaces.Items.Clear();
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    var ipProps = ni.GetIPProperties();
                    var ipv4 = ipProps.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                    if (ipv4 != null)
                    {
                        cmbInterfaces.Items.Add(new NetItem { Name = ni.Name, Ip = ipv4.Address.ToString() });
                    }
                }
            }
            cmbInterfaces.Items.Add(new NetItem { Name = "Localhost (Solo esta PC)", Ip = "127.0.0.1" });
            if (cmbInterfaces.Items.Count > 0) cmbInterfaces.SelectedIndex = 0;
        }

        private void ActualizarUrls()
        {
            if (cmbInterfaces.SelectedItem is NetItem item)
            {
                lblIpValue.Text = item.Ip;
                lnkAppUrl.Text = $"http://{item.Ip}:{Port}/";
                lnkApiUrl.Text = $"http://{item.Ip}:{Port}/api/ets2/telemetry";
            }
        }

        private void ActualizarEstadoSimulador()
        {
            bool conectado = TelemetryReader.IsSimulatorRunning();
            if (conectado)
            {
                lblStatusValue.Text = "● Conectado a ETS2 / ATS";
                lblStatusValue.ForeColor = Color.LightGreen;
            }
            else
            {
                lblStatusValue.Text = "● Simulador desconectado";
                lblStatusValue.ForeColor = Color.Coral;
            }
        }

        private void AbrirUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        private void IniciarServidorWeb()
        {
            running = true;
            serverThread = new Thread(() =>
            {
                try
                {
                    httpListener = new HttpListener();
                    httpListener.Prefixes.Add($"http://localhost:{Port}/");
                    httpListener.Prefixes.Add($"http://127.0.0.1:{Port}/");
                    
                    // Intentar agregar IP local si es posible
                    if (cmbInterfaces.SelectedItem is NetItem item && item.Ip != "127.0.0.1")
                    {
                        try { httpListener.Prefixes.Add($"http://{item.Ip}:{Port}/"); } catch { }
                    }

                    httpListener.Start();

                    while (running && httpListener.IsListening)
                    {
                        try
                        {
                            var ctx = httpListener.GetContext();
                            ProcesarPeticion(ctx);
                        }
                        catch
                        {
                            if (!running) break;
                        }
                    }
                }
                catch { }
            })
            { IsBackground = true };
            serverThread.Start();
        }

        private void ProcesarPeticion(HttpListenerContext ctx)
        {
            try
            {
                string path = ctx.Request.Url.AbsolutePath.ToLowerInvariant();
                ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");

                byte[] buffer;
                if (path == "/api/ets2/telemetry")
                {
                    ctx.Response.ContentType = "application/json; charset=utf-8";
                    string json = TelemetryReader.GetTelemetryJson();
                    buffer = Encoding.UTF8.GetBytes(json);
                }
                else
                {
                    ctx.Response.ContentType = "text/html; charset=utf-8";
                    buffer = Encoding.UTF8.GetBytes(DashboardHtml.GetHtml());
                }

                ctx.Response.ContentLength64 = buffer.Length;
                using (var output = ctx.Response.OutputStream)
                {
                    output.Write(buffer, 0, buffer.Length);
                }
            }
            catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            running = false;
            try { httpListener?.Stop(); } catch { }
            trayIcon.Visible = false;
            base.OnFormClosing(e);
        }

        private class NetItem
        {
            public string Name { get; set; }
            public string Ip { get; set; }
            public override string ToString() => $"{Name} ({Ip})";
        }
    }

    public static class TelemetryReader
    {
        private static readonly string[] MapNames = { "Local\\Ets2TelemetryServer", "Local\\SimTelemetryETS2" };

        public static bool IsSimulatorRunning()
        {
            bool procRunning = Process.GetProcessesByName("eurotrucks2").Length > 0 ||
                               Process.GetProcessesByName("amtrucks").Length > 0;
            if (!procRunning) return false;

            foreach (var name in MapNames)
            {
                try
                {
                    using var mmf = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read);
                    return true;
                }
                catch { }
            }
            return false;
        }

        public static string GetTelemetryJson()
        {
            foreach (var name in MapNames)
            {
                try
                {
                    using var mmf = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read);
                    using var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

                    bool paused = accessor.ReadBoolean(4);
                    float speed = accessor.ReadSingle(48);
                    float engineRpm = accessor.ReadSingle(60);
                    float engineRpmMax = accessor.ReadSingle(64);
                    int gear = accessor.ReadInt32(68);
                    float fuel = accessor.ReadSingle(84);
                    float fuelCapacity = accessor.ReadSingle(88);
                    float waterTemp = accessor.ReadSingle(104);
                    float oilTemp = accessor.ReadSingle(112);
                    float oilPressure = accessor.ReadSingle(108);
                    float airPressure = accessor.ReadSingle(120);
                    bool parkBrake = accessor.ReadBoolean(132);
                    bool motorBrake = accessor.ReadBoolean(133);
                    uint retarder = accessor.ReadUInt32(136);
                    float cruiseControl = accessor.ReadSingle(140);
                    bool blinkerLeft = accessor.ReadBoolean(152);
                    bool blinkerRight = accessor.ReadBoolean(153);
                    bool lightsLow = accessor.ReadBoolean(160);
                    bool lightsHigh = accessor.ReadBoolean(161);
                    bool lightsBeacon = accessor.ReadBoolean(164);
                    float wearEngine = accessor.ReadSingle(180);
                    float wearTransmission = accessor.ReadSingle(184);
                    float wearCabin = accessor.ReadSingle(188);
                    float wearChassis = accessor.ReadSingle(192);
                    float wearWheels = accessor.ReadSingle(196);
                    float wearTrailer = accessor.ReadSingle(200);
                    float navDistance = accessor.ReadSingle(208);

                    var payload = new
                    {
                        game = new { connected = true, paused = paused },
                        truck = new
                        {
                            speed = speed,
                            engineRpm = engineRpm,
                            engineRpmMax = engineRpmMax > 0 ? engineRpmMax : 2500,
                            gear = gear,
                            fuel = fuel,
                            fuelCapacity = fuelCapacity > 0 ? fuelCapacity : 1000,
                            waterTemperature = waterTemp,
                            oilTemperature = oilTemp,
                            oilPressure = oilPressure,
                            airPressure = airPressure,
                            parkBrake = parkBrake,
                            motorBrake = motorBrake,
                            retarderBrake = retarder,
                            cruiseControlSpeed = cruiseControl,
                            blinkerLeftOn = blinkerLeft,
                            blinkerRightOn = blinkerRight,
                            lightsBeamLowOn = lightsLow,
                            lightsBeamHighOn = lightsHigh,
                            lightsBeaconOn = lightsBeacon,
                            wearEngine = wearEngine,
                            wearTransmission = wearTransmission,
                            wearCabin = wearCabin,
                            wearChassis = wearChassis,
                            wearWheels = wearWheels
                        },
                        trailer = new { wear = wearTrailer },
                        navigation = new { estimatedDistance = navDistance }
                    };

                    return JsonSerializer.Serialize(payload);
                }
                catch { }
            }

            return "{\"game\":{\"connected\":false}}";
        }
    }

    public static class DashboardHtml
    {
        public static string GetHtml() => @"<!DOCTYPE html>
<html lang='es'>
<head>
<meta charset='UTF-8'>
<meta name='viewport' content='width=device-width, initial-scale=1.0'>
<title>Telemetría 2.0 - FederalVTC</title>
<style>
  :root {
    --celeste: #75aadb;
    --celeste-brillo: #99ccf8;
    --blanco: #ffffff;
    --dorado: #f4c430;
    --fondo: #09121d;
    --panel: rgba(15, 29, 46, 0.92);
  }
  * { box-sizing: border-box; margin: 0; padding: 0; font-family: 'Segoe UI', Tahoma, sans-serif; }
  body {
    background: radial-gradient(circle at top, #152b44 0%, var(--fondo) 100%);
    color: var(--blanco);
    min-height: 100vh;
    padding: 15px;
    display: flex;
    flex-direction: column;
    align-items: center;
  }
  .marco-argentino {
    width: 100%;
    max-width: 1280px;
    background: var(--panel);
    border: 3px solid var(--celeste);
    border-radius: 18px;
    padding: 18px;
    box-shadow: 0 0 30px rgba(117,170,219,0.25), inset 0 0 0 2px var(--dorado);
    position: relative;
  }
  .header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    border-bottom: 2px solid var(--celeste);
    padding-bottom: 12px;
    margin-bottom: 18px;
  }
  .titulo { font-size: 1.5rem; font-weight: 800; letter-spacing: 2px; color: var(--blanco); }
  .subtitulo { color: var(--dorado); font-size: 0.85rem; font-weight: 700; letter-spacing: 1.5px; }
  .badge-estado { padding: 6px 14px; border-radius: 20px; font-weight: bold; font-size: 0.85rem; }
  .online { background: rgba(74, 222, 128, 0.2); color: #4ade80; border: 1px solid #4ade80; }
  .offline { background: rgba(248, 113, 113, 0.2); color: #f87171; border: 1px solid #f87171; }

  .grid-principal {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
    gap: 15px;
    margin-bottom: 18px;
  }
  .caja {
    background: rgba(8, 16, 26, 0.85);
    border: 1px solid var(--celeste);
    border-radius: 12px;
    padding: 15px;
    text-align: center;
    position: relative;
  }
  .caja::after {
    content: '❖';
    position: absolute;
    top: 6px;
    right: 10px;
    color: var(--dorado);
    font-size: 11px;
  }
  .etiqueta { color: var(--celeste); font-size: 0.8rem; text-transform: uppercase; letter-spacing: 1px; margin-bottom: 6px; }
  .valor-grande { font-size: 2.8rem; font-weight: 900; color: var(--blanco); line-height: 1; }
  .unidad { font-size: 0.95rem; color: var(--celeste-brillo); margin-left: 4px; }

  .barra-progreso { width: 100%; height: 8px; background: #1e293b; border-radius: 4px; margin-top: 10px; overflow: hidden; }
  .barra-relleno { height: 100%; background: linear-gradient(90deg, var(--celeste), var(--blanco)); width: 0%; transition: width 0.2s; }

  .testigos {
    display: flex;
    flex-wrap: wrap;
    gap: 10px;
    justify-content: center;
    background: rgba(0,0,0,0.35);
    padding: 12px;
    border-radius: 10px;
    border: 1px solid rgba(117,170,219,0.3);
    margin-bottom: 18px;
  }
  .luz {
    padding: 6px 12px;
    border-radius: 6px;
    font-size: 0.8rem;
    font-weight: bold;
    background: #162231;
    color: #475569;
    border: 1px solid #1e293b;
  }
  .luz.activa-verde { background: rgba(34, 197, 94, 0.25); color: #4ade80; border-color: #4ade80; }
  .luz.activa-azul { background: rgba(56, 189, 248, 0.25); color: #38bdf8; border-color: #38bdf8; }
  .luz.activa-ambar { background: rgba(250, 204, 21, 0.25); color: #facc15; border-color: #facc15; }
  .luz.activa-roja { background: rgba(239, 68, 68, 0.25); color: #f87171; border-color: #f87171; }

  .grid-secundario {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
    gap: 10px;
  }
  .mini-caja {
    background: rgba(10, 20, 32, 0.7);
    border: 1px solid rgba(117,170,219,0.4);
    border-radius: 8px;
    padding: 10px;
    display: flex;
    justify-content: space-between;
    font-size: 0.85rem;
  }
  .mini-caja span:first-child { color: var(--celeste); }
  .mini-caja span:last-child { font-weight: bold; color: var(--blanco); }
</style>
</head>
<body>
<div class='marco-argentino'>
  <div class='header'>
    <div>
      <div class='titulo'>FEDERAL VTC • TELEMETRÍA 2.0</div>
      <div class='subtitulo'>❖ TABLERO DIGITAL EDICIÓN ARGENTINA ❖</div>
    </div>
    <div id='estado' class='badge-estado offline'>DESCONECTADO</div>
  </div>

  <div class='grid-principal'>
    <div class='caja'>
      <div class='etiqueta'>Velocidad</div>
      <div class='valor-grande'><span id='vel'>0</span><span class='unidad'>km/h</span></div>
      <div class='etiqueta' style='margin-top:8px'>Crucero: <span id='crucero' style='color:#fff'>OFF</span></div>
    </div>
    <div class='caja'>
      <div class='etiqueta'>Motor (RPM)</div>
      <div class='valor-grande' id='rpm'>0</div>
      <div class='barra-progreso'><div id='barra-rpm' class='barra-relleno'></div></div>
    </div>
    <div class='caja'>
      <div class='etiqueta'>Marcha</div>
      <div class='valor-grande' id='marcha' style='color:var(--dorado)'>N</div>
      <div class='etiqueta' style='margin-top:8px'>Retarder: <span id='retarder' style='color:#fff'>0</span></div>
    </div>
    <div class='caja'>
      <div class='etiqueta'>Combustible</div>
      <div class='valor-grande'><span id='comb'>0</span><span class='unidad'>L</span></div>
      <div class='barra-progreso'><div id='barra-comb' class='barra-relleno'></div></div>
    </div>
  </div>

  <div class='testigos'>
    <div id='luz-izq' class='luz'>◀ GIRO IZQ</div>
    <div id='luz-der' class='luz'>GIRO DER ▶</div>
    <div id='luz-baja' class='luz'>LUCES BAJAS</div>
    <div id='luz-alta' class='luz'>LUCES ALTAS</div>
    <div id='luz-baliza' class='luz'>BALIZA TECHO</div>
    <div id='luz-mano' class='luz'>FRENO MANO</div>
    <div id='luz-motor' class='luz'>FRENO MOTOR</div>
  </div>

  <div class='grid-secundario'>
    <div class='mini-caja'><span>Temp. Agua</span><span id='agua'>0 °C</span></div>
    <div class='mini-caja'><span>Temp. Aceite</span><span id='aceite-t'>0 °C</span></div>
    <div class='mini-caja'><span>Presión Aire</span><span id='aire'>0 PSI</span></div>
    <div class='mini-caja'><span>Daño Motor</span><span id='d-motor'>0%</span></div>
    <div class='mini-caja'><span>Daño Transmisión</span><span id='d-trans'>0%</span></div>
    <div class='mini-caja'><span>Daño Chasis</span><span id='d-chasis'>0%</span></div>
    <div class='mini-caja'><span>Daño Ruedas</span><span id='d-ruedas'>0%</span></div>
    <div class='mini-caja'><span>Daño Carga</span><span id='d-carga'>0%</span></div>
  </div>
</div>

<script>
async function actualizar() {
  try {
    const res = await fetch('/api/ets2/telemetry');
    const data = await res.json();
    const est = document.getElementById('estado');

    if (!data.game || !data.game.connected) {
      est.innerText = 'SIMULADOR DESCONECTADO';
      est.className = 'badge-estado offline';
      return;
    }

    est.innerText = 'EN RUTA • CONECTADO';
    est.className = 'badge-estado online';

    const t = data.truck;
    document.getElementById('vel').innerText = Math.abs(Math.round(t.speed * 3.6));
    document.getElementById('crucero').innerText = t.cruiseControlSpeed > 0 ? Math.round(t.cruiseControlSpeed * 3.6) + ' km/h' : 'OFF';
    document.getElementById('rpm').innerText = Math.round(t.engineRpm);
    document.getElementById('barra-rpm').style.width = Math.min(100, (t.engineRpm / t.engineRpmMax) * 100) + '%';

    let m = 'N';
    if (t.gear > 0) m = 'M' + t.gear;
    else if (t.gear < 0) m = 'R' + Math.abs(t.gear);
    document.getElementById('marcha').innerText = m;
    document.getElementById('retarder').innerText = t.retarderBrake;

    document.getElementById('comb').innerText = Math.round(t.fuel);
    document.getElementById('barra-comb').style.width = Math.min(100, (t.fuel / t.fuelCapacity) * 100) + '%';

    setLuz('luz-izq', t.blinkerLeftOn, 'activa-verde');
    setLuz('luz-der', t.blinkerRightOn, 'activa-verde');
    setLuz('luz-baja', t.lightsBeamLowOn, 'activa-verde');
    setLuz('luz-alta', t.lightsBeamHighOn, 'activa-azul');
    setLuz('luz-baliza', t.lightsBeaconOn, 'activa-ambar');
    setLuz('luz-mano', t.parkBrake, 'activa-roja');
    setLuz('luz-motor', t.motorBrake, 'activa-ambar');

    document.getElementById('agua').innerText = Math.round(t.waterTemperature) + ' °C';
    document.getElementById('aceite-t').innerText = Math.round(t.oilTemperature) + ' °C';
    document.getElementById('aire').innerText = Math.round(t.airPressure) + ' PSI';

    document.getElementById('d-motor').innerText = Math.round(t.wearEngine * 100) + '%';
    document.getElementById('d-trans').innerText = Math.round(t.wearTransmission * 100) + '%';
    document.getElementById('d-chasis').innerText = Math.round(t.wearChassis * 100) + '%';
    document.getElementById('d-ruedas').innerText = Math.round(t.wearWheels * 100) + '%';
    document.getElementById('d-carga').innerText = Math.round((data.trailer ? data.trailer.wear : 0) * 100) + '%';
  } catch (e) {}
}

function setLuz(id, activa, clase) {
  const el = document.getElementById(id);
  el.className = activa ? 'luz ' + clase : 'luz';
}

setInterval(actualizar, 150);
</script>
</body>
</html>";
    }
}
