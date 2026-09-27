using System.Diagnostics;
using System.Drawing;
using System.IO;
using AnimatedWallPaper.Services;
using Forms = System.Windows.Forms;

// Interactive ocean comparison fixture. Does not attach to Explorer, change preferences,
// initialize audio or replace the installed HYPNIX package.
internal static class OceanProofWindow
{
    public static void Show(string output, bool selfCheck = false, bool startMoon = false)
    {
        Directory.CreateDirectory(output);
        using var form = new Forms.Form
        {
            Text = "HYPNIX · Oceano — ciclo do Sol e da Lua",
            ClientSize = new Size(1200, 760), MinimumSize = new Size(820, 550),
            StartPosition = Forms.FormStartPosition.CenterScreen,
            BackColor = Color.FromArgb(18, 22, 29), ForeColor = Color.Gainsboro,
            Font = new Font("Segoe UI", 10), KeyPreview = true
        };
        var toolbar = new Forms.FlowLayoutPanel
        {
            Dock = Forms.DockStyle.Top, Height = 86, Padding = new Forms.Padding(12, 10, 12, 6),
            BackColor = Color.FromArgb(25, 30, 39), WrapContents = true,
            AutoSize = true, AutoSizeMode = Forms.AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0,86)
        };
        var status = new Forms.Label
        {
            Dock = Forms.DockStyle.Bottom, Height = 32, Padding = new Forms.Padding(12, 7, 0, 0),
            Text = "Preparando oceano…", ForeColor = Color.Silver
        };
        var surface = new Forms.Panel { Dock = Forms.DockStyle.Fill, BackColor = Color.Black };
        form.Controls.Add(surface); form.Controls.Add(status); form.Controls.Add(toolbar);
        Forms.ComboBox Choice(string[] labels, int selected, int width)
        {
            var combo = new Forms.ComboBox { DropDownStyle = Forms.ComboBoxStyle.DropDownList, Width = width };
            combo.Items.AddRange(labels); combo.SelectedIndex = selected; toolbar.Controls.Add(combo); return combo;
        }
        var showSun = new Forms.Button { Text = "Ver Sol", Width = 95, Height = 30 };
        var showMoon = new Forms.Button { Text = "Ver Lua", Width = 95, Height = 30 };
        toolbar.Controls.Add(showSun); toolbar.Controls.Add(showMoon);
        var light = Choice(["Pôr do sol", "Dia", "Lua", "Nublado"], startMoon ? 2 : 0, 125);
        using var hints = new Forms.ToolTip();
        hints.SetToolTip(showSun, "Ir ao nascer do Sol, com o astro visível sobre o horizonte.");
        hints.SetToolTip(showMoon, "Ir ao nascer da Lua, com sua fase real na data selecionada.");
        hints.SetToolTip(light, "Presets fixos: disponíveis quando o Ciclo do céu está desligado.");
        var sea = Choice(["Mar calmo", "Mar moderado", "Mar agitado"], 1, 145);
        var quality = Choice(["Leve", "Equilibrado", "Alto"], 1, 130);
        var view = Choice(["Com horizonte", "Perto da água"], 0, 155);
        var speed = Choice(["Velocidade 0,5×", "Velocidade 1×", "Velocidade 1,5×"], 1, 155);
        var model = Choice(["Ondas novas", "Ondas anteriores"], 0, 155);
        var atmosphere = Choice(["Céu refinado", "Céu estudo 03", "Iluminação anterior"], 0, 175);
        var phase = Choice(["Lua cheia", "Lua gibosa"], 0, 120);
        var bloom = new Forms.CheckBox { Text = "Brilho óptico", Checked = true, AutoSize = true, Margin = new Forms.Padding(6, 6, 6, 0) };
        toolbar.Controls.Add(bloom);
        var magnification = new Forms.CheckBox { Text = "Astros maiores no horizonte", Checked = true, AutoSize = true, Margin = new Forms.Padding(6,6,6,0) };
        toolbar.Controls.Add(magnification);
        var cycleEnabled = new Forms.CheckBox { Text = "Ciclo do céu", Checked = true, AutoSize = true, Margin = new Forms.Padding(6,6,6,0) };
        toolbar.Controls.Add(cycleEnabled);
        var cycleSpeed = Choice(["Céu em tempo real", "Dia em 2 horas", "Dia em 1 hora", "Dia em 20 min", "Dia em 10 min"],2,155);
        var composition = Choice(["Enquadramento cinema", "Vista geográfica"],0,175);
        var air = Choice(["Ar limpo", "Ar marítimo", "Névoa"],1,125);
        var date = new Forms.DateTimePicker { Format = Forms.DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm 'UTC'", Width=185,
            MinDate = new DateTime(1900,1,1), MaxDate = new DateTime(2100,12,31), Value = OceanCelestialSettings.Default.EpochUtc.UtcDateTime };
        toolbar.Controls.Add(date);
        var nowButton=new Forms.Button {Text="Agora UTC",Width=100,Height=30};
        toolbar.Controls.Add(nowButton);
        var eventChoice=Choice(["Nascer do Sol", "Pôr do Sol", "Nascer da Lua", "Pôr da Lua"],0,145);
        var seekEvent=new Forms.Button { Text="Ir ao horizonte", Width=135, Height=30 };
        toolbar.Controls.Add(seekEvent);
        Forms.NumericUpDown Number(string caption, decimal value, decimal min, decimal max)
        {
            toolbar.Controls.Add(new Forms.Label { Text=caption, AutoSize=true, Margin=new Forms.Padding(4,6,0,0) });
            var input=new Forms.NumericUpDown { Minimum=min, Maximum=max, DecimalPlaces=2, Value=value, Increment=1, Width=78 };
            toolbar.Controls.Add(input); return input;
        }
        var latitude=Number("Lat.",38.72m,-89.9m,89.9m);
        var longitude=Number("Lon.",-9.14m,-180,180);
        var heading=Number("Direção",90,0,359.99m);
        var pause = new Forms.Button { Text = "Pausar", Width = 90, Height = 30 };
        var capture = new Forms.Button { Text = "Guardar imagem", Width = 145, Height = 30 };
        toolbar.Controls.Add(pause); toolbar.Controls.Add(capture);

