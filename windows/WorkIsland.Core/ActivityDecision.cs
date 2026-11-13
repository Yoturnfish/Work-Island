namespace WorkIsland.Core;

public sealed record ActivityInput(string? FrontmostApplicationIdentifier, string? FrontmostApplicationName,
    double IdleSeconds, string? PointerApplicationIdentifier = null, string? PointerApplicationName = null,
    string? BrowserURL = null);
public sealed record ApplicationRule(string Identifier, string DisplayName, bool IsEnabled = true,
    string CategoryRawValue = "other");
public sealed record WebsiteRule(string Identifier, string DisplayName, string TitleContains,
    string PathContains, bool IsEnabled, bool? MatchesHostOnly = null, string CategoryRawValue = "other");
public sealed record TrackingTarget(string Identifier, string DisplayName, string CategoryRawValue, string Source);
public sealed record Decision(string State, TrackingTarget? Target = null);

// Same pointer / website / application precedence as the reference Swift rules.
public static class ActivityDecision
{
    public static Decision Evaluate(ActivityInput input, IEnumerable<ApplicationRule> apps,
        IEnumerable<WebsiteRule> websites, IEnumerable<string> browsers, double awayThreshold, bool paused)
    {
        if (paused) return new("paused");
        if (!RuntimeCompat.IsFinite(input.IdleSeconds) || input.IdleSeconds >= awayThreshold) return new("away");
        var identifier = input.PointerApplicationIdentifier ?? input.FrontmostApplicationIdentifier;
        if (identifier is not null && browsers.Contains(identifier, StringComparer.OrdinalIgnoreCase))
        {
            var site = websites.FirstOrDefault(rule => WebsiteMatches(input.BrowserURL, rule));
            if (site is not null) return new("tracking", new(site.Identifier, site.DisplayName, site.CategoryRawValue, "website"));
        }
        var app = apps.FirstOrDefault(rule => rule.IsEnabled && string.Equals(rule.Identifier, identifier, StringComparison.OrdinalIgnoreCase));
        return app is null ? new("away") : new("tracking", new(app.Identifier, app.DisplayName, app.CategoryRawValue, "application"));
    }

    public static bool WebsiteMatches(string? url, WebsiteRule rule)
    {
        if (!rule.IsEnabled || url is null) return false;
        var keyword = (string.IsNullOrEmpty(rule.PathContains) ? rule.TitleContains : rule.PathContains).Trim();
        if (keyword.Length == 0) return false;
        return rule.MatchesHostOnly == true
            ? Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
                && (keyword.Contains('.') ? uri.IdnHost.Equals(keyword,StringComparison.OrdinalIgnoreCase)
                    || uri.IdnHost.EndsWith("."+keyword,StringComparison.OrdinalIgnoreCase)
                    : uri.IdnHost.Split('.').Contains(keyword, StringComparer.OrdinalIgnoreCase))
            : url.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }
}
