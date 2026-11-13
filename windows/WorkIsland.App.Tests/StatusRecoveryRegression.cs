using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Interop;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private sealed class StatusShellFixture
    {
        public readonly List<(uint Message,NativeStatusItem.IconData Data)> Calls=[];
        public int AddFailures,ModifyFailures;
        public bool Invoke(uint message,ref NativeStatusItem.IconData data)
        {
            Calls.Add((message,data));
            if(message==0&&AddFailures>0){AddFailures--;return false;}
            if(message==1&&ModifyFailures>0){ModifyFailures--;return false;}
            return true;
        }
        public int Adds=>Calls.Count(c=>c.Message==0);
    }
    private static void SimulateOwnTaskbarCreated(NativeStatusItem item,nint handle)
    {
        var type=typeof(NativeStatusItem);var message=(uint)type.GetField("taskbarCreated",Flags)!.GetValue(item)!;
        object?[] args=[handle,unchecked((int)message),(nint)0,(nint)0,false];type.GetMethod("Message",Flags)!.Invoke(item,args);
    }
    [DllImport("shell32.dll",EntryPoint="Shell_NotifyIconW",CharSet=CharSet.Unicode)]
    private static extern bool RecoveryNotifyIcon(uint message,ref NativeStatusItem.IconData data);
    private static async Task RunStatusRecoveryChecks(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        var handle=new WindowInteropHelper(window).Handle;
        using var bitmap=new Bitmap(32,32);using(var g=Graphics.FromImage(bitmap))g.Clear(Color.LimeGreen);
        var fake=new StatusShellFixture();using var item=new NativeStatusItem(handle,Path.Combine(output,"fake-status"),fake.Invoke);
        item.Update(bitmap,"Work Island · stable");var selected=0;item.Selected+=()=>selected++;
        check("initial icon installs through unchanged Shell boundary",item.SetVisible(true)&&item.Installed&&fake.Adds==1,null);
        var identity=fake.Calls.Single(c=>c.Message==0).Data;
        fake.AddFailures=1;SimulateOwnTaskbarCreated(item,handle);
        check("simulated own shell restart preserves failed reconstruction state",!item.Installed&&fake.Adds==2,null);
        var atFailure=fake.Adds;for(var i=0;i<30;i++)item.UpdateTooltip("Work Island · stable");
        check("failed reconstruction is not retried on every frame",fake.Adds==atFailure,new{fake.Adds,atFailure});
        await Task.Delay(1150);item.UpdateTooltip("Work Island · stable");
        check("unchanged tooltip refresh reconstructs lost menu entry",item.Installed&&fake.Adds==atFailure+1,new{item.Installed,fake.Adds});
        var recovered=fake.Calls.Last(c=>c.Message==0).Data;
        check("reconstruction retains owner identity latest artwork and tooltip",recovered.Window==identity.Window&&recovered.Guid==identity.Guid&&recovered.Icon==identity.Icon&&recovered.Tip==identity.Tip,null);
        check("reconstruction never activates or selects panel",selected==0,null);
        fake.AddFailures=10;SimulateOwnTaskbarCreated(item,handle);var repeatedStart=fake.Adds;
        await Task.Delay(1150);item.UpdateTooltip("Work Island · stable");
        check("continued shell failure remains eligible for a bounded later retry",!item.Installed&&fake.Adds==repeatedStart+1,new{fake.Adds,repeatedStart});
        item.SetVisible(false);var hiddenAdds=fake.Adds;await Task.Delay(1150);item.Update(bitmap,"hidden");item.UpdateTooltip("hidden");
        check("explicit hide cancels pending recovery",!item.Installed&&fake.Adds==hiddenAdds,null);
        fake.AddFailures=0;check("explicit show after hide can install normally",item.SetVisible(true)&&item.Installed,null);
        fake.ModifyFailures=1;fake.AddFailures=1;item.Update(bitmap,"Work Island · changed");
        check("modify failure can leave a recoverable missing entry",!item.Installed,null);
        await Task.Delay(1150);item.Update(bitmap,"Work Island · changed");
        check("rejected retry after modify failure stays pending for a later refresh",!item.Installed,null);
        await Task.Delay(1150);item.Update(bitmap,"Work Island · changed");
        check("artwork refresh also reconstructs entry after modify failure",item.Installed,null);
        item.Dispose();var disposedCalls=fake.Calls.Count;item.Update(bitmap,"disposed");item.UpdateTooltip("disposed");item.SetVisible(true);item.Dispose();
        check("disposed entry cannot be resurrected or create further shell work",!item.Installed&&fake.Calls.Count==disposedCalls,new{calls=fake.Calls.Count,disposedCalls});

        // Real Shell registration of this one fixture GUID only. No Explorer
        // restart, broadcast, external window input or user's status item.
        NativeStatusItem.IconData latest=default;var realAdds=0;var rejectAdds=0;
        bool RealBoundary(uint message,ref NativeStatusItem.IconData data)
        {
            latest=data;if(message==0){realAdds++;if(rejectAdds>0){rejectAdds--;return false;}}
            return RecoveryNotifyIcon(message,ref data);
        }
        var native=new NativeStatusItem(handle,Path.Combine(output,"real-native-status"),RealBoundary);
        try
        {
            native.Update(bitmap,"Work Island · recovery fixture");
            check("own real Shell entry installs",native.SetVisible(true)&&native.Installed,null);
            var remove=latest;var removed=RecoveryNotifyIcon(2,ref remove);rejectAdds=1;SimulateOwnTaskbarCreated(native,handle);
            check("own real entry loss and rejected first add are recorded",removed&&!native.Installed,new{removed,realAdds});
            await Task.Delay(1150);native.UpdateTooltip("Work Island · recovery fixture");
            check("own real Shell entry is reconstructed by normal tooltip path",native.Installed&&realAdds==3,new{native.Installed,realAdds});
            native.SetVisible(false);var addsAfterHide=realAdds;await Task.Delay(1150);native.UpdateTooltip("Work Island · recovery fixture");
            check("hidden real entry stays hidden without retry",!native.Installed&&realAdds==addsAfterHide,null);
        }
        finally
        {
            native.Dispose();var cleanup=latest;RecoveryNotifyIcon(2,ref cleanup);
        }
        File.WriteAllText(Path.Combine(output,"status-recovery.json"),JsonSerializer.Serialize(new{processId=Environment.ProcessId,windowHandle=(long)handle,fakeCalls=fake.Calls.Select(c=>new{message=c.Message,owner=(long)c.Data.Window,guid=c.Data.Guid,icon=(long)c.Data.Icon,tip=c.Data.Tip}),realAdds,scope="Controlled Shell return failures and simulated own TaskbarCreated handler; actual native icon creation/registration/removal for unique own GUID. No Explorer restart, physical icon click, other window or user data. Uses real delay for retry window; no fake clock."},AtomicStore.JsonOptions));
    }
}