        var gate = new object();
        var clock = new OceanFrameClock();
        var celestialClock = new OceanCelestialClock(OceanCelestialSettings.Default.EpochUtc);
        var wall = Stopwatch.StartNew();
        var settings = new OceanSettings(Lighting: startMoon ? OceanLighting.Moon : OceanLighting.Sunset, Celestial: OceanCelestialSettings.Default);
        WallpaperRenderWorker? worker = null;
        OceanGpuRenderer? renderer = null;
        var userPaused = false;
        var rebuilding = true;
        var closing = false;
        var captureRequested = false;
        var serial = new SemaphoreSlim(1, 1);
        long frameCount = 0;
        string adapter = "", failure = "";
        var lastCount = 0L;
        var lastStatusTime = wall.Elapsed.TotalSeconds;
        using var resize = new Forms.Timer { Interval = 180 };
        using var statusTimer = new Forms.Timer { Interval = 1000 };
        using var pacer = new OceanFramePacer();

        void UpdatePause()
        {
            lock (gate)
            {
                var now=wall.Elapsed.TotalSeconds;
                var paused=userPaused || rebuilding || form.WindowState==Forms.FormWindowState.Minimized;
                clock.SetPaused(paused,now); celestialClock.SetPaused(paused,now);
            }
            worker?.Signal();
        }
        void UpdateSettings()
        {
            lock (gate)
            {
                var rate = speed.SelectedIndex switch { 0 => .5f, 2 => 1.5f, _ => 1f };
                clock.SetSpeed(rate, wall.Elapsed.TotalSeconds);
                var skyRate=cycleSpeed.SelectedIndex switch { 0=>1,1=>12,3=>72,4=>144,_=>24 };
                celestialClock.SetRate(skyRate,wall.Elapsed.TotalSeconds);
                settings = new((OceanLighting)light.SelectedIndex, sea.SelectedIndex switch { 0 => 0, 2 => 1, _ => .65f },
                    rate, (OceanQuality)quality.SelectedIndex, view.SelectedIndex == 0, (OceanSurface)model.SelectedIndex,
                    atmosphere.SelectedIndex != 2, atmosphere.SelectedIndex == 0, phase.SelectedIndex == 1, bloom.Checked, magnification.Checked,
                    cycleEnabled.Checked ? new OceanCelestialSettings(new DateTimeOffset(DateTime.SpecifyKind(date.Value,DateTimeKind.Utc)),skyRate,
                        (double)latitude.Value,(double)longitude.Value,(double)heading.Value,composition.SelectedIndex==0 ? OceanSkyComposition.Cinematic : OceanSkyComposition.Geographic,(OceanAir)air.SelectedIndex) : null);
                light.Enabled=phase.Enabled=!cycleEnabled.Checked;
            }
            worker?.Signal();
        }
        async Task StopAsync()
        {
            var previous = worker; worker = null;
            if (previous is null) return;
            var stopped = await Task.Run(() => previous.Stop(TimeSpan.FromSeconds(10)));
            if (!stopped) throw new TimeoutException("Ocean render worker did not stop.");
            previous.Dispose();
        }
        async Task RebuildAsync()
        {
            await serial.WaitAsync();
            try
            {
                if (closing || form.WindowState == Forms.FormWindowState.Minimized) return;
                rebuilding = true; UpdatePause();
                await StopAsync();
                var width = Math.Max(2, surface.ClientSize.Width);
                var height = Math.Max(2, surface.ClientSize.Height);
                var handle = surface.Handle;
                var frameCost = 0d;
                void Draw()
                {
                    var started = Stopwatch.GetTimestamp();
                    double time; DateTimeOffset celestialUtc; OceanSettings snapshot; bool save;
                    lock (gate)
                    {
                        var now=wall.Elapsed.TotalSeconds;
                        time = clock.Advance(now); celestialUtc=celestialClock.Advance(now);
                        snapshot = settings;
                        save = captureRequested; captureRequested = false;
                    }
                    renderer!.Render(time, snapshot, present: true, celestialUtc: celestialUtc);
                    if (save) OceanRenderChecks.Save(renderer.Pixels(),
                        Path.Combine(output, $"ocean-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png"), width, height);
                    Interlocked.Increment(ref frameCount);
                    frameCost = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                }
                worker = new WallpaperRenderWorker(() =>
                {
                    renderer = new OceanGpuRenderer(handle, width, height);
                    adapter = renderer.AdapterName;
                    Draw();
                }, Draw, () =>
                {
                    lock (gate) { if (clock.IsPaused) return Timeout.Infinite; }
                    pacer.Wait(1000d / 30 - frameCost);
                    return 0;
                }, () => { renderer?.Dispose(); renderer = null; });
                await worker.StartAsync(TimeSpan.FromSeconds(15));
                if (closing) { await StopAsync(); return; }
                rebuilding = false; failure = ""; UpdatePause();
            }
            catch (Exception exception)
            {
                failure = exception.Message;
                File.WriteAllText(Path.Combine(output, "ocean-preview-error.txt"), exception.ToString());
                status.Text = "Falha na prévia: " + failure;
                await StopAsync();
            }
            finally { serial.Release(); }
        }

