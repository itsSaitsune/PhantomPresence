using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;
namespace P5XboxPresence;
static class App
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static async Task Run(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        try
        {
            if(args.Contains("--runtime-status")) { Console.WriteLine(RuntimeStatus.Text); return; }
            if(args.Contains("--runtime-self-test")) { RuntimeStatus.SelfTest(); return; }
            var client=Array.IndexOf(args,"--status-test-client");
            if(client>=0) { using var bus=new MemoryStatus(args[client+1]); bus.Text="Connected"; return; }
            if(args.Contains("--settings-self-test")) { Settings.SelfTest(); return; }
            var launchPlan=Array.IndexOf(args,"--launch-plan");
            if(launchPlan>=0)
            {
                if(launchPlan+1>=args.Length) throw new Exception("Provide the P5R.exe path after --launch-plan.");
                var start=GameLauncher.BuildStartInfo(args[launchPlan+1]);
                Console.WriteLine(JsonSerializer.Serialize(new { start.FileName,Arguments=start.ArgumentList.ToArray() },Json)); return;
            }
            if (args.Contains("--self-test"))
            {
                if (GameReader.Date(147).ToString("MM/dd",CultureInfo.InvariantCulture) != "08/26" || GameReader.Date(147).DayOfWeek != DayOfWeek.Friday || GameReader.Date(275).ToString("MM/dd",CultureInfo.InvariantCulture) != "01/01") throw new Exception("Calendar test failed.");
                var ohya=new DialogueInfo(61,0,1,"Morgana","MSG_COOP_CHECK_NIGHT","Are you gonna hang out with Ohya?",null);
                if(GameReader.ClassifyActivity(10,12,ohya)!="Exploring Tokyo") throw new Exception("Conversation partner test failed.");
                if(GameReader.ClassifyActivity(3,2,null,"760_101")!="Studying") throw new Exception("Study-event test failed.");
                if(GameReader.ClassifyActivity(3,2,new(0,1,0,"Morgana","MSG_ReadStart",null,null))=="Reading") throw new Exception("Reading prompt misclassified.");
                if(GameReader.ClassifyActivity(3,2,new(0,1,0,null,"MND_BookID_001_Start",null,null))!="Reading") throw new Exception("Reading label test failed.");
                if(GameReader.ClassifyActivity(190,2,null,null,3)!="Exploring Mementos") throw new Exception("Mementos test failed.");
                var staleBecky=new DialogueInfo(31,0,0,"Becky","CHAT_REACTION_MSG",null,null);
                var activeKawakami=new DialogueInfo(33,1,0,"Kawakami","MSG_003_0_0",null,null);
                if(GameReader.SelectDialogue(new[]{staleBecky,activeKawakami})!=activeKawakami) throw new Exception("Inactive phone message took priority over live dialogue.");
                if(GameReader.ClassifyActivity(3,2,activeKawakami,"714_91")!="Hanging out with Sadayo Kawakami" ||
                   GameReader.ClassifyActivity(3,2,null,"714_91")!="Hanging out with Sadayo Kawakami" ||
                   GameReader.ClassifyActivity(3,2,new(31,1,0,"Becky",null,null,null))!="Exploring Tokyo") throw new Exception("Kawakami alias/confidant recognition failed.");
                if(GameReader.ClassifyActivity(3,2,activeKawakami,"700_310")!="Hanging out with Sadayo Kawakami" ||
                   GameReader.ClassifyActivity(3,2,staleBecky,"700_310")!="Following the story") throw new Exception("Confidant follow-up needs an active participant.");
                if(GameReader.ClassifyActivity(3,2,activeKawakami,"500_1")!="Following the story") throw new Exception("Story dialogue should remain generic.");
                var mappedLocations=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"locations.json")))!;
                if(mappedLocations.GetValueOrDefault("10_1")!="Takemi Medical Clinic" || PresenceText.SecondLine("Exploring Tokyo",mappedLocations["10_1"])!="Takemi Medical Clinic") throw new Exception("Clinic location/display regression.");
                foreach(var speaker in new[]{"Yusuke","Becky","Female Employee","Morgana"})
                {
                    var ordinary=new DialogueInfo(46,1,5,speaker,"MSG_E320102_YUSUKE_000_0",null,null);
                    var ordinaryActivity=GameReader.ClassifyActivity(12,26,ordinary);
                    if(PresenceText.SecondLine(ordinaryActivity,mappedLocations["12_26"])!="Waikiki Beach") throw new Exception("Ordinary NPC dialogue must display the location only.");
                }
                LocationNames.SelfTest(mappedLocations);
                Confidants.SelfTest();
                CompatibilityTests.Run();
                Console.WriteLine("Calendar, activities, build routing, signatures, and Steam state checks passed."); return;
            }
            if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) throw new Exception("64-bit Windows is required.");
            var folder = RuntimePaths.DataFolder;
            var config = Settings.Load();
            var locations = JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"locations.json")))!;
            if(args.Contains("--compatibility-report"))
            {
                var processes=Process.GetProcessesByName("P5R");
                try
                {
                    if(processes.Length!=1) throw new IOException("Run exactly one P5R.exe before creating the report.");
                    using var memory=new ProcessMemory(processes[0]);
                    string signature;
                    try { signature=$"Sequence RVA 0x{memory.FindSequenceGlobal()-memory.Base:X}"; }
                    catch(IOException ex) { signature=ex.Message; }
                    var report=new { Executable="P5R.exe", memory.Timestamp,memory.ImageSize, SequenceSignature=signature,
                        TestedXboxIdentity=BuildIdentity.IsTestedXbox(memory.Timestamp,memory.ImageSize),
                        Steam="Experimental: date/time and sequence only; locations and dialogue unavailable" };
                    Console.WriteLine(JsonSerializer.Serialize(report,Json));
                }
                finally { foreach(var process in processes) process.Dispose(); }
                return;
            }
            if (args.Contains("--probe") || args.Contains("--dialogue-probe"))
            {
                using var game = GameReader.Find(config.Path,config.ExperimentalSteam) ?? throw new Exception("P5R.exe is not running.");
                Console.WriteLine(JsonSerializer.Serialize(new { game.Build, State=game.Read(locations) }, Json));
                if(args.Contains("--dialogue-probe")) Console.WriteLine(JsonSerializer.Serialize(game.DialogueDiagnostics(),Json));
                return;
            }
            if(args.Contains("--stop"))
            {
                if(EventWaitHandle.TryOpenExisting("Local\\P5XboxPresence.Stop",out var signal)) { using(signal) signal.Set(); Console.WriteLine("Stop requested."); }
                else Console.WriteLine("Presence is not running.");
                return;
            }
            var startup=args.Contains("--worker");
            if(startup && !config.SetupComplete) return;
            using var mutex = new Mutex(true,"Local\\P5XboxPresence",out var first);
            if (!first)
            {
                const string message="Error: Another presence instance is running. Close it and retry.";
                Console.WriteLine(message); if(startup) RuntimeStatus.Text=message;
                Environment.ExitCode=2; return;
            }
            if(!startup && (!config.SetupComplete || !ValidId(config.ApplicationId) || args.Contains("--setup")))
            {
                config=Settings.Setup(config,Console.In,Console.Out);
                Settings.Save(config);
                Console.WriteLine("Saved settings to your Windows user profile.");
            }
            if(!Settings.ValidImage(config.Image)) throw new Exception("Invalid image in settings. Use an asset key or HTTPS URL.");
            if(!startup) Settings.SyncAutostart(config);
            if(args.Contains("--setup")) return;
            if(startup) { Console.SetOut(TextWriter.Null); Console.SetError(TextWriter.Null); }
            using var stop = new CancellationTokenSource();
            using var stopSignal=new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\P5XboxPresence.Stop");
            stopSignal.Reset();
            var stopRegistration=ThreadPool.RegisterWaitForSingleObject(stopSignal,(_,_)=>stop.Cancel(),null,Timeout.Infinite,true);
            Console.CancelKeyPress += (_,e) => { e.Cancel=true; stop.Cancel(); };
            var durationIndex=Array.IndexOf(args,"--duration");
            if(durationIndex>=0 && durationIndex+1<args.Length) stop.CancelAfter(TimeSpan.FromSeconds(int.Parse(args[durationIndex+1],CultureInfo.InvariantCulture)));
            Console.WriteLine("P5 Xbox Presence — read-only. Ctrl+C stops and clears activity.");
            IGameReader? reader=null; DiscordIpc? discord=null; string? lastLog=null; long? started=null; bool published=false;
            try
            {
                while(!stop.IsCancellationRequested)
                {
                    try
                    {
                        if(reader?.Exited==true) { reader.Dispose(); reader=null; started=null; if(args.Contains("--exit-with-game")) break; }
                        reader ??= GameReader.Find(config.Path,config.ExperimentalSteam);
                        var state=reader?.Read(locations);
                        if(state==null)
                        {
                            if(published && discord!=null) { await discord.Activity(null,stop.Token); published=false; }
                            Log(reader==null ? "Waiting for Persona 5 Royal (Xbox app)…" : "Waiting for a loaded save / valid field…");
                        }
                        else
                        {
                            started ??= DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                            discord ??= await DiscordIpc.Connect(config.ApplicationId,stop.Token);
                            var dateTime=string.Join(" - ",new[]{state.Date,state.Time}.Where(s=>!string.IsNullOrEmpty(s)));
                            var description=PresenceText.SecondLine(state.Activity,state.Location);
                            var activity=new Dictionary<string,object> { ["type"]=0,["details"]=dateTime.Length>0?dateTime:state.Activity,["state"]=Limit(dateTime.Length>0?description:"Persona 5 Royal"),["timestamps"]=new { start=started.Value } };
                            if(!string.IsNullOrWhiteSpace(config.Image)) activity["assets"]=new { large_image=config.Image,large_text=config.ImageText };
                            await discord.Activity(activity,stop.Token);
                            published=true;
                            Log($"{state.Date} · {state.Time} · {state.Location} · {state.Activity} [field {state.FieldId}] — Discord accepted");
                        }
                    }
                    catch(OperationCanceledException) when(stop.IsCancellationRequested) { break; }
                    catch(Exception ex)
                    {
                        if(discord!=null)
                        {
                            try { await discord.Activity(null,CancellationToken.None); } catch { }
                            discord.Dispose(); discord=null; published=false;
                        }
                        reader?.Dispose(); reader=null;
                        Log(ex.Message+" Retrying…");
                    }
                    try { await Task.Delay(5000,stop.Token); } catch(OperationCanceledException) { break; }
                }
            }
            finally
            {
                if(discord!=null) { try { await discord.Activity(null,CancellationToken.None); } catch { } discord.Dispose(); }
                reader?.Dispose();
                stopRegistration.Unregister(null);
                if(startup) RuntimeStatus.Text="Not connected";
            }
            void Log(string s) { if(s!=lastLog) { Console.WriteLine(s); lastLog=s; if(startup) RuntimeStatus.Text=PresenceStatus.Display(s); } }
        }
        catch(Exception ex)
        {
            Console.Error.WriteLine(ex.Message); Environment.ExitCode=1;
            if(args.Contains("--worker")) { try { RuntimeStatus.Text="Error: "+ex.Message; } catch(IOException) { } }
        }
    }
    static bool ValidId(string id)=> id.Length is >=17 and <=22 && id.All(char.IsAsciiDigit);
    static string Limit(string text)=>text.Length<=128?text:text[..125]+"…";
}
record GameState(string Date,string Time,string Location,string FieldId,int RawDay,int RawTime,string Activity,string? Speaker,string? MessageLabel,int Sequence=-1,string? EventId=null,int Dungeon=0,int Floor=0);
record DialogueInfo(int Context,int Active,int SpeakerId,string? Speaker,string? Label,string? Text,string? ScriptProfile);
record GameTask(string Name,long Payload,long Handle);
static class PresenceText
{
    internal static string SecondLine(string? activity,string? location)
    {
        var passive=string.IsNullOrWhiteSpace(activity) || activity is "Exploring Tokyo" or "Exploring the Metaverse" or "At the Mementos entrance";
        return passive && !string.IsNullOrWhiteSpace(location)?location:activity??"Persona 5 Royal";
    }
}
interface IGameReader : IDisposable
{
    bool Exited { get; }
    string Build { get; }
    GameState? Read(Dictionary<string,string> locations);
    DialogueInfo[] DialogueDiagnostics();
}
sealed class GameReader : IGameReader
{
    readonly Process process;
    readonly SafeProcessHandle handle;
    readonly long moduleBase;
    readonly ConfidantSession confidantSession=new();
    public bool Exited=>process.HasExited;
    public string Build=>"Xbox 1.10.27.0 (tested)";
    GameReader(Process process)
    {
        this.process=process;
        try
        {
            var module=process.MainModule ?? throw new IOException("Cannot inspect game module.");
            moduleBase=module.BaseAddress.ToInt64();
            // PROCESS_VM_READ | PROCESS_QUERY_LIMITED_INFORMATION. No write rights.
            handle=Native.OpenProcess(0x1010,false,process.Id);
            if(handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(),"Windows denied read access to the game.");
            var pe=U32(moduleBase+0x3c);
            if(U32(moduleBase+pe+8)!=1670832233 || U32(moduleBase+pe+80)!=396046336) throw new IOException("Game executable changed; refusing unverified offsets.");
        }
        catch { handle?.Dispose(); process.Dispose(); throw; }
    }
    public static IGameReader? Find(string? gamePath=null,bool experimentalSteam=false)
    {
        var ps=Process.GetProcessesByName("P5R");
        if(!string.IsNullOrWhiteSpace(gamePath))
        {
            var matching=new List<Process>();
            foreach(var p in ps)
            {
                try { if(GamePaths.Match(p.MainModule?.FileName??"",gamePath)) { matching.Add(p); continue; } }
                catch { p.Dispose(); throw; }
                p.Dispose();
            }
            ps=matching.ToArray();
        }
        if(ps.Length==0) return null;
        if(ps.Length>1) { foreach(var p in ps) p.Dispose(); throw new IOException("More than one P5R process is running."); }
        var candidate=ps[0];
        try
        {
            // Identify by the loaded executable, not its folder/store name.
            using var probe=new ProcessMemory(candidate);
            if(BuildIdentity.IsTestedXbox(probe.Timestamp,probe.ImageSize))
                return new GameReader(candidate);
            if(!experimentalSteam) throw new IOException("Unverified build. Enable Experimental Steam / modified builds in settings to try the separate Steam reader.");
            return new SteamReader(candidate,probe);
        }
        catch { candidate.Dispose(); throw; }
    }
    public GameState? Read(Dictionary<string,string> locations)
    {
        var sequence=CurrentSequence();
        if(sequence is 1 or 2) return new("","","","",0,0,"In the main menu",null,null,sequence);
        if(sequence is 3 or 11) return new("","","","",0,0,"Loading",null,null,sequence);
        var tasks=Tasks();
        var eventId=sequence==7?CurrentEvent(tasks):null;
        var calendar=Pointer(moduleBase+0x1e71380);
        var manager=Pointer(moduleBase+0x1f20048);
        if(calendar==0) return null;
        var field=manager==0?0:Pointer(manager+0x48);
        var bytes=Bytes(calendar,3);
        int day=BinaryPrimitives.ReadUInt16LittleEndian(bytes),time=bytes[2];
        var dialogue=SelectDialogue(DialogueDiagnostics());
        var usePreviousField=field==0;
        var ids=Bytes(usePreviousField?moduleBase+0x1f20380:field+0x1f0,4);
        int major=BinaryPrimitives.ReadUInt16LittleEndian(ids),minor=BinaryPrimitives.ReadUInt16LittleEndian(ids.AsSpan(2));
        if(day is <8 or >371 || time>6) return null;
        if(major==65535 || minor==65535 || (major==0 && minor==0))
        {
            usePreviousField=true; ids=Bytes(moduleBase+0x1f20380,4);
            major=BinaryPrimitives.ReadUInt16LittleEndian(ids); minor=BinaryPrimitives.ReadUInt16LittleEndian(ids.AsSpan(2));
        }
        // Previous-field data is meaningful only while a live dialogue/event is active.
        if(usePreviousField && dialogue==null && eventId==null) return null;
        if(major==65535 || minor==65535 || (major==0 && minor==0)) return null;
        if(Pointer(moduleBase+0x1f20048)!=manager || (manager!=0 && Pointer(manager+0x48)!=field) || !Bytes(calendar,3).SequenceEqual(bytes) || !Bytes(usePreviousField?moduleBase+0x1f20380:field+0x1f0,4).SequenceEqual(ids)) return null;
        var id=$"{major}_{minor}";
        var location=LocationNames.Resolve(major,minor,locations,dungeon:0);
        var dungeon=field==0?0:BinaryPrimitives.ReadUInt16LittleEndian(Bytes(field+0x1fe,2));
        var floor=field==0?0:BinaryPrimitives.ReadUInt16LittleEndian(Bytes(field+0x200,2));
        var activity=ClassifyActivity(major,minor,dialogue,eventId,dungeon);
        var confidant=confidantSession.Observe(eventId,dialogue);
        if(confidant!=null) activity=Confidants.HangingOut(confidant);
        if(LocationNames.IsProceduralMementos(major,minor,dungeon)) location="Mementos";
        if(Confidants.LocationOverride(eventId) is { } sceneLocation) location=sceneLocation;
        if(sequence is 9 or 10) activity="Watching a movie";
        if(sequence is 12 or 13) activity="Advancing the calendar";
        return new(Date(day).ToString("MMM d (ddd)",CultureInfo.InvariantCulture),new[]{"Early Morning","Morning","Lunchtime","Daytime","After School","Evening","Late Night"}[time],location,id,day,time,activity,dialogue?.Speaker,dialogue?.Label,sequence,eventId,dungeon,floor);
    }
    public static DateTime Date(int day)=>new DateTime(2016,4,1).AddDays(day);
    internal static DialogueInfo? SelectDialogue(IEnumerable<DialogueInfo> contexts) => contexts
        .Where(d=>d.Active!=0 || d.Speaker!=null || d.Label!=null)
        .OrderByDescending(d=>d.Active!=0)
        .FirstOrDefault();
    internal static string ClassifyActivity(int major,int minor,DialogueInfo? dialogue,string? eventId=null,int dungeon=0)
    {
        if(Confidants.ActivityOverride(eventId) is { } sceneActivity) return sceneActivity;
        var label=dialogue?.Label??"";
        if(dialogue?.ScriptProfile=="CafeStudy" || eventId=="760_101")
            return label=="MSG_END_TALK_MORU"?"Finished studying":"Studying";
        if(eventId=="763_1") return label is "MSG_RainDay" or "MSG_StudyBook_Info"?"Finished studying":"Studying";
        if(eventId=="762_501") return "Studying at the diner";
        if(eventId is "760_1" or "763_11") return Regex.IsMatch(label,@"ReadEnd|^SYS_BookEnd$")?"Finished reading":"Reading";
        if(Regex.IsMatch(label,@"^(?:MND|MSG)_BookID_\d+_Start(?:_\d+)?$")) return "Reading";
        if(Regex.IsMatch(label,@"^(?:MND|MSG)_BookID_\d+_ReadEnd(?:_\d+|_AF)?$") || label=="SYS_BookEnd") return "Finished reading";
        if(Regex.IsMatch(label,@"^MSG_RUMOR_Study_0[123]$")) return "Studying";
        if(Regex.IsMatch(label,@"^MND_STUDY_(MAKOTO|HARU|RYUJI|ANNE)$")) return "Finished studying with a friend";
        if(label=="MSG_EVT_END_NotSabori") return "Finished studying in class";
        if(label=="MSG_BOOK_TITLE_000") return "Choosing a book";
        if(Regex.IsMatch(label,@"^(MSG_ReadStart|MND_ReadStart|MND_BOOK_START_THINK(?:_MOR)?|SEL_BOOK_START_THINK|SEL_BOOK_READ_CHK|MND_BOOK_READ_CHK)$")) return "Considering reading";
        if(Regex.IsMatch(label,@"^(MSG_Study_Q_(Sojiro|R|Rain)|MND_Study_Q|SEL_TABLE_USE)$")) return "Considering studying";
        var confidant=Confidants.Scene(eventId,dialogue);
        if(confidant!=null) return Confidants.HangingOut(confidant);
        if(eventId!=null) return "Following the story";
        if(LocationNames.IsProceduralMementos(major,minor,dungeon)) return "Exploring Mementos";
        if(major==190 && minor==1) return "At the Mementos entrance";
        if(major==161) return "Exploring the Depths of Mementos";
        if(major==0 && minor==2) return "In the main menu";
        return major is >=100 and <=199 ? "Exploring the Metaverse" : "Exploring Tokyo";
    }
    int CurrentSequence()
    {
        var root=Pointer(moduleBase+0x1f22080); if(root==0) return -1;
        var data=Pointer(root+0x48); if(data==0) return -1;
        return (int)U32(data+4);
    }
    public GameTask[] Tasks()
    {
        var found=new List<GameTask>(); var seen=new HashSet<long>();
        var current=Pointer(moduleBase+0x1f80b10);
        for(int i=0;i<2048 && current!=0 && seen.Add(current);i++)
        {
            try
            {
                var bytes=Bytes(current,0xc0);
                var name=BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(0x18));
                var payload=BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(0x48));
                var handle=BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(0xb8));
                current=BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(0x80));
                if(name!=0) { var text=AsciiString(name,96); if(text!=null) found.Add(new(text,payload,handle)); }
            }
            catch(IOException) { break; }
        }
        return found.ToArray();
    }
    string? CurrentEvent(GameTask[] tasks)
    {
        var handle=Pointer(moduleBase+0x1f1ca08);
        var task=tasks.FirstOrDefault(t=>t.Name=="event_proc" && t.Handle==handle);
        if(task?.Payload is not >0) return null;
        var major=U32(task.Payload+4); var minor=U32(task.Payload+8);
        if(major>9999 || minor>9999) return null;
        var path=AsciiString(task.Payload+12,120);
        if(path==null || !path.StartsWith("event/",StringComparison.OrdinalIgnoreCase)) return null;
        return $"{major}_{minor}";
    }
    string? AsciiString(long address,int max)
    {
        var result=new List<byte>();
        for(int i=0;i<max;i++)
        {
            var b=Bytes(address+i,1)[0]; if(b==0) return result.Count>0?System.Text.Encoding.ASCII.GetString(result.ToArray()):null;
            if(b<32 || b>126) return null; result.Add(b);
        }
        return null;
    }
    public DialogueInfo[] DialogueDiagnostics()
    {
        var found=new List<DialogueInfo>();
        for(int i=0;i<64;i++)
        {
            try
            {
                var context=Pointer(moduleBase+0x1f732e8+i*64);
                if(context==0) continue;
                var active=Bytes(context+0x40,1)[0];
                if(active==0 && (U32(context)&7)==0) continue;
                var id=BinaryPrimitives.ReadUInt16LittleEndian(Bytes(context+0x28,2));
                string? speaker=null;
                if(id!=65535)
                {
                    long nameAddress;
                    if((id&0x8000)!=0) nameAddress=Pointer(context+0x1d8+(id&0x7fff)*8);
                    else
                    {
                        var data=Pointer(context+8);
                        if(data==0 || U32(data+8)!=0x3147534d) continue;
                        var count=U32(data+0x18);
                        if(count>10000 || id>1024) continue;
                        var namesHeader=data+(count+4)*8;
                        var names=namesHeader+U32(namesHeader);
                        var slot=names+id*4;
                        nameAddress=slot+U32(slot);
                    }
                    if(nameAddress!=0)
                    {
                        var raw=new List<byte>();
                        for(int n=0;n<96;n++) { var b=Bytes(nameAddress+n,1)[0]; if(b==0) break; raw.Add(b); }
                        // Names containing game control codes require a separate decoder.
                        if(raw.Count>0 && raw.All(b=>b>=32 && b<127)) speaker=System.Text.Encoding.ASCII.GetString(raw.ToArray());
                    }
                }
                string? label=null,text=null,profile=null;
                var message=Pointer(context+0x30);
                var bmd=Pointer(context+8);
                if(message!=0 && bmd!=0 && U32(bmd+8)==0x3147534d)
                {
                    var nameBytes=Bytes(message,24); var zero=Array.IndexOf(nameBytes,(byte)0);
                    label=System.Text.Encoding.ASCII.GetString(nameBytes,0,zero>=0?zero:nameBytes.Length);
                    if(!Regex.IsMatch(label,@"^[A-Za-z0-9_]{1,24}$")) label=null;
                    var count=U32(bmd+0x18); var length=U32(bmd+4);
                    if(count<=10000 && length<=16*1024*1024 && message>=bmd && message<bmd+length)
                    {
                        long end=bmd+length;
                        var labels=new HashSet<string>();
                        // Bound extraction to this message, never search neighboring dialogue.
                        for(int n=0;n<count;n++)
                        {
                            var slot=bmd+0x24+n*8; var offset=U32(slot);
                            if(offset==0) continue;
                            var next=slot+offset; if(next>message && next<end) end=next;
                            if(next>=bmd && next+24<=bmd+length)
                            {
                                var nb=Bytes(next,24); var z=Array.IndexOf(nb,(byte)0);
                                labels.Add(System.Text.Encoding.ASCII.GetString(nb,0,z>=0?z:nb.Length));
                            }
                        }
                        if(labels.Contains("MSG_Rain_Start") && labels.Contains("PARAUP_MORU") && labels.Contains("MSG_END_TALK_MORU")) profile="CafeStudy";
                        var amount=(int)Math.Min(end-message,16384);
                        if(amount>28)
                        {
                            var body=Bytes(message+28,amount-28);
                            var readable=new string(body.Select(b=>b>=32 && b<=126?(char)b:' ').ToArray());
                            text=Regex.Replace(readable,@"\s+"," ").Trim();
                        }
                    }
                }
                if(Pointer(moduleBase+0x1f732e8+i*64)==context && BinaryPrimitives.ReadUInt16LittleEndian(Bytes(context+0x28,2))==id)
                    found.Add(new(i,active,id,speaker,label,text,profile));
            }
            catch(IOException) { }
        }
        return found.ToArray();
    }
    long Pointer(long a)=>BinaryPrimitives.ReadInt64LittleEndian(Bytes(a,8));
    uint U32(long a)=>BinaryPrimitives.ReadUInt32LittleEndian(Bytes(a,4));
    byte[] Bytes(long a,int count)
    {
        var b=new byte[count];
        if(!Native.ReadProcessMemory(handle,(nint)a,b,(nuint)count,out var read) || read!=(nuint)count) throw new IOException("Game data temporarily unavailable.");
        return b;
    }
    public void Dispose() { handle.Dispose(); process.Dispose(); }
}
static class Native
{
    [DllImport("kernel32.dll",SetLastError=true)] internal static extern SafeProcessHandle OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadProcessMemory(SafeProcessHandle process,nint address,[Out] byte[] buffer,nuint size,out nuint read);
}
sealed class DiscordIpc : IDisposable
{
    readonly NamedPipeClientStream pipe;
    DiscordIpc(NamedPipeClientStream pipe)=>this.pipe=pipe;
    public static async Task<DiscordIpc> Connect(string id,CancellationToken cancellation)
    {
        for(int i=0;i<10;i++)
        {
            var p=new NamedPipeClientStream(".",$"discord-ipc-{i}",PipeDirection.InOut,PipeOptions.Asynchronous);
            try { await p.ConnectAsync(200,cancellation); }
            catch(TimeoutException) { p.Dispose(); continue; }
            catch { p.Dispose(); throw; }
            var client=new DiscordIpc(p);
            try
            {
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(5000);
                await client.Send(0,JsonSerializer.SerializeToUtf8Bytes(new { v=1,client_id=id }),timeout.Token);
                while(true)
                {
                    using var frame=await client.Receive(timeout.Token);
                    var root=frame.RootElement; Error(root);
                    if(root.TryGetProperty("evt",out var evt) && evt.GetString()=="READY") return client;
                }
            }
            catch { client.Dispose(); throw; }
        }
        throw new IOException("Discord desktop unavailable. Open Discord and enable activity sharing.");
    }
    public async Task Activity(object? activity,CancellationToken cancellation)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(5000);
        var nonce=Guid.NewGuid().ToString();
        await Send(1,JsonSerializer.SerializeToUtf8Bytes(new { cmd="SET_ACTIVITY",args=new { pid=Environment.ProcessId,activity },nonce }),timeout.Token);
        while(true)
        {
            using var frame=await Receive(timeout.Token); var root=frame.RootElement; Error(root);
            if(root.TryGetProperty("nonce",out var n) && n.GetString()==nonce) return;
        }
    }
    static void Error(JsonElement root)
    {
        if(root.TryGetProperty("evt",out var evt) && evt.GetString()=="ERROR") throw new IOException("Discord rejected presence: "+root.GetProperty("data"));
    }
    async Task Send(int opcode,byte[] data,CancellationToken token)
    {
        var packet=new byte[8+data.Length];
        BinaryPrimitives.WriteInt32LittleEndian(packet,opcode); BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4),data.Length); data.CopyTo(packet,8);
        await pipe.WriteAsync(packet,token); await pipe.FlushAsync(token);
    }
    async Task<JsonDocument> Receive(CancellationToken token)
    {
        while(true)
        {
            var header=new byte[8]; await pipe.ReadExactlyAsync(header,token);
            var opcode=BinaryPrimitives.ReadInt32LittleEndian(header); var len=BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
            if(len is <0 or >1048576) throw new IOException("Invalid Discord IPC frame.");
            var data=new byte[len]; await pipe.ReadExactlyAsync(data,token);
            if(opcode==3) { await Send(4,data,token); continue; }
            if(opcode==2) throw new IOException("Discord closed the connection: "+System.Text.Encoding.UTF8.GetString(data));
            if(opcode==1) return JsonDocument.Parse(data);
        }
    }
    public void Dispose()=>pipe.Dispose();
}
