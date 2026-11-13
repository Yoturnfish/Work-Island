using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using WorkIsland.Core;

namespace WorkIsland.App;

public partial class IslandWindow
{
    private bool profileSavePending;
    private string profileSaveMessage="",profileNoticeKey="";
    private TextBlock? profileSaveNotice;
    private Action? profilePreviewMotion;
    private void SetProfileNotice(string message)
    {
        profileNoticeKey=message;
        if(profileSaveNotice is null)return;
        profileSaveNotice.Text=Text(message);
        profileSaveNotice.Visibility=string.IsNullOrEmpty(message)?Visibility.Collapsed:Visibility.Visible;
        profileSaveNotice.Foreground=message==RecognitionSaveFailure?new SolidColorBrush(Color.FromRgb(255,183,85)):Brushes.White;
    }
    private void ResolveProfileSaveFeedback()
    {
        if(!profileSavePending)return;
        profileSavePending=false;profileSaveMessage="更改已保存。";
        if(profileNoticeKey==RecognitionSaveFailure)SetProfileNotice(profileSaveMessage);
        if(recognitionMessageKey==RecognitionSaveFailure)RecognitionMessage(profileSaveMessage);
    }
    private static string AccentName(string accent)=>accent switch
    {"blue"=>"蓝色","yellow"=>"黄色","cyan"=>"青色","pink"=>"粉色","orange"=>"浅粉色","purple"=>"紫色",_=>"绿色"};
    private static Color AccentColor(string accent)=>accent switch
    {"blue"=>Color.FromRgb(51,140,255),"yellow"=>Color.FromRgb(255,214,10),"cyan"=>Color.FromRgb(50,230,230),"pink"=>Color.FromRgb(255,55,95),"orange"=>Color.FromRgb(255,153,194),"purple"=>Color.FromRgb(191,90,242),_=>Color.FromRgb(64,255,97)};

    private void ProfileChanged()
    {
        CloseRecognitionPicker();
        RebuildRules();
        if(!Persist())
        {
            profileSavePending=true;profileSaveMessage=RecognitionSaveFailure;
            RecognitionMessage(profileSaveMessage);SetProfileNotice(profileSaveMessage);
        }
        ApplyLanguage();Render();
    }
    private void ActivateProfile(string id)
    {
        if(!smoke)Tick();engine.ResetObservation();ColorProfilesService.Activate(engine.Data.Settings,id);ProfileChanged();
    }
    private void CreateProfile()
    {
        if(!smoke)Tick();engine.ResetObservation();
        // The Mac model's explicit new-theme command starts with blue + no rules.
        ColorProfilesService.Create(engine.Data.Settings,"blue",Text("新组合"));ProfileChanged();
    }
    private void RenameCurrentProfile(string name)
    {
        if(engine.Data.Settings.ActiveProfileId is not { } id)return;
        ColorProfilesService.Rename(engine.Data.Settings,id,name);ProfileChanged();
    }
    private bool DeleteCurrentProfile()
    {
        if(engine.Data.Settings.ActiveProfileId is not { } id)return false;
        if(!smoke)Tick();engine.ResetObservation();
        var deleted=ColorProfilesService.Delete(engine.Data.Settings,id);ProfileChanged();return deleted;
    }

