using System.IO;
using System.IO.Pipes;
#if NETFRAMEWORK
using System.Security.AccessControl;
using System.Security.Principal;
#endif

namespace WorkIsland.App;

internal static class InstancePipes
{
    public static NamedPipeServerStream CreateServer(string name)
    {
#if NETFRAMEWORK
        using var identity=WindowsIdentity.GetCurrent();
        var user=identity.User??throw new UnauthorizedAccessException("Cannot identify the current user.");
        var security=new PipeSecurity();security.SetAccessRuleProtection(true,false);security.SetOwner(user);
        security.AddAccessRule(new PipeAccessRule(user,PipeAccessRights.FullControl,AccessControlType.Allow));
        // The Framework implementation sets FILE_FLAG_FIRST_PIPE_INSTANCE
        // when maxNumberOfServerInstances is one. Preserve the original
        // exclusive endpoint and current-user-only ACL on the older CLR.
        return new(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,0,0,security);
#else
        return new(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly|PipeOptions.FirstPipeInstance);
#endif
    }
    public static NamedPipeClientStream CreateClient(string name)
    {
#if NETFRAMEWORK
        return new(".",name,PipeAccessRights.ReadWrite|PipeAccessRights.ReadPermissions,PipeOptions.Asynchronous,TokenImpersonationLevel.Identification,HandleInheritability.None);
#else
        return new(".",name,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
#endif
    }
    public static void VerifyServerOwner(NamedPipeClientStream pipe)
    {
#if NETFRAMEWORK
        using var identity=WindowsIdentity.GetCurrent();
        var owner=pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier));
        if(identity.User is null||!identity.User.Equals(owner))throw new UnauthorizedAccessException("Control pipe belongs to a different user.");
#endif
    }
}
