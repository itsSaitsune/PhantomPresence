using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace P5XboxPresence;

sealed class Config
{
    [JsonPropertyName("applicationId")] public string ApplicationId { get; set; } = "1569451004389363884";
    [JsonPropertyName("image")] public string Image { get; set; } = "";
    [JsonPropertyName("imageText")] public string ImageText { get; set; } = "Persona 5 Royal";
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("experimentalSteam")] public bool ExperimentalSteam { get; set; }
    [JsonPropertyName("autostart")] public bool Autostart { get; set; }
    [JsonPropertyName("setupComplete")] public bool SetupComplete { get; set; }
    [JsonPropertyName("hideToTrayOnLaunch")] public bool HideToTrayOnLaunch { get; set; } = true;
}

static class Settings
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented=true, PropertyNameCaseInsensitive=true };
    internal static Config Load(string path) => File.Exists(path)
        ? JsonSerializer.Deserialize<Config>(File.ReadAllText(path),Json) ?? new() : new();

    internal static void Save(string path, Config config)
    {
        var temporary=path+".tmp";
        File.WriteAllText(temporary,JsonSerializer.Serialize(config,Json));
        File.Move(temporary,path,true);
    }

    internal static Config Setup(Config config, TextReader input, TextWriter output)
    {
        output.WriteLine("Persona 5 Royal presence — first-launch setup");
        output.WriteLine("The shared Discord identity is already configured. Press Enter to keep a displayed value.");
        if(string.IsNullOrWhiteSpace(config.Path))
        {
            foreach(var process in Process.GetProcessesByName("P5R"))
                using(process) { try { config.Path=process.MainModule?.FileName??""; } catch { } }
        }
        config.Path=Ask("Path to P5R.exe (blank = auto-detect running game)",config.Path,ValidGamePath,"Choose an existing P5R.exe file, or leave this blank for automatic detection.");
        while(true)
        {
            output.Write($"Create a shortcut that launches the game and presence together? [{config.Autostart.ToString().ToLowerInvariant()}]: ");
            var value=input.ReadLine() ?? throw new EndOfStreamException("Setup cancelled; no settings saved.");
            if(string.IsNullOrWhiteSpace(value)) break;
            if(value.Equals("yes",StringComparison.OrdinalIgnoreCase) || value.Equals("y",StringComparison.OrdinalIgnoreCase) || value.Equals("true",StringComparison.OrdinalIgnoreCase)) { config.Autostart=true; break; }
            if(value.Equals("no",StringComparison.OrdinalIgnoreCase) || value.Equals("n",StringComparison.OrdinalIgnoreCase) || value.Equals("false",StringComparison.OrdinalIgnoreCase)) { config.Autostart=false; break; }
            output.WriteLine("Enter true or false.");
        }
        config.SetupComplete=true;
        return config;

        string Ask(string title,string current,Func<string,bool> valid,string error)
        {
            while(true)
            {
                output.Write($"{title} [{current}]: ");
                var line=input.ReadLine() ?? throw new EndOfStreamException("Setup cancelled; no settings saved.");
                var result=string.IsNullOrWhiteSpace(line)?current:line.Trim().Trim('"');
                if(result=="-") result="";
                if(valid(result)) return result;
                output.WriteLine(error+" Enter - to clear an optional value.");
            }
        }
    }

    internal static bool ValidImage(string image) => image.Length==0 ||
        (Uri.TryCreate(image,UriKind.Absolute,out var uri) && uri.Scheme=="https") ||
        (image.Length<=256 && image.All(c=>char.IsAsciiLetterOrDigit(c) || c is '_' or '-'));
    internal static bool ValidGamePath(string path) => string.IsNullOrWhiteSpace(path) ||
        (System.IO.Path.IsPathFullyQualified(path) && System.IO.Path.GetFileName(path).Equals("P5R.exe",StringComparison.OrdinalIgnoreCase) && File.Exists(path));

    // A game launcher shortcut, never a Windows sign-in entry.
    internal static void SyncAutostart(Config config,string? shortcutFolder=null,string? executable=null)
    {
        shortcutFolder ??= AppContext.BaseDirectory;
        executable ??= System.IO.Path.Combine(AppContext.BaseDirectory,"P5XboxPresence.exe");
        var entry=System.IO.Path.Combine(shortcutFolder,"Play Persona 5 Royal.lnk");
        if(!config.Autostart) { if(File.Exists(entry)) File.Delete(entry); return; }
        if(!File.Exists(executable)) throw new IOException("Keep P5XboxPresence.exe in the app folder before enabling autostart.");
        var shellType=Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("Windows shortcuts are unavailable.");
        dynamic shell=Activator.CreateInstance(shellType)!;
        dynamic shortcut=shell.CreateShortcut(entry);
        try
        {
            shortcut.TargetPath=executable;
            shortcut.Arguments="--launch-game";
            shortcut.WorkingDirectory=System.IO.Path.GetDirectoryName(executable)!;
            shortcut.IconLocation=executable+",0";
            shortcut.Description="Launch Persona 5 Royal with Phantom Presence";
            shortcut.Save();
        }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut); System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
    }
    internal static void SelfTest()
    {
        var root=System.IO.Path.Combine(AppContext.BaseDirectory,"settings-test-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configPath=System.IO.Path.Combine(root,"config.json");
        var executable=System.IO.Path.Combine(root,"Presence with spaces.exe"); File.WriteAllText(executable,"");
        var startup=System.IO.Path.Combine(root,"Startup"); Directory.CreateDirectory(startup);
        var configured=Setup(new Config(),new StringReader("\ntrue\n"),TextWriter.Null);
        configured.Image="https://example.com/art.png";
        configured.ExperimentalSteam=true;
        Save(configPath,configured); var loaded=Load(configPath);
        if(!loaded.ExperimentalSteam || !loaded.SetupComplete || !loaded.Autostart || loaded.Image!="https://example.com/art.png") throw new Exception("First-launch config roundtrip failed.");
        SyncAutostart(loaded,startup,executable);
        var entry=System.IO.Path.Combine(startup,"Play Persona 5 Royal.lnk");
        if(!File.Exists(entry)) throw new Exception("Game launcher shortcut was not created.");
        dynamic shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic shortcut=shell.CreateShortcut(entry);
        try { if((string)shortcut.TargetPath!=executable || (string)shortcut.Arguments!="--launch-game") throw new Exception("Game launcher shortcut target is incorrect."); }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut); System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
        loaded.Autostart=false; SyncAutostart(loaded,startup,executable);
        if(File.Exists(entry)) throw new Exception("Disabling game autostart failed.");
        if(ValidImage("C:\\local.png") || ValidImage("javascript:bad") || !ValidImage("my_asset") || !ValidImage("https://example.com/art.png")) throw new Exception("Image validation failed.");
        if(!loaded.HideToTrayOnLaunch) throw new Exception("Configured launches should hide to tray by default.");
        GameLauncher.SelfTest(root);
        File.Delete(configPath); File.Delete(executable); Directory.Delete(startup); Directory.Delete(root);
        Console.WriteLine("Setup, config, game shortcut creation/removal, tray default, Xbox/Steam/standalone launch targets, and concise status checks passed (temporary folder only; no game launched).");
    }
}
