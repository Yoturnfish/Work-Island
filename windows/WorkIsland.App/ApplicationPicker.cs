using System.Windows;
using Microsoft.Win32;

namespace WorkIsland.App;

public interface IApplicationPicker
{
    string? Select(Window owner);
}
public sealed class WindowsApplicationPicker(Func<string,string>? translate=null) : IApplicationPicker
{
    public string? Select(Window owner)
    {
        var dialog=new OpenFileDialog
        {
            Title=translate?.Invoke("选择需要识别的应用")??"选择需要识别的应用",Filter=(translate?.Invoke("应用程序 (*.exe)")??"应用程序 (*.exe)")+"|*.exe",DefaultExt=".exe",
            Multiselect=false,CheckFileExists=true,DereferenceLinks=true,
            InitialDirectory=Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };
        return dialog.ShowDialog(owner)==true?dialog.FileName:null;
    }
}
