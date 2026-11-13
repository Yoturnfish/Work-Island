using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;

namespace WorkIsland.App;

public partial class IslandWindow
{
    private enum LoginFeedback { None, Unavailable, WriteFailed, NotApplied }
    private LoginFeedback loginFeedback;
    private string? loginFeedbackText;
    private static bool StartupFailure(Exception error)=>error is IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException;
    private string LoginFeedbackMessage(LoginFeedback feedback)=>engine.Data.Settings.Language=="en"?feedback switch
    {
        LoginFeedback.Unavailable=>"Login status unavailable. Reopen Settings.",
        LoginFeedback.WriteFailed=>"Login setting failed. Retry.",
        LoginFeedback.NotApplied=>"Login setting not applied. Retry.",
        _=>""
    }:feedback switch
    {
        LoginFeedback.Unavailable=>"无法读取登录项，请重新打开设置。",
        LoginFeedback.WriteFailed=>"登录项设置失败，请重试。",
        LoginFeedback.NotApplied=>"系统未应用登录项设置，请重试。",
        _=>""
    };
    private bool RefreshLoginStartupState(bool showError)
    {
        try
        {
            var actual=startupRegistration.IsEnabled;
            LoginCheck.IsChecked=actual;LoginCheck.IsEnabled=true;LoginCheck.Opacity=1;
            AutomationProperties.SetHelpText(LoginCheck,"");ClearLoginFeedback();return true;
        }
        catch(Exception error)when(StartupFailure(error))
        {
            // A failed status read is unknown, even if the write just returned.
            // Never guess the previous or requested value and never read again
            // from inside the catch path.
            LoginCheck.IsChecked=null;LoginCheck.IsEnabled=false;LoginCheck.Opacity=.55;
            AutomationProperties.SetHelpText(LoginCheck,LoginFeedbackMessage(LoginFeedback.Unavailable));
            PresentLoginFeedback(LoginFeedback.Unavailable,showError);return false;
        }
    }
    private void ChangeLoginStartup(object sender,RoutedEventArgs e)
    {
        if(!LoginCheck.IsEnabled)return;
        var requested=LoginCheck.IsChecked==true;var failed=false;
        try{startupRegistration.SetEnabled(requested);}
        catch(Exception error)when(StartupFailure(error)){failed=true;}
        if(!RefreshLoginStartupState(true))return;
        if(failed)PresentLoginFeedback(LoginFeedback.WriteFailed,true);
        else if(LoginCheck.IsChecked!=requested)PresentLoginFeedback(LoginFeedback.NotApplied,true);
    }
    private void PresentLoginFeedback(LoginFeedback feedback,bool navigate)
    {
        loginFeedback=feedback;loginFeedbackText=LoginFeedbackMessage(feedback);
        if(navigate){ShowError(loginFeedbackText);RevealLoginFeedback();}
        else{MessageLabel.Text=loginFeedbackText;MessageLabel.Visibility=Visibility.Visible;}
    }
    private void ClearLoginFeedback()
    {
        if(loginFeedbackText is {} previous&&MessageLabel.Text==previous){MessageLabel.Text="";MessageLabel.Visibility=Visibility.Collapsed;}
        loginFeedback=LoginFeedback.None;loginFeedbackText=null;
    }
    private void RefreshLoginFeedbackLanguage()
    {
        if(!LoginCheck.IsEnabled)AutomationProperties.SetHelpText(LoginCheck,LoginFeedbackMessage(LoginFeedback.Unavailable));
        if(loginFeedbackText is not {} previous||MessageLabel.Text!=previous)return;
        loginFeedbackText=LoginFeedbackMessage(loginFeedback);MessageLabel.Text=loginFeedbackText;
    }
    private void RevealLoginFeedback()
    {
        if(loginFeedbackText is null||MessageLabel.Text!=loginFeedbackText)return;
        MessageLabel.BringIntoView();
        Dispatcher.BeginInvoke(()=>
        {
            if(loginFeedbackText is null||MessageLabel.Text!=loginFeedbackText||!MessageLabel.IsVisible||!ReferenceEquals(activePage,SettingsPage))return;
            MessageLabel.UpdateLayout();var bottom=MessageLabel.TranslatePoint(new Point(0,MessageLabel.ActualHeight),PageScroller).Y;
            PageScroller.ScrollToVerticalOffset(PageScroller.VerticalOffset+Math.Max(0,bottom-(PageScroller.ActualHeight-8)));
        },DispatcherPriority.Loaded);
    }
}
