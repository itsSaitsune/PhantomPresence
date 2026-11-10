using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace P5XboxPresence;

// External, read-only access. This never injects code or calls game functions.
sealed class ProcessMemory : IDisposable
{
    readonly SafeProcessHandle handle;
    internal long Base { get; }
    internal uint Timestamp { get; }
    internal uint ImageSize { get; }
    internal uint Pe { get; }
    internal ProcessMemory(Process process)
    {
        Base=(process.MainModule??throw new IOException("Cannot inspect P5R.exe.")).BaseAddress.ToInt64();
        handle=Native.OpenProcess(0x1010,false,process.Id);
        try
        {
            if(handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(),"Windows denied read access to the game.");
            if(U16(Base)!=0x5a4d) throw new IOException("Invalid executable header.");
            Pe=U32(Base+0x3c);
            if(Pe>1024*1024 || U32(Base+Pe)!=0x4550 || U16(Base+Pe+4)!=0x8664 || U16(Base+Pe+24)!=0x20b)
                throw new IOException("Expected a Windows x64 P5R executable.");
            Timestamp=U32(Base+Pe+8); ImageSize=U32(Base+Pe+80);
            if(ImageSize is <0x100000 or >0x40000000) throw new IOException("Invalid executable size.");
        }
        catch { handle.Dispose(); throw; }
    }
    internal byte[] Read(long address,int length)
    {
        var data=new byte[length];
        if(!Native.ReadProcessMemory(handle,(nint)address,data,(nuint)length,out var count) || count!=(nuint)length)
            throw new IOException("Game data temporarily unavailable.");
        return data;
    }
    internal ushort U16(long a)=>BinaryPrimitives.ReadUInt16LittleEndian(Read(a,2));
    internal uint U32(long a)=>BinaryPrimitives.ReadUInt32LittleEndian(Read(a,4));
    internal long Pointer(long a)=>BinaryPrimitives.ReadInt64LittleEndian(Read(a,8));
    internal long FindSequenceGlobal()
    {
        // AnimatedSwine37/p5rpc.lib, Sequencer.cs (GPL-3.0).
        var pattern=Signature.Parse("48 8B 05 ?? ?? ?? ?? 66 44 89 4C 24 ??");
        var sections=U16(Base+Pe+6);
        if(sections is <1 or >96) throw new IOException("Invalid executable sections.");
        var table=Base+Pe+24+U16(Base+Pe+20);
        var targets=new HashSet<long>();
        for(int section=0;section<sections;section++)
        {
            var header=table+section*40;
            if((U32(header+36)&0x20000000)==0) continue;
            var rva=U32(header+12); var size=U32(header+8);
            if(size==0 || rva>=ImageSize || size>ImageSize-rva) continue;
            const int chunk=64*1024;
            for(uint offset=0;offset<size;offset+=chunk)
            {
                var length=(int)Math.Min((uint)(chunk+pattern.Length-1),size-offset);
                byte[] data;
                try { data=Read(Base+rva+offset,length); } catch(IOException) { continue; }
                foreach(var index in Signature.Matches(data,pattern))
                {
                    var address=Base+rva+offset+index;
                    var target=Signature.RipTarget(address,data.AsSpan(index+3,4),7);
                    if(target>=Base && target<=Base+ImageSize-8) targets.Add(target);
                }
            }
        }
        if(targets.Count!=1) throw new IOException("Unsupported Steam/modified build: the sequence signature is missing or ambiguous. Xbox offsets were not used.");
        return targets.Single();
    }
    public void Dispose()=>handle.Dispose();
}

static class Signature
{
    internal static byte?[] Parse(string text)=>text.Split(' ',StringSplitOptions.RemoveEmptyEntries).Select(s=>s=="??"?(byte?)null:Convert.ToByte(s,16)).ToArray();
    internal static IEnumerable<int> Matches(byte[] data,byte?[] pattern)
    {
        for(int i=0;i<=data.Length-pattern.Length;i++)
        {
            var match=true;
            for(int n=0;n<pattern.Length;n++) if(pattern[n] is byte value && data[i+n]!=value) { match=false; break; }
            if(match) yield return i;
        }
    }
    internal static long RipTarget(long instruction,ReadOnlySpan<byte> displacement,int length)=>checked(instruction+length+BinaryPrimitives.ReadInt32LittleEndian(displacement));
}