    private void ProfileMenuOpening(object sender,ContextMenuEventArgs args)
    {
        // The native service would reuse the old menu and its captured labels
        // after title/language changes. Use the same guarded, current-state
        // construction as the left button, and suppress the service's old menu.
        args.Handled=true;
        OpenProfilePopover();
    }
    private void OpenProfilePopover()
    {
        if(!CanOpenRecognitionSurface())return;
        if(RecognitionColorButton.ContextMenu is {} previous)previous.IsOpen=false;
        var menu=new ContextMenu {Background=new SolidColorBrush(Color.FromRgb(37,41,37)),Foreground=Brushes.White,FontFamily=FontFamily,FontSize=12,
            PlacementTarget=RecognitionColorButton,Placement=PlacementMode.Bottom};
        // Popup roots do not inherit the window's implicit control styles.
        foreach(var type in new[]{typeof(Button),typeof(TextBox),typeof(ScrollBar)})
            menu.Resources[type]=FindResource(type);
        var popupButtonStyle=new Style(typeof(Button),(Style)FindResource(typeof(Button)));
        var disabled=new Trigger {Property=IsEnabledProperty,Value=false};disabled.Setters.Add(new Setter(OpacityProperty,.4));popupButtonStyle.Triggers.Add(disabled);
        menu.Resources[typeof(Button)]=popupButtonStyle;
        menu.Template=(ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ContextMenu">
              <Border Background="{TemplateBinding Background}" BorderBrush="#38FFFFFF" BorderThickness="1" CornerRadius="10" Padding="10">
                <ItemsPresenter/>
              </Border>
            </ControlTemplate>
            """);
        var panel=new StackPanel {Width=300,Margin=new Thickness(4),Orientation=Orientation.Vertical};
        panel.Children.Add(new TextBlock {Text=Text("切换颜色可重新配置识别组合，适应不同工作类型。"),TextWrapping=TextWrapping.Wrap,FontSize=12,Foreground=new SolidColorBrush(Color.FromArgb(190,255,255,255)),Margin=new Thickness(0,0,0,12)});
        var palette=new UniformGrid {Rows=1,Columns=7}; // 7 source slots, including persisted soft-pink orange.
        panel.Children.Add(palette);
        var name=new TextBox {Height=28,MaxLength=60,Margin=new Thickness(0,10,0,7),Padding=new Thickness(6,3,6,3),TextAlignment=TextAlignment.Left,VerticalContentAlignment=VerticalAlignment.Center};
        AutomationProperties.SetName(name,Text("为颜色组合命名"));panel.Children.Add(name);
        // Keep the real WPF editor and its caret/selection, with the Mac rounded field surface.
        name.Template=(ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="TextBox">
              <Border x:Name="FieldSurface" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="7">
                <Grid>
                  <TextBlock x:Name="NameHint" Text="{TemplateBinding Tag}" Foreground="#ADFFFFFF" Margin="{TemplateBinding Padding}" Padding="4,0,0,0" VerticalAlignment="Center" IsHitTestVisible="False" Visibility="Collapsed"/>
                  <ScrollViewer x:Name="PART_ContentHost" Margin="0" HorizontalAlignment="Stretch" VerticalAlignment="Stretch" HorizontalContentAlignment="Left" VerticalContentAlignment="{TemplateBinding VerticalContentAlignment}" HorizontalScrollBarVisibility="Hidden" VerticalScrollBarVisibility="Hidden" Focusable="False"/>
                </Grid>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="FieldSurface" Property="BorderBrush" Value="#70FFFFFF"/></Trigger>
                <Trigger Property="Text" Value=""><Setter TargetName="NameHint" Property="Visibility" Value="Visible"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """);
        var styles=new StackPanel();panel.Children.Add(styles);
        var notice=new TextBlock {FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,6)};panel.Children.Add(notice);
        AutomationProperties.SetAutomationId(notice,"ProfileSaveNotice");profileSaveNotice=notice;
        var updating=false;
        var captions=new Dictionary<string,TextBlock>();
        var styleControls=new Dictionary<string,(Button Button,TextBlock Check,PixelProgress Preview)>();
        var previewFallback=false;
        void PreviewScanFailed(object? sender,EventArgs args)
        {
            // Both visual roots belong to the same popup target. If one loses
            // that target, switch the pair together instead of stranding its peer.
            if(previewFallback||!menu.IsOpen)return;
            previewFallback=true;
            // The sender's current failure path performs its own fallback.
            // Only update its peer here, avoiding reentrant clock creation.
            try{foreach(var control in styleControls.Values)if(!ReferenceEquals(control.Preview,sender))control.Preview.UseWpfScan(new NotSupportedException("Shared popup scan is unavailable."));}
            finally{previewFallback=false;}
        }
        bool PreviewReduced()=>PresentationReduced||ColorProfilesService.Active(engine.Data.Settings) is null;
        bool? previewReduced=null;
        Action refreshPreviewMotion=()=>
        {
            if(!menu.IsOpen)return;
            var reduced=PreviewReduced();
            if(previewReduced==reduced)return;
            previewReduced=reduced;
            foreach(var (style,control) in styleControls)
                control.Preview.Update(.62,"tracking",style,(SolidColorBrush)control.Check.Foreground,reduced,24);
        };
        profilePreviewMotion=refreshPreviewMotion;
        void RefreshNames()
        {
            // Update aliases in place so typing preserves the editor, caret and selection.
            foreach(var (accent,caption) in captions)
            {
                var active=ColorProfilesService.Active(engine.Data.Settings);
                var profile=active?.Accent==accent?active:engine.Data.Settings.ColorProfiles.FirstOrDefault(p=>p.Accent==accent);
                caption.Text=accent=="green"?Text("默认"):(profile?.Title??"");
            }
        }
        void Refresh()
        {
            updating=true;SetProfileNotice(profileSaveMessage);
            var active=ColorProfilesService.Active(engine.Data.Settings);
            palette.Children.Clear();captions.Clear();
            foreach(var accent in ColorProfilesService.Accents)
            {
                var group=new StackPanel {Margin=new Thickness(1)};
                var button=new Button {Width=30,Height=28,FontSize=14,VerticalContentAlignment=VerticalAlignment.Center,Content=engine.Data.Settings.Accent==accent?"✓":"",Background=new SolidColorBrush(AccentColor(accent)),Foreground=Brushes.Black,FontWeight=FontWeights.Bold};
                AutomationProperties.SetName(button,Text(AccentName(accent)));
                button.Click+=(_,_)=>
                {
                    var current=ColorProfilesService.Active(engine.Data.Settings);
                    var same=engine.Data.Settings.ColorProfiles.Where(p=>p.Accent==accent).ToArray();
                    // Retain access to existing same-color themes without a
                    // separate selector: repeated clicks advance within color.
                    if(same.Length>1&&current?.Accent==accent)
                        ActivateProfile(same[(Array.FindIndex(same,p=>p.Id==current.Id)+1)%same.Length].Id);
                    else {SwitchColorProfile(accent);ProfileChanged();}
                    Refresh();name.Focus();name.SelectAll();
                };
                group.Children.Add(button);
                var caption=new TextBlock {FontSize=11,Margin=new Thickness(0,4,0,0),TextTrimming=TextTrimming.CharacterEllipsis,Width=40,HorizontalAlignment=HorizontalAlignment.Center,TextAlignment=TextAlignment.Center};
                captions.Add(accent,caption);group.Children.Add(caption);palette.Children.Add(group);
            }
            RefreshNames();
            name.Tag=Text(active?.IsDefault==true?"屏幕工作时间":"为颜色组合命名");
            AutomationProperties.SetHelpText(name,(string)name.Tag);
            name.Text=active?.Title ?? "";name.IsEnabled=active is not null;
            foreach(var style in new[]{"matrix","waveform"})
            {
                if(styleControls.TryGetValue(style,out var existing))
                {
                    existing.Check.Text=engine.Data.Settings.ProgressStyle==style?"✓":"";
                    existing.Check.Foreground=new SolidColorBrush(ThemeColor);
                    existing.Preview.Update(.62,"tracking",style,new SolidColorBrush(ThemeColor),PreviewReduced(),24);
                    existing.Button.IsEnabled=active is not null;
                    continue;
                }
                // Keep the focused button attached while its Click is routed.
                // Replacing it here lets Return dismiss the surrounding menu.
                var content=new Grid {Margin=new Thickness(9,0,9,0)};content.ColumnDefinitions.Add(new ColumnDefinition());content.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(24)});
                var check=new TextBlock {Text=engine.Data.Settings.ProgressStyle==style?"✓":"",FontSize=14,HorizontalAlignment=HorizontalAlignment.Right,Foreground=new SolidColorBrush(ThemeColor),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(check,1);content.Children.Add(check);
                var preview=new PixelProgress {Height=12,VerticalAlignment=VerticalAlignment.Center};preview.Update(.62,"tracking",style,new SolidColorBrush(ThemeColor),PreviewReduced(),24);content.Children.Add(preview);
                preview.NativeScanFailure+=PreviewScanFailed;
                var button=new Button {Content=content,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Center,Height=30,Margin=new Thickness(0,2,0,2),Background=new SolidColorBrush(Color.FromArgb(20,255,255,255)),IsEnabled=active is not null};
                AutomationProperties.SetName(button,Text(style=="matrix"?"矩形矩阵":"波形进度条"));
                button.Click+=(_,_)=>{engine.Data.Settings.ProgressStyle=style;ProfileChanged();Refresh();};styles.Children.Add(button);
                styleControls.Add(style,(button,check,preview));
            }
            updating=false;
        }
        // Match the Mac text binding: changes survive dismissing the popover without Enter.
        name.TextChanged+=(_,_)=>{if(!updating){RenameCurrentProfile(name.Text);RefreshNames();}};
        name.KeyDown+=(_,eventArgs)=>
        {
            if(eventArgs.Key==Key.Enter){RenameCurrentProfile(name.Text);Refresh();eventArgs.Handled=true;}
            else if(eventArgs.Key==Key.Escape){menu.IsOpen=false;eventArgs.Handled=true;}
        };
        var host=new MenuItem {Header=panel,StaysOpenOnClick=true,Focusable=false,Padding=new Thickness(0)};
        // A form has neither an icon gutter nor the whole-row menu hover treatment.
        host.Template=(ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="MenuItem">
              <ContentPresenter ContentSource="Header"/>
            </ControlTemplate>
            """);
        menu.Items.Add(host);
        menu.Opened+=(_,_)=>
        {
            var matrix=styleControls["matrix"].Preview;
            foreach(var (style,control) in styleControls)
                if(PresentationSource.FromVisual(control.Preview) is HwndSource source)
                    control.Preview.EnableNativePopupScan(source,menu,style=="matrix"?null:()=>matrix.NativeScanForPeer);
            refreshPreviewMotion();
        };
        menu.Closed+=(_,_)=>
        {
            foreach(var control in styleControls.Values)control.Preview.NativeScanFailure-=PreviewScanFailed;
            if(ReferenceEquals(profilePreviewMotion,refreshPreviewMotion))profilePreviewMotion=null;
            if(ReferenceEquals(profileSaveNotice,notice)){profileSaveNotice=null;profileNoticeKey="";}
            if(!IsActive&&!smoke)BeginFocusReturn();
        };
        RecognitionColorButton.ContextMenu=menu;Refresh();menu.IsOpen=true;
    }
}
