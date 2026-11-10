namespace P5XboxPresence;

static class LocationNames
{
    internal static bool IsProceduralMementos(int major,int minor,int dungeon)=>
        major is >=190 and <=199 && !(major==190 && minor==1) && (major>=191 || dungeon>0);
    internal static string Resolve(int major,int minor,Dictionary<string,string> names,int dungeon)=>
        IsProceduralMementos(major,minor,dungeon)?"Mementos":names.GetValueOrDefault($"{major}_{minor}","");
    internal static void SelfTest(Dictionary<string,string> names)
    {
        if(Resolve(10,1,names,0)!="Takemi Medical Clinic" || Resolve(10,19,names,0)!="Big Bang Burger") throw new Exception("Live interior mappings failed.");
        if(Resolve(193,15,names,3)!="Mementos" || IsProceduralMementos(154,2,3) || IsProceduralMementos(190,1,0)) throw new Exception("Mementos classification leaked into a palace or entrance.");
        if(Resolve(999,999,names,0)!="" || PresenceText.SecondLine("Exploring Tokyo","")!="Exploring Tokyo") throw new Exception("Unknown field must not invent a place.");
        if(GameReader.ClassifyActivity(154,2,null,null,3)=="Exploring Mementos") throw new Exception("Palace dungeon value incorrectly classified as Mementos.");
        if(names["3_2"]!="Caf\u00e9 Leblanc \u00b7 Inside" || names.Values.Any(v=>v.Contains("\u00c3\u00a9") || v.Contains("\u00c2\u00b7") || v.Contains('\ufffd'))) throw new Exception("Location-name encoding regression.");
        foreach(var key in names.Keys) if(!System.Text.RegularExpressions.Regex.IsMatch(key,@"^\d+_\d+$") || string.IsNullOrWhiteSpace(names[key])) throw new Exception("Invalid field mapping.");
        if(names.Values.Any(v=>System.Text.RegularExpressions.Regex.IsMatch(v,@"^\d{3}_\d{3}|unfinished|singular door|single door",System.Text.RegularExpressions.RegexOptions.IgnoreCase))) throw new Exception("Unverified developer label exposed as a place.");
        foreach(var label in new[]{"MSG_026_0_0","MSG_055_0_0","MSG_083_0_0","MSG_090_0_0"})
            if(GameReader.ClassifyActivity(10,19,new(0,1,0,"Female Employee",label,null,null),"762_201")!="Participating in burger challenge") throw new Exception("Burger challenge text differs by stage.");
    }
}
