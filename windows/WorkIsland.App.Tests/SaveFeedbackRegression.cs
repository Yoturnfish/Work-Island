using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunSaveFeedbackChecks(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;var store=Field<AtomicStore>("store");settings.Paused=true;settings.BackgroundStyle="black";engine.ResetObservation();Call("Persist");
        var notice=Field<TextBlock>("MessageLabel");var scroller=Field<ScrollViewer>("PageScroller");var editor=Field<TextBox>("TitleEditor");
        var before=File.ReadAllBytes(store.Path);var history=JsonSerializer.Serialize(engine.Data.Sessions,AtomicStore.JsonOptions);var rules=JsonSerializer.Serialize(settings.Rules,AtomicStore.JsonOptions);
        void Enter(string title){editor.Visibility=Visibility.Visible;editor.Text=title;Call("TitleKeyDown",editor,new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window)!,0,Key.Enter));}
        Rect Bounds(){notice.UpdateLayout();return notice.TransformToAncestor(scroller).TransformBounds(new Rect(0,0,notice.ActualWidth,notice.ActualHeight));}
        bool InView(){var b=Bounds();return notice.IsVisible&&b.Height>0&&b.Top>=-.5&&b.Bottom<=scroller.ActualHeight-7.5;}
        using(var held=new FileStream(store.Path,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            Enter("暂留主标题");await Task.Delay(500);
            check("failed title submit retains memory title and closes editor safely",settings.DisplayTitle=="暂留主标题"&&editor.Visibility==Visibility.Collapsed&&engine.Current.State=="paused",null);
            check("failed title submit keeps durable bytes unchanged",File.ReadAllBytes(store.Path).SequenceEqual(before),null);
            check("ordinary save failure does not ask to retry exit",notice.Text.Contains("保存失败")&&!notice.Text.Contains("退出"),notice.Text);
            check("first failure scrolls actual warning fully into rounded viewport",InView(),new{bounds=Bounds(),height=scroller.ActualHeight,scroller.VerticalOffset});
            var offset=scroller.VerticalOffset;Call("Persist");await Task.Delay(80);
            check("repeated failure keeps current page and scroll position",ReferenceEquals(Field<FrameworkElement>("activePage"),Field<FrameworkElement>("SettingsPage"))&&Math.Abs(offset-scroller.VerticalOffset)<.5,null);
        }
        Call("Persist");await Task.Delay(80);
        check("background recovery replaces only its warning with saved status",notice.Text=="更改已保存。",notice.Text);
        check("recovered saved status remains fully visible",InView(),new{bounds=Bounds(),height=scroller.ActualHeight});
        check("recovery saves both title aliases",store.Load().Settings.DisplayTitle=="暂留主标题"&&ColorProfilesService.Active(store.Load().Settings)!.Title=="暂留主标题",null);
        check("recovery does not navigate or change pause history or rules",ReferenceEquals(Field<FrameworkElement>("activePage"),Field<FrameworkElement>("SettingsPage"))&&engine.Current.State=="paused"&&JsonSerializer.Serialize(engine.Data.Sessions,AtomicStore.JsonOptions)==history&&JsonSerializer.Serialize(settings.Rules,AtomicStore.JsonOptions)==rules,null);
        using(var held=new FileStream(store.Path,FileMode.Open,FileAccess.Read,FileShare.Read)){settings.DisplayTitle="新更改";Call("Persist");window.ShowError("较新的独立错误");}
        Call("Persist");check("background success preserves a newer independent error",notice.Text=="较新的独立错误",notice.Text);
        settings.Language="en";Call("ApplyLanguage");
        using(var held=new FileStream(store.Path,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            settings.DisplayTitle="English rename";Call("Persist");await Task.Delay(100);
            check("ordinary English warning describes pending data",notice.Text.Contains("Save failed")&&!notice.Text.Contains("exit"),notice.Text);
            settings.Language="zh";Call("ApplyLanguage");check("owned pending warning follows language change",notice.Text.Contains("保存失败")&&!notice.Text.Contains("Save failed"),notice.Text);
        }
        Call("Persist");check("recovered status uses current Chinese language",notice.Text=="更改已保存。",notice.Text);
        settings.Language="en";Call("ApplyLanguage");check("owned saved status follows later language change",notice.Text=="Changes saved.",notice.Text);
        using(var held=new FileStream(store.Path,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            check("forced exit still refuses IO failure",!(bool)Call("PrepareNormalExit")!,null);
            check("exit-specific failure still explains retry exit",notice.Text.Contains("retry exit"),notice.Text);
        }
        Call("Persist");check("background recovery also resolves forced-exit warning without exiting",notice.Text=="Changes saved."&&window.IsVisible,notice.Text);
        File.WriteAllText(Path.Combine(output,"save-feedback.json"),JsonSerializer.Serialize(new{scope="Actual own WPF title Enter handler and viewport geometry under FileStream IO lock; direct handler/control preparation, not native typing. Recovery uses real Persist. Newer error ownership, language and forced exit checked."},AtomicStore.JsonOptions));
    }
}
