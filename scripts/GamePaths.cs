using System.Xml.Linq;

namespace P5XboxPresence;

static class GamePaths
{
    internal static bool Match(string running,string? selected)
    {
        if(string.IsNullOrWhiteSpace(selected) || string.Equals(running,selected,StringComparison.OrdinalIgnoreCase)) return true;
        if(!Path.GetFileName(running).Equals("P5R.exe",StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(selected).Equals("P5R.exe",StringComparison.OrdinalIgnoreCase)) return false;
        // Xbox exposes both XboxGames/Content and WindowsApps paths for one
        // registered package. Compare its full manifest identity, not path text.
        var actual=Package(running); var chosen=Package(selected);
        return actual!=null && actual==chosen;
    }
    static string? Package(string executable)
    {
        try
        {
            var manifest=Path.Combine(Path.GetDirectoryName(executable)!,"AppxManifest.xml");
            if(!File.Exists(manifest)) return null;
            var document=XDocument.Load(manifest);
            var identity=document.Root?.Elements().FirstOrDefault(n=>n.Name.LocalName=="Identity");
            var name=identity?.Attribute("Name")?.Value;
            var publisher=identity?.Attribute("Publisher")?.Value;
            var version=identity?.Attribute("Version")?.Value;
            if(name!="SEGAofAmericaInc.F0cb6b3aer" || string.IsNullOrEmpty(publisher) || string.IsNullOrEmpty(version)) return null;
            return name+"\n"+publisher+"\n"+version;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Xml.XmlException) { return null; }
    }
    internal static void SelfTest(string root)
    {
        var a=Path.Combine(root,"XboxAlias"); var b=Path.Combine(root,"PackageAlias");
        Directory.CreateDirectory(a); Directory.CreateDirectory(b);
        var ma=Path.Combine(a,"AppxManifest.xml"); var mb=Path.Combine(b,"AppxManifest.xml");
        const string manifest="<Package><Identity Name='SEGAofAmericaInc.F0cb6b3aer' Publisher='CN=Test' Version='1.10.27.0'/></Package>";
        try
        {
            File.WriteAllText(ma,manifest); File.WriteAllText(mb,manifest);
            if(!Match(Path.Combine(a,"P5R.exe"),Path.Combine(b,"P5R.exe"))) throw new Exception("Xbox alias paths did not match.");
            File.WriteAllText(mb,manifest.Replace("CN=Test","CN=Other"));
            if(Match(Path.Combine(a,"P5R.exe"),Path.Combine(b,"P5R.exe"))) throw new Exception("Different Xbox publisher accepted.");
            File.Delete(mb);
            if(Match(Path.Combine(a,"P5R.exe"),Path.Combine(b,"P5R.exe"))) throw new Exception("Unrelated game path accepted.");
        }
        finally { File.Delete(ma); if(File.Exists(mb)) File.Delete(mb); Directory.Delete(a); Directory.Delete(b); }
    }
}
