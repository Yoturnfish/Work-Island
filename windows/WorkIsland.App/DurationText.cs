using System.Globalization;
using System.Numerics;

namespace WorkIsland.App;

// Each manually edited record fits Int64, but a day's or application's sum
// need not. Keep ordinary timer formatting on the existing integer fast path.
internal static class DurationText
{
    private static double Nonnegative(double seconds)=>seconds>0?seconds:0;
    public static string Compact(double seconds,string style)
    {
        seconds=Nonnegative(seconds);
        if(seconds<(double)long.MaxValue)
        {
            var s=(long)seconds;
            return style=="hoursMinutes"?$"{s/3600}H {s/60%60:00}M":$"{s/60:00}M {s%60:00}S";
        }
        if(!double.IsFinite(seconds))return "∞";
        var whole=new BigInteger(seconds);
        return style=="hoursMinutes"?$"{whole/3600}H {whole/60%60:00}M":$"{whole/60:00}M {whole%60:00}S";
    }
    public static string Clock(double seconds)
    {
        seconds=Nonnegative(seconds);
        if(seconds<(double)long.MaxValue)
        {
            var s=(long)seconds;return $"{s/3600:00}:{s/60%60:00}:{s%60:00}";
        }
        if(!double.IsFinite(seconds))return "∞";
        var whole=new BigInteger(seconds);return $"{whole/3600:00}:{whole/60%60:00}:{whole%60:00}";
    }
    public static string WholeKey(double seconds)
    {
        seconds=Nonnegative(seconds);
        return seconds<(double)long.MaxValue?((long)seconds).ToString(CultureInfo.InvariantCulture)
            :double.IsFinite(seconds)?new BigInteger(seconds).ToString(CultureInfo.InvariantCulture):"∞";
    }
    public static string Hours(double seconds)=>Component(seconds,3600,false);
    public static string Minutes(double seconds)=>Component(seconds,60,true);
    private static string Component(double seconds,int divisor,bool remainder)
    {
        seconds=Nonnegative(seconds);
        if(seconds<(double)long.MaxValue)
        {
            var value=(long)seconds/divisor;return (remainder?value%60:value).ToString(CultureInfo.InvariantCulture);
        }
        if(!double.IsFinite(seconds))return "∞";
        var whole=new BigInteger(seconds)/divisor;
        return (remainder?whole%60:whole).ToString(CultureInfo.InvariantCulture);
    }
}