sealed class SteamReader : IGameReader
{
    readonly Process process;
    readonly ProcessMemory memory;
    readonly long sequenceGlobal;
    // Public reference: Dekirai/MultiPresence, Presence/P5R.cs (MIT).
    // These RVAs are NOT presumed portable to every Steam/modified release.
    const uint CalendarRva=0x285c24c;
    public string Build=>"Steam-compatible experimental (date/time and sequence; no locations/dialogue)";
    public bool Exited=>process.HasExited;
    internal SteamReader(Process process,ProcessMemory identity)
    {
        this.process=process;
        memory=new ProcessMemory(process);
        try
        {
            if(identity.ImageSize<=CalendarRva+3) throw new IOException("Unsupported executable layout.");
            sequenceGlobal=memory.FindSequenceGlobal();
            Console.WriteLine($"Experimental Steam reader: PE timestamp {identity.Timestamp}, image size {identity.ImageSize}. Locations and conversation partners are unavailable.");
        }
        catch { memory.Dispose(); throw; }
    }
    public DialogueInfo[] DialogueDiagnostics()=>Array.Empty<DialogueInfo>();
    public GameState? Read(Dictionary<string,string> locations)
    {
        var root=memory.Pointer(sequenceGlobal); if(root==0) return null;
        var info=memory.Pointer(root+72); if(info==0) return null;
        var sequence=unchecked((int)memory.U32(info+4));
        if(sequence is <-1 or >13) throw new IOException("Unsupported Steam/modified sequence layout.");
        if(sequence is 0 or 1) return State(0,0,sequence,null);
        if(sequence is -1 or 2 or 10) return null;
        var calendar=memory.Read(memory.Base+CalendarRva,3);
        var day=BinaryPrimitives.ReadUInt16LittleEndian(calendar); var time=calendar[2];
        if(day is <8 or >371 || time>6) throw new IOException("Steam calendar layout did not validate; this executable needs another build profile.");
        string? eventId=null;
        if(sequence is 6 or 7)
        {
            var evt=memory.Pointer(info+24);
            if(evt!=0)
            {
                var major=memory.U32(evt); var minor=memory.U32(evt+4);
                if(major<=9999 && minor<=9999) eventId=$"{major}_{minor}";
            }
        }
        // Reject a snapshot collected while the scene/calendar was changing.
        if(memory.Pointer(sequenceGlobal)!=root || memory.Pointer(root+72)!=info || memory.U32(info+4)!=(uint)sequence ||
            !memory.Read(memory.Base+CalendarRva,3).SequenceEqual(calendar)) return null;
        return State(day,time,sequence,eventId);
    }
    internal static GameState State(int day,int time,int sequence,string? eventId)
    {
        string activity=sequence switch {
            0 or 1=>"In the main menu",4=>"In battle",6 or 7=>eventId==null?"Following the story":GameReader.ClassifyActivity(0,0,null,eventId),
            8 or 9=>"Watching a movie",11 or 12=>"Advancing the calendar",13=>"Viewing dungeon results",_=>"Playing Persona 5 Royal"
        };
        var hasCalendar=day>=8 && time is >=0 and <=6;
        return new(hasCalendar?GameReader.Date(day).ToString("MMM d (ddd)",CultureInfo.InvariantCulture):"",
            hasCalendar?new[]{"Early Morning","Morning","Lunchtime","Daytime","After School","Evening","Late Night"}[time]:"",
            "","",day,time,activity,null,null,sequence,eventId);
    }
    public void Dispose() { memory.Dispose(); process.Dispose(); }
}

static class BuildIdentity
{
    internal static bool IsTestedXbox(uint timestamp,uint size)=>timestamp==1670832233 && size==396046336;
}

static class CompatibilityTests
{
    internal static void Run()
    {
        if(!BuildIdentity.IsTestedXbox(1670832233,396046336) || BuildIdentity.IsTestedXbox(1670832234,396046336) || BuildIdentity.IsTestedXbox(1670832233,396046337)) throw new Exception("Build routing accepted a different executable identity.");
        var signature=Signature.Parse("48 8B 05 ?? ?? ?? ?? 66 44 89 4C 24 ??");
        var fixture=new byte[]{0,0x48,0x8b,5,0xff,0xff,0xff,0xff,0x66,0x44,0x89,0x4c,0x24,8};
        if(!Signature.Matches(fixture,signature).SequenceEqual(new[]{1}) || Signature.Matches(new byte[8],signature).Any()) throw new Exception("Signature boundary test failed.");
        fixture[8]=0;
        if(Signature.Matches(fixture,signature).Any()) throw new Exception("Signature mismatch accepted.");
        if(Signature.RipTarget(0x1000,new byte[]{0xfc,0xff,0xff,0xff},7)!=0x1003) throw new Exception("Signed RIP displacement failed.");
        if(SteamReader.State(0,0,0,null).Activity!="In the main menu" || SteamReader.State(147,5,4,null).Activity!="In battle" ||
            SteamReader.State(147,5,6,"760_101").Activity!="Studying" || SteamReader.State(147,5,6,"760_1").Activity!="Reading" ||
            SteamReader.State(147,5,6,null).Activity!="Following the story" || SteamReader.State(147,5,3,null).Location!="") throw new Exception("Steam sequence classification failed.");
    }
}
