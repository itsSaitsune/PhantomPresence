using System.Text.Json;

namespace P5XboxPresence;

static class Confidants

{

    sealed class SceneOverride

    {

        public string Activity { get; set; }="";

        public string Location { get; set; }="";

    }

    sealed class Catalog

    {

        public Dictionary<string,SceneOverride> SceneOverrides { get; set; }=new();

        public string[] Names { get; set; }=[];

        public Dictionary<string,string> Aliases { get; set; }=new();

        public Dictionary<string,string> EventFamilies { get; set; }=new();

        public Dictionary<string,string> EventOverrides { get; set; }=new();

        public Dictionary<string,string> FollowupLabels { get; set; }=new();

    }

    static readonly Catalog data=JsonSerializer.Deserialize<Catalog>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"confidants.json")),new JsonSerializerOptions { PropertyNameCaseInsensitive=true }) ?? throw new IOException("Confidant mappings are missing.");

    static readonly Dictionary<string,string> aliases=new(data.Aliases,StringComparer.OrdinalIgnoreCase);

    static readonly HashSet<string> followupParticipants=new(data.FollowupLabels.Values,StringComparer.Ordinal);

    internal static string? Name(string? value)=>value==null?null:aliases.GetValueOrDefault(value.Trim());

    internal static string HangingOut(string name)=>"Hanging out with "+name;

    internal static string? ActivityOverride(string? eventId)=>eventId!=null && data.SceneOverrides.TryGetValue(eventId,out var scene)?scene.Activity:null;

    internal static string? LocationOverride(string? eventId)=>eventId!=null && data.SceneOverrides.TryGetValue(eventId,out var scene)?scene.Location:null;

    internal static bool IsShared(string? eventId)=>eventId is "700_300" or "700_310";

    internal static string? Scene(string? eventId,DialogueInfo? dialogue)

    {

        if(ActivityOverride(eventId)!=null) return null;

        if(eventId!=null && data.EventOverrides.TryGetValue(eventId,out var special)) return special;

        if(eventId!=null)

        {

            var parts=eventId.Split('_');

            if(parts.Length==2 && int.TryParse(parts[0],out var major) && int.TryParse(parts[1],out _) &&

               data.EventFamilies.TryGetValue(major.ToString(System.Globalization.CultureInfo.InvariantCulture),out var owner)) return owner;

        }

        // A label number means nothing outside this specific shared script.

        // Ignore stale chat contexts, including ones that have a valid label.

        if(IsShared(eventId) && dialogue is { Active:not 0 })

        {

            var speaker=Name(dialogue.Speaker);

            if(speaker!=null && followupParticipants.Contains(speaker)) return speaker;

            if(dialogue.Label!=null && data.FollowupLabels.TryGetValue(dialogue.Label,out var owner)) return owner;

        }

        return null;

    }

    internal static string? Direct(DialogueInfo? dialogue)=>dialogue is { Active:not 0 }?Name(dialogue.Speaker):null;

    internal static void SelfTest()

    {

        if(data.Names.Length!=25 || data.Names.Any(n=>Name(n)!=n) || Name("Becky")!="Sadayo Kawakami" || Name("Languid Doctor")!="Tae Takemi") throw new Exception("Confidant roster/aliases failed.");

        foreach(var pair in data.EventFamilies)

            if(Scene(pair.Key+"_81",null)!=pair.Value) throw new Exception("Confidant event ownership failed: "+pair.Key);

        if(Scene("713_81",new(0,1,0,"Morgana",null,null,null))!="Tae Takemi" || Scene("710_81",null)!="Chihaya Mifune") throw new Exception("Observed confidant fixtures failed.");

        foreach(var pair in data.FollowupLabels)

            if(Scene("700_310",new(0,1,65535,null,pair.Key,null,null))!=pair.Value) throw new Exception("Shared confidant label failed: "+pair.Key);

        if(Scene("500_1",new(0,1,0,"Takemi","MSG_700_0_0",null,null))!=null || Scene("700_310",new(0,0,0,"Kawakami","MSG_830_0_0",null,null))!=null) throw new Exception("Story/stale-dialogue false positive.");

        if(Scene("722_52",null)!="Kasumi Yoshizawa" || Scene("722_62",null)!="Sumire Yoshizawa" || Scene("726_22",null)!="Goro Akechi" || Scene("729_100",null)!="Lavenza") throw new Exception("Royal phase/variant mapping failed.");

        if(Scene("714_100",new(0,1,0,"Kawakami","MSG_000_0_0",null,null))!=null ||

           GameReader.ClassifyActivity(3,2,new(0,1,0,"Kawakami","MSG_000_0_0",null,null),"714_100")!="Attending class" ||

           GameReader.ClassifyActivity(3,2,new(0,1,3,"Short-Haired Male Student","MSG_004_0_0",null,null),"714_100")!="Attending class" ||

           LocationOverride("714_100")!="Shujin Academy · Classroom") throw new Exception("Classroom scene incorrectly labelled as confidant conversation.");

        foreach(var lesson in new[]{"730_1","731_1","731_2","731_12","732_1","732_101","733_1"})
        {
            foreach(var teacher in new[]{"Maruki","Kawakami","Mr. Ushimaru","Ms. Chouno"})
                if(GameReader.ClassifyActivity(3,2,new(0,1,0,teacher,"MSG_463_0_0",null,null),lesson)!="Attending class" || Scene(lesson,null)!=null || LocationOverride(lesson)!="Shujin Academy \u00b7 Classroom") throw new Exception("Shared school lesson depends on speaker or stale location.");
        }
        if(GameReader.ClassifyActivity(3,2,null,"737_1")!="Taking exams" || GameReader.ClassifyActivity(3,2,null,"736_1")!="Checking exam results" || Scene("723_22",new(0,1,0,"Maruki",null,null,null))!="Takuto Maruki") throw new Exception("Class/exam versus Maruki confidant regression.");
        var classroomSession=new ConfidantSession();

        classroomSession.Observe("714_91",null);

        if(classroomSession.Observe("714_100",null)!=null || classroomSession.Observe("700_310",null)!=null) throw new Exception("Classroom did not clear confidant carryover.");

        var emptyActive=new DialogueInfo(38,1,65535,null,null,null,null);

        var staleChat=new DialogueInfo(25,0,0,"Chihaya","CHAT_REACTION_MSG",null,null);

        if(GameReader.SelectDialogue(new[]{staleChat,emptyActive})!=emptyActive) throw new Exception("Stale chat displaced active choice state.");

        var session=new ConfidantSession();

        if(session.Observe("710_81",null)!="Chihaya Mifune" || session.Observe("700_300",emptyActive)!="Chihaya Mifune" || session.Observe(null,null)!=null) throw new Exception("Observed Chihaya silent follow-up failed.");

        if(session.Observe("713_81",null)!="Tae Takemi" || session.Observe("700_310",null)!="Tae Takemi" || session.Observe("500_1",null)!=null || session.Observe("700_310",null)!=null) throw new Exception("Confidant carryover/reset failed.");

        if(session.Observe("700_310",new(0,1,0,"Chihaya","MSG_560_0_0",null,null))!="Chihaya Mifune" || session.Observe("700_310",new(0,1,0,"Joker",null,null,null))!="Chihaya Mifune" || session.Observe(null,null)!=null) throw new Exception("Shared-event entry/responses failed.");

    }

}

sealed class ConfidantSession

{

    string? current;

    internal string? Observe(string? eventId,DialogueInfo? dialogue)

    {

        var scene=Confidants.Scene(eventId,dialogue);

        if(scene!=null) return current=scene;

        if(Confidants.IsShared(eventId)) return current;

        current=null;

        return null;

    }

}
