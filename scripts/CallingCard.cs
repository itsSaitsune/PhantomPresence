using System.Diagnostics;
using System.Drawing.Drawing2D;
namespace P5XboxPresence;
sealed class CallingCard : Form
{
    static readonly Color Red=Color.FromArgb(220,16,43),Ink=Color.FromArgb(16,16,20);
    readonly string folder=RuntimePaths.DataFolder;
    readonly TextBox appId=new(),gamePath=new(),image=new(),hover=new();
    readonly CheckBox auto=new(),experimental=new();
    readonly Label status=new();
    readonly NotifyIcon tray;
    readonly Image? morgana;
    readonly System.Windows.Forms.Timer timer=new() { Interval=2000 };
    static readonly System.Drawing.Text.PrivateFontCollection displayFonts=LoadDisplayFonts();
    readonly EventWaitHandle launchRequest=new(false,EventResetMode.AutoReset,"Local\\P5XboxPresence.Launch");
    Config config;
    Process? worker;
    bool quit,launchRequested,hideAfterConnection,gameSeen,sessionActive;
    DateTime launchDeadline;
    internal CallingCard(bool startup,bool preview=false)
    {
        launchRequested=startup;
        config=Settings.Load();
        Text="Phantom Presence · Persona 5 Royal";
        ClientSize=new Size(1040,740); MinimumSize=MaximumSize=Size;
        StartPosition=FormStartPosition.CenterScreen; BackColor=Red; ForeColor=Color.White;
        Font=new Font("Segoe UI",10); DoubleBuffered=true; AutoScaleMode=AutoScaleMode.Dpi;
        Icon=new Icon(Path.Combine(AppContext.BaseDirectory,"Assets","app.ico"));
        var portrait=Path.Combine(AppContext.BaseDirectory,"Assets","morgana.png");
        if(File.Exists(portrait)) morgana=Image.FromFile(portrait);
        var left=new Panel { Location=new(32,180),Size=new(976,450),BackColor=Ink };
        Controls.Add(left);
        AddLabel(left,"01 / YOUR GAME",20,15,true);
        AddLabel(left,"P5R.exe path",20,52);
        Field(left,gamePath,20,77,831);
        Button(left,"BROWSE",861,76,95,32,()=>Browse());
        AddLabel(left,"Paste a path, browse, or leave blank to detect a running game.",20,114);
        AddLabel(left,"02 / READY FOR DISCORD",20,155,true);
        var steps=AddLabel(left,"1. Open Discord.\n\n2. Launch the game with Connect Now\n    (or use the Play Persona 5 Royal shortcut).\n\n3. Done! Your Phantom Presence should be visible!",20,202); steps.Size=new(930,148); steps.Font=new("Segoe UI",12);
        auto.Text="Allow autostart"; auto.Location=new(20,354); auto.Size=new(270,28); auto.ForeColor=Color.White; auto.Checked=config.Autostart;
        left.Controls.Add(auto);
        experimental.Text="Experimental Steam / modified builds (untested; limited data)";
        experimental.Location=new(20,416); experimental.Size=new(900,28); experimental.ForeColor=Color.White; experimental.Checked=config.ExperimentalSteam;
        left.Controls.Add(experimental);
        AddLabel(left,"Save settings to create a game + presence shortcut in this app folder. Closes with the game.",20,390);
        Button(this,"SAVE SETTINGS",32,650,182,46,()=>Save());
        Button(this,"CONNECT NOW",227,650,182,46,()=>ConnectNow());
        Button(this,"STOP PRESENCE",422,650,182,46,()=>StopWorker());
        Button(this,"HIDE TO TRAY",665,650,165,46,()=>HideToTray());
        Button(this,"QUIT",843,650,165,46,()=>Exit());
        status.Location=new(32,710); status.Size=new(980,26); status.Font=new("Segoe UI",10); status.Text="Not connected"; Controls.Add(status);
        appId.Text=config.ApplicationId; gamePath.Text=config.Path; image.Text=config.Image; hover.Text=config.ImageText;
        if(string.IsNullOrWhiteSpace(gamePath.Text)) DetectPath();
        var menu=new ContextMenuStrip();
        menu.Items.Add("Open calling card",null,(_,_)=>{ Show(); WindowState=FormWindowState.Normal; Activate(); });
        menu.Items.Add("Connect now",null,(_,_)=>ConnectNow());
        menu.Items.Add("Stop presence",null,(_,_)=>StopWorker());
        menu.Items.Add("Quit",null,(_,_)=>Exit());
        tray=new NotifyIcon { Icon=Icon,Text="Phantom Presence · waiting for P5R",Visible=!preview,ContextMenuStrip=menu };
        tray.DoubleClick+=(_,_)=>{ Show(); WindowState=FormWindowState.Normal; Activate(); };
        timer.Tick+=(_,_)=>Tick(); if(!preview) timer.Start();
        Shown+=(_,_)=>
        {
            if(preview) return;
            if(launchRequested && config.SetupComplete) { Hide(); ConnectNow(); return; }
            if(config.SetupComplete && config.HideToTrayOnLaunch) Hide();
            status.Text="Not connected";
            Tick();
        };
        FormClosing+=(_,e)=>
        {
            if(!quit && sessionActive) { e.Cancel=true; HideToTray(); return; }
            if(EventWaitHandle.TryOpenExisting("Local\\P5XboxPresence.Stop",out var signal)) { using(signal) signal.Set(); }
        };
        FormClosed+=(_,_)=>{ timer.Dispose(); tray.Dispose(); morgana?.Dispose(); launchRequest.Dispose(); worker?.Dispose(); };
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias;
        using var black=new SolidBrush(Ink); using var white=new SolidBrush(Color.White);
        for(int i=0;i<8;i++)
        {
            using var pen=new Pen(Color.FromArgb(50,0,0,0),20);
            g.DrawEllipse(pen,700-i*50,-210-i*30,390+i*100,390+i*60);
        }
        var saved=g.Save(); g.TranslateTransform(34,28); g.RotateTransform(-4);
        g.FillPolygon(white,new[]{new Point(0,0),new Point(495,8),new Point(510,62),new Point(9,68)});
        using var title=DisplayFont(66); g.DrawString("Phantom",title,black,10,-6); g.Restore(saved);
        saved=g.Save(); g.TranslateTransform(140,91); g.RotateTransform(2);
        g.FillRectangle(black,-10,-3,450,62); g.DrawString("Presence",title,white,4,-6); g.Restore(saved);
        // Draw the supplied portrait as a UI asset; crop to the head/scarf in
        // the layout while keeping the original PNG unchanged on disk.
        var bubble=new[]{new Point(650,32),new Point(856,22),new Point(880,39),new Point(1008,43),new Point(1019,128),new Point(859,139),new Point(821,157),new Point(645,137),new Point(614,113),new Point(639,91)};
        g.FillPolygon(white,bubble);
        var inside=new[]{new Point(654,39),new Point(854,29),new Point(878,46),new Point(1002,50),new Point(1011,122),new Point(857,132),new Point(820,149),new Point(648,130),new Point(625,112),new Point(646,94)};
        g.FillPolygon(black,inside);
        using var tagFont=new Font("Segoe UI",15,FontStyle.Bold);
        using var textLayout=new StringFormat { Alignment=StringAlignment.Near,LineAlignment=StringAlignment.Center };
        g.DrawString("Woah, looking\ncool Joker!",tagFont,white,new RectangleF(660,49,225,79),textLayout);
        if(morgana!=null)
        {
            g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            var cropHeight=Math.Min(morgana.Height,morgana.Width*1.04f);
            g.DrawImage(morgana,new RectangleF(879,0,155,155*cropHeight/morgana.Width),new RectangleF(0,0,morgana.Width,cropHeight),GraphicsUnit.Pixel);
        }
    }
    bool Save()
    {
        try
        {
            var id=appId.Text.Trim(); var path=gamePath.Text.Trim().Trim('"'); var art=image.Text.Trim();
            if(id.Length is <17 or >22 || !id.All(char.IsAsciiDigit)) throw new Exception("Enter your numeric Discord Application ID.");
            if(!Settings.ValidGamePath(path)) throw new Exception("Choose an existing P5R.exe, or leave the path blank for automatic detection.");
            if(!Settings.ValidImage(art)) throw new Exception("Use a Discord image asset key or a public HTTPS image URL.");
            if(hover.Text.Length>128) throw new Exception("Image hover text must be at most 128 characters.");
            var next=new Config { ApplicationId=id,Path=path,Image=art,ImageText=hover.Text.Trim(),Autostart=auto.Checked,ExperimentalSteam=experimental.Checked,SetupComplete=true,HideToTrayOnLaunch=config.HideToTrayOnLaunch };
            Settings.SyncAutostart(next);
            Settings.Save(next); config=next;
            if(worker is { HasExited:false }) { StopWorker(false); StartWorker(); }
            status.Text=worker is { HasExited:false }?status.Text:"Not connected";
            return true;
        }
        catch(Exception ex) { status.Text="Error: "+ex.Message; return false; }
    }
    void DetectPath()
    {
        foreach(var p in Process.GetProcessesByName("P5R")) using(p) { try { gamePath.Text=p.MainModule?.FileName??""; } catch { } }
    }
    void Browse()
    {
        // ValidateNames=false maps to FOS_NOVALIDATE: select the path without
        // testing read access to Xbox's protected executable. Validate it ourselves.
        using var dialog=new OpenFileDialog {
            Title="Choose Persona 5 Royal",Filter="Persona 5 Royal|P5R.exe",
            ValidateNames=false,CheckFileExists=false,CheckPathExists=true,
            Multiselect=false,RestoreDirectory=true
        };
        if(dialog.ShowDialog(this)!=DialogResult.OK) return;
        var selected=dialog.FileName;
        if(!Settings.ValidGamePath(selected)) { status.Text="Error: Select an existing P5R.exe file."; return; }
        gamePath.Text=selected;
    }
    void Tick()
    {
        if(launchRequest.WaitOne(0)) ConnectNow();
        if(worker!=null && worker.HasExited)
        {
            var failed=worker.ExitCode!=0; worker.Dispose(); worker=null;
            status.Text="Not connected";
            if(failed)
            {
                status.Text=PresenceStatus.Display(RuntimeStatus.Text);
                if(!status.Text.StartsWith("Error:",StringComparison.Ordinal)) status.Text="Error: Presence could not start.";
            }
        }
        if(sessionActive)
        {
            var running=GameRunning();
            if(running) gameSeen=true;
            else if(gameSeen) { Exit(); return; }
            else if(DateTime.UtcNow>launchDeadline)
            {
                sessionActive=false; StopWorker(false);
                status.Text="Error: The game did not start within two minutes.";
                Show(); WindowState=FormWindowState.Normal; Activate();
            }
        }
        if(worker!=null)
        {
            try
            {
                status.Text=PresenceStatus.Display(RuntimeStatus.Text);
                tray.Text="Phantom Presence · "+(status.Text=="Connected"?"connected":"not connected");
                if(hideAfterConnection && status.Text=="Connected") { hideAfterConnection=false; Hide(); }
            }
            catch(IOException) { }
        }
        else tray.Text="Phantom Presence · waiting for P5R";
    }
    bool GameRunning()
    {
        foreach(var p in Process.GetProcessesByName("P5R")) using(p)
        {
            try { if(GamePaths.Match(p.MainModule?.FileName??"",config.Path)) return true; } catch { }
        }
        return false;
    }
    void StartWorker()
    {
        if(worker is { HasExited:false }) return;
        if(!config.SetupComplete) { status.Text="Error: Save your config first."; return; }
        try
        {
            RuntimeStatus.Text="Not connected";
            worker=Process.Start(new ProcessStartInfo(RuntimePaths.Executable,"--worker --exit-with-game") { UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=folder });
            hideAfterConnection=true;
            status.Text=GameRunning()?"Loading the save file..":"Not connected";
        }
        catch(Exception ex) { status.Text="Error: "+ex.Message; }
    }
    void ConnectNow()
    {
        if(!Save()) return;
        try
        {
            gameSeen=GameRunning();
            if(!gameSeen) GameLauncher.Launch(config.Path);
            sessionActive=true; launchDeadline=DateTime.UtcNow.AddMinutes(2);
            status.Text="Loading the save file..";
            StartWorker();
        }
        catch(Exception ex) { status.Text="Error: "+ex.Message; }
    }
    void StopWorker(bool disableAutostart=true)
    {
        if(EventWaitHandle.TryOpenExisting("Local\\P5XboxPresence.Stop",out var signal)) { using(signal) signal.Set(); }
        if(worker!=null) { if(!worker.WaitForExit(5500)) { status.Text="Error: Presence is still stopping."; return; } worker.Dispose(); worker=null; }
        hideAfterConnection=false;
        status.Text="Not connected";
    }
    void HideToTray() { Hide(); tray.ShowBalloonTip(2500,"Phantom Presence","Phantom Presence is in the tray. Double-click the icon to reopen.",ToolTipIcon.Info); }
    void Exit() { quit=true; if(EventWaitHandle.TryOpenExisting("Local\\P5XboxPresence.Stop",out var signal)) { using(signal) signal.Set(); } Close(); }
    static Label AddLabel(Control parent,string text,int x,int y,bool bold=false)
    {
        var label=new Label { Text=text,Location=new(x,y),Size=new(parent.Width-x-15,26),ForeColor=Color.White,UseCompatibleTextRendering=true,Font=bold?DisplayFont(18):new("Segoe UI",9) }; parent.Controls.Add(label); return label;
    }
    static void Field(Control parent,TextBox input,int x,int y,int width)
    {
        input.Location=new(x,y); input.Size=new(width,30); input.BorderStyle=BorderStyle.FixedSingle; input.BackColor=Color.White; input.ForeColor=Ink; input.Font=new("Segoe UI",11); parent.Controls.Add(input);
    }
    static void Button(Control parent,string text,int x,int y,int width,int height,Action click)
    {
        var button=new Button { Text=text,Location=new(x,y),Size=new(width,height),BackColor=Color.White,ForeColor=Ink,FlatStyle=FlatStyle.Flat,UseCompatibleTextRendering=true,Font=DisplayFont(width<100?15:18),Cursor=Cursors.Hand }; button.FlatAppearance.BorderSize=0; button.Click+=(_,_)=>click(); parent.Controls.Add(button);
    }
    static System.Drawing.Text.PrivateFontCollection LoadDisplayFonts()
    {
        var fonts=new System.Drawing.Text.PrivateFontCollection(); var file=Path.Combine(AppContext.BaseDirectory,"Fonts","Expose-Regular.otf");
        if(File.Exists(file)) { try { fonts.AddFontFile(file); } catch(ArgumentException) { } }
        return fonts;
    }
    static Font DisplayFont(float size)=>displayFonts.Families.Length>0?new(displayFonts.Families[0],size,FontStyle.Regular):new("Arial Black",size,FontStyle.Bold);
}