        foreach (var combo in new[] { light, sea, quality, view, speed, model, atmosphere, phase, cycleSpeed, composition, air }) combo.SelectedIndexChanged += (_, _) => UpdateSettings();
        foreach(var input in new[] { latitude,longitude,heading }) input.ValueChanged+=(_,_)=>UpdateSettings();
        cycleEnabled.CheckedChanged+=(_,_)=>UpdateSettings();
        date.ValueChanged+=(_,_)=>
        {
            lock(gate) celestialClock.Seek(new DateTimeOffset(DateTime.SpecifyKind(date.Value,DateTimeKind.Utc)),wall.Elapsed.TotalSeconds);
            UpdateSettings();
        };
        nowButton.Click+=(_,_)=>date.Value=DateTime.UtcNow;
        void ShowBody(bool moon)
        {
            cycleEnabled.Checked=true;
            atmosphere.SelectedIndex=0;
            composition.SelectedIndex=0;
            view.SelectedIndex=0;
            UpdateSettings();
            eventChoice.SelectedIndex=moon ? 2 : 0;
            var utc=OceanCelestialModel.FindEvent(settings.Celestial!,new DateTimeOffset(DateTime.SpecifyKind(date.Value,DateTimeKind.Utc)),moon,true);
            if(utc is { } found) date.Value=found.AddMinutes(20).UtcDateTime;
            else status.Text="Não há esse nascer nos próximos dois dias para o local selecionado.";
        }
        showSun.Click+=(_,_)=>ShowBody(false);
        showMoon.Click+=(_,_)=>ShowBody(true);
        seekEvent.Click+=(_,_)=>
        {
            var config=settings.Celestial ?? OceanCelestialSettings.Default;
            var utc=OceanCelestialModel.FindEvent(config,new DateTimeOffset(DateTime.SpecifyKind(date.Value,DateTimeKind.Utc)),eventChoice.SelectedIndex>=2,eventChoice.SelectedIndex%2==0);
            if(utc is { } found) date.Value=found.AddMinutes(eventChoice.SelectedIndex%2==0 ? 3 : -12).UtcDateTime;
            else status.Text="Não há esse evento nos próximos dois dias para o local selecionado.";
        };
        bloom.CheckedChanged += (_, _) => UpdateSettings();
        magnification.CheckedChanged += (_, _) => UpdateSettings();
        pause.Click += (_, _) => { userPaused = !userPaused; pause.Text = userPaused ? "Continuar" : "Pausar"; UpdatePause(); };
        capture.Click += (_, _) => { lock (gate) captureRequested = true; worker?.Signal(); };
        form.KeyDown += (_, e) => { if (e.KeyCode == Forms.Keys.Space) pause.PerformClick(); if (e.KeyCode == Forms.Keys.Escape) form.Close(); };
        surface.Paint += (_, _) => worker?.Signal();
        surface.Resize += (_, _) => { resize.Stop(); rebuilding = true; UpdatePause(); resize.Start(); };
        resize.Tick += async (_, _) => { resize.Stop(); await RebuildAsync(); };
        form.Resize += (_, _) => UpdatePause();
        statusTimer.Tick += (_, _) =>
        {
            if (!rebuilding && worker?.Completion.IsCompleted == true && failure.Length == 0)
                failure = "O renderizador parou; consultar o log da aplicação.";
            if (failure.Length > 0) { status.Text = "Falha na prévia: " + failure; return; }
            var now = wall.Elapsed.TotalSeconds;
            var count = Interlocked.Read(ref frameCount);
            var fps = (count - lastCount) / (now - lastStatusTime);
            lastCount = count; lastStatusTime = now;
            DateTimeOffset skyTime; lock(gate) skyTime=celestialClock.Utc;
            status.Text = $"{(userPaused ? "Pausado" : $"{fps:F0} FPS")}  ·  {(cycleEnabled.Checked ? skyTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'")+"  ·  " : "")}{adapter}  ·  Espaço: pausar  ·  Esc: fechar";
        };
        form.Shown += async (_, _) => { UpdateSettings(); if(startMoon) showMoon.PerformClick(); resize.Stop(); await RebuildAsync(); statusTimer.Start(); };
        form.FormClosing += async (_, e) =>
        {
            if (closing) return;
            e.Cancel = true; closing = true; resize.Stop(); statusTimer.Stop();
            await serial.WaitAsync();
            try { await StopAsync(); }
            finally { serial.Release(); form.Close(); }
        };

        using var checkTimer = new Forms.Timer { Interval = 700 };
        var step = 0;
        if (selfCheck)
        {
            checkTimer.Tick += (_, _) =>
            {
                if (rebuilding && failure.Length == 0) return;
                if (step == 0) pause.PerformClick();
                if (step == 1) { eventChoice.SelectedIndex=2; seekEvent.PerformClick(); form.ClientSize = new Size(850, 650); }
                if (step == 2) { model.SelectedIndex = 1; atmosphere.SelectedIndex = 1; }
                if (step == 3) { model.SelectedIndex = 0; atmosphere.SelectedIndex = 0; pause.PerformClick(); }
                if (step == 4) { cycleSpeed.SelectedIndex=4; speed.SelectedIndex=0; composition.SelectedIndex=1; air.SelectedIndex=2; }
                if (step == 5) { composition.SelectedIndex=0; air.SelectedIndex=1; cycleSpeed.SelectedIndex=2; capture.PerformClick(); }
                if (step == 6) { showSun.PerformClick(); cycleEnabled.Checked=false; }
                if (step == 7) { showMoon.PerformClick(); if(!cycleEnabled.Checked || eventChoice.SelectedIndex!=2) throw new InvalidOperationException("Moon shortcut did not enable the lunar cycle view."); }
                if (step++ == 8) { checkTimer.Stop(); form.Close(); }
            };
            form.Shown += (_, _) => checkTimer.Start();
        }
        Forms.Application.Run(form);
        if (failure.Length > 0) throw new InvalidOperationException(failure);
        if (selfCheck && frameCount < 5) throw new InvalidOperationException("Ocean window did not present frames.");
        Console.WriteLine($"Ocean preview closed cleanly after {frameCount} frames.");
    }
}
