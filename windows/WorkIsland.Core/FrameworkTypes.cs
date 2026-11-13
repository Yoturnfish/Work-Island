#if NETFRAMEWORK
using System.Globalization;

namespace System.Runtime.CompilerServices
{
    public static class IsExternalInit { }
    [AttributeUsage(AttributeTargets.Class|AttributeTargets.Struct|AttributeTargets.Field|AttributeTargets.Property,Inherited=false)]
    public sealed class RequiredMemberAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.All,AllowMultiple=true,Inherited=false)]
    public sealed class CompilerFeatureRequiredAttribute(string featureName) : Attribute
    {
        public string FeatureName {get;}=featureName;
        public bool IsOptional {get;init;}
    }
}
namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Constructor,Inherited=false)]
    public sealed class SetsRequiredMembersAttribute : Attribute { }
}

namespace System
{
    // Only compiled for the Windows CLR; use the BCL types on modern .NET.
    public readonly struct DateOnly : IEquatable<DateOnly>, IComparable<DateOnly>, IComparable, IFormattable
    {
        private readonly DateTime value;
        public DateOnly(int year,int month,int day){value=new DateTime(year,month,day);}
        private DateOnly(DateTime value){this.value=value.Date;}
        public int Day=>value.Day;
        public static DateOnly MinValue=>default;
        public static DateOnly MaxValue=>new(DateTime.MaxValue);
        public int Month=>value.Month;
        public int Year=>value.Year;
        public int DayNumber=>(int)(value.Ticks/TimeSpan.TicksPerDay);
        public DayOfWeek DayOfWeek=>value.DayOfWeek;
        public static DateOnly FromDateTime(DateTime value)=>new(value);
        public DateOnly AddDays(int days)=>new(value.AddDays(days));
        public DateTime ToDateTime(TimeOnly time)=>value.Add(time.ToTimeSpan());
        public static DateOnly Parse(string text)=>new(DateTime.Parse(text,CultureInfo.CurrentCulture));
        public static DateOnly ParseExact(string text,string format,IFormatProvider? provider=null)=>new(DateTime.ParseExact(text,format,provider,DateTimeStyles.None));
        public static bool TryParseExact(string? text,string format,IFormatProvider? provider,DateTimeStyles styles,out DateOnly result)
        {
            var valid=DateTime.TryParseExact(text,format,provider,styles,out var parsed);
            result=valid?new(parsed):default;return valid;
        }
        public string ToString(string? format,IFormatProvider? provider=null)=>value.ToString(format,provider);
        public override string ToString()=>value.ToString("d",CultureInfo.CurrentCulture);
        public bool Equals(DateOnly other)=>value==other.value;
        public override bool Equals(object? other)=>other is DateOnly date&&Equals(date);
        public override int GetHashCode()=>value.GetHashCode();
        public int CompareTo(DateOnly other)=>value.CompareTo(other.value);
        int IComparable.CompareTo(object? other)=>other is null?1:other is DateOnly date?CompareTo(date):throw new ArgumentException("Expected a date.");
        public static bool operator ==(DateOnly a,DateOnly b)=>a.Equals(b);
        public static bool operator !=(DateOnly a,DateOnly b)=>!a.Equals(b);
        public static bool operator <(DateOnly a,DateOnly b)=>a.CompareTo(b)<0;
        public static bool operator >(DateOnly a,DateOnly b)=>a.CompareTo(b)>0;
        public static bool operator <=(DateOnly a,DateOnly b)=>a.CompareTo(b)<=0;
        public static bool operator >=(DateOnly a,DateOnly b)=>a.CompareTo(b)>=0;
    }
    public readonly struct TimeOnly
    {
        private readonly TimeSpan value;
        public TimeOnly(int hour,int minute){value=new DateTime(1,1,1,hour,minute,0).TimeOfDay;}
        public static TimeOnly MinValue=>default;
        public TimeSpan ToTimeSpan()=>value;
    }
    public readonly struct Index
    {
        public Index(int value,bool fromEnd=false){if(value<0)throw new ArgumentOutOfRangeException(nameof(value));Value=value;IsFromEnd=fromEnd;}
        public int Value {get;}
        public bool IsFromEnd {get;}
        public int GetOffset(int length)=>IsFromEnd?length-Value:Value;
        public static Index Start=>new(0);
        public static Index End=>new(0,true);
        public static implicit operator Index(int value)=>new(value);
    }
    public readonly struct Range
    {
        public Range(Index start,Index end){Start=start;End=end;}
        public Index Start {get;}
        public Index End {get;}
        public static Range All=>new(Index.Start,Index.End);
        public static Range StartAt(Index start)=>new(start,Index.End);
        public static Range EndAt(Index end)=>new(Index.Start,end);
        public (int Offset,int Length) GetOffsetAndLength(int length)
        {
            var start=Start.GetOffset(length);var end=End.GetOffset(length);
            if((uint)start>(uint)end||(uint)end>(uint)length)throw new ArgumentOutOfRangeException(nameof(length));
            return(start,end-start);
        }
    }
}
#endif
