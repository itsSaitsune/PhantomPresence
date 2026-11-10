using System.Diagnostics;
using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace P5XboxPresence;

static class GameLauncher
{
    internal static ProcessStartInfo BuildStartInfo(string gamePath)
    {
        if(!Settings.ValidGamePath(gamePath) || string.IsNullOrWhiteSpace(gamePath))
            throw new IOException("Choose the P5R.exe path before launching the game.");
        var directory=Path.GetDirectoryName(gamePath)!;
        var manifest=Path.Combine(directory,"AppxManifest.xml");
        if(File.Exists(manifest) && directory.Contains("WindowsApps",StringComparison.OrdinalIgnoreCase))
        {
            var document=XDocument.Load(manifest);
            var family=Path.GetFileName(directory);
            var publisherId=family[(family.LastIndexOf('_')+1)..];
            var aumid=ApplicationId(document,publisherId);
            var launch=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe")) { UseShellExecute=true };
            launch.ArgumentList.Add("shell:AppsFolder\\"+aumid);
            return launch;
        }
        // Recognize an actual Steam library manifest, not a DLL/appid text file
        // that can also be present beside independently launched copies.
        var parent=Directory.GetParent(directory);
        if(parent?.Name.Equals("common",StringComparison.OrdinalIgnoreCase)==true && parent.Parent is { } steamapps)
        {
            var steamManifest=Path.Combine(steamapps.FullName,"appmanifest_1687950.acf");
            if(File.Exists(steamManifest))
            {
                var install=Regex.Match(File.ReadAllText(steamManifest),"\"installdir\"\\s*\"([^\"]+)\"",RegexOptions.IgnoreCase);
                if(install.Success && install.Groups[1].Value.Equals(Path.GetFileName(directory),StringComparison.OrdinalIgnoreCase))
                    return new ProcessStartInfo("steam://rungameid/1687950") { UseShellExecute=true };
            }
        }
        return new ProcessStartInfo(gamePath) { UseShellExecute=true,WorkingDirectory=directory };
    }

    internal static string ApplicationId(XDocument manifest,string publisherId)
    {
        var name=manifest.Descendants().FirstOrDefault(n=>n.Name.LocalName=="Identity")?.Attribute("Name")?.Value;
        var applications=manifest.Descendants().Where(n=>n.Name.LocalName=="Application").ToArray();
        var application=applications.FirstOrDefault(n=>string.Equals(n.Attribute("Id")?.Value,"P5R",StringComparison.OrdinalIgnoreCase))
            ?? applications.FirstOrDefault(n=>string.Equals(Path.GetFileName(n.Attribute("Executable")?.Value),"P5R.exe",StringComparison.OrdinalIgnoreCase))
            ?? (applications.Length==1?applications[0]:null);
        var id=application?.Attribute("Id")?.Value;
        if(string.IsNullOrEmpty(name) || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(publisherId))
            throw new IOException("Could not identify the Xbox game's launch entry.");
        return name+"_"+publisherId+"!"+id;
    }

    internal static void Launch(string gamePath) => Process.Start(BuildStartInfo(gamePath))?.Dispose();

    internal static void SelfTest(string root)
    {
        var directory=Path.Combine(root,"WindowsApps","Example_1.0.0.0_x64__publisher123"); Directory.CreateDirectory(directory);
        var executable=Path.Combine(directory,"P5R.exe"); File.WriteAllText(executable,"");
        var manifest=Path.Combine(directory,"AppxManifest.xml");
        File.WriteAllText(manifest,"<Package xmlns='urn:test'><Identity Name='Example'/><Applications><Application Id='P5R' Executable='GameLaunchHelper.exe'/></Applications></Package>");
        var start=BuildStartInfo(executable);
        if(!start.FileName.EndsWith("explorer.exe",StringComparison.OrdinalIgnoreCase) || start.ArgumentList.Single()!="shell:AppsFolder\\Example_publisher123!P5R") throw new Exception("Xbox activation launch target failed.");
        File.Delete(manifest);
        var direct=BuildStartInfo(executable);
        if(direct.FileName!=executable || !direct.UseShellExecute) throw new Exception("Direct game launch target failed.");
        File.Delete(executable); Directory.Delete(directory); Directory.Delete(Path.Combine(root,"WindowsApps"));
        var steamFolder=Path.Combine(root,"steamapps","common","Persona 5 Royal"); Directory.CreateDirectory(steamFolder);
        var steamExe=Path.Combine(steamFolder,"P5R.exe"); File.WriteAllText(steamExe,"");
        var steamManifest=Path.Combine(root,"steamapps","appmanifest_1687950.acf");
        File.WriteAllText(steamManifest,"\"installdir\" \"Persona 5 Royal\"");
        if(BuildStartInfo(steamExe).FileName!="steam://rungameid/1687950") throw new Exception("Steam library activation failed.");
        File.Delete(steamManifest);
        if(BuildStartInfo(steamExe).FileName!=steamExe) throw new Exception("Standalone launch was misidentified as Steam.");
        File.Delete(steamExe); Directory.Delete(steamFolder); Directory.Delete(Path.GetDirectoryName(steamFolder)!); Directory.Delete(Path.Combine(root,"steamapps"));
        if(PresenceStatus.Display("Aug 27 · Daytime — Discord accepted")!="Connected" || PresenceStatus.Display("Waiting for a loaded save / valid field…")!="Loading the save file.." || PresenceStatus.Display("Windows denied read access. Retrying…")!="Error: Windows denied read access.") throw new Exception("Concise connection status failed.");
    }
}

static class PresenceStatus
{
    internal static string Display(string value)
    {
        if(value.EndsWith("— Discord accepted",StringComparison.Ordinal)) return "Connected";
        if(value.StartsWith("Waiting for a loaded save",StringComparison.Ordinal)) return "Loading the save file..";
        if(value.StartsWith("Waiting for Persona",StringComparison.Ordinal) || value=="Not connected") return "Not connected";
        if(value=="Connected" || value=="Loading the save file..") return value;
        if(value.StartsWith("Error:",StringComparison.Ordinal)) return value;
        return "Error: "+value.Replace(" Retrying…","");
    }
}
