using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using WorkIsland.App;
using WorkIsland.Core;

internal static class FeatureRegressions
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    public static void RunRegistryOnly(string output,Action<string,bool,object?> check)=>RegistryCases(output,check);
    public static void Run(IslandWindow window,AppState state,TrackingEngine engine,string output,Action<string,bool,object?> check)
    {
        void Check(string name,bool passed,object? evidence=null)=>check(name,passed,evidence);
        object? Call(string method,params object?[] arguments)=>typeof(IslandWindow).GetMethod(method,Flags)!.Invoke(window,arguments);
        var settings=JsonSerializer.Serialize(state.Settings,AtomicStore.JsonOptions);var sessions=state.Sessions.ToArray();
        try
        {
            var original=state.Settings.ActiveProfileId!;
            var initialBlueCount=state.Settings.ColorProfiles.Count(p=>p.Accent=="blue");
            Call("CreateProfile");var one=state.Settings.ActiveProfileId!;
            Check("legacy theme model can create an empty independent blue theme",one!=original && state.Settings.Accent=="blue" && state.Settings.Rules.Count==0);
            Call("RenameCurrentProfile","  剪辑组合  ");
            var input=(TextBox)typeof(IslandWindow).GetField("RuleInput",Flags)!.GetValue(window)!;input.Text="feature-editor.exe";Call("AddRule",null,new RoutedEventArgs());
            state.Settings.ProgressStyle="waveform";Call("Persist");
            Call("CreateProfile");var two=state.Settings.ActiveProfileId!;
            Check("legacy theme model retains multiple IDs sharing the same color",one!=two && state.Settings.ColorProfiles.Count(p=>p.Accent=="blue")==initialBlueCount+2);
            Call("ActivateProfile",one);
            Check("UI theme switch restores renamed title, rules and style",state.Settings.DisplayTitle=="剪辑组合" && state.Settings.Rules.Single().Identifier=="feature-editor.exe" && state.Settings.ProgressStyle=="waveform");
            state.Sessions.Add(new(){DayKey=LogicalDay.Key(DateTimeOffset.Now,engine.Zone,state.Settings.DayResetMinutes),StartedAt=DateTimeOffset.Now,EndedAt=DateTimeOffset.Now.AddSeconds(7),EffectiveSeconds=7});
            Check("UI delete command empties active theme without replacing it with default",(bool)Call("DeleteCurrentProfile")! && state.Settings.ActiveProfileId==one && state.Settings.Rules.Count==0);
            Check("UI theme deletion retains work history",state.Sessions.Sum(s=>s.EffectiveSeconds)==7);
            var persisted=new AtomicStore(Path.Combine(output,"isolated-state.json")).Load();
            Check("UI deleted theme stays unselected after state reload",persisted.Settings.ActiveProfileId==one && persisted.Settings.Rules.Count==0);
            Call("ActivateProfile",original);Check("UI protects the built-in default theme",!(bool)Call("DeleteCurrentProfile")!);
            Call("ToggleExpanded",null,new RoutedEventArgs());Call("ShowPage","recognition",false);window.UpdateLayout();Call("OpenProfilePopover");
            var colorButton=(Button)typeof(IslandWindow).GetField("RecognitionColorButton",Flags)!.GetValue(window)!;
            var header=(StackPanel)((MenuItem)colorButton.ContextMenu!.Items[0]).Header;
            Check("color management popover exposes seven source slots and naming controls",colorButton.ContextMenu.IsOpen
                && ((System.Windows.Controls.Primitives.UniformGrid)header.Children[1]).Children.Count==7 && header.Children.OfType<TextBox>().Count()==1);
            var name=header.Children.OfType<TextBox>().Single();
            Check("popup controls inherit the product's dark styles instead of Windows defaults",name.Style==window.FindResource(typeof(TextBox))
                && colorButton.ContextMenu.Template is not null);
            Check("profile naming uses one input without a selection bar or naming label",!header.Children.OfType<ComboBox>().Any()&&!header.Children.OfType<DockPanel>().Any());
            name.Text="关闭弹出层也保存";
            Check("theme name text binding persists without pressing Enter",state.Settings.DisplayTitle=="关闭弹出层也保存"
                && new AtomicStore(Path.Combine(output,"isolated-state.json")).Load().Settings.DisplayTitle=="关闭弹出层也保存");
            name.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window)!,0,Key.Escape){RoutedEvent=Keyboard.KeyDownEvent});
            Check("Escape from theme name field dismisses the popover",!colorButton.ContextMenu.IsOpen);
            Call("CollapseToTimer");
            var titleEditor=(TextBox)typeof(IslandWindow).GetField("TitleEditor",Flags)!.GetValue(window)!;
            foreach(var titleCase in new[]{
                (name:"overflow emoji",input:new string('a',59)+"😀",expected:new string('a',59)),
                (name:"exact-boundary emoji",input:new string('a',58)+"😀",expected:new string('a',58)+"😀"),
                (name:"trimmed Chinese emoji",input:"  剪辑🎬  ",expected:"剪辑🎬")})
            {
                var rulesBefore=JsonSerializer.Serialize(state.Settings.Rules,AtomicStore.JsonOptions);
                var sessionsBefore=JsonSerializer.Serialize(state.Sessions,AtomicStore.JsonOptions);
                titleEditor.Text=titleCase.input;
                Call("TitleKeyDown",titleEditor,new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window)!,0,Key.Enter));
                var restored=new AtomicStore(Path.Combine(output,"isolated-state.json")).Load();
                Check("UI title Enter persists intact Unicode: "+titleCase.name,state.Settings.DisplayTitle==titleCase.expected
                    && restored.Settings.DisplayTitle==titleCase.expected && ColorProfilesService.Active(restored.Settings)!.Title==titleCase.expected
                    && JsonSerializer.Serialize(restored.Settings.Rules,AtomicStore.JsonOptions)==rulesBefore
                    && JsonSerializer.Serialize(restored.Sessions,AtomicStore.JsonOptions)==sessionsBefore,
                    new{inputLength=titleCase.input.Length,savedLength=restored.Settings.DisplayTitle.Length});
            }
            var login=(CheckBox)typeof(IslandWindow).GetField("LoginCheck",Flags)!.GetValue(window)!;
            login.IsChecked=true;Call("ChangeLoginStartup",login,new RoutedEventArgs());
            using(var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"startup-fixture.json"))))
                Check("login checkbox enables isolated registration through production handler",login.IsEnabled && login.IsChecked==true && fixture.RootElement.GetProperty("enabled").GetBoolean());
            login.IsChecked=false;Call("ChangeLoginStartup",login,new RoutedEventArgs());
            using(var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"startup-fixture.json"))))
                Check("login checkbox disables only its isolated registration",login.IsChecked==false && !fixture.RootElement.GetProperty("enabled").GetBoolean());
            foreach(var mode in new[]{"tracking","away","paused"})
            {
                using var bitmap=TrayArtwork.Render(mode,"green",32);using var icon=TrayArtwork.Icon(mode,"green");
                Check("formal tray "+mode+" glyph has transparent margins and opaque status color",bitmap.GetPixel(0,0).A==0 && bitmap.GetPixel(16,5).A==255 && icon.Handle!=IntPtr.Zero);
            }
            var ico=TrayArtwork.Ico("tracking","green");
            Check("formal app icon supplies 16/20/24/32/48/64/256 resolution entries",BitConverter.ToUInt16(ico,4)==7 && File.Exists(Path.Combine(AppContext.BaseDirectory,"Assets","WorkIsland.ico")));
        }
        finally
        {
            state.Settings=JsonSerializer.Deserialize<Settings>(settings,AtomicStore.JsonOptions)!;state.Sessions.Clear();state.Sessions.AddRange(sessions);
            engine.ResetObservation();Call("ProfileChanged");
        }
        RegistryCases(output,Check);
        InstallationCases(output,Check);
    }
    private static void RegistryCases(string output,Action<string,bool,object?> check)
    {
        void Check(string name,bool value,object? evidence=null)=>check(name,value,evidence);
        var scope=@"Software\WorkIsland\Acceptance\Startup-"+Guid.NewGuid().ToString("N");
        object? Daily(){using var key=Registry.CurrentUser.OpenSubKey(WindowsStartupRegistration.RunKey);return key?.GetValue(WindowsStartupRegistration.ValueName);}
        var dailyBefore=Daily();var executable=Path.Combine(output,"Startup path with spaces","WorkIsland.exe");Directory.CreateDirectory(Path.GetDirectoryName(executable)!);File.WriteAllText(executable,"Registry fixture; never executed.");
        var registration=new WindowsStartupRegistration(executable,Path.Combine(output,"中文 数据"),scope);
        try
        {
            Check("unregistered isolated HKCU login entry reads disabled",!registration.IsEnabled);
            registration.SetEnabled(true);
            using(var key=Registry.CurrentUser.OpenSubKey(scope))Check("HKCU login entry stores quoted executable, minimized flag and isolated data",registration.IsEnabled && Equals(key!.GetValue(WindowsStartupRegistration.ValueName),registration.Command),registration.Command);
            using(var key=Registry.CurrentUser.OpenSubKey(scope,true))key!.SetValue("Unrelated","keep");
            registration.SetEnabled(false);
            using(var key=Registry.CurrentUser.OpenSubKey(scope))Check("disabling login deletes own value and retains unrelated values",!registration.IsEnabled && Equals(key!.GetValue("Unrelated"),"keep"));
            var oldPortable=StartupCommand.Create(Path.Combine(output,"Old portable","WorkIsland.exe"),Path.Combine(output,"中文 数据"));
            using(var key=Registry.CurrentUser.OpenSubKey(scope,true))key!.SetValue(WindowsStartupRegistration.ValueName,oldPortable);
            Check("old portable startup path does not mark current installed copy enabled",!registration.IsEnabled);
            registration.SetEnabled(true);
            using(var key=Registry.CurrentUser.OpenSubKey(scope))Check("enabling current copy updates old portable startup path",registration.IsEnabled && Equals(key!.GetValue(WindowsStartupRegistration.ValueName),registration.Command));
            var foreign="\"C:\\Other\\Other.exe\"";using(var key=Registry.CurrentUser.OpenSubKey(scope,true))key!.SetValue(WindowsStartupRegistration.ValueName,foreign);
            var refused=false;try{registration.SetEnabled(true);}catch(InvalidOperationException){refused=true;}
            using(var key=Registry.CurrentUser.OpenSubKey(scope))Check("foreign same-name startup value is refused without overwrite",refused && Equals(key!.GetValue(WindowsStartupRegistration.ValueName),foreign));
            var command=StartupCommand.Create(executable,@"D:\");var arguments=Parse(command);var savedArguments=Parse(registration.Command);
            Check("Windows argv parser roundtrips spaces, Unicode and trailing root separator",arguments.Length==4 && arguments[0]==Path.GetFullPath(executable) && arguments[1]=="--start-minimized" && arguments[2]=="--data-dir" && arguments[3]==@"D:\"
                && savedArguments[3]==Path.GetFullPath(Path.Combine(output,"中文 数据")),command);
        }
        finally
        {
            if(!scope.StartsWith(@"Software\WorkIsland\Acceptance\Startup-",StringComparison.Ordinal))throw new Exception("Unsafe registry cleanup scope");
            Registry.CurrentUser.DeleteSubKeyTree(scope,false);
        }
        Check("registry acceptance leaves user's actual Run entry unchanged",Equals(dailyBefore,Daily()));
    }
    private static void InstallationCases(string output,Action<string,bool,object?> check)
    {
        void Check(string name,bool value)=>check(name,value,null);
        var directory=Path.Combine(output,"installation-identity-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        Check("portable app creates no installation mutex",InstallationIdentity.Acquire(directory) is null);
        var id=Guid.NewGuid();var name=InstallationIdentity.MutexName(id);var marker=Path.Combine(directory,InstallationIdentity.MarkerName);
        File.WriteAllText(marker,"[installation]\nAppId="+id.ToString("D")+"\n");
        var one=InstallationIdentity.Acquire(directory)!;var two=InstallationIdentity.Acquire(directory)!;
        var exists=Mutex.TryOpenExisting(name,out var probe);probe?.Dispose();Check("installed copy exposes real named mutex for installer",exists);
        one.Dispose();exists=Mutex.TryOpenExisting(name,out probe);probe?.Dispose();Check("installation mutex remains while another copy is alive",exists);
        two.Dispose();exists=Mutex.TryOpenExisting(name,out probe);probe?.Dispose();Check("installation mutex disappears after final process reference exits",!exists);
        File.WriteAllText(marker,"AppId="+Guid.NewGuid().ToString("D"));
        using(var other=InstallationIdentity.Acquire(directory))Check("separate installation identity never creates another installation's mutex",!Mutex.TryOpenExisting(name,out probe));probe?.Dispose();
        foreach(var content in new[]{"AppId=invalid","AppId="+id+"\nAppId="+id,new string('x',8193)})
        {
            File.WriteAllText(marker,content);var rejected=false;try{using var value=InstallationIdentity.Acquire(directory);}catch(InvalidDataException){rejected=true;}
            Check("invalid, duplicate or oversized install marker refuses startup: "+content.Length,rejected);
        }
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]private static extern nint CommandLineToArgvW(string command,out int count);
    [DllImport("kernel32.dll")]private static extern nint LocalFree(nint address);
    private static string[] Parse(string command)
    {
        var memory=CommandLineToArgvW(command,out var count);if(memory==0)throw new Exception("argv parse failed");
        try{return Enumerable.Range(0,count).Select(i=>Marshal.PtrToStringUni(Marshal.ReadIntPtr(memory,i*IntPtr.Size))!).ToArray();}
        finally{LocalFree(memory);}
    }
}
