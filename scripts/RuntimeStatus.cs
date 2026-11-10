using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace P5XboxPresence;

// Named Windows page-file-backed memory; no status/log files are created.
sealed class MemoryStatus : IDisposable
{
    const int Capacity=32768;
    readonly MemoryMappedFile mapping;
    readonly Mutex gate;
    internal MemoryStatus(string name)
    {
        gate=new Mutex(false,name+".Lock");
        mapping=MemoryMappedFile.CreateOrOpen(name,Capacity,MemoryMappedFileAccess.ReadWrite);
    }
    internal string Text
    {
        get
        {
            Lock();
            try
            {
                using var view=mapping.CreateViewAccessor();
                var length=view.ReadInt32(0);
                if(length<=0 || length>Capacity-4) return "Not connected";
                var data=new byte[length]; view.ReadArray(4,data,0,length);
                return Encoding.UTF8.GetString(data);
            }
            finally { gate.ReleaseMutex(); }
        }
        set
        {
            var data=Encoding.UTF8.GetBytes(value.Length>5000?value[..5000]:value);
            Lock();
            try
            {
                using var view=mapping.CreateViewAccessor();
                view.WriteArray(4,data,0,data.Length); view.Write(0,data.Length);
            }
            finally { gate.ReleaseMutex(); }
        }
    }
    void Lock()
    {
        try { if(!gate.WaitOne(5000)) throw new IOException("Presence status is temporarily busy."); }
        catch(AbandonedMutexException) { }
    }
    public void Dispose() { mapping.Dispose(); gate.Dispose(); }
}

static class RuntimeStatus
{
    static readonly Lazy<MemoryStatus> bus=new(()=>new MemoryStatus(@"Local\PhantomPresence.RuntimeStatus"));
    internal static string Text { get=>bus.Value.Text; set=>bus.Value.Text=value; }
    internal static void SelfTest()
    {
        var name=@"Local\PhantomPresence.StatusTest."+Guid.NewGuid().ToString("N");
        using var test=new MemoryStatus(name);
        if(test.Text!="Not connected") throw new Exception("New status memory should be empty.");
        test.Text="Error: Café · test";
        using(var reader=new MemoryStatus(name)) if(reader.Text!="Error: Café · test") throw new Exception("Status memory UTF-8 roundtrip failed.");
        var start=new ProcessStartInfo(RuntimePaths.Executable) { UseShellExecute=false,CreateNoWindow=true };
        start.ArgumentList.Add("--status-test-client"); start.ArgumentList.Add(name);
        using var child=Process.Start(start)??throw new IOException("Could not start status test client.");
        if(!child.WaitForExit(15000) || child.ExitCode!=0 || test.Text!="Connected") throw new Exception("Cross-process in-memory status failed.");
        Console.WriteLine("Cross-process memory status and UTF-8 checks passed; no status files created.");
    }
}
