using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunProfileEditorLayout(IslandWindow window,string output,Action<string,bool,object?> check,bool review=false)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethods(Flags).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;
        settings.Paused=true;settings.ReduceMotion=true;Field<DispatcherTimer>("fullscreenTimer").Stop();Field<DispatcherTimer>("focusReturn").Stop();
        window.Left=360;window.Top=220;window.Topmost=true;window.Activate();
        if(review){window.ShowInTaskbar=true;window.Title="Work Island · 命名输入对齐验收 · "+Environment.ProcessId;}
        Call("ActivateProfile",settings.ColorProfiles.Single(p=>p.IsDefault).Id);Call("RenameCurrentProfile","");
        Call("ToggleExpanded",null,new RoutedEventArgs());Call("OpenRecognition",window,new RoutedEventArgs());Call("OpenProfilePopover");await Task.Delay(150);
        var menu=Field<Button>("RecognitionColorButton").ContextMenu!;var panel=(StackPanel)((MenuItem)menu.Items[0]).Header;var palette=panel.Children.OfType<UniformGrid>().Single();var name=ProfileNameEditor(panel);
        var metrics=new List<object>();
        async Task Geometry(string label)
        {
            name.Focus();Keyboard.Focus(name);name.CaretIndex=0;await Task.Delay(90);name.UpdateLayout();name.ApplyTemplate();
            var hint=(TextBlock)name.Template.FindName("NameHint",name);var caret=name.GetRectFromCharacterIndex(0,false);var hintX=hint.TransformToAncestor(name).Transform(new Point(hint.Padding.Left,0)).X;
            var expected=name.BorderThickness.Left+name.Padding.Left;
            check(label+" caret begins at the field inset",!caret.IsEmpty&&caret.Left>=expected-1&&caret.Left<=expected+3,new{caret=caret.ToString(),expected});
            check(label+" placeholder leaves the caret before its first glyph",!caret.IsEmpty&&caret.Left<=hintX+1&&hintX-caret.Left<=4,new{caretX=caret.Left,hintX});
            metrics.Add(new{label,text=name.Text,caret=caret.ToString(),hintX,expected,keyboardFocus=name.IsKeyboardFocusWithin});
        }
        try
        {
            var group=(StackPanel)palette.Children[0];var button=group.Children.OfType<Button>().Single();var caption=group.Children.OfType<TextBlock>().Single();
            menu.UpdateLayout();caption.UpdateLayout();
            var start=caption.ContentStart.GetCharacterRect(LogicalDirection.Forward);var endRect=caption.ContentEnd.GetCharacterRect(LogicalDirection.Backward);var textBounds=Rect.Union(start,endRect);
            var textCenter=caption.TransformToAncestor(palette).Transform(new Point(textBounds.Left+textBounds.Width/2,0)).X;var blockCenter=button.TransformToAncestor(palette).Transform(new Point(button.ActualWidth/2,0)).X;
            check("default caption text layout is centered under its green swatch",caption.Text=="默认"&&!textBounds.IsEmpty&&Math.Abs(textCenter-blockCenter)<1.2,new{textCenter,blockCenter,delta=textCenter-blockCenter,textBounds=textBounds.ToString(),caption.Text});
            await Geometry("empty default");
            ((StackPanel)palette.Children[3]).Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Geometry("empty ordinary color");
            check("empty ordinary profile still shows its naming hint",name.Text==""&&((TextBlock)name.Template.FindName("NameHint",name)).Visibility==Visibility.Visible,null);
            if(review)
            {
                void RecordInput(string action)=>File.AppendAllText(Path.Combine(output,"actual-editor-input.jsonl"),JsonSerializer.Serialize(new{action,at=DateTimeOffset.Now,text=name.Text,caret=name.CaretIndex,selection=name.SelectionLength,focus=name.IsKeyboardFocusWithin,rect=name.GetRectFromCharacterIndex(name.CaretIndex,false).ToString()},AtomicStore.JsonOptions)+Environment.NewLine);
                name.PreviewMouseUp+=(_,_)=>name.Dispatcher.BeginInvoke(()=>RecordInput("mouse-up"));name.PreviewKeyUp+=(_,_)=>RecordInput("key-up");
                File.WriteAllText(Path.Combine(output,"ready.json"),JsonSerializer.Serialize(new{pid=Environment.ProcessId,title=window.Title,scope="Own isolated profile popup, empty cyan naming field. Official click and typing review; no daily state."},AtomicStore.JsonOptions));
                var watch=System.Diagnostics.Stopwatch.StartNew();while(watch.Elapsed.TotalSeconds<180&&!File.Exists(Path.Combine(output,"finish-review.txt")))await Task.Delay(100);
            }
            name.Text="剪辑工作";name.CaretIndex=0;await Task.Delay(80);var first=name.GetRectFromCharacterIndex(0,false);
            check("typed text and caret share the single field inset",first.Left>=name.BorderThickness.Left+name.Padding.Left-1&&first.Left<=name.BorderThickness.Left+name.Padding.Left+3&&name.IsKeyboardFocusWithin,new{first=first.ToString()});
            name.Text=new string('测',45);name.CaretIndex=name.Text.Length;await Task.Delay(100);var end=name.GetRectFromCharacterIndex(name.Text.Length,true);
            check("long-name caret remains visible at the scrollable end",!end.IsEmpty&&end.Left>=0&&end.Left<=name.ActualWidth-name.BorderThickness.Right+1,new{end=end.ToString(),width=name.ActualWidth});
            name.Text="";await Geometry("cleared ordinary color");
            File.WriteAllText(Path.Combine(output,"profile-editor-layout.json"),JsonSerializer.Serialize(metrics,AtomicStore.JsonOptions));
        }
        finally{menu.IsOpen=false;await Task.Delay(150);Field<DispatcherTimer>("focusReturn").Stop();window.Topmost=false;}
    }
}
