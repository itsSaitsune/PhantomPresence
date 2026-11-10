using System.Runtime.InteropServices;
namespace P5XboxPresence;
static class Entry
{
    [STAThread]
    static void Main(string[] args)
    {
        if(args.Any(a=>a is "--console" or "--setup" or "--probe" or "--dialogue-probe" or "--self-test" or "--settings-self-test" or "--compatibility-report" or "--launch-plan" or "--stop" or "--worker" or "--runtime-status" or "--runtime-self-test" or "--status-test-client"))
        {
            if(args.Contains("--console") || args.Contains("--setup")) AllocConsole();
            App.Run(args).GetAwaiter().GetResult();
            return;
        }
        ApplicationConfiguration.Initialize();
        var render=Array.IndexOf(args,"--render-ui");
        if(render>=0 && render+1<args.Length)
        {
            using var form=new CallingCard(false,true);
            form.ShowInTaskbar=false; form.StartPosition=FormStartPosition.Manual; form.Location=new Point(-30000,-30000);
            form.Show(); Application.DoEvents();
            using var bitmap=new Bitmap(form.Width,form.Height);
            form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));
            bitmap.Save(args[render+1],System.Drawing.Imaging.ImageFormat.Png);
            return;
        }
        using var instance=new Mutex(true,"Local\\P5XboxPresence.UI",out var first);
        if(!first)
        {
            if(args.Contains("--launch-game") && EventWaitHandle.TryOpenExisting("Local\\P5XboxPresence.Launch",out var request)) { using(request) request.Set(); }
            return;
        }
        Application.Run(new CallingCard(args.Contains("--launch-game")));
    }
    [DllImport("kernel32.dll")] static extern bool AllocConsole();
}
