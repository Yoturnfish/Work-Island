using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WorkIsland.App;

internal static partial class Program
{
    private static object AuditControllerClocks(IslandWindow window)
    {
        var animated=new List<object>();var seen=new HashSet<DependencyObject>();
        void Visit(DependencyObject value,string path)
        {
            if(!seen.Add(value)||seen.Count>4000)return;
            var local=value.GetLocalValueEnumerator();
            while(local.MoveNext())
            {
                var entry=local.Current;var source=DependencyPropertyHelper.GetValueSource(value,entry.Property);
                if(source.IsAnimated)animated.Add(new{path,property=entry.Property.Name,type=value.GetType().Name});
                var child=value.GetValue(entry.Property);
                if(child is DependencyObject dependency)Visit(dependency,path+"/"+entry.Property.Name);
                if(child is IEnumerable list&&child is not string){var i=0;foreach(var element in list){if(element is DependencyObject item)Visit(item,path+"/"+entry.Property.Name+"/"+i);if(++i>1000)break;}}
            }
            if(value is Visual)for(var i=0;i<VisualTreeHelper.GetChildrenCount(value);i++)Visit(VisualTreeHelper.GetChild(value,i),path+"/visual-"+i);
        }
        Visit(window,"island");
        var clocks=new List<object>();var rootFields=new List<string>();string? failure=null;
        try
        {
            var contextType=typeof(Visual).Assembly.GetType("System.Windows.Media.MediaContext")!;
            var context=contextType.GetMethod("From",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,[window.Dispatcher])!;
            var manager=contextType.GetProperty("TimeManager",Flags|BindingFlags.Public)!.GetValue(context)!;
            var root=manager.GetType().GetProperty("TimeManagerClock",Flags|BindingFlags.Public)!.GetValue(manager)!;
            var visited=new HashSet<object>();
            void Read(object? item,int depth)
            {
                if(item==null||depth>5||!visited.Add(item))return;
                if(item is WeakReference weak){Read(weak.Target,depth+1);return;}
                if(item is Clock clock)
                {
                    clocks.Add(new{type=clock.Timeline.GetType().Name,state=clock.CurrentState.ToString(),duration=clock.Timeline.Duration.ToString(),repeat=clock.Timeline.RepeatBehavior.ToString(),speed=clock.CurrentGlobalSpeed,from=(clock.Timeline as DoubleAnimation)?.From,to=(clock.Timeline as DoubleAnimation)?.To});
                    if(clock is ClockGroup group)foreach(var child in group.Children)Read(child,depth+1);
                }
                if(item is IEnumerable enumerable)foreach(var child in enumerable)Read(child,depth+1);
            }
            foreach(var field in root.GetType().GetFields(Flags|BindingFlags.Public))
            {
                rootFields.Add(field.Name+":"+field.FieldType.Name);
                if(field.Name.Contains("hild",StringComparison.Ordinal))Read(field.GetValue(root),0);
            }
        }
        catch(Exception error){failure=error.ToString();}
        return new{animated,clocks,rootFields,failure,scope="Read-only reflection over own WPF visual/animation objects. No timing manager or animation state mutation."};
    }
}
