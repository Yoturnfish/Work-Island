using System.Text;
using System.Globalization;

namespace WorkIsland.Core;

public static class RecognitionRules
{
    public static readonly string[] Browsers = ["chrome.exe","msedge.exe","firefox.exe","brave.exe","vivaldi.exe","opera.exe"];
    // Mirrors Mac: mouse movement/click/scroll selects the pointer window;
    // a more recent keyboard event selects the foreground window.
    public static bool UsesPointer(double? pointerIdle, double keyboardIdle) => pointerIdle is double idle && idle <= keyboardIdle;

    public static WebsiteRule? Website(string input, bool fromAddress = false)
    {
        var keyword=input.Trim();
        if(keyword.Length==0 || keyword.Any(char.IsControl))return null;
        if(TryAddress(keyword,out var uri))keyword=uri!.IdnHost.ToLowerInvariant();
        else if(fromAddress || keyword.Contains("://") || keyword.IndexOfAny(['/', '\\', '?', '#', '@', ':'])>=0)return null;
        keyword=keyword.Trim('.').ToLowerInvariant();
        // The persisted rule is a host/keyword. A page's path, query and fragment
        // may be much longer without making that host invalid or worth storing.
        if(keyword.Length==0 || keyword.Length>253 || keyword.Any(c=>!char.IsLetterOrDigit(c) && c is not ('.' or '-')))return null;
        return new("web.keyword."+Convert.ToBase64String(Encoding.UTF8.GetBytes(keyword)),keyword,keyword,keyword,true,true);
    }

    public static WebsiteRule? AutomaticWebsite(string? address)
    {
        // Match the reference Mac registrableKeyword heuristic for new captures.
        // Existing explicit-host/manual rules are never converted or broadened.
        if(address is null||string.IsNullOrWhiteSpace(address)||!address.Contains("://",StringComparison.Ordinal)
            ||!TryAddress(address,out var uri)||uri!.HostNameType!=UriHostNameType.Dns)return null;
        var parts=uri.IdnHost.ToLowerInvariant().Split('.',StringSplitOptions.RemoveEmptyEntries);
        if(parts.Length<2)return null;
        var suffixCount=parts[^1].Length==2&&parts[^2] is "co" or "com" or "org" or "net" or "gov" or "edu" or "ac"?2:1;
        return parts.Length>suffixCount?Website(parts[^(suffixCount+1)]):null;
    }

    public static bool SameWebsiteKeyword(WebsiteRule rule,string keyword) =>
        string.Equals((string.IsNullOrEmpty(rule.PathContains)?rule.TitleContains:rule.PathContains).Trim(),keyword.Trim(),StringComparison.OrdinalIgnoreCase);

    public static bool TryAddress(string? value, out Uri? uri)
    {
        uri=null;
        if(value is null || string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))return false;
        var address=value.Contains("://",StringComparison.Ordinal)?value:"https://"+value;
        if(!Uri.TryCreate(address,UriKind.Absolute,out var parsed) || parsed.Scheme is not ("http" or "https")
            || string.IsNullOrEmpty(parsed.Host) || (!parsed.Host.Contains('.') && parsed.Host!="localhost"))return false;
        uri=parsed;return true;
    }

    public static string? SafeAddress(string? value)
    {
        if(!TryAddress(value,out var uri))return null;
        return new UriBuilder(uri!){UserName="",Password="",Query="",Fragment=""}.Uri.GetLeftPart(UriPartial.Path);
    }

    public static string? AddressPreview(string? value,int maximumLength=52)
    {
        if(string.IsNullOrWhiteSpace(value)||value.Any(char.IsControl)
            ||!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https")||string.IsNullOrEmpty(uri.Host))return null;
        // Sanitize the complete address before shortening it. Never put hidden
        // credentials or tokens into the label, tooltip or accessibility name.
        var safe=new UriBuilder(uri){UserName="",Password="",Query="",Fragment=""}.Uri.GetLeftPart(UriPartial.Path);
        var elements=StringInfo.GetTextElementEnumerator(safe);var text=new StringBuilder();var limit=Math.Max(0,maximumLength);
        for(var count=0;count<limit&&elements.MoveNext();count++)text.Append(elements.GetTextElement());
        return elements.MoveNext()?text.Append('…').ToString():text.ToString();
    }

    public static bool ValidWebsites(List<WebsiteRule>? rules) => rules is not null
        && rules.Select(r=>r?.Identifier).Distinct(StringComparer.OrdinalIgnoreCase).Count()==rules.Count
        && rules.All(r=>r is not null && !string.IsNullOrWhiteSpace(r.Identifier) && !string.IsNullOrWhiteSpace(r.DisplayName)
            && r.DisplayName.Length<=253 && r.TitleContains is not null && r.PathContains is not null
            && r.TitleContains.Length<=253 && r.PathContains.Length<=253);
}
