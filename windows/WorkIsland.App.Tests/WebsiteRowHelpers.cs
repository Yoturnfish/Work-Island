using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using WorkIsland.App;

internal static partial class Program
{
    private static Button WebsiteRow(Popup popup,string identifier,string browser)
    {
        return WebsiteDescendants(popup.Child).OfType<Button>().Single(button=>AutomationProperties.GetAutomationId(button)=="Website-"+identifier
            ||AutomationProperties.GetName(button).StartsWith(browser+" · ",StringComparison.Ordinal)
            ||button.Content is DockPanel line&&line.Children.OfType<TextBlock>().Any(text=>text.Text.StartsWith(browser+" · ",StringComparison.Ordinal)));
    }
    private static System.Collections.Generic.IEnumerable<System.Windows.DependencyObject> WebsiteDescendants(System.Windows.DependencyObject root)
    {
        yield return root;
        for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in WebsiteDescendants(System.Windows.Media.VisualTreeHelper.GetChild(root,i)))yield return child;
    }
    private static async Task ClickWebsiteRowAsync(IslandWindow window,Button button)
    {
        var field=typeof(IslandWindow).GetField("browserDiscovery",Flags)!;var before=(Task?)field.GetValue(window);
        button.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
        var after=(Task?)field.GetValue(window);
        if(after is not null&&!ReferenceEquals(before,after))
        {
            // Await the actual owned provider task, including a task whose UI
            // deadline has expired. No replacement worker or process is started.
            try{await after.WaitAsync(TimeSpan.FromSeconds(6));}
            catch(Exception)when(after.IsCompleted){ /* Product feedback is checked by callers. */ }
        }
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
    }
}
