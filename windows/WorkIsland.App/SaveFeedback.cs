using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace WorkIsland.App;

public partial class IslandWindow
{
    private string? saveFeedbackText;
    private bool saveFeedbackRecovered,saveFeedbackForExit;
    private string CurrentSaveFeedback=>saveFeedbackRecovered?Text("更改已保存。"):saveFeedbackForExit?SaveFailureMessage:
        engine.Data.Settings.Language=="en"?"Save failed; data remains in memory.":"保存失败，数据暂留内存。";
    private void PresentSaveFailure(bool forExit)
    {
        saveFeedbackRecovered=false;saveFeedbackForExit=forExit;
        var message=CurrentSaveFeedback;ShowError(message);
        if(ReferenceEquals(activePage,SettingsPage)&&MessageLabel.Text==message)saveFeedbackText=message;
    }
    private void ResolveSaveFeedback()
    {
        if(saveFeedbackText is not {} previous)return;
        if(MessageLabel.Text!=previous){saveFeedbackText=null;return;}
        if(saveFeedbackRecovered)return;
        saveFeedbackRecovered=true;saveFeedbackText=CurrentSaveFeedback;
        MessageLabel.Text=saveFeedbackText;MessageLabel.Foreground=Brushes.White;
    }
    private void RefreshSaveFeedbackLanguage()
    {
        if(saveFeedbackText is not {} previous)return;
        if(MessageLabel.Text!=previous){saveFeedbackText=null;return;}
        saveFeedbackText=CurrentSaveFeedback;MessageLabel.Text=saveFeedbackText;
    }
    private void RevealSettingsMessage(string expected)
    {
        // The compact-to-page transition must arrange the shared scroller first.
        Dispatcher.BeginInvoke(()=>
        {
            if(MessageLabel.Text!=expected||!MessageLabel.IsVisible||!ReferenceEquals(activePage,SettingsPage))return;
            MessageLabel.UpdateLayout();
            var bottom=MessageLabel.TranslatePoint(new Point(0,MessageLabel.ActualHeight),PageScroller).Y;
            PageScroller.ScrollToVerticalOffset(PageScroller.VerticalOffset+Math.Max(0,bottom-(PageScroller.ActualHeight-8)));
        },DispatcherPriority.Loaded);
    }
}
