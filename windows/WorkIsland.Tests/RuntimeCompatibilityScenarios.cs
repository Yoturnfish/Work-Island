using System.Globalization;
using System.Text;
using WorkIsland.Core;

internal static class RuntimeCompatibilityScenarios
{
    public static void Run(Action<string,Action> test)
    {
        void Require(bool value){if(!value)throw new Exception("Runtime compatibility mismatch.");}
        test("framework date interpolation preserves history and accessibility date formats",()=>
        {
            var day=new DateOnly(2024,2,29);
            Require($"{day:yyyy-MM-dd}"=="2024-02-29"&&day.AddDays(1)==new DateOnly(2024,3,1));
            Require(day.DayNumber==(int)(new DateTime(2024,2,29).Ticks/TimeSpan.TicksPerDay));
            Require(!DateOnly.TryParseExact("2025-02-29","yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _));
        });
        test("framework GUID byte order preserves native Composition interface identities",()=>
        {
            var guid=new Guid("00112233-4455-6677-8899-aabbccddeeff");
            var bytes=RuntimeCompat.GuidBigEndianBytes(guid);
            Require(RuntimeCompat.ToHexString(bytes)=="00112233445566778899AABBCCDDEEFF"&&RuntimeCompat.GuidFromBigEndianBytes(bytes)==guid);
        });
        test("framework hashing preserves existing instance and installation identities",()=>
            Require(RuntimeCompat.ToHexString(RuntimeCompat.HashData(Encoding.UTF8.GetBytes("abc")))=="BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD"));
        test("framework path normalization retains drive roots and canonicalizes UNC roots",()=>
        {
            var root=Path.GetPathRoot(Path.GetFullPath("."))!;
            Require(RuntimeCompat.TrimEndingDirectorySeparator(root)==root);
            var path=Path.Combine(root,"Example");
            Require(RuntimeCompat.TrimEndingDirectorySeparator(path+Path.DirectorySeparatorChar+Path.DirectorySeparatorChar)==path);
            if(Path.DirectorySeparatorChar=='\\')
            {var unc=@"\\server\share\";Require(RuntimeCompat.TrimEndingDirectorySeparator(unc)==unc.TrimEnd('\\'));}
        });
        test("framework numeric boundaries retain NaN infinity and clamp semantics",()=>
        {
            Require(!RuntimeCompat.IsFinite(double.NaN)&&!RuntimeCompat.IsFinite(double.PositiveInfinity)&&RuntimeCompat.IsFinite(0));
            Require(double.IsNaN(RuntimeCompat.Clamp(double.NaN,0,1))&&RuntimeCompat.Clamp(double.PositiveInfinity,0,1)==1&&RuntimeCompat.Clamp(-1,0,1)==0);
        });
    }
}
